"""F27: whole-boat silhouette and colour study, preserving a recessed wheel.

Separate offline Blender process. Source X forward, Y port, Z up; 0.5m/unit.
Imports only the reusable mesh helpers and existing game review character.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, Matrix

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import f_coaster_v1 as kit

ROOT=HERE.parents[1]
OUT=ROOT/'art-staging/f-coaster-v27'
class CraftedMesh(kit.Mesh):
    def ring(self,center,radius,thick,depth,col,n=32):
        x,y,z=center
        rings=[]
        for i in range(n):
            a=i*math.tau/n
            rings.append([(x+r*math.sin(a),y+dy,z+r*math.cos(a))
                          for r,dy in [(radius-thick,-depth/2),(radius,-depth/2),
                                       (radius,depth/2),(radius-thick,depth/2)]])
        for a,b in zip(rings,rings[1:]+rings[:1]):
            for j in range(4):self.face([a[j],a[(j+1)%4],b[(j+1)%4],b[j]],col)

    def face(self,points,col):
        points=list(points)
        # Broad painted facets survive FBX in GameColor. Small deterministic
        # changes avoid both perfectly uniform surfaces and speckled noise.
        if col in PATCH_COLORS:
            center=sum((Vector(p) for p in points),Vector())/len(points)
            value=math.sin(center.x*1.7+center.z*2.1)+.55*math.sin(center.y*1.3-center.x*.8)
            shade=max(0,min(4,round(2+value*1.1)))
            col=col+'_patch_'+str(shade)
        super().face(points,col)

PATCH_COLORS={'wood','woodlight','wooddark','deck','decklight','deckdark','sage','sage2','bottom',
              'plank_a','plank_b','plank_c','plank_d','cream'}
M=CraftedMesh
D=1.76
BASE=D+1.20
RISE=4.40
STERN=9.8
BAY=6.0
BOW=7.1
PLATFORM=7.2
SCALE=.5
H=4.64
LEVEL=1
CANOPY=False
WHEEL={}
SOURCE_CANNONS=[]
SOLIDS=[]


def root(name,x=0):
    o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o)
    o.location.x=x;kit.PARENT=o;return o


def hull_form(point,kind):
    x,y,z=point
    if kind=='stern':
        t=max(0,min(1,x/STERN));weight=(1-t)**2
        lift=1.05*weight;sweep=.50*weight;turn=.80*weight
    elif kind=='bow':
        t=max(0,min(1,x/BOW));weight=t*t
        lift=1.85*weight;sweep=.85*weight;turn=-1.40*weight
    else:return tuple(point)
    if z<=D:
        depth=max(0,min(1,(D-z)/(D+1.92)))
        return (x+turn*depth**1.5,y,z+lift*depth+sweep*(1-depth))
    shoulder=max(0,min(1,(rail_z(min(max(x,0),STERN if kind=='stern' else BOW),kind)-z)/
                            max(.1,rail_z(min(max(x,0),STERN if kind=='stern' else BOW),kind)-D)))
    return (x,y,z+sweep*shoulder)


def save(mesh,name,bevel=.02,solid=True,export=True):
    # Shape all exterior members with one shared section field, preserving
    # matching joins and planar working decks. Cut the wheel cavity afterwards.
    parent=kit.PARENT.name if kit.PARENT else ''
    kind={'Stern':'stern','Bow':'bow','Transom':'stern'}.get(parent)
    if kind and name!='Main_Deck' and not getattr(mesh,'formed',False):
        mesh.v=[hull_form(v,kind) for v in mesh.v]
    o=mesh.object(name,bevel,export)
    if solid:SOLIDS.append(o)
    return o


def loft(mesh,rings,colors,cap=True):
    n=len(rings[0])
    if cap:mesh.face(rings[0][::-1],colors[0]);mesh.face(rings[-1],colors[0])
    for a,b in zip(rings,rings[1:]):
        for i in range(n):mesh.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],colors[i%len(colors)])


def band(mesh,pts,w,t,color,start_join=False):
    # Eight-sided, softly crowned solid timber, with restrained tool-shaped
    # variation along the length. End sections stay exact for module joins.
    rings=[]
    for i,point in enumerate(pts):
        u=i/(len(pts)-1)
        tangent=Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(0,i-1)])
        out=Vector((0,1,0)) if start_join and i==0 else Vector((-tangent.y,tangent.x,0)).normalized()
        wobble=math.sin(math.pi*u)**2*(.025*math.sin(u*19)+.012*math.sin(u*37))
        p=Vector(point);ww=w/2+wobble;hh=t/2
        section=[(-ww*.72,-hh),(ww*.72,-hh),(ww,-hh*.42),(ww,hh*.40),
                 (ww*.68,hh),(-ww*.68,hh),(-ww,hh*.40),(-ww,-hh*.42)]
        rings.append([tuple(p+out*y+Vector((0,0,z+wobble*.5))) for y,z in section])
    loft(mesh,rings,[color]*8)



def timber(mesh,points,width,depth,col,axis=(1,0,0)):
    rings=[];axis=Vector(axis)
    for i,p in enumerate(points):
        tangent=(Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])).normalized()
        u=(axis-tangent*axis.dot(tangent)).normalized();v=tangent.cross(u).normalized()
        section=[(-.36,-.5),(.36,-.5),(.5,-.28),(.5,.28),(.36,.5),(-.36,.5),(-.5,.28),(-.5,-.28)]
        rings.append([tuple(Vector(p)+u*(a*width)+v*(b*depth)) for a,b in section])
    loft(mesh,rings,[col]*8)


def beam_at(x,kind):
    if kind=='stern':return H*(.88+.12*math.sin(min(x/4.6,1)*math.pi/2))
    if kind=='bow':
        t=max(0,min(1,(x-3.65)/(BOW-3.65)))
        # Full shoulders taper into a modest point, with a finite timber nose.
        knots=[(0,1),(.40,.76),(.76,.30),(1,.025)]
        for (a,wa),(b,wb) in zip(knots,knots[1:]):
            if t<=b:return H*(wa+(wb-wa)*(t-a)/(b-a))
        return H*.025
    return H


def rake(x,z,kind):
    if kind!='bow':return x
    t=max(0,min(1,x/BOW));return x+1.05*t**4*((z+1.92)/5.8)**2


def rail_z(x,kind):
    waist=D+1.10
    if kind=='stern':
        top=BASE+LEVEL*RISE
        # The whole stern shoulder flows down into the waist, including the
        # platform run. A short, proud aft crown replaces the long flat box.
        begin=PLATFORM if LEVEL else 2.6
        t=max(0,min(1,(x-begin)/(STERN-begin)))
        s=t*t*(3-2*t)
        return (top+1.35)*(1-s)+waist*s
    if kind=='bow':return waist+1.05*(x/BOW)**2
    return waist


def build_shell(kind,length):
    profile=[(-1.92,.28),(-1.58,.58),(-1.02,.85),(-.34,.98),(.37,1.025),(1.06,1.018),(D,1.0)]
    xs=[length*i/24 for i in range(25)]
    for side in [-1,1]:
        shell=M();rings=[]
        for x in xs:
            w=beam_at(x,kind)
            outer=[(rake(x,z,kind),side*w*f,z) for z,f in profile]
            inner=[(rake(x,z,kind),side*(w*f-.19),z+.015) for z,f in profile]
            rings.append(outer+inner[::-1])
        loft(shell,rings,['bottom','sage2','sage','sage2','sage','sage']+['wooddark']*8)
        save(shell,'Hull_Port' if side>0 else 'Hull_Starboard',0)
    bottom=M();rings=[]
    for x in xs:
        w=beam_at(x,kind)*.28
        rings.append([(rake(x,-1.92,kind),-w,-1.92),(rake(x,-1.92,kind),w,-1.92),
                      (rake(x,-1.7,kind),w,-1.70),(rake(x,-1.7,kind),-w,-1.70)])
    loft(bottom,rings,['bottom']*4);save(bottom,'Keel',0)
    # End face of the base stern is solid until the actual wheel well is cut.
    if kind=='stern':
        back=M();rings=[];w=beam_at(0,'stern')
        for z,f in profile:
            rings.append([(-.02,-w*f,z),(-.02,w*f,z),(.28,w*f,z),(.28,-w*f,z)])
        loft(back,rings,['sage2']*4);save(back,'Lower_Transom',0)
    if kind=='bow':
        stem=M();stem.formed=True
        points=[hull_form((rake(length,z,kind),0,z),kind) for z in
                [-1.75,-1.35,-.9,-.35,.3,1.0,1.7,2.4,rail_z(length,kind)+.12]]
        timber(stem,points,.90,.78,'woodlight',axis=(0,1,0))
        save(stem,'Blunt_Stem',0)
    kit.marker('Join_Aft',(0,0,D),standard=f'F27_{2*H:.2f}')
    kit.marker('Join_Forward',(length,0,D),standard=f'F27_{2*H:.2f}')


def bow_deck_edge(x,z,side,inset=.20):
    # One shared boundary for the deck, inner wall foot and perimeter timber.
    lo=-3.;hi=8.
    for _ in range(40):
        mid=(lo+hi)/2
        point=hull_form((rake(x,mid,'bow'),0,mid),'bow')
        if point[2]<z:lo=mid
        else:hi=mid
    px=hull_form((rake(x,(lo+hi)/2,'bow'),0,(lo+hi)/2),'bow')[0]
    return (px,side*max(.015,beam_at(x,'bow')-inset),z)


def floor(kind,start,end,z,name,omit=None):
    if kind=='stern':return fitted_stern_floor(start,end,z,name)
    mesh=M();count=max(1,math.ceil((end-start)/.62))
    for i in range(count):
        x0=start+(end-start)*i/count+.003
        x1=end if i==count-1 else start+(end-start)*(i+1)/count-.003
        # Sample the rounded bow within each plank. A single chord per plank
        # leaves visible triangular holes next to a strongly curved gunwale.
        subdivisions=16 if kind=='bow' else 2
        rings=[]
        for j in range(subdivisions+1):
            x=x0+(x1-x0)*j/subdivisions;w=max(.015,beam_at(x,kind)-.20)
            zz=z
            def edge(target_z,side):
                if kind!='bow':return (rake(x,target_z,kind),side*w,target_z)
                return bow_deck_edge(x,target_z,side)
            rings.append([edge(zz-.22,-1),edge(zz-.22,1),edge(zz,1),edge(zz,-1)])
        col=['deck','decklight','deck','deck','deckdark'][i%5]
        loft(mesh,rings,[col]*4)
    return save(mesh,name,.012)


def outer_panels(kind,length):
    bow_interiors={}
    xs=sorted(set([length*i/40 for i in range(41)]+([1.975,4.025] if kind=='middle' else [1.8] if kind=='bow' else [])))
    # A single closed wall with shallow grooves on both faces. This avoids
    # layered surfaces crossing each other at the strongly curved bow.
    for side in [-1,1]:
        body=M();cap=M()
        if kind=='bow':
            body.formed=True
            bow_interiors[side]=[]
        spans=[(0,1.975),(4.025,length)] if kind=='middle' else [(0,length)]
        for lo,hi in spans:
            samples=[x for x in xs if lo-1e-6<=x<=hi+1e-6];rings=[]
            for x in samples:
                bottom=D-.03;top=rail_z(x,kind)-.12;mid=(bottom+top)/2
                stations=[(bottom,0),(mid-.009,0),(mid,.022),(mid+.009,0),(top,0)]
                w=beam_at(x,kind);inner=max(.025,w-.29)
                outer=[(rake(x,z,kind),side*(w-inset),z) for z,inset in stations]
                inside=[(rake(x,z,kind),side*(inner+inset),z) for z,inset in stations[::-1]]
                if kind=='bow':
                    outer=[hull_form(p,kind) for p in outer]
                    inside=[hull_form(p,kind) for p in inside]
                    # Solid nose lining keeps the exterior rubbing timber
                    # behind the cabin-side surface as the stem turns inward.
                    inside=[(p[0]-.85*(x/BOW)**8*max(0,min(1,(rail_z(x,kind)-p[2])/(rail_z(x,kind)-D-.35))),p[1],p[2]) for p in inside]
                    # Extend the inner skin below the flat deck. Keep the
                    # exterior's lifted silhouette and the module join intact.
                    deck_z=D+.35 if x>=1.8 else D
                    if x>0:inside[-1]=bow_deck_edge(x,deck_z-.14,side,.29)
                    bow_interiors[side].append(inside[:])
                rings.append(outer+inside)
            colors=['plank_a','wooddark','wooddark','plank_b','woodlight',
                    'plank_b','wooddark','wooddark','plank_a','wooddark']
            loft(body,rings,colors)
        if kind=='middle':
            rings=[]
            for x in [1.975,4.025]:
                y=side*beam_at(x,kind)
                rings.append([(x,y,D),(x,y,D+.20),(x,y-side*.29,D+.20),(x,y-side*.29,D)])
            loft(body,rings,['wood']*4)
        if kind!='stern':
            cap_xs=([x for x in xs if x<length-.065]+[length-.065]) if kind=='bow' else xs
            cap_runs=[[x for x in cap_xs if lo<=x<=hi] for lo,hi in spans] if kind=='middle' else [cap_xs]
            for run in cap_runs:
                band(cap,[(rake(x,rail_z(x,kind),kind),side*beam_at(x,kind),rail_z(x,kind)) for x in run],.78,.49,'woodlight',start_join=kind=='bow')
            save(cap,'Cream_Cap_'+str(side),0)
        save(body,'Bulwark_'+str(side),0)
    if kind=='bow':
        nose=M();z=rail_z(length,kind)
        nose.box((rake(length,z,kind)-.035,0,z+.035),(.90,.94,.50),'woodlight')
        save(nose,'Bow_Cap_Connector',.09)
    if kind=='stern':
        cap=M();w=beam_at(0,kind)
        points=[(x,-beam_at(x,kind),rail_z(x,kind)) for x in xs[::-1]]
        points += [(0,y,rail_z(0,kind)+.22*(1-(y/w)**2)) for y in [-w+2*w*i/24 for i in range(1,25)]]
        points += [(x,beam_at(x,kind),rail_z(x,kind)) for x in xs[1:]]
        band(cap,points,.78,.49,'woodlight');save(cap,'Continuous_Stern_Cap',0)
    wale=M()
    for side in [-1,1]:
        pts=[(rake(x,D-.13,kind),side*(beam_at(x,kind)*1.018+.08),D-.13) for x in xs]
        if kind=='stern':
            w=beam_at(0,kind)*1.018+.08
            pts=[(0,side*y,D-.13) for y in [WHEEL['width_u']/2+.50,w-.30,w]]+pts[1:]
        band(wale,pts,.48,.43,'woodlight',start_join=kind=='bow')
    wale_obj=save(wale,'Solid_Timber_Wale',0)
    if kind=='bow':
        # Trim the rubbing timber against the actual closed interior volume,
        # rather than concealing protruding ends with extra decorative blocks.
        cavity=M();cavity.formed=True;rings=[]
        for left,right in zip(bow_interiors[-1],bow_interiors[1]):
            rings.append([(p[0]+.015,p[1]-.025,p[2]) for p in left]+
                         [(p[0]+.015,p[1]+.025,p[2]) for p in right[::-1]])
        loft(cavity,rings,['wood']*10)
        cutter=save(cavity,'Temporary_Bow_Interior',0,False,False)
        bpy.context.view_layer.update()
        mod=wale_obj.modifiers.new('Fit against inner bow lining','BOOLEAN')
        mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
        bpy.context.view_layer.objects.active=wale_obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(cutter,do_unlink=True)
    frame=M();frame.formed=True;metal=M();metal.formed=True
    stops=([0.0,stern_frame_station(4.35),PLATFORM] if kind=='stern' else [1.35,5.05] if kind=='middle' else [.55,3.65,5.55])
    for side in [-1,1]:
        for index,x in enumerate(stops):
            top=rail_z(x,kind)
            raw=[(rake(x,z,kind),side*(beam_at(x,kind)*f+.10),z)
                 for z,f in [(-1.85,.34),(-1.55,.60),(-1.0,.86),(-.34,.995),(.40,1.04),(D,1.018),(top+.22,1)]]
            if kind=='stern' and index==0:raw=raw[2:]
            pts=[hull_form(p,kind) for p in raw]
            timber(frame,pts,.60 if kind=='stern' else (.59 if index%2 else .66),.42,'woodlight')
            capcenter=hull_form((rake(x,top,kind),side*(beam_at(x,kind)+.10),top+.25),kind)
            if kind!='middle':frame.box(capcenter,(.76,.74,.35),'woodlight')
            # Seat the plate along the final rib, clear of the cap and its bolts.
            pa=Vector(pts[-2]);pb=Vector(pts[-1]);t=(pb-pa).normalized()
            u=Vector((1,0,0));u=(u-t*u.dot(t)).normalized()
            normal=u.cross(t).normalized()
            if normal.y*side<0:normal=-normal
            center=pb-t*.87+normal*.225
            rotation=Matrix((u,normal,t)).transposed()
            metal.box(center,(.46,.09,.94),'iron',rotation)
            for offset in [-.27,.27]:
                c=center+t*offset+normal*.048
                metal.cylinder(c,c+normal*.075,.095,.077,'brass',8)
    save(frame,'Carved_Frames',.018);save(metal,'Forged_Straps',.015)


def stern_interiors():
    top=BASE+LEVEL*RISE
    r=root('Stern_Interior')
    # The lower raised platform is structural, with a central three-step approach.
    floor('stern',0,PLATFORM,BASE,'Lower_Stern_Floor')
    fascia=M();fascia.box((PLATFORM-.15,0,(D+BASE-.22)/2),(.30,2*H-.45,BASE-D-.22),'wood')
    save(fascia,'Platform_Fascia')
    lower_steps=M()
    for i in range(3):lower_steps.box((PLATFORM+1.0-i*.4,0,D+(i+1)*.4-.10),(.43,2.5,.20),'decklight')
    save(lower_steps,'Center_Approach_Steps',.035)
    if LEVEL:
        r=root('Stern_Upper_1');r['rise_u']=RISE
        floor('stern',0,PLATFORM,top,'Upper_Stern_Floor')
        posts=M()
        posts.beam((PLATFORM,-H+.3,top-.28),(PLATFORM,H-.3,top-.28),.46,.44,'woodlight')
        save(posts,'Upper_Storey_Supports',.025)
        # Enclose the wheel only; keep the forward under-deck gun room open.
        housing=M();housing.box((3.82,0,BASE+1.12),(.20,6.15,2.24),'sage')
        save(housing,'Machinery_Bulkhead',.016)
        hatch=M();hatch.box((3.95,0,BASE+1.05),(.14,1.45,1.55),'wooddark')
        for yy in [-.78,.78]:hatch.box((4.04,yy,BASE+1.05),(.16,.16,1.78),'woodlight')
        for zz in [BASE+.2,BASE+1.9]:hatch.box((4.04,0,zz),(.16,1.72,.16),'woodlight')
        hatch.cylinder((4.13,.50,BASE+1.05),(4.23,.50,BASE+1.05),.085,.085,'brass',8)
        save(hatch,'Machinery_Service_Hatch',.01)
        # Two straight flights with a shared full-width turning landing.
        stairs=M();run=2.30;front=PLATFORM+run;midz=BASE+RISE/2
        lanes=[(H-1.50,BASE,1),(H-3.76,midz,-1)]
        for y,z0,direction in lanes:
            count=8
            for i in range(count):
                t=(i+.5)/count
                x=PLATFORM+run*t if direction==1 else front-run*t
                z=z0+(i+1)*RISE/2/count
                stairs.box((x,y,z-.10),(run/count,1.98,.20),'decklight')
            x0=PLATFORM if direction==1 else front;x1=front if direction==1 else PLATFORM
            for yy in [y-1.09,y+1.09]:
                stairs.beam((x0,yy,z0),(x1,yy,z0+RISE/2),.20,.26,'wooddark')
                stairs.beam((x0,yy,z0+1.55),(x1,yy,z0+RISE/2+1.55),.18,.22,'woodlight')
                for t in [0,.5,1]:
                    xx=x0+(x1-x0)*t;zz=z0+RISE/2*t
                    stairs.beam((xx,yy,zz),(xx,yy,zz+1.55),.18,.20,'woodlight')
        yc=H-2.63
        stairs.box((front+1.03,yc,midz-.12),(2.04,4.24,.22),'decklight')
        stairs.beam((front+2.04,yc-2.12,midz+1.55),(front+2.04,yc+2.12,midz+1.55),.20,.23,'woodlight')
        for yy in [yc-2.12,yc,yc+2.12]:stairs.beam((front+2.04,yy,D),(front+2.04,yy,midz+1.55),.24,.24,'woodlight')
        save(stairs,'Turning_Stairs',.015)
        guard=M();stair_y=H-3.76
        for ya,yb in [(-H+.25,stair_y-1.12),(stair_y+1.12,H-.25)]:
            guard.beam((PLATFORM,ya,top+1.45),(PLATFORM,yb,top+1.45),.24,.26,'woodlight')
            for yy in [ya,yb]:guard.beam((PLATFORM,yy,top),(PLATFORM,yy,top+1.45),.23,.23,'woodlight')
        save(guard,'Front_Guard',.02)
        kit.marker('Stair_Entry',(PLATFORM-.5,H-1.5,BASE),width_m=.95)
        kit.marker('Stair_Exit',(PLATFORM-.6,stair_y,top),width_m=.95)
        for side in [-1,1]:kit.marker('Optional_Stern_Gun_Bay',(5.7,side*(H-1.18),BASE),facing='port' if side>0 else 'starboard')
    root('Transom');face=M();w=beam_at(0,'stern');rings=[]
    for i in range(33):
        y=-w+2*w*i/32;zt=rail_z(0,'stern')+.22*(1-(y/w)**2)-.12
        rings.append([(-.05,y,D-.05),(.29,y,D-.05),(.29,y,zt),(-.05,y,zt)])
    loft(face,rings,['wood']*4);save(face,'Transom_Wall',0)


def wheel_parameters():
    top=BASE+LEVEL*RISE
    radius=2.03+LEVEL*1.32
    return dict(radius_u=radius,width_u=2*H-4.20,axle_u=[.12,0,radius-1.70],
                cavity_clearance_u=.25,stern_plane_u=0,tip_u=-1.70,
                nominal_projection_fraction=(radius-.12)/(2*radius),platform_z_u=top)


def boolean_well():
    # Bore the full cylinder through the stern assembly. Removing a real volume
    # is essential: translating the wheel inward would leave it inside decks.
    r=WHEEL['radius_u']+.25;hw=WHEEL['width_u']/2+.27;cx,_,cz=WHEEL['axle_u']
    kit.PARENT=None
    cutter=M();cutter.cylinder((cx,-hw,cz),(cx,hw,cz),r,r,'dark',64)
    ob=cutter.object('TEMP_Wheel_Clearance',0,False)
    bpy.context.view_layer.update()
    affected=[]
    for target in list(SOLIDS):
        if target.name.startswith('Carved_Frames'):
            # The ribs sit outside the well. Avoid booleans on intersecting trim
            # pieces: they can damage unrelated members of the trim assembly.
            continue
        pts=[target.matrix_world@Vector(p) for p in target.bound_box]
        if min(p.x for p in pts)>cx+r or max(p.x for p in pts)<cx-r:continue
        if min(p.y for p in pts)>hw or max(p.y for p in pts)<-hw:continue
        if min(p.z for p in pts)>cz+r or max(p.z for p in pts)<cz-r:continue
        bpy.context.view_layer.objects.active=target
        mod=target.modifiers.new('Recessed_wheel_well','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=ob
        bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        affected.append(target.name)
    bpy.data.objects.remove(ob,do_unlink=True)
    return affected


def wheel_well_and_rotor():
    r=WHEEL['radius_u'];hw=WHEEL['width_u']/2;cx,_,cz=WHEEL['axle_u'];top=cz+r+.60
    root('Wheel_Well')
    liner=M();rad=r+.25;thick=.17
    angles=[math.pi*i/48 for i in range(49)]
    rings=[[(cx+(rad+thick)*math.sin(a),y,cz+(rad+thick)*math.cos(a)) for a in angles]
           for y in [-hw-.44,hw+.44]]
    loft(liner,rings,['sage2']*49)
    lining=save(liner,'Wheel_Well_Lining',0)
    kit.PARENT=None;cutmesh=M();cutmesh.cylinder((cx,-hw-.27,cz),(cx,hw+.27,cz),rad,rad,'dark',96)
    cutter=cutmesh.object('TEMP_Liner_Inner',0,False)
    bpy.context.view_layer.objects.active=lining
    mod=lining.modifiers.new('Hollow_shell','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
    bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
    bm=bmesh.new();bm.from_mesh(lining.data)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                          plane_co=(0,0,-.82),plane_no=(.19,0,1),clear_inner=True,dist=.00001)
    edges=[e for e in bm.edges if e.is_boundary]
    if edges:bmesh.ops.holes_fill(bm,edges=edges,sides=0)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(lining.data);bm.free()
    kit.PARENT=bpy.data.objects['Wheel_Well']
    # Bore an actual axle hole through the lining, rather than hiding the axle
    # inside a solid half-disc. Bearings are annular for the same reason.
    kit.PARENT=None;hole=M();hole.cylinder((cx,-hw-1,cz),(cx,hw+1,cz),.24,.24,'dark',16)
    cut=hole.object('TEMP_Axle_Bore',0,False)
    bpy.context.view_layer.objects.active=lining
    mod=lining.modifiers.new('Axle_bore','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
    bpy.ops.object.modifier_move_up(modifier=mod.name);bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cut,do_unlink=True);kit.PARENT=bpy.data.objects['Wheel_Well']
    frame=M();bearings=M();ymax=hw+.51
    pts=[(-.17,-ymax,-.63),(-.17,-ymax,top-.06)]
    pts += [(-.17,y,top-.06+.55*(1-(y/ymax)**2)) for y in [-ymax+2*ymax*i/24 for i in range(1,25)]]
    pts += [(-.17,ymax,-.63)]
    timber(frame,pts,.46,.40,'woodlight')
    for side in [-1,1]:
        bearings.ring((cx,side*(hw+.475),cz),.43,.20,.55,'iron',16)
        frame.box((-.17,side*ymax,-.63),(.57,.55,.28),'iron')
    save(frame,'Stern_Opening_Frame',.015);save(bearings,'Axle_Bearings',.01)
    root('Paddle_Rotor_Module')
    rotor=M()
    rotor.cylinder((0,-hw-.72,0),(0,hw+.72,0),.19,.19,'iron',12)
    for side in [-1,1]:
        y=side*(hw-.08)
        rotor.ring((0,y,0),r-.015,.43,.46,'woodlight',16)
        rotor.cylinder((0,y-.20,0),(0,y+.20,0),.34,.34,'wooddark',12)
        for i in range(8):
            a=i*math.tau/8
            rotor.beam((.18*math.sin(a),y,.18*math.cos(a)),((r-.17)*math.sin(a),y,(r-.17)*math.cos(a)),.32,.34,'woodlight')
    n=max(10,round(math.tau*r/1.38))
    for i in range(n):
        a=i*math.tau/n;depth=min(.86,r*.42);rr=r-depth/2
        rotor.box((rr*math.sin(a),0,rr*math.cos(a)),(.19,2*hw,depth),['wood','woodlight','wood'][i%3],Matrix.Rotation(a,3,'Y'))
    o=save(rotor,'Paddle_Rotor',.024);o.location=(cx,0,cz);o['spin_axis']='local Y'
    kit.marker('Axle',(cx,0,cz),radius_u=r,width_u=2*hw)


def bore_structural_axle():
    """The long axle also crosses the hull cheek and short timber bearing post."""
    cx,_,cz=WHEEL['axle_u'];hw=WHEEL['width_u']/2
    kit.PARENT=None;mesh=M()
    mesh.cylinder((cx,-hw-1,cz),(cx,hw+1,cz),.255,.255,'dark',24)
    cut=mesh.object('TEMP_Structural_Axle_Bore',0,False)
    for name in ['Lower_Transom','Transom_Wall','Stern_Opening_Frame','Lower_Stern_Floor']:
        o=bpy.data.objects[name];bpy.context.view_layer.objects.active=o
        mod=o.modifiers.new('Structural_Axle_Bore','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
        while o.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cut,do_unlink=True)


def forecastle(bow_x):
    # A low foredeck and flush cargo hatch, not a high terrace and stair block.
    root('Bow_Foredeck',bow_x);z=D+.35
    floor('bow',1.8,BOW,z,'Foredeck_Floor')
    border=M();border.formed=True
    xs=[1.8+(BOW-.08-1.8)*i/64 for i in range(65)]
    pts=[bow_deck_edge(x,z+.025,-1,.36) for x in xs]
    tip=bow_deck_edge(BOW,z+.025,1,.36)
    pts += [(tip[0],0,z+.025)]
    pts += [bow_deck_edge(x,z+.025,1,.36) for x in xs[::-1]]
    band(border,pts,.30,.15,'woodlight')
    save(border,'Foredeck_Perimeter_Timber',0)
    front=M();w=beam_at(1.8,'bow')-.2
    front.box((1.78,0,D+.15),(.25,2*w,.30),'wooddark')
    front.box((1.39,0,D+.08),(.50,2.7,.16),'decklight')
    save(front,'Foredeck_Step',.035)
    hatch=M();hatch.box((3.45,0,z+.025),(1.90,1.72,.05),'wooddark')
    for i in range(5):hatch.box((2.72+i*.365,0,z+.06),(.35,1.62,.07),'deckdark')
    for yy in [-.53,.53]:hatch.box((3.45,yy,z+.11),(1.88,.11,.05),'sage2')
    for yy in [-.45,.45]:hatch.ring((4.0,yy,z+.14),.09,.026,.03,'brass',10)
    save(hatch,'Closed_Foredeck_Hatch',.003)
    kit.marker('Foredeck_Access',(2.0,0,z));kit.marker('Hatch',(3.45,0,z))


def fittings():
    top=BASE+LEVEL*RISE;root('Stern_Fittings')
    mesh=M()
    if CANOPY:
        for x in [.95,3.25]:
            for y in [-1.75,1.75]:
                mesh.beam((x,y,top),(x,y,top+4.35),.29,.29,'woodlight')
                mesh.box((x,y,top+4.38),(.47,.47,.24),'woodlight')
        for y in [-1.78,1.78]:mesh.beam((.75,y,top+4.23),(3.45,y,top+4.23),.28,.30,'woodlight')
        for x in [.82,3.37]:mesh.beam((x,-1.95,top+4.23),(x,1.95,top+4.23),.28,.30,'woodlight')
        # Sculpted canvas: shallow sag, subtly raised ridge, substantial hems.
        canvas=M();rows=[]
        for i in range(7):
            x=.65+2.95*i/6
            rows.append([(x,-2.01+j*4.02/6,top+4.29+.18*math.sin(j*math.pi/6)-.13*math.sin(i*math.pi/6)) for j in range(7)])
        for a,b in zip(rows,rows[1:]):
            for j in range(6):canvas.face([a[j],a[j+1],b[j+1],b[j]],'canvas')
        co=save(canvas,'Canvas_Shade',0,False);mod=co.modifiers.new('Canvas thickness','SOLIDIFY');mod.thickness=.045
    mesh.box((3.35,0,top+.64),(.46,.47,1.28),'wooddark')
    save(mesh,'Helm_Base',.008)
    # A squat metal boot, gently raked stack and a genuinely recessed mouth.
    xx=2.0;cy=H-1.30
    funnel=M()
    def funnel_ring(z,r,oval=1):
        cx=xx-.40*max(0,min(1,(z-.85)/3.55))
        return [(cx+r*math.cos(i*math.tau/12),cy+r*oval*math.sin(i*math.tau/12),top+z) for i in range(12)]
    boot=[funnel_ring(z,r,.88) for z,r in [(0,.64),(.10,.76),(.28,.76),(.72,.63),(.94,.52)]]
    loft(funnel,boot,['sage']*12)
    save(funnel,'Chimney_Rounded_Base',0)
    stack=M()
    profile=[(.80,.51),(1.12,.49),(2.65,.405),(3.4,.40),(3.85,.49),(4.25,.72),(4.40,.74),
             (4.40,.59),(4.22,.56),(3.85,.335),(3.55,.28)]
    rings=[funnel_ring(z,r) for z,r in profile]
    stack.face(rings[0][::-1],'sage2');stack.face(rings[-1],'dark')
    for j,(a,b) in enumerate(zip(rings,rings[1:])):
        col='sage' if j<5 else 'iron' if j<7 else 'dark'
        for i in range(12):stack.face([a[i],a[(i+1)%12],b[(i+1)%12],b[i]],col)
    save(stack,'Chimney_Flared_Stack',0)
    collar=M()
    loft(collar,[funnel_ring(z,r) for z,r in [(3.37,.415),(3.40,.475),(3.65,.475),(3.69,.445)]],['brass']*12)
    save(collar,'Chimney_Brass_Collar',0)
    kit.marker('Chimney_Smoke_Socket',(xx-.4,cy,top+4.41))
    helm=M();helm.ring((0,0,0),.68,.13,.17,'woodlight',16)
    helm.cylinder((0,-.14,0),(0,.14,0),.18,.18,'brass',10)
    o=save(helm,'Helm',.012);o.location=(3.39,0,top+1.46);o.rotation_euler.z=math.pi/2
    for i in range(8):
        spoke=M();a=i*math.tau/8
        spoke.beam((0,0,0),(.88*math.sin(a),0,.88*math.cos(a)),.12,.12,'woodlight')
        o=save(spoke,'Helm_Spoke_'+str(i),.01);o.location=(3.39,0,top+1.46);o.rotation_euler.z=math.pi/2
    kit.marker('Helm_Stand',(4.20,0,top))


def lanterns():
    root('Lantern_Module')
    glass=bpy.data.materials.new('Lantern_Amber_Emission');glass.use_nodes=True
    shader=next(n for n in glass.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(1,.40,.065,1)
    shader.inputs['Emission Color'].default_value=(1,.32,.045,1)
    shader.inputs['Emission Strength'].default_value=3.5
    shader.inputs['Roughness'].default_value=.35
    bpy.context.view_layer.update()
    bowcap=bpy.data.objects['Bow_Upper_Cap_Connector']
    bounds=[bowcap.matrix_world@Vector(v) for v in bowcap.bound_box]
    bow_x=sum(v.x for v in bounds)/8
    bow_z=max(v.z for v in bounds)-.065
    sites=[('Stern_Port',(.6,2.6,BASE+2.7+LEVEL*RISE),(.2,3.85,BASE+1.35+LEVEL*RISE)),
           ('Stern_Starboard',(.6,-2.6,BASE+2.7+LEVEL*RISE),(.2,-3.85,BASE+1.35+LEVEL*RISE)),
           ('Bow',(bow_x-.95,0,bow_z+1.45),(bow_x,0,bow_z))]
    for name,center,anchor in sites:
        x,y,z=center;ax,ay,az=anchor
        frame=M()
        frame.box((ax,ay,az+.08),(.40,.40,.16),'iron')
        points=[(ax,ay,az),(ax,ay,z+.7),(ax*.6+x*.4,ay*.6+y*.4,z+.96),(x,y,z+.96),(x,y,z+.60)]
        for a,b in zip(points,points[1:]):frame.beam(a,b,.13,.13,'iron')
        frame.box((x,y,z-.48),(.72,.72,.14),'iron')
        frame.box((x,y,z+.46),(.78,.78,.14),'iron')
        frame.cylinder((x,y,z+.51),(x,y,z+.72),.43,.15,'iron',4)
        for dx in [-.29,.29]:
            for dy in [-.29,.29]:frame.beam((x+dx,y+dy,z-.42),(x+dx,y+dy,z+.42),.095,.095,'iron')
        save(frame,'Lantern_'+name+'_Frame',.014)
        core=M();core.box((x,y,z),(.49,.49,.76),'amber')
        ob=save(core,'Lantern_'+name+'_Glass',.025)
        ob.data.materials.clear();ob.data.materials.append(glass)
        for face in ob.data.polygons:face.material_index=0
        ob.visible_shadow=False
        light=bpy.data.lights.new('Lantern_'+name+'_Light','POINT');light.energy=150
        light.color=(1,.46,.16);light.shadow_soft_size=.32
        obj=bpy.data.objects.new(light.name,light);bpy.context.scene.collection.objects.link(obj)
        obj.parent=kit.PARENT;obj.location=(x,y,z-.60)
        kit.marker('Lantern_'+name+'_Socket',center)


def cargo_and_cannons():
    # Load the actual existing cannon asset, with a 1.35x presentation scale for this study.
    kit.PARENT=None
    for side in [-1,1]:
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(ROOT/'art-staging/cannon-astra-v1/cannon.fbx'))
        imported=list(set(bpy.data.objects)-before)
        gunroot=next(o for o in imported if o.type=='EMPTY' and o.parent is None)
        gunroot.name='Review_Cannon_Port' if side>0 else 'Review_Cannon_Starboard'
        gunroot.location=(STERN+3,side*(H-1.18),D)
        gunroot.rotation_euler.z=math.pi if side>0 else 0
        gunroot.scale *= 1.35
        for o in imported:
            o['review_only']=True
            if o.type=='MESH':
                # Import uses the existing vertex colors, not a new painted proxy.
                attr=o.data.color_attributes.active_color
                mat=bpy.data.materials.get('Review_Cannon_Vertex')
                if mat is None:
                    mat=bpy.data.materials.new('Review_Cannon_Vertex');mat.use_nodes=True
                    p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
                    p.inputs['Roughness'].default_value=.75
                    vc=mat.node_tree.nodes.new('ShaderNodeVertexColor');vc.layer_name=attr.name if attr else 'Col'
                    mat.node_tree.links.new(vc.outputs['Color'],p.inputs['Base Color'])
                o.data.materials.clear();o.data.materials.append(mat)
        SOURCE_CANNONS.append(gunroot.name)


def setup():
    kit.PALETTE.update({'wood':(.39,.175,.060,1),'woodlight':(.58,.305,.105,1),'wooddark':(.235,.09,.027,1),
        'deck':(.47,.265,.115,1),'decklight':(.53,.315,.145,1),'deckdark':(.39,.205,.081,1),
        'sage':(.030,.064,.086,1),'sage2':(.025,.053,.071,1),'bottom':(.019,.037,.051,1),
        'cream':(.59,.32,.12,1),'iron':(.043,.052,.059,1),'brass':(.32,.25,.13,1),
        'canvas':(.75,.66,.47,1),
        'plank_a':(.39,.175,.060,1),'plank_b':(.44,.210,.073,1),
        'plank_c':(.42,.19,.061,1),'plank_d':(.365,.15,.047,1)})
    for name in PATCH_COLORS:
        c=kit.PALETTE[name]
        for i,factor in enumerate([.90,.95,1.0,1.045,1.09]):
            kit.PALETTE[name+'_patch_'+str(i)]=tuple(v*factor for v in c[:3])+(1,)
    kit.setup()
    scene=bpy.context.scene;scene.cycles.samples=32
    scene.view_settings.look='AgX - Medium High Contrast'
    ground=bpy.data.objects['Studio_Ground'].data.materials[0]
    node=next(n for n in ground.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(.31,.35,.34,1)
    next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=.45
    bpy.data.lights['Key'].energy=3000;bpy.data.lights['Key'].color=(1.0,.91,.78)
    bpy.data.lights['Fill'].energy=1500
    kit.STERN=STERN;kit.BOW=BOW


def stern_round_shift(x,y):
    # Wider flat rear and shorter shoulders, following the red-marked
    # transition area on the user reference.
    flat_half=WHEEL['width_u']*.365
    rear_half=beam_at(0,'stern')
    shoulder_mid=rear_half*.70
    a=abs(y)
    if a<=flat_half:rear=0.0
    elif a<=shoulder_mid:rear=.65*(a-flat_half)/(shoulder_mid-flat_half)
    else:rear=.65+(1.85-.65)*min(1,(a-shoulder_mid)/(rear_half-shoulder_mid))
    t=max(0,min(1,x/6.0))
    return rear*(1-3*t*t+2*t*t*t)


def stern_frame_station(target_x):
    # Place the upper posts in finished coordinates around the gun bay;
    # the lower hull ribs use the same stations through the hull shaping.
    lo=0.;hi=STERN
    for _ in range(40):
        mid=(lo+hi)/2
        if mid+stern_round_shift(mid,beam_at(mid,'stern'))<target_x:lo=mid
        else:hi=mid
    return (lo+hi)/2


def round_stern_meshes(objects):
    for ob in objects:
        if ob.name.startswith(('Main_Deck','Lower_Stern_Floor','Upper_Stern_Floor','Platform_Fascia','Center_Approach_Steps','Forward_Bulkhead','Portal_Frame','Door_Open','Port_Stairs','Front_Guard','Machinery_Service_Hatch','Upper_Storey_Supports','Machinery_Bulkhead','Turning_Stairs','Helm_Access_Steps')):continue
        bm=bmesh.new();bm.from_mesh(ob.data)
        edges=[e for e in bm.edges if abs(e.verts[0].co.y-e.verts[1].co.y)>.65 and min(v.co.x for v in e.verts)<5.1]
        if ob.name=='Stern_Opening_Frame':edges=[e for e in bm.edges if e.calc_length()>.30]
        if edges:bmesh.ops.subdivide_edges(bm,edges=edges,cuts=7,use_grid_fill=True)
        for v in bm.verts:
            weight=1.0
            if ob.name=='Stern_Opening_Frame':
                # Keep the bearing seat and axle bore fixed while the upper
                # arch follows the curved rear wall.
                t=max(0,min(1,(v.co.z-(WHEEL['axle_u'][2]+.47))/1.3));weight=t*t*(3-2*t)
            v.co.x+=stern_round_shift(v.co.x,v.co.y)*weight
        bmesh.ops.triangulate(bm,faces=list(bm.faces))
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free();ob.data.update()


def stern_skin_path():
    half=WHEEL['width_u']*.365;rear_half=beam_at(0,'stern');mid=rear_half*.70
    side_x=[STERN*i/40 for i in range(41)]
    rear_y=sorted({round(y,6) for y in [-rear_half+2*rear_half*i/64 for i in range(65)]+[-mid,-half,half,mid]})
    raw=[(x,-beam_at(x,'stern')) for x in side_x[::-1]]+[(0,y) for y in rear_y[1:]]+[(x,beam_at(x,'stern')) for x in side_x[1:]]
    xy=[Vector((x+stern_round_shift(x,y),y,0)) for x,y in raw]
    normals=[]
    for i,c in enumerate(xy):
        before=(c-xy[max(0,i-1)]).normalized() if i else (xy[1]-c).normalized()
        after=(xy[min(len(xy)-1,i+1)]-c).normalized() if i<len(xy)-1 else before
        n1=Vector((-before.y,before.x,0));n2=Vector((-after.y,after.x,0))
        n=(n1+n2).normalized();n/=max(.55,n.dot(n1));normals.append(n)
    return raw,xy,normals


def fitted_stern_floor(start,end,z,name):
    # Straight transverse boards clipped to the final wall outline. A small
    # buried overlap closes the seam without exposing boards outside the hull.
    raw,xy,normals=stern_skin_path()
    # Remove redundant centreline samples before offsetting: offsetting tiny
    # segments beside a hard corner can fold the inner contour back on itself.
    path=list(xy)
    changed=True
    while changed:
        changed=False
        for j in range(1,len(path)-1):
            a,b,c=path[j-1:j+2];u=b-a;v=c-b
            if u.cross(v).length/max(1e-8,u.length+v.length)<1e-5 and u.dot(v)>=0:
                path.pop(j);changed=True;break
    outline=[]
    for j,c in enumerate(path):
        before=(c-path[j-1]).normalized() if j else (path[1]-c).normalized()
        after=(path[j+1]-c).normalized() if j<len(path)-1 else before
        n1=Vector((-before.y,before.x,0));n2=Vector((-after.y,after.x,0))
        n=(n1+n2).normalized();n/=max(.55,n.dot(n1))
        outline.append(tuple(c-n*.24))
    def clip(poly,bound,keep_greater):
        result=[]
        for a,b in zip(poly,poly[1:]+poly[:1]):
            ia=(a[0]>=bound) if keep_greater else (a[0]<=bound)
            ib=(b[0]>=bound) if keep_greater else (b[0]<=bound)
            if ia:result.append(a)
            if ia!=ib:
                t=(bound-a[0])/(b[0]-a[0])
                result.append((bound,a[1]+t*(b[1]-a[1]),0))
        clean=[]
        for v in result:
            if not clean or (Vector(v)-Vector(clean[-1])).length>1e-7:clean.append(v)
        if len(clean)>1 and (Vector(clean[0])-Vector(clean[-1])).length<1e-7:clean.pop()
        changed=True
        while changed and len(clean)>3:
            changed=False
            for j,b in enumerate(clean):
                a=Vector(clean[j-1]);v=Vector(b);c=Vector(clean[(j+1)%len(clean)])
                ab=v-a;bc=c-v
                if ab.length<1e-4 or bc.length<1e-4 or (ab.cross(bc).length<1e-6 and ab.dot(bc)>=0):
                    clean.pop(j);changed=True;break
        return clean
    mesh=M();mesh.formed=True
    count=max(1,math.ceil((end-start)/.62))
    for i in range(count):
        x0=start+(end-start)*i/count+(0 if i==0 else .003)
        x1=end if i==count-1 else start+(end-start)*(i+1)/count-.003
        poly=clip(clip(outline,x0,True),x1,False)
        if len(poly)<3:continue
        color=['deck','decklight','deck','deck','deckdark'][i%5]
        bottom=[(p[0],p[1],z-.22) for p in poly]
        top=[(p[0],p[1],z) for p in poly]
        mesh.face(bottom[::-1],color);mesh.face(top,color)
        for j in range(len(poly)):
            k=(j+1)%len(poly);mesh.face([bottom[j],bottom[k],top[k],top[j]],color)
    ob=save(mesh,name,0)
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(ob.data);bm.free()
    return ob


def rebuild_stern_skin():
    global SOLIDS
    names={'Transom_Wall','Continuous_Stern_Cap'}
    for ob in list(kit.PARTS):
        if ob.name in names or (ob.parent and ob.parent.name=='Stern' and ob.name.startswith(('Bulwark_','Solid_Timber_Wale'))):
            kit.PARTS.remove(ob)
            if ob in SOLIDS:SOLIDS.remove(ob)
            bpy.data.objects.remove(ob,do_unlink=True)
    kit.PARENT=bpy.data.objects['Stern']
    rear_half=beam_at(0,'stern');side_x=[STERN*i/40 for i in range(41)]
    raw,xy,normals=stern_skin_path()
    wall=M();wall.formed=True;rings=[];caps=[]
    for (x,y),c,n in zip(raw,xy,normals):
        crown=.22*(1-(y/rear_half)**2) if x==0 else 0
        top=rail_z(x,'stern')+crown-.12
        bottom=hull_form((x,y,D-.03),'stern')[2]
        # Keep the exact top half of the wheel clearance below this skin.
        cx,_,cz=WHEEL['axle_u'];rad=WHEEL['radius_u']+.25
        if abs(y)<WHEEL['width_u']/2+(.65 if LEVEL else .27) and abs(c.x-cx)<rad:
            bottom=max(bottom,cz+math.sqrt(max(0,rad*rad-(c.x-cx)**2)))
        midz=(bottom+top)/2
        stations=[(bottom,0),(midz-.009,0),(midz,.022),(midz+.009,0),(top,0)]
        outer=[tuple(c-n*inset+Vector((0,0,z))) for z,inset in stations]
        inner=[tuple(c-n*(.29-inset)+Vector((0,0,z))) for z,inset in stations[::-1]]
        rings.append(outer+inner);caps.append(tuple(c+Vector((0,0,top+.12))))
    loft(wall,rings,['sage','sage2','sage2','sage','woodlight','plank_b','wooddark','wooddark','plank_a','wooddark'])
    wo=save(wall,'Transom_Wall',0)
    bm=bmesh.new();bm.from_mesh(wo.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(wo.data);bm.free()
    if LEVEL:
        # The raised axle now crosses the new wall skin, not only the lower
        # hull. Bore the finished skin after its contour has been rebuilt.
        kit.PARENT=None;hole=M();cx,_,cz=WHEEL['axle_u'];hw=WHEEL['width_u']/2
        hole.cylinder((cx,-hw-1,cz),(cx,hw+1,cz),.28,.28,'dark',32)
        cut=hole.object('TEMP_Upper_Axle_Clearance',0,False)
        bpy.context.view_layer.update();bpy.context.view_layer.objects.active=wo
        mod=wo.modifiers.new('Upper_axle_clearance','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
        bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cut,do_unlink=True)
    if LEVEL:
        kit.PARENT=None;ports=M()
        for side in [-1,1]:ports.box((5.7,side*H,BASE+1.25),(2.05,1.8,1.80),'dark')
        cut=ports.object('TEMP_Gun_Ports',0,False);bpy.context.view_layer.update()
        bpy.context.view_layer.objects.active=wo
        mod=wo.modifiers.new('Stern_gun_ports','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
        bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cut,do_unlink=True)
    kit.PARENT=bpy.data.objects['Stern']
    cap=M();cap.formed=True;band(cap,caps,.78,.49,'woodlight');save(cap,'Continuous_Stern_Cap',0)
    wale=M();wale.formed=True
    for side in [-1,1]:
        coords=[(x,side*beam_at(x,'stern')) for x in side_x[::-1]]
        stop=WHEEL['width_u']/2+.51
        coords += [(0,side*(rear_half-(rear_half-stop)*i/16)) for i in range(1,17)]
        pts=[]
        for x,y in coords:
            z=hull_form((x,y,D-.13),'stern')[2]
            pts.append((x+stern_round_shift(x,y),y+side*(beam_at(x,'stern')*.018+.08),z))
        band(wale,pts,.48,.43,'woodlight')
    save(wale,'Solid_Timber_Wale',0)
    if LEVEL:
        kit.PARENT=bpy.data.objects['Stern_Upper_1']
        beam=M();beam.formed=True
        # Follow the same finished rear perimeter, stopping at the deck's
        # front rather than continuing down the main-deck shoulder.
        pts=[tuple(c+n*.10+Vector((0,0,BASE+RISE-.30))) for (x,y),c,n in zip(raw,xy,normals) if c.x<=PLATFORM+.02]
        pts.insert(0,(PLATFORM,-H-.10,BASE+RISE-.30));pts.append((PLATFORM,H+.10,BASE+RISE-.30))
        band(beam,pts,.40,.38,'woodlight');save(beam,'Upper_Deck_Rim_Beam',0)


def restore_upper_timber_band():
    ob=bpy.data.objects['Transom_Wall'];height=BASE+LEVEL*RISE-.30
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                          plane_co=(0,0,height),plane_no=(0,0,1),dist=.000001)
    bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    material=kit.MATERIALS['plank_b'];index=list(ob.data.materials).index(material)
    colors=ob.data.color_attributes['GameColor']
    for face in ob.data.polygons:
        mat=ob.data.materials[face.material_index]
        if mat.name.startswith('F_sage') and min(ob.data.vertices[i].co.z for i in face.vertices)>=height-.00001:
            face.material_index=index
            for loop in face.loop_indices:colors.data[loop].color=kit.PALETTE['plank_b']
    ob.data.update()


def cannon_port_details():
    for side,label,xpos,zmid,gun_name in [(side,label,5.7,BASE+1.25,'Review_Lower_Review_Cannon_'+label) for side,label in [(1,'Port'),(-1,'Starboard')]] + [(side,'Bow_'+label,STERN+BAY+2.10,D+.35+1.15,'Review_Bow_Lower_'+label) for side,label in [(1,'Port'),(-1,'Starboard')]]:
        root('Cannon_Port_Frame_'+label)
        frame=M();x=xpos;y=side*(H+.10);z=zmid
        for xx in [x-1.10,x+1.10]:frame.box((xx,y,z),(.18,.24,2.14),'woodlight')
        for zz in [z-1.00,z+1.00]:frame.box((x,y,zz),(2.06,.24,.18),'woodlight')
        for xx in [x-.70,x+.70]:frame.cylinder((xx-.15,y,z+1.02),(xx+.15,y,z+1.02),.085,.085,'iron',10)
        save(frame,'Cannon_Port_Trim_'+label,.015)
        hinge=root('Cannon_Port_Cover_'+label,xpos);hinge.location.y=side*(H+.29);hinge.location.z=z+.98
        hinge['cannon_present']=True;hinge['closed_angle_degrees']=0;hinge['open_angle_degrees']=side*110
        lid=M();lid.box((0,0,-.96),(2.13,.13,1.92),'sage')
        # Applied timber battens sit proud of the cover; no coplanar border faces.
        for xx in [-.99,.99]:lid.box((xx,side*.095,-.96),(.15,.075,1.92),'woodlight')
        for zz in [-.075,-1.845]:lid.box((0,side*.095,zz),(1.90,.075,.15),'woodlight')
        for xx in [-.70,.70]:lid.box((xx,side*.14,-.30),(.13,.065,.54),'iron')
        ob=save(lid,'Hinged_Port_Cover_'+label,.01)
        driver=ob.driver_add('rotation_euler',0).driver
        variable=driver.variables.new();variable.name='occupied';variable.targets[0].id=hinge;variable.targets[0].data_path='["cannon_present"]'
        driver.expression=str(side*math.radians(110))+' * occupied'
        gun=bpy.data.objects[gun_name]
        for child in gun.children_recursive:
            if child.type!='MESH':continue
            for prop in ['hide_render','hide_viewport']:
                d=child.driver_add(prop).driver;v=d.variables.new();v.name='occupied';v.targets[0].id=hinge;v.targets[0].data_path='["cannon_present"]';d.expression='not occupied'


def middle_storey():
    # One low middle bay; access to the raised bow stays inside the bow module.
    root('Bow_Access_Stairs',STERN+BAY)
    stairs=M();run=4.10;foot=D;rise=RISE;count=14
    for i in range(count):
        xx=(i+.5)*run/count;zz=foot+(i+1)*rise/count
        stairs.box((xx,0,zz-.10),(run/count,2.16,.20),'decklight')
    for yy in [-1.19,1.19]:
        stairs.beam((0,yy,foot),(run,yy,foot+rise),.23,.28,'wooddark')
        stairs.beam((0,yy,foot+1.45),(run,yy,foot+rise+1.45),.20,.23,'woodlight')
        for t in [0,.5,1]:
            xx=run*t;zz=foot+rise*t
            stairs.beam((xx,yy,zz),(xx,yy,zz+1.45),.22,.22,'woodlight')
    save(stairs,'Low_Deck_To_Bow_Stairs',.015)
    ob=bpy.data.objects.get('Closed_Foredeck_Hatch')
    if ob:
        kit.PARTS.remove(ob);SOLIDS.remove(ob);bpy.data.objects.remove(ob,do_unlink=True)


def bow_upper_floor(outline,start,end,z,name):
    def clip(poly,bound,keep_greater,axis=0):
        result=[]
        for a,b in zip(poly,poly[1:]+poly[:1]):
            ia=(a[axis]>=bound) if keep_greater else (a[axis]<=bound)
            ib=(b[axis]>=bound) if keep_greater else (b[axis]<=bound)
            if ia:result.append(a)
            if ia!=ib:
                t=(bound-a[axis])/(b[axis]-a[axis])
                point=[a[k]+t*(b[k]-a[k]) for k in range(3)];point[axis]=bound;result.append(tuple(point))
        clean=[]
        for v in result:
            if not clean or (Vector(v)-Vector(clean[-1])).length>1e-7:clean.append(v)
        if len(clean)>1 and (Vector(clean[0])-Vector(clean[-1])).length<1e-7:clean.pop()
        changed=True
        while changed and len(clean)>3:
            changed=False
            for j,b in enumerate(clean):
                a=Vector(clean[j-1]);v=Vector(b);c=Vector(clean[(j+1)%len(clean)])
                ab=v-a;bc=c-v
                if ab.length<1e-4 or bc.length<1e-4 or (ab.cross(bc).length<1e-6 and ab.dot(bc)>=0):
                    clean.pop(j);changed=True;break
        return clean
    mesh=M();mesh.formed=True
    bounds=sorted(set([start+(end-start)*i/max(1,math.ceil((end-start)/.62)) for i in range(max(1,math.ceil((end-start)/.62))+1)]+[4.10]))
    count=len(bounds)-1
    for i in range(count):
        x0=bounds[i]+(0 if i==0 else .003)
        x1=end if i==count-1 else bounds[i+1]-.003
        poly=clip(clip(outline,x0,True),x1,False)
        polys=[poly]
        for poly in polys:
            if len(poly)<3:continue
            color=['deck','decklight','deck','deck','deckdark'][i%5]
            bottom=[(p[0],p[1],z-.22) for p in poly]
            top=[(p[0],p[1],z) for p in poly]
            mesh.face(bottom[::-1],color);mesh.face(top,color)
            for j in range(len(poly)):
                k=(j+1)%len(poly);mesh.face([bottom[j],bottom[k],top[k],top[j]],color)
    ob=save(mesh,name,0)
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(ob.data);bm.free()
    return ob


def bow_storey():
    z=D+RISE
    # The middle front guard is replaced by a continuous upper deck and
    # guarded stair opening in the nose module.
    ob=bpy.data.objects.get('Middle_Front_Guard')
    if ob:
        kit.PARTS.remove(ob);SOLIDS.remove(ob);bpy.data.objects.remove(ob,do_unlink=True)
    ro=root('Bow_Upper_1',STERN+BAY);ro['rise_u']=RISE
    xs=sorted(set([BOW*i/64 for i in range(65)]+[.55,3.65,5.55]))
    raw=[(x,-1) for x in xs]+[(x,1) for x in xs[::-1]]
    points=[]
    for x,side in raw:
        oldtop=rail_z(x,'bow')
        points.append(Vector(hull_form((rake(x,oldtop,'bow'),side*beam_at(x,'bow'),oldtop),'bow')))
    normals=[]
    for i,c in enumerate(points):
        a=(c-points[i-1]) if i else points[1]-c
        b=(points[i+1]-c) if i<len(points)-1 else a
        a.z=0;b.z=0;a.normalize();b.normalize()
        na=Vector((a.y,-a.x,0));nb=Vector((b.y,-b.x,0))
        n=(na+nb).normalized();n/=max(.55,n.dot(na));normals.append(n)
    wall=M();wall.formed=True;rings=[];rim=[];cap=[]
    for (x,side),c,n in zip(raw,points,normals):
        top=z+1.10+.55*(x/BOW)**2
        bottom=c.z-.20
        rings.append([tuple(c+Vector((0,0,bottom-c.z))),tuple(c+Vector((0,0,z-.25-c.z))),
                      tuple(c+Vector((0,0,top-.12-c.z))),tuple(c-n*.29+Vector((0,0,top-.12-c.z))),
                      tuple(c-n*.29+Vector((0,0,z-.25-c.z))),tuple(c-n*.29+Vector((0,0,bottom-c.z)))])
        rim.append((c.x+n.x*.08,c.y+n.y*.08,z-.25));cap.append((c.x,c.y,top))
    loft(wall,rings,['sage','wood','woodlight','wood','wood','wooddark'])
    save(wall,'Bow_Upper_Panels',0)
    beams=M();beams.formed=True
    band(beams,rim,.40,.38,'woodlight');save(beams,'Bow_Upper_Deck_Beam',0)
    rails=M();rails.formed=True;band(rails,cap,.78,.49,'woodlight');save(rails,'Bow_Upper_Cap',0)
    outline=[tuple(Vector((c.x,c.y,0))-n*.23) for c,n in zip(points,normals)]
    bow_upper_floor(outline,0,max(v[0] for v in outline),z,'Bow_Upper_Floor')
    frames=M();frames.formed=True
    for x in [.55,3.65,5.55]:
        for side in [-1,1]:
            i=raw.index((x,side));c=points[i];n=normals[i]
            frames.beam((c.x+n.x*.12,c.y+n.y*.12,c.z-.12),(c.x+n.x*.12,c.y+n.y*.12,z+1.30+.55*(x/BOW)**2),.59,.42,'woodlight')
    save(frames,'Bow_Upper_Posts',.015)
    guard=M()
    for side in [-1,1]:
        y=side*1.38
        guard.beam((0,y,z+1.35),(4.12,y,z+1.35),.20,.23,'woodlight')
        for x in [0,2.1,4.12]:guard.beam((x,y,z),(x,y,z+1.35),.20,.22,'woodlight')
    # Guard the aft drop and leave the top stair landing open toward the nose.
    guard.beam((0,-H+.25,z+1.35),(0,H-.25,z+1.35),.22,.24,'woodlight')
    for yy in [-H+.25,H-.25]:guard.beam((0,yy,z),(0,yy,z+1.35),.24,.24,'woodlight')
    save(guard,'Bow_Stairwell_Guard',.015)
    portal=M();portal.formed=True
    for side in [-1,1]:
        y=side*(H+1.40)/2
        portal.box((.12,y,(D+z-.25)/2),(.24,H-1.40,z-.25-D),'sage')
        for yy in [side*1.40,side*(H-.12)]:
            portal.beam((.0,yy,D),(.0,yy,z),.32,.34,'woodlight')
    portal.beam((.0,-H,z-.20),(.0,H,z-.20),.36,.38,'woodlight')
    save(portal,'Bow_Aft_Framed_Face',.01)
    tip=points[len(xs)-1];nose=M();nose.formed=True
    nose.box((tip.x,0,z+1.10+.55),(.74,.74,.26),'woodlight')
    save(nose,'Bow_Upper_Cap_Connector',.02)
    stem=M();stem.formed=True
    timber(stem,[(tip.x+.06,0,tip.z-.25),(tip.x+.10,0,z-.25),
                 (tip.x+.16,0,z+1.10+.55)],.66,.55,'woodlight',axis=(0,1,0))
    save(stem,'Bow_Upper_Stem',0)
    kit.marker('Bow_Upper_Access',(4.5,0,z),stair_opening_width_m=1.26)


def seat_stern_fittings():
    for ob in bpy.context.scene.objects:
        if ob.name.startswith('Lantern_Stern_'):
            ob.location.x+=stern_round_shift(.2,3.85)
        elif ob.type=='MESH' and ob.name.startswith('Chimney_'):
            ob.location.x+=(stern_round_shift(2.0,H-1.30)+.30)
        elif ob.name=='Chimney_Smoke_Socket':
            ob.location.x+=(stern_round_shift(2.0,H-1.30)+.30)


def compact_access():
    # Exposed ends are complete bulkheads. Ladders climb outside their faces.
    for name in ['Stair_Entry','Stair_Exit','Bow_Upper_Access']:
        ob=bpy.data.objects.get(name)
        if ob:
            if ob in kit.MARKERS:kit.MARKERS.remove(ob)
            bpy.data.objects.remove(ob,do_unlink=True)
    remove=['Turning_Stairs','Center_Approach_Steps','Front_Guard','Low_Deck_To_Bow_Stairs',
            'Bow_Stairwell_Guard','Bow_Aft_Framed_Face','Foredeck_Step']
    for name in remove:
        ob=bpy.data.objects.get(name)
        if ob:
            if ob in kit.PARTS:kit.PARTS.remove(ob)
            if ob in SOLIDS:SOLIDS.remove(ob)
            bpy.data.objects.remove(ob,do_unlink=True)
    for label,x,top,out in [('Stern',PLATFORM,BASE+RISE,1),('Bow',STERN+BAY,D+RISE,-1)]:
        root(label+'_Exposed_End',x)
        wall=M();wall.formed=True
        # Separate colour bands keep the navy below the upper timber beam.
        wall.box((-out*.12,0,(D+top-.24)/2),(.24,2*H-.28,top-.24-D),'sage')
        save(wall,label+'_Closed_End_Panels',.01)
        frame=M();frame.formed=True
        for yy in [-H+.14,-2.30,2.30,H-.14]:
            frame.beam((out*.035,yy,D),(out*.035,yy,top-.24),.34,.32,'woodlight')
        for zz in [D+.13,top-.30]:
            frame.beam((out*.035,-H+.04,zz),(out*.035,H-.04,zz),.36,.32,'woodlight')
        save(frame,label+'_End_Timber_Frame',.015)
        guard=M();guard.formed=True
        for ya,yb in [(-H+.20,-.85),(.85,H-.20)]:
            guard.beam((0,ya,top+1.25),(0,yb,top+1.25),.24,.26,'woodlight')
            for yy in [ya,yb]:guard.beam((0,yy,top),(0,yy,top+1.25),.23,.24,'woodlight')
        save(guard,label+'_Ladder_Landing_Guard',.015)
        root(label+'_Access_Ladder',x)
        ladder=M();ladder.formed=True
        bottom=D+.04;count=math.ceil((top-bottom)/.48)
        for yy in [-.65,.65]:
            ladder.beam((out*.73,yy,bottom),(out*.40,yy,top+.90),.17,.20,'wooddark')
            ladder.beam((out*.40,yy,top+.90),(-out*.16,yy,top+.90),.17,.20,'wooddark')
        for i in range(count):
            zz=D+.35+i*(top-D-.46)/(count-1)
            xx=out*(.73-(zz-bottom)/(top+.9-bottom)*.33)
            ladder.cylinder((xx,-.65,zz),(xx,.65,zz),.075,.075,'iron',8)
        save(ladder,label+'_Ladder',.012)
        brackets=M();brackets.formed=True
        for zz in [D+.65,top-.50]:
            for yy in [-.65,.65]:
                xx=out*(.73-(zz-bottom)/(top+.9-bottom)*.33)
                brackets.beam((0,yy,zz),(xx,yy,zz),.12,.14,'iron')
        save(brackets,label+'_Ladder_Brackets',.01)
        kit.marker(label+'_Climb_Bottom',(out*.95,0,D),width_m=.65)
        kit.marker(label+'_Climb_Top',(-out*.35,0,top),width_m=.65)


def bow_cannon_bays():
    xpos=STERN+BAY+2.10
    # Remove wall volumes at real barrel positions; never hide walls behind guns.
    for side,label in [(1,'Port'),(-1,'Starboard')]:
        for level,floor_z in [('Lower',D+.35),('Upper',D+RISE)]:
            source=bpy.data.objects['Review_Cannon_'+label];copies={}
            for ob in [source]+list(source.children_recursive):
                cp=ob.copy();cp.name='Review_Bow_'+level+'_'+label if ob==source else ob.name+'_Bow_'+level
                bpy.context.scene.collection.objects.link(cp);copies[ob]=cp
            for ob,cp in copies.items():cp.parent=copies.get(ob.parent)
            copies[source].location=(xpos,side*(H-1.18),floor_z)
            kit.PARENT=None
            cutter=M()
            zmid=floor_z+1.15 if level=='Lower' else floor_z+1.55
            height=1.80 if level=='Lower' else 2.70
            cutter.box((xpos,side*H,zmid),(2.05,1.80,height),'dark')
            co=cutter.object('TEMP_Bow_Gun_Port',0,False)
            bpy.context.view_layer.update()
            for target in list(kit.PARTS):
                if not target.parent or target.parent.name not in ['Bow','Bow_Upper_1']:continue
                if not target.name.startswith(('Bulwark','Cream_Cap','Bow_Upper_Panels','Bow_Upper_Cap')):continue
                mod=target.modifiers.new('Bow equipment opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=co
                bpy.context.view_layer.objects.active=target;bpy.ops.object.modifier_apply(modifier=mod.name)
            bpy.data.objects.remove(co,do_unlink=True)
            root('Bow_'+level+'_Equipment',STERN+BAY)
            kit.marker('Cannon_Socket_Bow_'+level+'_'+label,(2.10,side*(H-1.18),floor_z),facing=label.lower(),equipment='cannon')
    # Interrupt the raised floor-edge trim beneath the lower gun wheels.
    target=bpy.data.objects['Foredeck_Perimeter_Timber']
    for side in [-1,1]:
        kit.PARENT=None;mesh=M();mesh.box((xpos,side*H,D+.375),(2.20,1.80,.50),'dark')
        cut=mesh.object('TEMP_Wheel_Trim',0,False);bpy.context.view_layer.update()
        mod=target.modifiers.new('Clear bow gun wheels','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
        bpy.context.view_layer.objects.active=target;bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(cut,do_unlink=True)
    # Timber jambs make the open upper gun bays read as deliberate fittings.
    root('Bow_Upper_Port_Trim',STERN+BAY)
    trim=M()
    for side in [-1,1]:
        for xx in [1.02,3.18]:
            trim.box((xx,side*(H-.12),D+RISE+.58),(.18,.42,1.16),'woodlight')
    save(trim,'Bow_Upper_Gun_Bay_Trim',.015)


def sculpt_pointed_prow():
    # A common field keeps skins, deck edges, caps and stem connected.
    # Cannon shoulders remain fixed; the forward timber sweeps up and out.
    def shape(p):
        x,y,z=p
        t=max(0,min(1,(x-3.65)/(BOW+1.05-3.65)))
        reach=1.30*t**1.35*(1+.28*max(0,min(1,(z-D)/RISE)))
        crown=.95*t*t*max(0,min(1,(z-(D+RISE))/1.1))
        return (x+reach,y,z+crown)
    for ob in list(kit.PARTS):
        if ob.parent and ob.parent.name in ['Bow','Bow_Upper_1','Bow_Foredeck']:
            for v in ob.data.vertices:v.co=shape(v.co)
            ob.data.update()
    for ob in kit.MARKERS:
        if ob.parent and ob.parent.name in ['Bow','Bow_Upper_1','Bow_Foredeck']:ob.location=shape(ob.location)


def refine_bow_rails():
    bx=STERN+BAY;start=-1.85;end=.55;z=D+RISE;high=z+1.10+.55*(end/BOW)**2
    ob=bpy.data.objects.get('Bow_Ladder_Landing_Guard')
    if ob:
        kit.PARTS.remove(ob);SOLIDS.remove(ob);bpy.data.objects.remove(ob,do_unlink=True)
    # Cut back the existing low rail and raised wall before making one curve.
    kit.PARENT=None
    for parent,lo,hi,prefixes in [
        ('Middle_0',bx+start,bx+.02,('Bulwark','Cream_Cap')),
        ('Bow',bx-.02,bx+end,('Bulwark','Cream_Cap')),
        ('Bow_Upper_1',bx-.02,bx+end,('Bow_Upper_Panels','Bow_Upper_Cap'))]:
        cut=M();cut.box(((lo+hi)/2,0,5),(hi-lo,2*H+4,12),'dark');co=cut.object('TEMP_Rail_Transition',0,False)
        bpy.context.view_layer.update()
        for ob in list(kit.PARTS):
            if ob.parent and ob.parent.name==parent and ob.name.startswith(prefixes):
                mod=ob.modifiers.new('Replace abrupt rail end','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=co
                bpy.context.view_layer.objects.active=ob;bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(co,do_unlink=True)
    root('Bow_Curved_Transitions',bx)
    for side in [-1,1]:
        wall=M();cap=M();rings=[];pts=[]
        xs=[start+(0-start)*i/32 for i in range(33)]+[end]
        for x in xs:
            t=max(0,min(1,(x-start)/(-start)));e=t*t*(3-2*t)
            top=(D+1.10)*(1-e)+high*e
            outside=side*H;inside=side*(H-.29);split=max(D+.15,top-1.10)
            rings.append([(x,outside,D-.03),(x,outside,split),(x,outside,top-.12),
                          (x,inside,top-.12),(x,inside,split),(x,inside,D-.03)])
            pts.append((x,side*H,top))
        loft(wall,rings,['sage','wood','woodlight','wood','wood','wooddark'])
        save(wall,'Bow_Transition_Panel_'+str(side),0)
        band(cap,pts,.78,.49,'woodlight',start_join=True)
        save(cap,'Bow_Swept_Rail_'+str(side),0)
    root('Bow_Landing_Railing',bx)
    rail=M();posts=M()
    for side in [-1,1]:
        # Shared rail height and intentional junction with the swept side cap.
        band(rail,[(0,side*.87,high),(0,side*(H-.08),high)],.42,.32,'woodlight')
        for yy in [side*.87,side*2.62]:
            posts.beam((0,yy,z-.12),(0,yy,high-.12),.30,.30,'woodlight')
    save(rail,'Bow_Landing_Continuous_Top_Rail',0)
    save(posts,'Bow_Landing_Aligned_Posts',.015)
    edge=M();edge.box((-.10,0,z-.18),(.40,2*H-.10,.24),'woodlight')
    save(edge,'Bow_Landing_Edge_Timber',.02)


def main():
    global H,LEVEL,WHEEL,CANOPY
    p=argparse.ArgumentParser();p.add_argument('--width',type=float,default=9.28);p.add_argument('--stern-levels',type=int,choices=[0,1],default=1)
    p.add_argument('--canopy',action='store_true');p.add_argument('--name',default='curved-bow-transitions');p.add_argument('--quick',action='store_true');p.add_argument('--no-render',action='store_true')
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    H=args.width/2;LEVEL=args.stern_levels;CANOPY=args.canopy;setup();WHEEL=wheel_parameters()
    dest=OUT/args.name;dest.mkdir(parents=True,exist_ok=True)
    for name,kind,x,length in [('Stern','stern',0,STERN),('Middle_0','middle',STERN,BAY),('Bow','bow',STERN+BAY,BOW)]:
        ro=root(name,x);ro['length_u']=length;build_shell(kind,length);outer_panels(kind,length)
        floor(kind,0,1.8 if kind=='bow' else length,D,'Main_Deck')
    stern_interiors();forecastle(STERN+BAY)
    affected=boolean_well();wheel_well_and_rotor();bore_structural_axle()
    round_stern_meshes([o for o in list(SOLIDS) if (o.parent and o.parent.name in {'Stern','Transom','Stern_Interior','Stern_Upper_1'}) or o.name=='Stern_Opening_Frame'])
    rebuild_stern_skin()
    restore_upper_timber_band()
    middle_storey()
    bow_storey()
    sculpt_pointed_prow()
    fittings();lanterns();seat_stern_fittings();cargo_and_cannons()
    if LEVEL:
        for side,label in [(1,'Port'),(-1,'Starboard')]:
            source=bpy.data.objects['Review_Cannon_'+label]
            originals=[source]+list(source.children_recursive);copies={}
            for ob in originals:
                cp=ob.copy();cp.name='Review_Lower_'+ob.name;bpy.context.scene.collection.objects.link(cp);copies[ob]=cp
            for ob,cp in copies.items():cp.parent=copies.get(ob.parent)
            copies[source].location=(5.7,side*(H-1.18),BASE)

    kit.crew(H,0)
    bpy.data.objects['Review_Helmsman'].location=(4.22,0,BASE+LEVEL*RISE)
    bpy.data.objects['Review_Deckhand'].location=(STERN+3,-.2,D)
    compact_access()
    bow_cannon_bays()
    if LEVEL:cannon_port_details()
    refine_bow_rails()
    # Actual review obstacles have real mesh extents, but are not ship exports.
    bpy.context.view_layer.update()
    report={'family':'F27-study','canopy':CANOPY,'metres_per_unit':.5,'coordinates':'source +X bow,+Y port,+Z up; game (-y,z,x)*0.5',
            'beam_u':2*H,'stern_levels':LEVEL,'base_stern_deck_u':BASE,'layer_rise_u':RISE,
            'section_lengths_u':{'stern':STERN,'middle':BAY,'bow':BOW},'paddle':WHEEL,
            'cavity_cut_objects':affected,'actual_cannon_source':'art-staging/cannon-astra-v1/cannon.fbx',
            'cannon_scale':'1.35x source size; review presentation only; original asset unchanged','cannons':SOURCE_CANNONS,
            'surface_treatment':'Broad shaped chamfers and deterministic painted facets in GameColor; no external textures.',
            'scope':'Reference-led art and static geometry. No Unity runtime or navigation certification.'}
    models=dest/'models';models.mkdir(exist_ok=True);report['exports']=kit.export(models)
    report['triangles']=sum(sum(len(f.vertices)-2 for f in o.data.polygons) for o in kit.PARTS)
    report['markers']=[{'name':o.name,'parent':o.parent.name if o.parent else '', 'local_u':list(o.location)} for o in kit.MARKERS]
    (dest/'manifest.json').write_text(json.dumps(report,indent=2))
    total=STERN+BAY+BOW;target=(total*.40,0,3.1+LEVEL);eye=(-24,-33,26+LEVEL*3)
    if not args.no_render:kit.shot(dest/'hero.png',eye,target,34)
    bpy.ops.wm.save_as_mainfile(filepath=str(dest/'ship.blend'))
    if not args.quick and not args.no_render:
        kit.shot(dest/'stern-detail.png',(-17,-18,12+LEVEL*3),(1.0,0,2.0+LEVEL*1.5),19,(1500,1250))
        kit.shot(dest/'side.png',(total*.43,-65,4),(total*.43,0,3.8),30,(1750,850))
        kit.shot(dest/'top.png',(total*.44,0,65),(total*.44,0,0),30,(1600,1100))
        kit.shot(dest/'bow-detail.png',(STERN+BAY+12,-16,15),(STERN+BAY+3,0,1.4),13,(1500,1200))
        kit.shot(dest/'deck-detail.png',(STERN+7,-17,21),(STERN+1,0,D+1),19,(1400,1100))
    print('F27_DONE '+json.dumps({'folder':str(dest),'paddle':WHEEL,'triangles':report['triangles']}))


if __name__=='__main__':main()
