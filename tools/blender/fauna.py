"""Wild island fauna -- goat, boar, gull -- mesh + FBX export.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        --python tools/blender/fauna.py -- \
        [--renders DIR] [--no-render] [--no-export] [--blend FILE]

Three animals for the "carved" low-poly island style (docs/GDD.md
"Island art style", docs/art-direction/README.md): faceted planes, no
smoothing, colour is per-face VERTEX COLOUR only (attribute "Col", byte /
corner), no textures or UVs. Built from boxes/wedges/tapered frusta, same
recipe as `tools/blender/steamer.py`, whose helpers this file copies
verbatim where possible (`Builder.face/solid/box/cyl`, `make_material`,
`to_object`, `export_fbx`, `enum_ids`, `pick`, `srgb_to_linear`).

Everything is authored in the SAME frame steamer.py uses: +x = the
animal's right, +y = up, +z = forward/nose, mapped to Blender's authoring
frame (forward +X, left +Y, up +Z) by `S()`. `export_fbx` burns in the
same -90 deg yaw about Blender Z that steamer.py measured, so the
authoring +z (forward) lands on Unity +z (forward) / `transform.forward`
after the FBX axis conversion (-Z forward, Y up, bake_space_transform).
Goat and boar are grounded at the feet (y=0 = Unity ground); the gull
flies, so its origin is the body centre, y=0 sitting mid-body.

Export settings are copied unchanged from steamer.py's `export_fbx`:
mesh-only, FACE smoothing (no smoothing groups), bake_space_transform,
apply_unit_scale, FBX_SCALE_ALL, axis_forward -Z / axis_up Y,
colors_type LINEAR (vertex colours written scene-linear, matching the
project's shader which does no sRGB conversion on them).
"""
import math
import os
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(REPO, "Assets", "_Project", "Resources", "Fauna")
GOAT_FBX = os.path.join(OUT_DIR, "goat.fbx")
BOAR_FBX = os.path.join(OUT_DIR, "boar.fbx")
GULL_FBX = os.path.join(OUT_DIR, "gull.fbx")
ATTR = "Col"


# ------------------------------------------------------------------ colour --
def _hex(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_linear(c):
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
                 for v in c)


PAL = {k: _hex(v) for k, v in {
    # goat -- off-white body, grey-brown face stripe + legs, near-black horns
    "goatBody": "#DEDACE",
    "goatBodyLo": "#C2BCAC",
    "goatDark": "#3B342D",
    "goatFace": "#6E5F4C",
    "goatLeg": "#5C5040",
    "goatHoof": "#221D18",
    "goatHorn": "#2B241C",
    "goatEar": "#D69A93",
    # boar -- dark brown, lighter flanks, pale snout, ivory tusks
    "boarBody": "#4A3120",
    "boarBodyLo": "#332217",
    "boarFlank": "#6B4C30",
    "boarSnout": "#C9AD87",
    "boarTusk": "#EDE6D6",
    "boarBristle": "#241811",
    "boarHoof": "#14100C",
    # gull -- white body, grey wings, black tips, yellow beak
    "gullBody": "#F5F3EC",
    "gullBodyLo": "#D8D4C6",
    "gullWing": "#9AA3AC",
    "gullTip": "#232323",
    "gullBeak": "#E8B23A",
}.items()}


