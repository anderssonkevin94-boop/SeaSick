"""Standalone design study only. Run through Blender MCP. No Unity export."""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
OUT=ROOT/'docs/art-direction/ship-concept/v2'
OUT.mkdir(parents=True,exist_ok=True)
previous=bpy.context.window.scene
name='Adventure_Brig_Design_Study_V2'
scene=bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
bpy.context.window.scene=scene
random.seed(51)
def mat(name,col,metal=0,rough=.7,emit=0):
 m=bpy.data.materials.new('BrigStudy_'+name);m.diffuse_color=(*col,1);m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*col,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
 if emit:p.inputs['Emission Color'].default_value=(*col,1);p.inputs['Emission Strength'].default_value=emit
 return m
wood=[mat('Honey_'+str(i),(.39+.038*i,.16+.025*i,.047+.012*i)) for i in range(5)]
deckm=[mat('Deck_'+str(i),(.51+.025*i,.285+.022*i,.11+.013*i)) for i in range(4)]
teal=mat('Deep_teal',(.035,.20,.225));coral=mat('Coral',(.55,.105,.065));dark=mat('Dark_iron',(.047,.065,.077),.3)
gold=mat('Aged_brass',(.55,.34,.085),.55,.34);cream=mat('Ivory_canvas',(.98,.86,.65));seam=mat('Canvas_seams',(.63,.50,.30));rope=mat('Hemp',(.35,.25,.12));glass=mat('Warm_windows',(.96,.57,.12),0,.3,.5)
shadow=mat('Port_shadow',(.025,.033,.032));sea=mat('Study_water',(.045,.27,.35),.15,.28)
def mesh(name,vs,fs,ma):
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(name,me);scene.collection.objects.link(o)
 for m in ma if isinstance(ma,list) else [ma]:me.materials.append(m)
 return o
def cube(name,loc,size,ma,bevel=.04):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(ma)
 if bevel:b=o.modifiers.new('Soft crafted edges','BEVEL');b.width=bevel;b.segments=3;o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
 return o
def rod(name,a,b,r,ma,verts=10,r2=None):
 d=Vector(b)-Vector(a);bpy.ops.mesh.primitive_cone_add(vertices=verts,radius1=r,radius2=r if r2 is None else r2,depth=d.length,location=(Vector(a)+Vector(b))/2)
 o=bpy.context.object;o.name=name;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();o.data.materials.append(ma);return o
def line(name,points,r,ma):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=r;c.bevel_resolution=2
 sp=c.splines.new('POLY');sp.points.add(len(points)-1)
 for pt,co in zip(sp.points,points):pt.co=(*co,1)
 o=bpy.data.objects.new(name,c);scene.collection.objects.link(o);c.materials.append(ma);return o
def width(x):
 controls=[(-12,2.65),(-10,3.28),(-6,3.74),(0,3.9),(6,3.48),(10,2.12),(12,.8),(13,.08)]
 for i,((a,b),(c,d)) in enumerate(zip(controls,controls[1:])):
  if a<=x<=c:
   t=(x-a)/(c-a);prev=controls[max(0,i-1)];nxt=controls[min(len(controls)-1,i+2)]
   m0=(d-prev[1])/(c-prev[0]);m1=(nxt[1]-b)/(nxt[0]-a)
   return (2*t**3-3*t*t+1)*b+(t**3-2*t*t+t)*m0*(c-a)+(-2*t**3+3*t*t)*d+(t**3-t*t)*m1*(c-a)
 return .08
