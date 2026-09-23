"""Reference-driven stern steamer. Blender authoring: X bow, Y port, Z up.

Run with Blender --background --factory-startup --python this_file.
Closed hull shell includes the paddle recess; fittings remain separate solids.
"""
import json
import math
import os
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import steamer as ss

ROOT = HERE.parent.parent
OUT = ROOT / 'art-staging/stern-paddle-astra-model-v8'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = HERE / 'source/stern-paddle-astra-v8.blend'
EXPORT = OUT / 'models'
EXPORT.mkdir(exist_ok=True)

# Proportions follow the rear, side and rear-three-quarter references.
# Ship coordinates are retained for compatibility with the existing exporter.
ZT = -11.55
ZF = 12.4
WF = -8.95
HF = -8.6
AXLE = (0, 0.35, -10.83)
R = 1.62
HW = 2.08
OPEN = 2.43
ST = [-11.55, -10.8, -10.35, -9.8, -8.95, -8.4, -7, -5, -2.5, 0,
      2.5, 5, 7, 8.6, 9.9, 10.9, 11.7, 12.4]
BEAM = [3.85, 4.00, 4.04, 4.22, 4.38, 4.48, 4.58, 4.64, 4.68, 4.66,
        4.60, 4.43, 4.08, 3.53, 2.80, 1.97, 1.08, .08]
FRAC = [i/16 for i in range(-16,17)]
CONTROL_ST = ST[:]
CONTROL_BEAM = BEAM[:]
MATS = {
    'wood': '#C36B2D', 'wood2': '#D77C31', 'wood3': '#E09243',
    'bottom': '#804322', 'deck': '#DB9147', 'deck2': '#D18A45',
    'deck3': '#D99049', 'teal': '#7DA798', 'iron': '#49434A',
    'iron2': '#37333B', 'bolt': '#938786', 'rim': '#BF7136',
    'blade': '#D8903C', 'dark': '#241F25', 'seam': '#BA8043',
}
ss.PAL.update({k: ss._hex(v) for k, v in MATS.items()})


def hermite(x, knots, values):
    j=next((j for j in range(len(knots)-1) if x<=knots[j+1]),len(knots)-2)
    h=knots[j+1]-knots[j]
    t=max(0,min(1,(x-knots[j])/h))
    slopes=[(values[k+1]-values[k])/(knots[k+1]-knots[k]) for k in range(len(knots)-1)]
    def tangent(k):
        if k==0: return slopes[0]
        if k==len(values)-1: return slopes[-1]
        a,b=slopes[k-1:k+1]
        return 0 if a*b<=0 else 2*a*b/(a+b)
    return ((2*t**3-3*t*t+1)*values[j]+(t**3-2*t*t+t)*h*tangent(j)
            +(-2*t**3+3*t*t)*values[j+1]+(t**3-t*t)*h*tangent(j+1))


# Extra stations follow a shape-preserving curve, not linear subdivisions.
ST=sorted(set(CONTROL_ST+[a+(b-a)*t/3 for a,b in zip(CONTROL_ST,CONTROL_ST[1:]) for t in [1,2]]))
# A quarter-ellipse ends tangent to the transom instead of a straight tail.
# Closely spaced end stations preserve the rounded return at game scale.
ST=sorted(set(ST+[ZT+d for d in [.025,.06,.12,.22,.36,.52]]))
def stern_beam(z):
    t=max(0,min(1,(z-ZT)/(-5-ZT)))
    return 3.85+(4.64-3.85)*math.sqrt(max(0,1-(1-t)**2))
BEAM=[stern_beam(z) if z<=-5 else hermite(z,CONTROL_ST,CONTROL_BEAM) for z in ST]


def interp(z, vals):
    for i in range(len(ST)-1):
        if z<=ST[i+1]:
            t=max(0,(z-ST[i])/(ST[i+1]-ST[i]))
            return vals[i]*(1-t)+vals[i+1]*t
    return vals[-1]


def smooth(t):
    t = max(0, min(1, t))
    return t*t*(3-2*t)


def deck(z, x=0):
    # The working deck stays level through the stern and helm area.
    return 1.76 + .57*smooth((z-7)/5.4)


