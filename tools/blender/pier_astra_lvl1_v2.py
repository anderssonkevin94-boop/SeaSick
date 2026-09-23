"""Starter pier: modular deck, separate adjustable pilings and hinged shore ramp."""
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
OUT=HERE.parents[1]/'art-staging/pier-astra-lvl1-v2'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'wood':'#79583F','edge':'#A67B50','plank':'#BD9867','plank2':'#C7A775','iron':'#485251','rope':'#C1AB80','cut':'#C3A16B','repair':'#A88861','endgrain':'#99764E'}
parts={};reports={};contracts={}
def rope(k,name,points,r=.024,n=7,closed=False):
    points=[Vector(p) for p in points];verts=[]
    for j,p in enumerate(points):
        prev=points[(j-1)%len(points)] if closed else points[max(0,j-1)]
        nxt=points[(j+1)%len(points)] if closed else points[min(len(points)-1,j+1)]
        d=(nxt-prev).normalized();u=Vector((1,0,0)) if abs(d.z)>.95 else Vector((0,0,1));v=d.cross(u).normalized();u=v.cross(d).normalized()
        for i in range(n):verts.append(p+r*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)))
    faces=[]
    for j in range(len(points) if closed else len(points)-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,((j+1)%len(points))*n+(i+1)%n,((j+1)%len(points))*n+i))
    if not closed:faces += [tuple(reversed(range(n))),tuple((len(points)-1)*n+i for i in range(n))]
    return k.mesh(name,verts,faces,'rope')
