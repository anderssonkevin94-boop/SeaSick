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
OUT=HERE.parents[1]/'art-staging/pier-astra-lvl1-v4'
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
    mods=k.join_modules()
    extras=[]
    if name.startswith(('Pier_Deck','Pier_Infill','Pier_Sea_End')):
        for ob in mods.values():ob.location.z+=1.2
        sockets={key:([v[0],v[1],v[2]+1.2] if not key.startswith('Snap_') else v) for key,v in sockets.items()}
    if name=='Pier_Shore_Ramp':
        hinge=k.marker('Ramp_Hinge',[0,0,1.2],'rotate local Y by -atan2(drop,2.4)')
        span=k.marker('Ramp_Span',[0,0,0],'scale only local X by hypot(2.4,drop)/2.4');span.parent=hinge
        for ob in mods.values():ob.parent=span
        extras=[hinge,span]
    bpy.context.view_layer.update()
    markers=[k.marker(name+'__'+s,p,'local reference') for s,p in sockets.items()]
    if name=='Pier_Shore_Ramp':
        toe=next(m for m in markers if m.name.endswith('__Toe'));toe.parent=span;toe.location=(-2.4,0,0)
    markers+=extras
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
def deck(name,length=2,supported=True,end=False):
    k=Kit(name,COL);k.group=name+'_Deck'
    boundaries=[0,.34,.70,1] if length==1 else [0,.39,.81,1.19,1.62,2]
    for i in range(len(boundaries)-1):
        x0=boundaries[i]+.009;x1=boundaries[i+1]-.009
        low=-1.5+[0,.05,.02,.07,.03][i];high=1.5-[.04,0,.06,.01,.05][i]
        lo=max(x0,.145);hi=min(x1,.655)
        spans=[(x0,x1,low,high)]
        if supported and lo<hi:
            spans=[(a,b,l,h) for a,b,l,h in [(x0,lo,low,high),(lo,hi,-.985,.985),(hi,x1,low,high)] if b-a>1e-6]
        outline=[]
        for a,b,l,h in spans:outline.extend([(a,l),(b,l)])
        for a,b,l,h in reversed(spans):outline.extend([(b,h),(a,h)])
        n=len(outline)
        ob=k.mesh('Broad_Fitted_Plank',[(a,b,z) for z in [-.18,0] for a,b in outline],
            [tuple(reversed(range(n))),tuple(range(n,2*n))]+[(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)],
            'plank2' if i%3==1 else 'plank')
        bpy.context.view_layer.objects.active=ob
        m=ob.modifiers.new('Hand dressed edges','BEVEL');m.width=.013;m.segments=1
        bpy.ops.object.modifier_apply(modifier=m.name)
        for y in [-.84,.84]:
            k.lathe('Wood_Peg',((x0+x1)/2,y,0),[(.021,-.004),(.021,0)],'endgrain',6)
    for y in [-.84,.84]:k.box('Deep_Stringer',(length/2,y,-.34),(length,.25,.32),'wood',.02)
    sockets={'Snap_Land':[0,0,0],'Snap_Sea':[length,0,0],'Deck_Land':[0,0,0],'Deck_Sea':[length,0,0]}
    if supported:
        k.box('Heavy_Crosshead',(.4,0,-.62),(.34,2.94,.24),'wood',.024)
        sockets.update({'Pile_Left':[.4,1.24,-.74],'Pile_Right':[.4,-1.24,-.74]})
    if end:
        k.box('End_Fascia',(length-.06,0,-.32),(.12,3,.25),'edge',.02)
        k.group=name+'_Mooring_Cleat'
        cx=length-.23
        k.box('Cleat_Base',(cx,-1.12,.035),(.36,.18,.07),'iron',.018)
        for dx in [-.09,.09]:k.box('Cleat_Neck',(cx+dx,-1.12,.115),(.065,.07,.12),'iron',.01)
        k.beam('Cleat_Horns',(cx-.20,-1.12,.18),(cx+.20,-1.12,.18),.08,.08,'iron')
        sockets['Mooring_Tie']=[cx,-1.12,.18]
        k.group=name+'_Lantern_Post'
        k.box('Lamp_Standard',(.4,1.24,1.18),(.19,.19,1.30),'wood',.02)
        k.beam('Lamp_Arm',(.4,1.35,1.85),(.4,.52,1.85),.15,.15,'edge')
        k.beam('Arm_Brace',(.4,1.24,1.39),(.4,.90,1.78),.085,.085,'wood')
        k.group=name+'_Lantern_Frame'
        k.rod('Hanger',(.4,.58,1.80),(.4,.58,1.58),.018,'iron',6)
        k.box('Lantern_Base',(.4,.58,1.10),(.30,.30,.07),'iron',.025)
        k.lathe('Lantern_Hood',(.4,.58,0),[(.23,1.47),(.23,1.51),(.09,1.63)],'iron',4)
        for dx in [-.125,.125]:
            for dy in [-.125,.125]:
                k.beam('Corner_Frame',(.4+dx,.58+dy,1.12),(.4+dx,.58+dy,1.48),.03,.03,'iron')
        k.group=name+'_Lantern_Glow'
        glow=k.box('Amber_Panes',(.4,.58,1.30),(.22,.22,.32),'cut',.008)
        mat=bpy.data.materials.new('Lantern_Amber_Emission');mat.use_nodes=True
        bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        bs.inputs['Base Color'].default_value=(1,.45,.09,1)
        bs.inputs['Emission Color'].default_value=(1,.32,.035,1)
        bs.inputs['Emission Strength'].default_value=2.5
        glow.data.materials.clear();glow.data.materials.append(mat)
        attr=glow.data.color_attributes['Col']
        for item in attr.data:item.color=(1,.64,.19,1)
        sockets['Light_Source']=[.4,.58,1.30]
    finish(name,k,sockets)

