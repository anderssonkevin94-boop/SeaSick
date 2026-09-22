"""20-stage art fleet, separate from gameplay balance. Run in isolated Blender."""
import bpy, math, random, json, sys
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick');OUT=ROOT/'docs/art-direction/fleet-v1';MODELS=ROOT/'tools/blender/source/fleet-v1'
OUT.mkdir(exist_ok=True,parents=True);MODELS.mkdir(exist_ok=True,parents=True)
# length, beam, freeboard, draft, gun rows (lower, upper/weather), mast sail tiers
rows=[
('Fishing skiff',9,2.9,.80,.55,[],[1],'An open working boat: thwarts, fishing basket, plain canvas and a tiller.'),
('Voyager skiff',11.5,2.9,.90,.60,[],[1],'A stronger stem, extended rowing benches and a proper cargo rack.'),
('Painted skiff',11.5,3.7,1.0,.65,[],[1],'A teal sheer stripe, shaped bow cap and carefully finished gunwales.'),
('Decked launch',11.5,3.7,1.25,.80,[],[1],'First continuous deck, framed hatch and a raised steering platform.'),
('Coastal launch',15,3.7,1.30,.85,[],[1],'A bowsprit, small jib and lantern mark the first coastal voyager.'),
('Broad voyager',15,4.8,1.45,1.0,[],[1],'A canvas stern shelter, crafted rails and organized deck stowage.'),
('Coastal sloop',15,4.8,1.55,1.1,[],[2],'First enclosed cabin, arched amber windows and a wheel.'),
('Long sloop',18,4.8,1.65,1.2,[],[1,2],'A second mast, proper quarterdeck and a longer, cleaner sail silhouette.'),
('Merchant sloop',18,6.1,1.80,1.3,[],[2,2],'A fuller cabin, twin stern lanterns and the first carved wave at the bow.'),
('Armed escort',18,6.1,1.85,1.35,[0,3],[2,2],'Six bright red gun carriages give the merchant hull an escort identity.'),
('Long escort',22,6.1,2.00,1.5,[0,4],[2,2],'Eight guns, a larger stair landing and decorated quarterdeck rails.'),
('Guild escort',22,7.4,2.15,1.65,[0,5],[2,2],'Ten guns, an inset stern crest and brass window surrounds.'),
('Adventure brig',26,7.8,2.65,2.5,[3,3],[2,2],'The approved V14 brig: carved timber, fitted cabin and twelve cartoon cannons.'),
('Long brig',29,7.8,2.90,2.5,[4,3],[2,2],'Fourteen guns, a projecting stern balcony and forked command pennant.'),
('Guild brig',29,9.0,3.10,2.6,[4,4],[2,2],'Sixteen guns, gilt bulwark piping and brass-capped balcony balusters.'),
('Compact two-decker',29,9.0,4.65,3.0,[4,4],[2,2],'Two enclosed gun decks, a three-window stern gallery and open working deck.'),
('Ocean two-decker',32,9.0,4.80,3.1,[5,4],[2,3],'Eighteen guns, an extra topsail tier and a carved sea-bird figurehead.'),
('Grand voyager',32,9.5,4.95,3.2,[5,5],[3,3],'Twenty guns, projecting side galleries and curved carved roof brows.'),
('Admiral voyager',34,9.5,5.10,3.3,[6,5],[3,3],'Twenty-two guns, three stern lanterns and a richer blue-green hull with gold inlay.'),
('Sea Crown',34,9.5,5.20,3.3,[6,6],[3,3],'Twenty-four guns: crowned stern gallery, paired command pennants and a gilded wave figurehead.')]
manifest=[]
for i,(name,L,B,F,D,guns,tiers,feature) in enumerate(rows):
 manifest.append(dict(stage=i+1,node=i,name=name,length=L,beam=B,freeboard=F,draft=D,gun_rows=guns,cannons=sum(guns)*2,tiers=tiers,masts=len(tiers),feature=feature,image=f'{i+1:02d}.png',model=f'{i+1:02d}.blend'))
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
scene=None

def mat(name,col,metal=0,rough=.7):
 m=bpy.data.materials.new(name);m.diffuse_color=(*col,1);m.use_nodes=True
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*col,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
 return m

def mesh(name,vs,fs,ma,smooth=False):
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],fs);me.update();ob=bpy.data.objects.new(name,me);scene.collection.objects.link(ob)
 for m in ma if isinstance(ma,list) else [ma]:me.materials.append(m)
 for p in me.polygons:p.use_smooth=smooth
 return ob

def cube(name,loc,size,ma,bevel=.04):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(ma)
 if bevel:b=o.modifiers.new('Crafted corners','BEVEL');b.width=bevel;b.segments=3;o.modifiers.new('Corner normals','WEIGHTED_NORMAL')
 return o

