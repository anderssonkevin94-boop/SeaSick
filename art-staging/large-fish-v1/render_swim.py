import bpy
from pathlib import Path
from mathutils import Vector
p=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(p/'large-fish.blend'))
s=bpy.context.scene
# Three-quarter overhead view exposes the lateral wave and the upright tail.
s.camera.location=(3,-8,7);s.camera.rotation_euler=(Vector((-.3,0,0))-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=8.2
s.render.engine='BLENDER_EEVEE';s.render.resolution_x=800;s.render.resolution_y=600;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.fps=24
folder=p/'swim-frames';folder.mkdir(exist_ok=True)
for f in range(1,49):
 s.frame_set(f);s.render.filepath=str(folder/f'{f:04d}.png');bpy.ops.render.render(write_still=True)
