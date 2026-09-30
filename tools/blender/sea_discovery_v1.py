import bpy,bmesh,ast,math,json,random
from mathutils import Vector,Matrix
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];P=ROOT/'art-staging/sea-discovery-v1';P.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;sc.unit_settings.system='METRIC'
pal={'wood':(.46,.23,.075,1),'cut':(.68,.40,.16,1),'dark':(.19,.085,.027,1),'rope':(.70,.53,.29,1),'rock':(.22,.29,.31,1),'rocktop':(.39,.46,.46,1),'wet':(.07,.14,.16,1),'green':(.045,.31,.18,.35),'cork':(.38,.20,.065,1),'paper':(.83,.70,.40,1),'ink':(.22,.12,.045,1)}
mat=bpy.data.materials.new('SS_SeaDiscovery_GameColor');mat.use_nodes=True;n=mat.node_tree.nodes;vc=n.new('ShaderNodeVertexColor');vc.layer_name='GameColor';bs=n.get('Principled BSDF');bs.inputs['Roughness'].default_value=.78;mat.node_tree.links.new(vc.outputs['Color'],bs.inputs['Base Color'])
a=ast.parse((ROOT/'tools/blender/resource_kit_v1.py').read_text());exec(compile(ast.Module(body=[next(n for n in a.body if isinstance(n,ast.ClassDef) and n.name=='Mesh')],type_ignores=[]),'<Mesh>','exec'))
def loft(m,rings,c,cap=True):
 if cap:m.face(rings[0][::-1],c);m.face(rings[-1],c)
 for k,(a,b) in enumerate(zip(rings,rings[1:])):
  for i in range(len(a)):m.face([a[i],a[(i+1)%len(a)],b[(i+1)%len(a)],b[i]],c,.9+.07*(i%3))
def tube(m,pts,r,c):
 rings=[]
 for i,p in enumerate(pts):
  t=(Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(0,i-1)])).normalized();u=t.cross(Vector((0,0,1)))
  if u.length<.01:u=t.cross(Vector((0,1,0)))
  u.normalize();v=t.cross(u).normalized();rings.append([tuple(Vector(p)+r*(u*math.cos(j*math.tau/6)+v*math.sin(j*math.tau/6))) for j in range(6)])
 loft(m,rings,c)
def plank(length,width,seed=0):
 m=Mesh();w=width/2;l=length/2
 poly=[(-w,-l),(-w*.15,-l+.07),(w*.2,-l-.04),(w,-l+.04),(w,l-.09),(.55*w,l-.05),(.1*w,l-.16),(-.3*w,l+.025),(-w,l-.035)]
 for z in [-.035,.035]:m.face([(x,y,z) for x,y in poly], 'wood' if z>0 else 'dark')
 for i,(x,y) in enumerate(poly):
  xx,yy=poly[(i+1)%len(poly)];m.face([(x,y,-.035),(xx,yy,-.035),(xx,yy,.035),(x,y,.035)],'cut' if i<3 or i>3 else 'dark')
 return m
items=[];manifest=[]
def register(name,obs,notes):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0];bpy.ops.export_scene.fbx(filepath=str(P/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 tri=0
 for o in obs:o.data.calc_loop_triangles();tri+=len(o.data.loop_triangles);o.hide_render=True
 items.append((name,obs));manifest.append({'name':name,'triangles':tri,'notes':notes})
short=plank(1.25,.24).obj('BrokenBoardShort');register('BrokenBoardShort',[short],'Surface-centred, 1.25m class; preserve per-piece wave posing.')
long=plank(2.15,.34).obj('BrokenBoardLong');register('BrokenBoardLong',[long],'Surface-centred, 2.2m class matching SalvageSpawner flotsam.')
m=Mesh()
for i in range(3):m.join(plank(1.55+(i%2)*.14,.25),((i-1)*.255,0,.015*(i%2)))
for y in [-.43,.43]:
 pts=[(-.40,y,-.06),(-.40,y,.07),(.40,y,.07),(.40,y,-.06),(-.40,y,-.06)];tube(m,pts,.024,'rope')
bundle=m.obj('LashedBoardBundle');register('LashedBoardBundle',[bundle],'Three boards, two broad rope bindings; surface-centred.')
# Bottle lies at a shallow angle; clear green shell and a true solid parchment inside.
bottle=Mesh();profile=[(-.35,.11),(-.31,.17),(.15,.17),(.24,.105),(.28,.065),(.41,.065)]
loft(bottle,[[(r*math.cos(i*math.tau/12),y,r*math.sin(i*math.tau/12)) for i in range(12)] for y,r in profile],'green')
glass=bottle.obj('Bottle_Glass');gm=bpy.data.materials.new('SS_Bottle_Glass');gm.use_nodes=True;nodes=gm.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial');mix=nodes.new('ShaderNodeMixShader');mix.inputs[0].default_value=.26;trans=nodes.new('ShaderNodeBsdfTransparent');solid=nodes.new('ShaderNodeBsdfPrincipled');solid.inputs['Base Color'].default_value=(.025,.27,.12,1);solid.inputs['Roughness'].default_value=.18;gm.node_tree.links.new(trans.outputs[0],mix.inputs[1]);gm.node_tree.links.new(solid.outputs[0],mix.inputs[2]);gm.node_tree.links.new(mix.outputs[0],out.inputs['Surface']);glass.data.materials.clear();glass.data.materials.append(gm)
m=Mesh();tube(m,[(0,.37,0),(0,.465,0)],.073,'cork');tube(m,[(0,-.22,0),(0,.16,0)],.10,'paper');tube(m,[(0,-.045,0),(0,-.005,0)],.104,'dark')
# Rolled end's broad spiral cue, no illegible text.
for r in [.045,.073]:
 pts=[(r*math.cos(i*math.tau/12),-.223,r*math.sin(i*math.tau/12)) for i in range(13)];tube(m,pts,.006,'ink')
solid=m.obj('Bottle_CorkAndScroll')
for o in [glass,solid]:
 rot=Matrix.Rotation(math.radians(14),4,'X');o.data.transform(rot)
register('MessageBottle',[glass,solid],'0.34m body diameter, deliberately enlarged for phone review; root waterline +0.1m matches existing bob offset. Two materials: alpha glass and opaque scroll/cork.')
def shard(m,at,w,d,h,lean,seed):
 rng=random.Random(seed);n=6;rings=[]
 for z,s in [(-.5,1),(0,1),(min(.18,h*.30),.98),(h*.75,.60),(h,.26)]:
  rings.append([(at[0]+math.cos(i*math.tau/n)*w*s+lean*z,at[1]+math.sin(i*math.tau/n)*d*s,z) for i in range(n)])
 m.face(rings[0][::-1],'wet');m.face(rings[-1],'rocktop')
 for k,(a,b) in enumerate(zip(rings,rings[1:])):
  for i in range(n):m.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],'wet' if k==0 else 'rocktop' if i%3==0 else 'rock',.88+.20*rng.random())
