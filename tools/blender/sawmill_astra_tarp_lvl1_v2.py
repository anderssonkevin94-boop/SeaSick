"""Level-one open saw shed, within the existing 7.56 x 5.85 build footprint."""
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
OUT=HERE.parents[1]/'art-staging/sawmill-astra-tarp-lvl1-v2'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=HERE/'source/sawmill-astra-tarp-lvl1-v2.blend'
ss.clear_scene()
COLORS={'canvas':'#E6D7B5','canvas2':'#DDCAA3','patch':'#C8B68F','rope':'#BBA478','timber':'#795039','edge':'#A96D3D','plank':'#B88751','light':'#CFAC70',
 'bark':'#634737','end':'#D5AF72','heart':'#B98C53','roof':'#BE703C',
 'rooflight':'#D18B48','roofdark':'#9D5733','iron':'#48454A','steel':'#9A9A90',
 'stone':'#818984','stone2':'#9C9E8A','teal':'#628E82','cream':'#EBDFC0','dark':'#302C2C'}
MAT=bpy.data.materials.new('Sawmill_VertexColor');MAT.use_nodes=True
vcol=MAT.node_tree.nodes.new('ShaderNodeVertexColor');vcol.layer_name='Col'
MAT.node_tree.links.new(vcol.outputs['Color'],MAT.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
MAT.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9
root=bpy.data.objects.new('Sawmill_Level_1',None);bpy.context.collection.objects.link(root)
root['footprint_xz']=[7.56,5.85];root['level']=1
objects=[];GROUP='Structure'

def mesh(name,verts,faces,color,parent=root):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob)
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(me);bm.free();me.materials.append(MAT)
    attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in me.polygons:
        p.use_smooth=False
        c=color[p.index] if isinstance(color,list) else color
        for li in p.loop_indices:attr.data[li].color=(*ss.srgb_to_linear(ss._hex(COLORS[c])),1)
    ob.parent=parent;ob['module']=GROUP;objects.append(ob);return ob

def bevel(ob,width):
    bpy.context.view_layer.objects.active=ob
    m=ob.modifiers.new('Cut edges','BEVEL');m.width=width;m.segments=1
    bpy.ops.object.modifier_apply(modifier=m.name)
    return ob

def box(name,center,size,color,edge=0):
    v=[tuple(center[i]+s[i]*size[i]/2 for i in range(3))
       for s in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    o=mesh(name,v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],color)
    return bevel(o,edge) if edge else o

def beam(name,a,b,width,depth,color,edge=.012):
    a,b=Vector(a),Vector(b);d=b-a
    o=box(name,(0,0,0),(width,depth,d.length),color,edge)
    o.location=(a+b)/2;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return o

def prism(name,outline,y0,y1,color,edge=0):
    n=len(outline);v=[(x,y,z) for y in [y0,y1] for x,z in outline]
    f=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
    f += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    o=mesh(name,v,f,color);return bevel(o,edge) if edge else o

def cylinder(name,a,b,r,color,n=10,r2=None):
    a,b=Vector(a),Vector(b);d=(b-a).normalized()
    u=d.cross(Vector((0,0,1)))
    if u.length<.01:u=d.cross(Vector((0,1,0)))
    u.normalize();v=d.cross(u);r2=r if r2 is None else r2
    verts=[p+rr*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n))
           for p,rr in [(a,r),(b,r2)] for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    colors=[color]*len(faces)
    if color=='bark':colors[:2]=['end','end']
    return mesh(name,verts,faces,colors)

def marker(name,p,role,face=(0,1,0)):
    ob=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(ob)
    ob.parent=root;ob.location=p;ob['role']=role;ob['facing_z_up']=[float(v) for v in face]
    ob.empty_display_type='ARROWS';ob.empty_display_size=.25;return ob


def tube(name,path,r,color,n=6):
    closed=(Vector(path[0])-Vector(path[-1])).length<1e-6
    if closed:path=path[:-1]
    verts=[];previous=None
    for j,p in enumerate(path):
        p=Vector(p)
        after=(j+1)%len(path) if closed else min(j+1,len(path)-1)
        before=(j-1)%len(path) if closed else max(0,j-1)
        d=(Vector(path[after])-Vector(path[before])).normalized()
        u=previous-d*previous.dot(d) if previous is not None else d.cross(Vector((0,0,1)))
        if u.length<.01:u=d.cross(Vector((0,1,0)))
        u.normalize();v=d.cross(u)
        previous=u
        verts += [p+r*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)) for i in range(n)]
    faces=[] if closed else [tuple(reversed(range(n))),tuple((len(path)-1)*n+i for i in range(n))]
    for j in range(len(path) if closed else len(path)-1):
        k=(j+1)%len(path)
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,k*n+(i+1)%n,k*n+i))
    return mesh(name,verts,faces,color)


