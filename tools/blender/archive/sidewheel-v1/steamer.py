"""Side-wheel paddle steamer -- mesh, renders, FBX export.

    /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/blender/steamer.py -- \
        [--renders DIR] [--debug-views] [--no-render] [--no-export] [--blend FILE]

The hull is lofted from `steamer_form.HullForm.half_breadth`, the SAME function
the strip tables in Resources/Steamer/hullform.json are integrated from, so the
mesh and the physics are one shape (docs/steamer-spec.md).

Everything is authored in the SHIP frame (= Unity ship-local: +x starboard,
+y up, +z bow, origin at centreline / waterline / LWL midpoint) and mapped to
Blender's authoring frame (bow +X, port +Y, up +Z) by `S()`. Face winding is
never trusted: every face is given a direction its normal must agree with.

Colour is per-face VERTEX COLOUR only (attribute "Col", byte / corner). The
picks below are sRGB; they are written through `.color` (scene-linear) and the
FBX is exported with colors_type LINEAR, so Unity receives LINEAR values -- the
game shader does no sRGB conversion.

Export uses the chain measured in seasick_hulls.py: yaw -90 deg about Blender Z
(burnt into the vertices here, so the node is identity), bake_space_transform,
FBX_SCALE_ALL, -Z forward / Y up, top-level objects only.
"""
import math
import os
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import importlib
import steamer_form
importlib.reload(steamer_form)

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(REPO, "Assets", "_Project", "Resources", "Steamer")
HULL_FBX = os.path.join(OUT_DIR, "steamer_hull.fbx")
WHEEL_FBX = os.path.join(OUT_DIR, "steamer_wheel.fbx")
ATTR = "Col"


# ------------------------------------------------------------------ colour --
def _hex(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_linear(c):
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
                 for v in c)


PAL = {k: _hex(v) for k, v in {
    "hull": "#1A3327",        # black-green
    "bottom": "#14241C",
    "boot": "#A9332A",        # red boot-top
    "cream": "#EADDB9",       # sheer strake
    "rail": "#7A5433",        # rail cap, teak
    "deckA": "#BD9160", "deckB": "#B48755", "deckC": "#C59A69",
    "margin": "#94693F",
    "teak": "#8A5B34",
    "white": "#F1EEE4",
    "red": "#B5332B",
    "gold": "#E2AA3C",
    "buff": "#D8A766",
    "black": "#1B1B1C",
    "inside": "#2A2623",
    "iron": "#7C2C23",        # red-oxide wheel iron
    "float": "#9B7347",
    "brass": "#C89B3C",
    "glow": "#FFD88A",
    "glass": "#9FC3D1",
    "window": "#22384B",
    "canvas": "#5E7268",
    "spar": "#B98B57",
    "sea": "#5A9EC2",
}.items()}


# ----------------------------------------------------------------- builder --
def S(p):
    """Ship frame (x stbd, y up, z bow) -> Blender (bow +X, port +Y, up +Z)."""
    return Vector((p[2], -p[0], p[1]))


class Builder(object):
    def __init__(self):
        self.verts = []
        self.faces = []
        self.cols = []

    def face(self, pts, col, hint=None, away=None):
        P = []
        for p in pts:
            v = S(p)
            if not P or (v - P[-1]).length > 1e-5:
                P.append(v)
        if len(P) > 1 and (P[0] - P[-1]).length <= 1e-5:
            P.pop()
        if len(P) < 3:
            return
        n = Vector((0.0, 0.0, 0.0))
        for i in range(len(P)):
            a, b = P[i], P[(i + 1) % len(P)]
            n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x),
                         (a.x - b.x) * (a.y + b.y)))
        if n.length < 1e-7:
            return
        if hint is not None:
            h = S(hint)
        else:
            c = Vector((0.0, 0.0, 0.0))
            for v in P:
                c += v
            h = c / len(P) - S(away)
        if n.dot(h) < 0.0:
            P.reverse()
        i0 = len(self.verts)
        self.verts.extend(P)
        self.faces.append(list(range(i0, i0 + len(P))))
        self.cols.append(PAL[col] if isinstance(col, str) else col)

    def solid(self, faces, col):
        """Convex solid: every face turned away from the common centroid."""
        pts = [p for f in faces for p in f]
        c = tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))
        for k, f in enumerate(faces):
            self.face(f, col[k] if isinstance(col, (list, tuple)) and
                      not isinstance(col[0], float) else col, away=c)

    def box(self, x0, x1, y0, y1, z0, z1, col, top=None, skip=()):
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
             (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        quads = {"-z": (0, 1, 2, 3), "+z": (4, 5, 6, 7), "-y": (0, 1, 5, 4),
                 "+y": (3, 2, 6, 7), "-x": (0, 3, 7, 4), "+x": (1, 2, 6, 5)}
        ctr = (0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1))
        for k, q in quads.items():
            if k in skip:
                continue
            self.face([c[i] for i in q], top if (k == "+y" and top) else col,
                      away=ctr)

    def cyl(self, p0, p1, r0, r1, n, col, cap0=True, cap1=True, cap_col=None,
            phase=0.0):
        a, b = Vector(p0), Vector(p1)
        d = (b - a).normalized()
        u = d.cross(Vector((0, 1, 0)))
        if u.length < 1e-4:
            u = d.cross(Vector((1, 0, 0)))
        u.normalize()
        v = d.cross(u)
        ring0, ring1, dirs = [], [], []
        for i in range(n):
            t = phase + 2.0 * math.pi * i / n
            e = u * math.cos(t) + v * math.sin(t)
            dirs.append(e)
            ring0.append(tuple(a + e * r0))
            ring1.append(tuple(b + e * r1))
        for i in range(n):
            j = (i + 1) % n
            self.face([ring0[i], ring0[j], ring1[j], ring1[i]], col,
                      hint=tuple(dirs[i] + dirs[j]))
        cc = cap_col or col
        if cap0 and r0 > 1e-6:
            self.face(ring0, cc, hint=tuple(-d))
        if cap1 and r1 > 1e-6:
            self.face(ring1, cc, hint=tuple(d))

    def beam(self, p0, p1, w, col, caps=True):
        self.cyl(p0, p1, w * 0.7071, w * 0.7071, 4, col, caps, caps,
                 phase=math.pi / 4.0)

    def prism_x(self, poly_zy, x0, x1, col, cap_col=None):
        """Convex (z, y) polygon extruded along x."""
        n = len(poly_zy)
        cz = sum(p[0] for p in poly_zy) / n
        cy = sum(p[1] for p in poly_zy) / n
        ctr = (0.5 * (x0 + x1), cy, cz)
        for i in range(n):
            (za, ya), (zb, yb) = poly_zy[i], poly_zy[(i + 1) % n]
            self.face([(x0, ya, za), (x0, yb, zb), (x1, yb, zb), (x1, ya, za)],
                      col, away=ctr)
        cc = cap_col or col
        self.face([(x0, y, z) for z, y in poly_zy], cc, hint=(x0 - x1, 0, 0))
        self.face([(x1, y, z) for z, y in poly_zy], cc, hint=(x1 - x0, 0, 0))

    def tri_count(self):
        return sum(len(f) - 2 for f in self.faces)


