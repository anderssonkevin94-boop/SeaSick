"""Stage-one mountain study. Run through Blender MCP; no runtime exports.

Unity-style design coordinates: x, height, rearward distance. This intentionally
keeps the existing plateau source and all gameplay data untouched.
"""
import bpy
from pathlib import Path
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

ROOT = Path('/Users/kevinandersson/Desktop/SeaSick')
OUT = ROOT / 'docs/art-direction'
SOURCE = ROOT / 'tools/blender/source'

# Each loop is a different contour, not a level terrace. The western shoulder
# rises gradually; the eastern flank drops steeply into two low promontories.
foot = [(-111,-5,18),(-52,0,-2),(-18,0,6),(15,0,3),(52,0,-4),(116,-5,26),
        (106,-5,65),(61,0,91),(12,0,82),(-27,0,100),(-95,-5,77),(-113,-5,46)]
coast = [(-78,20,21),(-47,19,9),(-18,9,22),(12,9,22),(48,22,7),(94,17,32),
         (81,23,61),(46,21,77),(9,29,69),(-24,27,78),(-79,23,65),(-94,28,43)]
shoulder = [(-53,25,30),(-39,20,23),(-18,10.5,28),(12,10.5,28),(40,24,24),(51,25,36),
            (46,27,46),(36,29,58),(9,31,61),(-19,29,56),(-40,31,49),(-54,28,38)]
# An interrupted high shoulder creates a secondary rise and a shallow saddle
# before the main crown. This breaks the long straight tent-like skyline.
ridge = [(-31,36,34),(-25,31.5,29),(-8,34,32),(17,35,31),(36,36,34),(40,35,41),
         (37,38,49),(31,43,56),(7,47,58),(-20,44,55),(-32,50,49),(-38,45,41)]
summit_foot = [(-13,45,40),(-9,42,36),(8,36,35),(24,36,35),(34,37,39),(36,37,47),
               (32,40,52),(24,44,54),(12,46,56),(-5,47,56),(-12,46,51),(-15,45,46)]
# Summit is deliberately offset right and back. Several vertices lie on broad
# faces; the skyline has a sloping saddle and a blunt, irregular crown.
crown = [(5,57.5,43),(7,59.5,39),(13,61,38),(24,63,38),(31,62,42),(33,61,47),
         (30,64,52),(24,65,53),(15,62,55),(8,60,53),(5,58,50),(2,56.5,47)]

# Pinch some ledges into the rock mass instead of carrying equal-width grass
# ribbons around every level. Keep the southern approach and western saddle.
for i,weight in [(4,.78),(5,.82),(6,.72),(10,.72),(11,.76)]:
    p=shoulder[i];q=coast[i]
    shoulder[i]=tuple(a*(1-weight)+b*weight for a,b in zip(p,q))
for i,weight in [(4,.60),(5,.73),(6,.75),(7,.68),(8,.50)]:
    p=summit_foot[i];q=ridge[i]
    summit_foot[i]=tuple(a*(1-weight)+b*weight for a,b in zip(p,q))

def softened_corners(loop, amount=.16):
    # Two points at each authored corner form short, deliberate chamfers.
    # More geometry goes into the silhouette instead of noisy triangulation.
    result=[]
    for i,p in enumerate(loop):
        for neighbour in (loop[(i-1)%len(loop)],loop[(i+1)%len(loop)]):
            result.append(tuple((1-amount)*a+amount*b for a,b in zip(p,neighbour)))
    return result

verts = []
faces = []; zones=[]

def patch(indices,zone):
    # Triangulate in the ground plane so concave shelf corners cannot fold.
    points=[Vector((verts[i][0],verts[i][2],0)) for i in indices]
    for tri in tessellate_polygon([points]):
        ids=[v if isinstance(v,int) else min(range(len(points)),key=lambda k:(points[k]-v).length_squared) for v in tri]
        faces.append(tuple(indices[i] for i in ids));zones.append(zone)

base24=softened_corners(foot,.12);coast24=softened_corners(coast,.12)
shoulder24=softened_corners(shoulder,.12)
ridge24=softened_corners(ridge);foot24=softened_corners(summit_foot);crown24=softened_corners(crown)

