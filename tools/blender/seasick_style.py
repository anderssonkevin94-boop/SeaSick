# SeaSick -- style pass v2: "carved" low poly.
#
# Re-runnable:
#   exec(open('/Users/kevinandersson/Desktop/SeaSick/tools/blender/seasick_style.py').read())
#
# What this is: a proposal for how every island asset should be built from
# here on, shown on the two things that matter most -- a tree (the commonest
# object in the world) and an ore outcrop (the thing a crew sails for).
#
# The three rules the style is made of:
#   1. SILHOUETTE FIRST. A tree is judged at 150 m from a deck. Every triangle
#      goes on the outline; nothing goes inside it. The spruce is ONE jagged
#      envelope, not a stack of cones, because a stack of cones is a toy.
#   2. FACETED FORM, SOFT COLOUR. Faces are flat, but colour is a per-vertex
#      gradient (dark underneath and toward the trunk, lit and warm on top,
#      cool in the shade). This is what makes low poly read as "carved from
#      a real thing" rather than "asset pack". It costs nothing: vertex
#      colour, which the terrain/scenery shader already reads.
#   3. ROCK IS PLANES. Convex shards with big flat faces and sharp edges,
#      sunk into the ground. Boulders, ore hosts, sea stacks and cliffs are
#      all the same shard, at different sizes.
#
# Same conventions as the resources: up +Z, metres, origin on the ground.
# Nothing here touches the existing RES_ collections or the shared palette.

import bpy, bmesh, math, random
from math import sin, cos, pi
from mathutils import Vector, Euler, Matrix

OUT = "/private/tmp/claude-501/-Users-kevinandersson-Desktop-SeaSick/e10dc1e2-5bdc-4714-a250-075ea7e71e6a/scratchpad"
COLL = "STYLE_V2"
ORIGIN = Vector((0.0, 150.0, 0.0))      # clear of the ISLAND test ground

# ---------------------------------------------------------------- colours ---
# sRGB. Read off the two reference boards: the forest is a DARK blue-green
# mass with warm olive tips where the sun hits it; rock is warm grey-tan; the
# ground is olive, not lawn.
C = dict(
    spruce_d=(0.08, 0.14, 0.11), spruce_m=(0.15, 0.25, 0.15), spruce_l=(0.27, 0.37, 0.18),
    bark_d=(0.19, 0.13, 0.09),   bark_m=(0.30, 0.22, 0.15),   bark_l=(0.44, 0.35, 0.25),
    stone_d=(0.26, 0.25, 0.24),  stone_m=(0.42, 0.40, 0.36),  stone_l=(0.58, 0.54, 0.47),
    rust=(0.58, 0.28, 0.12),     rust_d=(0.31, 0.14, 0.08),   hematite=(0.17, 0.15, 0.16),
    broad_d=(0.12, 0.19, 0.10),  broad_m=(0.22, 0.33, 0.14),  broad_l=(0.40, 0.48, 0.19),
    palm_d=(0.14, 0.26, 0.11),   palm_l=(0.36, 0.50, 0.18),
    sand=(0.80, 0.73, 0.55),     moss=(0.26, 0.35, 0.17),     dry=(0.52, 0.52, 0.28),
    grass=(0.36, 0.44, 0.22),    shade=(0.08, 0.11, 0.16),    ruler=(0.75, 0.12, 0.10),
)


def srgb_to_lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def lerp(a, b, t):
    return a + (b - a) * t


def mix(a, b, t):
    return tuple(lerp(a[i], b[i], t) for i in range(3))


def scale(c, k):
    return tuple(min(1.0, x * k) for x in c)


def shade(c, f):
    """Darken by factor f (< 1) AND cool it, the way shadow actually reads.
    Brighten (> 1) and warm it slightly."""
    if f < 1.0:
        return mix(scale(c, f), C["shade"], (1.0 - f) * 0.45)
    return mix(scale(c, f), (1.0, 0.95, 0.80), (f - 1.0) * 0.25)


# ---------------------------------------------------------------- builder ---

class Build:
    """Triangles, each with a colour RULE: rule(face_normal, vertex_pos) ->
    sRGB, evaluated per loop (per corner) after normals are settled. That is
    the whole trick: flat faces, but every corner can be a different tone."""

    def __init__(self):
        self.v, self.f, self.rule = [], [], []

    def vert(self, p):
        self.v.append(Vector(p))
        return len(self.v) - 1

    def tri(self, a, b, c, rule):
        if len({a, b, c}) < 3:
            return
        self.f.append((a, b, c))
        self.rule.append(rule)

    def quad(self, a, b, c, d, rule):
        self.tri(a, b, c, rule)
        self.tri(a, c, d, rule)

    def add_bmesh(self, bm, rule):
        base = len(self.v)
        bm.verts.ensure_lookup_table()
        for vtx in bm.verts:
            self.v.append(vtx.co.copy())
        for fc in bm.faces:
            idx = [base + vtx.index for vtx in fc.verts]
            for k in range(1, len(idx) - 1):
                self.tri(idx[0], idx[k], idx[k + 1], rule)

    def emit(self, name, coll, smooth=False, recalc=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(p) for p in self.v], [], list(self.f))
        me.update()
        if recalc:
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            bm.to_mesh(me)
            bm.free()
            me.update()
        for pg in me.polygons:
            pg.use_smooth = smooth
        ca = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER')
        for pg in me.polygons:
            n = pg.normal
            rule = self.rule[pg.index]
            for li in pg.loop_indices:
                p = me.vertices[me.loops[li].vertex_index].co
                col = rule(n, p)
                ca.data[li].color = (*srgb_to_lin(col), 1.0)
        ob = bpy.data.objects.new(name, me)
        coll.objects.link(ob)
        me.materials.append(style_material())
        return ob