def finish(name,k,sockets):
    mods=k.join_modules();bpy.context.view_layer.update()
    markers=[k.marker(name+'__'+s,p,'local reference') for s,p in sockets.items()]
    d={'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0};points=[]
    for ob in mods.values():
        bm=bmesh.new();bm.from_mesh(ob.data)
        d['triangles']+=sum(len(f.verts)-2 for f in bm.faces);d['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges);d['degenerate_faces']+=sum(f.calc_area()<1e-10 for f in bm.faces)
        assert not any(p.use_smooth for p in ob.data.polygons);points.extend(ob.matrix_world@v.co for v in ob.data.vertices);bm.free()
    assert not d['nonmanifold_edges'] and not d['degenerate_faces'],(name,d)
    d['bounds']=[[min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]]
    reports[name]=d;contracts[name]={'file':name+'.fbx','sockets_blender':sockets,'marker_names':{s:name+'__'+s for s in sockets}}
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [k.root]+list(mods.values())+markers:ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
    parts[name]=(k.root,list(mods.values()),markers)
def deck(name,end=False,variant=False):
    k=Kit(name,COL);k.group=name+'_Deck'
    for i in range(8):
        x=.125+i*.25
        # Closely fitted edge notches, rather than shortening whole post-bay planks.
        x0=x-.119;x1=x+.119;lo=max(x0,.215);hi=min(x1,.535)
        color='plank2' if i%3==1 else 'plank'
        if variant and i==5:
            k.box('Old_Plank_End',(x,-.904,-.07),(.238,1.192,.14),'plank',.012)
            k.box('Replacement_Plank',(x,.606,-.07),(.238,1.788,.14),'repair',.012)
            k.box('Repair_Underside_Cleat',(x,-.30,-.20),(.58,.28,.12),'wood',.015)
        elif lo>=hi:k.box('Deck_Plank',(x,0,-.07),(.238,3,.14),color,.012)
        else:
            spans=[(a,b,w) for a,b,w in [(x0,lo,1.5),(lo,hi,1.11),(hi,x1,1.5)] if b-a>1e-6]
            outline=[]
            for a,b,w in spans:outline.extend([(a,-w),(b,-w)])
            for a,b,w in reversed(spans):outline.extend([(b,w),(a,w)])
            n=len(outline);verts=[(a,b,z) for z in [-.14,0] for a,b in outline]
            ob=k.mesh('Notched_Plank',verts,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)],color)
            bpy.context.view_layer.objects.active=ob;m=ob.modifiers.new('Soft plank edges','BEVEL');m.width=.009;m.segments=1;bpy.ops.object.modifier_apply(modifier=m.name)
    for i in range(8):
        for y in [-.98,.98]:
            k.lathe('Deck_Peg',(.125+i*.25,y,0),[(.014,.001),(.014,.004)],'iron',6)
    for y in [-.98,.98]:k.box('Stringer',(1,y,-.25),(2,.19,.22),'wood',.018)
    k.box('Pile_Crosshead',(.375,0,-.40),(.22,2.91,.20),'wood',.018)
    if end:k.box('Sea_End_Fascia',(1.94,0,-.235),(.12,3,.19),'edge',.018)
    k.group=name+'_Mooring_Posts'
    for y in [-1.30,1.30]:
        k.lathe('Post',(.375,y,0),[(.143,-.50),(.143,.45),(.122,.49)],'wood',8)
        k.lathe('End_Grain',(.375,y,0),[(.119,.490),(.119,.494)],'cut',8)
        k.lathe('Heartwood',(.375,y,0),[(.065,.494),(.065,.497)],'endgrain',8)
        for z in [.445]:
            k.lathe('Upper_Band',(.375,y,0),[(.146,z-.04),(.146,z)],'iron',8)
        k.rod('Mooring_Pin',(.375,y-.15,.29),(.375,y+.15,.29),.034,'edge',8)
        k.lathe('Iron_Collar',(.375,y,0),[(.149,.04),(.149,.10)],'iron',8)
    if end:
        k.group=name+'_Ropes'
        path=[]
        for i in range(65):
            t=i/64;a=t*math.tau*2.6;r=.24-.145*t
            path.append((.78+r*math.cos(a)*1.13,.75+r*math.sin(a)*.83,.032+.004*math.sin(a)))
        rope(k,'Deck_Coil',path,.025,6)
        for z in [.19,.245]:
            path=[(.375+.165*math.cos(i*math.tau/16),1.30+.165*math.sin(i*math.tau/16),z) for i in range(16)]
            rope(k,'Bollard_Wrap',path,.019,6,True)

        rope(k,'Coil_Loose_End',[(.53,.75,.033),(.45,.70,.033),(.42,.59,.033),(.31,.56,.033)],.025,6)
        k.group=name+'_Cleats'
        for y in [-1.24,1.24]:
            k.box('Cleat_Foot',(1.77,y,.035),(.27,.17,.07),'iron',.022)
            for x in [1.70,1.84]:k.box('Cleat_Neck',(x,y,.12),(.055,.06,.12),'iron',.012)
            k.beam('Cleat_Horn',(1.55,y,.17),(1.97,y,.17),.08,.085,'iron')
        k.group=name+'_Fenders'
        for x in [.87,1.70]:
            y=-1.58
            k.box('Lanyard_Anchor',(x,-1.32,.025),(.12,.065,.05),'iron',.01)
            k.lathe('Rope_Fender',(x,y,0),[(.045,-.92),(.088,-.84),(.10,-.52),(.08,-.39),(.037,-.35)],'rope',10)
            for z in [-.80,-.71,-.62,-.53,-.44]:
                path=[(x+.101*math.cos(i*math.tau/12),y+.101*math.sin(i*math.tau/12),z) for i in range(12)]
                rope(k,'Fender_Wrap',path,.013,5,True)
            rope(k,'Fender_Lanyard',[(x,-1.32,.015),(x,-1.46,-.01),(x,-1.58,-.18),(x,-1.58,-.36)],.017,6)
        k.group=name+'_Swim_Ladder'
        # Bent grab rails wrap over the edge, kept outside the central walking strip.
        for x in [1.06,1.61]:
            p=[(x,1.16,.03),(x,1.16,.34),(x,1.23,.44),(x,1.43,.44),(x,1.57,.34),(x,1.60,-1.37)]
            # The rope helper makes a closed tube; recolor as worn iron.
            ob=rope(k,'Grab_Rail',p,.032,8)
            attr=ob.data.color_attributes['Col']
            for item in attr.data:item.color=(*ss.srgb_to_linear(ss._hex(COL['iron'])),1)
        for z in [-.20,-.48,-.76,-1.04,-1.32]:
            k.box('Ladder_Step',(1.335,1.60,z),(.59,.16,.065),'edge',.012)

    finish(name,k,{'Snap_Land':[0,0,0],'Snap_Sea':[2,0,0],'Pile_Left':[.375,1.30,-.50],'Pile_Right':[.375,-1.30,-.50]})
