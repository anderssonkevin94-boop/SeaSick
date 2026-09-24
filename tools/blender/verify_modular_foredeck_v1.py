"""Independent saved-geometry / FBX checks for the two foredeck families."""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Matrix

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family

OUT=HERE.parents[1]/'art-staging/modular-foredeck-v1'
report={}
for mode in ['narrow','wide']:
    baseline={}
    expected={}
    for assembly in ['short','long']:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/mode/assembly/'ship.blend'))
        objects=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('Foredeck_'+mode+'__')]
        snapshot={o.name:[tuple(v.co) for v in o.data.vertices] for o in objects}
        if baseline: assert baseline==snapshot,'Foredeck must not change with hull length'
        baseline=snapshot
        for o in objects:
            expected[o.name.split('__')[1]]={
                'vertices':len(o.data.vertices),
                'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
                'bounds':[[min(v.co[i] for v in o.data.vertices) for i in range(3)],
                          [max(v.co[i] for v in o.data.vertices) for i in range(3)]]}
    family.ss.clear_scene()
    results={}
    for file in sorted((OUT/mode/'models').glob('*.fbx')):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(file))
        imported=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
        assert len(imported)==1
        obj=imported[0]
        bpy.context.view_layer.update()
        assert obj.location.length<1e-5
        obj.data.transform(Matrix.Rotation(math.pi/2,4,'Z')@obj.matrix_world)
        obj.matrix_world=Matrix.Identity(4)
        bounds=[[min(v.co[i] for v in obj.data.vertices) for i in range(3)],
                [max(v.co[i] for v in obj.data.vertices) for i in range(3)]]
        exp=expected[file.stem]
        error=max(abs(a-b) for aa,bb in zip(bounds,exp['bounds']) for a,b in zip(aa,bb))
        check=family.ship.check(obj)
        assert error<1e-4,(mode,file.stem,error)
        assert check['triangles']==exp['triangles'] and len(obj.data.vertices)==exp['vertices']
        assert all(check[k]==0 for k in ['boundary_edges','overconnected_edges','degenerate_faces'])
        assert 'Col' in obj.data.color_attributes
        results[file.stem]={'bounds_error':error,'triangles':check['triangles'],'vertex_colors':'Col'}
    assert len(results)==5
    report[mode]={'identical_on_short_and_long':True,'exports':results}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print('PASS: 10 FBX round trips, topology, local pivots, colors, and shared geometry across four assemblies.')
