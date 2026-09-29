"""Remove only decorative cargo crates from saved F9 day/night scenes."""
import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v9/flat-foredeck'
for name,overview in [('ship.blend','hero.png'),('ship-night.blend','night.png')]:
 bpy.ops.wm.open_mainfile(filepath=str(out/name))
 for obname in ['Review_Crates','Review_Cargo']:
  ob=bpy.data.objects.get(obname)
  if ob:bpy.data.objects.remove(ob,do_unlink=True)
 assert not any(o.name.startswith('Review_Crate') for o in bpy.context.scene.objects)
 bpy.context.scene.cycles.samples=24
 bpy.context.scene.render.filepath=str(out/overview)
 bpy.ops.wm.save_as_mainfile(filepath=str(out/name))
 bpy.ops.render.render(write_still=True)
 if name=='ship.blend':
  kit.shot(out/'foredeck-day.png',(17,-13,13),(20.4,0,2.8),11,(1500,1100))
  kit.shot(out/'foredeck-low.png',(18,-10,4.7),(21,0,2.4),9,(1400,900))
 else:kit.shot(out/'night-deck.png',(17,-13,13),(20.4,0,2.8),11,(1500,1100))
print('CRATES_REMOVED')