for length in [1,2]:
    deck('Pier_Deck_%dm'%length,length)
    deck('Pier_Infill_%dm'%length,length,supported=False)
    deck('Pier_Sea_End_%dm'%length,length,end=True)
k=Kit('Pier_Piling_1m',COL);k.group='Piling_Shaft'
k.lathe('Plain_Pile',(0,0,0),[(.23,-1),(.23,0)],'wood',8)
finish('Pier_Piling_1m',k,{'Top':[0,0,0],'Bottom':[0,0,-1]})
k=Kit('Pier_Pile_Cap',COL);k.group='Pile_Cap'
k.lathe('Heavy_Cap',(0,0,0),[(.23,0),(.23,1.22),(.20,1.28)],'wood',8)
k.lathe('End_Grain',(0,0,0),[(.198,1.28),(.198,1.285)],'cut',8)
k.lathe('Heartwood',(0,0,0),[(.104,1.286),(.104,1.289)],'endgrain',8)
k.lathe('Post_Band',(0,0,0),[(.234,1.11),(.234,1.17)],'iron',8)
finish('Pier_Pile_Cap',k,{'Shaft_Top':[0,0,0],'Cap_Top':[0,0,1.289]})
k=Kit('Pier_Shore_Ramp',COL);k.group='Ramp_Deck'
for i in range(6):k.box('Broad_Ramp_Plank',(-.2-i*.4,0,-.09),(.382,2.4,.18),'plank2' if i%3==1 else 'plank',.013)
for y in [-.85,.85]:k.box('Ramp_Stringer',(-1.20,y,-.28),(2.40,.22,.20),'wood',.018)
finish('Pier_Shore_Ramp',k,{'Hinge':[0,0,1.2],'Toe':[-2.4,0,1.2]})
k=Kit('Pier_Ramp_Foot_Pad',COL);k.group='Foot_Pad'
for i in range(2):k.box('Landing_Plank',(-.16-i*.32,0,-.09),(.308,2.50,.18),'plank2' if i else 'plank',.013)
for y in [-.9,.9]:k.box('Landing_Bearer',(-.32,y,-.235),(.64,.19,.11),'wood',.012)
finish('Pier_Ramp_Foot_Pad',k,{'Toe_Contact':[0,0,0],'Shore_Edge':[-.64,0,0]})
(OUT/'validation.json').write_text(json.dumps(reports,indent=2))
(OUT/'snap-contract.json').write_text(json.dumps({'parts':contracts,'deck_top_local_z':1.2,'module_root':'land end at mean water','deck_width':3,'axis':'Blender +X sea, +Y width, +Z up','deck_lengths':[1,2],'pile_attach_z':.46,'pile_length':1,'cap_height':1.289,'ramp_run':2.4,'ramp_drop_range':[.3,2.5],'ramp_equations':{'hinge_local':[0,0,1.2],'angle_Y':'-atan2(drop,2.4)','span_scale_X':'sqrt(2.4*2.4+drop*drop)/2.4','foot_pad_position':[-2.4,0,'1.2-drop'],'foot_pad_rotation':'identity'}},indent=2))
scene=bpy.context.scene
# Check the exact ramp geometry contract at both limits and intermediate drops.
hinge=next(m for m in parts['Pier_Shore_Ramp'][2] if m.name=='Ramp_Hinge')
span=next(m for m in parts['Pier_Shore_Ramp'][2] if m.name=='Ramp_Span')
toe=next(m for m in parts['Pier_Shore_Ramp'][2] if m.name.endswith('__Toe'))
ramp_tests={}
for drop in [.3,.5,1.2,2,2.5]:
    hinge.rotation_euler.y=-math.atan2(drop,2.4);span.scale.x=math.hypot(2.4,drop)/2.4
    bpy.context.view_layer.update();p=toe.matrix_world.translation
    assert (p-Vector((-2.4,0,1.2-drop))).length<1e-5,(drop,p)
    ramp_tests[str(drop)]={'toe':list(p),'span_length':math.hypot(2.4,drop),'PASS':True}
hinge.rotation_euler.y=0;span.scale.x=1
for name,d in reports.items():
    if name.startswith(('Pier_Deck','Pier_Infill','Pier_Sea_End')):
        assert abs(d['bounds'][0][1]+1.5)<1e-5 and abs(d['bounds'][1][1]-1.5)<1e-5,(name,d['bounds'])
