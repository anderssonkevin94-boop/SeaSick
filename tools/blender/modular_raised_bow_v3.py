"""Starboard access and continuous existing hull straps; preserves V2 outputs."""
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_raised_bow_v2 as base

base.OUT=HERE.parents[1]/'art-staging/modular-raised-bow-v3'/base.MODE
base.OUT.mkdir(parents=True,exist_ok=True)
base.ACCESS_Y=-2.15
original_build=base.build


def build():
    bow,parts,moving=original_build()
    obj=parts['Hull_Ironwork']
    bm=bmesh.new();bm.from_mesh(obj.data);bm.normal_update()
    caps=[f for f in bm.faces if f.normal.z>.9 and min(v.co.z for v in f.verts)>1.7
          and max(v.co.z for v in f.verts)<2.4 and abs(f.calc_center_median().y)>1
          and .2<max(v.co.x for v in f.verts)-min(v.co.x for v in f.verts)<.65]
    assert len(caps)==2,('Expected port and starboard brace tips',len(caps))
    bolt=base.ss.Builder()
    for cap in caps:
        c=cap.calc_center_median().copy()
        result=bmesh.ops.extrude_face_region(bm,geom=[cap])
        verts=[v for v in result['geom'] if isinstance(v,bmesh.types.BMVert)]
        for v in verts:
            v.co.x+=base.old.rake(v.co.x)
            v.co.z=base.H-.04
        bmesh.ops.delete(bm,geom=[cap],context='FACES_ONLY')
        s=1 if c.y>0 else -1
        for h in [2.65,3.85]:
            x=c.x+base.old.rake(c.x)*(h-c.z)/(base.H-.04-c.z)
            y=c.y+s*.045
            bolt.cyl(base.fore.point(x,y,h),base.fore.point(x,y+s*.08,h),.08,.08,6,'bolt')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free();obj.data.update()
    parts['UpperBraceBolts']=base.mesh('UpperBraceBolts',bolt,bow)
    bpy.context.view_layer.update()
    chimney=bpy.data.objects['Chimney']
    chimney.location.x=min(chimney.location.x,bow.location.x-1.65)
    bpy.context.view_layer.update()
    door=parts['Door'];angle=door.rotation_euler.z;tests=[]
    for degrees in range(0,111,5):
        door.rotation_euler.z=math.radians(degrees);bpy.context.view_layer.update()
        hits=base.family.wheel_study.bvh(door).overlap(base.family.wheel_study.bvh(chimney))
        assert not hits,('Door/chimney collision',degrees)
        tests.append(degrees)
    door.rotation_euler.z=angle;bpy.context.view_layer.update()
    bow['door_chimney_clearance_samples']=len(tests)
    return bow,parts,moving


base.build=build
if __name__=='__main__':
    base.main()
    path=base.OUT/'manifest.json'
    manifest=json.loads(path.read_text())
    manifest['id']='RaisedBowFull_'+base.MODE+'_v3'
    manifest['access_offset']='Door, internal chamber, ladder and hatch moved to starboard, Y=-2.15.'
    manifest['brace_revision']='Original lower port/starboard iron straps extruded continuously to upper deck; no overlay caps.'
    manifest['door_clearance']='23 sampled angles, 0 through 110 degrees, no chimney surface intersections on both assemblies.'
    path.write_text(json.dumps(manifest,indent=2))
