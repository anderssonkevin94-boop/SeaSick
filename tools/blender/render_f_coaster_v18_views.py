import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v18/timber-top-band'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
bpy.context.scene.cycles.samples=24
kit.shot(out/'stairs-and-upper-deck.png',(22,25,23),(5,0,4.2),22,(1500,1200))
kit.shot(out/'stern-top.png',(3,0,35),(3,0,0),15,(1200,1100))

# Demonstrate an empty bay: the same occupancy property closes the lid and
# hides that bay's review cannon through drivers.
hinge=bpy.data.objects['Cannon_Port_Cover_Starboard'];hinge['cannon_present']=False
hinge.update_tag();bpy.context.scene.frame_set(2);bpy.context.view_layer.update()
kit.shot(out/'empty-port-closed.png',(-24,-33,29),(22.2*.40,0,4.1),34,(1600,1100))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'ship-empty-starboard.blend'))