def rod(name,a,b,r,ma,verts=12,r2=None):
 d=Vector(b)-Vector(a);bpy.ops.mesh.primitive_cone_add(vertices=verts,radius1=r,radius2=r if r2 is None else r2,depth=d.length,location=(Vector(a)+Vector(b))/2)
 o=bpy.context.object;o.name=name;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();o.data.materials.append(ma);return o

def line(name,points,r,ma):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=r;c.bevel_resolution=2
 sp=c.splines.new('POLY');sp.points.add(len(points)-1)
 for p,co in zip(sp.points,points):p.co=(*co,1)
 o=bpy.data.objects.new(name,c);scene.collection.objects.link(o);c.materials.append(ma);return o

def orb(name,co,scale,ma):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,location=co);ob=bpy.context.object;ob.name=name;ob.scale=scale;ob.data.materials.append(ma)
 for p in ob.data.polygons:p.use_smooth=True
 return ob

def cannon(name,origin,side,scale=1):
 origin=Vector(origin);axis=Vector((0,side,.11)).normalized();right=Vector((1,0,0));up=right.cross(axis)
 profile=[(-.16,0),(-.14,.12),(-.04,.13),(0,.09),(.06,.25),(.2,.36),(.5,.38),(.7,.29),(1.15,.24),(1.26,.33),(1.40,.37),(1.53,.35),(1.53,.22),(1.18,.2),(1.15,0)]
 vs=[];fs=[];n=20
 for t,r in profile:
  for k in range(n):vs.append(origin+axis*(t*scale)+scale*r*(right*math.cos(math.tau*k/n)+up*math.sin(math.tau*k/n)))
 for i in range(len(profile)-1):
  for k in range(n):fs.append(tuple(reversed((i*n+k,i*n+(k+1)%n,(i+1)*n+(k+1)%n,(i+1)*n+k))))
 ob=mesh(name,vs,fs,[iron,gold,black],True)
 for p in ob.data.polygons:p.material_index=2 if p.index//n>=12 else (1 if 9<=p.index//n<=11 or p.index//n==1 else 0)
 return ob

def carriage(x,y,z,side,scale=1):
 cube('Red gun carriage',(x,y,z+.39),(.98,1.0,.20),wood[0],.06)
 for dx in [-.4,.4]:
  cube('Carriage cheek',(x+dx,y,z+.65),(.20,.94,.66),red,.10)
  for dy in [-.34,.34]:
   rod('Iron wheel rim',(x+dx-.12,y+dy,z+.27),(x+dx+.12,y+dy,z+.27),.27,iron)
   rod('Wooden wheel',(x+dx-.125,y+dy,z+.27),(x+dx+.125,y+dy,z+.27),.22,wood[2])
   sg=1 if dx>0 else -1;rod('Brass axle cap',(x+dx+sg*.125,y+dy,z+.27),(x+dx+sg*.15,y+dy,z+.27),.09,gold)
 cannon('Deck cannon',(x,y-side*.4,z+.99),side,scale)

def arched_window(name,centre,horizontal,normal,w,h,frame):
 c=Vector(centre);u=Vector(horizontal);v=Vector((0,0,1));n=Vector(normal)
 pts=[(-w/2,0),(w/2,0),(w/2,h-w/2)]+[(math.cos(a)*w/2,h-w/2+math.sin(a)*w/2) for a in [math.pi*k/12 for k in range(1,13)]]
 coords=[c+u*a+v*b for a,b in pts];mesh(name+' glass',coords,[tuple(range(len(coords)))],glass)
 line(name+' frame',coords+[coords[0]],.075,frame)
 rod(name+' mullion',c+n*.04,c+v*(h-.04)+n*.04,.025,gold)
 rod(name+' cross',c-u*w/2+v*h*.48+n*.04,c+u*w/2+v*h*.48+n*.04,.023,gold)

def lantern(co,size=1):
 x,y,z=co;rod('Lantern stem',(x,y,z),(x,y,z+.27*size),.045*size,gold)
 cube('Warm lantern',(x,y,z+.52*size),(.28*size,.28*size,.5*size),glass,.015)
 for dx in [-.17,.17]:
  for dy in [-.17,.17]:rod('Lantern frame',(x+dx*size,y+dy*size,z+.25*size),(x+dx*size,y+dy*size,z+.79*size),.028*size,gold)
 rod('Lantern roof',(x,y,z+.78*size),(x,y,z+1.01*size),.27*size,gold,4,r2=.025)

def wave(centre,u,v,radius,ma):
 c=Vector(centre);u=Vector(u);v=Vector(v)
 pts=[]
 for k in range(50):
  t=k/49;a=-.5+t*math.pi*2;r=radius*(1-.72*t);pts.append(c+r*(u*math.cos(a)+v*math.sin(a)))
 line('Carved wave',pts,.04 if radius<.5 else .055,ma)

