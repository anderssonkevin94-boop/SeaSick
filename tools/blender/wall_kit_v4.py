# SeaSick wooden wall kit v4: v3 runtime pieces, thicker planks/rails/posts for the distance read - recreated from the ChatGPT concept sheet.
# 2 m tile. Pivot at ground centre. One vertex-coloured material.
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

TILE = 2.0
BEV_WOOD = 0.035   # one chamfer for every wooden edge
BEV_METAL = 0.016  # one chamfer for every iron edge
LOD = 0   # 1 = cheap far version: no rivets, 4-sided rope, square stakes, sharp rails

def lin(c):  # sRGB 0-255 -> linear 0-1
    return tuple(((v / 255.0) ** 2.2) for v in c)

PAL = {
    "wood_light": (196, 158, 118),
    "wood_med":   (150, 106, 72),
    "wood_dark":  (112, 76, 50),
    "metal":      (66, 69, 74),
    "rivet":      (120, 124, 130),
    "rope":       (196, 162, 112),
    "rope_dark":  (150, 112, 68),
}

# ---------------------------------------------------------------- scene
def wipe():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)

def material():
    m = bpy.data.materials.get("M_WallKit") or bpy.data.materials.new("M_WallKit")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m

# ---------------------------------------------------------------- bmesh parts
# Every part is appended to a shared bmesh with a per-face colour tag.
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.col = {}  # face -> linear rgb

    def _tag(self, faces, rgb, shade_bottom=True):
        for f in faces:
            self.col[f] = rgb

    def add(self, bm_part, rgb, xform=Matrix.Identity(4), alt=None):
        bm_part.transform(xform)
        me = bpy.data.meshes.new("tmp")
        bm_part.to_mesh(me)
        bm_part.free()
        before = set(self.bm.faces)
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        self.bm.faces.ensure_lookup_table()
        new = [f for f in self.bm.faces if f not in before]
        for f in new:
            self.col[f] = alt if (alt is not None and f.material_index == 1) else rgb
            f.material_index = 0

    def finish(self, name, mat, ao_height=0.9, ao_min=0.62):
        me = bpy.data.meshes.new(name)
        bm = self.bm
        # merge colours into a face-corner attribute with a baked ground AO + top lift
        faces = list(bm.faces)
        cols = [self.col.get(f, (1, 0, 1)) for f in faces]
        bm.to_mesh(me)
        attr = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        zmax = max(v.co.z for v in me.vertices) or 1.0
        for poly, rgb in zip(me.polygons, cols):
            nz = poly.normal.z
            for li in poly.loop_indices:
                z = me.vertices[me.loops[li].vertex_index].co.z
                ao = ao_min + (1 - ao_min) * min(1.0, max(0.0, z / ao_height))
                lift = 1.0 + 0.10 * max(0.0, nz)          # up-facing faces a touch brighter
                top = 1.0 + 0.06 * (z / zmax)
                k = ao * lift * top
                attr.data[li].color = (min(1, rgb[0] * k), min(1, rgb[1] * k), min(1, rgb[2] * k), 1.0)
        bm.free()
        for p in me.polygons:
            p.use_smooth = False
        me.materials.append(mat)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob

def jitter(rgb, rnd, amt=0.07):
    k = 1 + rnd.uniform(-amt, amt)
    return tuple(min(1, c * k) for c in rgb)

def box(sx, sy, sz, bevel=0.0, z0=True):
    if LOD == 1:
        bevel = 0.0
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    if z0:
        bmesh.ops.translate(bm, vec=(0, 0, sz / 2), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1,
                        affect='EDGES', clamp_overlap=True, profile=0.5)
    return bm