# -------------------------------------------------------------------- hull --
LOW_T = (0.0, 0.05, 0.15, 0.35, 0.65, 1.0)
BOOT_LO, BOOT_HI = -0.25, 0.30
TB = 0.12                                   # bulwark thickness


def hull_stations(form):
    z0 = form.z_aft(5.0) + 0.002
    z1 = form.half_l
    for _ in range(30):                      # stem head: z = z_fwd(rail(z))
        z1 = form.z_fwd(form.shell_top_y(z1))
    z1 -= 0.002
    n = 44
    zs = []
    for i in range(n + 1):
        u = float(i) / n
        zs.append(z0 + (z1 - z0) * (0.55 * u + 0.45 * (0.5 - 0.5 * math.cos(math.pi * u))))
    zs += [z0 + d for d in (0.04, 0.12, 0.3)]           # the round of the stern
    zs += [z1 - d for d in (0.12,)]
    zs = sorted(zs)
    k = min(range(len(zs)), key=lambda i: abs(zs[i] - form.break_z))
    zs[k] = form.break_z
    out = [zs[0]]
    for z in zs[1:]:
        if z - out[-1] > 0.03:
            out.append(z)
    return out


def hull_rows(form, z, fc, closed=False):
    """(y, half-breadth) rows of the outer shell at station z, keel to rail.
    `closed`: the sternpost station, where port and starboard must meet."""
    keel = form.hull_keel_y(z)
    ymd = form.main_deck_y(z)
    top = ymd + form.bulwark + (form.fc_bulwark if fc else 0.0)
    ys = [keel + (BOOT_LO - keel) * t for t in LOW_T]
    ys += [BOOT_HI, BOOT_HI + (ymd - BOOT_HI) * 0.36, BOOT_HI + (ymd - BOOT_HI) * 0.70,
           ymd, ymd + 0.5 * form.bulwark, ymd + form.bulwark, top]
    rows = []
    for y in ys:
        y = min(max(y, keel), top)
        hb = form.half_breadth(z, y, skeg=False)
        if y <= keel + 1e-6:
            hb = form.f["stemHalfThickness"] if z > form.f["parallelFwdZ"] else 0.0
        rows.append((y, 0.0 if closed else hb))
    return rows


BAND_COL = ["bottom"] * 5 + ["boot", "hull", "hull", "hull", "hull", "cream", "hull"]
DECK_TONES = ("deckA", "deckB", "deckC", "deckB", "deckA", "deckC")
PLANK_W = 0.42
MARGIN_W = 0.28


