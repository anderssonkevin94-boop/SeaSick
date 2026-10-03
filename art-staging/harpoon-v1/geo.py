# Shared geometry + palette for harpoon-v1 (exec'd by build.py / render.py / check.py).
# Blender metres, Z up, FORWARD = -Y (Unity sees Blender (x,y,z) as (-x, z, -y), so -Y -> Unity +Z = the bow).
import bpy, bmesh, math, json
from mathutils import Vector, Matrix

ROOT = '/Users/kevinandersson/Desktop/SeaSick'
HV = ROOT + '/art-staging/harpoon-v1'

# Palette = the base coaster's own vertex colours (linear, read from art-staging/f-coaster-runtime/kit.json, BowLow):
TIMBER    = (0.58, 0.31, 0.10, 1)   # Cream_Cap / Solid_Timber_Wale
TIMBER_DK = (0.23, 0.09, 0.03, 1)   # Hull / Bulwark planking
DECK      = (0.47, 0.26, 0.12, 1)   # Foredeck_Floor
IRON      = (0.04, 0.05, 0.06, 1)   # Forged_Straps iron / Lantern_Bow_Frame
BRASS     = (0.32, 0.25, 0.13, 1)   # Forged_Straps brass bands
ROPE      = (1.0, 1.0, 1.0, 1)      # rope tile texture carries the colour
SLOT = {'timber': 0, 'rope': 1, 'iron': 2}

# Placement on CoasterFamily.Base (bow module BowLow), bow-local SOURCE units (+X bow, +Y port, +Z up), 0.5 m per unit
MOUNT_SRC_U = (6.3, 0.0, 2.11)
STERN_LEN_U = 9.8           # Base = low stern (9.8 u) + low bow, no middle
def src_to_review(p, bow=True):
    """bow-local source units -> review Blender metres (bow toward -Y, ship stern origin at y=0)."""
    x, y, z = p; x = x + (STERN_LEN_U if bow else 0.0)
    return Vector((y * 0.5, -x * 0.5, z * 0.5))
MOUNT_AT = src_to_review(MOUNT_SRC_U)   # (0, -8.05, 1.055)

# ------------------------------------------------------------------ BowLantern (Kevin 2026-10-04)
# A short chunky beam off the stem with the lantern hanging below its tip, in front of the stem and just under the
# harpoon's line of fire. Authored in the SAME root frame as HarpoonMount (origin = deck on the swivel axis), so the
# game puts both roots at BowLow-local (0, 1.055, 3.15) m. Numbers below are BowLow-local METRES in Unity axes
# (x starboard, y up, z forward), i.e. kit.json's raw frame; L() converts them to the root's Blender frame.
MOUNT_M = (0.0, 1.055, 3.15)
def L(x, y, z): return Vector((-x, -(z - MOUNT_M[2]), y - MOUNT_M[1]))
# Kevin 2026-10-04 (2nd note): "a little block that sticks out from the ship's nose", low on the stem, clearly under
# the line of fire. The muzzle is at (0, 2.775, 3.95); the lowest line (taut, to the sea 10 m dead ahead) is ~2.3 m up
# over the block, which tops out at 2.00 (check.py measures every bearing, range, sag and swing).
BEAM_Z0, BEAM_Z1 = 4.60, 5.30         # root end buried in the stem -> tip (~0.5 m proud of the stem face)
BEAM_TOP = 2.00                        # flat top, well under the stem cap (2.565) and every line of fire
BEAM_W0, BEAM_H0, BEAM_W1, BEAM_H1 = .26, .24, .24, .22   # a chunky block, barely tapered
def beam_h(z):
    t = (z - BEAM_Z0) / (BEAM_Z1 - BEAM_Z0); return BEAM_H0 + (BEAM_H1 - BEAM_H0) * t
def beam_w(z):
    t = (z - BEAM_Z0) / (BEAM_Z1 - BEAM_Z0); return BEAM_W0 + (BEAM_W1 - BEAM_W0) * t
