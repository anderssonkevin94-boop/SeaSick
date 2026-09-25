"""Raised-section wall variants + Astra's raised ends, as per-section modules.

docs/RAISED-SECTIONS.md sec 2-4. Builds, in ONE module-local frame (+X bow, +Y port, +Z up,
middle 0..6 u), without any boolean on the open shell:

  MiddleWF  hull.middle.w1xr.wf.v1  raised middle, END WALL at the forward face (stairs)
  MiddleWA  hull.middle.w1xr.wa.v1  raised middle, END WALL at the aft face (stairs, mirrored)
  MiddleWB  hull.middle.w1xr.wb.v1  raised middle, stairs wall fwd + door-only wall aft
  SternWF   hull.stern.w1xr.wf.v1   Astra's raised stern (modular-width-raised-v1) as authored
  BowWA     hull.bow.w1xr.wa.v1     Astra's raised bow (modular-width-raised-v1) as authored

Sources (both in the same whole-ship frame: stern root x 0, middle root x 9.3, bow root x 15.3):
  art-staging/modular-raised-middle-v1/continuous-upper-deck.blend  connected raised middle
  art-staging/modular-width-raised-v1/both-raised.blend              Astra's raised stern/bow

The wall kit is Astra's raised-stern forward face. Its bulkhead is baked into the stern
Hull_Shell at stern x 9.26 (0.04 inside the 9.30 face), with stair notches |y| 4.62-5.80,
x 6.06-9.26, a door tunnel and a hatch room. RE-BASE: the stern's upper shell for
stern x >= 6.0 (z >= 1.70) is translated by (6.0 - 9.3) so its face lands at middle x 6.0
(wall at 5.96); the aft wall is that piece mirrored about x (x' = 6 - x). The middle's own
connected shell supplies the lower hull (z <= 1.70, identical to W1x) and the plain upper
box elsewhere. Pieces are welded with the same T-joint-splitting weld Astra's
modular_width_inserts_v1.combined uses. No hatch/ladder in a middle (it would sit where the
chimney lands; the stern deck is closed under its hatch lid anyway), so the door leads into
the enclosed between-deck.
wb: two stair kits (3.2 u run each) cannot fit in 6 u -> stairs at the FORWARD end, and a
door-only wall aft (the same piece with its stair notches capped flat).

Run: SECTIONS_SCRATCH=<dir for raised-sections.blend> Blender --background --python tools/blender/modular_raised_sections_v1.py
"""
import json
import math
import os
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import modular_raised_stern_v2 as revision  # noqa: E402
import export_hull_hydrostatics as hydro  # noqa: E402

family, ship, ss, fore = revision.family, revision.ship, revision.ss, revision.fore
ROOT = HERE.parents[1]
CONNECTED = ROOT / 'art-staging/modular-raised-middle-v1/continuous-upper-deck.blend'
ENDS = ROOT / 'art-staging/modular-width-raised-v1/both-raised.blend'
OUT = ROOT / 'art-staging/modular-raised-sections-v1'
HYDRO_OUT = ROOT / 'Assets/_Project/Resources/ShipModules/Hydrostatics/HullW1xRSections_v1'

SHIFT = 6.0 - 9.3          # stern frame -> middle frame for the forward wall
CUT = 2.7                  # middle x where the wall piece meets the plain middle box
WALL_F, WALL_A = 5.96, 0.04
NOTCH_F, NOTCH_A = (2.76, 5.96), (0.04, 3.24)
BAND = (4.62, 5.80)
EPS = 2e-3
ALCOVE = 0.8               # depth of the door alcove behind a middle's wall door
LOW, UP = 1.76, 4.20

VARIANTS = {
    'MiddleWF': {'fwd': 'stairs', 'aft': None},
    'MiddleWA': {'fwd': None, 'aft': 'stairs'},
    'MiddleWB': {'fwd': 'stairs', 'aft': 'door'},
}


# ---------------------------------------------------------------- bmesh helpers
def bm_from(ob, matrix=None):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.transform(ob.matrix_basis if matrix is None else matrix)
    return bm


