import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v16/steering-and-gun-bays'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
bpy.context.scene.cycles.samples=24
kit.shot(out/'stairs-and-upper-deck.png',(22,25,23),(5,0,4.2),22,(1500,1200))
kit.shot(out/'stern-top.png',(3,0,35),(3,0,0),15,(1200,1100))
