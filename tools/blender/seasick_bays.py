"""seasick_bays.py -- the bay system for SeaSick's modular ship.

Companion to `seasick_hulls.py`. That file lofts HULLS; this one describes the
SPACE INSIDE them and builds the kit that furnishes it.

The unit is the BAY: one gun-port's worth of length. The brig's ports sit 3.0 m
apart because that is a gun's recoil room, so it is already the smallest slice
of hull that can hold anything useful. Bays run along her and TIERS run up her,
and one bay on one tier is a CELL -- the thing the player assigns to hold,
battery or quarters.

Two laws from the geometry standard are enforced here, because they are the
ones that stop a modular ship accreting into a pile:

  * **The generator emits SPACE, never contents.** Every cell is an empty at a
    MEASURED centre, sized to the real clear width. The barrels and guns are
    Unity prefabs placed at those empties -- ten guns are one mesh and ten
    transforms, so improving the gun improves it on every ship at once.

  * **Geometry only where the eye can reach it.** The kit below has no barrel
    bottoms, no crate undersides and no bulkhead back faces. It is the same
    rule that took the Solidify shell out of the brig and halved her.

Clear widths are RAY-CAST against the built hull rather than recomputed from
the loft. A number that comes from the mesh cannot drift from the mesh.
"""

import bpy
import bmesh
import math
from mathutils import Vector

# The atom, in metres. The brig's port spacing; the three-decker's is 2.90 and
# both round to the same bay count, so 3.0 is the number to reason in.
BAY_PITCH = 3.0

# Length overall divided by this gives the bay count. It is not fitted: the
# brig's hand-authored port_x has 7 entries and 26.0/3.8 = 6.8; the
# three-decker's has 12 and 46.0/3.8 = 12.1. The 3.8 rather than 3.0 is the
# bow and stern that cannot carry a gun.
BAY_DIVISOR = 3.8


def lerp(a, b, t):
    return a + (b - a) * t


# ----------------------------------------------------------------- plan -----

def bay_centres(p):
    """Built-x of every bay centre.

    A bay is a slice of HULL, so it does not depend on whether a gun happens
    to be run out there: a hull carries every bay her length gives her, and
    her gun ports are cut in the bays she can actually man. Reading the bays
    off `port_x` -- which they used to -- made that circular the moment the
    ports stopped being one per bay, and it would have taken the three-decker
    from 48 cells to 16 for no reason but her battery being capped.

    `bay_x` is the grid the ladder writes. The four authored hulls have no
    such field and carry a port in every bay, so for them the old rule holds
    and the two lists still agree."""
    if p.get("bay_x"):
        return list(p["bay_x"])
    if p.get("port_x"):
        return list(p["port_x"])
    n = max(1, round(p["length"] / BAY_DIVISOR))
    # centred on the loft origin, one pitch apart, so a lengthening inserts
    # bays amidships and the end bays keep their offsets from the ends.
    span = (n - 1) * BAY_PITCH
    return [(-span / 2.0) + i * BAY_PITCH for i in range(n)]


# ------------------------------------------------------------ the ports -----
#
# **A gun port is structural. It is cut when she is BUILT, and it stays cut
# whether or not there is a gun behind it.**
#
# For one afternoon on 2026-09-05 the generator cut one port per gun she could
# man, so a first-rate carrying twenty guns had ten holes in her side. It is
# the honest count and it looks wrong: at a gun's 2.9 m pitch, ten ports cover
# a third of a 46 m ship and she reads as an unfinished hull. Two different
# things had been folded into one number -- how many holes she has, which her
# LENGTH and her DECKS decide, and how many guns are aboard, which the bay
# economy decides.
#
# They are separate again, and the LID is what says which is which: every port
# is cut, every lid is baked SHUT, and the game swings open the ones with a gun
# behind them. See `build_port_lids`. That is also what a real ship looks like,
# and it makes "cleared for action" something you can see.
#
# The battery ceiling now lives in `Shipyard`, where the crew's training is --
# see [[seasick-bay-system]].


def tier_plan(p):
    """[(name, floor_z, ceiling_z)] from the hold up to the rail.

    Tiers are the hull's own deck levels wherever it has them -- the
    three-decker's `deck_levels` is already authored as Hold/Lower/Middle/Upper
    and measures 2.30 m deck-to-deck, which is the headroom the kit is built
    for. A hull with one deck gets one tier below it and one above; an open
    boat gets a single tier from her floor to her rail."""
    draft, depth = p["draft"], p["depth"]
    z_rail = depth - draft
    deck_z = p.get("deck_z")

    if p.get("deck_levels"):
        levels = list(p["deck_levels"])
        out = []
        for i, (name, z) in enumerate(levels):
            top = levels[i + 1][1] if i + 1 < len(levels) else z_rail
            out.append((name, z, top))
        return _usable(out, p)

    # The hold floor sits above the keel timber, not on it -- the backbone and
    # the bilge take the bottom of her.
    floor = -draft + max(0.20, draft * 0.14)

    if deck_z is None:
        return _usable([("Open", floor, z_rail)], p)

    # **The weather deck of a gunned hull is her UPPER GUN DECK, and it has to
    # be CALLED that on every rung that has one.** `Shipyard.PruneCells` keys a
    # cell on (bay, tier NAME), so a tier that is renamed between two rungs is
    # indistinguishable from a tier that was deleted -- and this one was called
    # `Deck` on rungs 9-14 and `UpperGunDeck` from 15 up, for the same physical
    # deck. Measured cost before the fix: raising a Beamy brig to a Two-decker
    # dropped ALL NINE of her guns, silently, because every battery cell was
    # keyed on a name that no longer existed. Rungs 15-19 already stack their
    # names down from the weather deck and lose nothing across a raise; this
    # makes the two-tier hulls speak the same language, so 14->15 adds
    # `MiddleGunDeck` underneath and takes nothing away.
    #
    # Keyed on whether she carries guns, NOT on tier count: an un-gunned decked
    # boat also comes through here, and calling her hold's lid a gun deck would
    # be a lie in the data even though `_usable` drops that tier for headroom.
    top = "UpperGunDeck" if p.get("gun_decks") else "Deck"
    return _usable([("Hold", floor, deck_z), (top, deck_z, z_rail)], p)


# A tier has to be a place, not a gap. Measured, the sloop's space above her
# deck is 0.45 m -- that is her BULWARK, not a room, and counting it would
# have given her four cells she cannot put anything in. The brig's equivalent
# is 1.45 m and does count, because her battery stands on it: a weather deck
# with guns on it is a tier even though it is open to the sky.
MIN_HEADROOM = 1.20


def _usable(tiers, p):
    sills = [s for (s, _) in _port_rows(p)]
    keep = []
    for (name, z0, z1) in tiers:
        room = z1 - z0
        carries_guns = any(z0 - 1e-6 <= s < z1 + 1e-6 for s in sills)
        if room >= MIN_HEADROOM or carries_guns:
            keep.append((name, z0, z1))
    return keep


def _port_rows(p):
    """(sill, head) per gun deck. Mirrors the generator's own helper rather
    than importing it, so a bay plan can be read without loading the loft."""
    h = p.get("port_height")
    if not h or not p.get("gun_decks"):
        return []
    return [(z, z + h) for z in p["gun_decks"]]


# ------------------------------------------------------------ measured -----

def _evaluated(obj):
    return obj.evaluated_get(bpy.context.evaluated_depsgraph_get())


def clear_half_beam(hull, x, z, limit=12.0):
    """Ray-cast outboard from the centreline; return the distance to the first
    surface, which is the inside of the planking.

    Returns None where the ray leaves the hull entirely -- past the stem, under
    the counter, or above the rail -- and that is the test that drops bays the
    hull does not actually have room for."""
    ev = _evaluated(hull)
    hit, loc, nor, idx = ev.ray_cast(Vector((x, 0.0, z)), Vector((0.0, 1.0, 0.0)),
                                     distance=limit)
    if not hit:
        return None
    return abs(loc.y)


