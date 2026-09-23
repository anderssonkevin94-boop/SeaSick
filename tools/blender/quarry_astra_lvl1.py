"""Staged art only: level-one manual stonecutting yard, 7.4 x 5.8 m plot."""
import bpy
import bmesh
import math
import json
import sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import steamer as ss

OUT = HERE.parents[1] / 'art-staging/quarry-astra-lvl1-v2'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = HERE / 'source/quarry-astra-lvl1-v2.blend'
ss.clear_scene()
COLORS = {'wood':'#865638', 'edge':'#B17B49', 'end':'#D0A66C',
          'iron':'#454850', 'steel':'#9AADB1', 'rope':'#C0A478',
          'stone':'#8C989E', 'stone2':'#A3AFB2', 'stone3':'#74838D',
          'cut':'#C4C9C3', 'cut2':'#ADB9B7', 'teal':'#568B86',
          'roof':'#678F87', 'roof2':'#7DA096', 'roof3':'#4E756F',
          'cream':'#EADDBB', 'dark':'#41474C', 'canvas':'#E6D7B5',
          'canvas2':'#DDCAA3', 'patch':'#C8B68F'}
mat = bpy.data.materials.new('Quarry_VertexColor')
mat.use_nodes = True
node = mat.node_tree.nodes.new('ShaderNodeVertexColor'); node.layer_name = 'Col'
mat.node_tree.links.new(node.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .92
root = bpy.data.objects.new('Quarry_Level_1', None)
bpy.context.collection.objects.link(root)
root['footprint_xz'] = [7.4, 5.8]
root['level'] = 1
objects = []
group = 'Structure'

def mesh(name, verts, faces, color):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces)); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    me.materials.append(mat)
    attr = me.color_attributes.new(name='Col', type='BYTE_COLOR', domain='CORNER')
    for p in me.polygons:
        p.use_smooth = False
        c = color[p.index % len(color)] if isinstance(color, list) else color
        for li in p.loop_indices:
            attr.data[li].color = (*ss.srgb_to_linear(ss._hex(COLORS[c])), 1)
    ob.parent = root; ob['module'] = group; objects.append(ob)
    return ob

def box(name, at, size, color, bevel=0):
    verts = [tuple(at[i]+s[i]*size[i]/2 for i in range(3)) for s in
             [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    ob = mesh(name, verts, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], color)
    if bevel:
        bpy.context.view_layer.objects.active = ob
        mod = ob.modifiers.new('Hand-cut arris', 'BEVEL'); mod.width = min(bevel,min(size)*.4); mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return ob

def beam(name, a, b, w, d, color, bevel=.015):
    a,b = Vector(a),Vector(b)
    ob = box(name, (0,0,0), (w,d,(b-a).length), color, bevel)
    ob.location=(a+b)/2; ob.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return ob

def rod(name, a, b, radius, color, sides=8):
    a,b=Vector(a),Vector(b); axis=(b-a).normalized()
    u=axis.cross(Vector((0,0,1)))
    if u.length<.01: u=axis.cross(Vector((0,1,0)))
    u.normalize(); v=axis.cross(u)
    verts=[p+radius*(u*math.cos(i*math.tau/sides)+v*math.sin(i*math.tau/sides)) for p in [a,b] for i in range(sides)]
    return mesh(name, verts, [tuple(reversed(range(sides))),tuple(sides+i for i in range(sides))]+
                [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],color)

def rock(name, at, size, seed):
    # Three irregular rings give a deliberate chipped silhouette, not smooth spheres.
    verts=[]; n=7
    for j,(z,r) in enumerate([(-.5,.71),(-.15,1),(.5,.69)]):
        for i in range(n):
            angle=math.tau*i/n
            jitter=1+.17*math.sin(i*3.7+seed+j*.6)
            verts.append((at[0]+math.cos(angle)*size[0]*.5*r*jitter+.07*size[0]*j*math.sin(seed),
                          at[1]+math.sin(angle)*size[1]*.5*r*jitter+.04*size[1]*j*math.cos(seed),
                          at[2]+size[2]*(z+.09*math.sin(i*2.1+seed))))
    faces=[tuple(reversed(range(n))),tuple(2*n+i for i in range(n))]
    faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(2) for i in range(n)]
    return mesh(name, verts, faces, ['stone','stone2','stone3','stone'])

