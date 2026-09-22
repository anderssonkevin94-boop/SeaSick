"""Inspect the saved ship scenes without modifying them."""
import bpy,json,sys
from pathlib import Path
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick');out=ROOT/'docs/art-direction/fleet-v2'
checks=[]
selected=[int(x) for x in sys.argv[sys.argv.index('--')+1:]] if '--' in sys.argv else list(range(1,21))
for spec in json.loads((out/'manifest.json').read_text()):
 if spec['stage'] not in selected:continue
 with bpy.data.libraries.load(str(ROOT/'tools/blender/source/fleet-v2'/spec['model']),link=False) as (src,dst):dst.scenes=src.scenes
 sc=dst.scenes[0]
 guns=[o.name for o in sc.objects if o.type=='MESH' and (o.name.startswith(('Deck cannon','Lower cannon')) or '_sculpted_barrel' in o.name)]
 sails=[o.name for o in sc.objects if o.type=='MESH' and o.name.startswith(('Square sail','Sail_course','Sail_topsail'))]
 item={'stage':spec['stage'],'scene':sc.name,'objects':len(sc.objects),'expected_cannons':spec['cannons'],'actual_cannons':len(guns),'expected_square_sails':sum(spec['tiers']),'actual_square_sails':len(sails),'has_camera':sc.camera is not None,'render_exists':(out/spec['image']).exists()}
 assert len(guns)==spec['cannons'] and len(sails)==sum(spec['tiers']),item
 assert item['has_camera'] and item['render_exists'],item
 if spec['stage'] not in [1,2,3,13]:
  assert not any(o.name.startswith(('Stern cabin','Canvas shelter','Awning post')) for o in sc.objects)
  item['rear_posts']=sum(o.name.startswith('Rear railing post') for o in sc.objects)
  assert item['rear_posts']==(5 if spec['stage']>=7 else 7),item
  if spec['stage']>=7:
   assert any(o.name.startswith('Integrated cabin front') for o in sc.objects)
   # The raised stern and the main hull form connected surfaces in the same mesh.
   for hull in [o for o in sc.objects if o.name.startswith('Hull shell')]:
    neighbors={v.index:set() for v in hull.data.vertices}
    for e in hull.data.edges:
     a,b=e.vertices;neighbors[a].add(b);neighbors[b].add(a)
    seen=set();pending=[0]
    while pending:
     a=pending.pop()
     if a in seen:continue
     seen.add(a);pending.extend(neighbors[a]-seen)
    assert len(seen)==len(neighbors),(spec['stage'],'disconnected stern',hull.name,len(seen),len(neighbors))
   item['stern_integrated']=True
   assert sum(o.name.startswith('Aft side stay') for o in sc.objects)==2
  if spec['stage']>=16:
   lower=[o for o in sc.objects if o.name.startswith('Lower cannon')]
   upper=[o for o in sc.objects if o.name.startswith('Deck cannon')]
   assert len(lower)==spec['gun_rows'][0]*2 and len(upper)==spec['gun_rows'][1]*2
   # The first barrel stations precede the bow sweep; compare actual mesh heights.
   bpy.context.window.scene=sc;bpy.context.view_layer.update()
   target=(1.45+spec['freeboard']-1.38)/2
   near=min(lower,key=lambda o:sum(v.co.x for v in o.data.vertices)/len(o.data.vertices))
   center=sum((near.matrix_world@v.co).z for v in near.data.vertices)/len(near.data.vertices)
   assert abs(center-target)<.22,(spec['stage'],center,target)
   item['enclosed_battery_center_m']=round(target,3);item['weather_deck_guns']=len(upper)
 checks.append(item)
 for ob in list(sc.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(sc)
 for dbs in [bpy.data.meshes,bpy.data.curves,bpy.data.materials,bpy.data.lights,bpy.data.cameras,bpy.data.worlds]:
  for db in list(dbs):
   if db.users==0:dbs.remove(db)
(out/('fleet-validation.json' if len(selected)==20 else 'partial-validation.json')).write_text(json.dumps(checks,indent=2))
print(f'Verified {len(checks)} scenes and images: gun/sail counts, complete rear guards, canopy removal, connected stern hulls and raised battery elevations.')
