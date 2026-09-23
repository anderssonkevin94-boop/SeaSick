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
OUT=HERE.parents[1]/'art-staging/sawmill-astra-lvl1'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=HERE/'source/sawmill-astra-lvl1.blend'
ss.clear_scene()
COLORS={'timber':'#795039','edge':'#A96D3D','plank':'#B88751','light':'#CFAC70',
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

# Low individual stone pads and a timber deck, not a raised diorama pedestal.
for x in [-1.96,1.96]:
    for y in [-1.32,1.35]:
        box('Footing',(x,y,.12),(.57,.56,.24),'stone',.08)
for x in [-1.65,0,1.65]:box('Floor_Joist',(x,.02,.19),(.16,3.15,.2),'timber',.012)
for i in range(14):
    x=-2.08+(i+.5)*4.16/14
    box('Floorboard',(x,.02,.27),(4.16/14-.014,3.16,.12),'plank' if i%3 else 'edge',.008)
box('Threshold',(0,-1.62,.22),(2.15,.28,.24),'timber',.025)
box('Entry_Step',(0,-1.93,.105),(1.65,.4,.21),'stone2',.045)

# Clear front bay with pegged joinery and knee braces.
for x in [-1.96,1.96]:
    for y in [-1.32,1.35]:
        box('Post',(x,y,1.42),(.24,.25,2.36),'timber',.025)
        box('Post_Shoe',(x,y,.4),(.265,.275,.18),'iron',.008)
        beam('Knee_Brace',(x,y,1.9),(x-math.copysign(.55,x),y,2.47),.14,.15,'edge')
        beam('Side_Brace',(x,y,1.98),(x,y-math.copysign(.48,y),2.46),.12,.14,'timber')
        cylinder('Joinery_Peg',(x,y-.145,2.4),(x,y-.17,2.4),.034,'light',8)
    beam('Wall_Plate',(x,-1.57,2.43),(x,1.63,2.43),.23,.23,'timber')
for y in [-1.32,1.35]:
    beam('Tie_Beam',(-2.1,y,2.5),(2.1,y,2.5),.22,.23,'timber')
    beam('King_Post',(0,y,2.52),(0,y,3.59),.15,.16,'edge')
    for s in [-1,1]:beam('Rafter',(s*2.22,y,2.28),(0,y,3.47),.18,.20,'timber')
# Rear wall boards and cross brace keep the silhouette open on three sides.
for i in range(13):
    x=-1.8+i*.3
    box('Rear_Plank',(x,1.39,1.29),(.286,.10,1.90),'plank' if i%3 else 'edge',.008)
beam('Rear_Brace',(-1.76,1.30,.42),(1.74,1.30,2.15),.12,.12,'timber')
box('Rear_Sill',(0,1.37,.37),(3.85,.16,.18),'timber')

# Thick tapered wooden shingles: five staggered courses with controlled variation.
GROUP='Roof'
for side in [-1,1]:
    prism('Roof_Deck',[(0,3.62),(side*2.37,2.43),(side*2.37,2.36),(0,3.55)],-1.77,1.82,'roofdark')
    for row in range(5):
        u0=row/5;u1=min(1.025,(row+1)/5+.022)
        boundaries=sorted(set([-1.79,1.85]+[-1.79+k*.455+(row%2)*.2275 for k in range(9)
                                            if -1.79<-1.79+k*.455+(row%2)*.2275<1.85]))
        for col,(y0,yend) in enumerate(zip(boundaries,boundaries[1:])):
            y1=yend-.014
            z0=3.66-1.19*u0+.009*(4-row)
            z1=3.66-1.19*u1+.009*(4-row)
            extension=[0,.024,-.012,.009][(col+2*row+(side==1))%4]
            x0=side*2.37*u0;x1=side*(2.37*u1+extension)
            shape=[(x0,z0),(x1,z1),(x1,z1-.055),(x0,z0-.025)]
            color=['roof','roof','rooflight','roofdark','roof'][((col*3+row*2)+(side==1))%5]
            prism('Shingle',shape,y0,y1,color)
    for y in [-1.83,1.89]:
        beam('Bargeboard',(side*2.46,y,2.41),(0,y,3.70),.135,.16,'edge',.015)
    beam('Eave_Fascia',(side*2.40,-1.80,2.41),(side*2.40,1.88,2.41),.13,.16,'timber')
beam('Ridge_Cap',(0,-1.94,3.73),(0,1.98,3.73),.17,.15,'light',.015)

# Tool shed identity: hanging teal sign with an actual saw silhouette.
GROUP='Structure'
for x in [-.46,.46]:beam('Sign_Hanger',(x,-1.49,2.52),(x,-1.49,2.26),.045,.045,'iron',0)
prism('Trade_Sign',[(-.67,1.99),(.67,1.99),(.74,2.13),(.65,2.33),(-.65,2.33),(-.74,2.13)],-1.60,-1.49,'teal',.018)
saw_outline=[(-.44,2.22),(.29,2.22),(.36,2.13)]
for i in range(9):saw_outline.append((.29-i*.082,2.09 if i%2 else 2.15))
prism('Sign_Saw',saw_outline,-1.619,-1.603,'cream')
prism('Sign_Handle',[(.28,2.10),(.49,2.10),(.49,2.27),(.32,2.27)],-1.625,-1.605,'light')

# Bench sits forward enough that the saw remains readable from the game camera.
GROUP='Machinery'
for x in [-.86,.47]:
    for y in [-2.22,-.48]:
        beam('Bench_Leg',(x,y,.06 if y<-1.6 else .33),(x*.86,y,1.02),.16,.17,'timber',.015)
    beam('Bench_Rail',(x,-2.35,.69),(x,-.32,.69),.13,.15,'edge')
for x,w in [(-.61,.70),(.19,.78)]:
    box('Saw_Table',(x,-1.33,1.03),(w,2.15,.14),'light',.012)
for y in [-2.36,-.54]:box('Bench_Crosspiece',(-.23,y,.91),(1.42,.15,.18),'timber',.012)
box('Fence',(-.81,-1.33,1.19),(.10,2.12,.19),'edge',.012)

blade_pivot=marker('Saw_Rotation',(-.23,-1.78,.94),'animation')
# Closed toothed plate in the YZ plane, axis X; one mesh rather than loose teeth.
n=72;verts=[]
for x in [-.021,.021]:
    for i in range(n):
        a=i*math.tau/n;r=[.42,.49,.435][i%3]
        verts.append((x,math.cos(a)*r,math.sin(a)*r))
faces=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
blade=mesh('Saw_Blade',verts,faces,'steel',blade_pivot)
cylinder('Saw_Axle',(-.94,-1.78,.94),(.96,-1.78,.94),.058,'iron',10)
for x in [-.77,.44]:box('Axle_Bearing',(x,-1.78,.90),(.18,.25,.23),'iron',.022)
# Open-spoked flywheel and hand crank, deliberately modest for level one.
crank=marker('Crank_Rotation',(.88,-1.78,.94),'animation')
verts=[];N=16
for x,r in [(-.045,.33),(.045,.33),(.045,.265),(-.045,.265)]:
    verts += [(x,r*math.cos(i*math.tau/N),r*math.sin(i*math.tau/N)) for i in range(N)]
faces=[]
for j in range(4):
    for i in range(N):faces.append((j*N+i,j*N+(i+1)%N,((j+1)%4)*N+(i+1)%N,((j+1)%4)*N+i))
mesh('Flywheel_Rim',verts,faces,'iron',crank)
for angle in [0,math.pi/2]:
    d=Vector((0,math.cos(angle)*.28,math.sin(angle)*.28))
    ob=beam('Flywheel_Spoke',-d,d,.045,.05,'iron',0);ob.parent=crank
ob=cylinder('Crank_Grip',(.89,-1.51,.94),(1.16,-1.51,.94),.055,'edge',8)
ob.parent=crank
for v in ob.data.vertices:v.co-=crank.location

# Short infeed billet and sawn board. The blade slot remains visibly open.
cylinder('Infeed_Log',(-.23,-1.11,1.281),(-.23,-.15,1.281),.19,'bark',10,r2=.18)
box('Finished_Board',(.13,-.92,1.15),(.28,.92,.09),'light',.01)

GROUP='Supplies'
for yy in [-.9,.95]:
    box('Log_Rack_Base',(-2.91,yy,.14),(1.27,.22,.28),'timber',.02)
    for xx in [-3.47,-2.38]:beam('Log_Rack_Stake',(xx,yy,.15),(xx,yy,.95),.095,.105,'timber')
for i,(x,z,r) in enumerate([(-3.19,.51,.23),(-2.69,.51,.23),(-2.94,.91,.23)]):
    y0=-1.48+[0,.12,-.08][i];y1=1.47-[0,.05,.12][i]
    cylinder('Stored_Log',(x,y0,z),(x,y1,z),r,'bark',10,r2=r*.93)
    # A recessed-looking heart on the exposed end gives a clean cut-end cue.
    cylinder('End_Heart',(x,y0-.003,z),(x,y0-.009,z),r*.60,'heart',10)
    cylinder('End_Core',(x,y0-.010,z),(x,y0-.014,z),r*.20,'end',8)
for y in [-.8,.88]:box('Board_Stack_Bearer',(2.94,y,.15),(1.22,.21,.30),'timber',.014)
for row in range(5):
    for col in range(3):
        box('Stacked_Plank',(2.51+col*.40,.1+(row%2)*.04,.36+row*.115),(.38,2.68-.07*((row+col)%3),.09),'light' if (col+row)%3 else 'plank',.006)
for y in [-.8,.9]:box('Stack_Batten',(2.91,y,.925),(1.2,.10,.09),'edge',.008)

marker('Entry',(1.5,-2.65,0),'entry')
marker('Sawyer',(1.33,-1.8,0),'worker',(-1,0,0))
marker('Log_feeder',(-1.50,-2.1,0),'worker',(1,0,0))
marker('Output',(2.87,-1.7,0),'delivery')

# Keep authoring pieces editable; merge static export modules to limit renderers.
def stats(items):
    report={};points=[]
    bpy.context.view_layer.update()
    for o in items:
        bm=bmesh.new();bm.from_mesh(o.data)
        d={'triangles':sum(len(f.verts)-2 for f in bm.faces),
           'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
           'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
        assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(o.name,d)
        assert not any(p.use_smooth for p in o.data.polygons)
        points += [o.matrix_world@v.co for v in o.data.vertices]
        report[o.name]=d;bm.free()
    lo=[min(v[i] for v in points) for i in range(3)];hi=[max(v[i] for v in points) for i in range(3)]
    assert max(abs(lo[0]),abs(hi[0]))<=3.78
    assert max(abs(lo[1]),abs(hi[1]))<=2.925
    assert hi[2]<=3.84
    return {'meshes':report,'triangles':sum(x['triangles'] for x in report.values()),'bounds':[lo,hi]}
report=stats(objects)
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
exports=[];groups={}
for ob in objects:
    cp=ob.copy();cp.data=ob.data.copy();bpy.context.collection.objects.link(cp)
    groups.setdefault((ob['module'],ob.parent.name),[]).append(cp)
for (group,parent),items in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in items:ob.select_set(True)
    bpy.context.view_layer.objects.active=items[0]
    if len(items)>1:bpy.ops.object.join()
    ob=bpy.context.object;ob.name='Sawmill_'+group+'_'+parent;exports.append(ob)
bpy.ops.object.select_all(action='DESELECT')
for ob in exports+[o for o in bpy.context.scene.objects if o.type=='EMPTY']:ob.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'sawmill-level-1.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,
    use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
for ob in exports:bpy.data.objects.remove(ob,do_unlink=True)
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65
scene.display.shading.show_shadows=False;scene.display.shading.background_type='WORLD'
scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));scene.collection.objects.link(cam)
scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1400,1100)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
render('front-three-quarter',(9,-12,8),(0,-.1,1.65),9.2)
render('front',(.1,-14,5),(0,-.1,1.65),8.7)
render('rear-three-quarter',(-8,11,7),(0,0,1.6),9.2)
render('game-scale',(9,-12,10),(0,-.1,1.65),9.2,(360,280))
render('work-bay',(5,-8,4),(-.05,-1.3,1.1),4.5)
render('front-three-quarter',(9,-12,8),(0,-.1,1.65),9.2)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX'
            s.region_3d.view_location=(0,0,1.5);s.region_3d.view_distance=11
            s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print(json.dumps({'triangles':report['triangles'],'bounds':report['bounds'],'export_meshes':len(groups)},indent=2))
