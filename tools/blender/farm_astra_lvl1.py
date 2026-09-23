"""Six-bed starter wheat farm with crop states; art staging only."""
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
OUT=HERE.parents[1]/'art-staging/farm-astra-lvl1-v1';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Farm_Level_1',{'wood':'#80583D','edge':'#B08350','plank':'#BC965F','iron':'#495052','stone':'#879698','soil':'#69584A','furrow':'#89715A','canvas':'#E7D9BA','canvas2':'#D8C7A3','rope':'#BFA675','teal':'#658F86','leaf':'#779B57','leaf2':'#9BAF60','straw':'#C9AF60','grain':'#E7C976','grain2':'#D4B668'})
k.root['footprint_xz']=[4.66,4.69]
centers=[(x,y) for y in [-1.20,-.02] for x in [-1.42,0,1.42]]
for index,(x,y) in enumerate(centers,1):
    k.group=f'Bed_{index:02d}_Soil'
    k.box('Earth',(x,y,.085),(1.12,.89,.17),'soil',.045)
    for dy in [-.48,.48]:k.box('Bed_Edge',(x,y+dy,.14),(1.23,.07,.18),'edge',.018)
    for dx in [-.60,.60]:k.box('Bed_End',(x+dx,y,.14),(.07,.91,.18),'wood',.018)
    for dy in [-.25,0,.25]:k.box('Furrow',(x,y+dy,.176),(1.03,.035,.026),'furrow',.01)
    for dx in [-.59,.59]:
        for dy in [-.46,.46]:k.box('Bed_Peg',(x+dx,y+dy,.15),(.085,.085,.30),'wood',.018)

# Crop geometry is grouped by bed and stage, never baked into the soil.
def blade(name,a,b,width,color):
    a,b=Vector(a),Vector(b);d=b-a;u=Vector((d.y,-d.x,0))
    if u.length<.01:u=Vector((1,0,0))
    u.normalize();mid=a.lerp(b,.48);t=Vector((0,0,.006))
    verts=[a,mid+u*width,b,mid-u*width,a+t,mid+u*width+t,b+t,mid-u*width+t]
    k.mesh(name,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],color)
def wheat(x,y,h,seed):
    lean=.045*math.sin(seed*2.1)
    k.rod('Stalk',(x,y,.18),(x+lean,y,h),.009,'straw',5)
    for side in [-1,1]:
        blade('Dry_Leaf',(x,y,h*.48),(x+side*.12,y+.03,h*.64),.018,'straw')
    # One closed lobed head replaces many individually modelled kernels.
    k.lathe('Grain_Head',(x+lean,y,h-.13),[(.012,0),(.039,.03),(.027,.058),(.036,.085),(.024,.112),(.030,.14),(.005,.18)],['grain','grain2','grain'],5)
    k.rod('Awn',(x+lean,y,h),(x+lean+.012,y,h+.09),.004,'grain',4)
for index,(x,y) in enumerate(centers,1):
    for stage in ['Sprout','Growing','Ripe']:
        k.group=f'Bed_{index:02d}_{stage}'
        for row in range(3):
            for col in range(4):
                xx=x-.39+col*.26;yy=y-.26+row*.26;seed=index+row*4+col
                if stage=='Ripe':wheat(xx,yy,.73+.055*math.sin(seed*1.7),seed)
                else:
                    h=.17 if stage=='Sprout' else .39
                    for side in [-1,1]:blade('Leaf',(xx,yy,.18),(xx+side*(.07 if stage=='Sprout' else .13),yy+.025,.18+h),.027 if stage=='Sprout' else .036,'leaf' if side<0 else 'leaf2')
                    if stage=='Growing':k.rod('Green_Stem',(xx,yy,.18),(xx,yy,.62),.008,'leaf',5)

