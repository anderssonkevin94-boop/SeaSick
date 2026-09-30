import bpy,bmesh,math,json,random
from pathlib import Path
from mathutils import Vector,Matrix
P=Path(__file__).resolve().parents[2]/'art-staging/resource-kit-v1';P.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;sc.unit_settings.system='METRIC'
pal={'bark':(.17,.068,.024,1),'cut':(.64,.35,.13,1),'heart':(.40,.18,.055,1),'board':(.55,.29,.095,1),'edge':(.29,.125,.033,1),'stone':(.40,.48,.51,1),'ore':(.08,.13,.15,1),'copper':(.65,.29,.08,1),'brick':(.53,.16,.065,1),'brickedge':(.28,.065,.025,1)}
mat=bpy.data.materials.new('SS_Resources_GameColor');mat.use_nodes=True;v=mat.node_tree.nodes.new('ShaderNodeVertexColor');v.layer_name='GameColor';bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.85;mat.node_tree.links.new(v.outputs['Color'],bs.inputs['Base Color'])
class Mesh:
 def __init__(self):self.v=[];self.f=[];self.c=[]
 def face(self,pts,c,shade=1):
  n=len(self.v);self.v.extend(pts);self.f.append(list(range(n,n+len(pts))));co=pal[c];self.c.append(tuple(x*shade if i<3 else x for i,x in enumerate(co)))
 def join(self,other,at=(0,0,0),scale=(1,1,1),yaw=0):
  n=len(self.v);rot=Matrix.Rotation(yaw,3,'Z');self.v += [tuple(rot@Vector((p[0]*scale[0],p[1]*scale[1],p[2]*scale[2]))+Vector(at)) for p in other.v];self.f += [[i+n for i in f] for f in other.f];self.c+=other.c
 def obj(self,name):
  me=bpy.data.meshes.new(name);me.from_pydata(self.v,[],self.f);me.update();o=bpy.data.objects.new(name,me);sc.collection.objects.link(o);me.materials.append(mat);ca=me.color_attributes.new(name='GameColor',type='FLOAT_COLOR',domain='CORNER')
  for f,c in zip(me.polygons,self.c):
   for i in f.loop_indices:ca.data[i].color=c
  bm=bmesh.new();bm.from_mesh(me);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free();return o

def log():
 m=Mesh();n=10;r=.12;L=1.6;rings=[]
 for k,y in enumerate([-L/2,-L/2+.045,L/2-.04,L/2]):
  rings.append([(math.cos(i*math.tau/n)*r*(1+.06*math.sin(i*3)),y,.12+math.sin(i*math.tau/n)*r*(1+.06*math.sin(i*3))) for i in range(n)])
 for a,b in zip(rings,rings[1:]):
  for i in range(n):m.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],'bark',.85+.22*(i%3)/2)
 for k,sign in [(0,-1),(-1,1)]:
  outer=rings[k];inner=[(x*.83,y+sign*.001,.12+(z-.12)*.83) for x,y,z in outer]
  for i in range(n):m.face([outer[i],outer[(i+1)%n],inner[(i+1)%n],inner[i]],'bark')
  small=[(x*.42,y+sign*.001,.12+(z-.12)*.42) for x,y,z in inner]
  for i in range(n):m.face([inner[i],inner[(i+1)%n],small[(i+1)%n],small[i]],'cut')
  m.face(small,'heart')
 return m

def block(size,top,edge,bevel):
 x,y,z=size;b=bevel;m=Mesh();rings=[]
 for h,inset in [(0,b),(b,0),(z-b,0),(z,b)]:
  xx=x/2-inset;yy=y/2-inset;cut=max(.002,b*.6)
  rings.append([(-xx+cut,-yy,h),(xx-cut,-yy,h),(xx,-yy+cut,h),(xx,yy-cut,h),(xx-cut,yy,h),(-xx+cut,yy,h),(-xx,yy-cut,h),(-xx,-yy+cut,h)])
 m.face(rings[0][::-1],edge);m.face(rings[-1],top)
 for k,(a,b) in enumerate(zip(rings,rings[1:])):
  for i in range(8):m.face([a[i],a[(i+1)%8],b[(i+1)%8],b[i]],edge if k!=1 else top,.87+.1*(i%3))
 return m

def rock(ore=False,seed=0):
 rnd=random.Random(seed+25);m=Mesh();rings=[];n=7
 for z,r in [(0,.17),(.085,.25),(.22,.215),(.31,.09)]:
  rings.append([(math.cos(i*math.tau/n)*r*(.85+rnd.random()*.3),math.sin(i*math.tau/n)*r*(.85+rnd.random()*.3),z) for i in range(n)])
 m.face(rings[0][::-1],'ore' if ore else 'stone');m.face(rings[-1],'copper' if ore else 'stone',1.15)
 for k,(a,b) in enumerate(zip(rings,rings[1:])):
  for i in range(n):m.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],'copper' if ore and ((i+2*k)%5==0) else 'ore' if ore else 'stone',.83+.24*rnd.random())
 return m
