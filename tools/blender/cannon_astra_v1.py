"""Faceted naval deck gun. Blender Z up, muzzle toward -Y, metres."""
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
OUT=HERE.parents[1]/'art-staging/cannon-astra-v1'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=HERE/'source/cannon-astra-v1.blend'
PAL={'wood':'#C36B2D','edge':'#E09243','wheel':'#804322',
     'iron':'#49434A','dark':'#241F25','bolt':'#938786','rim':'#37333B'}
PARTS=[]
ss.clear_scene()
MAT=bpy.data.materials.new('Cannon_VertexColor');MAT.use_nodes=True
node=MAT.node_tree.nodes.new('ShaderNodeVertexColor');node.layer_name='Col'
MAT.node_tree.links.new(node.outputs['Color'],MAT.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
MAT.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.8
root=bpy.data.objects.new('Cannon_Root',None);bpy.context.collection.objects.link(root)
pivot=bpy.data.objects.new('Elevation_Pivot',None);bpy.context.collection.objects.link(pivot)
pivot.parent=root;pivot.location=(0,0,.96)
pivot['axis']='Local X; barrel elevation. Muzzle points toward local -Y.'
muzzle=bpy.data.objects.new('Muzzle_Socket',None);bpy.context.collection.objects.link(muzzle)
muzzle.parent=pivot;muzzle.location=(0,-1.29,0)
muzzle.rotation_euler.x=math.pi/2
muzzle['forward']='Local +Z points out of the bore.'

def mesh(name,verts,faces,color,parent=root):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob)
    bm=bmesh.new();bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    me.materials.append(MAT)
    attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    c=ss.srgb_to_linear(ss._hex(PAL[color]))
    for d in attr.data:d.color=(*c,1)
    for p in me.polygons:p.use_smooth=False
    ob.parent=parent;PARTS.append(ob)
    return ob

def lathe(name,profile,axis,center,color,parent=root,n=12):
    # A complete cross-section includes the bore or hub, not stacked cylinders.
    verts=[]
    for t,r in profile:
        for i in range(n):
            a=math.tau*i/n
            q=(t,r*math.cos(a),r*math.sin(a)) if axis=='X' else (r*math.cos(a),t,r*math.sin(a))
            verts.append(tuple(q[j]+center[j] for j in range(3)))
    faces=[]
    for k in range(len(profile)-1):
        for i in range(n):
            j=(i+1)%n;faces.append((k*n+i,k*n+j,(k+1)*n+j,(k+1)*n+i))
    faces.extend([tuple(reversed(range(n))),tuple((len(profile)-1)*n+i for i in range(n))])
    return mesh(name,verts,faces,color,parent)

def prism(name,outline,x0,x1,color,bevel=0):
    n=len(outline);verts=[(x,y,z) for x in (x0,x1) for y,z in outline]
    faces=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    ob=mesh(name,verts,faces,color)
    if bevel:
        bpy.context.view_layer.objects.active=ob
        mod=ob.modifiers.new('Carved edge','BEVEL');mod.width=bevel;mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return ob

def box(name,lo,hi,color,bevel=0):
    return prism(name,[(lo[1],lo[2]),(hi[1],lo[2]),(hi[1],hi[2]),(lo[1],hi[2])],lo[0],hi[0],color,bevel)

# The muzzle, reinforcing rings, taper, breech and deep bore share one surface.
barrel=lathe('Barrel',[(.79,.065),(.77,.115),(.65,.14),(.61,.235),
    (.53,.275),(.43,.283),(.38,.268),(.17,.262),(.14,.280),(.085,.280),
    (.055,.252),(-.52,.202),(-.56,.216),(-.615,.216),(-.645,.195),
    (-1.09,.183),(-1.15,.224),(-1.25,.229),(-1.29,.211),
    (-1.29,.139),(-1.23,.132),(-.82,.132),(-.80,.12)],'Y',(0,0,0),'iron',pivot,n=16)
# Dark bore faces retain vertex shading without adding a false muzzle disk.
attr=barrel.data.color_attributes['Col']
for poly in barrel.data.polygons:
    if poly.index>=19*16:
        for li in poly.loop_indices:attr.data[li].color=(*ss.srgb_to_linear(ss._hex(PAL['dark'])),1)
lathe('Trunnion', [(-.43,.086),(-.38,.1),(.38,.1),(.43,.086)],'X',(0,0,0),'iron',pivot)

outline=[(-.69,.29),(.75,.29),(.75,.43),(.43,.47),(.34,.61),
         (.27,.72),(.24,.95)]
