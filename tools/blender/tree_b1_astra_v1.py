"""B1 tall-terrace broadleaf study. Staging only, no game modifications."""
import bpy,bmesh,math,json,random,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/tree-b1-astra-v1'
OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'bark':'#886346','barklight':'#A17B50','barkdark':'#70543F','leaf':'#56933E','leaflight':'#78AC45','leafmid':'#629F41','leafdark':'#498349','leafunder':'#44784E'}
k=Kit('Tree_B1',COL)
k.group='Tree_B1_Wood'
def branch(name,points,radii):
    n=9;verts=[]
    for j,p in enumerate(points):
        p=Vector(p);d=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
        d.normalize();u=d.cross(Vector((0,1,0))).normalized();v=d.cross(u).normalized()
        for i in range(n):
            a=math.tau*i/n
            verts.append(p+radii[j]*(u*math.cos(a)+v*math.sin(a)))
    faces=[tuple(reversed(range(n))),tuple((len(points)-1)*n+i for i in range(n))]
    for j in range(len(points)-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    return k.mesh(name,verts,faces,'bark')
branch('Trunk',[(0,0,-.08),(.03,0,.32),(-.07,.03,.95),(.02,0,1.8),(.14,.02,2.7),(.03,.04,3.65),(-.08,.06,4.6),(.02,.07,5.5),(-.13,.03,6.4)],[.40,.34,.235,.205,.175,.145,.12,.085,.02])
for i in range(5):
    a=i*math.tau/5+.3
    branch('Root',[(.02,0,.43),(.42*math.cos(a),.42*math.sin(a),.17),(.76*math.cos(a),.76*math.sin(a),.025)],[.22,.13,.025])
# Staggered bough centers follow a tapered envelope, with front/back coverage.
boughs=[
    (-.95,-.12,1.95,1.35,1.03,.78,.2),(.92,.12,2.12,1.29,1.08,.78,2.1),(.05,.75,2.40,1.35,1.05,.79,3.2),
    (.22,-.79,2.90,1.48,1.05,.82,.8),(-.92,.28,3.25,1.29,1.03,.76,2.5),(.92,.16,3.63,1.23,1.04,.74,4.0),
    (-.38,-.50,4.15,1.39,1.07,.80,1.9),(.11,.65,4.53,1.23,1.05,.80,.1),
    (.70,-.14,5.05,1.16,.95,.76,3.3),(-.62,.09,5.45,1.10,.96,.78,4.9),
    (.01,-.08,6.22,1.15,1.00,.87,2.7)]
for i,(x,y,z,rx,ry,h,a) in enumerate(boughs):
    branch('Bough',[(.05,.03,max(.8,z-1.15)),(x*.56,y*.56,z-.38),(x,y,z+.05)],[.13 if z<4 else .10,.075,.028])
wood=k.join_modules()['Tree_B1_Wood']
# Weld fork/root junctions into a continuous trunk before reducing topology.
bpy.context.view_layer.objects.active=wood
m=wood.modifiers.new('Continuous wood junctions','REMESH');m.mode='VOXEL';m.voxel_size=.055;m.use_smooth_shade=False
bpy.ops.object.modifier_apply(modifier=m.name)
m=wood.modifiers.new('Planar trunk reduction','DECIMATE');m.ratio=.16;m.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=m.name)
bm=bmesh.new();bm.from_mesh(wood.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.dissolve_degenerate(bm,dist=.00001,edges=list(bm.edges))
# Tiny detached twig-tip remnants are not part of the continuous wooden skeleton.
unseen=set(bm.verts);components=[]
while unseen:
    todo=[unseen.pop()];group=set(todo)
    while todo:
        v=todo.pop()
        for e in v.link_edges:
            other=e.other_vert(v)
            if other in unseen:unseen.remove(other);group.add(other);todo.append(other)
    components.append(group)
keep=max(components,key=len)
bmesh.ops.delete(bm,geom=[v for v in bm.verts if v not in keep],context='VERTS')
bm.to_mesh(wood.data);bm.free()
for attr in list(wood.data.color_attributes):wood.data.color_attributes.remove(attr)
attr=wood.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
for p in wood.data.polygons:
    p.use_smooth=False
    c='barklight' if p.normal.z>.4 else 'barkdark' if p.normal.z<-.3 else 'bark'
    for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL[c])),1)
