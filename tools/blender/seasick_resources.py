# SeaSick -- island resources: what the crew land to take, and what they leave
# behind them.
#
# Re-runnable:
#   exec(open('/Users/kevinandersson/blender_objects/seasick_resources.py').read())
#
# Convention, the same one the ships use: up +Z, metres, and the object's
# origin at the point it stands on the GROUND (z = 0), because a resource is
# planted on terrain rather than floated at a waterline.
#
# Every asset is authored in TWO states -- standing and worked -- because a
# node the crew have finished with has to say so from the deck of a ship, and
# `ResourceNode.Harvest` currently just switches the whole object off.
#
# Scale charter: WorldScale.Person = 1.7, trees 9-14 m, boulders 1.0-3.2 m.
# Nothing here types a size that is not measured against one of those.

import bpy, bmesh, math, os, io, types
from math import sin, cos, pi

HULLS_PY = "/Users/kevinandersson/Desktop/SeaSick/tools/blender/seasick_hulls.py"

PERSON = 1.7          # WorldScale.Person
TREE_MIN, TREE_MAX = 9.0, 14.0        # WorldScale.TreeMin / TreeMax
BOULDER_MIN, BOULDER_MAX = 1.0, 3.2   # WorldScale.BoulderMin / BoulderMax


# ------------------------------------------------------------- the ships ----

def load_hulls():
    """The ship generator, imported WITHOUT building a fleet.

    `seasick_hulls.py` ends `if __name__ == "__main__" or True:`, so merely
    exec'ing it lofts all twenty rungs -- into this file, where they do not
    belong. Swapping the condition for `if False:` (rather than truncating the
    source) keeps `audit`, `report_topology` and the palette, which all live
    BELOW that guard, and keeps the line numbers intact so a traceback still
    points at the real file.

    This is imported and not copied because the palette is the whole reason:
    ships and islands share one atlas and one material, so there is exactly
    one table of colours in the project and it cannot drift."""
    src = io.open(HULLS_PY, encoding="utf-8").read()
    guard = 'if __name__ == "__main__" or True:'
    if guard not in src:
        raise RuntimeError("the build guard in seasick_hulls.py has moved -- "
                           "importing it would rebuild the whole fleet in here")
    mod = types.ModuleType("seasick_hulls_readonly")
    mod.__file__ = HULLS_PY
    exec(compile(src.replace(guard, 'if False:'), HULLS_PY, "exec"), mod.__dict__)
    return mod


H = load_hulls()
SW = H.SW
CONIFER, BROADLEAF = H.CONIFER, H.BROADLEAF
BARKS, STONES = H.BARKS, H.STONES


def lerp(a, b, t):
    return a + (b - a) * t


def rnd(*keys):
    """Deterministic 0..1 from a set of integers. Same seed, same tree, every
    bake -- a canopy that reshuffles when you rebuild is a canopy nobody can
    approve."""
    return (H._shuffle(*[int(k * 977) & 0xFFFF for k in keys]) % 100003) / 100003.0


def jit(amount, *keys):
    return 1.0 + amount * (rnd(*keys) * 2.0 - 1.0)


# ---------------------------------------------------------------- builder ---

class Mesh:
    """Verts and faces with a SWATCH per face, resolved at emit time.

    The swatch is decided as the face is built, because here -- unlike the
    hulls' loft -- the builder knows what each face IS: a branch is a branch.
    Anything that varies WITHIN a part (a canopy darkening downward, bark
    weathering upward) is a callable and reads the face's own geometry, so it
    is still the geometry that decides, never a face index.

    Faces are matched back by vertex SET after `make_object`, never by index:
    the recalc-normals round trip through bmesh is free to reorder them, and
    that has already cost this project a day."""

    def __init__(self):
        self.v = []
        self.f = []

    def vert(self, p):
        self.v.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.v) - 1

    def ring(self, pts):
        return [self.vert(p) for p in pts]

    def face(self, idx, sw):
        if len(set(idx)) != len(idx):
            raise ValueError("face repeats a vertex: %r" % (idx,))
        self.f.append((list(idx), sw))

    def emit(self, name, coll):
        table = {}
        for idx, sw in self.f:
            k = frozenset(idx)
            if k in table:
                # Two faces on the same vertices IS the defect Kevin's rule
                # forbids. Catch it here, where the traceback says which
                # builder did it, rather than in the gate at the end.
                raise ValueError("%s: duplicate face on verts %r" % (name, sorted(k)))
            table[k] = sw
        ob = H.make_object(name, self.v, [i for i, _ in self.f], coll,
                           smooth=False, mirror=False, recalc=True)
        me = ob.data
        uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
        for pg in me.polygons:
            sw = table.get(frozenset(pg.vertices))
            if sw is None:
                raise RuntimeError("%s: a face survived validate() that this "
                                   "builder never made" % name)
            if callable(sw):
                sw = sw(pg.center, pg.normal)
            u, v = H.swatch_uv(sw)
            for li in pg.loop_indices:
                uv.data[li].uv = (u, v)
        ob.data.materials.append(H.wood_material())
        return ob


# ------------------------------------------------------------- primitives ---
# All of them CLOSED and all of them flat: a cap is fanned into triangles from
# a centre vertex rather than left as an n-gon, because `report_topology`
# fails on n-gons and because a resource prop is seen from every side -- there
# is no waterline below which nobody looks.

def tube(mb, path, radii, n, sw, uh=(1, 0, 0), vh=(0, 1, 0),
         cap_a=None, cap_b=None, phase=0.0, wobble=0.0, seed=0):
    """A closed tube swept along `path`, each ring drawn in the plane spanned
    by `uh`/`vh`. The frame is passed in rather than derived from the tangent:
    a trunk's rings are horizontal even where it leans, which is how a tree
    actually grows, and a felled log lying on its side just gets a different
    pair of axes. Deriving it would need parallel transport to avoid twisting,
    for no gain on anything this shape."""
    if len(path) != len(radii) or len(path) < 2:
        raise ValueError("tube: path and radii must pair up, twice over")
    rings = []
    for k, (c, r) in enumerate(zip(path, radii)):
        if r < 6e-3:
            raise ValueError("tube: radius %.4f collapses the ring" % r)
        pts = []
        for i in range(n):
            a = phase + 2.0 * pi * i / n
            rr = r * (jit(wobble, seed, k, i) if wobble else 1.0)
            du, dv = cos(a) * rr, sin(a) * rr
            pts.append((c[0] + uh[0] * du + vh[0] * dv,
                        c[1] + uh[1] * du + vh[1] * dv,
                        c[2] + uh[2] * du + vh[2] * dv))
        rings.append(mb.ring(pts))
    for k in range(len(rings) - 1):
        A, B = rings[k], rings[k + 1]
        for i in range(n):
            j = (i + 1) % n
            mb.face([A[i], A[j], B[j], B[i]], sw)
    if cap_b is not None:
        c = mb.vert(path[0])
        for i in range(n):
            mb.face([rings[0][(i + 1) % n], rings[0][i], c], cap_b)
    if cap_a is not None:
        c = mb.vert(path[-1])
        for i in range(n):
            mb.face([rings[-1][i], rings[-1][(i + 1) % n], c], cap_a)
    return rings


def whorl(mb, c, r_out, r_in, rise, droop, n, sw, seed=0, push=(0.0, 0.0)):
    """One branch layer of a conifer: a shallow cone over a shallower one.

    The rim ALTERNATES between a long branch and a short one and jitters each
    by a sixth, which is the whole trick -- a smooth cone reads as a party hat
    and costs exactly the same. `push` slides the layer downwind without
    moving where it joins the trunk."""
    if n % 2:
        raise ValueError("whorl: an odd rim cannot alternate")
    rim = []
    for i in range(n):
        a = 2.0 * pi * i / n + rnd(seed, i) * 0.4
        out = (i % 2 == 0)
        r = (r_out if out else r_in) * jit(0.17, seed, i, 3)
        dz = -droop if out else -droop * 0.45
        t = r / max(1e-6, r_out)
        rim.append((c[0] + cos(a) * r + push[0] * t,
                    c[1] + sin(a) * r + push[1] * t,
                    c[2] + dz))
    R = mb.ring(rim)
    top = mb.vert((c[0], c[1], c[2] + rise))
    bot = mb.vert((c[0], c[1], c[2] - droop * 0.15))
    for i in range(n):
        j = (i + 1) % n
        mb.face([R[i], R[j], top], sw)
        mb.face([R[j], R[i], bot], sw)
    return R


def blob(mb, c, rx, ry, rz, n, lat, sw, seed=0, rough=0.18, flat_bot=1.0):
    """A faceted spheroid: canopy, boulder, spoil heap, berry bush.

    `rough` jitters every vertex radially, which is the difference between a
    rock and a die. `flat_bot` squashes the lower half, so a boulder sits on
    the ground instead of balancing on it."""
    rings = []
    for k in range(lat):
        t = (k + 1.0) / (lat + 1.0)
        th = t * pi
        rr, zz = sin(th), cos(th)
        sq = 1.0 if zz >= 0.0 else flat_bot
        pts = []
        for i in range(n):
            a = 2.0 * pi * i / n
            f = jit(rough, seed, k, i)
            g = jit(rough * 0.6, seed, k, i, 7)
            pts.append((c[0] + cos(a) * rr * rx * f,
                        c[1] + sin(a) * rr * ry * f,
                        c[2] + zz * rz * sq * g))
        rings.append(mb.ring(pts))
    top = mb.vert((c[0], c[1], c[2] + rz * jit(rough * 0.5, seed, 91)))
    bot = mb.vert((c[0], c[1], c[2] - rz * flat_bot * jit(rough * 0.5, seed, 93)))
    for i in range(n):
        j = (i + 1) % n
        mb.face([rings[0][j], rings[0][i], top], sw)
        mb.face([rings[-1][i], rings[-1][j], bot], sw)
    for k in range(lat - 1):
        A, B = rings[k], rings[k + 1]
        for i in range(n):
            j = (i + 1) % n
            mb.face([A[i], A[j], B[j], B[i]], sw)
    return rings


def slab(mb, c, hx, hy, hz, yaw, pitch, sw):
    """A box, yawed and pitched. Coal is the reason this exists: it cleaves in
    flat parallel beds, and a lumpy black `blob` reads as a burnt boulder."""
    cy, sy = cos(yaw), sin(yaw)
    cp, sp = cos(pitch), sin(pitch)
    pts = []
    for sx, sy_, sz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                        (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)):
        x, y, z = sx * hx, sy_ * hy, sz * hz
        x, z = x * cp - z * sp, x * sp + z * cp          # pitch about Y
        x, y = x * cy - y * sy, x * sy + y * cy          # yaw about Z
        pts.append((c[0] + x, c[1] + y, c[2] + z))
    V = mb.ring(pts)
    for q in ([0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4],
              [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]):
        mb.face([V[i] for i in q], sw)
    return V


