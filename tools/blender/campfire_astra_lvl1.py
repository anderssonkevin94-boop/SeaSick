"""Level-one campfire art, staged only; no Unity import."""
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
OUT=HERE.parents[1]/'art-staging/campfire-astra-lvl1-v2'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
C={'stone':'#889799','stone2':'#A4ACAA','stone3':'#73828A','wood':'#78523B',
   'end':'#CBA46C','heart':'#A97848','edge':'#A57649','iron':'#43474B',
   'rim':'#68777A','ash':'#625E5A','char':'#343739','coal':'#BC5333',
   'ember':'#F39240','flame':'#F7B450','tip':'#F18139','cream':'#FFE1A0','teal':'#658C82'}
mat=bpy.data.materials.new('Campfire_VertexColor'); mat.use_nodes=True
shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
node=mat.node_tree.nodes.new('ShaderNodeVertexColor'); node.layer_name='Col'
mat.node_tree.links.new(node.outputs['Color'],shader.inputs['Base Color'])
shader.inputs['Roughness'].default_value=.9
root=bpy.data.objects.new('Campfire_Level_1',None); bpy.context.collection.objects.link(root)
root['footprint_xz']=[3.13,1.78]
objects=[]; group='Hearth'
def mesh(name,verts,faces,col):
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob); ob.parent=root
    ob['module']=group; objects.append(ob); me.materials.append(mat)
    attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in me.polygons:
        p.use_smooth=False; color=col[p.index%len(col)] if isinstance(col,list) else col
        for i in p.loop_indices: attr.data[i].color=(*ss.srgb_to_linear(ss._hex(C[color])),1)
    return ob
def box(name,p,size,col,bevel=.015):
    v=[tuple(p[i]+s[i]*size[i]/2 for i in range(3)) for s in
       [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    ob=mesh(name,v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],col)
    if bevel:
        bpy.context.view_layer.objects.active=ob; m=ob.modifiers.new('Worn edges','BEVEL'); m.width=min(bevel,min(size)*.35); m.segments=1
        bpy.ops.object.modifier_apply(modifier=m.name)
    return ob
def rod(name,a,b,r,col,n=8):
    a,b=Vector(a),Vector(b); d=(b-a).normalized(); u=d.cross(Vector((0,0,1)))
    if u.length<.01:u=d.cross(Vector((0,1,0)))
    u.normalize(); v=d.cross(u)
    verts=[p+r*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)) for p in [a,b] for i in range(n)]
    return mesh(name,verts,[tuple(reversed(range(n))),tuple(n+i for i in range(n))]+
        [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],col)
def lathe(name,center,rings,col,n=12):
    # A closed profile can describe a hollow pot without a duplicate inner shell.
    verts=[(center[0]+r*math.cos(i*math.tau/n),center[1]+r*math.sin(i*math.tau/n),z) for r,z in rings for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple((len(rings)-1)*n+i for i in range(n))]
    faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-1) for i in range(n)]
    return mesh(name,verts,faces,col)
def log(name,a,b,r,burning=False):
    a,b=Vector(a),Vector(b); d=(b-a).normalized(); u=d.cross(Vector((0,0,1))).normalized(); v=d.cross(u); n=8
    rings=[(a,r*.30),(a,r*.83),(a,r),(a.lerp(b,.30),r*1.03),(a.lerp(b,.70),r*.94),(b,r*.98),(b,r*.81),(b,r*.30)]
    verts=[p+rr*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)) for p,rr in rings for i in range(n)]
    faces=[tuple(reversed(range(n)))]; cols=['heart']
    for j in range(len(rings)-1):
        for i in range(n):
            faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
            cols.append(['end','heart','wood','char' if burning else 'wood','wood','heart','end'][j])
    faces.append(tuple(7*n+i for i in range(n))); cols.append('heart')
    return mesh(name,verts,faces,cols)

cx=-.25
lathe('Ash_Bed',(cx,0),[(.47,.007),(.49,.034),(.42,.056)],'ash',16)
for i in range(11):
    a=math.tau*i/11; p=(cx+.575*math.cos(a),.575*math.sin(a),.115)
    ob=box('Ring_Stone',(0,0,0),(.31,.235,.205+.018*math.sin(i*2)),['stone','stone2','stone3'][i%3],.06)
    ob.location=p; ob.rotation_euler.z=a+math.pi/2+.04*math.sin(i)
group='Burn_Logs'
for i,y in enumerate([-.21,.02,.23]):log('Hearth_Log',(cx-.38,y-.04,.14),(cx+.34,y+.04,.16),.088,True)
log('Cross_Log',(cx-.19,-.36,.28),(cx+.04,.32,.28),.072,True)
group='Embers'
for i in range(13):
    a=i*2.4; r=.09+.022*i
    lathe('Coal',(cx+r*math.cos(a),r*math.sin(a)),[(.031,.063),(.045,.081),(.027,.102)],'ember' if i%3==0 else 'coal',6)
group='Cooking_Frame'
for x in [cx-.69,cx+.69]:
    rod('Forked_Upright',(x,.28,.03),(x-.025,.28,.79),.045,'wood')
    rod('Fork_Tine',(x-.02,.28,.64),(x+.07,.28,.80),.026,'edge',6)
