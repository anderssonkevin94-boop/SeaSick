"""Middle-weight comparison; preserve the original study and Unity project assets."""
import bpy
import bmesh
import math
import random
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'art-staging/groundcover-reference-v1'
OUT = ROOT / 'art-staging/groundcover-midweight-v1'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'groundcover-reference-v1.blend'))
scene = bpy.context.scene
library = bpy.data.collections['Reusable assets - ground pivots']
study = bpy.data.collections['Reference arrangement']
material = bpy.data.materials['Shared matte vertex palette']

def color(h):
    def lin(c): return c/12.92 if c <= .04045 else ((c+.055)/1.055)**2.4
    return tuple(lin(int(h[i:i+2],16)/255) for i in (1,3,5))+(1,)

def replacement(name, verts, faces, colors):
    old = library.objects[name].data
    data = bpy.data.meshes.new(name+'_Midweight')
    data.from_pydata(verts, [], faces); data.update()
    bm=bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    data.materials.append(material)
    attr=data.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p in data.polygons:
        p.use_smooth=False
        for i in p.loop_indices: attr.data[i].color=colors[p.index]
    for ob in list(bpy.data.objects):
        if ob.type=='MESH' and ob.data==old: ob.data=data

def foliage(name,seed,count,spread,height,broad=False):
    rng=random.Random(seed); verts=[]; faces=[]; colors=[]
    pal=['#839B6E','#91A67C','#A3B58B'] if broad else ['#6F8543','#829648','#94A654']
    for i in range(count):
        a=i*2.399963+rng.uniform(-.25,.25)
        length=spread*rng.uniform(.65,1.1); h=height*rng.uniform(.65,1.12)
        w=length*(.32 if broad else .14)
        base=Vector((math.cos(a)*.055,math.sin(a)*.055,0))
        d=Vector((math.cos(a),math.sin(a),0)); side=Vector((-d.y,d.x,0))
        first=base+d*length*.32+Vector((0,0,h*.60))
        second=base+d*length*.72+Vector((0,0,h*.93))
        tip=base+d*length+Vector((0,0,h*(.60 if broad else 1.05)))
        points=[base,first-side*w,first+Vector((0,0,w*.14)),first+side*w,
                second-side*w*.65,second+Vector((0,0,w*.10)),second+side*w*.65,tip]
        # Keep the complete upper silhouette; remove the two underside ridge vertices.
        local=[(0,1,2),(0,2,3),(1,4,5,2),(2,5,6,3),(4,7,5),(5,7,6),
               (0,3,6),(0,6,7),(0,7,4),(0,4,1)]
        start=len(verts); verts.extend(points)
        faces.extend(tuple(start+j for j in f) for f in local)
        colors.extend([color(pal[i%3])]*len(local))
    replacement(name,verts,faces,colors)

for args in [('Grass_Fan',12,7,.62,.75,False),('Grass_Sparse',18,5,.42,.54,False),
             ('Grass_Tall',9,6,.48,.94,False),('Broadleaf_Rosette',24,7,.66,.34,True)]:
    foliage(*args)

# Three irregular five-sided rings instead of four seven-sided rings.
verts=[]; faces=[]; rng=random.Random(8); n=5
angles=[math.tau*i/n+rng.uniform(-.12,.12) for i in range(n)]
for radius,z,cx,cy in [(.95,-.08,0,0),(1,.40,-.04,.02),(.52,1,-.16,.02)]:
    for a in angles:
        r=radius*(1+rng.uniform(-.045,.045))
        verts.append((.35*(math.cos(a)*r+cx)/2,.28*(math.sin(a)*r+cy)/2,.22*z))
faces=[tuple(reversed(range(n))),tuple(2*n+i for i in range(n))]
for j in range(2):
    for i in range(n): faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
replacement('Pebble',verts,faces,[color('#8C8D85')]*len(faces))

verts=[]; faces=[]
for a,b,r in [((0,0,.08),(.9,.22,.09),.045),((.35,.08,.08),(.50,.46,.07),.032),
              ((.75,.18,.08),(1.04,.05,.06),.025)]:
    a,b=Vector(a),Vector(b); d=(b-a).normalized(); u=d.cross(Vector((0,0,1))).normalized(); v=d.cross(u)
    start=len(verts); n=4
    verts.extend(p+(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n))*r for p in (a,b) for i in range(n))
    faces.extend([tuple(start+i for i in reversed(range(n))),tuple(start+n+i for i in range(n))])
    faces.extend((start+i,start+(i+1)%n,start+(i+1)%n+n,start+i+n) for i in range(n))
replacement('Twig_Fork',verts,faces,[color('#795F3F')]*len(faces))

def tris(ob): return sum(len(p.vertices)-2 for p in ob.data.polygons)
original=json.loads((SOURCE/'mesh-audit.json').read_text())
report={}
for ob in library.objects:
    if ob.type!='MESH': continue
    bm=bmesh.new(); bm.from_mesh(ob.data)
    row={'original_triangles':original[ob.name]['triangles'],'triangles':tris(ob),
         'vertices':len(ob.data.vertices),'manifold':all(e.is_manifold for e in bm.edges),
         'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces)}
    bm.free(); assert row['manifold'] and row['degenerate_faces']==0,(ob.name,row)
    report[ob.name]=row
report['original_arrangement_triangles']=original['arrangement_triangles']
report['arrangement_triangles']=sum(tris(ob) for ob in study.objects if ob.type=='MESH')
(OUT/'mesh-audit.json').write_text(json.dumps(report,indent=2))

cam=scene.camera
def render(name,eye,target,scale,res):
    cam.location=eye; cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale; scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)

render('close-view',(9,-16,11),(0,0,2),10,(1500,1250))
render('gameplay-view',(11,-17,19),(0,0,1),18,(1300,1400))
cam.location=(9,-16,11); cam.rotation_euler=(Vector((0,0,2))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=10
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'groundcover-midweight-v1.blend'))
print('MIDWEIGHT_COMPLETE',json.dumps(report))
