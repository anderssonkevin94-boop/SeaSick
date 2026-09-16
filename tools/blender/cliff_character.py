"""Repeatable, local cliff study; execute through Blender MCP after dressing.
Uses the approved dressed source without modifying it. Exports remain separate.
"""
import bpy, json
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.geometry import tessellate_polygon
from mathutils.bvhtree import BVHTree
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
SOURCE=ROOT/'tools/blender/source'
previous=bpy.context.window.scene
with bpy.data.libraries.load(str(SOURCE/'island-dressed-study.blend'),link=False) as (src,dst):
    dst.scenes=['Island_Dressed_Study']
scene=dst.scenes[0];scene.name='Island_Cliff_Study'
bpy.context.window.scene=scene
try:
    ob=next(o for o in scene.objects if o.name.startswith('Mountain silhouette'))
    old=ob.data
    vertices=[v.co.copy() for v in old.vertices]
    faces=[];zones=[]
    selected=[{1,2,25,26,49,50},{3,4,27,28,51,52}]
    for p in old.polygons:
        if any(set(p.vertices).issubset(s) for s in selected):continue
        faces.append(tuple(p.vertices));zones.append(p.material_index)
    materials=list(old.materials)
    def mat(name,color):
        m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);materials.append(m);return len(materials)-1
    warm=mat('CliffStudy_WarmMineral',(.60,.522,.402))
    cool=mat('CliffStudy_CoolMineral',(.546,.491,.413))
    seam=mat('CliffStudy_Fracture',(.48,.432,.355))
    earth=mat('CliffStudy_EarthLip',(.33,.295,.15))
    def add(p):
        vertices.append(Vector(p));return len(vertices)-1
    def patch(ids,zone):
        points=[Vector((vertices[i].x,vertices[i].y,0)) for i in ids]
        for tri in tessellate_polygon([points]):
            ix=[v if isinstance(v,int) else min(range(len(points)),key=lambda j:(points[j]-v).length_squared) for v in tri]
            f=tuple(ids[j] for j in ix)
            a,b,c=[vertices[j] for j in f]
            if (b-a).cross(c-a).z<0:f=f[::-1]
            faces.append(f);zones.append(zone)
    for start,u,depth in [(1,.61,.55),(3,.38,.32)]:
        A,B,F,E,D,C=start,start+1,start+24,start+25,start+48,start+49
        def point(s,t):
            lo,hi=(A,B) if t<=.5 else (F,E)
            up,uq=(F,E) if t<=.5 else (D,C)
            fraction=t*2 if t<=.5 else (t-.5)*2
            return vertices[lo].lerp(vertices[hi],s).lerp(vertices[up].lerp(vertices[uq],s),fraction)
        # Tips taper to zero width. A recessed central spine creates the split
        # through real geometry, keeping collision and height queries identical.
        Q=add(point(u-.11,.17));T=add(point(u+.04,.90))
        U=add(point(u-.019,.61));V=add(point(u+.019,.61))
        p=point(u,.61);p.z-=depth;K=add(p)
        patch([A,Q,U,T,D,F],warm)
        patch([B,E,C,T,V,Q],cool)
        patch([A,B,Q],0)
        for ids in [(Q,K,U),(U,K,T),(T,K,V),(V,K,Q)]:patch(ids,seam)
        # A thin, uneven soil reveal is part of the wall, not a floating decal.
        L=add(vertices[D].lerp(vertices[T],.14));R=add(vertices[C].lerp(vertices[T],.22))
        patch([D,L,R,C],earth);patch([L,T,R],0)
    mesh=bpy.data.meshes.new('CliffStudy_ConnectedTerrain')
    mesh.from_pydata(vertices,[],faces);mesh.update()
    for m in materials:mesh.materials.append(m)
    for p,z in zip(mesh.polygons,zones):p.material_index=z;p.use_smooth=False
    ob.data=mesh
    # Reject flipped/vertical faces: runtime terrain is a single-valued surface.
    bad=[]
    for p in mesh.polygons:
        a,b,c=[mesh.vertices[i].co for i in p.vertices]
        if (b-a).cross(c-a).z<=.00001:bad.append(p.index)
    assert not bad, 'Invalid terrain faces: '+str(bad)
    bvhs=[]
    for land in scene.objects:
        if land.type!='MESH' or 'asset_id' in land or land.name.startswith('Sea level'):continue
        transform=Matrix.LocRotScale(land.location,land.rotation_euler.to_quaternion(),land.scale)
        bvhs.append(BVHTree.FromPolygons([transform@v.co for v in land.data.vertices],[tuple(p.vertices) for p in land.data.polygons]))
    def ground(x,y):
        hits=[b.ray_cast(Vector((x,y,150)),Vector((0,0,-1)),200)[0] for b in bvhs]
        return max(p.z for p in hits if p is not None)
    # Fallen pieces stay separate resource instances and leave open space.
    placements=[('Boulder_1',-64,22,(2.3,1.25,1.05),.24),('Boulder_3',-69,25,(.9,.72,.65),.41),('Boulder_0',-60,21,(.55,.7,.48),-.12)]
    for asset,x,y,scale,angle in placements:
        template=next(o for o in scene.objects if o.get('asset_id')==asset)
        rock=template.copy();scene.collection.objects.link(rock)
        rock.name='CliffStudy_Fallen_'+asset;rock.location=(x,y,ground(x,y)-.18)
        rock.scale=scale;rock.rotation_euler.z=angle
    bpy.data.libraries.write(str(SOURCE/'island-cliff-study.blend'),{scene})
    report={'mountain_vertices':len(vertices),'mountain_triangles':len(faces),'invalid_projected_faces':len(bad),'new_individual_stones':3,'studied_faces':[1,3]}
    (ROOT/'docs/art-direction/cliff-character-validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print(report)
finally:
    bpy.context.window.scene=previous
