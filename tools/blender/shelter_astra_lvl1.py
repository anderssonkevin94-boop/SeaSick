"""Two-berth canvas crew shelter, staged art only."""
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
OUT=HERE.parents[1]/'art-staging/shelter-astra-lvl1-v1';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Crew_Shelter_Level_1',{'wood':'#81573D','edge':'#B48250','plank':'#B99061','iron':'#464D50','stone':'#8A9699','canvas':'#E8D9B9','canvas2':'#D8C49E','patch':'#C8B18A','rope':'#B99A6E','teal':'#648F89','blue':'#617F9B','red':'#AD6D5D','cream':'#EDE1CA','dark':'#485552'})
k.root['footprint_xz']=[4.84,4.93]
k.group='Foundation'
for x in [-1.40,1.40]:
    for y in [-1.35,1.35]:k.box('Foot_Stone',(x,y,.10),(.42,.45,.20),'stone',.055)
    k.box('Sleeper',(x,0,.20),(.16,3.24,.16),'wood')
for i in range(13):k.box('Floor_Plank',(-1.53+i*.255,0,.31),(.242,3.25,.12),'plank' if i%3 else 'edge',.012)
k.box('Threshold',(0,-1.67,.30),(3.28,.12,.16),'edge',.02)
k.box('Step',(0,-1.91,.12),(1.28,.41,.24),'wood',.028)
k.group='Frame'
for x in [-1.49,1.49]:
    for y in [-1.43,1.42]:
        k.box('Corner_Post',(x,y,.96),(.16,.17,1.35),'wood',.022)
        k.box('Post_Band',(x,y,.52),(.177,.19,.13),'iron',.008)
    k.beam('Eave_Support',(x,-1.50,1.59),(x,1.55,1.59),.12,.12,'wood')
for y in [-1.40,1.41]:
    for side in [-1,1]:k.beam('Rafter',(side*1.49,y,1.60),(0,y,2.68),.11,.12,'wood')
k.beam('Ridge',(0,-1.55,2.69),(0,1.57,2.69),.11,.12,'edge')
# Doorway posts support a raised lintel without a low crossbeam across the opening.
for x in [-.74,.74]:k.box('Door_Post',(x,-1.43,1.27),(.12,.13,1.86),'edge')
k.beam('Door_Lintel',(-.81,-1.43,2.17),(.81,-1.43,2.17),.11,.12,'wood')
k.group='Lower_Walls'
for x in [-1.56,1.56]:
    for z in [.51,.74]:k.box('Side_Plank',(x,0,z),(.085,3.08,.20),'edge')
for z in [.51,.74]:k.box('Rear_Plank',(0,1.54,z),(3.18,.085,.20),'edge')

def panel(name,fn,nu,nv,thickness,color,axis=Vector((0,0,1))):
    verts=[fn(i/nu,j/nv)+axis*offset for offset in [0,-thickness] for j in range(nv+1) for i in range(nu+1)]
    count=(nu+1)*(nv+1);faces=[];cols=[]
    for j in range(nv):
        for i in range(nu):
            a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1)
            faces += [q,tuple(v+count for v in reversed(q))];c=color(i,j) if callable(color) else color;cols += [c,c]
    boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
    for j,a in enumerate(boundary):
        b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+count,a+count));cols.append('canvas2')
    return k.mesh(name,verts,faces,cols)
def roof(u,v):
    x=-1.80+3.60*u
    return Vector((x,-1.72+3.46*v,2.89-1.11*abs(x/1.8)-.055*math.sin(math.pi*abs(x/1.8))-.035*math.sin(math.pi*v)))
k.group='Canopy'
panel('Sailcloth',roof,20,12,.024,lambda i,j:'teal' if i in [1,18] else 'canvas2' if i in [5,14] else 'canvas')
for side in [0,1]:
    for j in range(16):k.rod('Eave_Roll',roof(side,j/16),roof(side,(j+1)/16),.045,'canvas',8)
for i in range(20):k.rod('Front_Hem',roof(i/20,0),roof((i+1)/20,0),.026,'canvas2',6)
panel('Roof_Patch',lambda u,v:roof(.64+.12*u,.57+.15*v)+Vector((0,0,.025)),3,3,.008,'patch')
k.group='Canvas_Walls'
for side in [-1,1]:
    panel('Side_Canvas',lambda u,v:Vector((side*(1.56+.035*math.sin(math.pi*u)*math.sin(math.pi*v)),-1.43+2.98*u,.84+.90*v)),10,4,.02,'canvas2',Vector((side,0,0)))
panel('Back_Canvas',lambda u,v:Vector((-1.56+3.12*u,1.52,.84+v*(2.70-1.0*abs(2*u-1)-.84))),12,6,.02,'canvas2',Vector((0,1,0)))
k.group='Ropes'
for side in [0,1]:
    for v in [0,1]:
        a=roof(side,v);b=Vector((-2.12 if side==0 else 2.12,a.y+(.18 if v else -.18),.16))
        k.rod('Guy_Rope',a,b,.018,'rope',6);k.beam('Peg',b-Vector((0,0,.15)),b+Vector((.02,0,.15)),.07,.07,'edge')

