"""Export approved evaluated meshes in explicit Unity metres; source studies stay untouched."""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/f-coaster-runtime';OUT.mkdir(exist_ok=True)
models=[]
def vec(v):return [-v.y*.5,v.z*.5,v.x*.5]
def export(name,source,origin,groups,exclude=(),prefixes=()):
 bpy.ops.wm.open_mainfile(filepath=str(ROOT/source))
 for o in bpy.data.objects:
  if 'cannon_present' in o:o['cannon_present']=False
  if o.name.startswith('Hinged_Port_Cover'):
   o.driver_remove('rotation_euler',0);o.rotation_euler[0]=0
 bpy.context.view_layer.update()
 dg=bpy.context.evaluated_depsgraph_get();parts=[]
 for o in list(bpy.context.scene.objects):
  if o.type!='MESH' or not o.parent:continue
  if o.parent.name not in groups and not any(o.name.startswith(p) for p in prefixes):continue
  if any(o.name.startswith(p) for p in exclude):continue
  ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles()
  mat=ev.matrix_world;nm=mat.to_3x3().inverted().transposed();verts=[];norms=[];cols=[];tris=[]
  ca=me.color_attributes.get('GameColor') or me.color_attributes.active_color
  for tri in me.loop_triangles:
   for li in tri.loops:
    vi=me.loops[li].vertex_index;p=mat@me.vertices[vi].co-Vector(origin)
    # Clip obsolete middle overhang / transition timber. Whole floor planks only.
    verts.append(vec(p));n=nm@me.corner_normals[li].vector;n.normalize();norms.append([-n.y,n.z,n.x])
    c=ca.data[li if ca.domain=='CORNER' else vi].color if ca else (.6,.3,.1,1)
    cols.append(list(c));tris.append(len(tris))
  # Source->game mapping reverses handedness; reverse triangles.
  for i in range(0,len(tris),3):tris[i+1],tris[i+2]=tris[i+2],tris[i+1]
  if name=='MiddleRaised' and 'Floor' in o.name:
   keep=[]
   for i in range(0,len(tris),3):
    if min(verts[j][2] for j in tris[i:i+3])>=-.001:keep.extend(tris[i:i+3])
   tris=keep
  if tris:parts.append(dict(name=o.name,vertices=[x for v in verts for x in v],normals=[x for v in norms for x in v],colors=[x for v in cols for x in v],triangles=tris))
  ev.to_mesh_clear()
 models.append(dict(name=name,parts=parts));print('EXPORTED',name,len(parts),sum(len(p['triangles'])//3 for p in parts),flush=True)
f29='art-staging/f-coaster-v29/base-bow-raised-stern/ship.blend'
flo='art-staging/f-coaster-v29/runtime-low-source/ship.blend'
f26='art-staging/f-coaster-v26/pointed-prow/ship.blend'
# Resolve folder names rather than assume the review label.
f26=str(next((ROOT/'art-staging/f-coaster-v26').glob('*/ship.blend')).relative_to(ROOT))
f19='art-staging/f-coaster-v19/rear-and-middle/ship.blend'
sg=['Stern','Stern_Interior','Stern_Upper_1','Wheel_Well','Stern_Fittings','Cannon_Port_Frame_Port','Cannon_Port_Frame_Starboard','Cannon_Port_Cover_Port','Cannon_Port_Cover_Starboard']
for level,src in [('Low',flo),('Raised',f29)]:
 export('Stern'+level,src,(0,0,0),sg,exclude=('Center_Approach_Steps','Turning_Stairs','Front_Guard','Helm_Access_Steps'),prefixes=('Lantern_Stern_',))
 export('Rotor'+level,src,(.12,0,.33 if level=='Low' else 1.65),['Paddle_Rotor_Module'])
export('MiddleLow',f29,(9.8,0,0),['Middle_0'])
export('MiddleRaised',f19,(9.8,0,0),['Middle_0','Middle_Upper_1','Cannon_Port_Frame_Middle_Port','Cannon_Port_Frame_Middle_Starboard','Cannon_Port_Cover_Middle_Port','Cannon_Port_Cover_Middle_Starboard'],exclude=('Middle_Aft_Transition','Middle_Front_Guard'))
export('BowLow',f29,(15.8,0,0),['Bow','Bow_Foredeck','Bow_Base_Port_Trim','Bow_Low_Foredeck_Edge'],prefixes=('Lantern_Bow_',))
export('BowRaised',f26,(15.8,0,0),['Bow','Bow_Foredeck','Bow_Upper_1','Bow_Upper_Port_Trim','Cannon_Port_Frame_Bow_Port','Cannon_Port_Frame_Bow_Starboard','Cannon_Port_Cover_Bow_Port','Cannon_Port_Cover_Bow_Starboard'],exclude=('Bow_Stairwell_Guard','Bow_Aft_Framed_Face','Foredeck_Step'),prefixes=('Lantern_Bow_',))
(OUT/'kit.json').write_text(json.dumps(dict(models=models),separators=(',',':')))
print('RUNTIME_KIT_DONE',flush=True)
