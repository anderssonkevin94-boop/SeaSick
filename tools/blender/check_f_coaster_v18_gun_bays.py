import bpy,sys,json
from pathlib import Path
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
import verify_f_coaster_v2 as checks
root=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v18'
bpy.ops.wm.open_mainfile(filepath=str(root/'timber-top-band/ship.blend'))
obstacles=[bpy.data.objects[n] for n in ['Transom_Wall','Carved_Frames','Upper_Storey_Supports','Upper_Stern_Floor','Wheel_Well_Lining','Machinery_Bulkhead','Turning_Stairs','Cannon_Port_Trim_Port','Cannon_Port_Trim_Starboard','Hinged_Port_Cover_Port','Hinged_Port_Cover_Starboard']]
def tree(ob):
 v,f=checks.evaluated(ob);return BVHTree.FromPolygons(v,f)
static={ob.name:tree(ob) for ob in obstacles};hits=[];bounds={}
for side in ['Port','Starboard']:
 r=bpy.data.objects['Review_Lower_Review_Cannon_'+side];verts=[]
 for ob in r.children_recursive:
  if ob.type!='MESH':continue
  v,f=checks.evaluated(ob);verts+=v;t=BVHTree.FromPolygons(v,f)
  for name,st in static.items():
   if t.overlap(st):hits.append([side,ob.name,name])
 bounds[side]=[[min(v[i] for v in verts) for i in range(3)],[max(v[i] for v in verts) for i in range(3)]]
gap=(bounds['Port'][0][1]-bounds['Starboard'][1][1])*.5
report={'passed':not hits,'static_intersections':hits,'gun_bounds_source_units':bounds,'between_guns_m':gap,'scope':'Static fit against walls, support beams, upper floor, stairs and wheel housing. Recoil, crew colliders and navigation remain untested.'}
(root/'gun-bay-validation.json').write_text(json.dumps(report,indent=2));print(report)
assert report['passed']
