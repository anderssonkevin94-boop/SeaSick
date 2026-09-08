# SeaSick -- island structure, v2: the whole island in the "carved" style.
#
# Re-runnable:
#   exec(open('/Users/kevinandersson/Desktop/SeaSick/tools/blender/seasick_island_v2.py').read())
#
# This is a VISUAL SPEC for Unity, not a Unity asset. Every part of it maps
# to something SeaSick.Terrain already has a hook for:
#
#   field()        the height function -- mask, relief noise, PROUD ROCK as a
#                  ridged field max'd over the soil (that is `RockBreak`),
#                  a beach band compressed to a ramp, and a shallow shelf
#                  before the seabed drops (that is `Seabed`/`Skerry`).
#   paint()        what `VertexColour` should do: sand below 1.6 m, olive
#                  grass with moss and dry-top variation, scree where the
#                  slope steepens, stone where rock WON or the face is sheer.
#   scatter()      what `IslandScenery` should do: trees by slope/rock/
#                  verdancy with a clumping noise, species by altitude and
#                  climate, boulders on the scree, SHARD CLIFFS wherever rock
#                  is proud and steep, skerries on the shelf.
#   the sea        one plane coloured by the depth under it: a foam line, a
#                  turquoise shelf, teal, then deep. This is the ocean
#                  shader's shoal tint, and it is half of what makes the
#                  reference boards read as islands.
#
# Three islands are built from ONE rule set with different knobs, because
# "various sizes and shapes with one style" is only true if the same code
# makes all of them.

import bpy, bmesh, math, random
from math import sin, cos, pi, atan2, hypot
from mathutils import Vector, noise

SS_NO_AUTORUN = 1
STYLE_PY = "/Users/kevinandersson/Desktop/SeaSick/tools/blender/seasick_style.py"
exec(open(STYLE_PY).read())

ICOLL = "ISLAND_V2"
SEA = dict(foam=(0.86, 0.92, 0.90), shallow=(0.42, 0.78, 0.72), teal=(0.11, 0.46, 0.56),
           deep=(0.05, 0.19, 0.33), seabed=(0.62, 0.60, 0.46), seabed_d=(0.22, 0.32, 0.30))
WIND = 0.35     # radians; every tree yaws to this +/- a jitter, like the resources say


# ------------------------------------------------------------------ noise ---

def nz(x, y, seed, octs=4, lac=2.0, gain=0.5):
    a, f, s, nrm = 1.0, 1.0, 0.0, 0.0
    for o in range(octs):
        s += a * noise.noise(Vector((x * f, y * f, seed * 13.7 + o * 3.1)))
        nrm += a
        a *= gain
        f *= lac
    return s / nrm                      # about -1..1


def ridged(x, y, seed, octs=3):
    a, f, s, nrm = 1.0, 1.0, 0.0, 0.0
    for o in range(octs):
        s += a * (1.0 - abs(noise.noise(Vector((x * f, y * f, seed * 7.7 + o * 5.3)))))
        nrm += a
        a *= 0.5
        f *= 2.1
    return s / nrm                      # 0..1, sharp at 1


def sat(x):
    return 0.0 if x < 0 else 1.0 if x > 1 else x


# ------------------------------------------------------------------ field ---

