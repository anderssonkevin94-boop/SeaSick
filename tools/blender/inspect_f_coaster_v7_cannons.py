import bpy,sys,json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,'/Users/kevinandersson/Desktop/SeaSick/tools/blender')
import f_coaster_v1 as kit
root=Path('/Users/kevinandersson/Desktop/SeaSick/art-staging/f-coaster-v7'); dest=root/'visible-cannons'
bpy.ops.wm.open_mainfile(filepath=str(dest/'ship.blend'))
dg=bpy.context.evaluated_depsgraph_get();report=[]
for o in bpy.context.scene.objects:
 if 'Truck_Wheel' in o.name:
  vs=[o.matrix_world@v.co for v in o.data.vertices]
  report.append({'part':o.name,'min':[min(v[i] for v in vs) for i in range(3)],'max':[max(v[i] for v in vs) for i in range(3)]})
assert all(max(abs(r['min'][1]),abs(r['max'][1]))<4.35 for r in report), 'Wheel overlaps gun bay sill'
(root/'cannon-wheel-bounds.json').write_text(json.dumps(report,indent=2));print('WHEEL_BOUNDS',json.dumps(report))
def tree(o):
 ev=o.evaluated_get(dg);me=ev.to_mesh()
 t=BVHTree.FromPolygons([o.matrix_world@v.co for v in me.vertices],[list(p.vertices) for p in me.polygons]);ev.to_mesh_clear();return t
surrounds=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.parent and o.parent.name=='Middle_0' and not o.name.startswith('Main_Deck')]
guns=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('review_only') and any(p.name.startswith('Review_Cannon') for p in [o.parent, o.parent.parent if o.parent else None] if p)]
hits=[]
for a in guns:
 ta=tree(a)
 for b in surrounds:
  if ta.overlap(tree(b)):hits.append([a.name,b.name])
(root/'cannon-fit-validation.json').write_text(json.dumps({'wheels_within_deck_and_sill':True,'cannon_ship_intersections':hits,'passed':not hits},indent=2))
assert not hits, hits
bpy.context.scene.cycles.samples=16
kit.shot(dest/'side.png',(11,-65,3.3),(11,0,3.3),30,(1700,900))
kit.shot(dest/'cannon-detail.png',(14,-17,12),(12.8,-2.4,2.6),9,(1400,1100))