def octa(mb, c, r, sw, squash=0.85):
    """Six verts, eight faces -- a berry, and the smallest thing in the
    project that still reads as round."""
    V = mb.ring([(c[0] + r, c[1], c[2]), (c[0], c[1] + r, c[2]),
                 (c[0] - r, c[1], c[2]), (c[0], c[1] - r, c[2]),
                 (c[0], c[1], c[2] + r * squash), (c[0], c[1], c[2] - r * squash)])
    for a, b in ((0, 1), (1, 2), (2, 3), (3, 0)):
        mb.face([V[a], V[b], V[4]], sw)
        mb.face([V[b], V[a], V[5]], sw)
    return V


# ---------------------------------------------------------------- shading ---
# Every gradient here runs the way the light does: dark underneath, lit on
# top. That is the hulls' rule about strakes ("the tone is a gradient, not a
# coin toss per face") applied to the only thing on an island with a top and a
# bottom, and it is why three greens read as a tree rather than as noise.

def graded(tones, z0, z1, up_bias=0.30, break_one_in=0, alt=None, seed=0):
    def pick(c, nrm):
        t = (c[2] - z0) / max(1e-6, z1 - z0)
        t = t * (1.0 - up_bias) + (nrm[2] * 0.5 + 0.5) * up_bias
        k = min(len(tones) - 1, max(0, int(max(0.0, min(0.999, t)) * len(tones))))
        if break_one_in and alt is not None \
                and H._shuffle(int(c[0] * 37), int(c[1] * 37), int(c[2] * 37),
                               seed) % break_one_in == 0:
            return alt
        return tones[k]
    return pick


def bark_of(z0, z1, seed=0):
    """Damp and dark at the foot, weathered pale where the weather gets at it.
    One facet in four steps one tone off, and only ever ONE -- the same rule
    that stops a hull's planking reading as a checkerboard."""
    def pick(c, nrm):
        t = (c[2] - z0) / max(1e-6, z1 - z0)
        k = min(2, max(0, int(max(0.0, min(0.999, t)) * 3)))
        if H._shuffle(int(c[2] * 21), int(c[0] * 21), seed) % 4 == 0:
            k = min(2, max(0, k + (1 if H._shuffle(int(c[2] * 13), seed) % 2 else -1)))
        return BARKS[k]
    return pick


# ------------------------------------------------------------------ trees ---
# The most common thing on an island, so the one that cannot repeat. Variation
# is STRUCTURAL first -- four silhouettes -- and only then a height and a lean,
# because thirteen copies of one outline at thirteen heights is still thirteen
# copies. Every crown piece is placed on `axis_at`, the trunk's ACTUAL centre
# at that height, never on x = 0: that is the hulls' first joinery rule, and it
# is the whole reason a leaning tree keeps its crown on its trunk.

TREES = [
    # conifer -- a spire of branch whorls, leader running to the top
# **The band 9-14 is DIVIDED by species, not shared.** The first cut drew
# every kind from the whole range, so the tallest tree in the set was a
# broadleaf at 13.8 m and the pines ran from 9.3 to 14.0 -- which is not what
# a coast looks like. A conifer outgrows a broadleaf on this sort of ground
# and it is the height DIFFERENCE between the two that reads, not either one
# on its own. So: pines take the top of the band, broadleaves the bottom, and
# the scrub the floor. Nothing leaves WorldScale's 9-14.
#
# `crown` is the widest whorl's RADIUS as a fraction of height, and it works
# the OPPOSITE way round -- narrow on the tall conifers, wide on the short
# broadleaves. Height and width pulling in opposite directions is what makes
# the two species read as different plants rather than one plant at two sizes.
    # conifer -- 11.8 to 14.0, the tallest thing growing on an island
    dict(name="Pine_A",  kind="conifer", h=14.0, whorls=8, base=0.20, crown=0.185, lean=0.35, seed=11),
    dict(name="Pine_B",  kind="conifer", h=12.4, whorls=7, base=0.24, crown=0.205, lean=0.20, seed=23),
    dict(name="Pine_C",  kind="conifer", h=11.8, whorls=6, base=0.18, crown=0.225, lean=0.55, seed=37),
    dict(name="Pine_D",  kind="conifer", h=13.6, whorls=8, base=0.26, crown=0.175, lean=0.10, seed=41),
    dict(name="Pine_E",  kind="conifer", h=12.9, whorls=7, base=0.16, crown=0.215, lean=0.70, seed=59),
    # broadleaf -- 9.8 to 11.6, a short bole under a wide round crown
    dict(name="Broad_A", kind="broad",   h=11.2, blobs=3, base=0.40, crown=0.300, lean=0.45, seed=67),
    dict(name="Broad_B", kind="broad",   h=11.6, blobs=3, base=0.46, crown=0.285, lean=0.25, seed=71),
    dict(name="Broad_C", kind="broad",   h=9.8,  blobs=2, base=0.38, crown=0.320, lean=0.60, seed=83),
    dict(name="Broad_D", kind="broad",   h=10.5, blobs=4, base=0.43, crown=0.265, lean=0.35, seed=97),
    # wind-bent -- 9.0 to 10.0: the coast scrub, lowest of the three
    dict(name="Bent_A",  kind="bent",    h=9.0,  bend=1.90, base=0.44, crown=0.255, seed=101),
    dict(name="Bent_B",  kind="bent",    h=10.0, bend=2.60, base=0.40, crown=0.230, seed=103),
    dict(name="Bent_C",  kind="bent",    h=9.4,  bend=1.40, base=0.48, crown=0.270, seed=107),
    # dead -- no canopy at all. Two or three of these in a wood is what stops
    # the wood reading as a plantation, and it costs no foliage whatsoever.
    # It is a dead PINE, so it is cut from a pine's height: a snag that is
    # shorter than every living tree round it reads as a fence post.
    dict(name="Snag_A",  kind="snag",    h=12.6, seed=109),
]

# The wind blows one way on an island, so every tree leans the SAME way: +X is
# downwind. Yaw them at random in Unity and the agreement is destroyed and the
# leans read as thirteen unrelated accidents -- so the placement rule that
# ships with these assets is: yaw to the island's wind, then jitter +/- 20 deg.
DOWNWIND = (1.0, 0.0)


def trunk_radius(p):
    """One section, and everything is cut from it. The hulls' `scantling`, for
    a tree: a bole is a fixed fraction of its own height, so a big tree is not
    a small tree scaled up in one axis."""
    # A snag is the STUMP of a big tree, not a small tree: it broke because
    # it was old, and what is left standing is the thick part.
    k = {"conifer": 0.0300, "broad": 0.0360, "bent": 0.0320, "snag": 0.0420}
    return p["h"] * k[p["kind"]]


def axis_of(p):
    """Where the trunk's centre actually is at height z. Every crown piece is
    hung off this."""
    h, kind = p["h"], p["kind"]
    if kind == "bent":
        b = p["bend"]
        return lambda z: (DOWNWIND[0] * b * (max(0.0, z) / h) ** 1.9,
                          DOWNWIND[1] * b * (max(0.0, z) / h) ** 1.9)
    lean = p.get("lean", 0.0)
    return lambda z: (DOWNWIND[0] * lean * (max(0.0, z) / h) ** 1.7,
                      DOWNWIND[1] * lean * (max(0.0, z) / h) ** 1.7)


