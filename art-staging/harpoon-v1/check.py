# Harpoon v1 static check: re-imports the two EXPORTED FBXs (not the blend) and checks them against CONTRACT.md
# and the REAL base-coaster bow (kit.json, read-only). Run (see RUN.md):
#   Blender -b --factory-startup --python art-staging/harpoon-v1/check.py
# Writes art-staging/harpoon-v1/export-verification.json and prints one CHECK line; when BowLantern.fbx exists it also
# checks the lantern (line clearance over the bow arc incl. the swing) -> lantern-verification.json + one CHECK_LANTERN line.
import bpy, math, json, re
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
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

EXPECT = {'HarpoonMount.fbx': ('HarpoonMount', {'Swivel': (0, 0, .16), 'Barb_Muzzle': (0, -.80, 1.72), 'Winch_Drum': (0, .42, 1.06), 'Harpooner_Stand': (0, .86, .30)}, 2500),
          'HarpoonBarb.fbx': ('HarpoonBarb', {'Line_Attach': (0, .335, 0)}, 400)}
objs = {}
for fbx, (rootname, empties, budget) in EXPECT.items():
    obs = load(fbx); objs[fbx] = obs; r = R[fbx] = {}
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
    bad = {m for v in r['meshes'].values() for m in v['materials']} - {'SS_Harpoon_Timber', 'SS_Harpoon_Rope', 'SS_Harpoon_Iron'}
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
hull = load_kit(models=('BowLow',))
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

