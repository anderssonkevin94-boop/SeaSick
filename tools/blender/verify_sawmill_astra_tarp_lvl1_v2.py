"""Independent exports and state-module checks for the tarp sawmill."""
import bpy
import json
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parents[2]/'art-staging/sawmill-astra-tarp-lvl1-v2'
expected=json.loads((OUT/'validation.json').read_text())
reports={}
for file in ['sawmill-tarp-state-kit.fbx','sawmill-tarp-empty.fbx','sawmill-tarp-review.fbx']:
    for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/file))
    meshes=[o for o in bpy.data.objects if o.type=='MESH']
    counts={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes}
    for o in meshes:
        assert 'Col' in o.data.color_attributes
        assert not any(p.use_smooth for p in o.data.polygons)
        assert o.parent is not None
    if 'state-kit' in file:
        assert sum(counts.values())==expected['all_variant_triangles']
        assert len([n for n in counts if n.startswith('Input_Log_')])==6
        assert len([n for n in counts if n.startswith('Output_Plank_')])==12
        for name in ['Bench_Loaded','Bench_Cutting','Bench_Finished','Saw_Tool']:
            assert bpy.data.objects[name].parent.name=='Bench_Anchor'
        points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
        for i in range(3):
            assert abs(min(v[i] for v in points)-expected['bounds'][0][i])<.001
            assert abs(max(v[i] for v in points)-expected['bounds'][1][i])<.001
        for o in meshes:
            if o.name.startswith(('Input_Log_','Output_Plank_')):
                center=Vector(tuple((min(v.co[i] for v in o.data.vertices)+max(v.co[i] for v in o.data.vertices))/2 for i in range(3)))
                assert center.length<.0001
    elif 'empty' in file:
        assert sum(counts.values())==expected['empty_triangles']
        assert not any(n.startswith(('Input_Log_','Output_Plank_','Bench_')) for n in counts)
    else:
        assert len([n for n in counts if n.startswith('Input_Log_')])==4
        assert len([n for n in counts if n.startswith('Output_Plank_')])==6
        assert 'Bench_Cutting' in counts and 'Bench_Loaded' not in counts and 'Bench_Finished' not in counts
    for n in ['Input_Container','Output_Container','Bench_Anchor','Entry','Sawyer']:
        assert n in bpy.data.objects
    reports[file]={'triangles':sum(counts.values()),'meshes':len(meshes),'flat_shading':True,'vertex_colors':True,'state_hierarchy':True}
(OUT/'export-verification.json').write_text(json.dumps(reports,indent=2))
print(json.dumps(reports,indent=2))
