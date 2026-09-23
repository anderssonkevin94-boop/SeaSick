"""Expanded low-poly resource forest: fourteen silhouettes, shared vertex colors."""
import bpy,bmesh,math,random,json,sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/forest-lowpoly-astra-v3';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'wood':'#8B7053','woodlight':'#A18762','wooddark':'#74604A','cut':'#CCA474','heart':'#B18B60','green':'#65904C','green2':'#79A44F','light':'#8AB450','shade':'#598449','deep':'#3D6D46'}
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
def color_mesh(ob,foliage=False,variant=0):
    for attr in list(ob.data.color_attributes):ob.data.color_attributes.remove(attr)
    attr=ob.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in ob.data.polygons:
        p.use_smooth=False
        c=('light' if p.normal.z>.35 else 'green2' if p.normal.z>-.25 else 'green') if foliage else 'wood'
        for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL[c])),1)
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

# Each crown is a deliberately authored silhouette, not a scaled source mesh.
profiles=[
('Forest_Hornbeam',10,[(1.62,.36,0,0),(2.15,.85,-.08,0),(2.95,1.18,.03,.02),(3.8,1.30,-.03,.03),(4.7,1.12,.05,0),(5.55,.85,-.07,.02),(6.3,.46,-.06,0),(6.9,.04,-.13,0)],.36,0),
('Forest_Broadleaf',10,[(1.55,.48,0,0),(2.0,1.25,-.06,0),(2.8,1.83,-.15,.03),(3.7,1.91,-.06,.04),(4.55,1.65,.14,.02),(5.15,1.13,.19,0),(5.62,.53,.11,.01),(5.8,.12,.17,0)],.43,1),
('Forest_Slender',8,[(1.9,.27,0,0),(2.6,.63,.03,0),(3.6,.86,.07,.03),(4.8,.92,.04,0),(5.9,.77,-.04,.02),(6.9,.44,-.11,.01),(7.55,.045,-.09,0)],.29,2),
('Forest_Leaning',9,[(1.45,.32,.12,0),(2.1,.87,.30,0),(2.9,1.23,.51,.03),(3.8,1.28,.69,0),(4.55,1.10,.84,.04),(5.25,.71,1.01,.03),(5.9,.06,1.15,0)],.34,0),
('Forest_Uneven',10,[(1.6,.42,0,0),(2.15,1.01,-.20,0),(2.95,1.47,-.33,.02),(3.75,1.50,-.20,0),(4.5,1.23,.11,.04),(5.12,.89,.38,.02),(5.6,.44,.48,.02),(5.94,.06,.40,0)],.39,2),
('Forest_Young',8,[(1.0,.20,0,0),(1.4,.54,-.04,0),(2.05,.79,.04,.01),(2.75,.75,.06,0),(3.45,.50,.02,.02),(4.04,.04,-.04,0)],.22,1)]
profiles.extend([
('Forest_Pine',8,[(1.3,.32,0,0),(1.65,1.22,0,0),(3.0,.70,.03,0),(3.12,.91,.03,0),(4.35,.43,.07,0),(4.46,.62,.07,0),(6.9,.025,.12,0)],.32,3),
('Forest_Coastal',9,[(1.8,.3,.25,0),(2.4,.89,.55,0),(3.0,1.35,.80,.03),(3.65,1.56,1.0,0),(4.13,1.35,1.28,0),(4.48,.75,1.53,0),(4.65,.08,1.66,0)],.38,4),
('Forest_Oak',10,[(1.5,.35,0,0),(2.0,1.20,-.1,0),(2.65,1.80,-.14,.02),(3.4,1.95,-.05,0),(4.05,1.64,.12,0),(4.5,.85,.23,0),(4.65,.09,.28,0)],.49,1),
('Forest_Birch',8,[(1.8,.22,0,0),(2.5,.60,.02,0),(3.35,.88,-.05,0),(4.25,.94,-.1,.02),(5.1,.75,.02,0),(5.85,.45,.08,0),(6.35,.035,.12,0)],.25,5)])
profiles.extend([
('Forest_MatureBeech',10,[(2.15,.42,0,0),(2.9,1.24,-.12,0),(4.0,1.91,-.2,.03),(5.3,2.10,-.08,.03),(6.55,1.87,.18,0),(7.55,1.26,.30,.02),(8.2,.61,.38,0),(8.55,.06,.34,0)],.57,1),
('Forest_TallFir',8,[(1.7,.31,0,0),(2.1,1.49,-.05,0),(3.7,.87,-.03,0),(3.88,1.17,-.03,0),(5.45,.56,.02,0),(5.62,.82,.02,0),(7.0,.33,.06,0),(7.14,.47,.06,0),(9.05,.025,.11,0)],.42,3),
('Forest_Rowan',8,[(1.12,.24,0,0),(1.5,.72,-.07,0),(2.05,1.02,-.09,.02),(2.65,.94,.03,0),(3.15,.62,.14,0),(3.42,.055,.20,0)],.24,5),
('Forest_DwarfPine',8,[(1.05,.27,0,0),(1.32,1.0,-.10,0),(2.12,.60,-.09,0),(2.24,.74,-.08,0),(2.94,.30,.03,0),(3.03,.40,.05,0),(3.8,.03,.18,0)],.27,3)])
palettes=[('#65904C','#79A44F','#8AB450'),('#598B51','#6E9D55','#81AD5C'),('#708F4C','#829F55','#95AF61'),('#396A53','#497D59','#629361'),('#557C50','#709450','#8AA35C'),('#6B954F','#85AC59','#A1BF69')]
reports={};trees={};stumps={};shared=None
for name,n,rings,base,palette in profiles:
    COL['green'],COL['green2'],COL['light']=palettes[palette]
    COL['wood']='#C6C5B3' if name=='Forest_Birch' else '#8B7053'
    k=Kit(name,COL);k.group=name+'_Wood'
    lean=.15 if name=='Forest_Leaning' else -.04 if name=='Forest_Uneven' else .35 if name=='Forest_Coastal' else 0
    trunk_top=3.6 if name=='Forest_MatureBeech' else 2.05 if name in ['Forest_Rowan','Forest_DwarfPine'] else 2.75
    wood=loft('Trunk',[(-.06,base*.86,0,0),(.12,base,0,0),(.38,base*.65,.01,0),(.95,base*.51,lean*.5,.01),(1.8,base*.39,lean,.02),(trunk_top,base*.17,lean*1.6,.01)],6,'wood')
    if name=='Forest_Birch':
        attr=wood.data.color_attributes['Col']
        for p in wood.data.polygons:
            if p.index%9==3:
                for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex('#828C80')),1)
    k.group=name+'_Canopy'
    crown=loft('Crown',rings,n,'green2',True);color_mesh(crown,True)
    mods=k.join_modules()
    if shared is None:shared=k.mat;shared.name='Forest_Shared_VertexColor'
    for ob in mods.values():ob.data.materials.clear();ob.data.materials.append(shared)
    markers=[k.marker(name+'__Ground',(0,0,0),'resource placement'),k.marker(name+'__Chop_Target',(lean*.5,-base*.51,.95),'axe-contact reference'),k.marker(name+'__Fell_Pivot',(0,0,.38),'reference only')]
    stump_root=bpy.data.objects.new(name+'_Stump',None);bpy.context.collection.objects.link(stump_root)
    stump=wood.copy();stump.data=wood.data.copy();stump.name=name+'_Stump_Mesh';bpy.context.collection.objects.link(stump);stump.parent=stump_root
    bm=bmesh.new();bm.from_mesh(stump.data)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,plane_co=(0,0,.38),plane_no=(0,0,1),clear_outer=True)
    bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(stump.data);bm.free()
    color_mesh(stump)
    attr=stump.data.color_attributes['Col']
    for p in stump.data.polygons:
        if all(abs(stump.data.vertices[v].co.z-.38)<1e-4 for v in p.vertices):
            for j in p.loop_indices:attr.data[j].color=(*ss.srgb_to_linear(ss._hex(COL['cut'])),1)
    reports[name]=export(name,k.root,list(mods.values()),markers)
    reports[name+'_Stump']=export(name+'_Stump',stump_root,[stump],[])
    assert reports[name]['triangles']<=250
    trees[name]=(k.root,list(mods.values()),markers);stumps[name]=(stump_root,stump)
    stump.hide_render=True;stump.hide_set(True)
