import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v14/straight-deck'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
bpy.context.scene.cycles.samples=24
kit.shot(out/'stern-top.png',(2,0,30),(2,0,0),13,(1200,1100))
