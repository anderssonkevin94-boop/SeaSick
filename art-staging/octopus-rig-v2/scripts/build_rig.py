"""Build OctopusRig v2 (arms lengthened by --factor along their own centrelines) on Kevin's Meshy octopus. Headless:
  Blender -b --factory-startup --python build_rig.py -- [--weights custom|heat] [--suckers rigid|attach]
Never edits mesh geometry/UVs/textures: only adds an armature, vertex groups, an Armature modifier."""
import bpy, sys, math, os, shutil, json
from mathutils import Vector, Matrix
HERE=os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0,HERE)
import octo_seg as S
argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
def opt(name,default):
    return argv[argv.index(name)+1] if name in argv else default
WEIGHTS=opt('--weights','custom'); SUCK=opt('--suckers','rigid'); OUTBLEND=opt('--out',os.path.join(HERE,'..','octopus_rig.blend'))
SRCDIR="/Users/kevinandersson/Downloads/Meshy_AI_Crimson_Octopus_1003103504_texture_fbx/"
SRC=SRCDIR+"Meshy_AI_Crimson_Octopus_1003103504_texture.fbx"
OUTDIR=os.path.abspath(os.path.join(HERE,'..'))
NB=10; Q=0.85; F=float(opt('--factor','1.45')); RAMP=0.08; BLEND=float(opt('--blend','0.08')); SMOOTH_IT=int(opt('--smooth','8'))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
ob=[o for o in bpy.data.objects if o.type=='MESH'][0]
ob.name="Octopus"; me=ob.data
ob.rotation_euler=(0,0,0); ob.location=(0,0,0); ob.scale=(1,1,1)   # import rotation was -4.4e-8 rad: zeroed, mesh data untouched
seg=S.segment(me); W=seg['W']

# ---------------- arm centrelines, ordering ----------------
arms=[]
for a in seg['arms']:
    cl=S.centreline(seg,a)
    pts=[p for g,p in cl]
    # light smoothing of interior points (ring noise on a low-poly tube)
    sm=[pts[0]]+[(pts[i-1]+2*pts[i]+pts[i+1])/4 for i in range(1,len(pts)-1)]+[pts[-1]]
    gs=[g for g,p in cl]
    arms.append(dict(seg=a, pts=sm, gs=gs))
FRONT=Vector((0,-1,0))   # eyes/face side: the mantle shell sits at +Y behind the eyes
def ang(v): return math.atan2(v.y,v.x)
fa=ang(FRONT)
for A in arms:
    d=(A['pts'][min(3,len(A['pts'])-1)]-A['pts'][0]); A['dir']=d
    A['ang']=ang(A['pts'][-1].xy - S.CORE_C.xy)   # overall direction base->tip seen from above
# start with arm closest to front, then counter-clockwise (increasing angle)
def rel(a): return (a-fa)%(2*math.pi)
def angdist(a): r=rel(a); return min(r,2*math.pi-r)
first=min(arms,key=lambda A:angdist(A['ang']))
arms.sort(key=lambda A:(rel(A['ang'])-rel(first['ang']))%(2*math.pi))

def arclen(pts):
    s=[0.0]
    for i in range(1,len(pts)): s.append(s[-1]+(pts[i]-pts[i-1]).length)
    return s
def at_s(pts,ss,s):
    for i in range(1,len(pts)):
        if ss[i]>=s:
            t=(s-ss[i-1])/max(1e-9,ss[i]-ss[i-1]); return pts[i-1].lerp(pts[i],t)
    return pts[-1]
for A in arms:
    ss=arclen(A['pts']); L=ss[-1]; A['L']=L
    lens=[L*(1-Q)/(1-Q**NB)*Q**i for i in range(NB)]
    js=[0.0]
    for l in lens: js.append(js[-1]+l)
    A['jointS']=js; A['joints']=[at_s(A['pts'],ss,s) for s in js]

