"""Offline modular stern study. Never reads or writes Unity Assets."""
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import stern_paddle_astra_v8 as ship

ss = ship.ss
ROOT = HERE.parents[1]
OUT = ROOT / 'art-staging/modular-ship-study-v1'
SOURCE = HERE / 'source/stern-paddle-astra-v8.blend'
AXLE = ss.S(ship.AXLE)
OUT.mkdir(parents=True, exist_ok=True)


def make(name, builder):
    obj = ship.mesh(name, builder)
    for face in obj.data.polygons:
        face.use_smooth = False
    return obj


def rotor(radius, reinforced):
    b = ss.Builder()
    hw = ship.HW
    b.cyl((-hw-.22, 0, 0), (hw+.22, 0, 0), .20, .20, 12, 'iron2')
    b.cyl((-hw+.16, 0, 0), (hw-.16, 0, 0), .66, .66, 16, 'iron2')
    for sign in [-1, 1]:
        x = sign * (hw-.13)
        ship.ring(b, x, radius-.02, radius-.37, .28, 'rim', sides=24)
        b.cyl((sign*(hw-.04), 0, 0), (sign*(hw+.21), 0, 0), .42, .34, 12, 'iron')
        for i in range(8):
            a = math.tau*i/8
            b.beam((x, .33*math.sin(a), .33*math.cos(a)),
                   (x, (radius-.20)*math.sin(a), (radius-.20)*math.cos(a)),
                   .22 if not reinforced else .25, 'rim')
        if reinforced:
            # A narrow facing sits against the outside of each timber rim.
            ship.ring(b, sign*(hw+.035), radius-.025, radius-.16, .05, 'iron', sides=24)
            for i in range(8):
                a = math.tau*i/8
                y, z = (radius-.09)*math.sin(a), (radius-.09)*math.cos(a)
                b.cyl((sign*(hw+.06), y, z), (sign*(hw+.095), y, z), .055, .055, 6, 'bolt')
    for i in range(12):
        a = math.tau*i/12
        e = Vector((0, math.sin(a), math.cos(a)))
        n = Vector((0, math.cos(a), -math.sin(a)))
        pts = [tuple(e*r+n*t+Vector((x, 0, 0)))
               for r in [radius-.55, radius] for x in [-hw, hw] for t in [-.085, .085]]
        ids = [(0,1,3,2), (4,5,7,6), (0,1,5,4), (2,3,7,6), (0,2,6,4), (1,3,7,5)]
        b.solid([[pts[k] for k in f] for f in ids], 'blade' if i % 3 else 'wood3')
    obj = make('Rotor', b)
    obj.location = AXLE
    obj['spin_axis'] = 'local Y'
    return obj


def empty(name, location, size=.3):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    o.location = location
    o.empty_display_size = size
    return o


