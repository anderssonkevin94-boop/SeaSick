"""Shared segmentation for the octopus rig: islands, geodesic arm labels, ring centrelines.
Imported by build_rig.py. Pure analysis, never modifies the mesh."""
import bmesh, math, heapq
from mathutils import Vector

CORE_C = Vector((-0.016, 0.0, 0.0))   # head-core centre (between the eyes, measured)
CORE_R = 0.13                          # geodesic sources: main-island verts inside this ball

def islands(me):
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    lab = [-1]*len(me.vertices); out = []
    for v in bm.verts:
        if lab[v.index] >= 0: continue
        st=[v]; cur=[]; lab[v.index]=len(out)
        while st:
            a=st.pop(); cur.append(a.index)
            for e in a.link_edges:
                b=e.other_vert(a)
                if lab[b.index] < 0: lab[b.index]=len(out); st.append(b)
        out.append(cur)
    edges=[(e.verts[0].index,e.verts[1].index) for e in bm.edges]
    bm.free()
    return out, edges

def dijkstra(adj, sources):
    d={s:0.0 for s in sources}; h=[(0.0,s) for s in sources]; heapq.heapify(h)
    while h:
        dd,u=heapq.heappop(h)
        if dd>d[u]: continue
        for v,L in adj[u]:
            nd=dd+L
            if nd<d.get(v,1e9): d[v]=nd; heapq.heappush(h,(nd,v))
    return d

def components(adj, verts):
    vs=set(verts); seen=set(); comps=[]
    for j in verts:
        if j in seen: continue
        st=[j]; c=[]; seen.add(j)
        while st:
            a=st.pop(); c.append(a)
            for b,_ in adj[a]:
                if b in vs and b not in seen: seen.add(b); st.append(b)
        comps.append(c)
    return comps

def segment(me):
    W=[v.co.copy() for v in me.vertices]
    isl, edges = islands(me)
    isl.sort(key=len, reverse=True)
    main=isl[0]; mains=set(main)
    rest=isl[1:]
    # mantle = 72-vert shell, eyes = the two ~33-vert pieces near the head; everything else = suckers
    mantle=rest[0]; eyes=rest[1:3]; suckers=rest[3:]
    adj={i:[] for i in main}
    for a,b in edges:
        if a in mains:
            L=(W[a]-W[b]).length; adj[a].append((b,L)); adj[b].append((a,L))
    src=[j for j in main if (W[j]-CORE_C).length<CORE_R]
    d=dijkstra(adj, src)
    # threshold where arms separate: pick components with long tips
    for T in [0.10,0.12,0.14,0.16,0.18,0.20,0.22]:
        comps=[c for c in components(adj,[j for j in main if d[j]>T]) if len(c)>=15]
        armc=[]; other=[]
        for c in comps:
            tip=max(c,key=lambda j:d[j])
            # an arm reaches far out; the dome under the mantle shell also sticks out but its tip is high (z>0.15)
            (armc if (d[tip]>0.30 and W[tip].xy.length>0.30 and W[tip].z<0.15) else other).append(c)
        if len(armc)>=7 and len(other)>=1: break
    # region-grow labels downward (watershed on d)
    label={}
    for k,c in enumerate(armc):
        for j in c: label[j]=k
    for k,c in enumerate(other):
        for j in c: label[j]=-2          # dome / non-arm bulge -> Body
    for j in sorted([j for j in main if j not in label], key=lambda j:-d[j]):
        if d[j] <= 0.0: continue
        nb=[label[b] for b,_ in adj[j] if b in label]
        if nb: label[j]=max(set(nb), key=nb.count)
    arms=[]
    for k,c in enumerate(armc):
        verts=[j for j in main if label.get(j)==k]
        tip=max(c,key=lambda j:d[j])
        dt=dijkstra({j:[(b,L) for b,L in adj[j] if label.get(b)==k] for j in verts}, [tip])
        arms.append(dict(verts=verts, tip=tip, dtip=dt))
    return dict(W=W, main=main, mantle=mantle, eyes=eyes, suckers=suckers, adj=adj,
                dcore=d, label=label, arms=arms, T=T, n_other=len(other))

def rings(seg, arm, step=0.02):
    W=seg['W']; dt=arm['dtip']; dc=seg['dcore']
    bins={}
    for j,g in dt.items(): bins.setdefault(int(g/step),[]).append(j)
    out=[]
    for b in sorted(bins):
        js=bins[b]; c=sum((W[j] for j in js),Vector())/len(js)
        r=sum((W[j]-c).length for j in js)/len(js)
        out.append(dict(g=(b+0.5)*step, c=c, r=r, n=len(js), dcore=min(dc[j] for j in js)))
    return out

def centreline(seg, arm, step=0.025, win=0.035, g_end=0.015, dcore_base=0.05):
    """Gaussian-window ring centroids by geodesic distance from the tip.
    Returns list of (g, point) ordered BASE -> TIP. The base is where the arm leaves the head-core ball
    (geodesic distance from the core ~dcore_base)."""
    W=seg['W']; dt=arm['dtip']; dc=seg['dcore']
    gmax=max(dt.values())
    # base g: smallest g whose nearby verts are within dcore_base of the core
    gb=gmax
    g=g_end; pts=[]
    while g<=gmax:
        num=Vector(); den=0.0; dcm=0.0
        for j,gj in dt.items():
            w=math.exp(-((gj-g)/win)**2)
            if w<1e-3: continue
            num+=W[j]*w; den+=w; dcm+=dc[j]*w
        if den>0:
            pts.append((g, num/den, dcm/den))
        g+=step
    # cut at base: first sample (walking tip->base) whose mean core distance drops below dcore_base
    out=[]
    for g,p,dcm in pts:
        out.append((g,p))
        if dcm<dcore_base: break
    # tip end point: the tip vertex region (first ring) itself
    tipc=sum((W[j] for j,gj in dt.items() if gj<g_end),Vector())
    ntip=sum(1 for j,gj in dt.items() if gj<g_end)
    if ntip: out.insert(0,(0.0,tipc/ntip))
    out.reverse()
    return out
