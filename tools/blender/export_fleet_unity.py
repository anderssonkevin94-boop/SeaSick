"""Bake approved fleet scenes into compact, modular Unity mesh data (metres, +Z bow).
Run in a separate background Blender process. Source .blend files are read-only.
"""
import bpy,json,struct,math,re
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parents[2];OUT=R/'tools/blender/exports/fleet-v3-unity';OUT.mkdir(parents=True,exist_ok=True)
specs=json.loads((R/'docs/art-direction/fleet-v3/manifest.json').read_text())
old=json.loads((R/'Assets/_Project/Resources/Ladder/ladder.txt').read_text())
def unity(v):return Vector((v.y,v.z,v.x))
def vdict(v):return dict(zip('xyz',map(float,v)))
def centre(o):return sum((o.matrix_world@Vector(v) for v in o.bound_box),Vector())/8
for s in specs:
 stage=s['stage'];i=stage-1;L=s['length'];B=s['beam'];F=s['freeboard'];D=s['draft']
 with bpy.data.libraries.load(str(R/'tools/blender/source/fleet-v3'/s['model']),link=False) as (src,dst):dst.scenes=src.scenes
 sc=dst.scenes[0];bpy.context.window.scene=sc;bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
 objects=[o for o in sc.objects if o.type in {'MESH','CURVE'} and not o.name.startswith(('Presentation','Crew'))]
 guns=sorted([o for o in objects if o.name.startswith(('Deck cannon','Lower cannon'))],key=lambda o:(centre(o).z,centre(o).x,centre(o).y))
 sails=sorted([o for o in objects if o.name.startswith('Square sail')],key=lambda o:(centre(o).x,centre(o).z))
 groups={'Hull':[]};meta=[];gunmeta=[];sailmeta=[];gun_info={}
 for k,o in enumerate(guns):
  side=1 if centre(o).y>0 else -1;deck=o.name.startswith('Deck');scale=(.80 if i<11 else .92) if deck else (.82 if i<15 else .95)
  axis=Vector((0,side,.11)).normalized();origin=o.matrix_world@o.data.vertices[0].co+axis*.16*scale
  base=unity(origin)-Vector((0,.99 if deck else .42,0));prefix=f'Gun{k:02d}'
  gun_info[o.name]=(prefix,base,side,deck)
  groups[prefix+'/Barrel']=[];groups[prefix+'/Carriage']=[]
  gunmeta.append(dict(name=prefix,position=vdict(base),side=side,pivot=.99 if deck else .42,muzzle=1.53*scale,deck=deck))
 for k,o in enumerate(sails):
  c=centre(o);mx=L*.08 if s['masts']==1 else min([-L*.14,L*.22],key=lambda x:abs(x-c.x))
  top=max((o.matrix_world@v.co).z for v in o.data.vertices);key=f'Sail{k:02d}';groups[key]=[]
  sailmeta.append(dict(name=key,position=vdict(Vector((0,top,mx))),top=top,station=mx))
 gunparts=('Red gun carriage','Carriage cheek','Iron wheel rim','Wooden wheel','Brass axle cap')
 sailparts=('Canvas seam','Canvas foot','Yard','Flagship sail piping')
 for o in objects:
  key='Hull'
  if o in guns:key=gun_info[o.name][0]+'/Barrel'
  elif o.name.startswith(gunparts):
   near=min([g for g in guns if gun_info[g.name][3]],key=lambda g:(centre(g)-centre(o)).length_squared);key=gun_info[near.name][0]+'/Carriage'
  elif o in sails:key=f'Sail{sails.index(o):02d}'
  elif o.name.startswith(sailparts):
   c=centre(o);nearest=min(range(len(sails)),key=lambda k:abs(c.x-sailmeta[k]['station'])+abs(c.z-sailmeta[k]['top'])*.7);key=f'Sail{nearest:02d}'
  groups[key].append(o)
 with (OUT/f'{stage:02d}.fleetmesh').open('wb') as f:
  def integer(n):f.write(struct.pack('<i',n))
  integer(len(groups));total=0
  for key,obs in groups.items():
   encoded=key.encode();integer(len(encoded));f.write(encoded);verts=[];norms=[];cols=[];indices=[]
   pivot=Vector();side=0
   if key.startswith('Gun'):
    gm=next(x for x in gunmeta if key.startswith(x['name']+'/'));pivot=Vector(tuple(gm['position'].values()));side=gm['side']
    if key.endswith('Barrel'):pivot.y+=gm['pivot']
   elif key.startswith('Sail'):pivot=Vector(tuple(next(x for x in sailmeta if x['name']==key)['position'].values()))
   for o in obs:
    ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();mw=o.matrix_world;nm=mw.to_3x3().inverted().transposed();cache={}
    for tr in me.loop_triangles:
     mat=me.materials[tr.material_index] if len(me.materials)>tr.material_index else None
     color=tuple(mat.diffuse_color) if mat else (.5,.5,.5,1)
     for li in reversed(tr.loops):
      loop=me.loops[li];normal=me.corner_normals[li].vector;n=unity((nm@normal).normalized());p=unity(mw@me.vertices[loop.vertex_index].co)-pivot
      if side:
       p=Vector((-side*p.z,p.y,side*p.x));n=Vector((-side*n.z,n.y,side*n.x))
       if key.endswith('Barrel'):
        a=math.atan(.11);c=math.cos(a);t=math.sin(a)
        p=Vector((p.x,p.y*c-p.z*t,p.y*t+p.z*c));n=Vector((n.x,n.y*c-n.z*t,n.y*t+n.z*c))
      token=tuple(round(x,6) for x in (*p,*n,*color))
      idx=cache.get(token)
      if idx is None:idx=len(verts);cache[token]=idx;verts.append(tuple(p));norms.append(tuple(n));cols.append(color)
      indices.append(idx)
    ev.to_mesh_clear()
   integer(len(verts));integer(len(indices));total+=len(indices)//3
   for p,n,c in zip(verts,norms,cols):f.write(struct.pack('<10f',*p,*n,*c))
   if indices:f.write(struct.pack('<%di'%len(indices),*indices))
 # Preserve bay identities; derive the new hydrostatic envelope by dimensionally
 # scaling the previously measured hull curves. This is a calibrated proxy,
 # not a claim of a new watertight displacement integration.
 node=dict(old['nodes'][i]);lr=L/node['length'];br=B/node['beam'];dr=D/node['draft'];vr=lr*br*dr
 node.update(name=s['name'],label=s['name'],length=L,beam=B,depth=F+D,draft=D,loa_over_beam=L/B,masts=s['masts'],probe_lift=D*.55)
 node['volume_m3']*=vr;node['mass_kg']=node['volume_m3']*1025;node['waterplane_m2']*=lr*br;node['tpc_t_per_cm']=node['waterplane_m2']*.01025
 node['kb_above_keel_m']*=dr;node['bm_m']*=br*br/dr;node['km_above_keel_m']=node['kb_above_keel_m']+node['bm_m']
 node['volume_curve_z']=[z*dr for z in node['volume_curve_z']];node['volume_curve_v']=[v*vr for v in node['volume_curve_v']]
 node['bay_x']=[x*lr for x in node['bay_x']];node['tier_floor']=[(x+old['nodes'][i]['draft'])*((F+D)/old['nodes'][i]['depth'])-D for x in node['tier_floor']];node['tier_ceiling']=[(x+old['nodes'][i]['draft'])*((F+D)/old['nodes'][i]['depth'])-D for x in node['tier_ceiling']]
 node['ports_per_row']=[n for n in s['gun_rows'] if n];node['gun_rows']=len(node['ports_per_row']);node['ports_per_side']=s['cannons']//2
 meta=dict(stage=stage,name=s['name'],triangles=total,guns=gunmeta,sails=sailmeta,node=node,freeboard=F,cannons=s['cannons'],helm=vdict(Vector((.55,F+(2.55 if i>=10 else 2.3)+.04 if i>=6 else F*.5+.12 if i<3 else F+.15,-L*.38))))
 (OUT/f'{stage:02d}.json').write_text(json.dumps(meta,separators=(',',':')))
 print('EXPORTED',stage,total,len(groups),flush=True)
 for ob in list(sc.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(sc)
 for dbs in [bpy.data.meshes,bpy.data.curves,bpy.data.materials,bpy.data.lights,bpy.data.cameras,bpy.data.worlds]:
  for db in list(dbs):
   if db.users==0:dbs.remove(db)
print('FLEET EXPORT COMPLETE',flush=True)
