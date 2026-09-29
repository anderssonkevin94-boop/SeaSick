import json, math
from pathlib import Path
R=Path(__file__).resolve().parents[1]/'Assets/_Project/Resources/ShipModules'
def write(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,indent=2)+'\n')
def v(x=0,y=0,z=0):return dict(x=x,y=y,z=z)
s=json.loads((R/'standards.json').read_text());s['joinProfiles']=[p for p in s['joinProfiles'] if not p['id'].startswith('F30')]
for name,up in [('F30',0),('F30R',6.16)]:s['joinProfiles'].append(dict(id=name,version=1,status='prototype',halfBeamU=4.64,deckZU=1.76,keelZU=-1.92,upperDeckZU=up,description='Approved F coaster; fixed beam, two levels.'))
s['mountStandards']=[p for p in s['mountStandards'] if not p['id'].startswith('F30')]
for l,r in [('low',2.03),('raised',3.35)]:s['mountStandards'].append(dict(id='F30-'+l,version=1,status='prototype',nominalRadius=r,sweptRadius=r+.03,paddleWidth=5.08,radiusLimit=r+.25,pocketCeilingZU=2*r-1.7+.25))
write(R/'standards.json',s)
def so(id,role,pos,standard='',**kw):return dict(id=id,role=role,posU=pos,standard=standard,**kw)
for kind,L,gunx,floor in [('stern',9.8,5.7,2.96),('middle',6,3,1.76),('bow',7.1,2.1,2.11)]:
 for raised in [False,True]:
  level='raised' if raised else 'low';name=kind.title()+level.title();id=f'hull.{kind}.f30.{level}.v1'
  sockets=[so('AftSocket','hull.origin' if kind=='stern' else 'hull.aft',v(),'F30' if kind!='stern' else '')]
  sockets.append(so('ForwardSocket','hull.tip' if kind=='bow' else 'hull.fwd',v(L),'F30' if kind!='bow' else ''))
  if kind=='bow':sockets.append(so('Stem','hull.stem',v(L)))
  if kind=='stern':sockets.append(so('WheelModuleSocket','wheel',v(.12,0,3.35-1.7 if raised else 2.03-1.7),'F30-'+level,radiusLimit=3.6 if raised else 2.28))
  slots=[];ids=[];passages=[]
  for deck,z in [(0,floor)]+([(1,6.16)] if raised and kind!='stern' else []):
   if kind=='stern' and not raised:continue
   for side in [-1,1]:
    sid=f'Gun_{deck}_{side}';ids.append(sid)
    sockets.append(so(sid,'deck.slot',v(gunx,side*3.46,z),'equipment.deck-gun',yawDeg=180 if side==1 else 0))
    slots.append(dict(id=sid,socketId=sid,clearanceSizeU=v(2.05,2.95,2),classes=['equipment.deck-gun'],provisional=False))
   passages.append(dict(id=f'Passage{deck}',centreU=v(L/2,0,z+1.7),sizeU=v(L,3.8,3.4),provisional=False))
  mass={'stern':10000,'middle':6500,'bow':6000}[kind]+(1700 if raised else 0)
  d=dict(schemaVersion=1,id=id,version=1,kind=kind.title(),family='F30R' if raised else 'F30',status='prototype',displayName=f'{kind.title()} — '+('two decks' if raised else 'base deck'),source='art-staging/f-coaster-runtime/kit.json',lengthU=L,boundsMinU=v(-.2,-4.95,-1.92),boundsMaxU=v(L+(2.2 if kind=='bow' else 0),4.95,9 if raised else 5),sockets=sockets,visuals=[dict(id=name,resourcePath='ShipModules/Meshes/FCoaster/'+name)],equipmentSlots=slots,passages=passages,lightship=dict(massKg=mass,provisional=True,rule='Initial playable balance; tune after sailing trials.'),capacity=dict(holdCells=dict(value={'stern':6,'middle':5,'bow':5}[kind]+(2 if raised else 0),provisional=True),berths=dict(value=4,provisional=True),gunSlots=dict(ids=ids,provisional=False)),hydrostatics=dict(resourcePath='ShipModules/Hydrostatics/FCoaster/'+kind,validWaterlineZU=[-1.92,1.76],notes='Numerical approximation of the authored station profile; initial flotation tuning.'))
  if raised:d['upperStructure']=dict(massKg=1700,centroidZU=4.6,deckZU=7.36 if kind=='stern' else 6.16,deckMassKg=1000,wallMassKg=700)
  write(R/'Modules'/f'{id}.json',d)
 # Approximate station integration from source hull profile. Includes sculpted keel lift.
 profile=[(-1.92,.28),(-1.58,.58),(-1.02,.85),(-.34,.98),(.37,1.025),(1.06,1.018),(1.76,1)]
 zs=[-1.92+3.68*i/64 for i in range(65)];vols=[]
 for wl in zs:
  total=0
  for j in range(120):
   x=L*(j+.5)/120;t=x/L
   weight=(1-t)**2 if kind=='stern' else t*t if kind=='bow' else 0
   lift=(1.05 if kind=='stern' else 1.85)*weight;sweep=(.5 if kind=='stern' else .85)*weight
   width=4.64
   if kind=='stern':width*=.88+.12*math.sin(min(x/4.6,1)*math.pi/2)
   if kind=='bow':
    tt=max(0,(x-3.65)/(7.1-3.65));kn=[(0,1),(.4,.76),(.76,.30),(1,.025)]
    for (a,aa),(b,bb) in zip(kn,kn[1:]):
     if tt<=b:width*=aa+(bb-aa)*(tt-a)/(b-a);break
   p=[(z+lift*(1.76-z)/3.68+sweep*(z+1.92)/3.68,width*f) for z,f in profile]
   area=0
   for (a,wa),(b,wb) in zip(p,p[1:]):
    top=min(wl,b)
    if top>a:
     wtop=wa+(wb-wa)*(top-a)/(b-a);area+=(wa+wtop)*(top-a)
   total+=area*L/120
  vols.append(total)
 write(R/'Hydrostatics/FCoaster'/f'{kind}.json',dict(schemaVersion=1,module=kind,validWaterlineZU=[-1.92,1.76],hullXRangeU=[0,L],waterlineZU=zs,integratedVolumeU3=vols))
for level,r in [('low',2.03),('raised',3.35)]:
 id='wheel.rotor.f30.'+level
 d=dict(schemaVersion=1,id=id,version=1,kind='Rotor',family='F30-'+level,status='prototype',displayName='Stern paddle wheel',boundsMinU=v(-r,-2.54,-r),boundsMaxU=v(r,2.54,r),sockets=[],visuals=[dict(id='Rotor',resourcePath='ShipModules/Meshes/FCoaster/Rotor'+level.title())],rotor=dict(mount='F30-'+level,nominalRadius=r,sweptRadius=r+.03,paddleWidth=5.08),lightship=dict(massKg=0,provisional=True))
 write(R/'Modules'/f'{id}.json',d)
 # Carrier is drawn with the hull; an empty visual marks the standard mount.
 id='wheel.carrier.f30.'+level
 d=dict(schemaVersion=1,id=id,version=1,kind='Carrier',family='F30-'+level,status='prototype',displayName='Integrated wheel bearings',boundsMinU=v(),boundsMaxU=v(),sockets=[],visuals=[dict(id='IntegratedCarrier',resourcePath='ShipModules/Meshes/FCoaster/IntegratedCarrier')],carrier=dict(mount='F30-'+level),lightship=dict(massKg=0,provisional=True))
 write(R/'Modules'/f'{id}.json',d)
print('F30 definitions written')
