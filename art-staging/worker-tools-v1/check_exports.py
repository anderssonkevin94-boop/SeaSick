import bpy,json,math
from pathlib import Path
p=Path(__file__).resolve().parent;expected=json.loads((p/'manifest.json').read_text());report=[]
for a in expected:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(p/(a['name']+'.fbx')))
 obs=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(obs)==1,a['name'];m=obs[0].data;m.calc_loop_triangles()
 assert len(m.loop_triangles)==a['triangles'],a['name'];assert 'GameColor' in m.color_attributes,a['name'];assert len(m.materials)==1
 assert all(math.isfinite(c) for v in m.vertices for c in v.co)
 report.append({'asset':a['name'],'triangles':len(m.loop_triangles),'gameColor':True,'materials':1,'finite':True})
(p/'export-validation.json').write_text(json.dumps({'passed':True,'assets':report},indent=2));print('PASS: all 7 FBX exports preserve geometry counts and GameColor.')
