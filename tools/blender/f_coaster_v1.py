"""F coaster first 3D study. Run in a separate background Blender, never live scene.

Authored coordinates follow SeaSick: +X forward, +Y port, +Z up, 0.5m/unit.
New F1 join family. Not a replacement for the existing W1 meshes.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-staging/f-coaster-v1'
DECK = 1.76
RISE = 4.4  # 2.2m floor-to-floor; 2.10m clear under the floor slab.
STERN = 9.3
BAY = 6.0
BOW = 8.0
PALETTE = {
    'wood': (.48,.255,.098,1), 'woodlight': (.61,.35,.14,1),
    'wooddark': (.37,.17,.055,1), 'deck': (.64,.39,.19,1),
    'decklight': (.70,.445,.235,1), 'deckdark': (.57,.325,.14,1),
    'cream': (.83,.77,.59,1), 'sage': (.225,.32,.25,1),
    'sage2': (.18,.26,.21,1), 'bottom': (.105,.16,.145,1),
    'iron': (.095,.115,.11,1), 'brass': (.51,.36,.15,1),
    'canvas': (.87,.83,.70,1), 'rope': (.58,.49,.32,1),
    'dark': (.04,.049,.043,1), 'amber': (.92,.48,.11,1),
}
MATERIALS = {}
PARTS = []
MARKERS = []
PARENT = None


class Mesh:
    def __init__(self):
        self.v=[]; self.f=[]; self.c=[]

    def face(self, points, col):
        n=len(self.v); self.v.extend(points)
        self.f.append(tuple(range(n,n+len(points)))); self.c.append(col)

    def box(self, center, size, col, rotation=None):
        c=Vector(center); d=Vector(size)*.5
        pts=[Vector((x*d.x,y*d.y,z*d.z)) for x,y,z in
             [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        pts=[tuple(c+(rotation@p if rotation else p)) for p in pts]
        for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:
            self.face([pts[i] for i in f],col)

    def beam(self,a,b,width,depth,col):
        a=Vector(a); b=Vector(b); delta=b-a
        self.box((a+b)*.5,(width,depth,delta.length),col,delta.to_track_quat('Z','Y').to_matrix())

    def cylinder(self,a,b,r1,r2,col,n=12):
        a=Vector(a); b=Vector(b); q=(b-a).to_track_quat('Z','Y').to_matrix()
        rings=[]
        for p,r in [(a,r1),(b,r2)]:
            rings.append([tuple(p+q@Vector((r*math.cos(i*math.tau/n),r*math.sin(i*math.tau/n),0))) for i in range(n)])
        self.face(rings[0][::-1],col); self.face(rings[1],col)
        for i in range(n): self.face([rings[0][i],rings[0][(i+1)%n],rings[1][(i+1)%n],rings[1][i]],col)

    def ring(self,center,radius,thick,depth,col,n=32):
        x,y,z=center
        for i in range(n):
            a=i*math.tau/n; b=(i+1)*math.tau/n
            pts=[(x+r*math.sin(t),y+dy,z+r*math.cos(t))
                 for dy in [-depth/2,depth/2] for r in [radius-thick,radius] for t in [a,b]]
            for f in [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]:
                self.face([pts[j] for j in f],col)

    def object(self,name,bevel=0,export=True):
        me=bpy.data.meshes.new(name); me.from_pydata(self.v,[],self.f); me.update()
        o=bpy.data.objects.new(name,me); bpy.context.scene.collection.objects.link(o)
        if PARENT: o.parent=PARENT
        for mat in MATERIALS.values(): me.materials.append(mat)
        names=list(MATERIALS)
        colors=me.color_attributes.new(name='GameColor',type='FLOAT_COLOR',domain='CORNER')
        for p,c in zip(me.polygons,self.c):
            p.material_index=names.index(c)
            for j in p.loop_indices: colors.data[j].color=PALETTE[c]
        bm=bmesh.new(); bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
        if bevel:
            mod=o.modifiers.new('Small crafted edges','BEVEL'); mod.width=bevel; mod.segments=1
        if export: PARTS.append(o)
        return o


def marker(name,loc,**props):
    o=bpy.data.objects.new(name,None); bpy.context.scene.collection.objects.link(o)
    o.parent=PARENT; o.location=loc; o.empty_display_size=.25
    for k,v in props.items(): o[k]=v
    MARKERS.append(o); return o


def width(x,kind,h):
    if kind=='stern':
        return h*(.84+.16*math.sin(min(1,x/5.2)*math.pi/2))
    if kind=='bow':
        t=max(0,min(1,x/BOW))
        return h*max(.055,math.cos(t*math.pi/2))
    return h


def sheer(x,kind):
    if kind=='stern': return 1.0*(max(0,1-x/5.2)**2)
    if kind=='bow': return 2.05*(x/BOW)**3
    return 0


def curve_strip(mesh,pts,w,depth,col):
    # Rectangular ribbon follows the rail, unlike disconnected rotated cubes.
    rings=[]
    for i,p in enumerate(pts):
        tangent=Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(0,i-1)])
        out=Vector((-tangent.y,tangent.x,0)).normalized()*w/2
        p=Vector(p)
        rings.append([tuple(p-out+Vector((0,0,-depth/2))),tuple(p+out+Vector((0,0,-depth/2))),
                      tuple(p+out+Vector((0,0,depth/2))),tuple(p-out+Vector((0,0,depth/2)))])
    mesh.face(rings[0][::-1],col);mesh.face(rings[-1],col)
    for a,b in zip(rings,rings[1:]):
        for j in range(4):mesh.face([a[j],a[(j+1)%4],b[(j+1)%4],b[j]],col)


def deck(mesh,kind,h,start,end,z,col='deck'):
    # Broad crosswise boards, individually clipped to the actual hull plan.
    count=math.ceil((end-start)/.58)
    for i in range(count):
        a=start+(end-start)*i/count+.008; b=start+(end-start)*(i+1)/count-.008
        wa=width(a,kind,h)-.18; wb=width(b,kind,h)-.18
        if min(wa,wb)<.06: continue
        top=[(a,-wa,z),(b,-wb,z),(b,wb,z),(a,wa,z)]
        bot=[(x,y,z-.20) for x,y,z in top]
        c=['deck','decklight','deck','deckdark','deck'][i%5] if col=='deck' else col
        mesh.face(top,c);mesh.face(bot[::-1],c)
        for k in range(4):mesh.face([bot[k],bot[(k+1)%4],top[(k+1)%4],top[k]],c)


def hull(kind,h,length,levels):
    shell=Mesh(); trim=Mesh(); ribs=Mesh(); floor=Mesh()
    xs=[length*i/(20 if kind!='middle' else 8) for i in range((20 if kind!='middle' else 8)+1)]
    # Structural shell below main deck: separate closed port/starboard strips.
    profile=[(-1.92,.18),(-1.53,.46),(-.95,.72),(-.30,.89),(.38,.965),(1.06,.995),(DECK,1)]
    for s in [-1,1]:
        for j,((za,fa),(zb,fb)) in enumerate(zip(profile,profile[1:])):
            c=['bottom','bottom','sage2','wooddark','wood','woodlight'][j]
            for a,b in zip(xs,xs[1:]):
                outer=[(a,s*width(a,kind,h)*fa,za),(b,s*width(b,kind,h)*fa,za),
                       (b,s*width(b,kind,h)*fb,zb),(a,s*width(a,kind,h)*fb,zb)]
                inner=[(x,y-s*.16,z) for x,y,z in outer]
                shell.face(outer,c);shell.face(inner[::-1],c)
                for k in range(4): shell.face([outer[k],outer[(k+1)%4],inner[(k+1)%4],inner[k]],c)
        # Main gunwale is omitted where a raised stern encloses the lower deck.
        visible=[x for x in xs if not(kind=='stern' and levels and x<8.0)]
        if kind=='stern' and levels: visible=[8.0]+[x for x in visible if x>8.0]
        for a,b in zip(visible,visible[1:]):
            for j in range(2):
                za=DECK+j*(.92+sheer(a,kind))/2; zb=DECK+(j+1)*(.92+sheer(a,kind))/2
                zc=DECK+j*(.92+sheer(b,kind))/2; zd=DECK+(j+1)*(.92+sheer(b,kind))/2
                outer=[(a,s*width(a,kind,h),za),(b,s*width(b,kind,h),zc),
                       (b,s*width(b,kind,h),zd),(a,s*width(a,kind,h),zb)]
                inner=[(x,y-s*.19,z) for x,y,z in outer]
                shell.face(outer,'sage' if j==1 else 'woodlight');shell.face(inner[::-1],'wood')
                for k in range(4):shell.face([outer[k],outer[(k+1)%4],inner[(k+1)%4],inner[k]],'wood')
        curve_strip(trim,[(x,s*width(x,kind,h),DECK+.97+sheer(x,kind)) for x in visible],.46,.23,'cream')
        # Broad curved timber ribs run down to the bilge, not hairline details.
        for x in ([1.0,4.1,7.5] if kind=='stern' else [1.15,4.85] if kind=='middle' else [1.3,4.4,6.6]):
            top=DECK+.88+sheer(x,kind)
            if kind=='stern' and levels and x<5.2:top=DECK+levels*RISE+.90
            pts=[(x,s*(width(x,kind,h)*f+.08),z) for z,f in profile[1:]]
            pts += [(x,s*(width(x,kind,h)+.08),top+.22)]
            for a,b in zip(pts,pts[1:]):ribs.beam(a,b,.24,.29,'woodlight')
            ribs.box((x,s*(width(x,kind,h)+.08),top+.14),(.42,.42,.19),'cream')
            ribs.box((x,s*(width(x,kind,h)+.14),DECK+.25),(.32,.20,.27),'cream')
    # Curved cream transition connects the high aft cap into the low waist.
    if kind=='stern' and levels:
        for s in [-1,1]:
            points=[]
            for i in range(13):
                t=i/12;x=5.2+2.8*t
                z=DECK+.97+levels*RISE*(1-t)**3
                points.append((x,s*width(x,kind,h),z))
            curve_strip(trim,points,.46,.23,'cream')
            for a,b in zip(points,points[1:]):
                for yoff in [0,-s*.18]:
                    trim.face([(a[0],a[1]+yoff,a[2]-.56),(b[0],b[1]+yoff,b[2]-.56),
                               (b[0],b[1]+yoff,b[2]),(a[0],a[1]+yoff,a[2])],'sage')
                    trim.face([(a[0],a[1]+yoff,DECK),(b[0],b[1]+yoff,DECK),
                               (b[0],b[1]+yoff,b[2]-.56),(a[0],a[1]+yoff,a[2]-.56)],'woodlight')
    # keel floor and end transom; module join faces stay open.
    for a,b in zip(xs,xs[1:]):
        shell.face([(a,-width(a,kind,h)*.18,-1.92),(b,-width(b,kind,h)*.18,-1.92),
                    (b,width(b,kind,h)*.18,-1.92),(a,width(a,kind,h)*.18,-1.92)],'bottom')
    if kind=='stern':
        for (za,fa),(zb,fb) in zip(profile,profile[1:]):
            shell.face([(0,-width(0,kind,h)*fa,za),(0,width(0,kind,h)*fa,za),
                        (0,width(0,kind,h)*fb,zb),(0,-width(0,kind,h)*fb,zb)],'sage2' if zb<0 else 'wood')
    if kind=='bow':
        # Solid chamfered stem follows the upward rake of F's cream nose.
        ribs.beam((length-.5,0,-1.6),(length+.20,0,DECK+2.65),.42,.52,'woodlight')
        ribs.box((length+.20,0,DECK+2.75),(.65,.70,.23),'cream')
        shell.face([(length,-width(length,kind,h),DECK),(length,width(length,kind,h),DECK),
                    (length,0,-1.92)],'wooddark')
    deck(floor,kind,h,0,length,DECK)
    if kind=='stern' and not levels:
        curve_strip(trim,[(0,-width(0,kind,h),DECK+1.97),(0,0,DECK+2.05),(0,width(0,kind,h),DECK+1.97)],.34,.20,'cream')
        ribs.box((.10,0,DECK+.9),(.22,2*width(0,kind,h),1.7),'sage')
    shell.object('Hull_Shell');floor.object('Main_Deck');trim.object('Cap_Rails',.025);ribs.object('Timber_Frames',.035)
    marker('Join_Aft',(0,0,DECK),standard=f'F1-{h*2:.2f}')
    marker('Join_Forward',(length,0,DECK),standard=f'F1-{h*2:.2f}')
    marker('Deck_Aisle',(length/2,0,DECK),clear_width_m=1.4)


def upper_stern(h,levels):
    global PARENT
    roots=[]
    for level in range(1,levels+1):
        root=bpy.data.objects.new(f'Stern_Layer_{level}',None);bpy.context.scene.collection.objects.link(root)
        root['stack_level']=level;root['rise_u']=RISE;PARENT=root;roots.append(root)
        bottom=DECK+(level-1)*RISE;z=bottom+RISE
        shell=Mesh(); floor=Mesh(); rail=Mesh(); frame=Mesh(); stairs=Mesh()
        xs=[i*5.2/12 for i in range(13)]
        for s in [-1,1]:
            for i in range(6):
                for a,b in zip(xs,xs[1:]):
                    lo=bottom+i*RISE/6; hi=bottom+(i+1)*RISE/6
                    out=[(a,s*width(a,'stern',h),lo),(b,s*width(b,'stern',h),lo),
                         (b,s*width(b,'stern',h),hi),(a,s*width(a,'stern',h),hi)]
                    inner=[(x,y-s*.20,z) for x,y,z in out]
                    shell.face(out,['wood','woodlight','wood','wooddark','wood','sage'][i]);shell.face(inner[::-1],'wood')
                    for k in range(4):shell.face([out[k],out[(k+1)%4],inner[(k+1)%4],inner[k]],'wood')
            # Topmost curved rail blends down toward the low center deck.
            if level==levels:
                curve_strip(rail,[(x,s*width(x,'stern',h),z+.92+.45*(1-x/5.2)**2) for x in xs],.39,.2,'cream')
                for a,b in zip(xs,xs[1:]):
                    rail.face([(a,s*width(a,'stern',h),z),(b,s*width(b,'stern',h),z),
                               (b,s*width(b,'stern',h),z+.87+.45*(1-b/5.2)**2),
                               (a,s*width(a,'stern',h),z+.87+.45*(1-a/5.2)**2)],'sage')
        # Closed rear wall; front portal leaves a full-size center doorway.
        shell.box((.13,0,bottom+RISE/2),(.26,2*width(0,'stern',h),RISE),'wood')
        front_half=(2*h-2.4)/2
        for s in [-1,1]:
            shell.box((5.07,s*(1.2+front_half/2),bottom+RISE/2),(.26,front_half,RISE),'wood')
            frame.beam((5.23,s*1.2,bottom),(5.23,s*1.2,z-.20),.20,.20,'woodlight')
        frame.beam((5.22,-1.3,z-.30),(5.22,1.3,z-.30),.23,.23,'cream')
        deck(floor,'stern',h,0,5.2,z)
        # One 1m-wide flight per storey. Same opening every time; center aisle remains open.
        y=h-1.35
        for i in range(12):
            x=8.9-i*3.7/12
            stairs.box((x,y,bottom+(i+1)*RISE/12-.09),(.33,2.0,.18),'decklight')
        for sy in [y-1.08,y+1.08]:
            stairs.beam((9.02,sy,bottom+.1),(5.17,sy,z-.10),.16,.24,'wooddark')
            stairs.beam((8.98,sy,bottom+1.30),(5.18,sy,z+1.30),.13,.16,'cream')
            for x,t in [(8.9,0),(7.05,.5),(5.2,1)]:
                stairs.beam((x,sy,bottom+t*RISE),(x,sy,bottom+t*RISE+1.3),.15,.15,'woodlight')
        # Guard the front drop; opening aligns with the stairs, not with the door below.
        if level==levels:
            for a,b in [(-h+.2,y-1.08),(y+1.08,h-.15)]:
                if b>a:
                    rail.beam((5.2,a,z+1.3),(5.2,b,z+1.3),.19,.22,'cream')
                    for yy in [a,(a+b)/2,b]:rail.beam((5.2,yy,z),(5.2,yy,z+1.3),.18,.18,'woodlight')
            # Arched cream/sage transom is F's signature.
            pts=[(-.04,yy,z+1.05+.38*(1-(yy/(.84*h))**2)) for yy in [h*.84*(-1+i/12*2) for i in range(13)]]
            curve_strip(rail,pts,.40,.22,'cream')
            for a,b in zip(pts,pts[1:]):rail.face([(a[0],a[1],z),(b[0],b[1],z),b,a],'sage')
        shell.object('Layer_Walls');floor.object('Layer_Floor');rail.object('Layer_Rails',.025)
        frame.object('Door_Frame',.025);stairs.object('Stairs',.025)
        marker('Stair_Bottom',(9.10,y,bottom),clear_width_m=.92)
        marker('Stair_Top',(4.80,y,z),clear_width_m=.92)
        marker('Through_Door',(5.2,0,bottom),clear_width_m=1.1,clear_height_m=2.0)
    return roots


def paddle(h,levels):
    global PARENT
    ztop=DECK+levels*RISE+.80
    water=-.45; tip=water-.55
    radius=(ztop-tip)/2
    axle_z=tip+radius
    axle_x=-radius-.38
    drum_width=2*h-2.00
    root=bpy.data.objects.new('Paddle_Module',None);bpy.context.scene.collection.objects.link(root);PARENT=root
    rotor=Mesh();support=Mesh();hw=drum_width/2
    # Rotor mesh is genuinely axle-local for runtime rotation.
    rotor.cylinder((0,-hw-.30,0),(0,hw+.30,0),.21,.21,'iron',12)
    for s in [-1,1]:
        yy=s*(hw-.10)
        rotor.ring((0,yy,0),radius-.04,.43,.42,'woodlight')
        rotor.cylinder((0,yy-s*.20,0),(0,yy+s*.29,0),.42,.34,'iron',10)
        for i in range(8):
            a=i*math.tau/8
            rotor.beam((.30*math.sin(a),yy,.30*math.cos(a)),((radius-.20)*math.sin(a),yy,(radius-.20)*math.cos(a)),.32,.34,'woodlight')
    count=max(10,round(math.tau*radius/1.75))
    for i in range(count):
        a=i*math.tau/count;blade_depth=.72+.065*radius;rr=radius-blade_depth/2
        rot=Matrix.Rotation(a,3,'Y')
        rotor.box((rr*math.sin(a),0,rr*math.cos(a)),(.20,drum_width,blade_depth),'woodlight' if i%3 else 'wood',rot)
    obj=rotor.object('Paddle_Rotor',.016);obj.location=(axle_x,0,axle_z);obj['spin_axis']='local Y'
    for s in [-1,1]:
        yy=s*(hw+.37)
        support.beam((1.1,yy,DECK-.30),(axle_x,yy,axle_z),.35,.40,'woodlight')
        support.beam((.75,yy,DECK+levels*RISE+.25),(axle_x,yy,axle_z),.32,.35,'wood')
        support.cylinder((axle_x,yy-s*.25,axle_z),(axle_x,yy+s*.15,axle_z),.38,.38,'iron',10)
    support.object('Paddle_Carrier',.035)
    marker('Axle',(axle_x,0,axle_z),radius_u=radius,width_u=drum_width)
    return {'radius_u':radius,'width_u':drum_width,'axle_u':[axle_x,0,axle_z],
            'tip_u':tip,'design_waterline_u':water,'forward_clearance_u':.38,'blade_count':count}


def fittings(h,levels):
    global PARENT
    root=bpy.data.objects.new('Stern_Fittings',None);bpy.context.scene.collection.objects.link(root);PARENT=root
    z=DECK+levels*RISE; m=Mesh()
    # Shallow canvas shade behind the helm, generous full-height clearance.
    for x in [.75,3.65]:
        for y in [-1.85,1.85]:
            m.beam((x,y,z),(x,y,z+4.65),.18,.18,'woodlight')
            m.box((x,y,z+4.64),(.33,.33,.18),'cream')
    for y in [-1.85,1.85]:m.beam((.6,y,z+4.52),(3.8,y,z+4.52),.2,.22,'woodlight')
    for x in [.65,3.75]:m.beam((x,-1.98,z+4.52),(x,1.98,z+4.52),.2,.22,'woodlight')
    # Four sloping fabric panels make a low, lightly sagging tarp.
    center=(2.2,0,z+4.67)
    corners=[(.55,-2.02,z+4.48),(3.85,-2.02,z+4.48),(3.85,2.02,z+4.48),(.55,2.02,z+4.48)]
    for i in range(4):m.face([corners[i],corners[(i+1)%4],center],'canvas')
    m.box((3.0,0,z+.68),(.42,.44,1.36),'wooddark')
    # Helm ring rotates about ship X; use a temporary Y-axis mesh then rotate.
    helm=Mesh();helm.ring((0,0,0),.67,.11,.15,'woodlight',16)
    for i in range(8):
        a=i*math.tau/8
        helm.beam((0,0,0),(.83*math.sin(a),0,.83*math.cos(a)),.10,.10,'woodlight')
    helm.cylinder((0,-.16,0),(0,.16,0),.16,.16,'brass',10)
    wheel=helm.object('Helm',.012);wheel.rotation_euler.z=math.pi/2;wheel.location=(3.10,0,z+1.40)
    # Chimney is outboard, keeping the helm approach and central lane clear.
    cy=-h+1.05
    m.box((2.5,cy,z+.40),(1.05,1.0,.80),'wood')
    m.cylinder((2.5,cy,z+.75),(2.5,cy,z+4.45),.38,.32,'iron',8)
    m.cylinder((2.5,cy,z+3.85),(2.5,cy,z+4.15),.43,.43,'sage',8)
    # Black recess at chimney mouth, no bright cap.
    m.cylinder((2.5,cy,z+4.45),(2.5,cy,z+4.49),.26,.26,'dark',8)
    m.object('Canopy_Chimney',.022)
    marker('Helm_Stand',(4.15,0,z));marker('Chimney',(2.5,cy,z))


def equipment(h,n):
    global PARENT
    root=bpy.data.objects.new('Review_Equipment',None);bpy.context.scene.collection.objects.link(root);PARENT=root
    # New simple greybox cannons: true dimensional obstacles, not final gun art.
    for side in [-1,1]:
        gun=Mesh();x=STERN+3; y=side*(h-1.35)
        gun.box((x,y,DECK+.64),(1.7,1.7,.92),'wooddark')
        for xx in [x-.71,x+.71]:
            for yy in [y-.57,y+.57]:gun.cylinder((xx-.12,yy,DECK+.29),(xx+.12,yy,DECK+.29),.29,.29,'iron',10)
        gun.cylinder((x,y-side*.80,DECK+1.48),(x,y+side*1.40,DECK+1.56),.38,.27,'iron',12)
        gun.cylinder((x,y+side*1.405,DECK+1.56),(x,y+side*1.43,DECK+1.56),.18,.18,'dark',12)
        gun.object('Review_Cannon_Port' if side>0 else 'Review_Cannon_Starboard',.025,False)
        marker('Cannon_Stand_'+str(side),(x,y-side*1.30,DECK),review_only=True)
    cargo=Mesh(); x=STERN+n*BAY+1.7
    for side in [-1,1]:
        y=side*(width(2.6,'bow',h)-1.20)
        cargo.box((x,y,DECK+.75),(1.65,1.65,1.5),'wood')
        for xx in [x-.72,x+.72]:cargo.box((xx,y,DECK+.78),(.14,1.74,1.65),'woodlight')
        for yy in [y-.77,y+.77]:cargo.box((x,yy,DECK+.77),(1.72,.12,1.66),'woodlight')
    cargo.object('Review_Cargo',.04,False)


def crew(h,levels):
    global PARENT
    PARENT=None
    # Existing game sailor, normalized to 1.8m tall in this authoring system.
    path=ROOT/'tools/blender/exports/crew-weathered-v2/male-coat-deckhand.glb'
    before=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    obs=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
    if not obs:return
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:o.select_set(True)
    bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join()
    o=bpy.context.object;o.name='Review_Deckhand'
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    points=[o.matrix_world@v.co for v in o.data.vertices]
    lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    factor=3.6/(hi.z-lo.z)
    for v in o.data.vertices:v.co=(o.matrix_world@v.co-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)))*factor
    o.matrix_world=Matrix.Identity(4);o.location=(STERN+1.3,-.25,DECK);o.rotation_euler.z=-math.pi/2
    o['review_only']=True
    other=o.copy();other.data=o.data; bpy.context.scene.collection.objects.link(other)
    other.name='Review_Helmsman';other.location=(4.10,0,DECK+levels*RISE);other.rotation_euler.z=-math.pi/2


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name,col in PALETTE.items():
        m=bpy.data.materials.new('F_'+name);m.diffuse_color=col;m.use_nodes=True
        node=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        node.inputs['Base Color'].default_value=col;node.inputs['Roughness'].default_value=.8
        MATERIALS[name]=m
    scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Warm studio');scene.world.use_nodes=True
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.68,.73,.72,1);bg.inputs[1].default_value=.6
    scene.view_settings.view_transform='AgX'
    for name,pos,power,size in [('Key',(4,-13,24),3200,16),('Fill',(8,16,18),2200,13),('Rim',(-12,4,18),2700,10)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
        ob=bpy.data.objects.new(name,data);scene.collection.objects.link(ob);ob.location=pos
        ob.rotation_euler=(Vector((9,0,2))-ob.location).to_track_quat('-Z','Y').to_euler()
    mat=bpy.data.materials.new('Studio ground');mat.diffuse_color=(.30,.345,.32,1);mat.use_nodes=True
    node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=mat.diffuse_color
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-2.10));g=bpy.context.object;g.name='Studio_Ground';g.data.materials.append(mat)
    ca=bpy.data.cameras.new('Camera');cam=bpy.data.objects.new('Camera',ca);scene.collection.objects.link(cam);scene.camera=cam;ca.type='ORTHO';ca.lens=45


def shot(path,eye,target,scale,res=(1600,1100)):
    scene=bpy.context.scene;cam=scene.camera;cam.location=eye
    cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x=res[0];scene.render.resolution_y=res[1]
    # Fit to actual geometry, keeping equal breathing room above and below.
    if path.name=='hero.png':
        inv=cam.matrix_world.inverted()
        bpy.context.view_layer.update();inv=cam.matrix_world.inverted()
        points=[inv@(o.matrix_world@Vector(v)) for o in scene.objects
                if o.type=='MESH' and o.name!='Studio_Ground' and not o.hide_render for v in o.bound_box]
        xmin=min(p.x for p in points);xmax=max(p.x for p in points)
        ymin=min(p.y for p in points);ymax=max(p.y for p in points)
        cam.location+=cam.rotation_euler.to_matrix()@Vector(((xmin+xmax)/2,(ymin+ymax)/2,0))
        cam.data.ortho_scale=max(xmax-xmin,(ymax-ymin)*res[0]/res[1])*1.13
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def export(dir):
    records=[]
    # One file per authored group, transforms retained relative to its section root.
    parents=list(dict.fromkeys(o.parent for o in PARTS))
    for parent in parents:
        obs=[o for o in PARTS if o.parent==parent]
        base=parent.location.copy();parent.location=(0,0,0)
        bpy.ops.object.select_all(action='DESELECT')
        for o in obs:o.select_set(True)
        bpy.context.view_layer.objects.active=obs[0]
        target=dir/(parent.name+'.fbx')
        bpy.ops.export_scene.fbx(filepath=str(target),use_selection=True,object_types={'MESH'},
            apply_unit_scale=False,axis_forward='-Z',axis_up='Y',bake_space_transform=False,
            use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False)
        parent.location=base
        records.append({'group':parent.name,'file':target.name,'assembly_position_u':list(base),
                        'parts':[o.name for o in obs]})
    return records


def main():
    global PARENT
    parser=argparse.ArgumentParser();parser.add_argument('--width',type=float,default=9.28)
    parser.add_argument('--stern-levels',type=int,default=1);parser.add_argument('--middles',type=int,default=1)
    parser.add_argument('--name',default='narrow-raised');parser.add_argument('--quick',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.stern_levels not in (0,1):
        parser.error('V1 has verified low and one-layer sterns only; additional storeys need a connected stair/landing design.')
    if args.middles<1 or args.middles>3:
        parser.error('V1 review layouts need 1–3 middle bays.')
    if args.width not in (9.28,12.08):
        parser.error('V1 authored beam options are 9.28 and 12.08 units.')
    setup();h=args.width/2;n=args.middles;level=args.stern_levels
    dest=OUT/args.name;dest.mkdir(parents=True,exist_ok=True)
    sections=[('Stern','stern',0,STERN)]+[(f'Middle_{i}','middle',STERN+i*BAY,BAY) for i in range(n)]+[('Bow','bow',STERN+n*BAY,BOW)]
    for name,kind,x,length in sections:
        root=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(root);root.location.x=x;root['length_u']=length;PARENT=root
        hull(kind,h,length,level if kind=='stern' else 0)
    upper_stern(h,level);wheel=paddle(h,level);fittings(h,level);equipment(h,n);crew(h,level)
    PARENT=None
    total=STERN+n*BAY+BOW
    report={'family':'F1-study','metres_per_unit':.5,'axes':'source (x,y,z) -> game (-y,z,x) * 0.5',
            'beam_u':args.width,'stern_levels':level,'layer_rise_u':RISE,'clear_layer_height_m':(RISE-.2)*.5,
            'middle_count':n,'middle_length_u':BAY,'paddle':wheel,
            'section_placements':[{'name':name,'x_u':x,'length_u':length} for name,kind,x,length in sections],
            'review_cannon_envelope_u':{'length_x':1.94,'depth_y':2.3},
            'center_clear_width_between_gun_carriages_m':(args.width-4.4)*.5,
            'planned_crew_capsule':{'height_m':1.8,'radius_m':.3},
            'status':'Art prototype; not installed or playtested. Cannon meshes are review proxies. No runtime collision/navigation certification.'}
    models=dest/'models';models.mkdir(exist_ok=True);report['exports']=export(models)
    report['markers']=[{'name':o.name,'section':o.parent.name if o.parent else '', 'position_u':list(o.location)} for o in MARKERS]
    report['triangles']=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in PARTS)
    (dest/'manifest.json').write_text(json.dumps(report,indent=2))
    target=(total*.42,0,2.4+level*.95);eye=(-26,-34,27+level*3)
    shot(dest/'hero.png',eye,target,39+level*2)
    bpy.ops.wm.save_as_mainfile(filepath=str(dest/'ship.blend'))
    if not args.quick:
        shot(dest/'top.png',(total*.43,0,65),(total*.43,0,0),35+level*2,(1500,1150))
        shot(dest/'side.png',(total*.40,-65,5),(total*.40,0,3.5),37+level*2,(1700,850))
        shot(dest/'deck-detail.png',(STERN+9,-15,19),(STERN+2,0,DECK),19,(1400,1100))
    print('F_COASTER_DONE '+json.dumps({'output':str(dest),'triangles':report['triangles'],'paddle':wheel}))


if __name__=='__main__':main()
