"""Uninhabited-island nature kit; staging only, no Unity changes."""
import bpy
import bmesh
import math
import random
import json
from pathlib import Path
from mathutils import Vector, noise

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-staging/island-nature-kit-v1'
OUT.mkdir(parents=True, exist_ok=True)
(OUT / 'fbx').mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
library = bpy.data.collections.new('Assets - ground pivots - export only')
display = bpy.data.collections.new('Nature arrangement - preview only')
stage = bpy.data.collections.new('Lighting and ground - preview only')
for c in (library, display, stage): scene.collection.children.link(c)

def color(h):
    def linear(v): return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4
    return tuple(linear(int(h[i:i+2],16)/255) for i in (1,3,5))+(1,)

mat = bpy.data.materials.new('Nature_Shared_VertexColor')
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = .93
bsdf.inputs['Specular IOR Level'].default_value = .15
vc = mat.node_tree.nodes.new('ShaderNodeVertexColor'); vc.layer_name = 'Col'
mat.node_tree.links.new(vc.outputs['Color'],bsdf.inputs['Base Color'])

def mesh(name, verts, faces, shades, collection=library):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces); data.update()
    bm=bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    ob=bpy.data.objects.new(name,data); collection.objects.link(ob)
    data.materials.append(mat)
    attr=data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in data.polygons:
        p.use_smooth=False
        # Colour follows form rather than independent random triangle noise.
        shade=shades[0] if len(shades)==1 else shades[1 if p.normal.z>.45 else 0]
        for li in p.loop_indices: attr.data[li].color=color(shade)
    return ob

def rock_geometry(width,depth,height,seed,n=7):
    rng=random.Random(seed); verts=[]; faces=[]
    angles=[math.tau*i/n+rng.uniform(-.12,.12) for i in range(n)]
    for radius,z,cx,cy in [( .88,-.06,0,0),(1,.25,0,0),(.79,.77,-.09,.04),(.48,1,-.17,.04)]:
        for a in angles:
            r=radius*rng.uniform(.94,1.06)
            verts.append((width*(math.cos(a)*r+cx)/2,depth*(math.sin(a)*r+cy)/2,
                          height*(z+rng.uniform(-.055,.055))))
    faces=[tuple(reversed(range(n))),tuple(3*n+i for i in range(n))]
    for j in range(3):
        for i in range(n): faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    return verts,faces

def append_geometry(verts,faces,v,f,at=(0,0,0),angle=0):
    k=len(verts); c,s=math.cos(angle),math.sin(angle)
    verts.extend((x*c-y*s+at[0],x*s+y*c+at[1],z+at[2]) for x,y,z in v)
    faces.extend(tuple(k+i for i in face) for face in f)

assets={}
for name,w,d,h,seed in [('Cliff_LowLedge',4.6,2.2,1.15,130),('Cliff_BrokenSlab',3.15,1.12,.68,84)]:
    v,f=rock_geometry(w,d,h,seed)
    assets[name]=mesh(name,v,f,['#8A8B83','#8E8F87'])

v=[]; f=[]
for w,d,h,seed,at,a in [(1.5,1.1,.72,39,(-.65,.13,0),-.3),
                        (1.05,.79,.53,77,(.67,.15,0),.4),
                        (.61,.57,.29,52,(.12,-.65,0),.9)]:
    rv,rf=rock_geometry(w,d,h,seed,n=5); append_geometry(v,f,rv,rf,at,a)
assets['Cliff_TalusCluster']=mesh('Cliff_TalusCluster',v,f,['#8A8B83','#94958C'])

v=[]; f=[]
for w,d,h,seed,at,a in [(.69,.52,.24,32,(-.38,.06,0),.3),
                       (.48,.37,.17,12,(.25,.28,0),-.6),
                       (.29,.25,.12,60,(.14,-.31,0),.9),
                       (.23,.2,.10,19,(.56,-.18,0),1)]:
    rv,rf=rock_geometry(w,d,h,seed,n=5); append_geometry(v,f,rv,rf,at,a)