deck('Pier_Deck_2m');deck('Pier_Deck_2m_Repaired',variant=True);deck('Pier_Sea_End_2m',True,True)
k=Kit('Pier_Piling_1m',COL);k.group='Piling_Shaft'
k.lathe('Pile',(0,0,0),[(.14,-1),(.14,0)],'wood',8)
finish('Pier_Piling_1m',k,{'Top':[0,0,0],'Bottom':[0,0,-1]})
k=Kit('Pier_Shore_Ramp',COL);k.group='Ramp_Deck'
for i in range(10):k.box('Ramp_Plank',(-.12-i*.24,0,-.07),(.228,2.4,.14),'plank2' if i%3==1 else 'plank',.01)
for y in [-.85,.85]:k.box('Ramp_Stringer',(-1.20,y,-.23),(2.40,.16,.18),'wood',.015)
for x in [-.20,-2.19]:
    for y in [-.95,.95]:k.box('End_Strap',(x,y,.014),(.20,.13,.027),'iron',.004)
finish('Pier_Shore_Ramp',k,{'Hinge':[0,0,0],'Toe':[-2.4,0,0]})
(OUT/'validation.json').write_text(json.dumps(reports,indent=2))
(OUT/'snap-contract.json').write_text(json.dumps({'parts':contracts,'deck_top_local_z':0,'runtime_root_height_above_mean_water':1.2,'axis':'Blender +X land to sea; +Y across deck; +Z up','nominal_width':3,'repeat_length':2,'nominal_runtime_length':14,'maximum_runtime_length':24,'ramp_length':2.4,'pile_unit_length':1,'notes':['Only piling shafts may be scaled axially. Do not stretch decking or caps.','Assembly is centered along X for the existing Pier.cs contract.','Sample support lengths and ramp angle are previews, not shoreline queries.']},indent=2))
# Build an exact 14m review, with supports reaching 2.8m below mean water.
for root,obs,marks in parts.values():
    for ob in obs:ob.hide_render=True;ob.hide_set(True)
assembly=bpy.data.objects.new('Pier_14m_Review',None);scene=bpy.context.scene;scene.collection.objects.link(assembly)
instances=[]
def instance(name,loc,angle=0,zscale=1):
    root=bpy.data.objects.new(name+'_Instance',None);scene.collection.objects.link(root);root.parent=assembly;root.location=loc;root.rotation_euler.y=angle;root.scale.z=zscale
    for src in parts[name][1]:
        ob=src.copy();ob.data=src.data;scene.collection.objects.link(ob);ob.parent=root;ob.hide_render=False;ob.hide_set(False);instances.append(ob)
    return root
for i in range(7):
    x=-7+i*2;instance('Pier_Sea_End_2m' if i==6 else 'Pier_Deck_2m_Repaired' if i in [1,4] else 'Pier_Deck_2m',(x,0,0))
    for y in [-1.3,1.3]:instance('Pier_Piling_1m',(x+.375,y,-.50),zscale=3.50)
ramp_angle=-math.asin(1/2.4)
instance('Pier_Shore_Ramp',(-7,0,0),ramp_angle)
scene.view_layers[0].update()
assert abs((-7+7*2)-7)<1e-8
ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Pier_Review_Camera',bpy.data.cameras.new('Pier_Review_Camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1600,1000)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('structure-review',(13,-18,12),(-.8,0,-1.4),19.8)
# Review-only water plane at mean water; never exported with the asset.
water=Kit('Review_Water',{'water':'#4B96B0'});water.group='Review_Only_Water';plane=water.box('Water',(-.8,0,-1.235),(24,12,.07),'water',0)
render('front-three-quarter',(13,-18,12),(-.8,0,-.2),19.8)
render('sea-end-detail',(11,-7,5),(5.8,0,-.1),5.3,(1200,1100))
render('ladder-side-detail',(10,8,5),(5.8,0,-.1),5.3,(1200,1100))
render('game-scale',(13,-18,12),(-.8,0,-.2),19.8,(480,300))
render('front-three-quarter',(13,-18,12),(-.8,0,-.2),19.8)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.overlay.show_overlays=False;s.region_3d.view_location=(-.8,0,-.2);s.region_3d.view_distance=22;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/pier-astra-lvl1-v2.blend'))
verification={}
for name in parts:
    bpy.ops.object.select_all(action='DESELECT')
    for ob in bpy.context.scene.objects:ob.hide_set(False);ob.select_set(True)
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];tri=0
    for ob in meshes:
        bm=bmesh.new();bm.from_mesh(ob.data)
        assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces)
        assert not any(p.use_smooth for p in ob.data.polygons) and len(ob.data.color_attributes)>0
        tri+=sum(len(f.verts)-2 for f in bm.faces);bm.free()
    for marker in contracts[name]['marker_names'].values():assert marker in bpy.context.scene.objects
    verification[name]={'result':'PASS','triangles':tri}
(OUT/'export-verification.json').write_text(json.dumps(verification,indent=2));print('PASS',verification)
