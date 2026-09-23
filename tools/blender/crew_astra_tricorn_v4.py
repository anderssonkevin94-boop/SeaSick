"""Connected garment topology and hand-weighted deformation study.

Blender coordinates: X across the shoulders, -Y forward, Z up.
The sleeve holes and crotch are explicitly bridged, not boolean unions.
"""
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import steamer as ss

ROOT=HERE.parents[1]
OUT=ROOT/'art-staging/crew-astra-tricorn-v4'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=HERE/'source/crew-astra-tricorn-v4.blend'
PALETTE={'linen':'#E8DFC7','cuff':'#EFE7D4','skin':'#D99259',
         'pants':'#42677D','belt':'#62452D','boot':'#483628',
         'sole':'#352C25','cap':'#38414B','capband':'#2B333B',
         'hair':'#392B22','eye':'#302921','button':'#857B62'}
COL={k:ss.srgb_to_linear(ss._hex(v)) for k,v in PALETTE.items()}
MODELS=[]
AUDIT={}
SHAPE=[(math.cos(i*math.tau/8),math.sin(i*math.tau/8)) for i in range(8)]
TORSO=[(-.72,-1),(.72,-1),(1,-.65),(1,0),(1,.65),
       (.72,1),(-.72,1),(-1,.65),(-1,0),(-1,-.65)]


class Mesh:
    def __init__(self,name):
        self.name=name;self.v=[];self.f=[];self.colors=[];self.weights=[]

    def vertex(self,p,w):
        self.v.append(tuple(p));self.weights.append(dict(w));return len(self.v)-1

    def face(self,ids,color):
        self.f.append(list(ids));self.colors.append(color)

    def ring(self,points,weights):
        return [self.vertex(p,weights(p) if callable(weights) else weights) for p in points]

    def bridge(self,a,b,color):
        assert len(a)==len(b)
        for i in range(len(a)):
            self.face([a[i],a[(i+1)%len(a)],b[(i+1)%len(b)],b[i]],color)

    def close(self,r,color):
        self.face(r,color)

    def finish(self,skin=False,faceted=False):
        used=sorted({i for face in self.f for i in face})
        remap={old:new for new,old in enumerate(used)}
        self.v=[self.v[i] for i in used];self.weights=[self.weights[i] for i in used]
        self.f=[[remap[i] for i in face] for face in self.f]
        me=bpy.data.meshes.new(self.name);me.from_pydata(self.v,[],self.f);me.update()
        o=bpy.data.objects.new(self.name,me);bpy.context.scene.collection.objects.link(o)
        me.materials.append(SKIN if skin else CLOTH)
        attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
        for p,c in zip(me.polygons,self.colors):
            for li in p.loop_indices:attr.data[li].color=(*COL[c],1)
        me.color_attributes.active_color=attr
        bm=bmesh.new();bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        for f in bm.faces:f.smooth=False
        for e in bm.edges:
            if e.is_manifold:e.smooth=e.calc_face_angle()<math.radians(55)
        bm.to_mesh(me);bm.free()
        groups={n:o.vertex_groups.new(name=n) for n in sorted({n for w in self.weights for n in w})}
        for i,w in enumerate(self.weights):
            total=sum(w.values())
            assert total>0
            for n,value in w.items():
                if value>0:groups[n].add([i],value/total,'REPLACE')
        mod=o.modifiers.new('Crew deformation','ARMATURE');mod.object=RIG
        mod.use_deform_preserve_volume=False
        o.parent=RIG;o['skin_tint']=skin
        MODELS.append(o)
        return o


def mat(name):
    m=bpy.data.materials.new(name);m.use_nodes=True
    a=m.node_tree.nodes.new('ShaderNodeVertexColor');a.layer_name='Col'
    b=m.node_tree.nodes.get('Principled BSDF');b.inputs['Roughness'].default_value=.85
    m.node_tree.links.new(a.outputs['Color'],b.inputs['Base Color'])
    return m


def side_name(base,side):return base+('.R' if side>0 else '.L')


