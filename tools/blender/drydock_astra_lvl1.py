"""Empty low-poly refit slip, staged outside Unity."""
import sys, math, json
from pathlib import Path
import bpy, bmesh
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/drydock-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Drydock_Level_1',{'wood':'#78583E','plank':'#B28B59','light':'#C4A171','teal':'#648D86','iron':'#39464C','rope':'#BEAF87','canvas':'#DDD1AA','patch':'#AE8A62','stone':'#858A82'})
# Broad walkways flank an unobstructed slip; the sea end stays completely open.
k.group='Walkways'
for side in [-1,1]:
    x=side*3.55
    for y in [-5,-2.5,0,2.5,5]:
        k.box('Piling',(x,y,.52),(.38,.40,1.04),'wood',.04)
        k.box('Cap',(x,y,1.06),(.50,.52,.12),'teal',.025)
    for dx in [-.58,.58]:k.box('Stringer',(x+dx,0,.70),(.20,10.8,.28),'wood',.025)
    for i in range(22):
        y=-5.25+i*.50
        k.box('Broad_Deck_Plank',(x+.035*math.sin(i*2.1),y,.90),(1.72+.08*math.sin(i),.475,.18),'light' if i%4==0 else 'plank',.015)
    for y in [-4.7,4.65]:
        k.box('Mooring_Post',(x+side*.54,y,1.25),(.25,.27,.64),'wood',.025)
        k.box('Mooring_Cap',(x+side*.54,y,1.58),(.34,.35,.10),'iron',.01)
for i in range(12):
    k.box('Land_End_Plank',(-2.75+i*.50,5.40,.90),(.48,1.50,.18),'plank' if i%3 else 'light',.015)
k.group='Ship_Cradle'
for x in [-1.55,1.55]:k.box('Slip_Runner',(x,0,.13),(.30,10.9,.26),'wood',.02)
for y in [-4,-2,0,2,4]:
    k.box('Cradle_Crossbeam',(0,y,.30),(5.2,.34,.30),'wood',.025)
    k.box('Keel_Block',(0,y,.56),(.72,.60,.26),'light',.025)
    for s in [-1,1]:
        k.beam('Hull_Prop',(s*2.15,y,.44),(s*1.72,y,1.00),.22,.28,'wood')
        k.beam('Hull_Pad',(s*1.55,y,1.04),(s*1.98,y,.84),.36,.42,'teal')
k.group='Lifting_Gantry'
for s in [-1,1]:
    x=s*3.1
    k.box('Gantry_Post',(x,2.4,2.58),(.36,.40,3.20),'wood',.03)
    for z in [1.18,3.91]:k.box('Iron_Collar',(x,2.4,z),(.39,.43,.19),'iron',.01)
    k.beam('Knee_Brace',(x,2.4,3.20),(s*2.23,2.4,4.02),.21,.23,'wood')
k.box('Gantry_Head',(0,2.4,4.17),(7.25,.48,.40),'wood',.035)
k.box('Painted_Fascia',(0,2.145,4.18),(6.6,.055,.17),'teal',.005)
k.box('Pulley_Block',(0,2.4,3.68),(.40,.32,.56),'wood',.04)
k.rod('Pulley_Axle',(0,2.16,3.69),(0,2.64,3.69),.12,'iron',8)
k.rod('Lifting_Line',(0,2.4,3.40),(0,2.4,2.20),.028,'rope',6)
hook=[(0,2.4,2.22),(0,2.4,1.99),(.16,2.4,1.89),(.30,2.4,2.02),(.30,2.4,2.13)]
for a,b in zip(hook,hook[1:]):k.rod('Hook',a,b,.052,'iron',6)
k.rod('Return_Line',(.18,2.4,3.77),(2.85,2.4,1.72),.024,'rope',5)
k.rod('Winch_Axle',(2.82,2.05,1.60),(2.82,2.75,1.60),.15,'wood',8)
for y in [2.10,2.70]:k.box('Winch_Cheek',(2.82,y,1.40),(.28,.12,.70),'iron',.012)
k.rod('Crank',(2.82,2.87,1.60),(2.82,2.87,1.95),.045,'iron',6)
k.rod('Crank_Grip',(2.82,2.87,1.95),(2.82,3.10,1.95),.06,'wood',6)
k.group='Work_Shelter'
for x in [-4.24,-2.83]:
    for y in [-1.9,.10]:k.box('Shelter_Post',(x,y,2.03),(.14,.14,2.08),'wood',.015)
    k.beam('Eave',(x,-2.10,3.08),(x,.30,3.08),.12,.13,'wood')