rod('Spit',(cx-.79,.28,.755),(cx+.79,.28,.755),.022,'iron')
rod('Spit_Grip',(cx+.68,.28,.755),(cx+.83,.28,.755),.036,'edge')
group='Cooking_Pot'
lathe('Kettle',(cx,.28),[(.11,.44),(.18,.475),(.21,.58),(.205,.655),(.22,.666),(.216,.69),(.189,.69),(.181,.64),(.181,.575),(.145,.50),(.085,.482)],'iron')
for i in range(10):
    a=math.pi*i/10; b=math.pi*(i+1)/10
    rod('Bail',(cx+.215*math.cos(a),.28,.615+.12*math.sin(a)),(cx+.215*math.cos(b),.28,.615+.12*math.sin(b)),.012,'rim',6)
rod('Pot_Hanger',(cx,.28,.735),(cx,.28,.759),.012,'iron',6)
group='Seat'
for y in [-.40,.40]:box('Seat_Foot',(1.08,y,.125),(.35,.16,.25),'wood',.025)
for x in [.98,1.18]:box('Seat_Plank',(x,0,.295),(.19,1.16,.12),'edge',.027)
for y in [-.40,.40]:
    for x in [.98,1.18]:rod('Seat_Peg',(x,y,.35),(x,y,.36),.021,'iron',6)
group='Fuel_Cradle'
for y in [-.34,.30]:
    box('Fuel_Sleeper',(-1.24,y,.055),(.43,.10,.11),'edge')
    for x in [-1.43,-1.05]:rod('Cradle_Peg',(x,y,.04),(x,y,.34),.035,'wood',6)
for i,(x,z) in enumerate([(-1.33,.17),(-1.15,.17),(-1.24,.32)]):
    group=f'Fuel_Log_{i+1:02d}';log(group,(x,-.46,z),(x,.40,z),.075)

modules={}; groups={}
for ob in objects:groups.setdefault(ob['module'],[]).append(ob)
for name,items in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in items:ob.select_set(True)
    bpy.context.view_layer.objects.active=items[0]
    if len(items)>1:bpy.ops.object.join()
    items[0].name=name; modules[name]=items[0]
objects=list(modules.values())
for name,p,role in [('Fire_Anchor',(cx,0,.20),'future_fire_effect'),('Fuel_Anchor',(-1.24,0,0),'fuel_storage'),('Sit_Anchor',(1.08,0,.355),'seat_faces_negative_x')]:
    ob=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(ob); ob.parent=root; ob.location=p; ob['role']=role
def state(name):
    for key,ob in modules.items():
        hidden=key=='Embers' and name=='cold'
        ob.hide_render=hidden; ob.hide_set(hidden)
report={}; points=[]; bpy.context.view_layer.update()
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;points += [ob.matrix_world@v.co for v in ob.data.vertices];bm.free()
lo=[min(v[i] for v in points) for i in range(3)];hi=[max(v[i] for v in points) for i in range(3)]
assert max(abs(lo[0]),abs(hi[0]))<=1.565 and max(abs(lo[1]),abs(hi[1]))<=.89 and hi[2]<=.84,(lo,hi)
validation={'modules':report,'triangles':sum(v['triangles'] for v in report.values()),'bounds':[lo,hi]}
(OUT/'validation.json').write_text(json.dumps(validation,indent=2))
(OUT/'state-contract.json').write_text(json.dumps({'version':2,'default_state':'cold','states':{'cold':{'Embers':False,'external_fire_effect':False},'embers':{'Embers':True,'external_fire_effect':False},'lit':{'Embers':True,'external_fire_effect':True}},'fuel_slots':['Fuel_Log_01','Fuel_Log_02','Fuel_Log_03'],'fuel_slots_are_not_gameplay_capacity':True,'fuel_visibility':'Show the first N slots for a display count from 0 to 3. Inventory capacity and count mapping must be decided by gameplay. Do not loop or randomize stock changes.','fire':'No flame geometry, smoke, light, particles or fire animation included. Attach future effects to Fire_Anchor. Embers are optional colored geometry, not emissive VFX.','burn_logs':'Burn_Logs is a separate static hearth-log module, not part of the storage count.','integration':'Art only, not imported into Unity. See README.md.'},indent=2))
empties=[o for o in bpy.context.scene.objects if o.type=='EMPTY']
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects+empties:
        if ob.type=='EMPTY' or not ob.hide_render:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
state('embers');export('campfire-state-kit.fbx');state('cold');export('campfire-cold.fbx')
for key,ob in modules.items():
    if key.startswith('Fuel_Log_'):ob.hide_render=True;ob.hide_set(True)
export('campfire-empty.fbx')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65;scene.display.shading.show_shadows=False
scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Campfire_Review',bpy.data.cameras.new('Campfire_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye=(4,-6,4),res=(1400,1000)):
    cam.location=eye;cam.rotation_euler=(Vector((-.05,0,.26))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.8
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for name in ['embers','cold']:state(name);render('state-'+name)
render('opposite-three-quarter',(-4,-6,4));render('game-scale',res=(360,260));render('front-three-quarter')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.color_type='VERTEX';area.spaces.active.region_3d.view_location=(-.05,0,.26);area.spaces.active.region_3d.view_distance=4.6;area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/campfire-astra-lvl1-v2.blend'))
print(json.dumps(validation,indent=2))
