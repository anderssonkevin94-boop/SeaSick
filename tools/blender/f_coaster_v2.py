"""F2: reference-led rebuild with an actual half-recessed stern wheel well.

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
OUT=ROOT/'art-staging/f-coaster-v2'
M=kit.Mesh
D=1.76
BASE=D+1.20
RISE=4.40
STERN=11.2
BAY=6.0
BOW=7.2
PLATFORM=6.2
SCALE=.5
H=4.64
LEVEL=1
WHEEL={}
SOURCE_CANNONS=[]
SOLIDS=[]


def root(name,x=0):
    o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o)
    o.location.x=x;kit.PARENT=o;return o


def save(mesh,name,bevel=.02,solid=True,export=True):
    o=mesh.object(name,bevel,export)
    if solid:SOLIDS.append(o)
    return o


def loft(mesh,rings,colors,cap=True):
    n=len(rings[0])
    if cap:mesh.face(rings[0][::-1],colors[0]);mesh.face(rings[-1],colors[0])
    for a,b in zip(rings,rings[1:]):
        for i in range(n):mesh.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],colors[i%len(colors)])


def band(mesh,pts,w,t,color):kit.curve_strip(mesh,pts,w,t,color)


def beam_at(x,kind):
    if kind=='stern':return H*(.95+.05*math.sin(min(x/4,1)*math.pi/2))
    if kind=='bow':
        t=max(0,min(1,x/BOW))
        # Rounded shoulders followed by a blunt, raked stem.
        return H*(.075+.925*math.cos(t*math.pi/2)**.72)
    return H


def rake(x,z,kind):
    if kind!='bow':return x
    t=max(0,min(1,x/BOW));return x+.55*t**4*(z+1.92)/6


def rail_z(x,kind):
    if kind=='stern':
        top=BASE+LEVEL*RISE
        t=max(0,min(1,(x-PLATFORM)/(STERN-PLATFORM)))
        # Smooth shoulder rather than a near-vertical U-shaped drop.
        s=t*t*(3-2*t)
        return (top+1.35)*(1-s)+(D+1.65)*s
    if kind=='bow':return D+1.65+1.40*(x/BOW)**2.2
    return D+1.65


def build_shell(kind,length):
    profile=[(-1.92,.28),(-1.58,.53),(-1.02,.78),(-.34,.93),(.37,.985),(1.06,1.0),(D,1.0)]
    xs=[length*i/24 for i in range(25)]
    for side in [-1,1]:
        shell=M();rings=[]
        for x in xs:
            w=beam_at(x,kind)
            outer=[(rake(x,z,kind),side*w*f,z) for z,f in profile]
            inner=[(rake(x,z,kind),side*(w*f-.19),z+.015) for z,f in profile]
            rings.append(outer+inner[::-1])
        loft(shell,rings,['bottom','bottom','sage2','wooddark','wood','woodlight']+['wood']*8)
        save(shell,'Hull_Port' if side>0 else 'Hull_Starboard',0)
    bottom=M();rings=[]
    for x in xs:
        w=beam_at(x,kind)*.28
        rings.append([(rake(x,-1.92,kind),-w,-1.92),(rake(x,-1.92,kind),w,-1.92),
                      (rake(x,-1.7,kind),w,-1.70),(rake(x,-1.7,kind),-w,-1.70)])
    loft(bottom,rings,['bottom']*4);save(bottom,'Keel',0)
    # End face of the base stern is solid until the actual wheel well is cut.
    if kind=='stern':
        back=M()
        for (za,fa),(zb,fb) in zip(profile,profile[1:]):
            w0=beam_at(0,kind)*fa;w1=beam_at(0,kind)*fb
            rr=[[(-.02,-w0,za),(-.02,w0,za),(-.02,w1,zb),(-.02,-w1,zb)],
                [(.24,-w0,za),(.24,w0,za),(.24,w1,zb),(.24,-w1,zb)]]
            loft(back,rr,['bottom' if zb<-.3 else 'wood']*4)
        save(back,'Lower_Transom',0)
    if kind=='bow':
        stem=M();last=[(rake(length,z,kind),0,z) for z in [-1.6,-.5,1.0,rail_z(length,kind)+.1]]
        for a,b in zip(last,last[1:]):stem.beam(a,b,.48,.64,'woodlight')
        stem.box(last[-1],(.74,.84,.28),'cream');save(stem,'Blunt_Stem',.055)
    kit.marker('Join_Aft',(0,0,D),standard=f'F2_{2*H:.2f}')
    kit.marker('Join_Forward',(length,0,D),standard=f'F2_{2*H:.2f}')


def floor(kind,start,end,z,name,omit=None):
    mesh=M();count=max(1,math.ceil((end-start)/.62))
    for i in range(count):
        x0=start+(end-start)*i/count+.003;x1=start+(end-start)*(i+1)/count-.003
        w0=max(.05,beam_at(x0,kind)-.20);w1=max(.05,beam_at(x1,kind)-.20)
        rr=[[(rake(x0,z,kind),-w0,z-.22),(rake(x0,z,kind),w0,z-.22),(rake(x0,z,kind),w0,z),(rake(x0,z,kind),-w0,z)],
            [(rake(x1,z,kind),-w1,z-.22),(rake(x1,z,kind),w1,z-.22),(rake(x1,z,kind),w1,z),(rake(x1,z,kind),-w1,z)]]
        col=['deck','decklight','deck','deck','deckdark'][i%5]
        loft(mesh,rr,[col]*4)
    return save(mesh,name,.012)


def outer_panels(kind,length):
    # Closed, continuous side panel strips; no overlapping internal faces.
    for side in [-1,1]:
        body=M();green=M();cap=M()
        xs=sorted(set([length*i/32 for i in range(33)]+([2.22,3.78] if kind=='middle' else [])))
        for a,b in zip(xs,xs[1:]):
            z0=rail_z(a,kind);z1=rail_z(b,kind)
            gunport=kind=='middle' and a>=2.22-1e-5 and b<=3.78+1e-5
            layers=[(D,min(D+.43,z0-.61),D,min(D+.43,z1-.61),'wood')] if gunport else [
                (z0-.66,z0-.14,z1-.66,z1-.14,'sage')]
            for la,ha,lb,hb,color in layers:
                if ha<=la or hb<=lb:continue
                rr=[]
                for x,lo,hi in [(a,la,ha),(b,lb,hb)]:
                    y=side*beam_at(x,kind)
                    rr.append([(rake(x,lo,kind),y,lo),(rake(x,hi,kind),y,hi),
                               (rake(x,hi,kind),y-side*.25,hi),(rake(x,lo,kind),y-side*.25,lo)])
                loft(green if color=='sage' else body,rr,[color]*4)
            if not gunport:
                # Horizontal, restrained plank courses clipped to the flowing sheer.
                def clip(poly,height,above):
                    result=[]
                    for pp,qq in zip(poly,poly[1:]+poly[:1]):
                        inp=pp[1]>=height if above else pp[1]<=height
                        inq=qq[1]>=height if above else qq[1]<=height
                        if inp:result.append(pp)
                        if inp!=inq:
                            t=(height-pp[1])/(qq[1]-pp[1])
                            result.append((pp[0]+t*(qq[0]-pp[0]),height))
                    return result
                for j in range(math.ceil((max(z0,z1)-D)/.66)):
                    poly=clip(clip([(a,D),(b,D),(b,z1-.66),(a,z0-.66)],D+j*.66+.004,True),D+(j+1)*.66-.004,False)
                    if len(poly)<3:continue
                    outside=[(rake(x,z,kind),side*beam_at(x,kind),z) for x,z in poly]
                    inside=[(x,y-side*.25,z) for x,y,z in outside]
                    col=['plank_a','plank_b','plank_c','plank_a','plank_d'][j%5]
                    body.face(outside,col);body.face(inside[::-1],col)
                    for k in range(len(poly)):body.face([outside[k],outside[(k+1)%len(poly)],inside[(k+1)%len(poly)],inside[k]],col)
        band(cap,[(rake(x,rail_z(x,kind),kind),side*beam_at(x,kind),rail_z(x,kind)) for x in xs],.52,.28,'cream')
        save(body,'Bulwark_'+str(side),.015);save(green,'Green_Sheer_Band_'+str(side),.015);save(cap,'Cream_Cap_'+str(side),.025)
    # Substantial posts/ribs, with broad cream cuffs and shaped bow ribs.
    frame=M()
    stops=([.55,3.8,6.2,9.0] if kind=='stern' else [0.65,5.35] if kind=='middle' else [1.0,3.8,5.8])
    for side in [-1,1]:
        for x in stops:
            top=rail_z(x,kind)
            pts=[(rake(x,z,kind),side*(beam_at(x,kind)*f+.08),z)
                 for z,f in [(-1.45,.59),(-.95,.81),(-.30,.955),(D,1),(top+.22,1)]]
            for a,b in zip(pts,pts[1:]):frame.beam(a,b,.32,.36,'woodlight')
            for z in [D+.25,top+.08]:frame.box((rake(x,z,kind),side*(beam_at(x,kind)+.08),z),(.47,.49,.22),'cream')
            frame.box((rake(x,top,kind),side*(beam_at(x,kind)+.08),top+.26),(.42,.44,.21),'woodlight')
    save(frame,'Carved_Frames',.055)


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
        wall=M();frame=M();door=M()
        # Portal on the front face, beyond the forward end of the wheel well.
        for s in [-1,1]:
            wid=H-1.18
            wall.box((PLATFORM-.14,s*(1.18+wid/2),(BASE+top)/2),(.28,wid,RISE),'wood')
            frame.beam((PLATFORM+.025,s*1.18,BASE),(PLATFORM+.025,s*1.18,top-.2),.22,.24,'woodlight')
        frame.box((PLATFORM+.025,0,top-.30),(.27,2.58,.30),'cream')
        wall.box((PLATFORM-.14,0,top-.16),(.28,2.36,.32),'wood')
        save(wall,'Forward_Bulkhead',.02);save(frame,'Portal_Frame',.045)
        # Door held fully open against the inside bulkhead; hinge stays separate.
        door.box((0,.54,1.9),(.13,1.08,3.8),'woodlight')
        for z in [.48,3.28]:door.box((-.07,.54,z),(.08,1.15,.17),'sage2')
        door.cylinder((-.14,.87,1.82),(-.22,.87,1.82),.075,.075,'brass',10)
        do=save(door,'Door_Open',.025);do.location=(PLATFORM-.32,1.10,BASE);do.rotation_euler.z=math.pi
        # Stair flight stays within this stern module, with a top landing.
        stairs=M();y=H-1.40;run=4.30;rise=top-D;count=math.ceil(rise/.36)
        for i in range(count):
            x=PLATFORM+run-(i+.5)*run/count;zz=D+(i+1)*rise/count
            stairs.box((x,y,zz-.10),(run/count+.018,1.96,.20),'decklight')
        stairs.box((PLATFORM+.10,y,top-.11),(.42,1.96,.22),'decklight')
        for yy in [y-1.07,y+1.07]:
            stairs.beam((PLATFORM+run,yy,D),(PLATFORM,yy,top),.22,.28,'wooddark')
            stairs.beam((PLATFORM+run,yy,D+1.55),(PLATFORM,yy,top+1.55),.20,.23,'cream')
            for t in [0,.5,1]:stairs.beam((PLATFORM+run*(1-t),yy,D+rise*t),(PLATFORM+run*(1-t),yy,D+rise*t+1.55),.23,.23,'woodlight')
        save(stairs,'Port_Stairs',.035)
        guard=M()
        guard.beam((PLATFORM,-H+.2,top+1.4),(PLATFORM,y-1.10,top+1.4),.24,.26,'cream')
        for yy in [-H+.2,-1.1,1.1,y-1.10]:guard.beam((PLATFORM,yy,top),(PLATFORM,yy,top+1.4),.23,.23,'woodlight')
        save(guard,'Front_Guard',.025)
        kit.marker('Stair_Entry',(PLATFORM+run+.35,y,D),width_m=.90)
        kit.marker('Stair_Exit',(PLATFORM-.45,y,top),width_m=.90)
        kit.marker('Door_Entry',(PLATFORM+.2,0,BASE),width_m=1.06)
    # Transom is a closed solid, later bored out with the real rotor envelope.
    root('Transom')
    face=M();rr=[]
    for x in [-.05,.27]:
        w=beam_at(0,'stern')
        rr.append([(x,-w,D-.03),(x,w,D-.03),(x,w,top+.25),(x,-w,top+.25)])
    loft(face,rr,['wood']*4);save(face,'Transom_Wall',0)
    # Green arched crown is thick and continuous across the aft face.
    fascia=M();lip=M()
    ys=[beam_at(0,'stern')*(-1+i/16*2) for i in range(17)]
    for a,b in zip(ys,ys[1:]):
        rr=[]
        for y in [a,b]:
            crown=.30*(1-(y/beam_at(0,'stern'))**2)
            rr.append([(-.14,y,top+.13+crown),(.24,y,top+.13+crown),(.24,y,top+1.35+crown),(-.14,y,top+1.35+crown)])
        loft(fascia,rr,['sage']*4)
    band(lip,[(-.10,y,top+1.48+.30*(1-(y/beam_at(0,'stern'))**2)) for y in ys],.57,.28,'cream')
    save(fascia,'Arched_Sage_Transom',.018);save(lip,'Arched_Cream_Cap',.03)


def wheel_parameters():
    top=BASE+LEVEL*RISE
    radius=(top-.60+1.0)/2
    return dict(radius_u=radius,width_u=2*H-4.60,axle_u=[.12,0,radius-1.0],
                cavity_clearance_u=.25,stern_plane_u=0,tip_u=-1.0,
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
    r=WHEEL['radius_u'];hw=WHEEL['width_u']/2;cx,_,cz=WHEEL['axle_u'];top=WHEEL['platform_z_u']
    root('Wheel_Well')
    liner=M();rad=r+.25;thick=.17
    # Forward semicylindrical casing: roof/forward wall, plus inner side liners.
    angles=[math.pi*i/32 for i in range(33)]
    rings=[]
    for a in angles:
        rings.append([(cx+rr*math.sin(a),y,cz+rr*math.cos(a)) for rr,y in
                     [(rad,-hw-.27),(rad,hw+.27),(rad+thick,hw+.27),(rad+thick,-hw-.27)]])
    loft(liner,rings,['wooddark']*4)
    for side in [-1,1]:
        # Solid half-disc side wall closes the cavity against the dry hull.
        ring=[(cx,side*(hw+.27),cz)]+[(cx+rad*math.sin(a),side*(hw+.27),cz+rad*math.cos(a)) for a in angles]
        back=[(x,y+side*.16,z) for x,y,z in ring]
        liner.face(ring,'wooddark');liner.face(back[::-1],'wooddark')
        for i in range(len(ring)):liner.face([ring[i],ring[(i+1)%len(ring)],back[(i+1)%len(ring)],back[i]],'wooddark')
    lining=save(liner,'Wheel_Well_Lining',.012)
    # Bore an actual axle hole through the lining, rather than hiding the axle
    # inside a solid half-disc. Bearings are annular for the same reason.
    kit.PARENT=None;hole=M();hole.cylinder((cx,-hw-1,cz),(cx,hw+1,cz),.24,.24,'dark',16)
    cut=hole.object('TEMP_Axle_Bore',0,False)
    bpy.context.view_layer.objects.active=lining
    mod=lining.modifiers.new('Axle_bore','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut
    bpy.ops.object.modifier_move_up(modifier=mod.name);bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cut,do_unlink=True);kit.PARENT=bpy.data.objects['Wheel_Well']
    # Visible opening: short cheeks and a crown, rather than long flying struts.
    frame=M();bearings=M()
    for side in [-1,1]:
        y=side*(hw+.51)
        frame.beam((-.18,y,-.68),(-.08,y,top+.12),.47,.45,'woodlight')
        frame.box((-.18,y,-.60),(.64,.66,.35),'cream')
        frame.box((-.08,y,top+.18),(.68,.70,.34),'cream')
        bearings.ring((cx,side*(hw+.475),cz),.43,.20,.55,'iron',16)
        # Short bearing brace terminates on the cheek, with no overhang arm.
        frame.beam((cx,side*(hw+.72),cz),(-.18,side*(hw+.64),-.45),.37,.43,'wood')
    pts=[(-.17,y,top-.06+.55*(1-(y/(hw+.52))**2)) for y in [(-hw-.52)+(2*hw+1.04)*i/16 for i in range(17)]]
    band(frame,pts,.46,.38,'woodlight')
    save(frame,'Stern_Opening_Frame',.045);save(bearings,'Axle_Bearings',.035)
    root('Paddle_Rotor_Module')
    rotor=M()
    rotor.cylinder((0,-hw-.72,0),(0,hw+.72,0),.19,.19,'iron',12)
    for side in [-1,1]:
        y=side*(hw-.08)
        rotor.ring((0,y,0),r-.015,.37,.34,'woodlight',24)
        rotor.cylinder((0,y-.20,0),(0,y+.20,0),.34,.34,'wooddark',12)
        for i in range(8):
            a=i*math.tau/8
            rotor.beam((.18*math.sin(a),y,.18*math.cos(a)),((r-.17)*math.sin(a),y,(r-.17)*math.cos(a)),.25,.28,'woodlight')
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
    root('Bow_Foredeck',bow_x);z=D+1.12
    floor('bow',2.0,BOW,z,'Foredeck_Floor')
    front=M()
    w=beam_at(2,'bow')-.2
    for s in [-1,1]:front.box((1.96,s*(1.30+(w-1.30)/2),D+.53),(.24,w-1.30,1.06),'wood')
    for i in range(3):front.box((.55+i*.5,0,D+(i+1)*1.12/3-.1),(.53,2.5,.20),'decklight')
    save(front,'Foredeck_Steps',.035)
    hatch=M();hatch.box((4.0,0,z+.10),(1.42,1.54,.20),'wooddark')
    for i in range(4):hatch.box((3.48+i*.35,0,z+.22),(.335,1.48,.16),'decklight')
    for yy in [-.54,.54]:hatch.box((4,yy,z+.31),(1.50,.13,.08),'sage2')
    save(hatch,'Closed_Foredeck_Hatch',.025)
    kit.marker('Foredeck_Access',(2.25,0,z));kit.marker('Hatch',(4,0,z))


def fittings():
    top=BASE+LEVEL*RISE;root('Stern_Fittings')
    mesh=M()
    for x in [.95,4.2]:
        for y in [-1.75,1.75]:
            mesh.beam((x,y,top),(x,y,top+4.35),.29,.29,'woodlight')
            mesh.box((x,y,top+4.38),(.47,.47,.24),'woodlight')
    for y in [-1.78,1.78]:mesh.beam((.75,y,top+4.23),(4.4,y,top+4.23),.28,.30,'woodlight')
    for x in [.82,4.32]:mesh.beam((x,-1.95,top+4.23),(x,1.95,top+4.23),.28,.30,'woodlight')
    # Sculpted canvas: shallow sag, subtly raised ridge, substantial hems.
    canvas=M();rows=[]
    for i in range(7):
        x=.65+3.9*i/6
        rows.append([(x,-2.01+j*4.02/6,top+4.29+.18*math.sin(j*math.pi/6)-.13*math.sin(i*math.pi/6)) for j in range(7)])
    for a,b in zip(rows,rows[1:]):
        for j in range(6):canvas.face([a[j],a[j+1],b[j+1],b[j]],'canvas')
    co=save(canvas,'Canvas_Shade',0,False);mod=co.modifiers.new('Canvas thickness','SOLIDIFY');mod.thickness=.045
    cy=H-1.0;xx=1.05
    mesh.box((xx,cy,top+.33),(.95,.95,.66),'wood')
    mesh.cylinder((xx,cy,top+.5),(xx,cy,top+4.35),.39,.34,'iron',8)
    mesh.cylinder((xx,cy,top+3.65),(xx,cy,top+3.97),.47,.47,'sage',8)
    mesh.cylinder((xx,cy,top+4.35),(xx,cy,top+4.39),.27,.27,'dark',8)
    mesh.box((4.30,0,top+.64),(.46,.47,1.28),'wooddark')
    save(mesh,'Canopy_Posts_Chimney',.04)
    helm=M();helm.ring((0,0,0),.68,.13,.17,'woodlight',16)
    for i in range(8):
        a=i*math.tau/8;helm.beam((0,0,0),(.88*math.sin(a),0,.88*math.cos(a)),.12,.12,'woodlight')
    helm.cylinder((0,-.14,0),(0,.14,0),.18,.18,'brass',10)
    o=save(helm,'Helm',.016);o.location=(4.34,0,top+1.46);o.rotation_euler.z=math.pi/2
    # One purposeful lantern, like F's reference; no decorative clutter.
    lamp=M();x=.83;y=-H+.80;zz=top+1.72
    lamp.box((x,y,zz),(.34,.34,.56),'amber')
    for dx in [-.20,.20]:
        for dy in [-.20,.20]:lamp.beam((x+dx,y+dy,zz-.35),(x+dx,y+dy,zz+.35),.07,.07,'iron')
    lamp.box((x,y,zz-.37),(.54,.54,.12),'iron');lamp.box((x,y,zz+.37),(.54,.54,.12),'iron')
    lamp.cylinder((x,y,zz+.42),(x,y,zz+.60),.22,.08,'iron',8)
    save(lamp,'Aft_Lantern',.012)
    kit.marker('Helm_Stand',(5.35,0,top))


def cargo_and_cannons():
    root('Review_Cargo',STERN+BAY)
    cargo=M()
    for x,y,z,w,d,height in [(3.0,-H*.48,D+1.12,1.45,1.32,1.05),(1.0,H-.95,D,1.20,1.25,1.0)]:
        cargo.box((x,y,z+height/2),(w,d,height),'wood')
        for yy in [y-d/2,y+d/2]:
            for zz in [z+.13,z+height-.13]:cargo.box((x,yy,zz),(w+.06,.13,.20),'woodlight')
        for xx in [x-w/2,x+w/2]:cargo.box((xx,y,z+height/2),(.14,d+.08,height+.06),'woodlight')
    save(cargo,'Review_Crates',.04,False,False)
    # Load the actual existing cannon asset, preserving all source transforms.
    kit.PARENT=None
    for side in [-1,1]:
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(ROOT/'art-staging/cannon-astra-v1/cannon.fbx'))
        imported=list(set(bpy.data.objects)-before)
        gunroot=next(o for o in imported if o.type=='EMPTY' and o.parent is None)
        gunroot.name='Review_Cannon_Port' if side>0 else 'Review_Cannon_Starboard'
        gunroot.location=(STERN+3,side*(H-1.08),D)
        gunroot.rotation_euler.z=math.pi if side>0 else 0
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
    kit.PALETTE.update({'wood':(.52,.278,.105,1),'woodlight':(.64,.37,.16,1),'wooddark':(.41,.205,.073,1),
        'deck':(.62,.375,.185,1),'decklight':(.67,.416,.22,1),'deckdark':(.575,.335,.15,1),
        'sage':(.245,.33,.255,1),'sage2':(.20,.265,.22,1),'cream':(.85,.795,.64,1),
        'plank_a':(.54,.295,.12,1),'plank_b':(.59,.33,.145,1),
        'plank_c':(.56,.305,.125,1),'plank_d':(.505,.266,.10,1)})
    kit.setup()
    scene=bpy.context.scene;scene.cycles.samples=40
    ground=bpy.data.objects['Studio_Ground'].data.materials[0]
    p=next(n for n in ground.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(.61,.59,.52,1)
    next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=.75
    bpy.data.lights['Key'].energy=4300;bpy.data.lights['Key'].color=(1.0,.88,.72)
    bpy.data.lights['Fill'].energy=2300
    kit.STERN=STERN;kit.BOW=BOW


def main():
    global H,LEVEL,WHEEL
    p=argparse.ArgumentParser();p.add_argument('--width',type=float,default=9.28);p.add_argument('--stern-levels',type=int,choices=[0,1],default=1)
    p.add_argument('--name',default='narrow-raised');p.add_argument('--quick',action='store_true');p.add_argument('--no-render',action='store_true')
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    H=args.width/2;LEVEL=args.stern_levels;setup();WHEEL=wheel_parameters()
    dest=OUT/args.name;dest.mkdir(parents=True,exist_ok=True)
    for name,kind,x,length in [('Stern','stern',0,STERN),('Middle_0','middle',STERN,BAY),('Bow','bow',STERN+BAY,BOW)]:
        ro=root(name,x);ro['length_u']=length;build_shell(kind,length);outer_panels(kind,length)
        floor(kind,0,length,D,'Main_Deck')
    stern_interiors();forecastle(STERN+BAY)
    affected=boolean_well();wheel_well_and_rotor();bore_structural_axle();fittings();cargo_and_cannons()
    kit.crew(H,0)
    bpy.data.objects['Review_Helmsman'].location=(5.38,0,BASE+LEVEL*RISE)
    bpy.data.objects['Review_Deckhand'].location=(STERN+1.1,-.25,D)
    # Actual review obstacles have real mesh extents, but are not ship exports.
    bpy.context.view_layer.update()
    report={'family':'F2-study','metres_per_unit':.5,'coordinates':'source +X bow,+Y port,+Z up; game (-y,z,x)*0.5',
            'beam_u':2*H,'stern_levels':LEVEL,'base_stern_deck_u':BASE,'layer_rise_u':RISE,
            'section_lengths_u':{'stern':STERN,'middle':BAY,'bow':BOW},'paddle':WHEEL,
            'cavity_cut_objects':affected,'actual_cannon_source':'art-staging/cannon-astra-v1/cannon.fbx',
            'cannon_scale':'source import transforms retained; no resizing','cannons':SOURCE_CANNONS,
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
        kit.shot(dest/'deck-detail.png',(STERN+7,-17,21),(STERN+1,0,D+1),19,(1400,1100))
    print('F2_DONE '+json.dumps({'folder':str(dest),'paddle':WHEEL,'triangles':report['triangles']}))


if __name__=='__main__':main()
