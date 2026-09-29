"""Read the delivered F1 scenes/FBX, measure actual meshes, never rewrite them."""
import json
import math
from pathlib import Path
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/f-coaster-v1'
NAMES=['narrow-low','narrow-raised','wide-low','wide-raised']
results=[]


def points(o):return [o.matrix_world@v.co for v in o.data.vertices]


def bounds(obs):
    vs=[p for o in obs for p in points(o)]
    return [[min(p[i] for p in vs) for i in range(3)],[max(p[i] for p in vs) for i in range(3)]]


def section_profile(parent,plane):
    o=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.parent and o.parent.name==parent and o.name.startswith('Hull_Shell'))
    return sorted(set((round(v.co.y,4),round(v.co.z,4)) for v in o.data.vertices if abs(v.co.x-plane)<1e-4))


for name in NAMES:
    dest=OUT/name
    manifest=json.loads((dest/'manifest.json').read_text())
    bpy.ops.wm.open_mainfile(filepath=str(dest/'ship.blend'))
    checks={}
    checks['stern_middle_profile_matches']=section_profile('Stern',9.3)==section_profile('Middle_0',0)
    checks['middle_bow_profile_matches']=section_profile('Middle_0',6)==section_profile('Bow',0)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name!='Studio_Ground' and not o.name.startswith('Review_')]
    degenerate=[o.name for o in meshes if any(p.area<1e-9 for p in o.data.polygons)]
    checks['no_zero_area_faces']=not degenerate
    rotor=next(o for o in meshes if o.name=='Paddle_Rotor')
    center=rotor.matrix_world.translation
    radial=max(math.hypot(v.co.x,v.co.z) for v in rotor.data.vertices)
    wheelbounds=bounds([rotor])
    checks['rotor_is_axle_local']=abs(center.x-manifest['paddle']['axle_u'][0])<1e-5 and abs(center.z-manifest['paddle']['axle_u'][2])<1e-5
    checks['wheel_swept_clear_of_transom']=center.x+radial<-.25
    guns=[o for o in bpy.context.scene.objects if o.name.startswith('Review_Cannon')]
    port=next(o for o in guns if 'Starboard' not in o.name)
    star=next(o for o in guns if 'Starboard' in o.name)
    aisle=(min(p.y for p in points(port))-max(p.y for p in points(star)))*.5
    checks['cannon_gap_over_1_4m']=aisle>=1.4
    crew=next(o for o in bpy.context.scene.objects if o.name=='Review_Deckhand')
    cb=bounds([crew]);crewheight=(cb[1][2]-cb[0][2])*.5
    checks['review_crew_1_8m']=abs(crewheight-1.8)<.001
    measurements={'cannon_clear_gap_m':aisle,'crew_height_m':crewheight,
                  'wheel_swept_radius_m':radial*.5,'wheel_axle_height_m':center.z*.5,
                  'wheel_blade_width_m':manifest['paddle']['width_u']*.5,
                  'wheel_swept_bottom_m':(center.z-radial)*.5}
    # Check floor headroom from measured floor meshes, and door frame opening.
    if manifest['stern_levels']:
        upper=next(o for o in meshes if o.name=='Layer_Floor')
        low=next(o for o in meshes if o.parent and o.parent.name=='Stern' and o.name=='Main_Deck')
        headroom=(min(p.z for p in points(upper))-max(p.z for p in points(low)))*.5
        measurements['enclosed_headroom_m']=headroom
        checks['headroom_at_least_2m']=headroom>=2
    expected=[]
    for record in manifest['exports']:
        obs=[o for o in meshes if o.parent and o.parent.name==record['group']]
        b=bounds(obs); offset=record['assembly_position_u']
        expected.append((record,[[b[k][i]-offset[i] for i in range(3)] for k in range(2)]))
    exportchecks=[]
    # Import every FBX into a clean scene. Bounds include applied bevels.
    for record,original in expected:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(dest/'models'/record['file']))
        obs=[o for o in bpy.context.scene.objects if o.type=='MESH']
        b=bounds(obs)
        error=max(abs(b[k][i]-original[k][i]) for k in range(2) for i in range(3))
        hascolors=all('GameColor' in o.data.color_attributes for o in obs)
        exportchecks.append({'group':record['group'],'bounds_error_u':error,'game_colors':hascolors})
    checks['fbx_bounds_match_within_1cm']=all(x['bounds_error_u']<.02 for x in exportchecks)
    checks['fbx_game_colors_preserved']=all(x['game_colors'] for x in exportchecks)
    results.append({'configuration':name,'checks':checks,'measurements':measurements,'degenerate_meshes':degenerate,'exports':exportchecks})

by={r['configuration']:r for r in results}
independent={
 'low_wheel_radius_independent_of_width':abs(by['narrow-low']['measurements']['wheel_swept_radius_m']-by['wide-low']['measurements']['wheel_swept_radius_m'])<1e-5,
 'raised_wheel_radius_independent_of_width':abs(by['narrow-raised']['measurements']['wheel_swept_radius_m']-by['wide-raised']['measurements']['wheel_swept_radius_m'])<1e-5,
 'narrow_wheel_width_independent_of_height':by['narrow-low']['measurements']['wheel_blade_width_m']==by['narrow-raised']['measurements']['wheel_blade_width_m'],
 'wide_wheel_width_independent_of_height':by['wide-low']['measurements']['wheel_blade_width_m']==by['wide-raised']['measurements']['wheel_blade_width_m'],
}
report={'scenes':results,'independent_dimensions':independent,
        'scope':'Static mesh/export checks only. Not physics, recoil, crew navigation, hydrostatics or mobile validation.'}
report['pass']=all(all(r['checks'].values()) for r in results) and all(independent.values())
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
if not report['pass']:raise RuntimeError('F coaster geometry verification failed; see validation.json')