def measure_cell(hull, x, z_floor, z_ceiling):
    """The clear box of one cell: half-beam sampled at three heights and at
    three stations, taking the NARROWEST, because a barrel has to fit the
    tightest part of the space and not the average of it."""
    xs = (x - BAY_PITCH * 0.4, x, x + BAY_PITCH * 0.4)
    zs = (z_floor + (z_ceiling - z_floor) * f for f in (0.15, 0.5, 0.85))
    widths = []
    for zz in zs:
        for xx in xs:
            w = clear_half_beam(hull, xx, zz)
            if w is not None:
                widths.append(w)
    if not widths:
        return None
    return min(widths)


# ------------------------------------------------------------- loading -----

HULLS_PY = "/Users/kevinandersson/blender_objects/seasick_hulls.py"


def load_hulls(path=HULLS_PY):
    """The hull generator's table and helpers, WITHOUT rebuilding the fleet.

    `seasick_hulls.py` ends in `if __name__ == "__main__" or True:` and so
    rebuilds every hull the moment it is exec'd. That is right for the
    generator and wrong for anything that only wants to READ the parameters --
    a bay plan should never be able to move a hull. Truncating at that guard
    gives the table and every helper with no side effect."""
    src = open(path).read()
    guard = 'if __name__ == "__main__" or True:'
    if guard not in src:
        raise RuntimeError("seasick_hulls.py: build guard not found")
    # Neutralise the guard rather than truncating at it. Truncating also threw
    # away `audit`, `report_topology`, `export_fleet` and `hydrostatics`, which
    # all live BELOW it -- and the topology gate is the one thing the kit must
    # not be built without. Swapping the condition keeps every line number
    # intact, so a traceback still points at the real file.
    g = {"__name__": "seasick_hulls_table", "__file__": path}
    exec(compile(src.replace(guard, "if False:"), path, "exec"), g)
    return g


def all_params(g=None):
    """Every hull's parameter dict, the raft included, in tier order."""
    g = g or load_hulls()
    return [g["RAFT"]] + list(g["FLEET"])


# ------------------------------------------------------------- naming -----

def bay_labels(n):
    """Bay names counted inward from EACH END, never straight through.

    A lengthening lets a new section into her amidships, so the bays at the
    bow and the stern keep their positions and the new ones appear in the
    middle. Numbering from the stern (`A1`, `A2`, ...) and from the bow (`F1`,
    `F2`, ...) means an upgrade never renames a bay the player has already
    filled -- it only adds unnamed space between them. Numbering straight
    through from one end would renumber half the ship every time she grew."""
    half, odd = n // 2, n % 2
    aft = ["A%d" % (i + 1) for i in range(half)]
    mid = ["C1"] if odd else []
    fwd = ["F%d" % (half - i) for i in range(half)]
    return aft + mid + fwd


# ----------------------------------------------------------- emission -----

def emit_bays(p, hull, coll, verbose=False):
    """One empty per CELL, at the measured centre of its clear space.

    The empty is a CUBE scaled to the real clear box, so opening the hull in
    Blender shows every space at true size and you can see whether a barrel
    fits before a barrel exists. Unity reads the same three numbers off the
    transform: `localScale` IS the bay's clear half-extent."""
    centres = bay_centres(p)
    labels = bay_labels(len(centres))
    tiers = tier_plan(p)
    made, skipped = [], 0

    for (label, x) in zip(labels, centres):
        for (tname, z0, z1) in tiers:
            half = measure_cell(hull, x, z0, z1)
            if half is None or half < 0.35:
                skipped += 1          # no room here -- the ends taper away
                continue
            zc = 0.5 * (z0 + z1)
            e = bpy.data.objects.new(
                "%s_Bay%s_%s" % (p["name"], label, tname), None)
            e.empty_display_type = 'CUBE'
            e.empty_display_size = 1.0
            e.location = (x, 0.0, zc)
            e.scale = (BAY_PITCH * 0.5, half, (z1 - z0) * 0.5)
            coll.objects.link(e)
            # Parent, and DO NOT set matrix_parent_inverse. The generator sets
            # it in `build_port_anchors` because it parents while the hull is
            # still at the world origin, where the inverse is identity and the
            # line is free. Copied into a pass that runs after the fleet has
            # been laid out in its row, the same line cancels the parent's
            # transform exactly -- and every empty lands at the world origin
            # while still reporting the right hull-local coordinates.
            e.parent = hull
            made.append((e.name, x, zc, half, z1 - z0))
            if verbose:
                print("    %-42s x%7.2f z%6.2f  half-beam %5.2f  head %4.2f"
                      % (e.name, x, zc, half, z1 - z0))
    return made, skipped


def clear_bays(prefix="T"):
    n = 0
    for o in list(bpy.data.objects):
        if o.type == 'EMPTY' and "_Bay" in o.name and o.name.startswith(prefix):
            bpy.data.objects.remove(o, do_unlink=True)
            n += 1
    return n


def build_bays(verbose=False):
    """Every hull in the scene gets its bay empties. Reports the cell count,
    which is the number the whole progression is denominated in.

    Un-hides every hull collection first and puts them back afterwards. A
    hidden object has NO EVALUATED MESH, so `ray_cast` does not return a miss
    -- it raises, and a measurement pass that ran fine yesterday dies today
    because somebody framed a screenshot in between. The measurement must not
    depend on what is currently visible."""
    g = load_hulls()
    clear_bays()
    hidden = {}
    for c in bpy.data.collections:
        if c.name in {p["name"] for p in g["FLEET"]}:
            hidden[c.name] = c.hide_viewport
            c.hide_viewport = False
    bpy.context.view_layer.update()
    try:
        rows = []
        for p in g["FLEET"]:
            hull = bpy.data.objects.get(p["name"] + "_Hull")
            if hull is None:
                continue
            coll = bpy.data.collections[p["name"]]
            made, skipped = emit_bays(p, hull, coll, verbose)
            rows.append((p["name"], len(bay_centres(p)), len(tier_plan(p)),
                         len(made), skipped))
    finally:
        for name, was in hidden.items():
            bpy.data.collections[name].hide_viewport = was
    return rows


# ================================================================== KIT =====
# One set of props that furnishes every bay on every hull in the game.
#
# It can be ONE set because a bay is a constant 3.0 m and the three-decker
# measures 2.30 m deck to deck -- the same headroom the brig's hold and the
# sloop's have room for. Nothing here is modelled per hull, and nothing here is
# modelled twice.
#
# Two laws do all the work:
#
#   * **Nothing hidden is modelled.** A barrel standing on a deck has no bottom
#     cap; a crate has no underside; a bulkhead has no top or bottom edge,
#     because the deck and the beams above it are already there. On the paddle
#     boat the equivalent saving is enormous -- her railing alone is 3,512
#     triangles, 21% of the whole boat, and the generated hulls get that same
#     capping rail free inside the J-section.
#
#   * **Flat for planking, smooth for turned.** Flat shading splits every
#     vertex, so it is reserved for what is actually flat. A barrel, a sack and
#     a rope coil are turned objects: smooth-shaded their vertices weld, the
#     cost halves, and they read rounder rather than cruder.

KIT_COLL = "SeaSick_Kit"


