"""SeaSick stern-paddle player ship, based on Astra concept V6.

Creates a watertight clipped-transom hull and a modular stern-wheel assembly.
Crew and cannons are deliberately omitted.

Run:
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        -P tools/blender/stern_paddle_astra_v1.py

Ship frame follows the existing steamer pipeline: +x starboard, +y up,
+z bow, metres. The wheel and cassette frame are authored around the axle so
Unity can replace or rotate them without changing the hull mesh.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import steamer as base
import steamer_form


REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
MODEL_DIR = os.path.join(REPO, "Assets", "_Project", "Art", "Ship",
                         "SternPaddleAstraV1", "Models")
BLEND_PATH = os.path.join(HERE, "source", "stern-paddle-astra-v1.blend")
RENDER_DIR = os.path.join(REPO, "art-staging", "stern-paddle-astra-model-v1",
                          "renders")

HULL_FBX = os.path.join(MODEL_DIR, "stern_paddle_hull.fbx")
FRAME_FBX = os.path.join(MODEL_DIR, "stern_paddle_module_frame.fbx")
WHEEL_FBX = os.path.join(MODEL_DIR, "stern_paddle_wheel.fbx")


class FlatStern(object):
    """Deck adapter for the closed, flat-transom V6 hull."""

    def __init__(self, form):
        self.form = form
        self.half = 0.0
        self.fwd_z = form.transom_z + 2.1
        self.plat_z = form.transom_z + 3.2
        self.ramp = 0.8
        self.floor = -0.9

    def deck_y(self, z):
        return self.form.main_deck_y(z)

    def in_well(self, z):
        return False

    def floor_y(self, z):
        return max(self.floor, self.form.hull_keel_y(z))

    def ceil_y(self, z):
        return self.deck_y(z) - base.CEIL_T


def hull_stations(form):
    z0 = form.transom_z
    z1 = form.half_l
    for _ in range(30):
        z1 = form.z_fwd(form.shell_top_y(z1))
    z1 -= 0.002
    n = 42
    zs = []
    for i in range(n + 1):
        u = float(i) / n
        eased = 0.55 * u + 0.45 * (0.5 - 0.5 * math.cos(math.pi * u))
        zs.append(z0 + (z1 - z0) * eased)
    return zs


def build_closed_hull(b, form, stern):
    """Loft a full hull with an intact, broad transom and open deck."""
    zs = hull_stations(form)

    def section(z):
        ys = base.hull_levels(form, stern, z)
        xs = [form.half_breadth(z, y, skeg=False) for y in ys]
        keel = form.hull_keel_y(z)
        if ys[0] <= keel + 1e-6:
            xs[0] = (form.f["stemHalfThickness"]
                     if z > form.f["parallelFwdZ"] else 0.0)
        return ys, xs

    def edge(z):
        ymd = form.main_deck_y(z)
        yt = ymd + form.bulwark
        yd = stern.deck_y(z)
        keel = form.hull_keel_y(z)
        ho = form.half_breadth(z, yt, skeg=False) if yt > keel else 0.0
        hd = form.half_breadth(z, yd, skeg=False) if yd > keel else 0.0
        hi_t = max(ho - base.TB, 0.0)
        hi_d = min(max(hd - base.TB, 0.0), hi_t) if ho > 0 else 0.0
        return yd, yt, ho, hi_t, hi_d

    for j in range(len(zs) - 1):
        za, zb = zs[j], zs[j + 1]
        zm = 0.5 * (za + zb)
        ya, xa = section(za)
        yb, xb = section(zb)

        for k in range(len(ya) - 1):
            for sx in (1.0, -1.0):
                b.face([(sx * xa[k], ya[k], za),
                        (sx * xa[k + 1], ya[k + 1], za),
                        (sx * xb[k + 1], yb[k + 1], zb),
                        (sx * xb[k], yb[k], zb)],
                       base.BAND_COL[k],
                       away=(0.0, 0.5 * (ya[k] + yb[k + 1]) + 1.0,
                             zm - math.copysign(1.5, zm)))

        b.face([(-xa[0], ya[0], za), (xa[0], ya[0], za),
                (xb[0], yb[0], zb), (-xb[0], yb[0], zb)],
               "plankLo" if ya[0] > base.BOOT_HI else "bottom",
               hint=(0, -1, 0.2 if zm > 0 else -0.2))

        ea, eb = edge(za), edge(zb)
        for sx in (1.0, -1.0):
            b.face([(sx * ea[2], ea[1], za), (sx * ea[3], ea[1], za),
                    (sx * eb[3], eb[1], zb), (sx * eb[2], eb[1], zb)],
                   "sheer", hint=(0, 1, 0))
            b.face([(sx * ea[3], ea[1], za), (sx * ea[4], ea[0], za),
                    (sx * eb[4], eb[0], zb), (sx * eb[3], eb[1], zb)],
                   "inside", hint=(-sx, 0.05, 0))

            ma = max(ea[4] - base.MARGIN_W, 0.0)
            mb = max(eb[4] - base.MARGIN_W, 0.0)
            b.face([(sx * ma, ea[0], za), (sx * ea[4], ea[0], za),
                    (sx * eb[4], eb[0], zb), (sx * mb, eb[0], zb)],
                   "margin", hint=(0, 1, 0))
            nk = int(math.ceil(max(ma, mb) / base.PLANK_W))
            for k in range(nk):
                a0, a1 = min(k * base.PLANK_W, ma), min((k + 1) * base.PLANK_W, ma)
                b0, b1 = min(k * base.PLANK_W, mb), min((k + 1) * base.PLANK_W, mb)
                b.face([(sx * a0, ea[0], za), (sx * a1, ea[0], za),
                        (sx * b1, eb[0], zb), (sx * b0, eb[0], zb)],
                       ("deckA", "deckB", "deckC")[k % 3], hint=(0, 1, 0))

    # Full transom, including the deck and bulwark. No wheel notch is cut.
    zt = zs[0]
    yt, xt = section(zt)
    for k in range(len(yt) - 1):
        b.face([(-xt[k], yt[k], zt), (xt[k], yt[k], zt),
                (xt[k + 1], yt[k + 1], zt), (-xt[k + 1], yt[k + 1], zt)],
               base.BAND_COL[k], hint=(0, 0, -1))

def swept_transom_rail(b, form, rise=0.9):
    """Straight in plan at the transom, arched only vertically in Z-up."""
    z = form.transom_z - 0.10
    y_side = form.main_deck_y(z) + form.bulwark + 0.05
    half = form.half_breadth(form.transom_z, y_side, skeg=False)
    n = 10
    h = 0.26
    depth = 0.34
    rings = []
    for i in range(n + 1):
        x = -half + 2.0 * half * i / n
        u = abs(x) / max(half, 1e-6)
        y = y_side + rise * (1.0 - u * u)
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


def add_bolt_z(b, x, y, z, aft=True, r=0.11):
    dz = -0.18 if aft else 0.18
    b.cyl((x, y, z), (x, y, z + dz), r, r, 6, "bolt")


def build_transom_mounts(b, form, wheel_width):
    """Permanent hull-side socket rails for the removable wheel cassette."""
    zt = form.transom_z - 0.19
    y0 = -0.35
    y1 = form.main_deck_y(form.transom_z) + form.bulwark + 0.28
    x = 0.5 * wheel_width + 0.45
    for sx in (-1.0, 1.0):
        xc = sx * x
        b.box(xc - 0.24, xc + 0.24, y0, y1, zt - 0.10, zt + 0.10, "steel")
        for t in (0.18, 0.82):
            add_bolt_z(b, xc, y0 + (y1 - y0) * t, zt - 0.10)


def build_module_frame(b, wheel_radius, wheel_width, transom_offset):
    """Non-rotating cassette frame, authored around the wheel axle."""
    hw = 0.5 * wheel_width
    x = hw + 0.20
    y0, y1 = -wheel_radius * 0.70, wheel_radius + 0.48
    z0, z1 = 0.24, 0.58
    for sx in (-1.0, 1.0):
        xc = sx * x
        b.box(xc - 0.18, xc + 0.18, y0, y1, z0, z1, "steelLo")
        b.box(xc - 0.22, xc + 0.22, -0.18, 0.18, z1, transom_offset, "steel")
        b.cyl((xc, 0.0, -0.14), (xc, 0.0, 0.72), 0.40, 0.40, 8,
              "steel", phase=math.pi / 8.0)
    b.box(-x - 0.18, x + 0.18, y1 - 0.26, y1, z0, z1, "steel")
    for sx in (-1.0, 1.0):
        b.cyl((sx * x, y1 - 0.13, transom_offset - 0.18),
              (sx * x, y1 - 0.13, transom_offset + 0.12),
              0.15, 0.15, 8, "bolt")


def make_instance(name, src, parent, location=(0.0, 0.0, 0.0)):
    ob = bpy.data.objects.new(name, src.data)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    ob.location = location
    return ob


def build_all():
    form = steamer_form.HullForm()
    stern = FlatStern(form)

    # V6 keeps the wheel outside the intact transom.
    design = dict(form.d)
    design["wheelRadius"] = 1.75
    design["wheelWidth"] = 4.8
    design["wheelFloats"] = 8
    design["wheelFloatDepth"] = 0.78
    axle = {"x": 0.0, "y": 0.78, "z": form.transom_z - 1.86}

    hull = base.Builder()
    build_closed_hull(hull, form, stern)
    base.build_skeg_rudder(hull, form)
    base.build_bilge_keels(hull, form)
    base.build_trim(hull, form, stern, design)
    base.build_topsides(hull, form, stern, design)
    swept_transom_rail(hull, form)
    build_transom_mounts(hull, form, design["wheelWidth"])

    wheel = base.Builder()
    base.build_wheel(wheel, design)

    frame = base.Builder()
    build_module_frame(frame, design["wheelRadius"], design["wheelWidth"],
                       form.transom_z - axle["z"])

    mat = base.make_material("AstraSteamerPaint", base.PAL["plankMid"])
    ob_hull = base.to_object("AstraSteamerHull", hull, mat)
    ob_frame = base.to_object("AstraWheelModuleFrame", frame, mat)
    ob_wheel = base.to_object("AstraPaddleWheel", wheel, mat)

    socket = bpy.data.objects.new("WheelModuleSocket", None)
    socket.empty_display_type = "ARROWS"
    socket.empty_display_size = 0.8
    socket.location = base.S((axle["x"], axle["y"], axle["z"]))
    socket["ship_x"] = axle["x"]
    socket["ship_y"] = axle["y"]
    socket["ship_z"] = axle["z"]
    socket["module_contract"] = "frame fixed; wheel rotates about local axle"
    bpy.context.scene.collection.objects.link(socket)

    fitted_frame = make_instance("WheelModuleFrame_Fitted", ob_frame, socket)
    fitted_wheel = make_instance("PaddleWheel_Rotating", ob_wheel, socket)

    ob_frame.hide_render = True
    ob_frame.hide_viewport = True
    ob_wheel.hide_render = True
    ob_wheel.hide_viewport = True

    print("TRIS hull %d frame %d wheel %d total %d" %
          (hull.tri_count(), frame.tri_count(), wheel.tri_count(),
           hull.tri_count() + frame.tri_count() + wheel.tri_count()))
    return form, ob_hull, ob_frame, ob_wheel, socket, fitted_frame, fitted_wheel


def main():
    print("Blender", bpy.app.version_string)
    base.clear_scene()
    scene = bpy.context.scene
    _, hull, frame, wheel, _, _, _ = build_all()

    os.makedirs(MODEL_DIR, exist_ok=True)
    base.export_fbx(hull, HULL_FBX)
    base.export_fbx(frame, FRAME_FBX)
    base.export_fbx(wheel, WHEEL_FBX)

    os.makedirs(RENDER_DIR, exist_ok=True)
    sea = base.sea_plane()
    base.setup_render(scene)
    for path in base.render_views(RENDER_DIR, scene, sea, debug=True):
        print("render", path)
    bpy.data.objects.remove(sea, do_unlink=True)

    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print("blend", BLEND_PATH)


if __name__ == "__main__":
    main()
