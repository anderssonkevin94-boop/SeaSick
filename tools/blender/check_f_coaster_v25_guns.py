import bpy,sys,json
from pathlib import Path
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
import verify_f_coaster_v2 as check
root=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v25'
bpy.ops.wm.open_mainfile(filepath=str(root/'bow-cannon-bays/ship.blend'))
manifest=json.loads((root/'bow-cannon-bays/manifest.json').read_text())
names={n for e in manifest['exports'] for n in e['parts']}
# Floors intentionally contact wheels; inspect enclosing structure and access.
obstacles=[bpy.data.objects[n] for n in names if not any(t in n for t in ['Floor','Main_Deck','Paddle_Rotor'])]
static={o.name:BVHTree.FromPolygons(*check.evaluated(o)) for o in obstacles}
hits=[];bounds={}
for prefix in ['Review_Cannon_','Review_Bow_Lower_','Review_Bow_Upper_','Review_Lower_Review_Cannon_']:
 for side in ['Port','Starboard']:
  gun=bpy.data.objects[prefix+side];verts=[]
  for o in gun.children_recursive:
   if o.type!='MESH':continue
   v,f=check.evaluated(o);verts+=v;tree=BVHTree.FromPolygons(v,f)
   for name,t in static.items():
    if tree.overlap(t):hits.append([gun.name,o.name,name])
  bounds[gun.name]=[[min(v[i] for v in verts) for i in range(3)],[max(v[i] for v in verts) for i in range(3)]]
r={'passed':not hits,'static_intersections':hits,'gun_bounds_source_units':bounds,'scope':'Static middle, bow and stern gun fit only; recoil and crew navigation are not simulated.'}
(root/'middle-gun-validation.json').write_text(json.dumps(r,indent=2));print(r);assert r['passed']
