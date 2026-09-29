# SeaSick level-1 watchtower in the wall kit v4 style (chunky logs, one bevel rule,
# rope + iron, random log tones). Exec AFTER wall_kit_v4.py, in the same namespace:
#   ns = {"__name__": "__main__"}; exec(open(".../wall_kit_v4.py").read(), ns)
#   exec(open(".../watchtower_l1_v4.py").read(), ns)
#
# Runtime contract (Scripts/World/Outpost.TowerLadder.cs, Combat/WatchtowerGun.cs,
# BuildPlan.Watchtower; same frame as art-staging/watchtower-astra-lvl1-v2):
#   metres, Blender Z-up, front/ladder toward -Y, root centred on the plot at ground;
#   fits the 2.6 x 2.6 m plot; walking surface at 4.61 m (WatchtowerGun.DeckHeight);
#   markers Ladder_Bottom (0,-1.23,0.05), Ladder_Top (0,-1.10,4.61), Lookout_Anchor (0,0,4.61);
#   meshes Footings / Posts / Walls / Platform / Ladder (Walls = palisade infill, 2026-09-28).
#   No roof or cross-bracing; the wall logs rise ~1 m above the deck as its railing.
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

DECK = 4.61
LEG = 0.36           # leg section
LEG_AT = 0.91        # leg centres at (+-0.91, +-0.91), as before (TowerTrim / TowerLegs expect ~1 m)
CAP_TOP = DECK + 1.18   # corner posts stand above the railing
RAIL_TOP = DECK + 0.64  # railing log bodies; pointed tips add ~0.3
LADDER_GAP = (-0.46, 0.51)   # front railing opening where the ladder arrives
DECK_HALF = 1.17

def tower_footings(mat, rnd):
    B = Builder()
    # rope-bound feet, like the wall posts: two wraps low on every leg, and a chunky
    # squared sill block under each foot so the tower sits on the ground, not in it
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * LEG_AT, sy * LEG_AT
            B.add(box(LEG + 0.16, LEG + 0.16, 0.16 + EMBED, bevel=BEV_WOOD), tone_at(0.15, rnd), T(x, y, -EMBED))
            for z in (0.24, 0.38):
                B.add(square_rope(LEG / 2, 0.075, z), lin(PAL["rope"]), T(x, y, 0))
    return B.finish("Footings", mat, ao_height=0.9)

def tower_posts(mat, rnd):
    B = Builder()
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * LEG_AT, sy * LEG_AT
            h = CAP_TOP + EMBED
            B.add(box(LEG, LEG, h, bevel=BEV_WOOD), wood_tone(rnd), T(x, y, -EMBED))
            B.add(pyramid_cap(LEG / 2 + 0.03, 0.07, 0.36), lin(PAL["wood_med"]), T(x, y, CAP_TOP - 0.02))
            # lashing under the deck, iron band at mid height (rivets on all four faces)
            for z in (DECK - 0.66, DECK - 0.52):
                B.add(square_rope(LEG / 2, 0.075, z), lin(PAL["rope"]), T(x, y, 0))
            bz = 2.05
            B.add(box(LEG + 0.06, LEG + 0.06, 0.22, bevel=BEV_METAL), lin(PAL["metal"]), T(x, y, bz))
            for ang in range(4):
                B.add(rivet(), lin(PAL["rivet"]),
                      T(x, y, 0) @ T(rz=ang * math.pi / 2) @ T(0, -(LEG / 2 + 0.03), bz + 0.11, rx=math.pi / 2))
    return B.finish("Posts", mat, ao_height=0.9)

def tower_platform(mat, rnd):
    """Deck inside the railing, a timber collar round the outside at deck level, a threshold
    board across the ladder opening."""
    B = Builder()
    rail_tone = tuple((a + b) / 2 for a, b in zip(lin(PAL["wood_light"]), lin(PAL["wood_med"])))
    inner = LEG_AT - STAKE_D / 2 + 0.03          # just into the railing logs' inner face
    # joists, hidden between the walls
    for sx in (-1, 1):
        B.add(box(0.24, 2 * inner, 0.26, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04),
              T(sx * (inner - 0.16), 0, DECK - 0.14 - 0.13))
    # deck: mixed-width planks running front-to-back, random tones, top at DECK
    widths = [0.30, 0.26, 0.34, 0.28, 0.32]
    k = 2 * inner / sum(widths)
    x = -inner
    for w in widths:
        w *= k
        B.add(box(w + 0.004, 2 * inner, 0.14, bevel=BEV_WOOD, z0=False), wood_tone(rnd),
              T(x + w / 2, 0, DECK - 0.07 + rnd.uniform(-0.008, 0.008)))
        x += w
    # threshold across the ladder opening, over the short logs, flush with the deck
    g0, g1 = LADDER_GAP
    B.add(box(g1 - g0 + 0.02, STAKE_D + 0.10, 0.14, bevel=BEV_WOOD, z0=False), wood_tone(rnd),
          T((g0 + g1) / 2, -LEG_AT - 0.03, DECK - 0.07))
    # collar: four beams wrapping the tower outside the logs and legs at deck level
    c = LEG_AT + LEG / 2 + 0.07
    for side in range(4):
        R = T(rz=side * math.pi / 2)
        B.add(box(2 * c + 0.16, 0.16, 0.28, bevel=BEV_WOOD, z0=False), jitter(rail_tone, rnd, 0.04),
              R @ T(0, -c, DECK - 0.20))
        # iron corner bracket + rivet
        B.add(box(0.30, 0.21, 0.34, bevel=BEV_METAL, z0=False), lin(PAL["metal"]), R @ T(c - 0.08, -c, DECK - 0.20))
        B.add(rivet(), lin(PAL["rivet"]), R @ T(c - 0.08, -c - 0.105, DECK - 0.20, rx=math.pi / 2))
    return B.finish("Platform", mat, ao_height=4.0, ao_min=0.80)