def housing(z, x):
    # A short, separate-looking casing is built into the hull shell.
    # The exterior bulkhead at HF sits ahead of the internal wheel cavity.
    width=interp(z,BEAM)
    crown=hermite(abs(x)/width,[0,.2,.5,.75,.9,1],[1.30,1.28,1.12,.80,.16,0])
    return deck(z)+crown


def roof(z,x):
    return housing(z,x) if z<=HF+.0001 else deck(z,x)


def keel(z):
    return -1.92 + .42*smooth((-z-8)/2.8) + 1.20*smooth((z-7.5)/4.9)


def profile(z):
    w = interp(z, BEAM)
    d = deck(z, w)
    k = keel(z)
    ts = [0, .17, .35, .53, .71, .87, 1]
    widths = [.66, .83, .94, 1.005, 1.025, 1.015, 1]
    levels=[i/12 for i in range(13)]
    return [(w*hermite(t,ts,widths),k+(d-k)*t) for t in levels]


def mesh(name, b, parent=None):
    # Adjacent solid segments share end caps; remove BOTH before welding.
    keys={}
    for i,face in enumerate(b.faces):
        key=tuple(sorted(tuple(round(c,6) for c in b.verts[v]) for v in face))
        keys.setdefault(key,[]).append(i)
    remove={i for ids in keys.values() if len(ids)>1 for i in ids}
    b.faces=[f for i,f in enumerate(b.faces) if i not in remove]
    b.cols=[c for i,c in enumerate(b.cols) if i not in remove]
    mat = ss.make_material('SeaSick_VertexPaint', ss.PAL['wood'])
    o = ss.to_object(name, b, mat)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00001)
    bm.verts.index_update()
    faces_by_key={}
    for face in bm.faces:
        key=tuple(sorted(v.index for v in face.verts))
        faces_by_key.setdefault(key,[]).append(face)
    internal=[f for fs in faces_by_key.values() if len(fs)>1 for f in fs]
    if internal:
        bmesh.ops.delete(bm,geom=internal,context='FACES_ONLY')
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    o.parent = parent
    # Small baked bevels carry silhouette highlights into the game export.
    widths={'Hull_Shell':.045,'Rails_Teal':.035,'Hull_Ironwork':.025,
            'Chimney':.035,'Helm':.018,'Paddle_Rotor':.028,'Paddle_Frame':.035,
            'Housing_Lid':.045,'Housing_Ironwork':.018,'Housing_AccessPanel':.018}
    if name in widths:
        bpy.context.view_layer.objects.active=o
        mod=o.modifiers.new('Edge highlights','BEVEL')
        mod.width=widths[name]
        mod.segments=2 if name in {'Rails_Teal','Paddle_Rotor'} else 1
        mod.limit_method='ANGLE'
        mod.angle_limit=math.radians(38)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


def cap(b, pts, col, hint):
    # Tessellate concave transom explicitly instead of relying on ngon export.
    poly = [Vector(p) for p in pts]
    for tri in tessellate_polygon([poly]):
        b.face([poly[v] if isinstance(v, int) else v for v in tri], col, hint=hint)


