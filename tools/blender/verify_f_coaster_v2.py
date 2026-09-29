"""Independent F2 inspection of saved geometry, including a rotated wheel sweep."""
import argparse
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/f-coaster-v2'


def evaluated(o):
    ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh()
    points=[o.matrix_world@v.co for v in me.vertices]
    faces=[list(p.vertices) for p in me.polygons]
    ev.to_mesh_clear()
    return points,faces


def bbox(vs):return [[min(p[i] for p in vs) for i in range(3)],[max(p[i] for p in vs) for i in range(3)]]


def inside_shape(p,radius,width):
    return math.hypot(p.x,p.z)<radius and abs(p.y)<width/2


def inspect(name):
    dest=OUT/name;m=json.loads((dest/'manifest.json').read_text())
    bpy.ops.wm.open_mainfile(filepath=str(dest/'ship.blend'));bpy.context.view_layer.update()
    wheel=bpy.data.objects['Paddle_Rotor'];axle=wheel.matrix_world.translation.copy()
    vertices,faces=evaluated(wheel);local=[p-axle for p in vertices]
    r=max(math.hypot(p.x,p.z) for p in local)
    # Measure projection from saved geometry, independently of manifest formula.
    projection=(r-axle.x)/(2*r)
    measured={'rotor_swept_radius_u':r,'axle_u':list(axle),'stern_projection_fraction':projection,
              'wheel_tip_z_u':axle.z-r,'rotor_total_width_u':max(p.y for p in local)-min(p.y for p in local)}
    ship_names={n for record in m['exports'] for n in record['parts']}
    static=[o for o in bpy.context.scene.objects if o.name in ship_names and o.type=='MESH' and o!=wheel]
    colliders=[];sv=[];sf=[];face_owner=[]
    wb=bbox(vertices)
    for o in static:
        vs,fs=evaluated(o);bb=bbox(vs)
        if any(bb[0][i]>wb[1][i]+.02 or bb[1][i]<wb[0][i]-.02 for i in range(3)):continue
        offset=len(sv);sv.extend(vs);sf.extend([[j+offset for j in f] for f in fs]);face_owner.extend([o.name]*len(fs))
        colliders.append(o.name)
    static_bvh=BVHTree.FromPolygons(sv,sf,all_triangles=False,epsilon=0.00001)
    hits={}
    for degrees in range(0,360,10):
        rot=Matrix.Rotation(math.radians(degrees),3,'Y')
        pts=[axle+rot@p for p in local]
        rb=BVHTree.FromPolygons(pts,faces,all_triangles=False,epsilon=.00001)
        for _,face in rb.overlap(static_bvh):
            hits.setdefault(face_owner[face],set()).add(degrees)
    measured['rotation_intersections']={k:sorted(v) for k,v in hits.items()}
    measured['checked_static_parts']=colliders
    # Section shell joins are compared directly, without using authoring functions.
    def profile(parent,x):
        out=[]
        for o in static:
            if not o.parent or o.parent.name!=parent or not(o.name.startswith('Hull_') or o.name.startswith('Keel')):continue
            out.extend((round(v.co.y,4),round(v.co.z,4)) for v in o.data.vertices if abs(v.co.x-x)<.0001)
        return sorted(set(out))
    lengths=m['section_lengths_u']
    joints=[profile('Stern',lengths['stern'])==profile('Middle_0',0),profile('Middle_0',lengths['middle'])==profile('Bow',0)]
    cannon_bounds=[]
    for cannon_name in m['cannons']:
        ro=bpy.data.objects[cannon_name]
        vs=[p for o in ro.children_recursive if o.type=='MESH' for p in evaluated(o)[0]]
        cannon_bounds.append((cannon_name,bbox(vs)))
    port=next(b for n,b in cannon_bounds if 'Port' in n)
    star=next(b for n,b in cannon_bounds if 'Starboard' in n)
    measured['between_cannon_meshes_m']=(port[0][1]-star[1][1])*.5
    checks={'roughly_half_wheel_projects':.45<projection<.55,'section_join_profiles_match':all(joints),
            'rotor_no_static_intersections_in_36_positions':not hits,
            'actual_cannon_clear_gap_over_1_4m':measured['between_cannon_meshes_m']>1.4}
    expected=[]
    for record in m['exports']:
        vs=[p for part in record['parts'] for p in evaluated(bpy.data.objects[part])[0]]
        b=bbox(vs);pos=record['assembly_position_u']
        expected.append((record,[[b[k][i]-pos[i] for i in range(3)] for k in range(2)]))
    export_results=[]
    for record,bb in expected:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(dest/'models'/record['file']))
        obs=[o for o in bpy.context.scene.objects if o.type=='MESH']
        out=bbox([p for o in obs for p in evaluated(o)[0]])
        error=max(abs(out[k][i]-bb[k][i]) for k in range(2) for i in range(3))
        export_results.append({'file':record['file'],'bounds_error_u':error,
                               'game_colors':all('GameColor' in o.data.color_attributes for o in obs)})
    measured['fbx_reimport']=export_results
    checks['fbx_evaluated_bounds_match']=all(e['bounds_error_u']<.002 for e in export_results)
    checks['fbx_game_colors_survive']=all(e['game_colors'] for e in export_results)
    return {'configuration':name,'checks':checks,'measured':measured}


def main():
    p=argparse.ArgumentParser();p.add_argument('--names',nargs='+',default=['narrow-raised','narrow-low','wide-low','wide-raised'])
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    records=[inspect(n) for n in args.names]
    independence={}
    if len(records)==4:
        by={r['configuration']:r['measured'] for r in records}
        for height in ['low','raised']:
            independence[height+'_radius_independent_of_width']=abs(by['narrow-'+height]['rotor_swept_radius_u']-by['wide-'+height]['rotor_swept_radius_u'])<.00001
        for width in ['narrow','wide']:
            independence[width+'_width_independent_of_height']=abs(by[width+'-low']['rotor_total_width_u']-by[width+'-raised']['rotor_total_width_u'])<.00001
    report={'scenes':records,'passed':all(all(r['checks'].values()) for r in records),
            'scope':'Static saved geometry; wheel sampled every 10 degrees. Not full physics, navigation, waterline or load certification.'}
    report['independent_dimensions']=independence
    report['passed']=report['passed'] and all(independence.values())
    path=OUT/('validation.json' if len(args.names)==4 else 'validation-partial.json')
    path.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
    if not report['passed']:raise RuntimeError('F2 validation failed; see report')


if __name__=='__main__':main()
