"""Independent FBX round-trip checks for the modular deck cannon."""
import bpy
import json
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parents[2]/'art-staging/cannon-astra-v1'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(OUT/'cannon.fbx'))
objects=bpy.data.objects
meshes=[o for o in objects if o.type=='MESH']
triangles=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)
expected=json.loads((OUT/'validation.json').read_text())
assert triangles==expected['total_triangles']
for o in meshes:
    assert 'Col' in o.data.color_attributes
    assert not any(p.use_smooth for p in o.data.polygons)
    assert o.parent is not None
barrel=objects['Barrel'];pivot=objects['Elevation_Pivot'];socket=objects['Muzzle_Socket']
assert barrel.parent==pivot and socket.parent==pivot
assert len([o for o in meshes if o.name.startswith('Truck_Wheel')])==4
for o in meshes:
    if o.name.startswith('Truck_Wheel'):
        center=sum((v.co for v in o.data.vertices),Vector())/len(o.data.vertices)
        assert center.length<1e-5
before=socket.matrix_world.translation.copy()
pivot.rotation_euler.x+=.1;bpy.context.view_layer.update()
assert (socket.matrix_world.translation-before).length>.05
report={'triangles':triangles,'meshes':len(meshes),'flat_shaded':True,
        'vertex_colors':True,'wheel_origins_centered':True,'elevation_and_muzzle_hierarchy':True}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
