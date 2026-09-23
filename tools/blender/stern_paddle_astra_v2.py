"""SeaSick stern-paddle ship V2, rebuilt from the Astra concept reference.

Design authority, in order: rear three-quarter, direct rear, side, chimney.
The concept's top-down inset is intentionally ignored as a design source.

The wheel clearance cylinder is defined first. The hull stern, raised aft deck,
structural cheeks, crown panel, and rail are constructed around that envelope.
Crew and cannons are intentionally absent.

Run:
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        -P tools/blender/stern_paddle_astra_v2.py
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import steamer as shared


REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
MODEL_DIR = os.path.join(REPO, "Assets", "_Project", "Art", "Ship",
                         "SternPaddleAstraV2", "Models")
BLEND_PATH = os.path.join(HERE, "source", "stern-paddle-astra-v2.blend")
RENDER_DIR = os.path.join(REPO, "art-staging", "stern-paddle-astra-model-v2",
                          "renders")
HULL_FBX = os.path.join(MODEL_DIR, "stern_paddle_hull_v2.fbx")
FRAME_FBX = os.path.join(MODEL_DIR, "stern_paddle_module_frame_v2.fbx")
WHEEL_FBX = os.path.join(MODEL_DIR, "stern_paddle_wheel_v2.fbx")


# Ship frame: +x starboard, +y up, +z bow, metres.
TRANSOM_Z = -13.0
BOW_Z = 14.45
WELL_FRONT_Z = -11.20
OPEN_HALF = 2.50
WHEEL_R = 1.70
WHEEL_W = 4.60
WHEEL_AXLE = (0.0, 0.45, -13.0)

STATIONS = (
    -13.0, -12.35, -11.70, -11.20, -10.45, -9.30, -7.0, -4.0,
    -1.0, 2.0, 5.0, 7.8, 10.0, 11.8, 13.05, 13.85, 14.45,
)
PLAN_HALF = (
    4.02, 4.18, 4.34, 4.46, 4.54, 4.59, 4.62, 4.64,
    4.64, 4.61, 4.50, 4.25, 3.78, 3.08, 2.12, 1.12, 0.16,
)
LEVEL_T = (0.0, 0.16, 0.34, 0.52, 0.70, 0.85, 1.0)
WIDTH_M = (0.16, 0.52, 0.80, 0.96, 1.00, 1.025, 1.035)
PLANK_COL = ("bottom", "plankLo", "plankMid", "plankMid", "plankHi", "plankHi")


def clamp(v, lo=0.0, hi=1.0):
    return max(lo, min(hi, v))


def smoothstep(t):
    t = clamp(t)
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def sample_profile(z, values):
    if z <= STATIONS[0]:
        return values[0]
    if z >= STATIONS[-1]:
        return values[-1]
    for i in range(len(STATIONS) - 1):
        if STATIONS[i] <= z <= STATIONS[i + 1]:
            t = (z - STATIONS[i]) / (STATIONS[i + 1] - STATIONS[i])
            return lerp(values[i], values[i + 1], t)
    return values[-1]


def deck_y(z):
    aft = 0.78 * smoothstep((-z - 9.25) / (13.0 - 9.25))
    bow = 0.96 * smoothstep((z - 8.0) / (14.45 - 8.0))
    waist = 1.72 + 0.04 * (z / 14.45) ** 2
    return waist + aft + bow


def keel_y(z):
    aft = 0.25 * smoothstep((-z - 10.0) / 3.0)
    bow = 1.36 * smoothstep((z - 9.0) / (14.45 - 9.0))
    return -2.02 + aft + bow


def rail_y(z):
    return deck_y(z) + 0.86


def plan_half(z):
    return sample_profile(z, PLAN_HALF)


def half_breadth(z, y):
    ky, dy = keel_y(z), deck_y(z)
    hb = plan_half(z)
    if y >= dy:
        return hb * 1.035
    t = clamp((y - ky) / max(dy - ky, 1e-6))
    for i in range(len(LEVEL_T) - 1):
        if LEVEL_T[i] <= t <= LEVEL_T[i + 1]:
            u = (t - LEVEL_T[i]) / (LEVEL_T[i + 1] - LEVEL_T[i])
            return hb * lerp(WIDTH_M[i], WIDTH_M[i + 1], u)
    return hb * WIDTH_M[-1]


def section(z):
    ky, dy = keel_y(z), deck_y(z)
    ys = [lerp(ky, dy, t) for t in LEVEL_T] + [rail_y(z)]
    xs = [half_breadth(z, y) for y in ys]
    return ys, xs


class WeldedBuilder(shared.Builder):
    """Builder compatible with the existing exporter, with shared vertices."""

    def __init__(self):
        super().__init__()
        self._vmap = {}
        self._face_keys = set()
        self.duplicate_faces = 0

    def _index(self, p):
        key = (round(p.x, 6), round(p.y, 6), round(p.z, 6))
        idx = self._vmap.get(key)
        if idx is None:
            idx = len(self.verts)
            self._vmap[key] = idx
            self.verts.append(p)
        return idx

    def face(self, pts, col, hint=None, away=None):
        pp = []
        for p in pts:
            v = shared.S(p)
            if not pp or (v - pp[-1]).length > 1e-6:
                pp.append(v)
        if len(pp) > 1 and (pp[0] - pp[-1]).length <= 1e-6:
            pp.pop()
        if len(pp) < 3:
            return

        n = Vector((0.0, 0.0, 0.0))
        for i in range(len(pp)):
            a, c = pp[i], pp[(i + 1) % len(pp)]
            n += Vector(((a.y - c.y) * (a.z + c.z),
                         (a.z - c.z) * (a.x + c.x),
                         (a.x - c.x) * (a.y + c.y)))
        if n.length < 1e-8:
            return
        if hint is not None:
            direction = shared.S(hint)
        else:
            center = sum(pp, Vector()) / len(pp)
            direction = center - shared.S(away)
        if n.dot(direction) < 0.0:
            pp.reverse()

        face = [self._index(p) for p in pp]
        if len(set(face)) < 3:
            return
        key = tuple(sorted(face))
        if key in self._face_keys:
            self.duplicate_faces += 1
            return
        self._face_keys.add(key)
        self.faces.append(face)
        self.cols.append(shared.PAL[col] if isinstance(col, str) else col)


def build_hull_skin(b):
    """Outer hull, bulwarks, deck, and rail with one shared station cage."""
    sections = [section(z) for z in STATIONS]
    inner_edges = []

    for j in range(len(STATIONS) - 1):
        za, zb = STATIONS[j], STATIONS[j + 1]
        ya, xa = sections[j]
        yb, xb = sections[j + 1]

        for k in range(len(LEVEL_T) - 1):
            for sx in (-1.0, 1.0):
                b.face([(sx * xa[k], ya[k], za), (sx * xb[k], yb[k], zb),
                        (sx * xb[k + 1], yb[k + 1], zb),
                        (sx * xa[k + 1], ya[k + 1], za)],
                       PLANK_COL[k], away=(0.0, 0.3, 0.5 * (za + zb)))

        # Orange outer bulwark from deck to rail.
        for sx in (-1.0, 1.0):
            b.face([(sx * xa[-2], ya[-2], za), (sx * xb[-2], yb[-2], zb),
                    (sx * xb[-1], yb[-1], zb), (sx * xa[-1], ya[-1], za)],
                   "plankHi", away=(0.0, 0.5 * (ya[-1] + yb[-1]),
                                     0.5 * (za + zb)))

        # Keel strip closes the two sides, except in the paddle well.
        if za >= WELL_FRONT_Z - 1e-6:
            b.face([(-xa[0], ya[0], za), (xa[0], ya[0], za),
                    (xb[0], yb[0], zb), (-xb[0], yb[0], zb)],
                   "bottom", hint=(0, -1, 0))

        # Inboard bulwark and cap rail.
        ia = max(xa[-2] - 0.23, 0.0)
        ib = max(xb[-2] - 0.23, 0.0)
        ita = max(xa[-1] - 0.25, 0.0)
        itb = max(xb[-1] - 0.25, 0.0)
        inner_edges.append((za, ia, ya[-2])) if j == 0 else None
        inner_edges.append((zb, ib, yb[-2]))
        for sx in (-1.0, 1.0):
            b.face([(sx * ia, ya[-2], za), (sx * ita, ya[-1], za),
                    (sx * itb, yb[-1], zb), (sx * ib, yb[-2], zb)],
                   "inside", hint=(-sx, 0.1, 0))

            # Teal cap is a closed low rectangular strip.
            oa0, oa1 = xa[-1] + 0.05, ita
            ob0, ob1 = xb[-1] + 0.05, itb
            h = 0.14
            b.face([(sx * oa0, ya[-1], za), (sx * ob0, yb[-1], zb),
                    (sx * ob0, yb[-1] + h, zb), (sx * oa0, ya[-1] + h, za)],
                   "sheer", hint=(sx, 0, 0))
            b.face([(sx * oa0, ya[-1] + h, za), (sx * ob0, yb[-1] + h, zb),
                    (sx * ob1, yb[-1] + h, zb), (sx * oa1, ya[-1] + h, za)],
                   "sheer", hint=(0, 1, 0))
            b.face([(sx * oa1, ya[-1], za), (sx * oa1, ya[-1] + h, za),
                    (sx * ob1, yb[-1] + h, zb), (sx * ob1, yb[-1], zb)],
                   "sheerLo", hint=(-sx, 0, 0))

        # Deliberate longitudinal deck planks, one surface with shared edges.
        lanes = 14
        for lane in range(lanes):
            t0 = -1.0 + 2.0 * lane / lanes
            t1 = -1.0 + 2.0 * (lane + 1) / lanes
            b.face([(t0 * ia, ya[-2], za), (t0 * ib, yb[-2], zb),
                    (t1 * ib, yb[-2], zb), (t1 * ia, ya[-2], za)],
                   ("deckA", "deckB", "deckC")[lane % 3], hint=(0, 1, 0))

    # Bow stem closes every level and caps the deck/bulwark.
    z = STATIONS[-1]
    ys, xs = sections[-1]
    for k in range(len(ys) - 1):
        b.face([(-xs[k], ys[k], z), (xs[k], ys[k], z),
                (xs[k + 1], ys[k + 1], z), (-xs[k + 1], ys[k + 1], z)],
               "steel" if k >= len(ys) - 2 else PLANK_COL[min(k, 5)],
               hint=(0, 0, 1))


def build_transom_and_well(b):
    """Purpose-built stern cheeks and raised crown around the wheel envelope."""
    zt = TRANSOM_Z
    ys, xs = section(zt)
    open_lo = ys[1]
    open_hi = ys[-2]

    # Full lower sill; split structural cheeks; full upper transom band.
    for k in range(len(LEVEL_T) - 1):
        if k == 0:
            b.face([(-xs[k], ys[k], zt), (xs[k], ys[k], zt),
                    (xs[k + 1], ys[k + 1], zt), (-xs[k + 1], ys[k + 1], zt)],
                   PLANK_COL[k], hint=(0, 0, -1))
            continue
        for sx in (-1.0, 1.0):
            outer0, outer1 = xs[k], xs[k + 1]
            b.face([(sx * OPEN_HALF, ys[k], zt), (sx * outer0, ys[k], zt),
                    (sx * outer1, ys[k + 1], zt),
                    (sx * OPEN_HALF, ys[k + 1], zt)],
                   PLANK_COL[k], hint=(0, 0, -1))

    b.face([(-xs[-2], open_hi, zt), (xs[-2], open_hi, zt),
            (xs[-1], ys[-1], zt), (-xs[-1], ys[-1], zt)],
           "plankHi", hint=(0, 0, -1))

    # Dark interior walls and roof make the wheel visibly embedded.
    well_zs = (TRANSOM_Z, -12.40, -11.80, WELL_FRONT_Z)
    for i in range(len(well_zs) - 1):
        za, zb = well_zs[i], well_zs[i + 1]
        ca, cb = deck_y(za) - 0.12, deck_y(zb) - 0.12
        for sx in (-1.0, 1.0):
            b.face([(sx * OPEN_HALF, open_lo, za),
                    (sx * OPEN_HALF, open_lo, zb),
                    (sx * OPEN_HALF, cb, zb),
                    (sx * OPEN_HALF, ca, za)],
                   "inside", hint=(-sx, 0, 0))
        b.face([(-OPEN_HALF, ca, za), (OPEN_HALF, ca, za),
                (OPEN_HALF, cb, zb), (-OPEN_HALF, cb, zb)],
               "inside", hint=(0, -1, 0))

    cf = deck_y(WELL_FRONT_Z) - 0.12
    b.face([(-OPEN_HALF, open_lo, WELL_FRONT_Z),
            (OPEN_HALF, open_lo, WELL_FRONT_Z),
            (OPEN_HALF, cf, WELL_FRONT_Z),
            (-OPEN_HALF, cf, WELL_FRONT_Z)],
           "inside", hint=(0, 0, -1))

    # The orange crown fills under the raised rail. It shares the transom base.
    side_y = rail_y(zt) + 0.14
    rise = 0.72
    n = 12
    crown = []
    for i in range(n + 1):
        x = -xs[-1] + 2.0 * xs[-1] * i / n
        u = abs(x) / max(xs[-1], 1e-6)
        crown.append((x, side_y + rise * (1.0 - u * u), zt - 0.02))
    for i in range(n):
        a, c = crown[i], crown[i + 1]
        b.face([(a[0], side_y, a[2]), (c[0], side_y, c[2]), c, a],
               "plankHi", hint=(0, 0, -1))

    # Teal rail follows exactly the crown curve; square section, few facets.
    h, depth = 0.24, 0.34
    rings = []
    for x, y, z in crown:
        rings.append([(x, y - h * 0.5, z - depth * 0.5),
                      (x, y + h * 0.5, z - depth * 0.5),
                      (x, y + h * 0.5, z + depth * 0.5),
                      (x, y - h * 0.5, z + depth * 0.5)])
    for i in range(n):
        a, c = rings[i], rings[i + 1]
        b.face([a[0], c[0], c[1], a[1]], "sheer", hint=(0, 0, -1))
        b.face([a[3], a[2], c[2], c[3]], "sheer", hint=(0, 0, 1))
        b.face([a[1], c[1], c[2], a[2]], "sheer", hint=(0, 1, 0))
        b.face([a[0], a[3], c[3], c[0]], "sheerLo", hint=(0, -1, 0))
    b.face(rings[0], "sheer", hint=(-1, 0, 0))
    b.face(rings[-1], "sheer", hint=(1, 0, 0))

    # Steel cheek plates, sparse bolts, and a single top compression bar.
    plate_x = OPEN_HALF + 0.24
    plate_y0, plate_y1 = open_lo - 0.08, side_y + 0.22
    for sx in (-1.0, 1.0):
        x = sx * plate_x
        b.box(x - 0.23, x + 0.23, plate_y0, plate_y1,
              zt - 0.24, zt + 0.10, "steel")
        for t in (0.14, 0.50, 0.86):
            y = lerp(plate_y0, plate_y1, t)
            b.cyl((x, y, zt - 0.24), (x, y, zt - 0.38),
                  0.105, 0.105, 6, "bolt")
    b.box(-plate_x, plate_x, open_hi + 0.04, open_hi + 0.27,
          zt - 0.22, zt + 0.12, "steelLo")


def build_hull_detail(b):
    # One heavy rubbing strake and four structural straps per side.
    for j in range(len(STATIONS) - 1):
        za, zb = STATIONS[j], STATIONS[j + 1]
        if za < WELL_FRONT_Z:
            continue
        ya0 = lerp(keel_y(za), deck_y(za), 0.67)
        yb0 = lerp(keel_y(zb), deck_y(zb), 0.67)
        h = 0.25
        for sx in (-1.0, 1.0):
            xa = half_breadth(za, ya0) + 0.045
            xb = half_breadth(zb, yb0) + 0.045
            b.face([(sx * xa, ya0, za), (sx * xb, yb0, zb),
                    (sx * xb, yb0 + h, zb), (sx * xa, ya0 + h, za)],
                   "steel", hint=(sx, 0, 0))

    for z in (-7.2, -1.0, 5.2, 10.0):
        for sx in (-1.0, 1.0):
            ys = [lerp(keel_y(z), rail_y(z), t) for t in (0.28, 0.48, 0.68, 0.88)]
            for i in range(len(ys) - 1):
                y0, y1 = ys[i], ys[i + 1]
                x0 = half_breadth(z, y0) + 0.07
                x1 = half_breadth(z, y1) + 0.07
                b.face([(sx * x0, y0, z - 0.20), (sx * x1, y1, z - 0.20),
                        (sx * x1, y1, z + 0.20), (sx * x0, y0, z + 0.20)],
                       "steel", hint=(sx, 0, 0))
            for t in (0.36, 0.78):
                y = lerp(keel_y(z), rail_y(z), t)
                x = half_breadth(z, y) + 0.08
                b.cyl((sx * x, y, z), (sx * (x + 0.14), y, z),
                      0.09, 0.09, 6, "bolt")

    # Frame-top blocks on the rail, deliberately sparse.
    for z in (-8.8, -4.2, 0.4, 5.0, 9.1, 12.2):
        x = half_breadth(z, rail_y(z)) - 0.12
        y = rail_y(z) + 0.12
        for sx in (-1.0, 1.0):
            b.box(sx * x - 0.18, sx * x + 0.18, y, y + 0.38,
                  z - 0.25, z + 0.25, "steel")


def build_chimney_and_helm(b):
    # Chimney: restrained octagonal stack with stepped foot and flared lip.
    z = 0.6
    y = deck_y(z)
    b.box(-0.72, 0.72, y, y + 0.24, z - 0.72, z + 0.72,
          "steelLo", top="steel")
    b.cyl((0, y + 0.24, z), (0, y + 4.72, z),
          0.48, 0.39, 8, "steel", cap0=False, cap1=False,
          phase=math.pi / 8.0)
    b.cyl((0, y + 4.72, z), (0, y + 5.04, z),
          0.45, 0.59, 8, "steel", cap0=False, cap1=True,
          cap_col="inside", phase=math.pi / 8.0)

    # Helm immediately ahead of the aft rise.
    z = -8.35
    y = deck_y(z)
    b.prism_x([(z - 0.34, y), (z + 0.34, y),
               (z + 0.25, y + 0.78), (z - 0.25, y + 0.78)],
              -0.22, 0.22, "teak")
    hub_y = y + 1.14
    hub_z = z
    b.cyl((-0.10, hub_y, hub_z), (0.10, hub_y, hub_z),
          0.13, 0.13, 8, "steel")
    for k in range(8):
        a0 = 2.0 * math.pi * k / 8.0
        a1 = 2.0 * math.pi * (k + 1) / 8.0
        p = lambda a, r: (0.0, hub_y + math.sin(a) * r, hub_z + math.cos(a) * r)
        b.beam(p(a0, 0.51), p(a1, 0.51), 0.065, "teak", caps=False)
        b.beam(p(a0, 0.08), p(a0, 0.64), 0.055, "teak")


def build_wheel(b):
    """Rotating wheel at local origin, deliberately broad and readable."""
    hw = WHEEL_W * 0.5
    floats = 8
    depth = 0.56
    ring_r = WHEEL_R - depth * 0.52

    b.cyl((-hw - 0.36, 0, 0), (hw + 0.36, 0, 0),
          0.18, 0.18, 8, "hub")
    for sx in (-1.0, 1.0):
        b.cyl((sx * (hw - 0.12), 0, 0), (sx * (hw + 0.34), 0, 0),
              0.43, 0.37, 8, "steel", phase=math.pi / 8.0)

    for xs in (-hw + 0.24, hw - 0.24):
        for k in range(floats):
            a0 = 2.0 * math.pi * k / floats
            a1 = 2.0 * math.pi * (k + 1) / floats
            p0 = (xs, math.sin(a0) * ring_r, math.cos(a0) * ring_r)
            p1 = (xs, math.sin(a1) * ring_r, math.cos(a1) * ring_r)
            b.beam((xs, 0, 0), p0, 0.11, "hub", caps=False)
            b.beam(p0, p1, 0.13, "steel", caps=False)

    for k in range(floats):
        a = 2.0 * math.pi * k / floats
        radial = Vector((0.0, math.sin(a), math.cos(a)))
        tangent = Vector((0.0, math.cos(a), -math.sin(a)))
        inner = radial * (WHEEL_R - depth)
        outer = radial * WHEEL_R
        thick = 0.09
        pts = []
        for c in (inner, outer):
            for x in (-hw, hw):
                for sn in (-thick, thick):
                    pts.append(tuple(c + tangent * sn + Vector((x, 0, 0))))
        faces = ((0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4),
                 (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5))
        b.solid([[pts[i] for i in f] for f in faces], "blade")


def build_module_frame(b):
    """Minimal non-rotating bearings and locking bridge at local axle origin."""
    hw = WHEEL_W * 0.5
    for sx in (-1.0, 1.0):
        x = sx * (hw + 0.28)
        b.cyl((x - sx * 0.18, 0, -0.12), (x + sx * 0.18, 0, -0.12),
              0.50, 0.50, 8, "steelLo", phase=math.pi / 8.0)
        b.box(x - 0.20, x + 0.20, -0.55, 0.55, -0.02, 0.44, "steel")
        b.box(x - 0.18, x + 0.18, 0.38, WHEEL_R + 0.30,
              0.18, 0.48, "steel")
        b.cyl((x, WHEEL_R + 0.08, 0.40), (x, WHEEL_R + 0.08, 0.72),
              0.14, 0.14, 8, "bolt")
    x = hw + 0.28
    b.box(-x - 0.18, x + 0.18, WHEEL_R + 0.08, WHEEL_R + 0.32,
          0.18, 0.48, "steelLo")


def make_instance(name, source, parent):
    ob = bpy.data.objects.new(name, source.data)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    return ob


def builder_checks(label, b):
    assert len(b.faces) == len(b.cols)
    assert b.duplicate_faces == 0, "%s has duplicate faces" % label
    assert all(len(set(face)) >= 3 for face in b.faces)
    print("CHECK %s verts=%d faces=%d tris=%d duplicates=%d" %
          (label, len(b.verts), len(b.faces), b.tri_count(), b.duplicate_faces))


def object_checks(label, ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    degenerate = sum(1 for f in bm.faces if f.calc_area() < 1e-9)
    boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    nonmanifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()
    assert degenerate == 0, "%s has degenerate faces" % label
    assert nonmanifold == 0, "%s has edges shared by more than two faces" % label
    print("CHECK %s degenerate=%d boundary=%d overconnected=%d" %
          (label, degenerate, boundary, nonmanifold))


def build_all():
    hull = WeldedBuilder()
    build_hull_skin(hull)
    build_transom_and_well(hull)
    build_hull_detail(hull)
    build_chimney_and_helm(hull)

    frame = WeldedBuilder()
    build_module_frame(frame)

    wheel = WeldedBuilder()
    build_wheel(wheel)

    builder_checks("hull", hull)
    builder_checks("frame", frame)
    builder_checks("wheel", wheel)

    mat = shared.make_material("AstraV2Paint", shared.PAL["plankMid"])
    ob_hull = shared.to_object("AstraV2_Hull", hull, mat)
    ob_frame = shared.to_object("AstraV2_WheelModuleFrame", frame, mat)
    ob_wheel = shared.to_object("AstraV2_PaddleWheel", wheel, mat)
    object_checks("hull", ob_hull)
    object_checks("frame", ob_frame)
    object_checks("wheel", ob_wheel)

    socket = bpy.data.objects.new("WheelModuleSocket", None)
    socket.empty_display_type = "ARROWS"
    socket.empty_display_size = 0.75
    socket.location = shared.S(WHEEL_AXLE)
    socket["ship_x"] = WHEEL_AXLE[0]
    socket["ship_y"] = WHEEL_AXLE[1]
    socket["ship_z"] = WHEEL_AXLE[2]
    socket["clearance_radius"] = WHEEL_R
    socket["clearance_width"] = WHEEL_W
    bpy.context.scene.collection.objects.link(socket)

    fitted_frame = make_instance("WheelModuleFrame_Fitted", ob_frame, socket)
    fitted_wheel = make_instance("PaddleWheel_Rotating", ob_wheel, socket)
    ob_frame.hide_render = ob_frame.hide_viewport = True
    ob_wheel.hide_render = ob_wheel.hide_viewport = True

    top_gap = deck_y(TRANSOM_Z) - 0.12 - (WHEEL_AXLE[1] + WHEEL_R)
    side_gap = OPEN_HALF - WHEEL_W * 0.5
    front_gap = WELL_FRONT_Z - (WHEEL_AXLE[2] + WHEEL_R)
    assert min(top_gap, side_gap, front_gap) > 0.05
    print("CLEARANCE top=%.3f side=%.3f front=%.3f" %
          (top_gap, side_gap, front_gap))
    print("TOTAL_TRIS", hull.tri_count() + frame.tri_count() + wheel.tri_count())
    return ob_hull, ob_frame, ob_wheel


def render_views(scene):
    os.makedirs(RENDER_DIR, exist_ok=True)
    shared.setup_render(scene)
    world = scene.world
    world.color = shared.srgb_to_linear(shared._hex("#4A9FE7"))
    shading = scene.display.shading
    try:
        shading.background_type = "WORLD"
    except Exception:
        pass

    cam_data = bpy.data.cameras.new("AstraV2_Camera")
    cam = bpy.data.objects.new("AstraV2_Camera", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.clip_end = 500.0

    def aim(eye, target):
        cam.location = Vector(eye)
        cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()

    views = (
        # Reference-authoritative views.
        ("rear34", (-29.0, -20.0, 10.5), (-7.0, 0.0, 1.2), None, 52, (1500, 980)),
        ("rear", (-40.0, 0.0, 3.0), (-11.0, 0.0, 1.2), None, 58, (1100, 900)),
        ("side", (0.0, -67.0, 2.5), (0.0, 0.0, 1.0), 35.0, 50, (1500, 650)),
        ("front34", (30.0, -20.0, 10.0), (3.0, 0.0, 1.0), None, 52, (1500, 980)),
        # Diagnostic only, never a design source.
        ("diagnostic_plan", (0.0, 0.0, 80.0), (0.0, 0.0, 0.0), 35.0, 50, (1500, 650)),
        ("diagnostic_understern", (-24.0, -15.0, -7.5), (-11.0, 0.0, 0.2), None, 48, (1500, 980)),
    )

    cam_types = shared.enum_ids(cam_data.bl_rna.properties["type"])
    paths = []
    for name, eye, target, ortho, lens, resolution in views:
        if ortho is not None:
            cam_data.type = shared.pick(cam_types, "ORTHO")
            cam_data.ortho_scale = ortho
        else:
            cam_data.type = shared.pick(cam_types, "PERSP")
            cam_data.lens = lens
        aim(eye, target)
        if name == "diagnostic_plan":
            cam.rotation_euler = (0.0, 0.0, 0.0)
        scene.render.resolution_x, scene.render.resolution_y = resolution
        scene.render.resolution_percentage = 100
        path = os.path.join(RENDER_DIR, "astra_v2_%s.png" % name)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        paths.append(path)
    return paths


def main():
    print("Blender", bpy.app.version_string)
    shared.clear_scene()
    scene = bpy.context.scene
    hull, frame, wheel = build_all()

    os.makedirs(MODEL_DIR, exist_ok=True)
    shared.export_fbx(hull, HULL_FBX)
    shared.export_fbx(frame, FRAME_FBX)
    shared.export_fbx(wheel, WHEEL_FBX)

    for path in render_views(scene):
        print("RENDER", path)

    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print("BLEND", BLEND_PATH)


if __name__ == "__main__":
    main()
