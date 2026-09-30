import bpy,bmesh,math,json,ast
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[2];P=ROOT/'art-staging/worker-tools-v1';P.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);sc=bpy.context.scene;sc.unit_settings.system='METRIC'
pal={'wood':(.43,.22,.075,1),'woodlight':(.65,.37,.14,1),'grip':(.18,.085,.035,1),'iron':(.12,.19,.23,1),'edge':(.52,.63,.66,1),'stone':(.27,.34,.36,1),'rope':(.59,.44,.23,1)}
mat=bpy.data.materials.new('SS_WorkerTools_GameColor');mat.use_nodes=True;n=mat.node_tree.nodes;vc=n.new('ShaderNodeVertexColor');vc.layer_name='GameColor';bs=n.get('Principled BSDF');bs.inputs['Roughness'].default_value=.64;mat.node_tree.links.new(vc.outputs['Color'],bs.inputs['Base Color'])
# Reuse only the tested mesh builder, without executing the resource kit generator.
a=ast.parse((ROOT/'tools/blender/resource_kit_v1.py').read_text());node=next(n for n in a.body if isinstance(n,ast.ClassDef) and n.name=='Mesh');exec(compile(ast.Module(body=[node],type_ignores=[]),'<Mesh helper>','exec'))
def pt(p):x,y,z=p;return(x,-z,y)
def face(m,points,c,shade=1):m.face([pt(p) for p in points],c,shade)
def haft(m,lo,hi,r,c='wood'):
 rings=[]
 for y,scale in [(lo,.86),(lo+.012,1),(hi-.012,1),(hi,.83)]:rings.append([(math.cos(i*math.tau/8)*r*scale,y,math.sin(i*math.tau/8)*r*scale) for i in range(8)])
 face(m,rings[0][::-1],c);face(m,rings[-1],c)
 for a,b in zip(rings,rings[1:]):
  for i in range(8):face(m,[a[i],a[(i+1)%8],b[(i+1)%8],b[i]],c,.82+.08*(i%3))
def box(m,center,size,c,bevel=.004):
 # Clip all cube edges once; write the resulting polygons into the single color mesh.
 bpy.ops.mesh.primitive_cube_add(size=1);o=bpy.context.object;o.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  mod=o.modifiers.new('Broad bevel','BEVEL');mod.width=bevel;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
 for p in o.data.polygons:face(m,[tuple(o.data.vertices[i].co+Vector(center)) for i in p.vertices],c,1.12 if len(p.vertices)==4 else .83)
 bpy.data.objects.remove(o,do_unlink=True)
def cheek(m,outline,thick,c):
 # Extruded polygon in the Y/Z working plane.
 for sign in [-1,1]:face(m,[(sign*thick/2,y,z) for y,z in (outline if sign>0 else outline[::-1])],c)
 for i,(y,z) in enumerate(outline):
  yy,zz=outline[(i+1)%len(outline)];face(m,[(-thick/2,y,z),(thick/2,y,z),(thick/2,yy,zz),(-thick/2,yy,zz)],'edge' if c=='iron' else c)
