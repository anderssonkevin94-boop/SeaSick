"""Reference deckhand: posed visual study and neutral unrigged source."""
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
ROOT=HERE.parents[1]
OUT=ROOT/'art-staging/crew-astra-reference-v1'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=HERE/'source/crew-astra-reference-v1.blend'
PALETTE={'linen':'#E8DFC7','cuff':'#EFE7D4','skin':'#D99259',
         'pants':'#42677D','belt':'#62452D','boot':'#483628',
         'sole':'#352C25','cap':'#38414B','capband':'#2B333B',
         'hair':'#392B22','eye':'#302921','button':'#857B62'}
COL={k:ss.srgb_to_linear(ss._hex(v)) for k,v in PALETTE.items()}
PARTS=[]


def material(name):
    m=bpy.data.materials.new(name);m.use_nodes=True
    nodes=m.node_tree.nodes
    attr=nodes.new('ShaderNodeVertexColor');attr.layer_name='Col'
    bsdf=nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.85
    m.node_tree.links.new(attr.outputs['Color'],bsdf.inputs['Base Color'])
    return m


def mesh(name,verts,faces,color,bevel=0):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    o=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(o)
    me.materials.append(SKIN if color=='skin' else CLOTH)
    attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in me.polygons:
        for i in p.loop_indices:attr.data[i].color=(*COL[color],1)
    me.color_attributes.active_color=attr
    bm=bmesh.new();bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    if bevel:
        bpy.context.view_layer.objects.active=o
        m=o.modifiers.new('Small tailored edge','BEVEL');m.width=bevel;m.segments=1
        m.limit_method='ANGLE';m.angle_limit=.65
        bpy.ops.object.modifier_apply(modifier=m.name)
    o['surface']='skin' if color=='skin' else 'cloth'
    PARTS.append(o)
    return o


RECT=[(-.72,-1),(.72,-1),(1,-.72),(1,.72),(.72,1),(-.72,1),(-1,.72),(-1,-.72)]