def lerp(a,b,t):return Vector(a).lerp(Vector(b),t)


def section(center,direction,rx,ry,n=8):
    d=Vector(direction).normalized()
    v=Vector((0,1,0));v=(v-d*v.dot(d)).normalized()
    u=v.cross(d).normalized()
    return [Vector(center)+u*rx*math.cos(i*math.tau/n)+v*ry*math.sin(i*math.tau/n) for i in range(n)]


def match_boundary(mesh,indices,points):
    # Cyclic correspondence preserves the armhole instead of twisting it.
    choices=[]
    for seq in [indices,list(reversed(indices))]:
        for k in range(len(seq)):
            order=seq[k:]+seq[:k]
            error=sum((Vector(mesh.v[j])-Vector(p)).length_squared for j,p in zip(order,points))
            choices.append((error,order))
    return min(choices,key=lambda pair:pair[0])[1]


REST={
    'root':((0,0,.02),(0,0,.24),None),
    'pelvis':((0,0,.88),(0,0,1.02),'root'),
    'spine':((0,0,1.02),(0,0,1.38),'pelvis'),
    'head':((0,0,1.38),(0,0,1.75),'spine')}
for s in [-1,1]:
    upper=side_name('upper_arm',s);fore=side_name('forearm',s);hand=side_name('hand',s)
    thigh=side_name('thigh',s);shin=side_name('shin',s);foot=side_name('foot',s)
    REST[upper]=((s*.235,0,1.345),(s*.365,0,1.145),'spine')
    REST[fore]=(REST[upper][1],(s*.48,-.01,.957),upper)
    REST[hand]=(REST[fore][1],(s*.535,-.015,.862),fore)
    REST[thigh]=((s*.115,0,.90),(s*.14,-.015,.575),'pelvis')
    REST[shin]=(REST[thigh][1],(s*.14,0,.17),thigh)
    REST[foot]=(REST[shin][1],(s*.14,-.18,.075),shin)


def rig():
    data=bpy.data.armatures.new('DeckhandSkeleton')
    o=bpy.data.objects.new('Deckhand_Rig',data);bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active=o;o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for name,(a,b,parent) in REST.items():
        bone=data.edit_bones.new(name);bone.head=a;bone.tail=b
        if parent:bone.parent=data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT');o.show_in_front=True
    return o


def shirt():
    m=Mesh('Shirt_Connected')
    rows=[(.995,.193,.123),(1.155,.213,.128),(1.285,.244,.136),
          (1.397,.232,.128),(1.443,.083,.073)]
    rings=[]
    for j,(z,rx,ry) in enumerate(rows):
        ids=[]
        for i,(x,y) in enumerate(TORSO):
            w={'spine':1}
            if j==0:w={'pelvis':.7,'spine':.3}
            if j in [1,2,3] and i in [2,3,4,7,8,9]:
                s=1 if x>0 else -1
                amount=[.04,.16,.27][j-1]
                w={'spine':1-amount,side_name('upper_arm',s):amount}
            ids.append(m.vertex((x*rx,y*ry,z),w))
        rings.append(ids)
    for j in range(len(rings)-1):
        for i in range(10):
            # Each shoulder opening occupies a 2x2 patch of the torso grid.
            if j in [1,2] and i in [2,3,7,8]:continue
            m.face([rings[j][i],rings[j][(i+1)%10],rings[j+1][(i+1)%10],rings[j+1][i]],'linen')
    for s,start in [(1,2),(-1,7)]:
        boundary=[rings[1][start],rings[1][start+1],rings[1][start+2],
                  rings[2][start+2],rings[3][start+2],rings[3][start+1],
                  rings[3][start],rings[2][start]]
        upper=side_name('upper_arm',s);fore=side_name('forearm',s)
        a,e,_=REST[upper];_,w,_=REST[fore]
        path=[(lerp(a,e,.29),.091,.089,{upper:1}),
              (lerp(a,e,.70),.081,.079,{upper:.9,fore:.1}),
              (lerp(a,e,.92),.077,.075,{upper:.65,fore:.35}),
              (lerp(e,w,.05),.076,.074,{upper:.35,fore:.65}),
              (lerp(e,w,.16),.077,.075,{fore:1}),
              (lerp(e,w,.17),.079,.077,{fore:1}),
              (lerp(e,w,.29),.077,.075,{fore:1}),
              (lerp(e,w,.30),.073,.071,{fore:1})]
        direction=Vector(w)-Vector(a)
        first=section(path[0][0],direction,path[0][1],path[0][2])
        prev=match_boundary(m,boundary,first)
        for j,(p,rx,ry,weights) in enumerate(path):
            r=m.ring(section(p,direction,rx,ry),weights)
            m.bridge(prev,r,'cuff' if j>=5 else 'linen');prev=r
        # Turn the cuff edge inward, leaving a real garment opening.
        inner=m.ring(section(path[-1][0],direction,.057,.054),{fore:1})
        m.bridge(prev,inner,'cuff')
    # Folded neckline: a narrow ring rather than a collar floating above skin.
    collar=m.ring([(x*.078,y*.068,1.452) for x,y in TORSO],{'spine':.5,'head':.5})
    m.bridge(rings[-1],collar,'cuff')
    o=m.finish();o['expected_components']=1;o['expected_boundary_loops']=4


