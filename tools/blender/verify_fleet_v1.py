"""Inspect the saved ship scenes without modifying them."""
import bpy,json
from pathlib import Path
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick');out=ROOT/'docs/art-direction/fleet-v1'
checks=[]
for spec in json.loads((out/'manifest.json').read_text()):
 with bpy.data.libraries.load(str(ROOT/'tools/blender/source/fleet-v1'/spec['model']),link=False) as (src,dst):dst.scenes=src.scenes
 sc=dst.scenes[0]
 guns=[o.name for o in sc.objects if o.type=='MESH' and (o.name.startswith(('Deck cannon','Lower cannon')) or '_sculpted_barrel' in o.name)]
 sails=[o.name for o in sc.objects if o.type=='MESH' and o.name.startswith(('Square sail','Sail_course','Sail_topsail'))]
 item={'stage':spec['stage'],'scene':sc.name,'objects':len(sc.objects),'expected_cannons':spec['cannons'],'actual_cannons':len(guns),'expected_square_sails':sum(spec['tiers']),'actual_square_sails':len(sails),'has_camera':sc.camera is not None,'render_exists':(out/spec['image']).exists()}
 assert len(guns)==spec['cannons'] and len(sails)==sum(spec['tiers']),item
 assert item['has_camera'] and item['render_exists'],item
 checks.append(item)
 for ob in list(sc.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(sc)
 for dbs in [bpy.data.meshes,bpy.data.curves,bpy.data.materials,bpy.data.lights,bpy.data.cameras,bpy.data.worlds]:
  for db in list(dbs):
   if db.users==0:dbs.remove(db)
(out/'fleet-validation.json').write_text(json.dumps(checks,indent=2))
print('Verified all 20 saved scenes, cameras and images; gun/sail counts match on all ships.')