outline += [(.108*math.cos(-i*math.pi/8),.96+.108*math.sin(-i*math.pi/8)) for i in range(9)]
outline += [(-.24,.95),(-.30,.82),(-.55,.66),(-.69,.53)]
for side in [-1,1]:
    x=side*.315
    prism('Carriage_Cheek_'+str(side),outline,x-.085,x+.085,'wood',.018)
    # A forged cap follows the bearing saddle; it is not a floating slab.
    cap=[(r*math.cos(i*math.pi/8),.96+r*math.sin(i*math.pi/8))
         for r,indices in [(.15,range(9)),(.108,range(8,-1,-1))] for i in indices]
    prism('Bearing_Cap_'+str(side),cap,x-.095,x+.095,'iron')
    for y in [-.46,.54]:
        lathe('Cheek_Bolt',[(side*.399,.036),(side*.423,.036)],'X',(0,y,.47),'bolt',n=6)
    for y in [-.43,.51]:
        # Truck wheels have recessed faces, thick wood rims and integral hubs.
        c=side*.50
        wheel=lathe('Truck_Wheel',[(c-.093,.07),(c-.093,.17),(c-.073,.211),
              (c+.073,.211),(c+.093,.17),(c+.093,.07)],'X',(0,y,.211),'wheel')
        center=Vector((c,y,.211))
        for v in wheel.data.vertices:v.co-=center
        wheel.location=center
        lathe('Wheel_Pin',[(c-.108,.057),(c+.108,.057)],'X',(0,y,.211),'iron',n=8)
for y in [-.43,.51]:
    lathe('Axle',[(-.61,.045),(.61,.045)],'X',(0,y,.211),'iron',n=8)
    box('Cross_Timber',(-.31,y-.095,.285),(.31,y+.095,.44),'wood',.012)
box('Carriage_Bed',(-.23,-.59,.40),(.23,.64,.50),'wheel',.012)
# A tapered elevation wedge and its pull handle give the back a useful detail.
prism('Elevation_Quoin',[(.20,.5),(.65,.5),(.65,.62),(.20,.69)],-.14,.14,'edge',.008)
lathe('Quoin_Handle',[(.65,.042),(.84,.042),(.88,.031)],'Y',(0,0,.56),'wheel',n=8)
pivot.rotation_euler.x=math.radians(-4)

def audit():
    report={}
    for o in PARTS:
        bm=bmesh.new();bm.from_mesh(o.data)
        d={'triangles':sum(len(f.verts)-2 for f in bm.faces),
           'boundary_edges':sum(e.is_boundary for e in bm.edges),
           'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
           'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
        assert d['nonmanifold_edges']==d['degenerate_faces']==0,(o.name,d)
        report[o.name]=d;bm.free()
    bpy.context.view_layer.update()
    points=[o.matrix_world@v.co for o in PARTS for v in o.data.vertices]
    report['dimensions_xyz']=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    report['total_triangles']=sum(d['triangles'] for d in report.values() if isinstance(d,dict))
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    return report

report=audit()
bpy.ops.object.select_all(action='DESELECT')
for o in PARTS+[root,pivot,muzzle]:o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'cannon.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,
    use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE')
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65
scene.display.shading.show_shadows=False
scene.display.shading.background_type='WORLD';scene.world.color=ss.srgb_to_linear(ss._hex('#708D96'))
cam=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));scene.collection.objects.link(cam)
scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1100,900)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
render('front-three-quarter',(3,-4,2.8),(0,-.15,.62),2.9)
render('rear-three-quarter',(-3,4,2.5),(0,-.1,.60),2.9)
render('side',(4,0,1.5),(0,-.15,.60),2.9)
render('front-three-quarter',(3,-4,2.8),(0,-.15,.62),2.9)
for screen in bpy.data.screens:
    for a in screen.areas:
        if a.type=='VIEW_3D':
            a.spaces.active.shading.color_type='VERTEX'
            a.spaces.active.region_3d.view_location=(0,-.1,.6)
            a.spaces.active.region_3d.view_distance=3.8
            a.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))

# Temporary fit review uses the actual approved hull, not a surrogate deck.
with bpy.data.libraries.load(str(HERE/'source/stern-paddle-astra-v8.blend'),link=False) as (src,dst):
    dst.objects=[n for n in src.objects if n!='ReviewCamera']
for o in dst.objects:
    if o is not None:scene.collection.objects.link(o)
root.location=(-2.5,-3.82,1.76)
for sx in [-1,1]:
    for longitudinal in [-6,-2.5,1,4.5]:
        if sx==1 and longitudinal==-2.5:continue
        r=root.copy();scene.collection.objects.link(r);r.location=(longitudinal,-sx*3.82,1.76)
        r.rotation_euler.z=0 if sx==1 else math.pi
        p=pivot.copy();scene.collection.objects.link(p);p.parent=r
        for o in PARTS:
            c=o.copy();scene.collection.objects.link(c);c.parent=p if o.parent==pivot else r
render('ship-fit',(-16,-23,19),(0,0,1),29,(1500,1100))
render('deck-fit',(-6,-9,7),(-2.5,-3.5,2.25),6,(1200,1000))
print(json.dumps({'triangles':report['total_triangles'],'dimensions':report['dimensions_xyz']},indent=2))