def marker(name, at, role):
    ob=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(ob)
    ob.parent=root; ob.location=at; ob['role']=role
    return ob

# Sheltered rear tool wall. Staggered masonry courses are actual separate blocks.
group='Masonry'
for row in range(3):
    widths=[.48,.93,.93,.93,.48] if row%2 else [.75,.75,.75,.75,.75]
    x=-1.9
    for i,w in enumerate(widths):
        box('Wall_Block',(x+w/2,1.97,.20+row*.38),(w-.028,.43,.35),['stone','stone2','stone3'][(i+row)%3],.055)
        x+=w
for x in [-1.96,1.96]:
    box('Post_Foot',(x,1.90,.15),(.57,.59,.30),'stone2',.055)
    box('Rear_Post',(x,1.90,1.40),(.24,.24,2.50),'wood',.025)
    box('Iron_Foot',(x,1.90,.39),(.26,.26,.17),'iron',.012)
    beam('Roof_Knee',(x,1.90,1.87),(x,.94,2.34),.13,.15,'edge')
    beam('Roof_Rafter',(x,.52,2.24),(x,2.28,2.75),.16,.17,'wood')
beam('Front_Header',(-2.10,.56,2.22),(2.10,.56,2.22),.16,.18,'edge')
beam('Rear_Header',(-2.10,2.17,2.56),(2.10,2.17,2.56),.16,.18,'wood')
group='Roof'
# The level-one family shares sailcloth shelters, with stock bays uncovered.
def cloth(u,v):
    return Vector((2.16*u,.35+1.95*v,
                   2.40+.45*v-.12*(1-u*u)*math.sin(math.pi*v)-.09*(1-u*u)*(1-v)))
nu,nv=12,8; verts=[]
for dz in [0,-.018]:
    for j in range(nv+1):
        for i in range(nu+1): verts.append(cloth(-1+2*i/nu,j/nv)+Vector((0,0,dz)))
k=(nu+1)*(nv+1); faces=[]; colors=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i; q=(a,a+1,a+nu+2,a+nu+1)
        faces += [q,tuple(v+k for v in reversed(q))]
        colors += ['canvas2' if i in [3,9] else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]
boundary += [nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)]; faces.append((a,b,b+k,a+k)); colors.append('canvas2')
mesh('Canvas_Canopy',verts,faces,colors)
for i in range(20): rod('Rolled_Hem',cloth(-1+2*i/20,0),cloth(-1+2*(i+1)/20,0),.057,'canvas',8)
for side in [-1,1]:
    for i in range(8): rod('Side_Hem',cloth(side,i/8),cloth(side,(i+1)/8),.022,'canvas2',6)
    for v in [0,1]:
        p=cloth(side,v)
        rod('Corner_Tie',p,p+Vector((-.13*side,.07,-.23)),.018,'rope',6)
