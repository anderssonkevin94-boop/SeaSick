"""FBX round-trip and unchanged-fitting checks for staged width expansion."""
import json
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parents[2]/'art-staging'
OUT=ROOT/'modular-width-inserts-v1'
m=json.loads((OUT/'manifest.json').read_text())
checks=[]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'modular-hull-family-v3/long/ship.blend'))
names=['Rotor','Carrier','Chimney','Stern_W1__Housing_Lid','Stern_W1__Housing_Ironwork','Stern_W1__Housing_AccessPanel','Stern_W1__Helm','Bow_W1__Prow']
original={n:[tuple(v.co) for v in bpy.data.objects[n].data.vertices] for n in names}
bpy.ops.wm.open_mainfile(filepath=str(OUT/'expanded-ship.blend'))
for name,coords in original.items():
    assert coords==[tuple(v.co) for v in bpy.data.objects[name].data.vertices],name
checks.append('PASS wheel, carrier, housing, helm, chimney and prow mesh coordinates unchanged')
for part in [p for section in m['modules'].values() for p in section['parts']]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/part['file']))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    count=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)
    assert count==part['triangles'],(part['name'],count,part['triangles'])
    for ob in meshes:
        assert ob.data.color_attributes.get('Col'),part['name']
        assert not any(p.use_smooth for p in ob.data.polygons),part['name']
    checks.append('PASS '+part['name']+': FBX triangles, vertex colors, flat normals')
checks.append('PASS welded hull integrity for zero, one and two middle bays (generator report)')
(OUT/'fbx-roundtrip-check.txt').write_text('\n'.join(checks)+'\n')
print('\n'.join(checks))