class Island:
    def __init__(self, name, cx, cy, R, relief, rock, verdancy, kind, seed, shelf=1.5):
        self.name, self.cx, self.cy, self.R = name, cx, cy, R
        self.relief, self.rock, self.verdancy = relief, rock, verdancy
        self.kind, self.seed, self.shelf = kind, seed, shelf
        # The rock threshold is a QUANTILE of the ridged field, not a number:
        # Perlin's |n| sits near zero, so 1-|n| is above 0.75 nearly
        # everywhere and a fixed 0.75 threshold put rock over the whole
        # island. `rock` = 0.9 now means "the top 27 % of the field breaks
        # out", whatever noise is underneath, and the same knob will mean the
        # same thing in Unity's SimplexD.
        rng = random.Random(seed)
        samp = sorted(ridged((rng.random() * 2 - 1) * 1.6, (rng.random() * 2 - 1) * 1.6, seed + 4)
                      for _ in range(600))
        self.rock_thr = samp[int(len(samp) * (1.0 - 0.30 * max(0.0, rock)))] if rock > 0 else 9.0

    def field(self, x, y):
        """(height, proud rock in metres) at a world point. Analytic, so the
        scatter can ask it anywhere and never disagree with the mesh."""
        dx, dy = x - self.cx, y - self.cy
        r = hypot(dx, dy)
        th = atan2(dy, dx)
        R, s = self.R, self.seed
        edge = R * (1.0 + 0.32 * nz(cos(th) * 1.3, sin(th) * 1.3, s + 1, octs=2))
        d = r / max(1.0, edge)
        mask = sat(1.0 - d)
        mask = sat(mask + 0.16 * mask * nz(x / (R * 0.6), y / (R * 0.6), s + 2, octs=3))
        if d < 1.0:
            base = self.relief * mask ** 1.35 * \
                (0.55 + 0.45 * (nz(x / (R * 0.9), y / (R * 0.9), s + 3, octs=4) * 0.5 + 0.5))
            # Proud rock: ridged noise max'd over the soil. The THRESHOLD is
            # the knob, not the amplitude -- at 0.62 half the island was
            # rock and the wood had nowhere to stand; at 0.74 rock breaks
            # out on the spurs and the summit and the flanks stay soil.
            rg = ridged(x / (R * 0.45), y / (R * 0.45), s + 4)
            rock = max(0.0, rg - self.rock_thr) * self.relief * 2.2 * mask ** 0.8
            h = base + rock
            if h < 3.0:                      # beach ramp
                h = h * (0.35 + 0.65 * h / 3.0)
            return h, rock
        e = d - 1.0
        sea = -(min(e, 0.5) * 2.0 * self.shelf + max(0.0, e - 0.5) ** 1.4 * 40.0)
        sea += 0.5 * nz(x / 30.0, y / 30.0, s + 5, octs=2)
        return sea, 0.0

    def grad(self, x, y, e=1.5):
        hx1, _ = self.field(x + e, y); hx0, _ = self.field(x - e, y)
        hy1, _ = self.field(x, y + e); hy0, _ = self.field(x, y - e)
        return (hx1 - hx0) / (2 * e), (hy1 - hy0) / (2 * e)


def sea_depth(islands, x, y):
    return -max(isl.field(x, y)[0] for isl in islands)


# ----------------------------------------------------------------- ground ---

def build_ground(isl, coll, step):
    R = isl.R
    x0, y0 = isl.cx - 1.6 * R, isl.cy - 1.6 * R
    n = int(3.2 * R / step) + 1
    B = Build()
    data = {}
    for j in range(n):
        for i in range(n):
            x, y = x0 + i * step, y0 + j * step
            h, rock = isl.field(x, y)
            data[(i, j)] = [h, rock]
            B.vert((x, y, h))
    for j in range(n):
        for i in range(n):
            h, rock = data[(i, j)]
            hx1 = data[(min(n - 1, i + 1), j)][0]; hx0 = data[(max(0, i - 1), j)][0]
            hy1 = data[(i, min(n - 1, j + 1))][0]; hy0 = data[(i, max(0, j - 1))][0]
            slope = hypot((hx1 - hx0) / (2 * step), (hy1 - hy0) / (2 * step))
            data[(i, j)].append(slope)

    def paint(nrm, p):
        i = int(round((p.x - x0) / step)); j = int(round((p.y - y0) / step))
        h, rock, slope = data[(i, j)]
        up = sat(1.0 / math.sqrt(1.0 + slope * slope))       # cos of the slope
        if h < -0.15:
            return mix(SEA["seabed"], SEA["seabed_d"], sat(-h / 9.0))
        if rock > 1.2 or slope > 1.0:
            tone = C["stone_d"] if up < 0.55 else C["stone_m"] if up < 0.85 else C["stone_l"]
            return shade(tone, 0.74 + 0.26 * up)
        g = mix(C["grass"], C["moss"], sat(nz(p.x / 22.0, p.y / 22.0, isl.seed + 8, octs=2) * 0.5 + 0.5))
        if isl.relief > 8:
            g = mix(g, C["dry"], 0.4 * sat((h - isl.relief * 0.55) / (isl.relief * 0.4)))
        # Scree starts at 31 degrees, not 24: at 0.45 the whole flank of a
        # 30 m island went two-thirds grey and the wood stood on clay.
        if slope > 0.60:
            g = mix(g, C["stone_m"], 0.55 * sat((slope - 0.60) / 0.35))
        if h < 2.0:
            g = mix(C["sand"], g, sat((h - 0.8) / 1.2))
        return shade(g, 0.82 + 0.18 * up)

    for j in range(n - 1):
        for i in range(n - 1):
            a = j * n + i; b = a + 1; c = a + n; d = c + 1
            B.tri(a, b, d, paint); B.tri(a, d, c, paint)
    ob = B.emit("I2_%s_Ground" % isl.name, coll, smooth=True, recalc=False)
    return ob, data