def trousers():
    m=Mesh('Trousers_Connected');tops={}
    # The two leg roots share their inner crotch edge. Their combined outer
    # perimeter becomes the pelvis, producing one connected pair of trousers.
    outline=[(0,-.067,.80),(.055,-.114,.866),(.178,-.117,.89),(.221,-.060,.882),
             (.221,.061,.882),(.178,.128,.885),(.055,.120,.86),(0,.072,.80)]
    shared={}
    for s in [1,-1]:
        thigh=side_name('thigh',s);shin=side_name('shin',s)
        r=[]
        for i,(x,y,z) in enumerate(outline):
            if x==0 and i in shared:r.append(shared[i])
            else:
                w={'pelvis':1} if x==0 else {'pelvis':.8,thigh:.2}
                idx=m.vertex((s*x,y,z),w);r.append(idx)
                if x==0:shared[i]=idx
        tops[s]=r
        specs=[(.755,s*.117,0,.099,.100,{thigh:1}),
               (.635,s*.135,-.012,.081,.087,{thigh:.9,shin:.1}),
               (.58,s*.14,-.015,.078,.079,{thigh:.55,shin:.45}),
               (.535,s*.14,-.012,.074,.075,{thigh:.15,shin:.85}),
               (.43,s*.14,-.004,.066,.065,{shin:1}),
               (.33,s*.14,0,.061,.059,{shin:1})]
        prev=r
        for j,(z,x,y,rx,ry,weights) in enumerate(specs):
            # Clockwise traversal matches the deliberately shaped leg root.
            shape=[(-1,-.65),(-.65,-1),(.65,-1),(1,-.65),(1,.65),(.65,1),(-.65,1),(-1,.65)]
            pts=[(x+s*u*rx,y+v*ry,z) for u,v in shape]
            r=m.ring(pts,weights);m.bridge(prev,r,'pants');prev=r
        m.close(prev,'pants')
    perimeter=tops[1]+list(reversed(tops[-1][1:-1]))
    prev=perimeter
    for z,rx,ry in [(.94,.210,.136),(.991,.201,.127)]:
        pts=[]
        for idx in perimeter:
            x,y,_=m.v[idx]
            a=math.atan2(y/.125,x/.22)
            pts.append((rx*math.cos(a),ry*math.sin(a),z))
        r=m.ring(pts,{'pelvis':1});m.bridge(prev,r,'pants');prev=r
    m.close(prev,'pants')
    o=m.finish();o['expected_components']=1;o['expected_boundary_loops']=0


