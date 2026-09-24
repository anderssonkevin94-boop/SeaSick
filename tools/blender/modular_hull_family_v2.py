"""Sparse, monotonic end profiles. Preserves V1 outputs for comparison."""
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family

ship=family.ship
ss=family.ss
OUT=HERE.parents[1]/'art-staging/modular-hull-family-v2'
OUT.mkdir(parents=True,exist_ok=True)
original_mesh=ship.mesh
original_tube=ship.tube


def lean_mesh(name,b,parent=None):
    # No blanket bevel on compound meshes. The rail has an authored chamfer.
    o=original_mesh(name+'_unbevelled',b,parent)
    o.name=name
    for p in o.data.polygons: p.use_smooth=False
    return o


def lean_tube(b,points,width,height,col):
    if col!='teal': return original_tube(b,points,width,height,col)
    w=width/2; h=height/2; c=.022
    section=[(-w+c,-h),(w-c,-h),(w,-h+c),(w,h-c),
             (w-c,h),(-w+c,h),(-w,h-c),(-w,-h+c)]
    rings=[]
    for i,p in enumerate(points):
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])
        side=Vector((-tangent.z,0,tangent.x)).normalized()
        rings.append([tuple(Vector(p)+side*x+Vector((0,y,0))) for x,y in section])
    for i,(a,c) in enumerate(zip(rings,rings[1:])):
        centre=(Vector(points[i])+Vector(points[i+1]))/2
        for j in range(8):
            b.face([a[j],c[j],c[(j+1)%8],a[(j+1)%8]],col,away=centre)
    b.face(rings[0],col,away=points[1]); b.face(rings[-1],col,away=points[-2])


def configure():
    ship.ST=[-11.55,-10.7,-9.7,-8.95,-8.6,-7,-5,family.CUT_A,
             0,family.CUT_B,5,6.7,8.3,9.8,11.2,12.4]
    ship.FRAC=[-1,-.75,-.5,-.25,0,.25,.5,.75,1]
    def width(x):
        if x < -5:
            t=(x-ship.ZT)/(-5-ship.ZT)
            return 4.64-.79*(1-t)**2
        if x<=5: return 4.64
        return 4.64-4.56*((x-5)/(ship.ZF-5))**2
    ship.BEAM=[width(x) for x in ship.ST]
    deck_levels=[1.76+.57*(max(0,x-5)/(ship.ZF-5))**2 for x in ship.ST]
    keel_levels=[-1.92+1.20*(max(0,x-5)/(ship.ZF-5))**2 for x in ship.ST]
    ship.deck=lambda x, transverse=0: ship.interp(x,deck_levels)
    ship.keel=lambda x: ship.interp(x,keel_levels)
    def profile(x):
        w=ship.interp(x,ship.BEAM)
        bottom=ship.keel(x); top=ship.deck(x)
        ts=[0,.17,.35,.53,.71,.87,1]
        widths=[.70,.85,.95,.99,1,1,1]
        return [(w*ship.hermite(t,ts,widths),bottom+(top-bottom)*t)
                for t in [i/12 for i in range(13)]]
    ship.profile=profile
    ship.housing=lambda x,y: ship.deck(x)+.33+.97*(1-(y/ship.interp(x,ship.BEAM))**2)