def clip(bm, axis, value, keep):
    co, no = [0, 0, 0], [0, 0, 0]
    co[axis], no[axis] = value, 1
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-6,
                           plane_co=co, plane_no=no, clear_inner=keep == 'hi', clear_outer=keep == 'lo')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    return bm


def mirror(bm):
    bm.transform(Matrix(((-1, 0, 0, 6), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))))
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    return bm


def col_layer(bm):
    return bm.loops.layers.float_color.get('Col') or bm.loops.layers.color.get('Col')


def paint_like_neighbour(bm, faces):
    layer = col_layer(bm)
    if layer is None:
        return
    for f in faces:
        f.normal_update()
        best = None
        for e in f.edges:
            for g in e.link_faces:
                if g is f or g in faces:
                    continue
                g.normal_update()
                if g.normal.dot(f.normal) > .99:
                    best = g
        if best is None:
            for e in f.edges:
                for g in e.link_faces:
                    if g is not f and g not in faces:
                        best = g
        if best is not None:
            c = tuple(best.loops[0][layer])
            for loop in f.loops:
                loop[layer] = c
            f.material_index = best.material_index


def ordered(verts, u, v):
    cu = sum(u(x) for x in verts) / len(verts)
    cv = sum(v(x) for x in verts) / len(verts)
    return sorted(verts, key=lambda x: math.atan2(v(x) - cv, u(x) - cu))


def to_mesh(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    return me


def weld(pieces, name):
    """Astra's combined(): weld, split edges at T joints, weld again."""
    bm = bmesh.new()
    for p in pieces:
        me = to_mesh(p, name + '_tmp')
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        p.free()
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=2e-5)
    for _ in range(12):
        boundary = [e for e in bm.edges if e.is_boundary]
        verts = list({v for e in boundary for v in e.verts})
        changed = False
        for e in boundary:
            if not e.is_valid:
                continue
            a, b = e.verts
            d = b.co - a.co
            if d.length_squared < 1e-14:
                continue
            hits = []
            for v in verts:
                if not v.is_valid or v in e.verts:
                    continue
                t = (v.co - a.co).dot(d) / d.length_squared
                if 1e-5 < t < 1 - 1e-5 and (v.co - (a.co + t * d)).length < 2e-5:
                    hits.append((t, v.co.copy()))
            if hits:
                _, p = min(hits, key=lambda h: h[0])
                _, new = bmesh.utils.edge_split(e, a, (p - a.co).length / d.length)
                new.co = p
                changed = True
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=2e-5)
        if not changed:
            break
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=1e-8)
    return bm


def stats(bm):
    return {'boundary_edges': sum(e.is_boundary for e in bm.edges),
            'overconnected_edges': sum(len(e.link_faces) > 2 for e in bm.edges),
            'degenerate_faces': sum(f.calc_area() < 1e-10 for f in bm.faces),
            'triangles': sum(len(f.verts) - 2 for f in bm.faces)}


def interface_only(bm, faces_x):
    """Boundary edges that do NOT lie in one of the section's interface planes."""
    bad = []
    for e in bm.edges:
        if e.is_boundary and not any(all(abs(v.co.x - x) < 1e-4 for v in e.verts) for x in faces_x):
            bad.append([tuple(round(c, 3) for c in v.co) for v in e.verts])
    return bad


# ---------------------------------------------------------------- wall pieces
def side_colour(mid_shell):
    """The connected middle's topside colour ramp: (z, rgba) samples from its side faces."""
    bm = bm_from(mid_shell)
    layer = col_layer(bm)
    bm.normal_update()
    samples = {}
    for f in bm.faces:
        if abs(f.normal.y) > .95 and f.calc_center_median().z > 1.0:
            for loop in f.loops:
                samples[round(loop.vert.co.z, 4)] = tuple(loop[layer])
    bm.free()
    ramp = sorted(samples.items())

    def at(z):
        if z <= ramp[0][0]:
            return ramp[0][1]
        for (z0, c0), (z1, c1) in zip(ramp, ramp[1:]):
            if z0 <= z <= z1:
                t = (z - z0) / (z1 - z0) if z1 > z0 else 0
                return tuple(a + t * (b - a) for a, b in zip(c0, c1))
        return ramp[-1][1]
    return at