def arms():
    for s in [-1,1]:
        m=Mesh('Arm_Hand'+('.R' if s>0 else '.L'))
        fore=side_name('forearm',s);hand=side_name('hand',s)
        e,w,_=REST[fore];end=REST[hand][1]
        direction=Vector(end)-Vector(e)
        rows=[(lerp(e,w,.22),.056,.053,{fore:1}),
              (lerp(e,w,.65),.048,.046,{fore:1}),
              (lerp(e,w,.92),.038,.036,{fore:.75,hand:.25}),
              (Vector(w),.037,.035,{fore:.35,hand:.65}),
              (lerp(w,end,.32),.057,.033,{hand:1}),
              (lerp(w,end,.77),.059,.032,{hand:1}),
              (lerp(w,end,1.06),.045,.027,{hand:1})]
        rings=[m.ring(section(p,direction,rx,ry),weights) for p,rx,ry,weights in rows]
        m.close(rings[0],'skin')
        # One palm side quad becomes the thumb root, rather than an overlap.
        side=7 if s>0 else 3
        for j in range(len(rings)-1):
            for i in range(8):
                if j==4 and i==side:continue
                m.face([rings[j][i],rings[j][(i+1)%8],rings[j+1][(i+1)%8],rings[j+1][i]],'skin')
        root=[rings[4][side],rings[4][(side+1)%8],rings[5][(side+1)%8],rings[5][side]]
        center=sum((Vector(m.v[i]) for i in root),Vector())/4
        previous=root
        for offset,scale in [(Vector((-s*.030,-.016,-.005)),.84),
                             (Vector((-s*.048,-.018,-.036)),.54)]:
            r=m.ring([center+offset+(Vector(m.v[i])-center)*scale for i in root],{hand:1})
            m.bridge(previous,r,'skin');previous=r
        m.close(previous,'skin');m.close(rings[-1],'skin')
        o=m.finish(skin=True);o['expected_components']=1;o['expected_boundary_loops']=0


def loft(m,rows,color,weights,shape=SHAPE,caps=True):
    rs=[]
    for x,y,z,rx,ry in rows:
        r=m.ring([(x+a*rx,y+b*ry,z) for a,b in shape],weights)
        if rs:m.bridge(rs[-1],r,color)
        rs.append(r)
    if caps:m.close(rs[0],color);m.close(rs[-1],color)
    return rs


def accessories():
    for s in [-1,1]:
        m=Mesh('Boot'+('.R' if s>0 else '.L'))
        foot=side_name('foot',s);shin=side_name('shin',s)
        shape=[(-.72,-1),(.72,-1),(1,-.72),(1,.72),(.72,1),(-.72,1),(-1,.72),(-1,-.72)]
        def weights(p):
            t=max(0,min(1,(p[2]-.105)/.16))
            return {foot:1-t,shin:t}
        rings=loft(m,[(s*.14,-.055,.024,.077,.145),(s*.14,-.057,.064,.078,.148),
                      (s*.14,-.062,.105,.077,.145),(s*.14,-.015,.17,.061,.087),
                      (s*.14,0,.24,.063,.068),(s*.14,0,.405,.076,.074),
                      (s*.14,0,.426,.078,.076)],'boot',weights,shape,caps=False)
        m.colors[:8]=['sole']*8
        m.close(rings[0],'sole')
        inner=m.ring([(s*.14+x*.071,-.003+y*.072,.426) for x,y in shape],{shin:1})
        m.bridge(rings[-1],inner,'boot')
        inside=m.ring([(s*.14+x*.066,-.002+y*.065,.302) for x,y in shape],{shin:1})
        m.bridge(inner,inside,'boot');m.close(inside,'boot')
        m.finish()
    m=Mesh('Belt');loft(m,[(0,0,.968,.207,.134),(0,0,1.016,.205,.132)],'belt',{'pelvis':1},TORSO)
    m.finish()


