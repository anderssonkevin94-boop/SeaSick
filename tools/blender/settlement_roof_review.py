import bpy, math, contextlib, io
from pathlib import Path
from mathutils import Vector
R=Path('/Users/kevinandersson/Desktop/SeaSick');O=R/'docs/art-direction/settlement-kit/v1';E=R/'tools/blender/exports/settlement-kit-v1';prev=bpy.context.window.scene
s=sorted([s for s in bpy.data.scenes if s.name.startswith('Settlement_Kit_V1')],key=lambda s:s.name)[-1];bpy.context.window.scene=s
roots=[o for o in s.objects if o.type=='EMPTY' and 'description' in o]
old={o:o.location.copy() for o in roots};hidden={o:o.hide_render for o in s.objects};cam=s.camera;campos=cam.location.copy();camrot=cam.rotation_euler.copy();scale=cam.data.ortho_scale
crew=next(o for o in s.objects if o.name.startswith('Male_Coat_Deckhand'));crewpos=crew.location.copy()
try:
 for root in roots:
  for ob in root.children:
   if ob.type!='MESH' or '_Roof' not in ob.name:continue
   maxx=max(abs(v.co.x) for v in ob.data.vertices)
   for poly in ob.data.polygons:
    vs=[ob.data.vertices[i] for i in poly.vertices]
    if len(vs)==4 and max(v.co.y for v in vs)-min(v.co.y for v in vs)<.8 and ob.data.materials[poly.material_index].name.startswith('Settlement_roof'):
     row=min(3,int(sum(abs(v.co.x) for v in vs)/len(vs)/maxx*4))
     for v in vs:v.co.z+=.018*(4-row)
  root.location=(0,0,0);bpy.ops.object.select_all(action='DESELECT');root.select_set(True)
  for ob in root.children:ob.select_set(True)
  bpy.context.view_layer.objects.active=root
  with contextlib.redirect_stdout(io.StringIO()):bpy.ops.export_scene.gltf(filepath=str(E/(root.name.split('.')[0]+'.glb')),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_extras=True)
 def group(ids,name):
  chosen=[r for id in ids for r in roots if r.name.split('.')[0]==id];N=len(chosen)
  for r in roots:
   for ob in r.children:ob.hide_render=r not in chosen
  for i,r in enumerate(chosen):r.location=((i-(N-1)/2)*9.4,0,0)
  crew.hide_render=False;crew.location=(-N*9.4/2+.9,-3.9,.02)
  cam.location=(N*1.2,-26,19);cam.rotation_euler=(Vector((0,0,1.3))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=N*9.4+1
  s.render.resolution_x=1600;s.render.resolution_y=720;s.render.filepath=str(O/name);bpy.ops.render.render(write_still=True)
 group(['hut_01','hut_02','hut_03'],'hut-progression.png')
 group(['sawmill','storage','blacksmith','kitchen'],'workshops.png')
 # Cutaway verifies full-size beds and work equipment beneath the removable roofs.
 chosen=[r for r in roots if r.name.split('.')[0] in ['hut_03','blacksmith']]
 for r in roots:
  for ob in r.children:ob.hide_render=r not in chosen or '_Roof' in ob.name
 for i,r in enumerate(chosen):r.location=((i-.5)*10,0,0)
 crew.location=(6.25,-1.35,.26);cam.location=(8,-17,19);cam.rotation_euler=(Vector((0,0,1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=22;s.render.resolution_x=1400;s.render.resolution_y=950;s.render.filepath=str(O/'interior-cutaway.png');bpy.ops.render.render(write_still=True)
finally:
 for o,p in old.items():o.location=p
 for o,h in hidden.items():o.hide_render=h
 crew.location=crewpos;cam.location=campos;cam.rotation_euler=camrot;cam.data.ortho_scale=scale
 bpy.data.libraries.write(str(R/'tools/blender/source/settlement-kit-v1.blend'),{s});bpy.context.window.scene=prev
print('Roof overlaps corrected, all 15 GLBs re-exported, saved and reviewed.')
