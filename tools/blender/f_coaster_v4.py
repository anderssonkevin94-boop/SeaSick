"""F4: whole-boat silhouette and colour study, preserving a recessed wheel.

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
OUT=ROOT/'art-staging/f-coaster-v4'
class CraftedMesh(kit.Mesh):
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
BOW=6.4
PLATFORM=4.9
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


def save(mesh,name,bevel=.02,solid=True,export=True):
    o=mesh.object(name,bevel,export)
    if solid:SOLIDS.append(o)
    return o


def loft(mesh,rings,colors,cap=True):
    n=len(rings[0])
    if cap:mesh.face(rings[0][::-1],colors[0]);mesh.face(rings[-1],colors[0])
    for a,b in zip(rings,rings[1:]):
        for i in range(n):mesh.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],colors[i%len(colors)])


def band(mesh,pts,w,t,color):
    # Eight-sided, softly crowned solid timber, with restrained tool-shaped
    # variation along the length. End sections stay exact for module joins.
    rings=[]
    for i,point in enumerate(pts):
        u=i/(len(pts)-1)
        tangent=Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(0,i-1)])
        out=Vector((-tangent.y,tangent.x,0)).normalized()
        wobble=math.sin(math.pi*u)**2*(.025*math.sin(u*19)+.012*math.sin(u*37))
        p=Vector(point);ww=w/2+wobble;hh=t/2
        section=[(-ww*.72,-hh),(ww*.72,-hh),(ww,-hh*.42),(ww,hh*.40),
                 (ww*.68,hh),(-ww*.68,hh),(-ww,hh*.40),(-ww,-hh*.42)]
        rings.append([tuple(p+out*y+Vector((0,0,z+wobble*.5))) for y,z in section])
    loft(mesh,rings,[color]*8)



def beam_at(x,kind):
    if kind=='stern':return H*(.88+.12*math.sin(min(x/4.6,1)*math.pi/2))
    if kind=='bow':
        t=max(0,min(1,x/BOW))
        # Rounded shoulders followed by a blunt, raked stem.
        return H*(.045+.955*math.cos(t*math.pi/2)**.48)
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
        t=max(0,min(1,(x-2.6)/(STERN-2.6)))
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
        back=M()
        for (za,fa),(zb,fb) in zip(profile,profile[1:]):
            w0=beam_at(0,kind)*fa;w1=beam_at(0,kind)*fb
            rr=[[(-.02,-w0,za),(-.02,w0,za),(-.02,w1,zb),(-.02,-w1,zb)],
                [(.24,-w0,za),(.24,w0,za),(.24,w1,zb),(.24,-w1,zb)]]
            loft(back,rr,['sage2' if zb<.4 else 'sage']*4)
        save(back,'Lower_Transom',0)
    if kind=='bow':
        stem=M();last=[(rake(length,z,kind),0,z) for z in [-1.6,-.5,1.0,rail_z(length,kind)+.1]]
        for a,b in zip(last,last[1:]):stem.beam(a,b,.78,.90,'woodlight')
        stem.box(last[-1],(1.02,1.06,.45),'woodlight');save(stem,'Blunt_Stem',.13)
    kit.marker('Join_Aft',(0,0,D),standard=f'F4_{2*H:.2f}')
    kit.marker('Join_Forward',(length,0,D),standard=f'F4_{2*H:.2f}')


def floor(kind,start,end,z,name,omit=None):
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
            rings.append([(rake(x,z,kind),-w,z-.22),(rake(x,z,kind),w,z-.22),
                          (rake(x,z,kind),w,z),(rake(x,z,kind),-w,z)])
        col=['deck','decklight','deck','deck','deckdark'][i%5]
        loft(mesh,rings,[col]*4)
    return save(mesh,name,.012)


def outer_panels(kind,length):
    # Long sweeping strakes carry the silhouette. Broader, fewer ribs frame
    # the mass instead of subdividing it into a repetitive fence.
    for side in [-1,1]:
        body=M();green=M();cap=M()
        xs=sorted(set([length*i/40 for i in range(41)]+([2.22,3.78] if kind=='middle' else [])))
        courses=3 if kind!='middle' else 2
        if kind=='stern' and LEVEL:courses=7
        for a,b in zip(xs,xs[1:]):
            gunport=kind=='middle' and a>=2.22-1e-5 and b<=3.78+1e-5
            for j in range(courses+1):
                if gunport:
                    if j:continue
                    heights=lambda x:(D,D+.35)
                    col='wood'
                elif j==courses:
                    heights=lambda x:(rail_z(x,kind)-.78,rail_z(x,kind)-.13)
                    col='woodlight'
                else:
                    heights=lambda x:(D+(rail_z(x,kind)-.78-D)*j/courses+.003,
                                      D+(rail_z(x,kind)-.78-D)*(j+1)/courses-.003)
                    col=['plank_a','plank_b','plank_c','plank_d'][j%4]
                rr=[]
                for x in [a,b]:
                    lo,hi=heights(x);y=side*beam_at(x,kind)
                    rr.append([(rake(x,lo,kind),y,lo),(rake(x,hi,kind),y,hi),
                               (rake(x,hi,kind),y-side*.28,hi),(rake(x,lo,kind),y-side*.28,lo)])
                loft(green if col=='woodlight' else body,rr,[col]*4)
        band(cap,[(rake(x,rail_z(x,kind),kind),side*beam_at(x,kind),rail_z(x,kind)) for x in xs],.78,.49,'woodlight')
        save(body,'Bulwark_'+str(side),.018);save(green,'Green_Sheer_Band_'+str(side),.02);save(cap,'Cream_Cap_'+str(side),.045)
    # A generous wooden rubbing wale unifies the whole hull at deck height.
    wale=M();samples=[length*i/40 for i in range(41)]
    for side in [-1,1]:
        band(wale,[(rake(x,D-.13,kind),side*(beam_at(x,kind)*1.018+.08),D-.13) for x in samples],.57,.57,'woodlight')
    save(wale,'Solid_Timber_Wale',.015)
    frame=M();metal=M()
    stops=([.75,4.7,8.5] if kind=='stern' else [1.10,5.10] if kind=='middle' else [2.4,5.15])
    for side in [-1,1]:
        for index,x in enumerate(stops):
            top=rail_z(x,kind);width=.59 if index%2 else .66
            path=[(rake(x,z,kind),side*(beam_at(x,kind)*f+.10),z)
                  for z,f in [(-1.48,.63),(-1.0,.85),(-.34,.985),(.40,1.025),(D,1.018),(top+.22,1)]]
            # One continuous curved rib, with broad chamfers instead of a chain
            # of identical boxes and dark gaps between their ends.
            rings=[]
            for k,pt in enumerate(path):
                u=k/(len(path)-1);w=width*(1+.08*math.sin(math.pi*u));d=.48
                profile=[(-.36*w,-d/2),(.36*w,-d/2),(w/2,-d*.25),(w/2,d*.25),
                         (.34*w,d/2),(-.34*w,d/2),(-w/2,d*.25),(-w/2,-d*.25)]
                rings.append([(pt[0]+xx,pt[1]+yy,pt[2]) for xx,yy in profile])
            loft(frame,rings,['woodlight']*8)
            frame.box((rake(x,top,kind),side*(beam_at(x,kind)+.10),top+.25),(.76,.74,.35),'woodlight')
            # Long iron straps bind the cap, post and rubbing wale together.
            zz=top-.47;yy=side*(beam_at(x,kind)+.37)
            metal.box((rake(x,zz,kind),yy,zz),(.49,.12,1.15),'iron')
            for dz in [-.34,.34]:
                metal.cylinder((rake(x,zz,kind),yy+side*.065,zz+dz),
                               (rake(x,zz,kind),yy+side*.14,zz+dz),.105,.085,'brass',8)
    save(frame,'Carved_Frames',.065);save(metal,'Forged_Straps',.035)


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
        loft(fascia,rr,['wood']*4)
    band(lip,[(-.10,y,top+1.48+.30*(1-(y/beam_at(0,'stern'))**2)) for y in ys],.72,.45,'woodlight')
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
        frame.box((-.18,y,-.60),(.64,.66,.35),'iron')
        frame.box((-.08,y,top+.18),(.68,.70,.34),'woodlight')
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
    front=M();w=beam_at(1.8,'bow')-.2
    front.box((1.78,0,D+.15),(.25,2*w,.30),'wooddark')
    front.box((1.39,0,D+.08),(.50,2.7,.16),'decklight')
    save(front,'Foredeck_Step',.035)
    hatch=M();hatch.box((3.45,0,z+.025),(1.90,1.72,.05),'wooddark')
    for i in range(5):hatch.box((2.72+i*.365,0,z+.06),(.35,1.62,.07),'deckdark')
    for yy in [-.53,.53]:hatch.box((3.45,yy,z+.11),(1.88,.11,.05),'sage2')
    for yy in [-.45,.45]:hatch.ring((4.0,yy,z+.14),.09,.026,.03,'brass',10)
    save(hatch,'Closed_Foredeck_Hatch',.018)
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
    cy=H-1.0;xx=1.05
    mesh.box((xx,cy,top+.33),(.95,.95,.66),'wood')
    mesh.cylinder((xx,cy,top+.5),(xx,cy,top+4.35),.39,.34,'iron',8)
    mesh.cylinder((xx,cy,top+3.65),(xx,cy,top+3.97),.47,.47,'sage',8)
    mesh.cylinder((xx,cy,top+4.35),(xx,cy,top+4.39),.27,.27,'dark',8)
    mesh.box((3.35,0,top+.64),(.46,.47,1.28),'wooddark')
    save(mesh,'Chimney_Helm_Base',.04)
    helm=M();helm.ring((0,0,0),.68,.13,.17,'woodlight',16)
    for i in range(8):
        a=i*math.tau/8;helm.beam((0,0,0),(.88*math.sin(a),0,.88*math.cos(a)),.12,.12,'woodlight')
    helm.cylinder((0,-.14,0),(0,.14,0),.18,.18,'brass',10)
    o=save(helm,'Helm',.016);o.location=(3.39,0,top+1.46);o.rotation_euler.z=math.pi/2
    # One purposeful lantern, like F's reference; no decorative clutter.
    lamp=M();x=.83;y=-H+.80;zz=top+1.72
    lamp.box((x,y,zz),(.34,.34,.56),'amber')
    for dx in [-.20,.20]:
        for dy in [-.20,.20]:lamp.beam((x+dx,y+dy,zz-.35),(x+dx,y+dy,zz+.35),.07,.07,'iron')
    lamp.box((x,y,zz-.37),(.54,.54,.12),'iron');lamp.box((x,y,zz+.37),(.54,.54,.12),'iron')
    lamp.cylinder((x,y,zz+.42),(x,y,zz+.60),.22,.08,'iron',8)
    save(lamp,'Aft_Lantern',.012)
    kit.marker('Helm_Stand',(4.20,0,top))


def cargo_and_cannons():
    root('Review_Cargo',STERN+BAY)
    cargo=M()
    for x,y,z,w,d,height in [(3.0,-H*.48,D+.35,1.45,1.32,1.05),(1.0,H-.95,D,1.20,1.25,1.0)]:
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


def main():
    global H,LEVEL,WHEEL,CANOPY
    p=argparse.ArgumentParser();p.add_argument('--width',type=float,default=9.28);p.add_argument('--stern-levels',type=int,choices=[0,1],default=0)
    p.add_argument('--canopy',action='store_true');p.add_argument('--name',default='open-helm');p.add_argument('--quick',action='store_true');p.add_argument('--no-render',action='store_true')
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    H=args.width/2;LEVEL=args.stern_levels;CANOPY=args.canopy;setup();WHEEL=wheel_parameters()
    dest=OUT/args.name;dest.mkdir(parents=True,exist_ok=True)
    for name,kind,x,length in [('Stern','stern',0,STERN),('Middle_0','middle',STERN,BAY),('Bow','bow',STERN+BAY,BOW)]:
        ro=root(name,x);ro['length_u']=length;build_shell(kind,length);outer_panels(kind,length)
        floor(kind,0,length,D,'Main_Deck')
    stern_interiors();forecastle(STERN+BAY)
    affected=boolean_well();wheel_well_and_rotor();bore_structural_axle();fittings();cargo_and_cannons()
    kit.crew(H,0)
    bpy.data.objects['Review_Helmsman'].location=(4.22,0,BASE+LEVEL*RISE)
    bpy.data.objects['Review_Deckhand'].location=(STERN+1.1,-.25,D)
    # Actual review obstacles have real mesh extents, but are not ship exports.
    bpy.context.view_layer.update()
    report={'family':'F4-study','canopy':CANOPY,'metres_per_unit':.5,'coordinates':'source +X bow,+Y port,+Z up; game (-y,z,x)*0.5',
            'beam_u':2*H,'stern_levels':LEVEL,'base_stern_deck_u':BASE,'layer_rise_u':RISE,
            'section_lengths_u':{'stern':STERN,'middle':BAY,'bow':BOW},'paddle':WHEEL,
            'cavity_cut_objects':affected,'actual_cannon_source':'art-staging/cannon-astra-v1/cannon.fbx',
            'cannon_scale':'source import transforms retained; no resizing','cannons':SOURCE_CANNONS,
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
    print('F4_DONE '+json.dumps({'folder':str(dest),'paddle':WHEEL,'triangles':report['triangles']}))


if __name__=='__main__':main()