def build_hull(b, form):
    zs = hull_stations(form)
    for j in range(len(zs) - 1):
        za, zb = zs[j], zs[j + 1]
        fc = 0.5 * (za + zb) > form.break_z
        ra, rb = hull_rows(form, za, fc, j == 0), hull_rows(form, zb, fc)
        zm = 0.5 * (za + zb)
        for sx in (1.0, -1.0):
            for k in range(len(ra) - 1):
                b.face([(sx * ra[k][1], ra[k][0], za), (sx * ra[k + 1][1], ra[k + 1][0], za),
                        (sx * rb[k + 1][1], rb[k + 1][0], zb), (sx * rb[k][1], rb[k][0], zb)],
                       BAND_COL[k],
                       # outward = away from a point inboard, above and TOWARDS MIDSHIPS of
                       # the face: near the ends the shell faces mostly fore/aft, not abeam
                       away=(0.0, 0.5 * (ra[k][0] + rb[k + 1][0]) + 1.0,
                             zm - math.copysign(1.5, zm)))
        # stem / keel bar between the two sides
        b.face([(-ra[0][1], ra[0][0], za), (ra[0][1], ra[0][0], za),
                (rb[0][1], rb[0][0], zb), (-rb[0][1], rb[0][0], zb)],
               "hull" if ra[0][0] > BOOT_HI else "bottom", hint=(0, -1, 0.2 if zm > 0 else -0.2))

        # rail cap, inner bulwark, deck
        def edge(z):
            yd = form.main_deck_y(z) + (form.bulwark if fc else 0.0)
            yt = yd + (form.fc_bulwark if fc else form.bulwark)
            keel = form.hull_keel_y(z)
            ho = form.half_breadth(z, yt, skeg=False) if yt > keel else 0.0
            hd = form.half_breadth(z, yd, skeg=False) if yd > keel else 0.0
            if z <= zs[0]:
                ho = hd = 0.0
            hi_t = max(ho - TB, 0.0)
            hi_d = min(max(hd - TB, 0.0), hi_t) if ho > 0 else 0.0
            return yd, yt, ho, hi_t, hi_d
        ea, eb = edge(za), edge(zb)
        for sx in (1.0, -1.0):
            b.face([(sx * ea[2], ea[1], za), (sx * ea[3], ea[1], za),
                    (sx * eb[3], eb[1], zb), (sx * eb[2], eb[1], zb)], "rail", hint=(0, 1, 0))
            b.face([(sx * ea[3], ea[1], za), (sx * ea[4], ea[0], za),
                    (sx * eb[4], eb[0], zb), (sx * eb[3], eb[1], zb)],
                   "cream" if not fc else "hull", hint=(-sx, 0.05, 0))
            # margin plank, then fore-and-aft planks in three tones
            ma, mb = max(ea[4] - MARGIN_W, 0.0), max(eb[4] - MARGIN_W, 0.0)
            b.face([(sx * ma, ea[0], za), (sx * ea[4], ea[0], za),
                    (sx * eb[4], eb[0], zb), (sx * mb, eb[0], zb)], "margin", hint=(0, 1, 0))
            nk = int(math.ceil(max(ma, mb) / PLANK_W))
            for k in range(nk):
                a0, a1 = min(k * PLANK_W, ma), min((k + 1) * PLANK_W, ma)
                b0, b1 = min(k * PLANK_W, mb), min((k + 1) * PLANK_W, mb)
                b.face([(sx * a0, ea[0], za), (sx * a1, ea[0], za),
                        (sx * b1, eb[0], zb), (sx * b0, eb[0], zb)],
                       DECK_TONES[k % len(DECK_TONES)], hint=(0, 1, 0))

    # forecastle break: bulkhead, and the ends of the forecastle bulwark
    z = form.break_z
    ymd = form.main_deck_y(z)
    yfd = ymd + form.bulwark
    w0 = form.half_breadth(z, ymd) - TB
    w1 = form.half_breadth(z, yfd) - TB
    b.face([(-w0, ymd, z), (w0, ymd, z), (w1, yfd, z), (-w1, yfd, z)], "white", hint=(0, 0, -1))
    b.face([(-0.45, ymd, z - 0.015), (0.45, ymd, z - 0.015), (0.45, ymd + 0.82, z - 0.015),
            (-0.45, ymd + 0.82, z - 0.015)], "teak", hint=(0, 0, -1))
    for sx in (1.0, -1.0):
        ho = form.half_breadth(z, yfd + form.fc_bulwark)
        b.face([(sx * (ho - TB), yfd, z), (sx * ho, yfd, z),
                (sx * ho, yfd + form.fc_bulwark, z), (sx * (ho - TB), yfd + form.fc_bulwark, z)],
               "hull", hint=(0, 0, -1))
        # three steps up to the forecastle
        for i in range(3):
            h = (i + 1) * (form.bulwark / 4.0)
            b.box(sx * 1.7 - 0.4, sx * 1.7 + 0.4, ymd, ymd + h,
                  z - 0.3 * (3 - i), z - 0.3 * (2 - i), "teak")


def build_skeg_rudder(b, form):
    sk = form.f["skeg"]
    t = sk["halfThickness"]
    n = 10
    zs = [sk["zAft"] + (sk["zFwd"] - sk["zAft"]) * i / n for i in range(n + 1)]
    bot = -form.T
    tops = [min(form.hull_keel_y(z) + 0.12, 0.0) for z in zs]
    for i in range(n):
        za, zb, ta, tb_ = zs[i], zs[i + 1], tops[i], tops[i + 1]
        for sx in (1.0, -1.0):
            b.face([(sx * t, bot, za), (sx * t, ta, za), (sx * t, tb_, zb), (sx * t, bot, zb)],
                   "bottom", hint=(sx, 0, 0))
        b.face([(-t, bot, za), (t, bot, za), (t, bot, zb), (-t, bot, zb)], "bottom", hint=(0, -1, 0))
    b.face([(-t, bot, zs[0]), (t, bot, zs[0]), (t, tops[0], zs[0]), (-t, tops[0], zs[0])],
           "bottom", hint=(0, 0, -1))
    # rudder blade + stock; the centre of effort sits at the spec's (0, -1.2, -16.3)
    zr0, zr1 = sk["zAft"] - 0.04, -17.15
    poly = [(zr0, bot + 0.04), (zr1, bot + 0.2), (zr1, -0.5), (zr0, -0.98)]
    b.prism_x(poly, -0.06, 0.06, "bottom")
    b.cyl((0, -0.95, zr0 - 0.1), (0, 0.4, zr0 - 0.1), 0.09, 0.09, 6, "bottom")


def build_bilge_keels(b, form):
    y0 = -1.72
    zs = [-7.5 + i * 1.0 for i in range(14)]
    for sx in (1.0, -1.0):
        secs = []
        for i, z in enumerate(zs):
            d = 0.34 * (0.0 if i in (0, len(zs) - 1) else 1.0)
            xa = form.half_breadth(z, y0 + 0.07) - 0.03
            xb = form.half_breadth(z, y0 - 0.07) - 0.03
            xm = form.half_breadth(z, y0)
            secs.append(((sx * xa, y0 + 0.07, z), (sx * xb, y0 - 0.07, z),
                         (sx * (xm + d * 0.72), y0 - d * 0.72, z)))
        for i in range(len(secs) - 1):
            a, c = secs[i], secs[i + 1]
            b.face([a[0], a[2], c[2], c[0]], "bottom", hint=(sx * 0.5, 1, 0))
            b.face([a[1], a[2], c[2], c[1]], "bottom", hint=(sx * 0.5, -1, 0))


