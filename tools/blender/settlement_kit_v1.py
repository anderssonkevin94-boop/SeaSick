"""Standalone settlement kit. Execute through Blender MCP; never touches Unity."""
import bpy, math, json, random
from pathlib import Path
from mathutils import Vector
R=Path('/Users/kevinandersson/Desktop/SeaSick'); OUT=R/'docs/art-direction/settlement-kit/v1'; EXP=R/'tools/blender/exports/settlement-kit-v1'
OUT.mkdir(parents=True,exist_ok=True);EXP.mkdir(parents=True,exist_ok=True)
previous=bpy.context.window.scene
scene=bpy.data.scenes.new('Settlement_Kit_V1');bpy.context.window.scene=scene
rng=random.Random(46); assets=[]; current=None; objects=[]; markers=[]
try:
 palette={'wood':(.32,.17,.07,1),'woodlight':(.49,.29,.13,1),'wooddark':(.16,.085,.04,1),'plaster':(.76,.65,.43,1),'roof':(.56,.19,.065,1),'rooflight':(.60,.225,.08,1),'roofdark':(.48,.16,.053,1),'teal':(.035,.21,.22,1),'coral':(.62,.12,.055,1),'stone':(.40,.43,.40,1),'stonesun':(.56,.54,.43,1),'iron':(.09,.13,.15,1),'sand':(.54,.43,.25,1),'soil':(.19,.105,.046,1),'leaf':(.29,.39,.085,1),'wheat':(.69,.49,.13,1),'canvas':(.81,.72,.49,1),'coal':(.045,.038,.03,1),'ember':(.92,.21,.012,1),'flame':(1,.58,.045,1)}
 mats={}
 for name,col in palette.items():
  m=bpy.data.materials.new('Settlement_'+name);m.diffuse_color=col;m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=col;p.inputs['Roughness'].default_value=.82
  if name in ['ember','flame']:p.inputs['Emission Color'].default_value=col;p.inputs['Emission Strength'].default_value=.5
  mats[name]=m
 def mesh(n,v,f,mat):
  me=bpy.data.meshes.new(n);me.from_pydata(v,[],f);me.update();o=bpy.data.objects.new(n,me);scene.collection.objects.link(o);me.materials.append(mats[mat]);o.parent=current;objects.append(o);return o
 def box(n,p,s,m):
  x,y,z=p;a,b,c=[t/2 for t in s];v=[(x+dx*a,y+dy*b,z+dz*c) for dz in [-1,1] for dy in [-1,1] for dx in [-1,1]]
  return mesh(n,v,[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)],m)
 def beam(n,a,b,r,m='wood',r2=None):
  a=Vector(a);b=Vector(b);d=(b-a).normalized();u=d.cross(Vector((0,0,1)))
  if u.length<.01:u=d.cross(Vector((0,1,0)))
  u.normalize();v=d.cross(u);r2=r if r2 is None else r2
  verts=[p+u*x*r+v*y*r2 for p in [a,b] for x,y in [(-1,-1),(1,-1),(1,1),(-1,1)]]
  return mesh(n,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],m)
 def cyl(n,p,r,h,m,N=10,top=None):
  x,y,z=p;rt=r if top is None else top;v=[(x+rr*math.cos(k*math.tau/N),y+rr*math.sin(k*math.tau/N),zz) for rr,zz in [(r,z),(rt,z+h)] for k in range(N)]
  return mesh(n,v,[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))]+[(k,(k+1)%N,(k+1)%N+N,k+N) for k in range(N)],m)
 def mark(n,p,role='worker',face=(0,1,0)):
  o=bpy.data.objects.new(n,None);scene.collection.objects.link(o);o.parent=current;o.location=p;o.empty_display_type='ARROWS';o.empty_display_size=.3;o['role']=role;o['facing_z_up']=list(face);o['clearance_radius_m']=.55;objects.append(o);markers.append({'name':n,'role':role,'position_z_up':list(p),'facing_z_up':list(face),'clearance_radius_m':.55});return o
 def start(n,foot,desc):
  global current,objects,markers
  current=bpy.data.objects.new(n,None);scene.collection.objects.link(current);objects=[];markers=[];current['description']=desc;current['footprint_m']=foot;current['units']='metres';return current
 def finish(n,foot,desc,**extra):
  global objects
  # Merge static pieces into useful modules, avoiding hundreds of tiny renderers.
  groups={};empties=[o for o in objects if o.type!='MESH']
  for o in objects:
   if o.type!='MESH':continue
   key='Structure'
   if o.name.startswith(('Roof_','Shingle','Gable_bargeboard','Eave','Ridge')):key='Roof'
   elif o.name.startswith('Wheat_'):key='Crop_'+o.name.split('_')[-1].split('.')[0]
   elif o.name.startswith('Flame_'):key='Fire_visual'
   elif o.name.startswith('Door_open'):key='Door'
   groups.setdefault(key,[]).append(o)
  merged=[]
  for key,group in groups.items():
   bpy.ops.object.select_all(action='DESELECT')
   for o in group:o.select_set(True)
   bpy.context.view_layer.objects.active=group[0]
   if len(group)>1:bpy.ops.object.join()
   ob=bpy.context.object;ob.name=n+'_'+key;merged.append(ob)
  objects=empties+merged
  geo=[v.co for o in objects if o.type=='MESH' for v in o.data.vertices]
  if geo:foot=[max(foot[0],2*max(abs(v.x) for v in geo)+.4),max(foot[1],2*max(abs(v.y) for v in geo)+.4)]
  current['footprint_m']=foot
  tris=0
  for o in objects:
   if o.type=='MESH':o.data.calc_loop_triangles();tris+=len(o.data.loop_triangles)
  bpy.ops.object.select_all(action='DESELECT');current.select_set(True)
  for o in objects:o.select_set(True)
  bpy.context.view_layer.objects.active=current
  bpy.ops.export_scene.gltf(filepath=str(EXP/(n+'.glb')),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_extras=True)
  assets.append({'id':n,'root':current,'objects':list(objects),'footprint_m':foot,'description':desc,'triangles':tris,'markers':list(markers),**extra})
 def floor(w,d):
  box('Stone_footing',(0,0,.08),(w+.16,d+.16,.16),'stone')
  for i in range(math.ceil(w/.48)):
   x=-w/2+(i+.5)*w/math.ceil(w/.48);box('Floorboard',(x,0,.20),(w/math.ceil(w/.48)-.016,d,.12),'woodlight')
 def roof(w,d,eave=2.85,rise=1.45):
  # Uneven shingle lengths in restrained broad courses; continuous substrate beneath.
  for side in [-1,1]:
   mesh('Roof_substrate',[(0,-d/2,eave+rise),(0,d/2,eave+rise),(side*w/2,d/2,eave),(side*w/2,-d/2,eave)],[(0,1,2,3)],'roofdark')
   for row in range(4):
    u0=row/4;u1=min(1.03,(row+1)/4+.04);count=math.ceil(d/.60)
    for j in range(count):
     y0=-d/2+j*d/count;y1=y0+d/count-.015
     v=[(side*w/2*u0,y0,eave+rise*(1-u0)+.045+.018*(4-row)),(side*w/2*u0,y1,eave+rise*(1-u0)+.045+.018*(4-row)),(side*w/2*u1,y1,eave+rise*(1-u1)+.045+.018*(4-row)),(side*w/2*u1,y0,eave+rise*(1-u1)+.045+.018*(4-row))]
     mesh('Shingle',v,[(0,1,2,3)],rng.choice(['roof','roof','rooflight','roofdark']))
   for y in [-d/2,d/2]:beam('Gable_bargeboard',(0,y,eave+rise+.06),(side*w/2,y,eave),.085,'wooddark')
   beam('Eave',(side*w/2,-d/2,eave),(side*w/2,d/2,eave),.095,'wood')
  beam('Ridge',(0,-d/2-.12,eave+rise+.07),(0,d/2+.12,eave+rise+.07),.09,'woodlight')
 def frame(w,d,h=2.85,back=True):
  for x in [-w/2,w/2]:
   for y in [-d/2,d/2]:
    box('Post',(x,y,(h+.26)/2),(.20,.20,h-.26),'wooddark');beam('Knee_brace',(x,y,h-.68),(x-math.copysign(.57,x),y,h-.12),.065)
  for y in [-d/2,d/2]:beam('Header',(-w/2,y,h),(w/2,y,h),.12)
  if back:
   box('Rear_wall',(0,d/2,1.50),(w,.14,2.46),'plaster')
   for x in [-w/2,0,w/2]:box('Rear_stud',(x,d/2-.1,1.51),(.13,.12,2.48),'wood')
 def crate(x,y,z=.26,s=.70):
  box('Crate',(x,y,z+s/2),(s,s,s),'woodlight')
  for dx in [-s*.40,s*.40]:box('Crate_band',(x+dx,y-s/2-.012,z+s/2),(.07,.035,s),'wooddark')
  beam('Crate_brace',(x-s*.4,y-s/2-.035,z+.07),(x+s*.4,y-s/2-.035,z+s-.07),.035)
 def barrel(x,y,z=.26):
  cyl('Barrel',(x,y,z),.31,.75,'wood',12,top=.32)
  for dz in [.10,.59]:cyl('Barrel_band',(x,y,z+dz),.327,.065,'iron',12)
 def bench(x,y,w=1.8):
  box('Bench_seat',(x,y,.69),(w,.48,.13),'woodlight')
  for dx in [-w*.35,w*.35]:box('Bench_leg',(x+dx,y,.43),(.16,.38,.50),'wood')
 def table(x,y,w=1.8,d=.75):
  box('Worktop',(x,y,1.05),(w,d,.14),'woodlight')
  for dx in [-w*.4,w*.4]:
   for dy in [-d*.35,d*.35]:box('Table_leg',(x+dx,y+dy,.62),(.13,.13,.86),'wood')
 def sign(x,y,z,kind):
  box('Trade_sign',(x,y,z),(.85,.10,.62),'teal')
  if kind=='saw':
   beam('Saw_icon',(x-.28,y-.07,z-.1),(x+.26,y-.07,z+.12),.035,'canvas')
   for i in range(5):box('Saw_teeth',(x-.22+i*.095,y-.075,z-.11+i*.04),(.035,.025,.10),'canvas')
  elif kind=='forge':
   box('Anvil_icon',(x,y-.07,z),(.43,.025,.13),'canvas');box('Anvil_foot',(x,y-.07,z-.14),(.24,.025,.11),'canvas')
  elif kind=='kitchen':
   cyl('Pot_icon',(x,y-.09,z-.12),.15,.19,'canvas',8)
  else:box('Store_icon',(x,y-.07,z),(.35,.025,.32),'canvas')
 def hut(t):
  w,d=[(4.5,4.4),(5.8,6.4),(7,8.6)][t-1];n='hut_%02d'%t;desc='Timber shelter for %d residents'%(t*2);start(n,[w+1,d+1],desc);floor(w,d);frame(w,d)
  for x in [-w/2,w/2]:box('Side_wall',(x,0,1.5),(.14,d,2.45),'woodlight' if t==1 else 'plaster')
  # Real doorway: wall segments end at jambs, never across the passage.
  for side in [-1,1]:box('Front_wall',(side*(w/4+.37),-d/2,1.5),((w-1.48)/2,.15,2.45),'woodlight' if t==1 else 'plaster')
  box('Door_lintel',(0,-d/2,2.65),(1.48,.18,.34),'wood')
  for x in [-.76,.76]:box('Door_jamb',(x,-d/2-.05,1.4),(.14,.23,2.3),'wooddark')
  box('Door_open_leaf',(.85,-d/2+.56,1.39),(.12,1.12,2.22),'teal')
  box('Entry_step',(0,-d/2-.35,.09),(1.8,.70,.18),'stonesun');mark('Entry',(0,-d/2-.85,.0),'entry')
  for i in range(t*2):
   side=-1 if i%2==0 else 1;row=i//2;y=-d/2+1.45+row*2.30;x=side*(w/2-.72)
   box('Bunk_frame',(x,y,.51),(1.08,2.12,.27),'wooddark');box('Bedroll',(x,y,.70),(.97,2.05,.15),'canvas');box('Blanket',(x,y+.20,.79),(.99,1.4,.05),'teal' if i%2 else 'coral');mark('Resident_%02d'%i,(x,y,.85),'resident')
  for x in [-w*.33,w*.33]:
   box('Window_shutter',(x,-d/2-.11,1.90),(.73,.10,.73),'teal')
   for dx in [-.38,.38]:box('Window_trim',(x+dx,-d/2-.18,1.90),(.075,.1,.85),'wooddark')
   box('Shutter_crossbar',(x,-d/2-.18,1.84),(.63,.055,.065),'woodlight')
   box('Window_sill',(x,-d/2-.19,1.47),(.91,.3,.09),'wood')
  for yy in [-d/2,d/2]:
   mesh('Timber_gable',[(-w/2,yy,2.85),(w/2,yy,2.85),(0,yy,4.05+t*.12)],[(0,1,2)],'woodlight')
   if yy<0:
    box('Loft_vent',(0,yy-.08,3.16),(.46,.06,.36),'wooddark')
    for xx in [-.14,0,.14]:box('Vent_louver',(xx,yy-.12,3.16),(.035,.045,.32),'wood')
  roof(w+.8,d+.75,rise=1.2+t*.12)
  if t>=2:barrel(w/2+.45,d*.20,0)
  if t==3:
   bench(-w*.30,-d/2-.70,1.7);beam('Clothes_line_post',(w/2+.4,-1,0),(w/2+.4,-1,2.4),.065);beam('Clothes_line',(w/2+.4,-1,2.35),(w/2+.4,1,2.35),.015,'wooddark');beam('Clothes_line_post',(w/2+.4,1,0),(w/2+.4,1,2.4),.065);box('Folded_linen',(w/2+.4,0,2.05),(.04,.65,.60),'canvas')
  finish(n,[w+1.5,d+2],desc,residents=2*t)
 def farm(t):
  count=[4,6,8][t-1];cols=count//2;w=cols*1.8+.8;d=5.4;n='farm_%02d'%t;desc='%d independent planting slots with central tending aisle'%count;start(n,[w,d],desc)
  for k in range(count):
   x=(k%cols-(cols-1)/2)*1.8;y=-1.5 if k<cols else 1.5
   box('Soil_bed_%02d'%k,(x,y,.09),(1.46,1.50,.18),'soil')
   for dx in [-.77,.77]:box('Raised_bed_edge',(x+dx,y,.13),(.10,1.66,.26),'wood')
   for dy in [-.80,.80]:box('Raised_bed_edge',(x,y+dy,.13),(1.64,.10,.26),'wood')
   mark('Plant_slot_%02d'%k,(x,y,.18),'planting_slot')
   if k<cols:mark('Tend_pair_%02d'%k,(x,0,0),'worker')
   # Separate named crop geometry; removable in future planting gameplay.
   for dx in [-.42,0,.42]:
    for dy in [-.43,0,.43]:
     h=rng.uniform(.48,.75);beam('Wheat_stem_%02d'%k,(x+dx,y+dy,.18),(x+dx+.04,y+dy,.18+h),.012,'wheat');cyl('Wheat_head_%02d'%k,(x+dx+.04,y+dy,.18+h),.075,.22,'wheat',5,top=.018)
     for side in [-1,1]:
      mesh('Wheat_leaf_%02d'%k,[(x+dx,y+dy,.32),(x+dx+side*.18,y+dy+.025,.48),(x+dx+side*.25,y+dy,.55),(x+dx+side*.13,y+dy-.025,.46)],[(0,1,2),(0,2,3)],'leaf')
  for x in [-w/2,w/2]:
   for y in [-2.6,0,2.6]:box('Fence_post',(x,y,.48),(.12,.12,.96),'wood')
   for z in [.38,.74]:beam('Fence_rail',(x,-2.6,z),(x,2.6,z),.045)
  barrel(-w/2-.5,1.4,0);mark('Entry',(0,-3.1,0),'entry');finish(n,[w+1.4,d+1],desc,planting_slots=count)
 def camp(t):
  n='campfire_%02d'%t;desc=['Landing fire','Stone-ring camp','Cooking camp','Gathering camp','Permanent settlement hearth'][t-1];rad=1.5+t*.25;start(n,[rad*2+1.5,rad*2+1.5],desc)
  if t>=4:
   cyl('Paved_court',(0,0,0),rad+.45,.12,'stonesun',18)
   for i in range(14):
    a=i*math.tau/14;box('Paving_edge',(math.cos(a)*(rad+.23),math.sin(a)*(rad+.23),.13),(.44,.4,.12),'stone')
  cyl('Ash_bed',(0,0,.12 if t>=4 else 0),.7,.09,'coal',12)
  for i in range(3):
   a=i*math.pi/3;beam('Firewood',(-.55*math.cos(a),-.55*math.sin(a),.23),(.55*math.cos(a),.55*math.sin(a),.29),.09,'wooddark')
  for i in range(5):
   a=i*math.tau/5;cyl('Flame_placeholder',(.22*math.cos(a),.22*math.sin(a),.29),.17,.50+.16*(i%2),'flame' if i%2 else 'ember',5,top=0)
  if t>=2:
   for i in range(10):
    a=i*math.tau/10;cyl('Hearth_stone',(.85*math.cos(a),.85*math.sin(a),.03),.23,.26,'stone' if i%3 else 'stonesun',6,top=.19)
  if t>=3:
   for x,y in [(-.9,-.35),(.9,-.35),(0,.8)]:beam('Cooking_tripod',(x,y,.13),(0,0,1.85),.047,'wooddark')
   beam('Pot_chain',(0,0,1.85),(0,0,.91),.018,'iron');cyl('Cooking_pot',(0,0,.68),.27,.31,'iron',10,top=.34)
  if t>=2:bench(-1.65,-.4,1.65)
  if t>=4:bench(1.65,-.4,1.65);barrel(-1.5,1.4,.12)
  if t==5:
   # Permanent masonry rear windbreak and store, leaving the fire open to the sky.
   for row in range(3):
    for j in range(7):box('Mortared_hearth_wall',(-1.95+j*.65+(row%2)*.12,2.05,.30+row*.29),(.62,.35,.27),'stone' if (row+j)%3 else 'stonesun')
   for x in [-2.2,2.2]:cyl('Court_corner_pier',(x,1.75,.12),.25,1.25,'stonesun',6)
   table(0,1.43,2.4,.65);beam('Camp_banner_post',(-2.6,1.7,0),(-2.6,1.7,3.35),.085);box('Settlement_banner',(-2.24,1.7,2.80),(.72,.045,.95),'coral');box('Banner_mark',(-2.24,1.667,2.85),(.12,.015,.46),'canvas')
  mark('Fire_tender',(0,-1.60,.12 if t>=4 else 0),'worker');mark('Entry',(0,-rad-.8,0),'entry');finish(n,[rad*2+1.5,rad*2+1.5],desc,stage=t)
 def workshop(kind):
  w,d={'sawmill':(6.8,5.8),'storage':(6,5),'blacksmith':(6.5,5.8),'kitchen':(6.2,5.5)}[kind];desc={'sawmill':'Open timber sawing shed with separate feeding and sawing positions','storage':'Dry store with central circulation aisle and packing station','blacksmith':'Open-sided forge with anvil, quench tub and masonry flue','kitchen':'Communal kitchen with prep bench, stove and serving counter'}[kind];start(kind,[w+1,d+2],desc);floor(w,d);frame(w,d);roof(w+.7,d+.6,rise=1.35)
  sign(0,-d/2-.15,2.54,{'sawmill':'saw','blacksmith':'forge','kitchen':'kitchen','storage':'store'}[kind]);mark('Entry',(0,-d/2-1,0),'entry')
  if kind=='sawmill':
   for y in [-.85,1.05]:
    beam('Sawhorse_top',(-1.65,y,.91),(.55,y,.91),.09)
    for x in [-1.4,.30]:
     for dy in [-.30,.30]:beam('Sawhorse_leg',(x,y+dy,.27),(x,y,.91),.06)
   beam('Saw_log',(-.62,-1.8,1.15),(-.62,1.85,1.15),.27,'wooddark',.22)
   box('Saw_blade',(-.45,0,1.56),(1.15,.035,.16),'iron')
   for x in [-1.03,.13]:box('Saw_handle',(x,0,1.56),(.09,.12,.5),'woodlight')
   for i in range(4):box('Sawn_plank',(2.1,1.55,.34+i*.13),(.64,2.45,.10),'woodlight')
   table(1.9,-1.65,1.6,.65);mark('Sawyer',(.64,0,.26),'worker');mark('Log_feeder',(-2.35,-.45,.26),'worker');mark('Finish_carpenter',(1.9,-.60,.26),'worker',face=(0,-1,0))
  elif kind=='storage':
   for side in [-1,1]:
    x=side*2.05
    for z in [.50,1.52]:
     box('Storage_shelf',(x,1.15,z),(1.35,2.3,.12),'woodlight')
     for y in [.4,1.2,2.0]:crate(x,y,z+.07,.64)
    for y in [0,2.3]:box('Shelf_post',(x,y,1.24),(.13,.13,2.0),'wooddark')
   table(1.70,-1.9,1.7,.75);crate(-2,-1.6,.26);mark('Storekeeper',(1.7,-.80,.26),'worker',face=(0,-1,0));mark('Delivery',(0,-1.3,.26),'delivery')
  elif kind=='blacksmith':
   box('Forge_base',(-1.7,1.1,.69),(2.0,1.6,.86),'stone');box('Coal_hearth',(-1.7,1.0,1.15),(1.5,1.15,.12),'coal');cyl('Hot_coals',(-1.7,1,1.22),.38,.10,'ember',8)
   for x in [-2.65,-.75]:box('Forge_jamb',(x,1.58,1.58),(.20,.45,1.3),'stonesun')
   box('Smoke_hood',(-1.7,1.55,2.25),(2.1,.95,.30),'stone');box('Chimney',(-1.7,1.8,3.5),(.8,.8,2.55),'stone');box('Chimney_cap',(-1.7,1.8,4.79),(1,.95,.15),'stonesun')
   cyl('Anvil_stump',(1.35,-.20,.26),.44,.57,'wooddark',10);box('Anvil_foot',(1.35,-.2,.9),(.60,.47,.17),'iron');box('Anvil_waist',(1.35,-.2,1.04),(.29,.31,.24),'iron');box('Anvil_face',(1.35,-.2,1.2),(.8,.41,.13),'iron');mesh('Anvil_horn',[(1.73,-.4,1.25),(1.73,0,1.25),(2.18,-.2,1.19),(1.73,-.2,1.08)],[(0,1,2),(0,2,3),(1,3,2),(0,3,1)],'iron')
   barrel(2.5,1.5);cyl('Quench_water',(2.5,1.5,1),.29,.015,'teal',12);mark('Smith',(1.25,-1.35,.26),'worker');mark('Forge_tender',(-1.7,-.5,.26),'worker');table(.9,2.0,1.8,.70)
  else:
   box('Stove_base',(-1.8,1.2,.69),(1.65,1.4,.86),'stonesun');box('Stovetop',(-1.8,1.2,1.18),(1.72,1.45,.12),'iron');cyl('Soup_pot',(-1.8,1.2,1.25),.35,.43,'iron',12,top=.39);box('Kitchen_flue',(-2.1,1.7,3.0),(.48,.48,3.5),'stone')
   table(1.7,1.1,2.0,1);box('Chopping_board',(1.7,1.1,1.15),(.8,.5,.06),'wooddark');cyl('Bread_loaf',(1.7,1.12,1.2),.20,.16,'wheat',8)
   table(1.55,-2.0,2.4,.75);barrel(-2.4,-1.6);mark('Cook',(-1.6,-.25,.26),'worker');mark('Prep_cook',(1.7,-.20,.26),'worker');mark('Server',(.85,-1.10,.26),'worker',face=(0,-1,0))
  if kind=='sawmill':
   # Exterior log rack makes the building readable even with the roof visible.
   for yy in [-1.25,1.25]:box('Log_rack_foot',(-w/2-.70,yy,.17),(1.25,.22,.34),'wooddark')
   for i,(xx,zz) in enumerate([(-w/2-.95,.54),(-w/2-.43,.54),(-w/2-.70,.99)]):
    N=10;v=[(xx+.23*math.cos(k*math.tau/N),y,zz+.23*math.sin(k*math.tau/N)) for y in [-1.65,1.65] for k in range(N)]
    mesh('Stored_log_bark',v,[(k,(k+1)%N,(k+1)%N+N,k+N) for k in range(N)],'wooddark')
    mesh('Stored_log_ends',v,[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))],'woodlight')
  elif kind=='storage':
   for xx in [-w/2,w/2]:box('Store_side_siding',(xx,0,1.1),(.13,d,1.67),'woodlight')
   barrel(w/2+.55,1.0,.0);crate(w/2+.55,-.10,.0,.66)
  elif kind=='kitchen':
   # Cream serving awning differentiates the kitchen from the workshops.
   for xx in [.15,2.9]:beam('Awning_post',(xx,-d/2-.9,0),(xx,-d/2-.9,2.6),.065)
   for i in range(5):
    x0=.05+i*.60;x1=x0+.60
    mesh('Canvas_awning',[(x0,-d/2+.05,2.82),(x1,-d/2+.05,2.82),(x1,-d/2-1,2.54),(x0,-d/2-1,2.54)],[(0,1,2,3)],'canvas' if i%2==0 else 'coral')
   box('Awning_valance',(1.55,-d/2-1,2.47),(3,.045,.14),'canvas')
  finish(kind,[w+1.5,d+2.5],desc)
 for t in range(1,6):camp(t)
 for t in range(1,4):hut(t)
 for t in range(1,4):farm(t)
 for kind in ['sawmill','storage','blacksmith','kitchen']:workshop(kind)
 # Saved sailor is linked as a review-only scale companion, never included in asset exports.
 with bpy.data.libraries.load(str(R/'tools/blender/source/crew-weathered-v2.blend'),link=False) as (src,dst):dst.objects=[n for n in src.objects if n.startswith('Male_Coat_Deckhand')][:1]
 sailor=next((o for o in dst.objects if o),None)
 if sailor:scene.collection.objects.link(sailor)
 # Review environment is not part of the assets.
 current=None;objects=[]
 ground=box('Review_ground',(0,0,-.12),(200,200,.2),'sand')
 world=bpy.data.worlds.new('Settlement_daylight');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.37,.52,.65,1);world.node_tree.nodes['Background'].inputs[1].default_value=.65;scene.world=world
 for p,power,size in [((-12,-16,24),3000,10),((12,5,18),1800,10)]:
  ld=bpy.data.lights.new('Review_light','AREA');o=bpy.data.objects.new('Review_light',ld);scene.collection.objects.link(o);o.location=p;ld.energy=power;ld.size=size;o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
 ld=bpy.data.lights.new('Sun','SUN');lo=bpy.data.objects.new('Sun',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.45,-.5,-.4);ld.energy=1.8;ld.angle=.12
 cd=bpy.data.cameras.new('Review_camera');cam=bpy.data.objects.new('Review_camera',cd);scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO'
 scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True;scene.view_settings.view_transform='AgX';scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 for a in assets:
  a['root'].location=(0,0,0)
  for o in a['objects']:o.hide_render=True
 def render_group(ids,filename):
  chosen=[a for a in assets if a['id'] in ids];count=len(chosen);spacing=9.4
  for a in assets:
   for o in a['objects']:o.hide_render=True
  for i,a in enumerate(chosen):
   a['root'].location=((i-(count-1)/2)*spacing,0,0)
   for o in a['objects']:o.hide_render=False
  if sailor:
   sailor.hide_render=False;sailor.location=(-count*spacing/2+.9,-3.9,.02)
   if count==1:
    spots=[m for m in chosen[0]['markers'] if m['role']=='worker']
    if spots:sailor.location=Vector(spots[0]['position_z_up'])
    else:
     entries=[m for m in chosen[0]['markers'] if m['role']=='entry']
     if entries:sailor.location=Vector(entries[0]['position_z_up'])
  target=Vector((0,0,1.3));cam.location=(count*1.2,-26,19) if count>1 else (8,-14,10);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cd.ortho_scale=max(12,count*spacing+1)
  scene.render.resolution_x=1600 if count>2 else 1200;scene.render.resolution_y=720 if count>2 else 900;scene.render.filepath=str(OUT/filename);bpy.ops.render.render(write_still=True)
 render_group(['campfire_%02d'%i for i in range(1,6)],'campfire-progression.png')
 render_group(['hut_%02d'%i for i in range(1,4)],'hut-progression.png')
 render_group(['farm_%02d'%i for i in range(1,4)],'farm-progression.png')
 render_group(['sawmill','storage','blacksmith','kitchen'],'workshops.png')
 # Individual close views, with the same real-size sailor beside each entry.
 for a in assets:
  if a['id'] in ['hut_03','blacksmith','campfire_05','farm_03']:render_group([a['id']],a['id']+'.png')
 for i,a in enumerate(assets):
  a['root'].location=((i%5)*10,(i//5)*11,0)
  for o in a['objects']:o.hide_render=False
 if sailor:sailor.location=(-4,-4,0)
 scene.render.resolution_x=1800;scene.render.resolution_y=1300;target=Vector((20,11,0));cam.location=target+Vector((22,-42,38));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cd.ortho_scale=61
 scene.render.filepath=str(OUT/'settlement-overview.png');bpy.ops.render.render(write_still=True)
 manifest={'units':'metres','coordinates':'Blender Z-up; GLB converts to Y-up','status':'Standalone static design assets; no Unity import, gameplay scripts, collisions or animations','stage_interpretation':'5 campfire / 3 hut / 3 farm total stages','character_height_m':1.93,'assets':[{k:v for k,v in a.items() if k not in ['root','objects']} for a in assets]}
 (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2));bpy.data.libraries.write(str(R/'tools/blender/source/settlement-kit-v1.blend'),{scene})
 print(json.dumps({'asset_count':len(assets),'triangles':{a['id']:a['triangles'] for a in assets}}))
finally:bpy.context.window.scene=previous
