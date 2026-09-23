"""Bare starter lookout: posts, deck and ladder; no roof or bracing."""
import bpy
import bmesh
import json
import sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/watchtower-astra-lvl1-v2'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Watchtower_Level_1',{'wood':'#77553E','edge':'#A98054','plank':'#BE9A69','stone':'#909C97'})
k.root['footprint_xz']=[2.6,2.6]
k.group='Footings'
for x in [-.91,.91]:
    for y in [-.91,.91]:k.box('Footing',(x,y,.10),(.39,.39,.20),'stone',.04)
k.group='Posts'
for x in [-.91,.91]:
    for y in [-.91,.91]:k.box('Upright',(x,y,2.28),(.24,.24,4.36),'wood',.022)
k.group='Platform'
for x in [-.91,.91]:k.box('Deck_Joist',(x,0,4.39),(.20,2.20,.22),'wood',.018)
for i in range(10):k.box('Deck_Plank',(-1.03+i*.229,0,4.55),(.214,2.20,.12),'plank' if i%3 else 'edge',.012)
for y in [-1.08,1.08]:k.box('Deck_Edge',(0,y,4.43),(2.27,.12,.22),'edge',.018)
k.group='Ladder'
def ladder_y(z):return -1.22+.02*z
for x in [-.325,.325]:k.beam('Stile',(x,ladder_y(.05),.05),(x,ladder_y(4.65),4.65),.075,.085,'wood')
for i in range(16):
    z=.25+i*.278;k.rod('Rung',(-.325,ladder_y(z)-.014,z),(.325,ladder_y(z)-.014,z),.031,'edge',8)
modules=k.join_modules()
for name,p,role in [('Ladder_Bottom',(0,-1.23,.05),'climb start reference'),('Ladder_Top',(0,-1.10,4.61),'climb arrival reference'),('Lookout_Anchor',(0,0,4.61),'standing floor reference')]:k.marker(name,p,role)
report={};points=[];bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;points.extend(ob.matrix_world@v.co for v in ob.data.vertices);bm.free()
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<1.30 and max(abs(lo[1]),abs(hi[1]))<1.30
(OUT/'validation.json').write_text(json.dumps({'modules':report,'triangles':sum(d['triangles'] for d in report.values()),'bounds':[lo,hi],'floor_height':4.61},indent=2))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=str(OUT/'watchtower-lvl1.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Watchtower_Review',bpy.data.cameras.new('Watchtower_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),res=(1100,1400)):
    cam.location=eye;cam.rotation_euler=(Vector((0,0,2.30))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=6.25
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('front',(0,-14,7));render('opposite-three-quarter',(-8,-12,8));render('game-scale',res=(280,360));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.overlay.show_overlays=False;s.region_3d.view_location=(0,0,2.3);s.region_3d.view_distance=8;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/watchtower-astra-lvl1-v2.blend'))
# Independently reimport the export after saving the authored review scene.
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(OUT/'watchtower-lvl1.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
assert {o.name for o in meshes}=={'Footings','Posts','Platform','Ladder'}
for ob in meshes:
    bm=bmesh.new();bm.from_mesh(ob.data)
    assert all(e.is_manifold for e in bm.edges)
    assert all(f.calc_area()>1e-10 for f in bm.faces)
    assert not any(p.use_smooth for p in ob.data.polygons)
    assert len(ob.data.color_attributes)>0
    bm.free()
(OUT/'export-verification.json').write_text(json.dumps({'result':'PASS','meshes':[o.name for o in meshes],'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)},indent=2))
print('PASS',sum(d['triangles'] for d in report.values()))
