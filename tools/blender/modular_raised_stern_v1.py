"""Hull-supported quarterdeck with unchanged M1 wheel and centreline hatch."""
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Matrix,Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_raised_bow_v4 as bow_revision

base=bow_revision.base
fore,family,ship,ss=base.fore,base.family,base.ship,base.ss
MODE=base.MODE
OUT=HERE.parents[1]/'art-staging/modular-raised-stern-v1'/MODE
OUT.mkdir(parents=True,exist_ok=True)
L=9.30
H=base.H
MIRROR=Matrix.Translation((L,0,0))@Matrix.Diagonal((-1,1,1,1))


def normals(obj):
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free()


def make(name,b,root):
    o=ship.mesh('RaisedStern__'+name,b);o.parent=root
    return o


def build():
    stern=bpy.data.objects[family.STERN]
    shell=bpy.data.objects[family.STERN+'__Hull_Shell']
    # Boolean operations need a closed operand; remove this temporary join cap
    # again afterwards so the original modular interface remains open.
    bm=bmesh.new();bm.from_mesh(shell.data)
    edges=[e for e in bm.edges if e.is_boundary and all(abs(v.co.x-L)<.0001 for v in e.verts)]
    bmesh.ops.holes_fill(bm,edges=edges,sides=0)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(shell.data);bm.free()
    stations=[(z-ship.ZT,ship.interp(z,ship.BEAM)) for z in ship.ST if z<=family.CUT_A]
    stations[0]=(-.025,stations[0][1])
    stations[-1]=(L-.04,stations[-1][1])
    def width(x):
        for (a,w),(b,v) in zip(stations,stations[1:]):
            if x<=b:return w+(v-w)*(x-a)/(b-a)
        return stations[-1][1]
    b=ss.Builder()
    outline=[(x,-w) for x,w in stations]+[(x,w) for x,w in reversed(stations)]
    fore.slab(b,outline,1.70,H,'deck')
    block=make('UpperVolume',b,stern)
    # Boolean a real upper hull volume into the existing shell, eliminating the
    # buried original deck and wheel housing roof instead of hiding them.
    ship.boolean_into(shell,block,'UNION')
    b=ss.Builder();fore.box(b,-2,ship.WF-ship.ZT,-ship.OPEN,ship.OPEN,-4,2.17,'dark')
    ship.boolean_into(shell,make('WheelPocketCutter',b,stern),'DIFFERENCE')
    # Access void: door at forward bulkhead, centreline hatch well behind it.
    b=ss.Builder()
    polygon=[(-.3,base.D),(base.HX1,base.D),(base.HX1,H+.6),
             (base.HX0,H+.6),(base.HX0,base.DOOR_TOP),(-.3,base.DOOR_TOP)]
    def passage_point(x,y,z):
        t=max(0,min(1,(x-base.old.CUT)/(base.HX0-base.old.CUT)))
        return fore.point(L-x,y+base.ACCESS_Y*(1-t),z)
    sides=[[passage_point(x,s*base.Y,z) for x,z in polygon] for s in [-1,1]]
    for s,points in zip([-1,1],sides):ship.cap(b,points,'wood2',(s,0,0))
    for i in range(len(polygon)):
        j=(i+1)%len(polygon)
        b.face([sides[0][i],sides[0][j],sides[1][j],sides[1][i]],'wood2',away=(0,2.7,L-1.2))
    cutter=make('AccessCutter',b,stern);normals(cutter)
    ship.boolean_into(shell,cutter,'DIFFERENCE')
    bm=bmesh.new();bm.from_mesh(shell.data)
    caps=[f for f in bm.faces if all(abs(v.co.x-L)<.0001 for v in f.verts)]
    bmesh.ops.delete(bm,geom=caps,context='FACES')
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.00001)
    for _ in range(5):
        redundant=[]
        for v in bm.verts:
            if abs(v.co.x-L)>.0001:continue
            v.co.x=L
            edges=[e for e in v.link_edges if e.is_boundary]
            if len(edges)!=2:continue
            a=edges[0].other_vert(v).co-v.co;b=edges[1].other_vert(v).co-v.co
            if a.length>1e-5 and b.length>1e-5 and a.normalized().dot(b.normalized())<-.999999:redundant.append(v)
        if not redundant:break
        bmesh.ops.dissolve_verts(bm,verts=redundant,use_face_split=False,use_boundary_tear=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(shell.data);bm.free()
    shell.name=family.STERN+'__Hull_Shell'
    # Remove fittings buried inside the new upper hull; the wheel itself stays.
    for name in ['Rails_Teal','Housing_Lid','Housing_Ironwork','Housing_AccessPanel']:
        o=bpy.data.objects.get(family.STERN+'__'+name)
        if o:bpy.data.objects.remove(o,do_unlink=True)
    for name in ['Plank_Seams','Hull_Ironwork']:
        o=bpy.data.objects[family.STERN+'__'+name]
        bm=bmesh.new();bm.from_mesh(o.data);todo=set(bm.verts)
        while todo:
            v=todo.pop();component={v};stack=[v]
            while stack:
                for e in stack.pop().link_edges:
                    for v in e.verts:
                        if v in todo:todo.remove(v);component.add(v);stack.append(v)
            if min(v.co.z for v in component)>1.70:
                bmesh.ops.delete(bm,geom=list(component),context='VERTS')
        bm.to_mesh(o.data);bm.free();o.data.update()
    iron=bpy.data.objects[family.STERN+'__Hull_Ironwork']
    bm=bmesh.new();bm.from_mesh(iron.data);bm.normal_update()
    caps=[f for f in bm.faces if f.normal.z>.99 and 1.70<f.calc_center_median().z<1.90
          and abs(f.calc_center_median().y)>3
          and .25<max(v.co.x for v in f.verts)-min(v.co.x for v in f.verts)<.5]
    assert len(caps)==4,('Stern brace caps',len(caps))
    bolts=ss.Builder()
    for cap in caps:
        c=cap.calc_center_median().copy()
        result=bmesh.ops.extrude_face_region(bm,geom=[cap])
        for v in result['geom']:
            if isinstance(v,bmesh.types.BMVert):v.co.z=H-.03
        bmesh.ops.delete(bm,geom=[cap],context='FACES_ONLY')
        sign=1 if c.y>0 else -1
        for z in [2.7,3.8]:bolts.cyl(fore.point(c.x,c.y+sign*.045,z),fore.point(c.x,c.y+sign*.12,z),.08,.08,6,'bolt')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(iron.data);bm.free()
    helm=bpy.data.objects[family.STERN+'__Helm']
    for v in helm.data.vertices:v.co.z+=H-base.D
    rails=ss.Builder();posts=ss.Builder();front=ss.Builder();seams=ss.Builder()
    for sign in [-1,1]:
        for a,b in [(stations[0][0],4.4),(6.0,L-.04)]:
            xs=[a]+[x for x,w in stations if a<x<b]+[b]
            fore.tube(rails,[(x,sign*width(x),H+.70) for x in xs],.28,.25,'teal')
        for x in [0,2.6,4.4,6.0,L-.04]:
            y=sign*width(x)
            fore.box(posts,x-.09,x+.09,y-.09,y+.09,H,H+.60,'wood')
            fore.box(posts,x-.13,x+.13,y-.13,y+.13,H+.59,H+.75,'iron')
        for z in [2.4,3.0,3.6]:
            fore.tube(seams,[(x,sign*(w+.01),z) for x,w in stations],.012,.016,'seam')
    fore.tube(rails,[(stations[0][0],-width(0),H+.70),(stations[0][0],width(0),H+.70)],.28,.25,'teal')
    for y in [-2,-1,0,1,2]:fore.box(posts,-.10,.06,y-.07,y+.07,H,H+.6,'teal')
    fore.tube(front,[(L-.04,-width(L),H+.70),(L-.04,width(L),H+.70)],.28,.25,'teal')
    for y in [-3,-1.5,0,1.5,3]:fore.box(front,L-.11,L+.03,y-.07,y+.07,H,H+.60,'teal')
    for n in range(-int(width(L)/.7),int(width(L)/.7)+1):
        y=n*.7
        start=0
        if abs(y)+.12>width(0):
            for (a,w),(b,v) in zip(stations,stations[1:]):
                if w<abs(y)+.12<=v:start=a+(b-a)*(abs(y)+.12-w)/(v-w);break
        intervals=[(start+.08,L-.12)] if abs(y)>.8 else [(start+.08,L-base.HX1-.07),(L-base.HX0+.07,L-.12)]
        for a,b in intervals:
            if b>a:fore.box(seams,a,b,y-.007,y+.007,H+.003,H+.012,'seam')
    for z in [2.4,3.0,3.6]:
        fore.box(seams,-.04,-.02,-width(0),width(0),z-.008,z+.008,'seam')
        for a,b in [(-width(L),base.ACCESS_Y-base.Y-.1),(base.ACCESS_Y+base.Y+.1,width(L))]:
            fore.box(seams,L-.04,L-.025,a,b,z-.008,z+.008,'seam')
    parts={}
    for name,b in [('UpperRails',rails),('UpperPosts',posts),('ForwardGuard',front),('UpperPlanks',seams),('UpperBraceBolts',bolts)]:parts[name]=make(name,b,stern)
    # Reuse the approved access fittings at their exact hinge-local dimensions.
    source=base.OUT/'short/ship.blend'
    with bpy.data.libraries.load(str(source),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n in ['RaisedBowV2__'+p for p in ['PortalFrames','InternalLadder','Door','Hatch']]]
    moving={}
    for obj in dst.objects:
        name=obj.name.split('__')[1].split('.')[0]
        bpy.context.scene.collection.objects.link(obj)
        obj.parent=stern;obj.matrix_parent_inverse=Matrix.Identity(4)
        if name in ['Door','Hatch']:
            pivot=obj.location.copy();obj.location=MIRROR@pivot
            obj.data.transform(Matrix.Diagonal((-1,1,1,1)))
            obj.rotation_euler=tuple(-v for v in obj.rotation_euler)
            moving[name]={'pivot':list(obj.location),'axis':'Z' if name=='Door' else 'Y','review_angle_degrees':-65 if name=='Door' else -80,'closed_angle_degrees':0}
        else:
            obj.matrix_basis=Matrix.Identity(4);obj.data.transform(MIRROR)
        normals(obj);obj.name='RaisedStern__'+name;parts[name]=obj
    parts.update({o.name.split('__',1)[1]:o for o in stern.children if o.type=='MESH' and o.name.startswith(family.STERN+'__')})
    for obj in list(stern.children):
        if obj.type=='EMPTY' and 'DeckSlot' in obj.name:bpy.data.objects.remove(obj,do_unlink=True)
    for name,pos in [('SternDoorEntry',(L+.4,base.ACCESS_Y,base.D)),('SternHatchExit',(L-1.75,0,H)),('SternUpperDeckJoin',(L,0,H))]:family.empty(name,pos,stern)
    return stern,parts,moving


def main():
    report={'family':MODE,'assemblies':{}}
    # Use a middle bay so both raised ends can be inspected around a low deck.
    for scene_name,source in [('stern-only',fore.SOURCE/'long/ship.blend'),('both-raised',base.OUT/'long/ship.blend')]:
        bpy.ops.wm.open_mainfile(filepath=str(source))
        stern,parts,moving=build();bpy.context.view_layer.update()
        checks={n:ship.check(o) for n,o in parts.items()}
        assert all(v['overconnected_edges']==v['degenerate_faces']==0 for v in checks.values()),checks
        bm=bmesh.new()
        for name in [family.STERN,family.MIDDLE,family.BOW]:
            o=bpy.data.objects[name+'__Hull_Shell'];m=o.data.copy();m.transform(o.matrix_world);bm.from_mesh(m);bpy.data.meshes.remove(m)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
        joined={'boundary_edges':sum(e.is_boundary for e in bm.edges),'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges)}
        if any(joined.values()):print('JOIN_BOUNDARIES',[[tuple(v.co) for v in e.verts] for e in bm.edges if e.is_boundary],flush=True)
        bm.free();assert not any(joined.values()),joined
        rotor=bpy.data.objects['Rotor'];saved=rotor.rotation_euler.copy();collisions=[]
        shell_tree=family.wheel_study.bvh(parts['Hull_Shell'])
        for a in range(0,360,5):
            rotor.rotation_euler.y=math.radians(a);bpy.context.view_layer.update()
            if shell_tree.overlap(family.wheel_study.bvh(rotor)):collisions.append(a)
        rotor.rotation_euler=saved;assert not collisions,collisions
        if scene_name=='stern-only':
            for name,o in parts.items():ss.export_fbx(o,str(OUT/'models'/(name+'.fbx')))
            (OUT/'manifest.json').write_text(json.dumps({'id':'RaisedStern_'+MODE+'_v1','interface':family.INTERFACE_STANDARD,
                'replacement':family.STERN,'length':L,'deck_height':H,'wheel':'Existing M1 rotor and carrier retained at unchanged axle socket.',
                'units':'V8 authoring units; not calibrated game metres','coordinates':'Blender +X bow, +Y port, +Z up; game (-y,z,x)',
                'files':{n:'models/'+n+'.fbx' for n in parts},'moving_parts':moving,
                'placement':'Static meshes use the original stern module origin. Door and hatch use the recorded hinge pivots.',
                'adjacency':'ForwardGuard is separate. Same-height adjacent modules still require tested upper joining geometry.',
                'sockets':{o.name:list(o.location) for o in stern.children if o.type=='EMPTY'},
                'status':'Offline shape prototype. Crew traversal, recoil, colliders, buoyancy and phone testing not implemented.'},indent=2))
        folder=OUT/scene_name;folder.mkdir(exist_ok=True)
        family.render(folder/'hero.png',(-15,-34,29),(13,0,2),33,(1600,1150))
        family.render(folder/'side.png',(13,-45,2),(13,0,2),31,(1650,850))
        family.save(folder/'ship.blend')
        report['assemblies'][scene_name]={'parts':checks,'joined_hull':joined,'wheel_samples':72,'wheel_hull_intersections':collisions}
        if scene_name=='stern-only':
            keep={stern,*stern.children_recursive}
            for o in bpy.context.scene.objects:
                if o.type=='MESH' and o not in keep:o.hide_render=True
            family.render(OUT/'isolated.png',(-14,-18,14),(4.2,0,1.7),19,(1500,1150))
            family.render(OUT/'access.png',(22,-17,14),(6,0,2.2),18,(1500,1150))
            family.render(OUT/'back.png',(-30,0,2.1),(4,0,2.1),15,(1250,1150))
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print('RAISED STERN PASS',MODE)


if __name__=='__main__':main()
