"""Independent Blender round-trip and rotating-clearance verification."""
import json
import math
from pathlib import Path
import bpy
from mathutils import Matrix
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/stern-paddle-astra-model-v7'
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'tools/blender/source/stern-paddle-astra-v7.blend'))
scene=bpy.context.scene
scene.frame_set(1)
rotor=bpy.data.objects['Paddle_Rotor']
shell=bpy.data.objects['Hull_Shell']
def tree(o, transform=None):
    o.data.calc_loop_triangles()
    m=transform if transform is not None else o.matrix_world
    return BVHTree.FromPolygons([m@v.co for v in o.data.vertices],
                               [tuple(t.vertices) for t in o.data.loop_triangles],all_triangles=True)
fixed=tree(shell)
mount=tree(bpy.data.objects['Paddle_Frame'])
collisions=[]
frame_collisions=[]
rotor.data.calc_loop_triangles()
radial=[max(math.hypot(rotor.data.vertices[v].co.x,rotor.data.vertices[v].co.z)
            for v in tri.vertices) for tri in rotor.data.loop_triangles]
for angle in range(0,360,5):
    m=rotor.parent.matrix_world@Matrix.Rotation(math.radians(angle),4,'Y')
    hits=fixed.overlap(tree(rotor,m))
    if hits:collisions.append({'angle':angle,'pairs':len(hits)})
    outside_bearings=[pair for pair in mount.overlap(tree(rotor,m)) if radial[pair[1]]>.55]
    if outside_bearings:frame_collisions.append({'angle':angle,'pairs':len(outside_bearings)})
report={'rotation_samples':72,'rotor_hull_intersections':collisions,'fbx_roundtrip':{}}
report['rotor_frame_intersections_outside_bearings']=frame_collisions
rail=bpy.data.objects['Rails_Teal'].data.color_attributes['Col']
report['rail_corners_matching_shader_recolor_rule']=sum(
    min(c.color[1],c.color[2])>=c.color[0]*1.65+.025 for c in rail.data)
assert report['rail_corners_matching_shader_recolor_rule']==0
original={o.name: {'vertices':len(o.data.vertices),
                  'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)}
          for o in bpy.data.objects if o.type=='MESH'}
for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
for path in sorted((OUT/'models').glob('*.fbx')):
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    obs=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
    assert len(obs)==1
    o=obs[0]
    entry={'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
           'vertex_colors':list(o.data.color_attributes.keys()),
           'bounds':[list(min(v.co[i] for v in o.data.vertices) for i in range(3)),
                     list(max(v.co[i] for v in o.data.vertices) for i in range(3))]}
    assert entry['triangles']==original[path.stem]['triangles']
    assert 'Col' in entry['vertex_colors']
    report['fbx_roundtrip'][path.stem]=entry
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
assert not collisions, 'Rotor intersects hull; see report'
assert not frame_collisions, 'Rotor intersects frame outside bearing region; see report'



