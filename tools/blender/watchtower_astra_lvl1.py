"""Compact timber lookout, clear ladder arrival and canvas roof."""
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
OUT=HERE.parents[1]/'art-staging/watchtower-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Watchtower_Level_1',{'wood':'#77553E','edge':'#A98054','plank':'#BE9A69','iron':'#485052','steel':'#899897','stone':'#909C97','canvas':'#E3D7B8','canvas2':'#CCBB96','patch':'#B6B69C','rope':'#B6A17A','teal':'#648D85','red':'#B96D53','brass':'#B29B60'})
k.root['footprint_xz']=[2.6,2.6]
k.group='Footings'
for x in [-.95,.95]:
    for y in [-.95,.95]:
        k.box('Stone_Foot',(x,y,.17),(.48,.48,.34),'stone',.055)
        k.box('Iron_Shoe',(x,y,.40),(.29,.29,.27),'iron',.018)
k.group='Tower_Frame'
for sx in [-1,1]:
    for sy in [-1,1]:
        k.beam('Main_Leg',(sx*.95,sy*.95,.29),(sx*.84,sy*.84,4.48),.24,.24,'wood')
for z in [1.72,3.25,4.32]:
    t=(z-.29)/(4.48-.29);w=.95-.11*t
    for s in [-1,1]:
        k.beam('Tie_Beam',(-w,s*w,z),(w,s*w,z),.16,.18,'edge')
        k.beam('Tie_Beam',(s*w,-w,z),(s*w,w,z),.16,.18,'edge')
# Paired diagonals occupy adjacent depths so the crossing is not coplanar.
for z0,z1 in [(.58,1.64),(1.84,3.17),(3.38,4.26)]:
    for side in [-1,1]:
        k.beam('Side_Diagonal',(side*.88,-.80,z0),(side*.88,.80,z1),.115,.12,'wood')
        k.beam('Side_Diagonal',(side*1.015,.80,z0),(side*1.015,-.80,z1),.115,.12,'wood')
    k.beam('Rear_Diagonal',(-.80,.90,z0),(.80,.90,z1),.115,.12,'wood')
    k.beam('Rear_Diagonal',(.80,1.035,z0),(-.80,1.035,z1),.115,.12,'wood')
    # Front chevron leaves the ladder visually legible.
    for s in [-1,1]:k.beam('Front_Chevron',(s*.80,-.91,z0),(0,-.91,z1),.11,.12,'wood')
k.group='Joinery'
for z in [1.72,3.25,4.32]:
    for x in [-.86,.86]:
        k.box('Strap',(x,-1.015,z),(.22,.055,.32),'iron',.01)
        for dz in [-.105,.105]:k.rod('Peg',(x,-1.05,z+dz),(x,-1.064,z+dz),.031,'steel',6)
k.group='Platform'
for x in [-.84,0,.84]:k.box('Deck_Joist',(x,0,4.39),(.18,2.22,.22),'wood',.025)
for i in range(10):k.box('Deck_Plank',(-1.03+i*.229,0,4.55),(.214,2.24,.12),'plank' if i%3 else 'edge',.012)
for y in [-1.12,1.12]:k.box('Fascia',(0,y,4.43),(2.29,.12,.26),'edge',.02)
k.group='Guardrails'
for x in [-1.02,1.02]:
    for y in [-1.02,1.02]:k.box('Upper_Post',(x,y,5.37),(.15,.15,1.63),'wood',.018)
for z in [4.83,5.50]:
    k.box('Rear_Rail',(0,1.02,z),(2.15,.13,.15),'teal' if z>5 else 'edge',.016)
    for x in [-1.02,1.02]:k.box('Side_Rail',(x,0,z),(.13,1.98,.15),'teal' if z>5 else 'edge',.016)
    for x in [-.745,.745]:k.box('Entry_Rail',(x,-1.02,z),(.55,.13,.15),'teal' if z>5 else 'edge',.016)