# ---------------- suckers -> arms ----------------
mainlab=seg['label']
from mathutils.kdtree import KDTree
kd=KDTree(len(seg['main']))
for j in seg['main']: kd.insert(W[j],j)
kd.balance()
def proj_poly(J,p,smin=None,smax=None):
    """closest point on joint polyline J; returns (s, dist, seg i, point)"""
    best=None; s0=0.0
    for i in range(len(J)-1):
        a,b=J[i],J[i+1]; ab=b-a; L=ab.length
        t=max(0.0,min(1.0,(p-a).dot(ab)/(L*L)))
        s=s0+t*L
        if (smin is None or s>=smin-L) and (smax is None or s<=smax+L):
            q=a+ab*t; d=(p-q).length
            if best is None or d<best[1]: best=(s,d,i,q)
        s0+=L
    return best
armidx={id(A['seg']):k for k,A in enumerate(arms)}
seg_to_k={}
for k,A in enumerate(arms): seg_to_k[seg['arms'].index(A['seg'])]=k
suck=[]
for isl in seg['suckers']:
    c=sum((W[j] for j in isl),Vector())/len(isl)
    # arm of the nearest main-island vertex (attachment surface)
    near=kd.find_n(c,6); labs=[mainlab.get(j) for _,j,_ in near]
    labs=[seg_to_k[l] for l in labs if l is not None and l>=0]
    if labs: k=max(set(labs),key=labs.count)
    else: k=min(range(len(arms)),key=lambda k:proj_poly(arms[k]['joints'],c)[1])
    s,d,i,q=proj_poly(arms[k]['joints'],c)
    suck.append(dict(verts=isl,c=c,arm=k,s=s,bone=i,off=(c-q),near=[j for _,j,_ in near]))

# ---------------- bone frames: Z axis -> sucker side ----------------
def transport(v, ya, yb):
    """parallel-transport a perpendicular vector from a bone with direction ya to one with yb"""
    r=ya.rotation_difference(yb); w=r@v; w=w-w.dot(yb)*yb
    return w.normalized() if w.length>1e-8 else v
for k,A in enumerate(arms):
    J=A['joints']; Ss=[x for x in suck if x['arm']==k]
    ys=[(J[i+1]-J[i]).normalized() for i in range(NB)]
    own={}
    for i in range(NB):
        acc=Vector()
        for x in Ss:
            if x['bone']==i:
                o=x['off']-x['off'].dot(ys[i])*ys[i]
                if o.length>1e-6: acc+=o.normalized()
        if acc.length>1e-6: own[i]=acc.normalized()
    A['own']=sorted(own)
    raw=[]
    for i in range(NB):
        if i in own: raw.append(own[i]); continue
        cand=Vector()
        prev=[p for p in own if p<i]; nxt=[n for n in own if n>i]
        if prev:
            p=max(prev); v=own[p]
            for q in range(p,i): v=transport(v,ys[q],ys[q+1])
            cand+=v/(i-p)
        if nxt:
            n=min(nxt); v=own[n]
            for q in range(n,i,-1): v=transport(v,ys[q],ys[q-1])
            cand+=v/(n-i)
        if cand.length<1e-6: cand=Vector((0,0,-1))
        cand=cand-cand.dot(ys[i])*ys[i]; raw.append(cand.normalized())
    # one smoothing pass (neighbours transported into this bone) so roll does not jitter bone to bone
    zd=[]
    for i in range(NB):
        v=raw[i]*2
        if i>0: v+=transport(raw[i-1],ys[i-1],ys[i])
        if i<NB-1: v+=transport(raw[i+1],ys[i+1],ys[i])
        v=v-v.dot(ys[i])*ys[i]; zd.append(v.normalized())
    A['zdirs']=zd