def style_material():
    m = bpy.data.materials.get("SS_Style")
    if m is None:
        m = bpy.data.materials.new("SS_Style")
        m.use_nodes = True
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        b.inputs["Roughness"].default_value = 0.92
        if "Specular IOR Level" in b.inputs:
            b.inputs["Specular IOR Level"].default_value = 0.15
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        nt.links.new(vc.outputs["Color"], b.inputs["Base Color"])
    return m


def flat_material(name, col):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*srgb_to_lin(col), 1.0)
        b.inputs["Roughness"].default_value = 0.95
        m.diffuse_color = (*srgb_to_lin(col), 1.0)
    return m


# ----------------------------------------------------------------- spruce ---
# One envelope. The profile zig-zags: a wide "skirt" ring, then a tucked-in
# ring above it where the next skirt hangs from. Consecutive rings are
# rotated half a step so the strip between them is a row of alternating
# triangles -- that stagger is what makes the outline ragged like foliage
# instead of stepped like a pagoda. Skirt tips droop; the tuck rings are the
# shadow under each layer, and they are painted that way.

def build_spruce(name, coll, h=13.0, tiers=6, sides=6, crown=0.165,
                 lean=0.35, seed=1, base=0.13, trunk_sides=5, trunk_segs=3):
    rng = random.Random(seed)
    B = Build()
    lean_dir = Vector((1.0, 0.0))            # +X is downwind, as the resources do
    ax = lambda z: Vector((lean_dir.x * lean * (z / h) ** 1.7,
                           lean_dir.y * lean * (z / h) ** 1.7))

    # --- trunk: 5-sided, flared foot, runs to just under the tip ------------
    r0 = h * 0.028
    top = h * 0.86            # ends inside the leader, never above it
    nseg = trunk_segs
    rings = []
    bark_rule = lambda n, p: shade(C["bark_m"] if p.z > h * 0.10 else C["bark_d"],
                                   0.72 + 0.34 * (n.z * 0.5 + 0.5) + 0.10 * (p.z / h))
    ts = trunk_sides
    for k in range(nseg + 1):
        t = k / nseg
        z = top * t
        r = max(0.035, r0 * (1.0 - t) ** 0.55) * (1.55 if k == 0 else 1.0)
        c = ax(z)
        ring = []
        for j in range(ts):
            a = 2 * pi * j / ts + (0.3 if k % 2 else 0.0)
            rr = r * (1.0 + 0.16 * (rng.random() - 0.5))
            ring.append(B.vert((c.x + cos(a) * rr, c.y + sin(a) * rr, z)))
        rings.append(ring)
    for k in range(nseg):
        A, Bq = rings[k], rings[k + 1]
        for j in range(ts):
            B.quad(A[j], A[(j + 1) % ts], Bq[(j + 1) % ts], Bq[j], bark_rule)
    foot = B.vert((0, 0, -0.05))
    for j in range(ts):
        B.tri(rings[0][(j + 1) % ts], rings[0][j], foot, bark_rule)

    # --- crown -------------------------------------------------------------
    z0 = h * base
    R0 = crown * h
    # rings from bottom to top: skirt, tuck, skirt, tuck, ... , skirt, tip
    # The tuck is SHALLOW (0.66 of the skirt), because a deep tuck turns
    # every layer into a bell and the tree into a pagoda. What makes the
    # outline read as foliage is the ragged skirt radius (0.6-1.25) and the
    # half-step stagger, not the depth of the notch. The lowest tier is
    # slightly narrower than the one above it: a spruce's bottom branches
    # are its sparsest, and a tree that is widest at the very bottom is a
    # cone on a stick.
    # The taper is a power curve that keeps thinning to the top: with a
    # floor on the radius the last two tiers came out the same width and
    # the tree finished as a bottle, not a spire.
    prof = []                                     # (z, r, is_skirt)
    gap = (h - z0) * 0.92 / max(1, tiers)
    for i in range(tiers):
        t = i / max(1, tiers - 1)
        zb = z0 + gap * i
        R = (R0 * (1.0 - t) ** 1.15 + h * 0.009) * (0.84 if i == 0 else 1.0)
        prof.append((zb, R, True))
        prof.append((zb + gap * 0.58, R * 0.66, False))
    ring_ids = []
    for ri, (z, r, skirt) in enumerate(prof):
        c = ax(z)
        ids = []
        for j in range(sides):
            a = 2 * pi * j / sides + (pi / sides if ri % 2 else 0.0) + 0.35 * (seed % 7) / 7.0
            if skirt:
                rr = r * (0.62 + 0.62 * rng.random())
                dz = -r * (0.04 + 0.14 * rng.random())          # drooping tips
            else:
                rr = r * (0.80 + 0.36 * rng.random())
                dz = r * 0.14 * (rng.random() - 0.5)
            ids.append(B.vert((c.x + cos(a) * rr, c.y + sin(a) * rr, z + dz)))
        ring_ids.append(ids)
    tipc = ax(h)
    tip = B.vert((tipc.x, tipc.y, h))

    def crown_rule(n, p):
        # facing up = lit, facing down = the underside of a layer. Then a
        # gentle gradient up the tree and a darkening toward the axis.
        up = n.z * 0.5 + 0.5
        tone = C["spruce_d"] if up < 0.38 else C["spruce_m"] if up < 0.72 else C["spruce_l"]
        c = ax(p.z)
        rad = math.hypot(p.x - c.x, p.y - c.y) / max(0.3, R0)
        f = 0.70 + 0.30 * min(1.0, rad) + 0.25 * up + 0.12 * ((p.z - z0) / max(1.0, h - z0))
        return shade(tone, f)

    for ri in range(len(ring_ids) - 1):
        A, Bq = ring_ids[ri], ring_ids[ri + 1]
        for j in range(sides):
            jn = (j + 1) % sides
            B.tri(A[j], A[jn], Bq[j], crown_rule)
            B.tri(Bq[j], A[jn], Bq[jn], crown_rule)
    last = ring_ids[-1]
    for j in range(sides):
        B.tri(last[j], last[(j + 1) % sides], tip, crown_rule)
    # underside of the lowest skirt, closed and dark
    under_c = ax(z0)
    under = B.vert((under_c.x, under_c.y, z0 - R0 * 0.12))
    first = ring_ids[0]
    for j in range(sides):
        B.tri(first[(j + 1) % sides], first[j], under, crown_rule)

    return B.emit(name, coll)