def head():
    m=Mesh('Head_Neck')
    def weights(p):
        t=max(0,min(1,(p[2]-1.40)/.075));return {'head':t,'spine':1-t}
    loft(m,[(0,0,1.408,.058,.057),(0,0,1.47,.061,.060),
            (0,-.006,1.499,.088,.076),(0,-.009,1.531,.115,.099),
            (0,0,1.625,.128,.11),(0,.002,1.702,.113,.095),
            (0,.002,1.727,.087,.08)],'skin',weights,TORSO)
    m.finish(skin=True)
    m=Mesh('Hair')
    loft(m,[(0,.044,1.535,.095,.066),(0,.030,1.585,.128,.081),
            (0,.018,1.699,.129,.105)],'hair',{'head':1},TORSO)
    m.finish(faceted=True)
    # Crown and folded brim form one continuous shell, including the inner lining.
    m=Mesh('Tricorn')
    n=24
    angles=[-math.pi/2+i*math.tau/n for i in range(n)]
    w={'head':1}
    def ellipse(rx,ry,z):
        return [(rx*math.cos(t),ry*math.sin(t),z) for t in angles]
    def brim(inset=0,dz=0):
        points=[]
        for t in angles:
            corner=(1+math.cos(3*(t+math.pi/2)))/2
            radius=.193+.060*corner-inset
            points.append((radius*math.cos(t),radius*math.sin(t),
                           1.735+.082*(1-corner)+dz))
        return points
    rows=[
        (ellipse(.071,.063,1.822),'cap'),
        (ellipse(.116,.099,1.797),'cap'),
        (ellipse(.137,.117,1.742),'cap'),
        (ellipse(.139,.120,1.716),'capband'),
        (brim(.010,-.009),'cap'),
        (brim(),'cap'),
        (brim(0,-.012),'capband'),
        (brim(.010,-.021),'capband'),
        (ellipse(.128,.109,1.704),'cap'),
        (ellipse(.124,.105,1.742),'capband'),
        (ellipse(.103,.087,1.785),'capband'),
        (ellipse(.068,.057,1.809),'capband')]
    rings=[m.ring(points,w) for points,color in rows]
    m.close(rings[0],'cap')
    for i in range(len(rings)-1):
        m.bridge(rings[i],rings[i+1],rows[i+1][1])
    m.close(rings[-1],'capband')
    m.finish(faceted=True)
    # Quiet face marks are secondary to the silhouette at gameplay scale.
    m=Mesh('Face_details')
    for s in [-1,1]:
        loft(m,[(s*.048,-.112,1.618,.011,.007),(s*.048,-.112,1.63,.011,.007)],'eye',{'head':1})
        loft(m,[(s*.048,-.111,1.65,.023,.008),(s*.048,-.111,1.659,.023,.008)],'hair',{'head':1})
    m.finish(faceted=True)
    m=Mesh('Nose')
    ids=m.ring([(-.018,-.105,1.63),(.018,-.105,1.63),(-.021,-.142,1.577),
                (.021,-.142,1.577),(-.022,-.104,1.572),(.022,-.104,1.572)],{'head':1})
    for f in [(0,1,3,2),(2,3,5,4),(0,2,4),(1,5,3),(0,4,5,1)]:m.face([ids[i] for i in f],'skin')
    m.finish(skin=True,faceted=True)