(OUT/'fit-verification.json').write_text(json.dumps({'ramp':ramp_tests,'deck_width':'PASS 3m','walking_surface':1.2},indent=2))
for root,obs,marks in parts.values():
    for ob in obs:ob.hide_render=True;ob.hide_set(True)
assembly=bpy.data.objects.new('Pier_15m_Review',None);scene.collection.objects.link(assembly)
def instance(name,loc):
    root=bpy.data.objects.new(name+'_Instance',None);scene.collection.objects.link(root);root.parent=assembly;root.location=loc
    mapping={}
    for src in parts[name][1]+parts[name][2]:
        ob=src.copy();scene.collection.objects.link(ob);mapping[src]=ob;ob.hide_render=False;ob.hide_set(False)
    for src,ob in mapping.items():ob.parent=mapping.get(src.parent,root)
    return root,mapping
# 15m sample exercises both bay lengths; four widely spaced pairs of supports.
for x,length,supported,end in [(0,2,True,False),(2,2,False,False),(4,2,True,False),(6,2,False,False),(8,2,True,False),(10,2,False,False),(12,1,False,False),(13,2,True,True)]:
    name=('Pier_Sea_End_' if end else 'Pier_Deck_' if supported else 'Pier_Infill_')+str(length)+'m'
    instance(name,(x,0,0))
    if supported:
        for y in [-1.24,1.24]:
            instance('Pier_Pile_Cap',(x+.4,y,.46))
            shaft,_=instance('Pier_Piling_1m',(x+.4,y,.46));shaft.scale.z=3.26
ramp,rmap=instance('Pier_Shore_Ramp',(0,0,0))
rh=rmap[hinge];rs=rmap[span]
drop=.5;rh.rotation_euler.y=-math.atan2(drop,2.4);rs.scale.x=math.hypot(2.4,drop)/2.4
pad,_=instance('Pier_Ramp_Foot_Pad',(-2.4,0,1.2-drop))
light_data=bpy.data.lights.new('Lantern_Warm_Point','POINT');light_data.energy=45;light_data.color=(1,.48,.16);light_data.shadow_soft_size=.22
light=bpy.data.objects.new('Lantern_Warm_Point',light_data);scene.collection.objects.link(light);light.location=(13.4,.58,2.50)
ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Pier_Review_Camera',bpy.data.cameras.new('Pier_Review_Camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1600,1000)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('structure-review',(20,-18,13),(6,0,0),21)
water=Kit('Review_Water',{'water':'#4B96B0'});water.group='Review_Only_Water';water_plane=water.box('Water',(6,0,-.035),(25,12,.07),'water',0)
render('front-three-quarter',(20,-18,13),(6,0,1),21)
render('sea-end-detail',(19,-7,6),(13.8,0,1.2),5.8,(1200,1100))
render('ramp-detail',(-6,-7,5),(-1,0,.6),5.8,(1200,1100))
water_plane.hide_render=True
for d in [.3,1.2,2.5]:
    rh.rotation_euler.y=-math.atan2(d,2.4);rs.scale.x=math.hypot(2.4,d)/2.4;pad.location.z=1.2-d
    render('ramp-drop-'+str(d),(-6,-7,4),(-1,0,.2),5.8,(900,900))
rh.rotation_euler.y=-math.atan2(.5,2.4);rs.scale.x=math.hypot(2.4,.5)/2.4;pad.location.z=.7
water_plane.hide_render=False
render('front-three-quarter',(20,-18,13),(6,0,1),21)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type='VERTEX';v.overlay.show_overlays=False;v.region_3d.view_location=(6,0,1);v.region_3d.view_distance=23;v.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/pier-astra-lvl1-v4.blend'))
verification={}
for name in parts:
    for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    meshes=[o for o in scene.objects if o.type=='MESH'];tri=0
    for ob in meshes:
        bm=bmesh.new();bm.from_mesh(ob.data)
        assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces)
        assert not any(p.use_smooth for p in ob.data.polygons) and 'Col' in ob.data.color_attributes
        tri+=sum(len(f.verts)-2 for f in bm.faces);bm.free()
    for marker in contracts[name]['marker_names'].values():assert marker in scene.objects
    if name=='Pier_Shore_Ramp':
        assert scene.objects['Ramp_Span'].parent.name=='Ramp_Hinge'
        assert all(o.parent.name=='Ramp_Span' for o in meshes)
        for d in [.3,.5,1.2,2,2.5]:
            scene.objects['Ramp_Hinge'].rotation_euler.y=-math.atan2(d,2.4)
            scene.objects['Ramp_Span'].scale.x=math.hypot(2.4,d)/2.4
            scene.view_layers[0].update()
            p=scene.objects['Pier_Shore_Ramp__Toe'].matrix_world.translation
            assert (p-Vector((-2.4,0,1.2-d))).length<1e-5,(d,p)
    verification[name]={'result':'PASS','triangles':tri}
(OUT/'export-verification.json').write_text(json.dumps(verification,indent=2));print('PASS',verification)