def _kit_mesh(name, verts, faces, coll, smooth=False):
    """Build, recalculate normals, and never leave a stray behind.

    Normals are a BUILD STEP, checked, not something to wind by inspection --
    the generator learned that lining 28 gun ports by hand is 28 chances to be
    wrong."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for pgon in me.polygons:
        pgon.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def _ring(n, r, z, cx=0.0, cy=0.0):
    return [(cx + r * math.cos(2 * math.pi * i / n),
             cy + r * math.sin(2 * math.pi * i / n), z) for i in range(n)]


def _tube_faces(rings, n, base=0):
    """Quads between consecutive rings of n verts. No caps -- callers decide
    which ends the eye can actually reach."""
    f = []
    for k in range(len(rings) - 1):
        a, b = base + k * n, base + (k + 1) * n
        for i in range(n):
            j = (i + 1) % n
            f.append([a + i, a + j, b + j, b + i])
    return f


# --- the pieces -------------------------------------------------------------

def kit_barrel(coll):
    """A cask, 0.88 m tall. Ten sides because it is seen from two metres away
    on a deck; smooth-shaded, so ten reads round. No bottom -- it stands on
    something."""
    # Four rings, not three. With a single mid-ring the taper is linear and
    # the silhouette reads as a pill; a barrel is a straight belly with a
    # shoulder at each end, and it is the SHOULDER that says "cask" at ten
    # metres. Two extra rings cost 20 triangles and buy the whole read.
    n = 10
    rings = [_ring(n, 0.27, 0.00), _ring(n, 0.36, 0.22),
             _ring(n, 0.36, 0.66), _ring(n, 0.27, 0.88)]
    verts = [v for r in rings for v in r]
    faces = _tube_faces(rings, n)
    top = len(verts)
    verts.append((0.0, 0.0, 0.90))
    last = 3 * n
    for i in range(n):
        faces.append([last + i, last + (i + 1) % n, top])
    return _kit_mesh("Kit_Barrel", verts, faces, coll, smooth=True)


def kit_crate(coll):
    """A box with no underside. Flat -- a crate is made of boards."""
    w, d, h = 0.70, 0.70, 0.55
    verts = [(-w/2, -d/2, 0), (w/2, -d/2, 0), (w/2, d/2, 0), (-w/2, d/2, 0),
             (-w/2, -d/2, h), (w/2, -d/2, h), (w/2, d/2, h), (-w/2, d/2, h)]
    faces = [[4,5,6,7], [0,1,5,4], [1,2,6,5], [2,3,7,6], [3,0,4,7]]
    return _kit_mesh("Kit_Crate", verts, faces, coll, smooth=False)


def kit_sack(coll):
    """Six sides, slumped, smooth. Grain and meal, the thing a hold is
    actually full of."""
    # Four rings for the same reason as the cask: three made a ball. A sack
    # sits WIDE at the bottom and gathers at the neck.
    n = 6
    rings = [_ring(n, 0.20, 0.00), _ring(n, 0.32, 0.18),
             _ring(n, 0.26, 0.38), _ring(n, 0.14, 0.52)]
    verts = [v for r in rings for v in r]
    faces = _tube_faces(rings, n)
    top = len(verts)
    verts.append((0.0, 0.0, 0.58))
    last = 3 * n
    for i in range(n):
        faces.append([last + i, last + (i + 1) % n, top])
    return _kit_mesh("Kit_Sack", verts, faces, coll, smooth=True)


def kit_seachest(coll):
    """A crew member's chest. No underside."""
    w, d, h = 0.90, 0.45, 0.40
    verts = [(-w/2, -d/2, 0), (w/2, -d/2, 0), (w/2, d/2, 0), (-w/2, d/2, 0),
             (-w/2, -d/2, h), (w/2, -d/2, h), (w/2, d/2, h), (-w/2, d/2, h)]
    faces = [[4,5,6,7], [0,1,5,4], [1,2,6,5], [2,3,7,6], [3,0,4,7]]
    return _kit_mesh("Kit_SeaChest", verts, faces, coll, smooth=False)


def kit_bulkhead(coll):
    """The partition that makes a cargo bay a room -- and that a battery bay
    does NOT have, because a ship cleared for action struck her bulkheads to
    give the guns a clear run. That one difference is what makes three uses of
    the same 3 m box read as three different places.

    No top or bottom face: the deck above and the deck below are already
    there, and two surfaces in the same plane is the whole thing we are
    avoiding."""
    w, h, t = 3.00, 2.00, 0.08
    verts = [(-w/2, -t/2, 0), (w/2, -t/2, 0), (w/2, -t/2, h), (-w/2, -t/2, h),
             (-w/2,  t/2, 0), (w/2,  t/2, 0), (w/2,  t/2, h), (-w/2,  t/2, h)]
    faces = [[0,1,2,3], [5,4,7,6], [1,5,6,2], [4,0,3,7]]
    return _kit_mesh("Kit_Bulkhead", verts, faces, coll, smooth=False)


def kit_hammock(coll):
    """A slung catenary. Single sheet -- nobody sees the top of a hammock with
    somebody in it and the underside from the same place, and it is cloth."""
    n, span, w, sag = 6, 1.90, 0.55, 0.30
    verts, faces = [], []
    for i in range(n + 1):
        t = i / n
        x = -span / 2 + span * t
        z = -sag * math.sin(math.pi * t)
        verts += [(x, -w / 2 * math.sin(math.pi * t) - 0.02, z),
                  (x,  w / 2 * math.sin(math.pi * t) + 0.02, z)]
    for i in range(n):
        a = i * 2
        faces.append([a, a + 2, a + 3, a + 1])
    ob = _kit_mesh("Kit_Hammock", verts, faces, coll, smooth=True)
    # A single sheet has ONE side. `recalc_face_normals` has nothing to work
    # out for an open surface, so it picked down -- which renders dark in
    # Blender's solid view and, worse, is CULLED in Unity: the hammocks would
    # simply not be there when seen from the deck. Point them up explicitly.
    # A hammock hangs at about chest height and is read from the side and
    # above, so one side is the right amount of geometry to pay for.
    if sum(f.normal.z for f in ob.data.polygons) < 0:
        ob.data.flip_normals()
    return ob


def kit_grating(coll):
    """A hatch grating: four bars each way with nine square holes between.

    The first version crossed flat strips over each other, which the audit
    passed and the eye would not have: at every crossing two coplanar quads
    occupied the same 0.08 m square. That is exactly the defect the whole rule
    exists to prevent, and face-centre tests cannot see it. Built as a GRID
    with the holes left out instead, it cannot happen -- there is one quad per
    cell or none.

    It has no frame of its own. The coaming around a hatch belongs to the
    deck, and a frame here would only put a second border in the same place as
    the deck's opening -- and leave T-junctions where its corners met the
    lattice."""
    bar, half = 0.08, 0.60
    hole = (2 * half - 4 * bar) / 3.0
    cuts, x = [-half], -half
    for k in range(7):
        x += bar if k % 2 == 0 else hole
        cuts.append(round(x, 5))
    is_hole = [k % 2 == 1 for k in range(7)]

    verts = [(cx, cy, 0.0) for cy in cuts for cx in cuts]
    n = len(cuts)
    faces = []
    for j in range(7):
        for i in range(7):
            if is_hole[i] and is_hole[j]:
                continue
            a = j * n + i
            faces.append([a, a + 1, a + n + 1, a + n])
    return _kit_mesh("Kit_Grating", verts, faces, coll, smooth=False)


def kit_ladder(coll):
    """Deck to deck, so it is built at the 2.30 m the three-decker measures.
    Rails keep their four long faces and lose their ends; rungs show a tread
    and a riser and nothing else."""
    h, w, r = 2.30, 0.50, 0.05
    verts, faces = [], []

    def bar(cx, cy, z0, z1, hx, hy):
        b = len(verts)
        verts.extend([(cx-hx, cy-hy, z0), (cx+hx, cy-hy, z0),
                      (cx+hx, cy+hy, z0), (cx-hx, cy+hy, z0),
                      (cx-hx, cy-hy, z1), (cx+hx, cy-hy, z1),
                      (cx+hx, cy+hy, z1), (cx-hx, cy+hy, z1)])
        faces.extend([[b,b+1,b+5,b+4], [b+1,b+2,b+6,b+5],
                      [b+2,b+3,b+7,b+6], [b+3,b,b+4,b+7]])

    bar(-w/2, 0, 0, h, r, r)
    bar( w/2, 0, 0, h, r, r)
    # A rung stops at the rail's INNER face. Run to the rail centreline and
    # half of every rung end is buried inside the rail -- geometry the eye can
    # never reach, on five rungs, on every ladder in the game.
    inner = w / 2 - r
    for k in range(5):
        z = 0.28 + k * 0.46
        b = len(verts)
        verts.extend([(-inner, -0.05, z), (inner, -0.05, z),
                      ( inner,  0.05, z), (-inner, 0.05, z),
                      (-inner, -0.05, z - 0.07), (inner, -0.05, z - 0.07)])
        faces.extend([[b, b+1, b+2, b+3], [b+4, b+5, b+1, b]])
    return _kit_mesh("Kit_Ladder", verts, faces, coll, smooth=False)