# ================================================================== BowLantern (Kevin 2026-10-04) -> lantern-verification.json
# Runs whenever BowLantern.fbx exists (after `build.py -- lantern`). Uses the mount placed + swept above (`mob`, `sw`,
# `sw0`, `axis`) and the real BowLow (`parts`) WITHOUT the kit's old bow lantern (the game hides it).
import os, bmesh
if os.path.exists(HV + '/BowLantern.fbx'):
    LE = []; LR = {'errors': LE}
    lobs = load('BowLantern.fbx'); lby = {stem(o.name): o for o in lobs}
    LR['roots'] = [stem(o.name) for o in lobs if o.parent is None]
    if LR['roots'] != ['BowLantern']: LE.append('roots %s' % LR['roots'])
    for n, par in (('BowLantern_Beam', 'BowLantern'), ('LanternBow_Pivot', 'BowLantern'), ('LanternBow_Chain', 'LanternBow_Pivot'),
                   ('Lantern_Bow_Frame', 'LanternBow_Pivot'), ('Lantern_Bow_Glass', 'LanternBow_Pivot')):
        o = lby.get(n)
        if not o: LE.append('missing ' + n); continue
        if not o.parent or stem(o.parent.name) != par: LE.append('%s parent %s != %s' % (n, o.parent and stem(o.parent.name), par))
    pv = lby.get('LanternBow_Pivot')
    if pv:
        if pv.type != 'EMPTY': LE.append('LanternBow_Pivot is not an empty')
        at = pv.matrix_world.translation; LR['pivot_root_frame'] = [round(x, 3) for x in at]
        if (at - L(*PIVOT_U)).length > TOL: LE.append('pivot at %s, expected %s' % (LR['pivot_root_frame'], [round(x, 3) for x in L(*PIVOT_U)]))
        fwd = (pv.matrix_world.to_3x3() @ Vector((0, -1, 0))).normalized(); up = (pv.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
        if (fwd - Vector((0, -1, 0))).length > .01 or (up - Vector((0, 0, 1))).length > .01: LE.append('pivot axes rotated')
    lmesh = [o for o in lobs if o.type == 'MESH']
    def islands_inward(o):
        bm = bmesh.new(); bm.from_mesh(o.data); seen = set(); n = bad = 0
        for f in bm.faces:
            if f.index in seen: continue
            stack = [f]; isl = []; seen.add(f.index)
            while stack:
                g = stack.pop(); isl.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h.index not in seen: seen.add(h.index); stack.append(h)
            c = sum((g.calc_center_median() for g in isl), Vector()) / len(isl); n += 1
            if sum(1 for g in isl if g.normal.dot(g.calc_center_median() - c) < 0) > len(isl) / 2: bad += 1
        bm.free(); return n, bad
    LR['meshes'] = {}
    for o in lmesh:
        isl, inward = islands_inward(o)
        LR['meshes'][stem(o.name)] = {'tris': tris(o), 'materials': [stem(m.name) for m in o.data.materials if m],
                                      'colour_attrs': [a.name for a in o.data.color_attributes], 'zero_area_faces': zero_area(o),
                                      'islands': isl, 'inward_islands': inward}
        if inward: LE.append('%s: %d of %d islands face inward (winding)' % (stem(o.name), inward, isl))
    LR['tris'] = sum(v['tris'] for v in LR['meshes'].values())
    if LR['tris'] > 600: LE.append('lantern %d tris > 600' % LR['tris'])
    if any(v['zero_area_faces'] for v in LR['meshes'].values()): LE.append('lantern zero-area faces')
    if any(not v['colour_attrs'] for v in LR['meshes'].values()): LE.append('lantern mesh without vertex colours')
    bad = {m for v in LR['meshes'].values() for m in v['materials']} - {'SS_Harpoon_Timber', 'SS_Harpoon_Rope', 'SS_Harpoon_Iron'}
    if bad: LE.append('lantern unexpected materials %s' % bad)
    if LR['meshes'].get('Lantern_Bow_Frame', {}).get('islands') != 8: LE.append('Lantern_Bow_Frame islands %s, expected the kit lantern\'s 8' % LR['meshes'].get('Lantern_Bow_Frame', {}).get('islands'))
    # CoasterOutfitting hangs its warm light on every renderer named *Lantern*_Glass: exactly one here.
    glassy = sorted(stem(o.name) for o in lmesh if 'Lantern' in stem(o.name) and '_Glass' in stem(o.name))
    if glassy != ['Lantern_Bow_Glass']: LE.append('light contract: *Lantern*_Glass renderers %s' % glassy)

    # ---- on the bow: same root spot as the mount
    lroot = lby['BowLantern']; lroot.matrix_world = Matrix.Translation(MOUNT_AT) @ lroot.matrix_world; bpy.context.view_layer.update()
    hullp = {n: o for n, o in parts.items() if not n.startswith('Lantern_Bow')}
    hull_bvh = bvh(list(hullp.values()))
    hv, _ = world_tris(list(hullp.values()))
    beam_o = lby['BowLantern_Beam']; swing_o = [lby[n] for n in ('LanternBow_Chain', 'Lantern_Bow_Frame', 'Lantern_Bow_Glass') if n in lby]
    body_o = [lby[n] for n in ('Lantern_Bow_Frame', 'Lantern_Bow_Glass') if n in lby]
    P = lby['LanternBow_Pivot'].matrix_world.translation.copy()
    def posed(objs, R):
        v, f = world_tris(objs); return [P + R @ (p - P) for p in v], f
    def poses(cone):
        if cone == 0: return [Matrix.Identity(3)]
        return [Matrix.Rotation(math.radians(cone), 3, Vector((math.cos(a), math.sin(a), 0))) for a in (k * math.pi / 4 for k in range(8))]
    beam_bvh = bvh([beam_o]); bv_, _ = world_tris([beam_o])

    # forward of the stem, below the muzzle (rest + the 10 deg cone)
    mz0 = mob['Barb_Muzzle'].matrix_world.translation.copy()
    sw_v = [p for R in poses(0) + poses(10) for p in posed(swing_o, R)[0]]
    zlo, zhi = min(p.z for p in sw_v), max(p.z for p in sw_v)
    hull_front = min(p.y for p in hv if zlo - .05 <= p.z <= zhi + .05)   # forward = -Y; the stem at the lantern's heights
    LR['lantern_ahead_of_hull_front_m'] = round(hull_front - max(p.y for p in sw_v), 3)
    if LR['lantern_ahead_of_hull_front_m'] <= 0: LE.append('the lantern swings back over the stem (%s m)' % LR['lantern_ahead_of_hull_front_m'])
    LR['lantern_top_below_muzzle_m'] = round(mz0.z - max(p.z for p in sw_v), 3)
    LR['beam_top_below_muzzle_m'] = round(mz0.z - max(p.z for p in bv_), 3)
    if LR['lantern_top_below_muzzle_m'] <= 0 or LR['beam_top_below_muzzle_m'] <= 0: LE.append('beam/lantern not below the muzzle')
    LR['glass_centre_above_sea_m'] = round(sum((lby['Lantern_Bow_Glass'].matrix_world @ v.co for v in lby['Lantern_Bow_Glass'].data.vertices), Vector()).z
                                           / len(lby['Lantern_Bow_Glass'].data.vertices) - 0.20, 3)
    # the beam is fixed in the stem: it meets the hull, and its root end sits just behind the stem face
    if not BVHTree.FromPolygons(*world_tris([beam_o])).overlap(hull_bvh): LE.append('the beam does not meet the stem (floating)')
    root_end = sorted(bv_, key=lambda p: -p.y)[:4]
    fr = [hull_bvh.ray_cast(p, Vector((0, -1, 0)), 1.0)[3] for p in root_end]
    LR['beam_root_end_behind_stem_face_m'] = [None if d is None else round(d, 3) for d in fr]
    if any(d is None or d > .40 for d in fr): LE.append('beam root end is not inside the stem %s' % LR['beam_root_end_behind_stem_face_m'])
    tip = Vector((MOUNT_AT.x, min(p.y for p in bv_) + .01, BEAM_TOP - .01))   # review z = game metres up (MOUNT_AT.z = deck 1.055)
    hit = hull_bvh.ray_cast(tip, Vector((0, 1, 0)), 3.0)
    LR['beam_sticks_out_of_stem_m'] = None if hit[0] is None else round(hit[3] + .01, 3)
    # the swing never touches the hull or the beam (chain-in-eye excluded)
    LR['swing_contacts'] = {}
    for cone in (10, 20, 34):
        hits = []
        for R in poses(cone):
            if BVHTree.FromPolygons(*posed(swing_o, R)).overlap(hull_bvh): hits.append('hull')
            if BVHTree.FromPolygons(*posed(body_o, R)).overlap(beam_bvh): hits.append('beam')
        LR['swing_contacts']['%d deg' % cone] = sorted(set(hits))
        if cone == 10 and hits: LE.append('a 10 deg swing touches %s' % sorted(set(hits)))

    # ---- the line of fire: muzzle -> targets on every bearing, straight (strained) and with the taut sag, rope radius
    look = json.load(open(HV + '/rope-look.json'))['states']
    rad = max(look['taut']['widthM'], look['strained']['widthM']) / 2
    sags = sorted({0.0, look['taut']['sagFractionOfSpan'], look['strained']['sagFractionOfSpan'], 0.035})   # 0.035 = HarpoonLine's under-strain hang
    ropes = []
    hull_block = {}
    for yaw in sorted(set(range(-45, 46, 5)) | set(range(-6, 7))):
        sw.matrix_world = Matrix.Translation(axis) @ Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Translation(-axis) @ sw0
        bpy.context.view_layer.update()
        mz = mob['Barb_Muzzle'].matrix_world.translation.copy(); d = (mob['Barb_Muzzle'].matrix_world.to_3x3() @ Vector((0, -1, 0))); d.z = 0; d.normalize()
        for rng in (10, 35):
            for hgt in (0.0, 1.5, 3.0):
                tgt = mz + d * rng; tgt.z = 0.20 + hgt; span = (tgt - mz).length
                if hgt == 0.0:
                    t = hull_bvh.ray_cast(mz, (tgt - mz).normalized(), span)
                    if t[0] is not None: hull_block['%d@%dm' % (yaw, rng)] = True
                # HarpoonLine (a44f586e): when the run would dip into the stem, straight muzzle -> fairlead
                # (HarpoonGun.StemTopWorld, measured in play: bow-local (0, 2.619, 4.665) m), then the sag on to the
                # barb; slack = its water-capped belly (mid stays 0.35 m over the low end, <= 0.35 x chord).
                FL = Vector((MOUNT_AT.x, MOUNT_AT.y - (4.665 - MOUNT_M[2]), 2.619))
                routed = hull_bvh.ray_cast(mz, (tgt - mz).normalized(), span)[0] is not None
                a0 = FL if routed else mz; chord = (tgt - a0).length
                slack_sag = max(0.0, min(.35 * chord, (a0.z + tgt.z) / 2 - min(a0.z, tgt.z) - .35))
                for sag in sags + ['slack']:
                    s_m = slack_sag if sag == 'slack' else sag * chord
                    n = int(chord / .02); pts = []
                    if routed:
                        m_ = int((FL - mz).length / .02) + 1
                        pts += [mz.lerp(FL, i / m_) for i in range(m_)]
                    for i in range(n + 1):
                        tt = i / n; p = a0.lerp(tgt, tt); p.z -= 4 * s_m * tt * (1 - tt); pts.append(p)
                    pts = [p for p in pts if (p - P).length < 2.0]
                    ropes.append(('%d deg %d m +%.1f sag %s%s' % (yaw, rng, hgt, sag, ' ROUTED' if routed else ''), pts))
    sw.matrix_world = sw0; bpy.context.view_layer.update()
    def clearance(bvh_):
        best = (9e9, '')
        for name, pts in ropes:
            for p in pts:
                hit = bvh_.find_nearest(p)
                if hit[0] is not None and hit[3] < best[0]: best = (hit[3], name)
        return round(best[0] - rad, 3), best[1]
    LR['rope_radius_m'] = rad; LR['rope_sags_tested'] = sags
    LR['line_clearance_m'] = {}
    c, who = clearance(beam_bvh); LR['line_clearance_m']['beam'] = [c, who]
    if c < .02: LE.append('line clears the beam by only %s m (%s)' % (c, who))
    for cone in (0, 10, 20, 34):
        worst = (9e9, '')
        for R in poses(cone):
            c, who = clearance(BVHTree.FromPolygons(*posed(swing_o, R)))
            if c < worst[0]: worst = (c, who)
        LR['line_clearance_m']['lantern+chain %d deg swing' % cone] = list(worst)
        if cone <= 10 and worst[0] < .02 and 'slack' not in worst[1]: LE.append('line clears the lantern by only %s m at a %d deg swing (%s)' % (worst[0], cone, worst[1]))
    LR['hull_blocks_line_at_sea_level_without_old_lantern'] = sorted(hull_block)
    json.dump(LR, open(HV + '/lantern-verification.json', 'w'), indent=1)
    print('CHECK_LANTERN tris %s | beam clear %s m | lantern clear %s m (10 deg swing) | ahead of hull %s m | top %s m below muzzle | errors %d: %s' % (
        LR['tris'], LR['line_clearance_m']['beam'][0], LR['line_clearance_m'].get('lantern+chain 10 deg swing', ['?'])[0],
        LR.get('lantern_ahead_of_hull_front_m'), LR.get('lantern_top_below_muzzle_m'), len(LE), '; '.join(LE[:8])))
