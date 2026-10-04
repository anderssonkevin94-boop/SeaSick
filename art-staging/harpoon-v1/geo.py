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
SLOT = {'timber': 0, 'rope': 1, 'iron': 2, 'lens': 0}   # 'lens' = index 0 of Lamp_Lens's own single slot

# Placement on CoasterFamily.Base (bow module BowLow), bow-local SOURCE units (+X bow, +Y port, +Z up), 0.5 m per unit
MOUNT_SRC_U = (6.3, 0.0, 2.11)
STERN_LEN_U = 9.8           # Base = low stern (9.8 u) + low bow, no middle
def src_to_review(p, bow=True):
    """bow-local source units -> review Blender metres (bow toward -Y, ship stern origin at y=0)."""
    x, y, z = p; x = x + (STERN_LEN_U if bow else 0.0)
    return Vector((y * 0.5, -x * 0.5, z * 0.5))
MOUNT_AT = src_to_review(MOUNT_SRC_U)   # (0, -8.05, 1.055)

# ------------------------------------------------------------------ Gun-lamp (Kevin 2026-10-04), mount frame (Blender metres)
# A hooded bullseye lamp bolted to the barrel's starboard side (Blender -X = Unity +X; the winch crank is on Blender +X = port), lens looking down the bore.
LAMP_C = (-0.33, -0.41, 1.72)      # casing centre (barrel axis height); the Lamp object's origin
LAMP_BOX = (.24, .32, .28)         # casing x/y/z: closed back, sides, top and bottom
LAMP_HOOD = .10                    # the hood (roof + two cheeks) projects this far ahead of the casing front
LAMP_LENS_Y = -0.60                # lens face: 0.20 m BEHIND the muzzle face (-0.80), inside the hood (front -0.66)
LAMP_RIM_R, LAMP_LENS_R = .105, .085
LAMP_BAND_Y = -0.41                # iron band round the barrel + bracket arm out to the casing
LENS_GLOW = (1.0, 0.78, 0.45, 1)   # warm glass (vertex colour; the importer makes SS_Harpoon_Lens emissive)

# ------------------------------------------------------------------ the in-game fairlead (HarpoonGun.StemTopWorld)
# = the stem cap's forward-most face at its top (HarpoonMount.ScanBow) + 0.12 m up (StemClearance). Bow-local METRES,
# Unity axes (x starboard, y up, z forward). The mount root stands at x/z (0, 3.15) on the top deck the scan finds.
BOWS = {
    'low':    {'mount': (0.0, 1.055, 3.15), 'fairlead': (0.0, 2.685, 5.035)},   # BowLow foredeck
    'raised': {'mount': (0.0, 3.08, 3.15),  'fairlead': (0.0, 4.621, 5.130)},   # raised bow's upper floor (muzzle +1.72)
}
def bow_to_root(p, mount):
    """bow-local metres (Unity axes) -> the mount root's Blender frame (forward -Y, up +Z)."""
    return Vector((-(p[0] - mount[0]), -(p[2] - mount[2]), p[1] - mount[1]))


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
