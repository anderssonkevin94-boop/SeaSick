"""Starter field kitchen; separated input, preparation, cooking and meal displays."""
import bpy
import bmesh
import math
import json
import sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/kitchen-astra-lvl1-v1';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Kitchen_Level_1',{'wood':'#81573D','edge':'#B38250','plank':'#C09A65','iron':'#444B50','steel':'#99A7A6','stone':'#8A989A','stone2':'#A3ADAA','canvas':'#E8DABB','canvas2':'#D6C29B','patch':'#C6B18A','rope':'#B99D6C','teal':'#648D87','cream':'#EBDEC3','red':'#BB6B50','green':'#88A15E','green2':'#A6B875','food':'#C4A166','soup':'#B88B45','char':'#414342'})
k.root['footprint_xz']=[6.26,6.12]
k.group='Frame'
for x in [-1.78,1.78]:
    for y,h in [(.55,2.60),(2.13,3.06)]:
        k.box('Footing',(x,y,.12),(.43,.45,.24),'stone',.055)
        k.box('Post',(x,y,(h+.20)/2),(.19,.20,h-.20),'wood',.022)
        k.box('Iron_Shoe',(x,y,.39),(.211,.222,.16),'iron',.009)
    k.beam('Side_Rafter',(x,.51,2.49),(x,2.25,3.04),.12,.14,'edge')
    k.beam('Knee_Brace',(x,2.10,2.32),(x,1.51,2.79),.11,.12,'wood')