# ----------------------------------------------------------------- builder --
def S(p):
    """Author frame (x right, y up, z forward) -> Blender (fwd +X, left +Y, up +Z)."""
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

    def block(self, near, far, col):
        """Frustum between two quads `near`/`far` (each 4 pts, matching winding)."""
        faces = [near, far]
        for i in range(4):
            j = (i + 1) % 4
            faces.append([near[i], near[j], far[j], far[i]])
        self.solid(faces, col)

    def prism_z(self, poly_xy, z0, z1, col, cap_col=None):
        """Convex (x, y) polygon extruded along z. Used for chamfered-top torsos."""
        n = len(poly_xy)
        cx = sum(p[0] for p in poly_xy) / n
        cy = sum(p[1] for p in poly_xy) / n
        ctr = (cx, cy, 0.5 * (z0 + z1))
        for i in range(n):
            (xa, ya), (xb, yb) = poly_xy[i], poly_xy[(i + 1) % n]
            self.face([(xa, ya, z0), (xb, yb, z0), (xb, yb, z1), (xa, ya, z1)],
                      col, away=ctr)
        cc = cap_col or col
        self.face([(x, y, z0) for x, y in poly_xy], cc, away=ctr)
        self.face([(x, y, z1) for x, y in poly_xy], cc, away=ctr)

    def taper_z(self, z0, q0, z1, q1, col):
        """Box whose two z-ends have DIFFERENT half-sizes/centres -- the cheap
        way off a box silhouette. `q = (cx, hw, y0, y1)` per end. `col` may be
        one colour or six, ordered [near, far, bottom, right, top, left]."""
        def quad(z, q):
            cx, hw, y0, y1 = q
            return [(cx - hw, y0, z), (cx + hw, y0, z),
                    (cx + hw, y1, z), (cx - hw, y1, z)]
        self.block(quad(z0, q0), quad(z1, q1), col)

    @staticmethod
    def chamf_sec(cx, hw, y0, y1, chamf, cy0=None):
        """A cross-section (x, y) hexagon: a rectangle whose TOP and BOTTOM
        edges are inset by `chamf`, so an extrusion of it reads as a carved
        barrel rather than a box. Returns 6 points, bottom edge first."""
        c0 = chamf if cy0 is None else cy0
        return [(-(hw - c0), y0), (hw - c0, y0), (hw, y0 + c0),
                (hw, y1 - chamf), (hw - chamf, y1), (-(hw - chamf), y1),
                (-hw, y1 - chamf), (-hw, y0 + c0)]

    def hull_z(self, sections, cols, cap_col=None):
        """Skin a run of (z, section) cross-sections along z, quad by quad, and
        cap the ends. All sections need the same vertex count. `cols` is one
        colour or one per section EDGE (edge i spans point i -> i+1)."""
        def col_of(i):
            return cols[i % len(cols)] if isinstance(cols, (list, tuple)) else cols
        for s in range(len(sections) - 1):
            z0, p0 = sections[s]
            z1, p1 = sections[s + 1]
            n = len(p0)
            cx = sum(p[0] for p in p0 + p1) / (2 * n)
            cy = sum(p[1] for p in p0 + p1) / (2 * n)
            axis = (cx, cy, 0.5 * (z0 + z1))
            for i in range(n):
                j = (i + 1) % n
                self.face([(p0[i][0], p0[i][1], z0), (p0[j][0], p0[j][1], z0),
                           (p1[j][0], p1[j][1], z1), (p1[i][0], p1[i][1], z1)],
                          col_of(i), away=axis)
        cc = cap_col or col_of(0)
        z0, p0 = sections[0]
        z1, p1 = sections[-1]
        ctr = (0.0, sum(q[1] for q in p0) / len(p0), z0)
        self.face([(x, y, z0) for x, y in p0], cc,
                  away=(ctr[0], ctr[1], z0 + 0.05))
        ctr = (0.0, sum(q[1] for q in p1) / len(p1), z1)
        self.face([(x, y, z1) for x, y in p1], cc,
                  away=(ctr[0], ctr[1], z1 - 0.05))

    def limb(self, pts, radii, col, hoof=None, hoof_col=None):
        """A leg: a polyline of tapered 4-sided segments (so the knee reads as
        a real bend), optionally capped by a small hoof box."""
        for i in range(len(pts) - 1):
            self.cyl(pts[i], pts[i + 1], radii[i], radii[i + 1], 4, col,
                     cap0=(i == 0), cap1=False, phase=math.pi * 0.25)
        if hoof:
            x, y, z = pts[-1]
            hw, hh, hl = hoof
            self.box(x - hw, x + hw, y - hh, y, z - hl, z + hl * 1.6,
                     hoof_col or col)

    def wedge_head(self, cx, z0, z1, hw_back, y0, y1_back, nose_hw, nose_y,
                    col, nose_col=None):
        """A head that tapers from a back rectangle down to a front edge (the
        nose/beak tip): 6 verts, 5 faces (back, bottom, top, two side tris)."""
        bl_b = (cx - hw_back, y0, z0)
        br_b = (cx + hw_back, y0, z0)
        bl_t = (cx - hw_back, y1_back, z0)
        br_t = (cx + hw_back, y1_back, z0)
        nl = (cx - nose_hw, nose_y, z1)
        nr = (cx + nose_hw, nose_y, z1)
        nc = nose_col or col
        self.face([bl_b, br_b, br_t, bl_t], col, away=((bl_b[0] + br_t[0]) / 2,
                   (bl_b[1] + br_t[1]) / 2, z0 - 0.05))
        ctr = (cx, (y0 + y1_back + nose_y) / 3.0, (z0 + z1) / 2.0)
        self.face([bl_b, br_b, nr, nl], nc, away=ctr)
        self.face([bl_t, br_t, nr, nl], nc, away=ctr)
        self.face([bl_b, bl_t, nl], col, away=ctr)
        self.face([br_b, br_t, nr], col, away=ctr)

    def tri_count(self):
        return sum(len(f) - 2 for f in self.faces)


