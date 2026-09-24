"""Full-length raised bow with an internal door-to-hatch access chamber."""
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Matrix

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_raised_bow_v1 as old

fore,family,ship,ss=old.fore,old.family,old.ship,old.ss
MODE=fore.MODE
OUT=HERE.parents[1]/'art-staging/modular-raised-bow-v2'/MODE
OUT.mkdir(parents=True,exist_ok=True)
old.CUT=.04
H=4.20
D=1.76
DOOR_TOP=3.78
Y=.72
HX0,HX1=1.05,2.55
ACCESS_Y=0.0
HATCH_Y=None


def hatch_y():
    return ACCESS_Y if HATCH_Y is None else HATCH_Y


def cut_surface(obj,selector,planes,remove):
    bm=bmesh.new();bm.from_mesh(obj.data)
    for co,no in planes:
        faces=[f for f in bm.faces if selector(f)]
        edges={e for f in faces for e in f.edges};verts={v for f in faces for v in f.verts}
        bmesh.ops.bisect_plane(bm,geom=faces+list(edges)+list(verts),plane_co=co,plane_no=no,dist=1e-6)
    faces=[f for f in bm.faces if selector(f) and remove(f.calc_center_median())]
    assert faces,'Portal not cut'
    bmesh.ops.delete(bm,geom=faces,context='FACES')
    bm.to_mesh(obj.data);bm.free();obj.data.update()


def mesh(name,b,bow):
    obj=ship.mesh('RaisedBowV2__'+name,b);obj.parent=bow
    return obj