(OUT/'validation.json').write_text(json.dumps(reports,indent=2))
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.5;scene.display.shading.show_shadows=False;scene.world.color=ss.srgb_to_linear(ss._hex('#A6B7BE'))
cam=bpy.data.objects.new('Forest_Review',bpy.data.cameras.new('Forest_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def render(name,eye,target,scale,res=(1600,1000)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale;scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for i,(name,(root,obs,marks)) in enumerate(trees.items()):root.location=((i%7-3)*4.6,(i//7)*8,0)
render('family-lineup',(2,-38,28),(0,4,4),35,(2100,1200))
for i,(name,(root,obs,marks)) in enumerate(trees.items()):
    if i<10:
        for ob in obs:ob.hide_render=True
    else:root.location=((i-11.5)*4.8,0,0)
render('new-additions',(2,-32,12),(0,0,4),22,(1600,1000))
for name,(root,obs,marks) in trees.items():
    for ob in obs:ob.hide_render=True;ob.hide_set(True)
for name,(root,obs,marks) in trees.items():
    old=root.location.copy();root.location=(0,0,0)
    for ob in obs:ob.hide_render=False;ob.hide_set(False)
    height=reports[name]['bounds'][1][2]
    render(name,(9,-16,height*.8),(0,0,height*.5),max(5,height*1.25),(900,1000))
    for ob in obs:ob.hide_render=True;ob.hide_set(True)
    root.location=old
# Mixed-age irregular spacing; a small clearing remains open in the foreground.
placement=[(0,-4,-1,.95,.4),(1,0,0,.91,2.1),(3,4,-.8,.96,1.8),(12,-2,-3,.95,3.4),(13,2,-3,.95,4.7),(6,-5,3,.98,2.8),(0,-1,4,1.02,5.2),(9,3.7,3,.94,.7),(7,5.8,1.2,1.05,1.4),(10,-3.2,7,.97,3.2),(11,1,7.5,.93,4.1),(0,5.4,6.7,.91,2.4)]
copies=[]
names=list(trees)
for index,x,y,scale,angle in placement:
    name=names[index];root=bpy.data.objects.new(name+'_Grove',None);scene.collection.objects.link(root);root.location=(x,y,0);root.scale=(scale,)*3;root.rotation_euler.z=angle
    for src in trees[name][1]:
        ob=src.copy();scene.collection.objects.link(ob);ob.parent=root;ob.hide_render=False;ob.hide_set(False);copies.append(ob)
render('mixed-forest',(17,-24,21),(0,2.5,3.5),26,(1600,1200))
render('game-scale',(17,-24,21),(0,2.5,2.6),23,(640,480))
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            v=area.spaces.active;v.shading.color_type='VERTEX';v.overlay.show_overlays=False;v.region_3d.view_location=(0,2.5,2.6);v.region_3d.view_distance=27;v.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'source/forest-lowpoly-astra-v3.blend'))
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
    if not name.endswith('_Stump'):
        for marker in ['Ground','Chop_Target','Fell_Pivot']:assert name+'__'+marker in scene.objects
    assert tri==reports[name]['triangles']
    verified[name]={'result':'PASS','triangles':tri}
(OUT/'export-verification.json').write_text(json.dumps(verified,indent=2));print('PASS',verified)