k.group='Entrance_Open'
for side in [-1,1]:
    rings=[(.37,1.23,.16),(.74,1.36,.17),(1.18,1.38,.055),(1.60,1.20,.18),(2.11,.85,.26)]
    verts=[];n=10
    for z,x,r in rings:
        verts += [(side*x+r*math.cos(i*math.tau/n),-1.55+.055*math.sin(i*math.tau/n),z) for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple(4*n+i for i in range(n))]+[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(4) for i in range(n)]
    k.mesh('Gathered_Flap',verts,faces,['canvas','canvas2','canvas'])
    for j in range(10):
        a=math.tau*j/10;b=math.tau*(j+1)/10
        k.rod('Flap_Tie',(side*1.38+.065*math.cos(a),-1.55+.065*math.sin(a),1.18),(side*1.38+.065*math.cos(b),-1.55+.065*math.sin(b),1.18),.013,'rope',5)
    k.rod('Tie_Tail',(side*1.38,-1.62,1.18),(side*1.36,-1.64,1.01),.012,'rope',5)
k.group='Entrance_Closed'
panel('Closed_Curtain',lambda u,v:Vector((-1.39+2.78*u,-1.58-.045*math.sin(6*math.pi*u)*math.sin(math.pi*v),.37+1.76*v)),16,8,.022,lambda i,j:'canvas2' if i in [7,8] else 'canvas',Vector((0,-1,0)))

for side,color in [(-1,'blue'),(1,'red')]:
    k.group='Bed_Left' if side<0 else 'Bed_Right'
    x=side*.91
    k.box('Bedroll',(x,.13,.48),(.78,1.93,.22),'canvas2',.075)
    k.box('Blanket',(x,-.10,.61),(.79,1.43,.075),color,.025)
    k.box('Blanket_Fold',(x,.49,.66),(.79,.18,.055),color,.02)
    k.box('Pillow',(x,.85,.64),(.56,.35,.17),'cream',.065)
    k.box('Blanket_Hem',(x,-.77,.656),(.73,.028,.012),'canvas2',.003)
    k.group='Chest_Left' if side<0 else 'Chest_Right'
    k.box('Sea_Chest',(x,-1.10,.56),(.57,.39,.37),'wood',.045)
    k.box('Chest_Lid',(x,-1.10,.76),(.61,.42,.08),'edge',.035)
    for dx in [-.20,.20]:k.box('Chest_Band',(x+dx,-1.305,.56),(.045,.025,.30),'iron',.004)
    k.box('Latch',(x,-1.324,.66),(.065,.024,.11),'iron',.007)
k.group='Porch_Details'
k.box('Boot_Scraper',(.93,-1.92,.13),(.42,.22,.26),'wood',.02)
for x in [.82,.94,1.06]:k.box('Scraper_Slat',(x,-1.92,.276),(.055,.20,.03),'iron',.005)
k.group='Lantern'
k.beam('Lantern_Bracket',(-1.5,-1.43,1.61),(-1.5,-1.87,1.61),.06,.06,'iron')
k.box('Opaque_Glass',(-1.5,-1.84,1.30),(.15,.14,.23),'cream',.008)
for z in [1.16,1.44]:k.box('Lantern_Cap',(-1.5,-1.84,z),(.23,.21,.065),'iron',.018)
for dx in [-.082,.082]:
    for dy in [-.076,.076]:k.beam('Lantern_Frame',(-1.5+dx,-1.84+dy,1.18),(-1.5+dx,-1.84+dy,1.44),.019,.019,'iron')
k.rod('Lantern_Hanger',(-1.5,-1.84,1.47),(-1.5,-1.84,1.61),.018,'iron',6)
modules=k.join_modules()
k.marker('Entry',(0,-2.19,0),'approach only; navigation unvalidated')
for side in [-1,1]:k.marker('Sleep_Left' if side<0 else 'Sleep_Right',(side*.91,.13,.59),'pose reference; head toward source +Y')
k.marker('Interior',(0,0,.37),'interior reference')
def state(closed):
    for name,ob in modules.items():
        hide=(name=='Entrance_Open' and closed) or (name=='Entrance_Closed' and not closed)
        ob.hide_render=hide;ob.hide_set(hide)
report={};points=[];bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data);d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons);report[name]=d;points += [ob.matrix_world@v.co for v in ob.data.vertices];bm.free()
for vertex in modules['Frame'].data.vertices:
    p=modules['Frame'].matrix_world@vertex.co
    assert p.z<roof((p.x+1.8)/3.6,(p.y+1.72)/3.46).z-.025,('Frame/canvas clearance',tuple(p))
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<2.42 and max(abs(lo[1]),abs(hi[1]))<2.465 and hi[2]<3.81
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_variant_triangles':sum(d['triangles'] for d in report.values()),'bounds':[lo,hi]},indent=2))
(OUT/'state-contract.json').write_text(json.dumps({'default_state':'open','mutually_exclusive':['Entrance_Open','Entrance_Closed'],'beds':'Two fixed bedrolls, not a resident count display. No occupants are included.','lantern':'Opaque unlit prop, no light or flame effect.','integration':'Art only. No housing, sleep, navigation, or occupancy code supplied.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in k.objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
for ob in k.objects:ob.hide_render=False;ob.hide_set(False)
export('shelter-state-kit.fbx');state(False);export('shelter-open.fbx');state(True);export('shelter-closed.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Shelter_Review',bpy.data.cameras.new('Shelter_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(6,-10,5.6),res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,0,1.1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=5.8
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('state-closed');state(False);render('front-three-quarter');render('front',(0,-12,4));render('opposite-three-quarter',(-6,-10,5.6));render('game-scale',res=(360,280));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,0,1.1);s.region_3d.view_distance=6;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/shelter-astra-lvl1-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
