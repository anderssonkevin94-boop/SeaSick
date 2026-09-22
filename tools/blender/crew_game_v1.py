"""Standalone low-poly game mesh candidate. No Unity writes or calls."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
R=Path('/Users/kevinandersson/Desktop/SeaSick');OUT=R/'docs/art-direction/crew-concept/game-v1';EXPORT=R/'tools/blender/exports/crew-game-v1'
previous=bpy.context.window.scene
try:
 with bpy.data.libraries.load(str(R/'tools/blender/source/crew-deckhand-c.blend'),link=False) as (src,dst):dst.scenes=[n for n in src.scenes if n.startswith('Crew_Study_C')][:1]
 scene=dst.scenes[0];scene.name='Crew_GameMesh_V1';bpy.context.window.scene=scene;bpy.context.view_layer.update()
 root=next(o for o in scene.objects if o.name.startswith('Crew_Deckhand_C'));parts=[o for o in root.children if o.type=='MESH']
 def triangles(ob):ob.data.calc_loop_triangles();return len(ob.data.loop_triangles)
 before=sum(triangles(o) for o in parts)
 for ob in parts:
  bpy.context.view_layer.objects.active=ob
  for mod in list(ob.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
  count=triangles(ob)
  if count<40:continue
  # Concentrate budget at silhouette/features; remove dense sculpt samples in the arms.
  if ob.name.startswith('Sculpted_hand'):ratio=min(1,600/count)
  elif ob.name.startswith(('Head','Ear','Eye','Nose','Smile')):ratio=.8
  else:ratio=.42
  mod=ob.modifiers.new('Game mesh reduction','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True
  bpy.ops.object.modifier_apply(modifier=mod.name)
 # One small palette texture and one renderer material, avoiding dozens of draw submissions.
 palette=[]
 for ob in parts:
  for m in ob.data.materials:
   if m and m not in palette:palette.append(m)
 size=64;cols=4;rows=4;pix=[0.]*(size*size*4)
 for y in range(size):
  for x in range(size):
   idx=(y//16)*4+x//16
   c=palette[idx].diffuse_color if idx<len(palette) else (0,0,0,1)
   # Store colour samples for a linear-tagged source image.
   off=(y*size+x)*4;pix[off:off+4]=list(c)
 tex=bpy.data.images.new('Crew_palette_64',width=size,height=size,alpha=True);tex.colorspace_settings.name='Non-Color';tex.pixels=pix;tex.filepath_raw=str(EXPORT/'crew-palette.png');tex.file_format='PNG';tex.save();tex.pack()
 mat=bpy.data.materials.new('Crew_palette_single_material');mat.use_nodes=True;p=mat.node_tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.78
 im=mat.node_tree.nodes.new('ShaderNodeTexImage');im.image=tex;im.interpolation='Closest';uvnode=mat.node_tree.nodes.new('ShaderNodeUVMap');uvnode.uv_map='PaletteUV';mat.node_tree.links.new(uvnode.outputs['UV'],im.inputs['Vector']);mat.node_tree.links.new(im.outputs['Color'],p.inputs['Base Color'])
 for ob in parts:
  uv=ob.data.uv_layers.new(name='PaletteUV');ob.data.uv_layers.active=uv;uv.active_render=True
  oldm=list(ob.data.materials)
  for face in ob.data.polygons:
   idx=palette.index(oldm[face.material_index]);co=((idx%4+.5)/4,(idx//4+.5)/4)
   for li in face.loop_indices:uv.data[li].uv=co
   face.material_index=0
  ob.data.materials.clear();ob.data.materials.append(mat)
 bpy.ops.object.select_all(action='DESELECT')
 for ob in parts:ob.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();body=bpy.context.object;body.name='Deckhand_C_LOD0'
 mw=body.matrix_world.copy();body.parent=None;body.matrix_world=mw
 bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
 # Ground-centred origin, metres, Z-up Blender source (GLB conversion handles Y-up).
 bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 tri=body.modifiers.new('Explicit game triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
 count=triangles(body)
 assert count<=5000,('Exceeded triangle target',count)
 assert all(math.isfinite(v) for vert in body.data.vertices for v in vert.co)
 # Distant mesh is separate for eventual engine LOD setup, not a second rendered body.
 lod=body.copy();lod.data=body.data.copy();scene.collection.objects.link(lod);lod.name='Deckhand_C_LOD1';bpy.context.view_layer.objects.active=lod
 mod=lod.modifiers.new('Distance reduction','DECIMATE');mod.ratio=.5;mod.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=mod.name)
 lod.hide_render=True;lod.hide_set(True)
 def export(ob,name):
  bpy.ops.object.select_all(action='DESELECT');ob.hide_set(False);ob.select_set(True);bpy.context.view_layer.objects.active=ob
  bpy.ops.export_scene.gltf(filepath=str(EXPORT/name),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True)
 export(body,'deckhand-c-lod0.glb');export(lod,'deckhand-c-lod1.glb');lod.hide_set(True)
 scene.cycles.samples=24;scene.render.filepath=str(OUT/'deckhand-game-close.png');bpy.ops.render.render(write_still=True)
 scene.render.resolution_x=220;scene.render.resolution_y=220;scene.render.filepath=str(OUT/'deckhand-game-small.png');bpy.ops.render.render(write_still=True)
 stats={'source_triangles':before,'lod0_triangles':count,'lod1_triangles':triangles(lod),'lod0_vertices':len(body.data.vertices),'materials':1,'texture_size':'64x64','rigged':False,'notes':'Static game mesh candidate. Skinning/topology validation for animation remains. Unity untouched.'}
 (OUT/'mesh-stats.json').write_text(json.dumps(stats,indent=2))
 bpy.data.libraries.write(str(R/'tools/blender/source/crew-deckhand-game-v1.blend'),{scene})
 print(json.dumps(stats))
finally:bpy.context.window.scene=previous