def topology(o):
    bm=bmesh.new();bm.from_mesh(o.data)
    unseen=set(bm.verts);components=0
    while unseen:
        components+=1;stack=[unseen.pop()]
        while stack:
            for e in stack.pop().link_edges:
                for v in e.verts:
                    if v in unseen:unseen.remove(v);stack.append(v)
    boundary={e for e in bm.edges if e.is_boundary};loops=0
    while boundary:
        loops+=1;stack=list(boundary.pop().verts)
        while stack:
            for e in stack.pop().link_edges:
                if e in boundary:boundary.remove(e);stack.extend(e.verts)
    d={'vertices':len(bm.verts),'triangles':sum(len(f.verts)-2 for f in bm.faces),
       'components':components,'boundary_loops':loops,
       'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges),
       'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    bm.free()
    assert not d['overconnected_edges'] and not d['degenerate_faces'],(o.name,d)
    if 'expected_components' in o:assert components==o['expected_components'],(o.name,d)
    if 'expected_boundary_loops' in o:assert loops==o['expected_boundary_loops'],(o.name,d)
    for v in o.data.vertices:assert abs(sum(g.weight for g in v.groups)-1)<1e-5
    return d


def pose(kind):
    RIG.location=(0,0,0)
    for pb in RIG.pose.bones:pb.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    if kind=='neutral':return
    def aim(name,head,direction):
        bone=RIG.data.bones[name];length=bone.length
        delta=(bone.tail_local-bone.head_local).rotation_difference(Vector(direction).normalized())
        rotation=delta.to_matrix().to_4x4()@bone.matrix_local.to_quaternion().to_matrix().to_4x4()
        rotation.translation=Vector(head);RIG.pose.bones[name].matrix=rotation
        bpy.context.view_layer.update()
        return Vector(head)+Vector(direction).normalized()*length
    for s in [-1,1]:
        upper=side_name('upper_arm',s);fore=side_name('forearm',s);hand=side_name('hand',s)
        if kind=='reach':u=(s*.30,-.90,.18);f=(s*.08,-1,.05)
        else:u=(s*.48,-.25,-.85);f=(s*.05,-1,-.08)
        e=aim(upper,REST[upper][0],u);w=aim(fore,e,f);aim(hand,w,f)
        thigh=side_name('thigh',s);shin=side_name('shin',s);foot=side_name('foot',s)
        if kind=='crouch':td=(s*.08,-.73,-.68);sd=(0,.65,-.76)
        elif kind=='stride':td=(0,-s*.48,-.88);sd=(0,s*.17,-.98)
        else:td=(s*.16,-.16,-.97);sd=(s*.02,.17,-.985)
        knee=aim(thigh,REST[thigh][0],td);ankle=aim(shin,knee,sd)
        aim(foot,ankle,(0,-.885,-.467))
    graph=bpy.context.evaluated_depsgraph_get()
    minimum=min((o.evaluated_get(graph).matrix_world@v.co).z
                for o in MODELS if o.name.startswith('Boot') for v in o.evaluated_get(graph).data.vertices)
    RIG.location.z=-minimum+.01;bpy.context.view_layer.update()


def render(name,eye=(3,5,3),size=(1000,1100),wire=False):
    cam.location=eye;cam.rotation_euler=(Vector((0,-.02,.96))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x,scene.render.resolution_y=size
    temporary=[]
    if wire:
        for o in MODELS:
            if o.name not in ['Shirt_Connected','Trousers_Connected','Arm_Hand.R','Arm_Hand.L']:continue
            ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get())
            ob=bpy.data.objects.new('Topology',bpy.data.meshes.new_from_object(ev))
            scene.collection.objects.link(ob);ob.matrix_world=o.matrix_world.copy()
            for c in ob.data.color_attributes['Col'].data:c.color=(.025,.025,.025,1)
            mod=ob.modifiers.new('Edges','WIREFRAME');mod.thickness=.0015;mod.offset=1;mod.use_replace=True
            temporary.append(ob)
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
    for o in temporary:bpy.data.objects.remove(o,do_unlink=True)


def intersection_count(a,b):
    graph=bpy.context.evaluated_depsgraph_get()
    def tree(o):
        ev=o.evaluated_get(graph);me=ev.to_mesh();me.calc_loop_triangles()
        t=BVHTree.FromPolygons([ev.matrix_world@v.co for v in me.vertices],
                              [tuple(f.vertices) for f in me.loop_triangles],all_triangles=True)
        ev.to_mesh_clear();return t
    return len(tree(a).overlap(tree(b)))


ss.clear_scene();CLOTH=mat('Crew_Cloth');SKIN=mat('Crew_Skin_Preview');RIG=rig()
shirt();trousers();arms();accessories();head()
AUDIT['topology']={o.name:topology(o) for o in MODELS}
AUDIT['triangles']=sum(d['triangles'] for d in AUDIT['topology'].values())
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=.65
scene.display.shading.background_type='WORLD';scene.world.color=ss.srgb_to_linear(ss._hex('#708D96'))
scene.display.shading.show_shadows=False;scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);scene.collection.objects.link(cam)
scene.camera=cam;cd.type='ORTHO';cd.ortho_scale=2.2
AUDIT['poses']={}
for frame,kind in [(1,'neutral'),(21,'working'),(41,'reach'),(61,'crouch'),(81,'stride')]:
    scene.frame_set(frame);pose(kind)
    for pb in RIG.pose.bones:
        pb.rotation_mode='QUATERNION'
        pb.keyframe_insert('location',frame=frame);pb.keyframe_insert('rotation_quaternion',frame=frame)
        pb.keyframe_insert('scale',frame=frame)
    RIG.keyframe_insert('location',frame=frame)
    graph=bpy.context.evaluated_depsgraph_get()
    degenerate=0
    for o in MODELS:
        ev=o.evaluated_get(graph);me=ev.to_mesh();me.calc_loop_triangles()
        degenerate+=sum(t.area<1e-10 for t in me.loop_triangles);ev.to_mesh_clear()
    pairs=[('Shirt_Connected','Arm_Hand.L'),('Shirt_Connected','Arm_Hand.R'),
           ('Trousers_Connected','Boot.L'),('Trousers_Connected','Boot.R')]
    contacts={a+' / '+b:intersection_count(bpy.data.objects[a],bpy.data.objects[b]) for a,b in pairs}
    AUDIT['poses'][kind]={'degenerate_triangles':degenerate,'garment_intersections':contacts}
    assert degenerate==0,(kind,degenerate)
    assert not any(contacts.values()),(kind,contacts)
    render(kind+'-rear')
    render(kind+'-front',(3,-5,2.6))
    if kind=='neutral':render('topology-front',(3,-5,2.6),wire=True)
    if kind=='working':render('game-size',(3,5,3),(180,198))
