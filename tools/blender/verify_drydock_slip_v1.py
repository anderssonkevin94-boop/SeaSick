"""Verify the modular dry-dock slip v1 FBX pieces against their manifest.

Run:  Blender --background --python tools/blender/verify_drydock_slip_v1.py
Reads art-staging/drydock-slip-v1/{manifest.json,*.fbx} (the exported files,
imported back through the Y-up FBX conversion) and checks, per piece:
  * every mesh closed (all edges manifold), no degenerate faces, flat shaded,
    vertex colour 'Col' present
  * Snap_Sea -> Snap_Land spans exactly lengthAlongSlipM along +Y, and all
    geometry lies inside [0, length] so neighbours butt with no overlap;
    the walkway stringers run the full [0, length] so there is no gap
  * channel clear width between the side walkway decks >= manifest and >= 7.4
  * walking surface at walkwayHeightM
  * Bay: keel-stack top and both shore-pad tops == keelRestZM == Keel_Rest.z,
    and nothing but the cradle rises into the hull envelope (|x| < 3.02)
  * no floating parts: every loose component touches the ground or another
Exit code 1 on any failure.
"""
import sys, json, math
from pathlib import Path
import bpy, bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-staging/drydock-slip-v1'
M = json.loads((OUT / 'manifest.json').read_text())
EPS = 2e-3
HULL_HALF = 3.02
fails, notes = [], []


def check(ok, msg):
    (notes if ok else fails).append(('PASS ' if ok else 'FAIL ') + msg)


def islands(bm):
    bm.verts.ensure_lookup_table(); seen = set(); out = []
    for v in bm.verts:
        if v.index in seen: continue
        comp, st = [], [v]; seen.add(v.index)
        while st:
            x = st.pop(); comp.append(x.co.copy())
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen: seen.add(y.index); st.append(y)
        mn = Vector([min(c[i] for c in comp) for i in range(3)])
        mx = Vector([max(c[i] for c in comp) for i in range(3)])
        out.append((mn, mx))
    return out


for name, info in M['pieces'].items():
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT / info['file']))
    L = info['lengthAlongSlipM']
    obs = {o.name.split('.')[0]: o for o in bpy.data.objects}
    mk = {n: obs[n].matrix_world.translation.copy() for n in obs if obs[n].type == 'EMPTY' and n != name}
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    check(bool(meshes), f'{name}: has meshes ({len(meshes)})')
    allv, isl, walk_deck, stringer_y, cradle = [], [], [], [], []
    for o in meshes:
        me = o.data
        bm = bmesh.new(); bm.from_mesh(me); bm.transform(o.matrix_world)
        bad_e = sum(1 for e in bm.edges if not e.is_manifold)
        bad_f = sum(1 for f in bm.faces if f.calc_area() < 1e-8)
        check(bad_e == 0 and bad_f == 0, f'{name}/{o.name}: manifold + non-degenerate (bad edges {bad_e}, degenerate faces {bad_f}, faces {len(bm.faces)})')
        check(not any(p.use_smooth for p in me.polygons), f'{name}/{o.name}: flat shaded')
        check(me.color_attributes.get('Col') is not None or len(me.color_attributes) > 0, f'{name}/{o.name}: vertex colour present ({[a.name for a in me.color_attributes]})')
        vs = [v.co.copy() for v in bm.verts]; allv += vs
        isl += islands(bm)
        if o.name.endswith('Walkways'):
            walk_deck += [v for v in vs if .8 < v.z < .995 and abs(v.x) > 3]
            stringer_y += [v.y for v in vs if .55 < v.z < .85 and abs(v.x) > 3.9 and abs(v.x) < 5.4]
        if o.name.endswith('Ship_Cradle'):
            cradle += vs
        bm.free()
    # span + butt joints
    for n in ('Snap_Sea', 'Snap_Land'): check(n in mk, f'{name}: marker {n}')
    if 'Snap_Sea' in mk and 'Snap_Land' in mk:
        d = mk['Snap_Land'] - mk['Snap_Sea']
        check(abs(d.y - L) < EPS and abs(d.x) < EPS and abs(d.z) < EPS and mk['Snap_Sea'].length < EPS,
              f'{name}: Snap_Sea at origin, Snap_Land at +Y {L} (got {tuple(round(c, 3) for c in d)})')
    ymin, ymax = min(v.y for v in allv), max(v.y for v in allv)
    check(ymin > -EPS and ymax < L + EPS, f'{name}: geometry inside y[0,{L}] (y {ymin:.3f}..{ymax:.3f}) -> no overlap with neighbours')
    check(stringer_y and min(stringer_y) < EPS and max(stringer_y) > L - EPS,
          f'{name}: walkway stringers run the full length (y {min(stringer_y):.3f}..{max(stringer_y):.3f}) -> no gap')
    # channel + walking surface
    side = [v for v in walk_deck if name != 'DryDock_Head' or v.y < 3.0 - EPS]
    inner_l = max(v.x for v in side if v.x < 0); inner_r = min(v.x for v in side if v.x > 0)
    clear = inner_r - inner_l
    check(clear >= 7.4 and clear >= M['channelClearWidthM'] - EPS, f'{name}: channel clear width {clear:.3f} m (manifest {M["channelClearWidthM"]}, need >= 7.4)')
    top = max(v.z for v in walk_deck)
    check(abs(top - M['walkwayHeightM']) < EPS, f'{name}: walking surface {top:.3f} m')
    # floating parts
    floating = 0
    for i, (a0, a1) in enumerate(isl):
        if a0.z < EPS: continue
        if not any(j != i and all(a0[k] <= b1[k] + EPS and b0[k] <= a1[k] + EPS for k in range(3)) for j, (b0, b1) in enumerate(isl)):
            floating += 1
    check(floating == 0, f'{name}: no floating parts ({len(isl)} components, {floating} floating)')
    if name == 'DryDock_Bay':
        k = M['keelRestZM']
        keel_top = max(v.z for v in cradle if abs(v.x) < .5)
        pad_l = max(v.z for v in cradle if -2.4 < v.x < -1.2)
        pad_r = max(v.z for v in cradle if 1.2 < v.x < 2.4)
        kr = mk.get('Keel_Rest')
        check(all(abs(z - k) < EPS for z in (keel_top, pad_l, pad_r)) and kr is not None and abs(kr.z - k) < EPS,
              f'{name}: pad tops keel {keel_top:.3f} / shores {pad_l:.3f}, {pad_r:.3f} == keelRestZ {k} == Keel_Rest.z {kr.z if kr else None}')
        intr = [v for v in allv if abs(v.x) < HULL_HALF and v.z > k + EPS]
        check(not intr, f'{name}: nothing rises into the hull envelope |x|<{HULL_HALF}, z>{k} ({len(intr)} verts)')
    if name == 'DryDock_SeaEnd':
        intr = [v for v in allv if abs(v.x) < HULL_HALF and v.z > M['keelRestZM'] + EPS]
        check(not intr and 'Sea_Entry' in mk, f'{name}: open channel over the keel line + Sea_Entry marker')
    if name == 'DryDock_Head':
        check('Interaction_Point' in mk, f'{name}: Interaction_Point at {tuple(round(c, 2) for c in mk.get("Interaction_Point", Vector()))}')

for n in notes + fails: print(n)
print(f'VERIFY {"FAIL" if fails else "PASS"}: {len(notes)} passed, {len(fails)} failed')
sys.exit(1 if fails else 0)