def build_sea(islands, coll, x0, x1, y0, y1, step=6.0):
    nx, ny = int((x1 - x0) / step) + 1, int((y1 - y0) / step) + 1
    B = Build()
    depth = {}
    for j in range(ny):
        for i in range(nx):
            x, y = x0 + i * step, y0 + j * step
            depth[(i, j)] = sea_depth(islands, x, y)
            B.vert((x, y, 0.0))

    def paint(nrm, p):
        i = int(round((p.x - x0) / step)); j = int(round((p.y - y0) / step))
        dep = depth[(i, j)]
        c = mix(SEA["shallow"], SEA["teal"], sat(dep / 5.0))
        c = mix(c, SEA["deep"], sat((dep - 5.0) / 26.0))
        if dep < 0.7:
            c = mix(c, SEA["foam"], (1.0 - sat(dep / 0.7)) * 0.65)
        return c

    for j in range(ny - 1):
        for i in range(nx - 1):
            a = j * nx + i; b = a + 1; c = a + nx; d = c + 1
            B.tri(a, b, d, paint); B.tri(a, d, c, paint)
    ob = B.emit("I2_Sea", coll, smooth=True, recalc=False)
    m = bpy.data.materials.get("SS_Sea")
    if m is None:
        m = bpy.data.materials.new("SS_Sea")
        m.use_nodes = True
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        b.inputs["Roughness"].default_value = 0.28
        vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
        nt.links.new(vc.outputs["Color"], b.inputs["Base Color"])
    ob.data.materials[0] = m
    return ob


# ---------------------------------------------------------------- scatter ---

def stone_rule_for(hmax):
    def rule(n_, p):
        up = n_.z * 0.5 + 0.5
        tone = C["stone_d"] if up < 0.40 else C["stone_m"] if up < 0.78 else C["stone_l"]
        return shade(tone, 0.72 + 0.28 * up + 0.08 * sat(p.z / max(1.0, hmax)))
    return rule


def scatter(isl, coll, templates, counts):
    rng = random.Random(isl.seed * 31)
    R = isl.R
    trop = isl.kind == "tropical"
    step = 4.6 if trop else 4.8
    floor = 1.2 if trop else 1.9
    rocks = Build()
    srule = stone_rule_for(isl.relief)
    ncliff = nbold = nsker = 0
    cap_cliff = int(R * 0.55)
    cap_bold = int(R * 1.0)
    cap_sker = 3 + int(R / 12)

    # trees
    x = isl.cx - 1.3 * R
    while x < isl.cx + 1.3 * R:
        y = isl.cy - 1.3 * R
        while y < isl.cy + 1.3 * R:
            px = x + (rng.random() - 0.5) * step * 0.9
            py = y + (rng.random() - 0.5) * step * 0.9
            h, rock = isl.field(px, py)
            if h > floor and rock < 1.0:
                gx, gy = isl.grad(px, py)
                slope = hypot(gx, gy)
                if slope < 0.9:
                    clump = nz(px / 38.0, py / 38.0, isl.seed + 9, octs=2) * 0.5 + 0.5
                    p = isl.verdancy * (1.0 - slope / 0.9) ** 0.5 * (0.55 + 0.45 * clump)
                    if rng.random() < p:
                        if trop:
                            key = "palm" if (h < 4.0 or rng.random() < 0.6) else "broad"
                        else:
                            key = "broad" if (h < 10.0 and rng.random() < 0.28) else "spruce"
                        src = templates[key]
                        ob = linked(src, "I2_%s_%s_%d" % (isl.name, key, counts[key]), coll,
                                    (px, py, h - 0.15), yaw=WIND + (rng.random() - 0.5) * 0.7,
                                    s=0.80 + 0.35 * rng.random())
                        counts[key] += 1
            y += step
        x += step

    # cliffs and boulders, on a finer walk
    step2 = 4.0
    x = isl.cx - 1.45 * R
    while x < isl.cx + 1.45 * R:
        y = isl.cy - 1.45 * R
        while y < isl.cy + 1.45 * R:
            px = x + (rng.random() - 0.5) * step2
            py = y + (rng.random() - 0.5) * step2
            h, rock = isl.field(px, py)
            gx, gy = isl.grad(px, py)
            slope = hypot(gx, gy)
            if h > 0.3:
                if (rock > 2.0 and slope > 0.5 and ncliff < cap_cliff and rng.random() < 0.40) or \
                        (slope > 1.15 and h > 4 and ncliff < cap_cliff and rng.random() < 0.25):
                    hz = max(2.5, min(10.0, rock * 0.8 + slope * 3.0))
                    down = atan2(-gy, -gx)
                    bm = shard(2.0 + 2.6 * rng.random(), 1.4 + 1.2 * rng.random(), hz, 12,
                               isl.seed * 3 + ncliff,
                               tilt=(0.12 * sin(down), -0.12 * cos(down), rng.random() * pi),
                               origin=(px, py, h - hz * 0.30))
                    rocks.add_bmesh(bm, srule); bm.free()
                    ncliff += 1
                elif 0.34 < slope < 0.7 and rock < 0.4 and h > 1.5 and nbold < cap_bold and rng.random() < 0.16:
                    r = 0.7 + 1.5 * rng.random()
                    bm = shard(r, r * (0.7 + 0.5 * rng.random()), r * 0.7, 12, isl.seed * 5 + nbold,
                               tilt=(rng.random() * 0.4, rng.random() * 0.4, rng.random() * pi),
                               origin=(px, py, h + r * 0.15))
                    rocks.add_bmesh(bm, srule); bm.free()
                    nbold += 1
                elif 0.4 < h < 1.8 and nbold < cap_bold and rng.random() < 0.012:
                    r = 0.9 + 1.4 * rng.random()
                    bm = shard(r, r * 0.8, r * 0.6, 12, isl.seed * 5 + nbold,
                               tilt=(0, 0, rng.random() * pi), origin=(px, py, h + r * 0.1))
                    rocks.add_bmesh(bm, srule); bm.free()
                    nbold += 1
            elif -3.5 < h < 0.3 and nsker < cap_sker and rng.random() < 0.025:
                # skerries on the shelf; taller off a rocky coast
                nearest_h, nearest_rock = isl.field(isl.cx + (px - isl.cx) * 0.82, isl.cy + (py - isl.cy) * 0.82)
                hz = 1.5 + 2.5 * rng.random() + (3.5 if nearest_rock > 1.0 else 0.0)
                bm = shard(1.2 + 1.6 * rng.random(), 1.0 + 1.2 * rng.random(), hz, 12, isl.seed * 7 + nsker,
                           tilt=(rng.random() * 0.2, rng.random() * 0.2, rng.random() * pi),
                           origin=(px, py, -0.6))
                rocks.add_bmesh(bm, srule); bm.free()
                nsker += 1
            y += step2
        x += step2
    if rocks.f:
        rocks.emit("I2_%s_Rock" % isl.name, coll)
    counts["cliff"] += ncliff; counts["boulder"] += nbold; counts["skerry"] += nsker