def fittings():
    rail=ss.Builder(); iron=ss.Builder(); seams=ss.Builder()
    # The sheer is level aft, then rises with the bow deck. No aft S-curve.
    side=lambda z,s:(s*ship.interp(z,ship.BEAM),ship.deck(z)+.47,z)
    path=[side(z,-1) for z in reversed(ship.ST)]
    path += [(f*ship.BEAM[0],ship.housing(ship.ZT,f*ship.BEAM[0])+.14,ship.ZT)
             for f in ship.FRAC[1:-1]]
    path += [side(z,1) for z in ship.ST]
    ship.tube(rail,path,.40,.32,'teal')
    for s in [-1,1]:
        for j in range(22):
            z=-9.4+j*.95; x=s*ship.interp(z,ship.BEAM); y=ship.deck(z)
            rail.box(x-.10,x+.10,y-.02,y+.34,z-.10,z+.10,'teal')
        for z in [ship.ZT,-7,-2.5,2.5,7,10.9]:
            x=s*ship.interp(z,ship.BEAM); y=ship.deck(z)
            iron.box(x-.18,x+.18,y,y+.68,z-.20,z+.20,'wood')
            iron.box(x-.21,x+.21,y+.61,y+.71,z-.23,z+.23,'iron')
        for level in [4,8]:
            ship.tube(seams,[(s*(ship.profile(z)[level][0]+.012),ship.profile(z)[level][1],z)
                             for z in ship.ST],.012,.018,'seam')
    section=ship.profile(ship.ZT)
    for y in [-.9,-.35,.22,.80,1.38]:
        j=next(j for j in range(len(section)-1) if section[j+1][1]>=y)
        a,c=section[j:j+2]; x=a[0]+(c[0]-a[0])*(y-a[1])/(c[1]-a[1])
        for s in [-1,1]:
            seams.box(min(s*ship.OPEN,s*(x-.04)),max(s*ship.OPEN,s*(x-.04)),
                      y-.008,y+.008,ship.ZT-.013,ship.ZT+.005,'seam')
    for z in [-8.4,-3.3,2.6,7.8]:
        for s in [-1,1]:
            a=ship.profile(z-.18); c=ship.profile(z+.18)
            for i in range(12):
                pts=[(s*(a[i][0]+.028),a[i][1],z-.18),(s*(c[i][0]+.028),c[i][1],z+.18),
                     (s*(c[i+1][0]+.028),c[i+1][1],z+.18),(s*(a[i+1][0]+.028),a[i+1][1],z-.18)]
                back=[(x-s*.07,y,zz) for x,y,zz in pts]
                iron.solid([pts,back,[pts[0],pts[1],back[1],back[0]],
                            [pts[1],pts[2],back[2],back[1]],[pts[2],pts[3],back[3],back[2]],
                            [pts[3],pts[0],back[0],back[3]]],'iron')
            for i in [4,10]:
                x,y=ship.profile(z)[i]
                iron.cyl((s*(x+.04),y,z),(s*(x+.13),y,z),.08,.08,6,'bolt')
    for s in [-1,1]:
        ship.tube(iron,[(s*(ship.profile(z)[6][0]+.035),ship.profile(z)[6][1],z)
                        for z in ship.ST],.10,.22,'iron')
        x=s*(ship.OPEN+.13)
        iron.box(x-.20,x+.20,-1.31,ship.deck(ship.ZT)-.04,ship.ZT-.10,ship.ZT+.12,'iron')
        for y in [-.88,1.66]:
            iron.cyl((x,y,ship.ZT-.1),(x,y,ship.ZT-.23),.105,.105,6,'bolt')
    plank_rows=int(max(ship.BEAM)/.70)
    for n in range(-plank_rows,plank_rows+1):
        x=n*.70
        zs=[z for z in ship.ST if (z>=ship.HF+.10 or abs(x)>3.32) and ship.interp(z,ship.BEAM)>abs(x)+.15]
        if len(zs)<2: continue
        j=ship.ST.index(zs[-1])
        if j<len(ship.ST)-1:
            t=(ship.BEAM[j]-abs(x)-.15)/(ship.BEAM[j]-ship.BEAM[j+1])
            zs.append(ship.ST[j]+t*(ship.ST[j+1]-ship.ST[j]))
        ship.tube(seams,[(x,ship.deck(z)+.014,z) for z in zs],.014,.018,'seam')
        for z in [-6+(n%3)*1.3,1+(n%3)*1.3,7.5+(n%3)*.7]:
            if ship.interp(z,ship.BEAM)>max(abs(x),abs(x+.7))+.2:
                ship.tube(seams,[(xx,ship.deck(z)+.014,z) for xx in [x,x+.70]],.014,.018,'seam')
    p=ship.profile(ship.ZF)
    iron.box(-.13,.13,p[0][1],p[-1][1]+.22,ship.ZF-.05,ship.ZF+.13,'iron')
    return [ship.mesh('Rails_Teal',rail),ship.mesh('Hull_Ironwork',iron),ship.mesh('Plank_Seams',seams)]