def build():
    bow=bpy.data.objects[family.BOW]
    shell=bpy.data.objects[family.BOW+'__Hull_Shell']
    old.raised_shell(shell)
    for name in ['Rails_Teal','Plank_Seams']:
        obj=bpy.data.objects[family.BOW+'__'+name]
        if name=='Rails_Teal':bpy.data.objects.remove(obj,do_unlink=True)
        else:old.trim_forward(obj,True)
    iron=bpy.data.objects[family.BOW+'__Hull_Ironwork']
    # Remove complete obsolete rail-post solids, retaining the lower hull bands.
    bm=bmesh.new();bm.from_mesh(iron.data);todo=set(bm.verts)
    while todo:
        v=todo.pop();group={v};stack=[v]
        while stack:
            for e in stack.pop().link_edges:
                for v in e.verts:
                    if v in todo:todo.remove(v);group.add(v);stack.append(v)
        if min(v.co.z for v in group)>1.7:
            bmesh.ops.delete(bm,geom=list(group),context='VERTS')
    bm.to_mesh(iron.data);bm.free();iron.data.update()
    cut_surface(shell,lambda f:all(abs(v.co.z-H)<1e-4 for v in f.verts),
        [((HX0,0,0),(1,0,0)),((HX1,0,0),(1,0,0)),((0,hatch_y()-Y,0),(0,1,0)),((0,hatch_y()+Y,0),(0,1,0))],
        lambda c:HX0<c.x<HX1 and hatch_y()-Y<c.y<hatch_y()+Y)
    cut_surface(shell,lambda f:all(abs(v.co.x-old.CUT)<1e-4 for v in f.verts) and min(v.co.z for v in f.verts)>D-.01,
        [((0,ACCESS_Y-Y,0),(0,1,0)),((0,ACCESS_Y+Y,0),(0,1,0)),((0,0,DOOR_TOP),(0,0,1))],
        lambda c:ACCESS_Y-Y<c.y<ACCESS_Y+Y and D<c.z<DOOR_TOP)
    interior=ss.Builder()
    polygon=[(old.CUT,D),(HX1,D),(HX1,H),(HX0,H),(HX0,DOOR_TOP),(old.CUT,DOOR_TOP)]
    for s in [-1,1]:ship.cap(interior,[fore.point(x,s*Y,z) for x,z in polygon],'wood2',(s,0,0))
    for pts in [
        [(old.CUT,-Y,D),(HX1,-Y,D),(HX1,Y,D),(old.CUT,Y,D)],
        [(HX1,-Y,D),(HX1,-Y,H),(HX1,Y,H),(HX1,Y,D)],
        [(HX0,-Y,DOOR_TOP),(HX0,-Y,H),(HX0,Y,H),(HX0,Y,DOOR_TOP)],
        [(old.CUT,-Y,DOOR_TOP),(old.CUT,Y,DOOR_TOP),(HX0,Y,DOOR_TOP),(HX0,-Y,DOOR_TOP)]]:
        interior.face([fore.point(*p) for p in pts],'wood2',hint=(0,1,0))
    inside=mesh('InteriorTemporary',interior,bow)
    for v in inside.data.vertices:
        t=max(0,min(1,(v.co.x-old.CUT)/(HX0-old.CUT)))
        v.co.y+=ACCESS_Y+(hatch_y()-ACCESS_Y)*t
    bm=bmesh.new();bm.from_mesh(shell.data);bm.from_mesh(inside.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
    # Portal cuts add collinear points; split incident edges before welding them.
    for _ in range(2):
        for edge in list(bm.edges):
            if not edge.is_boundary:continue
            a,b=edge.verts;vec=b.co-a.co
            if vec.length_squared<1e-10:continue
            candidates=[]
            for v in bm.verts:
                if v in edge.verts or not v.is_boundary:continue
                t=(v.co-a.co).dot(vec)/vec.length_squared
                if 1e-5<t<1-1e-5 and (a.co+vec*t-v.co).length<.00005:candidates.append((t,v.co.copy()))
            if candidates:
                t,co=min(candidates,key=lambda q:q[0])
                _,v=bmesh.utils.edge_split(edge,a,t);v.co=co
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(shell.data);bm.free();bpy.data.objects.remove(inside,do_unlink=True)
    stations=[]
    for original in [family.CUT_B,5,6.7,8.3,9.8,11.2,12.4]:
        x,w,z=fore.hull_point(original);stations.append((max(old.CUT,x)+old.rake(x),w+.28,z))
    def width(x):
        for (a,w,_),(b,v,_) in zip(stations,stations[1:]):
            if x<=b:return w+(v-w)*(x-a)/(b-a)
        return stations[-1][1]
    rails=ss.Builder();posts=ss.Builder();rear=ss.Builder();seams=ss.Builder();trim=ss.Builder()
    gap=(5.0,6.6)
    for s in [-1,1]:
        for a,b in [(old.CUT,gap[0]),(gap[1],stations[-1][0])]:
            xs=[a]+[x for x,_,_ in stations if a<x<b]+[b]
            fore.tube(rails,[(x,s*width(x),H+.70) for x in xs],.28,.25,'teal')
        for x in [old.CUT,2.8,gap[0],gap[1],stations[-2][0]]:
            y=s*width(x)
            fore.box(posts,x-.09,x+.09,y-.09,y+.09,H,H+.61,'wood')
            fore.box(posts,x-.13,x+.13,y-.13,y+.13,H+.59,H+.75,'iron')
        for height in [2.40,3.00,3.60]:
            pts=[]
            for original in [family.CUT_B,5,6.7,8.3,9.8,11.2,12.4]:
                x,w,z=fore.hull_point(original)
                pts.append((max(old.CUT,x)+old.rake(x)*(height-z)/(H-z),s*(w+.29),height))
            fore.tube(seams,pts,.012,.016,'seam')
    for a,b in [(-width(old.CUT),ACCESS_Y-Y-.1),(ACCESS_Y+Y+.1,width(old.CUT))]:
        for height in [2.40,3.00,3.60]:fore.box(seams,.025,.04,a,b,height-.008,height+.008,'seam')
    fore.tube(rear,[(old.CUT,-width(old.CUT),H+.70),(old.CUT,width(old.CUT),H+.70)],.28,.25,'teal')
    for y in [-3,-1.5,0,1.5,3]:fore.box(rear,-.03,.11,y-.07,y+.07,H,H+.60,'teal')
    # Upper planks are split around the actual hatch opening.
    for n in range(-int(width(old.CUT)/.7),int(width(old.CUT)/.7)+1):
        y=n*.7
        if abs(y)+.12>width(old.CUT):continue
        end=stations[-1][0]
        for (a,w,_),(b,v,_) in zip(stations,stations[1:]):
            if v<abs(y)+.12<=w:end=a+(b-a)*(w-abs(y)-.12)/(w-v);break
        intervals=[(.10,end)] if abs(y-hatch_y())>Y+.06 else [(.10,HX0-.07),(HX1+.07,end)]
        for a,b in intervals:
            if b>a:fore.box(seams,a,b,y-.007,y+.007,H+.003,H+.012,'seam')
    for s in [-1,1]:
        fore.box(trim,-.06,.10,s*(Y+.085)-.08,s*(Y+.085)+.08,D,DOOR_TOP+.14,'rim')
    fore.box(trim,-.06,.10,-Y-.16,Y+.16,DOOR_TOP,DOOR_TOP+.16,'rim')
    for y in [-Y-.07,Y+.07]:fore.box(trim,HX0-.12,HX1+.12,y-.07,y+.07,H,H+.16,'rim')
    for x in [HX0-.07,HX1+.07]:fore.box(trim,x-.07,x+.07,-Y,Y,H,H+.16,'rim')
    ladder=ss.Builder()
    for s in [-1,1]:fore.tube(ladder,[(2.0,s*.43,D+.05),(2.40,s*.43,H-.12)],.09,.09,'wood2')
    for i in range(7):
        t=(i+.5)/7
        fore.box(ladder,2.0+.4*t-.06,2.0+.4*t+.06,-.44,.44,D+.1+2.15*t-.04,D+.1+2.15*t+.04,'wood')
    parts={}
    for name,b in [('UpperRails',rails),('UpperPosts',posts),('RearGuard',rear),('PlankDetails',seams),('PortalFrames',trim),('InternalLadder',ladder)]:parts[name]=mesh(name,b,bow)
    for v in parts['PortalFrames'].data.vertices:v.co.y+=ACCESS_Y if v.co.x<.5 else hatch_y()
    parts['InternalLadder'].data.transform(Matrix.Translation((0,hatch_y(),0)))
    door=ss.Builder();fore.box(door,-.09,-.015,-Y+.04,Y-.04,D+.03,DOOR_TOP-.04,'wood2')
    for z in [D+.30,DOOR_TOP-.30]:fore.box(door,-.12,-.09,-Y+.07,Y-.07,z-.05,z+.05,'iron')
    for y in [-.35,0,.35]:fore.box(door,-.102,-.095,y-.008,y+.008,D+.06,DOOR_TOP-.07,'seam')
    door.cyl(fore.point(-.12,.45,2.7),fore.point(-.21,.45,2.7),.065,.065,6,'brass')
    lid=ss.Builder();fore.box(lid,HX0-.03,HX1+.03,-Y-.03,Y+.03,H+.16,H+.26,'wood2')
    for y in [-.48,.48]:fore.box(lid,HX0,HX1,y-.05,y+.05,H+.26,H+.29,'iron')
    moving={}
    for name,b,pivot,axis,angle in [('Door',door,(-.015,-Y,D),'Z',65),('Hatch',lid,(HX1,0,H+.16),'Y',80)]:
        obj=mesh(name,b,bow);obj.data.transform(Matrix.Translation(tuple(-v for v in pivot)))
        placed=(pivot[0],pivot[1]+(ACCESS_Y if name=='Door' else hatch_y()),pivot[2])
        obj.location=placed;obj.rotation_euler['XYZ'.index(axis)]=math.radians(angle)
        parts[name]=obj;moving[name]={'pivot':placed,'axis':axis,'review_angle_degrees':angle,'closed_angle_degrees':0}
    prow=bpy.data.objects[family.BOW+'__Prow']
    for v in prow.data.vertices:v.co.z+=H-ship.deck(ship.ZF);v.co.x+=old.RAKE
    stem=ss.Builder();fore.tube(stem,[(old.TIP,0,ship.deck(ship.ZF)),(stations[-1][0],0,H+.22)],.26,.18,'iron')
    parts['StemExtension']=mesh('StemExtension',stem,bow)
    parts.update({o.name.split('__',1)[1]:o for o in bow.children if o.type=='MESH' and o.name.startswith(family.BOW+'__')})
    # Old low-deck equipment markers are no longer valid on this replacement.
    for obj in list(bow.children):
        if obj.type=='EMPTY' and 'DeckSlot' in obj.name:bpy.data.objects.remove(obj,do_unlink=True)
    for name,pos in [('DoorEntry',(-.40,ACCESS_Y,D)),('HatchExit',(1.75,hatch_y(),H)),('UpperDeckJoin',(0,0,H))]:family.empty(name,pos,bow)
    bow['length']=max(v.co.x for v in prow.data.vertices)
    bpy.data.objects[family.BOW+'__ForwardSocket'].location.x=bow['length']
    return bow,parts,moving


def main():
    report={'family':MODE,'assemblies':{}}
    for assembly in ['short','long']:
        bpy.ops.wm.open_mainfile(filepath=str(fore.SOURCE/assembly/'ship.blend'))
        bow,parts,moving=build();bpy.context.view_layer.update()
        chimney=bpy.data.objects['Chimney']
        chimney.location.x=min(chimney.location.x,bow.location.x-1.65)
        bpy.context.view_layer.update()
        bm=bmesh.new()
        for name in [family.STERN]+([family.MIDDLE] if assembly=='long' else [])+[family.BOW]:
            obj=bpy.data.objects[name+'__Hull_Shell'];copy=obj.data.copy();copy.transform(obj.matrix_world)
            bm.from_mesh(copy);bpy.data.meshes.remove(copy)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
        joined={'boundary_edges':sum(e.is_boundary for e in bm.edges),'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges)}
        bm.free();assert not any(joined.values()),joined
        assert not family.wheel_study.bvh(chimney).overlap(family.wheel_study.bvh(parts['Hull_Shell'])),'Chimney intersects raised hull'
        checks={name:ship.check(obj) for name,obj in parts.items()}
        assert all(c['overconnected_edges']==c['degenerate_faces']==0 for c in checks.values()),checks
        if assembly=='short':
            for name,obj in parts.items():ss.export_fbx(obj,str(OUT/'models'/(name+'.fbx')))
            manifest={'id':'RaisedBowFull_'+MODE+'_v2','interface':family.INTERFACE_STANDARD,'replacement':family.BOW,
                'placement':'same bow-local transform; replaces entire low bow; not an overlay','length':bow['length'],
                'units':'V8 authoring units, not calibrated game metres','coordinates':'Blender +X bow +Y port +Z up; game (-y,z,x)',
                'upper_deck_height':H,'lower_door_height':D,'moving_parts':moving,
                'chimney_rule':'Independent fitting: X=min(original midpoint X, bow origin X - 1.65); Z unchanged. Keeps chimney aft of raised shell.',
                'files':{k:'models/'+k+'.fbx' for k in parts},
                'access':'DoorEntry to HatchExit is a proposed traversal link, not implemented navigation.',
                'adjacent_raised_section':'RearGuard is separate and should be removed when a matching raised module joins. Matching upper interface and bulkhead treatment still need integration.',
                'sockets':{o.name:list(o.location) for o in bow.children if o.type=='EMPTY'}}
            (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
        folder=OUT/assembly;folder.mkdir(exist_ok=True)
        mid=(bow.location.x+bow['length'])/2
        family.render(folder/'hero.png',(mid-26,-34,28),(mid,0,1.8),32,(1600,1150))
        family.render(folder/'side.png',(mid,-45,2),(mid,0,2),30,(1600,850))
        family.save(folder/'ship.blend')
        report['assemblies'][assembly]={'parts':checks,'joined_hull':joined,'chimney_hull_intersections':0,'chimney_position':list(chimney.location),'triangles':sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type=='MESH' and not o.hide_render for p in o.data.polygons)}
        if assembly=='short':
            keep={bow,*bow.children_recursive}
            for obj in bpy.context.scene.objects:
                if obj.type=='MESH' and obj not in keep:obj.hide_render=True
            x=bow.location.x
            family.render(OUT/'isolated.png',(x-11,-17,14),(x+4.5,0,1.8),19,(1550,1200))
            family.render(OUT/'access-detail.png',(x-7,-7,10),(x+1.2,0,3),10,(1300,1100))
            family.render(OUT/'top.png',(x+5,0,40),(x+5,0,0),16,(1500,1100))
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print('FULL RAISED BOW PASS',MODE)


if __name__=='__main__':main()