units={'Timber':log(),'Boards':block((.25,1.6,.075),'board','edge',.014),'Stone':rock(),'Ore':rock(True),'Brick':block((.36,.18,.12),'brick','brickedge',.012)}
assets=[];manifest=[]
def register(name,m,pivot,notes):
 o=m.obj(name);bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.fbx(filepath=str(P/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 o.data.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(o.data);boundary=sum(e.is_boundary for e in bm.edges);degenerate=sum(f.calc_area()<1e-10 for f in bm.faces);bm.free()
 manifest.append({'name':name,'triangles':len(o.data.loop_triangles),'dimensions_m':list(o.dimensions),'pivot':pivot,'notes':notes,'boundary_edges':boundary,'zero_area_faces':degenerate});assets.append(o);o.hide_render=True
for kind,u in units.items():
 register(kind+'_Unit',u,'bottom centre','Individual unit; stock counts and build progress remain runtime-owned.')
 single=Mesh()
 scales={'Timber':(.725,.725,.725),'Boards':(.52,.2875,.4667),'Stone':(.40,.40,.645),'Ore':(.40,.40,.645),'Brick':(.4722,.5556,.7083)}
 height={'Timber':.174,'Boards':.035,'Stone':.20,'Ore':.20,'Brick':.085}[kind]
 single.join(u,(0,0,-height/2),scales[kind],math.pi/2 if kind=='Boards' else 0)
 register(kind+'_CarryUnit',single,'grip centre','One carried item. Repeat up to existing runtime cap of six to preserve visible load counts.')
 carry=Mesh()
 if kind=='Timber':
  for i in range(2):carry.join(u,((i-.5)*.18,0,-.087),(.725,.725,.725))
 elif kind=='Boards':
  for i in range(3):carry.join(u,(0,0,i*.045-.0675),(.52,.2875,.4667),math.pi/2)
 elif kind in ['Stone','Ore']:
  for i in range(2):carry.join(u,((i-.5)*.17,0,-.10),(.40,.40,.645))
 else:
  for i in range(3):carry.join(u,(0,0,i*.09-.1325),(.4722,.5556,.7083))
 register(kind+'_Carry',carry,'grip centre','Display bundle; align to existing carry anchor. Not a stock count replacement.')
 stack=Mesh()
 if kind=='Timber':
  for row,n in enumerate([3,2,1]):
   for i in range(n):stack.join(u,((i-(n-1)/2)*.255,0,row*.208))
 elif kind=='Boards':
  for row in range(4):
   for i in range(3):stack.join(u,((i-1)*.275,(-1 if row%2 else 1)*.025,row*.081))
 elif kind in ['Stone','Ore']:
  for row,n in enumerate([4,3,1]):
   for i in range(n):
    a=i*math.tau/n;rad=.31 if row==0 else .21 if row==1 else 0
    stack.join(rock(kind=='Ore',i+row*5),(rad*math.cos(a),rad*math.sin(a),row*.22),yaw=i*1.3)
 else:
  for row in range(4):
   for i in range(3):
    for j in range(2):stack.join(u,((i-1)*.375+(.08 if row%2 else 0),(j-.5)*.195,row*.127))
 register(kind+'_Stack',stack,'bottom centre','Fixed review/static decoration stack. Use units for changing inventory piles.')
# studio; individual renders preserve true relative size across columns in each row
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012));floor=bpy.context.object;floor.name='REVIEW_floor';fm=bpy.data.materials.new('Backdrop');fm.use_nodes=True;fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.14,.19,.20,1);floor.data.materials.append(fm)
sc.world=bpy.data.worlds.new('Review world');sc.world.color=(.25,.25,.25)
for pos,energy,size in [((2,-3,5),600,5),((-3,2,4),400,4)]:
 bpy.ops.object.light_add(type='AREA',location=pos);li=bpy.context.object;li.data.energy=energy;li.data.shape='DISK';li.data.size=size;li.rotation_euler=(-li.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(2.4,-3.4,2.6));cam=bpy.context.object;sc.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=2.15;cam.rotation_euler=(Vector((0,0,.23))-cam.location).to_track_quat('-Z','Y').to_euler()
sc.render.engine='CYCLES';sc.cycles.samples=16;sc.cycles.use_denoising=True;sc.render.resolution_x=320;sc.render.resolution_y=280;sc.render.resolution_percentage=100;sc.view_settings.view_transform='AgX'
for o in assets:
 if o.name.endswith('CarryUnit'):continue
 o.hide_render=False;floor.location.z=-.16 if o.name.endswith('Carry') else -.012
 sc.render.filepath=str(P/(o.name+'.png'));bpy.ops.render.render(write_still=True);o.hide_render=True
for i,o in enumerate(assets):o.hide_render=False;o.location=((i//4-2)*2.3,(i%4-1.5)*2.4,0)
floor.location.z=-.16;cam.location=(7,-11,15);cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=14
bpy.ops.wm.save_as_mainfile(filepath=str(P/'resource-kit.blend'))
(P/'manifest.json').write_text(json.dumps(manifest,indent=2))
