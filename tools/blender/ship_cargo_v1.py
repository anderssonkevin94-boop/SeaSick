"""Reusable ship cargo, true metres, bottom-centre pivots; offline Blender only."""
import bpy,bmesh,math,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-staging/ship-cargo-v1';OUT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
palette={'wood':(.43,.215,.073,1),'light':(.58,.32,.12,1),'dark':(.30,.125,.039,1),'iron':(.045,.068,.083,1),'cream':(.65,.52,.32,1),'ochre':(.39,.23,.105,1),'rope':(.39,.28,.13,1)}
materials={}
for key,rough,metal in [('Wood',.76,0),('Iron',.48,.8),('Cloth',.92,0),('Rope',.92,0)]:
 m=bpy.data.materials.new('SS_Cargo_'+key);m.use_nodes=True;n=m.node_tree.nodes;p=n.get('Principled BSDF');p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal;c=n.new('ShaderNodeVertexColor');c.layer_name='GameColor';m.node_tree.links.new(c.outputs['Color'],p.inputs['Base Color']);materials[key]=m
class Mesh:
 def __init__(self):self.v=[];self.f=[];self.c=[];self.m=[]
 def face(self,pts,col,tag='Wood',shade=1):
  i=len(self.v);self.v.extend(pts);self.f.append(tuple(range(i,i+len(pts))));self.c.append(tuple(x*shade if j<3 else x for j,x in enumerate(palette[col])));self.m.append(tag)
 def box(self,c,s,col='wood',tag='Wood'):
  p=[(c[0]+x*s[0]/2,c[1]+y*s[1]/2,c[2]+z*s[2]/2) for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
  for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:self.face([p[j] for j in f],col,tag)
 def loft(self,rings,col,tag='Wood',cap=True,shade=1):
  if cap:self.face(rings[0][::-1],col,tag,shade);self.face(rings[-1],col,tag,shade)
  for k,(a,b) in enumerate(zip(rings,rings[1:])):
   for i in range(len(a)):self.face([a[i],a[(i+1)%len(a)],b[(i+1)%len(a)],b[i]],col,tag,shade*(1+.025*math.sin(i*2.3+k)))
 def object(self,name,bevel=0):
  me=bpy.data.meshes.new(name);me.from_pydata(self.v,[],self.f);me.update();o=bpy.data.objects.new(name,me);scene.collection.objects.link(o)
  for m in materials.values():me.materials.append(m)
  ca=me.color_attributes.new(name='GameColor',type='FLOAT_COLOR',domain='CORNER')
  for p,c,t in zip(me.polygons,self.c,self.m):
   p.material_index=list(materials).index(t)
   for j in p.loop_indices:ca.data[j].color=c
  bm=bmesh.new();bm.from_mesh(me);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
  if bevel:
   mod=o.modifiers.new('Carved edges','BEVEL');mod.width=bevel;mod.segments=1
   bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.modifier_apply(modifier=mod.name);o.select_set(False)
  return o

def tube(mesh,pts,r,col='rope',tag='Rope',sides=6):
 rings=[]
 for i,p in enumerate(pts):
  tangent=(Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(i-1,0)])).normalized();axis=tangent.cross(Vector((0,0,1)))
  if axis.length<.01:axis=tangent.cross(Vector((0,1,0)))
  axis.normalize();v=tangent.cross(axis).normalized();rings.append([tuple(Vector(p)+r*(axis*math.cos(j*math.tau/sides)+v*math.sin(j*math.tau/sides))) for j in range(sides)])
 mesh.loft(rings,col,tag)

