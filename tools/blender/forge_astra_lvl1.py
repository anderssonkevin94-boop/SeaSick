"""Flat-shaded starter smithy with separate production-state meshes."""
import bpy
import bmesh
import math
import json
import sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/forge-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Forge_Level_1',{'wood':'#795640','edge':'#AC8154','plank':'#BD9766','iron':'#42494E','steel':'#98A7AA','stone':'#8D9593','stone2':'#A5ADA6','darkstone':'#626D6F','canvas':'#C6C2AD','canvas2':'#ABA995','patch':'#8A9689','rope':'#BBA680','teal':'#5C8986','cream':'#E8DFC1','leather':'#885F4A','leather2':'#624D40','ore':'#636D77','orelight':'#929BA1','coal':'#353C40','water':'#608A96','hot':'#DB9A55'})
k.root['footprint_xz']=[6.53,5.85]
k.group='Frame'
for x in [-1.9,1.9]:
    for y,h in [(.70,2.65),(2.17,3.13)]:
        k.box('Stone_Foot',(x,y,.12),(.40,.42,.24),'darkstone',.05)
        k.box('Post',(x,y,(h+.20)/2),(.20,.21,h-.20),'wood',.025)
        k.box('Post_Shoe',(x,y,.36),(.225,.235,.17),'iron',.01)
    k.beam('Rafter',(x,.65,2.56),(x,2.26,3.08),.13,.14,'edge')
    k.beam('Brace',(x,2.17,2.38),(x,1.64,2.87),.12,.13,'wood')
k.beam('Header',(-2,2.17,3.03),(2,2.17,3.03),.13,.16,'wood')
k.group='Canopy'
def cloth(u,v):
    return Vector((-2.15+4.30*u,.50+1.91*v,2.84+.46*v-.13*math.sin(math.pi*u)*math.sin(math.pi*v)-.09*math.sin(math.pi*u)*(1-v)))
nu,nv=16,10
verts=[cloth(i/nu,j/nv)+Vector((0,0,dz)) for dz in [0,-.022] for j in range(nv+1) for i in range(nu+1)]
cnt=(nu+1)*(nv+1);faces=[];colors=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1)
        faces += [q,tuple(v+cnt for v in reversed(q))]
        colors += ['teal' if i==13 else 'canvas2' if i<4 and j<5 else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+cnt,a+cnt));colors.append('canvas2')
