import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v19/rear-and-middle'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'));bpy.context.scene.cycles.samples=24
kit.shot(out/'middle-access.png',(27,25,25),(12,0,4),26,(1500,1200))
kit.shot(out/'top.png',(11,0,40),(11,0,0),29,(1500,1100))