def build_tree(p, coll):
    mb = Mesh()
    h, seed, kind = p["h"], p["seed"], p["kind"]
    r0 = trunk_radius(p)
    ax = axis_of(p)
    bark = bark_of(0.0, h * 0.75, seed)

    # --- the bole ----------------------------------------------------------
    # A conifer's leader runs to the very top; a broadleaf's bole stops inside
    # its own crown, which is why one is a spire and the other is a ball.
    top = h * (0.97 if kind == "conifer" else
               0.86 if kind == "snag" else p["base"] + 0.16)
    nst = 6
    path, radii = [], []
    for k in range(nst + 1):
        t = k / nst
        z = top * t
        x, y = ax(z)
        path.append((x, y, z))
        # Taper is fast at the foot (the root flare) and slow above it.
        # Floored in METRES as well as in proportion: a fraction alone
        # gives the smallest tree a 1.5 cm leader, which is a wire, not a
        # tree. 4.5 cm of radius is a 9 cm leader, which is what one is.
        # A snag is a bole SNAPPED OFF part way up, so it is still stout
        # where it ends -- tapered like a living tree it came out a twig.
        shape = (1.0 - t * 0.45) ** 0.5 if kind == "snag" else (1.0 - t) ** 0.62
        radii.append(max(0.045, r0 * shape) * jit(0.06, seed, k, 5))
    # A snag is a BROKEN tree, so its top is a jagged stump, not a point.
    cap = SW["sapwood"] if kind == "snag" else BARKS[2]
    tube(mb, path, radii, 6, bark, cap_a=cap, cap_b=BARKS[0],
         wobble=0.09, seed=seed)

    # Root flare: three wedges where the bole meets the ground. Without them a
    # trunk is a bollard -- it is the cheapest thing on the tree that says the
    # tree grew there rather than being pushed in.
    for i in range(3):
        a = 2.0 * pi * i / 3.0 + rnd(seed, i, 2) * 1.4
        rr = r0 * (1.35 + 0.5 * rnd(seed, i, 4))
        blob(mb, (cos(a) * rr * 0.55, sin(a) * rr * 0.55, r0 * 0.30),
             rr * 0.62, rr * 0.62, r0 * 0.62, 5, 1, BARKS[0],
             seed=seed * 3 + i, rough=0.26, flat_bot=0.45)

    # --- the crown ---------------------------------------------------------
    if kind == "conifer":
        # Measured off the built mesh: the three greens land 50 / 59 / 31 on
        # Pine_A, which is a healthy spread -- so the bleached look in the
        # first render was Workbench's studio light on upward faces, not the
        # palette. (Chasing that as a colour bug is exactly the blue-sail
        # mistake from the ship import.) What IS wrong is WHERE the light tone
        # goes: driven mostly by height it collected at the top of the tree,
        # so a pine graded from dark at the foot to pale at the tip. Driven
        # mostly by the face's NORMAL it goes on the top of every whorl and
        # every underside goes dark, which is what makes a stack of cones read
        # as layers instead of as one cone.
        n = p["whorls"]
        # Four steps with the mid counted twice, the same weighting the bush
        # uses: at up_bias 0.70 every upward face took the lightest green and
        # half the tree went pale; at 0.34 the light collected at the top and
        # the tree graded dark-to-pale up its length. Weighted and at 0.50 it
        # lands where it belongs -- the sunward top of each whorl, and not
        # much of that.
        tones = [CONIFER[0], CONIFER[1], CONIFER[1], CONIFER[2]]
        z0 = h * p["base"]
        # **The top of the tree is the top WHORL'S APEX, not the height its
        # centre was placed at.** Giving the layers enough rise to close the
        # gaps between them promptly pushed two pines to 15.45 m against an
        # authored 14.0 and a charter that stops there -- the height was being
        # set on a point that is not the highest point on the tree. So the
        # last whorl is placed by where its tip LANDS, which is the hulls'
        # first joinery rule, and `h` means her finished height again.
        # Solved, not iterated once. The first cut took a provisional gap,
        # set z1 from it, then RECOMPUTED the gap -- which is smaller, so the
        # top whorl's rise shrank and the tree finished 26 cm under its
        # authored height. gap = (z1-z0)/(n-1) and z1 = h - 1.15*gap are two
        # equations in two unknowns; solving them costs one line.
        k = 1.15 / max(1.0, n - 1.0)
        z1 = (h + z0 * k) / (1.0 + k)
        gap = (z1 - z0) / max(1, n - 1.0)
        for i in range(n):
            t = i / max(1, n - 1.0)
            z = z0 + (z1 - z0) * t
            r = p["crown"] * h * (1.0 - t * 0.93) ** 0.85 * jit(0.10, seed, i, 8)
            x, y = ax(z)
            # Each layer has to REACH the one below it or the bole shows
            # through in stripes: the rise is measured against the gap it has
            # to cover, not only against its own radius.
            whorl(mb, (x, y, z), r, r * 0.66,
                  max(gap * 1.15, r * 0.80), r * 0.30, 10,
                  graded(tones, z0, z1, up_bias=0.50), seed=seed * 7 + i)

    elif kind == "broad":
        z0 = h * p["base"]
        n = p["blobs"]
        R = p["crown"] * h
        # Third time this exact bug, so it is worth stating as a rule: **a
        # tree's height is the top of its CROWN, and the crown is not placed
        # where the number that generated it says.** With z1 = h the topmost
        # mass sat AT h and then added its own radius on top, so a broadleaf
        # authored at 11.6 m finished at 12.8 -- taller than a pine authored
        # at 12.4, which is the one thing this pass was about. Solved from the
        # top mass's actual jittered radius, `h` means her finished height.
        r_top = R * (0.62 + 0.42 * (1.0 - abs(1.0 - 0.35))) * jit(0.12, seed, n - 1, 9)
        z1 = z0 + (h - r_top * 0.74 - z0) / 0.92
        for i in range(n):
            t = i / max(1, n - 1.0) if n > 1 else 0.0
            z = z0 + (z1 - z0) * (0.30 + 0.62 * t)
            r = (r_top if i == n - 1 else
                 R * (0.62 + 0.42 * (1.0 - abs(t - 0.35))) * jit(0.12, seed, i, 9))
            x, y = ax(z)
            # Slide each mass off the axis, or a crown is a stack of
            # concentric balls and reads as one ball.
            a = rnd(seed, i, 12) * 2.0 * pi
            off = R * 0.42 * rnd(seed, i, 13)
            blob(mb, (x + cos(a) * off, y + sin(a) * off, z),
                 r, r * jit(0.14, seed, i, 14), r * 0.74, 7, 3,
                 graded([BROADLEAF[0], BROADLEAF[1], BROADLEAF[1], BROADLEAF[2]],
                        z0 - R * 0.4, z1, up_bias=0.42),
                 seed=seed * 11 + i, rough=0.20)

    elif kind == "bent":
        # Built from FLATTENED masses swept downwind, not from conifer
        # whorls. The first cut used `whorl` here and every one came out a
        # blown-out parasol: a whorl is a ring of branches round a trunk,
        # which is the one shape a wind-flagged tree does not have -- the
        # windward side is bare, and that asymmetry IS the tree.
        z0 = h * p["base"]
        R = p["crown"] * h
        # Same rule as the conifer's top whorl, and it bit again the moment
        # the masses were moved down to overlap: the tree's top is the top of
        # the highest CROWN MASS, so that is what gets placed at `h`. Set
        # z1 = h and Bent_A finished at 8.85 against an authored 9.0.
        # Solved from the top mass's ACTUAL radius, jitter included. Using
        # the nominal one left the finish wherever that mass's dice fell --
        # Bent_A came out 8.89 against an authored 9.0, and a charter gate
        # that cries wolf over a rounding error is worse than no gate.
        rr_top = R * 0.80 * jit(0.10, seed, 1, 15)
        z1 = z0 + (h - rr_top * 0.76 - z0) / 0.78
        push = (DOWNWIND[0] * R * 0.85, DOWNWIND[1] * R * 0.85)
        # bare limbs, on the side the wind comes from
        for i in range(2):
            z = z0 + (z1 - z0) * (0.10 + 0.34 * i)
            x, y = ax(z)
            L = R * (0.62 - 0.14 * i)
            tube(mb, [(x, y, z),
                      (x - DOWNWIND[0] * L, y + (i - 0.5) * L * 0.7, z + L * 0.30)],
                 [max(0.05, r0 * 0.34), max(0.03, r0 * 0.16)], 5, BARKS[0],
                 cap_a=BARKS[0])
        # TWO masses, nearly round, heavily overlapped. Three flattened ones
        # spread along the wind read as lily pads threaded on a stick: at this
        # polygon count a mass squashed below about 0.7 of its own radius
        # stops being a volume and becomes a disc, and three discs in a row do
        # not add up to a crown. What makes this tree read as wind-shaped is
        # not flatness -- it is that the whole crown sits to one side of the
        # trunk with bare limbs on the other, which is what a flagged tree is.
        for i in range(2):
            t = float(i)
            z = z0 + (z1 - z0) * (0.34 + 0.44 * t)
            x, y = ax(z)
            rr = rr_top if i == 1 else R * 0.95 * jit(0.10, seed, i, 15)
            side = (rnd(seed, i, 16) - 0.5) * R * 0.28
            blob(mb, (x + push[0] * (0.46 + 0.30 * t),
                      y + push[1] * (0.46 + 0.30 * t) + side, z),
                 rr, rr * 0.80, rr * 0.76, 7, 3,
                 graded([BROADLEAF[0], BROADLEAF[1], BROADLEAF[1], BROADLEAF[2]],
                        z0, z1, up_bias=0.45,
                        break_one_in=7, alt=SW["scrub"], seed=seed + i),
                 seed=seed * 13 + i, rough=0.26)

    else:  # snag -- bare limbs, silvered. Weathered dead wood is grey, so it
           # borrows the stone tones rather than earning a swatch of its own.
        dead = graded([SW["bark_d"], SW["stone_d"], SW["stone_l"]], 0.0, top,
                      up_bias=0.45)
        # Broken limbs are what makes a snag read as a TREE and not a post,
        # so they are heavy and long: a third of the bole's radius and a fifth
        # of its height, kinked once on the way out.
        for i in range(4):
            z = top * (0.34 + 0.17 * i)
            x, y = ax(z)
            a = rnd(seed, i, 21) * 2.0 * pi
            L = h * (0.20 + 0.13 * rnd(seed, i, 22))
            rr = max(0.075, r0 * (0.46 - 0.06 * i))
            mid = (x + cos(a) * L * 0.55, y + sin(a) * L * 0.55, z + L * 0.30)
            end = (x + cos(a) * L, y + sin(a) * L, z + L * 0.62)
            tube(mb, [(x, y, z), mid, end], [rr, rr * 0.62, rr * 0.34], 5, dead,
                 cap_a=SW["stone_l"])
        # repaint the bole itself: a snag has no bark left worth the name
        for i, (idx, sw) in enumerate(mb.f):
            if sw is bark:
                mb.f[i] = (idx, dead)

    return mb.emit("R_Tree_" + p["name"], coll)


# --- what an axe leaves ------------------------------------------------------
# `SceneryWood.Fell` currently collapses a tree's vertices onto its own base,
# so a felled tree leaves NOTHING. A stump and a log are the difference
# between an island the crew have worked and an island that has quietly lost
# some trees, and the sapwood swatch is the whole tell: it is the one colour
# on the island that only ever appears where a blade has been.

STUMPS = [dict(name="A", r=0.42, h=0.82, seed=201),   # conifer bole
          dict(name="B", r=0.50, h=0.66, seed=203),   # broadleaf, cut lower
          dict(name="C", r=0.33, h=0.95, seed=207)]   # the small coast tree


def build_stump(p, coll):
    mb = Mesh()
    r, h, seed = p["r"], p["h"], p["seed"]
    bark = bark_of(0.0, h * 1.6, seed)
    # The cut is not level: a felled tree hinges, and the hinge leaves the top
    # sloping. Built as a ring whose z varies rather than as a flat disc.
    n = 7
    ring_z = [h * (1.0 + 0.10 * cos(2.0 * pi * i / n)) for i in range(n)]
    # The sloped top is welded onto the ring the tube already made. Drawing
    # a fresh ring at the same radius and height instead puts seven vertices
    # exactly on top of seven others -- which is precisely the defect Kevin's
    # rule is about, and it is invisible in the viewport.
    rings = tube(mb, [(0, 0, 0), (0, 0, h * 0.55)],
                 [r * 1.16, r * 1.02], n, bark, cap_b=BARKS[0])
    L = rings[-1]
    Hh = mb.ring([(cos(2 * pi * i / n) * r, sin(2 * pi * i / n) * r, ring_z[i])
                  for i in range(n)])
    for i in range(n):
        j = (i + 1) % n
        mb.face([L[i], L[j], Hh[j], Hh[i]], bark)
    c = mb.vert((0, 0, sum(ring_z) / n))
    for i in range(n):
        mb.face([Hh[i], Hh[(i + 1) % n], c], SW["sapwood"])
    for i in range(3):
        a = 2.0 * pi * i / 3.0 + rnd(seed, i) * 1.5
        rr = r * (1.4 + 0.45 * rnd(seed, i, 4))
        # Taller and a tone lighter than the first cut, which sat so low and
        # so dark that three flares read as three puddles of shadow round the
        # foot rather than as the tree's own roots.
        blob(mb, (cos(a) * rr * 0.52, sin(a) * rr * 0.52, r * 0.42),
             rr * 0.62, rr * 0.62, r * 0.85, 5, 1, BARKS[1],
             seed=seed + i, rough=0.28, flat_bot=0.55)
    return mb.emit("R_TreeStump_" + p["name"], coll)