assets['Shore_WashedStones']=mesh('Shore_WashedStones',v,f,['#93978E','#A1A398'])

def leaf(v,f,base,angle,length,height,width,lobed=False):
    d=Vector((math.cos(angle),math.sin(angle),0)); side=Vector((-d.y,d.x,0)); base=Vector(base)
    # Closed diamond cross-sections. Fern silhouette has broad paired lobes, not leaflets.
    ts=[.21,.37,.52,.67,.80] if lobed else [.34,.72]
    widths=[.66,1,.65,.83,.43] if lobed else [1,.64]
    start=len(v); v.append(base)
    for t,w in zip(ts,widths):
        center=base+d*(length*t)+Vector((0,0,height*math.sin(t*math.pi*.85)))
        v.extend([center-side*width*w,center+Vector((0,0,width*.15)),
                  center+side*width*w,center-Vector((0,0,.012))])
    tip=len(v); v.append(base+d*length+Vector((0,0,height*.34)))
    for j in range(4): f.append((start,start+1+(j+1)%4,start+1+j))
    for row in range(len(ts)-1):
        a=start+1+row*4; b=a+4
        for j in range(4): f.append((a+j,a+(j+1)%4,b+(j+1)%4,b+j))
    a=start+1+(len(ts)-1)*4
    for j in range(4): f.append((a+j,a+(j+1)%4,tip))

v=[]; f=[]
for i in range(5):
    leaf(v,f,(.065*math.cos(i*2.4),.065*math.sin(i*2.4),-.015),i*2.4+.2,
         .48+(i%3)*.07,.42+(i%2)*.19,.038+(i%2)*.012)
assets['Coast_DuneGrass']=mesh('Coast_DuneGrass',v,f,['#929D55','#A5AF65'])

# One continuous wind-shaped crown, not intersecting ball primitives.
v=[]; f=[]; n=9
for ring,z in enumerate([-.04,.22,.52,.77]):
    for i in range(n):
        a=math.tau*i/n
        r=[.44,1,.82,.34][ring]*(1+.13*math.sin(i*2.7+ring*.5))
        v.append((math.cos(a)*r*.9+ring*.065,math.sin(a)*r*.58,
                  z+(.065*math.sin(a*3+.5) if ring>0 else 0)))
f=[tuple(reversed(range(n))),tuple(3*n+i for i in range(n))]
for j in range(3):
    for i in range(n): f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
assets['Coast_WindScrub']=mesh('Coast_WindScrub',v,f,['#718541','#829450'])

# Branch grows out of an open side face: one connected watertight fork.
v=[]; f=[]; n=6
for j,(x,y,r) in enumerate([(-1.2,-.09,.06),(-.3,0,.14),(.16,.02,.12),(.70,-.16,.085),(1.20,-.23,.025)]):
    for i in range(n):
        a=math.tau*i/n-math.pi/6
        v.append((x+.02*math.sin(i*3+j),y+math.cos(a)*r,.16+math.sin(a)*r))
f=[tuple(reversed(range(n))),tuple(4*n+i for i in range(n))]
for j in range(4):
    for i in range(n):
        if j==1 and i==0: continue
        f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
port=[6,7,13,12]
center=sum((Vector(v[i]) for i in port),Vector())/4
for at,scale in [((.19,.47,.18),.55),((.60,.91,.13),.20)]:
    ring=[]
    for index in [6,7,13,12]:
        ring.append(len(v)); v.append(Vector(at)+(Vector(v[index])-center)*scale)
    for i in range(4): f.append((port[i],port[(i+1)%4],ring[(i+1)%4],ring[i]))
    port=ring
f.append(tuple(port))
assets['Shore_ForkedDriftwood']=mesh('Shore_ForkedDriftwood',v,f,['#93826A','#AC9A7C'])

v=[]; f=[]
for i in range(5): leaf(v,f,(0,0,-.012),i*math.tau/5+.25,.66+(i%2)*.12,.39+(i%3)*.06,.16,True)
assets['Forest_Bracken']=mesh('Forest_Bracken',v,f,['#65854D','#809B60'])