def finish_hull(shell):
    body=ship.casing_solid('CasingBody',2.90,ship.ZT+.06,ship.HF,.46,
                           [1.67,1.94,2.21,2.48],['wood','wood2','wood'])
    ship.boolean_into(shell,body,'UNION')
    b=ss.Builder()
    cross=[(f*ship.BEAM[0],ship.housing(ship.ZT,f*ship.BEAM[0])) for f in ship.FRAC]
    for (xa,ya),(xb,yb) in zip(cross,cross[1:]):
        q=[(xa,1.66,ship.ZT-.025),(xb,1.66,ship.ZT-.025),
           (xb,yb,ship.ZT-.025),(xa,ya,ship.ZT-.025)]
        v=[(x,y,ship.ZT+.22) for x,y,z in q]
        b.solid([q,v,[q[0],q[1],v[1],v[0]],[q[1],q[2],v[2],v[1]],
                 [q[2],q[3],v[3],v[2]],[q[3],q[0],v[0],v[3]]],'wood2')
    ship.boolean_into(shell,ship.mesh('TransomCrown',b),'UNION')
    b=ss.Builder()
    b.box(-ship.OPEN,ship.OPEN,-4,2.17,ship.ZT-2,ship.WF,'dark')
    ship.boolean_into(shell,ship.mesh('WheelClearance',b),'DIFFERENCE')
    shell.name='Hull_Shell'
    bm=bmesh.new(); bm.from_mesh(shell.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.00001)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(shell.data); bm.free()
    return shell


def setup():
    family.OUT=OUT
    family.INTERFACE_STANDARD='W1-r2'
    family.configure_geometry=configure
    ship.mesh=lean_mesh
    ship.fittings=fittings
    ship.finish_hull=finish_hull
    ship.tube=lean_tube


def main():
    setup()
    family.main()
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'short/ship.blend'))
    family.render(OUT/'stern-detail.png',(-10,-15,10),(2.1,0,.7),12.5,(1400,1050))
    family.render(OUT/'bow-detail.png',(31,-15,11),(15,0,1),12.5,(1400,1050))
    # Actual mesh edge overlay, to expose density rather than hide it in shading.
    hulls=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.endswith('__Hull_Shell')]
    for o in bpy.context.scene.objects:
        if o.type=='MESH': o.hide_render=o not in hulls or o.hide_render
    for o in hulls:
        if o.hide_render: continue
        line=o.copy(); line.data=o.data.copy(); bpy.context.scene.collection.objects.link(line)
        line.name=o.name+'_Topology'
        line.data.materials.clear()
        for attr in list(line.data.color_attributes): line.data.color_attributes.remove(attr)
        line.color=(.035,.045,.055,1)
        attr=line.data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
        for c in attr.data: c.color=(.015,.022,.03,1)
        line.data.color_attributes.active_color=attr
        mod=line.modifiers.new('Topology lines','WIREFRAME'); mod.thickness=.010; mod.use_replace=True
    family.render(OUT/'topology.png',(-16,-34,23),(8.975,0,.6),24,(1500,1100))
    current=json.loads((OUT/'validation.json').read_text())
    previous=json.loads((OUT.parent/'modular-hull-family-v1/validation.json').read_text())
    comparison={'stations':ship.ST,'station_count':len(ship.ST),'deck_cross_segments':len(ship.FRAC)-1,
                'triangles':{v:{'before':previous['assemblies'][v]['triangles'],
                                'after':current['assemblies'][v]['triangles']} for v in ['short','long']}}
    (OUT/'comparison.json').write_text(json.dumps(comparison,indent=2))
    print('COMPARISON',json.dumps(comparison),flush=True)


if __name__=='__main__': main()
