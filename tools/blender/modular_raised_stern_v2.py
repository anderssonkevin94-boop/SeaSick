"""Twin inset quarterdeck stairs, preserving the V1 stern envelope."""
import json
import bmesh
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import modular_raised_stern_v1 as old

base,fore,ship,ss,family=old.base,old.fore,old.ship,old.ss,old.family
old.OUT=old.HERE.parents[1]/'art-staging/modular-raised-stern-v2'/old.MODE
old.OUT.mkdir(parents=True,exist_ok=True)
original_build=old.build


def build():
    stern,parts,moving=original_build()
    shell=parts['Hull_Shell'];L,H=old.L,old.H
    width=ship.interp(family.CUT_A,ship.BEAM)
    outer=width-.24;inner=outer-1.18
    start,end=6.06,L-.04
    bm=bmesh.new();bm.from_mesh(shell.data)
    edges=[e for e in bm.edges if e.is_boundary and all(abs(v.co.x-L)<.0001 for v in e.verts)]
    bmesh.ops.holes_fill(bm,edges=edges,sides=0)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(shell.data);bm.free()
    steps=ss.Builder();guards=ss.Builder();nosings=ss.Builder()
    for sign,label in [(-1,'Starboard'),(1,'Port')]:
        lo,hi=sorted([sign*inner,sign*outer])
        for name in ['Hull_Shell','UpperPlanks','ForwardGuard']:
            b=ss.Builder();fore.box(b,start,L+.3,lo,hi,base.D,H+1.1,'wood2')
            cutter=old.make('StairCut',b,stern);old.normals(cutter)
            ship.boolean_into(parts[name],cutter,'DIFFERENCE')
        # A single watertight stepped solid, with no buried coplanar boxes.
        rise=(H-base.D)/8;run=(end-start)/8
        for i in range(8):
            x=round(end-i*run,5);a=round(end-(i+1)*run,5)
            z=base.D+(i+1)*rise;prev=base.D+i*rise
            ys=[lo+.018,hi-.018]
            for y,hint in zip(ys,[(1,0,0),(-1,0,0)]):
                outline=[(a,base.D),(x,base.D)]
                if i:outline.append((x,prev))
                outline.extend([(x,z),(a,z)])
                center=fore.point((a+x)/2,y,(base.D+z)/2)
                for j,p in enumerate(outline):
                    q=outline[(j+1)%len(outline)]
                    steps.face([center,fore.point(p[0],y,p[1]),fore.point(q[0],y,q[1])],'wood2',hint=hint)
            for xx0,zz0,xx1,zz1,col in [(a,base.D,x,base.D,'wood2'),(x,z,a,z,'deck'),(x,prev,x,z,'wood')]+([(a,z,a,base.D,'wood2')] if i==7 else []):
                steps.face([fore.point(xx0,ys[0],zz0),fore.point(xx1,ys[0],zz1),fore.point(xx1,ys[1],zz1),fore.point(xx0,ys[1],zz0)],col,away=fore.point((a+x)/2,(lo+hi)/2,base.D+.1))
        for i in range(8):
            x=end-i*run;z=base.D+(i+1)*rise
            fore.box(nosings,x-.055,x-.015,lo+.03,hi-.03,z+.002,z+.014,'rim')
        # Deck-height guard protects the inner edge; exits remain open aft.
        y=sign*(inner-.075)
        fore.tube(guards,[(start,y,H+.7),(end,y,H+.7)],.15,.18,'teal')
        for x in [start,7.65,end]:
            fore.box(guards,x-.065,x+.065,y-.065,y+.065,H,H+.66,'wood')
        # Recessed handrail descends with the treads inside the outer wall.
        y=sign*(outer-.10)
        fore.tube(guards,[(start+.20,y,H+.56),(end-.08,y,base.D+rise+.65)],.10,.12,'teal')
        for name,pos in [('Bottom',(end,(lo+hi)/2,base.D)),('Top',(start,(lo+hi)/2,H))]:
            family.empty('SternStair'+label+name,pos,stern)
    bm=bmesh.new();bm.from_mesh(shell.data)
    caps=[f for f in bm.faces if all(abs(v.co.x-L)<.0001 for v in f.verts)]
    bmesh.ops.delete(bm,geom=caps,context='FACES')
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    for _ in range(5):
        redundant=[]
        for v in bm.verts:
            if abs(v.co.x-L)>.0001:continue
            v.co.x=L
            es=[e for e in v.link_edges if e.is_boundary]
            if len(es)==2:
                a=es[0].other_vert(v).co-v.co;b=es[1].other_vert(v).co-v.co
                if a.length>1e-5 and b.length>1e-5 and a.normalized().dot(b.normalized())<-.999999:redundant.append(v)
        if not redundant:break
        bmesh.ops.dissolve_verts(bm,verts=redundant,use_face_split=False,use_boundary_tear=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(shell.data);bm.free()
    for name,b in [('TwinStairs',steps),('StairGuards',guards),('StairNosings',nosings)]:
        parts[name]=old.make(name,b,stern);old.normals(parts[name])
    assert ship.check(parts['TwinStairs'])['boundary_edges']==0
    assert all(v.co.x<=L+.0001 and abs(v.co.y)<width for v in parts['TwinStairs'].data.vertices)
    return stern,parts,moving


old.build=build
if __name__=='__main__':
    old.main()
    path=old.OUT/'manifest.json';manifest=json.loads(path.read_text())
    manifest['id']='RaisedStern_'+old.MODE+'_v2'
    manifest['stairs']={'count':2,'steps_each':8,'clear_width':1.144,'rise':.305,'run':.4,'footprint_length_unchanged':9.3,'direction':'Ascend aft (-X); entrances at existing forward bulkhead.'}
    path.write_text(json.dumps(manifest,indent=2))
