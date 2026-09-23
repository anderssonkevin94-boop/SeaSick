"""Independent FBX round-trip verification for the staged storage hut."""
import bpy
import bmesh
import json
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'art-staging/storage-astra-lvl1-v1'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(out/'storage-state-kit.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
report={'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0}
for ob in meshes:
    bm=bmesh.new();bm.from_mesh(ob.data)
    report['triangles']+=sum(len(f.verts)-2 for f in bm.faces)
    report['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges)
    report['degenerate_faces']+=sum(f.calc_area()<1e-10 for f in bm.faces)
    assert not any(p.use_smooth for p in ob.data.polygons),ob.name
    assert len(ob.data.color_attributes)>0,ob.name
    bm.free()
for kind,count in [('Timber',5),('Boards',8),('Food',3),('Cargo',2)]:
    assert len([o for o in meshes if o.name.startswith('Stock_'+kind+'_')])==count
assert report['nonmanifold_edges']==0 and report['degenerate_faces']==0,report
report['result']='PASS';(out/'export-verification.json').write_text(json.dumps(report,indent=2));print(report)
