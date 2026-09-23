"""Harvestable hornbeam study: upright foliage sprays, clear chopping trunk."""
import bpy,bmesh,math,random,json,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/hornbeam-astra-v2';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'wood':'#8B7053','woodlight':'#A18762','wooddark':'#74604A','cut':'#CCA474','heart':'#B18B60','green':'#65904C','green2':'#79A44F','light':'#8AB450','shade':'#598449','deep':'#3D6D46'}
k=Kit('Hornbeam',COL);k.group='Hornbeam_Wood'
def loft(name,rings,n,color,crown=False):
    verts=[]
    for j,(z,r,x,y) in enumerate(rings):
        for i in range(n):
            a=i*math.tau/n
            radius=r
            dz=0
            if crown:
                radius*=1+.10*math.cos(3*a+j*.63)
                if j in [2,4,6] and (i+j)%5==0:radius*=.81
                dz=.085*math.sin(2*a+j*.7)
            verts.append((x+radius*math.cos(a),y+radius*math.sin(a),z+dz))
    faces=[tuple(reversed(range(n))),tuple((len(rings)-1)*n+i for i in range(n))]
    for j in range(len(rings)-1):
        for i in range(n):
            a=j*n+i;b=j*n+(i+1)%n;c=(j+1)*n+(i+1)%n;d=(j+1)*n+i
            faces.extend([(a,b,c),(a,c,d)])
    return k.mesh(name,verts,faces,color)
wood=loft('Simple_Trunk',[(-.06,.31,0,0),(.12,.36,0,0),(.38,.235,.01,0),(1.2,.18,-.04,.01),(2.2,.14,.03,.02),(3.4,.06,.01,.01)],6,'wood')
def color_mesh(ob,foliage=False,variant=0):
    for attr in list(ob.data.color_attributes):ob.data.color_attributes.remove(attr)
    attr=ob.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in ob.data.polygons:
        p.use_smooth=False
        c=('light' if p.normal.z>.35 else 'green2' if p.normal.z>-.25 else 'green') if foliage else 'wood'
        for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL[c])),1)
k.group='Hornbeam_Canopy'
crown=loft('Single_Angular_Crown',[(1.62,.36,0,0),(2.15,.85,-.08,0),(2.95,1.18,.03,.02),(3.8,1.30,-.03,.03),(4.7,1.12,.05,0),(5.55,.85,-.07,.02),(6.3,.46,-.06,0),(6.9,.04,-.13,0)],10,'green2',True)
color_mesh(crown,True)
mods=k.join_modules()
markers=[k.marker('Hornbeam__Ground',(0,0,0),'resource placement'),k.marker('Hornbeam__Chop_Target',(0,-.19,.95),'visual chopping target'),k.marker('Hornbeam__Fell_Pivot',(0,0,.38),'reference pivot, not animation')]
# Stump is clipped from the very same trunk surface for a matching root footprint.
stump_root=bpy.data.objects.new('Hornbeam_Stump',None);bpy.context.collection.objects.link(stump_root)
stump=wood.copy();stump.data=wood.data.copy();stump.name='Hornbeam_Stump_Mesh';bpy.context.collection.objects.link(stump);stump.parent=stump_root
bm=bmesh.new();bm.from_mesh(stump.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,plane_co=(0,0,.38),plane_no=(0,0,1),clear_outer=True)
boundary=[e for e in bm.edges if e.is_boundary];bmesh.ops.holes_fill(bm,edges=boundary,sides=0);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(stump.data);bm.free()
color_mesh(stump)
attr=stump.data.color_attributes['Col']
for p in stump.data.polygons:
    if all(abs(stump.data.vertices[j].co.z-.38)<1e-4 for j in p.vertices):
        for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL['cut'])),1)
def export(name,root,obs,marks):
    bpy.context.view_layer.update();report={'triangles':0,'bounds':None};points=[]
    for ob in obs:
        bm=bmesh.new();bm.from_mesh(ob.data)
        assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces),(name,ob.name)
        report['triangles']+=sum(len(f.verts)-2 for f in bm.faces);bm.free();points.extend(ob.matrix_world@v.co for v in ob.data.vertices)
    report['bounds']=[[min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]]
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [root]+obs+marks:ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
    return report
reports={'Hornbeam':export('Hornbeam',k.root,list(mods.values()),markers),'Hornbeam_Stump':export('Hornbeam_Stump',stump_root,[stump],[])}
(OUT/'validation.json').write_text(json.dumps(reports,indent=2))
stump.hide_render=True;stump.hide_set(True)
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.5;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#A6B7BE'))
cam=bpy.data.objects.new('Hornbeam_Review',bpy.data.cameras.new('Hornbeam_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1100,1200)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('hornbeam-front',(9,-16,7),(0,0,3.5),8.3)
render('hornbeam-back',(-9,16,7),(0,0,3.5),8.3)
render('game-angle',(10,-16,17),(0,0,3.5),8.3)
for ob in mods.values():ob.hide_render=True
stump.hide_render=False;stump.hide_set(False)
render('stump',(2,-3,2),(0,0,.2),1.65,(900,900))
stump.hide_render=True;stump.hide_set(True)
for ob in mods.values():ob.hide_render=False
copies=[]
for i,(x,y,sc,a) in enumerate([(-3.2,1.2,.90,.9),(3.1,1.7,1.03,2.8),(-1.6,4.2,.94,4.5),(1.8,5,.86,1.8)]):
    root=bpy.data.objects.new('Review_Grove_%d'%i,None);scene.collection.objects.link(root);root.location=(x,y,0);root.scale=(sc,sc,sc);root.rotation_euler.z=a
    for src in mods.values():
        ob=src.copy();scene.collection.objects.link(ob);ob.parent=root;copies.append(ob)
render('grove',(15,-23,18),(0,2,3.2),14,(1500,1100))
for ob in copies:ob.hide_render=True;ob.hide_set(True)
render('hornbeam-front',(9,-16,7),(0,0,3.5),8.3)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type='VERTEX';v.overlay.show_overlays=False;v.region_3d.view_location=(0,0,3.5);v.region_3d.view_distance=11;v.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/hornbeam-astra-v2.blend'))
verified={}
for name in reports:
    for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    tri=0
    for ob in scene.objects:
        if ob.type!='MESH':continue
        bm=bmesh.new();bm.from_mesh(ob.data)
        assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces),(name,ob.name)
        assert not any(p.use_smooth for p in ob.data.polygons) and 'Col' in ob.data.color_attributes
        tri+=sum(len(f.verts)-2 for f in bm.faces);bm.free()
    if name=='Hornbeam':
        for marker in ['Hornbeam__Ground','Hornbeam__Chop_Target','Hornbeam__Fell_Pivot']:assert marker in scene.objects
    verified[name]={'result':'PASS','triangles':tri}
(OUT/'export-verification.json').write_text(json.dumps(verified,indent=2));print('PASS',verified,reports)
