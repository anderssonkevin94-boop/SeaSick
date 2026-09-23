"""Modular starter palisade, matched boundary sockets and staged exports."""
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
OUT=HERE.parents[1]/'art-staging/palisade-astra-lvl1-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'bark':'#806047','bark2':'#926E4D','bark3':'#71533E','cut':'#C5A272','cut2':'#B8905C','rail':'#9F774F','rope':'#C2AD82'}
parts={};report={};contracts={}
def stake(k,x,y,height,index,r=.119):
    n=8;verts=[]
    # Faceted trunk and an off-center axe-cut tip, in one closed mesh.
    for z,rad,dx,dy in [(0,r,0,0),(height-.40,r,0,0),(height-.28,r*.96,.006,-.006)]:
        for i in range(n):
            a=math.tau*i/n+math.pi/8;verts.append((x+dx+rad*math.cos(a),y+dy+rad*math.sin(a),z))
    tip=len(verts);verts.append((x+(.022 if index%2 else -.018),y+.012,height))
    faces=[tuple(reversed(range(n)))];cols=['bark']
    for j in range(2):
        for i in range(n):
            faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i));cols.append(['bark','bark2','bark3'][(index+i%3)%3])
    for i in range(n):faces.append((2*n+i,2*n+(i+1)%n,tip));cols.append('cut' if i%3 else 'cut2')
    k.mesh('Sharpened_Stake',verts,faces,cols)
def lashing(k,x,y,z):
    # Continuous thin rope loops around the stake and the rear rail.
    for dz in [-.031,.031]:
        path=[(x-.125,y-.08,z+dz),(x-.08,y-.125,z+dz),(x+.08,y-.125,z+dz),(x+.125,y-.08,z+dz),(x+.125,y+.205,z+dz),(x+.08,y+.245,z+dz),(x-.08,y+.245,z+dz),(x-.125,y+.205,z+dz)]
        n=6;verts=[]
        for j,p in enumerate(path):
            p=Vector(p);d=(Vector(path[(j+1)%8])-Vector(path[(j-1)%8])).normalized();u=Vector((0,0,1));v=d.cross(u)
            for i in range(n):verts.append(p+.012*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)))
        faces=[(j*n+i,j*n+(i+1)%n,((j+1)%8)*n+(i+1)%n,((j+1)%8)*n+i) for j in range(8) for i in range(n)]
        k.mesh('Rope_Binding',verts,faces,'rope')
def rail(k,a,b):k.beam('Backing_Rail',a,b,.12,.16,'rail')
def build(name,length=None,variant=0,corner=0,terminal=False,filler=False):
    k=Kit(name,COL);k.group=name+'_Mesh'
    if corner:
        stake(k,-.25,0,2.47,1);stake(k,0,0,2.60,2);stake(k,0,.25,2.43,0)
        outline=[(-.375,.11),(-.11,.11),(-.11,.375),(-.23,.375),(-.23,.23),(-.375,.23)]
        for z in [.68,1.69]:
            n=len(outline);verts=[(x,y,z+dz) for dz in [-.08,.08] for x,y in outline]
            k.mesh('Mitred_Corner_Rail',verts,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'rail')
        sockets={'Snap_Start':[-.375,0,0],'Snap_End':[0,.375,0]};turn=90
        if corner<0:
            for ob in k.objects:
                for v in ob.data.vertices:v.co.y=-v.co.y
                bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
            sockets['Snap_End'][1]=-.375;turn=-90
    else:
        n=round(length/.25)
        for i in range(n):
            h=[2.50,2.39,2.59,2.45,2.55,2.40,2.53,2.46][(i+variant*3)%8]
            stake(k,.125+i*.25,0,2.60 if terminal else h,i+variant)
        for z in [.68,1.69]:
            rail(k,(0,.17,z),(length,.17,z))
            if not filler:
                for x in ([.125] if terminal else [.375,length-.375]):lashing(k,x,0,z)
        sockets={'Snap_Start':[0,0,0],'Snap_End':[length,0,0]};turn=0
    meshes=k.join_modules();ob=next(iter(meshes.values()))
    markers=[k.marker(name+'__'+s,p,'centerline boundary socket, metres') for s,p in sockets.items()]
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert not d['nonmanifold_edges'] and not d['degenerate_faces'],(name,d)
    bm.free();report[name]=d
    contracts[name]={'file':name+'.fbx','sockets_blender':sockets,'marker_names':{s:name+'__'+s for s in sockets},'turn_degrees':turn,'rail_side':'+Y' if corner>=0 else '-Y','nominal_pitch':.25}
    bpy.ops.object.select_all(action='DESELECT')
    for o in [k.root,ob]+markers:o.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
    parts[name]=(k.root,ob,markers)
