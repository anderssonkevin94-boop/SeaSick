"""Raked, pointed bow and restrained carved prow; V2 stern stays unchanged."""
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v2 as v2

family=v2.family
ship=v2.ship
ss=v2.ss
OUT=HERE.parents[1]/'art-staging/modular-hull-family-v3'
OUT.mkdir(parents=True,exist_ok=True)
original_build=family.build


def plate(b,polygon,half_width,color,edge_color=None):
    for s in [-1,1]:
        ship.cap(b,[(s*half_width,z,x) for x,z in polygon],color,(s,0,0))
    for i,(x,z) in enumerate(polygon):
        xx,zz=polygon[(i+1)%len(polygon)]
        b.face([(-half_width,z,x),(half_width,z,x),(half_width,zz,xx),(-half_width,zz,xx)],
               edge_color if edge_color and i in [3,4] else color,hint=(0,-(xx-x),zz-z))


def build():
    parts,roots,rot,carrier,chimney,interface=original_build()
    # The connection region is unchanged. All bow surfaces and their trim use
    # one deformation, so the prow rake cannot leave floating rails or bands.
    for obj in parts[family.BOW].values():
        for v in obj.data.vertices:
            x=v.co.x+family.CUT_B
            t=max(0,min(1,(x-5)/(ship.ZF-5)))
            weight=t*t*(3-2*t)
            v.co.x+=weight*(.35+.55*(v.co.z-1))
            v.co.y*=1-.10*weight
        obj.data.update()
    b=ss.Builder()
    plate(b,[(11.72,-.65),(11.98,-.65),(13.77,2.79),(14.62,3.24),
             (14.47,3.47),(13.47,3.23),(13.15,2.54)],.22,'rim','teal')
    plate(b,[(14.35,3.09),(14.73,3.26),(14.58,3.52),(14.24,3.43)],.245,'iron')
    # Small flush brass diamond on each side of the carved head.
    for s in [-1,1]:
        points=[(13.80,3.18),(13.97,3.09),(14.14,3.18),(13.97,3.28)]
        outer=[(s*.231,z,x) for x,z in points]
        inner=[(s*.219,z,x) for x,z in points]
        b.solid([outer,inner]+[[outer[i],outer[(i+1)%4],inner[(i+1)%4],inner[i]] for i in range(4)],'brass')
    prow=ship.mesh(family.BOW+'__Prow',b)
    topology=ship.check(prow)
    assert all(topology[k]==0 for k in ['boundary_edges','overconnected_edges','degenerate_faces']),topology
    prow.data.transform(Matrix.Translation((-family.CUT_B,0,0)))
    prow.parent=roots[family.BOW]
    parts[family.BOW]['Prow']=prow
    family.LENGTHS[family.BOW]=max(v.co.x for obj in parts[family.BOW].values() for v in obj.data.vertices)
    roots[family.BOW]['length']=family.LENGTHS[family.BOW]
    bpy.data.objects[family.BOW+'__ForwardSocket'].location.x=family.LENGTHS[family.BOW]
    return parts,roots,rot,carrier,chimney,interface


def setup():
    v2.setup()
    family.OUT=OUT
    family.build=build


def main():
    setup()
    family.main()
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'short/ship.blend'))
    length=family.LENGTHS[family.STERN]+family.LENGTHS[family.BOW]
    family.render(OUT/'bow-detail.png',(length+13,-14,10),(length-2.8,0,1.4),12,(1400,1100))
    family.render(OUT/'side.png',(length/2,-50,2.4),(length/2,0,2.4),24,(1800,900))
    family.render(OUT/'top.png',(length/2,0,50),(length/2,0,0),24,(1800,1050))
    family.render(OUT/'front.png',(50,0,2.4),(length/2,0,2.4),12,(1050,1050))
    manifest=json.loads((OUT/'manifest.json').read_text())
    manifest['bow_revision']='V3: raked bow, carved timber head, iron shoe and flush brass diamond'
    manifest['compatibility']='W1-r2 interface unchanged; V2 stern/middle and M1 wheel remain compatible.'
    manifest['bow_length_note']='Bow length includes the decorative prow; stern and middle lengths are unchanged.'
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))


if __name__=='__main__': main()