# -------------------------------------------------------------- broadleaf ---
# One convex hull round three or four lobe centres. From a deck a broadleaf
# crown IS convex -- the lobes show as facets on the outline, not as separate
# balls -- and a hull is the cheapest closed surface there is. Two limbs
# under it are what say "tree" rather than "green rock on a post".

def build_broadleaf(name, coll, h=10.5, crown=0.31, seed=5, lobes=3, pts=34,
                    lean=0.4, lod=False):
    rng = random.Random(seed)
    B = Build()
    R = crown * h
    z0 = h * 0.40                            # crown bottom
    zc = z0 + (h - z0) * 0.52                # crown centre
    ax = lambda z: Vector((lean * (z / h) ** 1.6, 0.0))
    r0 = h * 0.034
    sides = 4 if lod else 5
    nseg = 2 if lod else 3
    bark_rule = lambda n, p: shade(C["bark_m"] if p.z > h * 0.08 else C["bark_d"],
                                   0.72 + 0.34 * (n.z * 0.5 + 0.5) + 0.10 * (p.z / h))
    rings = []
    for k in range(nseg + 1):
        t = k / nseg
        z = zc * t
        r = max(0.05, r0 * (1.0 - t * 0.55)) * (1.5 if k == 0 else 1.0)
        c = ax(z)
        rings.append([B.vert((c.x + cos(2 * pi * j / sides + 0.3 * k) * r * (1 + 0.15 * (rng.random() - 0.5)),
                              c.y + sin(2 * pi * j / sides + 0.3 * k) * r, z)) for j in range(sides)])
    for k in range(nseg):
        for j in range(sides):
            B.quad(rings[k][j], rings[k][(j + 1) % sides], rings[k + 1][(j + 1) % sides], rings[k + 1][j], bark_rule)
    foot = B.vert((0, 0, -0.05))
    for j in range(sides):
        B.tri(rings[0][(j + 1) % sides], rings[0][j], foot, bark_rule)
    if not lod:
        # two limbs, leaving the bole just under the crown and heading for
        # the lobes, 4-sided, one segment
        for i in range(2):
            a = rng.random() * 2 * pi
            z = z0 * (0.92 + 0.06 * i)
            c = ax(z)
            L = R * 0.75
            p0 = Vector((c.x, c.y, z)); p1 = p0 + Vector((cos(a) * L, sin(a) * L, L * 0.55))
            rr = r0 * 0.45
            ra = [B.vert(p0 + Vector((cos(2 * pi * j / 4) * rr, sin(2 * pi * j / 4) * rr, 0))) for j in range(4)]
            rb = [B.vert(p1 + Vector((cos(2 * pi * j / 4) * rr * 0.5, sin(2 * pi * j / 4) * rr * 0.5, 0))) for j in range(4)]
            for j in range(4):
                B.quad(ra[j], ra[(j + 1) % 4], rb[(j + 1) % 4], rb[j], bark_rule)
    # The crown: the union of a few lobe spheres, SAMPLED ALONG RAYS from
    # the crown centre so every point lands on the outer surface and every
    # one survives the hull. Random points on the lobes left most of them
    # inside, the hull came out as eight corners, and a crown with eight
    # corners is a green boulder on a post.
    bm = bmesh.new()
    cc = ax(zc)
    C0 = Vector((cc.x, cc.y, zc))
    lobes_ = [(C0, R * 0.80)]
    for i in range(lobes):
        a = 2 * pi * i / lobes + rng.random() * 1.2
        lobes_.append((Vector((cc.x + cos(a) * R * 0.42, cc.y + sin(a) * R * 0.42,
                               zc + (rng.random() - 0.30) * R * 0.45)), R * (0.62 + 0.16 * rng.random())))
    ga = pi * (3.0 - math.sqrt(5.0))
    for i in range(pts):
        u = 1.0 - 2.0 * (i + 0.5) / pts
        a = ga * i + rng.random() * 0.5
        s = math.sqrt(max(0.0, 1 - u * u))
        d = Vector((s * cos(a), s * sin(a), u * 0.88)).normalized()
        t_best = 0.0
        for c, rr in lobes_:
            oc = C0 - c
            b = oc.dot(d)
            disc = b * b - (oc.dot(oc) - rr * rr)
            if disc > 0:
                t_best = max(t_best, -b + math.sqrt(disc))
        bm.verts.new(C0 + d * t_best * (0.94 + 0.10 * rng.random()))
    bmesh.ops.convex_hull(bm, input=bm.verts[:])
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    zmin = z0 - R * 0.05
    for v in bm.verts:
        if v.co.z < zmin:
            v.co.z = zmin - 0.1 * rng.random()

    def crown_rule(n, p):
        up = n.z * 0.5 + 0.5
        tone = C["broad_d"] if up < 0.38 else C["broad_m"] if up < 0.72 else C["broad_l"]
        rad = math.hypot(p.x - cc.x, p.y - cc.y) / max(0.3, R)
        f = 0.62 + 0.22 * min(1.0, rad) + 0.30 * up + 0.14 * ((p.z - z0) / max(1.0, h - z0))
        return shade(tone, f)

    B.add_bmesh(bm, crown_rule)
    bm.free()
    return B.emit(name, coll)