k.group='Tool_Frame'
for x in [-1.40,1.40]:
    for y,h in [(.86,1.46),(1.98,1.89)]:
        k.box('Stone_Foot',(x,y,.09),(.30,.31,.18),'stone',.045)
        k.box('Post',(x,y,(h+.14)/2),(.12,.13,h-.14),'wood',.018)
    k.beam('Side_Rafter',(x,.80,1.43),(x,2.08,1.85),.10,.11,'edge')
    k.beam('Knee_Brace',(x,1.96,1.26),(x,1.47,1.65),.075,.08,'wood')
k.beam('Rear_Spreader',(-1.48,1.96,1.80),(1.48,1.96,1.80),.10,.12,'wood')
k.beam('Tool_Rail',(-1.44,1.94,1.11),(1.44,1.94,1.11),.085,.10,'edge')
k.group='Canopy'
def cloth(u,v):return Vector((-1.62+3.24*u,.75+1.45*v,1.60+.42*v-.08*math.sin(math.pi*u)*math.sin(math.pi*v)-.04*math.sin(math.pi*u)*(1-v)))
nu,nv=12,8;verts=[cloth(i/nu,j/nv)+Vector((0,0,dz)) for dz in [0,-.02] for j in range(nv+1) for i in range(nu+1)]
cnt=(nu+1)*(nv+1);faces=[];cols=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1);faces += [q,tuple(v+cnt for v in reversed(q))];cols += ['teal' if i==1 else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+cnt,a+cnt));cols.append('canvas2')
k.mesh('Canvas',verts,faces,cols)
for i in range(16):k.rod('Rolled_Hem',cloth(i/16,0),cloth((i+1)/16,0),.045,'canvas',8)
for u in [0,1]:
    for v in [0,1]:k.rod('Corner_Tie',cloth(u,v),cloth(u,v)+Vector((.11 if u==0 else -.11,0,-.22)),.014,'rope',5)
k.group='Tools'
for x in [-.40,.12]:
    k.rod('Tool_Handle',(x,1.81,.19),(x,1.81,1.16),.025,'edge',7)
    k.box('Tool_Hook',(x,1.85,1.08),(.08,.17,.035),'iron',.008)
k.box('Hoe_Head',(-.4,1.74,.23),(.27,.16,.045),'iron',.012)
k.box('Rake_Bar',(.12,1.78,.20),(.32,.04,.04),'iron',.008)
for i in range(5):k.rod('Rake_Tooth',(-.02+i*.07,1.78,.20),(-.02+i*.07,1.64,.15),.012,'iron',5)
k.group='Water_Butt'
k.lathe('Barrel',(-1.00,1.46,0),[(.23,.04),(.28,.16),(.30,.43),(.26,.72),(.235,.75),(.21,.74),(.23,.68),(.26,.42),(.24,.18),(.19,.10)],['wood','edge','wood'],12)
for z,r in [(.16,.286),(.59,.282)]:k.lathe('Hoop',(-1,1.46,0),[(r,z-.033),(r+.009,z-.023),(r+.009,z+.024),(r,z+.033),(r-.015,z+.022),(r-.015,z-.023)],'iron',12)
k.group='Seed_Box'
k.box('Box',(.84,1.55,.27),(.62,.45,.45),'wood',.03)
k.box('Lid',(.84,1.55,.51),(.65,.48,.07),'edge',.02)
k.box('Label',(.84,1.314,.32),(.20,.025,.18),'teal',.01)
k.group='Harvest_Basket'
# Open slatted basket stays forward of the plots and fully outside the canopy.
k.box('Basket_Floor',(1.44,-2.02,.075),(.98,.43,.15),'wood',.025)
for y in [-2.24,-1.80]:
    for z in [.19,.33]:k.box('Basket_Slat',(1.44,y,z),(1.04,.04,.10),'edge',.015)
for x in [.94,1.94]:
    for z in [.19,.33]:k.box('Basket_End',(x,-2.02,z),(.04,.43,.10),'plank',.014)
    k.rod('Handle_Post',(x,-2.02,.32),(x,-2.02,.48),.025,'wood',6)
    k.rod('Handle_Grip',(x,-2.12,.48),(x,-1.92,.48),.027,'edge',6)