def build_log(name, length, r, coll, seed=211, stubs=3):
    """The yield: a trimmed log lying on its side, both ends showing sapwood.

    Sized against the crew, not against the tree it came from -- a 13 m bole
    is not a thing a landing party carries, and the hulls' rule about anything
    a hand touches applies to what a hand LIFTS as well."""
    mb = Mesh()
    bark = bark_of(-r, r, seed)          # graded across the log, so the top lifts
    n = 7
    st = 4
    path = [(-length / 2.0 + length * k / st, 0.0,
             r * 1.02 + r * 0.05 * sin(pi * k / st)) for k in range(st + 1)]
    radii = [r * (1.0 - 0.16 * (k / st)) * jit(0.05, seed, k) for k in range(st + 1)]
    tube(mb, path, radii, n, bark, uh=(0, 1, 0), vh=(0, 0, 1),
         cap_a=SW["sapwood"], cap_b=SW["sapwood"], wobble=0.07, seed=seed)
    for i in range(stubs):
        t = 0.24 + 0.5 * (i / max(1, stubs - 1.0))
        x = -length / 2.0 + length * t
        a = rnd(seed, i, 31) * 2.0 * pi
        rr = r * 0.42
        # A trimmed branch is cut off close and sticks OUT, not up. Aimed
        # skyward the first time, three of them read as white candles stuck
        # along the log -- and the pale cut is meant to be the tell that it
        # was trimmed, not the whole silhouette.
        out = r * 1.35
        end = (x, cos(a) * out, r * 1.02 + sin(a) * out * 0.55)
        tube(mb, [(x, 0, r * 1.02), end], [rr, rr * 0.68], 5, BARKS[0],
             uh=(1, 0, 0), vh=(0, -sin(a) * 0.55, cos(a)),
             cap_a=SW["sapwood"])
    return mb.emit(name, coll)


# ------------------------------------------------------------------- iron ---
# An outcrop with the ore showing in it, at boulder scale (WorldScale says
# 1.0-3.2 m) so it reads as part of the same world as the rocks around it.
# The ore band is PAINTED, not modelled: it is chosen from the face's height
# and broken up by its angle, so it costs nothing and cannot be a seam of
# floating geometry.

def host_rock(span, nodules=(), worked=False, seed=0):
    """What one face of the outcrop is made of.

    **The ore is geometry; this only stains around it.** The first cut chose
    the ore from a face's own height and angle, and on a rock that is eight
    sides by three rings -- about thirty faces -- the finest thing that rule
    can draw is a whole facet. At one face in nine it still put a hand's
    breadth of flat orange down the side of the boulder: a sticker, not a
    vein. Now the nodules are modelled and the paint agrees with them, which
    is the same bargain the ships' gun-port lids struck -- find the feature
    from the thing that is actually there, not from the number that made it.

    On a worked rock the cut face wins: pale, freshly broken, with the last of
    the ore in it. A worked outcrop that is merely SMALLER than an untouched
    one says nothing at all from ten metres."""
    def pick(c, nrm):
        t = max(0.0, min(0.999, c[2] / max(1e-6, span)))
        if worked and c[0] > 0.05 and nrm[0] > 0.05 and 0.12 < t < 0.88:
            return SW["stone_l"] if H._shuffle(int(c[2] * 31), seed) % 3 \
                else SW["ore_d"]
        for x, y, z, r in nodules:
            if (c[0] - x) ** 2 + (c[1] - y) ** 2 + (c[2] - z) ** 2 < (r * 2.9) ** 2:
                # The BRIGHT rust, not the shaded one. Stained with `ore_d`
                # the halo sat at almost the same value as `stone_d` and the
                # whole outcrop went back to reading as a plain grey boulder
                # with a few dots on it -- which is a resource node that does
                # not say what it is from the beach. Eight scattered haloes
                # say "mineralised" where one solid panel said "sticker".
                return SW["ore"]
        # same lit-on-top grading the coal bank uses, so the two rocks in the
        # same row are lit by the same sun
        u = t * 0.62 + (nrm[2] * 0.5 + 0.5) * 0.38
        return STONES[min(2, max(0, int(max(0.0, min(0.999, u)) * 3)))]
    return pick


def build_iron(coll, worked=False):
    mb = Mesh()
    seed = 301
    hz = 1.28                       # 2.4 m of rock: mid-band for a boulder
    sw = object()                   # a placeholder: the real rule needs the
                                    # nodules, and they need the built surface
    rings = blob(mb, (0, 0, hz * 0.86), 1.52, 1.26, hz, 8, 3, sw,
                 seed=seed, rough=0.30, flat_bot=0.52)
    if worked:
        # The bite. Pull every vertex on the +X side, inside the ore band,
        # in toward the axis -- the ore has been cut out of the face, and what
        # is left is the pale scar of the host rock.
        # Pulled IN, never past its neighbour: mapping x to `x*0.46 - 0.30`
        # sends a vertex at the edge of the band further out than one inside
        # it, which folds the face through itself and shows as nothing at all
        # until something tries to shade it.
        for R in rings:
            for vi in R:
                x, y, z = mb.v[vi]
                t = z / (hz * 2.0)
                if x > 0.15 and 0.18 < t < 0.80:
                    mb.v[vi] = (0.15 + (x - 0.15) * 0.34, y * (0.55 + 0.45 * (1 - min(1.0, x))), z)
    # **The ore has to be GEOMETRY here, not paint.** The host rock is 8
    # sides by 3 rings -- about thirty faces -- so a per-face rule cannot draw
    # anything finer than a facet, and one facet of a boulder is a hand's
    # breadth of flat orange: a sticker, not a vein. (Measured, the painted
    # band plus the loose chunks were 57% of the whole prop's faces.) Nodules
    # half-buried in the face read as ore at any polygon count, cost twelve
    # triangles each, and on the WORKED rock they are simply absent from the
    # side that has been cut -- which is the clearest possible statement that
    # somebody took the ore out of it.
    band = [vi for Rg in rings for vi in Rg
            if 0.20 < mb.v[vi][2] / (hz * 2.0) < 0.78
            and not (worked and mb.v[vi][0] > 0.15)]
    band.sort(key=lambda vi: rnd(seed, vi, 77))
    nodules = []
    for j, vi in enumerate(band[:8]):
        vx, vy, vz = mb.v[vi]
        rr = 0.15 + 0.09 * rnd(seed, j, 78)
        cx, cy = vx * 0.90, vy * 0.90
        blob(mb, (cx, cy, vz), rr, rr * 0.88, rr * 0.80, 6, 1,
             graded([SW["ore_d"], SW["ore"], SW["ore"]], vz - rr, vz + rr,
                    up_bias=0.6),
             seed=seed * 17 + j, rough=0.30)
        nodules.append((cx, cy, vz, rr))
    # Now the host rock can be painted, because now there is something to
    # paint around. Matched on the placeholder OBJECT, so only the host's own
    # faces are repainted and the nodules keep their rust.
    rock_sw = host_rock(hz * 2.0, nodules, worked, seed)
    for i, (idx, sc) in enumerate(mb.f):
        if sc is sw:
            mb.f[i] = (idx, rock_sw)

    # A shoulder, so the outcrop is a broken thing and not an egg.
    blob(mb, (-1.05, 0.42, hz * 0.50), 0.78, 0.66, hz * 0.62, 7, 2,
         host_rock(hz * 1.2, (), False, seed + 1), seed=seed + 1,
         rough=0.34, flat_bot=0.5)

    if worked:
        # Spoil, and the ore that came out, gathered where a crew would drop
        # it: at the foot of the face they cut.
        blob(mb, (1.62, -0.30, 0.16), 0.78, 0.66, 0.24, 7, 2, SW["soil"],
             seed=seed + 5, rough=0.30, flat_bot=0.30)
        # A HEAP, not a scatter: chunks half as big again as the first cut,
        # some of them resting on the others. Spread flat on the ground at
        # 0.14 m they were specks beside a 2.3 m rock and the yield -- the
        # thing the whole prop is about -- was the least visible part of it.
        for i in range(7):
            a = rnd(seed, i, 41) * 2 * pi
            d = 0.62 * rnd(seed, i, 42) ** 0.7
            rr = 0.21 + 0.13 * rnd(seed, i, 43)
            lift = 0.0 if i < 4 else 0.30
            blob(mb, (1.62 + cos(a) * d, -0.30 + sin(a) * d, rr * 0.78 + lift),
                 rr, rr * 0.86, rr * 0.74, 6, 2,
                 # Measured on the first cut: `bloom` came out the single
                 # commonest swatch on the whole worked prop, 58 faces of it,
                 # because it sat at the TOP of the gradient and a heap is
                 # seen from above. Bloom is grey -- so the yield, the one
                 # thing the prop exists to show, read as a pile of pebbles.
                 # It is the exception now, one broken face in five, and the
                 # heap is the rust colour the ore actually is.
                 graded([SW["ore_d"], SW["ore"], SW["ore"]], 0.0, rr * 1.7,
                        up_bias=0.55, break_one_in=5, alt=SW["bloom"],
                        seed=seed + i),
                 seed=seed * 3 + i, rough=0.34, flat_bot=0.55)
    else:
        # Loose ore already weathered out of the face -- the reason a crew
        # would walk to this rock rather than any other.
        for i in range(3):
            a = 1.2 + rnd(seed, i, 51) * 2.6
            d = 1.45 + 0.55 * rnd(seed, i, 52)
            rr = 0.24 + 0.13 * rnd(seed, i, 53)
            blob(mb, (cos(a) * d, sin(a) * d, rr * 0.72), rr, rr * 0.88, rr * 0.7,
                 6, 2, graded([SW["ore_d"], SW["ore"], SW["ore"]], 0.0,
                              rr * 1.5, up_bias=0.5, break_one_in=6,
                              alt=SW["bloom"], seed=seed + i),
                 seed=seed * 7 + i, rough=0.32, flat_bot=0.45)
    return mb.emit("R_Iron_" + ("Worked" if worked else "Outcrop"), coll)


# ------------------------------------------------------------------- coal ---
# Coal is not a black boulder. It cleaves in flat parallel beds, so the seam is
# built from SLABS lying almost level, under a lip of ordinary stone that says
# the black stuff is IN something. That contrast -- blocky black against lumpy
# grey -- is what tells coal from iron across a beach at a glance.

