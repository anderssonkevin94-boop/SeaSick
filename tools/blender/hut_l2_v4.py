# SeaSick LEVEL-2 sleeping hut (Kevin 2026-09-28: "very good for a level 2 hut") in the wall kit v4 style: vertical log walls (mixed widths,
# random tones, one bevel rule), rope-bound corner posts, iron-strapped plank door, chunky
# straw thatch with crossed gable horns. Exec AFTER wall_kit_v4.py, in the same namespace.
#
# Runtime contract (BuildPlans.Hut, BuildingFactory.Dress, ShelterStateView; same frame as
# art-staging/shelter-astra-lvl1-v1): metres, Blender Z-up, entrance faces -Y, root centred
# at ground; fits the 4.84 x 4.93 m plot and ~3.81 m ridge. Meshes the code toggles by name:
#   Bed_Left / Bed_Right         (on per housed hand, max 2)
#   Entrance_Open / Entrance_Closed (mutually exclusive mesh swap, default open)
# Everything else is always-on art. Villagers vanish at the door; beds show through it.
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

HW, HD = 1.75, 1.55          # wall centre lines: x = +-HW (sides), y = +-HD (front/back)
WALL_T = 0.30                # hut logs a little thinner than the palisade
WALL_H = 1.95                # side wall log tops
POST = 0.40
EAVE_X = 2.17                # thatch eave line
PITCH_T = 0.60               # roof rise per metre
EAVE_Z = 2.20 - (EAVE_X - HW) * PITCH_T     # roof plane passes just over the side plates
ROOF_L = HD + 0.50           # thatch overhang past the gables
DOOR_W, DOOR_H = 1.00, 1.66
STRAW, STRAW_D = (205, 170, 102), (160, 125, 70)
WOOL = (74, 98, 126)

def roof_z(x):
    return EAVE_Z + (EAVE_X - abs(x)) * PITCH_T

def log_box(w, d, h, cx, top_fn=None):
    """A wall log from below ground to h; with top_fn its top follows the roof slope."""
    bm = box(w, d, h + EMBED, bevel=BEV_WOOD)
    bmesh.ops.translate(bm, vec=(0, 0, -EMBED), verts=bm.verts)
    if top_fn:
        for v in bm.verts:
            if v.co.z > h - BEV_WOOD - 1e-4:
                v.co.z += top_fn(cx + v.co.x) - h
    return bm

def wall_run(B, rnd, x0, x1, h, place, top_fn=None, skip=None, widths=None):
    """Logs along local X from x0 to x1 (placed by `place`), mixed widths, random tones."""
    L = x1 - x0
    if widths is None:
        widths = []
        while sum(widths) < L - 0.2:
            widths.append(rnd.uniform(0.30, 0.38))
        widths.append(L - sum(widths))
        if widths[-1] < 0.2:
            widths[-2] += widths.pop()
    x = x0
    for w in widths:
        cx = x + w / 2
        if skip and skip[0] - 0.01 <= cx <= skip[1] + 0.01:
            # above the door: a short log from the lintel up to the gable
            z0 = DOOR_H + 0.22
            ht = (top_fn(cx) if top_fn else h) - z0
            bm = box(w + 0.004, WALL_T, ht, bevel=BEV_WOOD)
            if top_fn:
                for v in bm.verts:
                    if v.co.z > ht - BEV_WOOD - 1e-4:
                        v.co.z += top_fn(cx + v.co.x) - (z0 + ht)
            B.add(bm, wood_tone(rnd), place @ T(cx, 0, z0))
        else:
            B.add(log_box(w + 0.004, WALL_T, h + rnd.uniform(-0.03, 0.02), cx, top_fn), wood_tone(rnd),
                  place @ T(cx, rnd.uniform(-0.01, 0.01), 0))
        x += w

