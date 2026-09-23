"""Start-origin palisade modules for runtime wall segments, no Unity import."""
import bpy,bmesh,math,json,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/palisade-astra-lvl1-v2'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'bark':'#806047','bark2':'#926E4D','bark3':'#71533E','cut':'#C5A272','cut2':'#B8905C','rail':'#9F774F','rope':'#C2AD82'}
parts={};reports={};contracts={}
def stake(k,x,height,index,broken=False):
    n=8;r=.114;dx=[-.008,.008,.004,-.006][index%4];dy=[.022,-.021,.013,-.015][index%4]
    if broken:dx=[.015,-.02,.035,-.018][index%4];dy=[.09,-.025,.13,-.08][index%4]
    verts=[]
    levels=[(-.08,1),(height-(.13 if broken else .32),1),(height-(.055 if broken else .22),.96)]
    for z,scale in levels:
        t=max(0,z)/height
        for i in range(n):
            a=i*math.tau/n+math.pi/8
            verts.append((x+r*scale*math.cos(a)+dx*t,r*scale*math.sin(a)+dy*t,z))
    faces=[tuple(reversed(range(n)))];colors=['bark3']
    for j in range(2):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i));colors.append(['bark','bark2','bark3'][(i+index)%3])
    if broken:
        for i in range(n):
            a=i*math.tau/n+math.pi/8
            verts.append((x+r*.93*math.cos(a)+dx,r*.93*math.sin(a)+dy,height+[.02,-.03,.055,-.02,.025,-.04,.035,-.01][(i+index)%8]))
        for i in range(n):faces.append((2*n+i,2*n+(i+1)%n,3*n+(i+1)%n,3*n+i));colors.append('cut2')
        faces.append(tuple(3*n+i for i in range(n)));colors.append('cut')
    else:
        tip=len(verts);verts.append((x+dx,dy,height))
        for i in range(n):faces.append((2*n+i,2*n+(i+1)%n,tip));colors.append('cut' if i%2 else 'cut2')
    k.mesh('Broken_Stake' if broken else 'Stake',verts,faces,colors)
def export(name,k,length,sockets):
    mods=k.join_modules();markers=[k.marker(name+'__'+s,p,'local metres') for s,p in sockets.items()]
    bpy.context.view_layer.update();points=[];d={'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0}
    for ob in mods.values():
        bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(ob.data)
        d['triangles']+=len(bm.faces);d['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges);d['degenerate_faces']+=sum(f.calc_area()<1e-10 for f in bm.faces);bm.free()
        points.extend(ob.matrix_world@v.co for v in ob.data.vertices)
    assert d['nonmanifold_edges']==d['degenerate_faces']==0,(name,d)
    bounds=[[min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]]
    assert bounds[0][0]>=-1e-5 and bounds[1][0]<=length+1e-5,(name,bounds)
    d['bounds_blender']=bounds;reports[name]=d
    contracts[name]={'length':length,'sockets':sockets,'markers':[o.name for o in markers]}
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [k.root]+list(mods.values())+markers:ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
    parts[name]=(k.root,list(mods.values()))
def run(name,length,variant=0,broken=False):
    k=Kit(name,COL);k.group=name+'_Stakes'
    count=round(length/.25)
    for i in range(count):
        h=[2.50,2.39,2.59,2.45,2.55,2.40,2.53,2.46][(i+variant*3)%8]
        if broken:h=[.39,.18,.61,.28][i%4]
        stake(k,.125+i*.25,h,i+variant,broken)
    k.group=name+'_Rear_Rails'
    if not broken:
        for z in [.68,1.69]:
            # Square ends meet exactly; no endpoint stakes or end caps added.
            k.box('Rear_Rail',(length/2,.1375,z),(length,.075,.16),'rail',0)
            for i in range(count):k.rod('Wood_Peg',(.125+i*.25,.175,z),(.125+i*.25,.183,z),.016,'cut2',6)
    else:
        k.box('Broken_Rail_Left',(.14,.1375,.19),(.28,.075,.12),'rail',.008)
        k.box('Broken_Rail_Right',(.9,.1375,.15),(.20,.075,.10),'rail',.008)
    export(name,k,length,{'Snap_Start':[0,0,0],'Snap_End':[length,0,0]})
for i,v in enumerate('ABC'):run('Palisade_Run_1m_'+v,1,i)
run('Palisade_Filler_050m',.5,1)
run('Palisade_Filler_025m',.25,2)
run('Palisade_Breached_1m',1,broken=True)
k=Kit('Palisade_Post',COL);k.group='Palisade_Post_Mesh'
k.box('Square_Post',(.2,0,1.34),(.38,.38,2.84),'bark',.018)
k.box('Cap_Shoulder',(.2,0,2.77),(.4,.4,.12),'rail',.018)
k.box('Cap',(.2,0,2.87),(.36,.36,.08),'cut',.018)
export('Palisade_Post',k,.4,{'Snap_Start':[0,0,0],'Snap_End':[.4,0,0],'Post_Center':[.2,0,0]})
(OUT/'validation.json').write_text(json.dumps(reports,indent=2))
(OUT/'snap-contract.json').write_text(json.dumps({'axes':'Blender +X run, +Y rear, +Z up','root':'start end at ground','pitch':.25,'parts':contracts,'run_y_envelope':[-.14,.183],'post_body_half_width':.19,'post_center':[.2,0,0],'rules':['Quarter-metre modules do not exactly fit arbitrary real lengths. Clip the final module and cap cut faces in the runtime adapter.','Trim run endpoints to each square post footprint for arbitrary-angle bends; do not butt modules to the post center.','Sub-quarter residuals and middle-third breach boundaries need runtime trimming, not changed gameplay lengths.','No fixed-angle corner assets are needed.']},indent=2))
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Palisade_V2_Review',bpy.data.cameras.new('Palisade_V2_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
layout=[(-2.7,1.5,0),(-1.1,1.5,0),(.5,1.5,0),(2.1,1.5,0),(3.1,1.5,0),(-1,-.65,0),(1.2,-.65,0)]
for (root,obs),loc in zip(parts.values(),layout):root.location=loc
def render(name,eye,target,scale):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale;scene.render.resolution_x=1500;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('kit-overview',(7,-13,8),(.25,.5,1.3),8.4)
render('kit-rear',(-7,13,8),(.25,.5,1.3),8.4)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type='VERTEX';v.overlay.show_overlays=False;v.region_3d.view_location=(.25,.5,1.3);v.region_3d.view_distance=10;v.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/palisade-astra-lvl1-v2.blend'))
verified={}
for name in parts:
    for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    tris=0
    for ob in scene.objects:
        if ob.type!='MESH':continue
        bm=bmesh.new();bm.from_mesh(ob.data)
        assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces),(name,ob.name)
        assert not any(p.use_smooth for p in ob.data.polygons) and 'Col' in ob.data.color_attributes
        tris+=len(bm.faces);bm.free()
    for marker in contracts[name]['markers']:assert marker in scene.objects
    verified[name]={'result':'PASS','triangles':tris}
(OUT/'export-verification.json').write_text(json.dumps(verified,indent=2));print('PASS',verified)
