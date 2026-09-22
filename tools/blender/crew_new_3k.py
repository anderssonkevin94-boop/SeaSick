"""New hand-built low-poly deckhand. No previous character geometry is used."""
import bpy,math,json
from pathlib import Path
from mathutils import Vector
R=Path('/Users/kevinandersson/Desktop/SeaSick');O=R/'docs/art-direction/crew-concept/new-3k';E=R/'tools/blender/exports/crew-new-3k';previous=bpy.context.window.scene
scene=bpy.data.scenes.new('New_Deckhand_3K');bpy.context.window.scene=scene
try:
 colors={'skin':(.52,.255,.13,1),'ochre':(.70,.34,.055,1),'navy':(.027,.085,.12,1),'cream':(.84,.77,.57,1),'boot':(.10,.047,.024,1),'hair':(.105,.032,.016,1),'eye':(.018,.021,.023,1),'lip':(.24,.067,.036,1),'brass':(.59,.34,.075,1)}
 material=bpy.data.materials.new('Crew_vertex_palette');material.use_nodes=True;p=material.node_tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.8;vc=material.node_tree.nodes.new('ShaderNodeVertexColor');vc.layer_name='Color';material.node_tree.links.new(vc.outputs['Color'],p.inputs['Base Color'])
 parts=[]
 def mesh(n,v,f,col,smooth=False):
  me=bpy.data.meshes.new(n);me.from_pydata(v,[],f);me.update();ob=bpy.data.objects.new(n,me);scene.collection.objects.link(ob);me.materials.append(material);a=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
  for c in a.data:c.color=colors[col]
  for poly in me.polygons:poly.use_smooth=smooth
  parts.append(ob);return ob
 def rings(n,rs,col,N=12,smooth=True):
  v=[]
  for x,y,z,rx,ry in rs:
   for k in range(N):
    t=k*math.tau/N;v.append((x+rx*math.cos(t),y+ry*math.sin(t),z))
  f=[tuple(range(N-1,-1,-1))]
  for j in range(len(rs)-1):
   for k in range(N):f.append((j*N+k,j*N+(k+1)%N,(j+1)*N+(k+1)%N,(j+1)*N+k))
  f.append(tuple((len(rs)-1)*N+k for k in range(N)));return mesh(n,v,f,col,smooth)
 def limb(n,points,radii,col,N=8):
  v=[]
  for i,p in enumerate(points):
   d=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)]);q=d.to_track_quat('Z','Y')
   for k in range(N):v.append(Vector(p)+q@Vector((radii[i]*math.cos(k*math.tau/N),radii[i]*math.sin(k*math.tau/N),0)))
  f=[tuple(range(N-1,-1,-1))]
  for j in range(len(points)-1):
   for k in range(N):f.append((j*N+k,j*N+(k+1)%N,(j+1)*N+(k+1)%N,(j+1)*N+k))
  f.append(tuple((len(points)-1)*N+k for k in range(N)));return mesh(n,v,f,col,True)
 def plate(n,outline,y,thick,col):
  v=[(x,y,z) for x,z in outline]+[(x,y+thick,z) for x,z in outline];N=len(outline);f=[tuple(range(N-1,-1,-1)),tuple(range(N,N*2))]
  for i in range(N):f.append((i,(i+1)%N,(i+1)%N+N,i+N))
  return mesh(n,v,f,col)
 # Short sea trousers and low, broad boots. Deliberate rings, no remeshing or decimation.
 for side in [-1,1]:
  x=side*.145
  rings('Boot',[(x,-.055,.025,.113,.17),(x,-.065,.07,.124,.19),(x,-.035,.15,.115,.16),(x,0,.215,.118,.12)],'boot',8)
  rings('Trouser_leg',[(x,0,.18,.087,.092),(x,0,.30,.115,.12),(side*.13,0,.46,.132,.135),(side*.10,0,.57,.12,.13)],'navy',10)
 # One tailored torso ending at the collar. Broad shoulders and tucked waist.
 rings('Smock',[(0,0,.55,.245,.16),(0,0,.62,.245,.16),(0,.01,.86,.275,.175),(0,.01,1.015,.31,.17),(0,0,1.085,.17,.115)],'ochre',16)

 # A single continuous neck-to-head mesh: no collar of disconnected pieces under the jaw.
 rings('Head_and_neck',[(0,0,1.045,.085,.082),(0,0,1.145,.09,.087),(0,-.009,1.19,.145,.12),(0,-.002,1.26,.185,.14),(0,0,1.38,.215,.16),(0,0,1.53,.213,.16),(0,.015,1.63,.18,.14),(0,.02,1.67,.09,.075)],'skin',16)
 # Flat sailor collar and short cream tie: a new colour/silhouette direction.
 rings('Cream_collar',[(0,0,1.065,.226,.16),(0,0,1.087,.204,.144),(0,0,1.13,.12,.103)],'cream',16)
 plate('Necktie',[(-.035,1.045),(.035,1.045),(.044,.87),(0,.825),(-.028,.88)],-.184,.018,'navy')
 # Shoulder sleeves and cuffs share boundaries. Exposed forearms tuck into the sleeve opening.
 for side in [-1,1]:
  a=Vector((side*.265,0,1.00));b=Vector((side*.35,-.01,.90));c=Vector((side*.395,-.025,.80));w=Vector((side*.405,-.09,.66))
  sleeve=limb('Sleeve_and_cuff',[a,b,b.lerp(c,.75),c.lerp(w,.10)],[.124,.127,.115,.112],'ochre',10)
  for face in sleeve.data.polygons[21:]:
   for li in face.loop_indices:sleeve.data.color_attributes['Color'].data[li].color=colors['cream']
  limb('Forearm',[c.lerp(w,-.06),c.lerp(w,.65),w],[.083,.075,.066],'skin',10)
  # Integrated thumb in a bevelled mitten outline instead of overlapping spheres.
  outline=[(-.058,.08),(.06,.08),(.087,.035),(.093,-.085),(.044,-.145),(-.037,-.145),(-.078,-.105),(-.085,-.025),(-.126,.005),(-.12,.057),(-.087,.067)]
  v=[]
  for y,scale in [(-.065,.79),(-.045,1),(.045,1),(.063,.79)]:
   for x,z in outline:v.append((w.x+side*x*1.15,w.y+y,w.z+z))
  N=len(outline);f=[tuple(range(N-1,-1,-1))]
  for j in range(3):
   for k in range(N):f.append((j*N+k,j*N+(k+1)%N,(j+1)*N+(k+1)%N,(j+1)*N+k))
  f.append(tuple(3*N+k for k in range(N)));mesh('Mitten',v,f,'skin',True)
 # Simple ears, face planes and a wedge nose. Features are large enough to survive small renders.
 for side in [-1,1]:
  rings('Ear',[(side*.211,0,1.34,.035,.032),(side*.231,-.005,1.385,.042,.035),(side*.21,0,1.435,.03,.025)],'skin',8)
  x=side*.081
  plate('Eye',[(x-.038,1.458),(x-.027,1.48),(x+.025,1.48),(x+.037,1.457),(x+.023,1.438),(x-.024,1.438)],-.158,.013,'eye')
  plate('Brow',[(x-.052,1.511),(x+.045,1.528+(side*.01)),(x+.05,1.55+(side*.01)),(x-.045,1.533)],-.157,.02,'hair')
 mesh('Nose',[(-.036,-.156,1.454),(.036,-.156,1.454),(.052,-.217,1.378),(-.043,-.223,1.379),(0,-.235,1.399)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(0,3,2,1)],'skin',True)
 plate('Smile',[(-.055,1.326),(.064,1.34),(.045,1.315),(-.026,1.306)],-.163,.009,'lip')
 # Short chestnut bob behind the face; swept cap gives an instantly distinct silhouette.
 rings('Hair_back',[(0,.067,1.28,.145,.087),(0,.061,1.36,.212,.12),(0,.04,1.53,.223,.15),(0,.025,1.65,.16,.13)],'hair',12)
 # Cover the front of the hair volume only above the forehead, with an asymmetric cap.
 rings('Watch_cap',[(0,.018,1.565,.224,.165),(0,.02,1.615,.228,.17),(-.035,.03,1.705,.20,.15),(-.07,.033,1.75,.11,.095),(-.08,.033,1.765,.025,.025)],'navy',16)
 rings('Cap_fold',[(0,.018,1.564,.23,.17),(0,.018,1.615,.233,.173)],'navy',16)
 plate('Forelock',[(-.13,1.60),(-.025,1.598),(-.075,1.51),(-.145,1.49)],-.174,.026,'hair')
 # Single lightweight mesh with vertex palette colours.
 bpy.ops.object.select_all(action='DESELECT')
 for ob in parts:ob.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();body=bpy.context.object;body.name='Deckhand_New_3K'
 mod=body.modifiers.new('Game triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name);body.data.calc_loop_triangles();count=len(body.data.loop_triangles)
 assert count<=3000,('Hard limit exceeded',count)
 bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 bpy.ops.export_scene.gltf(filepath=str(E/'deckhand-new-3k.glb'),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True)
 # Neutral studio and separate neck closeup for join review.
 bg=bpy.data.materials.new('Studio');bg.diffuse_color=(.055,.11,.13,1)
 bpy.ops.mesh.primitive_plane_add(size=200);floor=bpy.context.object;floor.location.z=-.003;floor.data.materials.append(bg)
 world=bpy.data.worlds.new('Studio_world');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.30,.35,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 for pos,power,size in [((-3,-4,6),500,4),((4,1,4),350,3)]:
  d=bpy.data.lights.new('Softbox','AREA');o=bpy.data.objects.new('Softbox',d);scene.collection.objects.link(o);o.location=pos;d.energy=power;d.size=size;o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
 cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO';cd.ortho_scale=2.22;cam.location=(2.7,-7,2.6);cam.rotation_euler=(Vector((0,0,.88))-cam.location).to_track_quat('-Z','Y').to_euler()
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.view_settings.view_transform='AgX';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.resolution_x=1000;scene.render.resolution_y=1100
 scene.render.filepath=str(O/'new-deckhand.png');bpy.ops.render.render(write_still=True)
 scene.render.resolution_x=200;scene.render.resolution_y=220;scene.render.filepath=str(O/'new-deckhand-small.png');bpy.ops.render.render(write_still=True)
 stats={'triangles':count,'faces':len(body.data.polygons),'vertices':len(body.data.vertices),'materials':1,'textures':0,'rigged':False,'construction':'New manually specified topology; continuous head/neck mesh; no prior character geometry, remeshing or decimation.'};(O/'stats.json').write_text(json.dumps(stats,indent=2))
 bpy.data.libraries.write(str(R/'tools/blender/source/crew-new-3k.blend'),{scene});print(json.dumps(stats))
finally:bpy.context.window.scene=previous
