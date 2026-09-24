"""Standalone reference study. Outputs only to art-staging; never imports into Unity."""
import bpy
import bmesh
import math
import random
import json
from pathlib import Path
from mathutils import Vector, noise

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-staging/groundcover-reference-v1'
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
for ob in list(scene.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
library = bpy.data.collections.new('Reusable assets - ground pivots')
scene.collection.children.link(library)
study = bpy.data.collections.new('Reference arrangement')
scene.collection.children.link(study)
stage = bpy.data.collections.new('Preview only - ground cameras lighting')
scene.collection.children.link(stage)

def linear(c):
    return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4

def color(h):
    return tuple(linear(int(h[i:i+2], 16) / 255) for i in (1, 3, 5)) + (1,)

material = bpy.data.materials.new('Shared matte vertex palette')
material.use_nodes = True
bsdf = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
bsdf.inputs['Roughness'].default_value = .93
bsdf.inputs['Specular IOR Level'].default_value = .15
vertex = material.node_tree.nodes.new('ShaderNodeVertexColor')
vertex.layer_name = 'Col'
material.node_tree.links.new(vertex.outputs['Color'], bsdf.inputs['Base Color'])

def mesh(name, verts, faces, palette, collection=library, face_colors=None):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    bm = bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    ob = bpy.data.objects.new(name, data); collection.objects.link(ob)
    data.materials.append(material)
    attr = data.color_attributes.new(name='Col', type='BYTE_COLOR', domain='CORNER')
    rng = random.Random(name)
    for p in data.polygons:
        p.use_smooth = False
        c = face_colors[p.index] if face_colors else color(rng.choice(palette))
        for i in p.loop_indices:
            attr.data[i].color = c
    return ob

def rock(name, width, depth, height, seed):
    rng = random.Random(seed)
    n = 7
    angles = [math.tau * i / n + rng.uniform(-.12, .12) for i in range(n)]
    # Broad crown, sloped shoulders, buried foot; no sphere subdivision noise.
    verts = []
    for level, radius, z, cx, cy in [(0,.95,-.08,0,0),(1,1,.30,0,0),(2,.84,.76,-.10,.04),(3,.52,1,-.16,.02)]:
        for i, a in enumerate(angles):
            r = radius * (1 + rng.uniform(-.045,.045))
            verts.append((width * (math.cos(a)*r+cx)/2,
                          depth * (math.sin(a)*r+cy)/2,
                          height * (z + (rng.uniform(-.08,.08) if level in (1,2) else 0))))
    faces = [tuple(reversed(range(n))), tuple(3*n+i for i in range(n))]
    for level in range(3):
        for i in range(n):
            a=level*n+i; b=level*n+(i+1)%n; c=b+n; d=a+n
            # Explicit planar triangles define intentional large facets.
            faces.extend([(a,b,d),(b,c,d)])
    ob = mesh(name, verts, faces, ['#8C8D85','#8E8F87','#8A8B83'])
    return ob

def foliage(name, seed, count, spread, height, broad=False):
    rng=random.Random(seed); verts=[]; faces=[]; shades=[]
    pal = ['#6F8543','#829648','#94A654'] if not broad else ['#839B6E','#91A67C','#A3B58B']
    for i in range(count):
        a=i*2.399963 + rng.uniform(-.25,.25)
        length=spread*rng.uniform(.65,1.1)
        h=height*rng.uniform(.65,1.12)
        w=length*(.32 if broad else .14)
        base=Vector((math.cos(a)*.055,math.sin(a)*.055,0))
        d=Vector((math.cos(a),math.sin(a),0)); side=Vector((-d.y,d.x,0))
        first=base+d*length*.32+Vector((0,0,h*.60))
        second=base+d*length*.72+Vector((0,0,h*.93))
        tip=base+d*length+Vector((0,0,h*(.60 if broad else 1.05)))
        # Two sections give an arcing blade, with a restrained central crease.
        points=[base,first-side*w,first+Vector((0,0,w*.14)),first+side*w,
                second-side*w*.65,second+Vector((0,0,w*.10)),second+side*w*.65,tip,
                first-Vector((0,0,.012)),second-Vector((0,0,.012))]
        local=[(0,1,2),(0,2,3),(1,4,5,2),(2,5,6,3),(4,7,5),(5,7,6),
               (0,8,1),(0,3,8),(1,8,9,4),(8,3,6,9),(4,9,7),(9,6,7)]
        start=len(verts); verts.extend(points)
        faces.extend(tuple(start+j for j in f) for f in local)
        shades.extend([color(pal[i%len(pal)]) for j in range(len(local))])
    return mesh(name,verts,faces,pal,face_colors=shades)

assets={}
for args in [('Boulder_Broad',2.7,2.15,1.85,21),('Boulder_Low',1.52,1.22,1.03,47),
             ('Boulder_Long',2.4,1.45,1.10,73),('Pebble',.35,.28,.22,8)]:
    ob=rock(*args); assets[ob.name]=ob
for args in [('Grass_Fan',12,7,.62,.75,False),('Grass_Sparse',18,5,.42,.54,False),
             ('Grass_Tall',9,6,.48,.94,False),('Broadleaf_Rosette',24,7,.66,.34,True)]:
    ob=foliage(*args); assets[ob.name]=ob

def instance(name, at, angle=0, scale=1):
    ob=assets[name].copy(); ob.data=assets[name].data; study.objects.link(ob)
    ob.location=at; ob.rotation_euler.z=angle; ob.scale=(scale,)*3
    return ob

instance('Boulder_Broad',(-1.0,.35,0),.18)
instance('Boulder_Low',(.25,-1.15,0),-.4)
for x,y,sc,a,kind in [(-2.45,.3,.95,1,'Grass_Tall'),(-2.1,-.85,1,.4,'Grass_Fan'),
                     (-.80,-1.65,.95,.6,'Grass_Fan'),(.95,-.7,.80,2,'Grass_Fan'),
                     (.60,1.0,1.1,3,'Grass_Tall'),(1.60,-.25,.8,4,'Grass_Sparse'),
                     (-1.0,1.55,.9,3,'Grass_Sparse')]:
    instance(kind,(x,y,0),a,sc)
instance('Broadleaf_Rosette',(2.1,-1.25,.01),.3,.90)
instance('Broadleaf_Rosette',(-2.25,-1.65,.01),1,.60)
for x,y,sc in [(.8,-2,.9),(1.14,-2.24,.7),(.68,-2.38,.5)]:
    instance('Pebble',(x,y,0),x*5,sc)

# Reuse approved tree geometry, with a restrained palette for this lighting study.
with bpy.data.libraries.load(str(ROOT/'tools/blender/source/forest-lowpoly-astra-v1.blend'),link=False) as (src,dst):
    dst.objects=[n for n in src.objects if n in ('Forest_Hornbeam_Wood','Forest_Hornbeam_Canopy')]
for ob in dst.objects:
    if ob is None: continue
    study.objects.link(ob); ob.parent=None; ob.location=(.9,.85,0); ob.scale=(.78,)*3
    ob.hide_render=False; ob.hide_set(False); ob.hide_viewport=False
    ob.data=ob.data.copy(); ob.data.materials.clear(); ob.data.materials.append(material)
    attr=ob.data.color_attributes.get('Col')
    rng=random.Random(ob.name)
    foliage_tree='Canopy' in ob.name
    for p in ob.data.polygons:
        c=color('#7A8E46' if foliage_tree else '#826647')
        for li in p.loop_indices: attr.data[li].color=c

# Low shrubs use a few closed faceted lobes, not stacks of tiny leaves.
for i,(at,sc) in enumerate([((2.05,1.20,0),.65),((2.70,1.05,0),.45),((2.35,1.65,0),.48)]):
    ob=instance('Boulder_Broad',at,i*1.7,sc)
    ob.data=ob.data.copy(); ob.name='Shrub_Lobe_%d'%i
    for p in ob.data.polygons:
        for li in p.loop_indices: ob.data.color_attributes['Col'].data[li].color=color('#718541')

# A forked fallen twig assembled into one reusable low-cost mesh.
verts=[]; faces=[]
for a,b,r in [((0,0,.08),(.9,.22,.09),.045),((.35,.08,.08),(.50,.46,.07),.032),
              ((.75,.18,.08),(1.04,.05,.06),.025)]:
    a,b=Vector(a),Vector(b); d=(b-a).normalized(); u=d.cross(Vector((0,0,1))).normalized(); v=d.cross(u)
    start=len(verts)
    verts.extend(p+(u*math.cos(i*math.tau/5)+v*math.sin(i*math.tau/5))*r for p in (a,b) for i in range(5))
    faces.extend([tuple(start+i for i in reversed(range(5))),tuple(start+5+i for i in range(5))])
    faces.extend((start+i,start+(i+1)%5,start+(i+1)%5+5,start+i+5) for i in range(5))
assets['Twig_Fork']=mesh('Twig_Fork',verts,faces,['#795F3F','#89704C'])
instance('Twig_Fork',(1.3,-2.4,-.035),-.8)

# Review ground uses one continuous mesh, including the soil tint; no floating decal discs.
rng=random.Random(63); verts=[]; faces=[]; cols=[]; size=45; n=50
for y in range(n+1):
    for x in range(n+1):
        verts.append(((x/n-.5)*size+rng.uniform(-.16,.16),(y/n-.5)*size+rng.uniform(-.16,.16),0))
for y in range(n):
    for x in range(n):
        i=y*(n+1)+x
        for f in [(i,i+1,i+n+1),(i+1,i+n+2,i+n+1)]:
            faces.append(f)
            center=sum((Vector(verts[j]) for j in f),Vector())/3
            variation=noise.noise_vector(Vector((center.x*.3,center.y*.3,1))).x*.035
            soil=math.exp(-((center.x/3.1)**2+(center.y/2.6)**2)*1.4)*.55
            green=color('#87934D'); earth=color('#94834F')
            cols.append(tuple(max(0,min(1,green[c]*(1-soil)+earth[c]*soil+variation)) for c in range(3))+(1,))
mesh('Preview ground - tint not exported',verts,faces,['#87934D'],stage,cols)
instance('Boulder_Long',(-8,6,0),.5,.8)
instance('Boulder_Low',(7,8,0),2,.7)
library.hide_render=True; library.hide_viewport=True

world=bpy.data.worlds.new('Soft outdoor fill'); scene.world=world; world.use_nodes=True
background=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
background.inputs['Color'].default_value=(.65,.73,.84,1); background.inputs['Strength'].default_value=.6
sun_data=bpy.data.lights.new('Afternoon sun','SUN'); sun=bpy.data.objects.new('Afternoon sun',sun_data); stage.objects.link(sun)
sun.rotation_euler=(.45,-.5,-.6); sun_data.energy=2.2; sun_data.angle=.13
try: scene.render.engine='CYCLES'
except TypeError as e: raise RuntimeError(str(e))
scene.cycles.samples=32; scene.cycles.use_denoising=True
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.film_transparent=False
scene.view_settings.view_transform='AgX'
camera_data=bpy.data.cameras.new('Close view'); camera=bpy.data.objects.new('Close view',camera_data); stage.objects.link(camera)
camera_data.type='ORTHO'; scene.camera=camera

report={}
for name,ob in assets.items():
    bm=bmesh.new(); bm.from_mesh(ob.data)
    report[name]={'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),
                  'vertices':len(ob.data.vertices), 'manifold':all(e.is_manifold for e in bm.edges),
                  'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces),
                  'flat_shaded':not any(p.use_smooth for p in ob.data.polygons)}
    bm.free()
    assert report[name]['manifold'] and report[name]['degenerate_faces']==0, (name,report[name])
report['arrangement_triangles']=sum(sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in study.objects if ob.type=='MESH')
(OUT/'mesh-audit.json').write_text(json.dumps(report,indent=2))

def render(name,eye,target,scale,res):
    camera.location=eye; camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)

render('close-view',(9,-16,11),(0,0,2.0),10.0,(1500,1250))
render('gameplay-view',(11,-17,19),(0,0,1),18,(1300,1400))
camera.location=(9,-16,11); camera.rotation_euler=(Vector((0,0,2))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=10
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            view=area.spaces.active; view.region_3d.view_location=(0,0,2)
            view.region_3d.view_distance=13; view.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
            view.shading.color_type='VERTEX'; view.overlay.show_overlays=False
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'groundcover-reference-v1.blend'))
print('STUDY_COMPLETE',json.dumps(report))
