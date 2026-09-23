"""Harvestable hornbeam study: upright foliage sprays, clear chopping trunk."""
import bpy,bmesh,math,random,json,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/hornbeam-astra-v1';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'wood':'#8B7053','woodlight':'#A18762','wooddark':'#74604A','cut':'#CCA474','heart':'#B18B60','green':'#65904C','green2':'#79A44F','light':'#8AB450','shade':'#598449','deep':'#3D6D46'}
k=Kit('Hornbeam',COL);k.group='Hornbeam_Wood'
def branch(points,radii):
    n=8;verts=[]
    for j,p in enumerate(points):
        p=Vector(p);d=(Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])).normalized()
        u=d.cross(Vector((0,1,0))).normalized();v=d.cross(u).normalized()
        for i in range(n):verts.append(p+radii[j]*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)))
    faces=[tuple(reversed(range(n))),tuple((len(points)-1)*n+i for i in range(n))]
    for j in range(len(points)-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    k.mesh('Tapered_Wood',verts,faces,'wood')
branch([(0,0,-.07),(.02,0,.3),(-.04,.01,1.0),(0,.03,1.8),(.07,.02,2.8),(-.02,.06,4),(.04,.02,5.3),(0,0,6.7)],[.34,.28,.19,.17,.14,.105,.065,.035])
for i in range(5):
    a=i*math.tau/5+.2
    branch([(0,0,.42),(.32*math.cos(a),.32*math.sin(a),.13),(.58*math.cos(a),.58*math.sin(a),.015)],[.16,.11,.035])
sprays=[]
for i in range(38):
    z=1.92+i*.105;a=i*2.39996
    envelope=math.sin((z-1.15)/6.3*math.pi)*.83
    p=(envelope*math.cos(a),envelope*math.sin(a),z)
    direction=Vector((.38*math.cos(a),.38*math.sin(a),1)).normalized()
    length=1.25+.18*math.sin(i*1.7)
    width=.49+.08*math.cos(i*2.1)
    sprays.append((p,direction,length,width,a))
    if i%3==0:branch([(0,.02,max(1.55,z-.55)),(p[0]*.6,p[1]*.6,z),(p[0],p[1],z+.30)],[.10,.064,.035])
wood=k.join_modules()['Hornbeam_Wood'];bpy.context.view_layer.objects.active=wood
m=wood.modifiers.new('Continuous forks','REMESH');m.mode='VOXEL';m.voxel_size=.055;m.use_smooth_shade=False;bpy.ops.object.modifier_apply(modifier=m.name)
m=wood.modifiers.new('Wood planes','DECIMATE');m.ratio=.12;m.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=m.name)
bm=bmesh.new();bm.from_mesh(wood.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.dissolve_degenerate(bm,dist=.00001,edges=list(bm.edges))
remaining=set(bm.verts);groups=[]
while remaining:
    todo=[remaining.pop()];group=set(todo)
    while todo:
        v=todo.pop()
        for e in v.link_edges:
            other=e.other_vert(v)
            if other in remaining:remaining.remove(other);group.add(other);todo.append(other)
    groups.append(group)
keep=max(groups,key=len);bmesh.ops.delete(bm,geom=[v for v in bm.verts if v not in keep],context='VERTS');bm.to_mesh(wood.data);bm.free()
def color_mesh(ob,foliage=False,variant=0):
    for attr in list(ob.data.color_attributes):ob.data.color_attributes.remove(attr)
    attr=ob.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in ob.data.polygons:
        p.use_smooth=False;n=p.normal.z
        if foliage:c='light' if n>.3 else ('green2' if variant%3 else 'green')
        else:c='woodlight' if n>.35 else 'wooddark' if n<-.35 else 'wood'
        for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL[c])),1)
color_mesh(wood)
k.group='Hornbeam_Canopy'
def spray(index,p,d,length,width,angle):
    p=Vector(p);d=Vector(d).normalized();u=Vector((-math.sin(angle),math.cos(angle),0));v=d.cross(u).normalized()
    if index<90:
        verts=[];faces=[]
        # Broad closed leaf fans: a small number of planes, with no alpha cards.
        for j,(t,sign) in enumerate([(.02,-1),(.10,1),(.27,-1),(.36,1),(.51,-1),(.60,1),(.74,0)]):
            base=p+d*(t*length)
            axis=(d+(u*sign*.62)).normalized();side=v.cross(axis).normalized()
            size=length*(.51 if sign else .42);w=width*(.63 if sign else .61)
            outline=[(0,0),(.22,-.62),(.37,-1),(.48,-.76),(.65,-.82),(1,0),(.65,.82),(.48,.76),(.37,1),(.22,.62)]
            offset=len(verts)
            for along,across in outline:verts.append(base+axis*(along*size)+side*(across*w))
            verts.append(base+axis*(size*.44)+v*.065)
            verts.append(base+axis*(size*.44)-v*.045)
            for q in range(10):faces.extend([(offset+10,offset+q,offset+(q+1)%10),(offset+11,offset+(q+1)%10,offset+q)])
        ob=k.mesh('Leaf_Fan_%02d'%index,verts,faces,'green');color_mesh(ob,True,index)
        return
    n=8;verts=[];rng=random.Random(320+index)
    # Upward-pointing opaque foliage shoots, with staggered scalloped shoulders.
    rings=[(0,.06),(.18,.66),(.38,1),(.58,.86),(.76,.64),(.90,.35)]
    for j,(t,r) in enumerate(rings):
        for i in range(n):
            a=i*math.tau/n
            radius=r*(1+rng.uniform(-.11,.11))
            height=t+(.035 if (i+j)%3==0 else -.015)
            verts.append(p+d*(height*length)+u*(math.cos(a)*width*radius)+v*(math.sin(a)*width*.65*radius))
    bottom=len(verts);verts.append(p-d*.025)
    top=len(verts);verts.append(p+d*length+u*.06)
    faces=[(bottom,(i+1)%n,i) for i in range(n)]
    for j in range(len(rings)-1):
        for i in range(n):
            a=j*n+i;b=j*n+(i+1)%n;c=(j+1)*n+(i+1)%n;dd=(j+1)*n+i
            faces.extend([(a,b,c),(a,c,dd)])
    faces.extend(((len(rings)-1)*n+i,(len(rings)-1)*n+(i+1)%n,top) for i in range(n))
    ob=k.mesh('Leafy_Shoot_%02d'%index,verts,faces,'green');color_mesh(ob,True,index)
# An opaque narrow inner crown closes sight lines without broad horizontal tiers.
spray(90,(0,0,1.9),(0,0,1),5.25,1.03,.3)
for i,args in enumerate(sprays):spray(i,*args)
spray(91,(-.03,.02,5.82),(.035,0,1),1.53,.43,1.2)
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
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/hornbeam-astra-v1.blend'))
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