def load_base(large=False):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    bpy.context.scene.frame_set(1)
    for o in list(bpy.data.objects):
        if o.name in {'Paddle_Rotor', 'Paddle_Frame', 'WheelModuleSocket'}:
            bpy.data.objects.remove(o, do_unlink=True)
    if not large:
        return
    for name in ['Hull_Shell', 'Rails_Teal', 'Hull_Ironwork', 'Plank_Seams',
                 'Housing_Lid', 'Housing_Ironwork', 'Housing_AccessPanel']:
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    ship.WF = -8.40
    ship.HF = -8.05
    old_housing = ship.housing
    ship.housing = lambda z, x: ship.deck(z)+(old_housing(z,x)-ship.deck(z))*1.50
    old_casing = ship.casing_solid
    def raised_casing(*args, **kwargs):
        o = old_casing(*args, **kwargs)
        for v in o.data.vertices:
            if v.co.z > 1.76:
                v.co.z = 1.76+(v.co.z-1.76)*1.75
        o.data.update()
        return o
    ship.casing_solid = raised_casing
    def enlarged_hull(shell):
        body = raised_casing('CasingBody', 2.90, ship.ZT+.06, ship.HF, .46,
                             [1.67,1.94,2.21,2.48], ['wood','wood2','wood'])
        ship.boolean_into(shell, body, 'UNION')
        b = ss.Builder()
        cross = [(f*ship.BEAM[0],ship.housing(ship.ZT,f*ship.BEAM[0])) for f in ship.FRAC]
        for (xa,ya),(xb,yb) in zip(cross,cross[1:]):
            q = [(xa,1.66,ship.ZT-.025),(xb,1.66,ship.ZT-.025),
                 (xb,yb,ship.ZT-.025),(xa,ya,ship.ZT-.025)]
            v = [(x,y,ship.ZT+.22) for x,y,z in q]
            b.solid([q,v,[q[0],q[1],v[1],v[0]],[q[1],q[2],v[2],v[1]],
                     [q[2],q[3],v[3],v[2]],[q[3],q[0],v[0],v[3]]], 'wood2')
        ship.boolean_into(shell, make('TransomCrown', b), 'UNION')
        b = ss.Builder()
        b.box(-ship.OPEN,ship.OPEN,-4,2.72,ship.ZT-3,ship.WF,'dark')
        ship.boolean_into(shell, make('WheelPocket',b), 'DIFFERENCE')
        shell.name = 'Hull_Shell'
        bm=bmesh.new(); bm.from_mesh(shell.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
        bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.00005)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(shell.data); bm.free()
        return shell
    ship.finish_hull = enlarged_hull
    ship.hull()
    ship.fittings()
    housing = ship.housing_fittings()
    for o in housing:
        if o.name == 'Housing_Lid':
            continue
        for v in o.data.vertices:
            if v.co.z > 1.76:
                v.co.z = 1.76+(v.co.z-1.76)*1.75
        o.data.update()


def carrier(radius):
    b = ss.Builder()
    hw = ship.HW
    top = radius+.18
    for s in [-1,1]:
        x = s*(hw+.27)
        b.box(x-.15,x+.15,-.68,top-.06,-.86,-.60,'iron')
        b.box(x-.12,x+.12,-.16,.16,-.65,.06,'iron')
        b.cyl((s*(hw+.06),0,0),(s*(hw+.33),0,0),.43,.43,12,'iron2')
        for y in [-.52,top-.19]:
            b.cyl((x,y,-.86),(x,y,-.94),.12,.12,8,'bolt')
    b.box(-hw-.19,hw+.19,top,top+.18,-.86,-.60,'iron')
    o=make('Carrier',b)
    o.location=AXLE
    return o


def partial_deck():
    b=ss.Builder()
    # Authoring helper coordinates: transverse, up, bow. Walking plane is flat.
    for i in range(10):
        x=-3.85+i*.77
        b.box(x,x+.754,4.20,4.42,-6.25,-1.30,'deck' if i%3 else 'deck2')
    for x in [-3.48,3.48]:
        for z in [-5.90,-1.70]:
            b.box(x-.14,x+.14,1.76,4.20,z-.14,z+.14,'wood')
            b.box(x-.18,x+.18,1.76,1.93,z-.18,z+.18,'iron')
        b.box(x-.13,x+.13,3.94,4.20,-6.20,-1.33,'wood')
    for z in [-5.90,-1.70]:
        b.box(-3.82,3.82,3.94,4.20,z-.13,z+.13,'wood')
    for x in [-3.48,3.48]:
        b.beam((x,3.30,-5.90),(x,3.99,-5.12),.18,'wood')
        b.beam((x,3.30,-1.70),(x,3.99,-2.48),.18,'wood')
    for x in [-3.79,3.79]:
        b.box(x-.09,x+.09,4.16,4.37,-6.24,-1.31,'teal')
    b.box(-3.88,3.88,4.16,4.37,-6.25,-6.09,'teal')
    b.box(-3.88,1.93,4.16,4.37,-1.47,-1.30,'teal')
    for x in [-3.78,3.78]:
        for z in [-6.10,-3.8,-1.48]:
            b.box(x-.085,x+.085,4.42,5.14,z-.085,z+.085,'wood')
            b.box(x-.11,x+.11,5.08,5.18,z-.11,z+.11,'iron')
        b.box(x-.105,x+.105,4.99,5.12,-6.15,-1.42,'teal')
    b.box(-3.82,3.82,4.99,5.12,-6.20,-6.01,'teal')
    b.box(-3.82,1.92,4.99,5.12,-1.50,-1.31,'teal')
    # Open stair on the starboard edge, with actual treads and two stringers.
    for i in range(8):
        z=-1.28+i*.40
        y=4.42-(i+1)*(2.66/8)
        b.box(2.0,3.60,y-.13,y,z,z+.40,'deck')
    for x in [2.02,3.58]:
        b.beam((x,4.08,-1.26),(x,1.62,1.86),.16,'wood')
        b.beam((x,5.0,-1.26),(x,2.45,1.86),.12,'teal')
        for y,z in [(4.08,-1.26),(1.76,1.86)]:
            b.box(x-.06,x+.06,y,y+.88,z-.06,z+.06,'wood')
    deck=make('PartialDeck_Aft',b)
    for side in [-1,1]:
        for i,x in enumerate([-4.95,-2.80]):
            o=empty(f'CannonSocket_{"Port" if side>0 else "Starboard"}_{i+1}',(x,side*2.92,4.42))
            o['clearance_box_local']=[1.8,1.55,1.65]
            o['status']='layout placeholder; not gameplay validated'
    empty('CrewPassage_Upper',(-3.6,0,4.42),.65)
    helm=bpy.data.objects['Helm']
    helm.data.transform(Matrix.Translation((2.22,0,2.66)))
    return deck