# ---------------------------------------------------------- guards & boxes --
BOX_R = 2.6
BOX_N = 13


def guard_outer(form, z, x_max):
    """Plan of the paddle guards: swelling out of the hull side to the box."""
    def ss(t):
        t = min(max(t, 0.0), 1.0)
        return t * t * (3.0 - 2.0 * t)
    hb = form.half_breadth(z, form.main_deck_y(z))
    aft = ss((z + 9.0) / 6.8)
    fwd = ss((form.break_z - z) / 5.2)
    return hb + (x_max - hb) * min(aft, fwd)


def build_guards(b, form, d):
    ax = d["wheelAxle"]
    x_max = 0.5 * d["beamOverGuards"] - 0.05
    half = math.sqrt(BOX_R ** 2 - (form.main_deck_y(ax["z"]) - ax["y"]) ** 2)
    spans = ((-9.0, ax["z"] - half, 9), (ax["z"] + half, form.break_z, 8))
    for z0, z1, n in spans:
        zs = [z0 + (z1 - z0) * i / n for i in range(n + 1)]
        for sx in (1.0, -1.0):
            secs = []
            for z in zs:
                yt = form.main_deck_y(z)
                xo = guard_outer(form, z, x_max)
                xi = form.half_breadth(z, yt) - 0.04
                w = max(xo - xi, 0.0)
                yb = yt - 0.30 - 0.42 * w
                xib = form.half_breadth(z, yb) - 0.04
                secs.append((z, yt, xo, xi, yb, xib, w))
            for i in range(n):
                a, c = secs[i], secs[i + 1]
                if max(a[6], c[6]) < 0.06:
                    continue
                q = lambda s, x, y: (sx * x, y, s[0])
                b.face([q(a, a[3], a[1]), q(a, a[2], a[1]), q(c, c[2], c[1]), q(c, c[3], c[1])],
                       "deckB", hint=(0, 1, 0))
                b.face([q(a, a[2], a[1]), q(a, a[2], a[1] - 0.30), q(c, c[2], c[1] - 0.30),
                        q(c, c[2], c[1])], "cream", hint=(sx, 0, 0))
                b.face([q(a, a[2], a[1] - 0.30), q(a, a[5], a[4]), q(c, c[5], c[4]),
                        q(c, c[2], c[1] - 0.30)], "hull", hint=(sx * 0.3, -1, 0))
                if min(a[6], c[6]) > 0.5:    # toe rail where the guard is wide enough to walk
                    ta, tc = a[2] - 0.09, c[2] - 0.09
                    b.face([q(a, a[2], a[1]), q(a, a[2], a[1] + 0.2), q(c, c[2], c[1] + 0.2),
                            q(c, c[2], c[1])], "cream", hint=(sx, 0, 0))
                    b.face([q(a, ta, a[1]), q(a, ta, a[1] + 0.2), q(c, tc, c[1] + 0.2),
                            q(c, tc, c[1])], "cream", hint=(-sx, 0, 0))
                    b.face([q(a, ta, a[1] + 0.2), q(a, a[2], a[1] + 0.2), q(c, c[2], c[1] + 0.2),
                            q(c, tc, c[1] + 0.2)], "rail", hint=(0, 1, 0))
            for s, hz in ((secs[0], -1), (secs[-1], 1)):
                if s[6] > 0.06:
                    b.face([(sx * s[3], s[1], s[0]), (sx * s[2], s[1], s[0]),
                            (sx * s[2], s[1] - 0.30, s[0]), (sx * s[5], s[4], s[0])],
                           "hull", hint=(0, 0, hz))


def build_paddle_boxes(b, form, d):
    ax = d["wheelAxle"]
    cz, cy = ax["z"], ax["y"]
    x_out = 0.5 * d["beamOverGuards"]
    x_in = 3.70

    def P(x, r, i):
        t = math.pi * i / BOX_N
        return (x, cy + r * math.sin(t), cz + r * math.cos(t))
    r_hub, r_vent, r_arc = 0.78, 1.92, 2.14
    for sx in (1.0, -1.0):
        xo, xi = sx * x_out, sx * x_in
        out, inn = (sx, 0, 0), (-sx, 0, 0)
        for i in range(BOX_N):
            j = i + 1
            # outer face: gold hub, red rays with open vents between, gold arc, white rim
            b.face([(xo, cy, cz), P(xo, r_hub, i), P(xo, r_hub, j)], "gold", hint=out)
            if i % 2 == 0:
                b.face([P(xo, r_hub, i), P(xo, r_vent, i), P(xo, r_vent, j), P(xo, r_hub, j)],
                       "red", hint=out)
            else:                            # vent: reveal the thickness of the planking
                for k, sg in ((i, 1.0), (j, -1.0)):
                    tk = math.pi * k / BOX_N
                    b.face([P(xo, r_hub, k), P(xo, r_vent, k), P(xo - sx * 0.08, r_vent, k),
                            P(xo - sx * 0.08, r_hub, k)], "inside",
                           hint=(0, sg * math.cos(tk), -sg * math.sin(tk)))
            b.face([P(xo, r_vent, i), P(xo, r_arc, i), P(xo, r_arc, j), P(xo, r_vent, j)],
                   "gold", hint=out)
            b.face([P(xo, r_arc, i), P(xo, BOX_R, i), P(xo, BOX_R, j), P(xo, r_arc, j)],
                   "white", hint=out)
            # curved top, outside and inside
            tm = math.pi * (i + 0.5) / BOX_N
            rad = (0, math.sin(tm), math.cos(tm))
            b.face([P(xi, BOX_R, i), P(xo, BOX_R, i), P(xo, BOX_R, j), P(xi, BOX_R, j)],
                   "white", hint=rad)
            b.face([P(xi, BOX_R - 0.06, i), P(xo, BOX_R - 0.06, i), P(xo, BOX_R - 0.06, j),
                    P(xi, BOX_R - 0.06, j)], "inside", hint=tuple(-v for v in rad))
            # inboard wall (deck side) and the dark wall behind the wheel
            b.face([(xi, cy, cz), P(xi, BOX_R, i), P(xi, BOX_R, j)], "white", hint=inn)
            b.face([(xi + sx * 0.04, cy, cz), P(xi + sx * 0.04, BOX_R, i),
                    P(xi + sx * 0.04, BOX_R, j)], "inside", hint=out)
            if i % 2 == 0:                   # back of the outer planking, seen through a vent
                b.face([(xo - sx * 0.08, cy, cz), P(xo - sx * 0.08, BOX_R, i),
                        P(xo - sx * 0.08, BOX_R, j)], "inside", hint=inn)
        # spring beam under the outer wall, carrying the outboard bearing
        b.box(min(xo, xo - sx * 0.22), max(xo, xo - sx * 0.22), cy - 0.34, cy,
              cz - BOX_R, cz + BOX_R, "cream")
        b.box(min(xo - sx * 0.02, xo - sx * 0.4), max(xo - sx * 0.02, xo - sx * 0.4),
              cy - 0.3, cy + 0.3, cz - 0.32, cz + 0.32, "black")


