import bpy,bmesh,ast,math,json
from mathutils import Vector,Matrix
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];P=ROOT/'art-staging/food-ingredients-v1';P.mkdir(exist_ok=True);(P/'icons').mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;sc.unit_settings.system='METRIC'
pal={'potato':(.48,.28,.105,1),'eye':(.22,.10,.031,1),'carrot':(.90,.24,.025,1),'leaf':(.12,.36,.055,1),'leaflight':(.27,.51,.07,1),'onion':(.46,.16,.27,1),'cream':(.83,.68,.39,1),'wheat':(.77,.49,.12,1),'wheatlight':(.96,.72,.26,1),'apple':(.65,.045,.025,1),'stem':(.22,.10,.028,1),'fish':(.07,.31,.37,1),'silver':(.51,.70,.68,1),'dark':(.025,.055,.06,1),'meat':(.62,.105,.075,1),'fat':(.85,.60,.38,1),'rind':(.35,.06,.033,1)}
mat=bpy.data.materials.new('SS_Food_GameColor');mat.use_nodes=True;vc=mat.node_tree.nodes.new('ShaderNodeVertexColor');vc.layer_name='GameColor';mat.node_tree.links.new(vc.outputs['Color'],mat.node_tree.nodes['Principled BSDF'].inputs['Base Color']);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.75
nodes=ast.parse((ROOT/'tools/blender/resource_kit_v1.py').read_text());exec(compile(ast.Module(body=[next(n for n in nodes.body if isinstance(n,ast.ClassDef) and n.name=='Mesh')],type_ignores=[]),'<Mesh>','exec'))
def loft(m,rings,c):
 m.face(rings[0][::-1],c);m.face(rings[-1],c)
 for k,(a,b) in enumerate(zip(rings,rings[1:])):
  for i in range(len(a)):m.face([a[i],a[(i+1)%len(a)],b[(i+1)%len(a)],b[i]],c,.87+.075*(i%3))
def lathe(m,profile,c,n=10):loft(m,[[(math.cos(i*math.tau/n)*r,math.sin(i*math.tau/n)*r,z) for i in range(n)] for z,r in profile],c)
def ellipsoid(m,at,size,c,sub=1):
 bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub,radius=1);o=bpy.context.object
 for f in o.data.polygons:m.face([tuple(Vector(at)+Vector(tuple(o.data.vertices[i].co[j]*size[j] for j in range(3)))) for i in f.vertices],c,.91+.045*(f.index%4))
 bpy.data.objects.remove(o,do_unlink=True)
def stem(m,a,b,r,c,n=6):
 a=Vector(a);b=Vector(b);t=(b-a).normalized();u=t.cross(Vector((0,1,0)))
 if u.length<.1:u=t.cross(Vector((1,0,0)))
 u.normalize();v=t.cross(u).normalized();loft(m,[[tuple(p+r*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n))) for i in range(n)] for p in [a,b]],c)
def leaf(m,start,end,width,c='leaf'):
 a=Vector(start);b=Vector(end);mid=a.lerp(b,.48);w=Vector((width,0,0));ridge=mid+Vector((0,-width*.22,width*.14));pts=[a,mid-w,b,mid+w]
 for sign in [-1,1]:
  for i in range(4):m.face([tuple(pts[i]),tuple(pts[(i+1)%4]),tuple(ridge+Vector((0,sign*.002,0)))],c if i%2 else 'leaflight')
foods={}
m=Mesh();ellipsoid(m,(0,0,.085),(.14,.10,.085),'potato',2)
for at in [(.06,-.048,.149),(-.06,-.052,.142)]:ellipsoid(m,at,(.018,.012,.006),'eye')
foods['Potato']=m
m=Mesh();lathe(m,[(0,.008),(.065,.026),(.20,.06),(.27,.055),(.28,.032)],'carrot')
for i in range(3):leaf(m,(0,0,.27),((i-1)*.065,.015*(i%2),.42-.025*abs(i-1)),.027)
foods['Carrot']=m
m=Mesh();lathe(m,[(0,.036),(.025,.078),(.09,.108),(.14,.095),(.18,.046),(.23,.014)],'onion');lathe(m,[(.175,.049),(.20,.025),(.245,.011)],'cream')
for i in [-1,1]:leaf(m,(0,0,.237),(i*.025,0,.32),.012)
foods['Onion']=m
m=Mesh()
for j in range(5):
 x=(j-2)*.025;y=(j%2)*.025;h=.35+.018*(j%3);stem(m,(x*.3,y*.3,0),(x,y,h),.006,'wheat')
 for k in range(4):
  z=h-.10+k*.027
  for side in [-1,1]:ellipsoid(m,(x+side*.016,y,z),(.023,.010,.027),'wheatlight' if k%2 else 'wheat')
 stem(m,(x,y,h),(x,y,h+.04),.0025,'wheatlight')
