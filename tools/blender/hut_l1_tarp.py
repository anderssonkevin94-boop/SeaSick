# SeaSick LEVEL-1 sleeping hut: a tarp shelter, minimal structure, in the wall kit v4 style
# (chunky bevelled logs, rope lashings, random log tones, flat vertex colour). Kevin 2026-09-28:
# "for the level 1 hut there should be a tarp roof and minimal structure" (the log cabin is L2).
# Exec AFTER wall_kit_v4.py, in the same namespace.
#
# Runtime contract (BuildPlans.Hut, BuildingFactory.Dress, ShelterStateView): metres, Blender
# Z-up, entrance faces -Y, root `Shelter` centred at ground; fits 4.84 x 4.93 m, <= 3.81 m.
# Toggled by name: Bed_Left / Bed_Right, Entrance_Open / Entrance_Closed (default open).
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

HALF_L = 1.75             # tarp half-length along Y (front -Y .. back +Y)
FRAME_Y = 1.66            # A-frames stand just inside the tarp ends
RIDGE_Z = 2.44            # top of the ridge log (tarp rests on it)
EAVE_X, EAVE_Z = 1.86, 0.30
BASE_X = 1.52             # where the A-frame poles meet the ground
CANVAS = (208, 190, 152)
CANVAS_D = (170, 150, 114)
TEAL = (70, 112, 114)

def slope_z(x):
    return RIDGE_Z - abs(x) * (RIDGE_Z - EAVE_Z) / EAVE_X

def beam(p0, p1, w, d, bevel=BEV_WOOD):
    """A bevelled square log from p0 to p1."""
    p0, p1 = Vector(p0), Vector(p1)
    v = p1 - p0
    bm = box(w, d, v.length, bevel=bevel)          # along +Z from 0
    q = Vector((0, 0, 1)).rotation_difference(v.normalized())
    bm.transform(Matrix.Translation(p0) @ q.to_matrix().to_4x4())
    return bm

def rope_line(p0, p1, r=0.022):
    return beam(p0, p1, 2 * r, 2 * r, bevel=0.0)

def tarp_slope(sx, rnd):
    """One side of the tarp: a sagging cloth slab, ridge to eave, with a teal stripe row."""
    us = [0.0, 0.45, 0.70, 0.83, 1.0]              # row 2 (0.70-0.83) is the stripe
    vs = [-HALF_L + 2 * HALF_L * i / 6 for i in range(7)]
    bm = bmesh.new()
    grid = []
    for u in us:
        row = []
        for j, y in enumerate(vs):
            x = sx * u * EAVE_X
            z = RIDGE_Z + 0.02 - u * (RIDGE_Z + 0.02 - EAVE_Z)
            end = j in (0, len(vs) - 1)
            if 0 < u < 1 and not end:
                z -= 0.07 * math.sin(math.pi * u) * (0.8 + 0.4 * rnd.random())   # cloth sag
            if u == 1.0 and not end:
                z += rnd.uniform(-0.02, 0.03)
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
            f.material_index = 1 if i == 2 else 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:                              # outward = up
        if f.normal.z < 0:
            f.normal_flip()
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.08)
    bm.normal_update()
    return bm

def triangle_panel(y, x_half, z_bottom, thickness=0.05, split=None):
    """A flat canvas panel filling the A between the tarp slopes at depth y (optionally one half)."""
    bm = bmesh.new()
    xs = (-x_half, 0.0, x_half) if split is None else ((-x_half, 0.0) if split < 0 else (0.0, x_half))
    pts = []
    if split is None:
        pts = [(-x_half, z_bottom), (x_half, z_bottom), (x_half, slope_z(x_half)), (0, RIDGE_Z), (-x_half, slope_z(x_half))]
    elif split < 0:
        pts = [(-x_half, z_bottom), (-0.02, z_bottom), (-0.02, RIDGE_Z - 0.04), (-x_half, slope_z(x_half))]
    else:
        pts = [(0.02, z_bottom), (x_half, z_bottom), (x_half, slope_z(x_half)), (0.02, RIDGE_Z - 0.04)]
    vs = [bm.verts.new((x, y, z)) for x, z in pts]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for g in ext["geom"]:
        if isinstance(g, bmesh.types.BMVert):
            g.co.y += thickness
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm

