"""Independent topology, rear wall coverage and finished-interface checks."""
import bpy,bmesh,json,sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v29'
dest=ROOT/'base-bow-raised-stern'
bpy.ops.wm.open_mainfile(filepath=str(dest/'ship.blend'))
m=json.loads((dest/'manifest.json').read_text())
names={n for e in m['exports'] for n in e['parts']}
dg=bpy.context.evaluated_depsgraph_get();records=[];trees={}
for name in sorted(names):
 o=bpy.data.objects[name]
 if o.type!='MESH':continue
 ev=o.evaluated_get(dg);mesh=ev.to_mesh();bm=bmesh.new();bm.from_mesh(mesh)
 records.append({'name':name,'boundary_edges':sum(e.is_boundary for e in bm.edges),
                 'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
                 'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces)})
 trees[name]=BVHTree.FromPolygons([o.matrix_world@v.co for v in mesh.vertices],[list(p.vertices) for p in mesh.polygons])
 bm.free();ev.to_mesh_clear()
wall=trees['Transom_Wall'];misses=[];samples=0
for y in [-3.8+i*.2 for i in range(39)]:
 for z in [m["paddle"]["platform_z_u"]+.10+i*.04 for i in range(24)]:
  samples+=1
  if wall.ray_cast(Vector((-1.5,y,z)),Vector((1,0,0)),6)[0] is None:misses.append([y,z])
# Compare all visible source profiles at both interfaces, not only the hull.
def profile(parent,x,prefixes):
 points=[]
 for name in names:
  o=bpy.data.objects[name]
  if o.type!='MESH' or not o.parent or o.parent.name!=parent or not o.name.startswith(prefixes):continue
  points.extend((round(v.co.y,4),round(v.co.z,4)) for v in o.data.vertices if abs(v.co.x-x)<1e-4)
 return set(points)
interfaces=[]
for left,right,length in [('Stern','Middle_0',m['section_lengths_u']['stern']),('Middle_0','Bow',m['section_lengths_u']['middle'])]:
 for label,prefix in [('shell',('Hull_','Keel')),('band',('Solid_Timber_Wale',))]:
  a=profile(left,length,prefix);b=profile(right,0,prefix)
  interfaces.append({'join':left+' / '+right,'layer':label,'matches':bool(a) and a==b,'left_points':len(a),'right_points':len(b),'different_points':len(a^b)})
checks={'export_meshes_closed_manifold':all(r['nonmanifold_edges']==0 for r in records),
        'no_degenerate_faces':all(r['degenerate_faces']==0 for r in records),
        'rear_wall_coverage':not misses,
        'lower_hull_and_wale_interfaces_match':all(r['matches'] for r in interfaces)}
r={'passed':all(checks.values()),'checks':checks,'rear_wall_rays':samples,'rear_wall_misses':misses,'interfaces':interfaces,'topology':records,
   'scope':'Closed exported parts, sampled transom skin coverage and source interface matching. Intentional openings remain; this does not certify a globally watertight assembled hull or gameplay.'}
(ROOT/'construction-validation.json').write_text(json.dumps(r,indent=2));print(json.dumps(r,indent=2))
if not r['passed']:raise RuntimeError('Construction verification failed')