# ------------------------------------------------------------------- palm ---
# A curved bole and a crown of folded fronds. Each frond is a V in section
# (ridge up), two segments long, and faced on BOTH sides because a palm on a
# sandbank is seen from below as often as from above. Rings of tone up the
# bole are the leaf scars, and they cost nothing.

def build_palm(name, coll, h=9.0, seed=9, fronds=8, bend=1.5, lod=False):
    rng = random.Random(seed)
    B = Build()
    sides = 4 if lod else 5
    nseg = 3 if lod else 5
    top = h * 0.90
    ax = lambda z: Vector((bend * (z / h) ** 2.0, 0.0))
    def bark_rule(n, p):
        band = int(p.z / (h * 0.09)) % 2
        return shade(C["bark_l"] if band else C["bark_m"], 0.74 + 0.30 * (n.z * 0.5 + 0.5))
    rings = []
    for k in range(nseg + 1):
        t = k / nseg
        z = top * t
        r = (0.26 - 0.10 * t) * (h / 9.0) * (1.4 if k == 0 else 1.0)
        c = ax(z)
        rings.append([B.vert((c.x + cos(2 * pi * j / sides) * r, c.y + sin(2 * pi * j / sides) * r, z))
                      for j in range(sides)])
    for k in range(nseg):
        for j in range(sides):
            B.quad(rings[k][j], rings[k][(j + 1) % sides], rings[k + 1][(j + 1) % sides], rings[k + 1][j], bark_rule)
    foot = B.vert((0, 0, -0.05))
    for j in range(sides):
        B.tri(rings[0][(j + 1) % sides], rings[0][j], foot, bark_rule)
    ct = ax(top)
    crown = Vector((ct.x, ct.y, top))

    def frond_rule(n, p):
        up = n.z * 0.5 + 0.5
        tone = C["palm_d"] if up < 0.45 else C["palm_l"]
        return shade(tone, 0.70 + 0.40 * up)

    nf = 6 if lod else fronds
    for i in range(nf):
        a = 2 * pi * i / nf + rng.random() * 0.5
        d = Vector((cos(a), sin(a), 0.0))
        L = h * (0.36 + 0.08 * rng.random())
        lift = 0.35 + 0.25 * rng.random()
        spine = [crown + Vector((0, 0, 0.15)),
                 crown + d * L * 0.5 + Vector((0, 0, L * lift * 0.55)),
                 crown + d * L * 1.0 + Vector((0, 0, L * (lift * 0.30 - 0.22)))]
        perp = Vector((-sin(a), cos(a), 0.0))
        widths = [0.06, L * 0.17, L * 0.05]
        sag = [0.0, L * 0.09, L * 0.05]
        Lft, Ctr, Rgt = [], [], []
        for k in range(3):
            Ctr.append(B.vert(spine[k]))
            Lft.append(B.vert(spine[k] + perp * widths[k] - Vector((0, 0, sag[k]))))
            Rgt.append(B.vert(spine[k] - perp * widths[k] - Vector((0, 0, sag[k]))))
        for k in range(2):
            B.quad(Lft[k], Lft[k + 1], Ctr[k + 1], Ctr[k], frond_rule)
            B.quad(Ctr[k], Ctr[k + 1], Rgt[k + 1], Rgt[k], frond_rule)
            if not lod:   # underside
                B.quad(Ctr[k], Ctr[k + 1], Lft[k + 1], Lft[k], frond_rule)
                B.quad(Rgt[k], Rgt[k + 1], Ctr[k + 1], Ctr[k], frond_rule)
    if not lod:
        for i in range(3):
            a = rng.random() * 2 * pi
            octa(B, crown + Vector((cos(a) * 0.22, sin(a) * 0.22, -0.18)), 0.14,
                 lambda n, p: shade(C["bark_m"], 0.8 + 0.3 * (n.z * 0.5 + 0.5)), seed=seed + i, squash=0.9)
    return B.emit(name, coll)