def hull():
    b = ss.Builder()
    for za, zb in zip(ST, ST[1:]):
        pa, pb = profile(za), profile(zb)
        for k in range(len(pa)-1):
            for s in [-1, 1]:
                b.face([(s*pa[k][0], pa[k][1], za),
                        (s*pb[k][0], pb[k][1], zb),
                        (s*pb[k+1][0], pb[k+1][1], zb),
                        (s*pa[k+1][0], pa[k+1][1], za)],
                       ['bottom','wood','wood2','wood','wood2','wood3'][min(5,k//2)],
                       hint=(s, -.3 if k<2 else 0, 0))
        wa, wb = interp(za, BEAM), interp(zb, BEAM)
        top=deck if zb<=HF+.0001 else deck
        for i in range(len(FRAC)-1):
            a,c=FRAC[i:i+2]
            b.face([(a*wa,top(za,a*wa),za),(a*wb,top(zb,a*wb),zb),
                    (c*wb,top(zb,c*wb),zb),(c*wa,top(za,c*wa),za)],
                   'wood3' if top==deck else 'deck',hint=(0,1,0))
        if abs(zb-HF)<.0001:
            for a,c in zip(FRAC,FRAC[1:]):
                b.face([(a*wb,deck(zb),zb),(c*wb,deck(zb),zb),
                        (c*wb,deck(zb,c*wb),zb),(a*wb,deck(zb,a*wb),zb)],
                       'wood2',hint=(0,0,1))
        if zb <= WF+.0001:
            # The cavity is open downward to water. Side bottoms, walls and
            # ceiling share the same station coordinates as the outer shell.
            ca, cb = deck(za, OPEN)-.18, deck(zb, OPEN)-.18
            for s in [-1, 1]:
                b.face([(s*pa[0][0],pa[0][1],za),(s*OPEN,pa[0][1],za),
                        (s*OPEN,pb[0][1],zb),(s*pb[0][0],pb[0][1],zb)],
                       'bottom',hint=(0,-1,0))
                b.face([(s*OPEN,pa[0][1],za),(s*OPEN,ca,za),
                        (s*OPEN,cb,zb),(s*OPEN,pb[0][1],zb)],
                       'dark',hint=(-s,0,0))
            b.face([(-OPEN,ca,za),(OPEN,ca,za),(OPEN,cb,zb),(-OPEN,cb,zb)],
                   'wood',hint=(0,-1,0))
        else:
            # Split the bottom at the same positions as the cavity bulkhead.
            limits_a = [-pa[0][0], -min(OPEN,pa[0][0]), min(OPEN,pa[0][0]), pa[0][0]]
            limits_b = [-pb[0][0], -min(OPEN,pb[0][0]), min(OPEN,pb[0][0]), pb[0][0]]
            for i in range(3):
                b.face([(limits_a[i],pa[0][1],za),(limits_b[i],pb[0][1],zb),
                        (limits_b[i+1],pb[0][1],zb),(limits_a[i+1],pa[0][1],za)],
                       'bottom',hint=(0,-1,0))

    for z, aft in [(ZT,True),(ZF,False)]:
        p = profile(z)
        w = interp(z, BEAM)
        pts = [(-x,y,z) for x,y in p]
        pts += [(f*w,deck(z,f*w),z) for f in FRAC[1:]]
        pts += [(x,y,z) for x,y in reversed(p[:-1])]
        if aft:
            c = deck(z, OPEN)-.18
            pts += [(OPEN,keel(z),z),(OPEN,c,z),(-OPEN,c,z),(-OPEN,keel(z),z)]
        cap(b,pts,'wood2',(0,0,-1 if aft else 1))
    c = deck(WF,OPEN)-.18
    b.face([(-OPEN,keel(WF),WF),(-OPEN,c,WF),(OPEN,c,WF),(OPEN,keel(WF),WF)],
           'dark',hint=(0,0,-1))
    return finish_hull(mesh('Hull_Core',b))


def rounded_plan(width, rear, front, radius, steps=8):
    points=[]
    for cx,cz,start in [(width-radius,front-radius,0),
                        (-width+radius,front-radius,90),
                        (-width+radius,rear+radius,180),
                        (width-radius,rear+radius,270)]:
        for i in range(steps+1):
            a=math.radians(start+i*90/steps)
            points.append((cx+radius*math.cos(a),cz+radius*math.sin(a)))
    return points


def casing_solid(name,width,rear,front,radius,levels,colors,crown=0):
    b=ss.Builder()
    plan=rounded_plan(width,rear,front,radius)
    rings=[[(x,y+(crown if k else 0)*max(0,1-(x/width)**2),z) for x,z in plan]
           for k,y in enumerate(levels)]
    # Body layers are true plank courses, with a shared closed surface.
    for k,(a,c) in enumerate(zip(rings,rings[1:])):
        for i in range(len(plan)):
            j=(i+1)%len(plan)
            b.face([a[i],a[j],c[j],c[i]],colors[k%len(colors)],away=(0,2,(rear+front)/2))
    b.face(rings[0],colors[0],hint=(0,-1,0))
    # Fan to the centre avoids a nonplanar ngon on the gently crowned lid.
    middle=(0,levels[-1]+crown,(rear+front)/2)
    for i in range(len(plan)):
        b.face([rings[-1][i],rings[-1][(i+1)%len(plan)],middle],colors[-1],hint=(0,1,0))
    o=mesh(name,b)
    if name=='Housing_Lid':
        for p in o.data.polygons:
            p.use_smooth=p.normal.z>.95
    return o


def boolean_into(target,other,operation):
    bpy.context.view_layer.objects.active=target
    mod=target.modifiers.new('Joinery '+operation,'BOOLEAN')
    mod.operation=operation
    mod.solver='EXACT'
    mod.object=other
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(other,do_unlink=True)


def finish_hull(shell):
    body=casing_solid('CasingBody',2.90,ZT+.06,HF,.46,
                      [1.67,1.94,2.21,2.48],['wood','wood2','wood'])
    boolean_into(shell,body,'UNION')
    # The transom crown is a narrow structural wall, not a raised deck.
    b=ss.Builder()
    cross=[(f*BEAM[0],housing(ZT,f*BEAM[0])) for f in FRAC]
    for (xa,ya),(xb,yb) in zip(cross,cross[1:]):
        q=[(xa,deck(ZT)-.10,ZT-.025),(xb,deck(ZT)-.10,ZT-.025),
           (xb,yb,ZT-.025),(xa,ya,ZT-.025)]
        v=[(x,y,ZT+.22) for x,y,z in q]
        b.solid([q,v,[q[0],q[1],v[1],v[0]],[q[1],q[2],v[2],v[1]],
                 [q[2],q[3],v[3],v[2]],[q[3],q[0],v[0],v[3]]],'wood2')
    boolean_into(shell,mesh('TransomCrown',b),'UNION')
    b=ss.Builder()
    b.box(-OPEN,OPEN,-4,2.17,ZT-2,WF,'dark')
    boolean_into(shell,mesh('WheelClearance',b),'DIFFERENCE')
    shell.name='Hull_Shell'
    bpy.context.view_layer.objects.active=shell
    mod=shell.modifiers.new('Joinery edge highlights','BEVEL')
    mod.width=.026;mod.segments=1;mod.limit_method='ANGLE'
    mod.angle_limit=math.radians(42)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bm=bmesh.new();bm.from_mesh(shell.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.00005)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(shell.data);bm.free()
    return shell


def strap(b,x,path,width=.43,thickness=.075):
    rings=[]
    for i,(y,z) in enumerate(path):
        a=Vector(path[max(0,i-1)]);c=Vector(path[min(len(path)-1,i+1)])
        tangent=(c-a).normalized()
        ny,nz=-tangent.y,tangent.x
        rings.append([(x+xx,y+ny*t,z+nz*t)
                      for xx,t in [(-width/2,-thickness/2),(width/2,-thickness/2),
                                   (width/2,thickness/2),(-width/2,thickness/2)]])
    for a,c in zip(rings,rings[1:]):
        for i in range(4):
            b.face([a[i],a[(i+1)%4],c[(i+1)%4],c[i]],'iron',
                   away=(x,2.15,(ZT+HF)/2))
    b.face(rings[0],'iron',hint=(0,0,-1))
    b.face(rings[-1],'iron',hint=(0,-1,0))


def housing_fittings():
    lid=casing_solid('Housing_Lid',2.96,ZT+.055,HF+.055,.49,
                     [2.48,2.64],['wood3'],crown=.12)
    b=ss.Builder();trim=ss.Builder()
    for x in [-2.32,2.32]:
        top=2.64+.12*(1-(x/2.96)**2)+.045
        path=[(top,ZT+.26),(top,HF-.08)]
        r=.18
        for i in range(1,7):
            a=i*math.pi/12
            path.append((top-r+r*math.cos(a),HF-.08+r*math.sin(a)))
        path.append((1.80,HF+.10))
        strap(b,x,path)
        for z in [ZT+.6,HF-.5]:
            b.cyl((x,top+.03,z),(x,top+.09,z),.10,.10,8,'bolt')
        for y in [1.94,2.35]:
            b.cyl((x,y,HF+.14),(x,y,HF+.21),.12,.12,6,'bolt')
    # A wooden inset sits behind a continuous open iron frame.
    trim.box(-1.12,1.12,1.95,2.36,HF+.008,HF+.043,'rim')
    outer=[(-1.24,1.90),(1.24,1.90),(1.24,2.42),(-1.24,2.42)]
    inner=[(-1.06,2.02),(1.06,2.02),(1.06,2.30),(-1.06,2.30)]
    for i in range(4):
        j=(i+1)%4
        q=[(*outer[i],HF+.04),(*outer[j],HF+.04),(*inner[j],HF+.04),(*inner[i],HF+.04)]
        v=[(x,y,HF+.125) for x,y,z in q]
        b.solid([q,v,[q[0],q[1],v[1],v[0]],[q[1],q[2],v[2],v[1]],
                 [q[2],q[3],v[3],v[2]],[q[3],q[0],v[0],v[3]]],'iron')
    for x in [-1.15,1.15]:
        for y in [1.96,2.36]:
            b.cyl((x,y,HF+.125),(x,y,HF+.18),.065,.065,8,'bolt')
    # Lid joints follow its surface; they do not bend the surrounding deck.
    for z in [ZT+.80,ZT+1.55,ZT+2.20]:
        pts=[(x,2.64+.12*(1-(x/2.96)**2)+.008,z) for x in [i*.2 for i in range(-13,14)]]
        tube(trim,pts,.012,.01,'seam')
    return [lid,mesh('Housing_Ironwork',b),mesh('Housing_AccessPanel',trim)]


def tube(b, points, width, height, col):
    # Rectangular section swept along a horizontal plan path with vertical rise.
    if col=='seam':
        simple=[points[0]]
        for i in range(1,len(points)-1):
            a=Vector(points[i])-Vector(simple[-1])
            c=Vector(points[i+1])-Vector(points[i])
            if a.cross(c).length>1e-6:
                simple.append(points[i])
        points=simple+[points[-1]]
    rings=[]
    for i,p in enumerate(points):
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])
        side=Vector((-tangent.z,0,tangent.x))
        if side.length<1e-8:
            side=Vector((1,0,0))
        side=side.normalized()*width/2
        v=Vector(p)
        rings.append([tuple(v-side+Vector((0,-height/2,0))),
                      tuple(v+side+Vector((0,-height/2,0))),
                      tuple(v+side+Vector((0,height/2,0))),
                      tuple(v-side+Vector((0,height/2,0)))])
    for a,c in zip(rings,rings[1:]):
        center=tuple((Vector(a[0])+Vector(c[2]))/2)
        for j in range(4):
            b.face([a[j],c[j],c[(j+1)%4],a[(j+1)%4]],col,away=center)
    b.face(rings[0],col,away=points[1])
    b.face(rings[-1],col,away=points[-2])