def beam_mid_y(z):
    t = (z - BEAM_Z0) / (BEAM_Z1 - BEAM_Z0); return BEAM_TOP - (BEAM_H0 + (BEAM_H1 - BEAM_H0) * t) / 2
COLLAR_Z = 4.80                        # iron strap round the block where it leaves the stem
PIVOT_U = (0.0, 1.75, 5.18)            # LanternBow_Pivot = chain top, in the iron eye under the tip band
CHAIN_DROP = .12                       # pivot -> top of the lantern's own hook post (two links)
# The kit's own bow lantern (BowLow Lantern_Bow_Frame/_Glass, kit.json), copied unchanged and only translated. Kept:
# the lantern body + its hook post (bbox y >= 2.95 and z <= 4.50); dropped: the deck foot, the post, the knee and the
# gallows arm -- that is the old post the line ran through.
KIT_HOOK_TOP, KIT_LANTERN_Z = 3.737, 4.2785
LANTERN_KEEP_YMIN, LANTERN_KEEP_ZMAX = 2.95, 4.50
LANTERN_SHIFT = (0.0, PIVOT_U[1] - CHAIN_DROP - KIT_HOOK_TOP, PIVOT_U[2] - KIT_LANTERN_Z)
# In the review/check scenes the BowLantern root sits at MOUNT_AT, exactly like the mount.

def kit_part(model, name):
    kit = json.load(open(ROOT + '/art-staging/f-coaster-runtime/kit.json'))
    mo = next(m for m in kit['models'] if m['name'] == model)
    return next(p for p in mo['parts'] if p['name'] == name)

