# Harpoon v1 static check: re-imports the two EXPORTED FBXs (not the blend) and checks them against CONTRACT.md
# and the REAL base-coaster bow (kit.json, read-only). Run (see RUN.md):
#   Blender -b --factory-startup --python art-staging/harpoon-v1/check.py
# Writes art-staging/harpoon-v1/export-verification.json + one CHECK line, then checks the gun-lamp (hierarchy, light
# empty, lens behind the muzzle, no clipping, rope clearance via the in-game stem fairlead on the low AND raised bow)
# -> lamp-verification.json + one CHECK_LAMP line.
import bpy, math, json, re
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
import sys; sys.path.insert(0, '/Users/kevinandersson/Desktop/SeaSick/tools/blender'); from check_winding import winding_report
exec(compile(open('/Users/kevinandersson/Desktop/SeaSick/art-staging/harpoon-v1/geo.py').read(), 'geo.py', 'exec'))
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
E = []; R = {'errors': E}
TOL = 0.01

def load(fbx):
    before = set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=HV + '/' + fbx)
    return [o for o in bpy.data.objects if o not in before]
def stem(n): return re.sub(r'\.\d+$', '', n)
def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
def world_tris(objs):
    v = []; f = []
    for o in objs:
        m = o.matrix_world; b = len(v); v += [m @ x.co for x in o.data.vertices]; f += [[b + i for i in p.vertices] for p in o.data.polygons]
    return v, f
def zero_area(o): return sum(1 for p in o.data.polygons if p.area < 1e-7)

EXPECT = {'HarpoonMount.fbx': ('HarpoonMount', {'Swivel': (0, 0, .16), 'Barb_Muzzle': (0, -.80, 1.72), 'Winch_Drum': (0, .42, 1.06), 'Harpooner_Stand': (0, .86, .30), 'Lamp_Light': (-.33, -.60, 1.72)}, 2500),
          'HarpoonBarb.fbx': ('HarpoonBarb', {'Line_Attach': (0, .335, 0)}, 400)}