def log(name,x,y,z,length,r=.21):
    # End-grain rings are part of the closed log mesh, not stacked discs.
    N=10;verts=[]
    rings=[(y-length/2+.005,r*.26),(y-length/2,r*.67),(y-length/2,r),
           (y+length/2,r*.94),(y+length/2,r*.63),(y+length/2-.005,r*.25)]
    for yy,rr in rings:
        verts += [(x+rr*math.cos(i*math.tau/N),yy,z+rr*math.sin(i*math.tau/N)) for i in range(N)]
    faces=[tuple(reversed(range(N)))];colors=['heart']
    for j in range(5):
        for i in range(N):
            faces.append((j*N+i,j*N+(i+1)%N,(j+1)*N+(i+1)%N,(j+1)*N+i))
            colors.append(['end','heart','bark','heart','end'][j])
    faces.append(tuple(5*N+i for i in range(N)));colors.append('heart')
    return mesh(name,verts,faces,colors)


# All storage is in front of the canopy. No solid wall or floor hides its state.
GROUP='Frame'
for x in [-1.78,1.78]:
    for y,h in [(.22,2.72),(1.88,3.38)]:
        box('Stone_Pad',(x,y,.11),(.55,.51,.22),'stone2',.075)
        box('Upright',(x,y,(h+.20)/2),(.23,.25,h-.20),'edge',.025)
        box('Iron_Shoe',(x,y,.38),(.253,.272,.14),'iron',.006)
        beam('Knee_Brace',(x,y,h-.87),(x-math.copysign(.46,x),y,h-.26),.13,.14,'timber')
    beam('Side_Rail',(x,.21,2.55),(x,1.88,3.20),.13,.16,'timber')
    beam('Rear_Splay',(x,1.89,2.95),(x-math.copysign(.36,x),2.38,.12),.14,.14,'timber')
beam('Back_Spreader',(-1.85,1.9,3.20),(1.85,1.9,3.20),.13,.15,'timber')

GROUP='Canopy'
def cloth(u,v):
    return (1.90*u,.06+1.98*v,2.62+.64*v-.16*(1-u*u)*math.sin(math.pi*v)-.10*(1-u*u)*(1-v))
NU,NV=16,10
verts=[]
for dz in [0,-.018]:
    for j in range(NV+1):
        for i in range(NU+1):
            x,y,z=cloth(-1+2*i/NU,j/NV);verts.append((x,y,z+dz))
K=(NU+1)*(NV+1);faces=[];cols=[]
for j in range(NV):
    for i in range(NU):
        a=j*(NU+1)+i;q=(a,a+1,a+NU+2,a+NU+1)
        faces.append(q);cols.append('canvas2' if i in [3,11] else 'canvas')
        faces.append(tuple(k+K for k in reversed(q)));cols.append('canvas2')