def wall_piece(stern_shell, side_rgba):
    """Stern upper shell, stern x >= 6.0 and z >= 1.70, re-based to the middle frame."""
    bm = bm_from(stern_shell, Matrix.Translation((SHIFT, 0, 0)) @ stern_shell.matrix_basis)
    clip(bm, 0, CUT, 'hi')
    clip(bm, 2, 1.70, 'hi')
    # Aft of stern x 6.55 the stern's topsides start to tumble in (|y| 6.02 at x 6.0); a middle
    # is parallel-sided, so square the piece's sides to the middle's 6.04 there (<= 0.02 u).
    for v in bm.verts:
        if v.co.x < 3.26 and abs(v.co.y) > 5.99:
            v.co.y = math.copysign(6.04, v.co.y)
    # Astra's raised stern paints its topsides darker than the connected middle's; a middle
    # keeps the middle's topside colour along its whole length.
    layer = col_layer(bm)
    bm.normal_update()
    for f in bm.faces:
        if abs(f.normal.y) > .95 and abs(f.calc_center_median().y) > 6.0:
            for loop in f.loops:
                loop[layer] = side_rgba(loop.vert.co.z)
    # Astra's stern runs a sloped companion tunnel from the door up to a hatch room open to the
    # deck. A middle gets no hatch (it would sit where the chimney lands), so that tunnel is
    # replaced by a straight door alcove ALCOVE deep and the deck opening is planked over.
    def tunnel(f):
        c = f.calc_center_median()
        if abs(c.z - UP) < EPS and f.normal.z > .9:
            return False                      # deck
        if all(abs(v.co.x - WALL_F) < EPS for v in f.verts):
            return False                      # bulkhead
        return all(3.4 <= v.co.x <= WALL_F + EPS and abs(v.co.y) <= 2.62 and LOW - EPS <= v.co.z <= UP + EPS
                   for v in f.verts)
    bm.normal_update()
    doomed = [f for f in bm.faces if tunnel(f)]
    assert len(doomed) >= 10, len(doomed)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    rim = [e for e in bm.edges if e.is_boundary and all(abs(v.co.z - UP) < EPS and 3.4 < v.co.x < 5.2 for v in e.verts)]
    lid = bmesh.ops.holes_fill(bm, edges=rim, sides=0)['faces']
    assert len(lid) == 1, len(lid)
    lid[0].normal_update()
    if lid[0].normal.z < 0:
        lid[0].normal_flip()
    door_edges = [e for e in bm.edges if e.is_boundary and all(abs(v.co.x - WALL_F) < EPS and -2.62 < v.co.y < -.88
                                                               for v in e.verts)]
    ring = bmesh.ops.extrude_edge_only(bm, edges=door_edges)
    back = [v for v in ring['geom'] if isinstance(v, bmesh.types.BMVert)]
    for v in back:
        v.co.x -= ALCOVE
    sides = [f for f in ring['geom'] if isinstance(f, bmesh.types.BMFace)]
    back_edges = [e for e in ring['geom'] if isinstance(e, bmesh.types.BMEdge) and all(v in back for v in e.verts)]
    cap = bmesh.ops.holes_fill(bm, edges=back_edges, sides=0)['faces']
    assert len(cap) == 1, len(cap)
    left = [e for e in bm.edges if e.is_boundary and CUT + EPS < min(v.co.x for v in e.verts) and
            max(v.co.x for v in e.verts) < 6 - EPS and min(v.co.z for v in e.verts) > 1.70 + EPS]
    assert not left, [[tuple(v.co) for v in e.verts] for e in left][:6]
    paint_like_neighbour(bm, lid)
    paint_like_neighbour(bm, sides + cap)
    return bm