k.beam('Rear_Header',(-1.88,2.13,2.98),(1.88,2.13,2.98),.12,.15,'wood')
k.beam('Tool_Rail',(-1.80,2.00,2.15),(1.80,2.00,2.15),.09,.12,'edge')
k.group='Canopy'
def cloth(u,v):return Vector((-2.02+4.04*u,.36+2.02*v,2.75+.49*v-.12*math.sin(math.pi*u)*math.sin(math.pi*v)-.09*math.sin(math.pi*u)*(1-v)))
nu,nv=16,10;verts=[cloth(i/nu,j/nv)+Vector((0,0,dz)) for dz in [0,-.022] for j in range(nv+1) for i in range(nu+1)]
cnt=(nu+1)*(nv+1);faces=[];cols=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1);faces += [q,tuple(v+cnt for v in reversed(q))];cols += ['red' if i==2 else 'canvas2' if i==11 else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+cnt,a+cnt));cols.append('canvas2')
k.mesh('Canvas',verts,faces,cols)
for i in range(20):k.rod('Rolled_Hem',cloth(i/20,0),cloth((i+1)/20,0),.055,'canvas',8)
p=[cloth(u,v) for u,v in [(.62,.52),(.79,.52),(.79,.75),(.62,.75)]]
k.mesh('Patch',[q+Vector((0,0,z)) for z in [.012,.025] for q in p],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
for e in range(4):
    a,b=p[e],p[(e+1)%4];d=(b-a).normalized();cross=Vector((-d.y,d.x,0))*.032
    for i in range(4):
        mid=a.lerp(b,(i+.5)/4)+Vector((0,0,.035));k.rod('Patch_Stitch',mid-cross,mid+cross,.008,'rope',4)
for u in [0,1]:
    for v in [0,1]:k.rod('Canvas_Tie',cloth(u,v),cloth(u,v)+Vector((.12 if u==0 else -.12,0,-.26)),.018,'rope',6)

def table(name,x,y,w,d,h):
    for dx in [-w*.40,w*.40]:
        for dy in [-d*.36,d*.36]:k.box(name+'_Leg',(x+dx,y+dy,h/2),(.14,.15,h),'wood',.018)
    for j in range(4):k.box(name+'_Top',(x,y-d/2+(j+.5)*d/4,h),(w,d/4-.014,.12),'plank' if j%2 else 'edge',.018)
    k.box(name+'_Stretcher',(x,y,h*.25),(w*.86,.12,.14),'wood')
k.group='Prep_Bench';table('Bench',0,1.14,2.86,.95,.98)
k.box('Chopping_Board',(-.30,.93,1.078),(1.13,.64,.075),'edge',.035)
for x in [-.77,.18]:k.box('Board_End', (x,.93,1.12),(.045,.61,.012),'wood',.003)
k.group='Utensils'
for i,x in enumerate([-.98,-.34,.38]):
    k.rod('Handle',(x,1.88,1.57),(x,1.88,2.15),.025,'edge',7)
    k.box('Hook',(x,1.95,2.13),(.06,.16,.045),'iron',.01)
    if i==0:k.lathe('Ladle_Cup',(x,1.84,1.44),[(.04,0),(.12,.07),(.125,.14),(.10,.14),(.09,.075),(.025,.025)],'iron',8)
    elif i==1:k.box('Spatula',(x,1.88,1.47),(.17,.045,.24),'iron',.024)
    else:
        for dx in [-.05,0,.05]:k.rod('Fork_Tine',(x+dx,1.88,1.61),(x+dx,1.88,1.37),.012,'iron',5)
k.box('Knife_Blade',(.70,.92,1.077),(.38,.13,.025),'steel',.008)
k.box('Knife_Handle',(1.01,.92,1.09),(.24,.095,.06),'wood',.018)
# Cloth is a closed ribbon over the front edge, not intersecting the countertop.
path=[(.98,1.07),(.69,1.07),(.64,1.035),(.625,.98),(.625,.57)]
verts=[(x,y,z) for x in [.89,1.23] for y,z in path]+[(x,y-.012,z-.012) for x in [.89,1.23] for y,z in path]
faces=[]
for j in range(4):faces += [(j,j+1,6+j,5+j),(10+j,15+j,16+j,11+j)]
boundary=[0,1,2,3,4,9,8,7,6,5]
for j,a in enumerate(boundary):b=boundary[(j+1)%10];faces.append((a,b,b+10,a+10))
k.mesh('Dish_Towel',verts,faces,'cream')

k.group='Hearth'
k.box('Hearth_Slab',(0,-1.06,.09),(1.67,1.46,.18),'stone2',.07)
for side in [-1,1]:
    for z in [.32,.64]:
        for y in [-1.42,-.78]:k.box('Stove_Block',(side*.61,y,z),(.39,.60,.29),'stone' if y< -1 else 'stone2',.045)
for z in [.32,.64]:k.box('Back_Block',(0,-.45,z),(.86,.27,.29),'stone2',.035)
for x in [-.55,-.27,0,.27,.55]:k.rod('Grate',(x,-1.64,.84),(x,-.48,.84),.03,'iron',8)
k.box('Fireplace_Lintel',(0,-1.57,.73),(.86,.23,.20),'stone',.04)
k.group='Burn_Logs'
for x in [-.22,.18]:k.rod('Charred_Log',(x,-1.58,.25),(x+.04,-.65,.27),.085,'char',8)
k.group='Cooking_Pot'
k.lathe('Cauldron',(0,-1.02,0),[(.28,.87),(.48,.98),(.53,1.21),(.48,1.46),(.51,1.49),(.50,1.54),(.44,1.54),(.43,1.43),(.47,1.22),(.41,1.01),(.24,.94)],'iron',16)
for side in [-1,1]:
    k.rod('Handle_Attach',(side*.45,-1.02,1.40),(side*.64,-1.02,1.40),.04,'iron',8)
    k.rod('Pot_Handle',(side*.64,-1.20,1.40),(side*.64,-.84,1.40),.045,'wood',8)

k.group='Input_Crate'
k.box('Input_Base',(-2.23,-1.11,.12),(1.13,1.31,.24),'wood',.025)
for x in [-2.77,-1.69]:
    for z in [.34,.54]:k.box('Crate_Side',(x,-1.11,z),(.07,1.30,.14),'edge')
for y in [-1.75,-.47]:
    for z in [.34,.54]:k.box('Crate_End',(-2.23,y,z),(1.1,.07,.14),'plank')
k.group='Serving_Shelf';table('Serving',2.20,-1.10,1.12,1.28,.97)
k.box('Serving_Back',(2.20,-.45,1.10),(1.16,.08,.25),'teal',.02)

def vegetable(x,y,z,color):
    k.lathe('Vegetable',(x,y,z),[(.055,0),(.12,.07),(.115,.17),(.045,.225)],color,7)
    k.rod('Stem',(x,y,z+.20),(x+.02,y,z+.255),.012,'green',5)
for i,(x,y) in enumerate([(-2.47,-1.44),(-2.01,-1.43),(-2.48,-.85),(-2.02,-.85)]):
    k.group=f'Input_Food_{i+1:02d}'
    if i<2:
        k.lathe('Sack',(x,y,.25),[(.12,0),(.20,.1),(.21,.36),(.12,.49),(.04,.54),(.06,.58)],'canvas2',9)
        k.lathe('Sack_Tie',(x,y,.25),[(.052,.515),(.052,.54)],'rope',8)
    else:
        for dx,dy in [(-.11,0),(.12,.06),(0,-.17)]:vegetable(x+dx,y+dy,.25,'green' if i==2 else 'red')
def meal(x,y,z):
    k.lathe('Meal_Bowl',(x,y,z),[(.075,0),(.16,.09),(.175,.18),(.153,.18),(.14,.10),(.06,.025)],'edge',10)
    k.lathe('Meal_Fill',(x,y,z),[(.135,.125),(.14,.14)],'food',10)
    for dx,dy in [(-.05,-.03),(.06,.04)]:k.box('Meal_Garnish',(x+dx,y+dy,z+.158),(.048,.035,.025),'green',.008)
for i in range(6):
    k.group=f'Output_Meal_{i+1:02d}';meal(1.95+(i%2)*.50,-1.48+(i//2)*.38,1.04)
k.group='Work_Preparing'
for x,y,c in [(-.57,.81,'green'),(-.12,1.03,'red')]:vegetable(x,y,1.119,c)
k.group='Work_Cooking'
k.lathe('Pot_Contents',(0,-1.02,0),[(.424,1.37),(.425,1.39)],'soup',16)
for x,y in [(-.16,-1.10),(.12,-.93),(.03,-1.22)]:k.box('Stew_Piece',(x,y,1.403),(.10,.07,.035),'green',.012)
k.group='Work_Finished';meal(-.28,.94,1.12)
k.group='Sign'
k.box('Sign_Board',(1.14,.39,2.23),(.70,.095,.43),'teal',.055)
for x in [.89,1.39]:k.beam('Sign_Strap',(x,.40,2.42),(x,.48,2.66),.035,.04,'iron')
# Bowl icon with a spoon: a recognisable silhouette at gameplay scale.
k.mesh('Bowl_Icon',[(x,y,z) for y in [.327,.341] for x,z in [(.88,2.32),(1.40,2.32),(1.30,2.11),(.99,2.11)]],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'cream')
k.rod('Spoon_Icon',(1.25,.315,2.30),(1.37,.315,2.48),.023,'cream',6)
modules=k.join_modules()
for name,ob in modules.items():
    if name.startswith(('Input_Food_','Output_Meal_')):
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob;bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY',center='BOUNDS')
for name,p,role in [('Input_Anchor',(-2.23,-1.11,.24),'input storage'),('Output_Anchor',(2.20,-1.1,1.04),'meal storage'),('Prep_Anchor',(-.30,.93,1.12),'preparation surface'),('Fire_Anchor',(0,-1.10,.30),'external fire VFX'),('Steam_Anchor',(0,-1.02,1.55),'external steam VFX'),('Worker_Stand',(0,.08,0),'reference only; navigation unvalidated')]:k.marker(name,p,role)
def state(inputs,outputs,work):
    for name,ob in modules.items():
        hidden=False
        if name.startswith('Input_Food_'):hidden=int(name.rsplit('_',1)[1])>inputs
        if name.startswith('Output_Meal_'):hidden=int(name.rsplit('_',1)[1])>outputs
        if name.startswith('Work_'):hidden=name!='Work_'+work
        ob.hide_render=hidden;ob.hide_set(hidden)
report={};points=[];bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data);d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons);report[name]=d;points += [ob.matrix_world@v.co for v in ob.data.vertices];bm.free()
for v in modules['Frame'].data.vertices:
    p=modules['Frame'].matrix_world@v.co
    assert p.z<cloth((p.x+2.02)/4.04,(p.y-.36)/2.02).z-.025,('Roof/frame clearance',tuple(p))
roof_min=min((modules['Canopy'].matrix_world@v.co).y for v in modules['Canopy'].data.vertices)
pot_max=max((modules['Cooking_Pot'].matrix_world@v.co).y for v in modules['Cooking_Pot'].data.vertices)
assert roof_min-pot_max>.60
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<3.13 and max(abs(lo[1]),abs(hi[1]))<3.06 and hi[2]<4.18
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_variant_triangles':sum(d['triangles'] for d in report.values()),'pot_canopy_horizontal_gap':roof_min-pot_max,'bounds':[lo,hi]},indent=2))
(OUT/'state-contract.json').write_text(json.dumps({'input_slots':4,'output_slots':6,'slots_are_not_gameplay_capacity':True,'default_state':'empty','work_variants':['Work_Preparing','Work_Cooking','Work_Finished'],'rules':['Require Food before starting and remove it from input when claimed.','Show only one work variant or none.','No input left does not cancel a job already cooking.','On full output retain Work_Finished and block the next job.','Transfer finished meal into output without duplicating stock.'],'vfx':'No flames, smoke, steam, dynamic light or fire animation provided. Fire_Anchor and Steam_Anchor are VFX attachment references.','integration':'Art only, no game import or production code.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in k.objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('kitchen-state-kit.fbx');state(0,0,'Idle');export('kitchen-empty.fbx');state(3,4,'Cooking');export('kitchen-review.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Kitchen_Review',bpy.data.cameras.new('Kitchen_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,.10,1.55))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=8.5
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for name,args in {'empty':(0,0,'Idle'),'preparing':(3,0,'Preparing'),'cooking':(3,2,'Cooking'),'blocked':(0,6,'Finished')}.items():state(*args);render('state-'+name)
state(3,4,'Cooking');render('front-three-quarter');render('front',(0,-14,7));render('opposite-three-quarter',(-8,-12,8));render('game-scale',res=(360,280));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,.10,1.30);s.region_3d.view_distance=8;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/kitchen-astra-lvl1-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
