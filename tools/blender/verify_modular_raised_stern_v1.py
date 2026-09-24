"""Round-trip the raised stern meshes, including the mirrored hinge-local parts."""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Matrix
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family
revision=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'v1'
assert revision in ['v1','v2']
OUT=HERE.parents[1]/('art-staging/modular-raised-stern-'+revision)
report={}
for mode,root in [('narrow','Stern_W1'),('wide','Stern_W2')]:
    manifest=json.loads((OUT/mode/'manifest.json').read_text())
    baseline={};expected={}
    for assembly in ['stern-only','both-raised']:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/mode/assembly/'ship.blend'))
        objects=[o for o in bpy.data.objects[root].children if o.type=='MESH' and '__' in o.name]
        snapshot={o.name:sorted(tuple(round(c,5) for c in v.co) for v in o.data.vertices) for o in objects}
        if baseline:assert baseline==snapshot,'Stern geometry changed between assemblies'
        baseline=snapshot
        for name in manifest['files']:
            obj=next(o for o in objects if o.name.endswith('__'+name))
            expected[name]={'check':family.ship.check(obj),'bounds':[[min(v.co[i] for v in obj.data.vertices) for i in range(3)],
                   [max(v.co[i] for v in obj.data.vertices) for i in range(3)]]}
    family.ss.clear_scene();results={}
    for name,path in manifest['files'].items():
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(OUT/mode/path))
        objects=[o for o in set(bpy.data.objects)-before if o.type=='MESH'];assert len(objects)==1
        obj=objects[0];bpy.context.view_layer.update();assert obj.location.length<1e-5
        obj.data.transform(Matrix.Rotation(math.pi/2,4,'Z')@obj.matrix_world);obj.matrix_world=Matrix.Identity(4)
        bounds=[[min(v.co[i] for v in obj.data.vertices) for i in range(3)],[max(v.co[i] for v in obj.data.vertices) for i in range(3)]]
        check=family.ship.check(obj);exp=expected[name]
        error=max(abs(a-b) for aa,bb in zip(bounds,exp['bounds']) for a,b in zip(aa,bb))
        assert error<1e-4 and check==exp['check'],(mode,name,error,check,exp)
        assert 'Col' in obj.data.color_attributes
        results[name]={'bounds_error':error,'topology':check,'vertex_colors':'Col'}
    report[mode]={'shared_geometry':True,'exports':results}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print('PASS:',sum(len(r['exports']) for r in report.values()),'stern mesh round trips, including hinge-local door/hatch geometry.')