# ------------------------------------------------------------------ blender glue --
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


# --------------------------------------------------------------------- goat --
def build_goat():
    """~0.8 m at the shoulder, ~1.3 m nose to rump. Origin at the feet,
    nose toward +z."""
    b = Builder()
    sec = Builder.chamf_sec
    # --- barrel body: deepest at the chest, tapering to a narrow rump, with a
    # slight belly sag through the middle. Flanks a shade darker than the back.
    side = ["goatBodyLo", "goatBody", "goatBodyLo", "goatBody",
            "goatBody", "goatBody", "goatBodyLo", "goatBody"]
    b.hull_z([(-0.55, sec(0.0, 0.150, 0.455, 0.735, 0.055)),
              (-0.28, sec(0.0, 0.180, 0.385, 0.772, 0.065)),
              (0.02, sec(0.0, 0.196, 0.378, 0.792, 0.075)),
              (0.24, sec(0.0, 0.192, 0.398, 0.796, 0.075)),
              (0.36, sec(0.0, 0.150, 0.455, 0.762, 0.060))],
             side, cap_col="goatBodyLo")
    # --- neck: a tapered block climbing forward at ~45 deg off the shoulders
    b.taper_z(0.28, (0.0, 0.118, 0.575, 0.790),
              0.58, (0.0, 0.088, 0.815, 1.000), "goatBody")
    # --- head: a long wedge, flat forehead, squared muzzle in the face stripe
    b.taper_z(0.55, (0.0, 0.080, 0.846, 1.006),
              0.72, (0.0, 0.066, 0.834, 0.966),
              ["goatBody", "goatFace", "goatBody", "goatBody",
               "goatFace", "goatBody"])
    b.taper_z(0.71, (0.0, 0.062, 0.834, 0.946),
              0.86, (0.0, 0.048, 0.826, 0.906), "goatFace")
    # --- ears: flat wedges angled out and back, pink on the inside
    for sx in (-1, 1):
        root_hi = (sx * 0.084, 0.992, 0.598)
        root_lo = (sx * 0.084, 0.930, 0.556)
        tip = (sx * 0.162, 0.986, 0.486)
        b.face([root_hi, root_lo, tip], "goatBody", hint=(sx * 0.9, 0.3, -0.2))
        b.face([root_hi, root_lo, tip], "goatEar", hint=(-sx * 0.9, -0.3, 0.2))
    # --- horns: three tapered segments, each swept further back than the last
    for sx in (-1, 1):
        pts = [(sx * 0.055, 1.010, 0.655), (sx * 0.066, 1.155, 0.618),
               (sx * 0.076, 1.248, 0.520), (sx * 0.086, 1.268, 0.408)]
        rad = [0.027, 0.020, 0.013, 0.005]
        for i in range(3):
            b.cyl(pts[i], pts[i + 1], rad[i], rad[i + 1], 4, "goatHorn",
                  cap0=(i == 0), cap1=(i == 2), phase=math.pi * 0.25)
    # --- beard: a short wedge hanging off the jaw
    b.taper_z(0.735, (0.0, 0.042, 0.760, 0.822),
              0.845, (0.0, 0.024, 0.672, 0.800), "goatFace")
    # --- tail: short and upturned
    b.cyl((0.0, 0.690, -0.530), (0.0, 0.818, -0.605), 0.036, 0.012, 4,
          "goatBody", phase=math.pi * 0.25)
    # --- legs: upper + lower at an angle so the knee reads, dark hooves
    for sx in (-1, 1):
        b.limb([(sx * 0.118, 0.470, 0.215), (sx * 0.118, 0.245, 0.248),
                (sx * 0.118, 0.062, 0.222)], [0.058, 0.042, 0.032],
               "goatLeg", hoof=(0.040, 0.062, 0.036), hoof_col="goatHoof")
        b.limb([(sx * 0.126, 0.470, -0.378), (sx * 0.126, 0.262, -0.428),
                (sx * 0.126, 0.062, -0.372)], [0.062, 0.044, 0.032],
               "goatLeg", hoof=(0.040, 0.062, 0.036), hoof_col="goatHoof")
    print("TRIS goat %d" % b.tri_count())
    return b