def barrel(name,h,r):
 m=Mesh();n=16;profile=[(0,.84),(.055,.89),(.23,1),(.50,1.055),(.77,1),(.945,.89),(1,.84)]
 for i in range(n):
  aa=i*math.tau/n+.006;bb=(i+1)*math.tau/n-.006;rings=[]
  for z,rad in profile:rings.append([(r*rad*math.cos(a),r*rad*math.sin(a),h*z) for a in [aa,bb]]+[(r*(rad-.085)*math.cos(a),r*(rad-.085)*math.sin(a),h*z) for a in [bb,aa]])
  m.loft(rings,'wood' if i%3 else 'light',shade=.92+.11*math.sin(i*1.9))
 # Actual inset plank lid, clipped to the polygonal opening.
 rad=r*.79;steps=5
 for j in range(steps):
  lo=-rad+2*rad*j/steps+.003;hi=-rad+2*rad*(j+1)/steps-.003
  poly=[(rad*math.cos(k*math.tau/32),rad*math.sin(k*math.tau/32)) for k in range(32)]
  for x,greater in [(lo,True),(hi,False)]:
   clipped=[]
   for a,b in zip(poly,poly[1:]+poly[:1]):
    ina=a[0]>=x if greater else a[0]<=x;inb=b[0]>=x if greater else b[0]<=x
    if ina:clipped.append(a)
    if ina!=inb:
     t=(x-a[0])/(b[0]-a[0]);clipped.append((x,a[1]+t*(b[1]-a[1])))
   poly=clipped
  m.loft([[(x,y,z) for x,y in poly] for z in [h-.045,h-.025]],'light' if j%2 else 'wood')
 for z in [.16,.82]:
  rr=r*(.89+(.16-.055)/(.23-.055)*.11) if z==.16 else r*.965
  rings=[]
  for zz,rrr in [(h*z-.035,rr+.015),(h*z+.035,rr+.015),(h*z+.035,rr-.012),(h*z-.035,rr-.012),(h*z-.035,rr+.015)]:rings.append([(rrr*math.cos(k*math.tau/n),rrr*math.sin(k*math.tau/n),zz) for k in range(n)])
  m.loft(rings,'iron','Iron',False)
 return m.object(name,.004)

def crate(name,w,d,h):
 m=Mesh();t=.055
 for axis in [0,1]:
  length=w if axis==0 else d;other=d if axis==0 else w
  for side in [-1,1]:
   for j in range(3):
    c=[0,0,(j+.5)*h/3];s=[length,t,h/3-.006]
    if axis==0:c[1]=side*(d-t)/2
    else:c[0]=side*(w-t)/2;s=[t,length,h/3-.006]
    m.box(c,s,'wood' if j%2 else 'light')
 for j in range(4):m.box((-w/2+(j+.5)*w/4,0,h-t/2-.020),(w/4-.006,d-.018,t),'wood' if j%2 else 'light')
 m.box((0,0,t/2),(w-.04,d-.04,t),'dark')
 # Strong framing, sized for stacking; no loose handles that snag crew.
 for x in [-w/2+.055,w/2-.055]:
  for y in [-d/2-.018,d/2+.018]:m.box((x,y,(h+.016)/2),(.105,.07,h+.016),'light')
 for y in [-d/2-.018,d/2+.018]:
  for z in [.055,h-.039]:m.box((0,y,z),(w-.21,.07,.11),'light')
 for x in [-w/2-.018,w/2+.018]:
  for z in [.055,h-.039]:m.box((x,0,z),(.07,d-.16,.11),'light')
 for x in [-w*.29,w*.29]:m.box((x,0,h+.005),(.095,d-.020,.05),'light')
 return m.object(name,.012)

def sack(name,h,w,d,col):
 m=Mesh();n=16;rings=[]
 # Broad asymmetric shoulders and gathered cloth, with a flat seated base.
 for k,(z,r) in enumerate([(0,.57),(.045,.83),(.18,1),(.39,1.02),(.60,.91),(.76,.68),(.84,.32),(.90,.26),(.96,.34),(1,.32)]):
  ring=[]
  for i in range(n):
   a=i*math.tau/n;fold=(.025+.08*max(0,(z-.60)/.40))*math.sin(a*5+.4);rad=r+fold
   ring.append((w*.5*rad*math.cos(a)+.045*z*z,d*.5*rad*math.sin(a)+.022*math.sin(z*4),h*z+(h*.012*math.sin(a*3) if k==9 else 0)))
  rings.append(ring)
 m.loft(rings,col,'Cloth')
 neckZ=h*.875;center=(.045*.875**2,.022*math.sin(.875*4),neckZ)
 for dz in [-.010,.009]:tube(m,[(center[0]+w*.155*math.cos(i*math.tau/24),center[1]+d*.155*math.sin(i*math.tau/24),neckZ+dz) for i in range(25)],.012)
 # Compact knot and two short hanging ends.
 tube(m,[(center[0]+.015,center[1]-d*.16,neckZ),(center[0]+.047,center[1]-d*.19,neckZ+.025),(center[0]+.067,center[1]-d*.18,neckZ),(center[0]+.025,center[1]-d*.16,neckZ-.017)],.014)
 for s in [-1,1]:tube(m,[(center[0]+.03,center[1]-d*.17,neckZ),(center[0]+s*.06,center[1]-d*.24,neckZ-.06),(center[0]+s*.055,center[1]-d*.29,neckZ-.12)],.011)
 return m.object(name)

