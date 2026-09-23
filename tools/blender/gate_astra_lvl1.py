"""Flat-shaded starter gate with a real hinge hierarchy; staging only."""
import bpy, bmesh, math, json, sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/gate-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'wood':'#806047','wood2':'#926E4D','dark':'#71533E','cut':'#C5A272','rail':'#9F774F','iron':'#485251','peg':'#B8905C'}
k=Kit('Gate_L1',COL)
k.group='Gate_Posts'
for x in [-1.28,1.28]:
    k.lathe('Octagonal_Gatepost',(x,0,0),[(.22,-.35),(.22,3.46),(.18,3.60)],'wood',8)
    k.lathe('Cut_End',(x,0,0),[(.178,3.60),(.178,3.604)],'cut',8)
    for z in [.35,2.94]:
        k.lathe('Iron_Collar',(x,0,0),[(.225,z),(.225,z+.095)],'iron',8)
k.group='Gate_Lintel'
k.box('Lintel',(0,0,3.23),(3.20,.35,.29),'rail',.035)
for x in [-1.28,1.28]:
    k.rod('Lintel_Peg',(x,-.194,3.23),(x,-.175,3.23),.035,'peg',8)
k.group='Gate_Leaf'
# Each plank has a deliberate shoulder and clipped crown, not a pile of boxes.
edges=[-1.005,-.50,0,.50,1.015]
for i in range(4):
    a=edges[i]+.008;b=edges[i+1]-.008
    h=[2.53,2.57,2.61,2.63][i]
    profile=[(a,.13),(b,.13),(b,h-.06),(b-.045,h),(a+.035,h),(a,h-.07)]
    n=len(profile);verts=[(x,y,z) for y in [-.075,.075] for x,z in profile]
    ob=k.mesh('Dressed_Plank',verts,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)],['wood','wood2','dark'][i%3])
    bpy.context.view_layer.objects.active=ob;m=ob.modifiers.new('Worn Edges','BEVEL');m.width=.012;m.segments=1;bpy.ops.object.modifier_apply(modifier=m.name)
for z in [.48,2.17]:
    k.box('Rear_Rail',(0,.135,z),(1.82,.12,.18),'rail',.015)
    for x in [-.87,-.30,.30,.87]:k.rod('Rail_Peg',(x,.197,z),(x,.208,z),.025,'peg',6)
# Compression brace rises from the bottom hinge toward the latch corner.
k.beam('Compression_Brace',(-.89,.138,.58),(.89,.138,2.07),.14,.115,'rail')
for z in [.48,2.17]:
    k.box('Hinge_Strap',(-.62,-.105,z),(.81,.045,.105),'iron',.016)
    for x in [-.88,-.60,-.30]:k.rod('Strap_Rivet',(x,-.133,z),(x,-.145,z),.022,'peg',6)
    k.lathe('Hinge_Barrel',(-1.02,-.11,0),[(.043,z-.095),(.043,z+.095)],'iron',8)
k.box('Latch_Plate',(.83,-.102,1.24),(.16,.05,.22),'iron',.015)
k.rod('Handle_Left',(.79,-.128,1.19),(.79,-.20,1.19),.02,'iron',8)
k.rod('Handle_Right',(.79,-.128,1.32),(.79,-.20,1.32),.02,'iron',8)
k.rod('Pull_Handle',(.79,-.20,1.19),(.79,-.20,1.32),.023,'iron',8)
k.group='Gate_Frame_Hardware'
for z in [.48,2.17]:
    k.box('Hinge_Mount',(-1.22,-.18,z),(.23,.07,.16),'iron',.015)
    k.rod('Hinge_Pin',(-1.02,-.11,z-.12),(-1.02,-.11,z+.12),.023,'iron',8)
    k.beam('Hinge_Gudgeon',(-1.20,-.18,z-.105),(-1.02,-.11,z-.105),.04,.04,'iron')

