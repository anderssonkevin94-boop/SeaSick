"""Forest composition using approved trees and middle-weight groundcover only."""
import bpy
import math
import random
import json
from pathlib import Path
from mathutils import Vector, noise

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/forest-groundcover-mockup-v1'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art-staging/groundcover-midweight-v1/groundcover-midweight-v1.blend'))
scene=bpy.context.scene
study=bpy.data.collections['Reference arrangement']
library=bpy.data.collections['Reusable assets - ground pivots']
for ob in list(study.objects): bpy.data.objects.remove(ob,do_unlink=True)
ground=bpy.data.objects.get('Preview ground - tint not exported')
if ground: bpy.data.objects.remove(ground,do_unlink=True)
material=bpy.data.materials['Shared matte vertex palette']
rng=random.Random(228)

def color(h):
    def lin(c): return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    return tuple(lin(int(h[i:i+2],16)/255) for i in (1,3,5))+(1,)

def elevation(x,y): return .24*math.sin(x*.13)+.35*math.cos(y*.11)+.12*math.sin((x+y)*.19)
def path_x(y): return 2.6*math.sin(y*.16)+.035*y
def cleared(x,y): return abs(x-path_x(y))<2.35 or ((x+1)/6)**2+((y+5)/5)**2<1

species=['Hornbeam','Broadleaf','Slender','Leaning','Uneven','Young']
names=[f'Forest_{s}_{part}' for s in species for part in ['Wood','Canopy']]
with bpy.data.libraries.load(str(ROOT/'tools/blender/source/forest-lowpoly-astra-v1.blend'),link=False) as (src,dst):
    dst.objects=[n for n in src.objects if n in names]
trees={}
greens=['#7A8E46','#788D4B','#829649','#72874B','#87944D','#819A50']
for ob in dst.objects:
    if ob is None: continue
    name=next(s for s in species if ob.name.startswith('Forest_'+s+'_'))
    library.objects.link(ob); ob.parent=None; ob.hide_render=False; ob.hide_viewport=False; ob.hide_set(False)
    ob.data=ob.data.copy(); ob.data.materials.clear(); ob.data.materials.append(material)
    attr=ob.data.color_attributes['Col']
    c=color(greens[species.index(name)] if 'Canopy' in ob.name else '#826647')
    for item in attr.data: item.color=c
    trees.setdefault(name,[]).append(ob)
assert all(len(trees[s])==2 for s in species)

instances={}; tree_positions=[]
def copy(src,x,y,angle,scale):
    ob=src.copy(); ob.data=src.data; study.objects.link(ob)
    ob.parent=None; ob.location=(x,y,elevation(x,y)); ob.rotation_euler=(0,0,angle); ob.scale=(scale,)*3
    ob.hide_render=False; ob.hide_viewport=False; ob.hide_set(False)
    return ob

def place(name,x,y,scale=1,angle=None):
    instances[name]=instances.get(name,0)+1
    return copy(library.objects[name],x,y,rng.random()*math.tau if angle is None else angle,scale)

# Jittered groves, with an open meandering corridor and a larger foreground clearing.
for y0 in range(-12,35,5):
    for x0 in range(-22,24,5):
        x=x0+rng.uniform(-1.7,1.7); y=y0+rng.uniform(-1.8,1.8)
        if cleared(x,y) or rng.random()<.20: continue
        if any((x-a)**2+(y-b)**2<3.4**2 for a,b in tree_positions): continue
        s=rng.choices(species,[4,3,1,1,2,2])[0]
        scale=rng.uniform(.70,1.06) if s!='Young' else rng.uniform(.9,1.12)
        angle=rng.random()*math.tau
        for src in trees[s]: copy(src,x,y,angle,scale)
        instances['Tree_'+s]=instances.get('Tree_'+s,0)+1
        tree_positions.append((x,y))