objs = {}
for fbx, (rootname, empties, budget) in EXPECT.items():
    obs = load(fbx); objs[fbx] = obs; r = R[fbx] = {}
    E.extend('%s inside-out: %s' % (fbx, e) for e in winding_report(obs))   # tools/blender/check_winding.py
    by = {stem(o.name): o for o in obs}
    roots = [stem(o.name) for o in obs if o.parent is None]
    r['roots'] = roots
    if roots != [rootname]: E.append('%s roots %s' % (fbx, roots))
    for n, p in empties.items():
        o = by.get(n)
        if not o: E.append('%s missing %s' % (fbx, n)); continue
        if o.type != 'EMPTY': E.append('%s %s is not an empty' % (fbx, n))
        at = o.matrix_world.translation; r[n] = [round(x, 3) for x in at]
        if (at - Vector(p)).length > TOL: E.append('%s %s at %s, expected %s' % (fbx, n, r[n], p))
        # axes: identity in the mount frame (local -Y = forward in Blender = Unity +Z)
        fwd = (o.matrix_world.to_3x3() @ Vector((0, -1, 0))).normalized(); up = (o.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
        if (fwd - Vector((0, -1, 0))).length > .01 or (up - Vector((0, 0, 1))).length > .01: E.append('%s %s axes rotated' % (fbx, n))
    meshes = [o for o in obs if o.type == 'MESH']
    r['meshes'] = {stem(o.name): {'tris': tris(o), 'parent': stem(o.parent.name) if o.parent else None,
                                  'materials': [stem(m.name) for m in o.data.materials if m], 'colour_attrs': [a.name for a in o.data.color_attributes],
                                  'zero_area_faces': zero_area(o)} for o in meshes}
    r['tris'] = sum(tris(o) for o in meshes)
    if r['tris'] > budget: E.append('%s %d tris > %d' % (fbx, r['tris'], budget))
    if any(v['zero_area_faces'] for v in r['meshes'].values()): E.append('%s zero-area faces' % fbx)
    if any(not v['colour_attrs'] for v in r['meshes'].values()): E.append('%s mesh without vertex colours' % fbx)
    bad = {m for v in r['meshes'].values() for m in v['materials']} - {'SS_Harpoon_Timber', 'SS_Harpoon_Rope', 'SS_Harpoon_Iron', 'SS_Harpoon_Lens'}
    if bad: E.append('%s unexpected materials %s' % (fbx, bad))
    v, f = world_tris(meshes); r['bounds'] = [[round(min(p[k] for p in v), 3) for k in range(3)], [round(max(p[k] for p in v), 3) for k in range(3)]]

mob = {stem(o.name): o for o in objs['HarpoonMount.fbx']}
if 'HarpoonMount' in R['HarpoonMount.fbx']['roots'] or True:
    m = R['HarpoonMount.fbx']['meshes']
    for n, par in (('Mount_Base', 'HarpoonMount'), ('Swivel_Body', 'Swivel'), ('Winch_Coil', 'Winch_Drum')):
        if n not in m: E.append('mesh %s missing' % n)
        elif m[n]['parent'] != par: E.append('%s parent %s != %s' % (n, m[n]['parent'], par))
    for n in ('Barb_Muzzle', 'Winch_Drum', 'Harpooner_Stand'):
        o = mob.get(n)
        if o and o.parent and stem(o.parent.name) != 'Swivel': E.append('%s is not under Swivel' % n)

# ------------------------------------------------------------------ fit on the REAL BowLow deck
hull = load_kit(models=('BowLow',), skip=('Lantern_Bow_Frame', 'Lantern_Bow_Glass'))   # the game hides the kit's bow lantern: the gun-lamp is the only bow light
parts = {o.name.split('_', 1)[1]: o for o in hull}
def bvh(objs):
    v, f = world_tris(objs); return BVHTree.FromPolygons(v, f)
solid = bvh([o for n, o in parts.items() if n != 'Foredeck_Floor'])
floor = bvh([parts['Foredeck_Floor']])
root = mob['HarpoonMount']; root.matrix_world = Matrix.Translation(MOUNT_AT) @ root.matrix_world; bpy.context.view_layer.update()
sw = mob['Swivel']; sw0 = sw.matrix_world.copy(); axis = sw0.translation.copy()
moving = [o for o in objs['HarpoonMount.fbx'] if o.type == 'MESH' and stem(o.name) != 'Mount_Base']
base = mob['Mount_Base']
# 1. the static base sits on the deck: sample its underside against the floor
bv, bf = world_tris([base]); low = [p for p in bv if p.z < MOUNT_AT.z + .005]
gaps = []
for p in low:
    hit = floor.ray_cast(p + Vector((0, 0, .3)), Vector((0, 0, -1)), 1.0)
    gaps.append(None if hit[0] is None else round(p.z - hit[0].z, 3))
R['base_on_deck'] = {'underside_points': len(low), 'max_abs_gap_m': max((abs(g) for g in gaps if g is not None), default=None), 'off_deck_points': gaps.count(None)}
if gaps.count(None) or R['base_on_deck']['max_abs_gap_m'] is None or R['base_on_deck']['max_abs_gap_m'] > .02: E.append('base not flush on the foredeck %s' % R['base_on_deck'])
if BVHTree.FromPolygons(*world_tris([base])).overlap(solid): E.append('Mount_Base intersects the hull')
# 2. sweep the swivel over the bow arc
gun = []   # gun-port clearance boxes (BowLow equipmentSlots, source u) -> review metres
for side in (-1, 1):
    a = src_to_review((1.075, side * 1.985, 2.11)); b = src_to_review((3.125, side * 4.935, 4.11))
    gun.append((Vector([min(a[k], b[k]) for k in range(3)]), Vector([max(a[k], b[k]) for k in range(3)])))
hatch = src_to_review((3.45, 0, 2.11))
sweep = {}; rope = {}
for yaw in range(-45, 46, 5):
    sw.matrix_world = Matrix.Translation(axis) @ Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Translation(-axis) @ sw0
    bpy.context.view_layer.update()
    v, f = world_tris(moving); hits = BVHTree.FromPolygons(v, f).overlap(solid)
    in_ports = sum(1 for p in v for lo, hi in gun if all(lo[k] - 1e-4 <= p[k] <= hi[k] + 1e-4 for k in range(3)))
    sweep[yaw] = {'hull_overlaps': len(hits), 'verts_in_gun_clearance': in_ports}
    if hits: E.append('yaw %d: swivel parts intersect the hull (%d tri pairs)' % (yaw, len(hits)))
    if in_ports: E.append('yaw %d: %d vertices inside a bow gun clearance box' % (yaw, in_ports))
    # rope line from the muzzle to sea-level targets 10/20/35 m along the bearing
    mz = mob['Barb_Muzzle'].matrix_world.translation.copy(); d = (mob['Barb_Muzzle'].matrix_world.to_3x3() @ Vector((0, -1, 0))); d.z = 0; d.normalize()
    for rng in (10, 20, 35):
        tgt = mz + d * rng; tgt.z = 0.2; dirv = tgt - mz
        blk = []
        for n, o in parts.items():
            t = BVHTree.FromPolygons(*world_tris([o])).ray_cast(mz, dirv.normalized(), dirv.length)
            if t[0] is not None: blk.append(n)
        if blk: rope['%d@%dm' % (yaw, rng)] = sorted(blk)
sw.matrix_world = sw0; bpy.context.view_layer.update()
R['sweep_-45_to_45'] = sweep
R['rope_blocked_by_hull (yaw@range: parts)'] = rope
R['muzzle_height_above_deck_m'] = round(mob['Barb_Muzzle'].matrix_world.translation.z - MOUNT_AT.z, 3)
bv, bf = world_tris([o for o in objs['HarpoonMount.fbx'] if o.type == 'MESH'])
R['footprint_review_m'] = [[round(min(p[k] for p in bv), 3) for k in range(2)], [round(max(p[k] for p in bv), 3) for k in range(2)]]
R['deck_level_clearance_to_hatch_marker_m'] = round(min((Vector((p.x, p.y)) - Vector((hatch.x, hatch.y))).length for p in bv if p.z < MOUNT_AT.z + .3), 3)
R['mount_at_review_m'] = [round(x, 3) for x in MOUNT_AT]
json.dump(R, open(HV + '/export-verification.json', 'w'), indent=1)
print('CHECK mount tris %s barb tris %s | muzzle %.2f m above deck | rope-blocked cases %d | errors %d: %s' % (
    R['HarpoonMount.fbx'].get('tris'), R['HarpoonBarb.fbx'].get('tris'), R['muzzle_height_above_deck_m'], len(rope), len(E), '; '.join(E[:8])))

# ================================================================== Gun-lamp (Kevin 2026-10-04) -> lamp-verification.json
# Uses the mount placed on the low bow + the sweep set-up above (`mob`, `sw`, `sw0`, `axis`). Everything stays in the
# review frame with the root at MOUNT_AT; the raised bow is the same gun with its own deck height, sea and fairlead.
LE = []; LR = {'errors': LE}
for n, par, kind in (('Lamp', 'Swivel', 'MESH'), ('Lamp_Lens', 'Lamp', 'MESH'), ('Lamp_Light', 'Lamp', 'EMPTY')):
    o = mob.get(n)
    if not o: LE.append('missing ' + n); continue
    if o.type != kind: LE.append('%s is %s, expected %s' % (n, o.type, kind))
    if not o.parent or stem(o.parent.name) != par: LE.append('%s parent %s != %s' % (n, o.parent and stem(o.parent.name), par))
lamp_o, lens_o, light_o, body_o = mob.get('Lamp'), mob.get('Lamp_Lens'), mob.get('Lamp_Light'), mob['Swivel_Body']
if lamp_o and lens_o and light_o:
    rootinv = root.matrix_world.inverted()
    LR['lamp_local_under_Swivel_blender_m'] = [round(x, 3) for x in (sw.matrix_world.inverted() @ lamp_o.matrix_world.translation)]
    LR['lamp_local_under_Swivel_unity_m'] = [round(-LR['lamp_local_under_Swivel_blender_m'][0], 3), LR['lamp_local_under_Swivel_blender_m'][2], round(-LR['lamp_local_under_Swivel_blender_m'][1], 3)]
    LR['tris'] = {'Lamp': tris(lamp_o), 'Lamp_Lens': tris(lens_o)}; LR['tris']['total'] = sum(LR['tris'].values())
    if LR['tris']['total'] > 300: LE.append('lamp %d tris > 300' % LR['tris']['total'])
    LR['materials'] = {'Lamp': [stem(m.name) for m in lamp_o.data.materials if m], 'Lamp_Lens': [stem(m.name) for m in lens_o.data.materials if m]}
    if LR['materials']['Lamp_Lens'] != ['SS_Harpoon_Lens']: LE.append('Lamp_Lens materials %s' % LR['materials']['Lamp_Lens'])
    # the light empty: at the lens centre, identity (forward = Unity +Z = down the bore)
    lens_v = [lens_o.matrix_world @ v.co for v in lens_o.data.vertices]
    front = min(p.y for p in lens_v); face = [p for p in lens_v if p.y < front + 1e-4]
    lc = sum(face, Vector()) / len(face); lt = light_o.matrix_world.translation
    LR['light_to_lens_face_centre_m'] = round((lt - lc).length, 4)
    if LR['light_to_lens_face_centre_m'] > .01: LE.append('Lamp_Light not at the lens centre (%s m)' % LR['light_to_lens_face_centre_m'])
    fwd = (light_o.matrix_world.to_3x3() @ Vector((0, -1, 0))).normalized(); up = (light_o.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
    if (fwd - Vector((0, -1, 0))).length > .01 or (up - Vector((0, 0, 1))).length > .01: LE.append('Lamp_Light axes rotated')
    # lens behind the muzzle face; lamp on the side away from the crank
    mzw = mob['Barb_Muzzle'].matrix_world.translation
    allv = [lamp_o.matrix_world @ v.co for v in lamp_o.data.vertices] + lens_v
    LR['lens_face_behind_muzzle_m'] = round(front - mzw.y, 3)
    LR['lamp_front_behind_muzzle_m'] = round(min(p.y for p in allv) - mzw.y, 3)
    if LR['lens_face_behind_muzzle_m'] <= 0: LE.append('lens not behind the muzzle')
    crank_x = max((mob['Winch_Coil'].matrix_world @ v.co).x for v in mob['Winch_Coil'].data.vertices) - root.matrix_world.translation.x
    lamp_x = (sum(allv, Vector()) / len(allv)).x - root.matrix_world.translation.x
    LR['lamp_side_vs_crank'] = {'lamp_centre_x_blender': round(lamp_x, 3), 'crank_tip_x_blender': round(crank_x, 3)}
    if lamp_x * crank_x >= 0: LE.append('lamp is on the crank side')
    # no clipping: lamp vs the rest of the gun (barrel/yoke/post/drum/crank/base) and a loaded barb
    barb_objs = [o for o in objs['HarpoonBarb.fbx'] if o.type == 'MESH']
    broot_ = next(o for o in objs['HarpoonBarb.fbx'] if o.parent is None)
    broot_.matrix_world = mob['Barb_Muzzle'].matrix_world.copy(); bpy.context.view_layer.update()
    lamp_bvh = BVHTree.FromPolygons(*world_tris([lamp_o, lens_o]))
    LR['clip'] = {}
    for name, ol in (('Swivel_Body', [body_o]), ('Winch_Coil', [mob['Winch_Coil']]), ('Mount_Base', [mob['Mount_Base']]), ('loaded barb', barb_objs)):
        ov = lamp_bvh.overlap(BVHTree.FromPolygons(*world_tris(ol)))
        ob_bvh = BVHTree.FromPolygons(*world_tris(ol))
        dmin = min(ob_bvh.find_nearest(p)[3] for p in allv)
        LR['clip'][name] = {'overlapping_tri_pairs': len(ov), 'nearest_lamp_vertex_m': round(dmin, 3)}
        if ov: LE.append('lamp intersects %s (%d tri pairs)' % (name, len(ov)))
    # the crank's whole turn: the handle sweeps a disc about the drum axle
    wc = mob['Winch_Coil']; wc0 = wc.matrix_world.copy(); dc = mob['Winch_Drum'].matrix_world.translation.copy(); crank_min = 9e9
    for k in range(0, 360, 15):
        wc.matrix_world = Matrix.Translation(dc) @ Matrix.Rotation(math.radians(k), 4, 'X') @ Matrix.Translation(-dc) @ wc0; bpy.context.view_layer.update()
        cb = BVHTree.FromPolygons(*world_tris([wc]))
        if lamp_bvh.overlap(cb): LE.append('crank at %d deg hits the lamp' % k)
        crank_min = min(crank_min, min(cb.find_nearest(p)[3] for p in allv))
    wc.matrix_world = wc0; bpy.context.view_layer.update()
    LR['clip']['crank full turn nearest_m'] = round(crank_min, 3)

    # ---- the rope never comes within 0.1 m of the lamp: muzzle -> (stem fairlead ->) sea-level target, every bearing
    look = json.load(open(HV + '/rope-look.json'))['states']
    rad = max(look['taut']['widthM'], look['strained']['widthM']) / 2
    sags = sorted({0.0, look['taut']['sagFractionOfSpan'], look['strained']['sagFractionOfSpan'], 0.035})
    RANGES = (10, 15, 20, 25, 30, 35); GATE = 0.10
    LR['rope_radius_m'] = rad; LR['sags_tested'] = sags; LR['ranges_m'] = RANGES; LR['yaw_deg'] = '-45..45 step 1'
    LR['fairleads_bow_local_m'] = {k: v['fairlead'] for k, v in BOWS.items()}
    worst = {}; routed_cases = {}
    for bow, B in BOWS.items():
        worst[bow] = {'straight': (9e9, ''), 'via_fairlead': (9e9, '')}; routed_cases[bow] = 0
    for yaw in range(-45, 46):
        sw.matrix_world = Matrix.Translation(axis) @ Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Translation(-axis) @ sw0
        bpy.context.view_layer.update()
        lb = BVHTree.FromPolygons(*world_tris([lamp_o, lens_o]))
        LC_ = sum((lamp_o.matrix_world @ v.co for v in lamp_o.data.vertices), Vector()) / len(lamp_o.data.vertices)
        mz = mob['Barb_Muzzle'].matrix_world.translation.copy(); d = (mob['Barb_Muzzle'].matrix_world.to_3x3() @ Vector((0, -1, 0))); d.z = 0; d.normalize()
        for bow, B in BOWS.items():
            FL = root.matrix_world.translation + bow_to_root(B['fairlead'], B['mount'])
            sea = root.matrix_world.translation.z + (0.20 - B['mount'][1])
            for rng in RANGES:
                tgt = mz + d * rng; tgt.z = sea
                # would the game lift it onto the fairlead? (the straight run passes under the fairlead's height there)
                v = FL - mz; dirh = (tgt - mz); tt = max(0.0, min(1.0, v.dot(dirh) / dirh.length_squared))
                if (mz.lerp(tgt, tt)).z < FL.z and tt > 0: routed_cases[bow] += 1
                for mode, a0 in (('straight', mz), ('via_fairlead', FL)):
                    chord = (tgt - a0).length
                    for sag in sags:
                        pts = []
                        if mode == 'via_fairlead':
                            m_ = int((FL - mz).length / .01) + 1; pts += [mz.lerp(FL, i / m_) for i in range(m_)]
                        n = int(min(chord, 3.0) / .01)   # only the first 3 m can reach the lamp
                        for i in range(n + 1):
                            t_ = i * .01 / chord; p = a0.lerp(tgt, t_); p.z -= 4 * sag * chord * t_ * (1 - t_); pts.append(p)
                        for p in pts:
                            if (p - LC_).length > 1.5: continue
                            h = lb.find_nearest(p)
                            if h[0] is not None and h[3] - rad < worst[bow][mode][0]:
                                worst[bow][mode] = (h[3] - rad, 'yaw %d, %d m, sag %.3f' % (yaw, rng, sag))
    sw.matrix_world = sw0; bpy.context.view_layer.update()
    LR['rope_clearance_to_lamp_m (surface minus rope radius)'] = {b: {m: [round(c, 3), w] for m, (c, w) in v.items()} for b, v in worst.items()}
    LR['cases_the_game_routes_over_the_fairlead'] = routed_cases
    for b, v in worst.items():
        for m, (c, w) in v.items():
            if c < GATE: LE.append('%s bow, %s: rope within %.3f m of the lamp (%s)' % (b, m, c, w))
json.dump(LR, open(HV + '/lamp-verification.json', 'w'), indent=1)
cl = LR.get('rope_clearance_to_lamp_m (surface minus rope radius)', {})
print('CHECK_LAMP tris %s | local %s | lens %.3f m behind muzzle | rope clearance %s | errors %d: %s' % (
    LR.get('tris', {}).get('total'), LR.get('lamp_local_under_Swivel_blender_m'), LR.get('lens_face_behind_muzzle_m', -1),
    {b: min(x[0] for x in v.values()) for b, v in cl.items()}, len(LE), '; '.join(LE[:8])))
