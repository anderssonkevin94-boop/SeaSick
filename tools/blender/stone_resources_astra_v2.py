"""Six deliberately simple, flat-shaded stone resources. Staging only."""
import bpy
import bmesh
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import steamer as ss
from staged_building_kit import Kit

OUT = HERE.parents[1] / 'art-staging/stone-resources-astra-v2'
OUT.mkdir(parents=True, exist_ok=True)
ss.clear_scene()  # This generator runs in its own factory-startup process.
COLORS = {'stone': '#92999A', 'top': '#B0B4AD', 'side': '#858E94', 'cut': '#BABCB2'}

# x, y, half-width, half-depth, height, angle, shoulder fraction, top offset.
PROFILES = {
    'Stone_Field': [(0, 0, 1.05, .85, 1.12, .12, .55, -.18)],
    'Stone_Broad': [(0, 0, 1.65, 1.02, 1.35, -.20, .65, .23)],
    'Stone_Split': [(-.72, 0, .70, .86, 1.75, -.10, .55, -.18),
                    (.74, .12, .58, .75, 1.12, .25, .68, .12)],
    'Stone_Upright': [(0, .08, .86, .69, 2.15, -.12, .76, -.17),
                      (1.05, -.35, .38, .40, .54, .30, .54, .06)],
    'Stone_LowRidge': [(-1.22, .08, .68, .78, .85, .14, .65, -.12),
                       (.10, .08, .66, .87, 1.22, -.10, .67, .10),
                       (1.30, -.10, .47, .61, .62, .15, .61, .04)],
    'Stone_Outcrop': [(-.58, .45, .93, .80, 2.05, -.12, .63, -.16),
                      (.95, .10, .62, .76, 1.40, .13, .63, .14),
                      (-.53, -.98, .66, .48, .75, -.20, .60, -.05)],
}
# A clipped, uneven octagonal footprint; the crown is offset, never a sphere.
OUTLINE = [(-.76,-.83),(.20,-1),(.88,-.60),(1,.20),(.56,.89),(-.19,1),(-.94,.53),(-1,-.24)]
reports, assets = {}, {}
shared = None