for i in range(4):
    k.group=f'Harvest_Sheaf_{i+1:02d}'
    x=1.07+i*.245
    for j in range(5):
        dx=(j-2)*.033
        k.rod('Cut_Stalk',(x+dx,-2.14,.19),(x+dx*1.4,-1.89,.40),.015,'straw',5)
        k.lathe('Harvest_Head',(x+dx*1.4,-1.89,.36),[(.02,0),(.031,.035),(.012,.085)],'grain',5)
    k.rod('Bundle_Tie',(x-.095,-2.03,.275),(x+.095,-2.03,.275),.019,'rope',6)
k.group='Sign'
k.box('Sign_Post',(-2.02,-1.89,.37),(.08,.08,.74),'wood',.014)
k.box('Farm_Sign',(-2.02,-1.95,.65),(.42,.07,.37),'teal',.025)
k.rod('Wheat_Stem',(-2.02,-1.994,.52),(-2.02,-1.994,.80),.012,'grain',5)
for z in [.60,.68,.76]:
    for side in [-1,1]:k.rod('Wheat_Kernel',(-2.02,-1.995,z),(-2.02+side*.085,-1.995,z+.045),.020,'grain',5)
modules=k.join_modules()
for i,(x,y) in enumerate(centers,1):k.marker(f'Bed_{i:02d}_Anchor',(x,y,.18),'crop bed center; not a worker standing position')
k.marker('Harvest_Anchor',(1.44,-2.02,.15),'output storage reference')
k.marker('Worker_Approach',(-.65,-2.11,0),'approach reference; navigation not validated')
def state(stages,harvest):
    for name,ob in modules.items():
        hide=False
        if name.startswith('Bed_') and not name.endswith('_Soil'):
            _,number,stage=name.split('_');hide=stage!=stages[int(number)-1]
        if name.startswith('Harvest_Sheaf_'):hide=int(name.rsplit('_',1)[1])>harvest
        ob.hide_render=hide;ob.hide_set(hide)
report={};points=[];bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data);d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons);report[name]=d;points += [ob.matrix_world@v.co for v in ob.data.vertices];bm.free()
for v in modules['Tool_Frame'].data.vertices:
    p=modules['Tool_Frame'].matrix_world@v.co
    assert p.z<cloth((p.x+1.62)/3.24,(p.y-.75)/1.45).z-.025,('Frame/canvas clearance',tuple(p))
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<=2.33 and max(abs(lo[1]),abs(hi[1]))<=2.345,(lo,hi)
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_variant_triangles':sum(d['triangles'] for d in report.values()),'bounds':[lo,hi],'requires_height_metadata_review':True},indent=2))
(OUT/'state-contract.json').write_text(json.dumps({'beds':6,'bed_states':['Bare','Sprout','Growing','Ripe'],'default_state':'Bare','rule':'Keep Soil visible; enable at most one crop stage per bed. Bare means all crop stage meshes hidden.','output_slots':4,'slots_are_not_gameplay_capacity':True,'warning':'Bind to real standing crop, harvest and stored Food; do not invent an independent growth timer. Seed_Box and Water_Butt are decorative props, not new resource requirements.','integration':'Art staging only; no Unity import or production code.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in k.objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('farm-state-kit.fbx');state(['Bare']*6,0);export('farm-bare.fbx')
state(['Ripe']*6,4);export('farm-ripe.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Farm_Review',bpy.data.cameras.new('Farm_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(6,-10,7),res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=7.1
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for stage in ['Bare','Sprout','Growing','Ripe']:state([stage]*6,0);render('state-'+stage.lower())
state(['Ripe','Ripe','Ripe','Growing','Sprout','Bare'],3);render('front-three-quarter');render('opposite-three-quarter',(-6,-10,7));render('front',(0,-12,8));render('game-scale',res=(360,280));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,0,.95);s.region_3d.view_distance=6.6;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/farm-astra-lvl1-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