assets=[barrel('Cargo_Barrel_Large',.85,.31),barrel('Cargo_Barrel_Small',.62,.25),crate('Cargo_Box_Large',.82,.64,.56),crate('Cargo_Box_Small',.56,.47,.44),sack('Cargo_Sack_Cream',.67,.55,.43,'cream'),sack('Cargo_Sack_Ochre',.49,.49,.39,'ochre')]
manifest=[]
for o in assets:
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 # UVs for future shared texture/bake work; no lighting baked into this kit.
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.015);bpy.ops.object.mode_set(mode='OBJECT')
 o.data.calc_loop_triangles();bounds=[min(v.co[k] for v in o.data.vertices) for k in range(3)]+[max(v.co[k] for v in o.data.vertices) for k in range(3)]
 assert abs(bounds[2])<.001,(o.name,bounds)
 bpy.ops.export_scene.gltf(filepath=str(OUT/(o.name+'.glb')),use_selection=True,export_format='GLB',export_yup=True)
 bpy.ops.export_scene.fbx(filepath=str(OUT/(o.name+'.fbx')),use_selection=True,object_types={'MESH'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_anim=False)
 manifest.append(dict(name=o.name,triangles=len(o.data.loop_triangles),bounds_blender=bounds,dimensions_m=[bounds[i+3]-bounds[i] for i in range(3)],pivot='bottom centre',materials=[m.name for m in o.data.materials]))
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
# Clean layout saved separately from the origin-centred individual exports.
for i,o in enumerate(assets):
 col=i//2;small=i%2;o.location=((col-1)*1.50+(.25 if small else -.18),.57 if small else -.47,0);o.rotation_euler.z=math.radians(-10 if col==1 else 8*small)
# Studio ground only; excluded from all exports.
bpy.ops.mesh.primitive_plane_add(size=200);ground=bpy.context.object;ground.name='PREVIEW_Ground';g=bpy.data.materials.new('PREVIEW slate');g.diffuse_color=(.105,.145,.16,1);g.use_nodes=True;g.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=g.diffuse_color;g.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.95;ground.data.materials.append(g);ground.location.z=-.006
scene.world=bpy.data.worlds.new('Studio world');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.64,.73,.82,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
for name,pos,power,size,color in [('Key',(-3,-4,7),650,5,(1,.89,.73)),('Fill',(4,-1,4),400,4,(.72,.84,1)),('Rim',(0,4,6),750,4,(1,.95,.84))]:
 l=bpy.data.lights.new(name,'AREA');l.energy=power;l.shape='DISK';l.size=size;l.color=color;o=bpy.data.objects.new(name,l);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,.3))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(2.7,-7.8,5.7));cam=bpy.context.object;cam.name='PREVIEW_Camera';cam.rotation_euler=(Vector((0,0,.35))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=5.6;scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True;scene.render.resolution_x=1800;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ship-cargo.blend'));scene.render.filepath=str(OUT/'cargo-set.png');bpy.ops.render.render(write_still=True)
# A compact, readable cargo grouping shown on an actual metre-scaled deck patch.
for o in assets:o.hide_render=True
for o,pos,rot in [(assets[0],(-.54,.26,0),-.1),(assets[2],(.28,.20,0),.06),(assets[4],(.23,-.46,0),-.25)]:o.hide_render=False;o.location=pos;o.rotation_euler.z=rot
m=Mesh()
for i in range(10):m.box(((i-4.5)*.27,0,-.065),(.264,2.0,.12),'light' if i%3 else 'wood')
deck=m.object('PREVIEW_Deck',.007);ground.location.z=-.13
cam.location=(3,-4,3.2);cam.rotation_euler=(Vector((0,0,.30))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.25;scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.render.filepath=str(OUT/'cargo-deck-group.png');bpy.ops.render.render(write_still=True)
print('CARGO_COMPLETE',json.dumps(manifest),flush=True)
