"""Read-only exported geometry checks; not a substitute for later navigation tests."""
import json,struct,math
from pathlib import Path
R=Path('/Users/kevinandersson/Desktop/SeaSick');reports=[]
def dseg(p,a,b):
 dx,dz=b[0]-a[0],b[1]-a[1];q=dx*dx+dz*dz;t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dz)/q)) if q else 0
 return math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dz)
def tri_distance(p,a,b,c):
 cr=lambda a,b,p:(b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
 q=[cr(a,b,p),cr(b,c,p),cr(c,a,p)]
 if abs(cr(a,b,c))>1e-9 and (min(q)>=0 or max(q)<=0):return 0
 return min(dseg(p,a,b),dseg(p,b,c),dseg(p,c,a))
for file in sorted((R/'tools/blender/exports/settlement-kit-v1').glob('*.glb')):
 blob=file.read_bytes();n=struct.unpack_from('<I',blob,12)[0];j=json.loads(blob[20:20+n]);binary=blob[28+n:]
 def acc(i):
  a=j['accessors'][i];v=j['bufferViews'][a['bufferView']];fmt={5126:'f',5125:'I',5123:'H',5121:'B'}[a['componentType']];width={'SCALAR':1,'VEC3':3,'VEC2':2,'VEC4':4}[a['type']];size=struct.calcsize('<'+fmt*width);stride=v.get('byteStride',size);off=v.get('byteOffset',0)+a.get('byteOffset',0)
  return [struct.unpack_from('<'+fmt*width,binary,off+k*stride) for k in range(a['count'])]
 tris=[];count=0
 for node in j['nodes']:
  if 'mesh' not in node:continue
  for p in j['meshes'][node['mesh']]['primitives']:
   vv=acc(p['attributes']['POSITION']);ii=acc(p['indices']);count+=len(ii)//3
   for k in range(0,len(ii),3):tris.append((node['name'],[vv[ii[k+z][0]] for z in range(3)]))
 workers=[o for o in j['nodes'] if o.get('extras',{}).get('role')=='worker'];conflicts=[]
 for worker in workers:
  x,y,z=worker.get('translation',[0,0,0]);hits=set()
  for name,v in tris:
   if max(p[1] for p in v)<=y+.10 or min(p[1] for p in v)>=y+1.93:continue
   if tri_distance((x,z),*[(p[0],p[2]) for p in v])<.40:hits.add(name)
  if hits:conflicts.append({'worker':worker['name'],'geometry':sorted(hits)})
 pairs=[]
 for i,a in enumerate(workers):
  for b in workers[i+1:]:
   p=a.get('translation',[0,0,0]);q=b.get('translation',[0,0,0]);d=math.hypot(p[0]-q[0],p[2]-q[2])
   if d<1.1:pairs.append([a['name'],b['name'],round(d,3)])
 reports.append({'id':file.stem,'triangles':count,'mesh_modules':len(j['meshes']),'worker_count':len(workers),'standing_body_radius_checked_m':.4,'potential_geometry_conflicts':conflicts,'overlapping_worker_reservations':pairs})
assert len(reports)==15
out=R/'docs/art-direction/settlement-kit/v1/export-audit.json';out.write_text(json.dumps(reports,indent=2));print(json.dumps(reports,indent=2))
