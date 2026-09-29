# SeaSick wooden wall kit v2 (chunkier, one bevel rule) - recreated from the ChatGPT concept sheet.
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

# ---------------------------------------------------------------- assets
POST_HALF = 0.28     # post is ~0.56 m square
RAIL_Z = (0.36, 0.84)

def build_post(name, mat, seed=3, height=1.80):
    rnd = random.Random(seed)
    B = Builder()
    lw = POST_HALF  # each of 4 logs is POST_HALF wide
    logs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for i, (sx, sy) in enumerate(logs):
        h = height + rnd.uniform(-0.03, 0.03)
        bm = box(lw * 0.97, lw * 0.97, h, bevel=BEV_WOOD)
        shade = PAL["wood_med"] if i % 2 == 0 else PAL["wood_dark"]
        B.add(bm, jitter(lin(shade), rnd), T(sx * lw / 2, sy * lw / 2, 0))
    # pyramid cap: chunky, 4-sided, same chamfer as the logs
    B.add(pyramid_cap(POST_HALF + 0.025, 0.07, 0.40), lin(PAL["wood_med"]), T(0, 0, height - 0.02))
    # rope lashings: two at the foot, two near the head
    for z in ((0.19, 0.325, height - 0.50, height - 0.365) if LOD == 0 else (0.25, height - 0.43)):
        rope = square_rope(POST_HALF, 0.072, z)
        B.add(rope, lin(PAL["rope"]))
    # iron band between the upper lashings and the cap region
    bz = height - 0.80
    band = box(POST_HALF * 2 + 0.06, POST_HALF * 2 + 0.06, 0.21, bevel=BEV_METAL)
    B.add(band, lin(PAL["metal"]), T(0, 0, bz))
    for ang in range(4):
        for dx in (-0.17, 0.17):
            rv = rivet()
            m = T(rz=ang * math.pi / 2) @ T(dx, -(POST_HALF + 0.03), bz + 0.105, rx=math.pi / 2)
            B.add(rv, lin(PAL["rivet"]), m)
    return B.finish(name, mat, ao_height=0.9)

def build_segment(name, mat, seed=7, n_stakes=6):
    """2 m run between two post centres (x = -1..1). Posts are separate pieces."""
    rnd = random.Random(seed)
    B = Builder()
    inner = TILE - 2 * POST_HALF + 0.06            # stake span between post faces
    pitch = inner / n_stakes
    sw, sd = pitch * 0.95, 0.24
    for i in range(n_stakes):
        x = -inner / 2 + pitch * (i + 0.5)
        h = 1.22 + rnd.uniform(-0.04, 0.04) + (0.06 if i % 2 == 0 else 0.0)
        tip = 0.30 + rnd.uniform(-0.02, 0.03)
        bm = stake(sw, sd, h, tip, rnd)
        shade = [PAL["wood_med"], PAL["wood_dark"], PAL["wood_med"]][i % 3]
        B.add(bm, jitter(lin(shade), rnd, 0.08),
              T(x, rnd.uniform(-0.01, 0.01), 0, rz=rnd.uniform(-0.04, 0.04), ry=rnd.uniform(-0.02, 0.02)))
    # rails: front and back, lighter wood, run into the posts
    rail_len = TILE - 2 * POST_HALF + 0.12   # tucks 6 cm into each post
    for side in (-1, 1):
        for k, rz in enumerate(RAIL_Z):
            y = side * (sd / 2 + 0.08)
            bm = box(rail_len, 0.16, 0.25, bevel=BEV_WOOD, z0=False)
            B.add(bm, jitter(tuple((a + b) / 2 for a, b in zip(lin(PAL["wood_light"]), lin(PAL["wood_med"]))), rnd, 0.05),
                  T(0, y, rz + rnd.uniform(-0.01, 0.01), ry=rnd.uniform(-0.012, 0.012)))
            # iron straps near each end, wrapping the rail
            for ex in (-1, 1):
                sx = ex * (TILE / 2 - POST_HALF - 0.11)
                st = box(0.16, 0.20, 0.31, bevel=BEV_METAL, z0=False)
                B.add(st, lin(PAL["metal"]), T(sx, y + side * 0.012, rz))
                rv = rivet()
                B.add(rv, lin(PAL["rivet"]),
                      T(sx, y + side * 0.112, rz, rx=-side * math.pi / 2))
    return B.finish(name, mat, ao_height=0.9)

def build():
    global LOD
    wipe()
    mat = material()
    out = []
    for LOD in (0, 1):
        sfx = "" if LOD == 0 else "_LOD1"
        out.append(build_post("Wall_Post" + sfx, mat))
        out.append(build_segment("Wall_Segment_2m_A" + sfx, mat, seed=7))
        out.append(build_segment("Wall_Segment_2m_B" + sfx, mat, seed=21))
    LOD = 0
    return out

if __name__ == "__main__" or True:
    objs = build()
    for o in objs:
        print(o.name, len(o.data.polygons), "faces",
              sum(len(p.vertices) - 2 for p in o.data.polygons), "tris")
