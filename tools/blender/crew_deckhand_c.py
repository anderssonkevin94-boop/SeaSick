"""Standalone crew art study; execute via Blender MCP. No Unity assets."""
import bpy,math
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick');OUT=ROOT/'docs/art-direction/crew-concept/c'
OUT.mkdir(parents=True,exist_ok=True)
old=bpy.context.window.scene
scene=bpy.data.scenes.new('Crew_Study_C');bpy.context.window.scene=scene
try:
 def mat(n,c):
  m=bpy.data.materials.new('Crew_'+n);m.diffuse_color=(*c,1);m.use_nodes=True
  p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Roughness'].default_value=.78
  return m
 skin=mat('warm_skin',(.57,.30,.16));skinlight=mat('nose',(.64,.35,.19));cream=mat('linen',(.86,.77,.55));teal=mat('navy_teal',(.028,.14,.16));pants=mat('slate_trousers',(.095,.17,.20));coral=mat('red_scarf',(.65,.11,.06));leather=mat('boots_belt',(.105,.051,.027));hair=mat('dark_hair',(.075,.038,.023));eyes=mat('eyes',(.021,.025,.026));gold=mat('brass',(.65,.42,.12))
 root=bpy.data.objects.new('Crew_Deckhand_C',None);scene.collection.objects.link(root)
 def mesh(n,v,f,m):
  me=bpy.data.meshes.new(n);me.from_pydata(v,[],f);me.update();o=bpy.data.objects.new(n,me);scene.collection.objects.link(o);me.materials.append(m);o.parent=root;return o
 def ell(n,p,s,m):
  bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,location=p);o=bpy.context.object;o.name=n;o.scale=s;o.data.materials.append(m);o.parent=root
  for f in o.data.polygons:f.use_smooth=True
  return o
 def box(n,p,s,m,r=.025):
  bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.name=n;o.dimensions=s;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(m);o.parent=root
  if r:
   b=o.modifiers.new('Soft tailored edges','BEVEL');b.width=r;b.segments=3;o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
  return o
 def tube(n,centers,radii,m,N=12):
  v=[]
  for i,p in enumerate(centers):
   d=Vector(centers[min(i+1,len(centers)-1)])-Vector(centers[max(0,i-1)]);q=d.to_track_quat('Z','Y')
   rx,ry=radii[i] if isinstance(radii[i],tuple) else (radii[i],radii[i])
   for k in range(N):v.append(Vector(p)+q@Vector((rx*math.cos(k*math.tau/N),ry*math.sin(k*math.tau/N),0)))
  f=[tuple(range(N-1,-1,-1))]
  for i in range(len(centers)-1):
   for k in range(N):f.append((i*N+k,i*N+(k+1)%N,(i+1)*N+(k+1)%N,(i+1)*N+k))
  f.append(tuple((len(centers)-1)*N+k for k in range(N)));o=mesh(n,v,f,m)
  for p in o.data.polygons:p.use_smooth=True
  return o
 # A sturdy silhouette, enlarged head/hands and clear gaps between arms and waist.
 for side in [-1,1]:
  x=side*.145
  box('Leather_boot',(x,-.075,.115),(.23,.38,.23),leather,.06)
  tube('Trouser_leg',[(x,0,.21),(x,0,.44),(side*.12,.025,.70),(side*.105,.025,.94)],[(.105,.10),(.115,.11),(.13,.13),(.145,.14)],pants)
  tube('Boot_cuff',[(x,0,.22),(x,0,.30)],[.113,.115],leather)
 tube('Linen_shirt',[(0,0,.83),(0,0,1.02),(0,0,1.29),(0,0,1.40)],[(.23,.155),(.235,.15),(.32,.17),(.245,.15)],cream,20)
 # One continuous open-front garment, following the shirt instead of separate floating panels.
 v=[];f=[];N=28
 for z,rx,ry,gap in [(.95,.25,.175,.30),(1.08,.253,.175,.32),(1.29,.332,.19,.60),(1.405,.252,.166,.72)]:
  for k in range(N+1):
   t=-math.pi/2+gap+(math.tau-2*gap)*k/N
   v.append((rx*math.cos(t),ry*math.sin(t),z))
 for j in range(3):
  for k in range(N):f.append((j*(N+1)+k,j*(N+1)+k+1,(j+1)*(N+1)+k+1,(j+1)*(N+1)+k))
 vest=mesh('Tailored_vest',v,f,teal)
 for poly in vest.data.polygons:poly.use_smooth=True
 mod=vest.modifiers.new('Garment thickness','SOLIDIFY');mod.thickness=.022
 mod=vest.modifiers.new('Soft sewn edges','BEVEL');mod.width=.008;mod.segments=2
 tube('Waist_belt',[(0,0,.89),(0,0,.97)],[(.244,.168),(.246,.168)],leather,20)
 box('Belt_buckle',(0,-.183,.93),(.105,.035,.085),gold,.013)
 box('Buckle_inset',(0,-.204,.93),(.057,.012,.042),leather,.005)
 box('Belt_pouch',(.23,-.10,.86),(.14,.13,.19),leather,.035)
 # One arm rests at the belt; the other hangs ready to work.
 for side in [-1,1]:
  shoulder=Vector((side*.295,0,1.33))
  elbow=Vector((side*(.49 if side>0 else .43),-.015,1.12))
  wrist=Vector((.33,-.23,1.03)) if side>0 else Vector((-.46,-.12,.94))
  tube('Linen_sleeve',[shoulder,shoulder.lerp(elbow,.55),elbow],[.14,.135,.115],cream)
  tube('Rolled_cuff',[shoulder.lerp(elbow,.83),elbow.lerp(wrist,.09)],[.126,.126],cream)
  tube('Forearm',[elbow,elbow.lerp(wrist,.55),wrist],[.098,.086,.075],skin)
  ell('Hand',wrist+Vector((0,-.015,-.055)),(.104,.086,.145),skin)
  ell('Wrist',wrist,(.083,.08,.065),skin)
  ell('Thumb',wrist+Vector((-side*.072,-.059,-.005)),(.043,.052,.075),skinlight)
 tube('Neck',[(0,0,1.36),(0,0,1.52)],[.105,.103],skin)
 # Sculpted head rings: broad cheeks, narrower jaw, low-detail readable features.
 tube('Head',[(0,0,1.49),(0,-.015,1.54),(0,0,1.70),(0,.008,1.84),(0,.008,1.89)],[(.105,.11),(.155,.14),(.188,.16),(.173,.148),(.115,.10)],skin,20)
 for side in [-1,1]:
  ell('Ear',(side*.182,.002,1.69),(.042,.038,.07),skin)
  ell('Eye',(side*.075,-.150,1.725),(.025,.014,.025),eyes)
  brow=box('Eyebrow',(side*.073,-.163,1.773+(0.014 if side<0 else 0)),(.082,.021,.017),hair,.007);brow.rotation_euler.y=-side*.18
 ell('Nose',(0,-.176,1.676),(.056,.065,.050),skinlight)
 # Quiet mouth and small chin shadow rather than tiny realistic face detail.
 # A broad, slightly crooked smile reads without realistic facial detail.
 v=[]
 for i in range(17):
  x=-.067+.134*i/16;t=x/.067
  z=1.599+.020*t*t+.008*t
  v.extend([(x,-.164,z),(x,-.165,z-.027*(1-t*t))])
 mesh('Smile',v,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(16)],hair)
 box('Smile_teeth',(-.004,-.174,1.596),(.074,.014,.016),cream,.005)
 ell('Chin_beard',(0,-.112,1.536),(.097,.043,.035),hair)
 # Hair cap and coral headcloth: a readable splash of colour at game scale.
 ell('Hair_cap',(0,.02,1.825),(.176,.15,.115),hair)
 for x,z,angle in [(-.065,1.885,-.38),(.018,1.88,-.55)]:
  tuft=ell('Hair_sweep',(x,-.11,z),(.065,.065,.09),hair);tuft.rotation_euler.y=angle
 for side in [-1,1]:box('Sideburn',(side*.165,-.015,1.728),(.035,.07,.10),hair,.015)
 tube('Headband',[(0,.005,1.80),(0,.005,1.87)],[(.181,.157),(.166,.144)],coral,24)
 ell('Bandana_knot',(.13,.139,1.82),(.055,.047,.05),coral)
 mesh('Bandana_tails',[(.13,.15,1.82),(.22,.16,1.78),(.22,.17,1.60),(.16,.16,1.67),(.11,.155,1.81),(.12,.18,1.62),(.075,.17,1.69)],[(0,1,2,3),(0,4,5,6)],coral)
 tube('Neckerchief',[(0,0,1.41),(0,0,1.465)],[(.136,.117),(.12,.108)],coral,20)
 mesh('Scarf_tip',[(-.07,-.145,1.43),(.065,-.145,1.43),(.035,-.186,1.22),(-.025,-.19,1.25)],[(0,1,2,3)],coral)
 # Option C: maintain height while enlarging the head, widening the torso and shortening legs.
 bpy.context.view_layer.update()
 anchors=[(0,0),(.30,.22),(.90,.59),(1.40,1.25),(1.49,1.35),(1.98,1.90)]
 def remap(z):
  for (a,b),(c,d) in zip(anchors,anchors[1:]):
   if z<=c:return b+(z-a)/(c-a)*(d-b)
  return 1.90+(z-1.98)
 headnames=('Head','Ear','Eye','Eyebrow','Nose','Smile','Chin','Hair','Sideburn','Bandana')
 for ob in list(root.children):
  bpy.context.view_layer.objects.active=ob
  for mod in list(ob.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
  mw=ob.matrix_world.copy();inv=mw.inverted();ishead=ob.name.startswith(headnames)
  hand=ob.name.startswith(('Hand','Thumb','Wrist'));center=mw.translation.copy()
  for v in ob.data.vertices:
   p=mw@v.co
   if ishead:
    p.x*=1.40;p.y*=1.40;p.z=remap(p.z)
    # Slight tilt makes the expression feel attentive, not symmetrical.
    from mathutils import Matrix
    pivot=Vector((0,0,1.35));p=pivot+Matrix.Rotation(-.065,3,'Y')@(p-pivot)
   elif hand:
    q=p-center;p=Vector((center.x*1.12,center.y*1.08,remap(center.z)))+Vector((q.x*1.17,q.y*1.17,q.z*1.12))
   else:p=Vector((p.x*1.12,p.y*1.08,remap(p.z)))
   v.co=inv@p
  ob.data.update()
 # Merge each skin forearm/hand into a continuous sculpted volume.
 for side in [-1,1]:
  bpy.ops.object.select_all(action='DESELECT');parts=[]
  for ob in root.children:
   if ob.name.startswith(('Forearm','Hand','Wrist','Thumb')):
    center=sum((ob.matrix_world@v.co for v in ob.data.vertices),Vector())/len(ob.data.vertices)
    if center.x*side>0:parts.append(ob);ob.select_set(True)
  bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();ob=bpy.context.object;ob.name='Sculpted_hand_and_forearm_'+str(side)
  mod=ob.modifiers.new('Continuous skin','REMESH');mod.mode='VOXEL';mod.voxel_size=.008;bpy.ops.object.modifier_apply(modifier=mod.name)
  mod=ob.modifiers.new('Soft skin','SMOOTH');mod.factor=.65;mod.iterations=3;bpy.ops.object.modifier_apply(modifier=mod.name)
  for poly in ob.data.polygons:poly.use_smooth=True
 # Render studio.
 world=bpy.data.worlds.new('Crew_studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.23,.34,.40,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 for n,loc,power,size in [('Key',(-3,-4,6),450,4),('Fill',(4,0,4),250,3)]:
  d=bpy.data.lights.new(n,'AREA');o=bpy.data.objects.new(n,d);scene.collection.objects.link(o);o.location=loc;d.energy=power;d.shape='DISK';d.size=size;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
 floor=mat('studio_ground',(.07,.17,.20));o=box('Studio_floor',(0,0,-.055),(200,200,.1),floor,0);o.parent=None
 d=bpy.data.cameras.new('Crew_camera');cam=bpy.data.objects.new('Crew_camera',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO';d.ortho_scale=2.5;cam.location=(3,-7,3.1);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler()
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX'
 scene.render.filepath=str(OUT/'sailor-portrait.png');bpy.ops.render.render(write_still=True)
 bpy.data.libraries.write(str(ROOT/'tools/blender/source/crew-deckhand-c.blend'),{scene})
 # Separate context scene: linked ship art, no change to the saved brig.
 with bpy.data.libraries.load(str(ROOT/'tools/blender/source/adventure-brig-design-v8.blend'),link=False) as (src,dst):dst.scenes=[n for n in src.scenes if n.startswith('Adventure_Brig_Design_Study_V8')][:1]
 ship=dst.scenes[0];ship.name='Crew_Scale_Review_V1';bpy.context.window.scene=ship
 for ob in list(root.children):
  clone=ob.copy();clone.data=ob.data.copy();ship.collection.objects.link(clone);clone.parent=None;clone.matrix_world=ob.matrix_world.copy();clone.location+=Vector((.1,-1.65,2.76))
 cam=ship.camera;cam.data=cam.data.copy();cam.data.ortho_scale=14;cam.location=(6,-19,14);cam.rotation_euler=(Vector((0,-.5,3.7))-cam.location).to_track_quat('-Z','Y').to_euler();ship.render.resolution_x=1400;ship.render.resolution_y=1000;ship.cycles.samples=24;ship.render.filepath=str(OUT/'sailor-on-deck.png');bpy.ops.render.render(write_still=True)
 bpy.data.libraries.write(str(ROOT/'tools/blender/source/crew-scale-review-c.blend'),{ship})
 print('Crew example saved: option C proportions, separate editable parts; portrait and ship-scale renders. No Unity changes.')
finally:bpy.context.window.scene=old
