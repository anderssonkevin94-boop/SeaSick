"""Render focused stern views for checking the requested structural changes."""
import bpy,json,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick');OUT=ROOT/'docs/art-direction/fleet-v2'
ships=json.loads((OUT/'manifest.json').read_text())
indices=[int(x)-1 for x in sys.argv[sys.argv.index('--')+1:]] if '--' in sys.argv else [3,6,19]
for i in indices:
 s=ships[i];L=s['length'];B=s['beam'];F=s['freeboard']
 with bpy.data.libraries.load(str(ROOT/'tools/blender/source/fleet-v2'/s['model']),link=False) as (src,dst):dst.scenes=src.scenes
 sc=dst.scenes[0];bpy.context.window.scene=sc;cam=sc.camera
 direction=Vector((-1,-.75,.58)).normalized();target=Vector((-L*.35,0,F+1.35));cam.location=target+direction*60;cam.rotation_euler=(-direction).to_track_quat('-Z','Y').to_euler()
 rot=cam.rotation_euler.to_matrix();inv=rot.transposed()
 corners=[inv@Vector((x,y,z)) for x in [-L*.54,-L*.15] for y in [-B*.52,B*.52] for z in [F-.5,F+(4.2 if i>=6 else 2.3)]]
 lo=Vector((min(p.x for p in corners),min(p.y for p in corners),0));hi=Vector((max(p.x for p in corners),max(p.y for p in corners),0))
 cam.location=rot@((lo+hi)/2+Vector((0,0,100)));cam.data.ortho_scale=max(hi.x-lo.x,(hi.y-lo.y)*4/3)*1.03
 sc.render.resolution_x=1440;sc.render.resolution_y=1080;sc.render.resolution_percentage=100;sc.cycles.samples=32;sc.render.threads_mode='FIXED';sc.render.threads=8
 sc.render.filepath=str(OUT/f'{i+1:02d}-stern.png');bpy.ops.render.render(write_still=True)
 for ob in list(sc.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(sc)
 for blocks in [bpy.data.meshes,bpy.data.curves,bpy.data.materials,bpy.data.lights,bpy.data.cameras,bpy.data.worlds]:
  for block in list(blocks):
   if block.users==0:blocks.remove(block)
 print('STERN_DETAIL',i+1,flush=True)