v=[]; f=[]; n=7
for j,(x,y,r) in enumerate([(-1.7,-.10,.31),(-1.0,-.03,.39),(.25,.13,.33),(1.38,.33,.21)]):
    for i in range(n):
        a=math.tau*i/n
        v.append((x+(.16*math.sin(i*4) if j in (0,3) else 0),y+math.cos(a)*r,.38+math.sin(a)*r))
f=[tuple(reversed(range(n))),tuple(3*n+i for i in range(n))]
for j in range(3):
    for i in range(n): f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
ob=mesh('Forest_FallenTrunk',v,f,['#756047','#89704E']); assets[ob.name]=ob
for p in ob.data.polygons:
    if abs(p.normal.x)>.8:
        for li in p.loop_indices: ob.data.color_attributes['Col'].data[li].color=color('#B19A70')
    elif p.normal.y<-.3:
        for li in p.loop_indices: ob.data.color_attributes['Col'].data[li].color=color('#69543C')

report={}
for name,ob in assets.items():
    bm=bmesh.new(); bm.from_mesh(ob.data)
    row={'triangles':len(ob.data.polygons),'vertices':len(ob.data.vertices),
         'closed_manifold':all(e.is_manifold for e in bm.edges),
         'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces),
         'flat_shaded':not any(p.use_smooth for p in ob.data.polygons),
         'dimensions_metres':[round(x,3) for x in ob.dimensions],
         'material_slots':len(ob.data.materials)}
    bm.free()
    assert row['closed_manifold'] and row['degenerate_faces']==0,(name,row)
    report[name]=row
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
    bpy.ops.export_scene.fbx(filepath=str(OUT/'fbx'/(name+'.fbx')),use_selection=True,
        object_types={'MESH'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',
        bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=False,mesh_smooth_type='FACE',
        colors_type='LINEAR')
(OUT/'mesh-audit.json').write_text(json.dumps(report,indent=2))

def instance(name,at,scale=1,angle=0):
    ob=assets[name].copy(); ob.data=assets[name].data; display.objects.link(ob)
    ob.location=at; ob.scale=(scale,)*3; ob.rotation_euler.z=angle
    return ob

# A compact untouched woodland/coastal-edge vignette; no roads or construction clearing.
instance('Cliff_LowLedge',(-1.5,1.4,0),1,.2)
instance('Cliff_BrokenSlab',(-.8,.1,0),1,-.3)
instance('Cliff_TalusCluster',(-2.1,-.9,0),1,.25)
instance('Coast_WindScrub',(1.3,1.0,0),1.3,.3)
instance('Forest_FallenTrunk',(2.2,-.85,0),1,-.45)
instance('Shore_ForkedDriftwood',(-.2,-2.6,0),1,.15)
instance('Shore_WashedStones',(-2,-2.3,0),1,.25)
for x,y,s,a in [(-3.1,.0,1,.2),(-2.9,1.5,1.2,.8),(.2,-.7,.85,1),(.8,-2,.8,.4),(3.8,-.5,.9,2)]:
    instance('Coast_DuneGrass',(x,y,0),s,a)
instance('Forest_Bracken',(.1,1.0,0),1,.3)
instance('Forest_Bracken',(2.7,.25,0),.75,1)

with bpy.data.libraries.load(str(ROOT/'tools/blender/source/forest-lowpoly-astra-v2.blend'),link=False) as (src,dst):
    dst.objects=[n for n in src.objects if n in ('Forest_Hornbeam_Wood','Forest_Hornbeam_Canopy')]
for ob in dst.objects:
    if ob is None: continue
    display.objects.link(ob); ob.parent=None; ob.location=(.4,2.5,0); ob.scale=(.72,)*3
    ob.hide_render=False; ob.hide_viewport=False; ob.hide_set(False)
    ob.data=ob.data.copy(); ob.data.materials.clear(); ob.data.materials.append(mat)
    for p in ob.data.polygons:
        for li in p.loop_indices: ob.data.color_attributes['Col'].data[li].color=color('#7A8E46' if 'Canopy' in ob.name else '#826647')

v=[]; f=[]; grid=50; size=40
for y in range(grid+1):
    for x in range(grid+1): v.append(((x/grid-.5)*size,(y/grid-.5)*size,-.065))
for y in range(grid):
    for x in range(grid):
        i=y*(grid+1)+x; f.extend([(i,i+1,i+grid+1),(i+1,i+grid+2,i+grid+1)])
ground=mesh('Preview ground - not exported',v,f,['#909C51'],stage)
for p in ground.data.polygons:
    for li in p.loop_indices:
        co=ground.data.vertices[ground.data.loops[li].vertex_index].co
        n=noise.noise_vector(Vector((co.x*.23,co.y*.23,4))).x
        t=max(0,min(1,math.exp(-((co.x/4)**2+(co.y/3)**2))*.75+n*.4))
        a=color('#909C51'); b=color('#9C8058')
        ground.data.color_attributes['Col'].data[li].color=tuple(a[i]*(1-t)+b[i]*t for i in range(4))

world=bpy.data.worlds.new('Soft outdoor fill'); scene.world=world; world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.65,.73,.84,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.6
sun=bpy.data.objects.new('Afternoon sun',bpy.data.lights.new('Afternoon sun','SUN')); stage.objects.link(sun)
sun.rotation_euler=(.45,-.5,-.6); sun.data.energy=2.2; sun.data.angle=.13
camera=bpy.data.objects.new('Nature kit camera',bpy.data.cameras.new('Nature kit camera')); stage.objects.link(camera)
camera.data.type='ORTHO'; scene.camera=camera
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
scene.view_settings.view_transform='AgX'; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
library.hide_render=True; library.hide_viewport=True

def render(name,eye,target,scale,w,h):
    camera.location=eye; camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale; scene.render.resolution_x=w; scene.render.resolution_y=h
    scene.render.filepath=str(OUT/(name+'.png')); bpy.ops.render.render(write_still=True)

render('nature-close',(10,-16,13),(0,0,1.65),12,1500,1250)
render('nature-gameplay',(9,-13,18),(0,0,1),19,1100,1400)
display.hide_render=True
lineup=bpy.data.collections.new('Nine-piece lineup - preview only'); scene.collection.children.link(lineup)
for i,(name,source) in enumerate(assets.items()):
    ob=source.copy(); ob.data=source.data; lineup.objects.link(ob)
    ob.location=((i%3-1)*5.4,(1-i//3)*3.8,0)
render('kit-lineup',(3,-15,20),(0,0,.3),18,1800,1450)
lineup.hide_render=True; lineup.hide_viewport=True; display.hide_render=False
camera.location=(10,-16,13); camera.rotation_euler=(Vector((0,0,1.65))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=12
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            view=area.spaces.active; view.region_3d.view_location=(0,0,1.5)
            view.region_3d.view_distance=15; view.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
            view.shading.color_type='VERTEX'; view.overlay.show_overlays=False
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'island-nature-kit-v1.blend'))

# Round-trip every exported mesh in a fresh scene, leaving the saved review intact.
bpy.ops.wm.read_factory_settings(use_empty=True)
checks={}
for name,expected in report.items():
    bpy.ops.import_scene.fbx(filepath=str(OUT/'fbx'/(name+'.fbx')),colors_type='LINEAR')
    objects=[ob for ob in bpy.context.selected_objects if ob.type=='MESH']
    count=sum(sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in objects)
    valid=count==expected['triangles'] and all(ob.data.color_attributes.get('Col') for ob in objects)
    checks[name]={'triangles':count,'vertex_colors':True if valid else False,'passed':bool(valid)}
    assert valid,(name,checks[name])
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob,do_unlink=True)
(OUT/'export-verification.json').write_text(json.dumps(checks,indent=2))
print('NATURE_KIT_COMPLETE',json.dumps(report))
