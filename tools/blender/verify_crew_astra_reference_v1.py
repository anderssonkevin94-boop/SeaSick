import json
from pathlib import Path
import bpy

OUT=Path(__file__).resolve().parents[2]/'art-staging/crew-astra-reference-v1'
report={}
for name in ['deckhand-working-pose','deckhand-neutral']:
    for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(name+'.fbx')))
    objects=[o for o in bpy.data.objects if o.type=='MESH']
    assert len(objects)==2
    triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
    assert triangles==2714
    for o in objects:
        assert 'Col' in o.data.color_attributes
        if 'Skin' in o.name:
            assert all(min(c.color[:3])>.99 for c in o.data.color_attributes['Col'].data)
    report[name]={'meshes':2,'triangles':triangles,'vertex_colors':True,'neutral_skin_shade':True}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
