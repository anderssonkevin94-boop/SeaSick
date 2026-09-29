import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v24/ladders-and-closed-ends'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'));bpy.context.scene.cycles.samples=24
kit.shot(out/'bow-module.png',(29,-24,24),(16,0,4.7),22,(1500,1200))
kit.shot(out/'top.png',(11,0,40),(11,0,0),29,(1500,1100))