def a_frame(B, rnd, y):
    """Two poles crossing just above the ridge, lashed where they cross."""
    top = RIDGE_Z - 0.06
    for sx in (-1, 1):
        p0 = Vector((sx * BASE_X, y, -EMBED * 0.6))
        cross = Vector((0, y, top))
        d = (cross - p0).normalized()
        p1 = cross + d * 0.42                      # carried past the crossing into horns
        B.add(beam(p0, p1, 0.22, 0.22), wood_tone(rnd), Matrix.Identity(4))
    B.add(square_rope(0.13, 0.06, 0), lin(PAL["rope"]), T(0, y, top - 0.02))
    B.add(square_rope(0.13, 0.06, 0), lin(PAL["rope"]), T(0, y, top - 0.14))

def shelter_frame(mat, rnd):
    B = Builder()
    for y in (-FRAME_Y, FRAME_Y):
        a_frame(B, rnd, y)
    # ridge log resting in the crossings, poking out past both ends
    B.add(beam((0, -HALF_L - 0.28, RIDGE_Z - 0.13), (0, HALF_L + 0.28, RIDGE_Z - 0.13), 0.24, 0.24),
          wood_tone(rnd), T(rx=0))
    # sill logs holding the tarp's lower edges on the ground
    for sx in (-1, 1):
        B.add(beam((sx * (EAVE_X - 0.02), -HALF_L - 0.12, 0.13), (sx * (EAVE_X - 0.02), HALF_L + 0.12, 0.13), 0.26, 0.26),
              wood_tone(rnd), T())
        # rope ties from the tarp edge round the sill
        for y in (-1.1, 0.0, 1.1):
            B.add(square_rope(0.15, 0.04, 0), lin(PAL["rope"]), T(sx * (EAVE_X - 0.02), y, 0.13, rx=math.pi / 2) @ T(rz=0))
    # guy rope from the back ridge end to a peg (none at the front: it would cross the door)
    a = (0, HALF_L + 0.22, RIDGE_Z - 0.10)
    b = (0, HALF_L + 0.60, 0.16)
    B.add(rope_line(a, b), lin(PAL["rope_dark"]), T())
    peg = stake(0.12, 0.12, 0.24 + EMBED, 0.10, rnd, 0.0)
    B.add(peg, tone_at(0.2, rnd), T(0, HALF_L + 0.62, -EMBED))
    return B.finish("Frame", mat, ao_height=0.9)

def shelter_canopy(mat, rnd):
    B = Builder()
    for sx in (-1, 1):
        B.add(tarp_slope(sx, rnd), lin(CANVAS), T(), alt=lin(TEAL))
    # the back is closed
    B.add(triangle_panel(HALF_L - 0.06, EAVE_X - 0.30, 0.02), lin(CANVAS_D), T())
    # a sewn patch on the left slope
    u = 0.30
    px, pz = -u * EAVE_X, RIDGE_Z - u * (RIDGE_Z - EAVE_Z) + 0.03
    ang = math.atan((RIDGE_Z - EAVE_Z) / EAVE_X)
    B.add(box(0.62, 0.70, 0.03, bevel=0.0, z0=False), lin(CANVAS_D), T(px, 0.55, pz + 0.02, ry=-ang))
    return B.finish("Canopy", mat, ao_height=2.5, ao_min=0.85)

def shelter_entrances(mat, rnd):
    xh = EAVE_X - 0.30
    y = -FRAME_Y + 0.13                    # just behind the front A-frame, which frames the door
    # closed: the two front flaps let down, toggles across the split
    Bc = Builder()
    for side in (-1, 1):
        Bc.add(triangle_panel(y - (0.012 if side > 0 else 0), xh, 0.02, split=side), lin(CANVAS), T())
    for z in (0.70, 1.35):
        Bc.add(box(0.26, 0.05, 0.05, bevel=0.0, z0=False), lin(PAL["rope_dark"]), T(0, y - 0.04, z))
        Bc.add(box(0.06, 0.06, 0.15, bevel=0.0, z0=False), lin(PAL["wood_light"]), T(0.10, y - 0.07, z))
    closed = Bc.finish("Entrance_Closed", mat, ao_height=1.0, ao_min=0.9)
    # open: each flap rolled up along its A-frame pole and tied
    Bo = Builder()
    for sx in (-1, 1):
        p0 = Vector((sx * (xh - 0.10), y - 0.10, 0.25))
        p1 = Vector((sx * 0.20, y - 0.10, RIDGE_Z - 0.30))
        roll = beam(p0, p1, 0.24, 0.22, bevel=0.05)
        Bo.add(roll, lin(CANVAS), T())
        mid = p0.lerp(p1, 0.45)
        d = (p1 - p0).normalized()
        q = Vector((0, 0, 1)).rotation_difference(d)
        Bo.add(square_rope(0.13, 0.04, 0), lin(PAL["rope_dark"]), Matrix.Translation(mid) @ q.to_matrix().to_4x4())
    opened = Bo.finish("Entrance_Open", mat, ao_height=1.0, ao_min=0.9)
    return closed, opened

