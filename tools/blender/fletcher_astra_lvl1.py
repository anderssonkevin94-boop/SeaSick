"""Starter fletcher: authored meshes, visible stocks and exclusive work states."""
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
OUT=HERE.parents[1]/'art-staging/fletcher-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Fletcher_Level_1',{'wood':'#805D43','edge':'#AF8455','plank':'#C6A576','bark':'#65503E','iron':'#485052','steel':'#A5B3B1','stone':'#929D95','canvas':'#E2D7B8','canvas2':'#CBBC97','patch':'#ADBA9D','rope':'#B9A37B','green':'#6F927A','cream':'#EAE5D0','red':'#B56C56','leather':'#88694D'})
k.root['footprint_xz']=[4.84,4.93]
k.group='Frame'
for x in [-1.62,1.62]:
    for y,h in [(.62,2.35),(1.91,2.77)]:
        k.box('Stone_Foot',(x,y,.10),(.34,.36,.20),'stone',.04)
        k.box('Post',(x,y,(h+.16)/2),(.16,.17,h-.16),'wood',.018)
    k.beam('Side_Rafter',(x,.57,2.29),(x,2.01,2.75),.10,.13,'edge')
    k.beam('Knee_Brace',(x,1.89,2.15),(x,1.45,2.60),.09,.11,'wood')
k.beam('Header',(-1.72,1.91,2.69),(1.72,1.91,2.69),.11,.13,'wood')
k.group='Canopy'
def cloth(u,v):
    return Vector((-1.86+3.72*u,.39+1.72*v,2.54+.42*v-.12*math.sin(math.pi*u)*math.sin(math.pi*v)-.08*math.sin(math.pi*u)*(1-v)))
nu,nv=16,8
verts=[cloth(i/nu,j/nv)+Vector((0,0,dz)) for dz in [0,-.02] for j in range(nv+1) for i in range(nu+1)]
cnt=(nu+1)*(nv+1);faces=[];cols=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1)
        faces += [q,tuple(v+cnt for v in reversed(q))];cols += ['green' if i in [1,14] else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+cnt,a+cnt));cols.append('canvas2')
k.mesh('Canvas',verts,faces,cols)
for i in range(20):k.rod('Hem',cloth(i/20,0),cloth((i+1)/20,0),.04,'green',8)
p=[cloth(u,v) for u,v in [(.61,.44),(.77,.44),(.77,.70),(.61,.70)]]
k.mesh('Patch',[q+Vector((0,0,z)) for z in [.01,.022] for q in p],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
for e in range(4):
    a,b=p[e],p[(e+1)%4];d=(b-a).normalized();cross=Vector((-d.y,d.x,0))*.025
    for i in range(4):
        mid=a.lerp(b,(i+.5)/4)+Vector((0,0,.031));k.rod('Stitch',mid-cross,mid+cross,.007,'rope',4)
for u in [0,1]:
    for v in [0,1]:k.rod('Tie',cloth(u,v),cloth(u,v)+Vector((.10 if u==0 else -.10,0,-.23)),.015,'rope',6)

k.group='Shaving_Horse'
k.box('Long_Seat',(-.22,-.70,.57),(.43,1.63,.13),'plank',.035)
for y in [-1.27,-.10]:
    for s in [-1,1]:k.beam('Splayed_Leg',(-.22+s*.27,y+s*.045,.065),(-.22+s*.15,y,.53),.11,.13,'wood')
k.beam('Inclined_Workbed',(-.22,-.69,.69),(-.22,.04,1.00),.27,.10,'edge')
k.box('Workbed_Support',(-.22,-.03,.76),(.17,.19,.37),'wood')
# A U-shaped treadle straddles the bed rather than clipping through its center.
for x in [-.43,-.01]:k.beam('Treadle_Arm',(x,-.64,.18),(x,-.28,1.02),.065,.075,'wood')
k.box('Foot_Pedal',(-.22,-.64,.19),(.59,.21,.07),'edge',.018)
k.box('Clamp_Head',(-.22,-.28,1.02),(.58,.20,.13),'wood',.025)
k.rod('Clamp_Axle',(-.52,-.45,.63),(.08,-.45,.63),.039,'iron',8)
for x in [-.53,.09]:k.rod('Axle_Cap',(x,-.45,.63),(x+.012,-.45,.63),.06,'steel',8)

k.group='Workbench'
for x in [.15,1.31]:
    for y in [.96,1.57]:k.box('Bench_Leg',(x,y,.46),(.12,.14,.92),'wood')
for j in range(4):k.box('Bench_Top',(.73,.91+j*.235,.94),(1.48,.222,.12),'plank' if j%2 else 'edge',.018)
k.box('Bench_Stretcher',(.73,1.26,.26),(1.31,.11,.13),'wood')
k.box('Work_Mat',(.70,1.12,1.01),(.90,.52,.025),'leather',.008)
for x in [.26,1.18]:
    k.box('Shaft_Jig',(x,1.10,1.066),(.06,.32,.09),'edge',.01)
k.group='Hand_Tools'
k.box('Drawknife_Blade',(.54,1.57,1.02),(.36,.065,.025),'steel',.007)
for x in [.32,.76]:k.rod('Drawknife_Grip',(x,1.45,1.04),(x,1.64,1.04),.032,'wood',7)
k.box('Knife_Blade',(1.22,1.37,1.02),(.18,.075,.02),'steel',.005)
k.box('Knife_Grip',(1.42,1.37,1.03),(.18,.07,.04),'wood',.01)
k.lathe('Glue_Pot',(.15,1.63,1.0),[(.08,0),(.12,.07),(.10,.18),(.07,.18),(.075,.09),(.05,.03)],'green',9)

def feather(name,base,length=.25,width=.055,color='cream',angle=0):
    b=Vector(base);u=Vector((math.cos(angle),math.sin(angle),0));v=Vector((-math.sin(angle),math.cos(angle),0))
    shape=[(0,0),(-width,.07),(0,length),(width*.75,length*.75),(width,.11)]
    pts=[b+u*x+Vector((0,0,z))+v*t for t in [-.006,.006] for x,z in shape];n=len(shape)
    k.mesh(name,pts,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],color)