k.group='Tree_B1_Canopy'
def canopy(index,data):
    x,y,z,rx,ry,h,rotation=data;rng=random.Random(710+index)
    n=18;verts=[]
    angles=[i*math.tau/n+rng.uniform(-.045,.045) for i in range(n)]
    contour=[1+.095*math.sin(i*math.tau/6+index)+rng.uniform(-.075,.075) for i in range(n)]
    # Wide shoulders and an irregular hanging skirt make a bough, not an ellipsoid.
    rings=[(.27,-.53),(.74,-.39),(1.0,-.12),(.87,.28),(.47,.65)]
    for j,(r,level) in enumerate(rings):
        for i,a in enumerate(angles):
            r2=r*contour[i]*(1+rng.uniform(-.055,.055))
            dz=rng.uniform(-.085,.085)
            if j==1:dz-=.10 if (i+index)%3==0 else 0
            px=rx*r2*math.cos(a);py=ry*r2*math.sin(a)
            verts.append((x+px*math.cos(rotation)-py*math.sin(rotation),y+px*math.sin(rotation)+py*math.cos(rotation),z+h*(level+dz)))
    bottom=len(verts);verts.append((x+.03,y,z-h*.62))
    top=len(verts);verts.append((x-.13*rx,y+.04,z+h*.86))
    faces=[]
    for i in range(n):faces.append((bottom,(i+1)%n,i))
    for j in range(len(rings)-1):
        for i in range(n):
            a=j*n+i;b=j*n+(i+1)%n;c=(j+1)*n+(i+1)%n;d=(j+1)*n+i
            if (i+j)%2:faces.extend([(a,b,d),(b,c,d)])
            else:faces.extend([(a,b,c),(a,c,d)])
    for i in range(n):faces.append(((len(rings)-1)*n+i,(len(rings)-1)*n+(i+1)%n,top))
    # Fuse irregular peripheral growth into the primary bough to break its rim.
    for l in range(6):
        angle=rotation+l*math.tau/6+.18*math.sin(index+l)
        center=Vector((x+rx*.65*math.cos(angle),y+ry*.65*math.sin(angle),z+h*(.02+.13*math.sin(l+index))))
        bm=bmesh.new();bmesh.ops.create_icosphere(bm,subdivisions=2,radius=1)
        bm.verts.ensure_lookup_table();offset=len(verts)
        for v in bm.verts:
            q=v.co
            verts.append(center+Vector((q.x*rx*.55,q.y*ry*.55,q.z*h*.67)))
        faces.extend(tuple(offset+v.index for v in f.verts) for f in bm.faces);bm.free()
    ob=k.mesh('Terraced_Bough_%02d'%index,verts,faces,'leaf')
    bpy.context.view_layer.objects.active=ob
    m=ob.modifiers.new('Sculpted foliage union','REMESH');m.mode='VOXEL';m.voxel_size=.095;m.use_smooth_shade=False;bpy.ops.object.modifier_apply(modifier=m.name)
    m=ob.modifiers.new('Broad foliage planes','DECIMATE');m.ratio=.18;bpy.ops.object.modifier_apply(modifier=m.name)
    bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.dissolve_degenerate(bm,dist=.00001,edges=list(bm.edges));bm.to_mesh(ob.data);bm.free()
    for old in list(ob.data.color_attributes):ob.data.color_attributes.remove(old)
    ob.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    attr=ob.data.color_attributes['Col']
    for p in ob.data.polygons:
        p.use_smooth=False;nz=p.normal.z
        c='leaflight' if nz>.65 else 'leafmid' if nz>.15 else 'leaf' if nz>-.3 else 'leafdark' if nz>-.7 else 'leafunder'
        for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL[c])),1)
for i,data in enumerate(boughs):canopy(i,data)
mods=k.join_modules()
markers=[k.marker('Tree_B1__Ground',(0,0,0),'ground placement'),k.marker('Tree_B1__Trunk_Target',(0,0,1),'interaction reference only')]
bpy.context.view_layer.update()
report={};allpoints=[]
for name,ob in mods.items():
    bm=bmesh.new();bm.from_mesh(ob.data)
    d={'triangles':sum(len(f.verts)-2 for f in bm.faces),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    assert d['nonmanifold_edges']==d['degenerate_faces']==0,(name,d)
    assert not any(p.use_smooth for p in ob.data.polygons)
    report[name]=d;bm.free();allpoints.extend(ob.matrix_world@v.co for v in ob.data.vertices)
report['bounds_blender']=[[min(v[i] for v in allpoints) for i in range(3)],[max(v[i] for v in allpoints) for i in range(3)]]
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
bpy.ops.object.select_all(action='DESELECT')
for ob in [k.root]+list(mods.values())+markers:ob.select_set(True)
bpy.context.view_layer.objects.active=k.root
bpy.ops.export_scene.fbx(filepath=str(OUT/'Tree_B1.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.45;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#A6B7BE'))
cam=bpy.data.objects.new('Tree_Review_Camera',bpy.data.cameras.new('Tree_Review_Camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1200,1200)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('tree-front',(9,-16,8),(0,0,3.4),8.4)
render('tree-back',(-10,15,8),(0,0,3.4),8.4)
render('game-angle',(10,-16,16),(0,0,3.3),8.5)
render('game-scale',(10,-16,16),(0,0,3.3),8.5,(320,320))
copies=[]
for i,(x,y,scale,angle) in enumerate([(-4.5,1,.90,.9),(4.0,1.4,.93,2.7),(-2.5,5,1.06,4.1),(2.1,5.7,.85,1.8),(.5,9,.96,5.3)]):
    root=bpy.data.objects.new('Review_Tree_%02d'%i,None);scene.collection.objects.link(root);root.location=(x,y,0);root.scale=(scale,)*3;root.rotation_euler.z=angle
    for src in mods.values():
        ob=src.copy();ob.data=src.data;scene.collection.objects.link(ob);ob.parent=root;copies.append(ob)
render('grove',(17,-23,21),(0,3.5,2.9),18,(1500,1100))
for ob in copies:ob.hide_render=True;ob.hide_set(True)
render('tree-front',(9,-16,8),(0,0,3.4),8.4)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type='VERTEX';v.overlay.show_overlays=False;v.region_3d.view_location=(0,0,3.4);v.region_3d.view_distance=11;v.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/tree-b1-astra-v1.blend'))
for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(OUT / 'Tree_B1.fbx'))
total=0
for ob in bpy.context.scene.objects:
    if ob.type!='MESH':continue
    bm=bmesh.new();bm.from_mesh(ob.data)
    assert all(e.is_manifold for e in bm.edges) and all(f.calc_area()>1e-10 for f in bm.faces),(ob.name,sum(not e.is_manifold for e in bm.edges),sum(f.calc_area()<1e-10 for f in bm.faces))
    assert not any(p.use_smooth for p in ob.data.polygons) and 'Col' in ob.data.color_attributes
    total+=sum(len(f.verts)-2 for f in bm.faces);bm.free()
assert 'Tree_B1__Ground' in bpy.context.scene.objects
(OUT/'export-verification.json').write_text(json.dumps({'result':'PASS','triangles':total},indent=2))
print('PASS',total,report)