def fittings():
    rail=ss.Builder()
    iron=ss.Builder()
    seams=ss.Builder()
    # One cap rail wraps continuously from bow, over the raised stern, to bow.
    def side_cap(z,s):
        x=s*interp(z,BEAM)
        return (x,deck(z,x)+.14+.33*smooth((z-ZT)/2.3),z)
    path=[side_cap(z,-1) for z in reversed(ST)]
    path += [(f*BEAM[0],housing(ZT,f*BEAM[0])+.14,ZT) for f in FRAC[1:-1]]
    path += [side_cap(z,1) for z in ST]
    tube(rail,path,.40,.32,'teal')
    for s in [-1,1]:
        for j in range(22):
            z=-9.4+j*.95
            x=s*interp(z,BEAM)
            y=deck(z,x)
            rail.box(x-.10,x+.10,y-.02,y+.34,z-.10,z+.10,'teal')
        for z in [ZT,-7,-2.5,2.5,7,10.9]:
            x=s*interp(z,BEAM)
            y=deck(z,x)
            iron.box(x-.18,x+.18,y,y+.68,z-.20,z+.20,'wood')
            iron.box(x-.21,x+.21,y+.61,y+.71,z-.23,z+.23,'iron')
        # Broad courses use very thin deliberate seams along existing loops.
        for level in [4,8]:
            points=[(s*(profile(z)[level][0]+.012),profile(z)[level][1],z) for z in ST]
            tube(seams,points,.012,.018,'seam')

    # Transom board joints continue across the cheeks, outside the opening.
    section=profile(ZT)
    for y in [-.9,-.35,.22,.80,1.38]:
        j=next(j for j in range(len(section)-1) if section[j+1][1]>=y)
        a,c=section[j:j+2]
        x=a[0]+(c[0]-a[0])*(y-a[1])/(c[1]-a[1])
        for sign in [-1,1]:
            seams.box(min(sign*OPEN,sign*(x-.04)),max(sign*OPEN,sign*(x-.04)),
                      y-.008,y+.008,ZT-.013,ZT+.005,'seam')
    # Thick, fitted straps evaluated against the shell at their two edges.
    for z in [-8.4,-3.3,2.6,7.8]:
        for s in [-1,1]:
            for i in range(12):
                a=profile(z-.18); c=profile(z+.18)
                pts=[(s*(a[i][0]+.028),a[i][1],z-.18),
                     (s*(c[i][0]+.028),c[i][1],z+.18),
                     (s*(c[i+1][0]+.028),c[i+1][1],z+.18),
                     (s*(a[i+1][0]+.028),a[i+1][1],z-.18)]
                b0=[(x-s*.07,y,zz) for x,y,zz in pts]
                iron.solid([pts,b0,[pts[0],pts[1],b0[1],b0[0]],
                            [pts[1],pts[2],b0[2],b0[1]],
                            [pts[2],pts[3],b0[3],b0[2]],
                            [pts[3],pts[0],b0[0],b0[3]]],'iron')
            for i in [4,10]:
                x,y=profile(z)[i]
                iron.cyl((s*(x+.04),y,z),(s*(x+.13),y,z),.08,.08,6,'bolt')
    for s in [-1,1]:
        pts=[(s*(profile(z)[6][0]+.035),profile(z)[6][1],z) for z in ST]
        tube(iron,pts,.10,.22,'iron')
        x=s*(OPEN+.13)
        iron.box(x-.20,x+.20,-1.31,deck(ZT,x)-.04,ZT-.10,ZT+.12,'iron')
        for y in [-.88,1.66]:
            iron.cyl((x,y,ZT-.1),(x,y,ZT-.23),.105,.105,6,'bolt')
    # Constant-width boards with staggered butt joints. Heights interpolate
    # the authored deck mesh rather than the unsampled crown function.
    def surface(z,x):
        j=next((j for j in range(len(ST)-1) if z<=ST[j+1]),len(ST)-2)
        za,zb=ST[j:j+2]; t=(z-za)/(zb-za)
        wa,wb=interp(za,BEAM),interp(zb,BEAM)
        f=x/(wa*(1-t)+wb*t)
        i=next((i for i in range(len(FRAC)-1) if f<=FRAC[i+1]),len(FRAC)-2)
        a,c=FRAC[i:i+2]
        p=[(a*wa,za,deck(za,a*wa)),(a*wb,zb,deck(zb,a*wb)),
           (c*wb,zb,deck(zb,c*wb)),(c*wa,za,deck(za,c*wa))]
        for ids in [(0,1,2),(0,2,3)]:
            v,q,r=[p[k] for k in ids]
            den=(q[1]-r[1])*(v[0]-r[0])+(r[0]-q[0])*(v[1]-r[1])
            u=((q[1]-r[1])*(x-r[0])+(r[0]-q[0])*(z-r[1]))/den
            w=((r[1]-v[1])*(x-r[0])+(v[0]-r[0])*(z-r[1]))/den
            if min(u,w,1-u-w)>=-1e-6:
                return u*v[2]+w*q[2]+(1-u-w)*r[2]
        return deck(z,x)
    for n in range(-6,7):
        x=n*.70
        zs=[z for z in ST if (z>=HF+.10 or abs(x)>3.32) and interp(z,BEAM)>abs(x)+.15]
        if len(zs)<2: continue
        j=ST.index(zs[-1])
        if j<len(ST)-1:
            t=(BEAM[j]-abs(x)-.15)/(BEAM[j]-BEAM[j+1])
            zs.append(ST[j]+t*(ST[j+1]-ST[j]))
        tube(seams,[(x,surface(z,x)+.014,z) for z in zs],.014,.018,'seam')
        for z in [-6+(n%3)*1.3,1+(n%3)*1.3,7.5+(n%3)*.7]:
            if interp(z,BEAM)>max(abs(x),abs(x+.70))+.2:
                tube(seams,[(xx,surface(z,xx)+.014,z) for xx in [x,x+.70]],.014,.018,'seam')
    # Bow stem follows the actual rake.
    p=profile(ZF)
    iron.box(-.13,.13,p[0][1],p[-1][1]+.22,ZF-.05,ZF+.13,'iron')
    return [mesh('Rails_Teal',rail),mesh('Hull_Ironwork',iron),mesh('Plank_Seams',seams)]