# ------------------------------------------------------------------ wheel --
def build_wheel(b, d):
    """One wheel about ITS OWN origin: axle along ship x (= Blender Y)."""
    R = d["wheelRadius"]
    W = d["wheelWidth"]
    nf = int(d["wheelFloats"])
    fd = d["wheelFloatDepth"]
    b.cyl((-W * 0.5 - 0.05, 0, 0), (W * 0.5 + 0.05, 0, 0), 0.2, 0.2, 8, "black")
    rim_x = W * 0.5 - 0.14
    r_out, r_in = R - 0.5 * fd, 1.2
    for xs in (-rim_x, rim_x):
        b.cyl((xs - 0.09, 0, 0), (xs + 0.09, 0, 0), 0.42, 0.42, 10, "iron")
        for k in range(nf):
            t0 = 2.0 * math.pi * k / nf - math.pi / 2.0
            t1 = 2.0 * math.pi * (k + 1) / nf - math.pi / 2.0
            e0 = (math.cos(t0), math.sin(t0))
            e1 = (math.cos(t1), math.sin(t1))
            b.beam((xs, e0[1] * 0.3, e0[0] * 0.3), (xs, e0[1] * (R - 0.08), e0[0] * (R - 0.08)),
                   0.1, "iron", caps=False)
            for r in (r_out, r_in):
                b.beam((xs, e0[1] * r, e0[0] * r), (xs, e1[1] * r, e1[0] * r), 0.09, "iron",
                       caps=False)
    for k in range(nf):
        t = 2.0 * math.pi * k / nf - math.pi / 2.0
        e = Vector((0.0, math.sin(t), math.cos(t)))          # radial
        n = Vector((0.0, math.cos(t), -math.sin(t)))         # board normal (tangent)
        c0, c1 = e * (R - fd), e * R
        hw, ht = W * 0.5 - 0.02, 0.04
        pts = []
        for c in (c0, c1):
            for sx_ in (-hw, hw):
                for sn in (-ht, ht):
                    pts.append(tuple(c + n * sn + Vector((sx_, 0, 0))))
        q = [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)]
        b.solid([[pts[i] for i in f] for f in q], "float")


