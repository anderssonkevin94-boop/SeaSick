"""Reference-led low-poly character. Independent geometry, Blender only."""
import bpy,math,json
from mathutils import Vector
from pathlib import Path
R=Path('/Users/kevinandersson/Desktop/SeaSick');O=R/'docs/art-direction/crew-concept/reference-v1';E=R/'tools/blender/exports/crew-reference-v1';prev=bpy.context.window.scene
scene=bpy.data.scenes.new('Crew_Reference_Style_V1');bpy.context.window.scene=scene
try:
 C={'skin':(.78,.46,.25,1),'cream':(.92,.86,.69,1),'coral':(.8,.095,.025,1),'coral2':(.56,.075,.024,1),'leather':(.17,.077,.035,1),'dark':(.025,.028,.035,1),'hair':(.27,.094,.025,1),'hairlight':(.40,.165,.045,1),'hairshade':(.16,.042,.015,1),'white':(.97,.97,.89,1),'blue':(.025,.45,.64,1),'blue2':(.012,.15,.24,1),'lip':(.40,.15,.071,1)}
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
 # Slim figure with readable angular dress and large head, based on the supplied style.
 for side in [-1,1]:
  x=side*.105
  rings('Boot_sole',[(x,-.064,.018,.079,.145),(x,-.064,.048,.079,.145)],'dark',8)
  rings('Boot',[(x,-.060,.049,.078,.142),(x,-.049,.10,.071,.13),(x,0,.16,.060,.071),(x,0,.255,.057,.064)],'leather',8)
  rings('Boot_coral_cuff',[(x,0,.20,.063,.068),(x,0,.275,.065,.07)],'coral',8)
  limb('Leg',[(x,0,.248),(x+side*.008,.015,.43),(side*.115,-.018,.54),(side*.09,0,.77)],[.048,.057,.060,.084],'skin',8)
 # Torso silhouette has gentle waist shaping, clean neckline and attached sleeve caps.
 rings('Bodice',[(0,0,.935,.14,.105),(0,0,1.05,.108,.082),(0,-.004,1.17,.155,.111),(0,0,1.28,.19,.10),(0,0,1.33,.082,.066)],'cream',12)
 rings('Waist_sash',[(0,0,.932,.167,.123),(0,0,.998,.124,.098)],'leather',12)
 rings('Skirt',[(0,0,.715,.238,.16),(0,0,.74,.259,.175),(0,0,.83,.217,.154),(0,0,.946,.15,.112)],['coral','coral','coral2','coral'],12)
 # Continuous neck and face; broad cheeks taper to a rounded small chin.
 rings('Head_neck',[(0,0,1.29,.053,.051),(0,0,1.405,.052,.05),(0,-.006,1.437,.103,.081),(0,-.008,1.468,.138,.111),(0,0,1.54,.169,.128),(0,.008,1.66,.187,.141),(0,.015,1.77,.172,.132),(0,.02,1.82,.12,.10)],'skin',16,True)
 rings('Collar',[(0,0,1.321,.086,.071),(0,0,1.342,.066,.057)],'coral2',12)
 for side in [-1,1]:
  shoulder=Vector((side*.177,0,1.26));sleeveend=Vector((side*.224,-.002,1.16));elbow=Vector((side*.274,-.016,.993));wrist=Vector((side*.293,-.059,.81))
  limb('Puff_sleeve',[shoulder,shoulder.lerp(sleeveend,.45),sleeveend],[.073,.082,.061],'coral',10)
  limb('Arm',[sleeveend.lerp(elbow,-.03),elbow,wrist],[.047,.039,.032],'skin',8)
  # A single extruded hand outline includes the thumb, with no intersecting finger blobs.
  shape=[(-.031,.025),(.028,.025),(.041,-.045),(.02,-.098),(-.014,-.09),(-.026,-.035),(-.050,-.071),(-.059,-.035),(-.048,.012)]
  v=[]
  for y in [-.027,.025]:
   for x,z in shape:v.append((wrist.x+side*x,wrist.y+y,wrist.z+z))
  N=len(shape);mesh('Hand',v,[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)],'skin')
 # Large painted-style eyes; discrete flat colour shapes avoid expensive eyeball spheres.
 for side in [-1,1]:
  x=side*.078;z=1.645
  eye_disc('Eye_rim',x,-.133,z,.054,.069,'hairshade')
  eye_disc('Eye_white',x,-.137,z-.003,.050,.063,'white')
  eye_disc('Iris',x+side*.002,-.145,z-.012,.030,.043,'blue')
  eye_disc('Iris_upper',x+side*.002,-.150,z+.006,.027,.032,'blue2')
  eye_disc('Pupil',x+side*.002,-.155,z+.003,.019,.031,'dark',12)
  eye_disc('Eye_glint',x-.010,-.164,z+.023,.010,.014,'white',8)
  plate('Brow',[(x-.052,1.731),(x+.046,1.739),(x+.042,1.756),(x-.045,1.754)],-.125,.015,'hairshade')
 mesh('Small_nose',[(-.016,-.132,1.593),(.018,-.132,1.593),(.020,-.177,1.553),(-.014,-.178,1.549),(0,-.187,1.566)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(0,3,2,1)],'skin')
 plate('Smile',[(-.037,1.505),(.040,1.511),(.023,1.498),(-.013,1.495)],-.127,.007,'lip')
 # Deliberate faceted chestnut hair shell, open around the face.
 N=16;v=[]
 for row in range(4):
  for k in range(N):
   t=k*math.tau/N;front=max(0,-math.sin(t))
   rx=[.055,.153,.206,.207][row];ry=[.05,.13,.167,.162][row]
   z=[1.884,1.856,1.77,1.48+.292*front**5][row]
   v.append((rx*math.cos(t),.023+ry*math.sin(t),z))
 f=[tuple(range(N))]
 for row in range(3):
  for k in range(N):
   a=row*N+k;b=row*N+(k+1)%N;c=(row+1)*N+(k+1)%N;d=(row+1)*N+k
   if row==1:f.extend([(a,b,c),(a,c,d)])
   else:f.append((a,b,c,d))
 mesh('Faceted_bob',v,f,['hair','hairlight','hair','hairshade','hair'])
 # Swept, tapered fringe chunks with an actual wedge profile.
 for n,pts in enumerate([[(-.17,1.795),(-.045,1.815),(-.005,1.743),(-.13,1.705)],[(-.043,1.815),(.087,1.809),(.113,1.771),(.025,1.733)],[(.10,1.801),(.182,1.766),(.184,1.628),(.138,1.689)]]):plate('Fringe',pts,-.159,.055,['hair','hairlight','hairshade'][n])
 for side in [-1,1]:plate('Hair_side_lock',[(side*.156,1.71),(side*.205,1.702),(side*.208,1.458),(side*.169,1.456)],-.015,.11,'hairshade')
 # Join one vertex-coloured mesh for export; explicitly triangulate and enforce ceiling.
 bpy.ops.object.select_all(action='DESELECT')
 for ob in parts:ob.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();body=bpy.context.object;body.name='Reference_Style_Deckhand'
 mod=body.modifiers.new('Game triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name);body.data.calc_loop_triangles();count=len(body.data.loop_triangles);assert count<=3000,count
 bpy.ops.export_scene.gltf(filepath=str(E/'reference-style-deckhand.glb'),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True)
 floorMat=bpy.data.materials.new('Studio_floor');floorMat.diffuse_color=(.12,.21,.25,1)
 bpy.ops.mesh.primitive_plane_add(size=200);bpy.context.object.data.materials.append(floorMat)
 world=bpy.data.worlds.new('Soft_studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.28,.39,.48,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 for pos,power,size in [((-3,-4,6),550,4),((4,1,4),350,3)]:
  ld=bpy.data.lights.new('Studio_light','AREA');lo=bpy.data.objects.new('Studio_light',ld);scene.collection.objects.link(lo);lo.location=pos;ld.energy=power;ld.size=size;lo.rotation_euler=(Vector((0,0,1))-lo.location).to_track_quat('-Z','Y').to_euler()
 cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO';cd.ortho_scale=2.2;cam.location=(2.1,-7,2.35);cam.rotation_euler=(Vector((0,0,.96))-cam.location).to_track_quat('-Z','Y').to_euler()
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.view_settings.view_transform='AgX';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.resolution_x=1000;scene.render.resolution_y=1100
 scene.render.filepath=str(O/'character.png');bpy.ops.render.render(write_still=True)
 scene.render.resolution_x=200;scene.render.resolution_y=220;scene.render.filepath=str(O/'character-small.png');bpy.ops.render.render(write_still=True)
 (O/'stats.json').write_text(json.dumps({'triangles':count,'faces':len(body.data.polygons),'vertices':len(body.data.vertices),'materials':1,'rigged':False},indent=2))
 bpy.data.libraries.write(str(R/'tools/blender/source/crew-reference-style-v1.blend'),{scene});print('New reference-led character:',count,'triangles. Unity untouched.')
finally:bpy.context.window.scene=prev