k.group='Feather_Supplies'
k.lathe('Feather_Cup',(1.23,1.61,1.0),[(.10,0),(.13,.23),(.105,.23),(.08,.04)],'edge',10)
for i in range(5):
    a=i*math.tau/5;x=1.23+math.cos(a)*.055;y=1.61+math.sin(a)*.055
    k.rod('Quill',(x,y,1.10),(x,y,1.46),.009,'edge',5)
    feather('Feather',(x,y,1.25),.29,.044,'cream' if i%2 else 'red',a)

k.group='Bow_Rack'
for x in [-1.45,-.68]:
    k.box('Rack_Upright',(x,1.43,1.05),(.09,.12,2.10),'wood')
    k.box('Rack_Foot',(x,1.43,.065),(.23,.52,.13),'wood')
for z in [.28,1.78]:k.box('Rack_Rail',(-1.065,1.43,z),(.89,.12,.09),'edge')
k.group='Display_Bows'
for offset in [-1.47,-.96]:
    # Rectangular sweep produces continuous limbs with deliberate taper.
    pts=[];n=17
    for i in range(n):
        t=i/(n-1);z=.30+1.65*t;x=offset+.27*math.sin(math.pi*t);w=.020+.017*math.sin(math.pi*t)
        pts += [(x+dx,1.29+dy,z) for dx,dy in [(-w,-.024),(w,-.024),(w,.024),(-w,.024)]]
    faces=[(3,2,1,0),tuple((n-1)*4+j for j in range(4))]+[(i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j) for i in range(n-1) for j in range(4)]
    k.mesh('Bow_Limbs',pts,faces,'edge')
    k.rod('Bowstring',(offset,1.29,.30),(offset,1.29,1.95),.008,'rope',5)
    for z in [1.035,1.075,1.115,1.155]:k.box('Grip_Wrap',(offset+.27,1.29,z),(.082,.061,.03),'leather',.006)

k.group='Timber_Cradle'
for y in [-1.56,-.58]:
    k.box('Cradle_Foot',(-1.57,y,.12),(1.07,.18,.24),'wood')
    for x in [-2.03,-1.11]:k.box('Cradle_Post',(x,y,.35),(.10,.13,.63),'edge')
for x in [-1.92,-1.24]:k.box('Cradle_Runner',(x,-1.07,.15),(.11,1.24,.15),'wood')
for i,(x,z) in enumerate([(-1.88,.34),(-1.57,.34),(-1.26,.34),(-1.73,.582),(-1.42,.582)]):
    k.group=f'Input_Timber_{i+1:02d}'
    k.rod('Bark_Log',(x,-1.88,z),(x,-.30,z),.145,'bark',9)
    k.rod('Cut_End',(x,-1.887,z),(x,-1.893,z),.124,'plank',9)
    k.rod('End_Heart',(x,-1.894,z),(x,-1.897,z),.055,'edge',8)

k.group='Arrow_Stand'
k.box('Stand_Base',(1.47,-1.12,.10),(1.11,1.03,.20),'wood',.025)
for x in [1.00,1.94]:
    for y in [-1.55,-.69]:k.box('Corner_Post',(x,y,.39),(.09,.09,.68),'wood')
for y in [-1.57,-.67]:
    for z in [.25,.60]:k.box('Basket_Rail',(1.47,y,z),(1.04,.06,.12),'green' if z>.5 else 'edge')
for x in [.97,1.97]:
    for z in [.25,.60]:k.box('Basket_Side',(x,-1.12,z),(.06,.84,.12),'edge')