configs=[('ReefSplitPeak',[((-.32,0),.38,.42,1.2,-.10),((.35,.05),.32,.37,.85,.13),((0,-.38),.32,.22,.24,0)]),('ReefLowLedge',[((-.28,0),.54,.43,.32,.08),((.33,.05),.44,.37,.51,-.10),((0,-.35),.46,.22,.18,0)]),('ReefLeaningTeeth',[((-.40,0),.25,.29,.65,.22),((0,.12),.27,.37,1.10,.22),((.38,0),.23,.31,.80,.22)])]
for name,spec in configs:
 m=Mesh()
 for i,(at,w,d,h,lean) in enumerate(spec):shard(m,at,w,d,h,lean,i)
 # Unit hazard-radius footprint; scale by current Reef.Configure radius in Unity.
 radius=max(math.hypot(v[0],v[1]) for v in m.v);m.v=[(x/radius,y/radius,z) for x,y,z in m.v]
 register(name,[m.obj(name)],'Unit horizontal radius, origin mean waterline; scale by existing hazard radius. Wet lower band. Preserve Reef.Configure and runtime foam separately.')
# Approved crate reused directly, unchanged mesh geometry, only posed for waterline review.
with bpy.data.libraries.load(str(ROOT/'art-staging/ship-cargo-v2/ship-cargo.blend'),link=False) as (a,b):b.objects=[n for n in a.objects if n=='Cargo_Box_Large']
crate=b.objects[0];sc.collection.objects.link(crate);crate.location=(0,0,-.22);crate.hide_render=False
planks=[]
for i,src in enumerate([short,long]):
 o=src.copy();o.data=src.data.copy();sc.collection.objects.link(o);o.location=((i*2-1)*.62,.1+i*.25,-.01);o.rotation_euler.z=(-.45 if i==0 else .5);planks.append(o)
register('SalvageCluster',[crate]+planks,'Approved V2 Cargo_Box_Large mesh, unchanged; posed with two new broken boards. Review/static cluster only. Keep runtime resource identities and count-driven CargoVisual for live flotsam.')
sc.world=bpy.data.worlds.new('Review');sc.world.color=(.23,.23,.23)
for pos,en,size in [((3,-4,7),850,5),((-3,1,5),600,4)]:
 bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=en;o.data.size=size;o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;sc.camera=cam;cam.data.type='ORTHO';sc.render.engine='CYCLES';sc.cycles.samples=24;sc.cycles.use_denoising=True;sc.render.resolution_x=400;sc.render.resolution_y=350;sc.render.resolution_percentage=100;sc.render.film_transparent=True
for name,obs in items:
 for o in obs:o.hide_render=False
 coords=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box];lo=Vector([min(v[i] for v in coords) for i in range(3)]);hi=Vector([max(v[i] for v in coords) for i in range(3)]);center=(lo+hi)/2;cam.location=center+Vector((3,-4,3.5));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=max(hi-lo)*1.5
 sc.render.filepath=str(P/(name+'.png'));bpy.ops.render.render(write_still=True)
 for o in obs:o.hide_render=True
for i,(name,obs) in enumerate(items):
 for o in obs:o.hide_render=False;o.location+=Vector(((i%4)*3,(i//4)*3,0))
bpy.ops.wm.save_as_mainfile(filepath=str(P/'sea-discovery.blend'));(P/'manifest.json').write_text(json.dumps(manifest,indent=2))
