"""Three equal-height proportion studies. Blender only, no Unity integration."""
import bpy, math
from mathutils import Vector
from pathlib import Path
R=Path('/Users/kevinandersson/Desktop/SeaSick');O=R/'docs/art-direction/crew-proportions'
previous=bpy.context.window.scene
scene=bpy.data.scenes.new('Crew_Proportions_V1');bpy.context.window.scene=scene
try:
 def mat(n,c):
  m=bpy.data.materials.new(n);m.diffuse_color=(*c,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Roughness'].default_value=.85;return m
 clay=mat('Neutral_clay',(.40,.55,.55));ground=mat('Backdrop',(.035,.065,.08));ink=mat('Labels',(.9,.89,.78))
 def sphere(n,p,s):
  bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=p);ob=bpy.context.object;ob.name=n;ob.scale=s;ob.data.materials.append(clay)
  for f in ob.data.polygons:f.use_smooth=True
  return ob
 def bone(n,a,b,r0,r1):
  d=Vector(b)-Vector(a);bpy.ops.mesh.primitive_cone_add(vertices=20,radius1=r0,radius2=r1,depth=d.length,location=(Vector(a)+Vector(b))/2);ob=bpy.context.object;ob.name=n;ob.rotation_euler=d.to_track_quat('Z','Y').to_euler();ob.data.materials.append(clay)
  for f in ob.data.polygons:f.use_smooth=True
  sphere(n+'_joint',a,(r0,r0,r0));sphere(n+'_end',b,(r1,r1,r1))
 def label(text,x,z,size):
  c=bpy.data.curves.new('Label','FONT');c.body=text;c.align_x='CENTER';c.size=size;c.extrude=0;o=bpy.data.objects.new('Label_'+text,c);scene.collection.objects.link(o);o.location=(x,-1.15,.02) if z<0 else (x,-.55,z);o.rotation_euler=(math.pi/2,0,0);c.materials.append(ink)
 for index,(ratio,x) in enumerate([(4.5,-1.55),(4.,0),(3.5,1.55)]):
  before=set(scene.objects);H=1.9;hh=H/ratio;hip=.72-index*.075;shoulder=H-hh-.105;sw=.305+index*.0175
  sphere('Head',(x,0,H-hh*.5),(hh*.43,hh*.36,hh*.5))
  bone('Neck',(x,0,shoulder-.025),(x,0,H-hh+.04),.092,.09)
  sphere('Chest',(x,0,(shoulder+hip)/2+.09),(sw,.17,(shoulder-hip)/2+.08))
  sphere('Pelvis',(x,.015,hip),(.23,.165,.15))
  for side in [-1,1]:
   fx=x+side*.17;knee=(x+side*.15,-.012,hip*.54);ankle=(fx,0,.16)
   bone('Thigh',(x+side*.12,.015,hip),knee,.135,.115)
   bone('Calf',knee,ankle,.108,.092)
   sphere('Foot',(fx,-.075,.10),(.115,.205,.10))
   a=(x+side*sw,0,shoulder-.045);e=(x+side*(sw+.115),-.015,shoulder-.26);w=(x+side*(sw+.13),-.09,hip+.10)
   bone('Upper_arm',a,e,.112,.095);bone('Forearm',e,w,.095,.077)
   handh=hh*.64
   sphere('Mitten_hand',(w[0],w[1]-.012,w[2]-handh*.34),(handh*.35,handh*.26,handh*.5))
   sphere('Thumb',(w[0]-side*handh*.28,w[1]-.055,w[2]-handh*.10),(handh*.15,handh*.18,handh*.24))
  root=bpy.data.objects.new('Blockout_'+str(ratio)+'_heads',None);scene.collection.objects.link(root)
  for ob in set(scene.objects)-before:
   if ob!=root:ob.parent=root
  label(['A  |  4.5 heads','B  |  4 heads','C  |  3.5 heads'][index],x,-.26,.135)
 label('SAME HEIGHT  /  SAME POSE  /  NO DETAIL',0,2.22,.115)
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015));bpy.context.object.data.materials.append(ground)
 world=bpy.data.worlds.new('Proportion_studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.20,.27,.31,1);world.node_tree.nodes['Background'].inputs[1].default_value=.6;scene.world=world
 for pos,energy,size in [((-3,-4,6),650,5),((4,1,4),450,4)]:
  ld=bpy.data.lights.new('Softbox','AREA');lo=bpy.data.objects.new('Softbox',ld);scene.collection.objects.link(lo);lo.location=pos;ld.energy=energy;ld.size=size;lo.rotation_euler=(Vector((0,0,1))-lo.location).to_track_quat('-Z','Y').to_euler()
 cd=bpy.data.cameras.new('Comparison_camera');camera=bpy.data.objects.new('Comparison_camera',cd);scene.collection.objects.link(camera);scene.camera=camera;cd.type='ORTHO';cd.ortho_scale=5.55;camera.location=(0,-12,3.0);camera.rotation_euler=(Vector((0,0,.95))-camera.location).to_track_quat('-Z','Y').to_euler()
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX';scene.render.resolution_percentage=100
 scene.render.resolution_x=1400;scene.render.resolution_y=800;scene.render.filepath=str(O/'proportions-large.png');bpy.ops.render.render(write_still=True)
 scene.render.resolution_x=320;scene.render.resolution_y=183;scene.render.filepath=str(O/'proportions-small.png');bpy.ops.render.render(write_still=True)
 bpy.data.libraries.write(str(R/'tools/blender/source/crew-proportions-v1.blend'),{scene})
 print('Three equal 1.9m blockouts rendered at large and small sizes; original scene restored.')
finally:bpy.context.window.scene=previous
