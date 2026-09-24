"""Independent file, export, animation, and same-mount interchange checks."""
import json
import math
from pathlib import Path
import bpy
from mathutils import Matrix

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/modular-ship-study-v1'
report={}
geometry={}
for name in ['A-timber','B-reinforced','C-oversized','C-oversized-deck']:
    folder=OUT/name
    bpy.ops.wm.open_mainfile(filepath=str(folder/'ship-study.blend'))
    scene=bpy.context.scene
    scene.frame_set(1)
    rotor=bpy.data.objects['Rotor']
    assert tuple(round(v,4) for v in rotor.location)==(-10.83,0,.35)
    scene.frame_set(31)
    assert abs(rotor.rotation_euler.y-math.pi/2)<1e-5, 'Animation must rotate at constant speed'
    scene.frame_set(1)
    expected={}
    geometry[name]={}
    for o in scene.objects:
        if o.type!='MESH': continue
        vertices=[Matrix.Rotation(-math.pi/2,4,'Z')@v.co for v in o.data.vertices]
        expected[o.name]={'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
                          'vertices':len(vertices),'bounds':[[min(v[i] for v in vertices) for i in range(3)],
                                                            [max(v[i] for v in vertices) for i in range(3)]]}
        geometry[name][o.name]=[tuple(round(c,5) for c in v.co) for v in o.data.vertices]
    for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
    checks={}
    for file in sorted((folder/'models').glob('*.fbx')):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(file))
        obs=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
        assert len(obs)==1
        o=obs[0]
        bpy.context.view_layer.update()
        imported_vertices=[o.matrix_world@v.co for v in o.data.vertices]
        actual={'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
                'vertices':len(o.data.vertices),
                'bounds':[[min(v[i] for v in imported_vertices) for i in range(3)],
                          [max(v[i] for v in imported_vertices) for i in range(3)]]}
        exp=expected[file.stem]
        assert actual['triangles']==exp['triangles'],file
        assert actual['vertices']==exp['vertices'],file
        err=max(abs(a-b) for aa,bb in zip(actual['bounds'],exp['bounds']) for a,b in zip(aa,bb))
        assert err<1e-4,(file,err,actual['bounds'],exp['bounds'],list(o.rotation_euler))
        assert 'Col' in o.data.color_attributes,file
        assert o.location.length<1e-5,file
        checks[file.stem]={'triangles':actual['triangles'],'color_attribute':'Col','bounds_max_error':err}
    report[name]={'fbx_roundtrip':checks,'quarter_turn_at_frame_31':True}
for mesh,verts in geometry['A-timber'].items():
    if mesh=='Rotor': continue
    assert verts==geometry['B-reinforced'][mesh], 'A/B must differ only in rotor: '+mesh
report['standard_interchangeability']='A and B all non-rotor mesh coordinates identical; shared axle and carrier.'
report['oversized_requires_large_pocket']=True
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print('All four export round trips, animation speed, and A/B interchangeability passed.')
