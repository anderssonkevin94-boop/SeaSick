"""Extract upright hydrostatic station areas from mesh-local hull shells only."""
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def triangles(obj):
    mesh=obj.data
    mesh.calc_loop_triangles()
    return [[tuple(mesh.vertices[i].co) for i in t.vertices] for t in mesh.loop_triangles]


def section_segments(tris,x):
    segments=[]
    for tri in tris:
        hits=[]
        for a,b in zip(tri,tri[1:]+tri[:1]):
            if (a[0]<x)!=(b[0]<x):
                t=(x-a[0])/(b[0]-a[0])
                hits.append((a[1]+t*(b[1]-a[1]),a[2]+t*(b[2]-a[2])))
        if len(hits)!=2:continue
        a,b=hits
        normal=(Vector(tri[1])-Vector(tri[0])).cross(Vector(tri[2])-Vector(tri[0]))
        if (b[0]-a[0])*(-normal.z)+(b[1]-a[1])*normal.y<0:a,b=b,a
        segments.append((a,b))
    # A valid transverse section is a closed directed boundary, including
    # concavities and cavities. Do not silently publish an incomplete slice.
    assert abs(sum(b[0]-a[0] for a,b in segments))<1e-5,('Open Y section',x)
    assert abs(sum(b[1]-a[1] for a,b in segments))<1e-5,('Open Z section',x)
    return segments


def submerged_area(segments,water_z):
    terms=[]
    for a,b in segments:
        if a[1]>=water_z and b[1]>=water_z:continue
        if (a[1]>water_z)!=(b[1]>water_z):
            t=(water_z-a[1])/(b[1]-a[1])
            cut=(a[0]+t*(b[0]-a[0]),water_z)
            if a[1]>water_z:a=cut
            else:b=cut
        # Green's theorem: integral (water_z - z) dy. The closing waterline
        # contributes zero, so disconnected pockets need no guessed polygon.
        terms.append((b[0]-a[0])*(water_z-(a[1]+b[1])*.5))
    area=math.fsum(terms)
    assert area>=-1e-6,('Inverted section',water_z,area)
    return max(0,area)


def sample(tris,waters,spacing):
    xs=sorted(set(p[0] for tri in tris for p in tri))
    low,high=xs[0],xs[-1]
    points={low+1e-6,high-1e-6}
    count=math.ceil((high-low)/spacing)
    points.update(low+(high-low)*i/count for i in range(1,count))
    # Capture abrupt transitions and every authored station without slicing
    # exactly along a triangle edge or a coplanar end face.
    for x in xs:
        for offset in [-1e-6,1e-6]:
            if low<x+offset<high:points.add(x+offset)
    rows=[]
    for x in sorted(points):
        if any(abs(x-v)<1e-8 for v in xs):x+=2e-7
        if rows and x-rows[-1]['xU']<1e-8:continue
        segments=section_segments(tris,x)
        areas=[submerged_area(segments,z) for z in waters]
        assert all(b>=a-1e-5 for a,b in zip(areas,areas[1:])),('Nonmonotonic areas',x)
        rows.append({'xU':x,'areaU2':areas})
    return rows


def volumes(rows,n):
    return [math.fsum((b['xU']-a['xU'])*(a['areaU2'][i]+b['areaU2'][i])*.5
                     for a,b in zip(rows,rows[1:])) for i in range(n)]


def self_test():
    # Closed unit cube: each transverse section has area == submerged height.
    vertices=[(x,y,z) for x,y,z in [(0,0,0),(1,0,0),(1,1,0),(0,1,0),
                                   (0,0,1),(1,0,1),(1,1,1),(0,1,1)]]
    faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    tris=[[vertices[f[i]] for i in t] for f in faces for t in [(0,1,2),(0,2,3)]]
    segments=section_segments(tris,.35)
    for z in [-.1,0,.25,.5,.9,1,1.1]:
        assert abs(submerged_area(segments,z)-max(0,min(1,z)))<1e-8
    # A triangular prism catches sloped sides and clipped partial immersion.
    verts=[(x,y,z) for x in [0,1] for y,z in [(-1,1),(0,0),(1,1)]]
    fs=[(0,2,1),(3,4,5),(0,1,4),(0,4,3),(1,2,5),(1,5,4),(2,0,3),(2,3,5)]
    tri=[[verts[i] for i in f] for f in fs]
    seg=section_segments(tri,.5)
    for z in [.2,.5,.8,1]:assert abs(submerged_area(seg,z)-z*z)<1e-8


def export_module(obj,deck_z,path):
    tris=triangles(obj)
    low_z=min(p[2] for tri in tris for p in tri)
    waters=sorted({low_z,deck_z,0.0}|{low_z+(deck_z-low_z)*i/64 for i in range(1,64)})
    coarse=sample(tris,waters,.125);fine=sample(tris,waters,.0625)
    a,b=volumes(coarse,len(waters)),volumes(fine,len(waters))
    # Small near-keel volumes need an absolute error floor.
    relative=max(abs(x-y)/max(y,b[-1]*.01) for x,y in zip(a,b))
    assert relative<.002,('Station integration did not converge',obj.name,relative)
    digest=hashlib.sha256(json.dumps(tris,separators=(',',':')).encode()).hexdigest()
    result={'schemaVersion':1,'module':obj.name.split('__')[0],
            'sourceMesh':obj.name,'sourceGeometrySha256':digest,
            'coordinates':'Blender module-local +X bow, +Y port, +Z up',
            'units':'authoring units; area U^2, integrated volume U^3',
            'validWaterlineZU':[low_z,deck_z],
            'hullXRangeU':[min(p[0] for t in tris for p in t),max(p[0] for t in tris for p in t)],
            'waterlineZU':waters,'stations':fine,'integratedVolumeU3':b,
            'method':'Directed mesh-plane intersections; exact clipped sectional area. Trapezoidal integration along X. Linear interpolation between tabulated waterlines is approximate.',
            'exclusions':['decorative prow','rails','iron bands','chimney','wheel','carrier','equipment'],
            'limits':'Upright, level immersion only. Not heel/trim hydrostatics, stability, cargo volume, mass or usable interior space. Upper waterline is capped at the family main deck; flooding above that is not modeled.',
            'verification':{'coarseMaxSpacingU':.125,'fineMaxSpacingU':.0625,
                            'maxVolumeDifferenceFractionWithOnePercentFloor':relative,
                            'monotonicAreaAndClosedSectionFlux':True,'analyticBoxAndWedgeTests':True}}
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(result,indent=2))
    return {'schemaVersion':1,'file':str(path.parent.name+'/'+path.name),
            'sourceGeometrySha256':digest,'validWaterlineZU':[low_z,deck_z],
            'uprightOnly':True,'stationCount':len(fine),'waterlineCount':len(waters)}


def attach(manifest,parts,out):
    self_test()
    for name,entry in manifest['modules'].items():
        entry['hydrostatics']=export_module(parts[name]['Hull_Shell'],manifest['cross_section']['deck_height'],out/'hydrostatics'/(name+'.json'))


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    out=Path(args[0]).resolve()
    manifest=json.loads((out/'manifest.json').read_text())
    bpy.ops.wm.open_mainfile(filepath=str(out/'long/ship.blend'))
    parts={n:{'Hull_Shell':bpy.data.objects[n+'__Hull_Shell']} for n in manifest['modules']}
    attach(manifest,parts,out)
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2))
    print('HYDROSTATICS PASS',out,flush=True)


if __name__=='__main__':main()
