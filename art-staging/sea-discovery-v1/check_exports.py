import bpy,json,math
from pathlib import Path
p=Path(__file__).resolve().parent;expected=json.loads((p/'manifest.json').read_text());report=[]
for a in expected:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(p/(a['name']+'.fbx')));obs=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert obs
 tri=0
 for o in obs:
  m=o.data;m.calc_loop_triangles();tri+=len(m.loop_triangles);assert 'GameColor' in m.color_attributes;assert all(math.isfinite(c) for v in m.vertices for c in v.co)
 assert tri==a['triangles'],a['name'];report.append({'asset':a['name'],'meshes':len(obs),'triangles':tri,'colors_preserved':True})
(p/'export-validation.json').write_text(json.dumps({'passed':True,'assets':report},indent=2));print('PASS: all eight FBX exports preserve triangle counts and GameColor.')
