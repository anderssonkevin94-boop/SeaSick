"""Independent FBX round-trip verification for the starter fletcher."""
import bpy
import bmesh
import json
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'art-staging/fletcher-astra-lvl1-v1'
results={}
for file,inputs,outputs,work in [('fletcher-state-kit.fbx',5,4,{'Work_Shaping','Work_Fletching','Work_Finished'}),('fletcher-empty.fbx',0,0,set()),('fletcher-review.fbx',4,3,{'Work_Shaping'})]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(out/file))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];names={o.name for o in meshes}
    assert len([n for n in names if n.startswith('Input_Timber_')])==inputs
    assert len([n for n in names if n.startswith('Output_Arrows_')])==outputs
    assert {n for n in names if n.startswith('Work_')}==work
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
    for name in ['Input_Anchor','Output_Anchor','Shaping_Anchor','Fletching_Anchor','Worker_Stand']:
        assert name in bpy.context.scene.objects
    results[file]=d
(out/'export-verification.json').write_text(json.dumps({'result':'PASS','exports':results},indent=2))
print(results)