# --------------------------------------------------------------- topsides --
def build_topsides(b, form, d):
    dk = form.main_deck_y
    # wheelhouse
    wh = d["wheelhouse"]
    z0, z1, hw, top = wh["zAft"], wh["zFwd"], wh["halfWidth"], wh["topY"]
    y0 = dk(0.5 * (z0 + z1)) - 0.02
    wall_top = top - 0.2
    b.box(-hw, hw, y0, y0 + 0.4, z0, z1, "teak", skip=("-y", "+y"))
    b.box(-hw, hw, y0 + 0.4, wall_top, z0, z1, "white", skip=("-y", "+y"))
    b.box(-hw - 0.28, hw + 0.28, wall_top, top - 0.06, z0 - 0.28, z1 + 0.45, "canvas")
    b.box(-hw + 0.3, hw - 0.3, top - 0.06, top, z0 + 0.3, z1 - 0.3, "canvas")
    wy0, wy1, e = y0 + 1.15, y0 + 1.9, 0.015
    for cx in (-1.0, 0.0, 1.0):
        b.face([(cx - 0.4, wy0, z1 + e), (cx + 0.4, wy0, z1 + e), (cx + 0.4, wy1, z1 + e),
                (cx - 0.4, wy1, z1 + e)], "window", hint=(0, 0, 1))
    for sx in (1.0, -1.0):
        for cz in (z0 + 0.75, z1 - 0.75):
            b.face([(sx * (hw + e), wy0, cz - 0.45), (sx * (hw + e), wy0, cz + 0.45),
                    (sx * (hw + e), wy1, cz + 0.45), (sx * (hw + e), wy1, cz - 0.45)],
                   "window", hint=(sx, 0, 0))
        b.face([(sx * 1.0 - 0.35, wy0, z0 - e), (sx * 1.0 + 0.35, wy0, z0 - e),
                (sx * 1.0 + 0.35, wy1, z0 - e), (sx * 1.0 - 0.35, wy1, z0 - e)],
               "window", hint=(0, 0, -1))
    b.face([(-0.38, y0 + 0.02, z0 - e), (0.38, y0 + 0.02, z0 - e), (0.38, y0 + 1.9, z0 - e),
            (-0.38, y0 + 1.9, z0 - e)], "teak", hint=(0, 0, -1))

    # helm: binnacle pedestal and the ship's wheel, just forward of the helmsman
    hz = d["helmZ"]
    yh = dk(hz)
    b.box(-0.14, 0.14, yh, yh + 0.95, hz + 0.55, hz + 0.85, "teak", top="brass")
    hub = (0.0, yh + 1.0, hz + 0.5)
    b.cyl((0, yh + 1.0, hz + 0.42), (0, yh + 1.0, hz + 0.58), 0.09, 0.09, 6, "brass")
    for k in range(8):
        t0, t1 = math.pi * k / 4.0, math.pi * (k + 1) / 4.0
        p = lambda t, r: (r * math.cos(t), hub[1] + r * math.sin(t), hub[2])
        b.beam(p(t0, 0.46), p(t1, 0.46), 0.06, "teak", caps=False)
        if k % 2 == 0:
            b.beam(p(t0, -0.6), p(t0, 0.6), 0.04, "spar")

    # engine casing with a pitched skylight, on the centreline between the funnels
    c0, c1, cw = -0.2, 3.4, 0.85
    yc = dk(1.6) - 0.02
    b.box(-cw, cw, yc, yc + 0.55, c0, c1, "white", skip=("-y",))
    ridge = [(-cw + 0.1, yc + 0.55), (cw - 0.1, yc + 0.55), (0.0, yc + 0.95)]
    za, zb = c0 + 0.25, c1 - 0.25
    for sx in (1.0, -1.0):
        b.face([(sx * (cw - 0.1), yc + 0.55, za), (sx * (cw - 0.1), yc + 0.55, zb),
                (0, yc + 0.95, zb), (0, yc + 0.95, za)], "glass", hint=(sx, 1, 0))
    for z, h in ((za, -1), (zb, 1)):
        b.face([(x, y, z) for x, y in ridge], "white", hint=(0, 0, h))

    # twin slim raked funnels, abreast
    fz, fx, ftop = d["funnelZ"], d["funnelX"], d["funnelTopY"]
    rake = math.tan(math.radians(6.0))
    r = 0.43
    for sx in (1.0, -1.0):
        yb = dk(fz) - 0.02
        ax = lambda y: (sx * fx, y, fz - (y - yb) * rake)
        b.box(sx * fx - 0.7, sx * fx + 0.7, yb, yb + 0.4, fz - 0.75, fz + 0.7, "white",
              top="canvas", skip=("-y",))
        b.cyl(ax(yb + 0.4), ax(ftop - 1.55), r, r, 12, "buff", cap0=False, cap1=False)
        b.cyl(ax(ftop - 1.55), ax(ftop - 1.25), r + 0.015, r + 0.015, 12, "red",
              cap0=False, cap1=False)
        b.cyl(ax(ftop - 1.25), ax(ftop - 0.12), r, r, 12, "black", cap0=False, cap1=False)
        b.cyl(ax(ftop - 0.12), ax(ftop), r + 0.07, r + 0.07, 12, "black", cap_col="inside")

    # short signal mast on the forecastle head of the break, lantern on a bracket
    mz = form.break_z + 1.0
    ym = form.deck_y(mz)
    b.cyl((0, ym, mz), (0, 8.1, mz - 0.25), 0.11, 0.06, 6, "spar")
    b.cyl((0, 8.1, mz - 0.25), (0, 8.28, mz - 0.26), 0.09, 0.0, 6, "brass", cap0=True)
    b.cyl((-1.25, 6.7, mz - 0.19), (1.25, 6.7, mz - 0.19), 0.045, 0.045, 5, "spar")
    b.beam((0, 7.35, mz - 0.2), (0, 7.35, mz + 0.42), 0.05, "brass")
    ly = 6.86
    b.box(-0.15, 0.15, ly, ly + 0.34, mz + 0.27, mz + 0.57, "glow")
    b.box(-0.18, 0.18, ly - 0.06, ly, mz + 0.24, mz + 0.60, "brass")
    b.cyl((0, ly + 0.34, mz + 0.42), (0, ly + 0.52, mz + 0.42), 0.24, 0.0, 4, "brass",
          phase=math.pi / 4)

    # capstan on the forecastle
    cz = 14.0
    yk = form.deck_y(cz)
    b.cyl((0, yk, cz), (0, yk + 0.12, cz), 0.42, 0.42, 8, "black")
    b.cyl((0, yk + 0.12, cz), (0, yk + 0.72, cz), 0.30, 0.2, 8, "teak", cap0=False, cap1=False)
    b.cyl((0, yk + 0.72, cz), (0, yk + 0.9, cz), 0.34, 0.34, 8, "brass")

    # cargo hatch forward of the casing
    yhh = dk(6.4) - 0.02
    b.box(-0.95, 0.95, yhh, yhh + 0.3, 5.3, 7.5, "teak", top="canvas", skip=("-y",))

    # companionway on the after deck: sloped sliding top
    k0 = -10.4
    yk = dk(k0) - 0.02
    b.prism_x([(k0, yk), (k0 + 1.3, yk), (k0 + 1.3, yk + 1.25), (k0 + 0.55, yk + 1.25),
               (k0, yk + 0.8)], -0.6, 0.6, "teak")
    b.face([(-0.42, yk + 0.05, k0 + 1.315), (0.42, yk + 0.05, k0 + 1.315),
            (0.42, yk + 1.12, k0 + 1.315), (-0.42, yk + 1.12, k0 + 1.315)], "inside",
           hint=(0, 0, 1))

    # bollards, in pairs
    for z in (15.4, 6.6, -8.2, -14.6):
        y = form.deck_y(z)
        x = form.half_breadth(z, y) - TB - 0.5
        for sx in (1.0, -1.0):
            for dzz in (-0.22, 0.22):
                b.cyl((sx * x, y, z + dzz), (sx * x, y + 0.42, z + dzz), 0.1, 0.1, 6, "black")
                b.cyl((sx * x, y + 0.42, z + dzz), (sx * x, y + 0.5, z + dzz), 0.14, 0.14, 6,
                      "black")


