"""Open octopus_rig.blend, verify the curl convention, pose P1-P4, measure deformation, render.
  Blender -b --factory-startup --python poses_measure_render.py -- [--norender] [--save]"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
HERE=os.path.dirname(os.path.abspath(__file__)); OUT=os.path.abspath(os.path.join(HERE,'..'))
argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
BLEND=argv[argv.index('--blend')+1] if '--blend' in argv else os.path.join(OUT,'octopus_rig.blend')
RENDER='--norender' not in argv; ONLY=argv[argv.index('--only')+1].split(',') if '--only' in argv else None
bpy.ops.wm.open_mainfile(filepath=BLEND)
sc=bpy.context.scene; rig=bpy.data.objects['OctopusRig']; ob=bpy.data.objects['Octopus']; me=ob.data
rig.data.pose_position='POSE'
NB=10; NA=len([b for b in rig.data.bones if b.name.endswith('_00')])
WATER_Z=0.1

# ---------- islands (independent of build code) ----------
import bmesh
bm=bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
lab=[-1]*len(me.vertices); isl=[]
for v in bm.verts:
    if lab[v.index]>=0: continue
    st=[v]; cur=[]; lab[v.index]=len(isl)
    while st:
        a=st.pop(); cur.append(a.index)
        for e in a.link_edges:
            b=e.other_vert(a)
            if lab[b.index]<0: lab[b.index]=len(isl); st.append(b)
    isl.append(cur)
bm.free()
order=sorted(range(len(isl)),key=lambda i:-len(isl[i]))
MAIN=order[0]; SUCK=[i for i in order[4:]]
mainfaces=[p.index for p in me.polygons if lab[p.vertices[0]]==MAIN]
REST=[v.co.copy() for v in me.vertices]
gname={g.index:g.name for g in ob.vertex_groups}
VW=[{gname[g.group]:g.weight for g in v.groups} for v in me.vertices]
def dom(j): return max(VW[j].items(),key=lambda kv:kv[1])[0]

def reset():
    for pb in rig.pose.bones:
        pb.rotation_mode='XYZ'; pb.rotation_euler=(0,0,0); pb.location=(0,0,0); pb.scale=(1,1,1)
def apply(pose):
    reset()
    for n,v in pose.items():
        pb=rig.pose.bones[n]
        if n=='Root' and 'loc' in v: pb.location=v['loc']
        r=v.get('rot',(0,0,0)); pb.rotation_euler=tuple(math.radians(a) for a in r)
    bpy.context.view_layer.update()
def posed_verts():
    dg=bpy.context.evaluated_depsgraph_get(); e=ob.evaluated_get(dg); m=e.to_mesh()
    P=[v.co.copy() for v in m.vertices]; e.to_mesh_clear(); return P

# ---------- curl convention verification ----------
def sucker_info():
    out=[]
    for i in SUCK:
        c=sum((REST[j] for j in isl[i]),Vector())/len(isl[i])
        best=None
        for b in rig.data.bones:
            if not b.name.startswith('Arm'): continue
            h=b.head_local; t=b.tail_local; ab=t-h; tt=max(0,min(1,(c-h).dot(ab)/ab.length_squared)); q=h+ab*tt
            d=(c-q).length
            if best is None or d<best[0]: best=(d,b.name,q,tt)
        out.append(dict(i=i,c=c,bone=best[1],q=best[2],t=best[3]))
    return out
SI=sucker_info()
reset(); bpy.context.view_layer.update()
conv=[]; worst=(2,None)
def transport(v, ya, yb):
    r=ya.rotation_difference(yb); w=r@v; w=w-w.dot(yb)*yb
    return w.normalized() if w.length>1e-8 else v
for k in range(NA):
    bs=[rig.data.bones["Arm%d_%02d"%(k,i)] for i in range(NB)]
    ys=[(b.tail_local-b.head_local).normalized() for b in bs]
    own={}
    for i,b in enumerate(bs):
        raw=[s for s in SI if s['bone']==b.name]
        if raw:
            sd=Vector()
            for s in raw:
                o=s['c']-s['q']; o-=o.dot(ys[i])*ys[i]; sd+=o.normalized()
            own[i]=(sd.normalized(),len(raw))
    for i,b in enumerate(bs):
        n=b.name; y=ys[i]; z=b.matrix_local.to_3x3().col[2].normalized()
        if i in own: sd=own[i][0]; src='own suckers(%d)'%own[i][1]
        else:
            p=min(own,key=lambda p:abs(p-i)); v=own[p][0]; st=1 if i>p else -1
            for q in range(p,i,st): v=transport(v,ys[q],ys[q+st])
            sd=v; src='transported from %s'%bs[p].name
        reset(); rig.pose.bones[n].rotation_euler=(math.radians(15),0,0); bpy.context.view_layer.update()
        tail=rig.pose.bones[n].tail.copy(); reset(); bpy.context.view_layer.update()
        dv=(tail-b.tail_local); dv-=dv.dot(y)*y
        conv.append(dict(bone=n,z_dot=round(z.dot(sd),3),move_dot=round(dv.normalized().dot(sd),3),src=src))
        if z.dot(sd)<worst[0]: worst=(z.dot(sd),n)
own_c=[c for c in conv if c['src'].startswith('own')]; tr_c=[c for c in conv if not c['src'].startswith('own')]
print("CONV bones=%d own-sucker bones=%d min z.dot=%.3f mean=%.3f | transported bones=%d min z.dot=%.3f | min move_dot all=%.3f worst=%s"%(len(conv),len(own_c),min(c['z_dot'] for c in own_c),sum(c['z_dot'] for c in own_c)/len(own_c),len(tr_c),min([c['z_dot'] for c in tr_c] or [1]),min(c['move_dot'] for c in conv),worst[1]))

# ---------- metrics ----------
restB=BVHTree.FromPolygons(REST,[tuple(me.polygons[f].vertices) for f in mainfaces])
def skin_mats():
    M={}
    for pb in rig.pose.bones: M[pb.name]=rig.matrix_world@pb.matrix@pb.bone.matrix_local.inverted()
    return M
def measure(P):
    M=skin_mats()
    armf=[f for f in mainfaces if any(dom(j).startswith('Arm') for j in me.polygons[f].vertices)]
    rmax=0; rmin=9; flips=0; shear=0; flipat=[]; wmax=wmin=None
    for f in armf:
        vs=list(me.polygons[f].vertices)
        for a,b in zip(vs,vs[1:]+vs[:1]):
            L0=(REST[a]-REST[b]).length
            if L0>1e-7:
                r=(P[a]-P[b]).length/L0
                if r>rmax: rmax=r; wmax=(dom(a),dom(b),round(L0,4))
                if r<rmin: rmin=r; wmin=(dom(a),dom(b),round(L0,4))
    for f in range(len(me.polygons)):
        vs=list(me.polygons[f].vertices)
        if len(vs)<3: continue
        n0=(REST[vs[1]]-REST[vs[0]]).cross(REST[vs[2]]-REST[vs[0]])
        n1=(P[vs[1]]-P[vs[0]]).cross(P[vs[2]]-P[vs[0]])
        if n0.length<1e-12 or n1.length<1e-12: continue
        S=Matrix(((0,0,0),(0,0,0),(0,0,0)))
        for j in vs:
            for g,w in VW[j].items():
                S=S+M[g].to_3x3()*(w/len(vs))
        ex=(S.inverted_safe().transposed()@n0).normalized()
        d=ex.dot(n1.normalized())
        if d<0: flips+=1; flipat.append(dom(vs[0]))
        elif d<0.5: shear+=1
    posB=BVHTree.FromPolygons(P,[tuple(me.polygons[f].vertices) for f in mainfaces])
    drift=0; worst=None
    for s in SI:
        js=isl[s['i']]
        c0=s['c']; c1=sum((P[j] for j in js),Vector())/len(js)
        q0,_,_,d0=restB.find_nearest(c0); q1,_,_,d1=posB.find_nearest(c1)
        thick=2*max(0.01,(q0-s['q']).length)
        rel=abs(d1-d0)/thick
        if rel>drift: drift=rel; worst=(s['bone'],round(d0,4),round(d1,4),round(thick,4))
    # interpenetration: head (all-Body faces) vs arm k (faces whose verts are all dominated by Arm k bones 02..09), arm vs arm
    groups={}
    for f in range(len(me.polygons)):
        ds=[dom(j) for j in me.polygons[f].vertices]
        if all(d=='Body' for d in ds): key='head'
        elif all(d.startswith('Arm') and int(d[5:])>=2 for d in ds) and len(set(d[:4] for d in ds))==1: key=ds[0][:4]
        else: continue
        groups.setdefault(key,[]).append(tuple(me.polygons[f].vertices))
    T={k:BVHTree.FromPolygons(P,v) for k,v in groups.items()}
    keys=sorted(T); inter={}
    for i,a in enumerate(keys):
        for b in keys[i+1:]:
            n=len(T[a].overlap(T[b]))
            if n: inter[a+'-'+b]=n
    return dict(interpenetrating_face_pairs=inter, edge_ratio_max=round(rmax,3),at_max=wmax,edge_ratio_min=round(rmin,3),at_min=wmin,flipped_faces=flips,flipped_at=flipat,sheared_faces_dot_lt_0p5=shear,
                sucker_drift_max_rel_thickness=round(drift,3),sucker_worst=worst)

# ---------- poses ----------
def arm_curve(k, rots, yaw=0, twist=0):
    d={}
    for i,a in enumerate(rots):
        d["Arm%d_%02d"%(k,i)]={'rot':(a, twist if i==0 else 0, yaw if i==0 else 0)}
    return d
def raised(k, lift, s1, tip, yaw=0):
    # lift = curl away from suckers at the base (suckers face down at rest -> arm rises), S-curve, curled tip
    r=[-lift*0.45,-lift*0.35,-lift*0.2, s1, s1*0.5, tip*0.5, tip*0.7, tip*0.9, tip, tip]
    return arm_curve(k,r,yaw)
POSES={}
POSES['bind']={}
ROOTUP={'Root':{'loc':(0,0.05,0)}}       # Root local Y = world +Z: the whole octopus rises 5 cm
def chain_pts(k=0):
    return [rig.pose.bones["Arm%d_%02d"%(k,i)].head.copy() for i in range(NB)]+[rig.pose.bones["Arm%d_09"%k].tail.copy()]
headB=BVHTree.FromPolygons(REST,[tuple(p.vertices) for p in me.polygons if all(dom(j)=='Body' for j in p.vertices)])
def clearance(pts, rootz=0.05):
    m=9
    for i in range(2,len(pts)):
        for t in (0.5,1.0):
            q=pts[i-1].lerp(pts[i],t)-Vector((0,0,rootz))   # head moved up with Root: compare in rest frame
            m=min(m,headB.find_nearest(q)[3])
    return m
def search(base, family, grid, score, k=0, label=''):
    best=None
    for prm in grid:
        pose=dict(base); pose.update(family(*prm)); apply(pose); pts=chain_pts(k)
        sc_=score(pts)
        if sc_ is not None and (best is None or sc_>best[0]): best=(sc_,prm,pose)
    print("SEARCH",label,"best",best[1],round(best[0],3), "tip",[round(x,3) for x in chain_pts(k)[-1]] if apply(best[2]) is None else ''); return best[2]
LOW=dict(ROOTUP)
for k in range(NA): LOW.update(arm_curve(k,[-25,-10,0,5,5,8,8,10,10,10]))
# P1 surfaced idle: six arms tower in S-curves with hooked tips around the head; Arm1 stays low.
# Small per-arm search: tall, clear of the head (>= 7 cm), clear of the arms already placed (>= 9 cm between centrelines).
placed=[]
def famS(k,yaw):
    return lambda L,m,t: arm_curve(k,[-L*0.5,-L*0.3,-L*0.2, m, m, m*0.5, t, t, t+10, t+10],yaw=yaw)
def seg_pts(pts):
    out=list(pts)
    for i in range(1,len(pts)): out.append((pts[i-1]+pts[i])/2)
    return out
def scS_for(k):
    def sc(pts):
        if clearance(pts)<0.07: return None
        if pts[-1].z > pts[8].z: return None            # tip hooks over
        mine=seg_pts(pts[2:])
        for q in placed:
            if min((a-b).length for a in mine for b in q)<0.09: return None
        return pts[8].z + 0.3*pts[5].z
    return sc
P1=dict(ROOTUP)
# front pair stays low so the face stays clear; tips break the surface
P1.update(arm_curve(0,[-12,-4,0,0,0,0,0,0,0,0],yaw=-25)); P1.update(arm_curve(6,[-12,-4,0,0,0,0,0,0,0,0],yaw=25))
for k,yaw in ((1,-15),(5,15),(2,-5),(3,-10),(4,10)):
    g=[(L,m,t) for L in range(40,131,15) for m in range(-50,21,10) for t in (-10,0,10,20)]
    P1=search(P1,famS(k,yaw),g,scS_for(k),k=k,label='P1 arm%d'%k)
    apply(P1); placed.append(seg_pts(chain_pts(k)[2:]))
POSES['P1']=P1
# P2 windup: Arm0 high and coiled back over the head, tip behind the mantle (mantle back edge y~0.39)
def fam2(a,c1,c2,y): return arm_curve(0,[a*0.5,a*0.3,a*0.2,c1,c1,c1,c2,c2,c2,c2],yaw=y)
def sc2(pts):
    if clearance(pts)<0.055: return None
    t=pts[-1]
    mz=max(p.z for p in pts)
    if t.y<0.18 or mz<0.55: return None     # high arc leaning back, tip over the mantle
    return mz + 1.5*t.y
grid2=[(a,c1,c2,y) for a in range(-180,-59,20) for c1 in range(-60,1,10) for c2 in range(-90,71,10) for y in (-60,-40,-20,0,20,40,60)]
P2=search(LOW,fam2,grid2,sc2,label='P2')
POSES['P2']=P2
# P3 slam: Arm0 whipped forward/down, extended, tip on the water plane in front (furthest reach, arm arcs above water)
def fam3(a,c,y): return arm_curve(0,[a,c*0.5]+[c]*8,yaw=y)
def sc3(pts):
    t=pts[-1]
    if abs(t.z-WATER_Z)>0.015: return None
    if max(p.z for p in pts)<0.2: return None
    return -t.y
grid3=[(a,c,y) for a in range(-80,1,5) for c in range(-35,16,2) for y in (-10,0,10)]
P3=search(LOW,fam3,grid3,sc3,label='P3')
POSES['P3']=P3
# P4 extreme curl: Arm2 lifted then rolled into a tight spiral toward its suckers
P4={}; P4.update(arm_curve(2,[-70,-20,40,50,55,60,60,60,60,60]))
POSES['P4']=P4
if '--sanity' in argv:
    POSES['SANITY_arm_into_head']=dict(ROOTUP); POSES['SANITY_arm_into_head'].update(arm_curve(0,[-120,-40,-30,0,0,0,0,0,0,0],yaw=-30))
if '--curlsweep' in argv:
    for ang in (20,30,40,50):
        POSES['P4sweep_%d'%ang]=arm_curve(2,[-70,-20]+[ang]*8)
results={}
for name,pose in POSES.items():
    if ONLY and name not in ONLY: continue
    apply(pose); P=posed_verts()
    m=measure(P)
    tip0=rig.pose.bones["Arm0_09"].tail
    results[name]=dict(metrics=m, arm0_tip=[round(x,3) for x in tip0])
    print("POSE",name,json.dumps(results[name]))
json.dump(dict(convention=conv,results=results),open(os.path.join(HERE,'_measure.json'),'w'),indent=1)

# ---------- render ----------
if RENDER:
    try: sc.render.engine='BLENDER_EEVEE'
    except TypeError as e:
        try: sc.render.engine='BLENDER_EEVEE_NEXT'
        except TypeError: sc.render.engine='BLENDER_WORKBENCH'
    print("ENGINE",sc.render.engine)
    sc.render.resolution_x=1200; sc.render.resolution_y=900
    try: sc.eevee.taa_render_samples=32
    except Exception: pass
    sc.view_settings.view_transform='Standard'
    w=bpy.data.worlds.new("W"); sc.world=w; w.use_nodes=True
    bg=next(n for n in w.node_tree.nodes if n.type=='BACKGROUND'); bg.inputs[0].default_value=(0.78,0.80,0.83,1); bg.inputs[1].default_value=1.0
    sun=bpy.data.objects.new("Sun",bpy.data.lights.new("Sun",'SUN')); sun.data.energy=3.5; sc.collection.objects.link(sun)
    sun.rotation_euler=(math.radians(40),math.radians(10),math.radians(-35))
    fill=bpy.data.objects.new("Fill",bpy.data.lights.new("Fill",'SUN')); fill.data.energy=1.2; sc.collection.objects.link(fill)
    fill.rotation_euler=(math.radians(-60),math.radians(0),math.radians(150))
    cam=bpy.data.objects.new("Cam",bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera=cam
    cam.data.lens=50
    # water plane (render only, deleted before any save)
    bpy.ops.mesh.primitive_plane_add(size=10,location=(0,0,WATER_Z)); water=bpy.context.active_object; water.name="WaterPreview"
    wm=bpy.data.materials.new("WaterPreview"); wm.use_nodes=True
    bs=next(n for n in wm.node_tree.nodes if n.type=='BSDF_PRINCIPLED'); bs.inputs['Base Color'].default_value=(0.15,0.4,0.75,1); bs.inputs['Alpha'].default_value=0.45
    bs.inputs['Roughness'].default_value=0.3
    try: wm.surface_render_method='BLENDED'
    except Exception: pass
    water.data.materials.append(wm)
    def shoot(fname, target, dirv, dist, with_water, lens=50):
        water.hide_render=not with_water
        cam.data.lens=lens
        d=Vector(dirv).normalized(); cam.location=Vector(target)+d*dist
        cam.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler()
        sc.render.filepath=os.path.join(OUT,'renders',fname); bpy.ops.render.render(write_still=True)
    shots={
      'bind':[('bind_front34.png',(0,-0.02,0.0),(0.8,-1.2,0.75),2.9,False)],
      'P1':[('P1_front34.png',(0,0,0.25),(0.8,-1.2,0.45),3.2,True),('P1_face34.png',(0,0,0.25),(-0.35,-1.2,0.3),3.2,True),('P1_side.png',(0,0,0.25),(1,0,0.12),3.2,True)],
      'P2':[('P2_windup_front34.png',(0.1,0.05,0.3),(1.0,-0.7,0.4),3.4,True)],
      'P3':[('P3_slam_front34.png',(0.05,-0.3,0.15),(1.0,-0.65,0.35),3.4,True)],
      'P4':[('P4_curl_closeup.png',None,(-0.25,-1,0.15),1.5,False)],
    }
    for name,lst in shots.items():
        if ONLY and name not in ONLY: continue
        apply(POSES[name])
        for f,t,dv,dist,ww in lst:
            if t is None:
                pts=chain_pts(2); t=tuple(sum(pts[3:],Vector())/len(pts[3:]))
            shoot(f,t,dv,dist,ww)
    bpy.data.objects.remove(water)
reset()
if '--save' in argv:
    # keep poses as actions on the rig for reference; armature saved in rest
    for name,pose in POSES.items():
        if name=='bind': continue
        apply(pose); act=bpy.data.actions.new("Pose_"+name); act.use_fake_user=True
        rig.animation_data_create(); rig.animation_data.action=act
        for pb in rig.pose.bones:
            for fr in (1,5):
                pb.keyframe_insert('rotation_euler',frame=fr); pb.keyframe_insert('location',frame=fr)
        rig.animation_data.action=None
    reset(); rig.data.pose_position='REST'
    for o in [o for o in bpy.data.objects if o.type in ('LIGHT','CAMERA')]: bpy.data.objects.remove(o)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("SAVED with actions")
