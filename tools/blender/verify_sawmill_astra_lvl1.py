"""Independent FBX checks: footprint, flat normals, modules and moving parts."""
import bpy
import json
from pathlib import Path
OUT=Path(__file__).resolve().parents[2]/'art-staging/sawmill-astra-lvl1'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(OUT/'sawmill-level-1.fbx'))
meshes=[o for o in bpy.data.objects if o.type=='MESH']
assert len(meshes)==6,len(meshes)
expected=json.loads((OUT/'validation.json').read_text())
triangles=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)
assert triangles==expected['triangles']
for o in meshes:
    assert 'Col' in o.data.color_attributes
    assert not any(p.use_smooth for p in o.data.polygons)
    assert o.parent is not None
points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
lo=[min(v[i] for v in points) for i in range(3)]
hi=[max(v[i] for v in points) for i in range(3)]
for i in range(3):
    assert abs(lo[i]-expected['bounds'][0][i])<.001
    assert abs(hi[i]-expected['bounds'][1][i])<.001
for name in ['Entry','Sawyer','Log_feeder','Output','Saw_Rotation','Crank_Rotation']:
    assert name in bpy.data.objects
for name in ['Saw_Rotation','Crank_Rotation']:
    pivot=bpy.data.objects[name]
    children=[o for o in meshes if o.parent==pivot];assert len(children)==1
    ob=children[0];before=[ob.matrix_world@v.co for v in ob.data.vertices]
    pivot.rotation_euler.x+=.2;bpy.context.view_layer.update()
    after=[ob.matrix_world@v.co for v in ob.data.vertices]
    assert max((a-b).length for a,b in zip(before,after))>.03
report={'triangles':triangles,'mesh_modules':len(meshes),'bounds':[lo,hi],
        'flat_shading':True,'vertex_colors':True,'worker_markers':True,'moving_pivots':True}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