# ------------------------------------------------------------ blender glue --
def enum_ids(rna_prop):
    return [i.identifier for i in rna_prop.enum_items]


def pick(ids, *wanted):
    for w in wanted:
        if w in ids:
            return w
    raise RuntimeError("none of %r in %r" % (wanted, ids))


def make_material(name, view_col):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = tuple(srgb_to_linear(view_col)) + (1.0,)
    try:
        mat.use_nodes = True
    except Exception:
        pass
    nt = mat.node_tree
    if nt is not None:
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is not None:
            attr = next((n for n in nt.nodes if n.type == "ATTRIBUTE"), None) \
                or nt.nodes.new("ShaderNodeAttribute")
            attr.attribute_name = ATTR
            attr.location = (bsdf.location.x - 260, bsdf.location.y)
            base = next(s for s in bsdf.inputs if s.identifier == "Base Color")
            nt.links.new(attr.outputs[0], base)
            for s in bsdf.inputs:
                if s.identifier == "Roughness":
                    s.default_value = 0.8
    return mat


def to_object(name, b, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in b.verts], [], b.faces)
    me.validate()
    me.update()
    fn = bpy.types.AttributeGroupMesh.bl_rna.functions["new"] \
        if hasattr(bpy.types, "AttributeGroupMesh") else None
    a_type, a_dom = "BYTE_COLOR", "CORNER"
    if fn is not None:
        a_type = pick(enum_ids(fn.parameters["type"]), "BYTE_COLOR")
        a_dom = pick(enum_ids(fn.parameters["domain"]), "CORNER")
    attr = me.color_attributes.new(ATTR, a_type, a_dom)
    assert len(me.polygons) == len(b.cols), "validate() dropped faces"
    flat = []
    for poly, c in zip(me.polygons, b.cols):
        lin = srgb_to_linear(c)
        for _ in range(poly.loop_total):
            flat.extend((lin[0], lin[1], lin[2], 1.0))
    attr.data.foreach_set("color", flat)          # `.color` is scene-linear
    me.color_attributes.active_color = attr
    try:
        me.color_attributes.render_color_index = 0
    except Exception:
        pass
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def clear_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for it in list(coll):
            if it.users == 0:
                coll.remove(it)


def build_all():
    form = steamer_form.HullForm()
    d = form.d
    hull = Builder()
    build_hull(hull, form)
    build_skeg_rudder(hull, form)
    build_bilge_keels(hull, form)
    build_guards(hull, form, d)
    build_paddle_boxes(hull, form, d)
    build_topsides(hull, form, d)
    wheel = Builder()
    build_wheel(wheel, d)
    mat = make_material("SteamerPaint", PAL["hull"])
    ob_h = to_object("SteamerHull", hull, mat)
    ob_w = to_object("SteamerWheel", wheel, mat)
    # the wheel template is parked at the origin; two instances sit on the axles
    ax = d["wheelAxle"]
    inst = []
    for sx, nm in ((1.0, "S"), (-1.0, "P")):
        o = bpy.data.objects.new("SteamerWheel_" + nm, ob_w.data)
        o.location = S((sx * ax["x"], ax["y"], ax["z"]))
        bpy.context.scene.collection.objects.link(o)
        inst.append(o)
    ob_w.hide_render = True
    ob_w.hide_viewport = True
    print("TRIS hull %d  wheel %d  total in game (hull + 2 wheels) %d"
          % (hull.tri_count(), wheel.tri_count(), hull.tri_count() + 2 * wheel.tri_count()))
    return form, ob_h, ob_w, inst


# ----------------------------------------------------------------- renders --
def setup_render(scene):
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError as e:
        print("engine:", e)
    sh = scene.display.shading
    sh.color_type = pick(enum_ids(sh.bl_rna.properties["color_type"]), "VERTEX")
    sh.light = pick(enum_ids(sh.bl_rna.properties["light"]), "STUDIO")
    sh.show_shadows = True
    sh.shadow_intensity = 0.35
    sh.show_cavity = True
    sh.show_backface_culling = True
    sh.show_specular_highlight = False
    sh.show_object_outline = False
    scene.display.light_direction = (-0.35, 0.45, 0.82)
    try:
        scene.display.render_aa = pick(enum_ids(scene.display.bl_rna.properties["render_aa"]),
                                       "16", "8", "5")
    except Exception:
        pass
    vs = scene.view_settings
    try:                                     # dynamic (OCIO) enum: RNA under-reports it
        vs.view_transform = "Standard"
    except TypeError as e:
        print("view_transform:", e)
    world = scene.world or bpy.data.worlds.new("SteamerWorld")
    scene.world = world
    world.color = srgb_to_linear(_hex("#CFE3EC"))
    ims = scene.render.image_settings
    ims.file_format = pick(enum_ids(ims.bl_rna.properties["file_format"]), "PNG")
    scene.render.film_transparent = False


def sea_plane():
    b = Builder()
    s = 400.0
    b.face([(-s, 0, -s), (s, 0, -s), (s, 0, s), (-s, 0, s)], "sea", hint=(0, 1, 0))
    return to_object("SeaPlane", b, make_material("SeaPaint", PAL["sea"]))