def stone_shoulders(low,high,phase=0):
    # Uneven shoulder elevations split broad walls into inclined stone planes.
    # Keep their horizontal position between the surrounding loops; no added
    # boulder shells, surface noise, or detached decorative geometry.
    heights=(.42,.63,.50,.57,.38,.69,.47,.60,.40,.65,.51,.58)
    result=[]
    for i,(a,b) in enumerate(zip(low,high)):
        t=heights[((i+1)//2+phase)%12]
        horizontal=t-.11
        result.append((a[0]*(1-horizontal)+b[0]*horizontal,
                       a[1]*(1-t)+b[1]*t,
                       a[2]*(1-horizontal)+b[2]*horizontal))
    return result

lower_break=stone_shoulders(base24,coast24)
middle_break=stone_shoulders(shoulder24,ridge24,3)
bevel=[]
for low,high in zip(foot24,crown24):
    # A short upper shoulder catches light above the broad main rock planes.
    bevel.append((low[0]*.22+high[0]*.78,high[1]-3.0,low[2]*.22+high[2]*.78))
loops=[base24,lower_break,coast24,shoulder24,middle_break,ridge24,foot24,bevel,crown24]
verts=[p for loop in loops for p in loop]

def ground_zone(low,high,i):
    # Classify the original whole wall/slope, then carry that decision through
    # every new facet. Added geometry must not create scattered turf triangles.
    j=(i+1)%24
    a,b,c=[Vector((p[0],p[2],p[1])) for p in (low[i],low[j],high[j])]
    return 1 if (b-a).cross(c-a).normalized().z>.83 else 0

for ring in range(len(loops)-1):
    for i in range(24):
        j=(i+1)%24;a=ring*24+i;b=ring*24+j
        if ring in (0,1):zone=ground_zone(base24,coast24,i)
        elif ring in (3,4):zone=ground_zone(shoulder24,ridge24,i)
        elif ring in (2,5):zone=1
        else:zone=1 if i in (0,21,22,23) else 0
        patch([a,b,b+24,a+24],zone)
centre=len(verts);verts.append((17,61.6,47))
top_start=(len(loops)-1)*24
for i in range(24):faces.append((top_start+i,top_start+(i+1)%24,centre));zones.append(1)

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
    zone_attribute=mesh.attributes.new(name='TerrainZone',type='INT',domain='FACE')
    for i,zone in enumerate(zones):zone_attribute.data[i].value=zone
    land=bpy.data.objects.new('Mountain silhouette — long shoulder and offset crown',mesh)
    scene.collection.objects.link(land)
    clay=material('MountainStudy_Clay',(.48,.43,.34))
    mesh.materials.append(clay)
    for poly in mesh.polygons: poly.use_smooth=False

    # A very shallow foundation closes the mountain into a single solid form.
    bottom=bpy.data.meshes.new('MountainStudy_Foundation')
    foundation=[(x,z,h) for x,h,z in base24]+[(x,z,-2) for x,h,z in base24]
    sides=[(i,(i+1)%24,(i+1)%24+24,i+24) for i in range(24)]
    sides.append(tuple(range(47,23,-1)))
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
        if globals().get('RENDER_STUDIES',True): bpy.ops.render.render(write_still=True)
    # Pure silhouette reveals proportion without lighting doing the work.
    ink=material('MountainStudy_Ink',(.028,.045,.067),True)
    mesh.materials[0]=ink;bottom.materials[0]=ink
    camera.location=views[0][1];camera.rotation_euler=(Vector(views[0][2])-camera.location).to_track_quat('-Z','Y').to_euler()
    world.node_tree.nodes['Background'].inputs[0].default_value=(.8,.86,.91,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=1
    scene.render.filepath=str(OUT/'mountain-silhouette-outline.png')
    if globals().get('RENDER_STUDIES',True): bpy.ops.render.render(write_still=True)
    mesh.materials[0]=clay;bottom.materials[0]=clay
    world.node_tree.nodes['Background'].inputs[0].default_value=(.42,.52,.65,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.35
    bpy.data.libraries.write(str(SOURCE/'mountain-silhouette-study.blend'),{scene})
    print(f'Saved isolated study: {len(verts)} vertices, {len(faces)} faces, four review views. No Unity assets written.')
finally:
    bpy.context.window.scene=previous
