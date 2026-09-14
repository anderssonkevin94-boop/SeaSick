"""Option B palette study using the unmodified, shipped node-12 brig FBX.

Run through Blender MCP. Owns only the SeaSick_OptionB_Brig scene; preserves
the user's active scene. No hull generator is executed and no FBX is exported.
"""
import ast
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path('/Users/kevinandersson/Desktop/SeaSick')
SCENE = 'SeaSick_OptionB_Brig'

# Semantic fleet slots 0–24; resource slots 25+ remain the generator's values.
FLEET = [
    (.46,.27,.14), (.57,.35,.18), (.67,.44,.23), (.75,.53,.30),
    (.90,.75,.48), (.82,.65,.38), (.20,.24,.28), (.83,.62,.32),
    (.34,.24,.17), (.31,.20,.14), (.76,.55,.29), (.97,.88,.67),
    (.78,.67,.46), (.72,.29,.19), (.20,.25,.30), (.91,.70,.36),
    (.87,.78,.59), (.93,.84,.64), (.58,.46,.29), (.18,.22,.26),
    (.63,.42,.22), (.94,.74,.37), (.21,.42,.52), (.55,.50,.44), (.66,.47,.29),
]

def palette():
    tree = ast.parse((ROOT/'tools/blender/seasick_hulls.py').read_text())
    swatches = next(ast.literal_eval(n.value) for n in tree.body
                    if isinstance(n, ast.Assign)
                    and any(isinstance(t, ast.Name) and t.id == 'SWATCH' for t in n.targets))
    swatches[:25] = FLEET
    image = bpy.data.images.new('SeaSick_GraphicPalette', 128, 128, alpha=False)
    image.colorspace_settings.name = 'sRGB'
    pixels = [0.0] * (128*128*4)
    for i, colour in enumerate(swatches):
        for y in range((i//8)*16, (i//8+1)*16):
            for x in range((i%8)*16, (i%8+1)*16):
                at = (y*128+x)*4
                pixels[at:at+4] = [*colour, 1.0]
    image.pixels = pixels
    image.filepath_raw = str(ROOT/'docs/art-direction/graphic-palette-v1.png')
    image.file_format = 'PNG'
    image.save()
    return image

def prepare():
    s = bpy.data.scenes.get(SCENE)
    if s is None:
        raise RuntimeError('Import the shipped n12_brig.fbx into the isolated preview scene first.')
    for o in list(s.objects):
        if o.name.startswith('GraphicPreview'):
            bpy.data.objects.remove(o, do_unlink=True)
    s.render.engine = 'BLENDER_EEVEE'
    s.render.resolution_x, s.render.resolution_y = 1200, 800
    s.render.resolution_percentage = 100
    s.render.image_settings.file_format = 'PNG'
    s.view_settings.view_transform = 'Standard'
    s.view_settings.look = 'None'
    world = bpy.data.worlds.new('GraphicPreviewWorld')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.24,.34,.48,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .55
    s.world = world
    camera = bpy.data.objects.new('GraphicPreviewCamera', bpy.data.cameras.new('GraphicPreviewCamera'))
    s.collection.objects.link(camera)
    camera.location = (34, 39, 25)
    camera.rotation_euler = (Vector((0,-3,5))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 40
    s.camera = camera
    light = bpy.data.objects.new('GraphicPreviewSun', bpy.data.lights.new('GraphicPreviewSun','SUN'))
    s.collection.objects.link(light)
    light.rotation_euler = Vector((-25,-35,-45)).to_track_quat('-Z','Y').to_euler()
    light.data.energy = 2.2
    light.data.angle = .12
    light.data.color = (1,.96,.88)
    fill = bpy.data.objects.new('GraphicPreviewFill', bpy.data.lights.new('GraphicPreviewFill','AREA'))
    s.collection.objects.link(fill)
    fill.location = (-15, 15, 22)
    fill.rotation_euler = (Vector((0,0,5))-fill.location).to_track_quat('-Z','Y').to_euler()
    fill.data.energy = 2200
    fill.data.shape = 'DISK'
    fill.data.size = 25
    fill.data.color = (.65,.79,1)
    mesh = bpy.data.meshes.new('GraphicPreviewSea')
    mesh.from_pydata([(-200,-200,-.1),(200,-200,-.1),(200,200,-.1),(-200,200,-.1)],[],[(0,1,2,3)])
    plane = bpy.data.objects.new('GraphicPreviewSea',mesh)
    s.collection.objects.link(plane)
    sea = bpy.data.materials.new('GraphicPreviewSea')
    sea.diffuse_color = (.025,.18,.38,1)
    sea.use_nodes = True
    sea.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.025,.18,.38,1)
    sea.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .7
    mesh.materials.append(sea)
    shipmat = next(o.data.materials[0] for o in s.objects if o.type=='MESH' and o.name.startswith('N12_Brig_Hull'))
    tex = next(n for n in shipmat.node_tree.nodes if n.type=='TEX_IMAGE')
    tex.image = bpy.data.images.load(str(ROOT/'Assets/_Project/Art/Ship/Hulls/seasick_palette.png'), check_existing=True)
    tex.interpolation = 'Closest'
    bsdf = shipmat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value = .82
    return s, tex

def render_pair():
    original = bpy.context.window.scene
    try:
        s, tex = prepare()
        bpy.context.window.scene = s
        s.render.filepath = str(ROOT/'docs/art-direction/brig-baseline-blender.png')
        bpy.ops.render.render(write_still=True)
        tex.image = palette()
        s.render.filepath = str(ROOT/'docs/art-direction/brig-option-b-blender.png')
        bpy.ops.render.render(write_still=True)
    finally:
        bpy.context.window.scene = original

if __name__ == '__main__':
    render_pair()