for y in [-1.9,.10]:
    for x in [-4.24,-2.83]:k.beam('Rafter',(x,y,3.08),(-3.535,y,3.48),.10,.12,'wood')
k.beam('Ridge',(-3.535,-2.15,3.48),(-3.535,.35,3.48),.12,.12,'wood')
verts=[]
for dz in [0,-.025]:
    for y in [-2.17,.37]:
        for x,z in [(-4.46,3.08),(-3.535,3.54),(-2.61,3.08)]:verts.append((x,y,z+dz))
k.mesh('Canvas',verts,[(0,1,4,3),(1,2,5,4),(6,9,10,7),(7,10,11,8),(0,6,7,1),(1,7,8,2),(3,4,10,9),(4,5,11,10),(0,3,9,6),(2,8,11,5)],'canvas')
for x in [-4.04,-3.03]:
    for y in [-1.6,-.45]:k.box('Bench_Leg',(x,y,1.37),(.12,.12,.76),'wood',.01)
k.box('Worktop',(-3.535,-1.025,1.80),(1.3,1.55,.12),'plank',.02)
k.box('Plan_Sheet',(-3.55,-1.10,1.872),(.67,.67,.014),'canvas',0)
for y in [-1.38,-.86]:k.rod('Plan_Roller',(-3.95,y,1.91),(-3.20,y,1.91),.035,'wood',6)
k.box('Mallet_Head',(-3.07,-.54,1.95),(.19,.25,.14),'iron',.012)
k.box('Mallet_Handle',(-3.38,-.54,1.90),(.48,.06,.06),'wood',.008)
k.group='Timber_Stock'
for j in range(2):
    for i in range(3):k.box('Repair_Timber',(3.5+(i-1)*.27,-2.1,.99+j*.16),(.24,2.1-j*.2,.14),'light' if i==1 else 'plank',.008)
mods=k.join_modules()
markers=[k.marker('Ship_Center',(0,0,.69),'visual cradle reference, not a physics spawn'),k.marker('Sea_Entry',(0,-5.5,0),'open sea end; source -Y'),k.marker('Interaction_Point',(0,5.8,.99),'ship modification interaction')]
report={}
for name,ob in mods.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    assert all(e.is_manifold for e in bm.edges),name
    assert all(f.calc_area()>1e-10 for f in bm.faces),name
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=sum(len(p.vertices)-2 for p in ob.data.polygons);bm.free()
(OUT/'validation.json').write_text(json.dumps({'triangles':sum(report.values()),'modules':report,'source_up':'Z','sea_direction':'-Y','ship_included':False},indent=2))
bpy.ops.object.select_all(action='DESELECT')
for ob in [k.root]+list(mods.values())+markers:ob.select_set(True)
bpy.context.view_layer.objects.active=k.root
bpy.ops.export_scene.fbx(filepath=str(OUT/'drydock-lvl1.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.5
cam=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=17.8
scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
for name,eye in [('preview',(13,-17,13)),('opposite',(-14,-17,14))]:
    cam.location=eye;cam.rotation_euler=(Vector((0,.2,1.3))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,0,1.5);s.region_3d.view_distance=18;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'drydock-lvl1.blend'))
print('DRYDOCK PASS',sum(report.values()))
