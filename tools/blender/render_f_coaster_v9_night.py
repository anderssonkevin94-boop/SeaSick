"""Night presentation of F9. Saves a separate editable lighting scene."""
import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v9/flat-foredeck'
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
s=bpy.context.scene;s.cycles.samples=48
bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND')
bg.inputs[0].default_value=(.12,.22,.40,1);bg.inputs[1].default_value=.28
for name,power,col in [('Key',1100,(.45,.62,1)),('Fill',400,(.36,.51,.85)),('Rim',1200,(.45,.65,1))]:
 d=bpy.data.lights[name];d.energy=power;d.color=col
p=next(n for n in bpy.data.objects['Studio_Ground'].data.materials[0].node_tree.nodes if n.type=='BSDF_PRINCIPLED')
p.inputs['Base Color'].default_value=(.025,.05,.085,1);p.inputs['Roughness'].default_value=.48
s.view_settings.exposure=.7
s.render.filepath=str(out/'night.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'ship-night.blend'))
kit.shot(out/'night-deck.png',(17,-13,13),(20.4,0,2.8),11,(1500,1100))
print('NIGHT_DONE')