def hut_walls(mat, rnd):
    B = Builder()
    gable = lambda x: roof_z(x) - 0.03
    inner_x = HW - POST / 2
    inner_y = HD - POST / 2
    # front (-Y) with the door gap, back (+Y) plain; both run up into the gable
    front_w = [0.36, 0.35, 0.34 + (inner_x - 0.5 - 1.05)] + [0.34, 0.32, 0.34] + [0.35, 0.37, inner_x - 0.5 - 0.72]
    wall_run(B, rnd, -inner_x, inner_x, WALL_H, T(0, -HD, 0), gable, skip=(-0.5, 0.5), widths=front_w)
    wall_run(B, rnd, -inner_x, inner_x, WALL_H, T(0, HD, 0), gable)
    # sides (+-X), level tops under the plates
    for sx in (-1, 1):
        wall_run(B, rnd, -inner_y, inner_y, WALL_H, T(sx * HW, 0, 0, rz=math.pi / 2))
    return B.finish("Lower_Walls", mat, ao_height=0.9)

def hut_frame(mat, rnd):
    B = Builder()
    rail_tone = tuple((a + b) / 2 for a, b in zip(lin(PAL["wood_light"]), lin(PAL["wood_med"])))
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * HW, sy * HD
            B.add(box(POST, POST, 2.20 + EMBED, bevel=BEV_WOOD), wood_tone(rnd), T(x, y, -EMBED))
            for z in (0.24, 1.70, 1.84):
                B.add(square_rope(POST / 2, 0.075, z), lin(PAL["rope"]), T(x, y, 0))
    # side top plates carrying the thatch
    for sx in (-1, 1):
        B.add(box(0.26, 2 * HD + 0.60, 0.26, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04),
              T(sx * HW, 0, 2.07))
    # ridge beam, poking out past both gables
    rz = roof_z(0) - 0.10
    B.add(box(0.26, 2 * ROOF_L + 0.30, 0.26, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04), T(0, 0, rz))
    # crossed gable horns front and back: two boards along the roof line, crossing above the ridge
    ang = math.atan(PITCH_T)
    for sy in (-1, 1):
        y = sy * (ROOF_L + 0.02)
        for sx in (-1, 1):
            # a board along the thatch line of the slope falling toward sx, carried 0.35 m past
            # the ridge so the pair cross in an X above it
            ln = 1.35
            hb = box(ln, 0.12, 0.20, bevel=BEV_WOOD, z0=False)
            cx = sx * (ln / 2 - 0.35)
            cz = roof_z(0) + 0.30 - PITCH_T * sx * cx
            B.add(hb, wood_tone(rnd), T(cx, y, cz, ry=sx * ang))
        B.add(box(0.22, 0.18, 0.22, bevel=BEV_METAL, z0=False), lin(PAL["metal"]), T(0, y, roof_z(0) + 0.26))
    # door frame: jambs + lintel in light wood, iron at the corners
    for sx in (-1, 1):
        B.add(box(0.18, WALL_T + 0.08, DOOR_H + 0.22 + EMBED, bevel=BEV_WOOD), jitter(rail_tone, rnd, 0.04),
              T(sx * (DOOR_W / 2 + 0.09), -HD, -EMBED))
    B.add(box(DOOR_W + 0.60, WALL_T + 0.10, 0.24, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04),
          T(0, -HD - 0.01, DOOR_H + 0.12))
    for sx in (-1, 1):
        B.add(box(0.20, 0.10, 0.28, bevel=BEV_METAL, z0=False), lin(PAL["metal"]), T(sx * (DOOR_W / 2 + 0.09), -HD - 0.19, DOOR_H + 0.12))
        B.add(rivet(), lin(PAL["rivet"]), T(sx * (DOOR_W / 2 + 0.09), -HD - 0.24, DOOR_H + 0.12, rx=math.pi / 2))
    # doorstep log
    B.add(box(DOOR_W + 0.40, 0.34, 0.14, bevel=BEV_WOOD), tone_at(0.3, rnd), T(0, -HD - 0.28, -0.04))
    return B.finish("Frame", mat, ao_height=0.9)

def thatch_tier(rnd, sx, v0, v1, lift, th, col):
    """One straw tier as a single slab: straight top edge, ragged tufted lower edge.
    Built in slope space (u along the ridge, v up-slope from the eave, w off the roof)."""
    n = 16
    L = 2 * ROOF_L
    bm = bmesh.new()
    low = []
    for i in range(n + 1):
        u = -ROOF_L + L * i / n
        drop = rnd.uniform(0.07, 0.17) if i % 2 else rnd.uniform(0.0, 0.04)
        low.append(bm.verts.new((u, v0 - drop, 0)))
    high = [bm.verts.new((ROOF_L - L * i / n, v1, 0)) for i in range(n + 1)]
    f = bm.faces.new(low + high)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    top = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    for v in top:
        v.co.z += th
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=BEV_WOOD, segments=1, affect='EDGES', clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    if sx < 0:                                   # mirror to the other slope
        for v in bm.verts:
            v.co.y = -v.co.y
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.normal_update()
    ang = math.atan(PITCH_T)
    return bm, T(sx * EAVE_X, 0, EAVE_Z + lift, rz=math.pi / 2, rx=sx * ang)