def top(x):return 2.6+.6*(abs(x)/13)**2+.35*max(0,-x/12)**3
xs=[-12+i*25/50 for i in range(51)]
# Eight wide strakes follow the sheer. Keel and planking are connected geometry.
levels=[(-2.45,.02),(-2.12,.36),(-1.5,.68),(-.7,.87),(.05,.96),(.75,1.00),(1.45,1.015),(2.12,1.01),(2.6,1.00)]
for side in [-1,1]:
 vs=[]
 for x in xs:
  bow=max(0,(x-7)/6);stern=max(0,(-x-8)/4)
  for z,w in levels:
   lift=(top(x)-2.6)*max(0,(z+2.45)/5.05)
   bottomlift=1.0*bow**2*max(0,1-(z+2.45)/5.05)
   vs.append((x,side*width(x)*w,z+lift+bottomlift))
 fs=[]
 for i in range(50):
  for j in range(8):fs.append((i*9+j,(i+1)*9+j,(i+1)*9+j+1,i*9+j+1))
 o=mesh('Hull_Port' if side<0 else 'Hull_Starboard',vs,fs,wood+[teal])
 for p in o.data.polygons:p.material_index=5 if p.index%8>=6 else (p.index%8)%5
 # Fine broad strake seams, two bold timber bands and coral accent.
 for j in [2,3,4,5,6,8]:
  points=[vs[i*9+j] for i in range(51)]
  line('Hull_strake',points,.026 if j not in [5,8] else .09,wood[1] if j!=8 else deckm[2])
 line('Coral_sheer_stripe',[(x,side*(width(x)*1.018+.02),1.43+(top(x)-2.6)*.78) for x in xs],.063,coral)
# Closed transom, deck laid in long planks.
mesh('Transom',[(-12,-width(-12)*w,z+top(-12)-2.6) for z,w in levels]+[(-12,width(-12)*w,z+top(-12)-2.6) for z,w in reversed(levels)], [tuple(range(18))],wood[2])
for band in range(24):
 vs=[]
 for x in xs:
  for t in [band/24,(band+1)/24-.002]:vs.append((x,width(x)*(t*2-1),top(x)+.015))
 mesh('Deck_plank_%02d'%band,vs,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(50)],deckm[band%4])
# Short cabin and raised working quarterdeck.
cube('Stern_cabin',(-9.35,0,4.1),(5.1,5.35,2.05),teal,.12)
cube('Cabin_coral_sill',(-9.35,0,3.22),(5.22,5.48,.16),coral)
cube('Quarterdeck',(-9.45,0,5.22),(5.45,5.8,.30),deckm[2],.22)
for y in [-2.73,2.73]:
 for x in [-11.8,-9.5,-6.83]:cube('Cabin_timber',(x,y,4.2),(.17,.18,2),wood[2])
# Arched amber windows, actual polygon glass and frames.
def window(name,centre,horizontal,normal,w=.78,h=1.25):
 c=Vector(centre);u=Vector(horizontal);v=Vector((0,0,1));n=Vector(normal)
 pts=[(-w/2,0),(w/2,0),(w/2,h-w/2)]+[(math.cos(a)*w/2,h-w/2+math.sin(a)*w/2) for a in [math.pi*k/8 for k in range(1,9)]]
 coords=[c+u*a+v*b for a,b in pts];mesh(name+'_glass',coords,[tuple(range(len(coords)))],glass)
 line(name+'_frame',coords+[coords[0]],.09,wood[1]);rod(name+'_mullion',c,c+v*(h-.04),.035,gold);rod(name+'_cross',c-u*w/2+v*.56,c+u*w/2+v*.56,.028,gold)
for y in [-1.62,0,1.62]:window('Stern_window',(-12.02,y,3.48),(0,1,0),(-1,0,0),.95,1.38)
for side in [-1,1]:
 for x in [-10.45,-8.75]:window('Cabin_side_window',(x,side*2.79,3.48),(1,0,0),(0,side,0))
cube('Cabin_door',(-6.75,0,3.96),(.1,1.10,1.55),wood[0])
for y in [-.55,.55]:cube('Door_frame',(-6.65,y,3.96),(.14,.10,1.7),deckm[1])
# Stairs on one side preserve an open deck corridor.
for i in range(8):cube('Quarterdeck_step',(-4.8-i*.27,-1.62,2.93+i*.29),(.36,1.24,.16),deckm[i%4])
for y in [-2.30,-.95]:rod('Stair_handrail',(-4.55,y,3.75),(-6.9,y,6),.055,wood[2])
# Deck rail posts and long caps; raised stern rails separately.
for side in [-1,1]:
 for x in [-6.4,-4.4,-2.4,0,2.4,4.8,7,9,10.8,12]:
  y=side*(width(x)-.1);z=top(x);cube('Rail_post',(x,y,z+.38),(.15,.18,.78),wood[2]);cube('Post_cap',(x,y,z+.8),(.24,.25,.1),deckm[2])
 line('Bulwark_cap',[(x,side*(width(x)-.08),top(x)+.73) for x in xs if x>=-6.5],.11,deckm[2])
 for x in [-11.9,-10.4,-8.7,-7]:cube('Quarterdeck_post',(x,side*2.8,5.62),(.16,.17,.7),wood[2])
 rod('Quarterdeck_rail',(-12,side*2.8,5.96),(-6.8,side*2.8,5.96),.10,deckm[2])