k.mesh('Canvas',verts,faces,colors)
for i in range(20):k.rod('Canvas_Hem',cloth(i/20,0),cloth((i+1)/20,0),.05,'canvas2',8)
p=[cloth(u,v) for u,v in [(.58,.51),(.74,.51),(.74,.76),(.58,.76)]]
k.mesh('Repair_Patch',[q+Vector((0,0,z)) for z in [.012,.025] for q in p],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
for e in range(4):
    a,b=p[e],p[(e+1)%4];d=(b-a).normalized();cross=Vector((-d.y,d.x,0))*.032
    for i in range(4):
        mid=a.lerp(b,(i+.5)/4)+Vector((0,0,.035));k.rod('Stitch',mid-cross,mid+cross,.008,'rope',4)
for u in [0,1]:
    for v in [0,1]:k.rod('Tie',cloth(u,v),cloth(u,v)+Vector((.12 if u==0 else -.12,0,-.24)),.019,'rope',6)

def profile(name,outline,y0,y1,color):
    n=len(outline)
    return k.mesh(name,[(x,y,z) for y in [y0,y1] for x,z in outline],[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],color)

k.group='Forge_Hearth'
k.box('Foundation',(-1.28,-.95,.10),(1.46,1.44,.20),'darkstone',.06)
for level,z in enumerate([.34,.65]):
    for x in [-1.74,-1.28,-.82]:
        k.box('Front_Masonry',(x,-1.52,z),(.445,.27,.285),'stone' if level else 'darkstone',.035)
    for y in [-1.16,-.79]:
        for x in [-1.83,-.73]:k.box('Side_Masonry',(x,y,z),(.28,.35,.285),'stone2' if y< -1 else 'stone',.035)
k.box('Coal_Bed',(-1.28,-1.08,.72),(.82,.70,.12),'coal',.025)
for x in [-1.76,-.80]:k.box('Side_Lip',(x,-1.02,.86),(.29,1.03,.13),'darkstone',.025)
k.box('Front_Lip',(-1.28,-1.57,.86),(1.25,.24,.13),'darkstone',.025)
# Hollow masonry chimney: the rear wall continues into an open flue.
for level in range(6):
    z=.20+level*.34+.17;w=1.03-level*.067;d=.54-level*.023
    for side in [-1,1]:k.box('Flue_Side',(-1.28+side*(w/2-.09),-.47,z),(.18,d,.325),'stone' if level%2 else 'stone2',.025)
    k.box('Flue_Back',(-1.28,-.47+d/2-.075,z),(w-.36,.15,.325),'stone',.022)
    if level>=3:k.box('Flue_Front',(-1.28,-.47-d/2+.075,z),(w-.36,.15,.325),'darkstone',.022)
for x in [-1.64,-.92]:k.box('Chimney_Cap',(x,-.47,2.30),(.20,.64,.17),'darkstone',.025)
for y in [-.70,-.24]:k.box('Chimney_Cap',(-1.28,y,2.30),(.52,.18,.17),'darkstone',.025)
k.group='Coal'
for i in range(9):
    x=-1.55+(i%3)*.27;y=-1.32+(i//3)*.22
    k.lathe('Coal_Lump',(x,y,.79),[(.10,0),(.13,.06),(.065,.13)],'coal',5)

k.group='Bellows'
# Four broad leather folds taper into a nozzle; no intersecting cone primitives.
rings=[(-.78,.12,.71),(-.63,.24,.72),(-.42,.34,.75),(-.22,.28,.78),(-.06,.35,.81),(.12,.28,.84),(.27,.31,.86)]
verts=[]
for y,w,z in rings:
    verts += [(-.33+math.cos(i*math.tau/8)*w,y,z+math.sin(i*math.tau/8)*w*.45) for i in range(8)]
faces=[tuple(reversed(range(8))),tuple(range((len(rings)-1)*8,len(rings)*8))]+[(j*8+i,j*8+(i+1)%8,(j+1)*8+(i+1)%8,(j+1)*8+i) for j in range(len(rings)-1) for i in range(8)]
k.mesh('Leather_Folds',verts,faces,['leather','leather2'])
for z in [.70,.98]:
    k.box('Bellows_Plate',(-.33,.05,z),(.64,.57,.065),'edge',.045)
k.rod('Air_Nozzle',(-.33,-.76,.71),(-.80,-.88,.71),.075,'iron',8)
k.beam('Bellows_Handle',(-.33,.12,1.01),(-.33,.60,1.06),.09,.08,'wood')
for x in [-.52,-.14]:k.box('Bellows_Stand',(x,.08,.34),(.10,.12,.68),'wood')

k.group='Anvil'
k.lathe('Stump',(.67,-.97,0),[(.40,0),(.44,.16),(.37,.64),(.39,.69)],['wood','edge'],10)
for z in [.14,.52]:k.lathe('Stump_Band',(.67,-.97,0),[(.424 if z<.2 else .398,z),(.424 if z<.2 else .398,z+.07)],'iron',10)
outline=[(.15,.70),(1.12,.70),(1.05,.82),(.84,.88),(.82,1.04),(1.17,1.13),(1.18,1.29),(.08,1.29),(.07,1.12),(.43,1.03),(.43,.88),(.22,.82)]
profile('Anvil_Body',outline,-1.18,-.76,'iron')
k.box('Working_Face',(.62,-.97,1.30),(1.12,.45,.07),'steel',.018)
# The horn is a tapered elliptical sweep, continuous at each ring.
verts=[]
for x,ry,rz,z in [(1.15,.205,.115,1.21),(1.43,.16,.09,1.235),(1.67,.08,.045,1.27),(1.79,.015,.012,1.28)]:
    verts += [(x,-.97+ry*math.cos(i*math.tau/8),z+rz*math.sin(i*math.tau/8)) for i in range(8)]
k.mesh('Anvil_Horn',verts,[tuple(reversed(range(8))),tuple(range(24,32))]+[(j*8+i,j*8+(i+1)%8,(j+1)*8+(i+1)%8,(j+1)*8+i) for j in range(3) for i in range(8)],'steel')
for x in [.27,1.00]:
    k.box('Anvil_Holdfast',(x,-1.21,.74),(.11,.10,.15),'iron')
    k.rod('Holdfast_Pin',(x,-1.28,.75),(x,-1.30,.75),.03,'steel',6)

k.group='Workbench'
for x in [-.70,1.50]:
    for y in [1.13,1.75]:k.box('Bench_Leg',(x,y,.47),(.17,.18,.94),'wood',.02)
for j in range(4):k.box('Bench_Plank',(.40,1.06+j*.24,1.0),(2.55,.225,.13),'edge' if j%2 else 'plank',.018)
k.box('Bench_Brace',(.40,1.44,.28),(2.40,.12,.16),'wood')
k.box('Tool_Backboard',(.40,2.05,1.72),(2.64,.12,.50),'wood',.025)
def hammer(x,y,z,head_width=.34):
    k.rod('Hammer_Handle',(x,y,z),(x,y,z+.57),.038,'edge',7)
    k.box('Hammer_Head',(x,y,z+.54),(head_width,.16,.18),'steel',.03)
def tongs(x,y,z):
    for s in [-1,1]:
        k.beam('Tong_Arm',(x+s*.08,y,z),(x-s*.045,y,z+.42),.032,.038,'iron')
        k.beam('Tong_Jaw',(x-s*.045,y,z+.42),(x-s*.075,y,z+.56),.04,.04,'steel')
    k.rod('Tong_Rivet',(x,y-.026,z+.29),(x,y+.026,z+.29),.036,'steel',6)
k.group='Smith_Tools'
hammer(-.45,1.88,1.36);tongs(.18,1.93,1.34);hammer(.92,1.88,1.43)
k.box('Vise_Base',(1.18,1.06,1.13),(.38,.34,.13),'iron',.025)
for x in [1.02,1.34]:k.box('Vise_Jaw',(x,1.06,1.29),(.08,.35,.20),'steel',.015)
k.rod('Vise_Screw',(1.22,.91,1.15),(1.22,.65,1.15),.04,'iron',8)
k.rod('Vise_Handle',(1.22,.65,1.0),(1.22,.65,1.32),.025,'steel',7)
k.group='Apron'
profile('Leather_Apron',[(.11,.92),(.12,.45),(.66,.45),(.70,.92),(.58,1.10),(.25,1.10)],.921,.953,'leather')
for x in [.24,.57]:k.rod('Apron_Tie',(x,.94,1.07),(x,1.14,1.10),.015,'rope',6)

k.group='Quench_Tub'
k.lathe('Open_Tub',(1.55,.17,0),[(.28,0),(.34,.12),(.39,.66),(.34,.66),(.29,.14),(.23,.08)],['wood','edge'],12)
for z,r in [(.12,.348),(.55,.39)]:k.lathe('Tub_Hoop',(1.55,.17,0),[(r,z),(r,z+.065)],'iron',12)
k.lathe('Water',(1.55,.17,0),[(.325,.51),(.33,.52)],'water',12)

k.group='Ore_Bin'
k.box('Bin_Floor',(-2.50,-1.34,.10),(.90,1.39,.20),'wood')
for x in [-2.93,-2.07]:
    for z in [.28,.48]:k.box('Bin_Side',(x,-1.34,z),(.065,1.37,.14),'edge')
for y in [-2.00,-.68]:
    for z in [.28,.48]:k.box('Bin_End',(-2.5,y,z),(.87,.07,.14),'plank')
for i in range(5):
    k.group=f'Input_Ore_{i+1:02d}'
    x=-2.69+(i%2)*.38;y=-1.77+(i//2)*.42
    k.lathe('Ore_Chunk',(x,y,.21),[(.12,0),(.20,.10),(.16,.28),(.06,.37)],['ore','orelight','ore'],7)
k.group='Tool_Rack'
for x in [2.15,2.82]:k.box('Rack_Leg',(x,-1.17,.51),(.12,.16,1.02),'wood')
for z in [.20,.83]:k.box('Rack_Crosspiece',(2.485,-1.17,z),(.91,.17,.14),'teal' if z>.5 else 'wood')
for x in [2.15,2.82]:k.box('Rack_Foot',(x,-1.17,.075),(.23,.88,.15),'wood')
for i in range(4):
    k.group=f'Output_Tool_{i+1:02d}'
    hammer(2.18+i*.205,-1.37,.35+i%2*.06,head_width=.17)
k.group='Work_Heating'
k.box('Billet_In_Forge',(-1.28,-1.10,.95),(.45,.16,.12),'hot',.025)
k.group='Work_Forging'
k.box('Billet_On_Anvil',(.62,-.97,1.385),(.42,.14,.10),'hot',.02)
k.group='Work_Finished'
k.box('Finished_Head',(.60,-.97,1.43),(.35,.17,.19),'steel',.025)
k.group='Sign'
k.box('Sign_Board',(1.28,.52,2.35),(.78,.10,.45),'teal',.045)
for x in [.99,1.56]:k.beam('Sign_Strap',(x,.53,2.56),(x,.63,2.73),.035,.045,'iron')
profile('Anvil_Emblem',[(.96,2.20),(1.48,2.20),(1.33,2.30),(1.33,2.36),(1.57,2.44),(.97,2.44),(.97,2.35),(1.13,2.31),(1.13,2.28)],.452,.463,'cream')

modules=k.join_modules()
for name,ob in modules.items():
    if name.startswith(('Input_Ore_','Output_Tool_')):
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY',center='BOUNDS')
for name,p,role in [('Input_Anchor',(-2.50,-1.34,.21),'ore display'),('Output_Anchor',(2.485,-1.37,.35),'finished tool display'),('Fire_Anchor',(-1.28,-1.10,.91),'external fire VFX'),('Smoke_Anchor',(-1.28,-.47,2.40),'external chimney smoke VFX'),('Sparks_Anchor',(.62,-.97,1.40),'external hammering VFX'),('Anvil_Anchor',(.62,-.97,1.335),'work surface'),('Worker_Stand',(.48,-1.90,0),'reference only; navigation unvalidated')]:k.marker(name,p,role)
def state(inputs,outputs,work):
    for name,ob in modules.items():
        hidden=False
        if name.startswith('Input_Ore_'):hidden=int(name.rsplit('_',1)[1])>inputs
        if name.startswith('Output_Tool_'):hidden=int(name.rsplit('_',1)[1])>outputs
        if name.startswith('Work_'):hidden=name!='Work_'+work
        ob.hide_render=hidden;ob.hide_set(hidden)
report={};points=[];bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;points += [ob.matrix_world@v.co for v in ob.data.vertices];bm.free()
for v in modules['Frame'].data.vertices:
    p=modules['Frame'].matrix_world@v.co
    assert p.z<cloth((p.x+2.15)/4.30,(p.y-.50)/1.91).z-.025,('Roof/frame clearance',tuple(p))
roof_min=min((modules['Canopy'].matrix_world@v.co).y for v in modules['Canopy'].data.vertices)
forge_max=max((modules['Forge_Hearth'].matrix_world@v.co).y for v in modules['Forge_Hearth'].data.vertices)
assert roof_min-forge_max>.40
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<3.265 and max(abs(lo[1]),abs(hi[1]))<2.925 and hi[2]<4.29
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_variant_triangles':sum(d['triangles'] for d in report.values()),'forge_canopy_horizontal_gap':roof_min-forge_max,'bounds':[lo,hi]},indent=2))
(OUT/'state-contract.json').write_text(json.dumps({'input_slots':5,'output_slots':4,'slots_are_not_gameplay_capacity':True,'default_state':'empty','work_variants':['Work_Heating','Work_Forging','Work_Finished'],'rules':['Claim Ore before starting; visual slots are normalized inventory indicators, not recipe quantities.','Use only one work variant, or none when idle.','A running job may continue after input storage empties.','Retain Work_Finished if output is full; do not duplicate finished stock.','Use existing Ore-to-Tools economy; no additional coal or ingot resource implied.'],'vfx':'Fire, smoke and sparks are external. Work hot colors are vertex colors, not an emissive shader.','integration':'Art only; no gameplay, rig, animations, colliders or navigation supplied.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in k.objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('forge-state-kit.fbx');state(0,0,'Idle');export('forge-empty.fbx');state(4,3,'Forging');export('forge-review.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Forge_Review',bpy.data.cameras.new('Forge_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,.10,1.55))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=8.7
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for name,args in {'empty':(0,0,'Idle'),'heating':(4,0,'Heating'),'forging':(3,2,'Forging'),'blocked':(0,4,'Finished')}.items():state(*args);render('state-'+name)
state(4,3,'Forging');render('front-three-quarter');render('front',(0,-14,7));render('opposite-three-quarter',(-8,-12,8));render('game-scale',res=(360,280));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,.10,1.55);s.region_3d.view_distance=8.7;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/forge-astra-lvl1-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