scene.frame_end=81
scene.frame_set(1);pose('neutral')
for o in MODELS:
    if o['skin_tint']:
        for c in o.data.color_attributes['Col'].data:c.color=(1,1,1,1)
export_objects=[]
for skin in [False,True]:
    copies=[]
    for o in MODELS:
        if bool(o['skin_tint'])!=skin:continue
        ob=o.copy();ob.data=o.data.copy();scene.collection.objects.link(ob);copies.append(ob)
    bpy.ops.object.select_all(action='DESELECT')
    for o in copies:o.select_set(True)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join()
    ob=bpy.context.object;ob.name='CREW_Skin' if skin else 'CREW_Cloth';export_objects.append(ob)
bpy.ops.object.select_all(action='DESELECT');RIG.select_set(True)
for o in export_objects:o.select_set(True)
bpy.context.view_layer.objects.active=RIG
bpy.ops.export_scene.fbx(filepath=str(OUT/'deckhand-rigged.fbx'),use_selection=True,
    object_types={'MESH','ARMATURE'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
    use_armature_deform_only=True,bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE')
for o in export_objects:bpy.data.objects.remove(o,do_unlink=True)
for o in MODELS:
    if o['skin_tint']:
        for c in o.data.color_attributes['Col'].data:c.color=(*COL['skin'],1)
AUDIT['skin_base_color_srgb']=PALETTE['skin'];AUDIT['rig_bones']=len(RIG.data.bones)
AUDIT['notes']='Five keyed deformation tests, not polished gameplay animations. No Unity integration.'
(OUT/'validation.json').write_text(json.dumps(AUDIT,indent=2))
scene.frame_set(21)
bpy.ops.object.select_all(action='DESELECT')
cam.location=(3,5,3);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler()
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active
            space.shading.color_type='VERTEX'
            space.region_3d.view_location=(0,0,.95)
            space.region_3d.view_distance=3.2
            space.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print(json.dumps(AUDIT,indent=2))


