"""Large sea fish: metres, Blender +X forward; closed faceted mesh and skinned swim loop."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parents[2]/'art-staging/large-fish-v1';OUT.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
cols=[('Deep teal',(.035,.16,.19,1)),('Blue flank',(.10,.35,.38,1)),('Silver belly',(.52,.68,.60,1)),('Dorsal stripe',(.28,.64,.58,1)),('Fin edge',(.10,.26,.28,1)),('Eye gold',(.87,.58,.16,1)),('Pupil',(.009,.025,.03,1))]
mats=[]
for name,c in cols:
 m=bpy.data.materials.new(name);m.diffuse_color=c;m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=c;p.inputs['Roughness'].default_value=.64;mats.append(m)
verts=[];faces=[];mi=[]
def face(points,col):
 ids=list(range(len(verts),len(verts)+len(points)));verts.extend(points);faces.append(ids);mi.append(col)
# Rounded heavy head, powerful shoulders and fine tail wrist. Shared vertices welded below.
rings=[(2.5,.05,.06,.02),(2.34,.39,.37,.01),(1.96,.68,.64,.04),(1.35,.84,.79,.06),(.5,.83,.82,.09),(-.35,.68,.68,.06),(-1.1,.48,.49,.02),(-1.7,.26,.29,0),(-2.15,.13,.18,0),(-2.4,.11,.16,0)]
N=16
for k,(a,b) in enumerate(zip(rings,rings[1:])):
 for j in range(N):
  pts=[]
  for ring,q in [(a,j),(b,j),(b,j+1),(a,j+1)]:
   x,w,h,z=ring;t=q*2*math.pi/N;pts.append((x,math.sin(t)*w,math.cos(t)*h+z))
  t=(j+.5)*2*math.pi/N;co=3 if j in [0,15] else (0 if math.cos(t)>.3 else 2 if math.cos(t)<-.45 else 1)
  face(pts,co)
for ring,rev in [(rings[0],True),(rings[-1],False)]:
 x,w,h,z=ring;pts=[(x,math.sin(j*2*math.pi/N)*w,math.cos(j*2*math.pi/N)*h+z) for j in range(N)];face(pts[::-1] if rev else pts,0)
def fin(poly,thick,axis,col=1):
 # Closed beveled-looking wedge, a central ridge on each face.
 points=[Vector(p) for p in poly];center=sum(points,Vector())/len(points);off=Vector(axis)*thick
 for sign in [-1,1]:
  tip=center+off*sign
  for i in range(len(points)):
   a=points[i];b=points[(i+1)%len(points)]
   face([tuple(a),tuple(b),tuple(tip)] if sign>0 else [tuple(b),tuple(a),tuple(tip)],col if i%3 else 3)
# Swept dorsal sail and smaller rear dorsal.
fin([(1.05,0,.80),(.18,0,1.60),(-.12,0,1.53),(-.90,0,.53)],.105,(0,1,0),0)
fin([(-1.14,0,.43),(-1.47,0,.71),(-1.83,0,.23)],.055,(0,1,0),1)
for side in [-1,1]:
 fin([(1.15,side*.65,.08),(.66,side*.97,-.05),(-.62,side*1.75,-.27),(-.19,side*.91,-.32),(.32,side*.60,-.33)],.07,(0,0,1),1)
 fin([(-.77,side*.30,-.30),(-1.67,side*.66,-.69),(-1.29,side*.18,-.30)],.045,(0,0,1),1)
# Upright crescent tail: pronounced fork, thick base, long tips.
fin([(-2.18,0,.14),(-2.63,0,.77),(-3.28,0,1.28),(-3.17,0,.65),(-2.79,0,0),(-3.17,0,-.65),(-3.28,0,-1.28),(-2.63,0,-.77),(-2.18,0,-.14)],.10,(0,1,0),1)
mesh=bpy.data.meshes.new('LargeFishMesh');mesh.from_pydata(verts,[],faces);mesh.update();ob=bpy.data.objects.new('LargeFish',mesh);bpy.context.collection.objects.link(ob)
for m in mats:mesh.materials.append(m)
for p,c in zip(mesh.polygons,mi):p.material_index=c
bpy.context.view_layer.objects.active=ob;ob.select_set(True)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.remove_doubles(threshold=.0001);bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
parts=[ob]
for side in [-1,1]:
 for name,loc,scale,mat in [('Eye',(2.03,side*.53,.24),(.16,.075,.16),5),('Pupil',(2.08,side*.586,.25),(.085,.035,.10),6)]:
  bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,location=loc);eye=bpy.context.object;eye.name=name;eye.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);eye.data.materials.append(mats[mat]);parts.append(eye)
 # Bold gill arc, broad enough to survive reduction.
 curve=bpy.data.curves.new('Gill','CURVE');curve.dimensions='3D';curve.bevel_depth=.025;curve.bevel_resolution=0
 spl=curve.splines.new('POLY');spl.points.add(3)
 for p,co in zip(spl.points,[(1.52,side*.73,.47,1),(1.37,side*.85,.20,1),(1.36,side*.84,-.14,1),(1.47,side*.73,-.39,1)]):p.co=co
 g=bpy.data.objects.new('Gill',curve);bpy.context.collection.objects.link(g);g.data.materials.append(mats[0]);bpy.context.view_layer.objects.active=g;ob.select_set(False);g.select_set(True);bpy.ops.object.convert(target='MESH');parts.append(bpy.context.object)
curve=bpy.data.curves.new('Mouth','CURVE');curve.dimensions='3D';curve.bevel_depth=.022;curve.bevel_resolution=0
spl=curve.splines.new('POLY');spl.points.add(4)
for p,co in zip(spl.points,[(2.24,-.42,-.20,1),(2.39,-.29,-.12,1),(2.50,0,-.035,1),(2.39,.29,-.12,1),(2.24,.42,-.20,1)]):p.co=co
mouth=bpy.data.objects.new('Mouth',curve);bpy.context.collection.objects.link(mouth);mouth.data.materials.append(mats[0]);bpy.ops.object.select_all(action='DESELECT');mouth.select_set(True);bpy.context.view_layer.objects.active=mouth;bpy.ops.object.convert(target='MESH');parts.append(bpy.context.object)
bpy.ops.object.select_all(action='DESELECT')
for p in parts:p.select_set(True)
bpy.context.view_layer.objects.active=ob;bpy.ops.object.join()
# Bake palette to GameColor as used by the existing game's art shader.
attr=ob.data.color_attributes.new(name='GameColor',type='FLOAT_COLOR',domain='CORNER')
for p in ob.data.polygons:
 c=ob.data.materials[p.material_index].diffuse_color
 for i in p.loop_indices:attr.data[i].color=c
arm=bpy.data.armatures.new('FishSkeleton');rig=bpy.data.objects.new('LargeFishRig',arm);bpy.context.collection.objects.link(rig);bpy.context.view_layer.objects.active=rig
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
xs=[2.6,.7,-.7,-1.7,-2.4,-3.4];names=['Body','Spine','TailBase','TailWrist','TailFin'];prev=None
for i,name in enumerate(names):
 b=arm.edit_bones.new(name);b.head=(xs[i],0,0);b.tail=(xs[i+1],0,0)
 if prev:b.parent=prev;b.use_connect=True
 prev=b
bpy.ops.object.mode_set(mode='OBJECT');ob.parent=rig;mod=ob.modifiers.new('Swim skin','ARMATURE');mod.object=rig
vg=[ob.vertex_groups.new(name=n) for n in names]
centers=[1.5,0,-1.2,-2.05,-2.9]
for v in ob.data.vertices:
 x=v.co.x;weights=[math.exp(-((x-c)/.75)**2) for c in centers];ids=sorted(range(5),key=lambda i:weights[i],reverse=True)[:2];total=sum(weights[i] for i in ids)
 for i in ids:vg[i].add([v.index],weights[i]/total,'REPLACE')
sc=bpy.context.scene;sc.render.fps=24;sc.frame_start=1;sc.frame_end=49
for frame in range(1,50,2):
 t=(frame-1)/48*2*math.pi
 for i,name in enumerate(names):
  b=rig.pose.bones[name];b.rotation_mode='XYZ';b.rotation_euler=(0,0,math.sin(t-i*.65)*[.025,.055,.12,.19,.26][i]);b.keyframe_insert(data_path='rotation_euler',frame=frame,group=name)
rig.animation_data.action.name='Swim_Slow_Loop'
sc.frame_set(1)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);ob.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'large-fish-rigged.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0)
# Review studio. Floor/camera/lights are excluded from export.
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-1.65));floor=bpy.context.object;floor.name='Review backdrop';m=bpy.data.materials.new('Sea blue backdrop');m.diffuse_color=(.035,.115,.15,1);m.use_nodes=True;m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=m.diffuse_color;floor.data.materials.append(m)
sc.world.color=(.22,.22,.22)
for loc,power,size in [((2,-5,8),1500,7),((-4,3,5),1100,5)]:
 bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(7,-10,6));cam=bpy.context.object;sc.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=8.8
sc.render.engine='CYCLES';sc.cycles.samples=32;sc.cycles.use_denoising=True;sc.render.resolution_x=1200;sc.render.resolution_y=850;sc.render.resolution_percentage=100
sc.view_settings.view_transform='AgX'
def shot(name,pos):
 cam.location=pos;cam.rotation_euler=(Vector((-.3,0,0))-cam.location).to_track_quat('-Z','Y').to_euler();sc.render.filepath=str(OUT/name);bpy.ops.render.render(write_still=True)
shot('fish-preview.png',(5,-9,5))
shot('fish-top.png',(-.3,-.01,12))
# Save with pleasing inspection angle.
cam.location=(5,-9,5);cam.rotation_euler=(Vector((-.3,0,0))-cam.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'large-fish.blend'))
ob.data.calc_loop_triangles()
report={'length_m':5.88,'triangles':len(ob.data.loop_triangles),'bones':names,'animation':'Swim_Slow_Loop, frames 1–49 at 24fps (2 seconds)','forward_blender':'+X','export':'FBX -Z forward, Y up','note':'Asset only; not placed in Unity. Review images have no actual ocean shader.'}
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
