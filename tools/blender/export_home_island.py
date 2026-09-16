"""Export approved Blender geometry, split normals and dressing for Unity."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
out=ROOT/'Assets/_Project/Resources/Flora'
with bpy.data.libraries.load(str(ROOT/'tools/blender/source/island-dressed-study.blend'),link=False) as (src,dst):
    dst.scenes=['Island_Dressed_Study']
scene=dst.scenes[0]
def xyz(v):return {'x':round(v.x,6),'y':round(v.z,6),'z':round(v.y,6)}
verts=[];normals=[];colors=[];tris=[];instances=[]
for ob in scene.objects:
    if 'asset_id' in ob:
        instances.append({'assetId':ob['asset_id'],'kind':ob['resource_type'],
            'position':xyz(ob.location),'scale':xyz(ob.scale),'yaw':-math.degrees(ob.rotation_euler.z)})
    elif ob.type=='MESH' and not ob.name.startswith('Sea level'):
        # Library-loaded scenes have not evaluated matrix_world yet. All study
        # terrain is unparented; build its authored transform explicitly.
        assert ob.parent is None
        transform=Matrix.LocRotScale(ob.location,ob.rotation_euler.to_quaternion(),ob.scale)
        mesh=ob.data;mesh.calc_loop_triangles()
        tint=mesh.color_attributes.get('BeachTint')
        for tri in mesh.loop_triangles:
            base=len(verts)
            for vi,li in zip(tri.vertices,tri.loops):
                verts.append(xyz(transform@mesh.vertices[vi].co))
                normals.append(xyz(transform.to_3x3().inverted().transposed()@mesh.corner_normals[li].vector))
                c=tint.data[vi].color if tint else mesh.materials[tri.material_index].diffuse_color
                colors.append(dict(zip(['r','g','b','a'],c)))
            tris.extend([base,base+2,base+1])
        if ob.name.startswith('Sculpted beach'):
            # Underwater continuation makes the depth field follow the exact coast.
            n=200;shore=[v.co.copy() for v in mesh.vertices[:n]]
            rings=[shore]
            for distance,depth in [(6,-4),(14,-12)]:
                ring=[]
                for i,p in enumerate(shore):
                    tangent=(shore[(i+1)%n]-shore[(i-1)%n]).normalized()
                    normal=Vector((tangent.y,-tangent.x,0)).normalized()
                    q=p+normal*distance;q.z=depth;ring.append(q)
                rings.append(ring)
            for k in range(2):
                for i in range(n):
                    j=(i+1)%n
                    for points in [(rings[k][i],rings[k+1][i],rings[k+1][j]),(rings[k][i],rings[k+1][j],rings[k][j])]:
                        base=len(verts);normal=(points[1]-points[0]).cross(points[2]-points[0]).normalized()
                        for p in points:
                            verts.append(xyz(p));normals.append(xyz(normal));colors.append({'r':.55,'g':.43,'b':.25,'a':1})
                        tris.extend([base,base+2,base+1])
(out/'HomeIslandTerrain.json').write_text(json.dumps({'vertices':verts,'normals':normals,'colors':colors,'triangles':tris},separators=(',',':')))
(out/'HomeIslandDressing.json').write_text(json.dumps({'instances':instances},indent=2))
# Burst-compatible readonly arrays with an 8m spatial index. The same triangle
# vertices drive rendering, collision and barycentric height queries.
N=40;MIN=-160;CELL=8;buckets=[[] for _ in range(N*N)]
for i in range(len(tris)//3):
    vs=[verts[tris[i*3+j]] for j in range(3)]
    for z in range(max(0,int((min(v['z'] for v in vs)-MIN)//CELL)),min(N-1,int((max(v['z'] for v in vs)-MIN)//CELL))+1):
        for x in range(max(0,int((min(v['x'] for v in vs)-MIN)//CELL)),min(N-1,int((max(v['x'] for v in vs)-MIN)//CELL))+1):buckets[z*N+x].append(i)
offsets=[0];indices=[]
for bucket in buckets:indices+=bucket;offsets.append(len(indices))
def floats(v):return ','.join(str(v[k])+'f' for k in ['x','y','z'])
cs=['// Generated from the approved Blender study by export_home_island.py.','using Unity.Mathematics;','namespace SeaSick.Terrain { public static class HomePlateauData {',f'public const int TriangleCount={len(tris)//3};',
    'static readonly float3[] Vertices = new float3[] {']
cs += ['new float3('+floats(v)+'),' for v in verts]
cs += ['};','static readonly int[] Triangles = new int[] {'+','.join(map(str,tris))+'};',
       'static readonly int[] Offsets = new int[] {'+','.join(map(str,offsets))+'};',
       'static readonly int[] Candidates = new int[] {'+','.join(map(str,indices))+'};',
       'public static float3 Vertex(int i) => Vertices[i];',
       'public static int3 Triangle(int i) => new int3(Triangles[i*3],Triangles[i*3+1],Triangles[i*3+2]);',
       'public static int Start(int cell) => Offsets[cell]; public static int End(int cell) => Offsets[cell+1]; public static int Candidate(int i) => Candidates[i];','}}']
(ROOT/'Assets/_Project/Scripts/Terrain/HomePlateauData.cs').write_text('\n'.join(cs)+'\n')
for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
bpy.data.scenes.remove(scene)
print(f'Exported {len(tris)//3} triangles, {len(instances)} individual resources; {len(indices)} spatial references.')
