"""Validate replacement-bow exports and shared local geometry."""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Matrix

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family
VERSION=sys.argv[-1] if '--' in sys.argv and sys.argv[-1] in ['v2','v3','v4'] else 'v1'
OUT=HERE.parents[1]/('art-staging/modular-raised-bow-'+VERSION)
report={}
for mode,bow_name in [('narrow','Bow_W1'),('wide','Bow_W2')]:
    manifest=json.loads((OUT/mode/'manifest.json').read_text())
    baseline={};expected={}
    for assembly in ['short','long']:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/mode/assembly/'ship.blend'))
        objects=[o for o in bpy.data.objects[bow_name].children if o.type=='MESH']
        # BMesh portal cuts can reorder vertices without changing geometry.
        snapshot={o.name:sorted(tuple(round(c,5) for c in v.co) for v in o.data.vertices) for o in objects}
        if baseline:assert baseline==snapshot,'Short/long replacement geometry differs'
        baseline=snapshot
        for name in manifest['files']:
            obj=next(o for o in objects if o.name.endswith('__'+name))
            expected[name]={'vertices':len(obj.data.vertices),'check':family.ship.check(obj),
                'bounds':[[min(v.co[i] for v in obj.data.vertices) for i in range(3)],
                          [max(v.co[i] for v in obj.data.vertices) for i in range(3)]]}
    family.ss.clear_scene();results={}
    for name,relative in manifest['files'].items():
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(OUT/mode/relative))
        objects=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
        assert len(objects)==1
        obj=objects[0];bpy.context.view_layer.update()
        assert obj.location.length<1e-5
        obj.data.transform(Matrix.Rotation(math.pi/2,4,'Z')@obj.matrix_world)
        obj.matrix_world=Matrix.Identity(4)
        bounds=[[min(v.co[i] for v in obj.data.vertices) for i in range(3)],
                [max(v.co[i] for v in obj.data.vertices) for i in range(3)]]
        exp=expected[name]
        error=max(abs(a-b) for aa,bb in zip(bounds,exp['bounds']) for a,b in zip(aa,bb))
        check=family.ship.check(obj)
        assert error<1e-4 and check==exp['check'] and len(obj.data.vertices)==exp['vertices'],(mode,name,error,check,exp)
        assert 'Col' in obj.data.color_attributes
        results[name]={'bounds_error':error,'topology':check,'vertex_colors':'Col'}
    report[mode]={'shared_geometry':True,'exports':results}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print('PASS:',sum(len(v['exports']) for v in report.values()),'FBX round trips; shared replacement geometry on short and long ships.')