# Broad binding across gathered stalks.
lathe(m,[(.09,.05),(.112,.05)],'stem',8);foods['Wheat']=m
m=Mesh();lathe(m,[(0,.035),(.026,.075),(.095,.105),(.16,.097),(.192,.067),(.181,.025)],'apple',12);stem(m,(0,0,.18),(.012,0,.24),.009,'stem');leaf(m,(.007,0,.217),(.083,0,.246),.021);foods['Apple']=m
m=Mesh();rings=[]
for x,w,h in [(-.19,.015,.020),(-.12,.045,.034),(-.035,.078,.055),(.065,.072,.052),(.15,.035,.028),(.18,.007,.010)]:rings.append([(x,math.sin(i*math.tau/10)*w,.057+math.cos(i*math.tau/10)*h) for i in range(10)])
loft(m,rings,'fish')
# Cream flank stripe, broad body facets rather than scales.
for i in range(len(m.c)):
 if i%10 in [4,5,6]:m.c[i]=pal['silver']
for sign in [-1,1]:
 m.face([(-.19,0,.055),(-.29,sign*.08,.06),(-.265,0,.075)],'silver')
 m.face([(-.19,0,.04),(-.265,0,.075),(-.29,sign*.08,.06)],'fish')
for side in [-1,1]:ellipsoid(m,(.113,side*.043,.078),(.014,.008,.014),'dark')
foods['Fish']=m
m=Mesh();poly=[(-.12,-.05),(-.10,-.10),(.03,-.105),(.125,-.055),(.14,.025),(.085,.10),(-.01,.105),(-.10,.055)]
loft(m,[[(x,y,z) for x,y in poly] for z in [0,.052]],'rind');inner=[(x*.82,y*.82,.056) for x,y in poly]
for i in range(len(poly)):
 j=(i+1)%len(poly);m.face([(poly[i][0],poly[i][1],.052),(poly[j][0],poly[j][1],.052),inner[j],inner[i]],'fat')
m.face(inner,'meat');ellipsoid(m,(.048,.017,.060),(.032,.027,.005),'cream');foods['Meat']=m
assets=[];records=[]
def export(name,obs):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0];bpy.ops.export_scene.fbx(filepath=str(P/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 tri=0
 for o in obs:o.data.calc_loop_triangles();tri+=len(o.data.loop_triangles);o.hide_render=True
 records.append({'name':name,'triangles':tri,'meshes':len(obs)});assets.append((name,obs))
units={}
for name,m in foods.items():o=m.obj(name);units[name]=o;export(name+'_Unit',[o])
with bpy.data.libraries.load(str(ROOT/'art-staging/ship-cargo-v2/ship-cargo.blend'),link=False) as (a,b):b.objects=['Cargo_Box_Small']
base=b.objects[0];sc.collection.objects.link(base);base.hide_render=True
for name,unit in units.items():
 crate=base.copy();crate.data=base.data.copy();sc.collection.objects.link(crate);crate.location=(0,0,0);obs=[crate]
 count=3 if name in ['Fish','Meat','Wheat'] else 5
 for i in range(count):
  o=unit.copy();o.data=unit.data.copy();sc.collection.objects.link(o);o.name=name+'_Display_'+str(i+1);o.scale=(.65,)*3;o.location=((i%3-1)*.15,(i//3-.5)*.16,.445)
  if name=='Fish':o.location=((i-1)*.12,0,.445);o.rotation_euler.z=math.pi/2
  elif name=='Wheat':o.location=((i-1)*.15,0,.445)
  obs.append(o)
 export(name+'_Display',obs)
sc.world=bpy.data.worlds.new('Review');sc.world.color=(.24,.24,.24)
for pos,en,size in [((2,-3,5),650,5),((-3,2,4),400,4)]:
 bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.data.energy=en;l.data.size=size;l.rotation_euler=(-l.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;sc.camera=cam;cam.data.type='ORTHO';sc.render.engine='CYCLES';sc.cycles.samples=24;sc.cycles.use_denoising=True;sc.render.film_transparent=True;sc.render.resolution_percentage=100
for name,obs in assets:
 for o in obs:o.hide_render=False
 bpy.context.view_layer.update();coords=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box];lo=Vector([min(v[i] for v in coords) for i in range(3)]);hi=Vector([max(v[i] for v in coords) for i in range(3)]);center=(lo+hi)/2;cam.location=center+Vector((2,-3,3));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=max(hi-lo)*1.45
 sc.render.resolution_x=256 if name.endswith('Unit') else 320;sc.render.resolution_y=sc.render.resolution_x
 sc.render.filepath=str(P/'icons'/(name.replace('_Unit','')+'.png')) if name.endswith('Unit') else str(P/(name+'.png'));bpy.ops.render.render(write_still=True)
 for o in obs:o.hide_render=True
for i,(name,obs) in enumerate(assets):
 for o in obs:o.hide_render=False;o.location+=Vector(((i%7)*.9,(i//7)*1.1,0))
bpy.ops.wm.save_as_mainfile(filepath=str(P/'food-ingredients.blend'));(P/'manifest.json').write_text(json.dumps(records,indent=2))