# ------------------------------------------------------------------ cliff ---
# A cliff is shards stood on end and leaned together. Nothing else. The same
# builder as the boulder and the ore host, taller: that is what makes the
# rock on an island read as one material from the scree to the headland.

def build_cliff(name, coll, w=14.0, h=9.0, seed=21, n=6):
    rng = random.Random(seed)
    B = Build()
    def stone_rule(n_, p):
        up = n_.z * 0.5 + 0.5
        tone = C["stone_d"] if up < 0.40 else C["stone_m"] if up < 0.78 else C["stone_l"]
        return shade(tone, 0.72 + 0.28 * up + 0.10 * min(1.0, p.z / h))
    for i in range(n):
        t = (i + 0.5) / n
        x = -w / 2 + w * t + (rng.random() - 0.5) * w / n
        hz = h * (0.55 + 0.45 * rng.random()) * (1.0 - 0.5 * abs(t - 0.5))
        bm = shard(w / n * (0.9 + 0.5 * rng.random()), 1.6 + 1.4 * rng.random(), hz, 12, seed * 7 + i,
                   tilt=((rng.random() - 0.5) * 0.25, (rng.random() - 0.5) * 0.25, rng.random() * 0.5),
                   origin=(x, (rng.random() - 0.5) * 1.5, hz * 0.55))
        B.add_bmesh(bm, stone_rule)
        bm.free()
    return B.emit(name, coll)


# -------------------------------------------------------------------- ore ---

def shard(rx, ry, rz, n, seed, tilt=(0, 0, 0), origin=(0, 0, 0), planar_deg=24.0,
          bury=0.0, cuts=()):
    """A convex lump with big flat faces: random points on an ellipsoid,
    hulled, then near-coplanar faces merged so the rock reads as planes and
    edges rather than as a pebble."""
    rng = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        u = rng.random() * 2 - 1
        a = rng.random() * 2 * pi
        s = math.sqrt(max(0.0, 1 - u * u))
        k = 0.82 + 0.18 * rng.random()
        bm.verts.new((s * cos(a) * rx * k, s * sin(a) * ry * k, u * rz * k))
    bmesh.ops.convex_hull(bm, input=bm.verts[:])
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(planar_deg),
                             verts=bm.verts[:], edges=bm.edges[:])
    M = Euler(tilt).to_matrix().to_4x4()
    M.translation = Vector(origin)
    bm.transform(M)
    # Cuts, in world space: a vertex colour can only change where there is
    # a vertex, and a hull face two metres across has none in the middle.
    # Scoring the hull along the vein plane gives the stain an edge to live
    # on -- a vein for about twenty triangles, where subdividing the whole
    # rock would cost two hundred.
    for co, no in cuts:
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                               plane_co=Vector(co), plane_no=Vector(no), dist=1e-4)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    for v in bm.verts:
        if v.co.z < -bury:
            v.co.z = -bury - 0.08 * rng.random()
    return bm


def octa(B, c, r, rule, seed=0, squash=0.8):
    rng = random.Random(seed)
    rot = Euler((rng.random() * 0.8, rng.random() * 0.8, rng.random() * pi)).to_matrix()
    pts = [Vector((r, 0, 0)), Vector((0, r, 0)), Vector((-r, 0, 0)), Vector((0, -r, 0)),
           Vector((0, 0, r * squash)), Vector((0, 0, -r * squash))]
    ids = [B.vert(Vector(c) + rot @ p) for p in pts]
    for j in range(4):
        B.tri(ids[j], ids[(j + 1) % 4], ids[4], rule)
        B.tri(ids[(j + 1) % 4], ids[j], ids[5], rule)