mods=k.join_modules()
hinge=k.marker('Gate_Hinge',(-1.02,-.11,0),'rotate local Z from 0 to -100 degrees; outward -Y')
leaf=mods['Gate_Leaf']
for v in leaf.data.vertices:v.co.x=-1.02+(v.co.x+1.02)*.49
leaf.parent=hinge
leaf.location=-hinge.location
leaf.name='Gate_Leaf_Left'
right_hinge=k.marker('Gate_Hinge_Right',(1.02,-.11,0),'rotate local Z from 0 to +100 degrees; outward -Y')
right=leaf.copy();right.data=leaf.data.copy();bpy.context.collection.objects.link(right)
right.name='Gate_Leaf_Right';right.parent=right_hinge;right.location=-right_hinge.location
for v in right.data.vertices:v.co.x=-v.co.x
bm=bmesh.new();bm.from_mesh(right.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(right.data);bm.free()
mods['Gate_Leaf_Right']=right
hardware=mods['Gate_Frame_Hardware']
rh=hardware.copy();rh.data=hardware.data.copy();bpy.context.collection.objects.link(rh);rh.name='Gate_Frame_Hardware_Right'
for v in rh.data.vertices:v.co.x=-v.co.x
bm=bmesh.new();bm.from_mesh(rh.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(rh.data);bm.free()
mods['Gate_Frame_Hardware_Right']=rh
markers=[k.marker('Gate__Snap_Start',(-1.5,0,0),'wall boundary'),k.marker('Gate__Snap_End',(1.5,0,0),'wall boundary'),k.marker('Gate__Passage',(0,0,0),'ground passage center')]
bpy.context.view_layer.update()
report={}
for name,ob in mods.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==d['degenerate_faces']==0
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;bm.free()
bpy.ops.object.select_all(action='DESELECT')
for ob in [k.root,hinge,right_hinge]+list(mods.values())+markers:ob.select_set(True)
bpy.context.view_layer.objects.active=k.root
FBX=OUT/'Gate_L1.fbx'
bpy.ops.export_scene.fbx(filepath=str(FBX),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
(OUT/'snap-contract.json').write_text(json.dumps({'span':3,'height':3.604,'clear_post_width':2.12,'snap_start':[-1.5,0,0],'snap_end':[1.5,0,0],'hinge_blender':[-1.02,-.11,0],'open_degrees':-100,'interior':'+Y','minimum_swing_clearance':1.15,'right_hinge_blender':[1.02,-.11,0],'right_open_degrees':100,'axes':'Blender X wall run, Z up; use imported markers for Unity mapping'},indent=2))
from mathutils.bvhtree import BVHTree
def world_bvh(ob):
    return BVHTree.FromPolygons([ob.matrix_world@v.co for v in ob.data.vertices],[list(p.vertices) for p in ob.data.polygons])
for angle in range(0,-101,-5):
    hinge.rotation_euler.z=math.radians(angle);right_hinge.rotation_euler.z=math.radians(-angle);bpy.context.view_layer.update()
    for fixed in ['Gate_Posts','Gate_Lintel']:
        assert not world_bvh(leaf).overlap(world_bvh(mods[fixed])),('left swing intersection',angle,fixed)
        assert not world_bvh(right).overlap(world_bvh(mods[fixed])),('right swing intersection',angle,fixed)
    assert not world_bvh(leaf).overlap(world_bvh(right)),('leaf collision',angle)
hinge.rotation_euler.z=0;right_hinge.rotation_euler.z=0
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Gate_Review_Camera',bpy.data.cameras.new('Gate_Review_Camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target=(0,0,1.65),scale=5.4):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x=1200;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('closed-exterior',(5,-9,5))
render('closed-interior',(-5,9,5))
hinge.rotation_euler.z=math.radians(-100);right_hinge.rotation_euler.z=math.radians(100)
render('open-interior',(-5,9,6),(0,-.5,1.5),6.1)
hinge.rotation_euler.z=0;right_hinge.rotation_euler.z=0
# Adjacent palisades are review context only, never part of the gate export.
wall=HERE.parents[1]/'art-staging/palisade-astra-lvl1-v1/Palisade_Straight_2m_A.fbx'
before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(wall));added=set(bpy.data.objects)-before
wallroot=next(o for o in added if o.parent is None)
wallroot.location=(-3.5,0,0)
# FBX round-trip restores Blender axes; copy the imported hierarchy intact.
mapping={}
for ob in added:
    copy=ob.copy();scene.collection.objects.link(copy);mapping[ob]=copy
for ob,copy in mapping.items():copy.parent=mapping.get(ob.parent)
mapping[wallroot].location=(1.5,0,0)
render('palisade-context',(7,-12,7),(0,0,1.6),8.5)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type='VERTEX';v.overlay.show_overlays=False;v.region_3d.view_location=(0,0,1.6);v.region_3d.view_distance=10;v.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/gate-astra-lvl1-v1.blend'))
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(FBX))
total=0
for ob in bpy.context.scene.objects:
    if ob.type!='MESH':continue
    bm=bmesh.new();bm.from_mesh(ob.data)
    assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces)
    assert not any(p.use_smooth for p in ob.data.polygons) and 'Col' in ob.data.color_attributes
    total+=sum(len(f.verts)-2 for f in bm.faces);bm.free()
assert bpy.data.objects['Gate_Leaf_Left'].parent.name=='Gate_Hinge'
assert bpy.data.objects['Gate_Leaf_Right'].parent.name=='Gate_Hinge_Right'
for name in ['Gate__Snap_Start','Gate__Snap_End','Gate__Passage']:assert name in bpy.context.scene.objects
(OUT/'export-verification.json').write_text(json.dumps({'result':'PASS','triangles':total,'hinge_parent_verified':True},indent=2))
print('PASS Gate triangles:',total)
