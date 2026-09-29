import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-staging/stern-pbr';OUT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.bake.margin=4;scene.render.bake.use_clear=True
scene.world=bpy.data.worlds.new('Neutral');scene.world.use_nodes=True
k=json.loads((ROOT/'art-staging/f-coaster-runtime/kit.json').read_text());parts=next(m for m in k['models'] if m['name']=='SternRaised')['parts']
objects=[]
for part in parts:
 if any(s in part['name'] for s in ['_Glass','Hinged_Port_Cover','Helm_Spoke'] ) or part['name']=='Helm':continue
 vs=part['vertices'];ns=part['normals'];cs=part['colors'];inds=part['triangles'];verts=[];normals=[];lookup={};remap=[]
 for i in range(len(vs)//3):
  # Unity -> Blender, actual imported mesh authoring coordinates.
  p=(vs[3*i+2]*2,-vs[3*i]*2,vs[3*i+1]*2);n=(ns[3*i+2],-ns[3*i],ns[3*i+1]);key=tuple(round(x,6) for x in p)
  if key not in lookup:lookup[key]=len(verts);verts.append(p);normals.append(n)
  remap.append(lookup[key])
 valid=[t for t in range(0,len(inds),3) if len({remap[inds[t+j]] for j in range(3)})==3]
 faces=[tuple(remap[inds[t+j]] for j in [0,2,1]) for t in valid]
 me=bpy.data.meshes.new(part['name']);me.from_pydata(verts,[],faces);me.update();me.normals_split_custom_set([(ns[3*inds[t+j]+2],-ns[3*inds[t+j]],ns[3*inds[t+j]+1]) for t in valid for j in [0,2,1]])
 ca=me.color_attributes.new(name='Paint',type='FLOAT_COLOR',domain='CORNER')
 for t in range(len(faces)):
  for j,oj in enumerate([0,2,1]):ca.data[t*3+j].color=cs[4*inds[valid[t]+oj]:4*inds[valid[t]+oj]+4]
 ob=bpy.data.objects.new(part['name'],me);scene.collection.objects.link(ob);objects.append(ob)
# Join for a single tightly packed UV atlas and local fixed-part occlusion.
bpy.ops.object.select_all(action='DESELECT')
for ob in objects:ob.select_set(True)
bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();ob=bpy.context.object
# Vertex attribute tracks the named source part, used by the Unity mesh replacement.
# Instead, export one atlas mesh and map by geometric signatures below in Unity.
print('UNWRAPPING',len(ob.data.vertices),flush=True);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(65),island_margin=.0015);bpy.ops.object.mode_set(mode='OBJECT')
print('UV_DONE',flush=True)
mat=bpy.data.materials.new('Baked stylized materials');mat.use_nodes=True;ob.data.materials.clear();ob.data.materials.append(mat)
for p in ob.data.polygons:p.material_index=0
nodes=mat.node_tree.nodes;nodes.clear();links=mat.node_tree.links
out=nodes.new('ShaderNodeOutputMaterial');emit=nodes.new('ShaderNodeEmission');links.new(emit.outputs[0],out.inputs['Surface']);color=nodes.new('ShaderNodeVertexColor');color.layer_name='Paint'
tex=nodes.new('ShaderNodeTexImage');nodes.active=tex
images={}
def bake(name,source,typ='EMIT',color_space='Non-Color'):
 im=bpy.data.images.new(name,width=2048,height=2048,alpha=True);im.colorspace_settings.name=color_space;tex.image=im;nodes.active=tex
 if source:links.new(source,emit.inputs['Color'])
 bpy.ops.object.bake(type=typ);images[name]=im;print('BAKED',name,flush=True)
bake('BaseColor',color.outputs['Color'],color_space='sRGB')
ao=nodes.new('ShaderNodeAmbientOcclusion');ao.inputs['Distance'].default_value=.5;ao.only_local=True;ao.samples=16
bake('AO',ao.outputs['Color'])
# Bake slight bevel shading; the silhouette remains the authored geometry.
bsdf=nodes.new('ShaderNodeBsdfPrincipled');bevel=nodes.new('ShaderNodeBevel');bevel.inputs['Radius'].default_value=.025;bevel.samples=4;links.new(bevel.outputs['Normal'],bsdf.inputs['Normal']);links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
bake('Normal',None,'NORMAL')
# Bake material masks from the existing painted face palette.
links.new(emit.outputs[0],out.inputs['Surface'])
ca=ob.data.color_attributes['Paint'];mask=ob.data.color_attributes.new(name='Surface',type='FLOAT_COLOR',domain='CORNER')
for i,d in enumerate(ca.data):
 r,g,b,a=d.color;hi=max(r,g,b);lo=min(r,g,b)
 iron=hi<.19 and hi-lo<.045
 smooth=.40 if iron else (.28 if b>r else .22)
 mask.data[i].color=(.85 if iron else 0,1,smooth,1)
color.layer_name='Surface';bake('Surface',color.outputs['Color'])
import numpy as np
pixels=np.empty(2048*2048*4,dtype=np.float32);images['Surface'].pixels.foreach_get(pixels);pixels=pixels.reshape(-1,4)
aop=np.empty(pixels.size,dtype=np.float32);images['AO'].pixels.foreach_get(aop);aop=aop.reshape(-1,4)
pixels[:,3]=pixels[:,2];pixels[:,1]=.40+.60*aop[:,0];pixels[:,2]=0
images['Surface'].pixels.foreach_set(pixels.ravel());images['Surface'].update()
for name in ['BaseColor','Normal','Surface']:
 im=images[name];im.filepath_raw=str(OUT/(name+'.png'));im.file_format='PNG';im.save()
me=ob.data;me.calc_loop_triangles();uv=me.uv_layers.active.data;result=[]
for tri in me.loop_triangles:
 row=[]
 for li in tri.loops:
  p=me.vertices[me.loops[li].vertex_index].co;n=me.corner_normals[li].vector
  row.append(dict(p=[-p.y,p.z,p.x],n=[-n.y,n.z,n.x],uv=list(uv[li].uv)))
 result.append([row[0],row[2],row[1]])
(OUT/'atlas.json').write_text(json.dumps(dict(triangles=result),separators=(',',':')))
(OUT/'atlas-flat.json').write_text(json.dumps(dict(corners=[c for t in result for c in t]),separators=(',',':')))
(OUT/'included.json').write_text(json.dumps([p['name'] for p in parts if not(any(s in p['name'] for s in ['_Glass','Hinged_Port_Cover','Helm_Spoke']) or p['name']=='Helm')]))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'bake.blend'));print('STERN_BAKE_DONE',flush=True)
