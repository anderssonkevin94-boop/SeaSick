"""Round-trip checks for farm FBX geometry and crop-stage inventories."""
import bpy
import bmesh
import json
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'art-staging/farm-astra-lvl1-v1'
results={}
for file,stages,stock in [('farm-state-kit.fbx',['Sprout','Growing','Ripe'],4),('farm-bare.fbx',[],0),('farm-ripe.fbx',['Ripe'],4)]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(out/file))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];names={o.name for o in meshes}
    assert len([n for n in names if n.startswith('Harvest_Sheaf_')])==stock
    for i in range(1,7):
        assert f'Bed_{i:02d}_Soil' in names
        for stage in ['Sprout','Growing','Ripe']:assert (f'Bed_{i:02d}_{stage}' in names)==(stage in stages)
        assert f'Bed_{i:02d}_Anchor' in bpy.context.scene.objects
    d={'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0}
    for ob in meshes:
        bm=bmesh.new();bm.from_mesh(ob.data)
        d['triangles']+=sum(len(f.verts)-2 for f in bm.faces);d['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges);d['degenerate_faces']+=sum(f.calc_area()<1e-10 for f in bm.faces)
        assert not any(p.use_smooth for p in ob.data.polygons);assert len(ob.data.color_attributes)>0;bm.free()
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,d
    results[file]=d
(out/'export-verification.json').write_text(json.dumps({'result':'PASS','exports':results},indent=2));print(results)