def kit_lantern(coll):
    """The paddle boat's lantern is 1,100 triangles. This one is the same
    object at a fortieth of that, and the difference is entirely thickness
    nobody can see. Panes are flat because they are panes; the bail is smooth
    because it is bent wire."""
    n, r, h = 6, 0.11, 0.22
    rings = [_ring(n, r, 0.0), _ring(n, r, h)]
    verts = [v for rg in rings for v in rg]
    faces = _tube_faces(rings, n)
    base = len(verts)
    verts.append((0.0, 0.0, -0.03))                    # bottom
    for i in range(n):
        faces.append([i, (i + 1) % n, base])
    apex = len(verts)
    verts.append((0.0, 0.0, h + 0.10))                 # cap
    for i in range(n):
        faces.append([n + (i + 1) % n, n + i, apex])
    b = len(verts)                                     # bail
    # Springs from the top of the BODY and arcs over the cap. Hung off the
    # cone's apex height instead, both ends floated 0.10 m clear of the
    # lantern in mid-air -- true in the numbers, wrong in the viewport.
    for k in range(6):
        a = math.pi * k / 5
        z = h + 0.02 + 0.10 * math.sin(a)
        verts.append((-0.015, -r * math.cos(a), z))
        verts.append(( 0.015, -r * math.cos(a), z))
    for k in range(5):
        q = b + k * 2
        faces.append([q, q + 2, q + 3, q + 1])
    return _kit_mesh("Kit_Lantern", verts, faces, coll, smooth=True)


def kit_ropecoil(coll):
    """Genuinely round in both axes, so it is the one piece that costs real
    triangles. Eight by four and smooth is the floor before it reads as a
    hexagon lying on the deck."""
    maj, mnr, N, M = 0.35, 0.07, 8, 4
    verts, faces = [], []
    for i in range(N):
        a = 2 * math.pi * i / N
        for j in range(M):
            b = 2 * math.pi * j / M
            rr = maj + mnr * math.cos(b)
            verts.append((rr * math.cos(a), rr * math.sin(a),
                          mnr * math.sin(b) * 0.55 + mnr * 0.55))
    for i in range(N):
        for j in range(M):
            a0, a1 = i * M, ((i + 1) % N) * M
            j1 = (j + 1) % M
            faces.append([a0 + j, a1 + j, a1 + j1, a0 + j1])
    return _kit_mesh("Kit_RopeCoil", verts, faces, coll, smooth=True)


KIT = [kit_barrel, kit_crate, kit_sack, kit_seachest, kit_bulkhead,
       kit_hammock, kit_grating, kit_ladder, kit_lantern, kit_ropecoil]


def build_kit(x0=0.0, y0=-18.0, pitch=1.6):
    """Build every piece, stand them in a row, and GATE them.

    The row is the point: the kit is checked the way `HullLab` checks hulls,
    by standing the whole set in one photograph beside each other at true
    scale, so a piece that is too dense or too crude is visible against its
    neighbours rather than in isolation."""
    coll = bpy.data.collections.get(KIT_COLL)
    if coll is None:
        coll = bpy.data.collections.new(KIT_COLL)
        bpy.context.scene.collection.children.link(coll)
    out = []
    for i, fn in enumerate(KIT):
        ob = fn(coll)
        ob.location = (x0 + i * pitch, y0, 0.0)
        me = ob.data
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        out.append((ob.name, tris, len(me.vertices), len(me.loops),
                    any(p.use_smooth for p in me.polygons)))
    return out


def kit_gun(coll):
    """A carriage gun, built once for every calibre.

    Calibre is uniform SCALE -- 0.75x for a 4-pounder, 1.0 for a 9, 1.25 for
    an 18 -- which is honest rather than lazy: real guns of different weight
    of shot are close to geometrically similar, because they are all sized off
    the bore. Trucks keep their rims and lose their faces; they stand on a
    deck between two cheeks and no camera ever gets to the ends of them."""
    verts, faces = [], []

    def tube(rings, n, cap_last=False, cap_first=False):
        b = len(verts)
        for r in rings:
            verts.extend(r)
        faces.extend(_tube_faces(rings, n, base=b))
        if cap_first:
            c = len(verts); verts.append((0.0, 0.0, rings[0][0][2]))
            for i in range(n):
                faces.append([b + (i + 1) % n, b + i, c])
        if cap_last:
            o = b + (len(rings) - 1) * n
            c = len(verts); verts.append((0.0, 0.0, rings[-1][0][2]))
            for i in range(n):
                faces.append([o + i, o + (i + 1) % n, c])
        return b

    # --- barrel: breech to muzzle, lying along +X, so build in Z and rotate
    n = 10
    prof = [(0.00, 0.115), (0.28, 0.105), (1.20, 0.078), (1.42, 0.092)]
    rings = [[(z, r * math.cos(2*math.pi*i/n), r * math.sin(2*math.pi*i/n))
              for i in range(n)] for (z, r) in prof]
    b = len(verts)
    for r in rings:
        verts.extend(r)
    faces.extend(_tube_faces(rings, n, base=b))
    for (o, flip) in ((b, True), (b + 3 * n, False)):     # breech and muzzle
        c = len(verts)
        verts.append((prof[0][0] - 0.06 if flip else prof[-1][0], 0.0, 0.0))
        for i in range(n):
            f = [o + (i + 1) % n, o + i, c] if flip else [o + i, o + (i + 1) % n, c]
            faces.append(f)

    def box(x0, x1, y0, y1, z0, z1, skip=()):
        s = len(verts)
        verts.extend([(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),
                      (x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)])
        q = {"bottom":[s,s+3,s+2,s+1], "top":[s+4,s+5,s+6,s+7],
             "front":[s,s+1,s+5,s+4], "right":[s+1,s+2,s+6,s+5],
             "back":[s+2,s+3,s+7,s+6], "left":[s+3,s,s+4,s+7]}
        for k, f in q.items():
            if k not in skip:
                faces.append(f)

    # --- carriage: two cheeks and a bed. No bottoms: it stands on a deck.
    for sy in (-1, 1):
        box(-0.34, 0.46, sy*0.20 - 0.05, sy*0.20 + 0.05, -0.38, -0.10,
            skip=("bottom",))
    box(-0.30, 0.42, -0.17, 0.17, -0.34, -0.26, skip=("bottom",))

    # --- four trucks, rims only
    for sx in (-0.24, 0.34):
        for sy in (-0.26, 0.26):
            rr = [[(sx + 0.09*math.cos(2*math.pi*i/6), sy + w,
                    -0.47 + 0.09*math.sin(2*math.pi*i/6)) for i in range(6)]
                  for w in (-0.05, 0.05)]
            s = len(verts)
            for r in rr: verts.extend(r)
            faces.extend(_tube_faces(rr, 6, base=s))

    return _kit_mesh("Kit_Gun", verts, faces, coll, smooth=False)


KIT.append(kit_gun)


# ============================================================= FURNISH ======
# Places the kit into measured cells. This is a Blender-side PREVIEW of what
# Unity will do at runtime with instanced prefabs -- the point of building it
# here is that the aesthetic can be judged before any of it is wired up.
#
# Every placement links the SAME mesh data. Ten barrels are one mesh and ten
# objects, which is Law 4 made literal: improving the cask improves every cask
# in the game at once.

def _place(name, src, coll, loc, rot=(0, 0, 0), scale=1.0, parent=None):
    """A placement is a new OBJECT on the SAME mesh data -- never a copy.

    Parented to the hull, because a cell empty's `location` is hull-LOCAL and
    the hulls stand at their own offsets in the scene. Reading `location` and
    linking to a collection put 225 pieces of furniture in a neat ship-shaped
    pile at the world origin, a hundred metres from the ship."""
    ob = bpy.data.objects.new(name, src.data)      # shared mesh, not a copy
    coll.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = rot
    ob.scale = scale if hasattr(scale, "__len__") else (scale, scale, scale)
    if parent is not None:
        ob.parent = parent          # inverse stays identity -- see emit_bays
    return ob