for x in [-.445,.445]:k.box('Entry_Post',(x,-1.02,5.06),(.105,.12,1.02),'wood',.012)
for x in [-1.02,1.02]:
    for y in [-.5,0,.5]:k.box('Side_Baluster',(x,y,5.16),(.075,.085,.60),'edge',.01)
for x in [-.5,0,.5]:k.box('Rear_Baluster',(x,1.02,5.16),(.08,.08,.60),'edge',.01)
k.group='Ladder'
def ladder_y(z):return -1.22+.025*z
for x in [-.325,.325]:k.beam('Ladder_Stile',(x,ladder_y(.05),.05),(x,ladder_y(5.48),5.48),.075,.085,'wood')
for i in range(16):
    z=.25+i*.278;k.rod('Rung',(-.325,ladder_y(z)-.014,z),(.325,ladder_y(z)-.014,z),.031,'edge',8)
for z in [1.72,3.25,4.37]:
    for x in [-.325,.325]:k.beam('Ladder_Bracket',(x,ladder_y(z)+.025,z),(x,-.93,z),.05,.06,'iron')
k.group='Roof_Frame'
for x in [-1.02,1.02]:
    for y in [-1.02,1.02]:k.box('Roof_Post',(x,y,6.22),(.15,.15,.30),'wood',.018)
for y in [-1.02,1.02]:
    k.beam('Gable_Tie',(-1.09,y,6.35),(1.09,y,6.35),.12,.14,'edge')
    for s in [-1,1]:
        k.beam('Rafter',(s*1.09,y,6.35),(0,y,6.94),.105,.12,'wood')
        k.beam('Roof_Knee',(s*1.02,y,5.92),(s*.66,y,6.35),.09,.10,'wood')
k.beam('Ridge_Pole',(0,-1.16,6.99),(0,1.16,6.99),.12,.13,'wood')
k.group='Canopy'
def cloth(u,v):
    x=-1.24+2.48*u;y=-1.24+2.48*v
    return Vector((x,y,6.55+.65*(1-abs(x)/1.24)-.055*math.sin(math.pi*v)*math.sin(math.pi*u)))
nu,nv=16,10
verts=[cloth(i/nu,j/nv)+Vector((0,0,z)) for z in [0,-.025] for j in range(nv+1) for i in range(nu+1)]
cnt=(nu+1)*(nv+1);faces=[];cols=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;q=(a,a+1,a+nu+2,a+nu+1)
        faces += [q,tuple(v+cnt for v in reversed(q))];cols += ['teal' if j in [1,8] else 'canvas','canvas2']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for j,a in enumerate(boundary):
    b=boundary[(j+1)%len(boundary)];faces.append((a,b,b+cnt,a+cnt));cols.append('canvas2')
k.mesh('Canvas',verts,faces,cols)
for u in [0,1]:
    for i in range(16):k.rod('Eave_Hem',cloth(u,i/16),cloth(u,(i+1)/16),.028,'canvas2',7)