for y in [-1.56,-1.12,-.68]:k.box('Compartment',(1.47,y,.30),(.84,.035,.28),'wood',.006)
k.box('Center_Divider',(1.47,-1.12,.30),(.035,.86,.28),'wood',.006)
def arrow(base,h=.99):
    x,y,z=base
    k.rod('Shaft',(x,y,z+.09),(x,y,z+h),.016,'plank',6)
    k.lathe('Arrowhead',(x,y,z),[(.006,0),(.054,.105),(.022,.14)],'steel',4)
    for a in [0,math.tau/3,2*math.tau/3]:feather('Fletching',(x,y,z+h-.24),.20,.06,'cream',a)
    k.lathe('Binding',(x,y,z),[(.021,h-.27),(.021,h-.24)],'red',6)
for i in range(4):
    k.group=f'Output_Arrows_{i+1:02d}'
    x=1.24+(i%2)*.46;y=-1.34+(i//2)*.44
    for j,(dx,dy) in enumerate([(-.09,0),(.09,0),(0,.14)]):arrow((x+dx,y+dy,.21),1.01+j*.055)
k.group='Work_Shaping'
k.rod('Clamped_Blank',(-.22,-.69,.78),(-.22,.21,1.17),.038,'plank',8)
k.group='Work_Fletching'
for y in [1.03,1.18]:
    k.rod('Bench_Shaft',(.20,y,1.13),(1.22,y,1.13),.019,'plank',6)
    k.box('Loose_Fletching',(.84,y,1.16),(.19,.054,.015),'cream',.004)
k.group='Work_Finished'
# Finished arrows use the same geometry laid on the bench, not a new item type.
for y in [1.02,1.18]:
    before=len(k.objects);arrow((0,0,0),.96)
    for ob in k.objects[before:]:
        ob.rotation_euler[1]=math.pi/2;ob.location=(.22,y,1.135)
k.group='Sign'
k.rod('Target_Wood',(1.23,.28,2.02),(1.23,.37,2.02),.285,'edge',16)
k.rod('Target_Face',(1.23,.267,2.02),(1.23,.279,2.02),.255,'cream',16)
k.rod('Target_Ring',(1.23,.256,2.02),(1.23,.266,2.02),.17,'green',16)
k.rod('Target_Center',(1.23,.245,2.02),(1.23,.255,2.02),.078,'red',12)
for x in [1.06,1.40]:k.rod('Sign_Cord',(x,.34,2.23),(x,.48,2.47),.013,'rope',6)

modules=k.join_modules()
for name,ob in modules.items():
    if name.startswith(('Input_Timber_','Output_Arrows_')):
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob;bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY',center='BOUNDS')
for name,p,role in [('Input_Anchor',(-1.57,-1.07,.34),'timber stock'),('Output_Anchor',(1.47,-1.12,.21),'arrow stock'),('Shaping_Anchor',(-.22,-.28,1.09),'horse clamp'),('Fletching_Anchor',(.72,1.10,1.13),'bench work'),('Worker_Stand',(.55,.12,0),'reference only; navigation unvalidated')]:k.marker(name,p,role)
def state(inputs,outputs,work):
    for name,ob in modules.items():
        hidden=False
        if name.startswith('Input_Timber_'):hidden=int(name.rsplit('_',1)[1])>inputs
        if name.startswith('Output_Arrows_'):hidden=int(name.rsplit('_',1)[1])>outputs
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
    assert p.z<cloth((p.x+1.86)/3.72,(p.y-.39)/1.72).z-.025,('Frame/roof',tuple(p))
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<2.42 and max(abs(lo[1]),abs(hi[1]))<2.465 and hi[2]<3.2
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_variant_triangles':sum(d['triangles'] for d in report.values()),'bounds':[lo,hi]},indent=2))
(OUT/'state-contract.json').write_text(json.dumps({'input_slots':5,'output_slots':4,'arrows_per_visual_bundle':3,'slots_are_not_gameplay_capacity':True,'default_state':'empty','work_variants':['Work_Shaping','Work_Fletching','Work_Finished'],'rules':['Claim Timber once before starting.','Show only one work variant or none while idle.','Empty input does not cancel an already claimed job.','Retain Work_Finished when output is full; block a new job.','Transfer without duplicating the job in work and output displays.','Use existing Timber-to-Arrows recipe and yield, not mesh counts.'],'integration':'Art only. No rig, animations, navigation, colliders or gameplay. Bows and feather supplies are decorative, not additional resources.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in k.objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('fletcher-state-kit.fbx');state(0,0,'Idle');export('fletcher-empty.fbx');state(4,3,'Shaping');export('fletcher-review.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Fletcher_Review',bpy.data.cameras.new('Fletcher_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,.12,1.40))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=7.1
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for name,args in {'empty':(0,0,'Idle'),'shaping':(4,0,'Shaping'),'fletching':(3,2,'Fletching'),'blocked':(0,4,'Finished')}.items():state(*args);render('state-'+name)
state(4,3,'Shaping');render('front-three-quarter');render('front',(0,-14,7));render('opposite-three-quarter',(-8,-12,8));render('game-scale',res=(360,280));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,.12,1.4);s.region_3d.view_distance=7.6;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/fletcher-astra-lvl1-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