rock_positions=[(-6.1,-4.8),(6.9,1.5),(-8.7,8.9),(8,13.8),(-3.8,24),(15.5,-7),(-17,2),(16,27)]
for index,(x,y) in enumerate(rock_positions):
    place('Boulder_Broad' if index%3 else 'Boulder_Long',x,y,rng.uniform(.95,1.35))
    place('Boulder_Low',x+1.35,y-.90,rng.uniform(.65,.95))
    for j in range(4):
        a=rng.random()*math.tau; d=rng.uniform(1.2,2.0)
        place('Grass_Fan' if j%2 else 'Grass_Tall',x+math.cos(a)*d,y+math.sin(a)*d,rng.uniform(.75,1.05))
    place('Broadleaf_Rosette',x-1.0,y-1.75,rng.uniform(.75,1.05))
    for j in range(2): place('Pebble',x+rng.uniform(.6,1.4),y-rng.uniform(1.3,1.7),rng.uniform(.7,1.1))

for i,(x,y) in enumerate(tree_positions):
    # Not every trunk gets the same decorative skirt.
    if i%3==0:
        for j in range(2):
            a=rng.random()*math.tau; d=rng.uniform(.45,.85)
            place('Grass_Sparse',x+math.cos(a)*d,y+math.sin(a)*d,rng.uniform(.7,1.1))
    if i%8==0:
        place('Broadleaf_Rosette',x+.8,y-.6,.8)
    if i%11==0:
        ob=place('Twig_Fork',x-.9,y-1.3,1); ob.location.z-=.035
    if i%9==0:
        # Reuse the existing shrub lobe shape, with one shared green mesh.
        if 'Shrub_Master' not in bpy.data.objects:
            src=library.objects['Boulder_Broad']; shrub=src.copy(); shrub.data=src.data.copy()
            shrub.name='Shrub_Master'; library.objects.link(shrub)
            for item in shrub.data.color_attributes['Col'].data: item.color=color('#718541')
        place('Shrub_Master',x+1.1,y+.5,.48)
        place('Shrub_Master',x+1.7,y+.8,.33)

# Continuous preview-only terrain: the winding opening is a tint, not a raised path strip.
verts=[]; faces=[]; cols=[]; n=110; span=150
gr=color('#87934D'); soil=color('#A69A67')
for y in range(n+1):
    for x in range(n+1):
        px=(x/n-.5)*span+rng.uniform(-.20,.20)
        py=(y/n-.5)*span+rng.uniform(-.20,.20)
        verts.append((px,py,elevation(px,py)-.025))
for y in range(n):
    for x in range(n):
        i=y*(n+1)+x
        for f in [(i,i+1,i+n+1),(i+1,i+n+2,i+n+1)]:
            faces.append(f); p=sum((Vector(verts[j]) for j in f),Vector())/3
            v=noise.noise_vector(Vector((p.x*.19,p.y*.19,1))).x*.017
            path=math.exp(-((p.x-path_x(p.y))/1.8)**4)*.55
            patch=math.exp(-(((p.x+1)/5)**2+((p.y+5)/4)**2))*.28
            mix=max(path,patch)
            cols.append(tuple(max(0,gr[k]*(1-mix)+soil[k]*mix+v) for k in range(3))+(1,))
data=bpy.data.meshes.new('Preview rolling terrain'); data.from_pydata(verts,[],faces); data.update()
ob=bpy.data.objects.new('Preview terrain - not a game asset',data); scene.collection.objects.link(ob)
data.materials.append(material); attr=data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
for p in data.polygons:
    for i in p.loop_indices: attr.data[i].color=cols[p.index]

cam=scene.camera; scene.cycles.samples=32
def render(name,eye,target,scale,res):
    cam.location=eye; cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale; scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.filepath=str(OUT/(name+'.png')); bpy.ops.render.render(write_still=True)

report={'instances':instances,'trees':len(tree_positions),
        'arrangement_triangles':sum(sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in study.objects if ob.type=='MESH'),
        'preview_terrain_triangles':len(faces),'unity_imported':False}
(OUT/'scene-audit.json').write_text(json.dumps(report,indent=2))
render('forest-overview',(27,-38,43),(0,8,1),57,(1800,1400))
render('forest-phone',(15,-28,36),(0,4,1),44,(1080,1600))
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            view=area.spaces.active; view.region_3d.view_location=(0,4,1)
            view.region_3d.view_distance=48; view.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'forest-groundcover-mockup-v1.blend'))
print('FOREST_COMPLETE',json.dumps(report))