def furnish_cell(cell, use, kit, coll, rng, parent=None):
    """Fill one cell. Three uses, three completely different reads out of the
    same 3 m box -- which is the whole aesthetic argument for bays."""
    x, z = cell.location.x, cell.location.z
    half, head = cell.scale.y, cell.scale.z * 2.0
    floor = z - cell.scale.z
    out = []

    if use == "hold":
        # Bulkheads fore and aft: a cargo bay is a ROOM, and this is the one
        # line that makes it read as one. The panel is authored 3.0 x 2.0, so
        # it is SCALED to the cell it actually closes -- the measured clear
        # beam and the measured headroom. Left unscaled it spanned half the
        # brig's hold and stopped a metre under her deck.
        for sx in (-1, 1):
            out.append(_place("Fx_Bulkhead", kit["Kit_Bulkhead"], coll,
                              (x + sx * (BAY_PITCH * 0.5 - 0.04), 0.0, floor),
                              rot=(0, 0, math.radians(90)), parent=parent,
                              scale=(half * 2.0 / 3.0, 1.0, head / 2.0)))
        cols = max(1, int((half * 2 - 0.9) / 0.95))
        for iy in range(cols):
            y = -half + 0.75 + iy * 0.95
            if abs(y) > half - 0.55: continue
            for ix in range(3):
                xx = x - 0.90 + ix * 0.90
                layers = 2 if head > 2.0 else 1
                for L in range(layers):
                    pick = ("Kit_Barrel", "Kit_Crate", "Kit_Sack")[(ix + iy + L) % 3]
                    hgt = {"Kit_Barrel": 0.90, "Kit_Crate": 0.55, "Kit_Sack": 0.58}[pick]
                    out.append(_place("Fx_" + pick, kit[pick], coll,
                                      (xx, y, floor + L * hgt), parent=parent,
                                      rot=(0, 0, rng.uniform(-0.3, 0.3))))
    elif use == "battery":
        # No bulkheads at all -- cleared for action, a clear run for recoil.
        for sy in (-1, 1):
            # The barrel is authored along +X; a gun points OUTBOARD, through
            # the port its bay owns. Left at zero the whole battery aimed
            # fore-and-aft down the length of the ship.
            out.append(_place("Fx_Gun", kit["Kit_Gun"], coll,
                              (x, sy * (half - 0.62), floor + 0.47), parent=parent,
                              rot=(0, 0, math.radians(90 * sy))))
            out.append(_place("Fx_RopeCoil", kit["Kit_RopeCoil"], coll,
                              (x + 1.0, sy * (half - 0.5), floor), parent=parent))
    elif use == "quarters":
        rows_ = max(1, int((half * 2 - 0.6) / 0.75))
        for iy in range(rows_):
            y = -half + 0.6 + iy * 0.75
            if abs(y) > half - 0.4: continue
            out.append(_place("Fx_Hammock", kit["Kit_Hammock"], coll,
                              (x, y, floor + head * 0.66), parent=parent))
            out.append(_place("Fx_SeaChest", kit["Kit_SeaChest"], coll,
                              (x + 0.8, y, floor), rot=(0, 0, math.radians(90)), parent=parent))
        out.append(_place("Fx_Lantern", kit["Kit_Lantern"], coll,
                          (x - 1.0, 0.0, floor + head - 0.42), parent=parent))
    return out


FURNISH_COLL = "SeaSick_Furnish"


def clear_furnish():
    coll = bpy.data.collections.get(FURNISH_COLL)
    if coll is None:
        return 0
    n = len(coll.objects)
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    return n


def furnish(hull_name, plan, seed=3):
    """`plan` maps "<BayLabel>_<Tier>" -> "hold" | "battery" | "quarters".

    Returns the triangle cost, which is the number that decides whether this
    whole approach is affordable."""
    import random
    rng = random.Random(seed)
    coll = bpy.data.collections.get(FURNISH_COLL)
    if coll is None:
        coll = bpy.data.collections.new(FURNISH_COLL)
        bpy.context.scene.collection.children.link(coll)
    clear_furnish()
    kit = {o.name: o for o in bpy.data.collections[KIT_COLL].objects}
    total, placed = 0, 0
    for key, use in plan.items():
        cell = bpy.data.objects.get("%s_Bay%s" % (hull_name, key))
        if cell is None:
            print("   no cell:", key); continue
        objs = furnish_cell(cell, use, kit, coll, rng, parent=cell.parent)
        placed += len(objs)
        for o in objs:
            total += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return placed, total


# ============================================================== LADDER ======
# The twenty nodes of the modular ladder, and the interpolator that turns the
# four hand-authored hulls into every hull between them.
#
# The four anchors stay exactly as they were drawn -- a node that lands on an
# anchor's dimensions must come out as that anchor, or the ladder has quietly
# redrawn hulls that were already measured and signed off.
#
# Shape SCALARS (fullness, midship, the run coefficients, sheer, planking) are
# lerped between the two anchors that bracket the node by length. Structural
# LISTS -- gun decks, ports, masts, deck levels -- are DERIVED, because
# interpolating a 7-entry port list against a 12-entry one is meaningless and
# because the rules that generate them are known and exact.

#            L      B      D    tiers decked  move        label
NODES = [
    ( 9.00,  2.90,  1.45,  1, False, "start",    "Fishing skiff"),
    (11.50,  2.90,  1.45,  1, False, "lengthen", "Lengthened skiff"),
    (11.50,  3.70,  1.45,  1, False, "girdle",   "Girdled skiff"),
    (11.50,  3.70,  1.95,  1, True,  "raise",    "Decked boat"),
    (15.00,  3.70,  1.95,  1, True,  "lengthen", "Long boat"),
    (15.00,  4.80,  1.95,  1, True,  "girdle",   "Beamy boat"),
    (15.00,  4.80,  2.55,  1, True,  "raise",    "Coastal sloop"),
    (19.50,  4.80,  2.55,  1, True,  "lengthen", "Long sloop"),
    (19.50,  6.10,  2.55,  1, True,  "girdle",   "Beamy sloop"),
    (19.50,  6.10,  3.60,  2, True,  "raise",    "First battery"),
    (26.00,  6.10,  3.60,  2, True,  "lengthen", "Long battery"),
    (26.00,  7.80,  3.60,  2, True,  "girdle",   "Beamy battery"),
    (26.00,  7.80,  5.20,  2, True,  "raise",    "Brig"),
    (32.50,  7.80,  5.20,  2, True,  "lengthen", "Long brig"),
    (32.50, 10.00,  5.20,  2, True,  "girdle",   "Beamy brig"),
    (32.50, 10.00,  8.00,  3, True,  "raise",    "Two-decker"),
    (41.00, 10.00,  8.00,  3, True,  "lengthen", "Long two-decker"),
    (41.00, 13.00,  8.00,  3, True,  "girdle",   "Beamy two-decker"),
    (46.00, 13.00,  8.00,  3, True,  "lengthen", "Full length"),
    (46.00, 13.00, 12.40,  4, True,  "raise",    "Three-decker"),
]

# Which authored hull each node is allowed to BE. Everything else is derived.
ANCHOR_AT = {0: "T2_Skiff", 6: "T3_Sloop", 12: "T4_Brig", 19: "T5_ShipOfTheLine"}

# Displacement per node, filled in by `build_ladder` from the hull's own
# stations. The manifest refuses to write without it: a guessed mass is how the
# paddle steamer floated 0.57 m high for a fortnight.
NODE_VOLUME = {}
NODE_HYDRO = {}

# Deck to deck, measured off the three-decker's own `deck_levels` and holding
# to the centimetre across all three of her gaps. It is also what the kit is
# built for, so it is a constant of the whole system rather than a per-hull
# number.
DECK_PITCH = 2.30

# Scalars that describe the SHAPE and are safe to interpolate.
LERP_KEYS = ("midship", "fullness", "aft_end", "aft_a", "aft_b", "aft_rise",
             "fwd_end", "fwd_a", "fwd_b", "fwd_rise", "rake_k",
             "station_end", "station_mid")
# Scalars that are a FRACTION of a leading dimension, lerped as that fraction.
FRAC_OF_L = ("rake_fwd", "rake_aft")
FRAC_OF_D = ("sheer_fwd", "sheer_aft", "planking", "plank_t")
ROUND_KEYS = ("stations", "n_under", "n_top", "deck_spans")


def _anchor_specs(g=None):
    g = g or load_hulls()
    by = {p["name"]: p for p in g["FLEET"]}
    return [by[ANCHOR_AT[i]] for i in sorted(ANCHOR_AT)], sorted(ANCHOR_AT)


