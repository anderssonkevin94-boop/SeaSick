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
OUT=HERE.parents[1]/'art-staging/pier-astra-lvl1-v3'
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
def deck(name,supported=True,end=False):
    k=Kit(name,COL);k.group=name+'_Deck'
    boundaries=[0,.39,.81,1.19,1.62,2]
    offsets=[-.045,.055,-.025,.035,-.055]
    for i in range(5):
        x0=boundaries[i]+.009;x1=boundaries[i+1]-.009
        shift=offsets[i];extent=1.5+[.015,-.025,.045,-.015,.025][i]
        lo=max(x0,.145);hi=min(x1,.655)
        spans=[(x0,x1,-extent+shift,extent+shift)]
        if supported and lo<hi:
            spans=[(a,b,l,h) for a,b,l,h in [(x0,lo,-extent+shift,extent+shift),(lo,hi,-.985,.985),(hi,x1,-extent+shift,extent+shift)] if b-a>1e-6]
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
            k.lathe('Wood_Peg',((x0+x1)/2,y,0),[(.021,.001),(.021,.004)],'endgrain',6)
    for y in [-.84,.84]:k.box('Deep_Stringer',(1,y,-.34),(2,.25,.32),'wood',.02)
    sockets={'Snap_Land':[0,0,0],'Snap_Sea':[2,0,0]}
    if supported:
        k.box('Heavy_Crosshead',(.4,0,-.62),(.34,2.94,.24),'wood',.024)
        k.group=name+'_Mooring_Posts'
        for y in [-1.24,1.24]:
            k.lathe('Heavy_Post',(.4,y,0),[(.23,-.74),(.23,.48),(.20,.54)],'wood',8)
            k.lathe('End_Grain',(.4,y,0),[(.198,.54),(.198,.545)],'cut',8)
            k.lathe('Heartwood',(.4,y,0),[(.104,.546),(.104,.549)],'endgrain',8)
            k.lathe('Post_Band',(.4,y,0),[(.234,.37),(.234,.43)],'iron',8)
        sockets.update({'Pile_Left':[.4,1.24,-.74],'Pile_Right':[.4,-1.24,-.74]})
    if end:
        k.box('End_Fascia',(1.94,0,-.32),(.12,3,.25),'edge',.02)
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

deck('Pier_Deck_2m')
deck('Pier_Infill_2m',supported=False)
deck('Pier_Sea_End_2m',end=True)
k=Kit('Pier_Piling_1m',COL);k.group='Piling_Shaft'
k.lathe('Pile',(0,0,0),[(.23,-1),(.23,0)],'wood',8)
finish('Pier_Piling_1m',k,{'Top':[0,0,0],'Bottom':[0,0,-1]})
k=Kit('Pier_Shore_Ramp',COL);k.group='Ramp_Deck'
for i in range(6):
    k.box('Broad_Ramp_Plank',(-.2-i*.4,[-.025,.025,0][i%3],-.09),(.382,2.4,.18),'plank2' if i%3==1 else 'plank',.013)
for y in [-.85,.85]:k.box('Ramp_Stringer',(-1.20,y,-.28),(2.40,.22,.20),'wood',.018)
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
    x=-7+i*2;instance('Pier_Sea_End_2m' if i==6 else 'Pier_Infill_2m' if i%2 else 'Pier_Deck_2m',(x,0,0))
    if i%2==0:
        for y in [-1.24,1.24]:instance('Pier_Piling_1m',(x+.4,y,-.74),zscale=3.26)
light_data=bpy.data.lights.new('Lantern_Warm_Point','POINT');light_data.energy=45;light_data.color=(1,.48,.16);light_data.shadow_soft_size=.22
light=bpy.data.objects.new('Lantern_Warm_Point',light_data);scene.collection.objects.link(light);light.location=(5.4,.58,1.30)
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
render('lantern-side-detail',(10,8,5),(5.8,0,-.1),5.3,(1200,1100))
render('game-scale',(13,-18,12),(-.8,0,-.2),19.8,(480,300))
render('front-three-quarter',(13,-18,12),(-.8,0,-.2),19.8)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.overlay.show_overlays=False;s.region_3d.view_location=(-.8,0,-.2);s.region_3d.view_distance=22;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/pier-astra-lvl1-v3.blend'))
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