# --------------------------------------------------------------------- boar --
def build_boar():
    """~0.7 m at the shoulder hump, ~1.4 m nose to rump. Origin at the feet,
    nose toward +z, head carried LOW."""
    b = Builder()
    sec = Builder.chamf_sec
    # --- heavy body: light rump, deepest at the shoulders, pronounced hump.
    side = ["boarBodyLo", "boarFlank", "boarFlank", "boarBody",
            "boarBody", "boarBody", "boarFlank", "boarFlank"]
    b.hull_z([(-0.620, sec(0.0, 0.160, 0.370, 0.560, 0.055)),
              (-0.340, sec(0.0, 0.205, 0.330, 0.620, 0.070)),
              (-0.060, sec(0.0, 0.232, 0.310, 0.678, 0.085)),
              (0.140, sec(0.0, 0.245, 0.300, 0.716, 0.095)),
              (0.380, sec(0.0, 0.196, 0.322, 0.628, 0.075))],
             side, cap_col="boarBodyLo")
    # --- bristle ridge: a thin raised strip of wedges down the spine
    for zc, ytop in ((-0.44, 0.578), (-0.26, 0.612), (-0.06, 0.678),
                     (0.12, 0.716), (0.30, 0.664)):
        lo = ytop - 0.030
        tri = [(0.0, lo, zc - 0.075), (0.0, lo, zc + 0.075),
               (0.0, ytop + 0.085, zc + 0.020)]
        b.face(tri, "boarBristle", hint=(1.0, 0.0, 0.0))
        b.face(tri, "boarBristle", hint=(-1.0, 0.0, 0.0))
    # --- head: a big wedge slung low off the shoulders, nose at knee height
    b.taper_z(0.300, (0.0, 0.182, 0.300, 0.616),
              0.540, (0.0, 0.140, 0.204, 0.470),
              ["boarBody", "boarBody", "boarBody", "boarFlank",
               "boarBody", "boarFlank"])
    # --- snout: a long taper ending in a pale flat disc
    b.taper_z(0.525, (0.0, 0.086, 0.186, 0.386),
              0.775, (0.0, 0.062, 0.172, 0.302),
              ["boarBody", "boarSnout", "boarBody", "boarBody",
               "boarBody", "boarBody"])
    # --- tusks: two short ivory segments curving up out of the lower jaw
    for sx in (-1, 1):
        pts = [(sx * 0.070, 0.190, 0.680), (sx * 0.090, 0.268, 0.752),
               (sx * 0.102, 0.352, 0.744)]
        rad = [0.024, 0.015, 0.005]
        for i in range(2):
            b.cyl(pts[i], pts[i + 1], rad[i], rad[i + 1], 4, "boarTusk",
                  cap0=(i == 0), cap1=(i == 1), phase=math.pi * 0.25)
    # --- ears: small triangles, dark outside, flank-brown inside
    for sx in (-1, 1):
        tri = [(sx * 0.148, 0.508, 0.476), (sx * 0.148, 0.416, 0.410),
               (sx * 0.256, 0.576, 0.396)]
        b.face(tri, "boarFlank", hint=(sx * 0.6, 0.8, -0.2))
        b.face(tri, "boarBody", hint=(-sx * 0.6, -0.8, 0.2))
    # --- tail: thin, with a tuft on the end
    b.cyl((0.0, 0.520, -0.612), (0.0, 0.558, -0.730), 0.018, 0.011, 4,
          "boarBody", phase=math.pi * 0.25)
    b.box(-0.032, 0.032, 0.512, 0.578, -0.790, -0.726, "boarBristle")
    # --- legs: short and thick, a visible knee, near-black hooves
    for sx in (-1, 1):
        b.limb([(sx * 0.136, 0.360, 0.142), (sx * 0.136, 0.208, 0.166),
                (sx * 0.136, 0.068, 0.146)], [0.068, 0.050, 0.038],
               "boarBody", hoof=(0.044, 0.068, 0.038), hoof_col="boarHoof")
        b.limb([(sx * 0.142, 0.360, -0.418), (sx * 0.142, 0.216, -0.454),
                (sx * 0.142, 0.068, -0.406)], [0.072, 0.052, 0.038],
               "boarBody", hoof=(0.044, 0.068, 0.038), hoof_col="boarHoof")
    print("TRIS boar %d" % b.tri_count())
    return b