def ring(b, x, outer, inner, thick, col, sides=32):
    for i in range(sides):
        a=2*math.pi*i/sides; c=2*math.pi*(i+1)/sides
        def p(xx,r,t):return (xx,r*math.sin(t),r*math.cos(t))
        q=[p(x-thick/2,inner,a),p(x-thick/2,outer,a),p(x-thick/2,outer,c),p(x-thick/2,inner,c)]
        v=[p(x+thick/2,inner,a),p(x+thick/2,outer,a),p(x+thick/2,outer,c),p(x+thick/2,inner,c)]
        b.face(q,col,hint=(-1,0,0));b.face(v,col,hint=(1,0,0))
        b.face([q[1],v[1],v[2],q[2]],col,hint=(0,math.sin((a+c)/2),math.cos((a+c)/2)))
        b.face([q[0],v[0],v[3],q[3]],col,hint=(0,-math.sin((a+c)/2),-math.cos((a+c)/2)))


def wheel():
    b=ss.Builder()
    b.cyl((-HW-.22,0,0),(HW+.22,0,0),.20,.20,16,'iron2')
    b.cyl((-HW+.15,0,0),(HW-.15,0,0),.91,.91,32,'iron2')
    for s in [-1,1]:
        ring(b,s*(HW-.13),R-.02,R-.43,.32,'rim')
        b.cyl((s*(HW-.05),0,0),(s*(HW+.21),0,0),.47,.41,12,'iron')
        for i in range(8):
            a=2*math.pi*i/8
            b.beam((s*(HW-.13),.32*math.sin(a),.32*math.cos(a)),
                   (s*(HW-.13),(R-.22)*math.sin(a),(R-.22)*math.cos(a)),.22,'rim')
    # Broad radial paddles; their full swept radius includes board thickness.
    for i in range(12):
        a=2*math.pi*i/12
        e=Vector((0,math.sin(a),math.cos(a)))
        n=Vector((0,math.cos(a),-math.sin(a)))
        points=[tuple(e*r+n*t+Vector((x,0,0)))
                for r in [R-.66,R] for x in [-HW,HW] for t in [-.095,.095]]
        faces=[(0,1,3,2),(4,5,7,6),(0,1,5,4),(2,3,7,6),(0,2,6,4),(1,3,7,5)]
        b.solid([[points[k] for k in f] for f in faces],'blade')
    return mesh('Paddle_Rotor',b)


