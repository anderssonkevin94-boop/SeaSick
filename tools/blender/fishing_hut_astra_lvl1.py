"""Low-poly, canvas-roof fishing station. Staged only; no Unity changes."""
import sys, math, json
from pathlib import Path
import bpy, bmesh
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/fishing-hut-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'wood':'#795940','edge':'#AC8055','plank':'#BC996A','teal':'#608B83','teal2':'#77988B',
     'canvas':'#E0D8B9','shade':'#C9BC99','patch':'#B58B64','rope':'#BCAA80',
     'iron':'#414E52','stone':'#87928C','fish':'#91B3AD','belly':'#D2D7BB','fin':'#567D80','eye':'#243A3C'}
k=Kit('Fishing_Hut_Level_1',COL)
k.root['plot_metres']=[5.5,5.2]
k.root['front']='-Y; dry land approach; rear may face shore'
k.group='Frame'
for x in [-1.35,1.35]:
    for y in [-.60,1.55]:
        k.box('Stone_Foot',(x,y,.09),(.38,.40,.18),'stone',.045)
        k.box('Square_Post',(x,y,1.25),(.18,.20,2.32),'wood',.018)
        k.box('Post_Shoe',(x,y,.28),(.20,.22,.17),'iron',.006)
    k.beam('Eave',(x,-.75,2.36),(x,1.66,2.36),.14,.16,'edge')
for y in [-.60,1.55]:
    k.beam('Tie',(-1.42,y,2.30),(1.42,y,2.30),.14,.16,'wood')
    for x in [-1.35,1.35]:
        k.beam('Rafter',(x,y,2.36),(0,y,3.05),.12,.14,'edge')
        k.beam('Knee',(x,y,1.91),(x*.69,y,2.29),.10,.10,'wood')
k.beam('Ridge',(0,-.92,3.06),(0,1.77,3.06),.13,.13,'wood')
k.group='Windbreak'
for i in range(9):
    x=-1.21+i*.30
    k.box('Back_Plank',(x,1.58,.90+(.025 if i%3==0 else 0)),(.285,.08,1.22),
          'teal' if i%3 else 'teal2',.008)
for z in [.42,1.29]:k.box('Rear_Batten',(0,1.65,z),(2.62,.09,.10),'wood',.008)
k.group='Canopy'
def cloth(u,v):
    x=-1.61+3.22*u;y=-.94+2.76*v
    return Vector((x,y,3.17-.73*abs(2*u-1)-.075*math.sin(math.pi*v)*math.sin(2*math.pi*u)**2))
nu,nv=8,4
verts=[cloth(i/nu,j/nv)+Vector((0,0,d)) for d in [0,-.025] for j in range(nv+1) for i in range(nu+1)]
count=(nu+1)*(nv+1);faces=[];colors=[]
for j in range(nv):
    for i in range(nu):
        a=j*(nu+1)+i;face=(a,a+1,a+nu+2,a+nu+1)
        faces += [face,tuple(v+count for v in reversed(face))]
        colors += ['shade' if i in [0,7] else 'canvas','shade']
boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
for i,a in enumerate(boundary):
    b=boundary[(i+1)%len(boundary)];faces.append((a,b,b+count,a+count));colors.append('shade')
k.mesh('Taut_Canvas',verts,faces,colors)
for i in range(nu):k.rod('Front_Hem',cloth(i/nu,0),cloth((i+1)/nu,0),.035,'shade',5)
for u in [0,1]:
    for v in [0,1]:k.rod('Corner_Tie',cloth(u,v),(-1.35 if u==0 else 1.35,-.60 if v==0 else 1.55,2.22),.019,'rope',5)
