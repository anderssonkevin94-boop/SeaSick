"""Reference-led low-poly character. Independent geometry, Blender only."""
import bpy,math,json
from mathutils import Vector
from pathlib import Path
R=Path('/Users/kevinandersson/Desktop/SeaSick');O=R/'docs/art-direction/crew-concept/male-coat-v1';E=R/'tools/blender/exports/crew-male-coat-v1';prev=bpy.context.window.scene
scene=bpy.data.scenes.new('Crew_Male_Coat_V1');bpy.context.window.scene=scene
try:
 C={'skin':(.78,.46,.25,1),'cream':(.92,.86,.69,1),'coral':(.8,.095,.025,1),'coral2':(.56,.075,.024,1),'leather':(.17,.077,.035,1),'dark':(.025,.028,.035,1),'hair':(.27,.094,.025,1),'hairlight':(.40,.165,.045,1),'hairshade':(.16,.042,.015,1),'white':(.97,.97,.89,1),'blue':(.025,.45,.64,1),'blue2':(.012,.15,.24,1),'lip':(.40,.15,.071,1)}
 O.mkdir(parents=True,exist_ok=True);E.mkdir(parents=True,exist_ok=True)
 C.update({'teal':(.025,.19,.22,1),'tealshade':(.022,.12,.15,1),'brass':(.66,.38,.10,1),'pants':(.16,.20,.23,1)})
 mat=bpy.data.materials.new('Character_vertex_palette');mat.use_nodes=True;p=mat.node_tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.78;a=mat.node_tree.nodes.new('ShaderNodeVertexColor');a.layer_name='Color';mat.node_tree.links.new(a.outputs['Color'],p.inputs['Base Color']);parts=[]
 def mesh(n,v,f,c,smooth=False):
  me=bpy.data.meshes.new(n);me.from_pydata(v,[],f);me.update();ob=bpy.data.objects.new(n,me);scene.collection.objects.link(ob);me.materials.append(mat);ca=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
  for i,poly in enumerate(me.polygons):
   poly.use_smooth=smooth;col=C[c[i%len(c)] if isinstance(c,list) else c]
   for li in poly.loop_indices:ca.data[li].color=col
  parts.append(ob);return ob
 def rings(n,rows,c,N=12,smooth=False):
  v=[]
  for x,y,z,rx,ry in rows:
   for k in range(N):t=k*math.tau/N;v.append((x+rx*math.cos(t),y+ry*math.sin(t),z))
  f=[tuple(range(N-1,-1,-1))]
  for j in range(len(rows)-1):
   for k in range(N):f.append((j*N+k,j*N+(k+1)%N,(j+1)*N+(k+1)%N,(j+1)*N+k))
  f.append(tuple((len(rows)-1)*N+k for k in range(N)));return mesh(n,v,f,c,smooth)
 def limb(n,points,radii,c,N=8):
  v=[]
  for i,p in enumerate(points):
   d=(Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])).normalized();u=(Vector((1,0,0))-d*d.x).normalized();w=d.cross(u)
   for k in range(N):v.append(Vector(p)+radii[i]*(u*math.cos(k*math.tau/N)+w*math.sin(k*math.tau/N)))
  f=[tuple(range(N-1,-1,-1))]
  for j in range(len(points)-1):
   for k in range(N):f.append((j*N+k,j*N+(k+1)%N,(j+1)*N+(k+1)%N,(j+1)*N+k))
  f.append(tuple((len(points)-1)*N+k for k in range(N)));return mesh(n,v,f,c)
 def plate(n,outline,y,thick,c):
  N=len(outline);v=[(x,y,z) for x,z in outline]+[(x,y+thick,z) for x,z in outline];f=[tuple(range(N-1,-1,-1)),tuple(range(N,N*2))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)];return mesh(n,v,f,c)
 def eye_disc(n,x,y,z,rx,rz,col,N=16):
  v=[(x,y-.005,z)]+[(x+rx*math.cos(k*math.tau/N),y,z+rz*math.sin(k*math.tau/N)) for k in range(N)]
  return mesh(n,v,[(0,k+1,(k+1)%N+1) for k in range(N)],col,True)
 # Male deckhand: practical boots, trousers and a tailored open sea coat.
 for side in [-1,1]:
  x=side*.112
  rings('Boot_sole',[(x,-.052,.018,.083,.142),(x,-.052,.05,.083,.142)],'dark',8)
  rings('Boot',[(x,-.052,.05,.081,.139),(x,-.05,.105,.08,.128),(x,0,.165,.064,.078),(x,0,.35,.067,.073)],'leather',8)
  rings('Boot_cuff',[(x,0,.31,.073,.08),(x,0,.365,.075,.08)],'leather',8)
  limb('Trouser_leg',[(x,0,.34),(side*.12,-.014,.53),(side*.105,0,.77),(side*.09,0,.90)],[.058,.071,.086,.097],'pants',8)
 rings('Shirt',[(0,0,.85,.146,.089),(0,0,1.04,.148,.095),(0,0,1.22,.188,.100),(0,0,1.30,.163,.077),(0,0,1.335,.071,.058)],'cream',12)
 # Coat wraps around the torso with an open front and solid edge thickness.
 rows=[(.70,.211,.133,.62),(.87,.181,.128,.43),(1.035,.17,.124,.34),(1.21,.226,.137,.42),(1.29,.211,.117,.47),(1.335,.08,.074,.58)]
 N=16;v=[]
 for inset in [0,.009]:
  for z,rx,ry,gap in rows:
   for k in range(N):
    t=-math.pi/2+gap+(math.tau-2*gap)*k/(N-1)
    v.append(((rx-inset)*math.cos(t),(ry-inset)*math.sin(t),z))
 L=len(rows)*N;f=[]
 for shell in [0,1]:
  off=shell*L
  for j in range(len(rows)-1):
   for k in range(N-1):
    q=(off+j*N+k,off+j*N+k+1,off+(j+1)*N+k+1,off+(j+1)*N+k)
    f.append(q if shell==0 else q[::-1])
 for j in range(len(rows)-1):
  for k in [0,N-1]:
   a=j*N+k;b=(j+1)*N+k;f.append((a,b,b+L,a+L))
 for row in [0,len(rows)-1]:
  for k in range(N-1):
   a=row*N+k;f.append((a,a+L,a+L+1,a+1))
 mesh('Open_sea_coat',v,f,'teal')
 rings('Belt',[(0,0,.913,.182,.146),(0,0,.96,.182,.146)],'leather',12)
 plate('Buckle',[(-.029,.918),(.029,.918),(.029,.958),(-.029,.958)],-.151,.012,'brass')
 plate('Buckle_inset',[(-.017,.928),(.017,.928),(.017,.948),(-.017,.948)],-.165,.005,'leather')
 for side in [-1,1]:
  plate('Coat_lapel',[(side*.068,1.331),(side*.132,1.285),(side*.097,1.225),(side*.12,1.198),(side*.053,1.075),(side*.043,1.237)],-.137,.022,'cream')
  for z in [1.03,1.11]:eye_disc('Brass_button',side*.115,-.129,z,.011,.011,'brass',8)
 rings('Neckerchief',[(0,0,1.313,.083,.072),(0,0,1.353,.064,.06)],'coral',12)
 plate('Scarf_tail',[(-.036,1.339),(.008,1.325),(.034,1.208),(-.013,1.229)],-.091,.022,'coral')
 # Continuous neck and face; broad cheeks taper to a rounded small chin.
 rings('Head_neck',[(0,0,1.29,.053,.051),(0,0,1.405,.052,.05),(0,-.006,1.427,.111,.081),(0,-.008,1.468,.144,.111),(0,0,1.54,.169,.128),(0,.008,1.66,.187,.141),(0,.015,1.77,.172,.132),(0,.02,1.82,.12,.10)],'skin',16,True)
 for side in [-1,1]:
  shoulder=Vector((side*.205,0,1.255));elbow=Vector((side*.285,-.014,1.055));wrist=Vector((side*.315,-.072,.866))
  limb('Coat_sleeve',[shoulder,shoulder.lerp(elbow,.35),elbow,wrist],[.080,.084,.065,.053],'teal',8)
  limb('Turned_cuff',[elbow.lerp(wrist,.77),wrist],[.061,.059],'cream',8)
  # A single extruded hand outline includes the thumb, with no intersecting finger blobs.
  shape=[(-.031,.025),(.028,.025),(.041,-.045),(.02,-.098),(-.014,-.09),(-.026,-.035),(-.050,-.071),(-.059,-.035),(-.048,.012)]
  v=[]
  for y in [-.027,.025]:
   for x,z in shape:v.append((wrist.x+side*x,wrist.y+y,wrist.z+z))
  N=len(shape);mesh('Hand',v,[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)],'skin')
 # Large painted-style eyes; discrete flat colour shapes avoid expensive eyeball spheres.
 for side in [-1,1]:
  x=side*.077;z=1.635
  eye_disc('Eye_rim',x,-.133,z,.050,.054,'hairshade')
  eye_disc('Eye_white',x,-.137,z-.003,.045,.047,'white')
  eye_disc('Iris',x+side*.002,-.145,z-.012,.025,.034,'blue')
  eye_disc('Iris_upper',x+side*.002,-.150,z+.006,.023,.025,'blue2')
  eye_disc('Pupil',x+side*.002,-.155,z+.003,.017,.025,'dark',12)
  eye_disc('Eye_glint',x-.010,-.164,z+.023,.008,.010,'white',8)
  plate('Brow',[(x-.052,1.701),(x+.046,1.715),(x+.042,1.734),(x-.045,1.727)],-.125,.015,'hairshade')
 mesh('Small_nose',[(-.016,-.132,1.593),(.018,-.132,1.593),(.020,-.177,1.553),(-.014,-.178,1.549),(0,-.187,1.566)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(0,3,2,1)],'skin')
 plate('Smile',[(-.037,1.505),(.040,1.511),(.023,1.498),(-.013,1.495)],-.127,.007,'lip')
 # Deliberate faceted chestnut hair shell, open around the face.
 N=16;v=[]
 for row in range(4):
  for k in range(N):
   t=k*math.tau/N;front=max(0,-math.sin(t))
   rx=[.055,.153,.206,.207][row];ry=[.05,.13,.167,.162][row]
   z=[1.874,1.85,1.77,1.58+.158*front**5][row]
   v.append((rx*math.cos(t),.023+ry*math.sin(t),z))
 f=[tuple(range(N))]
 for row in range(3):
  for k in range(N):
   a=row*N+k;b=row*N+(k+1)%N;c=(row+1)*N+(k+1)%N;d=(row+1)*N+k
   if row==1:f.extend([(a,b,c),(a,c,d)])
   else:f.append((a,b,c,d))
 mesh('Short_swept_hair',v,f,['hair','hairlight','hair','hairshade','hair'])
 # Swept, tapered fringe chunks with an actual wedge profile.
 plate('Swept_fringe',[(-.176,1.793),(-.035,1.836),(.16,1.793),(.149,1.75),(.07,1.767),(-.119,1.708),(-.084,1.756),(-.17,1.735)],-.163,.064,'hair')
 for side in [-1,1]:
  rings('Ear',[(side*.176,0,1.546,.025,.026),(side*.187,0,1.60,.032,.03),(side*.18,0,1.639,.022,.026)],'skin',8,True)
 # Join one vertex-coloured mesh for export; explicitly triangulate and enforce ceiling.
 bpy.ops.object.select_all(action='DESELECT')
 for ob in parts:ob.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();body=bpy.context.object;body.name='Male_Coat_Deckhand'
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
 mod=body.modifiers.new('Game triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name);body.data.calc_loop_triangles();count=len(body.data.loop_triangles);assert count<=3000,count
 bpy.ops.export_scene.gltf(filepath=str(E/'male-coat-deckhand.glb'),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True)
 floorMat=bpy.data.materials.new('Studio_floor');floorMat.diffuse_color=(.12,.21,.25,1)
 bpy.ops.mesh.primitive_plane_add(size=200);bpy.context.object.data.materials.append(floorMat)
 world=bpy.data.worlds.new('Soft_studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.28,.39,.48,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 for pos,power,size in [((-3,-4,6),550,4),((4,1,4),350,3)]:
  ld=bpy.data.lights.new('Studio_light','AREA');lo=bpy.data.objects.new('Studio_light',ld);scene.collection.objects.link(lo);lo.location=pos;ld.energy=power;ld.size=size;lo.rotation_euler=(Vector((0,0,1))-lo.location).to_track_quat('-Z','Y').to_euler()
 cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO';cd.ortho_scale=2.2;cam.location=(2.1,-7,2.1);cam.rotation_euler=(Vector((0,0,.96))-cam.location).to_track_quat('-Z','Y').to_euler()
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.view_settings.view_transform='AgX';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.resolution_x=1000;scene.render.resolution_y=1100
 scene.render.filepath=str(O/'character.png');bpy.ops.render.render(write_still=True)
 scene.render.resolution_x=200;scene.render.resolution_y=220;scene.render.filepath=str(O/'character-small.png');bpy.ops.render.render(write_still=True)
 (O/'stats.json').write_text(json.dumps({'triangles':count,'faces':len(body.data.polygons),'vertices':len(body.data.vertices),'materials':1,'rigged':False},indent=2))
 bpy.data.libraries.write(str(R/'tools/blender/source/crew-male-coat-v1.blend'),{scene});print('New reference-led character:',count,'triangles. Unity untouched.')
finally:bpy.context.window.scene=prev
