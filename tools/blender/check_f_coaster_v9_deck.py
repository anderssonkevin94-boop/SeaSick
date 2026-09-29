import bpy,json,sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
root=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v9';out=root/'flat-foredeck'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
dg=bpy.context.evaluated_depsgraph_get()
floor=bpy.data.objects['Foredeck_Floor']
zlevels=sorted(set(round(v.co.z,5) for v in floor.data.vertices))
vs=[];fs=[]
for o in bpy.context.scene.objects:
 if o.type=='MESH' and o.parent and o.parent.name=='Bow' and o.name.startswith('Bulwark_'):
  e=o.evaluated_get(dg);m=e.to_mesh();offset=len(vs)
  vs.extend(o.matrix_world@v.co for v in m.vertices);fs.extend([i+offset for i in p.vertices] for p in m.polygons);e.to_mesh_clear()
tree=BVHTree.FromPolygons(vs,fs);misses=[];count=0
# Rays from the open deck to the wall immediately above the walking surface.
# Sample both sides and several heights, independent of authoring helpers.
for x in [17.8+i*(22.0-17.8)/84 for i in range(85)]:
 for z in [2.13,2.18,2.26,2.40,2.60]:
  for side in [-1,1]:
   count+=1
   if tree.ray_cast(Vector((x,0,z)),Vector((0,side,0)),5)[0] is None:misses.append([x,z,side])
r={'flat_deck':zlevels==[1.89,2.11],'floor_vertex_heights':zlevels,'wall_coverage_rays':count,'wall_misses':misses,'passed':zlevels==[1.89,2.11] and not misses}
(root/'deck-validation.json').write_text(json.dumps(r,indent=2));print(r)
assert r['passed']
bpy.context.scene.cycles.samples=24
kit.shot(out/'foredeck-day.png',(17,-13,13),(20.4,0,2.8),11,(1500,1100))
kit.shot(out/'foredeck-low.png',(18,-10,4.7),(21,0,2.4),9,(1400,900))
