"""Verify the FBX round trip independently of the generator scene."""
import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

out=Path(__file__).resolve().parents[2]/'art-staging/quarry-astra-lvl1-v2'
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(out/'quarry-state-kit.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
report={'mesh_count':len(meshes),'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0}
for ob in meshes:
    bm=bmesh.new(); bm.from_mesh(ob.data)
    report['triangles']+=sum(len(f.verts)-2 for f in bm.faces)
    report['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges)
    report['degenerate_faces']+=sum(f.calc_area()<1e-10 for f in bm.faces)
    assert not any(p.use_smooth for p in ob.data.polygons),ob.name
    assert len(ob.data.color_attributes)>0,ob.name
    bm.free()
assert report['nonmanifold_edges']==0 and report['degenerate_faces']==0,report
for prefix,count,parent in [('Input_Stone_',5,'Input_Container'),('Output_Brick_',12,'Output_Container')]:
    slots=[o for o in meshes if o.name.startswith(prefix)]
    assert len(slots)==count
    assert all(o.parent and o.parent.name==parent for o in slots)
roof=bpy.data.objects['Roof']; roof.data.calc_loop_triangles()
crane=bpy.data.objects['Lifting_Frame']
roof_min_y=min((roof.matrix_world@v.co).y for v in roof.data.vertices)
crane_max_y=max((crane.matrix_world@v.co).y for v in crane.data.vertices)
report['crane_roof_clearance_m']=roof_min_y-crane_max_y
assert report['crane_roof_clearance_m']>.25,report
tree=BVHTree.FromPolygons([roof.matrix_world@v.co for v in roof.data.vertices],
    [tuple(t.vertices) for t in roof.data.loop_triangles],all_triangles=True)
hits={}
for eye in [(8,-12,8),(-8,-12,8),(0,-14,7)]:
    direction=(Vector(eye)-Vector((0,0,1.15))).normalized(); count=0
    for x in [-2.64,2.615]:
        for dx in [-.4,0,.4]:
            for y in [-1.9,-1.2,-.5]:
                for z in [.3,.9]:
                    count+=tree.ray_cast(Vector((x+dx,y,z)),direction,30)[0] is not None
    hits[str(eye)]=count
assert not any(hits.values()),hits
report['roof_occlusion_hits_at_review_angles']=hits
report['result']='PASS'
(out/'export-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