def cap_notches(bm):
    """Door-only wall: the two stair notches of a forward wall piece capped flat."""
    x0, x1 = NOTCH_F
    new = []
    for s in (-1, 1):
        def inside(v):
            return (x0 - EPS <= v.co.x <= x1 + EPS and BAND[0] - EPS <= s * v.co.y <= BAND[1] + EPS
                    and LOW - EPS <= v.co.z <= UP + EPS)
        doomed = [f for f in bm.faces if all(inside(v) for v in f.verts)]
        assert len(doomed) >= 4, ('notch faces', s, len(doomed))
        bmesh.ops.delete(bm, geom=doomed, context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        top = [v for v in bm.verts if v.is_boundary and inside(v) and abs(v.co.z - UP) < EPS]
        front = [v for v in bm.verts if v.is_boundary and inside(v) and abs(v.co.x - WALL_F) < EPS]
        ft = bm.faces.new(ordered(top, lambda v: v.co.x, lambda v: v.co.y))
        ft.normal_update()
        if ft.normal.z < 0:
            ft.normal_flip()
        ff = bm.faces.new(ordered(front, lambda v: v.co.y, lambda v: v.co.z))
        ff.normal_update()
        if ff.normal.x < 0:
            ff.normal_flip()
        new += [ft, ff]
    paint_like_neighbour(bm, new)
    return bm


def middle_shell(kind, mid_shell, stern_shell):
    fwd, aft = kind['fwd'], kind['aft']
    rgba = side_colour(mid_shell)
    pieces = []
    if fwd and aft:
        pieces.append(clip(bm_from(mid_shell), 2, 1.70, 'lo'))
        pieces.append(wall_piece(stern_shell, rgba))
        pieces.append(clip(mirror(cap_notches(wall_piece(stern_shell, rgba))), 0, CUT, 'lo'))
    elif fwd:
        pieces.append(clip(bm_from(mid_shell), 0, CUT, 'lo'))
        pieces.append(clip(clip(bm_from(mid_shell), 0, CUT, 'hi'), 2, 1.70, 'lo'))
        pieces.append(wall_piece(stern_shell, rgba))
    else:
        pieces.append(clip(bm_from(mid_shell), 0, 6 - CUT, 'hi'))
        pieces.append(clip(clip(bm_from(mid_shell), 0, 6 - CUT, 'lo'), 2, 1.70, 'lo'))
        pieces.append(mirror(wall_piece(stern_shell, rgba)))
    bm = weld(pieces, 'shell')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


# ---------------------------------------------------------------- generated trim
def gen_trim(kind):
    fwd, aft = kind['fwd'], kind['aft']
    rails, posts, planks = ss.Builder(), ss.Builder(), ss.Builder()
    xa = WALL_A if aft else 0.0
    xb = WALL_F if fwd else 6.0
    for sign in (-1, 1):
        y = sign * 6.04
        fore.tube(rails, [(xa, y, 4.90), (xb, y, 4.90)], .28, .25, 'teal')
        for x in (1.5, 3.0, 4.5):
            fore.box(posts, x - .085, x + .085, y - .085, y + .085, UP, 4.80, 'wood')
            fore.box(posts, x - .12, x + .12, y - .12, y + .12, 4.79, 4.94, 'iron')
        for xc, on in ((WALL_F, fwd), (WALL_A, aft)):
            if on:   # Astra's corner post at the top of a wall (stern x 9.17-9.35, cap 9.13-9.39)
                fore.box(posts, xc - .09, xc + .09, y - .09, y + .09, UP, 4.80, 'wood')
                fore.box(posts, xc - .13, xc + .13, y - .13, y + .13, 4.79, 4.95, 'iron')
        for z in (2.4, 3.0, 3.6):
            fore.tube(planks, [(xa, y + sign * .01, z), (xb, y + sign * .01, z)], .012, .016, 'seam')
    lo = 0.12 if aft else 0.0
    hi = 5.88 if fwd else 6.0
    notches = []
    if fwd == 'stairs':
        notches.append(NOTCH_F)
    if aft == 'stairs':
        notches.append(NOTCH_A)

    def in_band(y0, y1):
        return max(abs(y0), abs(y1)) > BAND[0] - .01 and min(abs(y0), abs(y1)) < BAND[1] + .01 and y0 * y1 >= 0

    for n in range(-8, 9):
        y = n * .7
        segs = [(lo, hi)]
        if in_band(y - .007, y + .007):
            for nx0, nx1 in notches:
                segs = [s for seg in segs for s in ((seg[0], min(seg[1], nx0)), (max(seg[0], nx1), seg[1]))
                        if s[1] - s[0] > .05]
        for x0, x1 in segs:
            fore.box(planks, x0, x1, y - .007, y + .007, 4.203, 4.215, 'seam')
        x = 1.5 + (n % 3) * 1.3
        if lo + .1 < x < hi - .1 and not (in_band(y, y + .7) and any(a - .05 <= x <= b + .05 for a, b in notches)):
            fore.box(planks, x - .007, x + .007, y, y + .7, 4.203, 4.215, 'seam')
    # bulkhead plank seams on the outboard face of each wall (stern kit: 9.26-9.27)
    for xw, side, on in ((WALL_F, 1, fwd), (WALL_A, -1, aft)):
        if not on:
            continue
        spans = ([(-6.04, -5.80), (-4.62, -2.97), (-1.33, 4.62), (5.80, 6.04)] if on == 'stairs'
                 else [(-6.04, -2.97), (-1.33, 6.04)])
        x0, x1 = sorted([xw, xw + side * .012])
        for y0, y1 in spans:
            for z in (2.4, 3.0, 3.6):
                fore.box(planks, x0, x1, y0, y1, z - .01, z + .01, 'seam')
    return {'UpperRails': rails, 'UpperPosts': posts, 'UpperPlanks': planks}


def door_guard(xw):
    b = ss.Builder()
    fore.tube(b, [(xw, -6.04, 4.90), (xw, 6.04, 4.90)], .28, .25, 'teal')
    for y in (-4.5, -3.0, -1.5, 0.0, 1.5, 3.0, 4.5):
        fore.box(b, xw - .07, xw + .07, y - .07, y + .07, UP, 4.80, 'wood')
    return b


# ---------------------------------------------------------------- objects
def new_object(name, me, parent, loc=(0, 0, 0), rot=(0, 0, 0)):
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    ob.location, ob.rotation_euler = loc, rot
    return ob


def finish(ob, material):
    if material is not None and not ob.data.materials:
        ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = False
    if ob.data.color_attributes.get('Col'):
        ob.data.color_attributes.active_color = ob.data.color_attributes['Col']
    return ob


def keep_components(bm, pred):
    todo, doomed = set(bm.verts), []
    while todo:
        seed = todo.pop()
        stack, group = [seed], {seed}
        while stack:
            for e in stack.pop().link_edges:
                for v in e.verts:
                    if v in todo:
                        todo.remove(v)
                        group.add(v)
                        stack.append(v)
        if not pred(group):
            doomed += list(group)
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')
    return bm


def kit_mesh(src, name, end, filt=None):
    """Astra's stern kit part re-based to a middle wall: fwd = translate, aft = mirror."""
    bm = bm_from(src, Matrix.Translation((SHIFT, 0, 0)) @ src.matrix_basis)
    if filt:
        keep_components(bm, filt)
    if end == 'aft':
        mirror(bm)
    me = to_mesh(bm, name)
    bm.free()
    return me


def door(src, name, end, parent, material):
    """The door keeps its hinge pivot as the object transform (manifest position/rotation)."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    loc = Vector(src.location) + Vector((SHIFT, 0, 0))
    rz = src.rotation_euler.z
    if end == 'aft':
        bm.transform(Matrix.Scale(-1, 4, (1, 0, 0)))
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        loc.x, rz = 6 - loc.x, -rz
    me = to_mesh(bm, name)
    bm.free()
    return finish(new_object(name, me, parent, loc, (0, 0, rz)), material)


def build_middle(label, kind, src, root, material):
    parts = {}
    shell_bm = middle_shell(kind, src['mid']['Midship_W1__Hull_Shell'], src['stern']['Hull_Shell'])
    st = stats(shell_bm)
    bad = interface_only(shell_bm, [0.0, 6.0])
    st['non_interface_boundary'] = len(bad)
    if bad:
        print('NON-INTERFACE BOUNDARY', label, bad[:12], flush=True)
    me = to_mesh(shell_bm, label + '__Hull_Shell')
    shell_bm.free()
    parts['Hull_Shell'] = finish(new_object(label + '__Hull_Shell', me, root), material)
    for comp in ('Hull_Ironwork', 'Plank_Seams'):
        o = src['mid']['Midship_W1__' + comp]
        parts[comp] = finish(new_object(label + '__' + comp, o.data.copy(), root), material)
    o = src['mid']['RaisedMiddle__UpperBraceBolts']
    parts['UpperBraceBolts'] = finish(new_object(label + '__UpperBraceBolts', o.data.copy(), root), material)
    for comp, b in gen_trim(kind).items():
        ob = ship.mesh(label + '__' + comp, b)
        ob.parent = root
        parts[comp] = finish(ob, None)
    for end in ('fwd', 'aft'):
        what = kind[end]
        if not what:
            continue
        tag = 'Fwd' if end == 'fwd' else 'Aft'
        if what == 'stairs':
            for comp in ('TwinStairs', 'StairGuards', 'StairNosings', 'ForwardGuard'):
                n = f'{label}__{"EndGuard" if comp == "ForwardGuard" else comp}_{tag}'
                parts[n.split('__')[1]] = finish(new_object(n, kit_mesh(src['stern'][comp], n, end), root), material)
        else:
            n = f'{label}__EndGuard_{tag}'
            ob = ship.mesh(n, door_guard(WALL_A if end == 'aft' else WALL_F))
            ob.parent = root
            parts[n.split('__')[1]] = finish(ob, None)
        n = f'{label}__PortalFrames_{tag}'
        parts[n.split('__')[1]] = finish(new_object(
            n, kit_mesh(src['stern']['PortalFrames'], n, end, lambda g: min(v.co.x for v in g) > 5.5), root), material)
        n = f'{label}__Door_{tag}'
        parts[n.split('__')[1]] = door(src['stern']['Door'], n, end, root, material)
    return parts, st


# ---------------------------------------------------------------- measurement
def measure(shell_ob, stairs_flights):
    """Areas above the main deck (Z 1.76) of a module-local Hull_Shell, U^2."""
    bm = bmesh.new()
    bm.from_mesh(shell_ob.data)
    clip(bm, 2, LOW, 'hi')
    bm.normal_update()
    deck = wall = bulk = wz = 0.0
    for f in bm.faces:
        a = f.calc_area()
        c = f.calc_center_median()
        n = f.normal
        if c.z < LOW + 1e-3:
            continue
        if n.z > .9 and abs(c.z - UP) < EPS:
            deck += a
            continue
        wall += a
        wz += a * c.z
        if abs(n.x) > .9:
            bulk += a
    bm.free()
    return {'deckAreaU2': deck, 'wallAreaU2': wall, 'wallCentroidZU': wz / wall if wall else 0.0,
            'bulkheadAreaU2': bulk, 'stairFlights': stairs_flights}


# ---------------------------------------------------------------- main
def load_sources():
    bpy.ops.wm.open_mainfile(filepath=str(ENDS))
    for ob in list(bpy.data.objects):
        ob.name = 'E_' + ob.name
    with bpy.data.libraries.load(str(CONNECTED), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n == 'Midship_W1' or n.startswith(('Midship_W1__', 'RaisedMiddle'))]
    mid = {ob.name: ob for ob in dst.objects}
    assert abs(mid['Midship_W1'].location.x - 9.3) < 1e-6
    stern_root = bpy.data.objects['E_Stern_W1']
    bow_root = bpy.data.objects['E_Bow_W1']

    def key(o):
        return o.name.split('__')[-1] if '__' in o.name else o.name[2:]
    stern = {key(o): o for o in stern_root.children if o.type == 'MESH'}
    bow = {key(o): o for o in bow_root.children if o.type == 'MESH'}
    material = stern['Hull_Shell'].data.materials[0]
    return {'mid': mid, 'stern': stern, 'bow': bow, 'stern_root': stern_root, 'bow_root': bow_root}, material


def export_folder(label, parts, extra):
    folder = OUT / label
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob('*.fbx'):
        old.unlink()
    entry = {}
    for ob in sorted(parts.values(), key=lambda o: o.name):
        chk = ship.check(ob)
        assert not chk['overconnected_edges'] and not chk['degenerate_faces'], (ob.name, chk)
        path = folder / (ob.name + '.fbx')
        ss.export_fbx(ob, str(path))
        entry[ob.name] = {'file': f'{label}/{ob.name}.fbx', 'position': list(ob.location),
                          'rotation_radians': list(ob.rotation_euler), 'triangles': chk['triangles']}
    manifest = {'interface': 'W1x (wall faces) / W1xR (connected faces)', 'section': label,
                'source_axes': '+X forward, +Y port, +Z up', 'export_axes': 'game (-y,z,x)',
                'modules': {label: entry}}
    manifest.update(extra)
    (folder / 'manifest.json').write_text(json.dumps(manifest, indent=2))
    return manifest


def end_module(label, root_src, comps, material):
    """Astra's raised end as authored; the hatch's pitch is baked into its exported mesh
    (VisualPart carries only a yaw), with the hinge pivot kept as the object position."""
    root = bpy.data.objects.new(label, None)
    bpy.context.scene.collection.objects.link(root)
    root.location = root_src.location
    parts = {}
    for comp, src in comps.items():
        me = src.data.copy()
        rot = src.rotation_euler.copy()
        if comp == 'Hatch':
            me.transform(Matrix.Rotation(rot.y, 4, 'Y'))
            rot = (0, 0, 0)
        parts[comp] = finish(new_object(f'{label}__{comp}', me, root, Vector(src.location), rot), material)
    return root, parts


def main():
    scratch = Path(os.environ.get('SECTIONS_SCRATCH', str(OUT)))
    src, material = load_sources()
    for ob in bpy.data.objects:
        if ob.type == 'MESH':
            ob.hide_render = True
    report = {}
    for label, kind in VARIANTS.items():
        root = bpy.data.objects.new(label, None)
        bpy.context.scene.collection.objects.link(root)
        root.location = (9.3, 0, 0)
        parts, st = build_middle(label, kind, src, root, material)
        m = measure(parts['Hull_Shell'], 2 * sum(1 for e in ('fwd', 'aft') if kind[e] == 'stairs'))
        tables = hydro.export_module(parts['Hull_Shell'], UP, HYDRO_OUT / f'{label}.json')
        report[label] = {'hull_shell_check': st, 'measure': m, 'hydrostatics': tables, 'walls': kind}
        export_folder(label, parts, {'hull_shell_check': st, 'walls': kind})
        print(label, st, m, flush=True)
    for label, root_src, comps, flights in (('SternWF', src['stern_root'], src['stern'], 2),
                                           ('BowWA', src['bow_root'], src['bow'], 0)):
        root, parts = end_module(label, root_src, comps, material)
        bm = bm_from(parts['Hull_Shell'], Matrix())
        st = stats(bm)
        bm.free()
        m = measure(parts['Hull_Shell'], flights)
        tables = hydro.export_module(parts['Hull_Shell'], UP, HYDRO_OUT / f'{label}.json')
        report[label] = {'hull_shell_check': st, 'measure': m, 'hydrostatics': tables}
        export_folder(label, parts, {'hull_shell_check': st})
        print(label, st, m, flush=True)
    (OUT / 'build-report.json').write_text(json.dumps(report, indent=2))
    for ob in list(bpy.data.objects):
        if ob.name.startswith('E_') or ob.name.startswith(('Midship_W1', 'RaisedMiddle')):
            bpy.data.objects.remove(ob, do_unlink=True)
    for ob in bpy.data.objects:
        if ob.type == 'MESH':
            ob.hide_render = False
    path = scratch / 'raised-sections.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
    b1 = Path(str(path) + '1')
    if b1.exists():
        b1.unlink()
    print('RAISED SECTIONS BUILT', path, flush=True)


if __name__ == '__main__':
    main()
