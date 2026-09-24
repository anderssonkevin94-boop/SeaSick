"""Check FBX topology, vertex colours, normals, units and ground pivots."""
import bpy
import bmesh
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/island-nature-kit-v1'
expected=json.loads((OUT/'mesh-audit.json').read_text())
results={}
for name,row in expected.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/'fbx'/(name+'.fbx')),colors_type='LINEAR')
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert len(objects)==1,(name,len(objects))
    ob=objects[0]; data=ob.data
    points=[ob.matrix_world@v.co for v in data.vertices]
    dimensions=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    bm=bmesh.new(); bm.from_mesh(data)
    result={
        'triangles':sum(len(p.vertices)-2 for p in data.polygons),
        'closed_manifold':all(e.is_manifold for e in bm.edges),
        'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces),
        'flat_shaded':not any(p.use_smooth for p in data.polygons),
        'vertex_colors':data.color_attributes.get('Col') is not None,
        'one_material':len(data.materials)==1,
        'origin_at_ground_anchor':ob.matrix_world.translation.length<.0001,
        'unit_bounds_match':all(abs(a-b)<.002 for a,b in zip(dimensions,row['dimensions_metres']))}
    bm.free()
    result['passed']=(result['triangles']==row['triangles'] and result['degenerate_faces']==0
                      and all(result[k] for k in ['closed_manifold','flat_shaded','vertex_colors',
                                                  'one_material','origin_at_ground_anchor','unit_bounds_match']))
    results[name]=result
    assert result['passed'],(name,result)
(OUT/'export-verification.json').write_text(json.dumps(results,indent=2))
print('PASS',len(results),'exports;',sum(r['triangles'] for r in results.values()),'triangles total')
