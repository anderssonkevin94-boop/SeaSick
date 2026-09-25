"""Round-trip every raised-section FBX (modular_raised_sections_v1.py output): triangle count,
vertex colours and flat normals against each <Label>/manifest.json."""
import json
from pathlib import Path
import bpy
OUT = Path(__file__).resolve().parents[2] / 'art-staging/modular-raised-sections-v1'
results = {}
for folder in sorted(p for p in OUT.iterdir() if (p / 'manifest.json').exists()):
    m = json.loads((folder / 'manifest.json').read_text())
    for section in m['modules'].values():
        for name, part in section.items():
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=str(OUT / part['file']))
            meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
            total = sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons)
            assert total == part['triangles'], (name, total, part['triangles'])
            assert all(o.data.color_attributes.get('Col') for o in meshes), name
            assert not any(p.use_smooth for o in meshes for p in o.data.polygons), name
            results[part['file']] = {'triangles': total, 'colors_and_flat_normals': True}
(OUT / 'export-verification.json').write_text(json.dumps(results, indent=2))
print('PASS', len(results), 'raised-section FBX files')
