"""Export approved study into grouped Unity vertex-colour meshes and fittings."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
previous=bpy.context.window.scene
with bpy.data.libraries.load(str(ROOT/'tools/blender/source/adventure-brig-design-v4.blend'),link=False) as (src,dst):
 dst.scenes=[next(n for n in src.scenes if n.startswith('Adventure_Brig_Design_Study_V4'))]
scene=dst.scenes[0];bpy.context.window.scene=scene
try:
 bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get()
 hull=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(('Hull_Port','Hull_Starboard'))]
 coords=[o.matrix_world@v.co for o in hull for v in o.data.vertices]
 lo=min(v.x for v in coords);hi=max(v.x for v in coords)
 sx=26/(hi-lo);cx=(hi+lo)*.5;sy=7.8/(2*max(abs(v.y) for v in coords));sz=2.5/abs(min(v.z for v in coords))
 def conv(p):return {'x':round(-p.y*sy,5),'y':round(p.z*sz,5),'z':round((p.x-cx)*sx,5)}
 bvhs=[BVHTree.FromPolygons([o.matrix_world@v.co for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons]) for o in hull]
 groups={};skip=('Presentation_water','Gunport_','Coral_port_','Cannon_barrel','Muzzle','Open_port_lid')
 for ob in scene.objects:
  if ob.type not in {'MESH','CURVE'} or ob.name.startswith(skip):continue
  ev=ob.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
  # Group an entire sail assembly, including its yard, emblem and stitches.
  movable=ob.name.startswith(('Sail_','Canvas_foot','Yard'))
  center=sum((ob.matrix_world@v.co for v in me.vertices),Vector())/max(1,len(me.vertices))
  mast='Aft' if center.x<1 else 'Fore';tier='Top' if center.z>12 else 'Course'
  key='Sail_'+mast+tier if movable else 'Structure'
  g=groups.setdefault(key,{'name':key,'vertices':[],'normals':[],'colors':[],'triangles':[]})
  nm=ob.matrix_world.to_3x3().inverted().transposed()
  for tri in me.loop_triangles:
   a,b,c0=[ob.matrix_world@me.vertices[i].co for i in tri.vertices]
   face=(b-a).cross(c0-a);flip=False
   if ob.name.startswith(('Deck_plank','Quarterdeck_fitted')):flip=face.z<0
   elif ob.name.startswith('Hull_Port'):flip=face.y>0
   elif ob.name.startswith('Hull_Starboard'):flip=face.y<0
   elif ob.name.startswith('Transom') and not ob.name.startswith('Transom_strake'):flip=face.x>0
   elif ob.name.startswith('Cabin_front_bulkhead'):flip=face.x<0
   base=len(g['vertices']);m=me.materials[tri.material_index] if len(me.materials)>tri.material_index else None
   c=m.diffuse_color if m else (.4,.3,.2,1)
   for vi,li in zip(tri.vertices,tri.loops):
    g['vertices'].append(conv(ob.matrix_world@me.vertices[vi].co));n=nm@me.corner_normals[li].vector;n=Vector((-n.y/sy,n.z/sz,n.x/sx)).normalized()
    if flip:n=-n
    g['normals'].append(dict(zip(['x','y','z'],n)));g['colors'].append(dict(zip(['r','g','b','a'],c)))
   g['triangles'].extend([base,base+1,base+2] if flip else [base,base+2,base+1])
  ev.to_mesh_clear()
 # Seven sockets match the existing brig bays. Frames and lids remain separate.
 bays=[-8.6,-5.6,-2.6,.4,3.4,6.4,9.4];sockets=[]
 def box(g,center,size,color):
  x,y,z=center;a,b,c=[v/2 for v in size]
  vs=[(x+dx*a,y+dy*b,z+dz*c) for dx,dy,dz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
  for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)]:
   n=(Vector(vs[f[1]])-Vector(vs[f[0]])).cross(Vector(vs[f[2]])-Vector(vs[f[0]])).normalized();base=len(g['vertices'])
   for i in f:g['vertices'].append(dict(zip(['x','y','z'],vs[i])));g['normals'].append(dict(zip(['x','y','z'],n)));g['colors'].append(dict(zip(['r','g','b','a'],color)))
   g['triangles'].extend([base,base+1,base+2,base,base+2,base+3])
 for bi,z in enumerate(bays):
  x=z/sx+cx;hits=[b.ray_cast(Vector((x,-20,1.9/sz)),Vector((0,1,0)),40)[0] for b in bvhs];hits=[p for p in hits if p is not None]
  width=max(abs(p.y)*sy for p in hits) if hits else 3
  sockets.append({'x':width-.55,'y':1.72,'z':z})
  for side in [-1,1]:
   g=groups['Structure'];w=side*(width+.06)
   box(g,(w,1.98,z),(.08,.88,1.05),(.025,.033,.032,1))
   for dz in [-.60,.60]:box(g,(w+side*.04,1.98,z+dz),(.13,1.08,.14),(.55,.105,.065,1))
   for dy in [-.49,.49]:box(g,(w+side*.04,1.98+dy,z),(.13,.14,1.34),(.55,.105,.065,1))
   key='PortLid_'+str(bi)+'_'+str(side);lid={'name':key,'vertices':[],'normals':[],'colors':[],'triangles':[]}
   box(lid,(w+side*.14,1.98,z),(.09,.86,1.06),(.035,.20,.225,1));groups[key]=lid
 deck=[]
 deckobs=[o for o in scene.objects if o.name.startswith(('Deck_plank','Quarterdeck_fitted_plank'))]
 db=[BVHTree.FromPolygons([o.matrix_world@v.co for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons]) for o in deckobs]
 for z in bays:
  hits=[b.ray_cast(Vector((z/sx+cx+dx,-1.56/sy+dy,15)),Vector((0,0,-1)),30)[0] for b in db for dx,dy in [(0,0),(.03,.03),(-.03,-.03)]];hits=[p for p in hits if p is not None]
  assert hits, 'Deck ray missing at '+str(z)
  deck.append(max(p.z*sz for p in hits))
 target=ROOT/'Assets/_Project/Resources/Ships';target.mkdir(parents=True,exist_ok=True)
 (target/'AdventureBrig.json').write_text(json.dumps({'groups':list(groups.values()),'stations':bays,'deckHeights':deck,'gunSockets':sockets},separators=(',',':')))
 print('Exported',len(groups),'groups,',sum(len(g['triangles'])//3 for g in groups.values()),'triangles; deck',deck)
finally:
 bpy.context.window.scene=previous
 for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(scene)