rod('Transom_rail',(-12,-2.8,5.96),(-12,2.8,5.96),.12,deckm[2])
# Gun ports as separate inset faces, framing, open lids and gun barrels.
for side in [-1,1]:
 for x in [-4.8,.2,5.1]:
  y=side*(width(x)*1.025+.045);z=1.99
  cube('Gunport_recess',(x,y,z),(1.05,.10,.85),shadow,.015)
  for dx in [-.57,.57]:cube('Coral_port_jamb',(x+dx,y+side*.065,z),(.13,.16,1.0),coral)
  for dz in [-.49,.49]:cube('Coral_port_sill',(x,y+side*.065,z+dz),(1.25,.16,.13),coral)
  rod('Cannon_barrel',(x,y-side*.3,z),(x,y+side*.78,z+.04),.19,dark,12,r2=.14)
  rod('Muzzle',(x,y+side*.77,z+.04),(x,y+side*.82,z+.04),.105,shadow,12)
  lid=cube('Open_port_lid',(x,y+side*.42,z+.77),(1.07,.86,.10),teal);lid.rotation_euler.x=side*.25
# Mast hardware, yards, curved separate sail panels.
def sail(name,x,ztop,span,height):
 vs=[];nu=24;nv=16
 def pos(u,v):
  belly=1.65 if height>4 else 1.05
  return (x+.76+belly*math.sin(math.pi*v)*math.cos(u*math.pi/2),u*span/2*(1+.10*v-.12*math.sin(math.pi*v)),ztop-height*v+.74*u*u*v+.07*math.sin(u*math.pi*2)*v)
 for j in range(nv+1):
  for i in range(nu+1):vs.append(pos(-1+2*i/nu,j/nv))
 fs=[(j*(nu+1)+i,j*(nu+1)+i+1,(j+1)*(nu+1)+i+1,(j+1)*(nu+1)+i) for j in range(nv) for i in range(nu)]
 o=mesh(name,vs,fs,cream);sol=o.modifiers.new('Canvas thickness','SOLIDIFY');sol.thickness=.018
 for u in [-1,-.75,-.5,-.25,0,.25,.5,.75,1]:line('Sail_panel_seam',[pos(u,j/nv) for j in range(nv+1)],.012,seam)
 line('Canvas_foot',[pos(-1+2*i/nu,1) for i in range(nu+1)],.03,rope)
 rod('Yard',(x+.76,-span*.56,ztop+.06),(x+.76,span*.56,ztop+.06),.10,wood[2],12,r2=.07)
for x,peak in [(-3.3,17.2),(5,18.2)]:
 rod('Mast',(x,0,2.7),(x,0,peak),.23,wood[2],12,r2=.11)
 for z in [3,3.5,8.6,13.2]:rod('Mast_band',(x,0,z),(x,0,z+.14),.245,gold,12)
 sail('Sail_course',x,11.6 if x<0 else 12.5,8.0 if x<0 else 8.6,4.7)
 sail('Sail_topsail',x,15.5 if x<0 else 16.5,5.4 if x<0 else 5.9,3.15)
 rod('Mast_platform',(x-.35,0,12.7 if x<0 else 13.7),(x-.35,0,12.9 if x<0 else 13.9),.48,wood[1],12)
 # Paired shrouds and ratlines fall to deck rather than float beside sails.
 for side in [-1,1]:
  for dx in [-.72,.72]:rod('Shroud',(x+dx,side*2.9,3.2),(x,side*.18,13.1),.026,rope,6)
  for k in range(1,20):
   t=k/22;z=3.2+9.9*t;rod('Ratline',(x-.72*(1-t),side*(2.9*(1-t)+.18*t),z),(x+.72*(1-t),side*(2.9*(1-t)+.18*t),z),.017,rope,6)
