"""Verify exported shelter topology, colors and mutually exclusive entrances."""
import bpy
import bmesh
import json
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'art-staging/shelter-astra-lvl1-v1'
results={}
for file,entrances in [('shelter-state-kit.fbx',{'Entrance_Open','Entrance_Closed'}),('shelter-open.fbx',{'Entrance_Open'}),('shelter-closed.fbx',{'Entrance_Closed'})]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(out/file))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert {o.name for o in meshes if o.name.startswith('Entrance_')}==entrances
    d={'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0}
    for ob in meshes:
        bm=bmesh.new();bm.from_mesh(ob.data)
        d['triangles']+=sum(len(f.verts)-2 for f in bm.faces)
        d['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges)
        d['degenerate_faces']+=sum(f.calc_area()<1e-10 for f in bm.faces)
        assert not any(p.use_smooth for p in ob.data.polygons)
        assert len(ob.data.color_attributes)>0
        bm.free()
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,d
    assert all(name in bpy.context.scene.objects for name in ['Entry','Interior','Sleep_Left','Sleep_Right'])
    results[file]=d
(out/'export-verification.json').write_text(json.dumps({'result':'PASS','exports':results},indent=2));print(results)