# ------------------------------------------------------------------ build ---

ISLANDS = [
    Island("Crag",     0.0, 600.0, 85.0, 30.0, 0.90, 0.90, "temperate", 3,  shelf=1.6),
    Island("Sandbank", 250.0, 560.0, 30.0, 4.5, 0.00, 0.95, "tropical", 11, shelf=1.1),
    Island("Skerry",  -200.0, 520.0, 22.0, 12.0, 1.00, 0.30, "temperate", 17, shelf=1.4),
]


def clear_islands():
    c = bpy.data.collections.get(ICOLL)
    if c:
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0 and m.name.startswith("I2_"):
            bpy.data.meshes.remove(m)


def build_islands():
    clear_islands()
    coll = get_coll(ICOLL)
    templates = dict(
        spruce=build_spruce("I2_T_Spruce", coll, h=13.0, seed=3),
        broad=build_broadleaf("I2_T_Broad", coll, h=10.5, seed=5),
        palm=build_palm("I2_T_Palm", coll, h=9.0, seed=9),
    )
    for i, t in enumerate(templates.values()):
        t.location = (-320 + i * 12, 420, 0)          # parked in a corner
    counts = dict(spruce=0, broad=0, palm=0, cliff=0, boulder=0, skerry=0)
    for isl in ISLANDS:
        step = 2.5 if isl.R > 60 else 1.5
        build_ground(isl, coll, step)
        scatter(isl, coll, templates, counts)
    build_sea(ISLANDS, coll, -900, 1000, 150, 1500, step=9.0)
    print("ISLANDS", counts)
    return counts


def shoot_islands():
    A, Bk, Sk = ISLANDS
    only = (ICOLL,)
    shoot(OUT + "/isl_crag_air.png", (A.cx + 140, A.cy - 190, 120), (A.cx, A.cy + 10, 10), lens=40, w=1800, h=1100, only=only)
    shoot(OUT + "/isl_crag_deck.png", (A.cx - 250, A.cy - 150, 3.0), (A.cx, A.cy, 22), lens=42, w=1800, h=900, only=only)
    shoot(OUT + "/isl_all.png", (60, 250, 260), (10, 590, 0), lens=32, w=1800, h=1000, only=only)
    shoot(OUT + "/isl_sandbank_air.png", (Bk.cx + 75, Bk.cy - 95, 62), (Bk.cx, Bk.cy, 2), lens=45, w=1600, h=1000, only=only)
    shoot(OUT + "/isl_skerry_deck.png", (Sk.cx - 90, Sk.cy - 70, 3.0), (Sk.cx, Sk.cy, 8), lens=42, w=1600, h=900, only=only)


build_islands()
shoot_islands()