# --------------------------------------------------------------------- gull --
def build_gull():
    """Flight pose: origin at the body centre (not the feet -- this one flies).
    Wings spread flat and blocky, root to tip, black tips."""
    b = Builder()
    # body: slim hexagon-prism torso, tail behind, chest forward
    hw, y0, y1, chamf = 0.075, -0.075, 0.085, 0.03
    poly = [(-hw, y0), (hw, y0), (hw, y1 - chamf), (hw - chamf, y1),
            (-(hw - chamf), y1), (-hw, y1 - chamf)]
    b.prism_z(poly, -0.20, 0.14, "gullBody", cap_col="gullBodyLo")
    # tail fan (flat wedge behind)
    b.face([(-0.09, 0.0, -0.20), (0.09, 0.0, -0.20), (0.0, 0.0, -0.36)],
           "gullBody", hint=(0.0, 1.0, 0.0))
    b.face([(-0.09, 0.0, -0.20), (0.0, 0.0, -0.36), (0.09, 0.0, -0.20)],
           "gullBody", hint=(0.0, -1.0, 0.0))
    # head + beak: small wedge forward of the chest
    b.wedge_head(0.0, 0.10, 0.30, 0.06, -0.03, 0.09, 0.012, 0.02,
                 "gullBody", nose_col="gullBeak")
    # wings: each a root panel (grey) + a tip panel (black), spread flat,
    # slight forward sweep and a touch of dihedral (tip a bit higher).
    span_root = 0.10
    span_mid = 0.55
    span_tip = 0.85
    for sx in (-1, 1):
        root_in = [(sx * span_root, -0.02, 0.06), (sx * span_root, 0.02, -0.10),
                   (sx * span_root, 0.02, -0.10), (sx * span_root, -0.02, 0.06)]
        # root panel: inner chord (thin box) -> mid chord
        near = [(sx * span_root, -0.015, 0.08), (sx * span_root, 0.015, 0.08),
                (sx * span_root, 0.015, -0.14), (sx * span_root, -0.015, -0.14)]
        far = [(sx * span_mid, 0.02, 0.0), (sx * span_mid, 0.045, 0.0),
               (sx * span_mid, 0.045, -0.16), (sx * span_mid, 0.02, -0.16)]
        b.block(near, far, "gullWing")
        tip_near = far
        tip_far = [(sx * span_tip, 0.06, -0.08), (sx * span_tip, 0.075, -0.08),
                   (sx * span_tip, 0.075, -0.20), (sx * span_tip, 0.06, -0.20)]
        b.block(tip_near, tip_far, "gullTip")
    print("TRIS gull %d" % b.tri_count())
    return b


