import bpy,json,sys
from pathlib import Path
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
root=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v11';out=root/'rounded-stern'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
dg=bpy.context.evaluated_depsgraph_get()
def tree(o):
 e=o.evaluated_get(dg);m=e.to_mesh();t=BVHTree.FromPolygons([o.matrix_world@v.co for v in m.vertices],[list(p.vertices) for p in m.polygons]);e.to_mesh_clear();return t
funnel=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('Chimney_')]
neighbors=[o for o in bpy.context.scene.objects if o.type=='MESH' and (o.name.startswith('Lantern_Stern') or o.name.startswith('Helm'))]
hits=[]
for a in funnel:
 ta=tree(a)
 for b in neighbors:
  if ta.overlap(tree(b)):hits.append([a.name,b.name])
r={'passed':not hits,'funnel_lantern_helm_intersections':hits,'scope':'Static funnel clearance from stern lanterns and helm geometry.'}
(root/'funnel-validation.json').write_text(json.dumps(r,indent=2));print(r);assert r['passed']
bpy.context.scene.cycles.samples=24
kit.shot(out/'chimney-day.png',(-8,12,11),(1.7,2.8,5.2),9,(1200,1200))