def _bracket(L, anchors):
    """The two authored hulls this length falls between, and the blend."""
    lens = [a["length"] for a in anchors]
    if L <= lens[0]:
        return anchors[0], anchors[0], 0.0
    if L >= lens[-1]:
        return anchors[-1], anchors[-1], 0.0
    for i in range(len(lens) - 1):
        if lens[i] <= L <= lens[i + 1]:
            t = (L - lens[i]) / (lens[i + 1] - lens[i])
            return anchors[i], anchors[i + 1], t
    return anchors[-1], anchors[-1], 0.0


def node_params(i, g=None):
    """The full parameter dict for ladder node `i`, ready for `build_hull`."""
    g = g or load_hulls()
    L, B, D, tiers, decked, move, label = NODES[i]
    anchors, _ = _anchor_specs(g)

    # An anchor node IS its authored hull. Copy it whole and change nothing:
    # these four were drawn, measured, floated and topology-gated already.
    for idx, nm in ANCHOR_AT.items():
        if idx == i:
            p = dict(next(a for a in anchors if a["name"] == nm))
            p["name"] = "N%02d_%s" % (i, nm.split("_", 1)[1])
            p["node"], p["move"], p["label"] = i, move, label
            # Her authored `mast_x` is a leftover -- `plan_rig` restates it
            # against the built hull -- so the count the manifest ships comes
            # from the same rule the other sixteen rungs use.
            p["masts"] = g["mast_count"](L)
            # Her authored `port_x` is also her BAY grid, which is what
            # `bay_centres` has always read her cells off. Stated once here so
            # bays and ports stop being the same list by coincidence.
            if p.get("port_x"):
                p["bay_x"] = list(p["port_x"])
                # Stated per ROW even though every row is the same, because
                # the manifest counts her whole side off this and an anchor
                # that leaves it unset ships a first-rate with no ports.
                p["port_rows_x"] = [list(p["port_x"])
                                    for _ in p.get("gun_decks") or []]
            return p

    a, b, t = _bracket(L, anchors)
    p = {}
    for k in LERP_KEYS:
        if k in a or k in b:
            p[k] = lerp(a.get(k, b.get(k, 0.0)), b.get(k, a.get(k, 0.0)), t)
    for k in ROUND_KEYS:
        if k in a or k in b:
            p[k] = int(round(lerp(a.get(k, b.get(k, 3)), b.get(k, a.get(k, 3)), t)))

    draft_ratio = lerp(a["draft"] / a["depth"], b["draft"] / b["depth"], t)
    draft = D * draft_ratio
    p.update(name="N%02d_%s" % (i, label.replace(" ", "")), tier=i, label=label,
             node=i, move=move, length=L, beam=B, depth=D, draft=draft)
    z_rail = D - draft

    for k in FRAC_OF_L:
        p[k] = L * lerp(a[k] / a["length"], b[k] / b["length"], t)
    for k in FRAC_OF_D:
        if k in a and k in b:
            p[k] = D * lerp(a[k] / a["depth"], b[k] / b["depth"], t)

    # --- topside: control heights normalised by the rail, then rescaled -----
    ta, tb = a["topside"], b["topside"]
    ra, rb = a["depth"] - a["draft"], b["depth"] - b["draft"]
    p["topside"] = [(z_rail * lerp(ca[0] / ra, cb[0] / rb, t),
                     lerp(ca[1], cb[1], t)) for ca, cb in zip(ta, tb)]

    # --- decks: DERIVED, never lerped ---------------------------------------
    # The top deck stands one bulwark below the rail and every deck below it is
    # DECK_PITCH down. Checked against both hulls that have decks: the brig's
    # 2.70 rail less a 1.45 bulwark is 1.25, her authored deck_z exactly; the
    # three-decker's 7.00 less 1.85 is 5.15, and 2.30 steps give 2.85 and 0.55
    # -- her authored `deck_levels`, to the centimetre.
    # A bulwark is not a free choice once the hull carries guns: the sill
    # stands 0.70 above the deck, the port is `port_height` tall, and a strip
    # of capping rail has to survive above it. That is the whole reason the
    # brig's bulwark is 1.45 -- 0.70 + 0.58 + 0.17 -- and it is why a small
    # hull with a battery has a deep bulwark and a shallow hold. Un-gunned
    # hulls get the sloop's much lighter rail instead.
    ph = lerp(0.58, 0.80, min(1.0, max(0.0, (L - 26.0) / 20.0)))
    bulwark = (0.70 + ph + 0.17) if tiers > 1 else 0.45 * (D / 2.55)

    if not decked:
        p["deck_z"] = None
        p["gun_decks"] = []
        p["inner_floor"] = -0.30 * (D / 1.45)
    else:
        top = z_rail - bulwark
        p["deck_z"] = top
        hold = -draft + max(0.20, draft * 0.14)

        # Gun rows are tiers - 1, stacked DOWN from the weather deck. Checked
        # against both: the brig is 2 tiers and has ONE row, on her weather
        # deck; the three-decker is 4 and has three, at 5.15 / 2.85 / 0.55.
        rows = [top - k * DECK_PITCH for k in range(tiers - 1)]
        p["gun_decks"] = sorted(r + 0.72 for r in rows)

        if tiers >= 3:
            # Only a hull with a deck BELOW her weather deck has deck_levels;
            # the brig has none and must not be given any. Emitting them at two
            # tiers invented a gun deck 2.30 m down, which on a 3.60 m hull
            # landed BELOW her own hold floor.
            p["deck_levels"] = ([("Hold", hold)] +
                                [(n, z) for n, z in
                                 zip(["LowerGunDeck", "MiddleGunDeck",
                                      "UpperGunDeck"][3 - len(rows):],
                                     sorted(rows))])
            # The inner planking must reach the lowest deck that carries guns,
            # or those ports are holes in a single sheet you see through.
            p["inner_floor"] = min(rows)

    # --- ports: one per gun she can actually MAN -----------------------------
    #
    # `bay_x` is the grid the ports are cut in, and it is exactly the list that
    # used to BE `port_x` -- same count, same pitch, same centre -- so her
    # cells do not move now that her ports are a subset of them. It is written
    # only for a hull that carries guns, because an open boat's bays have
    # always come from `bay_centres`'s own rule and there is no reason to
    # shift where her crew stand.
    if p["gun_decks"]:
        n_bays = max(1, round(L / BAY_DIVISOR))
        span = L * 0.69
        pitch = span / max(1, n_bays - 1)
        centre = L * 0.02
        p["bay_x"] = [round(centre - span / 2 + k * pitch, 2)
                      for k in range(n_bays)]
        # One port per bay per gun deck. `bay_x` is the same list this used to
        # write straight into `port_x` -- same count, pitch and centre -- so
        # her cells are where they always were, and the ports are back on top
        # of them.
        p["port_x"] = list(p["bay_x"])
        p["port_rows_x"] = [list(p["bay_x"]) for _ in p["gun_decks"]]
        p["port_height"] = ph
        p["port_width"] = lerp(0.68, 0.90, min(1.0, max(0.0, (L - 26.0) / 20.0)))
    else:
        p["port_x"] = []
        p["port_rows_x"] = []

    # --- rig -----------------------------------------------------------------
    # Only the COUNT here, and only because `ladder.json` has to carry it for
    # `ShipFit`'s sail-plan gate. Where the masts stand and how tall they are
    # is `plan_rig`, which is run against the hull once she is lofted -- a mast
    # is stepped in a ship, so it is placed off the ship and not off a fraction
    # of LOA. Two tables for one rig is exactly how the paddle steamer ended up
    # at a third of her displacement.
    p["masts"] = g["mast_count"](L)
    p["bowsprit"] = L >= 13.0
    p["rudder"] = True
    p["keel_batten"] = True
    return p


def ladder_specs(g=None):
    g = g or load_hulls()
    return [node_params(i, g) for i in range(len(NODES))]


def clear_ladder():
    """Only the ladder's own collections. The four authored hulls stay."""
    n = 0
    for coll in list(bpy.data.collections):
        if not coll.name.startswith("N") or not coll.name[1:3].isdigit():
            continue
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
            n += 1
        bpy.data.collections.remove(coll)
    return n