def frame():
    b=ss.Builder()
    for s in [-1,1]:
        x=s*(HW+.24)
        b.box(x-.16,x+.16,-.5,1.71,-.86,-.60,'iron')
        b.box(x-.12,x+.12,-.16,.16,-.65,.06,'iron')
        b.cyl((s*(HW+.06),0,0),(s*(HW+.33),0,0),.43,.43,12,'iron2')
        b.cyl((x,1.64,-.86),(x,1.64,-1.02),.13,.13,8,'bolt')
    b.box(-HW-.18,HW+.18,1.72,1.92,-.86,-.60,'iron')
    return mesh('Paddle_Frame',b)


def topsides():
    b=ss.Builder(); z=.3; y=deck(z)
    # Continuous revolved profile includes base, steps, lip and hollow throat.
    contour=[(.78,0),(.78,.28),(.66,.28),(.66,.58),(.52,.58),
             (.48,4.72),(.64,4.72),(.64,5.12),(.43,5.12),(.43,4.45)]
    for i in range(8):
        a=2*math.pi*i/8;c=2*math.pi*(i+1)/8
        def p(r,t,yy):return (r*math.cos(t),yy,z+r*math.sin(t))
        for k,((r0,h0),(r1,h1)) in enumerate(zip(contour,contour[1:])):
            if h0==h1:
                hint=(0,1,0)
            else:
                sign=-1 if k==8 else 1
                hint=(sign*math.cos((a+c)/2),0,sign*math.sin((a+c)/2))
            b.face([p(r0,a,y+h0),p(r0,c,y+h0),p(r1,c,y+h1),p(r1,a,y+h1)],
                   'dark' if k==8 else 'bolt' if k==7 else 'iron',hint=hint)
    b.face([(.78*math.cos(i*math.pi/4),y,z+.78*math.sin(i*math.pi/4)) for i in range(8)],'iron',hint=(0,-1,0))
    b.face([(.43*math.cos(i*math.pi/4),y+4.45,z+.43*math.sin(i*math.pi/4)) for i in range(8)],'dark',hint=(0,1,0))
    chimney=mesh('Chimney',b)
    b=ss.Builder();z=-7.15;y=deck(z)
    b.box(-.22,.22,y,y+.78,z-.18,z+.18,'rim')
    cy=y+1.12
    b.cyl((0,cy,z-.16),(0,cy,z+.16),.12,.12,8,'iron')
    for i in range(10):
        a=2*math.pi*i/10;c=2*math.pi*(i+1)/10
        p=lambda t,r:(r*math.cos(t),cy+r*math.sin(t),z)
        b.beam(p(a,.64),p(c,.64),.10,'rim')
        b.beam(p(a,.13),p(a,.80),.075,'rim')
    return [chimney,mesh('Helm',b)]


