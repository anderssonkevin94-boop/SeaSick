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
    # goat -- grey-white body, dark legs/face, tan-dark horns
    "goatBody": "#D9D5C9",
    "goatBodyLo": "#B8B3A5",
    "goatDark": "#3B342D",
    "goatHorn": "#5B4E3C",
    # boar -- dark brown, pale snout, bristle ridge, pale tusks
    "boarBody": "#4A3220",
    "boarBodyLo": "#332217",
    "boarSnout": "#C9AD87",
    "boarTusk": "#EDE6D6",
    "boarBristle": "#241811",
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
    b = Builder()
    # legs: four tapered frusta (cyl with n=4), narrow hoof -> wider at belly
    leg_y = 0.42
    for x, z in ((-0.13, 0.42), (0.13, 0.42), (-0.13, -0.42), (0.13, -0.42)):
        b.cyl((x, 0.0, z), (x, leg_y, z), 0.045, 0.075, 4, "goatDark")
    # torso: chamfered-top box via a hexagon cross-section prism
    hw, y0, y1, chamf = 0.20, 0.40, 0.78, 0.09
    poly = [(-hw, y0), (hw, y0), (hw, y1 - chamf), (hw - chamf, y1),
            (-(hw - chamf), y1), (-hw, y1 - chamf)]
    b.prism_z(poly, -0.55, 0.35, "goatBody", cap_col="goatBodyLo")
    # neck + head: a wedge tapering forward and down to the muzzle
    b.wedge_head(0.0, 0.35, 0.85, 0.13, 0.55, 0.85, 0.05, 0.62,
                 "goatDark", nose_col="goatDark")
    # ears: tiny flat triangles off the head sides
    for sx in (-1, 1):
        b.face([(sx * 0.13, 0.75, 0.55), (sx * 0.24, 0.78, 0.50),
                (sx * 0.13, 0.70, 0.48)], "goatDark",
               hint=(sx * 1.0, 0.1, -0.1))
    # horns: short back-swept tapered spikes
    for sx in (-1, 1):
        b.cyl((sx * 0.09, 0.82, 0.62), (sx * 0.13, 1.02, 0.48), 0.025, 0.006,
              4, "goatHorn")
    # tail
    b.cyl((0.0, 0.62, -0.55), (0.0, 0.72, -0.66), 0.03, 0.01, 4, "goatDark")
    print("TRIS goat %d" % b.tri_count())
    return b


# --------------------------------------------------------------------- boar --
def build_boar():
    b = Builder()
    leg_y = 0.34
    for x, z in ((-0.16, 0.48), (0.16, 0.48), (-0.16, -0.48), (0.16, -0.48)):
        b.cyl((x, 0.0, z), (x, leg_y, z), 0.05, 0.085, 4, "boarBody")
    # stocky torso, chamfered top
    hw, y0, y1, chamf = 0.24, 0.33, 0.70, 0.10
    poly = [(-hw, y0), (hw, y0), (hw, y1 - chamf), (hw - chamf, y1),
            (-(hw - chamf), y1), (-hw, y1 - chamf)]
    b.prism_z(poly, -0.65, 0.45, "boarBody", cap_col="boarBodyLo")
    # bristle ridge along the spine: a row of small flat fins
    for t in (-0.5, -0.25, 0.0, 0.25):
        zc = -0.5 + t * 0.9
        b.face([(0.0, 0.70, zc - 0.06), (0.0, 0.70, zc + 0.06),
                (0.0, 0.86, zc)], "boarBristle", hint=(0.0, 0.3, 0.0))
    # head: blunt wedge, dark, with a pale snout block on the front tip
    b.wedge_head(0.0, 0.45, 0.70, 0.16, 0.38, 0.66, 0.10, 0.40,
                 "boarBody", nose_col="boarBody")
    snout_near = [(-0.10, 0.30, 0.68), (0.10, 0.30, 0.68),
                  (0.10, 0.50, 0.68), (-0.10, 0.50, 0.68)]
    snout_far = [(-0.08, 0.32, 0.80), (0.08, 0.32, 0.80),
                 (0.08, 0.46, 0.80), (-0.08, 0.46, 0.80)]
    b.block(snout_near, snout_far, "boarSnout")
    # tusks: tiny pale curved spikes either side of the snout
    for sx in (-1, 1):
        b.cyl((sx * 0.08, 0.32, 0.78), (sx * 0.13, 0.42, 0.82), 0.018, 0.004,
              4, "boarTusk")
    # ears
    for sx in (-1, 1):
        b.face([(sx * 0.16, 0.66, 0.44), (sx * 0.27, 0.70, 0.36),
                (sx * 0.16, 0.60, 0.32)], "boarBody",
               hint=(sx * 1.0, 0.15, -0.1))
    # tail: short curl
    b.cyl((0.0, 0.50, -0.65), (0.0, 0.58, -0.74), 0.025, 0.008, 4, "boarBody")
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


def render_one(name, ob, out_dir, scene, eye, target, lens=45, res=(800, 600)):
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

        solo(ob_goat)
        print("render", render_one("goat", ob_goat, out_dir, scene,
                                    (2.6, 1.2, 2.8), (0.0, 0.45, 0.0), 32))
        solo(ob_boar)
        print("render", render_one("boar", ob_boar, out_dir, scene,
                                    (2.8, 1.2, 3.0), (0.0, 0.45, 0.0), 32))
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
