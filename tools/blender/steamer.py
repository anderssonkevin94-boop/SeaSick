"""Stern-wheel paddle steamer -- mesh, renders, FBX export.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        -P tools/blender/steamer.py -- \
        [--renders DIR] [--debug-views] [--no-render] [--no-export] [--blend FILE]

Built to the approved reference sheet in
`art-staging/stern-paddle-integrated-hull-v2/`: a broad oak workboat with one
big paddle recessed INSIDE her stern, an open gun deck, a centreline chimney
and an aft helm. The paddle enclosure is not a roof bolted on -- it is her own
planking carried aft into two cheeks, joined over the wheel by a low flat
platform, with the same sheer trim and the same steel bands running through it.

The hull is lofted from `steamer_form.HullForm.half_breadth`, the SAME function
the strip tables in Resources/Steamer/hullform.json are integrated from, so the
mesh and the physics are one shape (docs/steamer-spec.md). Two places where the
mesh knowingly departs from that function, both of them ABOVE the design
waterline, so neither changes a gram of what she floats on:

  * the loft is CUT at `form.transom_z`, which is what makes her stern square
    instead of the point the analytic plan would close to;
  * the paddle WELL is cut out of her between the cheeks, from y = 0 (or the
    counter's underside, whichever is higher) up to the platform's underside.

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


# Warm oak in three broad courses, a sage-teal sheer, dark slate ironwork.
# The wood sits between the reference sheet's orange and the adventure brig's
# honey, because she has to look like the brig's sister in the same light.
PAL = {k: _hex(v) for k, v in {
    "bottom": "#7A4620",      # underbody, wet oak
    "plankLo": "#9C5A28",     # lowest course
    "plankMid": "#BA6E30",
    "plankHi": "#CF873C",     # top course, under the sheer
    "sheer": "#6FA391",       # sage-teal sheer band and cap rail
    "sheerLo": "#55836F",     # the cap's shadowed inboard edge
    "steel": "#434049",       # bands, stem cap, corner caps, chimney
    "steelLo": "#33313A",
    "bolt": "#6C6774",
    "deckA": "#C88B4B", "deckB": "#BC8043", "deckC": "#D29554",
    "margin": "#9A6533",
    "inside": "#6B4526",      # inboard planking and anything in shadow
    "blade": "#B5762F",       # paddle floats, ochre oak
    "hub": "#3A3740",
    "teak": "#8A5B34",
    "brass": "#C79A3F",
    "glow": "#FFD88A",
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


def smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


# -------------------------------------------------------------------- hull --
LOW_T = (0.0, 0.05, 0.15, 0.35, 0.65, 1.0)
BOOT_LO, BOOT_HI = -0.30, 0.30
TB = 0.14                                   # bulwark thickness
PLANK_W = 0.46
MARGIN_W = 0.30
CEIL_T = 0.16                               # thickness of the stern platform

# One band per gap between the levels below: the underbody, then three broad
# oak courses, then the sheer. Picked by LEVEL, never by face index.
BAND_COL = (["bottom"] * 5 + ["plankLo", "plankLo", "plankMid", "plankMid",
                              "plankHi", "plankHi", "sheer"])
assert len(BAND_COL) == 12


class Stern(object):
    """Everything about the paddle well, read once out of the design."""

    def __init__(self, form):
        w = form.d["well"]
        self.half = float(w["halfWidth"])
        self.fwd_z = float(w["fwdZ"])
        self.rise = float(w["platformRise"])
        self.plat_z = float(w["platformFwdZ"])
        self.floor = float(w["floorY"])
        self.ramp = 0.9                      # chamfer forward of the platform
        self.form = form

    def deck_y(self, z):
        """Height of the planking she is walked on: the main deck, rising over
        a short chamfer into the raised stern platform."""
        return self.form.main_deck_y(z) + self.rise * smoothstep(
            (self.plat_z + self.ramp - z) / self.ramp)

    def in_well(self, z):
        return z <= self.fwd_z

    def floor_y(self, z):
        """The well's floor. Below the waterline, and deliberately so: the
        wheel has to reach water. `HullForm.net_half_breadth` cuts the same
        slot out of the sections it integrates, so this is not the mesh
        telling a different story from the physics."""
        return max(self.floor, self.form.hull_keel_y(z))

    def ceil_y(self, z):
        return self.deck_y(z) - CEIL_T


def hull_levels(form, stern, z):
    """13 heights, keel to rail -- the SAME scheme at every station, so the
    loft between two of them is watertight.

    The well's FLOOR is one of them, at every station, whether that station is
    anywhere near the well or not. A level there means the cut lands on a row
    instead of halfway across a band, and one seam in the underbody planking
    forward costs nothing."""
    keel = form.hull_keel_y(z)
    ymd = form.main_deck_y(z)
    top = ymd + form.bulwark
    fl = min(max(stern.floor, keel), BOOT_LO)
    ys = [keel, keel + (fl - keel) * 0.35, keel + (fl - keel) * 0.72, fl,
          fl + (BOOT_LO - fl) * 0.45, BOOT_LO, 0.0, BOOT_HI]
    ys += [BOOT_HI + (ymd - BOOT_HI) * f for f in (0.34, 0.68)]
    ys += [ymd, ymd + 0.55 * form.bulwark, top]
    return [min(max(y, keel), top) for y in ys]


def hull_stations(form, stern):
    """Loft stations, transom to stem head, bunched where she is turning."""
    z0 = form.transom_z
    z1 = form.half_l
    for _ in range(30):                      # stem head: z = z_fwd(rail(z))
        z1 = form.z_fwd(form.shell_top_y(z1))
    z1 -= 0.002
    n = 40
    zs = []
    for i in range(n + 1):
        u = float(i) / n
        zs.append(z0 + (z1 - z0) * (0.55 * u + 0.45 * (0.5 - 0.5 * math.cos(math.pi * u))))
    # Stations that HAVE to exist, or a step lands in the middle of a band.
    zs += [stern.fwd_z, stern.plat_z, stern.plat_z + stern.ramp, z1 - 0.12]
    zs = sorted(zs)
    out = [zs[0]]
    for z in zs[1:]:
        if z - out[-1] > 0.05:
            out.append(z)
    for forced in (stern.fwd_z, stern.plat_z, stern.plat_z + stern.ramp):
        k = min(range(len(out)), key=lambda i: abs(out[i] - forced))
        out[k] = forced
    return sorted(out)


def build_hull(b, form, stern):
    zs = hull_stations(form, stern)
    WH = stern.half

    def section(z):
        ys = hull_levels(form, stern, z)
        xs = [form.half_breadth(z, y, skeg=False) for y in ys]
        keel = form.hull_keel_y(z)
        xs[0] = (form.f["stemHalfThickness"] if z > form.f["parallelFwdZ"] else 0.0) \
            if ys[0] <= keel + 1e-6 else xs[0]
        return ys, xs

    for j in range(len(zs) - 1):
        za, zb = zs[j], zs[j + 1]
        zm = 0.5 * (za + zb)
        ya, xa = section(za)
        yb, xb = section(zb)
        well = stern.in_well(zm)
        if well:
            fa, ca = stern.floor_y(za), stern.ceil_y(za)
            fb, cb = stern.floor_y(zb), stern.ceil_y(zb)

            def void(y, x, f, c):
                return f - 1e-6 <= y <= c + 1e-6 and x <= WH + 1e-6
        for k in range(len(ya) - 1):
            if well and void(ya[k], xa[k], fa, ca) and void(ya[k + 1], xa[k + 1], fa, ca) \
                    and void(yb[k], xb[k], fb, cb) and void(yb[k + 1], xb[k + 1], fb, cb):
                continue                     # this band is inside the well
            for sx in (1.0, -1.0):
                b.face([(sx * xa[k], ya[k], za), (sx * xa[k + 1], ya[k + 1], za),
                        (sx * xb[k + 1], yb[k + 1], zb), (sx * xb[k], yb[k], zb)],
                       BAND_COL[k],
                       # outward = away from a point inboard, above and TOWARDS
                       # midships of the face: near the ends the shell faces
                       # mostly fore and aft, not abeam
                       away=(0.0, 0.5 * (ya[k] + yb[k + 1]) + 1.0,
                             zm - math.copysign(1.5, zm)))
        # keel bar / stem between the two sides
        b.face([(-xa[0], ya[0], za), (xa[0], ya[0], za),
                (xb[0], yb[0], zb), (-xb[0], yb[0], zb)],
               "plankLo" if ya[0] > BOOT_HI else "bottom",
               hint=(0, -1, 0.2 if zm > 0 else -0.2))

        # ---- rail cap, inner bulwark, deck ------------------------------
        def edge(z):
            ymd = form.main_deck_y(z)
            yt = ymd + form.bulwark
            yd = stern.deck_y(z)
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
                    (sx * eb[3], eb[1], zb), (sx * eb[2], eb[1], zb)], "sheer",
                   hint=(0, 1, 0))
            b.face([(sx * ea[3], ea[1], za), (sx * ea[4], ea[0], za),
                    (sx * eb[4], eb[0], zb), (sx * eb[3], eb[1], zb)], "inside",
                   hint=(-sx, 0.05, 0))
            ma, mb = max(ea[4] - MARGIN_W, 0.0), max(eb[4] - MARGIN_W, 0.0)
            b.face([(sx * ma, ea[0], za), (sx * ea[4], ea[0], za),
                    (sx * eb[4], eb[0], zb), (sx * mb, eb[0], zb)], "margin",
                   hint=(0, 1, 0))
            nk = int(math.ceil(max(ma, mb) / PLANK_W))
            for k in range(nk):
                a0, a1 = min(k * PLANK_W, ma), min((k + 1) * PLANK_W, ma)
                b0, b1 = min(k * PLANK_W, mb), min((k + 1) * PLANK_W, mb)
                b.face([(sx * a0, ea[0], za), (sx * a1, ea[0], za),
                        (sx * b1, eb[0], zb), (sx * b0, eb[0], zb)],
                       ("deckA", "deckB", "deckC")[k % 3], hint=(0, 1, 0))

        # ---- the well: walls, floor and ceiling --------------------------
        if not well:
            continue
        na = 5
        for k in range(na):
            t0, t1 = float(k) / na, float(k + 1) / na
            pa0, pa1 = fa + (ca - fa) * t0, fa + (ca - fa) * t1
            pb0, pb1 = fb + (cb - fb) * t0, fb + (cb - fb) * t1
            wa0 = min(WH, form.half_breadth(za, pa0, skeg=False))
            wa1 = min(WH, form.half_breadth(za, pa1, skeg=False))
            wb0 = min(WH, form.half_breadth(zb, pb0, skeg=False))
            wb1 = min(WH, form.half_breadth(zb, pb1, skeg=False))
            for sx in (1.0, -1.0):
                b.face([(sx * wa0, pa0, za), (sx * wa1, pa1, za),
                        (sx * wb1, pb1, zb), (sx * wb0, pb0, zb)], "inside",
                       hint=(-sx, 0, 0))
        wa = min(WH, form.half_breadth(za, fa, skeg=False))
        wb = min(WH, form.half_breadth(zb, fb, skeg=False))
        if form.hull_keel_y(zm) < stern.floor - 1e-6 and max(wa, wb) > 1e-3:
            b.face([(-wa, fa, za), (wa, fa, za), (wb, fb, zb), (-wb, fb, zb)],
                   "inside", hint=(0, 1, 0))         # the well's floor
        b.face([(-WH, ca, za), (WH, ca, za), (WH, cb, zb), (-WH, cb, zb)],
               "inside", hint=(0, -1, 0))            # the platform, from below

    # ---- the transom ----------------------------------------------------
    #
    # The analytic plan would close her stern to a point; the loft is cut
    # short of that, so the aft face has to be built. It is exactly the
    # planking between the well's wall and the outer shell -- which is to say
    # the end grain of the two cheeks -- with the aperture between them.
    zt = zs[0]
    yt_, xt_ = section(zt)
    ft, ct = stern.floor_y(zt), stern.ceil_y(zt)
    for k in range(len(yt_) - 1):
        xi0 = WH if ft - 1e-6 <= yt_[k] <= ct + 1e-6 else 0.0
        xi1 = WH if ft - 1e-6 <= yt_[k + 1] <= ct + 1e-6 else 0.0
        xo0, xo1 = max(xt_[k], xi0), max(xt_[k + 1], xi1)
        if xo0 - xi0 < 1e-3 and xo1 - xi1 < 1e-3:
            continue
        for sx in (1.0, -1.0):
            b.face([(sx * xi0, yt_[k], zt), (sx * xo0, yt_[k], zt),
                    (sx * xo1, yt_[k + 1], zt), (sx * xi1, yt_[k + 1], zt)],
                   BAND_COL[k], hint=(0, 0, -1))

    # forward bulkhead of the well, where the deck steps over the wheel
    z = stern.fwd_z
    y0, y1 = stern.floor_y(z), stern.ceil_y(z)
    b.face([(-WH, y0, z), (WH, y0, z), (WH, y1, z), (-WH, y1, z)], "inside",
           hint=(0, 0, -1))


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
        b.face([(-t, bot, za), (t, bot, za), (t, bot, zb), (-t, bot, zb)], "bottom",
               hint=(0, -1, 0))
    b.face([(-t, bot, zs[0]), (t, bot, zs[0]), (t, tops[0], zs[0]), (-t, tops[0], zs[0])],
           "bottom", hint=(0, 0, -1))
    # The rudder GANG, forward of the wheel where a stern-wheeler's is: she
    # steers on the water the paddle is pulling past it. Four blades, not one,
    # because `rudderArea` is 16 m2 and one blade of that on a 2 m draft would
    # be 8 m long -- the number and the drawing have to agree.
    r = form.d["rudder"]
    zr, area = float(r["z"]), float(form.d["rudderArea"])
    blades = 4
    chord, depth = 2.4, 1.65
    per = area / blades
    chord = per / depth
    for sx in (1.0, -1.0):
        for cx in (1.3, 3.4):
            x = sx * cx
            # Hung UP from the keel, not down from the waterline: a blade
            # this deep measured off the wrong end put 1.3 m of rudder below
            # her keel, where the first sandbank would have it.
            bot = form.hull_keel_y(zr) + 0.06
            top = bot + depth
            poly = [(zr - 0.5 * chord, bot), (zr + 0.5 * chord, bot + 0.18),
                    (zr + 0.5 * chord, top), (zr - 0.5 * chord, top)]
            b.prism_x(poly, x - 0.07, x + 0.07, "bottom")
            b.cyl((x, top - 0.05, zr + 0.42 * chord), (x, top + 0.5, zr + 0.42 * chord),
                  0.1, 0.1, 6, "bottom")
        # the tiller bar that ties the gang together
        b.box(sx * 0.9, sx * 3.8, top + 0.34, top + 0.48,
              zr + 0.42 * chord - 0.1, zr + 0.42 * chord + 0.1, "steelLo")


def build_bilge_keels(b, form):
    y0 = -1.55
    zs = [-6.5 + i * 1.0 for i in range(13)]
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


# ------------------------------------------------------------------- trim --
def band_strip(b, form, z, y0, y1, half, col, out=0.07, n=4):
    """A steel strap laid ON the planking: it follows the shell's own
    half-breadth at both of its edges, so it hugs her where she is turning
    instead of standing off her, and it is given a thickness so it reads as a
    strap rather than a decal."""
    for sx in (1.0, -1.0):
        za, zb = z - half, z + half
        for k in range(n):
            ya = y0 + (y1 - y0) * k / n
            yb = y0 + (y1 - y0) * (k + 1) / n
            xa0 = form.half_breadth(za, ya, skeg=False)
            xa1 = form.half_breadth(za, yb, skeg=False)
            xb0 = form.half_breadth(zb, ya, skeg=False)
            xb1 = form.half_breadth(zb, yb, skeg=False)
            b.face([(sx * (xa0 + out), ya, za), (sx * (xa1 + out), yb, za),
                    (sx * (xb1 + out), yb, zb), (sx * (xb0 + out), ya, zb)],
                   col, hint=(sx, 0, 0))
            for x0e, x1e, zz, hz in ((xa0, xa1, za, -1.0), (xb0, xb1, zb, 1.0)):
                b.face([(sx * x0e, ya, zz), (sx * (x0e + out), ya, zz),
                        (sx * (x1e + out), yb, zz), (sx * x1e, yb, zz)],
                       col, hint=(0, 0, hz))


def bolt(b, form, z, y, r=0.085, out=0.1):
    for sx in (1.0, -1.0):
        x = form.half_breadth(z, y, skeg=False)
        b.cyl((sx * x, y, z), (sx * (x + out), y, z), r, r, 6, "bolt")


def build_trim(b, form, stern, d):
    """Rubbing strake, steel straps and their bolts, stem cap, rail blocks."""
    zs = hull_stations(form, stern)
    z_lo, z_hi = zs[0], zs[-1]

    # one heavy rubbing strake, a third of the way up the topside
    for j in range(len(zs) - 1):
        za, zb = zs[j], zs[j + 1]
        if za < form.transom_z + 0.1:
            continue
        ya = form.main_deck_y(za) - 0.72
        yb = form.main_deck_y(zb) - 0.72
        h = 0.30
        for sx in (1.0, -1.0):
            xa = form.half_breadth(za, ya, skeg=False)
            xb = form.half_breadth(zb, yb, skeg=False)
            if min(xa, xb) < 0.25:
                continue
            for dy, hint in ((0.0, (0, -1, 0)), (h, (0, 1, 0))):
                b.face([(sx * xa, ya + dy, za), (sx * (xa + 0.11), ya + dy, za),
                        (sx * (xb + 0.11), yb + dy, zb), (sx * xb, yb + dy, zb)],
                       "steel", hint=hint)
            b.face([(sx * (xa + 0.11), ya, za), (sx * (xa + 0.11), ya + h, za),
                    (sx * (xb + 0.11), yb + h, zb), (sx * (xb + 0.11), yb, zb)],
                   "steel", hint=(sx, 0, 0))

    # vertical straps at the stations that carry something: the guns, the
    # chimney, the well's bulkhead and the transom corners
    # at the stations that actually carry something -- the guns, the well's
    # bulkhead, her shoulders -- and never two within a metre of each other,
    # which reads as a mistake rather than as structure.
    straps = sorted(set(list(d["gunZ"]) + [stern.fwd_z, -8.4, 12.4]))
    kept = []
    for z in straps:
        if not (z_lo + 0.5 < z < z_hi - 0.8):
            continue
        if kept and z - kept[-1] < 1.5:
            continue
        kept.append(z)
    for z in kept:
        y0 = min(-0.35, form.hull_keel_y(z) + 0.2)
        # under the sheer band, not through it: a strap that crossed the
        # teal left a notch in the one line that runs her whole length.
        y1 = form.main_deck_y(z) + 0.42 * form.bulwark
        band_strip(b, form, z, y0, y1, 0.30, "steel")
        for t in (0.12, 0.52, 0.88):
            bolt(b, form, z, y0 + (y1 - y0) * t)

    # stem cap: the one piece of iron she wears on her nose. SWEPT along the
    # stem curve -- built as stacked boxes it came out a staircase, which is
    # the one thing a stem must never look like.
    n = 9
    y_top = form.main_deck_y(form.z_stem_deck) + form.bulwark + 0.3
    ys = [-0.5 + (y_top + 0.5) * i / n for i in range(n + 1)]
    w, t = 0.2, 0.2
    for i in range(n):
        ya, yb = ys[i], ys[i + 1]
        b.prism_x([(form.z_fwd(ya) - 0.02, ya), (form.z_fwd(ya) + t, ya),
                   (form.z_fwd(yb) + t, yb), (form.z_fwd(yb) - 0.02, yb)],
                  -w, w, "steel")

    # rail blocks: her frame tops, capped in steel. FEW and BIG -- one every
    # 2.7 m came out a picket fence, which is the opposite of the reference's
    # handful of deliberate pieces.
    step = 5.0
    n = int((z_hi - z_lo - 3.0) / step)
    for i in range(n + 1):
        z = z_lo + 1.8 + i * step
        yt = form.main_deck_y(z) + form.bulwark
        x = form.half_breadth(z, yt, skeg=False)
        if x < 1.2:
            continue
        for sx in (1.0, -1.0):
            b.box(sx * (x - TB - 0.06), sx * (x + 0.07), yt, yt + 0.4,
                  z - 0.3, z + 0.3, "steel")


# ------------------------------------------------------------------ stern --
def build_stern(b, form, stern, d):
    """The transom's ironwork: a chamfered steel cap down each corner, a pair
    of bolted straps flanking the aperture, and two small lanterns on the
    after posts. On a stern this square the corner IS the silhouette, so that
    is where her iron goes."""
    zt = form.transom_z
    WH = stern.half
    yt = form.main_deck_y(zt) + form.bulwark
    ce = stern.ceil_y(zt)

    def edge_x(y):
        return form.half_breadth(zt, min(y, yt), skeg=False)

    n = 7
    # stops just under the waterline: carried on down it followed the
    # tuck round in an arc and read as a claw, not a corner post.
    y0, y1 = -0.25, yt + 0.34
    cw = 0.62                                # how far inboard the cap reaches
    z0, z1 = zt - 0.06, zt + 0.40
    for sx in (1.0, -1.0):
        for k in range(n):
            ya = y0 + (y1 - y0) * k / n
            yb = y0 + (y1 - y0) * (k + 1) / n
            xoa, xob = edge_x(ya) + 0.05, edge_x(yb) + 0.05
            xia = max(xoa - cw, WH + 0.08)
            xib = max(xob - cw, WH + 0.08)
            if xoa - xia < 0.05 and xob - xib < 0.05:
                continue
            # aft face of the cap
            b.face([(sx * xia, ya, z0), (sx * xoa, ya, z0), (sx * xob, yb, z0),
                    (sx * xib, yb, z0)], "steel", hint=(0, 0, -1))
            # outboard and inboard sides
            b.face([(sx * xoa, ya, z0), (sx * xoa, ya, z1), (sx * xob, yb, z1),
                    (sx * xob, yb, z0)], "steel", hint=(sx, 0, 0))
            b.face([(sx * xia, ya, z0), (sx * xia, ya, z1), (sx * xib, yb, z1),
                    (sx * xib, yb, z0)], "steel", hint=(-sx, 0, 0))
        for t in (0.18, 0.5, 0.82):
            y = y0 + (y1 - y0) * t
            x = edge_x(y) - 0.28
            b.cyl((sx * x, y, zt - 0.06), (sx * x, y, zt - 0.2), 0.09, 0.09, 6, "bolt")

    # straps either side of the aperture, holding the cheeks to the platform
    for sx in (1.0, -1.0):
        x = sx * (WH + 0.42)
        b.box(x - 0.24, x + 0.24, stern.floor_y(zt) + 0.1, ce + 0.25,
              zt - 0.06, zt + 0.02, "steel")
        for t in (0.25, 0.75):
            y = stern.floor_y(zt) + 0.1 + (ce + 0.15 - stern.floor_y(zt)) * t
            b.cyl((x, y, zt - 0.06), (x, y, zt - 0.18), 0.085, 0.085, 6, "bolt")

    # two small lanterns on the after posts, secondary to the paddle
    for sx in (1.0, -1.0):
        x = sx * (edge_x(yt) - 0.55)
        b.box(x - 0.16, x + 0.16, yt + 0.34, yt + 0.62, zt + 0.08, zt + 0.4, "glow")
        b.box(x - 0.19, x + 0.19, yt + 0.62, yt + 0.7, zt + 0.05, zt + 0.43, "steel")


# ------------------------------------------------------------------ wheel --
def build_wheel(b, d):
    """The stern wheel, about ITS OWN origin: axle along ship x (= Blender Y).

    Big oak floats between two steel rims, bold octagonal bosses on the ends
    of the shaft. Everything about her that MOVES is round; everything that
    holds it is square. Deliberately FEW pieces: through a 4.8 m aperture at
    the far end of the ship, a truss reads as a tangle and four broad boards
    read as a paddle."""
    R = d["wheelRadius"]
    W = d["wheelWidth"]
    nf = int(d["wheelFloats"])
    fd = d["wheelFloatDepth"]
    hw = W * 0.5

    b.cyl((-hw - 0.5, 0, 0), (hw + 0.5, 0, 0), 0.19, 0.19, 8, "hub")
    for sx in (-1.0, 1.0):                   # octagonal bosses on the shaft
        b.cyl((sx * (hw + 0.06), 0, 0), (sx * (hw + 0.52), 0, 0), 0.5, 0.38, 8,
              "steel", phase=math.pi / 8.0)

    r = R - fd * 0.5
    for xs in (-hw + 0.28, hw - 0.28):
        for k in range(nf):
            t0 = 2.0 * math.pi * k / nf
            t1 = 2.0 * math.pi * (k + 1) / nf
            e0 = (math.cos(t0), math.sin(t0))
            e1 = (math.cos(t1), math.sin(t1))
            b.beam((xs, e0[1] * 0.2, e0[0] * 0.2), (xs, e0[1] * r, e0[0] * r),
                   0.15, "hub", caps=False)
            b.beam((xs, e0[1] * r, e0[0] * r), (xs, e1[1] * r, e1[0] * r),
                   0.15, "steel", caps=False)

    for k in range(nf):
        t = 2.0 * math.pi * k / nf
        e = Vector((0.0, math.sin(t), math.cos(t)))          # radial
        n = Vector((0.0, math.cos(t), -math.sin(t)))         # board normal
        c0, c1 = e * (R - fd), e * R
        ht = 0.09
        pts = []
        for c in (c0, c1):
            for sx_ in (-hw, hw):
                for sn in (-ht, ht):
                    pts.append(tuple(c + n * sn + Vector((sx_, 0, 0))))
        q = [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6),
             (0, 2, 6, 4), (1, 3, 7, 5)]
        b.solid([[pts[i] for i in f] for f in q], "blade")


# --------------------------------------------------------------- topsides --
def build_topsides(b, form, stern, d):
    dk = stern.deck_y

    # --- chimney: slim, octagonal, tapered, with a flared mouth -----------
    fz, ftop = d["funnelZ"], d["funnelTopY"]
    yb = dk(fz) - 0.02
    r0, r1 = 0.52, 0.40
    b.box(-0.82, 0.82, yb, yb + 0.26, fz - 0.82, fz + 0.82, "steelLo",
          top="steel", skip=("-y",))
    for sx in (1.0, -1.0):
        for dz in (-0.58, 0.58):
            b.cyl((sx * 0.58, yb + 0.26, fz + dz), (sx * 0.58, yb + 0.34, fz + dz),
                  0.09, 0.09, 6, "bolt")
    b.cyl((0, yb + 0.26, fz), (0, ftop - 1.5, fz), r0, r1 + 0.03, 8, "steel",
          cap0=False, cap1=False, phase=math.pi / 8.0)
    b.cyl((0, ftop - 1.5, fz), (0, ftop - 1.22, fz), r1 + 0.09, r1 + 0.09, 8,
          "steelLo", cap0=False, cap1=False, phase=math.pi / 8.0)
    b.cyl((0, ftop - 1.22, fz), (0, ftop - 0.42, fz), r1, r1, 8, "steel",
          cap0=False, cap1=False, phase=math.pi / 8.0)
    b.cyl((0, ftop - 0.42, fz), (0, ftop, fz), r1, r1 + 0.16, 8, "steel",
          cap0=False, cap1=True, cap_col="inside", phase=math.pi / 8.0)

    # --- helm: a solid pedestal and a wheel with bold spokes ---------------
    hz = d["helmZ"]
    yh = dk(hz)
    b.prism_x([(hz + 0.34, yh), (hz + 0.86, yh), (hz + 0.74, yh + 0.86),
               (hz + 0.46, yh + 0.86)], -0.24, 0.24, "teak")
    b.box(-0.3, 0.3, yh + 0.86, yh + 0.96, hz + 0.4, hz + 0.82, "steel")
    hub_y = yh + 1.32
    b.cyl((0, hub_y, hz + 0.5), (0, hub_y, hz + 0.68), 0.12, 0.12, 8, "steel")
    for k in range(8):
        t0, t1 = math.pi * k / 4.0, math.pi * (k + 1) / 4.0
        p = lambda t, r: (r * math.cos(t), hub_y + r * math.sin(t), hz + 0.59)
        b.beam(p(t0, 0.52), p(t1, 0.52), 0.09, "teak", caps=False)
        b.beam(p(t0, 0.06), p(t0, 0.66), 0.06, "teak")

    # --- a flush hatch down to the machinery -------------------------------
    hzz = -6.4
    yk = dk(hzz) - 0.02
    b.box(-1.0, 1.0, yk, yk + 0.22, hzz - 1.1, hzz + 1.1, "teak", top="steelLo",
          skip=("-y",))

    # --- bollards, in pairs ------------------------------------------------
    for z in (13.6, 5.0, -9.6):
        y = dk(z)
        x = form.half_breadth(z, form.main_deck_y(z), skeg=False) - TB - 0.55
        if x < 0.8:
            continue
        for sx in (1.0, -1.0):
            for dzz in (-0.26, 0.26):
                b.cyl((sx * x, y, z + dzz), (sx * x, y + 0.44, z + dzz), 0.11,
                      0.11, 6, "steelLo")
                b.cyl((sx * x, y + 0.44, z + dzz), (sx * x, y + 0.54, z + dzz),
                      0.16, 0.16, 6, "steelLo")


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
    stern = Stern(form)
    hull = Builder()
    build_hull(hull, form, stern)
    build_skeg_rudder(hull, form)
    build_bilge_keels(hull, form)
    build_trim(hull, form, stern, d)
    build_stern(hull, form, stern, d)
    build_topsides(hull, form, stern, d)
    wheel = Builder()
    build_wheel(wheel, d)
    mat = make_material("SteamerPaint", PAL["plankMid"])
    ob_h = to_object("SteamerHull", hull, mat)
    ob_w = to_object("SteamerWheel", wheel, mat)
    ax = d["wheelAxle"]
    inst = bpy.data.objects.new("SteamerWheel_Fitted", ob_w.data)
    inst.location = S((ax["x"], ax["y"], ax["z"]))
    bpy.context.scene.collection.objects.link(inst)
    ob_w.hide_render = True
    ob_w.hide_viewport = True
    print("TRIS hull %d  wheel %d  total in game %d"
          % (hull.tri_count(), wheel.tri_count(),
             hull.tri_count() + wheel.tri_count()))
    return form, ob_h, ob_w, [inst]


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
        ("profile", (-0.5, -80.0, 2.6), (-0.5, 0.0, 2.6), 41.0, 50, (1400, 560)),
        ("bow34", (34.0, -24.0, 12.0), (2.0, 0.0, 1.2), None, 50, (1400, 860)),
        ("stern34", (-33.0, -19.0, 10.0), (-10.0, 0.0, 1.0), None, 48, (1400, 860)),
        ("chase", (-17.0 - 26.0, 0.0, 13.5), (8.0, 0.0, 1.0), None, 32, (1400, 860)),
        ("plan", (-0.5, 0.0, 90.0), (-0.5, 0.0, 0.0), 41.0, 50, (1400, 560)),
        ("astern", (-46.0, 0.0, 2.2), (-10.0, 0.0, 1.6), None, 55, (1000, 800)),
    ]
    if debug:                                # for the modeller: no sea, see the underbody
        views += [
            ("dbg_under_stern", (-34.0, -18.0, -9.0), (-13.0, 0.0, 0.0), None, 45, (1400, 860)),
            ("dbg_under_bow", (28.0, -20.0, -11.0), (2.0, 0.0, 0.0), None, 45, (1400, 860)),
            ("dbg_well", (-24.0, -9.0, 3.0), (-16.0, 0.0, 0.6), None, 40, (1400, 860)),
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