# One repaired panel gives character without repeating ornamental clutter.
p=[cloth(u,v) for u,v in [(.30,.42),(.57,.42),(.57,.67),(.30,.67)]]
pv=[q+Vector((0,0,dz)) for dz in [.008,.018] for q in p]
mesh('Canvas_Patch',pv,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
for e in range(4):
    a,b=p[e],p[(e+1)%4]; d=(b-a).normalized(); cross=Vector((-d.y,d.x,0))*.028
    for i in range(4):
        mid=a.lerp(b,(i+.5)/4)+Vector((0,0,.025))
        rod('Patch_Stitch',mid-cross,mid+cross,.007,'rope',4)

group='Tool_Wall'
for z in [1.38,1.76]: box('Tool_Rail',(0,1.70,z),(3.46,.11,.12),'edge',.016)
for i,x in enumerate([-.9,-.35,.27,.88]):
    rod('Tool_Peg',(x,1.70,1.73),(x,1.43,1.73),.035,'iron')
    beam('Tool_Handle',(x,1.43,1.16+i*.045),(x,1.43,1.61),.07,.065,'end',.01)
    if i<2:
        beam('Chisel_Steel',(x,1.43,1.01+i*.045),(x,1.43,1.17+i*.045),.055,.05,'steel',.005)
    else:
        box('Hanging_Hammer',(x,1.43,1.64),(.29 if i==2 else .39,.13,.15),'iron',.025)
box('Tool_Shelf',(0,1.39,1.05),(3.42,.62,.12),'wood',.025)

group='Cutting_Bench'
for x in [-.62,.62]: box('Bench_Foot',(x,-.22,.25),(.38,1.24,.5),'stone3',.06)
box('Dressing_Block',(0,-.22,.68),(1.85,1.43,.46),'stone2',.11)
box('Sacrificial_Timber',(0,-.22,.97),(1.65,1.20,.13),'edge',.025)
for x in [-.73,.73]: box('Bench_Strap',(x,-.22,1.044),(.075,1.21,.025),'iron',.008)

group='Lifting_Frame'
# Modest hand-operated jib; rig stays behind the exposed dressing block.
box('Jib_Foot',(-1.31,.69,.16),(.65,.63,.32),'stone3',.07)
box('Jib_Mast',(-1.31,.69,1.47),(.29,.30,2.61),'wood',.025)
beam('Jib_Arm',(-1.47,.69,2.67),(.43,.74,2.67),.24,.24,'edge')
beam('Jib_Brace',(-1.30,.69,1.68),(-.12,.73,2.55),.17,.18,'wood')
for z in [.50,2.45]: box('Mast_Band',(-1.31,.69,z),(.314,.324,.12),'iron',.01)
for x,z in [(-1.31,2.65),(-1.31,1.78),(-.12,2.59)]:
    box('Joint_Plate',(x,.535,z),(.25,.055,.27),'iron',.025)
    rod('Joint_Pin',(x,.475,z),(x,.52,z),.06,'steel',8)
box('Jib_End_Band',(.31,.74,2.67),(.12,.264,.264),'iron',.012)
rod('Winch_Axle',(-1.54,.69,1.12),(-1.05,.69,1.12),.105,'iron')
for i in range(6): rod('Winch_Rope',(-1.46+i*.05,.69,1.12),(-1.423+i*.05,.69,1.12),.132,'rope')
beam('Crank',(-1.60,.69,1.12),(-1.60,.69,.82),.055,.06,'iron',.006)
rod('Crank_Grip',(-1.60,.69,.82),(-1.80,.69,.82),.052,'end')
for i in range(12):
    a=math.tau*i/12; b=math.tau*(i+1)/12
    rod('Winch_Rim',(-1.59,.69+.245*math.cos(a),1.12+.245*math.sin(a)),
        (-1.59,.69+.245*math.cos(b),1.12+.245*math.sin(b)),.026,'iron',6)
for i in range(4):
    a=math.tau*i/4
    rod('Winch_Spoke',(-1.59,.69,1.12),(-1.59,.69+.245*math.cos(a),1.12+.245*math.sin(a)),.023,'iron',6)
rod('Pulley',(.18,.65,2.52),(.18,.83,2.52),.145,'iron',12)
rod('Winch_Lead',(-1.34,.52,1.23),(-1.34,.52,2.47),.024,'rope',6)
rod('Jib_Rope',(-1.34,.52,2.47),(.18,.74,2.64),.024,'rope',6)
rod('Hoist_Line',(.19,.74,2.39),(.19,.74,1.91),.028,'rope',6)
beam('Hook_Spine',(.19,.74,1.92),(.19,.74,1.76),.047,.05,'iron',.005)
beam('Hook_Toe',(.19,.74,1.76),(.31,.74,1.76),.047,.05,'iron',.005)
beam('Hook_Tip',(.31,.74,1.76),(.31,.74,1.85),.047,.05,'iron',.005)

group='Input_Bay'
for x in [-3.30,-1.97]:
    box('Bay_Sleeper',(x,-1.17,.12),(.17,1.96,.24),'wood',.025)
    for y in [-2.08,-.26]: box('Bay_Post',(x,y,.40),(.16,.16,.78),'edge',.025)
    for z in [.30,.57]: box('Bay_Side',(x,-1.17,z),(.10,1.84,.17),'wood',.015)
box('Bay_Back',(-2.64,-.26,.38),(1.43,.11,.42),'edge',.02)
box('Bay_Lip',(-2.64,-2.08,.19),(1.43,.11,.21),'edge',.02)

group='Output_Pallet'
for x in [2.05,3.18]: box('Pallet_Sleeper',(x,-1.18,.095),(.20,1.93,.19),'wood',.02)
for i in range(7): box('Pallet_Deck',(2.615,-2.04+i*.285,.23),(1.5,.26,.10),'end',.012)
for x in [1.92,3.31]:
    box('Pallet_EndPost',(x,-.31,.58),(.14,.16,.90),'wood',.02)
box('Pallet_Back',(2.615,-.31,.67),(1.50,.10,.25),'teal',.018)

group='Sign'
outline=[(.94,1.78),(1.06,1.67),(1.57,1.70),(1.67,1.82),(1.64,2.13),(1.04,2.15),(.95,2.05)]
n=len(outline)
mesh('Trade_Sign',[(x,y,z) for y in [.45,.55] for x,z in outline],
     [tuple(reversed(range(n))),tuple(n+i for i in range(n))]+
     [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'teal')
for x in [1.08,1.54]:
    beam('Sign_Hanger',(x,.49,2.11),(x,.49,2.25),.026,.035,'iron',.004)
beam('Hammer_Symbol',(1.15,.425,1.80),(1.41,.425,2.05),.055,.025,'cream',.006)
beam('Hammer_Head',(1.31,.42,2.11),(1.51,.42,1.92),.11,.035,'cream',.008)

for i,(x,y,z) in enumerate([(-2.97,-1.71,.57),(-2.37,-1.64,.57),(-2.98,-.83,.60),(-2.35,-.74,.58),(-2.67,-1.23,1.08)]):
    group=f'Input_Stone_{i+1:02d}'; rock(group,(x,y,z),(.62,.72,.62),i)
for i in range(12):
    group=f'Output_Brick_{i+1:02d}'
    layer=i//6; cell=i%6
    box(group,(2.26+(cell%2)*.70,-1.79+(cell//2)*.55,.43+layer*.31),(.65,.50,.29),'cut' if i%3 else 'cut2',.035)
group='Bench_Loaded'; rock(group,(0,-.22,1.34),(1.17,.86,.57),11)
group='Bench_Cutting'
box('Squared_Work',(0,-.22,1.29),(1.06,.74,.47),'cut2',.065)
for x in [-.24,.24]:
    for y in [-.44,-.20,.04]: beam('Splitting_Wedge',(x,y,1.51),(x,y,1.62),.032,.055,'iron',.004)
group='Bench_Finished'
for x in [-.36,.0,.36]: box('Finished_Block',(x,-.22,1.24),(.32,.70,.37),'cut',.025)
group='Mallet_Tool'
beam('Mallet_Handle',(.52,-.88,1.05),(.93,-.67,1.09),.065,.07,'end')
box('Mallet_Head',(.93,-.67,1.12),(.24,.30,.17),'iron',.035)

# Join only functional modules, keeping inventory slots and work variants independent.
modules={}
groups={}
for ob in objects: groups.setdefault(ob['module'],[]).append(ob)
for name,items in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in items: ob.select_set(True)
    bpy.context.view_layer.objects.active=items[0]
    if len(items)>1: bpy.ops.object.join()
    ob=items[0]; ob.name=name; modules[name]=ob
modules['Lifting_Frame'].location.y-=1.05
objects=list(modules.values())
input_anchor=marker('Input_Container',(-2.64,-1.17,0),'rough_stone_inventory')
output_anchor=marker('Output_Container',(2.615,-1.18,0),'brick_inventory')
bench_anchor=marker('Bench_Anchor',(0,-.22,1.04),'work_in_progress')
marker('Worker_Stand',(0,-1.53,0),'worker_faces_positive_y')
marker('Input_Pickup',(-2.64,-2.48,0),'pickup')
marker('Output_Dropoff',(2.615,-2.48,0),'dropoff')
bpy.context.view_layer.update()
for name,ob in modules.items():
    parent=input_anchor if name.startswith('Input_Stone_') else output_anchor if name.startswith('Output_Brick_') else bench_anchor if name.startswith('Bench_') or name=='Mallet_Tool' else None
    if parent is not None:
        world=ob.matrix_world.copy(); ob.parent=parent; ob.matrix_world=world
        bpy.context.view_layer.update()
        bpy.context.view_layer.objects.active=ob
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY',center='BOUNDS')

def state(inputs, outputs, work):
    for name,ob in modules.items():
        hidden=False
        if name.startswith('Input_Stone_'): hidden=int(name.rsplit('_',1)[1])>inputs
        if name.startswith('Output_Brick_'): hidden=int(name.rsplit('_',1)[1])>outputs
        if name.startswith('Bench_'): hidden=name!='Bench_'+work
        if name=='Mallet_Tool': hidden=work not in ['Loaded','Cutting']
        ob.hide_render=hidden; ob.hide_set(hidden)

report={}; points=[]
bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new(); bm.from_mesh(ob.data)
    report[name]={'triangles':sum(len(f.verts)-2 for f in bm.faces),
                  'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
                  'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert report[name]['nonmanifold_edges']==0 and report[name]['degenerate_faces']==0,(name,report[name])
    assert not any(p.use_smooth for p in ob.data.polygons)
    points += [ob.matrix_world@v.co for v in ob.data.vertices]; bm.free()
lo=[min(v[i] for v in points) for i in range(3)]; hi=[max(v[i] for v in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<=3.7 and max(abs(lo[1]),abs(hi[1]))<=2.9 and hi[2]<=2.9,(lo,hi)
validation={'modules':report,'bounds':[lo,hi],'all_variant_triangles':sum(v['triangles'] for v in report.values())}
(OUT/'validation.json').write_text(json.dumps(validation,indent=2))
states={'empty':(0,0,'Empty'),'ready':(5,0,'Empty'),'working':(4,3,'Cutting'),'output-ready':(2,8,'Empty'),'output-blocked':(0,12,'Finished')}
contract={'input_display_slots':5,'output_display_slots':12,'slots_are_not_gameplay_capacity':True,
          'default_state':'empty','bench_variants':['Bench_Loaded','Bench_Cutting','Bench_Finished'],
          'production_rules':['Require rough stone before starting; move it out of the input bay when loaded.',
                              'Finish an existing job even with empty input storage.',
                              'If output is full, retain finished work on the bench and block the next job.'],
          'integration':'Art staging only. No Unity assets, production code, or game scenes modified.'}
(OUT/'state-contract.json').write_text(json.dumps(contract,indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name, items):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in items: ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('quarry-state-kit.fbx',objects+empties)
state(0,0,'Empty'); export('quarry-empty.fbx',[o for o in objects if not o.hide_render]+empties)
state(4,6,'Cutting'); export('quarry-review.fbx',[o for o in objects if not o.hide_render]+empties)
scene=bpy.context.scene; ss.setup_render(scene); scene.view_settings.exposure=.65
scene.display.shading.show_shadows=False
scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Review',bpy.data.cameras.new('Review')); scene.collection.objects.link(cam)
scene.camera=cam; cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),res=(1400,1100)):
    cam.location=eye; cam.rotation_euler=(Vector((0,0,1.15))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=8.4; scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.resolution_percentage=100; scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
for name,args in states.items(): state(*args); render('state-'+name)
state(4,6,'Cutting'); render('front-three-quarter'); render('front',(0,-14,7))
render('opposite-three-quarter',(-8,-12,8)); render('game-scale',res=(360,280))
render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.color_type='VERTEX'
            area.spaces.active.region_3d.view_location=(0,0,1.15)
            area.spaces.active.region_3d.view_distance=10
            area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
root['preview_only']='Stock shown is a staged art review, not live inventory.'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print(json.dumps(validation,indent=2))