def render_views(out_dir, scene, sea=None, debug=False):
    os.makedirs(out_dir, exist_ok=True)
    # A sheet of sea BEHIND the ship for the profile: an orthographic camera at
    # deck height sees the z = 0 plane edge-on, so the waterline would not read.
    bd = Builder()
    bd.face([(-60, -40, -200), (-60, -40, 200), (-60, 0, 200), (-60, 0, -200)], "sea",
            hint=(1, 0, 0))
    backdrop = to_object("SeaBackdrop", bd, make_material("SeaPaint", PAL["sea"]))
    cam_data = bpy.data.cameras.new("SteamerCam")
    cam = bpy.data.objects.new("SteamerCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.clip_end = 1000.0

    def aim(eye, target):
        cam.location = Vector(eye)
        cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
    ctype = enum_ids(cam_data.bl_rna.properties["type"])
    views = [
        # name, eye (Blender), target, ortho scale or None, lens, res
        ("profile", (0.3, -80.0, 2.6), (0.3, 0.0, 2.6), 41.0, 50, (1400, 560)),
        ("bow34", (36.0, -25.0, 13.0), (3.5, 0.0, 1.2), None, 50, (1400, 860)),
        ("chase", (-17.8 - 25.0, 0.0, 14.0), (9.0, 0.0, 1.0), None, 32, (1400, 860)),
        ("plan", (0.3, 0.0, 90.0), (0.3, 0.0, 0.0), 41.0, 50, (1400, 560)),
    ]
    if debug:                                # for the modeller: no sea, see the underbody
        views += [
            ("dbg_bow", (48.0, -4.0, 1.2), (0.0, 0.0, 1.2), None, 70, (1400, 860)),
            ("dbg_under_bow", (30.0, -22.0, -12.0), (2.0, 0.0, 0.0), None, 45, (1400, 860)),
            ("dbg_under_stern", (-32.0, -20.0, -10.0), (-4.0, 0.0, 0.0), None, 45, (1400, 860)),
            ("dbg_box", (6.0, -16.0, 4.5), (1.0, -5.0, 1.8), None, 40, (1400, 860)),
        ]
    paths = []
    for name, eye, tgt, ortho, lens, res in views:
        if ortho:
            cam_data.type = pick(ctype, "ORTHO")
            cam_data.ortho_scale = ortho
        else:
            cam_data.type = pick(ctype, "PERSP")
            cam_data.lens = lens
        aim(eye, tgt)
        if name == "plan":                   # bow to the right, starboard down
            cam.rotation_euler = (0.0, 0.0, 0.0)
        backdrop.hide_render = name != "profile"
        if sea is not None:
            sea.hide_render = name.startswith("dbg_")
        scene.render.resolution_x, scene.render.resolution_y = res
        scene.render.resolution_percentage = 100
        path = os.path.join(out_dir, "steamer_%s.png" % name)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        paths.append(path)
    bpy.data.objects.remove(backdrop, do_unlink=True)
    return paths


# ------------------------------------------------------------------ export --
def export_fbx(src, path):
    """Export ONE top-level mesh object, the yaw burnt into a copy of its data."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    me = src.data.copy()
    me.transform(Matrix.Rotation(math.radians(-90.0), 4, "Z"))   # bow +X -> -Y (= Unity +Z)
    me.update()
    ob = bpy.data.objects.new(src.name + "_export", me)
    bpy.context.scene.collection.objects.link(ob)
    true_name = src.name
    src.name = true_name + "_src"
    ob.name = true_name
    me.name = true_name
    for o in bpy.data.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.context.view_layer.update()
    props = bpy.ops.export_scene.fbx.get_rna_type().properties
    kw = dict(filepath=path, use_selection=True, object_types={"MESH"},
              use_mesh_modifiers=True,
              mesh_smooth_type=pick(enum_ids(props["mesh_smooth_type"]), "FACE"),
              global_scale=1.0, apply_unit_scale=True,
              apply_scale_options=pick(enum_ids(props["apply_scale_options"]), "FBX_SCALE_ALL"),
              bake_space_transform=True,
              axis_forward=pick(enum_ids(props["axis_forward"]), "-Z"),
              axis_up=pick(enum_ids(props["axis_up"]), "Y"),
              use_triangles=False, add_leaf_bones=False,
              path_mode=pick(enum_ids(props["path_mode"]), "STRIP"))
    if "colors_type" in props:
        kw["colors_type"] = pick(enum_ids(props["colors_type"]), "LINEAR")
    bpy.ops.export_scene.fbx(**kw)
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.meshes.remove(me)
    src.name = true_name
    print("exported", path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    def opt(flag, default=None):
        return argv[argv.index(flag) + 1] if flag in argv else default
    print("Blender", bpy.app.version_string)
    clear_scene()
    scene = bpy.context.scene
    form, ob_h, ob_w, inst = build_all()
    if "--no-render" not in argv:
        out_dir = opt("--renders", os.path.join(tempfile.gettempdir(), "steamer_renders"))
        sea = sea_plane()
        setup_render(scene)
        for p in render_views(out_dir, scene, sea, "--debug-views" in argv):
            print("render", p)
        if "--keep-sea" not in argv:
            bpy.data.objects.remove(sea, do_unlink=True)
    if "--no-export" not in argv:
        ob_w.hide_viewport = False
        export_fbx(ob_h, HULL_FBX)
        export_fbx(ob_w, WHEEL_FBX)
        ob_w.hide_viewport = True
    blend = opt("--blend")
    if blend:
        bpy.ops.wm.save_as_mainfile(filepath=blend)


if __name__ == "__main__":
    main()