rod('Bowsprit',(10.3,0,3.2),(17.5,0,5.1),.22,wood[2],12,r2=.11)
rod('Forestay',(5,0,17.8),(17.1,0,5.05),.033,rope,6)
rod('Backstay',(-3.3,0,17),(-11.6,0,5.9),.03,rope,6)
rod('Mast_stay',(-3.3,0,16.9),(5,0,17.8),.035,rope,6)
mesh('Triangular_jib',[(6.1,0,15.8),(16.3,0,5.2),(8.1,.35,6.1),(10.1,.72,8.7)],[(0,1,3),(1,2,3),(2,0,3)],cream)
line('Jib_hem',[(6.1,0,15.8),(16.3,0,5.2),(8.1,.35,6.1),(6.1,0,15.8)],.027,rope)
# Pennant, wheel, capstan, hatch and two restrained cargo barrels.
mesh('Coral_pennant',[(-3.3,0,17.2),(-3.3,0,18),(-5,.12,17.8),(-7,.4,18.0),(-6.1,.48,17.25),(-4.9,.25,17.3)],[(0,1,2,5),(2,3,4,5)],coral)
rod('Wheel_pedestal',(-9.2,0,5.3),(-9.2,0,6.2),.12,wood[1])
bpy.ops.mesh.primitive_torus_add(major_radius=.55,minor_radius=.055,major_segments=24,minor_segments=8,location=(-9.2,0,6.3),rotation=(0,math.pi/2,0));bpy.context.object.name='Helm_wheel';bpy.context.object.data.materials.append(wood[2])
for i in range(8):
 a=i*math.tau/8;rod('Wheel_spoke',(-9.2,0,6.3),(-9.2,.68*math.cos(a),6.3+.68*math.sin(a)),.035,wood[2])
cube('Cargo_hatch',(.2,0,2.86),(2.5,2.2,.23),wood[1])
for i in range(8):rod('Hatch_grate',(-.85+i*.30,-1,3),(-.85+i*.30,1,3),.035,dark)
rod('Capstan',(8.8,0,2.9),(8.8,0,3.9),.30,wood[1],12)
rod('Capstan_bar',(8.8,-.9,3.75),(8.8,.9,3.75),.06,wood[2])
for x,y in [(-1,2.4),(2,-2.4)]:
 rod('Barrel',(x,y,2.8),(x,y,3.8),.4,wood[2],12)
 for z in [2.96,3.62]:rod('Barrel_hoop',(x,y,z),(x,y,z+.08),.42,dark,12)
def lantern(x,y,z):
 rod('Lantern_post',(x,y,z-.4),(x,y,z+.15),.055,gold)
 cube('Lantern_glass',(x,y,z+.5),(.32,.32,.53),glass,.025)
 for dx in [-.19,.19]:
  for dy in [-.19,.19]:rod('Lantern_frame',(x+dx,y+dy,z+.2),(x+dx,y+dy,z+.81),.035,gold)
 rod('Lantern_roof',(x,y,z+.78),(x,y,z+1.07),.32,gold,4,r2=.02)
for y in [-2.63,2.63]:lantern(-11.7,y,6.05)
lantern(10.5,0,3.9)
# Final design cleanup shared by generator and live study.
from mathutils import Matrix
for o in list(scene.objects):
 if o.name.startswith(('Sail_course','Sail_topsail')):
  for p in o.data.polygons:p.use_smooth=True
 if o.name.startswith(('Sail_course','Sail_topsail','Sail_panel_seam','Canvas_foot','Yard')):
  center=sum((o.matrix_world@Vector(c) for c in o.bound_box),Vector())/8
  x=-3.3 if center.x<1 else 5
  pivot=Vector((x,0,0));o.matrix_world=Matrix.Translation(pivot)@Matrix.Rotation(math.radians(24),4,'Z')@Matrix.Translation(-pivot)@o.matrix_world