boundary=list(range(NU+1))+[j*(NU+1)+NU for j in range(1,NV+1)]
boundary += [NV*(NU+1)+i for i in range(NU-1,-1,-1)]+[j*(NU+1) for j in range(NV-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+K,a+K));cols.append('canvas2')
canopy=mesh('Sailcloth',verts,faces,cols)
tube('Rolled_Front_Hem',[cloth(-1+2*i/24,0) for i in range(25)],.075,'canvas',10)
for side in [-1,1]:tube('Reinforced_Selvage',[cloth(side,i/12) for i in range(13)],.022,'canvas2')
for u0,v0,du,dv in [(-.52,.60,.23,.18),(.42,.31,.22,.20)]:
    p=[cloth(u0+u,v0+v) for u,v in [(-du,-dv),(du,-dv),(du,dv),(-du,dv)]]
    pv=[(x,y,z+dz) for dz in [.008,.017] for x,y,z in p]
    mesh('Canvas_Repair',pv,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
    for edge in range(4):
        a,b=Vector(p[edge]),Vector(p[(edge+1)%4]);d=(b-a).normalized()
        cross=Vector((-d.y,d.x,0))*.035
        for j in range(5):
            mid=a.lerp(b,(j+.5)/5)+Vector((0,0,.025))
            tube('Patch_Stitch',[mid-cross,mid+cross],.008,'timber',4)
GROUP='Lashings'
for x in [-1.78,1.78]:
    for y,z in [(.22,2.61),(1.88,3.25)]:
        for turn in range(3):
            path=[(x+.169*math.cos(i*math.tau/16),y+.18*math.sin(i*math.tau/16),z-.05+turn*.044+.018*math.sin(i*math.tau/16)) for i in range(17)]
            tube('Post_Lashing',path,.022,'rope')
for u in [-.67,0,.67]:
    x,y,z=cloth(u,0)
    tube('Roll_Tie',[(x,y+.094*math.cos(i*math.tau/12),z+.094*math.sin(i*math.tau/12)) for i in range(13)],.016,'rope')

GROUP='Sign'
beam('Sign_Bracket',(1.80,.18,2.40),(2.47,.18,2.40),.095,.10,'timber')
for x in [2.01,2.37]:beam('Sign_Strap',(x,.18,2.40),(x,.18,2.13),.032,.035,'iron',0)
prism('Sawyer_Sign',[(1.89,1.65),(2.50,1.65),(2.55,1.74),(2.50,2.14),(1.89,2.14)],.115,.205,'teal',.016)
shape=[(1.99,2.01),(2.43,2.01),(2.45,1.89)]
shape += [(2.42-i*.053,1.85 if i%2 else 1.91) for i in range(9)]
prism('Saw_Symbol',shape,.099,.113,'cream')

GROUP='Input_Rack'
for y in [-2.06,-.52]:
    box('Log_Cradle',(-2.72,y,.20),(1.38,.19,.25),'timber',.018)
    for x in [-3.34,-2.10]:
        box('Log_Retainer',(x,y,.51),(.10,.12,.82),'edge',.012)
        box('Retainer_Band',(x,y,.35),(.12,.14,.085),'iron',.004)
for x in [-3.10,-2.34]:beam('Rack_Runner',(x,-2.30,.12),(x,-.30,.12),.13,.14,'timber')
# Six explicit display slots; these are NOT an invented gameplay capacity.
for i,(dx,z) in enumerate([(-.43,.52),(0,.52),(.43,.52),(-.215,.895),(.215,.895),(0,1.27)]):
    GROUP='Input_Log_%02d'%(i+1)
    log('Stored_Log',-2.72+dx,-1.28,z,2.05,.21)

GROUP='Output_Rack'
for y in [-2.06,-.52]:
    box('Plank_Bearer',(2.73,y,.16),(1.48,.21,.32),'timber',.018)
    for x in [2.03,3.43]:box('Plank_Retainer',(x,y,.44),(.10,.11,.68),'edge',.01)
for x in [2.32,3.14]:beam('Plank_Runner',(x,-2.30,.10),(x,-.30,.10),.12,.16,'timber')
for i in range(12):
    GROUP='Output_Plank_%02d'%(i+1)
    row,col=divmod(i,3)
    box('Stored_Plank',(2.28+col*.45,-1.28,.365+row*.112),(.425,2.05,.10),'light' if i%3 else 'plank',.009)

GROUP='Workbench'
for y in [-1.49,.20]:
    beam('Sawhorse_Crossbar',(-.52,y,.94),(.52,y,.94),.15,.17,'edge')
    for side in [-1,1]:
        beam('Sawhorse_Leg',(side*.48,y-.15,.04),(side*.28,y,.94),.12,.14,'timber')
        beam('Sawhorse_Back_Leg',(side*.48,y+.15,.04),(side*.28,y,.94),.12,.14,'timber')
    beam('Sawhorse_Stretcher',(-.42,y,.36),(.42,y,.36),.10,.11,'edge')
beam('Horse_Link',(.31,-1.49,.58),(.31,.20,.58),.11,.11,'timber')
for x in [-.72,.72]:
    for y in [1.04,1.59]:box('Tool_Bench_Leg',(x,y,.52),(.11,.11,1.04),'timber',.01)
box('Tool_Bench_Top',(0,1.32,1.09),(1.68,.83,.13),'plank',.016)
box('Tool_Tray',(0,1.52,1.20),(.74,.29,.10),'timber',.01)
beam('Mallet_Handle',(-.59,1.11,1.2),(-.17,1.23,1.2),.045,.045,'edge',.006)
box('Mallet_Head',(-.58,1.10,1.25),(.15,.23,.13),'timber',.013)

GROUP='Bench_Loaded'
log('Work_Log',0,-.65,1.20,2.30,.20)
GROUP='Bench_Cutting'
# Two close semicircular solids expose a narrow pale rip cut at the center.
for side in [-1,1]:
    outline=[(side*.014,1.0),(side*.014,1.4)]
    outline += [(side*(.014+.20*math.sin(i*math.pi/8)),1.2+.20*math.cos(i*math.pi/8)) for i in range(1,8)]
    ob=prism('Ripped_Log_Half',outline,-1.80,.50,'bark')
    for p in ob.data.polygons:
        if abs(p.normal.x)>.99 or abs(p.normal.y)>.99:
            for li in p.loop_indices:ob.data.color_attributes['Col'].data[li].color=(*ss.srgb_to_linear(ss._hex(COLORS['end'])),1)
GROUP='Bench_Finished'
box('Finished_Plank',(0,-.65,1.075),(.37,2.25,.12),'light',.01)

GROUP='Saw_Tool'
# The saw is its own module, so a later worker animation can move it.
for y in [-1.45,.08]:beam('Saw_Handle',(0,y,1.35),(0,y,1.90),.066,.07,'edge',.006)
beam('Saw_Frame',(0,-1.50,1.82),(0,.13,1.82),.065,.07,'timber',.006)
N=19
outline=[(-1.43,1.50),(.06,1.50),(.06,1.39)]
outline += [(.06-i*1.49/N,1.36 if i%2 else 1.40) for i in range(N+1)]
verts=[(x,y,z) for x in [-.013,.013] for y,z in outline];n=len(outline)
faces=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
mesh('Hand_Saw_Blade',verts,faces,'steel')

for name,p,role,face in [('Entry',(0,-2.70,0),'entry',(0,1,0)),
    ('Sawyer',(.97,-.70,0),'worker',(-1,0,0)),('Log_Feeder',(-.98,-.75,0),'worker',(1,0,0)),
    ('Input_Delivery',(-2.72,-2.62,0),'delivery',(0,1,0)),
    ('Output_Collection',(2.73,-2.62,0),'collection',(0,1,0))]:marker(name,p,role,face)

# Merge by semantic module, preserving each actual inventory display slot.
modules={}
for ob in objects:modules.setdefault(ob['module'],[]).append(ob)
objects=[]
for name,items in modules.items():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in items:ob.select_set(True)
    bpy.context.view_layer.objects.active=items[0]
    if len(items)>1:bpy.ops.object.join()
    ob=bpy.context.object;ob.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    ob['module']=name;modules[name]=ob;objects.append(ob)

input_container=marker('Input_Container',(-2.72,-1.28,0),'inventory')
output_container=marker('Output_Container',(2.73,-1.28,0),'inventory')
bench_anchor=marker('Bench_Anchor',(0,-.65,1.0),'work_in_progress')
for name,ob in modules.items():
    if name.startswith('Input_Log_'):parent=input_container
    elif name.startswith('Output_Plank_'):parent=output_container
    elif name.startswith('Bench_') or name=='Saw_Tool':parent=bench_anchor
    else:continue
    center=Vector(tuple((min(v.co[i] for v in ob.data.vertices)+max(v.co[i] for v in ob.data.vertices))/2 for i in range(3)))
    for v in ob.data.vertices:v.co-=center
    ob.parent=parent;ob.location=center-parent.location

def inventory(prefix,count):
    for name,ob in modules.items():
        if name.startswith(prefix):
            hidden=int(name.rsplit('_',1)[1])>count;ob.hide_render=hidden;ob.hide_set(hidden)

def state(inputs,output,bench):
    inventory('Input_Log_',inputs);inventory('Output_Plank_',output)
    for name in ['Loaded','Cutting','Finished']:
        ob=modules['Bench_'+name];ob.hide_render=name!=bench;ob.hide_set(name!=bench)
    ob=modules['Saw_Tool'];hidden=bench not in ['Loaded','Cutting']
    ob.hide_render=hidden;ob.hide_set(hidden)

def validate():
    from mathutils.bvhtree import BVHTree
    report={};points=[]
    bpy.context.view_layer.update()
    for o in objects:
        bm=bmesh.new();bm.from_mesh(o.data)
        d={'triangles':sum(len(f.verts)-2 for f in bm.faces),
           'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
           'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
        assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(o.name,d)
        assert not any(p.use_smooth for p in o.data.polygons)
        report[o.name]=d;points += [o.matrix_world@v.co for v in o.data.vertices];bm.free()
    lo=[min(v[i] for v in points) for i in range(3)];hi=[max(v[i] for v in points) for i in range(3)]
    assert max(abs(lo[0]),abs(hi[0]))<3.78
    assert max(abs(lo[1]),abs(hi[1]))<2.925 and hi[2]<3.84
    roof=modules['Canopy'];roof.data.calc_loop_triangles()
    tree=BVHTree.FromPolygons([roof.matrix_world@v.co for v in roof.data.vertices],
        [tuple(t.vertices) for t in roof.data.loop_triangles],all_triangles=True)
    occlusion={}
    # Orthographic rays toward the reviewed front camera directions.
    for view in [(-9,-12,9),(0,-15,10),(9,-12,9)]:
        direction=(Vector(view)-Vector((0,0,1.45))).normalized();hits=0
        for x in [-2.72,2.73]:
            for dx in [-.4,0,.4]:
                for y in [-2.2,-1.3,-.4]:
                    for z in [.35,1.1]:
                        location,normal,index,distance=tree.ray_cast(Vector((x+dx,y,z)),direction,30)
                        hits+=location is not None
        occlusion[str(view)]=hits
        assert hits==0,('Canopy obscures rack samples',view,hits)
    return {'modules':report,'all_variant_triangles':sum(d['triangles'] for d in report.values()),
            'bounds':[lo,hi],'canopy_occlusion_hits':occlusion}

report=validate()
report['empty_triangles']=sum(d['triangles'] for n,d in report['modules'].items()
    if not n.startswith(('Input_Log_','Output_Plank_','Bench_')) and n!='Saw_Tool')
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
states={'empty':(0,0,'Empty'),'ready':(6,0,'Empty'),'working':(5,0,'Cutting'),
        'output-ready':(5,1,'Empty'),'output-blocked':(0,12,'Finished')}
contract={'input_display_slots':6,'output_display_slots':12,
 'slots_are_not_gameplay_capacity':True,'default_state':'empty',
 'bench_variants':['Bench_Loaded','Bench_Cutting','Bench_Finished'],
 'states_for_review':{k:{'input':v[0],'output':v[1],'bench':v[2]} for k,v in states.items()},
 'production_rules':['Start only if an input log exists; remove it from storage when loading the bench.',
 'Finish the current job even if input storage is now empty.',
 'When output cannot accept production, keep finished work on the bench and block the next job.',
 'Inventory counts and job state must drive visibility; do not randomize or loop stock changes.'],
 'overflow':'Agree gameplay capacity or add a count indicator before integrating inventories larger than the display slots.',
 'integration':'Model only. Runtime inventory, navigation, yields and animation code are not modified.'}
(OUT/'state-contract.json').write_text(json.dumps(contract,indent=2))

def export(name,items):
    bpy.ops.object.select_all(action='DESELECT')
    for o in items:o.hide_set(False);o.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,
        object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,
        use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
for o in objects:o.hide_render=False;o.hide_set(False)
export('sawmill-tarp-state-kit.fbx',objects+empties)
static=[o for o in objects if not o.name.startswith(('Input_Log_','Output_Plank_','Bench_')) and o.name!='Saw_Tool']
export('sawmill-tarp-empty.fbx',static+empties)
state(4,6,'Cutting')
export('sawmill-tarp-review.fbx',[o for o in objects if not o.hide_render]+empties)

scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65
scene.display.shading.show_shadows=False;scene.display.shading.background_type='WORLD'
scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));scene.collection.objects.link(cam)
scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),scale=8.4,res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,-.05,1.45))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
for name,values in states.items():
    state(*values);render('state-'+name)
state(4,6,'Cutting');render('front-three-quarter')
render('front',eye=(0,-14,7))
render('opposite-three-quarter',eye=(-8,-12,8))
render('game-scale',res=(360,280))
render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX'
            s.region_3d.view_location=(0,0,1.45);s.region_3d.view_distance=10
            s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
root['preview_only']='Displayed inventory is a staged review pose, not live game data. See state-contract.json.'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print(json.dumps({'all_variant_triangles':report['all_variant_triangles'],'modules':len(objects),'bounds':report['bounds']},indent=2))
