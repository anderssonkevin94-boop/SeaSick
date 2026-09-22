"""Standalone design study only. Run through Blender MCP. No Unity export."""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
OUT=ROOT/'docs/art-direction/ship-concept/v12'
OUT.mkdir(parents=True,exist_ok=True)
previous=bpy.context.window.scene
name='Adventure_Brig_Design_Study_V12'
scene=bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
bpy.context.window.scene=scene
try:
 random.seed(51)
 def mat(name,col,metal=0,rough=.7,emit=0):
  m=bpy.data.materials.new('BrigStudy_'+name);m.diffuse_color=(*col,1);m.use_nodes=True
  p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*col,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
  if emit:p.inputs['Emission Color'].default_value=(*col,1);p.inputs['Emission Strength'].default_value=emit
  return m
 wood=[mat('Honey_'+str(i),(.22+.045*i,.065+.020*i,.012+.006*i)) for i in range(5)]
 deckm=[mat('Deck_'+str(i),(.48+.030*i,.21+.020*i,.045+.009*i)) for i in range(4)]
 teal=mat('Deep_teal',(.006,.095,.145),rough=.42);coral=mat('Coral',(.72,.026,.014),rough=.46);dark=mat('Dark_iron',(.012,.022,.031),.65,.4)
 gold=mat('Aged_brass',(.72,.38,.045),.78,.25);cream=mat('Ivory_canvas',(.94,.76,.46),rough=.92);seam=mat('Canvas_seams',(.40,.25,.10));rope=mat('Hemp',(.21,.12,.042));glass=mat('Warm_windows',(1.0,.40,.045),0,.3,.5)
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
  c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=r;c.bevel_resolution=2;c.use_fill_caps=True
  sp=c.splines.new('POLY');sp.points.add(len(points)-1)
  for pt,co in zip(sp.points,points):pt.co=(*co,1)
  o=bpy.data.objects.new(name,c);scene.collection.objects.link(o);c.materials.append(ma);return o
 def width(x):
  # One continuous fair waterline: broad shoulders flow all the way into the ends.
  t=(x+2.0)/15.0
  return max(.08,4.65*max(0,1-t*t)**.65)
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
  line(name+'_frame',coords+[coords[0]],.12,wood[1]);rod(name+'_mullion',c+n*.045,c+v*(h-.04)+n*.045,.035,gold);rod(name+'_cross',c-u*w/2+v*(h*.46)+n*.045,c+u*w/2+v*(h*.46)+n*.045,.028,gold)
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
 # Broad cloth panels and reinforced hems retain a readable, plain canvas finish.
 cloth=[mat('Canvas_panel_'+str(i),(.94*f,.76*f,.46*f),rough=.94) for i,f in enumerate([.94,1.0,.975,1.025])]
 hem=mat('Canvas_edge_binding',(.68,.49,.245),rough=.95)
 # Turned cartoon barrels: fat breech, pinched neck and a bell-shaped hollow muzzle.
 def character_barrel(prefix,origin,side,length=1.86,scale=1.0):
  origin=Vector(origin);axis=Vector((0,side,.105)).normalized();right=Vector((1,0,0));up=right.cross(axis).normalized()
  profile=[(-.19,0),(-.18,.10),(-.12,.15),(-.03,.14),(0,.09),(.05,.24),(.16,.35),(.38,.39),(.60,.37),(.72,.29),(1.24,.245),(1.32,.33),(1.43,.37),(1.57,.37),(1.65,.33),(1.65,.225),(1.27,.205),(1.24,0)]
  verts=[];faces=[];count=24
  for t,r in profile:
   centre=origin+axis*(t*length/1.65)
   for k in range(count):
    angle=math.tau*k/count;verts.append(centre+scale*r*(right*math.cos(angle)+up*math.sin(angle)))
  for i in range(len(profile)-1):
   for k in range(count):faces.append((i*count+k,i*count+(k+1)%count,(i+1)*count+(k+1)%count,(i+1)*count+k))
  ob=mesh(prefix+'_sculpted_barrel',verts,[tuple(reversed(f)) for f in faces],[dark,gold,shadow])
  for face in ob.data.polygons:
   ring=face.index//count;face.material_index=2 if ring>=15 else (1 if ring in [1,2,3,11,12,13,14] else 0);face.use_smooth=True
  # Thick reinforcing collar at the breech, with a brass touchhole.
  rod(prefix+'_reinforcing_band',origin+axis*.34,origin+axis*.44,.40*scale,dark,24)
  rod(prefix+'_touchhole',origin+axis*.30+Vector((0,0,.35*scale)),origin+axis*.30+Vector((0,0,.40*scale)),.055*scale,gold,10)
 # Mast hardware, yards, curved separate sail panels.
 def sail(name,x,ztop,span,height):
  vs=[];nu=24;nv=16
  def pos(u,v):
   belly=2.45 if height>4 else 1.48
   return (x+.76+belly*math.sin(math.pi*v*.90)**.80*math.cos(u*math.pi/2)+.045*math.sin(u*math.pi*8)*math.sin(math.pi*v)**2,u*span/2*(1+.06*v-.025*math.sin(math.pi*v)),ztop-height*v+.70*(1-u*u)*v)
  for j in range(nv+1):
   for i in range(nu+1):vs.append(pos(-1+2*i/nu,j/nv))
  fs=[(j*(nu+1)+i,j*(nu+1)+i+1,(j+1)*(nu+1)+i+1,(j+1)*(nu+1)+i) for j in range(nv) for i in range(nu)]
  o=mesh(name,vs,fs,cloth)
  for poly in o.data.polygons:poly.material_index=((poly.index%nu)//3)%len(cloth)
  sol=o.modifiers.new('Canvas thickness','SOLIDIFY');sol.thickness=.018
  for u in [-1,-.75,-.5,-.25,0,.25,.5,.75,1]:line('Sail_panel_seam',[pos(u,j/nv) for j in range(nv+1)],.008,seam)
  line('Canvas_foot',[pos(-1+2*i/nu,1) for i in range(nu+1)],.038,hem)
  for u in [-1,1]:line('Sail_edge_hem',[pos(u,j/nv) for j in range(nv+1)],.035,hem)
  line('Sail_head_hem',[pos(-1+2*i/nu,0) for i in range(nu+1)],.032,hem)
  for k in range(13):
   u=-.96+1.92*k/12;y=u*span/2
   line('Sail_yard_lashing',[(x+.76+.13*math.cos(a),y,ztop+.025+.13*math.sin(a)) for a in [math.tau*j/16 for j in range(17)]],.016,rope)
  rod('Yard',(x+.76,-span*.56,ztop+.06),(x+.76,span*.56,ztop+.06),.10,wood[2],12,r2=.07)
  # A single curling-wave signature on the aft mainsail, visible both sides.
  if False and name=='Sail_course' and x<0:
   def paintstroke(label,path,thickness):
    for face in [-1,1]:
     verts=[]
     for k,(u,v) in enumerate(path):
      prev=Vector(path[max(0,k-1)]);nxt=Vector(path[min(len(path)-1,k+1)])
      d=(nxt-prev).normalized();n=Vector((-d.y,d.x))*thickness
      for q in [Vector((u,v))+n,Vector((u,v))-n]:
       p=Vector(pos(q.x,q.y));p.x+=face*.027;verts.append(p)
     mesh('Sail_wave_emblem_'+label,verts,[(k*2,k*2+1,k*2+3,k*2+2) for k in range(len(path)-1)],coral)
   curl=[]
   for k in range(65):
    a=-.35+k/64*math.pi*2.05;r=.24*(1-.77*k/64)
    curl.append((-.01+math.cos(a)*r,.48+math.sin(a)*r*.88))
   paintstroke('curl',curl,.023)
   paintstroke('swell',[(-.30+.60*k/40,.76-.07*math.sin(k/40*math.pi*2)) for k in range(41)],.022)
  # One broad canvas repair with sparse stitches; readable, not distressed.
  if False and name=='Sail_course' and x>0:
   patch=mat('Sail_patch',(.77,.64,.41))
   pts=[(-.69,.68),(-.40,.66),(-.37,.84),(-.66,.85)]
   for face in [-1,1]:
    coords=[]
    for j in range(9):
     for i in range(9):
      v=.67+.18*j/8;u=-.68+.29*i/8
      p=Vector(pos(u,v));p.x+=face*.04;coords.append(p)
    mesh('Sail_patch',coords,[(j*9+i,j*9+i+1,(j+1)*9+i+1,(j+1)*9+i) for j in range(8) for i in range(8)],patch)
   for k in range(6):
    u=-.67+k*.05
    p=Vector(pos(u,.68));q=Vector(pos(u,.71));p.x+=.04;q.x+=.04
    rod('Sail_patch_stitch',p,q,.016,seam,6)
 
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
 # A single continuous, filled jib with restrained edge tension.
 A=Vector((9.2,0,13.25));B=Vector((16.3,0,5.2));C=Vector((10.3,0,6.6));res=24
 vs=[];ids={}
 def jibpos(i,j):
  b=i/res;c=j/res;a=1-b-c
  p=A*a+B*b+C*c
  p.y+=.80*27*a*b*c
  p.z+=.40*4*b*c
  return p
 for i in range(res+1):
  for j in range(res+1-i):ids[i,j]=len(vs);vs.append(jibpos(i,j))
 fs=[]
 for i in range(res):
  for j in range(res-i):
   fs.append((ids[i,j],ids[i+1,j],ids[i,j+1]))
   if i+j<res-1:fs.append((ids[i+1,j],ids[i+1,j+1],ids[i,j+1]))
 jib=mesh('Triangular_jib',vs,fs,cloth)
 for poly in jib.data.polygons:poly.material_index=int(poly.center.x)%4
 for p in jib.data.polygons:p.use_smooth=True
 mod=jib.modifiers.new('Canvas thickness','SOLIDIFY');mod.thickness=.018
 for edge in [[jibpos(i,0) for i in range(res+1)],[jibpos(0,j) for j in range(res+1)],[jibpos(i,res-i) for i in range(res+1)]]:line('Jib_hem',edge,.023,rope)
 for i in [4,8,12,16,20]:line('Jib_panel_seam',[jibpos(i,j) for j in range(res+1-i)],.008,seam)
 # Head halyard, tack tie and paired working sheets secure every corner.
 line('Jib_halyard',[A,(5,0,17.8),(5,-.28,3.2)],.028,rope)
 line('Jib_tack_tie',[B,(17.1,0,5.05)],.035,rope)
 for side in [-1,1]:
  line('Jib_sheet',[C,(9.8,side*1.05,5.25),(9.1,side*2.0,top(9.1)+.78)],.032,rope)
 for t in [.08,.24,.40,.56,.72,.88]:
  p=A.lerp(B,t);f=(17.8-p.z)/12.75
  stay=Vector((5+12.1*f,0,p.z))
  line('Jib_stay_hank',[p,stay+Vector((0,-.045,0)),stay+Vector((0,.045,0)),p],.018,rope)
 # Pennant, wheel, capstan, hatch and two restrained cargo barrels.
 mesh('Coral_pennant',[(-3.3,0,17.2),(-3.3,0,18),(-5.1,.27,17.75),(-8.2,.75,18.12),(-7.25,.92,17.36),(-5.0,.28,17.12)],[(0,1,2,5),(2,3,4,5)],coral)
 rod('Wheel_pedestal',(-9.2,0,5.3),(-9.2,0,6.2),.12,wood[1])
 bpy.ops.mesh.primitive_torus_add(major_radius=.55,minor_radius=.055,major_segments=24,minor_segments=8,location=(-9.2,0,6.3),rotation=(0,math.pi/2,0));bpy.context.object.name='Helm_wheel';bpy.context.object.data.materials.append(wood[2])
 for i in range(8):
  a=i*math.tau/8;rod('Wheel_spoke',(-9.2,0,6.3),(-9.2,.68*math.cos(a),6.3+.68*math.sin(a)),.035,wood[2])
 cube('Cargo_hatch',(.2,0,2.86),(2.5,2.2,.23),wood[1])
 for i in range(8):rod('Hatch_grate',(-.85+i*.30,-1,3),(-.85+i*.30,1,3),.035,dark)
 rod('Capstan',(8.8,0,2.9),(8.8,0,3.9),.30,wood[1],12)
 rod('Capstan_bar',(8.8,-.9,3.75),(8.8,.9,3.75),.06,wood[2])
 for x,y in [(9.5,-.8),(9.5,.8)]:
  rod('Barrel',(x,y,2.8),(x,y,3.8),.4,wood[2],12)
  for z in [2.96,3.62]:rod('Barrel_hoop',(x,y,z),(x,y,z+.08),.42,dark,12)
 def lantern(x,y,z):
  before=set(scene.objects)
  rod('Lantern_post',(x,y,z-.4),(x,y,z+.15),.055,gold)
  cube('Lantern_glass',(x,y,z+.5),(.32,.32,.53),glass,.025)
  for dx in [-.19,.19]:
   for dy in [-.19,.19]:rod('Lantern_frame',(x+dx,y+dy,z+.2),(x+dx,y+dy,z+.81),.035,gold)
  rod('Lantern_roof',(x,y,z+.78),(x,y,z+1.07),.32,gold,4,r2=.02)
  if x<-8:
   bpy.context.view_layer.update()
   anchor=Vector((x,y,z));factor=1.38
   for detail in set(scene.objects)-before:
    detail.location=anchor+(detail.location-anchor)*factor;detail.scale*=factor
 for y in [-2.63,2.63]:lantern(-11.7,y,6.05)
 lantern(10.5,0,3.9)
 bpy.context.view_layer.update()
 # Final design cleanup shared by generator and live study.
 from mathutils import Matrix
 for o in list(scene.objects):
  if o.name.startswith(('Sail_course','Sail_topsail')):
   for p in o.data.polygons:p.use_smooth=True
  if o.name.startswith(('Sail_course','Sail_topsail','Sail_panel_seam','Sail_wave_emblem','Sail_patch','Sail_edge_hem','Sail_head_hem','Sail_yard_lashing','Canvas_foot','Yard')):
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
 
 # Replace the inset deckhouse with an actual continuation of the hull shell.
 # Station indices and the upper hull vertices are reused, not overlapped.
 remove_prefixes=('Stern_cabin','Cabin_coral_sill','Cabin_timber','Quarterdeck',
                  'Cabin_side_window','Stern_window','Transom_rail','Stair_handrail',
                  'Cabin_door','Door_frame','Wheel_','Helm_wheel')
 for ob in list(scene.objects):
  if ob.name.startswith(remove_prefixes):bpy.data.objects.remove(ob,do_unlink=True)
 # Old aft lanterns are reseated onto the new full-width quarterdeck.
 for ob in list(scene.objects):
  if ob.name.startswith('Lantern') and ob.location.x<-8:bpy.data.objects.remove(ob,do_unlink=True)
 end=11 # x=-6.5, aligned to the original hull and deck station grid
 cabin_x=xs[:end+1];roof_z=5.25
 upper_edges={};join_checks=[]
 for side,prefix in [(-1,'Hull_Port'),(1,'Hull_Starboard')]:
  ob=next(o for o in scene.objects if o.name.startswith(prefix));old=ob.data
  vs=[v.co.copy() for v in old.vertices];fs=[tuple(p.vertices) for p in old.polygons];mi=[p.material_index for p in old.polygons]
  upper=[]
  for i,x in enumerate(cabin_x):
   upper.append(len(vs));vs.append(Vector((x,side*width(x)*.93,roof_z)))
  for i in range(end):
   # Lower row is the existing hull's sheer: shared vertex indices give no gap.
   fs.append((i*9+8,(i+1)*9+8,upper[i+1],upper[i]));mi.append(5)
   join_checks.append([ob.name,i*9+8,(i+1)*9+8])
  me=bpy.data.meshes.new(prefix+'_IntegratedCabin');me.from_pydata(vs,[],fs);me.update()
  for m in old.materials:me.materials.append(m)
  for p,idx in zip(me.polygons,mi):p.material_index=idx
  ob.data=me;upper_edges[side]=[vs[i] for i in upper]
 # The rear cabin wall is an extension of the transom itself.
 ob=next(o for o in scene.objects if o.name=='Transom' or o.name.startswith('Transom.'));old=ob.data
 vs=[v.co.copy() for v in old.vertices];fs=[tuple(p.vertices) for p in old.polygons]
 vs.extend([upper_edges[-1][0],upper_edges[1][0]])
 fs.append((8,9,19,18))
 me=bpy.data.meshes.new('Continuous_transom_and_cabin');me.from_pydata(vs,[],fs);me.update()
 me.materials.append(wood[2]);me.materials.append(teal);me.polygons[-1].material_index=1;ob.data=me
 # Close the front bulkhead right down onto the same cambered deck surface.
 x=cabin_x[-1];w=width(x);wt=w*.93
 mesh('Cabin_front_bulkhead',[(x,-w,top(x)),(x,w,top(x)),(x,wt,roof_z),(x,-wt,roof_z)],[(0,1,2,3)],teal)
 # Full-width quarterdeck follows the hull contour instead of a rectangular slab.
 for i in range(end):
  a,b=cabin_x[i:i+2]
  for band in range(18):
   t0=-1+band/9;t1=-1+(band+1)/9-.002
   mesh('Quarterdeck_fitted_plank',[(a,width(a)*.93*t0,roof_z+.045),(b,width(b)*.93*t0,roof_z+.045),(b,width(b)*.93*t1,roof_z+.045),(a,width(a)*.93*t1,roof_z+.045)],[(0,1,2,3)],deckm[band%4])
 for side in [-1,1]:
  line('Quarterdeck_solid_fascia',[(x,side*width(x)*.93,roof_z+.035) for x in cabin_x],.13,deckm[2])
  line('Quarterdeck_rail',[(x,side*(width(x)*.93-.05),roof_z+.82) for x in cabin_x],.10,deckm[2])
  for x in [-11.8,-10.4,-8.8,-7]:
   y=side*(width(x)*.93-.05);cube('Quarterdeck_post',(x,y,roof_z+.43),(.18,.18,.82),wood[2])
  # Quiet plank joints continue the topside treatment across the raised stern.
  for z in [3.9,4.55]:
   line('Cabin_plank_joint',[(x,side*width(x)*(1-.07*max(0,min(1,(z-top(x))/(roof_z-top(x)))))+side*.012,z) for x in cabin_x],.014,wood[0])
  # Windows curve with the wall rather than hovering off a rectangular box.
  for x in [-10.5,-8.45]:
   before=set(scene.objects)
   window('Cabin_side_window',(x,side*2.79,3.64),(1,0,0),(0,side,0),1.04,1.40)
   bpy.context.view_layer.update()
   for detail in set(scene.objects)-before:
    mw=detail.matrix_world.copy();inv=mw.inverted()
    def seat(p):
     t=max(0,min(1,(p.z-top(p.x))/(roof_z-top(p.x))))
     return Vector((p.x,side*(width(p.x)*(1-.07*t)+.075)+(p.y-side*2.79),p.z))
    if detail.type=='MESH':
     for v in detail.data.vertices:v.co=inv@seat(mw@v.co)
    elif detail.type=='CURVE':
     for spline in detail.data.splines:
      for p in spline.points:p.co=(*(inv@seat(mw@Vector(p.co[:3]))),1)
 rod('Quarterdeck_rear_fascia',(-12,-width(-12)*.93,roof_z+.035),(-12,width(-12)*.93,roof_z+.035),.13,deckm[2])
 rod('Quarterdeck_rear_rail',(-12,-width(-12)*.93,roof_z+.82),(-12,width(-12)*.93,roof_z+.82),.10,deckm[2])
 for y in [-1.55,0,1.55]:window('Stern_window',(-12.045,y,3.62),(0,1,0),(-1,0,0),1.14,1.40)
 # A continuous sill is deliberately omitted outside: the hull planking IS the wall.
 # The door bulkhead seats onto the deck and stair stringers meet the upper deck.
 floor=top(-6.5)
 cube('Cabin_door',(-6.46,.30,floor+.87),(.12,1.02,1.73),wood[0])
 for y in [-.26,.86]:cube('Door_frame',(-6.38,y,floor+.91),(.12,.12,1.85),deckm[1])
 for i in range(8):cube('Quarterdeck_step',(-4.6-i*.27,-1.62,2.99+i*.31),(.36,1.24,.16),deckm[i%4])
 for y in [-2.3,-.95]:
  rod('Stair_handrail',(-4.55,y,3.75),(-6.55,y,6.03),.065,wood[2])
  rod('Stair_stringer',(-4.6,y,2.83),(-6.7,y,5.13),.075,wood[1])
 rod('Wheel_pedestal',(-9.2,0,roof_z+.05),(-9.2,0,6.2),.12,wood[1])
 bpy.ops.mesh.primitive_torus_add(major_radius=.55,minor_radius=.055,major_segments=24,minor_segments=8,location=(-9.2,0,6.3),rotation=(0,math.pi/2,0));bpy.context.object.name='Helm_wheel';bpy.context.object.data.materials.append(wood[2])
 for i in range(8):
  a=i*math.tau/8;rod('Wheel_spoke',(-9.2,0,6.3),(-9.2,.68*math.cos(a),6.3+.68*math.sin(a)),.035,wood[2])
 for side in [-1,1]:lantern(-11.7,side*width(-11.7)*.91,6.1)
 # Every cabin-to-hull edge must belong to two faces of the same mesh.
 for name,a,b in join_checks:
  ob=scene.objects[name]
  assert sum(a in p.vertices and b in p.vertices for p in ob.data.polygons)==2,(name,a,b)
 print('Verified',len(join_checks),'shared cabin/hull seam edges; connected transom extension.')
 
 # A small wave carving gives the bow the same signature as the canvas.
 for side in [-1,1]:
  points=[]
  for k in range(48):
   a=-1.3+k/47*math.pi*1.9;r=.56*(1-.62*k/47)
   x=11.9+r*math.cos(a);z=2.65+r*math.sin(a)
   points.append((x,side*(width(x)+.06),z))
  line('Bow_wave_carving',points,.075,gold)
 # Solid timber beam ties the cabin entrance to the quarterdeck edge.
 rod('Cabin_entrance_beam',(-6.48,-width(-6.5)*.93,5.25),(-6.48,width(-6.5)*.93,5.25),.13,deckm[2],12)
 
 # The transom perimeter comes from the actual hull vertices, not a second height formula.
 ob=next(o for o in scene.objects if o.name=='Transom' or o.name.startswith('Transom.'))
 left=[v.co.copy() for v in port.data.vertices[:9]]+[upper_edges[-1][0].copy()]
 right=[v.co.copy() for v in star.data.vertices[:9]]+[upper_edges[1][0].copy()]
 vs=[];fs=[];mats=[]
 for a,b in zip(left,right):
  for j in range(33):vs.append(a.lerp(b,j/32))
 for i in range(len(left)-1):
  for j in range(32):fs.append((i*33+j,i*33+j+1,(i+1)*33+j+1,(i+1)*33+j));mats.append(5 if i>=6 else i%5)
 me=bpy.data.meshes.new('Transom_matched_to_hull');me.from_pydata(vs,[],fs);me.update()
 for m in wood+[teal]:me.materials.append(m)
 for p,mi in zip(me.polygons,mats):p.material_index=mi;p.use_smooth=True
 ob.data=me
 for i in range(len(left)):
  assert (me.vertices[i*33].co-left[i]).length<1e-6
  assert (me.vertices[i*33+32].co-right[i]).length<1e-6
 print('Verified all 20 transom corner vertices match hull and cabin boundaries exactly.')
 # Remove the old straight rear trim; rebuild only the continuous structural bands.
 for detail in list(scene.objects):
  if detail.name.startswith(('Transom_strake','Quarterdeck_rear_fascia','Quarterdeck_rear_rail')):bpy.data.objects.remove(detail,do_unlink=True)
 for j in [2,3,4,5,6,8]:
  pts=[]
  for k in range(33):
   p=left[j].lerp(right[j],k/32);p.x-=.018;pts.append(p)
  line('Transom_continuous_strake',pts,.063 if j==6 else (.09 if j in [5,8] else .026),coral if j==6 else (deckm[2] if j==8 else wood[1]))
 for dz,label in [(.035,'fascia'),(.82,'rail')]:
  pts=[]
  for k in range(33):
   p=left[-1].lerp(right[-1],k/32);p.z+=dz;pts.append(p)
  line('Quarterdeck_rear_'+label,pts,.13 if label=='fascia' else .10,deckm[2])
 # Two fitted corner timbers follow the exact rear hull/cabin arrises.
 # Their cross sections overlap the shell, covering the join without floating clear of it.
 for side,edge in [(-1,left),(1,right)]:
  vs=[];fs=[]
  for i in range(2,len(edge)):
   p=edge[i]
   for dx,dy in [(-.16,-.15),(.13,-.15),(.13,.15),(-.16,.15)]:
    vs.append((p.x+dx,p.y+dy,p.z))
  count=len(vs)//4
  for i in range(count-1):
   for j in range(4):fs.append((i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j))
  fs.extend([(3,2,1,0),tuple((count-1)*4+j for j in range(4))])
  beam=mesh('Stern_corner_timber_Port' if side<0 else 'Stern_corner_timber_Starboard',vs,fs,wood[2])
  bevel=beam.modifiers.new('Soft timber edges','BEVEL');bevel.width=.025;bevel.segments=3
  beam.modifiers.new('Timber corner normals','WEIGHTED_NORMAL')
 # Below-deck battery: cut real openings through the side shell and add recessed reveals.
 for side in [-1,1]:
  hull=port if side<0 else star
  bpy.context.view_layer.objects.active=hull
  sol=hull.modifiers.new('Timber hull thickness','SOLIDIFY');sol.thickness=.10
  bpy.ops.object.modifier_apply(modifier=sol.name)
  for station,x in enumerate([-1.7,2.8,7.3]):
   y=side*width(x)*1.012;z=1.57+(top(x)-2.6)*.72
   cutter=cube('Temporary_gunport_cut',(x,y,z),(1.26,2.2,.92),shadow,0)
   bpy.context.view_layer.objects.active=hull
   mod=hull.modifiers.new('True gunport opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
   bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
   assembly=bpy.data.objects.new(('Port' if side<0 else 'Starboard')+'_LowerGun_%02d'%(station+1),None);scene.collection.objects.link(assembly)
   before=set(scene.objects)
   cube('LowerGun_dark_interior',(x,y-side*.56,z),(1.22,.08,.89),shadow,.02)
   for dx in [-.70,.70]:cube('LowerGun_jamb',(x+dx,y,z),(.13,.22,1.09),coral,.025)
   for dz in [-.52,.52]:cube('LowerGun_sill',(x,y,z+dz),(1.52,.22,.13),coral,.025)
   character_barrel('LowerGun',(x,y-side*.61,z-.025),side,length=1.62,scale=.82)
   lid=cube('LowerGun_open_lid',(x,y+side*.44,z+.82),(1.25,.87,.10),teal,.025);lid.rotation_euler.x=side*.30
   for ob in set(scene.objects)-before:ob.parent=assembly
 
 # Two three-gun working rows. Each gun remains an individual assembly.
 # The middle passage and aft stair landing stay clear; broadside barrels clear the cap rail.
 for side in [-1,1]:
  for station,x in enumerate([-1.7,2.8,7.3]):
   y=side*(width(x)-.87);z=top(x)+.06
   before=set(scene.objects)
   cube('DeckGun_carriage_bed',(x,y,z+.36),(1.15,1.20,.23),wood[0],.085)
   for dx in [-.48,.48]:
    cheek=cube('DeckGun_carriage_cheek',(x+dx,y,z+.725),(.24,1.12,.77),coral,.11)
    # Large brass axle pins and stout iron-rimmed wooden wheels.
    outer=1 if dx>0 else -1
    rod('DeckGun_trunnion',(x+dx,y,z+1.12),(x+dx+outer*.19,y,z+1.12),.135,dark,16)
    rod('DeckGun_cheek_pin',(x+dx+outer*.13,y,z+.87),(x+dx+outer*.16,y,z+.87),.08,gold,8)
    for dy in [-.40,.40]:
     rod('DeckGun_truck_tire',(x+dx-.145,y+dy,z+.30),(x+dx+.145,y+dy,z+.30),.29,dark,16)
     rod('DeckGun_truck_wood',(x+dx-.15,y+dy,z+.30),(x+dx+.15,y+dy,z+.30),.24,wood[2],16)
     rod('DeckGun_truck_hub',(x+dx+outer*.15,y+dy,z+.30),(x+dx+outer*.19,y+dy,z+.30),.105,gold,10)
   for dy in [-.40,.40]:rod('DeckGun_axle',(x-.66,y+dy,z+.30),(x+.66,y+dy,z+.30),.075,dark,12)
   character_barrel('DeckGun',(x,y-side*.55,z+1.10),side)
   # Breeching ropes stay beside the carriage, rather than across the walkway.
   for dx in [-.60,.60]:
    line('DeckGun_breeching',[(x+dx,side*(width(x)-.12),z+.30),(x+dx*1.05,y-side*.54,z+.13),(x+dx*.4,y-side*.63,z+.75)],.028,rope)
   assembly=bpy.data.objects.new(('Port' if side<0 else 'Starboard')+'_DeckGun_%02d'%(station+1),None);scene.collection.objects.link(assembly)
   for ob in set(scene.objects)-before:
    if ob!=assembly:ob.parent=assembly
 # Clip EVERY longitudinal trim curve around each opening, including the coral stripe.
 # Exact segment/rectangle subdivision keeps trim ends seated under the jambs.
 rectangles=[(x-.77,x+.77,1.57+(top(x)-2.6)*.72-.58,1.57+(top(x)-2.6)*.72+.58) for x in [-1.7,2.8,7.3]]
 def inside(p):return any(a<p.x<b and c<p.z<d for a,b,c,d in rectangles)
 clipped=0
 for ob in list(scene.objects):
  if ob.type!='CURVE' or not ob.name.startswith(('Hull_strake','Coral_sheer_stripe')):continue
  runs=[];run=[]
  for spline in ob.data.splines:
   pts=[Vector(p.co[:3]) for p in spline.points]
   for p,q in zip(pts,pts[1:]):
    ts=[0.,1.];delta=q-p
    for a,b,c,d in rectangles:
     for axis,v in [(0,a),(0,b),(2,c),(2,d)]:
      if abs(delta[axis])>1e-9:
       t=(v-p[axis])/delta[axis]
       if 0<t<1:ts.append(t)
    ts=sorted(set(ts))
    for t,u in zip(ts,ts[1:]):
     if inside(p.lerp(q,(t+u)/2)):
      if len(run)>1:runs.append(run)
      run=[];clipped+=1
     else:
      start=p.lerp(q,t);end=p.lerp(q,u)
      if not run:run=[start]
      run.append(end)
   if len(run)>1:runs.append(run)
   run=[]
  ob.data.splines.clear()
  for run in runs:
   sp=ob.data.splines.new('POLY');sp.points.add(len(run)-1)
   for point,co in zip(sp.points,run):point.co=(*co,1)
   for p,q in zip(run,run[1:]):assert not inside((p+q)*.5),'Trim still crosses port'
 print('Gunport trim check passed; removed',clipped,'intersecting trim segments.')
 # Smooth the hull across its dense longitudinal station grid; seams keep the crafted timber read.
 for ob in [port,star]:
  for poly in ob.data.polygons:poly.use_smooth=True
 
 # Selective wear: subtle plank groups, exposed end grain and walked stair noses.
 endgrain=mat('Exposed_endgrain',(.14,.047,.012),rough=.9)
 worn=mat('Worn_timber_edges',(.53,.285,.085),rough=.82)
 for ob in [port,star]:
  for poly in ob.data.polygons:
   if poly.material_index<5:
    poly.material_index=max(0,min(4,poly.material_index+([-1,0,0,1,0][(poly.index//80)%5])))
 for ob in list(scene.objects):
  if ob.type=='MESH' and ob.name.startswith(('Rail_post','Quarterdeck_post')):
   idx=len(ob.data.materials);ob.data.materials.append(endgrain)
   for poly in ob.data.polygons:
    if poly.normal.z>.9:poly.material_index=idx
 for i in range(8):
  x=-4.6-i*.27;z=2.99+i*.31+.084
  line('Stair_tread_wear',[(x+.14,-2.05,z),(x+.15,-1.72,z+.003),(x+.14,-1.32,z)],.016,worn)
 for k in range(24):
  x=-3.8+random.random()*10.5;y=-.9+random.random()*1.8
  length=.18+random.random()*.42
  line('Deck_walk_scuff',[(x,y,top(x)+.035),(x+length,y+.012,top(x+length)+.035)],.005,worn)
 # A small timber nameboard sits below the stern windows, with matching wave carvings.
 board_vs=[];board_fs=[]
 for i in range(33):
  y=-2.3+4.6*i/32
  board_vs.extend([(-12.175,y,2.98),(-12.175,y,3.46),(-12.035,y,3.46),(-12.035,y,2.98)])
 for i in range(32):
  for k in range(4):board_fs.append((4*i+k,4*i+(k+1)%4,4*(i+1)+(k+1)%4,4*(i+1)+k))
 board_fs.extend([(3,2,1,0),(128,129,130,131)])
 mesh('Stern_nameboard',board_vs,board_fs,wood[0])
 for z in [3.005,3.435]:line('Nameboard_border',[(-12.187,-2.15+4.3*i/32,z) for i in range(33)],.026,gold)
 textcurve=bpy.data.curves.new('Stern_name_letters','FONT');textcurve.body='SEASICK';textcurve.size=.31;textcurve.extrude=.003
 label=bpy.data.objects.new('Stern_name_letters',textcurve);scene.collection.objects.link(label);textcurve.materials.append(gold)
 bpy.context.view_layer.update()
 label.location=(-12.19,label.dimensions.x/2,3.10);label.rotation_euler=(math.pi/2,0,-math.pi/2)
 bpy.ops.object.select_all(action='DESELECT');label.select_set(True);bpy.context.view_layer.objects.active=label
 bpy.ops.object.convert(target='MESH')
 for side in [-1,1]:
  pts=[]
  for k in range(40):
   a=-.4+k/39*math.pi*1.9;r=.145*(1-.65*k/39)
   pts.append((-12.19,side*(1.65+r*math.cos(a)),3.23+r*math.sin(a)))
  line('Stern_wave_carving',pts,.022,gold)
 # Deform construction together so rails, frames and deck remain seated.
 # Artistic shaping only; hydrostatics intentionally remain outside this study.
 def sculpt(p):
  x,y,z=p
  bow=max(0,min(1,(x-5)/8));stern=max(0,min(1,(-x-5)/7))
  surface=max(0,min(1,(z+2.5)/5.8))
  nx=x+bow**2*(1.75*surface+.30*math.sin(math.pi*surface)-.90*(1-surface))-.48*stern**2*surface
  dz=(1.28*bow**3+.62*stern**2)*max(0,min(1,(z+1)/3.6))
  # Tumblehome and raked cabin: broad seated base, quieter upper silhouette.
  cabin=max(0,min(1,(-x-6.4)/1.2))*max(0,min(1,(z-3.3)/2.2))
  ny=y*(1-.095*cabin)
  # A shallow transverse crown and flowing stern lift soften the broad decks.
  dz+=.12*math.exp(-(x/10)**2)*max(0,1-(y/4.4)**2)*max(0,min(1,(z+1)/3.6))
  nx-=.30*cabin
  # Round the transom across its width; all windows and timbers follow the same surface.
  nx-=.52*math.exp(-((x+12)/1.55)**2)*max(0,1-(y/3.5)**2)
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
 
 # Seat jib hanks on the final straight forestay after hull shaping.
 stay_top=sculpt(Vector((5,0,17.8)));stay_bottom=sculpt(Vector((17.1,0,5.05)))
 for ob in scene.objects:
  if not ob.name.startswith('Jib_stay_hank'):continue
  sp=ob.data.splines[0];z=sp.points[0].co.z
  target=stay_top.lerp(stay_bottom,(stay_top.z-z)/(stay_top.z-stay_bottom.z))
  for idx,side in [(1,-1),(2,1)]:sp.points[idx].co=(*(target+Vector((0,side*.045,0))),1)
 # Static longitudinal clearance check includes applied canvas thickness.
 jib_ob=next(o for o in scene.objects if o.name.startswith('Triangular_jib'))
 square=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(('Sail_course','Sail_topsail'))]
 jib_min=min((jib_ob.matrix_world@v.co).x for v in jib_ob.data.vertices)
 square_max=max((o.matrix_world@v.co).x for o in square for v in o.data.vertices)
 assert jib_min-square_max>.5, 'Insufficient sail clearance'
 print('Jib longitudinal clearance:',jib_min-square_max)
 # Quiet presentation water isolates the ship, not a fabricated game screenshot.
 cube('Presentation_water',(0,0,-.5),(2000,2000,.1),sea,0)
 world=bpy.data.worlds.new('BrigStudy_World');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.36,.50,.66,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 ld=bpy.data.lights.new('BrigStudy_Sun','AREA');lo=bpy.data.objects.new('BrigStudy_Sun',ld);scene.collection.objects.link(lo);lo.location=(8,-20,35);ld.energy=10000;ld.shape='DISK';ld.size=12;lo.rotation_euler=(Vector((0,0,5))-lo.location).to_track_quat('-Z','Y').to_euler()
 ld=bpy.data.lights.new('BrigStudy_Fill','AREA');lo=bpy.data.objects.new('BrigStudy_Fill',ld);scene.collection.objects.link(lo);lo.location=(-20,10,19);ld.energy=3500;ld.size=15;lo.rotation_euler=(Vector((0,0,6))-lo.location).to_track_quat('-Z','Y').to_euler()
 cd=bpy.data.cameras.new('BrigStudy_Camera');camera=bpy.data.objects.new('BrigStudy_Camera',cd);scene.collection.objects.link(camera);scene.camera=camera;cd.type='ORTHO';cd.ortho_scale=38
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
 scene.render.resolution_x=1280;scene.render.resolution_y=960;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 scene.view_settings.view_transform='AgX'
 views=[('cannon-detail',(7,-14,9),(2.8,-3.8,4.15),6.3),('front',(38,-30,23),(2,0,8),38),('side',(0,-50,21),(2,0,8),38)]
 for label,position,target,scale in views:
  cd.ortho_scale=scale;camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/('brig-'+label+'.png'))
  bpy.ops.render.render(write_still=True)
 bpy.data.libraries.write(str(ROOT/'tools/blender/source/adventure-brig-design-v12.blend'),{scene})
 print('Standalone brig complete:',len(scene.objects),'objects. Three review renders. No Unity exports.')
finally:
 bpy.context.window.scene=previous
