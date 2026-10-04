# Raw FBX diff (no Blender scene): models/Lcl/parents, vertices, polygon winding, normals, colours.
import sys
from io_scene_fbx import parse_fbx
A,B=sys.argv[sys.argv.index("--")+1:][:2]
def load(path):
    root,_=parse_fbx.parse(path)
    objs=[c for c in root.elems if c.id==b"Objects"][0]; con=[c for c in root.elems if c.id==b"Connections"][0]
    byid={}; models={}; geoms={}
    for c in objs.elems:
        nm=c.props[1].split(b"\x00")[0].decode(); byid[c.props[0]]=(c.id,nm)
        if c.id==b"Model":
            p={}
            for s in c.elems:
                if s.id==b"Properties70":
                    for q in s.elems:
                        k=q.props[0].decode()
                        if k.startswith("Lcl"): p[k]=tuple(q.props[4:])
            models[nm]=p
        elif c.id==b"Geometry": geoms[nm]=c
    parent={}
    for c in con.elems:
        if c.props[0]==b"OO" and c.props[1] in byid:
            ch=byid[c.props[1]]; pa=byid.get(c.props[2],(b"",'ROOT'))
            if ch[0]==b"Model": parent[ch[1]]=pa[1]
    return models,geoms,parent
def sub(e,i): 
    r=[s for s in e.elems if s.id==i]; return r[0] if r else None
def layer(g,lid):
    L=sub(g,lid)
    if L is None: return None
    vals=L.elems; d={s.id:s.props[0] for s in vals}
    key=[k for k in d if k in(b"Normals",b"Colors")][0]
    data=d[key]; w=3 if key==b"Normals" else 4
    idx=d.get(b"NormalsIndex",d.get(b"ColorIndex"))
    rows=[tuple(data[i*w:(i+1)*w]) for i in range(len(data)//w)]
    return [rows[i] for i in idx] if idx is not None else rows
def polys(g):
    pvi=sub(g,b"PolygonVertexIndex").props[0]; out=[]; cur=[]; start=0
    for k,i in enumerate(pvi):
        if i<0: cur.append(~i); out.append((start,cur)); cur=[]; start=k+1
        else: cur.append(i)
    return out
mA,gA,pA=load(A); mB,gB,pB=load(B); bad=0
if set(mA)!=set(mB): print("MODEL NAMES DIFFER", set(mA)^set(mB)); bad+=1
for n in sorted(mA):
    if n in mB:
        for k in set(mA[n])|set(mB[n]):
            dflt=(1,1,1) if "Scaling" in k else (0,0,0); va=mA[n].get(k,dflt); vb=mB[n].get(k,dflt)
            dv=max(abs(x-y) for x,y in zip(va,vb))
            if dv>1e-4: print("LCL",n,k,va,vb); bad+=1
        if pA.get(n)!=pB.get(n): print("PARENT",n,pA.get(n),pB.get(n)); bad+=1
for n in sorted(gA):
    a=gA[n]; b=gB.get(n)
    if b is None: print("MISSING GEOM",n); bad+=1; continue
    va=sub(a,b"Vertices").props[0]; vb=sub(b,b"Vertices").props[0]
    if len(va)!=len(vb): print("VCOUNT",n,len(va),len(vb)); bad+=1; continue
    dv=max(abs(x-y) for x,y in zip(va,vb))
    PA=polys(a); PB=polys(b); nA=layer(a,b"LayerElementNormal"); nB=layer(b,b"LayerElementNormal")
    cA=layer(a,b"LayerElementColor"); cB=layer(b,b"LayerElementColor")
    def uvl(g):
        L=sub(g,b"LayerElementUV")
        if L is None: return None
        d={x.id:x.props[0] for x in L.elems}; data=d[b"UV"]; idx=d.get(b"UVIndex")
        rows=[tuple(data[i*2:i*2+2]) for i in range(len(data)//2)]
        return [rows[i] for i in idx] if idx is not None else rows
    uA=uvl(a); uB=uvl(b); ubad=0
    def mats(g):
        L=sub(g,b"LayerElementMaterial")
        return None if L is None else list([x for x in L.elems if x.id==b"Materials"][0].props[0])
    mA=mats(a); mB=mats(b)
    mbad=0 if (mA is None)==(mB is None) else 1
    same=rev=other=nbad=cbad=0
    for (sa,qa),(sb,qb) in zip(PA,PB):
        ka=len(qa)
        if sorted(qa)!=sorted(qb): other+=1; continue
        rot=[qa[i:]+qa[:i] for i in range(ka)]
        if qb in rot: same+=1; flip=False
        elif qb[::-1] in rot: rev+=1; flip=True
        else: other+=1; continue
        for j,vi in enumerate(qb):
            ja=qa.index(vi)
            na=nA[sa+ja]; nb=nB[sb+j]; d=sum(x*y for x,y in zip(na,nb))
            if (d<0.999 and not flip) or (d>-0.999 and flip): nbad+=1
            if uA is not None and (uB is None or max(abs(x-y) for x,y in zip(uA[sa+ja],uB[sb+j]))>1e-5): ubad+=1
            if cA is not None and max(abs(x-y) for x,y in zip(cA[sa+ja],cB[sb+j]))>1/255: cbad+=1
    if mA and mB and len(mA)>1 and len(mB)>1 and mA!=mB: mbad=sum(1 for x,y in zip(mA,mB) if x!=y)
    if len(PA)!=len(PB) or other or nbad or cbad or ubad or mbad or dv>1e-4: bad+=1
    print(f"GEOM {n}: verts {len(va)//3} maxdelta {dv:.2e} polys {len(PA)}/{len(PB)} same {same} reversed {rev} other {other} normal_mismatch {nbad} colour_mismatch {cbad} uv_mismatch {ubad} mat_mismatch {mbad}")
def types(p):
    r,_=parse_fbx.parse(p); o=[c for c in r.elems if c.id==b"Objects"][0]
    from collections import Counter
    c=Counter(e.id.decode() for e in o.elems)
    c.update(("Material:"+e.props[1].split(b"\x00")[0].decode()) for e in o.elems if e.id in (b"Material",b"AnimationStack")); return c
tA=types(A); tB=types(B)
if tA!=tB: print("OBJECT TYPES DIFFER", dict(tA-tB), dict(tB-tA)); bad+=1
print("DIFF_BAD", bad)