def stake(w, d, h, tip, rnd, lean_tip=0.03):
    """Square stake with a 4-sided point; every edge chamfered with BEV_WOOD."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(w, d, h), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0, h / 2), verts=bm.verts)
    top = [f for f in bm.faces if f.normal.z > 0.9][0]
    apex = bmesh.ops.poke(bm, faces=[top])["verts"][0]
    apex.co.z += tip
    apex.co.x += rnd.uniform(-lean_tip, lean_tip)
    apex.co.y += rnd.uniform(-lean_tip, lean_tip) * 0.5
    if LOD == 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=BEV_WOOD, segments=1,
                        affect='EDGES', clamp_overlap=True)
    bm.normal_update()
    return bm

def pyramid_cap(half, h_base, tip):
    """Post cap: a low block with a 4-sided point, chamfered like everything else."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(half * 2, half * 2, h_base), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0, h_base / 2), verts=bm.verts)
    top = [f for f in bm.faces if f.normal.z > 0.9][0]
    bmesh.ops.poke(bm, faces=[top])["verts"][0].co.z += tip
    if LOD == 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=BEV_WOOD, segments=1,
                        affect='EDGES', clamp_overlap=True)
    bm.normal_update()
    return bm

def rivet(r=0.045, h=0.026):
    bm = bmesh.new()
    if LOD == 1:
        return bm
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=r, radius2=r * 0.55, depth=h)
    bmesh.ops.translate(bm, vec=(0, 0, h / 2), verts=bm.verts)
    return bm

def twisted_ring(R, r, segs=20, sides=4, twists=5.0, squash=1.0):
    """Torus with a square section that rotates along the ring -> reads as rope."""
    bm = bmesh.new()
    rings = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ca, sa = math.cos(a), math.sin(a)
        rot = 2 * math.pi * twists * i / segs
        ring = []
        for j in range(sides):
            b = rot + 2 * math.pi * j / sides
            rr = R + r * math.cos(b)
            ring.append(bm.verts.new((rr * ca, rr * sa, r * math.sin(b) * squash)))
        rings.append(ring)
    for i in range(segs):
        a, b = rings[i], rings[(i + 1) % segs]
        # rotate index of next ring so the twist wraps cleanly
        for j in range(sides):
            bm.faces.new((a[j], a[(j + 1) % sides], b[(j + 1) % sides], b[j]))
    bm.normal_update()
    return bm

def square_rope(half, r, z, sides=5, pitch=None, slant=0.55):
    if LOD == 1:
        sides, pitch = 4, r * 2.8
    """Rope lashing round a square post: a tube resampled evenly along a rounded
    square, alternating fat coil / pinched groove; each groove ring is tilted so the
    strands read diagonal, like laid rope."""
    pitch = pitch or r * 1.7
    hw = half + r * 0.55
    cr = r * 1.8
    dense = []
    for k in range(4):
        cx = (hw - cr) * (1 if k in (0, 3) else -1)
        cy = (hw - cr) * (1 if k in (0, 1) else -1)
        a0 = k * math.pi / 2
        for i in range(9):
            a = a0 + (math.pi / 2) * i / 8
            dense.append(Vector((cx + cr * math.cos(a), cy + cr * math.sin(a), 0)))
    dense.append(dense[0].copy())
    L = [0.0]
    for i in range(1, len(dense)):
        L.append(L[-1] + (dense[i] - dense[i - 1]).length)
    total = L[-1]
    n = max(8, int(round(total / pitch)))
    n += n % 2
    pts = []
    j = 0
    for i in range(n):
        t = total * i / n
        while L[j + 1] < t:
            j += 1
        f = (t - L[j]) / max(1e-9, L[j + 1] - L[j])
        pts.append(dense[j].lerp(dense[j + 1], f))
    bm = bmesh.new()
    rings = []
    up = Vector((0, 0, 1))
    for i, p in enumerate(pts):
        tng = (pts[(i + 1) % n] - pts[i - 1]).normalized()
        side = tng.cross(up).normalized()
        groove = (i % 2 == 1)
        rad = r * (0.66 if groove else 1.0)
        ring = []
        for k in range(sides):
            b = 2 * math.pi * k / sides + math.pi / 2
            cb, sb = math.cos(b), math.sin(b)
            off = side * (rad * cb) + up * (rad * sb)
            if groove:
                off += tng * (slant * r * sb)    # tilt the groove -> diagonal strand
            ring.append(bm.verts.new(p + off + Vector((0, 0, z))))
        rings.append(ring)
    for i in range(n):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(sides):
            bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]))
    bm.normal_update()
    return bm

