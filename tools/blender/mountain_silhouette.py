"""Stage-one mountain study. Run through Blender MCP; no runtime exports.

Unity-style design coordinates: x, height, rearward distance. This intentionally
keeps the existing plateau source and all gameplay data untouched.
"""
import bpy
from pathlib import Path
from mathutils import Vector

ROOT = Path('/Users/kevinandersson/Desktop/SeaSick')
OUT = ROOT / 'docs/art-direction'
SOURCE = ROOT / 'tools/blender/source'

# Each loop is a different contour, not a level terrace. The western shoulder
# rises gradually; the eastern flank drops steeply into two low promontories.
foot = [(-78,0,15),(-52,0,-2),(-18,0,6),(15,0,3),(52,0,-4),(78,0,20),
        (79,0,48),(52,0,73),(12,0,82),(-27,0,77),(-65,0,61),(-80,0,40)]
coast = [(-67,24,19),(-47,19,9),(-18,15,17),(12,13,13),(48,18,7),(65,21,23),
         (64,26,44),(45,23,64),(9,29,69),(-24,27,65),(-53,30,53),(-68,27,37)]
shoulder = [(-53,30,30),(-39,32,23),(-18,30,28),(12,25,25),(40,26,24),(51,29,36),
            (46,34,46),(36,36,58),(9,42,61),(-19,39,56),(-40,35,49),(-54,33,38)]
# An interrupted high shoulder creates a secondary rise and a shallow saddle
# before the main crown. This breaks the long straight tent-like skyline.
ridge = [(-31,40,34),(-25,43,29),(-8,40,32),(17,39,31),(36,37,34),(40,35,41),
         (37,38,49),(31,43,56),(7,47,58),(-20,44,55),(-32,50,49),(-38,45,41)]
summit_foot = [(-13,42,40),(-9,44,36),(8,46,35),(24,44,35),(34,42,39),(36,40,47),
               (32,45,54),(24,49,57),(12,50,58),(-5,45,56),(-12,43,51),(-15,42,46)]
# Summit is deliberately offset right and back. Several vertices lie on broad
# faces; the skyline has a sloping saddle and a blunt, irregular crown.
crown = [(2,55,43),(5,59,39),(13,61,38),(24,63,38),(31,62,42),(33,61,47),
         (30,64,52),(24,65,55),(15,63,55),(8,61,53),(2,57,50),(-1,54,47)]
verts = foot + coast + shoulder + ridge + summit_foot + crown + [(17,63,47)]
faces = []
for ring in range(5):
    for i in range(12):
        j=(i+1)%12; a=ring*12+i; b=ring*12+j; c=b+12; d=a+12
        # Alternating the chosen diagonal follows the direction of the ridge.
        faces.extend([(a,b,d),(b,c,d)] if i in (0,4,7,10) else [(a,b,c),(a,c,d)])
for i in range(12): faces.append((60+i,60+(i+1)%12,72))

def material(name, color, emission=False):
    mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color=(*color,1); mat.use_nodes=True
    nodes=mat.node_tree.nodes; nodes.clear()
    output=nodes.new('ShaderNodeOutputMaterial')
    shader=nodes.new('ShaderNodeEmission' if emission else 'ShaderNodeBsdfPrincipled')
    shader.inputs['Color' if emission else 'Base Color'].default_value=(*color,1)
    if not emission: shader.inputs['Roughness'].default_value=.88
    mat.node_tree.links.new(shader.outputs[0],output.inputs['Surface'])
    return mat

previous=bpy.context.window.scene
scene=bpy.data.scenes.get('Mountain_Silhouette_Study') or bpy.data.scenes.new('Mountain_Silhouette_Study')
for ob in list(scene.objects): bpy.data.objects.remove(ob,do_unlink=True)
bpy.context.window.scene=scene
try:
    mesh=bpy.data.meshes.new('Mountain_Study_Connected_Surface')
    mesh.from_pydata([(x,z,h) for x,h,z in verts],[],faces); mesh.update()
    land=bpy.data.objects.new('Mountain silhouette — long shoulder and offset crown',mesh)
    scene.collection.objects.link(land)
    clay=material('MountainStudy_Clay',(.48,.43,.34))
    mesh.materials.append(clay)
    for poly in mesh.polygons: poly.use_smooth=False

    # A very shallow foundation closes the mountain into a single solid form.
    bottom=bpy.data.meshes.new('MountainStudy_Foundation')
    foundation=[(x,z,h) for x,h,z in foot]+[(x,z,-2) for x,h,z in foot]
    sides=[(i,(i+1)%12,(i+1)%12+12,i+12) for i in range(12)]
    sides.append(tuple(range(23,11,-1)))
    bottom.from_pydata(foundation,[],sides);bottom.update()
    base=bpy.data.objects.new('Study foundation',bottom);scene.collection.objects.link(base);bottom.materials.append(clay)

    camera_data=bpy.data.cameras.new('MountainStudy_Camera')
    camera=bpy.data.objects.new('MountainStudy_Camera',camera_data);scene.collection.objects.link(camera)
    scene.camera=camera;camera_data.type='ORTHO';camera_data.ortho_scale=195
    light_data=bpy.data.lights.new('MountainStudy_Key','AREA');light_data.energy=240000;light_data.shape='DISK';light_data.size=70
    light=bpy.data.objects.new('MountainStudy_Key',light_data);scene.collection.objects.link(light)
    light.location=(-60,-90,170);light.rotation_euler=(Vector((0,35,20))-light.location).to_track_quat('-Z','Y').to_euler()
    world=bpy.data.worlds.new('MountainStudy_World');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.42,.52,.65,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.35;scene.world=world
    scene.render.engine='CYCLES';scene.cycles.samples=32
    scene.render.resolution_x=1500;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='Medium High Contrast' if 'Medium High Contrast' in [x.identifier for x in scene.view_settings.bl_rna.properties['look'].enum_items] else 'None'

    views=[('sea', (0,-240,43),(0,37,30)),
           ('gameplay',(-145,-195,150),(0,38,24)),
           ('reverse',(140,220,115),(0,39,26))]
    for label,position,target in views:
        camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/f'mountain-silhouette-{label}.png')
        bpy.ops.render.render(write_still=True)
    # Pure silhouette reveals proportion without lighting doing the work.
    ink=material('MountainStudy_Ink',(.028,.045,.067),True)
    mesh.materials[0]=ink;bottom.materials[0]=ink
    camera.location=views[0][1];camera.rotation_euler=(Vector(views[0][2])-camera.location).to_track_quat('-Z','Y').to_euler()
    world.node_tree.nodes['Background'].inputs[0].default_value=(.8,.86,.91,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=1
    scene.render.filepath=str(OUT/'mountain-silhouette-outline.png');bpy.ops.render.render(write_still=True)
    mesh.materials[0]=clay;bottom.materials[0]=clay
    world.node_tree.nodes['Background'].inputs[0].default_value=(.42,.52,.65,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.35
    bpy.data.libraries.write(str(SOURCE/'mountain-silhouette-study.blend'),{scene})
    print(f'Saved isolated study: {len(verts)} vertices, {len(faces)} faces, four review views. No Unity assets written.')
finally:
    bpy.context.window.scene=previous
