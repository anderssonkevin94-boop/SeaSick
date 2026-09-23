"""Independent rigged-FBX round trip: geometry, skin data and deformation."""
import json
import math
from pathlib import Path
import bpy

OUT=Path(__file__).resolve().parents[2]/'art-staging/crew-astra-topology-v2'
for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'deckhand-rigged.fbx'))
rigs=[o for o in bpy.data.objects if o.type=='ARMATURE']
meshes=[o for o in bpy.data.objects if o.type=='MESH']
assert len(rigs)==1 and len(meshes)==2
rig=rigs[0];assert len(rig.data.bones)==16
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
assert triangles==1668,triangles
for o in meshes:
    assert 'Col' in o.data.color_attributes
    assert any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)
    for v in o.data.vertices:
        assert abs(sum(g.weight for g in v.groups)-1)<1e-4
        assert all(o.vertex_groups[g.group].name in rig.data.bones for g in v.groups)
    if 'Skin' in o.name:
        assert all(min(c.color[:3])>.99 for c in o.data.color_attributes['Col'].data)
def positions():
    graph=bpy.context.evaluated_depsgraph_get()
    return [(o.evaluated_get(graph).matrix_world@v.co).copy()
            for o in meshes for v in o.evaluated_get(graph).data.vertices]
before=positions()
pb=rig.pose.bones['forearm.R'];pb.rotation_mode='XYZ';pb.rotation_euler.x=.5
bpy.context.view_layer.update();after=positions()
motion=max((a-b).length for a,b in zip(after,before))
assert motion>.01,motion
report={'meshes':len(meshes),'bones':len(rig.data.bones),'triangles':triangles,
        'vertex_colors':True,'normalized_weights':True,'neutral_skin_shade':True,
        'imported_rig_deformation_max_displacement':motion}
(OUT/'export-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
