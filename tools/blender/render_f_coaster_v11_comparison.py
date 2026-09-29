import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
root=Path(__file__).resolve().parents[2]/'art-staging';out=root/'f-coaster-v11/rounded-stern'
for label,path in [('base',root/'ship-approved-base-f10/funnel/ship.blend'),('rounded',out/'ship.blend')]:
 bpy.ops.wm.open_mainfile(filepath=str(path));bpy.context.scene.cycles.samples=16
 kit.shot(out/(label+'-stern.png'),(-13,-16,14),(1.6,0,2.8),17,(1400,1100))
 kit.shot(out/(label+'-top.png'),(2,0,30),(2,0,0),13,(1200,1100))
