"""Blender composition study: flat foreland, broad beaches, sheltered cove.
Builds on the approved mountain study without exporting any Unity assets.
"""
import bpy
from pathlib import Path
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
OUT=ROOT/'docs/art-direction'
SOURCE=ROOT/'tools/blender/source'

# Counter-clockwise shoreline with a southern inlet cut into its foreground.
shore=[(0,-28),(36,-28),(47,-43),(50,-64),(62,-84),(91,-77),
       (112,-50),(121,-14),(117,28),(99,72),(71,109),(35,126),
       (-10,130),(-52,118),(-86,92),(-107,55),(-120,15),(-116,-30),
       (-101,-68),(-73,-94),(-41,-103),(-16,-94),(-6,-77),(-9,-53),(-6,-37)]
turf=[(0,-12),(34,-12),(59,-36),(65,-58),(69,-66),(80,-60),
      (98,-39),(105,-10),(102,24),(89,65),(65,97),(32,112),
      (-8,115),(-47,104),(-74,83),(-94,50),(-103,12),(-99,-24),
      (-86,-57),(-64,-76),(-39,-85),(-31,-78),(-23,-68),(-24,-47),(-20,-25)]

def mat(name,color,roughness=.85):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=roughness
    return m

def mesh_object(scene,name,verts,faces,material):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);scene.collection.objects.link(ob)
    mesh.materials.append(material)
    return ob

previous=bpy.context.window.scene
scene=bpy.data.scenes.get('Island_Foreland_Study') or bpy.data.scenes.new('Island_Foreland_Study')
for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
bpy.context.window.scene=scene
try:
    grass=mat('ForelandStudy_Grass',(.24,.38,.065))
    sand=mat('ForelandStudy_Sand',(.78,.61,.33))
    stone=mat('ForelandStudy_Stone',(.57,.50,.39))
    water=mat('ForelandStudy_Water',(.025,.25,.38),.2)

    # Load only the mountain surface; its original study stays intact.
    with bpy.data.libraries.load(str(SOURCE/'mountain-silhouette-study.blend'),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n.startswith('Mountain silhouette')]
    mountain=dst.objects[0];scene.collection.objects.link(mountain)
    mountain.location=(0,25,3.2)
    mountain.data.materials.clear();mountain.data.materials.append(stone);mountain.data.materials.append(grass)
    for p in mountain.data.polygons:p.material_index=1 if p.normal.z>.83 else 0

    # The usable ground is genuinely planar, not a sloped skirt around the hill.
    flat=[Vector((x,y,3.2)) for x,y in turf]
    tris=tessellate_polygon([flat])
    def index(v):return v if isinstance(v,int) else min(range(len(flat)),key=lambda i:(flat[i]-v).length_squared)
    foreland=mesh_object(scene,'Flat grassy foreland — 3.2 m level',flat,
                         [tuple(index(v) for v in t) for t in tris],grass)
    # Broad dry beach, then a shallow wet-sand approach to the waterline.
    lower=[(x*.75+gx*.25,y*.75+gy*.25,1.5) for (x,y),(gx,gy) in zip(shore,turf)]
    upper=[(x*.25+gx*.75,y*.25+gy*.75,1.65) for (x,y),(gx,gy) in zip(shore,turf)]
    beachverts=[(x,y,-.18) for x,y in shore]+lower+upper+[(x,y,3.2) for x,y in turf]
    n=len(shore);beachfaces=[]
    for ring in range(3):
        for i in range(n):
            j=(i+1)%n;a=ring*n+i;b=ring*n+j
            beachfaces.extend([(a,b,b+n),(a,b+n,a+n)])
    mesh_object(scene,'Broad beaches and cove banks',beachverts,beachfaces,sand)
    mesh_object(scene,'Sea level', [(-10000,-10000,0),(10000,-10000,0),(10000,10000,0),(-10000,10000,0)],[(0,1,2,3)],water)

    data=bpy.data.cameras.new('ForelandStudy_Camera');camera=bpy.data.objects.new('ForelandStudy_Camera',data)
    scene.collection.objects.link(camera);scene.camera=camera;data.type='ORTHO';data.clip_end=12000
    lightdata=bpy.data.lights.new('ForelandStudy_Sun','SUN');lightdata.energy=2.4;lightdata.angle=.12
    sun=bpy.data.objects.new('ForelandStudy_Sun',lightdata);scene.collection.objects.link(sun)
    sun.rotation_euler=(.5,-.6,-.4)
    world=bpy.data.worlds.new('ForelandStudy_World');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.40,.55,.75,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.6;scene.world=world
    scene.render.engine='CYCLES';scene.cycles.samples=32
    scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='Standard'
    views=[('overview',(-180,-270,225),(0,10,17),325),
           ('harbour',(20,-320,60),(0,32,21),300),
           ('plan',(0,10,400),(0,10,0),355)]
    for label,position,target,scale in views:
        camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        data.ortho_scale=scale;scene.render.filepath=str(OUT/f'island-foreland-{label}.png')
        bpy.ops.render.render(write_still=True)
    camera.location=views[0][1];camera.rotation_euler=(Vector(views[0][2])-camera.location).to_track_quat('-Z','Y').to_euler()
    data.ortho_scale=views[0][3]
    bpy.data.libraries.write(str(SOURCE/'island-foreland-study.blend'),{scene})
    print('Saved foreland study: flat ground at 3.2 m, mountain moved 25 m rearward, broad nearly level dry beach at 1.5–1.65 m, three review views. Unity unchanged.')
finally:
    bpy.context.window.scene=previous
