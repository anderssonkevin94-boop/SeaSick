"""Isolated paddle-impact asset development. Never reads/writes Unity editor state.
Outputs ONLY art-staging/paddle-splash-v1. Source kit is read-only.
"""
import bpy,math,json,sys,argparse,random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-staging/paddle-splash-v1';OUT.mkdir(exist_ok=True)
args=argparse.ArgumentParser();args.add_argument('--render',default='poster',choices=['poster','close','slow','wide','phone','test','all']);args.add_argument('--frames',type=int,default=72);opt=args.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;sc.unit_settings.system='METRIC'
sc.render.engine='CYCLES' if opt.render=='poster' else 'BLENDER_EEVEE';sc.cycles.samples=12;sc.cycles.use_denoising=True
# Eevee is used for motion review; asset preview lighting deliberately simple.
sc.render.engine='CYCLES' if opt.render=='poster' else 'BLENDER_EEVEE'
sc.render.resolution_x=1100;sc.render.resolution_y=780;sc.render.resolution_percentage=100;sc.render.image_settings.file_format='PNG';sc.render.fps=24
sc.view_settings.view_transform='AgX';sc.view_settings.look='AgX - Medium High Contrast'
def mat(name,col,rough=.5,metal=0):
 m=bpy.data.materials.new(name);m.diffuse_color=(*col,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*col,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal;return m
shipmat=mat('PREVIEW Ship vertex paint',(.5,.3,.1),.78);n=shipmat.node_tree.nodes.new('ShaderNodeVertexColor');n.layer_name='GameColor';shipmat.node_tree.links.new(n.outputs['Color'],shipmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
watermat=mat('PREVIEW Deep blue water',(.012,.085,.16),.48)
watermat.node_tree.nodes['Principled BSDF'].inputs['Specular IOR Level'].default_value=.15
fanmat=mat('Splash Blue',(.065,.43,.63),.25);foam=mat('Splash Ivory',(.66,.83,.85),.5);dropmat=mat('Splash Droplet',(.20,.61,.77),.2)
# Exact runtime kit geometry in actual metres, converted Unity->Blender.
kit=json.loads((ROOT/'art-staging/f-coaster-runtime/kit.json').read_text());rotor=None
for modelname,offset in [('SternRaised',(0,0,0)),('RotorRaised',(.06,0,.825)),('MiddleLow',(4.9,0,0)),('BowLow',(7.9,0,0))]:
 model=next(x for x in kit['models'] if x['name']==modelname)
 for part in model['parts']:
  vs=part['vertices'];ns=part['normals'];cs=part['colors'];ts=part['triangles'];v=[(vs[i+2],-vs[i],vs[i+1]) for i in range(0,len(vs),3)];norm=[(ns[i+2],-ns[i],ns[i+1]) for i in range(0,len(ns),3)];faces=[(ts[i],ts[i+2],ts[i+1]) for i in range(0,len(ts),3)]
  me=bpy.data.meshes.new(part['name']);me.from_pydata(v,[],faces);me.update();me.normals_split_custom_set_from_vertices(norm);ca=me.color_attributes.new(name='GameColor',type='FLOAT_COLOR',domain='CORNER')
  for poly in me.polygons:
   for li in poly.loop_indices:
    vi=me.loops[li].vertex_index;ca.data[li].color=cs[vi*4:vi*4+4]
  ob=bpy.data.objects.new('PREVIEW_'+part['name'],me);sc.collection.objects.link(ob);ob.location=offset;me.materials.append(shipmat)
  if modelname=='RotorRaised':rotor=ob
# A blue water surface that visibly meets the submerged wheel.
verts=[];faces=[];N=100;size=100
for y in range(N+1):
 for x in range(N+1):verts.append(((x/N-.5)*size,(y/N-.5)*size,0))
for y in range(N):
 for x in range(N):a=y*(N+1)+x;faces.append((a,a+1,a+N+2,a+N+1))
me=bpy.data.meshes.new('PREVIEW Water');me.from_pydata(verts,[],faces);me.materials.append(watermat);o=bpy.data.objects.new('PREVIEW Water',me);sc.collection.objects.link(o);sea=o
for p in me.polygons:p.use_smooth=True
# Fan is a curved, tapering 2D sheet with a scalloped broken crest.
# Local +Y runs across the blade, +X throws water away from contact, +Z is up.
COLS=16;ROWS=6
fanfaces=[]
for j in range(ROWS):
 for i in range(COLS):a=j*(COLS+1)+i;fanfaces.append((a,a+1,a+COLS+2,a+COLS+1))
def fanpoints(age,strength=1,seed=0):
 life=.43;p=min(1,max(0,age/life));grow=math.sin(math.pi*p)**.85
 out=[]
 for j in range(ROWS+1):
  u=j/ROWS
  for i in range(COLS+1):
   s=i/COLS*2-1;shape=max(.05,1-s*s)**.55
   jag=.90+.06*math.sin(i*1.5+seed)+.03*math.cos(i*3.1)
   out.append((u*(.10+.95*p)*strength,(s*(.38+.48*p)*u+s*.14)*strength,
    (u*grow*(.14+.53*shape)*jag-(p**3)*.22*u)*strength))
 return out
me=bpy.data.meshes.new('EntryFan');me.from_pydata(fanpoints(.0),[],fanfaces);me.materials.append(fanmat);me.materials.append(foam)
for p in me.polygons:
 row=p.index//COLS;col=p.index%COLS;p.material_index=1 if row==ROWS-1 and col%5!=1 else 0;p.use_smooth=True
ob=bpy.data.objects.new('Paddle_EntryFan',me);sc.collection.objects.link(ob);template=ob
ob.shape_key_add(name='Basis')
uv=me.uv_layers.new(name='UVMap')
for poly in me.polygons:
 for li in poly.loop_indices:
  vi=me.loops[li].vertex_index;uv.data[li].uv=(vi%(COLS+1)/COLS,vi//(COLS+1)/ROWS)
ages=[0,.06,.14,.24,.32,.38,.43,.48]
for idx,age in enumerate(ages[1:-1]):
 key=ob.shape_key_add(name='Age_'+str(age));
 for v,p in zip(key.data,fanpoints(age)):v.co=p
 for t,val in [(t,1 if t==age else 0) for t in ages]:key.value=val;key.keyframe_insert(data_path='value',frame=1+t*24)
for action in bpy.data.actions:
 try:
  for fc in action.fcurves:
   for k in fc.keyframe_points:k.interpolation='LINEAR'
 except AttributeError:pass
# Export the reusable animated fan alone, reset its root before preview instances.
bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
bpy.ops.export_scene.gltf(filepath=str(OUT/'Paddle_EntryFan.glb'),use_selection=True,export_format='GLB',export_animations=True,export_frame_range=False)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Paddle_EntryFan.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
ob.hide_render=True;ob.hide_set(True)
# Simple low-poly droplets, reusable unit mesh exported separately.
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1);drop=bpy.context.object;drop.name='Paddle_Droplet';drop.data.materials.append(dropmat)
for v in drop.data.vertices:v.co.z*=1.35
bpy.ops.export_scene.gltf(filepath=str(OUT/'Paddle_Droplet.glb'),use_selection=True,export_format='GLB');drop.hide_render=True;drop.hide_set(True)
# A pooled preview: each entry can produce fans at both exposed blade ends.
fans=[];drops=[];foamchips=[]
for k in range(10):
 fm=bpy.data.meshes.new('Preview fan geometry');fm.from_pydata(fanpoints(0),[],fanfaces);fm.materials.append(fanmat);fm.materials.append(foam)
 for p in fm.polygons:p.material_index=1 if p.index//COLS==ROWS-1 and p.index%5!=1 else 0;p.use_smooth=True
 f=bpy.data.objects.new('PREVIEW Impact fan',fm);sc.collection.objects.link(f);fans.append(f)
for k in range(100):
 d=bpy.data.objects.new('PREVIEW Droplet',drop.data);sc.collection.objects.link(d);drops.append(d)
# Flat chunky patches follow the surface, not billboards facing the camera.
chipmesh=bpy.data.meshes.new('Foam fleck');chipmesh.from_pydata([(-1,-.5,0),(-.55,-1,0),(.5,-.75,0),(1,.05,0),(.35,.7,0),(-.65,.65,0)],[],[(0,1,2,3,4,5)]);chipmesh.materials.append(foam)
for k in range(100):
 f=bpy.data.objects.new('PREVIEW Foam fleck',chipmesh);sc.collection.objects.link(f);foamchips.append(f)
R=1.675;AXLE=.825;BLADES=15;entry=math.acos(-AXLE/R);period=math.tau/BLADES
# preview events are crossing-time based, not a continuous emitter.
def wave(x,y,t):return .025*math.sin(x*1.45+y*.7-t*2)+.014*math.sin(y*2-x*.5+t*1.5)
def update(t,omega=1.4):
 rotor.rotation_euler.y=omega*t
 for v in sea.data.vertices:v.co.z=wave(v.co.x,v.co.y,t)
 sea.data.update()
 interval=period/omega
 # Entering blade contact is inside the wheel well. Outboard fans escape its sides.
 ids=[]
 latest=math.floor((omega*t-entry)/period)
 for j in range(5):
  event=latest-j;born=(entry+event*period)/omega;age=t-born
  for side in [-1,1]:ids.append((event,age,side))
 for f,(event,age,side) in zip(fans,ids):
  f.hide_render=not(0<=age<=.43)
  if f.hide_render:continue
  strength=(.65+.35*omega/1.4)*(1+.09*math.sin(event*2.1+side))
  for v,p in zip(f.data.vertices,fanpoints(age,strength,event*.8)):v.co=p
  f.data.update();f.location=(-.45,side*1.56,wave(-.45,side*1.56,t));f.rotation_euler.z=side*2.10
  # Main fan is pushed sideways out of the well; short tail momentum aft.
 for k,d in enumerate(drops):
  event,age,side=ids[k//10];rng=random.Random(event*29+k%10+side*133);delay=.045+rng.random()*.06;a=age-delay
  d.hide_render=not(0<a<.65)
  if d.hide_render:continue
  vel=Vector((-.25-rng.random()*.65,side*(.65+rng.random()*1.25),1.6+rng.random()*1.5))*(.65+.35*omega/1.4)
  p=Vector((-.45,side*1.56,0))+vel*a+Vector((0,0,-4.9*a*a))
  if p.z<wave(p.x,p.y,t):d.hide_render=True;continue
  d.location=p;radius=(.032+rng.random()*.026)*max(.2,1-a/.75);d.scale=(radius,radius,radius*(1.4 if a<.18 else 1));d.rotation_euler=(vel+Vector((0,0,-9.8*a))).to_track_quat('Z','Y').to_euler()
 for k,f in enumerate(foamchips):
  event=latest-k//10;born=(entry+event*period)/omega;age=t-born;rng=random.Random(event*101+k%10)
  f.hide_render=not(.03<age<1.25) or k%10>4
  if f.hide_render:continue
  y=(rng.random()*2-1)*1.5; x=-.10-age*(1.1+omega*.4)-rng.random()*.25
  f.location=(x,y,wave(x,y,t)+.015);fade=math.sin(min(1,age/1.25)*math.pi)**.7
  f.scale=(fade*(.045+rng.random()*.065),fade*(.025+rng.random()*.05),1);f.rotation_euler.z=rng.random()*math.tau
sc.world=bpy.data.worlds.new('Preview sky');sc.world.use_nodes=True;sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.50,.68,.85,1);sc.world.node_tree.nodes['Background'].inputs[1].default_value=.6
for name,pos,power,size,color in [('Sun',(-4,-5,9),1500,6,(1,.91,.78)),('Sky',(3,5,7),1100,7,(.7,.84,1))]:
 l=bpy.data.lights.new('PREVIEW '+name,'AREA');l.energy=power;l.shape='DISK';l.size=size;l.color=color;o=bpy.data.objects.new('PREVIEW '+name,l);sc.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;cam.name='PREVIEW Camera';sc.camera=cam;cam.data.type='ORTHO'
def camera(wide=False):
 cam.location=(-6.2,-7.5,4.4) if not wide else (-12,-5,9)
 target=Vector((.15,0,.85)) if not wide else Vector((3.6,0,1.0));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=5.9 if not wide else 14
camera();update(2.48);bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'paddle-splash-preview.blend'))
(OUT/'manifest.json').write_text(json.dumps({'fan_vertices':(COLS+1)*(ROWS+1),'fan_triangles':len(fanfaces)*2,'droplet_triangles':len(drop.data.polygons),'lifetime_seconds':.43,'blade_count':BLADES,'wheel_radius_m':R,'preview_only':'Ship, sea, lights, pooled objects. Export only Paddle_EntryFan and Paddle_Droplet.','axes':'Blender +X outward, +Y blade span, +Z up. glTF/FBX exported Y up.'},indent=2))
if opt.render=='poster':
 # Choose early impact age, with visible fan silhouette.
 t=(entry+5*period)/1.4+.18;update(t);sc.cycles.samples=24;sc.render.filepath=str(OUT/'impact-close.png');bpy.ops.render.render(write_still=True)
 camera(True);sc.render.filepath=str(OUT/'impact-wide.png');bpy.ops.render.render(write_still=True)
else:
 sc.render.engine='BLENDER_EEVEE';
 if hasattr(sc.eevee,'taa_render_samples'):sc.eevee.taa_render_samples=64
 sc.cycles.samples=8;sc.render.resolution_x=900;sc.render.resolution_y=640
 variants=['close','slow','wide'] if opt.render=='all' else [opt.render]
 for variant in variants:
  camera(variant in ['wide','phone']);folder=OUT/('frames-'+variant);folder.mkdir(exist_ok=True);omega=.65 if variant=='slow' else 1.4
  if variant in ['wide','phone']:sc.render.resolution_x=540;sc.render.resolution_y=960;cam.data.ortho_scale=17
  else:sc.render.resolution_x=900;sc.render.resolution_y=640
  for frame in range(1 if variant=='test' else opt.frames):
   sc.frame_set(frame+1);update(2+frame/24,omega);sc.render.filepath=str(folder/f'{frame:04d}.png');bpy.ops.render.render(write_still=True)
print('PADDLE_SPLASH_DONE',flush=True)