p=[cloth(u,v) for u,v in [(.67,.35),(.86,.35),(.86,.60),(.67,.60)]]
k.mesh('Patch',[q+Vector((0,0,z)) for z in [.014,.025] for q in p],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
for e in range(4):
    a,b=p[e],p[(e+1)%4];d=(b-a).normalized();cross=Vector((-d.y,d.x,0))*.026
    for i in range(4):
        mid=a.lerp(b,(i+.5)/4)+Vector((0,0,.037));k.rod('Stitch',mid-cross,mid+cross,.007,'rope',4)
for u in [0,1]:
    for v in [0,1]:k.rod('Canvas_Tie',cloth(u,v),Vector(((-1 if u==0 else 1)*1.02,(-1 if v==0 else 1)*1.02,6.33)),.014,'rope',6)
k.group='Bell_Bracket'
k.beam('Bell_Hanger',(.78,-1.02,6.35),(.78,-.82,6.04),.06,.08,'iron')
k.group='Signal_Bell'
rings=[(.20,5.76),(.20,5.81),(.145,5.88),(.09,6.02),(.05,6.04),(.043,6.00),(.066,5.96),(.12,5.85),(.166,5.80),(.166,5.76)]
verts=[(.78+r*math.cos(i*math.tau/12),-.82+r*math.sin(i*math.tau/12),z) for r,z in rings for i in range(12)]
faces=[(j*12+i,j*12+(i+1)%12,((j+1)%len(rings))*12+(i+1)%12,((j+1)%len(rings))*12+i) for j in range(len(rings)) for i in range(12)]
k.mesh('Hollow_Bell',verts,faces,'brass')
k.rod('Clapper',(.78,-.82,5.99),(.78,-.82,5.73),.023,'iron',8)
k.lathe('Clapper_End',(.78,-.82,0),[(.032,5.70),(.044,5.73),(.022,5.76)],'iron',8)
k.group='Bell_Rope'
k.rod('Pull_Rope',(.78,-.82,5.72),(.78,-.82,5.14),.012,'rope',6)
k.lathe('Rope_Grip',(.78,-.82,0),[(.023,5.10),(.027,5.14),(.020,5.21)],'wood',8)
k.group='Flag_Pole'
k.rod('Signal_Pole',(-.77,.78,4.61),(-.77,.78,6.20),.024,'wood',8)
for z in [4.85,5.45]:k.box('Pole_Clip',(-.77,.93,z),(.085,.19,.055),'iron',.01)
k.group='Flag_Stowed'
k.rod('Rolled_Signal',(-.77,.78,5.61),(-.77,.78,6.12),.049,'red',9)
for z in [5.70,6.02]:k.lathe('Flag_Tie',(-.77,.78,0),[(.054,z),(.054,z+.025)],'rope',8)
k.group='Flag_Alert'
verts=[(-.77,.78,6.12),(-.10,.70,6.05),(-.22,.71,5.82),(-.07,.70,5.60),(-.77,.78,5.64)]
k.mesh('Signal_Flag',verts+[(x,y+.014,z) for x,y,z in verts],[(0,1,2,3,4),(9,8,7,6,5)]+[(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)],'red')
modules=k.join_modules()
for name,p,role in [('Ladder_Bottom',(0,-1.23,.05),'climb start reference'),('Ladder_Top',(0,-1.10,4.61),'climb arrival reference'),('Lookout_Anchor',(0,.05,4.61),'standing floor reference'),('Bell_Anchor',(.78,-.82,6.04),'bell suspension pivot reference')]:k.marker(name,p,role)
report={};points=[];bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;points.extend(ob.matrix_world@v.co for v in ob.data.vertices);bm.free()
for v in modules['Roof_Frame'].data.vertices:
    p=modules['Roof_Frame'].matrix_world@v.co
    assert p.z<cloth((p.x+1.24)/2.48,(p.y+1.24)/2.48).z-.026,('Roof clearance',tuple(p))
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<1.30 and max(abs(lo[1]),abs(hi[1]))<1.30 and hi[2]<7.5
(OUT/'validation.json').write_text(json.dumps({'modules':report,'triangles_all_variants':sum(d['triangles'] for d in report.values()),'bounds':[lo,hi],'floor_height':4.61,'entry_clear_width':.785,'ladder_inner_width':.575},indent=2))
def state(alert):
    for name in ['Flag_Alert','Flag_Stowed']:
        hidden=(name=='Flag_Alert')!=alert;modules[name].hide_render=hidden;modules[name].hide_set(hidden)
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in list(modules.values())+[o for o in bpy.context.scene.objects if o.type=='EMPTY']:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('watchtower-state-kit.fbx');state(False);export('watchtower-idle.fbx');state(True);export('watchtower-alert.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Watchtower_Review',bpy.data.cameras.new('Watchtower_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(10,-16,11),target=(0,0,3.70),scale=8.7,res=(1100,1400)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('state-alert');state(False);render('front',(0,-18,8));render('opposite-three-quarter',(-10,-16,11));render('platform-detail',(5,-8,8.5),(0,0,5.75),4.2,(1200,1100));render('game-scale',res=(280,360));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.overlay.show_overlays=False;s.region_3d.view_location=(0,0,3.7);s.region_3d.view_distance=11;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/watchtower-astra-lvl1-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
