"""One-eyed camp pug, static flat-shaded character study and staged FBX."""
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
OUT=HERE.parents[1]/'art-staging/camp-pug-astra-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
k=Kit('Camp_Pug',{'fawn':'#C6AA80','light':'#DBC49B','mask':'#51483E','muzzle':'#706151','nose':'#282C2C','ear':'#504538','eye':'#342C26','iris':'#765632','glint':'#EFE8CF','green':'#648D7D','green2':'#80A28B','stitch':'#C8BD91'})
def ellipsoid(name,p,s,color,n=16,rings=10):
    verts=[(p[0],p[1],p[2]-s[2])]
    for j in range(1,rings):
        a=-math.pi/2+j*math.pi/rings
        for i in range(n):
            t=i*math.tau/n
            verts.append((p[0]+s[0]*math.cos(a)*math.cos(t),p[1]+s[1]*math.cos(a)*math.sin(t),p[2]+s[2]*math.sin(a)))
    top=len(verts);verts.append((p[0],p[1],p[2]+s[2]))
    faces=[(0,1+(i+1)%n,1+i) for i in range(n)]
    faces += [(1+j*n+i,1+j*n+(i+1)%n,1+(j+1)*n+(i+1)%n,1+(j+1)*n+i) for j in range(rings-2) for i in range(n)]
    faces += [(top,1+(rings-2)*n+i,1+(rings-2)*n+(i+1)%n) for i in range(n)]
    return k.mesh(name,verts,faces,color)
k.group='Body'
ellipsoid('Torso',(0,.07,.46),(.235,.39,.255),'fawn',24,16)
ellipsoid('Chest',(0,-.20,.47),(.23,.23,.30),'fawn',24,16)
ellipsoid('Neck',(0,-.27,.63),(.22,.22,.24),'fawn',24,16)
ellipsoid('Head',(0,-.36,.73),(.285,.23,.255),'fawn',24,16)
ellipsoid('Face',(0,-.493,.71),(.233,.10,.191),'mask',24,16)
for x in [-.16,.16]:
    ellipsoid('Front_Leg',(x,-.22,.25),(.077,.088,.225),'fawn')
    ellipsoid('Front_Paw',(x,-.26,.067),(.093,.126,.067),'fawn')
    ellipsoid('Haunch',(x,.30,.37),(.12,.165,.19),'fawn')
    ellipsoid('Hock',(x,.34,.19),(.067,.08,.145),'fawn')
    ellipsoid('Rear_Paw',(x,.29,.065),(.086,.12,.065),'fawn')
# Fuse anatomical masses before simplification so the limbs share the body surface.
body=k.join_modules()['Body']
bpy.context.view_layer.objects.active=body
remesh=body.modifiers.new('Connected anatomy','REMESH');remesh.mode='VOXEL';remesh.voxel_size=.012;remesh.use_smooth_shade=False
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth=body.modifiers.new('Blend anatomical transitions','SMOOTH');smooth.factor=.7;smooth.iterations=4
bpy.ops.object.modifier_apply(modifier=smooth.name)
dec=body.modifiers.new('Faceted game mesh','DECIMATE');dec.ratio=.095;dec.use_collapse_triangulate=True
bpy.ops.object.modifier_apply(modifier=dec.name)
for a in list(body.data.color_attributes):body.data.color_attributes.remove(a)
attr=body.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
for p in body.data.polygons:
    p.use_smooth=False;c=p.center
    color='fawn'
    if c.y<-.49 and (c.x/.24)**2+((c.z-.705)/.21)**2<1:color='mask'
    elif c.y<-.29 and .30<c.z<.56:color='light'
    rgb=(*ss.srgb_to_linear(ss._hex(k.colors[color])),1)
    for i in p.loop_indices:attr.data[i].color=rgb

def tube(name,points,radii,color,n=7):
    points=[Vector(p) for p in points];verts=[]
    for j,p in enumerate(points):
        d=(points[min(j+1,len(points)-1)]-points[max(j-1,0)]).normalized()
        u=Vector((0,1,0));v=d.cross(u).normalized();u=v.cross(d).normalized()
        for i in range(n):verts.append(p+radii[j]*(math.cos(i*math.tau/n)*u+math.sin(i*math.tau/n)*v))
    faces=[tuple(reversed(range(n))),tuple((len(points)-1)*n+i for i in range(n))]
    faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(points)-1) for i in range(n)]
    return k.mesh(name,verts,faces,color)
k.group='Curled_Tail'
points=[];radii=[]
for i in range(25):
    t=i/24;a=-math.pi/2+t*math.tau*1.12;r=.133*(1-.60*t)
    points.append((r*math.cos(a),.35+.058*t,.74+r*math.sin(a)))
    radii.append(.043*(1-.73*t))
tube('Tail',points,radii,'fawn',8)
k.group='Folded_Ears'
for s in [-1,1]:
    verts=[(s*x,y,z) for x,y,z in [(.18,-.36,.929),(.31,-.36,.85),(.275,-.54,.75),(.20,-.555,.81),(.19,-.41,.92),(.265,-.465,.863)]]
    k.mesh('Button_Ear',verts,[(0,1,5),(1,2,5),(2,3,5),(3,4,5),(4,0,5),(0,4,3,2,1)],'ear')