def bedroll(rnd, name, mat):
    B = Builder()
    B.add(box(0.80, 1.90, 0.12, bevel=BEV_WOOD), lin((176, 146, 96)), T(0, 0, 0.02))        # straw pallet
    B.add(box(0.82, 1.20, 0.09, bevel=0.03), jitter(lin(WOOL), rnd, 0.06), T(0, 0.30, 0.13))   # wool blanket
    B.add(box(0.52, 0.30, 0.13, bevel=0.04), lin((222, 208, 178)), T(0, -0.68, 0.13))       # pillow, door end
    return B.finish(name, mat, ao_height=0.5, ao_min=0.8)

WOOL = (74, 98, 126)

def shelter_interior(mat, rnd):
    B = Builder()
    B.add(box(2 * (EAVE_X - 0.16), 2 * HALF_L - 0.10, 0.03, bevel=0.0), lin((70, 52, 36)), T(0, 0, 0.0))
    return B.finish("Interior", mat, ao_height=1.0, ao_min=1.0)

def shelter_porch(mat, rnd):
    B = Builder()
    # lantern hung from the front ridge end
    lx, ly, lz = 0.0, -HALF_L - 0.22, RIDGE_Z - 0.55
    B.add(rope_line((lx, ly, RIDGE_Z - 0.20), (lx, ly, lz + 0.14), 0.015), lin(PAL["rope_dark"]), T())
    B.add(box(0.22, 0.22, 0.28, bevel=BEV_METAL), lin(PAL["metal"]), T(lx, ly, lz - 0.14))
    B.add(box(0.17, 0.23, 0.20, bevel=0.0), lin((242, 214, 150)), T(lx, ly, lz - 0.10))
    B.add(box(0.17, 0.23, 0.20, bevel=0.0), lin((242, 214, 150)), T(lx, ly, lz - 0.10, rz=math.pi / 2))
    B.add(pyramid_cap(0.12, 0.03, 0.09), lin(PAL["metal"]), T(lx, ly, lz + 0.14))
    return B.finish("Porch_Details", mat, ao_height=0.9)

def build_shelter_l1():
    rnd = random.Random(31)
    mat = bpy.data.materials["M_WallKit"]
    root = bpy.data.objects.new("Shelter", None)
    bpy.context.scene.collection.objects.link(root)
    root["footprint_xz"] = [4.84, 4.93]
    parts = [shelter_frame(mat, rnd), shelter_canopy(mat, rnd), shelter_interior(mat, rnd), shelter_porch(mat, rnd)]
    closed, opened = shelter_entrances(mat, rnd)
    bl = bedroll(rnd, "Bed_Left", mat); bl.location = (-0.66, 0.20, 0.03)
    br = bedroll(rnd, "Bed_Right", mat); br.location = (0.66, 0.20, 0.03)
    for ob in parts + [closed, opened, bl, br]:
        ob.parent = root
    closed.hide_render = True
    for name, p in (("Sleep_Left", (-0.66, 0.20, 0.22)), ("Sleep_Right", (0.66, 0.20, 0.22)),
                    ("Entry", (0, -HALF_L - 0.70, 0))):
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 0.2
        e.location = p
        bpy.context.scene.collection.objects.link(e)
        e.parent = root
    bpy.context.view_layer.update()
    return root

HUT = build_shelter_l1()
print("Shelter L1", tris(HUT), "tris", {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons)
                                        for o in HUT.children if o.type == 'MESH'}, bounds(HUT))