def T(x=0, y=0, z=0, rz=0, rx=0, ry=0):
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(rz, 4, 'Z')
            @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X'))

# ---------------------------------------------------------------- runtime contract
# Matches the game's palisade adapter (Scripts/World/WallVisual.cs) and the old
# kit's frame (art-staging/palisade-astra-lvl1-v2/README.md), so the existing
# Resources/Palisade wrappers keep working:
#   Blender +X along the wall, +Y = rear/rail side (the game turns it to face
#   the camp), +Z up; every module's root at its START end on the ground.
#   Stake centres at 0.125 + i*0.25; rails end exactly at x = 0 and x = length.
#   Post: root at its start edge, centre marker at (0.2, 0, 0).
#   Gate: 3 m, own posts centred at x = 0.2 / 2.8, hinges Gate_Hinge /
#   Gate_Hinge_Right (swing about local Z, outward toward -Y), Gate__Passage.
PITCH = 0.25
EMBED = 0.22          # below-ground length: pieces are only sunk, never tilted (slope cap 30 deg)
POST_HALF = 0.38      # 0.76 m square: must enclose the run's full depth (stake + rail) at every node
POST_C = 0.2          # post centre, measured from its root (the old wrapper's offset)
STAKE_W, STAKE_D = PITCH * 1.02, 0.40   # overlap 2.5 mm so no seam shows daylight; chamfers make the grooves
STAKE_H = [1.80, 1.72, 1.86, 1.76, 1.83, 1.70, 1.85, 1.75]   # body tops; tips add ~0.30
RAIL_Z = (0.50, 1.28)
RAIL_H, RAIL_D = 0.30, 0.16
RAIL_Y = STAKE_D / 2 + RAIL_D / 2 + 0.005                    # rear (+Y) face, inside the post
ROPE_Z, ROPE_R = 1.52, 0.078                                  # outside lashing
POST_H = 2.20        # shaft top; cap brings it to ~2.6 m (the game's wall collider)