def kit_islands(p):
    """Triangle islands of a kit part (joined by shared indices AND by position: the kit is flat-shaded soup)."""
    v = p['vertices']; t = p['triangles']; n = len(v) // 3; par = list(range(n))
    def f(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    seen = {}
    for i in range(n):
        k = (round(v[3 * i], 4), round(v[3 * i + 1], 4), round(v[3 * i + 2], 4))
        if k in seen: par[f(i)] = f(seen[k])
        else: seen[k] = i
    for i in range(0, len(t), 3):
        a = f(t[i]); par[f(t[i + 1])] = a; par[f(t[i + 2])] = a
    isl = {}
    for i in range(0, len(t), 3): isl.setdefault(f(t[i]), []).append((t[i], t[i + 1], t[i + 2]))
    return list(isl.values())

def add_kit_island(mb, p, tris, shift, slot):
    """Add one kit island to an MB, welded by position, translated by `shift` (metres, Unity axes), in the root's
    Blender frame. Winding = load_kit's (the Unity->Blender mirror flips it); colour = the kit's own vertex colour."""
    v = p['vertices']; c = p['colors']; idx = {}; verts = []; faces = []
    for a, b, d in tris:
        face = []
        for i in (a, d, b):   # mirror flips winding
            k = (round(v[3 * i], 4), round(v[3 * i + 1], 4), round(v[3 * i + 2], 4))
            if k not in idx:
                idx[k] = len(verts); verts.append(L(v[3 * i] + shift[0], v[3 * i + 1] + shift[1], v[3 * i + 2] + shift[2]))
            face.append(idx[k])
        faces.append(tuple(face))
    i0 = tris[0][0]; col = (c[4 * i0], c[4 * i0 + 1], c[4 * i0 + 2], 1)
    mb.add(verts, faces, slot, col)

def island_bbox(p, tris):
    v = p['vertices']; ids = {i for t in tris for i in t}
    return ([min(v[3 * i + k] for i in ids) for k in range(3)], [max(v[3 * i + k] for i in ids) for k in range(3)])


class MB:
    """Mesh builder: faces carry a material slot, a colour and per-corner UVs."""
    def __init__(s): s.v = []; s.f = []; s.uv = []; s.mi = []; s.col = []
    def add(s, verts, faces, slot, color, uvs=None):
        base = len(s.v); s.v += [tuple(v) for v in verts]
        for k, f in enumerate(faces):
            s.f.append(tuple(base + i for i in f)); s.mi.append(SLOT[slot]); s.col.append(color)
            if uvs: s.uv.append(uvs[k])
            else:
                s.uv.append([((verts[i][0] + verts[i][2]) * .5, (verts[i][1] + verts[i][2]) * .5) for i in f])

    def prism(s, a, b, r0, r1=None, n=8, slot='timber', color=TIMBER, rot=0.0, caps=True, sq=1.0):
        """n-gon prism/frustum from a to b (radius r0 at a, r1 at b); sq squashes the local Y radius."""
        r1 = r0 if r1 is None else r1
        a = Vector(a); b = Vector(b); q = (b - a).to_track_quat('Z', 'Y')
        ring = [(math.cos(rot + i * math.tau / n), math.sin(rot + i * math.tau / n) * sq) for i in range(n)]
        v = [a + q @ Vector((x * r0, y * r0, 0)) for x, y in ring] + [b + q @ Vector((x * r1, y * r1, 0)) for x, y in ring]
        f = [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
        if caps: f += [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        s.add(v, f, slot, color)

    def box(s, c, size, slot='timber', color=TIMBER):
        c = Vector(c); hx, hy, hz = (x / 2 for x in size)
        v = [c + Vector((x, y, z)) for z in (-hz, hz) for y in (-hy, hy) for x in (-hx, hx)]
        f = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        s.add(v, f, slot, color)

    def frustum_y(s, y0, y1, w0, h0, w1, h1, zc0, zc1, slot='timber', color=TIMBER):
        """box tapering along Y: section (w0,h0) centred at z=zc0 at y0 -> (w1,h1) at z=zc1 at y1."""
        v = []
        for y, w, h, zc in ((y0, w0, h0, zc0), (y1, w1, h1, zc1)):
            v += [Vector((x, y, zc + z)) for x, z in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
        f = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (3, 2, 1, 0), (4, 5, 6, 7)]
        s.add(v, f, slot, color)

    def torus(s, c, axis, R, r, nR=12, nr=6, slot='rope', color=ROPE, twist=0.0):
        c = Vector(c); q = Vector(axis).normalized().to_track_quat('Z', 'Y')
        v = []; uv_cache = []
        for i in range(nR):
            a = i * math.tau / nR; d = q @ Vector((math.cos(a), math.sin(a), 0)); up = q @ Vector((0, 0, 1))
            for j in range(nr):
                b = j * math.tau / nr + twist; v.append(c + d * (R + r * math.cos(b)) + up * (r * math.sin(b)))
        f = []; uvs = []
        L = math.tau * R
        for i in range(nR):
            for j in range(nr):
                i2 = (i + 1) % nR; j2 = (j + 1) % nr
                f.append((i * nr + j, i2 * nr + j, i2 * nr + j2, i * nr + j2))
                u0 = i / nR * L * 3; u1 = (i + 1) / nR * L * 3
                uvs.append([(u0, j / nr), (u1, j / nr), (u1, (j + 1) / nr), (u0, (j + 1) / nr)])
        s.add(v, f, slot, color, uvs)

    def tube(s, pts, r, n=6, slot='rope', color=ROPE):
        pts = [Vector(p) for p in pts]; v = []
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized(); q = t.to_track_quat('Z', 'Y')
            v += [p + q @ Vector((r * math.cos(j * math.tau / n), r * math.sin(j * math.tau / n), 0)) for j in range(n)]
        f = []; uvs = []; dist = 0.0
        for i in range(len(pts) - 1):
            d2 = dist + (pts[i + 1] - pts[i]).length
            for j in range(n):
                f.append((i * n + j, i * n + (j + 1) % n, (i + 1) * n + (j + 1) % n, (i + 1) * n + j))
                uvs.append([(dist * 3, j / n), (dist * 3, (j + 1) / n), (d2 * 3, (j + 1) / n), (d2 * 3, j / n)])
            dist = d2
        f += [tuple(reversed(range(n))), tuple((len(pts) - 1) * n + j for j in range(n))]; uvs += [[(0, 0)] * n] * 2
        s.add(v, f, slot, color, uvs)

    def sector(s, r0, r1, a0, a1, z0, z1, n=8, slot='timber', color=DECK):
        """annular sector (angles in radians, 0 = +X, CCW)."""
        v = []
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n; c, sn = math.cos(a), math.sin(a)
            v += [Vector((c * r0, sn * r0, z0)), Vector((c * r1, sn * r1, z0)), Vector((c * r0, sn * r0, z1)), Vector((c * r1, sn * r1, z1))]
        f = []
        for i in range(n):
            k = i * 4; m = k + 4
            f += [(k + 2, k + 3, m + 3, m + 2), (k + 0, m + 0, m + 1, k + 1), (k + 1, m + 1, m + 3, k + 3), (k + 0, k + 2, m + 2, m + 0)]
        e = n * 4; f += [(0, 1, 3, 2), (e + 0, e + 2, e + 3, e + 1)]
        s.add(v, f, slot, color)

    def finish(s, name, mats, coll, parent=None, origin=(0, 0, 0), recalc=True):
        """recalc=False keeps the authored winding (the kit's lantern islands: copied, never re-oriented)."""
        o_ = Vector(origin)
        me = bpy.data.meshes.new(name); me.from_pydata([Vector(p) - o_ for p in s.v], [], s.f); me.update()
        for m in mats: me.materials.append(m)
        uv = me.uv_layers.new(name='UVMap'); col = me.color_attributes.new(name='GameColor', type='FLOAT_COLOR', domain='CORNER')
        for p, coords, mi, c in zip(me.polygons, s.uv, s.mi, s.col):
            p.material_index = mi; p.use_smooth = False
            for li, xy in zip(p.loop_indices, coords): uv.data[li].uv = xy; col.data[li].color = c
        if recalc:
            bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free(); me.update()
        ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
        if parent is not None: ob.parent = parent
        ob.location = o_ - (parent.matrix_world.translation if parent is not None else Vector())
        return ob


def load_kit(models=('SternLow', 'BowLow', 'RotorLow'), coll=None, mat=None, skip=()):
    """Build the REAL base-coaster meshes from the game's kit.json (read-only) into review metres:
    kit raw = metres, Unity axes (x starboard, y up, z forward) -> Blender (-x, -z, y); the bow sits 9.8 u (4.9 m) forward."""
    kit = json.load(open(ROOT + '/art-staging/f-coaster-runtime/kit.json'))
    out = []
    for mo in kit['models']:
        if mo['name'] not in models: continue
        dz = STERN_LEN_U * 0.5 if mo['name'].startswith('Bow') else 0.0
        for p in mo['parts']:
            if p['name'] in skip: continue
            vv = p['vertices']; verts = [(-vv[i], -(vv[i + 2] + dz), vv[i + 1]) for i in range(0, len(vv), 3)]
            t = p['triangles']; faces = [(t[i], t[i + 2], t[i + 1]) for i in range(0, len(t), 3)]   # mirror flips winding
            me = bpy.data.meshes.new(mo['name'] + '_' + p['name']); me.from_pydata(verts, [], faces); me.update()
            cc = p['colors']; col = me.color_attributes.new(name='GameColor', type='FLOAT_COLOR', domain='POINT')
            for i in range(len(verts)): col.data[i].color = (cc[i * 4], cc[i * 4 + 1], cc[i * 4 + 2], 1)
            if mat: me.materials.append(mat)
            ob = bpy.data.objects.new(me.name, me)
            (coll or bpy.context.scene.collection).objects.link(ob); out.append(ob)
    return out
