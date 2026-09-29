import bpy,json,math
from pathlib import Path
p=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.context.scene.render.fps=24
bpy.ops.import_scene.fbx(filepath=str(p/'large-fish-rigged.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE'];assert len(meshes)==1 and len(rigs)==1
m=meshes[0];r=rigs[0];assert len(r.data.bones)==5 and r.animation_data.action
assert all(v.groups for v in m.data.vertices)
assert all(math.isfinite(c) for v in m.data.vertices for c in v.co)
print("RANGE",r.animation_data.action.frame_range[:]);poses=[]
start,end=r.animation_data.action.frame_range
for f in [int(start),int((start+end)/2),int(end)]:
 bpy.context.scene.frame_set(f);bpy.context.view_layer.update();poses.append([list(b.matrix_basis.to_quaternion()) for b in r.pose.bones])
print("POSES",poses)
assert max(abs(a-b) for va,vb in zip(poses[0],poses[2]) for a,b in zip(va,vb))<.001
assert max(abs(a-b) for va,vb in zip(poses[0],poses[1]) for a,b in zip(va,vb))>.05
m.data.calc_loop_triangles()
(p/'export-check.json').write_text(json.dumps({'passed':True,'triangles':len(m.data.loop_triangles),'bones':len(r.data.bones),'weighted_vertices':len(m.data.vertices),'animation_moves':True,'loop_endpoints_match':True,'vertex_color_layers':list(m.data.color_attributes.keys())},indent=2))