def rail(length, rnd):
    """Rail with only its long edges chamfered: square ends butt invisibly at every piece seam."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(length, RAIL_D, RAIL_H), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(length / 2, 0, 0), verts=bm.verts)
    if LOD == 0:
        long_edges = [e for e in bm.edges if abs(e.verts[0].co.x - e.verts[1].co.x) > 1e-4]
        bmesh.ops.bevel(bm, geom=long_edges, offset=BEV_WOOD, segments=1, affect='EDGES', clamp_overlap=True)
    return bm

def line_rope(x0, x1, y, z, r, pitch=0.125, sides=5, slant=0.55):
    """Straight laid rope along X, fat at every 0.125 so it continues across piece seams. Open ends."""
    n = max(1, round((x1 - x0) / pitch))
    bm = bmesh.new()
    rings = []
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        groove = (i % 2 == 1)
        rad = r * (0.66 if groove else 1.0)
        ring = []
        for k in range(sides):
            b = 2 * math.pi * k / sides + math.pi / 2
            dx = slant * r * math.sin(b) if groove else 0.0
            ring.append(bm.verts.new((x + dx, y + rad * math.cos(b), z + rad * math.sin(b))))
        rings.append(ring)
    for i in range(n):
        a, b = rings[i], rings[i + 1]
        for k in range(sides):
            bm.faces.new((a[k], b[k], b[(k + 1) % sides], a[(k + 1) % sides]))
    # capped ends: hidden inside the neighbour on a flat seam, clean where a slope steps the wall
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bm.normal_update()
    return bm

def strap(x, z, rnd, B):
    """Iron strap wrapping a rail, one rivet on the rear face."""
    B.add(box(0.15, RAIL_D + 0.05, RAIL_H + 0.06, bevel=BEV_METAL, z0=False), lin(PAL["metal"]), T(x, RAIL_Y, z))
    B.add(rivet(), lin(PAL["rivet"]), T(x, RAIL_Y + RAIL_D / 2 + 0.025, z, rx=-math.pi / 2))

# Log widths per piece. The game only needs each piece to be exactly 1 / 0.5 / 0.25 m long;
# what is inside is free, so a metre is three chunky logs of mixed width, not four pickets.
WIDTHS = {
    (1.0, 0): [0.34, 0.31, 0.35],
    (1.0, 1): [0.30, 0.37, 0.33],
    (1.0, 2): [0.36, 0.32, 0.32],
    (0.5, 0): [0.26, 0.24], (0.5, 1): [0.26, 0.24], (0.5, 2): [0.24, 0.26],
    (0.25, 0): [0.25], (0.25, 1): [0.25], (0.25, 2): [0.25],
}
# The three 1 m variants repeat along every wall (the game picks A/B/C by a position hash),
# so their nine logs share out one spread of tones, dark to pale, shuffled once: any three
# neighbours look random, and no variant carries "the dark one" in a fixed slot.
_strata = [0.02, 0.14, 0.27, 0.40, 0.52, 0.63, 0.75, 0.87, 1.0]
random.Random(1234).shuffle(_strata)
TONE_U = {(1.0, v): _strata[v * 3:(v + 1) * 3] for v in range(3)}

def tone_at(u, rnd):
    d, m, l = lin(PAL["wood_dark"]), lin(PAL["wood_med"]), lin(PAL["wood_light"])
    c = tuple(a + (b - a) * min(u, 1.0) for a, b in zip(d, m))
    if u >= 0.99:                                 # the pale, weathered one
        c = tuple(a + (b - a) * 0.3 for a, b in zip(c, l))
    return jitter(c, rnd, 0.03)

def wood_tone(rnd):
    """A random log colour on the dark..medium wood range (no fixed pattern): most logs
    sit in the middle, some go dark, a few are sun-bleached toward the light wood."""
    d, m, l = lin(PAL["wood_dark"]), lin(PAL["wood_med"]), lin(PAL["wood_light"])
    u = rnd.betavariate(2.0, 1.5)                # 0 = dark, 1 = medium, skewed to medium
    c = tuple(a + (b - a) * u for a, b in zip(d, m))
    if rnd.random() < 0.12:                      # the odd weathered, paler log
        k = rnd.uniform(0.2, 0.4)
        c = tuple(a + (b - a) * k for a, b in zip(c, l))
    return jitter(c, rnd, 0.04)

def stakes(B, length, variant, rnd, broken=False):
    x0 = 0.0
    for i, w in enumerate(WIDTHS[(length, variant)]):
        x = x0 + w / 2
        x0 += w
        ww = w + 0.005                     # 2.5 mm overlap each side: no daylight at seams
        if broken:
            h = [0.42, 0.20, 0.64, 0.30][(i + variant) % 4]
            bm = stake(ww, STAKE_D, h + EMBED, 0.0001, rnd, lean_tip=0.0)
            top = [v for v in bm.verts if v.co.z > h + EMBED - 0.05]
            for v in top:
                v.co.z += rnd.uniform(-0.05, 0.14)
        else:
            h = STAKE_H[(i + variant * 3) % 8]
            bm = stake(ww, STAKE_D, h + EMBED, 0.9 * w + rnd.uniform(-0.02, 0.03), rnd)
        tones = TONE_U.get((length, variant))
        B.add(bm, tone_at(tones[i], rnd) if tones else wood_tone(rnd),
              T(x, rnd.uniform(-0.01, 0.01), -EMBED, rz=rnd.uniform(-0.012, 0.012), ry=rnd.uniform(-0.005, 0.005)))

def run_piece(name, mat, length, variant, seed):
    rnd = random.Random(seed)
    B = Builder()
    n = round(length / PITCH)
    stakes(B, length, variant, rnd)
    for z in RAIL_Z:
        B.add(rail(length, rnd), jitter(tuple((a + b) / 2 for a, b in zip(lin(PAL["wood_light"]), lin(PAL["wood_med"]))), rnd, 0.04),
              T(0, RAIL_Y, z))
    B.add(line_rope(0, length, -(STAKE_D / 2 + ROPE_R * 0.55), ROPE_Z, ROPE_R), lin(PAL["rope"]))
    # character per variant: A = iron straps, B = plain, C = one strap on the top rail
    if length >= 1.0 and LOD == 0:
        if variant == 0:
            for z in RAIL_Z:
                strap(0.5, z, rnd, B)
        elif variant == 2:
            strap(0.5, RAIL_Z[1], rnd, B)
    return B.finish(name, mat, ao_height=0.9)

def breach_piece(name, mat):
    """1 m of stumps for the breached middle third; splintered rail stubs; a fallen stake."""
    rnd = random.Random(99)
    B = Builder()
    stakes(B, 1.0, 0, rnd, broken=True)
    for z, (a, b) in zip(RAIL_Z[:1], [(0.0, 0.22)]):
        bm = rail(b - a, rnd)
        B.add(bm, lin(PAL["wood_med"]), T(a, RAIL_Y, z, ry=0.12))
    bm = rail(0.26, rnd)
    B.add(bm, lin(PAL["wood_med"]), T(0.74, RAIL_Y, RAIL_Z[0], ry=-0.15))
    # a snapped stake lying across the gap, outside (-Y), inside the 0..1 span
    fallen = stake(STAKE_W, STAKE_D, 0.62, 0.26, rnd)
    B.add(fallen, jitter(lin(PAL["wood_dark"]), rnd), T(0.18, -0.50, 0.11, rz=0.35, ry=math.pi / 2 - 0.08))
    return B.finish(name, mat, ao_height=0.9)

def post_mesh(B, cx, height, rnd, band=True):
    lw = POST_HALF
    for i, (sx, sy) in enumerate([(-1, -1), (1, -1), (1, 1), (-1, 1)]):
        h = height + EMBED + rnd.uniform(-0.03, 0.03)
        B.add(box(lw * 0.97, lw * 0.97, h, bevel=BEV_WOOD), wood_tone(rnd),
              T(cx + sx * lw / 2, sy * lw / 2, -EMBED))
    B.add(pyramid_cap(POST_HALF + 0.03, 0.08, 0.50), lin(PAL["wood_med"]), T(cx, 0, height - 0.02))
    for z in (0.21, 0.365, height - 0.54, height - 0.385):
        B.add(square_rope(POST_HALF, 0.082, z), lin(PAL["rope"]), T(cx, 0, 0))
    if band:
        bz = height - 0.80
        B.add(box(POST_HALF * 2 + 0.06, POST_HALF * 2 + 0.06, 0.21, bevel=BEV_METAL), lin(PAL["metal"]), T(cx, 0, bz))
        for ang in range(4):
            for dx in (-0.23, 0.23):
                B.add(rivet(), lin(PAL["rivet"]),
                      T(cx, 0, 0) @ T(rz=ang * math.pi / 2) @ T(dx, -(POST_HALF + 0.03), bz + 0.105, rx=math.pi / 2))

def post_piece(name, mat):
    rnd = random.Random(3)
    B = Builder()
    post_mesh(B, POST_C, POST_H, rnd)
    return B.finish(name, mat, ao_height=0.9)

# ---- gate: 3 m, own posts, lintel, two hinged leaves
GATE_L = 3.0
GATE_POST_H = 2.78
LINTEL_Z = 2.36
HINGE_Y = -0.11
LEAF_T = 0.24

def gate_frame(B, rnd, broken=False):
    post_mesh(B, 0.2, GATE_POST_H, rnd)
    post_mesh(B, 2.8, GATE_POST_H, rnd)
    if broken:
        # snapped lintel stub hanging off the left post, splinters at the foot of the right
        bm = rail(0.55, rnd)
        B.add(bm, lin(PAL["wood_med"]), T(0.40, 0, LINTEL_Z - 0.05, ry=0.35))
        for i, x in enumerate((2.30, 2.47)):
            s = stake(STAKE_W, STAKE_D, 0.45 + 0.15 * i, 0.001, rnd, 0)
            B.add(s, jitter(lin(PAL["wood_dark"]), rnd), T(x, -0.05, -EMBED + 0.05))
        return
    # lintel: a chunky beam across the posts, iron-shod ends
    beam = box(GATE_L - 0.02, 0.42, 0.34, bevel=BEV_WOOD, z0=False)
    B.add(beam, lin(PAL["wood_light"]), T(GATE_L / 2, 0, LINTEL_Z + 0.15))
    for x in (0.2, 2.8):
        B.add(box(POST_HALF * 2 + 0.06, 0.48, 0.16, bevel=BEV_METAL, z0=False), lin(PAL["metal"]), T(x, 0, LINTEL_Z + 0.15))
    for x in (1.0, 2.0):
        B.add(rivet(), lin(PAL["rivet"]), T(x, -0.22, LINTEL_Z + 0.15, rx=math.pi / 2))
    # three short points on the lintel give the gate a crown that reads from far away
    for i, x in enumerate((1.0, 1.5, 2.0)):
        s = stake(0.24, 0.30, 0.18, 0.28 if i != 1 else 0.38, rnd, 0)
        B.add(s, lin(PAL["wood_med"]), T(x, 0, LINTEL_Z + 0.28))

def leaf(mat, name, width, rnd, mirror):
    """A leaf in hinge space: hinge at origin, leaf running +X (or -X if mirror), outside face -Y."""
    B = Builder()
    sgn = -1 if mirror else 1
    n = 3                                  # three logs a leaf, like the wall
    pitch = (width - 0.02) / n
    for i in range(n):
        x = sgn * (0.01 + pitch * (i + 0.5))
        h = [1.90, 1.97, 2.04][i] - 0.06
        bm = stake(pitch * 1.0, LEAF_T, h - 0.12, 0.9 * pitch, rnd, 0.0)
        B.add(bm, wood_tone(rnd), T(x, 0, 0.12))
    # rear rails + a diagonal brace, rails on the camp side (+Y), iron straps at the hinge
    y = LEAF_T / 2 + 0.07
    for z in (0.50, 1.40):
        bm = box(width - 0.06, 0.14, 0.26, bevel=BEV_WOOD, z0=False)
        B.add(bm, lin(PAL["wood_light"]), T(sgn * width / 2, y, z))
        st = box(0.34, 0.18, 0.31, bevel=BEV_METAL, z0=False)
        B.add(st, lin(PAL["metal"]), T(sgn * 0.19, y + 0.005, z))
        B.add(rivet(), lin(PAL["rivet"]), T(sgn * 0.26, y + 0.095, z, rx=-math.pi / 2))
    length = math.hypot(width - 0.30, 0.90)
    ang = math.atan2(0.90, width - 0.30)
    br = box(length, 0.13, 0.22, bevel=BEV_WOOD, z0=False)
    B.add(br, lin(PAL["wood_med"]), T(sgn * width / 2, y - 0.005, 0.95, ry=-sgn * ang))
    ob = B.finish(name, mat, ao_height=0.9)
    return ob

def empty(name, loc, parent):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.15
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    return e

def rooted(ob, name, markers):
    ob.name = name + "_Mesh"
    ob.data.name = name + "_Mesh"
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    ob.parent = root
    for k, v in markers.items():
        empty(name + "__" + k, v, root)
    return root

def gate_pieces(mat):
    rnd = random.Random(11)
    B = Builder(); gate_frame(B, rnd)
    root = rooted(B.finish("Gate_L1", mat, ao_height=0.9), "Gate_L1", {})
    for k, v in {"Snap_Start": (0, 0, 0), "Snap_End": (GATE_L, 0, 0), "Passage": (GATE_L / 2, 0, 0)}.items():
        empty("Gate__" + k, v, root)
    hx_l, hx_r = 0.2 + POST_HALF + 0.02, 2.8 - POST_HALF - 0.02
    w = (hx_r - hx_l) / 2 - 0.01
    hl = empty("Gate_Hinge", (hx_l, HINGE_Y, 0), root)
    hr = empty("Gate_Hinge_Right", (hx_r, HINGE_Y, 0), root)
    for hinge, nm, mir in ((hl, "Gate_Leaf_Left", False), (hr, "Gate_Leaf_Right", True)):
        lf = leaf(mat, nm, w, rnd, mir)
        lf.parent = hinge
    rnd = random.Random(12)
    B = Builder(); gate_frame(B, rnd, broken=True)
    root_b = rooted(B.finish("Gate_Breached_3m", mat, ao_height=0.9), "Gate_Breached_3m", {})
    for k, v in {"Snap_Start": (0, 0, 0), "Snap_End": (GATE_L, 0, 0), "Passage": (GATE_L / 2, 0, 0)}.items():
        empty("Gate__" + k + "_Broken", v, root_b)
    return root, root_b

PIECES = {}

def build():
    global LOD
    LOD = 0
    wipe()
    mat = material()
    for nm, L, var, seed in (("Palisade_Run_1m_A", 1.0, 0, 7), ("Palisade_Run_1m_B", 1.0, 1, 8),
                             ("Palisade_Run_1m_C", 1.0, 2, 9), ("Palisade_Filler_050m", 0.5, 1, 10),
                             ("Palisade_Filler_025m", 0.25, 2, 13)):
        PIECES[nm] = rooted(run_piece(nm, mat, L, var, seed), nm, {"Snap_Start": (0, 0, 0), "Snap_End": (L, 0, 0)})
    PIECES["Palisade_Breached_1m"] = rooted(breach_piece("Palisade_Breached_1m", mat), "Palisade_Breached_1m",
                                            {"Snap_Start": (0, 0, 0), "Snap_End": (1, 0, 0)})
    PIECES["Palisade_Post"] = rooted(post_piece("Palisade_Post", mat), "Palisade_Post",
                                     {"Snap_Start": (0, 0, 0), "Snap_End": (0.4, 0, 0), "Post_Center": (POST_C, 0, 0)})
    PIECES["Gate_L1"], PIECES["Gate_Breached_3m"] = gate_pieces(mat)
    return PIECES

def tris(root):
    t = 0
    for o in [root] + list(root.children_recursive):
        if o.type == 'MESH':
            t += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return t

def bounds(root):
    xs, ys, zs = [], [], []
    for o in [root] + list(root.children_recursive):
        if o.type == 'MESH':
            for v in o.data.vertices:
                w = o.matrix_world @ v.co
                xs.append(w.x); ys.append(w.y); zs.append(w.z)
    return (round(min(xs), 3), round(max(xs), 3)), (round(min(ys), 3), round(max(ys), 3)), (round(min(zs), 3), round(max(zs), 3))

if __name__ == "__main__" or True:
    build()
    bpy.context.view_layer.update()
    for k, r in PIECES.items():
        print(k, tris(r), "tris", bounds(r))