def bvh(o, matrix=None):
    o.data.calc_loop_triangles()
    m=matrix if matrix is not None else o.matrix_world
    return BVHTree.FromPolygons([m@v.co for v in o.data.vertices],
        [tuple(t.vertices) for t in o.data.loop_triangles],all_triangles=True)


def verify(rot, mount, radius):
    bpy.context.view_layer.update()
    counts={o.name:ship.check(o) for o in bpy.context.scene.objects if o.type=='MESH'}
    fixed=[o for o in bpy.context.scene.objects if o.type=='MESH' and o not in {rot,mount}]
    trees={o.name:bvh(o) for o in fixed}
    mount_tree=bvh(mount)
    rot.data.calc_loop_triangles()
    radial=[max(math.hypot(rot.data.vertices[v].co.x,rot.data.vertices[v].co.z)
                for v in t.vertices) for t in rot.data.loop_triangles]
    hits=[]
    for degree in range(0,360,5):
        test=bvh(rot,Matrix.Translation(AXLE)@Matrix.Rotation(math.radians(degree),4,'Y'))
        for name,tree in trees.items():
            overlap=tree.overlap(test)
            if overlap: hits.append({'angle':degree,'object':name,'pairs':len(overlap)})
        overlap=[p for p in mount_tree.overlap(test) if radial[p[1]]>.55]
        if overlap: hits.append({'angle':degree,'object':mount.name,'pairs':len(overlap)})
    return {'meshes':counts,'total_triangles':sum(c['triangles'] for c in counts.values()),
            'rotation_samples':72,'bearing_contact_excluded_radius':.55,
            'surface_intersections':hits,
            'swept_radius':max(math.hypot(v.co.x,v.co.z) for v in rot.data.vertices),
            'pocket_ceiling':2.72 if radius>2 else 2.17,
            'notes':'BVH surface tests sampled every 5 degrees; intentional axle/bearing contact excluded. Not a physics or watertightness simulation.'}