port=next(o for o in scene.objects if o.name.startswith('Hull_Port'))
star=next(o for o in scene.objects if o.name.startswith('Hull_Starboard'))
transom=next(o for o in scene.objects if o.name=='Transom' or o.name.startswith('Transom.'))
for v,p in zip(transom.data.vertices,[v.co.copy() for v in port.data.vertices[:9]]+[v.co.copy() for v in star.data.vertices[:9]][::-1]):v.co=p
transom.data.update()
for j in [2,3,4,5,6,8]:
 a=port.data.vertices[j].co.copy();b=star.data.vertices[j].co.copy();a.x-=.02;b.x-=.02
 rod('Transom_strake',a,b,.065,coral if j==6 else wood[2])
rod('Carved_bow_stem',(13,0,-.5),(13,0,3.55),.17,wood[2],10)
cube('Stern_rudder',(-12.18,0,-.35),(.45,.22,2.3),wood[1],.07)
scene.view_settings.exposure=.65

# Deform construction together so rails, frames and deck remain seated.
# Artistic shaping only; hydrostatics intentionally remain outside this study.
def sculpt(p):
 x,y,z=p
 bow=max(0,min(1,(x-5)/8));stern=max(0,min(1,(-x-5)/7))
 surface=max(0,min(1,(z+2.5)/5.8))
 nx=x+bow**2*(1.35*surface-.90*(1-surface))-.48*stern**2*surface
 dz=(.90*bow**3+.62*stern**2)*max(0,min(1,(z+1)/3.6))
 # Tumblehome and raked cabin: broad seated base, quieter upper silhouette.
 cabin=max(0,min(1,(-x-6.4)/1.2))*max(0,min(1,(z-3.3)/2.2))
 ny=y*(1-.095*cabin)
 nx-=.30*cabin
 return Vector((nx,ny,z+dz))
bpy.context.view_layer.update()
for ob in list(scene.objects):
 if ob.type not in {'MESH','CURVE'}:continue
 # Apply modifiers before bending to retain the intended rounded roof corners.
 if ob.type=='MESH' and ob.modifiers:
  bpy.context.view_layer.objects.active=ob
  for modifier in list(ob.modifiers):bpy.ops.object.modifier_apply(modifier=modifier.name)
 mw=ob.matrix_world.copy();inv=mw.inverted()
 if ob.type=='MESH':
  for vertex in ob.data.vertices:vertex.co=inv@sculpt(mw@vertex.co)
  ob.data.update()
 else:
  for spline in ob.data.splines:
   for point in spline.points:
    co=inv@sculpt(mw@Vector(point.co[:3]));point.co=(*co,1)

# Quiet presentation water isolates the ship, not a fabricated game screenshot.
cube('Presentation_water',(0,0,-.5),(2000,2000,.1),sea,0)
world=bpy.data.worlds.new('BrigStudy_World');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.36,.50,.66,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
ld=bpy.data.lights.new('BrigStudy_Sun','AREA');lo=bpy.data.objects.new('BrigStudy_Sun',ld);scene.collection.objects.link(lo);lo.location=(8,-20,35);ld.energy=10000;ld.shape='DISK';ld.size=12;lo.rotation_euler=(Vector((0,0,5))-lo.location).to_track_quat('-Z','Y').to_euler()
ld=bpy.data.lights.new('BrigStudy_Fill','AREA');lo=bpy.data.objects.new('BrigStudy_Fill',ld);scene.collection.objects.link(lo);lo.location=(-20,10,19);ld.energy=3500;ld.size=15;lo.rotation_euler=(Vector((0,0,6))-lo.location).to_track_quat('-Z','Y').to_euler()
cd=bpy.data.cameras.new('BrigStudy_Camera');camera=bpy.data.objects.new('BrigStudy_Camera',cd);scene.collection.objects.link(camera);scene.camera=camera;cd.type='ORTHO';cd.ortho_scale=34
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1600;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
views=[('front',(42,-15,21),(2,0,8)),('back',(-42,-14,20),(-1,0,8)),('side',(0,-50,17),(2,0,8))]
for label,position,target in views:
 camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/('brig-'+label+'.png'))
 bpy.ops.render.render(write_still=True)
bpy.data.libraries.write(str(ROOT/'tools/blender/source/adventure-brig-design-v2.blend'),{scene})
print('Standalone brig complete:',len(scene.objects),'objects. Three review renders. No Unity exports.')
bpy.context.window.scene=previous