# ---------------- armature ----------------
arm_data=bpy.data.armatures.new("OctopusRig"); rig=bpy.data.objects.new("OctopusRig",arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig; rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb=arm_data.edit_bones
hub=S.CORE_C.xy.copy()
zunder=min(W[j].z for j in seg['main'] if (W[j].xy-hub).length<0.05)
root=eb.new("Root"); root.head=(hub.x,hub.y,zunder); root.tail=(hub.x,hub.y,zunder+0.08); root.align_roll(FRONT)
mant=seg['mantle']; top=max(mant,key=lambda j:W[j].z)
topc=sum((W[j] for j in mant if W[j].z>W[top].z-0.02),Vector()); topc/=sum(1 for j in mant if W[j].z>W[top].z-0.02)
body=eb.new("Body"); body.head=root.head; body.tail=topc; body.parent=root; body.use_connect=False; body.align_roll(FRONT)
for k,A in enumerate(arms):
    prev=None
    for i in range(NB):
        b=eb.new("Arm%d_%02d"%(k,i)); b.head=A['joints'][i]; b.tail=A['joints'][i+1]
        b.align_roll(A['zdirs'][i])
        if prev is None: b.parent=body; b.use_connect=False
        else: b.parent=prev; b.use_connect=True
        prev=b
bpy.ops.object.mode_set(mode='OBJECT')
ob.parent=rig
mod=ob.modifiers.new("Armature",'ARMATURE'); mod.object=rig

# ---------------- weights ----------------
def smoothstep(a,b,x):
    t=max(0.0,min(1.0,(x-a)/(b-a))); return t*t*(3-2*t)
names=[b.name for b in arm_data.bones]
groups={n:ob.vertex_groups.new(name=n) for n in names if n!="Root"}
wts=[dict() for _ in me.vertices]
def armweights(k,s):
    """tent weights along arm k at arc position s (s may be <0 before the base)"""
    A=arms[k]; js=A['jointS']; mids=[(js[i]+js[i+1])/2 for i in range(NB)]
    out={}
    wb=1.0-smoothstep(-BLEND,BLEND,s)
    if wb>0: out["Body"]=wb
    ra=1.0-wb
    if ra<=0: return out
    if s<=mids[0]: out["Arm%d_00"%k]=ra
    elif s>=mids[-1]: out["Arm%d_%02d"%(k,NB-1)]=ra
    else:
        for i in range(NB-1):
            if mids[i]<=s<=mids[i+1]:
                t=smoothstep(0,1,(s-mids[i])/(mids[i+1]-mids[i]))
                out["Arm%d_%02d"%(k,i)]=ra*(1-t); out["Arm%d_%02d"%(k,i+1)]=ra*t
    return out
def s_on_arm(k,p,prior=None):
    A=arms[k]; J=A['joints']
    first=(J[1]-J[0]).normalized()
    r=proj_poly(J,p,*( (prior-0.06,prior+0.06) if prior is not None else (None,None)))
    if r is None: r=proj_poly(J,p)
    s,d,i,q=r
    if s<=1e-6:   # before the base: signed distance along first bone direction
        s=(p-J[0]).dot(first)
    return s
# geodesic->arc prior per arm: map tip-geodesic g to centreline arc s
for k,A in enumerate(arms):
    ss=arclen(A['pts']); L=ss[-1]; A['g2s']=list(zip([A['gs'][len(A['gs'])-1-i] for i in range(len(A['gs']))][::-1],ss))
def prior_s(k,g):
    A=arms[k]; gl=list(reversed(A['gs'])); ss=arclen(A['pts'])   # pts base->tip, gs base->tip descending g
    gsb=A['gs']  # base->tip, g decreasing
    for i in range(1,len(gsb)):
        if gsb[i]<=g<=gsb[i-1] or gsb[i-1]<=g<=gsb[i]:
            t=(g-gsb[i-1])/((gsb[i]-gsb[i-1]) or 1e-9); return ss[i-1]+t*(ss[i]-ss[i-1])
    return ss[0] if g>gsb[0] else ss[-1]
base_pts=[A['joints'][0] for A in arms]
# 1) arc position s per vertex (arm verts: projection with geodesic prior; core verts near a base: signed offset)
S_of={}; K_of={}
for j in seg['main']:
    p=W[j]; l=mainlab.get(j)
    if l is not None and l>=0:
        k=seg_to_k[l]; g=seg['arms'][l]['dtip'].get(j)
        S_of[j]=s_on_arm(k,p, prior_s(k,g) if g is not None else None); K_of[j]=k
    else:
        k=min(range(len(arms)),key=lambda k:(p-base_pts[k]).length)
        if (p-base_pts[k]).length<2.5*BLEND: S_of[j]=s_on_arm(k,p); K_of[j]=k
# 2) smooth the s field over the surface (removes projection outliers in the curled tips, where an edge
#    could otherwise straddle two bones and tear when the curl changes)
adj=seg['adj']
for it in range(SMOOTH_IT):
    new={}
    for j,sv in S_of.items():
        nb=[S_of[b] for b,_ in adj[j] if K_of.get(b)==K_of[j]]
        new[j]=0.5*sv+0.5*sum(nb)/len(nb) if nb else sv
    S_of=new
for j in seg['main']:
    wts[j]=armweights(K_of[j],S_of[j]) if j in S_of else {"Body":1.0}
for isl in [seg['mantle']]+seg['eyes']:
    for j in isl: wts[j]={"Body":1.0}
def bone_seg_nearest(k,c):
    J=arms[k]['joints']; return proj_poly(J,c)[2]
for x in suck:
    if SUCK=='rigid':
        w={"Arm%d_%02d"%(x['arm'],x['bone']):1.0}
        if x['bone']==0 and x['s']<BLEND:   # inside the arm/mantle blend zone: one uniform Body/Arm_00 mix (still rigid per island)
            w=armweights(x['arm'],x['s'])
    else:   # one blended transform for the whole island = the arm-surface weights at the sucker's own arc position
        w=armweights(x['arm'],x['s'])
    for j in x['verts']: wts[j]=dict(w)

if WEIGHTS=='heat':
    # Blender bone-heat on everything, then keep suckers/eyes/mantle rules on top
    for o in bpy.data.objects: o.select_set(False)
    ob.modifiers.remove(mod); ob.parent=None
    for g in list(ob.vertex_groups): ob.vertex_groups.remove(g)
    ob.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active=rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    mod=[m for m in ob.modifiers if m.type=='ARMATURE'][0]
    groups={g.name:g for g in ob.vertex_groups}
    heat=[dict() for _ in me.vertices]
    for v in me.vertices:
        for ge in v.groups:
            if ge.weight>1e-4: heat[v.index][ob.vertex_groups[ge.group].name]=ge.weight
    for g in list(ob.vertex_groups): ob.vertex_groups.remove(g)
    groups={n:ob.vertex_groups.new(name=n) for n in names if n!="Root"}
    for j in seg['main']: wts[j]=heat[j]
    print("HEAT unweighted main verts:",sum(1 for j in seg['main'] if not heat[j]))

# limit 4, normalise, write
nzero=0; maxinf=0
for j,w in enumerate(wts):
    w={n:v for n,v in w.items() if v>1e-4 and n!="Root"}
    w=dict(sorted(w.items(),key=lambda kv:-kv[1])[:4])
    t=sum(w.values())
    if t<=0: nzero+=1; continue
    maxinf=max(maxinf,len(w))
    for n,v in w.items(): groups[n].add([j],v/t,'REPLACE')
print("WEIGHTS",WEIGHTS,"suckers",SUCK,"unweighted",nzero,"maxinf",maxinf)

# ---------------- v2: lengthen the arms along their centrelines (Kevin 2026-10-03: arms tower over the head) ----------------
# Arc position s on each arm maps to phi(s) = s + (F-1)*g(s), g = integral of smoothstep(0,RAMP) -> the stretch ramps in
# over the first RAMP metres so the arm/mantle junction is not pulled. Every arm cross-section is TRANSLATED to its new
# place on the lengthened centreline (no scaling, thickness unchanged). Suckers translate as one rigid piece.
# Head core, mantle and eyes (s<=0 / no arm) do not move at all.
def gint(u):
    if u<=0: return 0.0
    if u>=RAMP: return u-RAMP/2
    t=u/RAMP; return RAMP*(t**3-t**4/2)
def phi(u): return u+(F-1)*gint(u)
ORIG_JOINTS=[list(A['joints']) for A in arms]
for k,A in enumerate(arms):
    J=A['joints']; js=A['jointS']; dirs=[(J[i+1]-J[i]).normalized() for i in range(NB)]
    NJ=[J[0].copy()]
    for i in range(NB): NJ.append(NJ[-1]+dirs[i]*(phi(js[i+1])-phi(js[i])))
    A['dirs']=dirs; A['newJ']=NJ
def disp(k,s):
    if s<=0: return Vector()
    A=arms[k]; js=A['jointS']; J=A['joints']
    i=min(NB-1,max(0,next((i for i in range(NB) if s<js[i+1]),NB-1)))
    old=J[i]+A['dirs'][i]*(s-js[i]); new=A['newJ'][i]+A['dirs'][i]*(phi(s)-phi(js[i]))
    return new-old
D=[Vector() for _ in me.vertices]
for j in seg['main']:
    if j in S_of: D[j]=disp(K_of[j],S_of[j])
for x in suck:
    d=disp(x['arm'],x['s'])
    for j in x['verts']: D[j]=d.copy()
ORIG_CO=[v.co.copy() for v in me.vertices]
for j,v in enumerate(me.vertices):
    if D[j].length>0: v.co=ORIG_CO[j]+D[j]
me.update()
nonarm=[j for j,w in enumerate(wts) if set(k for k,vv in w.items() if vv>1e-4)<={"Body"}]
print("LENGTHEN F",F,"moved verts",sum(1 for d in D if d.length>0),"max disp %.4f"%max(d.length for d in D),
      "| non-arm (100%% Body) verts %d, max displacement %.3g, bit-identical %s"%(len(nonarm),max((me.vertices[j].co-ORIG_CO[j]).length for j in nonarm),
      all(tuple(me.vertices[j].co)==tuple(ORIG_CO[j]) for j in nonarm)))
bpy.context.view_layer.objects.active=rig; bpy.ops.object.mode_set(mode='EDIT')
for k,A in enumerate(arms):
    for i in range(NB):
        b=arm_data.edit_bones["Arm%d_%02d"%(k,i)]
        if i==0: b.head=A['newJ'][0]
        b.tail=A['newJ'][i+1]; b.align_roll(A['zdirs'][i])
bpy.ops.object.mode_set(mode='OBJECT')
for k,A in enumerate(arms):
    A['L_old']=A['L']; A['L']=sum((A['newJ'][i+1]-A['newJ'][i]).length for i in range(NB)); A['joints']=A['newJ']
    print("ARMLEN %d %.3f -> %.3f (x%.3f)"%(k,A['L_old'],A['L'],A['L']/A['L_old']))

# ---------------- textures next to the export, material relinked (files byte-identical copies) ----------------
for f in os.listdir(SRCDIR):
    if f.endswith('.png'): shutil.copy2(SRCDIR+f, os.path.join(OUTDIR,f))
# The FBX carries embedded copies (JPG/PNG) of Meshy's maps; point the material at the original PNGs instead
# (byte-identical copies next to the export), chosen by the socket each texture feeds.
BASE="Meshy_AI_Crimson_Octopus_1003103504_texture"
role_file={'Base Color':BASE+".png",'Color':BASE+"_normal.png",'Roughness':BASE+"_roughness.png",'Metallic':BASE+"_metallic.png"}
for mat in me.materials:
    for n in mat.node_tree.nodes:
        if n.type!='TEX_IMAGE' or not n.outputs[0].links: continue
        sock=n.outputs[0].links[0].to_socket.name
        if sock in role_file:
            old=n.image
            n.image=bpy.data.images.load(os.path.join(OUTDIR,role_file[sock]),check_existing=True)
            if sock!='Base Color': n.image.colorspace_settings.name='Non-Color'
            if old and old.users==0: bpy.data.images.remove(old)
print("IMAGES",[(i.name,i.filepath) for i in bpy.data.images])

# ---------------- report data for later steps ----------------
info=dict(order=[dict(arm=k,angle_deg=round(math.degrees(A['ang'])%360,1),L=round(A['L'],3),
                      base=[round(x,4) for x in A['joints'][0]],tip=[round(x,4) for x in A['joints'][-1]],
                      nsuckers=sum(1 for x in suck if x['arm']==k)) for k,A in enumerate(arms)],
          root=[round(x,4) for x in root.head] if False else None)
json.dump(dict(info=info,sucker_arm=[x['arm'] for x in suck]),open(os.path.join(OUTDIR,'scripts','_rig_info.json'),'w'),indent=1)
for k,A in enumerate(arms):
    print("ARM%d ang %.0f L %.3f nsuck %d base %s tip %s"%(k,math.degrees(A['ang'])%360,A['L'],sum(1 for x in suck if x['arm']==k),
          tuple(round(v,3) for v in A['joints'][0]),tuple(round(v,3) for v in A['joints'][-1])))
rig.data.pose_position='REST'
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(OUTBLEND))
print("SAVED",os.path.abspath(OUTBLEND))