# ------------------------------------------------------------------ renders --
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
    try:
        vs.view_transform = "Standard"
    except TypeError as e:
        print("view_transform:", e)
    world = scene.world or bpy.data.worlds.new("FaunaWorld")
    scene.world = world
    world.color = srgb_to_linear(_hex("#CFE3EC"))
    ims = scene.render.image_settings
    ims.file_format = pick(enum_ids(ims.bl_rna.properties["file_format"]), "PNG")
    scene.render.film_transparent = False


def render_one(name, ob, out_dir, scene, eye, target, lens=45, res=(800, 600),
               light=None, sun_deg=None):
    """`light` aims the shadow direction, `sun_deg` swings the workbench key
    light in WORLD space (off by default -- it is view-relative otherwise).
    Both are restored afterwards, so a render that passes neither is bit-for-bit
    the render this scene would have produced without them."""
    sh = scene.display.shading
    prev_light = tuple(scene.display.light_direction)
    prev_ws = sh.use_world_space_lighting
    prev_rot = sh.studiolight_rotate_z
    if light is not None:
        scene.display.light_direction = light
    if sun_deg is not None:
        sh.use_world_space_lighting = True
        sh.studiolight_rotate_z = math.radians(sun_deg)
    cam_data = bpy.data.cameras.new(name + "Cam")
    cam = bpy.data.objects.new(name + "Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.clip_end = 100.0
    cam_data.lens = lens
    cam.location = Vector(eye)
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    path = os.path.join(out_dir, "%s.png" % name)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.cameras.remove(cam_data)
    scene.display.light_direction = prev_light
    sh.use_world_space_lighting = prev_ws
    sh.studiolight_rotate_z = prev_rot
    return path


# ------------------------------------------------------------------ export --
def export_fbx(src, path):
    """Export ONE top-level mesh object, the yaw burnt into a copy of its data.
    Settings copied unchanged from steamer.py's export_fbx."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    me = src.data.copy()
    me.transform(Matrix.Rotation(math.radians(-90.0), 4, "Z"))   # fwd +X -> -Y (= Unity +Z)
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

    goat = build_goat()
    boar = build_boar()
    gull = build_gull()

    ob_goat = to_object("Goat", goat, make_material("GoatPaint", PAL["goatBody"]))
    ob_boar = to_object("Boar", boar, make_material("BoarPaint", PAL["boarBody"]))
    ob_gull = to_object("Gull", gull, make_material("GullPaint", PAL["gullBody"]))

    if "--no-render" not in argv:
        out_dir = opt("--renders", os.path.join(REPO, "docs", "art-direction", "fauna"))
        os.makedirs(out_dir, exist_ok=True)
        setup_render(scene)
        all_obs = (ob_goat, ob_boar, ob_gull)

        def solo(ob):
            for o in all_obs:
                o.hide_render = o is not ob

        # goat/boar: three-quarter front, a little above, close enough that the
        # animal fills ~60% of the frame, sun from the front-left of the shot.
        # (Blender frame: +X = nose, +Y = the animal's left, +Z = up.)
        sun = (0.54, -0.11, 0.84)
        solo(ob_goat)
        print("render", render_one("goat", ob_goat, out_dir, scene,
                                    (2.75, 1.72, 1.70), (0.02, 0.0, 0.60), 50,
                                    light=sun, sun_deg=285))
        solo(ob_boar)
        print("render", render_one("boar", ob_boar, out_dir, scene,
                                    (2.85, 1.80, 1.60), (0.02, 0.0, 0.46), 50,
                                    light=sun, sun_deg=285))
        solo(ob_gull)
        print("render", render_one("gull", ob_gull, out_dir, scene,
                                    (2.0, 1.0, 2.0), (0.0, 0.0, 0.0), 32))
        for o in all_obs:
            o.hide_render = False

    if "--no-export" not in argv:
        export_fbx(ob_goat, GOAT_FBX)
        export_fbx(ob_boar, BOAR_FBX)
        export_fbx(ob_gull, GULL_FBX)

    blend = opt("--blend")
    if blend:
        bpy.ops.wm.save_as_mainfile(filepath=blend)


if __name__ == "__main__":
    main()