def build_coal(coll, worked=False):
    mb = Mesh()
    seed = 401
    # the bank the seam is cut into
    blob(mb, (-0.35, 0.0, 0.95), 1.55, 1.15, 1.02, 8, 3,
         graded(STONES, 0.0, 2.1, up_bias=0.5), seed=seed, rough=0.28,
         flat_bot=0.42)
    beds = 5
    for i in range(beds):
        z = 0.30 + i * 0.235
        reach = 1.32 - 0.10 * i
        if worked:
            # Bitten back: the beds nearest working height are the ones that
            # have gone, which is what a face looks like after a week of it.
            reach *= 0.34 if 0.45 < z < 1.25 else 0.82
        slab(mb, (0.62 + reach * 0.30, 0.06 * (i % 3 - 1), z),
             reach * 0.62, 0.92 - 0.07 * i, 0.105,
             (rnd(seed, i) - 0.5) * 0.30, (rnd(seed, i, 2) - 0.5) * 0.24,
             graded([SW["coal"], SW["coal"], SW["coal_face"]], z - 0.12, z + 0.12,
                    up_bias=0.75))
    if worked:
        # spoil of shards, and the heap that came off the face
        for i in range(9):
            a = rnd(seed, i, 61) * 2 * pi
            d = 0.9 + 1.1 * rnd(seed, i, 62)
            rr = 0.10 + 0.11 * rnd(seed, i, 63)
            slab(mb, (1.15 + cos(a) * d * 0.8, sin(a) * d * 0.75, rr * 0.55),
                 rr * 1.5, rr * 1.1, rr * 0.42,
                 rnd(seed, i, 64) * 3.1, (rnd(seed, i, 65) - 0.5) * 0.5,
                 SW["coal"] if i % 3 else SW["coal_face"])
        blob(mb, (2.05, -0.55, 0.30), 0.72, 0.62, 0.36, 7, 2,
             graded([SW["coal"], SW["coal_face"]], 0.0, 0.7, up_bias=0.8),
             seed=seed + 9, rough=0.30, flat_bot=0.28)
    else:
        for i in range(4):
            a = rnd(seed, i, 71) * 2 * pi
            d = 1.35 + 0.6 * rnd(seed, i, 72)
            rr = 0.13 + 0.10 * rnd(seed, i, 73)
            slab(mb, (1.5 + cos(a) * d * 0.55, sin(a) * d * 0.8, rr * 0.5),
                 rr * 1.6, rr * 1.15, rr * 0.40,
                 rnd(seed, i, 74) * 3.1, (rnd(seed, i, 75) - 0.5) * 0.4,
                 SW["coal"] if i % 2 else SW["coal_face"])
    return mb.emit("R_Coal_" + ("Worked" if worked else "Seam"), coll)


# ------------------------------------------------------------------ berry ---
# Person-scale, because it is picked by hand: 1.65 m is chest-to-eye on a
# 1.7 m crew member, so a landing party reads as picking rather than as
# stooping over a shrub.

def build_berry(coll, picked=False):
    mb = Mesh()
    seed = 501
    # Measured, the first cut stood 1.90 m: taller than the 1.7 m crew
    # member who is supposed to be picking it, so the fruit was over his head
    # and the whole read of the prop was wrong. `top` is the AXIS the masses
    # are hung on, not the finished height -- the crown sits above it -- so it
    # is set from the height that was measured back off the built mesh.
    top = 1.42 * (0.88 if picked else 1.0)
    # Four steps, not three, with the mid tone counted twice: the lightest
    # green was landing on a third of the foliage, and at seven sides a single
    # face is a big piece of a bush.
    tones = ([SW["broad_d"], SW["scrub"], SW["scrub"], SW["broad_m"]] if picked
             else [SW["broad_d"], SW["broad_m"], SW["broad_m"], SW["broad_l"]])
    # bare twigs first, so a picked bush shows the frame it is built on
    for i in range(5):
        a = 2 * pi * i / 5.0 + rnd(seed, i) * 0.7
        # short, and mostly inside the foliage: a stem is the frame the bush
        # is built on, not a leg it stands on
        L = top * (0.34 + 0.22 * rnd(seed, i, 2)) * (1.30 if picked else 1.0)
        # Each stem comes out of the ground at its OWN spot. Started from a
        # single point they all drew the same base ring at the same radius and
        # the same phase -- five rings of five vertices, twenty of them exactly
        # on top of another, which is the defect the gate exists for and is
        # completely invisible in the viewport. A clump of stems is also what a
        # bush actually does.
        foot = (cos(a) * 0.16 * jit(0.4, seed, i, 6),
                sin(a) * 0.16 * jit(0.4, seed, i, 7), -0.06)
        tube(mb, [foot, (cos(a) * L * 0.42, sin(a) * L * 0.42, L)],
             [0.055, 0.028], 5, SW["bark_d"],
             phase=rnd(seed, i, 8) * 1.3, cap_a=SW["bark_d"], cap_b=SW["bark_d"])
    # Radii scale with `top` so the bush stays the same SHAPE at any height
    # -- absolute numbers here meant retuning the height changed the plant.
    # **A bush sits ON the ground.** Hung on `top * 0.60` with a full round
    # underside, the lowest foliage floated 0.35 m clear and the five stems
    # showed under it: the prop read as a green mushroom on legs. Each mass is
    # placed by where its BOTTOM lands now -- centre = rz * flat_bot + a
    # fingerbreadth -- which is the hulls' joinery rule (place a piece off the
    # thing it lands on) applied to the ground.
    FLAT = 0.82
    masses = ((0.00, 0.00, 0.99, 0.85, 0.70),
              (0.38, 0.27, 0.71, 0.64, 0.53),
              (-0.35, -0.22, 0.80, 0.72, 0.47))
    sc = 0.88 if picked else 1.0
    for i, (x, y, rx, ry, rz) in enumerate(masses):
        rx, ry, rz = rx * sc, ry * sc, rz * sc
        z = rz * FLAT + 0.06 + (0.30 * rz if i == 1 else 0.0)
        blob(mb, (x * sc, y * sc, z), rx, ry, rz, 7, 3,
             graded(tones, 0.0, top, up_bias=0.45,
                    break_one_in=(5 if picked else 0), alt=SW["scrub"], seed=seed),
             seed=seed * 3 + i, rough=0.22, flat_bot=FLAT)
    if picked:
        # three on the ground -- the ones that were dropped
        for i in range(3):
            a = rnd(seed, i, 81) * 2 * pi
            d = 0.72 + 0.42 * rnd(seed, i, 82)
            octa(mb, (cos(a) * d, sin(a) * d, 0.055), 0.055, SW["berry"])
    else:
        # Measured: eleven berries at 0.105 m were 88 of the bush's 247
        # faces -- more than a third of the mesh -- and still read as specks
        # on a 2.2 m plant. Seven at 0.17 cost 32 fewer triangles and can
        # actually be seen, which is the entire job of the fruit: it is what
        # tells a crew this bush is worth walking to.
        for i in range(7):
            a = rnd(seed, i, 91) * 2 * pi
            t = 0.30 + 0.60 * rnd(seed, i, 92)
            rad = 0.86 * (0.58 + 0.42 * sin(t * pi))
            octa(mb, (cos(a) * rad, sin(a) * rad * 0.88, top * (0.24 + 0.54 * t)),
                 0.17, SW["berry"])
    return mb.emit("R_Berry_" + ("Picked" if picked else "Bush"), coll)


# ------------------------------------------------------------------ wheat ---
# One stalk is invisible from a ship, so the NODE is a patch: a rough disc
# 3.4 m across of bundled stalks, each bundle standing for a handful. Every
# bundle leans downwind, the same +X the trees do, because they are in the
# same weather.

PATCH_R = 1.45
# Thirteen fat four-sided bundles in a 3.4 m disc read as a row of fence pegs:
# too few, too thick, too far apart. A crop is DENSE -- that is most of what
# says crop rather than scrub -- so the patch is tighter, the stalks are half
# as thick, three-sided (nothing this thin needs four), and there are thirty
# of them. Even at thirty this is 500-odd triangles for one node, which buys
# the read of a whole field.
PATCH_N = 30


def _patch_spots(seed, n=PATCH_N):
    """Spots on a sunflower spiral, not on a ring: a ring leaves the middle of
    the patch empty and the eye reads the hole, not the crop."""
    out = []
    golden = pi * (3.0 - 5.0 ** 0.5)
    for i in range(n):
        a = i * golden + (rnd(seed, i) - 0.5) * 0.5
        d = PATCH_R * ((i + 0.5) / n) ** 0.5 * jit(0.16, seed, i, 9)
        out.append((cos(a) * d, sin(a) * d, i))
    return out


def build_wheat(coll, reaped=False):
    mb = Mesh()
    seed = 601
    for x, y, i in _patch_spots(seed):
        if reaped:
            # stubble: the same stalks, cut at boot height, gone pale
            hh = 0.17 * jit(0.25, seed, i, 3)
            tube(mb, [(x, y, -0.04), (x, y, hh)],
                 [0.062 * jit(0.2, seed, i, 4), 0.052], 3, SW["straw"],
                 cap_a=SW["straw"], cap_b=SW["soil"],
                 phase=rnd(seed, i, 5) * 2.1)
        else:
            hh = 1.06 * jit(0.13, seed, i, 3)
            lean = 0.13 * jit(0.5, seed, i, 6)
            r = 0.062 * jit(0.18, seed, i, 4)
            stalk = SW["wheat_d"]
            path = [(x, y, -0.04),
                    (x + DOWNWIND[0] * lean * 0.25, y, hh * 0.52),
                    (x + DOWNWIND[0] * lean * 0.72, y, hh * 0.78),
                    (x + DOWNWIND[0] * lean, y, hh)]
            # The ear is the widest thing on the stalk and a different
            # colour: the two together are all that separates wheat from grass
            # at this triangle count.
            tube(mb, path, [r, r * 0.72, r * 1.95, r * 0.55], 3,
                 graded([stalk, stalk, SW["wheat_l"]], hh * 0.52, hh * 0.90,
                        up_bias=0.2),
                 cap_a=SW["wheat_l"], cap_b=SW["soil"],
                 phase=rnd(seed, i, 5) * 2.1)
    if reaped:
        # The sheaf: what the patch became. A bound bundle standing in its own
        # stubble is the oldest picture there is of a field that has been cut,
        # and it says "yield" without a basket or an icon.
        sx, sy = PATCH_R * 0.30, -PATCH_R * 0.34
        hh = 1.16
        r = 0.30
        tube(mb, [(sx, sy, 0.0), (sx, sy, hh * 0.52), (sx, sy, hh * 0.72),
                  (sx, sy, hh)],
             [r * 0.72, r, r * 0.62, r * 0.90], 7,
             graded([SW["straw"], SW["straw"], SW["wheat_l"]], hh * 0.55, hh * 0.9,
                    up_bias=0.25),
             cap_a=SW["wheat_l"], cap_b=SW["straw"])
        # the binding, at the waist, where the tie actually goes
        tube(mb, [(sx, sy, hh * 0.50), (sx, sy, hh * 0.58)],
             [r * 1.06, r * 1.06], 7, SW["wheat_d"])
    return mb.emit("R_Wheat_" + ("Reaped" if reaped else "Patch"), coll)