assets=[];report=[]
def save(name,m,contact):
 o=m.obj(name);bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.fbx(filepath=str(P/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 o.data.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(o.data);bad=sum(f.calc_area()<1e-10 for f in bm.faces);boundary=sum(e.is_boundary for e in bm.edges);bm.free()
 report.append({'name':name,'triangles':len(o.data.loop_triangles),'size_blender_m':list(o.dimensions),'grip_source':[0,0,0],'contact_game_frame':contact,'zero_area_faces':bad,'boundary_edges':boundary});o.hide_render=True;assets.append(o)
m=Mesh();haft(m,-.04,.32,.021);haft(m,-.035,.065,.023,'grip');box(m,(0,.30,.015),(.075,.077,.15),'iron');box(m,(0,.30,.094),(.080,.082,.012),'edge',.003);box(m,(0,.30,-.073),(.044,.050,.035),'iron');save('Hammer',m,[0,.30,.10])
m=Mesh();haft(m,-.05,.62,.023);haft(m,-.04,.095,.025,'grip');cheek(m,[(.605,-.043),(.625,.027),(.67,.13),(.455,.13),(.51,.02),(.515,-.043)],.035,'iron');cheek(m,[(.67,.13),(.455,.13),(.447,.112),(.662,.112)],.014,'edge');box(m,(0,.56,-.025),(.062,.081,.075),'iron');save('Axe',m,[0,.56,.13])
m=Mesh()
# Continuous D-shaped wooden ring: the fist grips its front bar at the origin.
outer=[(-.085,.022),(.09,.022),(.11,-.045),(.085,-.13),(-.085,-.13),(-.105,-.05)]
inner=[(-.045,-.018),(.052,-.018),(.065,-.05),(.05,-.09),(-.05,-.09),(-.065,-.05)]
for i in range(6):
 j=(i+1)%6
 for sign in [-1,1]:
  face(m,[(sign*.018,*outer[i]),(sign*.018,*outer[j]),(sign*.018,*inner[j]),(sign*.018,*inner[i])],'woodlight')
 for ring in [outer,inner]:
  face(m,[(-.018,*ring[i]),(.018,*ring[i]),(.018,*ring[j]),(-.018,*ring[j])],'wood')
outline=[(.075,-.052),(.57,-.022),(.58,.025)]
for i in range(12,-1,-1):
 y=.08+i*.037;outline.extend([(y,.042),(y-.017,.055)])
outline.append((.075,.015));cheek(m,outline,.010,'iron');save('Saw',m,[0,.33,.055])
m=Mesh();haft(m,-.08,1.065,.023);haft(m,-.065,.09,.025,'grip');box(m,(0,1.02,.033),(.070,.060,.11),'iron');box(m,(0,1.014,.09),(.19,.027,.10),'iron');box(m,(0,1.014,.135),(.19,.022,.010),'edge',.003);save('Hoe',m,[0,1.02,.14])
m=Mesh();haft(m,-.06,.64,.019);haft(m,-.055,.035,.021,'woodlight')
# Paddle blade: broad chamfered wooden oval, flattened in Z.
poly=[(-.035,.575),(.035,.575),(.060,.63),(.052,.73),(.03,.755),(-.03,.755),(-.052,.73),(-.06,.63)]
for sign in [-1,1]:face(m,[(x,y,sign*.013) for x,y in (poly if sign>0 else poly[::-1])],'woodlight')
for i,(x,y) in enumerate(poly):
 xx,yy=poly[(i+1)%len(poly)];face(m,[(x,y,-.013),(xx,yy,-.013),(xx,yy,.013),(x,y,.013)],'wood')
save('StirPaddle',m,[0,.66,0])
for iron in [False,True]:
 m=Mesh();haft(m,-.65,1.17,.020);haft(m,-.10,.13,.022,'grip');haft(m,1.12,1.19,.03,'iron' if iron else 'rope')
 w=.042 if iron else .052;tip=1.45 if iron else 1.405;base=1.16
 outline=[(0,tip),(w,base+.095),(w*.70,base+.033),(0,base),(-w*.70,base+.033),(-w,base+.095)]
 for side in [-1,1]:
  ridge=(0,base+.12,side*(.012 if iron else .021))
  for i,(x,y) in enumerate(outline):
   xx,yy=outline[(i+1)%len(outline)];face(m,[(x,y,0),(xx,yy,0),ridge],'edge' if iron and i%2 else 'iron' if iron else 'stone',1+.13*(i%2))
 save('SpearIron' if iron else 'SpearStone',m,[0,tip,0])
sc.world=bpy.data.worlds.new('Review');sc.world.color=(.24,.24,.24)
# Each image is framed independently; declared lengths accompany the contact sheet.
for pos,energy,size in [((3,-4,6),600,5),((-3,0,4),450,4)]:
 bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.data.energy=energy;l.data.size=size;l.rotation_euler=(-l.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;sc.camera=cam;cam.data.type='ORTHO';sc.render.engine='CYCLES';sc.cycles.samples=20;sc.cycles.use_denoising=True;sc.render.resolution_x=360;sc.render.resolution_y=430;sc.render.resolution_percentage=100;sc.render.film_transparent=True
for o in assets:
 o.hide_render=False;coords=[Vector(c) for c in o.bound_box];center=sum(coords,Vector())/8;cam.location=center+Vector((3,-1.7,1.0));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=max(o.dimensions)*1.25
 sc.render.filepath=str(P/(o.name+'.png'));bpy.ops.render.render(write_still=True);o.hide_render=True
for i,o in enumerate(assets):o.hide_render=False;o.location.x=i*.5
bpy.ops.wm.save_as_mainfile(filepath=str(P/'worker-tools.blend'));(P/'manifest.json').write_text(json.dumps(report,indent=2))