k.group='Muzzle'
for x in [-.076,.076]:ellipsoid('Muzzle_Pad',(x,-.589,.637),(.093,.057,.069),'muzzle',12,7)
ellipsoid('Chin',(0,-.563,.565),(.104,.058,.035),'mask',12,6)
ellipsoid('Nose',(0,-.641,.695),(.066,.037,.041),'nose',10,6)
tube('Philtrum',[(0,-.647,.674),(0,-.647,.64),(0,-.643,.613)],[.006,.006,.005],'nose',5)
for s in [-1,1]:tube('Mouth',[(0,-.643,.613),(s*.039,-.638,.601),(s*.073,-.62,.608)],[.005,.006,.003],'nose',5)
k.group='Left_Eye'
# Facing -Y makes +X her anatomical LEFT. No eyeball is created on -X.
ellipsoid('Left_Eye_Rim',(.133,-.559,.787),(.080,.044,.079),'muzzle',14,9)
ellipsoid('Left_Eyeball',(.133,-.590,.79),(.060,.026,.060),'eye',16,10)
ellipsoid('Left_Iris',(.127,-.612,.79),(.031,.007,.036),'iris',12,7)
ellipsoid('Left_Pupil',(.126,-.618,.793),(.020,.005,.027),'nose',12,7)
ellipsoid('Catchlight',(.112,-.623,.810),(.009,.004,.012),'glint',8,6)
k.group='Right_Healed_Eyelid'
ellipsoid('Healed_Lid',(-.133,-.552,.787),(.075,.023,.034),'muzzle',12,8)
tube('Closed_Lid_Seam',[(-.194,-.563,.791),(-.17,-.573,.779),(-.134,-.576,.776),(-.10,-.573,.780),(-.076,-.559,.790)],[.003,.004,.004,.004,.003],'mask',6)
k.group='Brow_Folds'
for s in [-1,1]:tube('Brow',[(s*.063,-.536,.858),(s*.119,-.551,.872),(s*.172,-.536,.861)],[.007,.010,.005],'fawn',6)
tube('Forehead_Fold',[(-.08,-.527,.905),(0,-.551,.916),(.07,-.527,.905)],[.003,.006,.003],'muzzle',5)
k.group='Neckerchief'
# A closed cloth strip follows the neck; the front flap stays outside the chest.
n=20;verts=[]
for inset,z in [(0,.525),(0,.595),(-.012,.595),(-.012,.525)]:
    for i in range(n):
        a=i*math.tau/n;verts.append(((.226+inset)*math.cos(a),-.235+(.213+inset)*math.sin(a),z))
faces=[(j*n+i,j*n+(i+1)%n,((j+1)%4)*n+(i+1)%n,((j+1)%4)*n+i) for j in range(4) for i in range(n)]
k.mesh('Cloth_Band',verts,faces,'green')
k.mesh('Front_Flap',[(-.15,-.427,.542),(.15,-.427,.542),(.035,-.433,.32),(-.15,-.440,.542),(.15,-.440,.542),(.035,-.446,.32)],[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],'green2')
ellipsoid('Side_Knot',(-.222,-.235,.55),(.039,.055,.037),'green',10,6)
modules=k.join_modules()
# Small mascot proportions: about 0.61m to the top of the head, all feet grounded.
scale=.62
lowest=min(v.co.z for v in modules['Body'].data.vertices)
for ob in modules.values():
    for v in ob.data.vertices:v.co=Vector((v.co.x*scale,v.co.y*scale,(v.co.z-lowest)*scale))
    ob.data.update()
k.root['anatomical_right']='-X; closed healed lid; no eyeball'
k.root['forward']='-Y in Blender authoring coordinates'
k.marker('Ground_Anchor',(0,0,0),'root placement reference')
k.marker('Head_Anchor',(0,-.36*scale,(.73-lowest)*scale),'head reference; not rigged')
report={};points=[]
for name,ob in modules.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-12 for f in bm.faces)}
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    if name=='Body':
        unseen=set(bm.verts);components=0
        while unseen:
            components+=1;stack=[unseen.pop()]
            while stack:
                for e in stack.pop().link_edges:
                    for v in e.verts:
                        if v in unseen:unseen.remove(v);stack.append(v)
        assert components==1,components
        d['connected_components']=components
    report[name]=d;points.extend(v.co.copy() for v in ob.data.vertices);bm.free()
lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
(OUT/'validation.json').write_text(json.dumps({'modules':report,'triangles':sum(d['triangles'] for d in report.values()),'bounds':[lo,hi],'right_eye':'Absent; healed closed eyelid on anatomical right (-X).','rigged':False},indent=2))
def export(name,accessory=True):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in list(modules.values())+[o for o in bpy.context.scene.objects if o.type=='EMPTY']:
        if accessory or ob.name!='Neckerchief':ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
export('camp-pug.fbx');export('camp-pug-no-neckerchief.fbx',False)
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.55;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#718F99'))
cam=bpy.data.objects.new('Pug_Review',bpy.data.cameras.new('Pug_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,ortho=1.25,res=(1200,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,-.03,.31))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=ortho
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('front',(0,-3,1.02));render('right-healed-eye',(-1.8,-2.7,1.3));render('rear',(1.8,2.7,1.3));render('game-scale',(.9,-1.6,1.25),1.3,(320,300));render('front-three-quarter',(1.8,-2.7,1.3))
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.overlay.show_overlays=False;s.region_3d.view_location=(0,-.03,.31);s.region_3d.view_distance=1.5;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/camp-pug-astra-v1.blend'))
print('PASS',sum(d['triangles'] for d in report.values()),lo,hi)