build('Palisade_Straight_2m_A',2)
build('Palisade_Straight_2m_B',2,variant=1)
build('Palisade_Straight_1m',1)
build('Palisade_Corner_Left',corner=1)
build('Palisade_Corner_Right',corner=-1)
build('Palisade_Terminal',.25,terminal=True)
build('Palisade_Filler_025m',.25,filler=True)
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
(OUT/'snap-contract.json').write_text(json.dumps({'parts':contracts,'rules':['Match boundary sockets, not mesh bounds.','Straight pieces include no duplicate end stakes.','Corners occupy 0.375 m on each centerline leg.','Terminal adds 0.25 m, not a zero-length overlay.','No geometry stretching to fit arbitrary length. Quantize/rebuild the stake row in the adapter.','Visual stake count never sets timber cost.']},indent=2))
# Gallery layout is only a review arrangement; exports above remain local-origin.
layout={'Palisade_Straight_2m_A':(-2.7,1.4,0),'Palisade_Straight_2m_B':(.1,1.4,0),'Palisade_Straight_1m':(2.9,1.4,0),'Palisade_Corner_Left':(-2.2,-.8,0),'Palisade_Corner_Right':(-.2,-.8,0),'Palisade_Terminal':(1.3,-.8,0),'Palisade_Filler_025m':(2.5,-.8,0)}
for name,(root,ob,markers) in parts.items():root.location=layout[name]
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Palisade_Review',bpy.data.cameras.new('Palisade_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1400,1000)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('kit-overview',(7,-12,8),(.5,.4,1.2),9.6)
# Real socket-matched L assembly, with other modules hidden for review.
for name,(root,ob,markers) in parts.items():ob.hide_render=True
for name,loc,angle in [('Palisade_Straight_2m_A',(0,0,0),0),('Palisade_Corner_Left',(2.375,0,0),0),('Palisade_Straight_2m_B',(2.375,.375,0),math.pi/2),('Palisade_Terminal',(2.375,2.375,0),math.pi/2)]:
    root,ob,markers=parts[name];root.location=loc;root.rotation_euler.z=angle;ob.hide_render=False
bpy.context.view_layer.update()
def socket(name,key):return parts[name][0].matrix_world@Vector(contracts[name]['sockets_blender'][key])
for a,b in [('Palisade_Straight_2m_A','Palisade_Corner_Left'),('Palisade_Corner_Left','Palisade_Straight_2m_B'),('Palisade_Straight_2m_B','Palisade_Terminal')]:assert (socket(a,'Snap_End')-socket(b,'Snap_Start')).length<1e-5
render('assembled-interior',(-7,10,7),(1.1,.8,1.15),6.2)
render('assembled-exterior',(7,-10,6),(1.1,.8,1.15),6.2)
render('game-scale',(-7,10,7),(1.1,.8,1.15),6.2,(360,280))
# Mirrored turn uses reversed straight pieces to keep both rails on the same side.
parts['Palisade_Straight_2m_A'][0].location=(-.375,0,0);parts['Palisade_Straight_2m_A'][0].rotation_euler.z=math.pi
parts['Palisade_Corner_Right'][0].location=(0,0,0)
parts['Palisade_Straight_2m_B'][0].location=(0,-2.375,0);parts['Palisade_Straight_2m_B'][0].rotation_euler.z=math.pi/2
bpy.context.view_layer.update()
assert (socket('Palisade_Straight_2m_A','Snap_Start')-socket('Palisade_Corner_Right','Snap_Start')).length<1e-5
assert (socket('Palisade_Straight_2m_B','Snap_End')-socket('Palisade_Corner_Right','Snap_End')).length<1e-5
for name,(root,ob,markers) in parts.items():root.location=layout[name];root.rotation_euler.z=0;ob.hide_render=False
render('kit-overview',(7,-12,8),(.5,.4,1.2),9.6)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.overlay.show_overlays=False;s.region_3d.view_location=(.5,.4,1.2);s.region_3d.view_distance=11;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/palisade-astra-lvl1-v1.blend'))
verified={}
for name in parts:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(meshes)==1
    ob=meshes[0];bm=bmesh.new();bm.from_mesh(ob.data)
    assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces)
    assert not any(p.use_smooth for p in ob.data.polygons) and len(ob.data.color_attributes)>0
    verified[name]={'triangles':sum(len(f.verts)-2 for f in bm.faces),'result':'PASS'};bm.free()
    assert len([o for o in bpy.context.scene.objects if o.type=='EMPTY'])==3
    for marker in contracts[name]['marker_names'].values():assert marker in bpy.context.scene.objects
(OUT/'export-verification.json').write_text(json.dumps(verified,indent=2));print('PASS',verified)
