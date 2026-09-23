"""Canvas-roof storage hut, art staging only."""
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
OUT=HERE.parents[1]/'art-staging/storage-astra-lvl1-v1';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Storage_Level_1',{'wood':'#80553C','edge':'#AF794B','plank':'#B78C59','end':'#D2AD73','iron':'#454A4E','stone':'#88989B','canvas':'#E7D9B9','canvas2':'#D8C69F','patch':'#C7B68F','rope':'#BDA275','teal':'#648E86','dark':'#3E4C4B'})
k.root['footprint_xz']=[6.46,5.14]
k.group='Platform'
for x in [-2.25,0,2.25]:
    for y in [-.20,1.83]:k.box('Footing',(x,y,.13),(.52,.50,.26),'stone',.06)
    k.box('Floor_Joist',(x,.78,.28),(.18,2.65,.18),'wood',.02)
for i in range(18):k.box('Floorboard',(-2.45+i*.288,.78,.42),(.275,2.63,.13),'plank' if i%3 else 'edge',.012)
k.box('Threshold',(0,-.565,.43),(5.25,.16,.20),'edge',.025)
k.box('Step',(0,-.88,.16),(1.36,.50,.32),'wood',.03)
k.group='Frame'
for x in [-2.30,2.30]:
    for y in [-.20,1.83]:
        k.box('Upright',(x,y,1.43),(.20,.22,2.06),'wood',.025)
        k.box('Iron_Shoe',(x,y,.59),(.224,.244,.17),'iron',.01)
        k.beam('Knee_Brace',(x,y,1.82),(x-math.copysign(.48,x),y,2.38),.12,.14,'edge')
    k.beam('Eave_Rail',(x,-.36,2.40),(x,2.08,2.40),.14,.15,'edge')
for y in [-.20,1.83]:
    k.beam('Tie_Beam',(-2.36,y,2.35),(2.36,y,2.35),.14,.17,'wood')
    for side in [-1,1]:k.beam('Roof_Rafter',(2.30*side,y,2.42),(0,y,3.33),.12,.14,'wood')
    k.beam('King_Post',(0,y,2.35),(0,y,3.35),.13,.13,'edge')
k.beam('Ridge',(0,-.40,3.37),(0,2.13,3.37),.13,.14,'wood')
# Low rear slats leave the hut open and keep supplies off the edge.
for z in [.69,.96,1.23]:k.box('Back_Slat',(0,1.95,z),(4.68,.10,.20),'edge',.015)
for x in [-2.34,2.34]:
    for z in [.69,.96]:k.box('Side_Slat',(x,.85,z),(.10,2.15,.20),'wood')
k.group='Canopy'
def cloth(u,v):
    return Vector((2.57*u,-.47+2.73*v,3.55-1.01*abs(u)-.065*math.sin(math.pi*abs(u))-.045*math.sin(math.pi*v)))
nu,nv=20,10;verts=[]
for dz in [0,-.022]:
    for j in range(nv+1):
        for i in range(nu+1):verts.append(cloth(-1+2*i/nu,j/nv)+Vector((0,0,dz)))
count=(nu+1)*(nv+1);faces=[];cols=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1)
        faces += [q,tuple(v+count for v in reversed(q))];cols += ['teal' if i in [1,18] else 'canvas2' if i in [5,14] else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+count,a+count));cols.append('canvas2')
k.mesh('Tarp',verts,faces,cols)
for side in [-1,1]:
    for i in range(12):k.rod('Rolled_Eave',cloth(side,i/12),cloth(side,(i+1)/12),.052,'canvas',8)