def tower_ladder(mat, rnd):
    B = Builder()
    def ly(z): return -1.22 + 0.02 * z            # the old ladder's line, so the markers still fit
    z0, z1 = -0.05, DECK + 0.30
    rail_tone = tuple((a + b) / 2 for a, b in zip(lin(PAL["wood_light"]), lin(PAL["wood_med"])))
    L = math.hypot(z1 - z0, ly(z1) - ly(z0))
    tilt = math.atan2(ly(z1) - ly(z0), z1 - z0)
    for sx in (-1, 1):
        st = box(0.15, 0.17, L, bevel=BEV_WOOD)
        B.add(st, jitter(rail_tone, rnd, 0.04), T(sx * 0.36, ly(z0), z0, rx=-tilt))
    n = 14
    for i in range(n):
        z = 0.30 + i * 0.33
        if z > DECK - 0.05:
            break
        B.add(box(0.84, 0.10, 0.10, bevel=0.025, z0=False), wood_tone(rnd), T(0, ly(z) - 0.065, z))
    # rope lashing where the ladder meets the deck edge, iron shoes at the foot
    for sx in (-1, 1):
        B.add(square_rope(0.085, 0.055, DECK - 0.30), lin(PAL["rope"]), T(sx * 0.36, ly(DECK - 0.30), 0))
        B.add(box(0.21, 0.23, 0.18, bevel=BEV_METAL), lin(PAL["metal"]), T(sx * 0.36, ly(0), -0.02))
    return B.finish("Ladder", mat, ao_height=0.9)

def tower_walls(mat, rnd):
    """Palisade infill between the legs on all four sides, ground to deck (Kevin 2026-09-28:
    "walls all the way up"), the wall's own logs and outside rope: a wall run can arrive from
    any side (the tower is squared to the first run), so it is closed all round and no run ever
    meets a hole. Ladder leans on the front. The logs run on through the deck as a pointed
    railing (Kevin: "let the logs continue a little bit as a railing"), open where the ladder arrives."""
    B = Builder()
    widths = [0.34, 0.30, 0.36, 0.31, 0.29]      # spans +-0.80, hidden 7 cm into each leg
    span = sum(widths)
    for side in range(4):
        R = T(rz=side * math.pi / 2) @ T(0, -LEG_AT, 0)
        x = -span / 2
        for i, w in enumerate(widths):
            x0, x1 = x, x + w
            in_gap = side == 0 and x1 > LADDER_GAP[0] + 0.01 and x0 < LADDER_GAP[1] - 0.01
            if in_gap:
                # under the ladder's arrival: stop flat under the deck, leaving the opening
                top = DECK - 0.30 + rnd.uniform(-0.03, 0.0)
                bm = box(w + 0.005, STAKE_D, top + EMBED, bevel=BEV_WOOD)
            else:
                # ground to railing in one log, through the deck, pointed like the wall
                h = RAIL_TOP + rnd.uniform(-0.05, 0.07)
                bm = stake(w + 0.005, STAKE_D, h + EMBED, 0.9 * w + rnd.uniform(-0.02, 0.03), rnd)
            B.add(bm, wood_tone(rnd), R @ T(x + w / 2, rnd.uniform(-0.01, 0.01), -EMBED,
                                            rz=rnd.uniform(-0.008, 0.008)))
            x += w
        inner = LEG_AT - LEG / 2
        y_rope = -(STAKE_D / 2 + ROPE_R * 0.55)
        for z in (ROPE_Z, 3.25):                 # the wall's lashing line, and one high up
            B.add(line_rope(-inner, inner, y_rope, z, ROPE_R), lin(PAL["rope"]), R)
        # the railing's own lashing, split round the ladder opening on the front
        zr = DECK + 0.38
        spans = [(-inner, LADDER_GAP[0]), (LADDER_GAP[1], inner)] if side == 0 else [(-inner, inner)]
        for a, b in spans:
            B.add(line_rope(a, b, y_rope, zr, ROPE_R), lin(PAL["rope"]), R)
    return B.finish("Walls", mat, ao_height=0.9)

def build_tower():
    rnd = random.Random(2026)
    mat = bpy.data.materials["M_WallKit"]
    root = bpy.data.objects.new("Watchtower_Level_1", None)
    bpy.context.scene.collection.objects.link(root)
    root["footprint_xz"] = [2.6, 2.6]
    for fn in (tower_footings, tower_posts, tower_walls, tower_platform, tower_ladder):
        ob = fn(mat, rnd)
        ob.parent = root
    for name, p in (("Ladder_Bottom", (0, -1.23, 0.05)), ("Ladder_Top", (0, -1.10, DECK)),
                    ("Lookout_Anchor", (0, 0, DECK))):
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 0.2
        e.location = p
        bpy.context.scene.collection.objects.link(e)
        e.parent = root
    bpy.context.view_layer.update()
    return root

TOWER = build_tower()
tt = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in TOWER.children if o.type == 'MESH')
print("Watchtower_Level_1", tt, "tris", {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons)
                                         for o in TOWER.children if o.type == 'MESH'}, bounds(TOWER))