def loft(name,rows,color,shape=RECT,bevel=0):
    verts=[(x+u*rx,y+v*ry,z) for x,y,z,rx,ry in rows for u,v in shape]
    n=len(shape);faces=[tuple(reversed(range(n)))]
    for j in range(len(rows)-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    faces.append(tuple((len(rows)-1)*n+i for i in range(n)))
    return mesh(name,verts,faces,color,bevel)


def limb(name,points,sizes,color):
    verts=[];n=8
    for i,point in enumerate(points):
        tangent=(Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])).normalized()
        u=Vector((1,0,0));u=(u-tangent*u.dot(tangent)).normalized();v=tangent.cross(u)
        for a,b in RECT:verts.append(tuple(Vector(point)+u*a*sizes[i][0]+v*b*sizes[i][1]))
    faces=[tuple(reversed(range(n)))]
    for j in range(len(points)-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    faces.append(tuple((len(points)-1)*n+i for i in range(n)))
    return mesh(name,verts,faces,color)


def box(name,center,size,col,bevel=0):
    x,y,z=center;w,d,h=size
    return loft(name,[(x,y,z-h/2,w/2,d/2),(x,y,z+h/2,w/2,d/2)],col,bevel=bevel)


def hand(side,wrist,posed):
    # A single extruded mitten silhouette includes the thumb root.
    outline=[(-.040,.025),(.040,.025),(.053,-.042),(.037,-.100),
             (-.019,-.108),(-.040,-.067),(-.071,-.046),(-.074,-.006),(-.051,.004)]
    verts=[]
    direction=Vector((0,-.85,-.52)) if posed else Vector((side*.20,0,-.98))
    width=Vector((side,0,0));normal=width.cross(direction).normalized()
    for depth in [-.029,.029]:
        for x,z in outline:
            verts.append(tuple(Vector(wrist)+width*x+direction*(-z)+normal*depth))
    n=len(outline)
    mesh('Hand',verts,[tuple(reversed(range(n))),tuple(range(n,n*2))]+
         [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'skin',.006)


def build(posed):
    global PARTS
    PARTS=[]
    lean=-.055 if posed else 0
    # Boots have separate soles, a forward toe, ankle and tall tapered shaft.
    for side in [-1,1]:
        ax=side*(.19 if posed else .125)
        ay=side*.055 if posed else 0
        loft('Boot_sole',[(ax,ay-.067,.018,.085,.156),(ax,ay-.067,.054,.085,.156)],'sole',bevel=.007)
        loft('Boot',[(ax,ay-.070,.053,.081,.151),(ax,ay-.075,.105,.080,.144),
                     (ax,ay-.025,.161,.068,.097),(ax,ay,.225,.061,.069),
                     (ax,ay,.432,.079,.078)],'boot',bevel=.005)
        box('Boot_heel',(ax,ay+.044,.016),(.142,.108,.032),'boot',.003)
        knee=(side*.17,-.08+ay*.5,.59) if posed else (side*.13,0,.61)
        hip=(side*.095,0,.91)
        limb('Trousers',[(ax,ay,.409),knee,hip],[(.067,.065),(.083,.083),(.103,.107)],'pants')
    loft('Trouser_seat',[(0,0,.855,.207,.123),(0,0,.990,.207,.128)],'pants')
    loft('Shirt',[(0,0,.98,.195,.125),(0,lean*.35,1.06,.201,.128),
                  (0,lean*.8,1.30,.239,.137),(0,lean,1.387,.239,.129),
                  (0,lean,1.437,.112,.089)],'linen')
    loft('Leather_belt',[(0,0,.955,.210,.139),(0,lean*.1,1.018,.208,.137)],'belt',bevel=.004)
    box('Belt_buckle',(0,-.141,.985),(.068,.022,.042),'button',.004)
    loft('Neck',[(0,lean,1.404,.065,.064),(0,lean,1.515,.066,.068)],'skin')
    loft('Collar',[(0,lean,1.414,.089,.078),(0,lean,1.441,.080,.071)],'cuff',bevel=.003)
    for side in [-1,1]:
        shoulder=Vector((side*.195,lean,1.315))
        elbow=Vector((side*.335,-.115,1.16)) if posed else Vector((side*.345,0,1.15))
        wrist=Vector((side*.335,-.34,1.065)) if posed else Vector((side*.415,-.008,.967))
        cuffend=elbow.lerp(wrist,.18)
        limb('Shirt_sleeve',[shoulder,shoulder.lerp(elbow,.38),elbow,cuffend],
             [(.083,.086),(.087,.086),(.074,.075),(.074,.075)],'linen')
        limb('Rolled_cuff',[elbow.lerp(wrist,.07),cuffend,elbow.lerp(wrist,.22)],
             [(.080,.081),(.080,.081),(.075,.075)],'cuff')
        limb('Forearm',[elbow.lerp(wrist,.19),elbow.lerp(wrist,.6),wrist],
             [(.063,.059),(.057,.050),(.040,.039)],'skin')
        hand(side,wrist,posed)
    # Modest head, blocky jaw and short hair; no oversized cartoon eyes.
    loft('Head',[(0,lean-.004,1.480,.087,.075),(0,lean-.009,1.512,.119,.102),
                 (0,lean,1.625,.131,.111),(0,lean+.004,1.703,.123,.105),
                 (0,lean+.004,1.73,.091,.084)],'skin',bevel=.004)
    for side in [-1,1]:
        box('Ear',(side*.132,lean+.002,1.605),(.035,.040,.069),'skin',.006)
        box('Eye',(side*.050,lean-.113,1.625),(.021,.009,.012),'eye',.002)
        box('Brow',(side*.050,lean-.115,1.651),(.039,.011,.011),'hair',.002)
    mesh('Nose',[(-.022,lean-.107,1.632),(.022,lean-.107,1.632),
                 (-.021,lean-.147,1.577),(.021,lean-.147,1.577),
                 (-.026,lean-.105,1.569),(.026,lean-.105,1.569)],
         [(0,1,3,2),(2,3,5,4),(0,2,4),(1,5,3),(0,4,5,1)],'skin')
    box('Mouth',(0,lean-.113,1.542),(.037,.007,.006),'belt')
    loft('Hair_back',[(0,lean+.044,1.529,.093,.071),(0,lean+.033,1.57,.127,.088),
                     (0,lean+.018,1.698,.131,.108)],'hair')
    circle=[(math.cos(i*math.tau/12),math.sin(i*math.tau/12)) for i in range(12)]
    loft('Cap_band',[(0,lean,1.692,.140,.122),(0,lean,1.735,.144,.127)],'capband',circle)
    loft('Cap_crown',[(0,lean+.005,1.722,.178,.151),(0,lean+.005,1.767,.204,.169),
                     (0,lean+.008,1.813,.164,.136),(0,lean+.008,1.827,.108,.089)],'cap',circle,bevel=.003)
    # Broad forward peak, flattened and rounded in plan.
    outline=[(-.14,-.03),(-.194,-.093),(-.197,-.189),(-.145,-.245),
             (0,-.262),(.145,-.245),(.197,-.189),(.194,-.093),(.14,-.03)]
    verts=[(x,y+lean,1.727+z) for z in [-.009,.012] for x,y in outline]
    n=len(outline)
    mesh('Cap_peak',verts,[tuple(reversed(range(n))),tuple(range(n,2*n))]+
         [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'cap',.004)
    # Keep costume shells editable during silhouette review. Deformation
    # topology and skinning are deliberately deferred until proportions settle.
    result=[]
    groups={surface:[o for o in PARTS if o['surface']==surface] for surface in ['cloth','skin']}
    for surface in ['cloth','skin']:
        obs=groups[surface]
        bpy.ops.object.select_all(action='DESELECT')
        for o in obs:o.select_set(True)
        bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join()
        o=bpy.context.object;o.name='CREW_'+surface.capitalize()
        result.append(o)
    return result


def validate(objects):
    report={}
    for o in objects:
        bm=bmesh.new();bm.from_mesh(o.data)
        d={'triangles':sum(len(f.verts)-2 for f in bm.faces),
           'boundary_edges':sum(e.is_boundary for e in bm.edges),
           'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges),
           'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
        assert not any(d[k] for k in ['boundary_edges','overconnected_edges','degenerate_faces']),d
        report[o.name]=d;bm.free()
    return report


def export(objects,name):
    # Runtime skin stores neutral shade; CrewVertexColor supplies its tint.
    for o in objects:
        if o['surface']=='skin':
            for c in o.data.color_attributes['Col'].data:c.color=(1,1,1,1)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,
        mesh_smooth_type='FACE',use_triangles=True,colors_type='LINEAR',add_leaf_bones=False)
    for o in objects:
        if o['surface']=='skin':
            for c in o.data.color_attributes['Col'].data:c.color=(*COL['skin'],1)


ss.clear_scene();CLOTH=material('Crew_Cloth');SKIN=material('Crew_Skin_Preview')
posed=build(True);report={'working_pose':validate(posed)};export(posed,'deckhand-working-pose')
scene=bpy.context.scene;ss.setup_render(scene)
scene.view_settings.exposure=.8
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD';scene.world.color=ss.srgb_to_linear(ss._hex('#6A8791'))
cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);scene.collection.objects.link(cam)
scene.camera=cam;cd.type='ORTHO';cd.ortho_scale=2.23
def render(name,eye,res=(900,1100)):
    cam.location=eye;cam.rotation_euler=(Vector((0,-.05,.94))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('rear-reference',(3.0,5.0,3.1))
render('front',(2.8,-5,2.5))
render('game-size',(3,5,3.1),(160,196))
for o in posed:o.hide_render=True;o.hide_set(True)
neutral=build(False);report['neutral_pose']=validate(neutral);export(neutral,'deckhand-neutral')
render('neutral-front',(2.8,-5,2.5))
report['rigged']=False;report['runtime_skin_base_color_linear']=list(COL['skin'])
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
for o in neutral:o.hide_render=True;o.hide_set(True)
for o in posed:o.hide_render=False;o.hide_set(False)
cam.location=(3,5,3.1);cam.rotation_euler=(Vector((0,-.05,.94))-cam.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print(json.dumps(report,indent=2))
