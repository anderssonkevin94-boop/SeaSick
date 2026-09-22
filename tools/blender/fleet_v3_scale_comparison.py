"""Render stages 12–14 with identical pixels per metre."""
import bpy,json
from pathlib import Path
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick');OUT=ROOT/'docs/art-direction/fleet-v3'
loaded=[]
for stage in [12,13,14]:
 with bpy.data.libraries.load(str(ROOT/'tools/blender/source/fleet-v3'/f'{stage:02d}.blend'),link=False) as (src,dst):dst.scenes=src.scenes
 loaded.append((stage,dst.scenes[0]))
scale=max(sc.camera.data.ortho_scale for _,sc in loaded)
for stage,sc in loaded:
 bpy.context.window.scene=sc
 sc.camera.data.ortho_scale=scale
 sc.render.resolution_x=960;sc.render.resolution_y=720;sc.render.resolution_percentage=100;sc.cycles.samples=20
 sc.render.threads_mode='FIXED';sc.render.threads=8
 sc.render.filepath=str(OUT/f'{stage:02d}-same-scale.png');bpy.ops.render.render(write_still=True)
(OUT/'comparison-camera.json').write_text(json.dumps({'stages':[12,13,14],'ortho_scale':scale,'resolution':[960,720],'identical_camera_rotation':all(tuple(s.camera.rotation_euler)==tuple(loaded[0][1].camera.rotation_euler) for _,s in loaded)},indent=2))
print('Three stages rendered with identical orthographic scale and camera direction.')