def check(o):
    bm=bmesh.new();bm.from_mesh(o.data)
    result={'vertices':len(bm.verts),'triangles':sum(len(f.verts)-2 for f in bm.faces),
            'boundary_edges':sum(e.is_boundary for e in bm.edges),
            'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges),
            'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces)}
    bm.free()
    return result


def render(scene):
    ss.setup_render(scene)
    scene.view_settings.exposure=.65
    scene.display.shading.light='STUDIO'
    scene.display.shading.studiolight_rotate_z=.5
    scene.display.shading.show_shadows=False
    scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH'
    scene.world.color=ss.srgb_to_linear(ss._hex('#528FC4'))
    scene.display.shading.background_type='WORLD'
    camdata=bpy.data.cameras.new('ReviewCamera')
    cam=bpy.data.objects.new('ReviewCamera',camdata);scene.collection.objects.link(cam)
    scene.camera=cam;camdata.type='ORTHO';camdata.clip_end=500
    views=[('rear34',(-29,-34,23),(0,0,1),30,(1500,1050)),
           ('rear',(-50,0,2),(ZT,0,1),12,(1200,1000)),
           ('side',(0,-50,2),(0,0,2),29,(1500,650)),
           ('stern_detail',(-24,-14,13),(-10.5,0,.8),13,(1200,1000)),
           ('deck_housing',(-1,-9,9),(-9.3,0,2),11,(1200,1000)),
           ('housing_front',(-3,0,6.5),(-9.4,0,2.25),9,(1200,1000))]
    for name,eye,target,scale,res in views:
        cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
        camdata.ortho_scale=scale
        scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100
        scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
    cam.location=(-32,-25,25);cam.rotation_euler=(Vector((0,0,1))-cam.location).to_track_quat('-Z','Y').to_euler();camdata.ortho_scale=31


def main():
    ss.clear_scene()
    shell=hull();fixed=[shell]+fittings()+topsides()+housing_fittings()
    rotor=wheel();mount=frame()
    report={o.name:check(o) for o in fixed+[rotor,mount]}
    for name,counts in report.items():
        assert counts['boundary_edges']==0, name+' has open edges'
        assert counts['overconnected_edges']==0, name+' has overconnected edges'
        assert counts['degenerate_faces']==0, name+' has degenerate faces'
    socket=bpy.data.objects.new('WheelModuleSocket',None);bpy.context.scene.collection.objects.link(socket)
    socket.location=ss.S(AXLE);socket['unity_position']=AXLE
    rotor.parent=socket;mount.parent=socket
    report['total_triangles']=sum(v['triangles'] for v in report.values())
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2))
    for o in fixed+[rotor,mount]:
        ss.export_fbx(o,str(EXPORT/(o.name+'.fbx')))
    render(bpy.context.scene)
    # Saved authoring scene opens to the assembled ship and vertex colors.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.shading.color_type='VERTEX'
                area.spaces.active.region_3d.view_rotation=bpy.context.scene.camera.rotation_euler.to_quaternion()
                area.spaces.active.region_3d.view_distance=30
                area.spaces.active.region_3d.view_location=(0,0,1)
    bpy.context.scene.render.fps=30
    rotor.rotation_euler=(0,0,0);rotor.keyframe_insert('rotation_euler',frame=1)
    rotor.rotation_euler.y=2*math.pi;rotor.keyframe_insert('rotation_euler',frame=121)
    bpy.context.scene.frame_end=120;bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))


if __name__=='__main__':
    main()