# ------------------------------------------------------------- collected ----
# What a landing party carries back and stacks on the beach: the resource
# after somebody has handled it. Every one of these is a made thing -- bound,
# woven or nailed -- because that is the whole difference between a resource
# and a yield, and it is what the standing props deliberately do not have.
#
# **Sized off the STACK, not off the plant.** `Stockpile.layerHeight` is
# 0.9 m and `CargoVisual` builds its units about a metre across, so a
# collected unit is roughly 1.2 x 1.2 x 0.9 and layers cleanly. That is the
# hulls' joinery rule again -- a piece is placed off the thing it lands on,
# and these land on each other. The wheat stook is the one deliberate
# exception and it is called out below.
#
# The crate and the ropes are cut from the SHIPS' swatches (plank, deck,
# rope, iron), not from island ones: a crate is a shipwright's work and
# should look like it came out of the same yard as the vessel it is loaded
# into.

STACK_H = 0.9                # Stockpile.layerHeight -- the unit these fit


def _lashing(mb, x, half_w, h, t, sw):
    """A binding over a bundle: down one side, across the top, down the other.

    Three straight pieces rather than one swept loop, because `tube` carries a
    FIXED ring frame -- a path that turns a corner puts its ring in the plane
    of travel and the section collapses. Three frames, three pieces, and the
    corners simply overlap, which is an intersection and allowed."""
    slab(mb, (x, 0.0, h + t * 0.5), t * 0.6, half_w, t * 0.5, 0.0, 0.0, sw)
    for sy in (-1.0, 1.0):
        slab(mb, (x, sy * (half_w + t * 0.4), h * 0.5),
             t * 0.6, t * 0.5, h * 0.55, 0.0, 0.0, sw)


def build_logs(coll):
    """A bundle of logs: nine of them, three courses, lashed twice.

    The ends are the point. `sapwood` is the one swatch on the island that
    only ever appears where a blade has been, and a bundle stacked end-on to
    the viewer is nine of them at once -- which says "cut, trimmed and
    carried" without a single extra triangle."""
    mb = Mesh()
    seed = 701
    L, r = 2.00, 0.150
    courses = ((4, 0.16), (3, 0.16 + 0.265), (2, 0.16 + 0.530))
    for c, (n_log, z) in enumerate(courses):
        for i in range(n_log):
            y = (i - (n_log - 1) * 0.5) * 0.305
            rr = r * jit(0.06, seed, c, i)
            tube(mb, [(-L / 2.0, y, z), (L / 2.0, y, z)],
                 [rr, rr * 0.94], 6, bark_of(z - rr, z + rr, seed * 3 + c * 7 + i),
                 uh=(0, 1, 0), vh=(0, 0, 1),
                 cap_a=SW["sapwood"], cap_b=SW["sapwood"],
                 phase=rnd(seed, c, i) * 1.6)
    top = 0.16 + 0.530 + r
    for x in (-L * 0.27, L * 0.27):
        _lashing(mb, x, 0.305 * 1.5 + r * 0.4, top, 0.075, SW["rope"])
    return mb.emit("R_Yield_Logs", coll)


def build_sheaves(coll):
    """A stook: three bound sheaves stood against each other, and one laid by.

    **This is the one collected asset that breaks the 0.9 m stack height, on
    purpose.** Sheaves are not piled -- they are stood in a stook to finish
    drying, which is why a stook is the oldest picture there is of a field
    that has been cut. Laying them flat to make the number work would be
    changing what the object IS to satisfy a spacing constant."""
    mb = Mesh()
    seed = 711
    # Feet 0.30 apart with sheaves 0.26 thick, converging to 0.22 of that
    # at the head: the three overlapped almost completely and the stook came
    # out as one lumpy sack with a lid on it. A stook only reads as a stook if
    # you can see that it is THREE things leaning on each other -- so the feet
    # stand well apart, the heads lean in without meeting, and each sheaf is
    # slimmer than the gap between them.
    # The overlap has to go the RIGHT way round, and it took two goes. At
    # foot 0.30 / head 0.22 the three merged everywhere and it was one sack;
    # at foot 0.46 / head 0.30 they parted at the bottom and merged at the
    # top, which is a camera tripod. A stook is the opposite: butts nearly
    # touching on the ground, heads leaning together but each still its own
    # bundle. So the feet sit about one sheaf apart and the heads overlap by
    # rather less than one.
    hh, r = 1.06, 0.225
    for i in range(3):
        a = 2.0 * pi * i / 3.0 + 0.4
        foot = 0.32
        bx, by = cos(a) * foot, sin(a) * foot
        tx, ty = cos(a) * foot * 0.53, sin(a) * foot * 0.53
        path = [(bx, by, 0.0),
                (lerp(bx, tx, 0.50), lerp(by, ty, 0.50), hh * 0.50),
                (lerp(bx, tx, 0.74), lerp(by, ty, 0.74), hh * 0.74),
                (tx, ty, hh)]
        # Flared at the head, not capped: the ears spread when a sheaf is
        # tied, and the flare is what stops the top reading as a lid.
        tube(mb, path, [r * 0.72, r, r * 0.68, r * 1.06], 7,
             graded([SW["straw"], SW["straw"], SW["wheat_d"], SW["wheat_l"]],
                    hh * 0.52, hh * 0.94, up_bias=0.26),
             cap_a=SW["wheat_l"], cap_b=SW["straw"],
             phase=rnd(seed, i) * 1.7)
        # the tie, at the waist where a tie actually goes
        w0 = (lerp(bx, tx, 0.46), lerp(by, ty, 0.46), hh * 0.46)
        w1 = (lerp(bx, tx, 0.56), lerp(by, ty, 0.56), hh * 0.56)
        tube(mb, [w0, w1], [r * 1.08, r * 1.08], 7, SW["wheat_d"],
             phase=rnd(seed, i) * 1.7)
    # one laid by, so the group reads as a working heap and not an ornament.
    # Seven sides and the same straw/ear grading as the standing ones -- at
    # six sides and a flat straw it read as a planed board lying in the grass.
    ly, lr, lL = -0.92, 0.215, 1.02
    tube(mb, [(-lL / 2.0, ly, lr), (lL / 2.0, ly, lr)],
         [lr * 1.12, lr * 0.88], 7,
         graded([SW["wheat_d"], SW["straw"], SW["wheat_l"]], ly - lr, ly + lr,
                up_bias=0.55),
         uh=(0, 1, 0), vh=(0, 0, 1),
         cap_a=SW["wheat_l"], cap_b=SW["wheat_l"])
    tube(mb, [(-0.07, ly, lr), (0.07, ly, lr)], [lr * 1.14, lr * 1.14], 7,
         SW["wheat_d"], uh=(0, 1, 0), vh=(0, 0, 1))
    return mb.emit("R_Yield_Wheat", coll)


def _basket(mb, cx, cy, scale, seed, fill_sw, heap=True):
    """A woven basket, filled and heaped.

    The weave is COURSES read off height -- the same rule that makes a strake
    a course on the hulls -- so four rings of a tapered tube come out as four
    bands of osier and the basket reads as woven rather than as a pot."""
    n = 8
    rim = 0.34 * scale
    hgt = 0.46 * scale
    tube(mb, [(cx, cy, 0.0), (cx, cy, hgt * 0.30), (cx, cy, hgt * 0.72),
              (cx, cy, hgt)],
         [rim * 0.62, rim * 0.86, rim * 0.98, rim], n,
         graded([SW["straw"], SW["wheat_d"], SW["straw"], SW["wheat_d"]],
                0.0, hgt, up_bias=0.12),
         cap_b=SW["wheat_d"], phase=rnd(seed, 1) * 0.8)
    # the rim hoop: the stiffest thing on a basket and what tops it off
    tube(mb, [(cx, cy, hgt), (cx, cy, hgt + 0.055 * scale)],
         [rim * 1.07, rim * 1.02], n, SW["bark_m"], phase=rnd(seed, 1) * 0.8)
    if not heap:
        return
    blob(mb, (cx, cy, hgt + 0.02 * scale), rim * 0.94, rim * 0.94,
         0.15 * scale, n, 1, fill_sw, seed=seed + 3, rough=0.16, flat_bot=0.2)
    for i in range(5):
        a = rnd(seed, i, 12) * 2 * pi
        d = rim * 0.55 * rnd(seed, i, 13)
        octa(mb, (cx + cos(a) * d, cy + sin(a) * d,
                  hgt + 0.13 * scale + 0.05 * scale * rnd(seed, i, 14)),
             0.085 * scale, SW["berry"])


def build_baskets(coll):
    """Two baskets of berries, one full and one part-filled."""
    mb = Mesh()
    seed = 721
    _basket(mb, 0.0, 0.0, 1.30, seed, SW["berry"])
    _basket(mb, 0.62, -0.34, 0.92, seed + 40, SW["berry"])
    # a handful spilt, because a full basket that has never been touched
    # reads as a shop display
    for i in range(4):
        a = rnd(seed, i, 21) * 2 * pi
        d = 0.62 + 0.34 * rnd(seed, i, 22)
        octa(mb, (cos(a) * d, sin(a) * d - 0.1, 0.055), 0.055, SW["berry"])
    return mb.emit("R_Yield_Berries", coll)


