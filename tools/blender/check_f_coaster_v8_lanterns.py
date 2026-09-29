import bpy,json
from pathlib import Path
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v8'
bpy.ops.wm.open_mainfile(filepath=str(root/'lanterns/ship.blend'))
dg=bpy.context.evaluated_depsgraph_get()
def tree(o):
 e=o.evaluated_get(dg);m=e.to_mesh();t=BVHTree.FromPolygons([o.matrix_world@v.co for v in m.vertices],[list(p.vertices) for p in m.polygons]);e.to_mesh_clear();return t
chimney=tree(bpy.data.objects['Chimney_Helm_Base']);hits=[]
for o in bpy.context.scene.objects:
 if o.type=='MESH' and o.name.startswith('Lantern_Stern'):
  if tree(o).overlap(chimney):hits.append(o.name)
lights=[o.name for o in bpy.context.scene.objects if o.type=='LIGHT' and o.name.startswith('Lantern_')]
bow_contact=bool(tree(bpy.data.objects['Lantern_Bow_Frame']).overlap(tree(bpy.data.objects['Bow_Cap_Connector'])))
r={'bow_mount_contacts_cap':bow_contact,'passed':not hits and len(lights)==3 and bow_contact,'chimney_intersections':hits,'lantern_lights':lights,'scope':'Static fixture/chimney separation and light count; Blender art preview only.'}
(root/'lighting-validation.json').write_text(json.dumps(r,indent=2));print(r)
assert r['passed']
