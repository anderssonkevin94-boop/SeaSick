"""Verify static pug exports, vertex colors and deliberate eye asymmetry."""
import bpy
import bmesh
import json
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'art-staging/camp-pug-astra-v1'
results={}
for file,accessory in [('camp-pug.fbx',True),('camp-pug-no-neckerchief.fbx',False)]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(out/file))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];names={o.name for o in meshes}
    assert 'Left_Eye' in names and 'Right_Healed_Eyelid' in names
    assert not any('Right_Eye'==n or 'Right_Eyeball'==n for n in names)
    assert ('Neckerchief' in names)==accessory
    assert not any(o.type=='ARMATURE' for o in bpy.context.scene.objects)
    d={'triangles':0,'nonmanifold_edges':0,'degenerate_faces':0}
    for ob in meshes:
        bm=bmesh.new();bm.from_mesh(ob.data)
        d['triangles']+=sum(len(f.verts)-2 for f in bm.faces)
        d['nonmanifold_edges']+=sum(not e.is_manifold for e in bm.edges)
        d['degenerate_faces']+=sum(f.calc_area()<1e-12 for f in bm.faces)
        assert not any(p.use_smooth for p in ob.data.polygons)
        assert len(ob.data.color_attributes)>0
        bm.free()
    assert d['nonmanifold_edges']==0 and d['degenerate_faces']==0,d
    results[file]=d
(out/'export-verification.json').write_text(json.dumps({'result':'PASS','exports':results},indent=2));print(results)