def rock(k, name, shape, rubble=False):
    x,y,rx,ry,h,angle,shoulder,offset = shape
    # Uneven ellipsoidal volume: no horizontal shoulder ring or repeated flat cap.
    seed=bmesh.new();bmesh.ops.create_icosphere(seed,subdivisions=2,radius=1)
    verts=[]
    rotation=__import__('mathutils').Euler((.27+offset,.18+shoulder*.3,angle*3)).to_matrix()
    for vertex in seed.verts:
        u,v,w=rotation@vertex.co
        bulge=1+.12*math.sin(2.8*u+1.3*v+angle*7)+.075*math.cos(4*v-2*w)
        xx=rx*(u*bulge + .18*w*w + offset*w)
        yy=ry*(v*(1+.10*u-.09*w)+.09*w)
        zz=max(-.07,h*(w+.57)/1.57 + .10*h*u-.045*h*v)
        if rubble:zz=-.07 if zz<=-.069 else .035+(zz/h)*.28
        verts.append((x+xx*math.cos(angle)-yy*math.sin(angle),
                      y+xx*math.sin(angle)+yy*math.cos(angle),zz))
    seed.free()
    bm=bmesh.new()
    for co in verts:bm.verts.new(co)
    result=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused:bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.dissolve_limit(bm,angle_limit=.015,verts=list(bm.verts),edges=list(bm.edges))
    bm.verts.ensure_lookup_table();bm.verts.index_update()
    verts=[tuple(v.co) for v in bm.verts];faces=[tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    ob = k.mesh(name, verts, faces, 'stone')
    attr = ob.data.color_attributes['Col']
    for p in ob.data.polygons:
        # Same broad face color after triangulation; no noisy per-triangle confetti.
        key = 'cut' if rubble and p.normal.z>.6 else 'top' if p.normal.z>.6 else 'side' if p.normal.y>.5 else 'stone'
        c = ss.srgb_to_linear(ss._hex(COLORS[key]))
        for index in p.loop_indices: attr.data[index].color = (*c,1)
    return ob

def validate(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    assert all(e.is_manifold for e in bm.edges), ob.name
    assert all(f.calc_area()>1e-9 for f in bm.faces), ob.name
    assert bm.calc_volume(signed=True)>0, ob.name
    triangles = sum(len(f.verts)-2 for f in bm.faces); bm.free()
    assert not any(p.use_smooth for p in ob.data.polygons)
    assert 'Col' in ob.data.color_attributes
    return triangles

for name, shapes in PROFILES.items():
    for rubble in [False, True]:
        key = name + ('_Depleted' if rubble else '')
        k = Kit(key, COLORS)
        if shared is None: shared = k.mat; shared.name = 'Stone_Shared_VertexColor'
        k.mat = shared
        for i,shape in enumerate(shapes): rock(k, key+'_'+str(i), shape, rubble)
        ob = k.join_modules()['Structure']; ob.name = key+'__Mesh'
        # Geometry is authored around the ground pivot; joining must not displace it.
        assert ob.location.length < 1e-6
        markers = [k.marker(key+'__Ground',(0,0,0),'Ground center')]
        if not rubble:
            front = min(v.co.y for v in ob.data.vertices)
            markers += [k.marker(key+'__Mine_Target',(0,front,.45),'Pickaxe contact reference, not a navigation destination')]
        tri = validate(ob)
        assert tri <= 240
        bounds = [[min(v.co[i] for v in ob.data.vertices) for i in range(3)],
                  [max(v.co[i] for v in ob.data.vertices) for i in range(3)]]
        reports[key] = {'triangles':tri,'bounds_m':bounds,'mesh_objects':1,'material_slots':1}
        bpy.ops.object.select_all(action='DESELECT')
        for item in [k.root,ob]+markers: item.select_set(True)
        bpy.context.view_layer.objects.active=k.root
        bpy.ops.export_scene.fbx(filepath=str(OUT/(key+'.fbx')),use_selection=True,
            object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,
            use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
        assets[key]=(k.root,ob)
        ob.hide_render=True; ob.hide_set(True)

(OUT/'validation.json').write_text(json.dumps(reports,indent=2))
scene=bpy.context.scene; ss.setup_render(scene)
scene.world.color=ss.srgb_to_linear(ss._hex('#B7C8CC'))
scene.display.shading.show_cavity=False
scene.view_settings.exposure=.35
cam=bpy.data.objects.new('Stone_Review',bpy.data.cameras.new('Stone_Review'))
scene.collection.objects.link(cam);scene.camera=cam
cam.data.type=ss.pick(ss.enum_ids(cam.data.bl_rna.properties['type']),'ORTHO')

def render(filename, eye, target, span, resolution):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=span
    scene.render.resolution_x,scene.render.resolution_y=resolution;scene.render.resolution_percentage=100
    scene.render.filepath=str(OUT/(filename+'.png'));bpy.ops.render.render(write_still=True)

names=list(PROFILES)
for i,name in enumerate(names):
    root,ob=assets[name];root.location=((i%3-1)*5.2,(i//3)*5.0,0);ob.hide_render=False;ob.hide_set(False)
render('family-lineup',(9,-21,20),(0,2.3,.7),18,(1500,1000))
render('game-scale',(9,-21,20),(0,2.3,.7),18,(600,400))
for name in names:
    assets[name][1].hide_render=True
for i,name in enumerate(names):
    root,ob=assets[name+'_Depleted'];root.location=((i%3-1)*5.2,(i//3)*5.0,0);ob.hide_render=False
render('depleted-lineup',(9,-21,20),(0,2.3,.2),18,(1500,1000))
for root,ob in assets.values():ob.hide_render=True
for name in names:
    root,ob=assets[name]; old=root.location.copy();root.location=(0,0,0);ob.hide_render=False
    render(name,(6,-10,7),(0,0,.9),5.3,(800,700))
    ob.hide_render=True;root.location=old
for name in names: assets[name][1].hide_render=False
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type=ss.pick(ss.enum_ids(v.shading.bl_rna.properties['color_type']),'VERTEX')
            v.overlay.show_overlays=False;v.region_3d.view_location=(0,2.3,.7)
            v.region_3d.view_distance=20
            v.region_3d.view_rotation=(Vector((0,2.3,.7))-Vector((9,-21,20))).to_track_quat('-Z','Y')
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/stone-resources-astra-v2.blend'))

# Round-trip the actual delivery files, not merely their source meshes.
verified={}
for name,report in reports.items():
    ss.clear_scene()
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert len(meshes)==1
    tri=validate(meshes[0]);assert tri==report['triangles']
    assert name+'__Ground' in bpy.context.scene.objects
    if not name.endswith('_Depleted'):assert name+'__Mine_Target' in bpy.context.scene.objects
    verified[name]={'result':'PASS','triangles':tri}
(OUT/'export-verification.json').write_text(json.dumps(verified,indent=2))
print('STONE EXPORTS PASS',json.dumps(verified))