def square_sail(mx,top,span,height,i):
 angle=math.radians(-18);c=math.cos(angle);s=math.sin(angle)
 def pos(u,v):
  x=.28+min(1.65,height*.45)*math.sin(math.pi*v*.86)*math.cos(u*math.pi/2)
  y=u*span/2*(1+.05*v);z=top-height*v+.40*(1-u*u)*v
  return Vector((mx+x*c-y*s,x*s+y*c,z))
 nu=20;nv=14;vs=[pos(-1+2*k/nu,j/nv) for j in range(nv+1) for k in range(nu+1)]
 fs=[(j*(nu+1)+k,j*(nu+1)+k+1,(j+1)*(nu+1)+k+1,(j+1)*(nu+1)+k) for j in range(nv) for k in range(nu)]
 ob=mesh('Square sail',vs,fs,cloth,True)
 for p in ob.data.polygons:p.material_index=(p.index%nu//5)%len(cloth)
 mod=ob.modifiers.new('Canvas thickness','SOLIDIFY');mod.thickness=.014
 for u in [-1,-.5,0,.5,1]:line('Canvas seam',[pos(u,j/nv) for j in range(nv+1)],.008,hem)
 line('Canvas foot',[pos(-1+2*k/nu,1) for k in range(nu+1)],.025,hem)
 rod('Yard',pos(-1.12,0),pos(1.12,0),max(.055,span*.009),wood[2],12,r2=.055)
 if i>=17:
  # A restrained gold-bound central cloth stripe is earned late in the fleet.
  for u in [-.10,.10]:line('Flagship sail piping',[pos(u,j/nv) for j in range(nv+1)],.017,hem)

def build(i):
 global scene,wood,deck,teal,red,gold,iron,black,glass,rope,cloth,hem
 spec=manifest[i];L=spec['length'];B=spec['beam'];F=spec['freeboard'];D=spec['draft'];random.seed(90+i)
 scene=bpy.data.scenes.new(f"Fleet_{i+1:02d}_{spec['name']}");bpy.context.window.scene=scene
 wood=[mat(f'Honey timber {k}',(.22+.036*k,.070+.018*k,.017+.007*k)) for k in range(5)]
 deck=[mat(f'Deck {k}',(.49+.02*k,.255+.015*k,.075+.009*k)) for k in range(4)]
 teal=mat('Ocean teal',(.007,.115 if i<18 else .075,.145 if i<18 else .13),rough=.43)
 red=mat('Vermilion',(.66,.035,.020),rough=.48);gold=mat('Warm brass',(.70,.37,.060),.65,.3)
 iron=mat('Blue black iron',(.019,.033,.042),.55,.4);black=mat('Recess shadow',(.012,.020,.023))
 glass=mat('Amber glass',(.92,.51,.105),.12,.3);rope=mat('Hemp',(.22,.13,.055),rough=.95)
 cloth=[mat(f'Canvas {k}',(.91*f,.77*f,.51*f),rough=.95) for k,f in enumerate([.98,1,1.02,.99])];hem=mat('Canvas binding',(.55,.38,.17),rough=.9)
 # Preserve the approved anchor ship exactly; surrounding stages inherit its vocabulary.
 if i==12:
  with bpy.data.libraries.load(str(ROOT/'tools/blender/source/adventure-brig-design-v14.blend'),link=False) as (src,dst):dst.scenes=src.scenes
  source=next(s for s in dst.scenes if s.name.startswith('Adventure_Brig_Design_Study_V14'));bpy.context.window.scene=source;bpy.context.view_layer.update();hh=[o for o in source.objects if o.type=='MESH' and o.name.startswith(('Hull_Port','Hull_Starboard'))]
  pts=[o.matrix_world@v.co for o in hh for v in o.data.vertices];lo=min(v.x for v in pts);hi=max(v.x for v in pts);bw=max(v.y for v in pts)-min(v.y for v in pts)
  T=Matrix.Diagonal((26/(hi-lo),7.8/bw,26/(hi-lo),1))@Matrix.Translation(Vector((-(lo+hi)/2,0,0)))
  transforms={o.name:o.matrix_world.copy() for o in source.objects}
  bpy.context.window.scene=scene
  for o in source.objects:
   if o.type not in {'MESH','CURVE'} or o.name.startswith('Presentation_water'):continue
   ob=o.copy();ob.parent=None;scene.collection.objects.link(ob);ob.matrix_world=T@transforms[o.name]
  finish(i,spec);return
 def width(x):return B/2*max(.0001,1-((x+L*.08)/(L*.58))**2)**.62
 xs=sorted(set([-L/2+L*k/48 for k in range(49)]+[-L*.08]))
 wmax=max(width(x) for x in xs)
 def w(x):return width(x)/wmax*B/2
 def sheer(x):return (.16 if i<3 else .27)*(abs(x)/(L/2))**2+(.12 if i<6 else .32)*max(0,-x/(L/2))**3
 def top(x):return F+sheer(x)
 factors=[.06,.37,.66,.86,.97,1,.98]
 def side_y(x,z):
  f=max(0,min(1,(z+D)/(top(x)+D)));r=f*6;a=min(5,int(r));t=r-a
  return w(x)*(factors[a]*(1-t)+factors[a+1]*t)
 # Lofted timber shell, with a broad continuous painted topside band.
 for side in [-1,1]:
  vs=[(x,side*w(x)*f,-D+(top(x)+D)*k/6) for x in xs for k,f in enumerate(factors)]
  fs=[]
  for j in range(len(xs)-1):
   for k in range(6):fs.append((j*7+k,(j+1)*7+k,(j+1)*7+k+1,j*7+k+1))
  if side>0:fs=[tuple(reversed(f)) for f in fs]
  ob=mesh('Hull shell',vs,fs,wood+[teal],True)
  for p in ob.data.polygons:p.material_index=5 if (i>=2 and p.index%6>= (5 if i<6 else 4)) else min(4,p.index%6)
  mod=ob.modifiers.new('Timber thickness','SOLIDIFY');mod.thickness=.10
  for k in range(2,7):
   line('Long hull strake',[(x,side*w(x)*factors[k],-D+(top(x)+D)*k/6) for x in xs],.026 if k<6 else .09,wood[1] if k<6 else deck[2])
 # Continuous stem closes the tapered bow.
 line('Crafted stem',[(L*.5,0,-D),(L*.5,0,F+.30)],.12 if i<6 else .19,wood[2])
 if i>=9:
  for side in [-1,1]:
   for zz in ([F*.30] if i<15 else [1.02,F-1.82]):
    line('Vermilion battery wale',[(x,side*(side_y(x,zz)+.075),zz+sheer(x)*.35) for x in xs],.07,red)
 if i>=14:
  for side in [-1,1]:
   for zz in [F-.28,F-.42]:line('Gilded upper molding',[(x,side*(side_y(x,zz)+.055),zz) for x in xs],.037,gold)
 # Transom and keel.
 verts=[]
 for k,f in enumerate(factors):
  z=-D+(top(-L/2)+D)*k/6;verts.extend([(-L/2,-w(-L/2)*f,z),(-L/2,w(-L/2)*f,z)])
 ob=mesh('Transom',verts,[(k*2,k*2+1,k*2+3,k*2+2) for k in range(6)],wood+[teal])
 for p in ob.data.polygons:p.material_index=5 if i>=2 and p.index>=4 else p.index%5
 line('Keel backbone',[(x,0,-D+.04) for x in xs],.11,wood[0])
 cube('Rudder',(-L/2-.18,0,-D*.3),(.28,.18,D+F*.8),wood[0],.05)
 if i<3:
  # Open hull: visible inner bilge, cross-thwarts and working oars.
  for side in [-1,1]:
   iv=[(x,side*(w(x)*f-.10),-D+(top(x)+D)*k/6+.06) for x in xs[1:-1] for k,f in enumerate(factors[2:],2)]
   mesh('Inner planking',iv,[(j*5+k,j*5+k+1,(j+1)*5+k+1,(j+1)*5+k) for j in range(len(xs)-3) for k in range(4)],wood[2],True)
  for x in [-L*.28,0,L*.26]:cube('Rowing thwart',(x,0,F*.48),(.48,w(x)*1.72,.16),deck[2],.045)
  for side in [-1,1]:
   rod('Oar shaft',(-L*.18,side*B*.28,F*.58),(L*.19,side*B*.65,F*.55),.055,wood[2])
   ob=cube('Oar blade',(L*.19,side*B*.65,F*.55),(.65,.23,.08),wood[3],.04);ob.rotation_euler.z=side*.5
  rod('Tiller',(-L/2,0,F*.5),(-L*.30,0,F*.56),.07,wood[1])
  basket=Vector((-L*.22,B*.15,F*.28));rr=.29
  for k in range(7):line('Basket weave',[basket+Vector((rr*math.cos(a),rr*math.sin(a),k*.065)) for a in [math.tau*j/24 for j in range(25)]],.026,rope)
  for k in range(12):
   a=math.tau*k/12;rod('Basket stave',basket+Vector((rr*math.cos(a),rr*math.sin(a),0)),basket+Vector((rr*math.cos(a),rr*math.sin(a),.42)),.02,deck[2])
  if i>=1:
   for k in range(5):cube('Cargo rack',(-L*.36,(-.32+k*.16)*B*.65,F*.57),(L*.15,.14,.10),deck[k%4],.025)
   cube('Canvas cargo roll',(-L*.36,0,F*.77),(.72,B*.37,.32),cloth[0],.14)
   for yy in [-B*.13,B*.13]:line('Cargo binding',[(-L*.39,yy,F*.70),(-L*.39,yy,F*.95),(-L*.33,yy,F*.95),(-L*.33,yy,F*.70)],.03,rope)
 else:
  for b in range(20):
   vs=[]
   for x in xs:
    for t in [b/20,(b+1)/20-.002]:vs.append((x,w(x)*.97*(2*t-1),top(x)+.025))
   mesh('Deck planks',vs,[(j*2,j*2+1,j*2+3,j*2+2) for j in range(len(xs)-1)],deck[b%4])
  cube('Cargo hatch',(.02*L,0,F+.14),(L*.10,B*.26,.2),wood[0],.06)
  for k in range(7):rod('Hatch slat',(.02*L-L*.045+k*L*.015,-B*.12,F+.25),(.02*L-L*.045+k*L*.015,B*.12,F+.25),.035,iron)
 # Rails gain carved posts and gold capping as the hull matures.
 for side in [-1,1]:
  if i>=3:
   rxs=[x for x in xs if x>=(-L*.29 if i>=6 else -L*.47) and x<L*.48]
   for x in rxs[::4]:
    cube('Rail post',(x,side*w(x)*.95,top(x)+.34),(.13,.16,.72),wood[2])
    if i>=14:orb('Brass post cap',(x,side*w(x)*.95,top(x)+.73),(.10,.10,.07),gold)
   line('Cap rail',[(x,side*w(x)*.95,top(x)+.69) for x in rxs],.085,deck[2])
   if i>=14:line('Gilded sheer',[(x,side*(w(x)*.986+.025),top(x)-.13) for x in xs],.035,gold)
 # A modest raised stern becomes an inhabited quarterdeck, then a gallery.
 cabin_front=-L*.28;cabin_back=-L*.48;cabin_h=1.55 if i<10 else (1.8 if i<15 else 2.0);cabin_z=top(-L*.38)
 roof=cabin_z+cabin_h
 if i>=6:
  cx=(cabin_front+cabin_back)/2;half=w(cx)*.79
  cube('Stern cabin',(cx,0,cabin_z+cabin_h/2),(cabin_front-cabin_back,B*.67,cabin_h),teal,.12)
  cube('Quarterdeck',(cx,0,roof+.04),(cabin_front-cabin_back+.22,B*.73,.20),deck[1],.10)
  nwin=1 if i<8 else (2 if i<15 else 3)
  for side in [-1,1]:
   for k in range(nwin):
    x=cabin_back+.42+(cabin_front-cabin_back-.84)*(k+.5)/nwin
    arched_window('Cabin window',(x,side*(B*.335+.015),cabin_z+.30),(1,0,0),(0,side,0),min(.8,(cabin_front-cabin_back-.5)/nwin*.58),1.05,gold if i>=11 else wood[1])
   for x in [cabin_back+.08,cabin_front-.08]:cube('Cabin corner',(x,side*B*.344,cabin_z+cabin_h/2),(.15,.14,cabin_h),wood[1])
   rod('Quarterdeck rail',(cabin_back,side*B*.355,roof+.7),(cabin_front,side*B*.355,roof+.7),.09,deck[2])
   for k in range(5):cube('Quarterdeck baluster',(cabin_back+(cabin_front-cabin_back)*k/4,side*B*.355,roof+.36),(.11,.13,.7),gold if i>=14 and k%2 else wood[2])
  for k in range(3 if i>=15 else 2):
   y=(k-(1 if i>=15 else .5))*B*.18
   arched_window('Rear window',(cabin_back-.02,y,cabin_z+.30),(0,1,0),(-1,0,0),.75,1.05,gold if i>=11 else wood[1])
  if i>=11:
   for side in [-1,1]:
    for z in [cabin_z+.15,roof-.08]:rod('Cabin brass cornice',(cabin_back,side*B*.347,z),(cabin_front,side*B*.347,z),.055,gold)
    for k in range(4):
     x=cabin_back+(cabin_front-cabin_back)*k/3
     rod('Cabin crafted pilaster',(x,side*B*.347,cabin_z+.1),(x,side*B*.347,roof-.05),.065,gold if i>=17 else wood[2])
  # Stairs end on the quarterdeck, with enough width for a crew member.
  n=7
  for k in range(n):cube('Quarterdeck stair',(cabin_front+1.85-1.85*k/(n-1),-B*.20,F+.12+(roof-F)*k/(n-1)),(.38,.95,.16),deck[k%4])
  for y in [-B*.20-.52,-B*.20+.52]:rod('Stair rail',(cabin_front+2,y,F+1.0),(cabin_front,y,roof+.85),.045,wood[1])
  rod('Helm pedestal',(cx,0,roof+.13),(cx,0,roof+.85),.09,wood[0])
  centre=Vector((cx,0,roof+1));rr=.4
  line('Helm wheel',[centre+Vector((0,rr*math.cos(a),rr*math.sin(a))) for a in [math.tau*k/32 for k in range(33)]],.045,wood[2])
  for k in range(8):a=math.tau*k/8;rod('Helm spoke',centre,centre+Vector((0,.50*math.cos(a),.50*math.sin(a))),.025,wood[2])
  lantern((cabin_back+.2,-B*.34,roof+.7),.8)
  if i>=8:lantern((cabin_back+.2,B*.34,roof+.7),.8)
  if i>=18:lantern((cabin_back-.3,0,roof+.75),1.05)
 elif i>=3:
  cube('Steering platform',(-L*.36,0,F+.35),(L*.20,B*.66,.22),deck[2],.08)
  rod('Tiller',(-L*.48,0,F+.50),(-L*.30,0,F+.62),.07,wood[1])
  if i==5:
   for x in [-L*.46,-L*.26]:
    for side in [-1,1]:rod('Awning post',(x,side*B*.27,F+.46),(x,side*B*.27,F+2.05),.055,wood[2])
   mesh('Canvas shelter',[(-L*.46,-B*.3,F+2),(-L*.46,B*.3,F+2),(-L*.26,B*.3,F+2),(-L*.26,-B*.3,F+2),(-L*.36,0,F+2.35)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],cloth[0])
 # Earned stern balcony and gallery layers.
 if i>=13:
  bx=cabin_back-.40
  cube('Stern balcony deck',(bx,0,roof-.02),(.9,B*.74,.17),deck[2],.08)
  for side in [-1,1]:rod('Balcony side',(cabin_back,side*B*.36,roof+.66),(bx-.4,side*B*.36,roof+.66),.065,gold if i>=14 else wood[2])
  line('Balcony rear cap',[(bx-.42,-B*.36,roof+.68),(bx-.55,0,roof+.73),(bx-.42,B*.36,roof+.68)],.065,gold if i>=14 else wood[2])
  for k in range(11):
   y=-B*.34+B*.68*k/10;xx=bx-.42-.13*(1-(y/(B*.36))**2)
   rod('Balcony spindle',(xx,y,roof),(xx,y,roof+.65),.038,gold if i>=17 else wood[2])
 if i>=17:
  for side in [-1,1]:
   gx=(cabin_front+cabin_back)/2;gy=side*B*.39
   cube('Side gallery',(gx,gy,cabin_z+cabin_h*.55),(L*.10,.65,cabin_h*.66),teal,.10)
   cube('Gallery projecting sill',(gx,gy,cabin_z+.20),(L*.115,.85,.15),gold,.07)
   for k in [-1,0,1]:arched_window('Gallery window',(gx+k*.70,gy+side*.34,cabin_z+.45),(1,0,0),(0,side,0),.48,.88,gold)
   line('Gallery curved roof',[(gx-L*.058+L*.116*k/20,gy+side*.38,roof+.05+.20*math.sin(math.pi*k/20)) for k in range(21)],.09,gold)
 if i>=11:
  wave((cabin_back-.12,0,cabin_z+.08),(0,1,0),(0,0,1),.34,gold)
 if i==19:
  # Crown-like fan of crafted timbers, integrated above the stern gallery.
  for k in range(7):
   y=(k-3)*.27;z=roof+.18+.52*(1-abs(k-3)/4)
   rod('Stern crest ray',(cabin_back-.06,y,roof+.06),(cabin_back-.08,y,z),.065,gold)
  wave((cabin_back-.13,0,roof+.32),(0,1,0),(0,0,1),.33,gold)
 # Stowage and fittings stay human-sized, rather than enlarging with the hull.
 if i>=3:
  for x,y in [(L*.28,-B*.16),(L*.28,B*.16)]:
   z=top(x)+.04;rod('Cargo barrel',(x,y,z),(x,y,z+.8),.30,wood[2],14)
   for dz in [.15,.62]:rod('Barrel hoop',(x,y,z+dz),(x,y,z+dz+.065),.315,iron,14)
  rod('Capstan',(L*.23,0,F+.03),(L*.23,0,F+.7),.19,wood[1]);rod('Capstan bar',(L*.23,-.5,F+.6),(L*.23,.5,F+.6),.04,wood[2])
 if i>=4:lantern((L*.37,0,top(L*.37)+.06),.65)
 if i>=8:
  for side in [-1,1]:wave((L*.39,side*(side_y(L*.39,F*.83)+.04),F*.83),(1,0,0),(0,0,1),.36 if i<16 else .48,gold)
 if i>=16:
  # Swept sea-bird / gilded wave prow: a true silhouette change, not a decal.
  c=Vector((L*.51,0,top(L*.49)+.22))
  rod('Figurehead neck',c-Vector((.45,0,.08)),c+Vector((.22,0,.42)),.12,gold if i==19 else wood[3],12,r2=.07)
  orb('Figurehead head',c+Vector((.22,0,.43)),(.18,.12,.16),gold if i==19 else wood[3])
  mesh('Figurehead beak',[c+Vector((.28,-.07,.44)),c+Vector((.28,.07,.44)),c+Vector((.56,0,.4)),c+Vector((.28,0,.34))],[(0,1,2),(0,2,3),(1,3,2)],gold)
  for side in [-1,1]:line('Figurehead wing',[c+Vector((0,0,.2)),c+Vector((-.35,side*.35,.6)),c+Vector((-.7,side*.48,.48))],.09,gold if i==19 else wood[2])
 # Guns: weather-deck batteries stay visible until the true two-decker stage.
 for row,count in enumerate(spec['gun_rows']):
  if not count:continue
  positions=[-L*.19+L*.49*k/(count-1) for k in range(count)] if count>1 else [0]
  ondeck=(row==1 and i<15)
  for side in [-1,1]:
   for x in positions:
    if ondeck:
     y=side*(w(x)*.94-.65);carriage(x,y,top(x)+.03,side,.80 if i<11 else .92)
    else:
     z=F*.43 if i<15 else (1.45 if row==0 else F-1.38)
     y=side*side_y(x,z)
     pts=[(x+dx,side*(side_y(x+dx,z+dz)+.065),z+dz) for dx,dz in [(-.56,-.42),(.56,-.42),(.56,.42),(-.56,.42)]]
     mesh('Gunport recess',pts,[(0,1,2,3)],black);line('Red gunport frame',pts+[pts[0]],.07,red)
     cannon('Lower cannon',(x,y-side*.32,z),side,.82 if i<15 else .95)
 # Rig grows in architecture, not simply by scaling a single rig.
 tiers=spec['tiers'];peaks=[]
 for m,nt in enumerate(tiers):
  mx=L*.08 if len(tiers)==1 else (-L*.14 if m==0 else L*.22)
  total=5.2+L*.18+(nt-1)*1.3
  heights=[max(2.2,B*.40)*(1-.18*k) for k in range(nt)]
  bottom=F+2.0+(1.0 if i>=15 else 0);heads=[]
  for h in heights:heads.append(bottom+h);bottom+=h+.80
  peak=heads[-1]+.75;peaks.append((mx,peak))
  rod('Mast',(mx,0,F if i>=3 else F*.35),(mx,0,peak),.11+B*.01,wood[2],14,r2=.08)
  for k,(head,h) in enumerate(zip(heads,heights)):square_sail(mx,head,B*(1.10-.23*k),h,i)
  if i>=7:
   for side in [-1,1]:
    base=Vector((mx,side*B*.36,top(mx)+.04));tip=Vector((mx,side*.12,heads[0]+.3))
    for dx in [-.48,.48]:rod('Shroud',base+Vector((dx,0,0)),tip,.024,rope,6)
    for k in range(1,17):
     t=k/18;c=base.lerp(tip,t);rod('Ratline',c+Vector((-.48*(1-t),0,0)),c+Vector((.48*(1-t),0,0)),.014,rope,6)
  if i>=10:
   for z in [F+1,heads[0]+.3]:rod('Mast brass collar',(mx,0,z),(mx,0,z+.12),.13+B*.01,gold,14)
 if len(peaks)>1:rod('Mast stay',(*peaks[0][:1],0,peaks[0][1]-.2),(*peaks[1][:1],0,peaks[1][1]-.2),.026,rope,8)
 aft=Vector((peaks[0][0],0,peaks[0][1]-.25));rod('Aft stay',aft,(-L*.46,0,roof+.65 if i>=6 else F+.5),.025,rope,8)
 if i>=4:
  bowtip=Vector((L*.63,0,F+1.35));rod('Bowsprit',(L*.40,0,F+.15),bowtip,.13 if i<10 else .19,wood[2],14,r2=.07)
  head=Vector((peaks[-1][0],0,peaks[-1][1]-.25));rod('Forestay',head,bowtip,.028,rope,8)
  a=head.lerp(bowtip,.69);b=head.lerp(bowtip,.965);c=Vector((a.x+.20,0,b.z+.44));vs=[];fs=[];ids={};n=16
  for j in range(n+1):
   for k in range(n+1-j):
    u=j/n;v=k/n;t=1-u-v;p=a*t+b*u+c*v;p.y+=.30*27*u*v*t;ids[j,k]=len(vs);vs.append(p)
  for j in range(n):
   for k in range(n-j):
    fs.append((ids[j,k],ids[j+1,k],ids[j,k+1]))
    if j+k<n-1:fs.append((ids[j+1,k],ids[j+1,k+1],ids[j,k+1]))
  mesh('Small jib',vs,fs,cloth[0],True);line('Jib edge',[a,c,b],.02,hem)
  for side in [-1,1]:
   xx=L*.39;anchor=Vector((xx,side*w(xx)*.94,top(xx)+.72));rod('Jib cleat',anchor-Vector((.12,0,0)),anchor+Vector((.12,0,0)),.033,iron)
   line('Straight jib sheet',[c,anchor],.022,rope)
 if i>=2:
  x,z=peaks[0];size=.65 if i<6 else 1.0 if i<13 else 1.4
  verts=[(x,0,z),(x,0,z+size*.5),(x-size*2.2,.10,z+size*.35),(x-size*1.7,.12,z+size*.18),(x-size*2.2,.10,z)]
  mesh('Command pennant',verts,[(0,1,2,3,4)] if i>=13 else [(0,1,2,4)],red)
 if i==19:
  x,z=peaks[-1];mesh('Second command pennant',[(x,0,z),(x,0,z+.6),(x-2.2,.12,z+.45),(x-1.8,.14,z+.2),(x-2.2,.1,z)],[(0,1,2,3,4)],teal)
 # Human reference, in the same dimensions on every hull.
 if CREW:
  for x,y in [(L*.06,-B*.20)]+([(L*.24,-B*.2)] if i>=10 else []):
   loc=Vector((x,y,top(x)+.035 if i>=3 else F*.5+.1));ob=CREW.copy();ob.data=CREW.data.copy();ob.parent=None;ob.matrix_world=Matrix.Identity(4);scene.collection.objects.link(ob);ob.name='Crew 1.7m'
   for v,p in zip(ob.data.vertices,CREW_POINTS):v.co=(p-CREW_ORIGIN)*CREW_SCALE+loc
 finish(i,spec)

def finish(i,spec):
 # Identical light/camera direction. Each ship is framed independently for detail review.
 world=bpy.data.worlds.new('Fleet studio');world.use_nodes=True;bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.36,.50,.66,1);bg.inputs[1].default_value=.65;scene.world=world
 sea=mat('Studio blue sea',(.045,.27,.35),.10,.35);cube('Presentation water',(0,0,-.20),(2000,2000,.1),sea,0)
 for name,pos,power,size in [('Key',(8,-25,40),14000,15),('Fill',(-20,12,26),5500,18)]:
  ld=bpy.data.lights.new(name,'AREA');ob=bpy.data.objects.new(name,ld);scene.collection.objects.link(ob);ob.location=pos;ld.energy=power;ld.size=size;ob.rotation_euler=(Vector((0,0,4))-ob.location).to_track_quat('-Z','Y').to_euler()
 camd=bpy.data.cameras.new('Fleet camera');cam=bpy.data.objects.new('Fleet camera',camd);scene.collection.objects.link(cam);scene.camera=cam;camd.type='ORTHO'
 cam.location=(48,-64,37);cam.rotation_euler=(Vector((0,0,8))-cam.location).to_track_quat('-Z','Y').to_euler()
 bpy.context.view_layer.update();rot=cam.rotation_euler.to_matrix();inv=rot.transposed();points=[]
 for ob in scene.objects:
  if ob.type not in {'MESH','CURVE'} or ob.name.startswith('Presentation'):continue
  points.extend(inv@(ob.matrix_world@Vector(c)) for c in ob.bound_box)
 minx=min(p.x for p in points);maxx=max(p.x for p in points);miny=min(p.y for p in points);maxy=max(p.y for p in points)
 target=rot@Vector(((minx+maxx)/2,(miny+maxy)/2,0));cam.location=target+rot@Vector((0,0,100))
 scene.render.resolution_x=1440;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
 camd.ortho_scale=max(maxx-minx,(maxy-miny)*4/3)*1.16
 scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
 scene.view_settings.view_transform='AgX';scene.view_settings.exposure=.4;scene.render.image_settings.file_format='PNG'
 scene.render.filepath=str(OUT/spec['image']);bpy.ops.render.render(write_still=True)
 bpy.data.libraries.write(str(MODELS/spec['model']),{scene})
 counts={'objects':len(scene.objects),'render':spec['image'],'complete':True}
 (OUT/f'{i+1:02d}-validation.json').write_text(json.dumps({**spec,**counts},indent=2))
 print('FLEET_DONE',i+1,spec['name'],flush=True)
 # Do not retain twenty detailed ship scenes in memory during the batch.
 for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
 bpy.data.scenes.remove(scene)
 for datablocks in [bpy.data.meshes,bpy.data.curves,bpy.data.materials,bpy.data.lights,bpy.data.cameras,bpy.data.worlds]:
  for db in list(datablocks):
   if db.users==0:datablocks.remove(db)

# Load just the crew mesh, preserving its authored world transform for normalization.
with bpy.data.libraries.load(str(ROOT/'tools/blender/source/crew-weathered-v2.blend'),link=False) as (src,dst):dst.objects=[n for n in src.objects if n.startswith('Male_Coat')]
CREW=next((o for o in dst.objects if o and o.type=='MESH'),None)
CREW_POINTS=[CREW.matrix_world@v.co for v in CREW.data.vertices] if CREW else []
if CREW:
 CREW.use_fake_user=True
 CREW_ORIGIN=Vector(((min(v.x for v in CREW_POINTS)+max(v.x for v in CREW_POINTS))/2,(min(v.y for v in CREW_POINTS)+max(v.y for v in CREW_POINTS))/2,min(v.z for v in CREW_POINTS)))
 CREW_SCALE=1.7/(max(v.z for v in CREW_POINTS)-min(v.z for v in CREW_POINTS))
indices=[int(x) for x in sys.argv[sys.argv.index('--')+1:]] if '--' in sys.argv else list(range(20))
for i in indices:build(i)