def thatch_side(B, rnd, sx):
    """One roof slope: three overlapping tiers, each lifted a little over the one below."""
    ang = math.atan(PITCH_T)
    run = EAVE_X / math.cos(ang)
    for k, (a, b, u) in enumerate([(0.00, 0.46, 0.30), (0.34, 0.76, 0.55), (0.64, 1.04, 0.80)]):
        col = tuple(c0 + (c1 - c0) * (u + rnd.uniform(-0.06, 0.06)) for c0, c1 in zip(lin(STRAW_D), lin(STRAW)))
        bm, M = thatch_tier(rnd, sx, a * run, b * run, 0.05 * k, 0.22, col)
        B.add(bm, col, M)

def hut_roof(mat, rnd):
    B = Builder()
    for sx in (-1, 1):
        thatch_side(B, rnd, sx)
    # ridge cap: a fat straw roll with rope bands
    zc = roof_z(0) + 0.24
    cap = box(0.34, 2 * ROOF_L + 0.06, 0.34, bevel=BEV_WOOD * 2, z0=False)
    B.add(cap, lin(STRAW_D), T(0, 0, zc, ry=math.pi / 4))
    for y in (-ROOF_L + 0.45, -0.7, 0.7, ROOF_L - 0.45):
        B.add(square_rope(0.15, 0.05, 0), lin(PAL["rope_dark"]),
              T(0, y, zc) @ T(ry=math.pi / 4) @ T(rx=math.pi / 2))
    return B.finish("Canopy", mat, ao_height=3.0, ao_min=0.85)

def door_leaf(rnd):
    """Plank door in hinge space: hinge at origin, leaf running +X, face toward -Y."""
    B = Builder()
    w = DOOR_W - 0.04
    widths = [0.33, 0.31, w - 0.64]
    x = 0.0
    for i, pw in enumerate(widths):
        B.add(box(pw + 0.004, 0.12, DOOR_H - 0.04, bevel=BEV_WOOD), wood_tone(rnd), T(x + pw / 2, 0, 0.02))
        x += pw
    rail_tone = tuple((a + b) / 2 for a, b in zip(lin(PAL["wood_light"]), lin(PAL["wood_med"])))
    for z in (0.35, 1.25):
        B.add(box(w - 0.04, 0.08, 0.20, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04), T(w / 2, -0.09, z))
        B.add(box(0.36, 0.10, 0.24, bevel=BEV_METAL, z0=False), lin(PAL["metal"]), T(0.17, -0.10, z))
        B.add(rivet(), lin(PAL["rivet"]), T(0.26, -0.16, z, rx=math.pi / 2))
    L = math.hypot(w - 0.2, 0.9); a = math.atan2(0.9, w - 0.2)
    B.add(box(L, 0.07, 0.16, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04), T(w / 2, -0.085, 0.80, ry=-a))
    # iron ring pull
    B.add(square_rope(0.05, 0.018, 0), lin(PAL["metal"]), T(w - 0.16, -0.14, 0.85, rx=math.pi / 2))
    return B

def hut_entrances(mat, rnd):
    hinge = Vector((-DOOR_W / 2 + 0.02, -HD - 0.02, 0))
    Bc = door_leaf(random.Random(5))
    closed = Bc.finish("Entrance_Closed", mat, ao_height=0.9)
    closed.location = hinge
    Bo = door_leaf(random.Random(5))
    opened = Bo.finish("Entrance_Open", mat, ao_height=0.9)
    opened.location = hinge + Vector((0, -0.02, 0))
    opened.rotation_euler = (0, 0, -math.radians(163))     # swung out, lying back along the front wall
    return closed, opened

