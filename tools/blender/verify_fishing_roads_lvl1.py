"""Round-trip check the staged fishing hut and road FBX deliveries."""
import bpy, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
HUT=ROOT/'art-staging/fishing-hut-astra-lvl1-v1'
ROADS=ROOT/'art-staging/roads-astra-lvl1-v1'
results=[]
def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    meshes=[ob for ob in bpy.context.scene.objects if ob.type=='MESH']
    assert meshes,path
    for ob in meshes:
        assert ob.data.color_attributes,path
        assert not any(p.use_smooth for p in ob.data.polygons),ob.name
    bpy.context.view_layer.update()
    return sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)
total=load(HUT/'fishing-hut-state-kit.fbx')
assert total==json.loads((HUT/'validation.json').read_text())['all_states_triangles']
for name in ['Frame','Canopy','Work_Catch','Output_Crate']+[f'Output_Fish_{i:02d}' for i in range(1,5)]:
    assert bpy.data.objects.get(name),name
points=[ob.matrix_world@v.co for ob in bpy.context.scene.objects if ob.type=='MESH' for v in ob.data.vertices]
lowest=min(p.z for p in points)
# Inclined rack poles have a shallow planted toe, not a hovering circular end.
assert -.02<lowest<.001,lowest
assert all(abs(p.x)<=2.75 and abs(p.y)<=2.6 for p in points)
results.append('PASS hut master triangles, flat normals, vertex colours, stock groups, ground/plot bounds')
load(HUT/'fishing-hut-empty.fbx')
assert not any(ob.name.startswith(('Output_Fish_','Work_Catch')) for ob in bpy.context.scene.objects)
results.append('PASS empty hut contains no fish stock or work catch')
manifest=json.loads((ROADS/'module-contract.json').read_text())['modules']
counts=json.loads((ROADS/'validation.json').read_text())
for name,info in manifest.items():
    total=load(ROADS/info['file']);assert total==counts[name]['triangles'],name
    for key,pos in info['sockets_blender'].items():
        ob=bpy.data.objects.get(info['marker_names'][key]);assert ob,name+' '+key
        assert (ob.matrix_world.translation-Vector(pos)).length<.001,(name,key)
    if name.startswith('Road_'):
        ob=next(ob for ob in bpy.context.scene.objects if ob.type=='MESH')
        alpha=[c.color[3] for c in ob.data.color_attributes[0].data]
        assert min(alpha)<.01 and max(alpha)>.99,name
    results.append('PASS '+name+': triangles, colours, flat normals, socket transforms'+(', feather alpha' if name.startswith('Road_') else ''))
(ROADS/'fbx-roundtrip-check.txt').write_text('\n'.join(results)+'\n')
(HUT/'fbx-roundtrip-check.txt').write_text('\n'.join(results[:2])+'\n')
print('\n'.join(results))