def build_ore(name, coll, seed=7):
    rng = random.Random(seed)
    B = Build()
    # two shards, the second leaning on the first: the crease between them
    # is where the ore is
    # Fewer hull points = bigger, less regular faces. Twenty points made a
    # twenty-sided die; thirteen makes a broken block.
    # The vein: one plane through the whole outcrop. Every shard is scored
    # along it (three parallel cuts bound the band) and the nodules and the
    # stain both hang off the same plane, so they agree by construction.
    vn = Vector((0.45, 0.80, 0.40)).normalized()
    vc = Vector((0.35, -0.25, 0.90))
    VEIN_W = 0.30
    cuts = [(vc, vn), (vc + vn * VEIN_W, vn), (vc - vn * VEIN_W, vn)]
    s1 = shard(1.85, 1.05, 1.25, 13, seed, tilt=(0.15, -0.28, 0.4), origin=(0, 0, 0.95), cuts=cuts)
    s2 = shard(1.10, 0.75, 1.15, 11, seed + 1, tilt=(0.45, 0.60, 1.3), origin=(1.05, -0.55, 0.62), cuts=cuts)
    # a spall: a flat slab that has come off the front, so the outcrop is
    # a broken thing and not two dice leaning together
    s3 = shard(0.95, 0.65, 0.42, 10, seed + 2, tilt=(0.20, 0.12, 0.8), origin=(-0.85, -1.05, 0.22), cuts=cuts)
    # The ore is a VEIN: a plane cut through both shards. Every hull vertex
    # within a hand's breadth of that plane gets a nodule pushed just proud
    # of the surface, so the vein wraps the rock the way a real one does
    # and the stain (below) follows it. Found from the geometry that exists,
    # not typed as coordinates -- the first cut typed them and every one
    # landed INSIDE the rock.
    # Candidates are face centres and edge midpoints, not just hull corners:
    # a twelve-vertex hull has two corners near any plane, and two nodules
    # is a rock with two dots on it.
    cands = []
    for bm in (s1, s2, s3):
        bm.faces.ensure_lookup_table()
        seen = set()
        for f in bm.faces:
            pts = [f.calc_center_median()] + [(e.verts[0].co + e.verts[1].co) * 0.5 for e in f.edges]
            for p in pts:
                key = (round(p.x, 2), round(p.y, 2), round(p.z, 2))
                if key in seen:
                    continue
                seen.add(key)
                if abs((p - vc).dot(vn)) < VEIN_W * 0.7 and p.z > 0.25 and f.normal.z > -0.15:
                    cands.append((p.copy(), f.normal.copy()))
    rng.shuffle(cands)
    nodes = []
    for p, out in cands[:9]:
        r = 0.17 + 0.11 * rng.random()
        nodes.append((p + out * r * 0.30, r))
        # a second, smaller nodule beside each: ore comes in clusters
        if rng.random() < 0.5:
            side = out.cross(Vector((0, 0, 1))).normalized() * (r * 1.5)
            nodes.append((p + out * r * 0.25 + side, r * 0.6))
    if not nodes:
        nodes.append((Vector((0.9, -0.3, 1.2)), 0.16))

    def stone_rule(n, p):
        up = n.z * 0.5 + 0.5
        tone = C["stone_d"] if up < 0.40 else C["stone_m"] if up < 0.78 else C["stone_l"]
        f = 0.74 + 0.26 * up + 0.06 * min(1.0, p.z / 2.2)
        col = shade(tone, f)
        # rust bloom: feathered stain around the vein, per corner -- the one
        # thing a per-face swatch can never do
        # Stain reach is 3.5 nodule radii and falls off fast: at 5 radii the
        # whole face went orange and the rock read as painted, not stained.
        # the vein band itself, then a softer halo round each nodule
        band = abs((p - vc).dot(vn)) / VEIN_W
        if band < 1.0:
            k = (1.0 - band) ** 0.7
            col = mix(col, C["rust_d"] if k > 0.5 else C["rust"], 0.90 * k)
        d = min((p - q).length / (r * 3.0) for q, r in nodes)
        if d < 1.0:
            col = mix(col, C["rust"], 0.6 * (1.0 - d) ** 1.5)
        return col

    B.add_bmesh(s1, stone_rule)
    B.add_bmesh(s2, stone_rule)
    B.add_bmesh(s3, stone_rule)
    s1.free(); s2.free(); s3.free()

    def ore_rule(n, p):
        up = n.z * 0.5 + 0.5
        return mix(C["hematite"], C["rust"], 0.25 + 0.55 * up)

    for i, (p, r) in enumerate(nodes):
        octa(B, p, r, ore_rule, seed=seed * 3 + i, squash=0.75)
    # loose ore that weathered out, at the foot on the downhill side
    for i in range(4):
        a = -0.9 + rng.random() * 1.6
        d = 1.9 + 0.7 * rng.random()
        r = 0.17 + 0.10 * rng.random()
        octa(B, (cos(a) * d, sin(a) * d, r * 0.55), r, ore_rule, seed=seed * 5 + i, squash=0.6)
    return B.emit(name, coll)


# --------------------------------------------------------------- staging ----

def get_coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def clear():
    c = bpy.data.collections.get(COLL)
    if c:
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0 and m.name.startswith(("S2_", "V2_")):
            bpy.data.meshes.remove(m)