def bedroll(rnd, name, mat):
    B = Builder()
    B.add(box(0.84, 1.95, 0.16, bevel=BEV_WOOD), lin((176, 146, 96)), T(0, 0, 0.02))       # straw pallet
    B.add(box(0.86, 1.30, 0.10, bevel=0.03), jitter(lin(WOOL), rnd, 0.06), T(0, 0.28, 0.17))  # wool blanket
    B.add(box(0.56, 0.32, 0.14, bevel=0.04), lin((222, 208, 178)), T(0, -0.70, 0.17))      # pillow (door end)
    return B.finish(name, mat, ao_height=0.5, ao_min=0.8)

def hut_interior(mat, rnd):
    """Dark earth floor + dark inner lining so the doorway reads as a warm, shadowed room."""
    B = Builder()
    ix, iy = HW - WALL_T / 2 - 0.01, HD - WALL_T / 2 - 0.01
    B.add(box(2 * ix, 2 * iy, 0.04, bevel=0.0), lin((64, 46, 32)), T(0, 0, 0.0))
    return B.finish("Interior", mat, ao_height=1.0, ao_min=1.0)

def hut_porch(mat, rnd):
    B = Builder()
    # lantern on an iron bracket right of the door
    lx, ly, lz = DOOR_W / 2 + 0.42, -HD - 0.30, 1.55
    B.add(box(0.06, 0.34, 0.06, bevel=0.0, z0=False), lin(PAL["metal"]), T(lx, -HD - 0.14, lz + 0.26))
    B.add(box(0.24, 0.24, 0.30, bevel=BEV_METAL), lin(PAL["metal"]), T(lx, ly, lz - 0.15))
    B.add(box(0.19, 0.25, 0.22, bevel=0.0), lin((242, 214, 150)), T(lx, ly, lz - 0.11))
    B.add(box(0.19, 0.25, 0.22, bevel=0.0), lin((242, 214, 150)), T(lx, ly, lz - 0.11, rz=math.pi / 2))
    B.add(pyramid_cap(0.13, 0.03, 0.10), lin(PAL["metal"]), T(lx, ly, lz + 0.15))
    # firewood stack along the right wall, under the eave: logs parallel to the wall, pale ends
    for (x, z) in ((HW + 0.33, 0.12), (HW + 0.55, 0.12), (HW + 0.44, 0.33)):
        ln = 1.5 + rnd.uniform(-0.1, 0.1)
        y = -0.1 + rnd.uniform(-0.08, 0.08)
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.12, radius2=0.12, depth=ln)
        B.add(bm, wood_tone(rnd), T(x, y, z, rx=math.pi / 2))
        for sy in (-1, 1):
            disc = bmesh.new()
            bmesh.ops.create_cone(disc, cap_ends=True, segments=6, radius1=0.095, radius2=0.095, depth=0.02)
            B.add(disc, lin((214, 180, 128)), T(x, y + sy * ln / 2, z, rx=math.pi / 2))
    return B.finish("Porch_Details", mat, ao_height=0.9)

def build_hut():
    rnd = random.Random(77)
    mat = bpy.data.materials["M_WallKit"]
    root = bpy.data.objects.new("Shelter", None)
    bpy.context.scene.collection.objects.link(root)
    root["footprint_xz"] = [4.84, 4.93]
    parts = [hut_walls(mat, rnd), hut_frame(mat, rnd), hut_roof(mat, rnd), hut_interior(mat, rnd),
             hut_porch(mat, rnd)]
    closed, opened = hut_entrances(mat, rnd)
    bl = bedroll(rnd, "Bed_Left", mat); bl.location = (-0.78, 0.30, 0.04)
    br = bedroll(rnd, "Bed_Right", mat); br.location = (0.78, 0.30, 0.04)
    for ob in parts + [closed, opened, bl, br]:
        ob.parent = root
    closed.hide_render = True        # default state: open
    for name, p in (("Sleep_Left", (-0.78, 0.30, 0.25)), ("Sleep_Right", (0.78, 0.30, 0.25)),
                    ("Entry", (0, -HD - 0.60, 0))):
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 0.2
        e.location = p
        bpy.context.scene.collection.objects.link(e)
        e.parent = root
    bpy.context.view_layer.update()
    return root

HUT = build_hut()
print("Shelter", tris(HUT), "tris", {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons)
                                     for o in HUT.children if o.type == 'MESH'}, bounds(HUT))
