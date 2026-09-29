import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v22/two-middle-sections'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'));bpy.context.scene.cycles.samples=24
kit.shot(out/'bow-module.png',(34,-30,29),(18,0,4.7),29,(1500,1200))
kit.shot(out/'top.png',(14,0,45),(14,0,0),36,(1500,1100))