def build_ladder(nodes=None, gate=True, lay_out=True):
    """Bake every node on the ladder, then GATE every one of them.

    Twenty hulls is twenty bakes on disk and still exactly one mesh in memory
    at runtime -- the ladder costs no frame time over the five-hull fleet. What
    it does cost is twenty chances for the interpolator to produce a hull that
    measures right and looks wrong, which is why nothing here ships without
    passing the generator's own `report_topology`."""
    g = load_hulls()
    clear_ladder()
    mat = g["wood_material"]()
    idx = range(len(NODES)) if nodes is None else nodes
    built, report = [], []

    for i in idx:
        p = node_params(i, g)
        c = g["get_collection"](p["name"])
        hull, stations, tags = g["build_hull"](p, c)
        # Step her masts against the hull that was just lofted, before
        # anything asks where they are. See `plan_rig`: this is Kevin's brig
        # rig, stated as a rule, and it is what makes twenty rungs read as one
        # ship instead of as one hand-arranged ship and nineteen strangers.
        g["plan_rig"](p, stations, tags)
        objs = [hull]
        if p.get("deck_z") is not None:
            objs.append(g["build_deck"](p, stations, c, p["deck_z"], tags))
        objs.extend(g["build_fittings"](p, stations, c, tags))
        objs.extend(g["build_trim"](p, stations, c, tags, hull))
        objs.extend(g["build_stern"](p, stations, c, tags))
        objs.extend(g["build_sails"](p, c))
        g["build_port_anchors"](p, stations, tags, c, hull)
        # Material AND swatches, from the generator, so every rung on the
        # ladder is planked by the same rule the five authored hulls are.
        g["dress"](objs, p)
        # battery-level empties, the same way the generator writes them
        for label, z in p.get("deck_levels", []):
            e = bpy.data.objects.new(p["name"] + "_" + label, None)
            e.empty_display_type = 'SINGLE_ARROW'
            e.empty_display_size = p["beam"] * 0.35
            e.location = (0.0, 0.0, z)
            c.objects.link(e)
            e.parent = hull
        # Displacement while the stations are still in hand. Unity needs an
        # HONEST mass per node -- the paddle steamer floated 0.57 m high for
        # weeks on a number carried up from an older boat by a cube law.
        v = g["_volume"](stations, 0.0)
        p["volume_m3"] = v
        p["mass_t"] = 1.025 * v
        NODE_VOLUME[i] = v
        NODE_HYDRO[i] = hydro_extras(g, stations, p["draft"])
        built.append((p, objs, c, stations))

    if lay_out:
        y = 40.0
        for p, objs, c, _ in built:
            y += p["beam"] * 0.5 + 3.0
            for o in objs:
                # ROOTS only. Port lids are children of the hull with their
                # origin on their own hinge, and setting y on them would move
                # every lid onto the centreline.
                if o.parent is None:
                    o.location.y = y
            c["lineup_y"] = y
            y += p["beam"] * 0.5

    if gate:
        audit = g["audit"]
        for p, objs, c, _ in built:
            worst = {}
            for o in objs:
                r = audit(o)
                for k in ("coincident_verts", "zero_len_edges", "zero_area_faces",
                          "duplicate_faces", "overlapping_faces", "ngons",
                          "non_manifold"):
                    if r.get(k):
                        worst[k] = worst.get(k, 0) + r[k]
            # Count the EVALUATED mesh. Every hull is a half with a live
            # Mirror, so counting `o.data.polygons` reports exactly half the
            # ship -- which looks like a sensational optimisation and is a
            # measuring error.
            dg = bpy.context.evaluated_depsgraph_get()
            tris = 0
            for o in objs:
                ev = o.evaluated_get(dg)
                me = ev.to_mesh()
                tris += sum(len(f.vertices) - 2 for f in me.polygons)
                ev.to_mesh_clear()
            report.append((p["node"], p["label"], p["length"], tris, worst))
    return report


UNITY_ART = ("/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/Art/"
             "Ship/Hulls")
# Where the ladder actually LIVES in the game. `Shipyard` does
# `Resources.Load(n.ResourcePath)` and that resolves under Resources, so this
# is the only directory an exported hull can be loaded from -- exporting into
# the art folder and copying by hand was a step nobody could see going wrong.
LADDER_DIR = ("/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/"
              "Resources/Ladder")


def export_ladder(out_dir=LADDER_DIR, nodes=None):
    """One FBX per node, with the generator's own export settings.

    Those settings are not negotiable and the reasons are in HULLS.md: the mesh
    nodes arrive at scale 100 with vertices at 1/100 unless
    `apply_scale_options='FBX_SCALE_ALL'` and `bake_space_transform` are set,
    and the -90 degree yaw is what makes the bow point +Z in Unity instead of
    aft. Copying the flags without copying the yaw ships a fleet that sails
    backwards."""
    import os
    os.makedirs(out_dir, exist_ok=True)
    vl = bpy.context.view_layer
    for o in bpy.data.objects:
        o.hide_set(False)
    done = []
    names = [node_params(i)["name"] for i in
             (range(len(NODES)) if nodes is None else nodes)]
    for cname in names:
        coll = bpy.data.collections.get(cname)
        if coll is None:
            continue
        objs = list(coll.objects)
        roots = [o for o in objs if o.parent is None]
        saved = {o.name: (tuple(o.location), tuple(o.rotation_euler))
                 for o in roots}
        for o in bpy.data.objects:
            o.select_set(False)
        for o in roots:
            o.location = (0.0, 0.0, 0.0)
            o.rotation_euler = (0.0, 0.0, math.radians(-90.0))
        for o in objs:
            o.select_set(True)
        vl.objects.active = objs[0]
        vl.update()
        fname = cname.lower() + ".fbx"
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(out_dir, fname), use_selection=True,
            object_types={'MESH', 'EMPTY'}, use_mesh_modifiers=True,
            mesh_smooth_type='FACE', global_scale=1.0, apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
            axis_forward='-Z', axis_up='Y', use_triangles=False,
            # STRIP, not COPY: the palette is already a Unity asset with its
            # own import settings (Point, no mips, uncompressed), and a texture
            # copied in beside the mesh would be a second one of it inside
            # Resources -- shipped in the build, and the wrong one to edit.
            add_leaf_bones=False, path_mode='STRIP')
        for o in roots:
            o.location, o.rotation_euler = saved[o.name]
        vl.update()
        done.append(fname)
    return done


MANIFEST = ("/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/Art/"
            "Ship/Hulls/ladder.json")
# What `ShipLadder` actually reads. Resources cannot serve a .json, so the
# same bytes go down twice under two extensions -- written together, in one
# call, from one dict, so there is no window in which they disagree. The old
# arrangement asked a human to copy one to the other and said so in an error
# message, which is a step that gets skipped exactly once.
MANIFEST_RESOURCE = ("/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/"
                     "Resources/Ladder/ladder.txt")


