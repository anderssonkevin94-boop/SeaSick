"""Independent saved-scene and FBX verification for the W1 hull family."""
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family

if '--' in sys.argv and sys.argv[sys.argv.index('--')+1:]==['v2']:
    import modular_hull_family_v2 as revised
    revised.setup()
elif '--' in sys.argv and sys.argv[sys.argv.index('--')+1:]==['v3']:
    import modular_hull_family_v3 as revised
    revised.setup()
elif '--' in sys.argv and sys.argv[sys.argv.index('--')+1:]==['wide']:
    import modular_hull_wide_v1 as revised
    revised.setup()

OUT=family.OUT
report={'wheel_tests':{},'exports':{},'imported_assemblies':{}}
baseline={}
expected={}
for variant in ['short','long']:
    bpy.ops.wm.open_mainfile(filepath=str(OUT/variant/'ship.blend'))
    bpy.context.view_layer.update()
    rotor=bpy.data.objects['Rotor']
    carrier=bpy.data.objects['Carrier']
    trees={o.name:family.wheel_study.bvh(o) for o in bpy.context.scene.objects
           if o.type=='MESH' and o not in {rotor,carrier} and not o.hide_render}
    mount=family.wheel_study.bvh(carrier)
    rotor.data.calc_loop_triangles()
    radial=[max(math.hypot(rotor.data.vertices[v].co.x,rotor.data.vertices[v].co.z)
                for v in t.vertices) for t in rotor.data.loop_triangles]
    collisions=[]
    for angle in range(0,360,5):
        matrix=Matrix.Translation(rotor.matrix_world.translation)@Matrix.Rotation(math.radians(angle),4,'Y')
        test=family.wheel_study.bvh(rotor,matrix)
        for name,tree in trees.items():
            hits=tree.overlap(test)
            if hits: collisions.append([angle,name,len(hits)])
        hits=[p for p in mount.overlap(test) if radial[p[1]]>.55]
        if hits: collisions.append([angle,'Carrier',len(hits)])
    report['wheel_tests'][variant]={'samples':72,'surface_intersections':collisions,'bearing_exclusion_radius':.55}
    assert not collisions,(variant,collisions)
    snapshot={o.name:[tuple(v.co) for v in o.data.vertices]
              for o in bpy.context.scene.objects if o.type=='MESH'}
    if baseline: assert baseline==snapshot,'Assemblies must share identical module geometry'
    baseline=snapshot
    if variant=='long':
        for o in bpy.context.scene.objects:
            if o.type!='MESH': continue
            group,part=o.name.split('__',1) if '__' in o.name else ('Fittings',o.name)
            key=f'{group}/{part}'
            expected[key]={'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
                           'bounds':[[min(v.co[i] for v in o.data.vertices) for i in range(3)],
                                     [max(v.co[i] for v in o.data.vertices) for i in range(3)]]}

family.ss.clear_scene()
parts={m:{} for m in family.MODULES}
roots={m:family.empty(m) for m in family.MODULES}
for file in sorted((OUT/'models').glob('*/*.fbx')):
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(file))
    objects=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
    assert len(objects)==1
    o=objects[0]
    bpy.context.view_layer.update()
    assert o.location.length<1e-5,'Export origin must remain zero'
    # Undo importer axis conversion and the V8 export yaw, yielding module-local
    # Blender geometry. Scene placement must not have leaked into mesh exports.
    o.data.transform(Matrix.Rotation(math.pi/2,4,'Z')@o.matrix_world)
    o.matrix_world=Matrix.Identity(4)
    group=file.parent.name
    key=f'{group}/{file.stem}'
    exp=expected[key]
    bounds=[[min(v.co[i] for v in o.data.vertices) for i in range(3)],
            [max(v.co[i] for v in o.data.vertices) for i in range(3)]]
    error=max(abs(a-b) for aa,bb in zip(bounds,exp['bounds']) for a,b in zip(aa,bb))
    triangles=sum(len(p.vertices)-2 for p in o.data.polygons)
    assert error<1e-4,(key,error)
    assert len(o.data.vertices)==exp['vertices'],key
    assert triangles==exp['triangles'],key
    assert 'Col' in o.data.color_attributes,key
    report['exports'][key]={'triangles':triangles,'bounds_error':error,'vertex_colors':'Col'}
    if group in parts:
        parts[group][file.stem]=o
        o.parent=roots[group]
    elif file.stem=='Chimney': chimney=o
for count in range(4):
    family.assemble(roots,chimney,count)
    result=family.joined_check(parts,roots,count)
    report['imported_assemblies'][str(count)]=result
    assert all(not any(c.values()) for c in result.values()),(count,result)
report['shared_geometry']='Short and long saved scenes contain identical module mesh coordinates.'
report['notes']='Wheel intersections sampled every 5 degrees; excludes intentional axle/bearing contact. No Unity or iOS testing.'
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print(f'PASS: identical shared modules, both wheel checks, {len(report["exports"])} FBX round trips, and 0-3 middle-bay assembly tests.')