def render(name, out, eye, target, scale, resolution=(1300,1050)):
    scene=bpy.context.scene
    ss.setup_render(scene)
    scene.display.shading.show_shadows=False
    scene.display.shading.cavity_type='BOTH'
    scene.display.shading.studiolight_rotate_z=.5
    scene.view_settings.exposure=.50
    scene.world.color=ss.srgb_to_linear(ss._hex('#99B5C4'))
    scene.display.shading.background_type='WORLD'
    cam=scene.camera
    cam.data.type='ORTHO'; cam.data.ortho_scale=scale
    cam.location=eye
    cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x,scene.render.resolution_y=resolution
    scene.render.resolution_percentage=100
    scene.render.filepath=str(out/(name+'.png'))
    bpy.ops.render.render(write_still=True)


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    variant=args[0]
    large=variant.startswith('C')
    upper=variant.endswith('deck')
    out=OUT/variant; out.mkdir(parents=True,exist_ok=True)
    load_base(large)
    radius=2.15 if large else 1.62
    rot=rotor(radius,variant!='A-timber')
    mount=carrier(radius)
    socket=empty('WheelModuleSocket',AXLE)
    socket['mount_standard']='M1-L' if large else 'M1'
    socket['rotor_width']=4.16
    socket['radius_limit']=2.17 if large else 1.64
    empty('WheelServiceAccess',(-7.30,0,1.76),.5)
    if upper: partial_deck()
    for o in bpy.context.scene.objects:
        if o.type=='MESH':
            for p in o.data.polygons: p.use_smooth=False
    report=verify(rot,mount,radius)
    report.update({'variant':variant,'blender_version':bpy.app.version_string,
                   'socket_blender':list(AXLE),'socket_game_convention':[0,.35,-10.83],
                   'mount':socket['mount_standard']})
    manifest={'variant':variant,'coordinates':'Blender +X bow, +Y port, +Z up; dimensions in unchanged V8 authoring units',
              'rotor':{'file':'models/Rotor.fbx','origin':'axle centre',
                       'installed_blender_position':list(AXLE),'game_position':[0,.35,-10.83],
                       'blender_spin_axis':'Y','game_spin_axis':'X','nominal_radius':radius,
                       'swept_radius':report['swept_radius'],'paddle_width':4.16},
              'carrier':{'file':'models/Carrier.fbx','origin':'axle centre','mount':socket['mount_standard']},
              'fixed_meshes':'Hull-origin local, at zero. Rotor and Carrier alone must be translated to the socket.',
              'sockets':{o.name:{'position_blender':list(o.location),
                                'metadata':{k:v.to_list() if hasattr(v,'to_list') else v for k,v in o.items()}}
                         for o in bpy.context.scene.objects if o.type=='EMPTY'},
              'integration_status':'OFFLINE REVIEW ONLY. No Unity import, scale calibration, colliders, LODs, navigation or balance included.'}
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2))
    (out/'validation.json').write_text(json.dumps(report,indent=2))
    print('VALIDATION',json.dumps({k:v for k,v in report.items() if k!='meshes'}),flush=True)
    assert not report['surface_intersections'], 'Wheel collision; inspect validation.json'
    assert all(c['degenerate_faces']==0 and c['overconnected_edges']==0 and c['boundary_edges']==0
               for c in report['meshes'].values()), 'Invalid mesh; inspect validation.json'
    exports=out/'models'; exports.mkdir(exist_ok=True)
    for o in list(bpy.context.scene.objects):
        if o.type=='MESH': ss.export_fbx(o,str(exports/(o.name+'.fbx')))
    render('stern',out,(-24,-15,12),(-9.7,0,1.05),12.7)
    render('rear',out,(-40,0,1.5),(-10.8,0,1.5),11.4,(1100,1000))
    render('side',out,(-3,-45,1.6),(0,0,1.6),27.5,(1500,650))
    render('ship',out,(-29,-34,25),(0,0,1.5),30.5,(1500,1100))
    rot.rotation_euler=(0,0,0); rot.keyframe_insert('rotation_euler',frame=1)
    rot.rotation_euler.y=math.tau; rot.keyframe_insert('rotation_euler',frame=121)
    action=rot.animation_data.action
    # Blender 4.x action API; the saved source and this study use this runtime.
    curves=[]
    if hasattr(action,'fcurves'):
        curves=list(action.fcurves)
    else:
        for layer in action.layers:
            for strip in layer.strips:
                bag=strip.channelbag(rot.animation_data.action_slot)
                if bag: curves.extend(bag.fcurves)
    for curve in curves:
        for key in curve.keyframe_points: key.interpolation='LINEAR'
    bpy.context.scene.frame_end=120
    bpy.context.scene.frame_set(1)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.shading.color_type='VERTEX'
                area.spaces.active.region_3d.view_rotation=bpy.context.scene.camera.rotation_euler.to_quaternion()
                area.spaces.active.region_3d.view_distance=30
                area.spaces.active.region_3d.view_location=(0,0,1.5)
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'ship-study.blend'))


if __name__=='__main__': main()