def write_manifest(path=MANIFEST):
    """Everything Unity needs to drive the ladder, in one file.

    Written from the SAME `node_params` the meshes were baked from, so the
    game cannot disagree with the geometry about how many bays a hull has.
    Bays and tiers are recomputed here rather than copied, because they are
    derived quantities and a copied number is a number that can drift."""
    import json, os
    if len(NODE_VOLUME) < len(NODES):
        raise RuntimeError(
            "run build_ladder() first -- displacement comes from the built "
            "stations, and a manifest without it would ship a guessed mass")
    g = load_hulls()
    out = {"deck_pitch": DECK_PITCH, "bay_pitch": BAY_PITCH,
           "bay_divisor": BAY_DIVISOR, "nodes": []}
    for i in range(len(NODES)):
        p = node_params(i, g)
        tiers = tier_plan(p)
        bays = bay_centres(p)
        labels = bay_labels(len(bays))
        out["nodes"].append({
            "node": i, "name": p["name"], "label": p["label"],
            "move": p["move"], "mesh": p["name"].lower() + ".fbx",
            "length": round(p["length"], 3), "beam": round(p["beam"], 3),
            "depth": round(p["depth"], 3), "draft": round(p["draft"], 3),
            "loa_over_beam": round(p["length"] / p["beam"], 3),
            "probe_lift": round(0.55 * p["draft"], 3),
            "volume_m3": round(NODE_VOLUME[i], 3),
            "mass_kg": round(1025.0 * NODE_VOLUME[i], 1),
            "waterplane_m2": NODE_HYDRO[i]["waterplane_m2"],
            "tpc_t_per_cm": NODE_HYDRO[i]["tpc_t_per_cm"],
            "kb_above_keel_m": NODE_HYDRO[i]["kb_above_keel_m"],
            "bm_m": NODE_HYDRO[i]["bm_m"],
            "km_above_keel_m": NODE_HYDRO[i]["km_above_keel_m"],
            "volume_curve_z": [z for z, _ in NODE_HYDRO[i]["volume_curve"]],
            "volume_curve_v": [v for _, v in NODE_HYDRO[i]["volume_curve"]],
            "bays": len(bays), "tiers": len(tiers),
            "cells": len(bays) * len(tiers),
            "bay_labels": labels,
            "bay_x": [round(x, 3) for x in bays],
            "tier_names": [t[0] for t in tiers],
            "tier_floor": [round(t[1], 3) for t in tiers],
            "tier_ceiling": [round(t[2], 3) for t in tiers],
            "gun_rows": len(p.get("gun_decks") or []),
            # The battery she can carry, and where the holes for it are. It
            # used to be `len(port_x)`, which was the count on ONE row and so
            # never the answer to "how many guns" on a hull with three. It is
            # the whole side now, and `Shipyard` refuses a gun past it -- a
            # gun with no port is a carriage run out at solid planking.
            "ports_per_side": sum(len(r) for r in (p.get("port_rows_x") or [])),
            "ports_per_row": [len(r) for r in (p.get("port_rows_x") or [])],
            "masts": p.get("masts") or len(p.get("mast_x") or []),
        })
    for dest in (path, MANIFEST_RESOURCE):
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "w") as f:
            json.dump(out, f, indent=1)
    return path, len(out["nodes"])


# Same reason as LADDER_DIR: `Shipyard` loads the kit with
# `Resources.Load("Kit/seasick_kit")`, so under Resources is the only place it
# can be loaded from, and exporting anywhere else needs a human to copy it.
KIT_FBX = ("/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/"
           "Resources/Kit/seasick_kit.fbx")


def export_kit(path=KIT_FBX):
    """The whole kit in ONE file. Eleven prefabs come out of eleven meshes in
    one import, and every placement in the game is an instance of one of
    them."""
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    coll = bpy.data.collections[KIT_COLL]
    for o in bpy.data.objects:
        o.hide_set(False)
        o.select_set(False)
    # The scale reference is a 1.7 m block for judging the kit against a
    # crew member in the viewport. It is a RULER, not an asset, and it has no
    # business being imported into the game.
    objs = [o for o in coll.objects
            if o.type == 'MESH' and "ScaleRef" not in o.name]
    saved = {o.name: tuple(o.location) for o in objs}
    for o in objs:
        o.location = (0.0, 0.0, 0.0)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        use_mesh_modifiers=True, mesh_smooth_type='FACE', global_scale=1.0,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True, axis_forward='-Z', axis_up='Y',
        use_triangles=False, add_leaf_bones=False, path_mode='STRIP')
    for o in objs:
        o.location = saved[o.name]
    return path, len(objs)


# ========================================================= HYDROSTATICS =====
# Beyond displacement: the numbers that decide how a hull ANSWERS.
#
# Volume alone tells you what she weighs. It does not tell you how far she
# sinks when you load her, or how hard she resists being heeled over by a
# broadside. Those come from the WATERPLANE — the shape she cuts at the
# surface — and they are what make a small ship feel small.
#
#   Aw   waterplane area. Load her and she sinks by (added mass / (Aw x rho)).
#        Expressed per centimetre this is TPC, and it is why three tonnes of
#        cargo barely moves a brig and puts a skiff's rail near the water.
#   It   second moment of the waterplane about the centreline. Divided by
#        displaced volume it gives BM, the metacentric radius — the whole of a
#        hull's form stability, and it goes as the CUBE of her beam.
#   KB   height of the centre of buoyancy above the keel.
#
#   KM = KB + BM, and GM = KM - KG. GM is the one number that says whether she
#   is stiff or tender: righting moment = mass x g x GM x sin(heel).
#
# All three are integrals over the same station rings the hull was lofted from,
# so they cannot disagree with the mesh.


def _half_breadth_at(ring, z):
    """Half-breadth where a station crosses height `z`, by interpolation."""
    for k in range(len(ring) - 1):
        z0, z1 = ring[k][2], ring[k + 1][2]
        if z0 <= z <= z1 and z1 > z0:
            t = (z - z0) / (z1 - z0)
            return abs(ring[k][1] + (ring[k + 1][1] - ring[k][1]) * t)
    return abs(ring[-1][1]) if z > ring[-1][2] else abs(ring[0][1])


def _section_moment_z(ring, z_top):
    """First moment of the half-section about z = 0, up to `z_top`.

    Same trapezoid walk as the generator's `_section_area`, carrying z as
    well, so KB comes out of exactly the polygon the volume did."""
    m = 0.0
    for k in range(len(ring) - 1):
        z0, y0 = ring[k][2], abs(ring[k][1])
        z1, y1 = ring[k + 1][2], abs(ring[k + 1][1])
        if z1 <= z0 or z0 >= z_top:
            continue
        if z1 > z_top:
            t = (z_top - z0) / (z1 - z0)
            y1 = y0 + (y1 - y0) * t
            z1 = z_top
        # centroid of the trapezoid slice, weighted by its area
        a = 0.5 * (y0 + y1) * (z1 - z0)
        zc = z0 + (z1 - z0) * (y0 + 2.0 * y1) / (3.0 * max(1e-9, y0 + y1))
        m += a * zc
    return m


def hydro_extras(g, stations, draft):
    """Waterplane area, its second moment, KB — and a volume/draft curve.

    The curve is what makes sinkage EXACT rather than linear: a hull's
    waterplane widens as she settles, so the first centimetre of immersion
    costs less than the tenth. Sampled from the keel to the rail."""
    xs = [r[0][0] for r in stations]
    aw = it = 0.0
    vol_moment = 0.0
    vol = 0.0
    for i in range(len(stations) - 1):
        dx = abs(xs[i + 1] - xs[i])
        if dx <= 0:
            continue
        y0 = _half_breadth_at(stations[i], 0.0)
        y1 = _half_breadth_at(stations[i + 1], 0.0)
        aw += (y0 + y1) * dx                       # 2 x mean half-breadth x dx
        # I about the centreline of a strip 2y wide is (2/3) y^3 per unit length
        it += (2.0 / 3.0) * 0.5 * (y0 ** 3 + y1 ** 3) * dx
        a0 = g["_section_area"](stations[i], 0.0)
        a1 = g["_section_area"](stations[i + 1], 0.0)
        m0 = _section_moment_z(stations[i], 0.0)
        m1 = _section_moment_z(stations[i + 1], 0.0)
        vol += (a0 + a1) * dx                      # both sides
        vol_moment += (m0 + m1) * dx

    kb_from_wl = vol_moment / vol if vol > 1e-9 else 0.0
    curve = []
    steps = 12
    for k in range(steps + 1):
        z = -draft + (draft * 1.6) * k / steps     # keel to well above the line
        curve.append((round(z, 3), round(g["_volume"](stations, z), 3)))

    return {
        "waterplane_m2": round(aw, 3),
        "waterplane_I_m4": round(it, 3),
        # KB is reported from the KEEL, the way a naval architect writes it.
        "kb_above_keel_m": round(kb_from_wl + draft, 3),
        "bm_m": round(it / vol, 3) if vol > 1e-9 else 0.0,
        "km_above_keel_m": round(kb_from_wl + draft + (it / vol if vol > 1e-9 else 0.0), 3),
        # tonnes per centimetre immersion — the number that says how much a
        # given load moves HER in particular.
        "tpc_t_per_cm": round(aw * 1.025 / 100.0, 4),
        "volume_curve": curve,
    }