def _crate(mb, w, d, h, seed, sw_board, sw_post):
    """Four walls and four corner posts -- no lid and no floor.

    Built as WALLS rather than as a solid box because the contents have to
    heap out of the top: a box with a lid on it is a box, and whatever you
    pile on the lid is sitting on a box rather than in one. The posts are what
    make it read as nailed together instead of moulded, and they are the same
    trick as the ships' timberheads.

    Proportioned off `Kit_Crate` (0.70 x 0.70 x 0.55) so the yard that made
    the ship's crates made this one."""
    t = 0.055
    for sy in (-1.0, 1.0):
        slab(mb, (0.0, sy * (d / 2.0 - t / 2.0), h / 2.0),
             w / 2.0, t / 2.0, h / 2.0, 0.0, 0.0, sw_board)
    for sx in (-1.0, 1.0):
        # d/2 - t*0.63, not d/2 - t. Inset by exactly the wall thickness, the
        # end walls' corners landed on the side walls' corners -- eight
        # vertices sitting precisely on eight others, which is the defect the
        # gate exists for and is completely invisible in a viewport. Boards
        # are allowed to OVERLAP; they are not allowed to coincide.
        slab(mb, (sx * (w / 2.0 - t / 2.0), 0.0, h / 2.0),
             t / 2.0, d / 2.0 - t * 0.63, h / 2.0, 0.0, 0.0, sw_board)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            slab(mb, (sx * (w / 2.0 - t * 0.4), sy * (d / 2.0 - t * 0.4),
                      h / 2.0 + 0.02),
                 t * 1.1, t * 1.1, h / 2.0 + 0.02, 0.0, 0.0, sw_post)


def build_crate(coll, name, fill, lumps, lump_r, slabby=False, seed=731):
    """A crate with a resource heaped above its rim."""
    mb = Mesh()
    w, d, h = 1.00, 0.86, 0.58
    _crate(mb, w, d, h, seed,
           # A step lighter than the first cut. Black coal in a dark brown
           # box is one silhouette; the boards have to sit clear of what is
           # heaped on them or the crate stops being visible at all.
           graded([SW["plank_d"], SW["plank_w"], SW["deck_m"]], 0.0, h,
                  up_bias=0.30, break_one_in=5, alt=SW["deck_l"], seed=seed),
           SW["wale"])
    blob(mb, (0.0, 0.0, h + 0.02), w * 0.44, d * 0.44, 0.13, 8, 1, fill,
         seed=seed + 5, rough=0.18, flat_bot=0.25)
    for i in range(lumps):
        a = rnd(seed, i, 31) * 2 * pi
        dd = (w * 0.34) * rnd(seed, i, 32) ** 0.6
        rr = lump_r * (0.78 + 0.44 * rnd(seed, i, 33))
        at = (cos(a) * dd, sin(a) * dd * 0.86, h + 0.13 + rr * 0.55)
        if slabby:
            slab(mb, at, rr * 1.5, rr * 1.1, rr * 0.42,
                 rnd(seed, i, 34) * 3.1, (rnd(seed, i, 35) - 0.5) * 0.5, fill)
        else:
            # Two latitude rings, not one. One ring makes a bipyramid, and
            # seven bipyramids in a box read as a crate of cut gemstones
            # rather than as broken rock.
            blob(mb, at, rr, rr * 0.88, rr * 0.76, 6, 2, fill,
                 seed=seed * 3 + i, rough=0.34)
    return mb.emit(name, coll)


def build_coal_crate(coll):
    # Slabs, not lumps: coal cleaves flat, and that is the same cue the seam
    # uses. A crate of round black pebbles would read as a crate of ore.
    return build_crate(coll, "R_Yield_Coal",
                       graded([SW["coal"], SW["coal"], SW["coal_face"]],
                              0.55, 0.85, up_bias=0.72),
                       lumps=7, lump_r=0.115, slabby=True, seed=731)


def build_ore_crate(coll):
    return build_crate(coll, "R_Yield_Ore",
                       # bloom at one lump in nine, not one in five: at
                       # five a cool grey lump turned up in every crate and
                       # read as a stray rock somebody had boxed by mistake
                       graded([SW["ore_d"], SW["ore"], SW["ore"]], 0.55, 0.88,
                              up_bias=0.55, break_one_in=9, alt=SW["bloom"],
                              seed=741),
                       lumps=7, lump_r=0.135, slabby=False, seed=741)


# ----------------------------------------------------------------- layout ----
# Rows, with a 1.7 m crew reference standing at the head of every one. Scale is
# the one thing that cannot be judged from a screenshot unless the ruler is IN
# the screenshot -- that is what `RunProbe.Ruler` exists for on the Unity side,
# and the same rule applies here.

ROWS = [
    ("Trees",     0.0),
    ("Felled",  -16.0),
    ("Rock",    -26.0),
    ("Growing", -36.0),
    ("Collected", -46.0),
]


def clear_resources():
    for coll in list(bpy.data.collections):
        if coll.name.startswith(("RES_", "REVIEW")):
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(coll)
    for o in list(bpy.data.objects):
        if o.name.startswith(("R_", "CrewRef", "Ground")):
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


def ground(coll):
    """Something for everything to stand ON. A prop judged against a void
    reads a size it does not have."""
    s = 200.0
    ob = H.make_object("Ground", [(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)],
                       [[0, 1, 2, 3]], coll, smooth=False, mirror=False)
    m = bpy.data.materials.get("SeaSick_Ground")
    if m is None:
        m = bpy.data.materials.new("SeaSick_Ground")
        m.use_nodes = True
        b = m.node_tree.nodes.get("Principled BSDF")
        if b:
            # TerrainChunkMesher's own grass, so foliage is judged against the
            # colour it will actually stand on and not against a grey slab.
            b.inputs["Base Color"].default_value = (0.30, 0.55, 0.22, 1.0)
            b.inputs["Roughness"].default_value = 0.9
    # **Workbench draws `diffuse_color`, not the Principled node.** The props
    # come out right because they carry the palette TEXTURE and the shading
    # mode is TEXTURE; anything without one falls back to the material's
    # viewport colour, so the grass rendered as the same grey as the sky and
    # every canopy was being judged against nothing.
    m.diffuse_color = (0.30, 0.55, 0.22, 1.0)
    ob.data.materials.append(m)
    return ob


def ruler(name, x, y, coll):
    """The 1.7 m crew member, painted so he cannot be mistaken for an asset.

    Scale is the one thing that cannot be judged from a screenshot, because
    every cue in the frame is one of the things under suspicion -- so the
    ruler goes IN the frame, and it goes in red."""
    ob = H.crew_ref(name, x, y, coll)
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    u, v = H.swatch_uv(SW["lining"])
    for li in range(len(me.loops)):
        uv.data[li].uv = (u, v)
    me.materials.append(H.wood_material())
    return ob


def build_all():
    clear_resources()
    H.wood_material()
    made = {}

    trees = H.get_collection("RES_Trees")
    y = ROWS[0][1]
    x = 0.0
    for p in TREES:
        ob = build_tree(p, trees)
        ob.location = (x, y, 0.0)
        made.setdefault("Trees", []).append(ob)
        x += 8.6
    ruler("CrewRef_Trees", -6.0, y, trees)

    felled = H.get_collection("RES_Felled")
    y = ROWS[1][1]
    x = 0.0
    for p in STUMPS:
        ob = build_stump(p, felled)
        ob.location = (x, y, 0.0)
        made.setdefault("Felled", []).append(ob)
        x += 3.4
    for nm, L, r in (("R_Timber_Log", 4.6, 0.36), ("R_Timber_Billet", 2.1, 0.28)):
        ob = build_log(nm, L, r, felled, seed=211 + int(L * 10),
                       stubs=3 if L > 3 else 2)
        ob.location = (x, y, 0.0)
        made.setdefault("Felled", []).append(ob)
        x += L + 2.2
    ruler("CrewRef_Felled", -3.0, y, felled)

    rock = H.get_collection("RES_Rock")
    y = ROWS[2][1]
    for i, (fn, kw) in enumerate(((build_iron, {}), (build_iron, {"worked": True}),
                                  (build_coal, {}), (build_coal, {"worked": True}))):
        ob = fn(rock, **kw)
        ob.location = (i * 7.0, y, 0.0)
        made.setdefault("Rock", []).append(ob)
    ruler("CrewRef_Rock", -3.0, y, rock)

    grow = H.get_collection("RES_Growing")
    y = ROWS[3][1]
    for i, (fn, kw) in enumerate(((build_berry, {}), (build_berry, {"picked": True}),
                                  (build_wheat, {}), (build_wheat, {"reaped": True}))):
        ob = fn(grow, **kw)
        ob.location = (i * 6.0, y, 0.0)
        made.setdefault("Growing", []).append(ob)
    ruler("CrewRef_Growing", -3.0, y, grow)

    got = H.get_collection("RES_Collected")
    y = ROWS[4][1]
    for i, fn in enumerate((build_logs, build_sheaves, build_baskets,
                            build_coal_crate, build_ore_crate)):
        ob = fn(got)
        ob.location = (i * 3.6, y, 0.0)
        made.setdefault("Collected", []).append(ob)
    ruler("CrewRef_Collected", -2.6, y, got)

    ground(H.get_collection("RES_Stage"))
    bpy.context.view_layer.update()
    return made


# ----------------------------------------------------------------- report ----

def measure():
    """Size and cost of every asset, checked against the charter.

    A table and not a screenshot, for the reason WorldScale.cs gives: scale is
    the one thing that cannot be judged from a picture, because every cue in
    the frame is one of the things under suspicion."""
    bpy.context.view_layer.update()
    rows, total = [], 0
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type != 'MESH' or not o.name.startswith("R_"):
            continue
        bb = [o.matrix_world @ __import__("mathutils").Vector(c) for c in o.bound_box]
        w = max(p.x for p in bb) - min(p.x for p in bb)
        d = max(p.y for p in bb) - min(p.y for p in bb)
        hgt = max(p.z for p in bb) - min(p.z for p in bb)
        a = H.audit(o)
        total += a["triangles"]
        rows.append((o.name, hgt, max(w, d), a["triangles"]))
    print("%-26s %8s %8s %8s  %s" % ("asset", "height", "spread", "tris", "charter"))
    for n, hgt, sp, t in rows:
        note = ""
        if n.startswith("R_Tree_Snag"):
            # A snag is a tree that BROKE. Holding it to TreeMin would be
            # holding a stump to the height of the trunk it came off; what
            # matters is that it still reads as a tree, so it is checked
            # against the band it broke out of.
            note = "broken bole, 0.5-0.9 of a live tree" + \
                ("" if TREE_MIN * 0.5 <= hgt <= TREE_MAX * 0.9 else "  <-- OUT")
        elif n.startswith("R_Tree_"):
            # 2%: the crown's top vertex carries the same roughness jitter
            # every other vertex does, so the finished height is the authored
            # one give or take a facet. Tighter than that and the gate fails
            # on the variety it was built to allow.
            note = "trees 9-14" + ("" if TREE_MIN * 0.98 <= hgt <= TREE_MAX * 1.02
                                   else "  <-- OUT")
        elif n.startswith(("R_Iron", "R_Coal")):
            note = "boulder 1.0-3.2" + ("" if hgt <= BOULDER_MAX + .1 else "  <-- OUT")
        elif n.startswith("R_Yield_"):
            # These stack, so the number that matters is the stockpile's
            # layer height -- except the wheat stook, which is stood, not
            # piled, and says so.
            note = ("stook, stood not stacked" if "Wheat" in n
                    else "stacks at %.2f" % STACK_H
                    + ("" if hgt <= STACK_H * 1.12 else "  <-- TALL"))
        elif n.startswith("R_Berry"):
            # Picked BY HAND, so it is judged against the hand: the fruit has
            # to be reachable, which means the bush stops below eye height.
            note = "picked by hand, person %.2f" % PERSON + \
                ("" if hgt <= PERSON else "  <-- OVER HIS HEAD")
        print("%-26s %8.2f %8.2f %8d  %s" % (n, hgt, sp, t, note))
    print("TOTAL %d triangles across %d assets" % (total, len(rows)))
    return rows