patch=[cloth(u,v)+Vector((0,0,.014)) for u,v in [(.64,.46),(.86,.46),(.86,.72),(.64,.72)]]
k.mesh('Canvas_Repair',patch+ [p+Vector((0,0,.012)) for p in patch],
       [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'patch')
for a,b in zip(patch,patch[1:]+patch[:1]):
    d=(b-a).normalized();side=Vector((-d.y,d.x,0))*.03
    for t in [.22,.5,.78]:
        p=a.lerp(b,t)+Vector((0,0,.032));k.rod('Stitch',p-side,p+side,.008,'rope',4)
k.group='Work_Bench'
for x in [-.92,.92]:
    for y in [.12,.75]:k.box('Bench_Leg',(x,y,.43),(.14,.15,.86),'wood',.012)
for i in range(3):k.box('Bench_Top',(0,.10+i*.27,.91),(2.16,.25,.12),'plank' if i%2 else 'edge',.015)
k.box('Bench_Tie',(0,.48,.29),(1.95,.11,.13),'wood',.01)
k.box('Cutting_Board',(-.13,.28,1.005),(.95,.51,.07),'edge',.03)
k.group='Gear'
k.box('Knife_Handle',(.68,.32,1.00),(.22,.065,.05),'wood',.01)
k.box('Knife_Blade',(.45,.32,.994),(.26,.10,.025),'iron',.003)
# A folded sailcloth bundle, floats and one thick hand-line, not hundreds of knots.
k.box('Folded_Net_Bundle',(-.65,.61,1.06),(.59,.27,.19),'rope',.055)
for x in [-.79,-.51]:k.box('Bundle_Tie',(x,.61,1.067),(.04,.29,.20),'iron',.005)
for y in [-.06,.14,.34]:k.lathe('Float',(1.22,y,.26),[(.07,0),(.11,.10),(.09,.23),(.04,.29)],'canvas',6)
k.group='Net_Rack'
for y in [-.17,1.25]:k.rod('Rack_Leg',(-2.13,y,0),(-1.98,y,2.03),.075,'wood',6)
k.beam('Rack_Rail',(-1.98,-.28,1.98),(-1.98,1.37,1.98),.13,.13,'edge')
# Coarse diamond mesh on a plane outside the roof edge.
def netpoint(u,v):return (-2.04-.10*math.sin(math.pi*u)*math.sin(math.pi*v),-.15+1.37*u,.59+1.28*v)
for i in range(6):
    u=i/5
    for j in range(4):
        v=j/4
        k.rod('Net_Warp',netpoint(u,v),netpoint(u,(j+1)/4),.009,'rope',4)
for j in range(5):
    for i in range(5):k.rod('Net_Weft',netpoint(i/5,j/4),netpoint((i+1)/5,j/4),.009,'rope',4)
for y in [-.15,.31,.76,1.22]:
    k.lathe('Net_Weight',(-2.04,y,.51),[(.025,0),(.06,.045),(.04,.09)],'stone',5)
k.group='Output_Crate'
cx,cy=1.20,-1.37
k.box('Crate_Floor',(cx,cy,.10),(1.35,.90,.20),'wood',.015)
for x in [cx-.64,cx+.64]:
    for z in [.27,.46]:k.box('Crate_End',(x,cy,z),(.08,.88,.13),'teal',.006)
for y in [cy-.42,cy+.42]:
    for z in [.27,.46]:k.box('Crate_Front',(cx,y,z),(1.35,.075,.13),'plank',.006)
for x in [cx-.62,cx+.62]:
    for y in [cy-.39,cy+.39]:k.box('Crate_Corner',(x,y,.32),(.10,.10,.48),'wood',.008)

def fish(name,p,scale=1):
    x,y,z=p;n=6
    sections=[(-.32,.025),(-.20,.105),(.07,.12),(.25,.07),(.30,.018)]
    verts=[(x+a*scale,y+math.cos(i*math.tau/n)*r*scale,z+math.sin(i*math.tau/n)*r*.62*scale) for a,r in sections for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple((len(sections)-1)*n+i for i in range(n))]
    faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(sections)-1) for i in range(n)]
    k.mesh(name,verts,faces,['fish','belly','fish','fin','fish','belly'])
    k.mesh('Tail',[(x+a*scale,y+b*scale,z+c*scale) for a,b,c in [(-.30,0,0),(-.47,-.12,0),(-.43,.12,0),(-.40,0,.045)]],[(0,2,1),(0,1,3),(1,2,3),(2,0,3)],'fin')
    k.box('Eye',(x+.17*scale,y-.066*scale,z+.029*scale),(.031*scale,.016*scale,.027*scale),'eye',0)
for i in range(4):
    k.group=f'Output_Fish_{i+1:02d}';fish('Catch',(cx+(-.28 if i%2==0 else .31),cy+(-.18 if i<2 else .18),.28+.10*(i//2)),.77)
k.group='Work_Catch';fish('On_Board',(-.13,.28,1.13),1.05)
k.group='Sign'
k.box('Sign',(.55,-.68,2.05),(.85,.10,.38),'teal',.035)
for x in [.22,.89]:k.rod('Sign_Hanger',(x,-.68,2.23),(x,-.62,2.33),.017,'rope',5)
outline=[(-.29,0),(-.16,-.075),(.13,-.075),(.26,0),(.13,.075),(-.16,.075),(-.29,0),(-.39,.10),(-.39,-.10)]
# Two solid icon pieces preserve the negative-space tail pinch.
for poly in [outline[:6],[outline[6],outline[7],outline[8]]]:
    N=len(poly);vs=[(.58+x,-.745+d,2.05+z) for d in [0,.015] for x,z in poly]
    k.mesh('Fish_Emblem',vs,[tuple(reversed(range(N))),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)],'canvas')
mods=k.join_modules()
markers=[k.marker(n,p,r) for n,p,r in [('Worker_Stand',(0,-.58,0),'reference; crew reach and navigation unvalidated'),('Catch_Anchor',(0,.28,1.11),'work visual'),('Output_Anchor',(cx,cy,.21),'visible catch store'),('Shore_Direction',(0,2.3,0),'orient toward water; not a water height assumption')]]
report={}
for name,ob in mods.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert not d['nonmanifold_edges'] and not d['degenerate_faces'],(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;bm.free()
total=sum(d['triangles'] for d in report.values());assert total<6500,total
(OUT/'validation.json').write_text(json.dumps({'modules':report,'all_states_triangles':total,'plot_metres':[5.5,5.2],'inventory_slots':4},indent=2))
def state(stock,working):
    for name,ob in mods.items():
        visible=(not name.startswith('Output_Fish_') or int(name[-2:])<=stock) and (name!='Work_Catch' or working)
        ob.hide_render=not visible;ob.hide_set(not visible)
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [k.root]+list(mods.values())+markers:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
state(4,True);export('fishing-hut-state-kit.fbx')
state(0,False);export('fishing-hut-empty.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.5
cam=bpy.data.objects.new('Fishing_Hut_Review',bpy.data.cameras.new('Fishing_Hut_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=6.6
def render(name,eye=(7,-11,7),size=(1200,1000)):
    cam.location=eye;cam.rotation_euler=(Vector((-.1,.1,1.35))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x,scene.render.resolution_y=size;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('empty');state(3,True);render('front-three-quarter');render('opposite',(-8,-11,7));render('game-scale',size=(360,300));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(0,0,1.35);s.region_3d.view_distance=7;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'fishing-hut-lvl1.blend'))
print('FISHING HUT PASS',total)