def box(name, coll, w, d, h, col, at):
    B = Build()
    x, y = w / 2, d / 2
    ids = [B.vert((sx * x, sy * y, z)) for z in (0, h) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    rule = lambda n, p: shade(col, 0.8 + 0.3 * (n.z * 0.5 + 0.5))
    B.quad(ids[0], ids[3], ids[2], ids[1], rule)
    B.quad(ids[4], ids[5], ids[6], ids[7], rule)
    for j in range(4):
        jn = (j + 1) % 4
        B.quad(ids[j], ids[jn], ids[4 + jn], ids[4 + j], rule)
    ob = B.emit(name, coll)
    ob.location = at
    return ob


def linked(src, name, coll, at, yaw=0.0, s=1.0):
    ob = bpy.data.objects.new(name, src.data)
    coll.objects.link(ob)
    ob.location = at
    ob.rotation_euler = (0, 0, yaw)
    ob.scale = (s, s, s)
    return ob


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def build_all():
    clear()
    coll = get_coll(COLL)
    # ground: olive, a 400 m slab so the grove has a horizon under it
    B = Build()
    s = 200.0
    g = [B.vert((sx * s, sy * s, 0)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    B.quad(g[0], g[1], g[2], g[3], lambda n, p: C["grass"])
    ground = B.emit("V2_Ground", coll)
    ground.location = ORIGIN

    y = ORIGIN.y
    made = {}
    made["ruler"] = box("V2_Ruler", coll, 0.45, 0.30, 1.7, C["ruler"], (-22.0, y, 0))
    sp = build_spruce("S2_Spruce_A", coll, h=13.0, tiers=7, sides=6, crown=0.165, seed=3)
    sp.location = (-8.0, y, 0)
    lod = build_spruce("S2_Spruce_A_LOD1", coll, h=13.0, tiers=4, sides=5, crown=0.165, seed=3)
    lod.location = (2.0, y, 0)
    old = bpy.data.objects.get("R_Tree_Pine_A")
    if old:
        linked(old, "V2_old_pine", coll, (-16.0, y, 0))
    ore = build_ore("S2_Ore_Iron_A", coll, seed=7)
    ore.location = (16.0, y, 0)
    oldore = bpy.data.objects.get("R_Iron_Outcrop")
    if oldore:
        linked(oldore, "V2_old_ore", coll, (10.0, y, 0))
    # second row, to the east of the first so neither shot sees the other
    y2 = y
    oldbroad = bpy.data.objects.get("R_Tree_Broad_A")
    if oldbroad:
        linked(oldbroad, "V2_old_broad", coll, (34.0, y2, 0))
    br = build_broadleaf("S2_Broad_A", coll, h=10.5, seed=5)
    br.location = (43.0, y2, 0)
    brl = build_broadleaf("S2_Broad_A_LOD1", coll, h=10.5, seed=5, pts=16, lod=True)
    brl.location = (52.0, y2, 0)
    pm = build_palm("S2_Palm_A", coll, h=9.0, seed=9)
    pm.location = (60.0, y2, 0)
    pml = build_palm("S2_Palm_A_LOD1", coll, h=9.0, seed=9, lod=True)
    pml.location = (67.0, y2, 0)
    cl = build_cliff("S2_Cliff_A", coll, w=14.0, h=9.0, seed=21)
    cl.location = (82.0, y2 + 3.0, 0)
    made.update(spruce=sp, spruce_lod=lod, broad=br, broad_lod=brl, palm=pm, palm_lod=pml, cliff=cl, ore=ore)
    # a grove: the style is judged as a MASS, because that is how an island
    # is seen. 6 x 6 at 5.5 m, on a low mound, all leaning the same way.
    rng = random.Random(11)
    for i in range(6):
        for j in range(6):
            x = -14 + i * 5.5 + (rng.random() - 0.5) * 2.4
            yy = y + 40 + j * 5.5 + (rng.random() - 0.5) * 2.4
            d = math.hypot(x, yy - (y + 54))
            z = 7.0 * math.exp(-(d / 15.0) ** 2)
            linked(sp, "V2_grove_%02d" % (i * 6 + j), coll, (x, yy, z),
                   yaw=(rng.random() - 0.5) * 0.7, s=0.82 + 0.36 * rng.random())
    # a few shards as boulders among them, so rock and wood are judged together
    for k in range(5):
        Bb = Build()
        bm = shard(1.2 + rng.random(), 0.9 + rng.random() * 0.8, 0.7 + rng.random() * 0.6, 16, 100 + k,
                   tilt=(rng.random() * 0.4, rng.random() * 0.4, rng.random() * 3), origin=(0, 0, 0.35))
        Bb.add_bmesh(bm, lambda n, p: shade(C["stone_d"] if n.z < -0.2 else C["stone_m"] if n.z < 0.55 else C["stone_l"],
                                            0.8 + 0.3 * (n.z * 0.5 + 0.5)))
        bm.free()
        ob = Bb.emit("S2_Boulder_%d" % k, coll)
        x = -18 + rng.random() * 36
        yy = y + 34 + rng.random() * 8
        ob.location = (x, yy, 0.3)
    print("TRIS spruce %d/%d  broad %d/%d  palm %d/%d  cliff %d  ore %d  | old pine %d  old ore %d" % (
        tris(sp), tris(lod), tris(br), tris(brl), tris(pm), tris(pml), tris(cl), tris(ore),
        tris(old) if old else -1, tris(oldore) if oldore else -1))
    return made


# ------------------------------------------------------------------ shoot ---

def shoot(path, loc, target, lens=40, w=1800, h=900, only=(COLL,)):
    scn = bpy.context.scene
    lc = bpy.context.view_layer.layer_collection.children
    for c in lc:
        c.exclude = c.name not in only
    cam = bpy.data.objects.get("ReviewCam")
    if cam is None:
        cam = bpy.data.objects.new("ReviewCam", bpy.data.cameras.new("ReviewCam"))
        scn.collection.objects.link(cam)
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    cam.data.clip_end = 5000
    scn.camera = cam
    scn.render.resolution_x, scn.render.resolution_y = w, h
    scn.render.resolution_percentage = 100
    scn.render.filepath = path
    scn.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    for c in lc:
        c.exclude = False


def shoot_all():
    y = ORIGIN.y
    shoot(OUT + "/v2_row.png", (0, y - 46, 7), (0, y, 5.5), lens=36, w=2000, h=900)
    shoot(OUT + "/v2_ore.png", (14.5, y - 9, 3.2), (16.3, y, 1.0), lens=50, w=1400, h=900)
    shoot(OUT + "/v2_grove_deck.png", (0, y - 90, 3.0), (0, y + 54, 9), lens=45, w=1800, h=900)
    shoot(OUT + "/v2_grove_air.png", (60, y - 40, 70), (0, y + 52, 6), lens=40, w=1800, h=1000)
    shoot(OUT + "/v2_row2.png", (60, y - 48, 7), (60, y, 5.5), lens=36, w=2000, h=900)


# ----------------------------------------------------------------- kit ------
# The set Unity stamps from. Every template is built AT THE ORIGIN, origin on
# the ground, +Z up, in metres -- the FBX exporter turns that into Unity's
# Y-up and Unity's `SceneryKit` reads the vertices straight out of the mesh.
# Boulders and cliffs are UNIT shards (about 1 m) so Unity can scale them to
# the metres of proud rock at the spot; trees are their real size.

KIT_COLL = "KIT_V2"
KIT_FBX = "/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/Resources/Flora/seasick_flora.fbx"


def build_unit_shard(name, coll, seed, tall=False):
    B = Build()
    rule = lambda n, p: shade(C["stone_d"] if n.z < -0.2 else C["stone_m"] if n.z < 0.55 else C["stone_l"],
                              0.78 + 0.30 * (n.z * 0.5 + 0.5))
    rng = random.Random(seed)
    if tall:
        bm = shard(1.0, 0.65 + 0.2 * rng.random(), 1.0, 12, seed,
                   tilt=((rng.random() - 0.5) * 0.2, (rng.random() - 0.5) * 0.2, rng.random() * pi),
                   origin=(0, 0, 0.62), bury=0.35)
    else:
        bm = shard(1.0, 0.75 + 0.35 * rng.random(), 0.72, 12, seed,
                   tilt=(rng.random() * 0.4, rng.random() * 0.4, rng.random() * pi),
                   origin=(0, 0, 0.42), bury=0.25)
    B.add_bmesh(bm, rule)
    bm.free()
    return B.emit(name, coll)


def build_kit():
    c = bpy.data.collections.get(KIT_COLL)
    if c:
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    coll = get_coll(KIT_COLL)
    kit = [
        build_spruce("Spruce", coll, h=13.0, tiers=7, sides=6, crown=0.165, seed=3),
        build_spruce("Spruce_LOD1", coll, h=13.0, tiers=3, sides=5, crown=0.165, seed=3,
                     trunk_sides=4, trunk_segs=2),
        build_broadleaf("Broad", coll, h=10.5, seed=5),
        build_broadleaf("Broad_LOD1", coll, h=10.5, seed=5, pts=16, lod=True),
        build_palm("Palm", coll, h=9.0, seed=9),
        build_palm("Palm_LOD1", coll, h=9.0, seed=9, lod=True),
        build_ore("Ore", coll, seed=7),
    ]
    for i in range(4):
        kit.append(build_unit_shard("Boulder_%d" % i, coll, 400 + i))
    for i in range(3):
        kit.append(build_unit_shard("Cliff_%d" % i, coll, 500 + i, tall=True))
    print("KIT " + "  ".join("%s %d" % (o.name, tris(o)) for o in kit))
    return kit


def export_kit(kit, path=KIT_FBX):
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    vl = bpy.context.view_layer
    for o in bpy.data.objects:
        o.select_set(False)
    for o in kit:
        o.select_set(True)
    vl.objects.active = kit[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        use_mesh_modifiers=True, mesh_smooth_type='FACE',
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True, axis_forward='-Z', axis_up='Y',
        use_triangles=True, add_leaf_bones=False, path_mode='COPY',
        colors_type='SRGB')
    print("exported", path)


# Exec'd from seasick_island_v2.py with SS_NO_AUTORUN set: it wants the
# builders, not the contact sheet.
if not globals().get("SS_NO_AUTORUN"):
    build_all()
    shoot_all()