def palette_share(prefix="R_", floor=3.0):
    """What each asset is actually MADE of, weighted by AREA.

    Weighted by face COUNT this lies, and it lied here: eight ore nodules are
    112 of an outcrop's 203 faces, which reads as "75% of the rock is ore" --
    while each nodule is the size of a fist and the thing next to it is a
    square metre of boulder. By area the same rock is 71% stone and 28% ore,
    which is what the eye gets. Count is the right metric for what a mesh
    COSTS and the wrong one for what it LOOKS like.

    It did earn its keep once, on the worked iron: `bloom` was both the
    commonest face AND the largest area, and bloom is grey -- so the yield,
    the one thing that prop exists to show, was rendering as a heap of
    pebbles."""
    name_of = {v: k for k, v in SW.items()}
    n = H.PALETTE_GRID
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type != 'MESH' or not o.name.startswith(prefix):
            continue
        me = o.data
        uv = me.uv_layers.get("UVMap")
        if uv is None:
            continue
        area = {}
        for pg in me.polygons:
            u, v = uv.data[pg.loop_indices[0]].uv
            c = int(v * n) * n + int(u * n)
            area[c] = area.get(c, 0.0) + pg.area
        tot = sum(area.values()) or 1.0
        sh = sorted(((name_of.get(k, str(k)), 100.0 * v / tot)
                     for k, v in area.items()), key=lambda kv: -kv[1])
        print("%-26s %7.1f m2  %s" % (o.name, tot,
              "  ".join("%s %.0f%%" % (k, v) for k, v in sh if v >= floor)))


def check_palette():
    """Read the palette back OFF DISK and sample every cell against the table.

    Not paranoia: setting the colour space on a saved image re-reads the file,
    and calling `update()` on a generated one re-runs the generator -- both of
    which have written an all-black palette to disk in this project while
    every render still looked correct, because the render reads the buffer in
    memory. The file is the thing Unity loads."""
    path = os.path.join(H.PALETTE_DIR, H.PALETTE_NAME + ".png")
    img = bpy.data.images.load(path, check_existing=False)
    try:
        px, size, n = list(img.pixels), img.size[0], H.PALETTE_GRID
        cell = size // n
        bad = []
        for i, want in enumerate(H.SWATCH):
            cx, cy = (i % n) * cell + cell // 2, (i // n) * cell + cell // 2
            o = (cy * size + cx) * 4
            got = tuple(px[o:o + 3])
            if max(abs(a - b) for a, b in zip(got, want)) > 0.01:
                bad.append((i, want, got))
        print("palette %dx%d on disk, %d swatches: %s"
              % (size, size, len(H.SWATCH),
                 "OK" if not bad else "MISMATCH %r" % bad))
        return bad
    finally:
        bpy.data.images.remove(img)


# ------------------------------------------------------------------ shoot ----

def _bounds(objs):
    import mathutils
    pts = [o.matrix_world @ mathutils.Vector(c) for o in objs for c in o.bound_box]
    lo = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts),
                           min(p.z for p in pts)))
    hi = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts),
                           max(p.z for p in pts)))
    return lo, hi


def shoot(path, lo, hi, ortho=True, az=-70.0, el=16.0, margin=1.14,
          res=(2000, 900), flat=False):
    """Render the camera's view of a box that has ALREADY been measured.

    Bounds come in rather than being read here, because framing a row means
    hiding the other three and **a hidden object has no evaluated mesh** --
    measure everything while it is visible, then hide, then shoot. Four cells
    of a hull contact sheet came out as extreme close-ups on stale bounds the
    last time that order was the other way round.

    `view_context=False` is not optional either: without it `render.opengl`
    renders whatever the VIEWPORT is looking at and silently ignores the
    camera."""
    import mathutils
    sc = bpy.context.scene
    ctr = (lo + hi) * 0.5
    span = max(hi.x - lo.x, (hi.z - lo.z) * res[0] / res[1], 1.0)

    cam = bpy.data.objects.get("ShotCam")
    if cam is None:
        cam = bpy.data.objects.new("ShotCam", bpy.data.cameras.new("ShotCam"))
        sc.collection.objects.link(cam)
    d = span * 1.6
    a, e = math.radians(az), math.radians(el)
    eye = ctr + mathutils.Vector((cos(a) * cos(e), sin(a) * cos(e), sin(e))) * d
    cam.location = eye
    cam.rotation_euler = (ctr - eye).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO' if ortho else 'PERSP'
    cam.data.clip_end = max(1000.0, d * 4.0)
    if ortho:
        # Orthographic on purpose for a line-up: a review of a set of props is
        # a SIZE comparison first, and perspective makes the near end of a row
        # bigger than the far end for reasons that have nothing to do with the
        # assets.
        cam.data.ortho_scale = span * margin
    else:
        cam.data.lens = 55.0
    # Say where it actually stands. Guessing an azimuth put the camera on the
    # closed side of a sectioned hull twice; a printed vector cannot lie.
    print("  %-18s cam (%.1f %.1f %.1f) -> (%.1f %.1f %.1f)  span %.1f m"
          % (os.path.basename(path), eye.x, eye.y, eye.z,
             ctr.x, ctr.y, ctr.z, span))
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'FLAT' if flat else 'STUDIO'
    sh.color_type = 'TEXTURE'          # EEVEE's opengl render comes out flat grey
    sh.show_shadows = not flat
    sh.show_cavity = not flat
    sh.cavity_type = 'WORLD'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.filepath = path
    bpy.ops.render.opengl(animation=False, write_still=True, view_context=False)
    return path


def _row_objects(name):
    coll = bpy.data.collections.get("RES_" + name)
    if coll is None:
        return []
    return [o for o in coll.objects if o.type == 'MESH']


def contact(out_dir, res_map=None):
    """One shot per row, plus a perspective three-quarter of the trees.

    `render.opengl` renders the SCENE, not the objects handed to a framing
    call, so each row is shot with the other three hidden -- and every bound
    is measured up front, before anything is hidden."""
    os.makedirs(out_dir, exist_ok=True)
    bpy.context.view_layer.update()

    bounds = {}
    for name, _y in ROWS:                       # measure first, all visible
        objs = _row_objects(name)
        if objs:
            bounds[name] = _bounds(objs)

    hideable = [o for o in bpy.data.objects
                if o.type == 'MESH' and (o.name.startswith("R_")
                                         or o.name.startswith("CrewRef"))]
    shots = []
    try:
        for name, _y in ROWS:
            if name not in bounds:
                continue
            keep = set(o.name for o in _row_objects(name))
            keep |= {o.name for o in hideable if o.name.startswith("CrewRef_" + name)}
            for o in hideable:
                o.hide_render = o.name not in keep
                o.hide_set(o.name not in keep)
            lo, hi = bounds[name]
            res = (res_map or {}).get(name, (2100, 820))
            shots.append(shoot(os.path.join(out_dir, "row_%s.png" % name.lower()),
                               lo, hi, ortho=True, res=res))
            if name == "Trees":
                shots.append(shoot(
                    os.path.join(out_dir, "row_trees_persp.png"), lo, hi,
                    ortho=False, az=-62.0, el=11.0, res=(2100, 900)))
                # FLAT light shows the swatch and nothing else. Workbench's
                # studio lighting multiplies an upward face hard enough to
                # make a mid green look like snow, and tuning a palette
                # against that is how a fleet of correctly-coloured sails got
                # hunted for a UV bug that was never there.
                shots.append(shoot(
                    os.path.join(out_dir, "row_trees_flat.png"), lo, hi,
                    ortho=True, res=res, flat=True))
    finally:
        for o in hideable:                      # always, not just on success
            o.hide_render = False
            o.hide_set(False)
        bpy.context.view_layer.update()
    return shots


# ----------------------------------------------------------------- export ----
# Not called by `build_all`. These go into Unity only once Kevin has looked at
# them, and they go through the SAME settings the hulls use -- the flags are
# not negotiable and the reasons are in HULLS.md: without
# `apply_scale_options='FBX_SCALE_ALL'` and `bake_space_transform` the meshes
# arrive at scale 100 with vertices at 1/100.
#
# The hulls also yaw -90 deg on the way out, so a bow drawn at +X lands on
# Unity +Z. A tree has no bow: it is yawed at random on placement, so there is
# nothing to correct and no yaw is applied. What DOES survive the trip is the
# lean -- built along +X here, which is Unity -X -- so `DOWNWIND` has to be
# read on the Unity side rather than assumed.

RES_DIR = ("/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/"
           "Resources/Island")


def export(out_dir=RES_DIR):
    if H.report_topology("R_"):
        raise RuntimeError("topology defects -- fix them before exporting")
    os.makedirs(out_dir, exist_ok=True)
    vl = bpy.context.view_layer
    for o in bpy.data.objects:
        o.hide_set(False)
    done = []
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.name.startswith("R_"):
            continue
        saved = tuple(o.location)
        for q in bpy.data.objects:
            q.select_set(False)
        o.location = (0.0, 0.0, 0.0)
        o.select_set(True)
        vl.objects.active = o
        vl.update()
        fname = o.name[2:].lower() + ".fbx"
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(out_dir, fname), use_selection=True,
            object_types={'MESH'}, use_mesh_modifiers=True,
            mesh_smooth_type='FACE', global_scale=1.0, apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
            axis_forward='-Z', axis_up='Y', use_triangles=False,
            add_leaf_bones=False, path_mode='STRIP')
        o.location = saved
        done.append(fname)
    vl.update()
    return done


if __name__ == "__main__" or True:
    RESULT = build_all()
    print("built:", {k: len(v) for k, v in RESULT.items()})