for i in range(20):k.rod('Front_Hem',cloth(-1+2*i/20,0),cloth(-1+2*(i+1)/20,0),.027,'canvas2',6)
p=[cloth(u,v) for u,v in [(.36,.34),(.57,.34),(.57,.57),(.36,.57)]]
k.mesh('Repair_Patch',[q+Vector((0,0,z)) for z in [.012,.026] for q in p],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
k.group='Ropes'
for x in [-1,1]:
    for v in [0,1]:
        a=cloth(x,v);b=Vector((x*2.94,a.y,.18))
        k.rod('Guy_Rope',a,b,.018,'rope',6);k.beam('Ground_Peg',b-Vector((0,0,.16)),b+Vector((.04*x,0,.14)),.075,.075,'edge')
k.group='Front_Racks'
for x in [-1.84,1.84]:
    for y in [-1.92,-.77]:
        k.box('Rack_Sleeper',(x,y,.12),(1.48,.15,.24),'wood',.02)
        for dx in [-.63,.63]:k.box('Rack_Post',(x+dx,y,.46),(.12,.12,.81),'edge',.02)
    for dx in [-.65,.65]:k.box('Rack_Side',(x+dx,-1.35,.41),(.10,1.22,.17),'wood')
    for i in range(5):k.box('Rack_Floor',(x,-1.91+i*.28,.28),(1.40,.26,.09),'plank',.01)
k.group='Shelving'
for x in [-1.69,1.69]:
    for z in [.59,1.24]:k.box('Shelf',(x,1.12,z),(1.04,1.15,.10),'plank',.02)
    for dx in [-.45,.45]:
        for y in [.59,1.64]:k.box('Shelf_Post',(x+dx,y,.98),(.10,.10,1.04),'wood')
k.group='Sign'
k.box('Supply_Sign',(.75,-.31,2.10),(.64,.11,.46),'teal',.04)
for x in [.51,.99]:k.beam('Sign_Strap',(x,-.31,2.29),(x,-.31,2.44),.035,.04,'iron')
for x in [.56,.94]:k.box('Crate_Icon_Side',(x,-.38,2.10),(.036,.025,.24),'end',.003)
for z in [1.98,2.22]:k.box('Crate_Icon_Edge',(.75,-.38,z),(.41,.025,.035),'end',.003)
k.beam('Crate_Icon_Brace',(.58,-.38,2.00),(.92,-.38,2.20),.032,.025,'end')
for i,(x,z) in enumerate([(-2.24,.47),(-1.84,.47),(-1.44,.47),(-2.04,.80),(-1.64,.80)]):
    k.group=f'Stock_Timber_{i+1:02d}'
    k.rod('Log',(x,-1.99,z),(x,-.66,z),.18,'wood',10)
    # Ends are inset inside the bark cap, with no coplanar faces.
    k.rod('Endgrain',(x,-2.005,z),(x,-1.991,z),.145,'end',10)
for i in range(8):
    k.group=f'Stock_Boards_{i+1:02d}'
    k.box('Plank',(1.52+(i%2)*.65,-1.34,.40+(i//2)*.13),(.59,1.31,.115),'plank' if i%3 else 'end',.018)
for i,(x,y,z) in enumerate([(-1.88,.88,.65),(-1.46,1.27,.65),(-1.78,1.11,1.29)]):
    k.group=f'Stock_Food_{i+1:02d}'
    k.lathe('Sack',(x,y,z),[(.16,0),(.225,.09),(.235,.31),(.18,.47),(.065,.54),(.072,.60)],['canvas2','canvas','canvas2'],9)
    k.lathe('Sack_Tie',(x,y,z),[(.078,.53),(.078,.56)],'rope',9)
for i,(x,y,z) in enumerate([(1.69,.94,.87),(1.69,1.09,1.52)]):
    k.group=f'Stock_Cargo_{i+1:02d}'
    k.box('Crate',(x,y,z),(.76,.66,.46),'wood',.025)
    for dx in [-.32,.32]:k.box('Crate_Batten',(x+dx,y-.345,z),(.085,.055,.46),'edge')
    for dz in [-.18,.18]:k.box('Crate_Rail',(x,y-.35,z+dz),(.76,.055,.08),'plank')
    k.beam('Crate_Diagonal',(x-.27,y-.39,z-.14),(x+.27,y-.39,z+.14),.07,.045,'edge')
modules=k.join_modules()
for name,ob in modules.items():
    if name.startswith('Stock_'):
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob;bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY',center='BOUNDS')
k.marker('Entry',(0,-1.55,0),'approach; navigation not validated')
k.marker('Storage_Anchor',(0,.72,.485),'storage reference')
def state(full):
    for name,ob in modules.items():
        hide=name.startswith('Stock_') and not full;ob.hide_render=hide;ob.hide_set(hide)
report={};points=[];bpy.context.view_layer.update()
for vertex in modules['Frame'].data.vertices:
    p=modules['Frame'].matrix_world@vertex.co
    if abs(p.x)<=2.57 and -.47<=p.y<=2.26:
        assert p.z<cloth(p.x/2.57,(p.y+.47)/2.73).z-.025,('Frame intersects canopy',tuple(p))
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data);d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons);report[name]=d
    points += [ob.matrix_world@v.co for v in ob.data.vertices];bm.free()
lo=[min(v[i] for v in points) for i in range(3)];hi=[max(v[i] for v in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<=3.23 and max(abs(lo[1]),abs(hi[1]))<=2.57 and hi[2]<=3.84,(lo,hi)
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_stock_triangles':sum(v['triangles'] for v in report.values()),'bounds':[lo,hi]},indent=2))
slots={kind:[name for name in modules if name.startswith('Stock_'+kind+'_')] for kind in ['Timber','Boards','Food','Cargo']}
(OUT/'state-contract.json').write_text(json.dumps({'default_state':'empty','display_slots':slots,'slots_are_not_gameplay_capacity':True,'generic_cargo_mapping':'Unassigned; do not count crates as a specific resource without gameplay mapping.','integration':'No inventory code or game import. Storage does not process resources. Read README.md.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in k.objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
state(True);export('storage-state-kit.fbx');state(False);export('storage-empty.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Storage_Review',bpy.data.cameras.new('Storage_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(8,-12,8),res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,0,1.35))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=7.8
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('state-empty');state(True);render('front-three-quarter');render('front',(0,-14,6));render('opposite-three-quarter',(-8,-12,8));render('game-scale',res=(360,280));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.color_type='VERTEX';area.spaces.active.region_3d.view_location=(0,0,1.35);area.spaces.active.region_3d.view_distance=8;area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/storage-astra-lvl1-v1.blend'))
print('PASS',sum(v['triangles'] for v in report.values()),lo,hi)
