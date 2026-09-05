# SeaSick — progression hull fleet.
# Re-runnable: exec(open('/Users/kevinandersson/blender_objects/seasick_hulls.py').read())
#
# Convention (matches paddle_boat.glb): bow +X, up +Z, beam +/-Y, metres,
# waterline at z = 0. Every hull is authored as a STARBOARD HALF with a live
# Mirror modifier, so it stays symmetric while you edit it. FBX export applies
# the modifier.
#
# Scale charter: WorldScale.Person = 1.7 m. Every size below is chosen against
# that, and against the current paddle steamer at 24.2 m.

import bpy
import bmesh, math, os
from mathutils import Vector

# ----------------------------------------------------------------- helpers --

def clear_fleet():
    for name in ("Cube",):
        o = bpy.data.objects.get(name)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
    for coll in list(bpy.data.collections):
        if coll.name.startswith(("T1_", "T2_", "T3_", "T4_", "T5_",
                                 "REFERENCE", "_hydro_tmp")):
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(coll)
    # Anything still answering to a fleet name is an ORPHAN -- a hull left in
    # the file by a build that raised part way through, which then makes the
    # next build name its hull `T2_Skiff_Hull.001`. Two hulls in the same place
    # is the object-level version of the overlapping faces this whole pass is
    # about, and the export walks collections by name, so it is easy to miss.
    for o in list(bpy.data.objects):
        if o.name.startswith(("T1_", "T2_", "T3_", "T4_", "T5_")):
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


def get_collection(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def make_object(name, verts, faces, coll, smooth=False, mirror=True, solidify=0.0,
                recalc=True, flat_keys=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate(verbose=False)
    if recalc:
        # Make the winding consistent and outward as a BUILD STEP rather than
        # by hand. The port linings are the reason: their four quads are
        # authored against a tunnel whose "outside" is the opposite of the
        # hull's, and getting that right by inspection is 28 faces of guessing
        # per hull. Blender knows the answer; the check that it is the RIGHT
        # answer is the signed volume, asserted in the topology report.
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    if flat_keys:
        # Gun-port linings shade FLAT. Smooth-shaded, they were averaging into
        # the vertex normals of the outer skin all round each opening, and on
        # the three-decker -- 72 ports in three rows -- that read as a quilt of
        # diamonds across her whole side. A lining points into its tunnel; it
        # has no business bending the planking around it.
        #
        # Matched by vertex SET rather than by face index, because the
        # recalc-normals round trip through bmesh is free to reorder faces.
        for pg in me.polygons:
            if frozenset(pg.vertices) in flat_keys:
                pg.use_smooth = False
    me.update()
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    if mirror:
        m = ob.modifiers.new("Mirror", 'MIRROR')
        m.use_axis = (False, True, False)
        m.use_clip = True
        m.use_mirror_merge = True
        m.merge_threshold = 0.002
    if solidify > 0.0:
        sm = ob.modifiers.new("Planking", 'SOLIDIFY')
        sm.thickness = solidify
        sm.offset = -1.0                  # inward: the outside stays as drawn
        sm.use_even_offset = True
        sm.use_rim = True
    return ob


def lerp(a, b, t):
    return a + (b - a) * t


def poly_y_at_z(ctrl, z):
    """Beam factor at height z from a monotone-in-z control polyline."""
    if z <= ctrl[0][0]:
        return ctrl[0][1]
    for i in range(len(ctrl) - 1):
        z0, f0 = ctrl[i]
        z1, f1 = ctrl[i + 1]
        if z <= z1:
            return lerp(f0, f1, (z - z0) / max(1e-6, z1 - z0))
    return ctrl[-1][1]

# ------------------------------------------------------------- hull loft ----

def hull_section(bmax, draft, ctrl, z_rail, n_under, n_top, fullness, levels=None):
    """One station, keel -> rail, on the +Y side. Returns [(y, z), ...].

    `levels` overrides the topside heights. It exists so a station can carry
    gun-port loops at heights that vary along the hull (the ports follow the
    DECK, which carries full sheer, while the plain topside loops carry sheer
    only in proportion to their height). The COUNT must be the same at every
    station or the loft stops being a grid."""
    pts = []
    bw = bmax * ctrl[0][1]                       # half beam at the waterline
    for k in range(n_under + 1):                 # keel (t=0) -> waterline (t=1)
        t = k / n_under
        z = -draft * (1.0 - t)
        # superellipse: fullness 1 = straight V, 2 = elliptical, 3+ = flat floor
        y = bw * (1.0 - (1.0 - t) ** fullness) ** (1.0 / fullness)
        pts.append((y, z))
    if levels is None:
        levels = topside_levels(ctrl, z_rail, n_top)
    for z in levels:                                # waterline -> rail
        pts.append((bmax * poly_y_at_z(ctrl, z), z))
    return pts


_LEVEL_CACHE = {}


def topside_levels(ctrl, z_rail, n_top):
    """Heights to sample the topside at: evenly spaced, PLUS every control
    height, so the widest point of the section is actually hit and the built
    beam equals the authored beam instead of landing a per cent under."""
    key = (id(ctrl), z_rail, n_top)
    if key not in _LEVEL_CACHE:
        zs = [z_rail * k / n_top for k in range(1, n_top + 1)]
        zs += [c[0] for c in ctrl if 0.02 < c[0] < z_rail - 0.02]
        out = []
        for z in sorted(zs):
            if not out or z - out[-1] > 1e-4:
                out.append(z)
        _LEVEL_CACHE[key] = out
    return _LEVEL_CACHE[key]


def _rake_at(p, x, x_aft, x_fwd):
    """The raw rake offset at loft station x, before the height taper."""
    rk_f, rk_a = p.get("rake_fwd", 0.0), p.get("rake_aft", 0.0)
    rk_k = p.get("rake_k", 3.0)
    if x >= 0.0:
        u = x / x_fwd if x_fwd else 0.0
        return rk_f * min(1.0, u) ** rk_k
    u = abs(x / x_aft) if x_aft else 0.0
    return -rk_a * min(1.0, u) ** rk_k


def _loft_x_for_built_x(p, x_built, zn, x_aft, x_fwd):
    """Invert the rake: which loft station lands at `x_built` at height `zn`?

    Ports are specified where you would MEASURE them -- along the built side --
    but stations are placed in loft space, and the rake moves the two apart by
    up to 2.4 m at the bow of the brig."""
    lo, hi = x_aft - 4.0, x_fwd + 4.0
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if mid + _rake_at(p, mid, x_aft, x_fwd) * zn < x_built:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


# --- the section, for hulls built with a real bulwark ------------------------
# A ring is a J: up the OUTSIDE from the keel, over the capping rail, and back
# DOWN an inner face to the deck. Thickness therefore exists exactly where the
# eye can reach it -- through a gun port and over the rail -- and nowhere else.
#
# What this replaces: a Solidify modifier over the whole hull, which built a
# second complete hull inside the first. Measured on the brig, that inner shell
# was 2,572 of the asset's 5,950 triangles -- 43 per cent of the budget, to
# line seven 0.68 m holes.
#
# The level list carries a MODE per height, because sheer does not treat them
# alike. A plain topside loop is lifted by sheer in proportion to its height
# (sheer * z / z_rail), but the deck -- and so the gun ports, which sit a fixed
# 0.70 m above it -- carries sheer in full. Mixing the two in one list is what
# keeps a port square to the deck at the bow instead of sagging 0.14 m.

def _port_rows(p):
    """[(sill, head)] for every gun-port row on this hull, low to high."""
    if not p.get("port_x"):
        return []
    return [(z, z + p["port_height"]) for z in sorted(p["gun_decks"])]


def _inner_floor(p):
    """How far DOWN the inner planking runs.

    A gun port is a hole through a WALL, so there has to be a wall. On the brig
    the one battery is above her one deck, the wall is the bulwark, and the
    inner face can stop at the deck. On a three-decker TWO of the three
    batteries are below her upper deck, inside the hull -- stop the inner face
    at the upper deck and her lower ports become holes in a single sheet, and
    you see clean through the ship. So the planking reaches the lowest deck
    that carries guns, which is what `inner_floor` names."""
    return p.get("inner_floor", p.get("deck_z"))


def _level_plan(p, z_rail):
    """[(target z, mode)] from the waterline to the rail, outer surface.

    Deliberately has NO level between the port sill and head: that is what
    makes a gun port exactly ONE face row, so its lining is four quads and its
    jamb is one, instead of a row of slivers."""
    ctrl = p["topside"]
    zs = [(z_rail * 0.22, "prop")]
    for c in ctrl:                      # sample the control heights or the
        if 0.02 < c[0] < z_rail - 0.02: # built beam lands under the drawn one
            zs.append((c[0], "prop"))
    if p.get("deck_z") is not None:
        zs.append((p["deck_z"], "deck"))
    for (sill, head) in _port_rows(p):
        zs.append((sill, "deck"))
        zs.append((head, "deck"))
    floor = _inner_floor(p)
    # Only if it is ABOVE the waterline. This list is the TOPSIDE, and the
    # ring is built underwater-chain-then-plan; a negative level here lands
    # after the waterline point and folds the section back on itself. On the
    # skiff, whose floor is -0.30, that doubled her measured displacement
    # (5.19 -> 10.38 m3, Cb 0.79 on an open boat) and welded a vertex onto
    # itself. Below the waterline the floor is already an underwater index --
    # see j_floor_idx.
    if floor is not None and floor > 1e-6:
        zs.append((floor, "deck"))
    zs.append((z_rail, "prop"))
    zs.sort(key=lambda t: t[0])
    out = []
    for z, m in zs:
        if not out or z - out[-1][0] > 1e-4:
            out.append((z, m))

    # Nothing may sit strictly BETWEEN a sill and its head. On the brig that
    # was free -- one battery, and no filler happened to land in it. On the
    # three-decker the filler at z_rail * 0.22 = 1.54 landed inside her lower
    # battery's band of 1.30-2.10, which split every one of those ports into
    # two face rows. The lining then spanned sill to head across a vertex that
    # was still there: a diagonal, not an edge, and 48 non-manifold edges.
    bands = _port_rows(p)
    out = [(z, m) for (z, m) in out
           if not any(sv + 1e-6 < z < hv - 1e-6 for (sv, hv) in bands)]

    # Even out what is left. The functional heights land where they must, and
    # that left a 0.70 m gap between the deck and the sill against 0.17 m at
    # the rail -- so the topside carried quads twice the height of their
    # neighbours, non-planar, and smooth shading turned that into a row of
    # chevrons all down her side. The geometry was never wrong; flat-shaded the
    # grid is even. It is the SHADING that reads the twist, so the fix is
    # smaller quads, not different topology.
    #
    # The sill-to-head gap is never subdivided: a gun port has to stay exactly
    # one face row or its lining stops being four quads.
    limit = z_rail * 0.16
    sills = [s for (s, _) in _port_rows(p)]
    filled = []
    for i, (z, m) in enumerate(out):
        filled.append((z, m))
        if i + 1 >= len(out):
            break
        z2, m2 = out[i + 1]
        if any(abs(z - sv) < 1e-6 for sv in sills):
            continue                                   # a port opening
        gap = z2 - z
        n = int(gap / limit)
        mode = "deck" if (m == "deck" and m2 == "deck") else "prop"
        for k in range(1, n + 1):
            if gap * k / (n + 1) > 1e-4:
                filled.append((z + gap * k / (n + 1), mode))
    # and the run from the waterline up to the first level
    z0 = filled[0][0]
    n0 = int(z0 / limit)
    head = [(z0 * k / (n0 + 1), "prop") for k in range(1, n0 + 1)]
    return sorted(head + filled, key=lambda t: t[0])


def _sheared(z, mode, sheer, z_rail):
    """Where a level actually lands once sheer is applied."""
    return z + sheer if mode == "deck" else z + sheer * (z / z_rail)


def build_hull(p, coll):
    """Loft a hull from the parameter dict p. Returns (hull_obj, stations)."""
    LOA, B, D = p["length"], p["beam"], p["draft"]
    depth = p["depth"]                        # keel to rail top, amidships
    z_rail = depth - D                        # rail height above the waterline
    rk_f, rk_a = p.get("rake_fwd", 0.0), p.get("rake_aft", 0.0)
    rk_k = p.get("rake_k", 3.0)
    L = LOA - rk_f - rk_a                     # the loft runs stem foot to tuck
    xm = p.get("midship", 0.47)               # fraction of L, from the stern
    ns = p["stations"]
    n_under = p["n_under"]
    n_top = p["n_top"]
    plank_t = p.get("plank_t", 0.0)           # 0 = the old Solidify hulls
    x_aft, x_fwd = -L * xm, L * (1.0 - xm)

    def sheer_at(x):
        if x < 0.0:
            return p["sheer_aft"] * min(1.0, abs(x / x_aft)) ** 2.0
        return p["sheer_fwd"] * min(1.0, x / x_fwd) ** 2.0

    # --- gun ports: solve each jamb in BUILT x, iterating on the sheer ------
    ports = []
    rows = _port_rows(p)
    floor = _inner_floor(p)
    if rows:
        hw = p["port_width"] * 0.5
        # Every row shares a station, because a station is ONE loft x for the
        # whole ring. The rake then leans the port columns slightly as they
        # climb, which is what a real ship's ports do. The jambs are solved at
        # the MEAN sill so that lean is shared out rather than dumped on the
        # topmost battery.
        ref = sum(sv for (sv, _) in rows) / len(rows)

        def jamb(x_built):
            x = x_built
            for _ in range(6):
                zn = min(1.0, max(0.0, (ref + sheer_at(x) + D) / depth))
                x = _loft_x_for_built_x(p, x_built, zn, x_aft, x_fwd)
            return x
        for xc in p["port_x"]:
            ports.append((jamb(xc - hw), jamb(xc + hw)))

    # --- stations: the jambs ARE stations, they are not extra ---------------
    # Every loop has to earn its place. A cosine station that lands inside a
    # port, or within a guard of a jamb, is DROPPED rather than kept alongside
    # it -- otherwise a 0.68 m opening drags a full keel-to-rail loop into the
    # mesh for nothing. `stations` is set lower on ported hulls to suit.
    if ports:
        # The jambs are fixed points; everything else FILLS THE GAPS between
        # them, graded fine at the ends and coarser amidships. Keeping whatever
        # cosine stations happened to survive instead gave spacings of 0.17 m
        # next to 2.12 m, and a smooth-shaded 2 m quad beside a 0.7 m one
        # wrinkles visibly all down the topside.
        req = sorted({x_aft, x_fwd} | {x for pr in ports for x in pr})
        half = (x_fwd - x_aft) * 0.5
        t_end = p.get("station_end", 0.55)
        t_mid = p.get("station_mid", 1.30)
        xs = []
        for k in range(len(req) - 1):
            x0, x1 = req[k], req[k + 1]
            xm = 0.5 * (x0 + x1)
            is_port = any(abs(x0 - a) < 1e-9 and abs(x1 - b) < 1e-9
                          for (a, b) in ports)
            if is_port:
                xs.append(x0)                 # a port stays ONE face row
                continue
            f = min(xm - x_aft, x_fwd - xm) / half   # 0 at an end, 1 amidships
            target = t_end + (t_mid - t_end) * max(0.0, min(1.0, f))
            n = max(1, int(round((x1 - x0) / target)))
            for i in range(n):
                xs.append(lerp(x0, x1, i / n))
        xs.append(req[-1])
    else:
        xs = []
        for i in range(ns + 1):
            t = i / ns
            t = 0.5 - 0.5 * math.cos(math.pi * t)
            xs.append(lerp(x_aft, x_fwd, t))
    xs.sort()
    merged = [xs[0]]
    for x in xs[1:]:
        if x - merged[-1] > 1e-4:
            merged.append(x)
    xs = merged

    plan = _level_plan(p, z_rail) if plank_t > 0.0 else None
    base_levels = topside_levels(p["topside"], z_rail, n_top)

    # Where the inner chain starts, as an INDEX into the ring, decided once.
    # Deciding it per station by height does not work: the keel rises at the
    # ends, so a floor below the waterline qualifies a different number of
    # points at every station and the loft stops being a grid. (The guard at
    # the bottom of the station loop caught exactly that, at x = -3.33.)
    j_floor_idx = None
    if plan is not None and floor is not None:
        nominal = [-D * (1.0 - k / n_under) for k in range(n_under + 1)]
        nominal += [z for (z, _) in plan]
        j_floor_idx = next((k for k in range(len(nominal))
                            if nominal[k] >= floor - 1e-6), None)

    stations, ring_tags = [], None
    for x in xs:
        if x < 0.0:
            u = abs(x / x_aft)
            e, a, b = p["aft_end"], p["aft_a"], p["aft_b"]
            keel_f = 1.0 - p["aft_rise"] * u ** p.get("aft_keel_k", 2.4)
        else:
            u = x / x_fwd
            e, a, b = p["fwd_end"], p["fwd_a"], p["fwd_b"]
            keel_f = 1.0 - p["fwd_rise"] * u ** p.get("fwd_keel_k", 2.6)
        sheer = sheer_at(x)
        fb = e + (1.0 - e) * (1.0 - min(1.0, u) ** a) ** b       # beam factor
        bmax = B * 0.5 * fb
        rake = (rk_f if x >= 0.0 else -rk_a) * min(1.0, u) ** rk_k

        if plank_t <= 0.0:                       # --- the old path, unchanged
            sec = hull_section(bmax, D * keel_f, p["topside"],
                               z_rail, n_under, n_top, p["fullness"], base_levels)
            ring = []
            for (y, z) in sec:
                zz = z + (sheer * (z / z_rail) if z > 0.0 else 0.0)
                zn = min(1.0, max(0.0, (zz + D) / depth))
                ring.append((x + rake * zn, y, zz))
            stations.append(ring)
            continue

        # --- underwater body: keel -> waterline, single-sided ---------------
        # Underwater loops spaced by ARC LENGTH along the section, not evenly
        # in height. Even-in-z put three loops within 0.08 m of each other on
        # the flat just under the waterline (y 3.69 / 3.76 / 3.77 amidships)
        # and gave the entire turn of the bilge a single loop -- the same
        # mistake, in the other axis, that cosine station spacing fixed at the
        # bow. Sample where the shape is.
        pts, tags = [], []
        bw = bmax * p["topside"][0][1]
        dz, ful = D * keel_f, p["fullness"]
        dense = []
        for k in range(65):
            t = k / 64.0
            dense.append((bw * (1.0 - (1.0 - t) ** ful) ** (1.0 / ful),
                          -dz * (1.0 - t)))
        cum = [0.0]
        for k in range(1, len(dense)):
            cum.append(cum[-1] + math.hypot(dense[k][0] - dense[k - 1][0],
                                            dense[k][1] - dense[k - 1][1]))
        total = cum[-1] or 1.0
        for k in range(n_under + 1):
            target = total * k / n_under
            m = 1
            while m < len(cum) - 1 and cum[m] < target:
                m += 1
            f = (target - cum[m - 1]) / max(1e-12, cum[m] - cum[m - 1])
            pts.append((lerp(dense[m - 1][0], dense[m][0], f),
                        lerp(dense[m - 1][1], dense[m][1], f), "prop"))
            tags.append("hull")
        # --- outside, waterline -> rail -------------------------------------
        tagz = {}
        for ri, (sv, hv) in enumerate(rows):
            tagz[round(sv, 6)] = "sill%d" % ri
            tagz[round(hv, 6)] = "head%d" % ri
        if p.get("deck_z") is not None:
            tagz.setdefault(round(p["deck_z"], 6), "deck")
        if floor is not None:
            tagz.setdefault(round(floor, 6), "floor")
        for (z, mode) in plan:
            pts.append((bmax * poly_y_at_z(p["topside"], z), z, mode))
            tags.append(tagz.get(round(z, 6), "out"))
        tags[-1] = "rail"
        # --- over the cap and back DOWN the inside, as far as `floor` -------
        # Built by walking the OUTER chain back down and offsetting inboard,
        # rather than off the topside plan, so the floor is free to sit below
        # the waterline. That is what an open boat needs: the skiff has no deck
        # to stop at, and single-skinned she is a hull you can see through.
        outer_n = len(pts)
        j0 = j_floor_idx
        if j0 is not None and j0 < outer_n - 1:
            for k in range(outer_n - 1, j0 - 1, -1):
                yv, zv, mv = pts[k]
                # Never let the inner face reach the centreline. Where the
                # planking is thicker than the half-beam -- the last stations
                # at a fine bow -- `yv - plank_t` goes negative, and clamping
                # that to zero welds the inner point onto the centreline as a
                # duplicate. Physically there is no cavity there at all, it is
                # solid timber; holding the inner face at a fraction of the
                # outer half-beam says exactly that and stays non-degenerate.
                pts.append((max(yv * 0.35, yv - plank_t), zv, mv))
                t = tags[k]
                tags.append(t + "_in" if t not in ("out", "hull") else "in")

        ring = []
        for (y, z, mode) in pts:
            zz = _sheared(z, mode, sheer, z_rail) if z > 0.0 else z
            zn = min(1.0, max(0.0, (zz + D) / depth))
            ring.append((x + rake * zn, y, zz))
        stations.append(ring)
        if ring_tags is None:
            ring_tags = tags
        elif tags != ring_tags:
            raise RuntimeError("ring layout changed along the hull at x=%.2f" % x)

    if plank_t <= 0.0:
        return _weld_old(p, stations, coll) + (None,)

    return _weld_bulwark(p, stations, ring_tags, xs, ports, coll) + (ring_tags,)


def _weld_old(p, stations, coll):
    """The pre-2026-09-02 topology: single shell, Solidify for thickness,
    n-gon caps at stem and transom. Still used by the hulls that have not been
    moved over yet -- T2, T3, T5 -- so their measurements do not move."""
    verts, faces = [], []
    per = len(stations[0])
    for ring in stations:
        verts.extend(ring)
    for i in range(len(stations) - 1):
        for j in range(per - 1):
            a0 = i * per + j
            b0 = (i + 1) * per + j
            faces.append([a0, a0 + 1, b0 + 1, b0])
    faces.append(list(range(per - 1, -1, -1)))
    base = (len(stations) - 1) * per
    faces.append([base + j for j in range(per)])
    hull = make_object(p["name"] + "_Hull", verts, faces, coll,
                       smooth=p.get("smooth", True),
                       solidify=p.get("planking", 0.0))
    return hull, stations


def _poly_area(pts):
    """Area of a polygon given as a list of (x, y, z)."""
    ax = ay = az = 0.0
    for i in range(len(pts)):
        x0, y0, z0 = pts[i]
        x1, y1, z1 = pts[(i + 1) % len(pts)]
        ax += y0 * z1 - z0 * y1
        ay += z0 * x1 - x0 * z1
        az += x0 * y1 - y0 * x1
    return 0.5 * math.sqrt(ax * ax + ay * ay + az * az)


def _weld_bulwark(p, stations, tags, xs, ports, coll):
    """Sweep the J-section, cut the ports, line them, and close both ends.

    Both ends close onto a CENTRELINE column rather than an n-gon cap: the bow
    collapses to the stem line and the stern's column sits at the transom's own
    x, so the transom comes out as a quad-gridded panel. That removes the two
    15-gons the old caps produced at every hull end."""
    per = len(stations[0])
    verts, faces = [], []
    for ring in stations:
        verts.extend(ring)

    j_rail = tags.index("rail")
    # One entry per gun-port ROW: (outer sill, outer head, inner sill, inner
    # head). A three-decker has three. The inner index of an outer level is its
    # mirror about the rail, because the inner chain is the outer chain from
    # the floor up, reversed.
    rowj = []
    ri = 0
    while ("sill%d" % ri) in tags:
        js = tags.index("sill%d" % ri)
        jh = tags.index("head%d" % ri)
        if jh != js + 1:
            raise RuntimeError(
                "gun-port row %d spans %d face rows, not 1 (sill j=%d, head j=%d)."
                " Something is sitting between the sill and the head, so the"
                " lining would span a diagonal instead of an edge. See"
                " _level_plan." % (ri, jh - js, js, jh))
        rowj.append((js, jh, 2 * j_rail + 1 - js, 2 * j_rail + 1 - jh))
        ri += 1

    def port_span(i):
        xc = 0.5 * (xs[i] + xs[i + 1])
        return any(x0 - 1e-6 <= xc <= x1 + 1e-6 for (x0, x1) in ports)

    # A port is a hole through a WALL, so both skins have to go. Removing only
    # the outer row left the inner bulwark face running behind the opening --
    # which is not a gun port, it is a recess, and it put three faces on every
    # lining edge (28 non-manifold edges, 4 a port).
    skip = set()
    for (js, jh, jsi, jhi) in rowj:
        skip.add(js)          # outer face row, sill -> head
        skip.add(jhi)         # inner face row, head_in -> sill_in
    for i in range(len(stations) - 1):
        cut = port_span(i)
        for j in range(per - 1):
            if cut and j in skip:
                continue                                  # the opening itself
            a0, b0 = i * per + j, (i + 1) * per + j
            faces.append([a0, a0 + 1, b0 + 1, b0])

    # --- port linings: sill, head and the two jambs, all quads -------------
    lining = set()
    for (j_sill, j_head, j_si, j_hi) in rowj:
        for i in range(len(stations) - 1):
            if not port_span(i):
                continue
            a, b = i * per, (i + 1) * per
            for q in ([a + j_si, b + j_si, b + j_sill, a + j_sill],      # sill
                      [a + j_head, b + j_head, b + j_hi, a + j_hi]):     # head
                faces.append(q)
                lining.add(frozenset(q))
        for (x0, x1) in ports:
            for x, flip in ((x0, True), (x1, False)):
                k = min(range(len(xs)), key=lambda n: abs(xs[n] - x))
                a = k * per
                q = [a + j_sill, a + j_head, a + j_hi, a + j_si]
                faces.append(q[::-1] if flip else q)                     # jamb
                lining.add(frozenset(q))

    # --- close both ends ---------------------------------------------------
    # The end plane is TILED, not covered twice. Below the deck the hull is a
    # single skin and closes straight onto the centreline. From the deck up it
    # is a wall, and a wall seen end-on has three parts: its end grain (outer
    # face across to inner face), then the inner face's own run in to the
    # centreline. Closing BOTH skins onto the centreline instead -- which is
    # what "the planking thins to nothing at the stem" implies -- put four
    # faces on every centreline edge in the bulwark and left the two closures
    # nearly coplanar at the rail, where the cap is flat.
    # Where the inner chain begins, derived rather than looked up by name:
    # it is as many levels below the rail as the inner chain is long.
    n_inner = per - 1 - j_rail
    j_deck = j_rail - n_inner + 1 if n_inner > 0 else j_rail
    for end, ring in ((0, stations[0]), (len(stations) - 1, stations[-1])):
        a = end * per
        col, seen = [], {}
        for j in range(j_rail + 1):
            x, y, z = ring[j]
            if abs(y) < 1e-6:
                col.append(a + j)               # already on the centreline
                continue
            key = (round(x, 5), round(z, 5))
            if key not in seen:
                seen[key] = len(verts)
                verts.append((x, 0.0, z))
            col.append(seen[key])

        def emit(q):
            f = []
            for idx in q:
                if not f or idx != f[-1]:
                    f.append(idx)
            if len(f) > 2 and f[0] == f[-1]:
                f.pop()
            if len(f) >= 3 and _poly_area([verts[i] for i in f]) > 1e-7:
                faces.append(f if end == 0 else f[::-1])

        for j in range(j_deck):                       # single skin, to y = 0
            emit([a + j, col[j], col[j + 1], a + j + 1])
        for j in range(j_deck, j_rail):               # the wall's end grain
            m0, m1 = 2 * j_rail + 1 - j, 2 * j_rail - j
            emit([a + j, a + j + 1, a + m1, a + m0])
        # The inner face is deliberately NOT run in to the centreline. Three
        # surfaces would meet along its forward edge -- the face itself, the
        # end grain and the closure -- which is a T-junction, and a T-junction
        # is non-manifold no matter how it is wound. What is left is a slot
        # between the two inner faces, 0.23 m wide at the brig's stem, and the
        # thing that fills a slot at the stem is the stem timber: the backbone
        # is 0.33 m sided and runs to the rail at both ends, so it covers it.

    hull = make_object(p["name"] + "_Hull", verts, faces, coll,
                       smooth=p.get("smooth", True), solidify=0.0,
                       flat_keys=lining)
    return hull, stations


def build_deck(p, stations, coll, deck_z, tags=None):
    """A cambered deck spanning the hull at deck_z (above the waterline).

    On a bulwark hull the deck edge is taken from the hull's OWN `deck_in`
    ring point -- the bottom of the bulwark's inner face -- so the two meet
    exactly instead of being placed near each other and hoped about. It also
    has real spans across it now rather than a centreline and an edge, which
    is what lets hatches and gratings be cut into it later."""
    L, B, D = p["length"], p["beam"], p["draft"]
    z_rail = p["depth"] - D
    camber = B * 0.012
    spans = p.get("deck_spans", 3)

    edges = []
    if tags is not None and "deck_in" in tags:
        j = tags.index("deck_in")
        for ring in stations:
            x, y, z = ring[j]
            edges.append((x, y, z))
    else:
        for ring in stations:
            sheer = ring[-1][2] - z_rail
            z = deck_z + sheer
            x, y = ring[0][0], 0.0
            for k in range(len(ring) - 1):
                z0, z1 = ring[k][2], ring[k + 1][2]
                if z0 <= z <= z1 and z1 > z0:
                    t = (z - z0) / (z1 - z0)
                    y = lerp(ring[k][1], ring[k + 1][1], t)
                    x = lerp(ring[k][0], ring[k + 1][0], t)
                    break
            else:
                src = ring[-1] if z > ring[-1][2] else ring[0]
                x, y = src[0], src[1]
            y = max(0.0, y - p.get("plank_in", 0.06))
            edges.append((x, y, z))

    verts, faces = [], []
    per_row = spans + 1
    for (x, ye, z) in edges:
        for k in range(per_row):
            f = k / spans
            y = ye * f
            # camber: highest on the centreline, flat at the edge
            verts.append((x, y, z + camber * (1.0 - f * f)))
    for i in range(len(edges) - 1):
        a, b = i * per_row, (i + 1) * per_row
        for k in range(spans):
            faces.append([a + k, a + k + 1, b + k + 1, b + k])
    return make_object(p["name"] + "_Deck", verts, faces, coll, smooth=False)


# --------------------------------------------------------- fittings ---------
# Everything that is not the planked shell: the backbone (keel, stem and
# sternpost as ONE swept batten, because on a real ship they are one timber
# assembly and drawing them as three invites three different thicknesses),
# the rudder, the lower masts and the channels.
#
# All of it is generated from the loft the hull was built from, so it follows
# the hull instead of being placed beside it. A hull whose rake or sheer
# changes drags its fittings along with it.


def _sweep_batten(path, half_width, thick, name, coll, taper=None):
    """Sweep a rectangular section along a polyline in the XZ plane.

    `path` is [(x, z), ...]; the section stands OUT of the hull along the
    path normal (dz, -dx), which points down along the keel, forward at the
    stem and aft at the sternpost -- provided the path runs stern-rail, down,
    along the keel and up to the bow. Built as a half on +Y for the Mirror."""
    verts, faces = [], []
    n = len(path)
    for i, (x, z) in enumerate(path):
        if i == 0:
            dx, dz = path[1][0] - x, path[1][1] - z
        elif i == n - 1:
            dx, dz = x - path[-2][0], z - path[-2][1]
        else:
            dx, dz = path[i + 1][0] - path[i - 1][0], path[i + 1][1] - path[i - 1][1]
        m = math.hypot(dx, dz) or 1.0
        nx, nz = dz / m, -dx / m
        t = thick * (taper[i] if taper else 1.0)
        hw = half_width * (taper[i] if taper else 1.0)
        # inner (flush with the hull) and outer (proud by `thick`) edges
        xi, zi = x, z
        xo, zo = x + nx * t, z + nz * t
        verts += [(xi, 0.0, zi), (xi, hw, zi), (xo, hw, zo), (xo, 0.0, zo)]
    for i in range(n - 1):
        a, b = i * 4, (i + 1) * 4
        faces.append([a + 0, b + 0, b + 1, a + 1])     # inner face
        faces.append([a + 1, b + 1, b + 2, a + 2])     # the side you see
        faces.append([a + 2, b + 2, b + 3, a + 3])     # outer face
    faces.append([0, 3, 2, 1])                          # caps
    e = (n - 1) * 4
    faces.append([e + 0, e + 1, e + 2, e + 3])
    return make_object(name, verts, faces, coll, smooth=False)


def _tube(name, coll, base, top, r_base, r_top, seg=8):
    """A tapered pole from `base` to `top` (both (x, z)), on the centreline.

    Cross-sections are perpendicular to the AXIS, not horizontal: built
    horizontal first, a bowsprit came out as a flat ribbon, because a
    horizontal circle swept along a near-horizontal spar has no thickness in
    the direction that matters.

    The ends are capped with QUADS -- opposite pairs bridged across the ring --
    rather than one n-gon per end. `seg` must be even for that to close."""
    if seg % 2:
        seg += 1
    ax, az = top[0] - base[0], top[1] - base[1]
    m = math.hypot(ax, az) or 1.0
    ax, az = ax / m, az / m
    px, pz = -az, ax
    verts, faces = [], []
    for (x, z), r in ((base, r_base), (top, r_top)):
        for i in range(seg):
            a = 2.0 * math.pi * i / seg
            c, sn = math.cos(a), math.sin(a)
            verts.append((x + px * r * c, r * sn, z + pz * r * c))
    for i in range(seg):
        j = (i + 1) % seg
        faces.append([i, j, seg + j, seg + i])
    for off, flip in ((0, True), (seg, False)):
        for k in range(seg // 2 - 1):
            a, b = off + k, off + k + 1
            c, d = off + seg - 2 - k, off + seg - 1 - k
            q = [a, b, c, d]
            faces.append(q[::-1] if flip else q)
    return make_object(name, verts, faces, coll, smooth=True, mirror=False)


def _resample(path, n):
    """Resample a polyline to n points at equal arc length."""
    cum = [0.0]
    for k in range(1, len(path)):
        cum.append(cum[-1] + math.hypot(path[k][0] - path[k - 1][0],
                                        path[k][1] - path[k - 1][1]))
    total = cum[-1] or 1.0
    out = []
    for i in range(n):
        target = total * i / (n - 1)
        m = 1
        while m < len(cum) - 1 and cum[m] < target:
            m += 1
        f = (target - cum[m - 1]) / max(1e-12, cum[m] - cum[m - 1])
        out.append((lerp(path[m - 1][0], path[m][0], f),
                    lerp(path[m - 1][1], path[m][1], f)))
    return out


def build_fittings(p, stations, coll, tags=None):
    """Backbone, rudder, masts and channels for one hull. Returns the objects.

    Each is its own object because each is a thing the game may switch out --
    a rig upgrade, a fitted battery. They are allowed to INTERSECT the hull
    (a mast passes through a deck); what none of them may contain is two of
    its own faces in the same place."""
    out = []
    D, depth = p["draft"], p["depth"]
    z_rail = depth - D
    per = len(stations[0])
    j_rail = tags.index("rail") if tags and "rail" in tags else per - 1

    # --- backbone: stern rail -> down -> along the keel -> up the stem ------
    if p.get("keel_batten"):
        raw = ([(stations[0][j][0], stations[0][j][2]) for j in range(j_rail, -1, -1)]
               + [(r[0][0], r[0][2]) for r in stations]
               + [(stations[-1][j][0], stations[-1][j][2]) for j in range(j_rail + 1)])
        path, last = [], None
        for pt in raw:
            if last is None or math.hypot(pt[0] - last[0], pt[1] - last[1]) > 1e-4:
                path.append(pt); last = pt
        # A timber does not need the hull's station count: 26 sections at equal
        # arc length instead of the 103 the loft happened to have, which was
        # 680 triangles and 24 coincident vertices where the profile doubled
        # back over the bulwark's inner face.
        path = _resample(path, p.get("backbone_sections", 26))
        # thin as it climbs out of the water: full siding on the keel, less on
        # the posts, read off the height rather than off a list index
        taper = [1.0 if z <= 0.0 else max(0.55, 1.0 - 0.45 * min(1.0, z / z_rail))
                 for (_, z) in path]
        out.append(_sweep_batten(path, p["beam"] * 0.021, p["beam"] * 0.040,
                                 p["name"] + "_Backbone", coll, taper))

    # --- rudder: hung on the sternpost, below the counter ------------------
    if p.get("rudder"):
        aft = stations[0]
        x_heel, z_heel = aft[0][0], aft[0][2]
        z_head = min(z_rail * 0.35, 0.9)
        x_head = x_heel
        for j in range(j_rail):
            if aft[j][2] <= z_head <= aft[j + 1][2]:
                t = (z_head - aft[j][2]) / max(1e-6, aft[j + 1][2] - aft[j][2])
                x_head = lerp(aft[j][0], aft[j + 1][0], t)
                break
        chord = p["length"] * 0.055
        hw = p["beam"] * 0.011
        prof = [(x_head, z_head), (x_head - chord * 0.55, z_head),
                (x_heel - chord, z_heel + 0.05), (x_heel, z_heel + 0.05)]
        verts = [(x, 0.0, z) for (x, z) in prof] + [(x, hw, z) for (x, z) in prof]
        # NO face in the mirror plane: the y=0 cap this used to carry was
        # duplicated by the Mirror and put four faces on every edge of it.
        faces = [[7, 6, 5, 4],
                 [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]]
        out.append(make_object(p["name"] + "_Rudder", verts, faces, coll, smooth=False))

    # --- lower masts and channels ------------------------------------------
    for k, mx in enumerate(p.get("mast_x", [])):
        ring = min(stations, key=lambda r: abs(r[0][0] - mx))
        h = p["mast_height"][k]
        r_b = p["beam"] * 0.042
        out.extend(_mast(p["name"] + "_Mast%d" % (k + 1), coll,
                         mx, -D * 0.85, h, r_b))
        y_rail = ring[j_rail][1]
        cl, cw = p["length"] * 0.10, p["beam"] * 0.055
        z_ch = ring[j_rail][2] - p["depth"] * 0.05
        top = [(mx - cl * 0.5, y_rail, z_ch), (mx + cl * 0.5, y_rail, z_ch),
               (mx + cl * 0.4, y_rail + cw, z_ch), (mx - cl * 0.4, y_rail + cw, z_ch)]
        verts = top + [(x, y, z - p["depth"] * 0.022) for (x, y, z) in top]
        faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                 [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        out.append(make_object(p["name"] + "_Channel%d" % (k + 1), verts, faces,
                               coll, smooth=False))

    if p.get("bowsprit"):
        out.extend(build_head(p, stations, coll, j_rail))
    return out


def build_head(p, stations, coll, j_rail):
    """The head: bowsprit, cap, gammoning, knightheads and dolphin striker.

    A bowsprit is not a stick glued to the front of a boat -- it is the one
    spar that is part of the HULL. Its heel is housed on the deck between two
    heavy timbers, it is lashed down where it crosses the stem, and everything
    forward of that hangs off it. Drawn as a bare tapered cylinder it read as
    exactly what it was: a cylinder added to the ship.

    Four pieces put it back into the structure, and every one of them is a
    real thing rather than decoration:

    - **knightheads**, the pair of timbers either side of the heel. They are
      what the spar is gripped BY, and they are the reason it does not simply
      float in front of the stem.
    - **gammoning**, the lashing where it crosses the stem. Two iron-dark
      bands, which is also where the eye reads it as being held down.
    - **the cap**, squaring off the outer end, so it terminates instead of
      tapering into nothing.
    - **the dolphin striker**, hanging down under the cap. It is what the
      headstays are led round, and in silhouette it is the piece that says
      this end of the ship is rigged.

    About 190 triangles for the lot."""
    out = []
    bow = stations[-1]
    L, B, depth, D = p["length"], p["beam"], p["depth"], p["draft"]
    z_rail = depth - D
    z_b = p["deck_z"] + (bow[j_rail][2] - z_rail) + depth * 0.06
    x_b = bow[j_rail][0]
    # Heavier than it was. A bowsprit is one of the stoutest spars in the ship
    # -- it takes the whole pull of the headstays -- and drawn at B*0.030 it
    # was thinner than the mast it braces.
    r_b = B * 0.040

    # The spar, HOUSED: the heel starts well abaft the stem, on the deck,
    # rather than at the planking. Steeved up about eleven degrees.
    heel = (x_b - L * 0.13, z_b - depth * 0.10)
    head = (x_b + L * 0.21, z_b + depth * 0.13)
    out.append(_tube(p["name"] + "_Bowsprit", coll, heel, head,
                     r_b, r_b * 0.46, seg=10))

    ax, az = head[0] - heel[0], head[1] - heel[1]
    m = math.hypot(ax, az) or 1.0
    ax, az = ax / m, az / m

    def block(name, at_t, half_len, rw, rh, swatch_tag):
        """A band or cap square across the spar at `at_t` along it."""
        cx = heel[0] + ax * m * at_t
        cz = heel[1] + az * m * at_t
        # Square section, aligned to the spar's axis.
        px, pz = -az, ax
        quad = [(-half_len, -rh), (half_len, -rh), (half_len, rh), (-half_len, rh)]
        verts = []
        for sy in (-rw, rw):
            for (dl, dh) in quad:
                verts.append((cx + ax * dl + px * dh, sy, cz + az * dl + pz * dh))
        faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                 [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        return make_object(p["name"] + swatch_tag + name, verts, faces, coll,
                           smooth=False, mirror=False)

    # Gammoning: two lashings where the spar crosses the stem.
    out.append(block("Gammon1", 0.42, r_b * 0.45, r_b * 1.30, r_b * 1.30, "_Band"))
    out.append(block("Gammon2", 0.52, r_b * 0.38, r_b * 1.22, r_b * 1.22, "_Band"))
    # The cap, squaring off the outer end.
    out.append(block("Cap", 0.95, r_b * 0.55, r_b * 1.10, r_b * 1.10, "_Band"))

    # --- the bowsprit knee, the piece that does the actual connecting -------
    #
    # Everything else here is fitting. THIS is structure: a bracket under the
    # spar, standing on the stem, which is what stops it being levered up by
    # the headstays. It is also the piece that, in silhouette, makes the spar
    # look grown out of the hull instead of pushed into a hole in it -- there
    # is a solid triangle of timber between the two, and the eye reads that as
    # a joint rather than as an intersection.
    kn_t = 0.30                            # where the knee meets the spar
    kx0, kz0 = heel[0] + ax * m * 0.06, heel[1] + az * m * 0.06
    kx1, kz1 = heel[0] + ax * m * kn_t, heel[1] + az * m * kn_t
    drop = depth * 0.30
    prof = [(kx0, kz0 - r_b * 0.5), (kx1, kz1 - r_b * 0.5),
            (kx1, kz1 - drop * 0.55), (kx0, kz0 - drop)]
    kw = r_b * 0.62
    verts = [(px, -kw, pz) for (px, pz) in prof] + \
            [(px, kw, pz) for (px, pz) in prof]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(p["name"] + "_BowspritKnee", verts, faces, coll,
                           smooth=False, mirror=False))

    # Knightheads: the pair of heavy timbers the heel is gripped between.
    #
    # Placed where the spar crosses the RAIL, not down at its heel. At the
    # heel they were correct and completely invisible -- buried under the
    # forecastle with the bulwark in front of them -- and a structural member
    # nobody can see is doing no work for the silhouette. Here they stand
    # proud of the caprail, one either side, and they are the thing the spar
    # is plainly held BY. Built as a starboard half and mirrored, so they are
    # a pair by construction.
    kn_x = heel[0] + ax * m * 0.34
    kn_z = heel[1] + az * m * 0.34
    ky0, kyt = r_b * 1.05, r_b * 0.85       # inboard face, and siding
    kh_dn, kh_up = depth * 0.26, depth * 0.20
    kt = r_b * 1.5
    verts = [(kn_x - kt, ky0, kn_z - kh_dn), (kn_x + kt, ky0, kn_z - kh_dn),
             (kn_x + kt, ky0 + kyt, kn_z - kh_dn), (kn_x - kt, ky0 + kyt, kn_z - kh_dn),
             (kn_x - kt * 0.7, ky0, kn_z + kh_up),
             (kn_x + kt * 0.7, ky0, kn_z + kh_up),
             (kn_x + kt * 0.7, ky0 + kyt * 0.8, kn_z + kh_up),
             (kn_x - kt * 0.7, ky0 + kyt * 0.8, kn_z + kh_up)]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(p["name"] + "_Knighthead", verts, faces, coll,
                           smooth=False))

    # The dolphin striker, hanging under the cap.
    sx = heel[0] + ax * m * 0.95
    sz = heel[1] + az * m * 0.95
    sw, sl = r_b * 0.40, depth * 0.26
    verts = [(sx - sw, -sw, sz), (sx + sw, -sw, sz),
             (sx + sw, sw, sz), (sx - sw, sw, sz),
             (sx - sw * 0.45, -sw * 0.45, sz - sl),
             (sx + sw * 0.45, -sw * 0.45, sz - sl),
             (sx + sw * 0.45, sw * 0.45, sz - sl),
             (sx - sw * 0.45, sw * 0.45, sz - sl)]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(p["name"] + "_DolphinStriker", verts, faces, coll,
                           smooth=False, mirror=False))
    return out


# --------------------------------------------------------- joinery rules ----
#
# **Everything small on this ship is cut from ONE section.** Before this, a
# rail post was 0.19 square, a timberhead 0.34 by 0.30 and a caprail 0.19
# thick -- three unrelated numbers, so nothing looked like it came out of the
# same yard. A scantling is a plank size; a shipwright has a few of them and
# uses multiples.
#
# **And every piece is placed off the piece it LANDS ON, never off the number
# that generated it.** The quarterdeck rail was set at the nominal break, but
# the platform actually ends at the last station at or before it -- so the rail
# stood 0.66 m forward of the deck it was supposed to guard, in mid-air. That
# is the whole class of bug: measure the thing you are joining to.

PERSON = 1.7                       # WorldScale.Person; the yardstick for anything a hand touches


def scantling(p):
    """The one section the ship's small timber is cut from."""
    return p["beam"] * 0.017


def _ring_at_z(ring, z, j_max):
    """Where a station's section crosses a height. The hull rakes, so this
    has to interpolate x as well as y -- a strake drawn at constant x would
    walk off the planking at the bow."""
    for j in range(j_max):
        z0, z1 = ring[j][2], ring[j + 1][2]
        if (z0 - z) * (z1 - z) <= 0.0 and abs(z1 - z0) > 1e-9:
            t = (z - z0) / (z1 - z0)
            return (lerp(ring[j][0], ring[j + 1][0], t),
                    lerp(ring[j][1], ring[j + 1][1], t), z)
    return None


def _sweep_strake(path3, proud, height, name, coll, drop=0.0, taper=0.0):
    """A timber running along the ship's SIDE, standing proud of the planking.

    `_sweep_batten` sweeps in the XZ plane and is right for a keel, which lies
    on the centreline. A wale does not: it follows the sheer in all three
    axes and its section stands outboard, so the normal has to be taken in XY.

    Built closed -- four sides and two caps. The inner face lies against the
    planking, which is an INTERSECTION and allowed; what is not allowed is two
    of this object's own faces in the same place, and there are none."""
    n = len(path3)
    verts, faces = [], []
    for i, (x, y, z) in enumerate(path3):
        # Run OUT at the ends rather than stopping square.
        #
        # A strake swept at full section to the last station gets a flat cap
        # across its end, and at the bow -- where the two mirrored halves meet
        # -- that reads as a pale slab laid across the stem. Real timbers are
        # let into the stem and die away into it. Eased over the last `taper`
        # of the run at each end.
        k = 1.0
        if taper > 1e-6:
            e = min(i, n - 1 - i) / max(1.0, (n - 1) * taper)
            k = 0.30 + 0.70 * min(1.0, e)
        if i == 0:
            dx, dy = path3[1][0] - x, path3[1][1] - y
        elif i == n - 1:
            dx, dy = x - path3[-2][0], y - path3[-2][1]
        else:
            dx, dy = path3[i + 1][0] - path3[i - 1][0], path3[i + 1][1] - path3[i - 1][1]
        m = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / m, dx / m           # outboard, for a half built on +Y
        pr, hh = proud * k, height * k
        zt, zb = z + hh * 0.5 - drop, z - hh * 0.5 - drop
        verts += [(x, y, zt), (x, y, zb),
                  (x + nx * pr, y + ny * pr, zb),
                  (x + nx * pr, y + ny * pr, zt)]
    for i in range(n - 1):
        a, b = i * 4, (i + 1) * 4
        faces.append([a + 0, b + 0, b + 1, a + 1])     # inner, against the hull
        faces.append([a + 1, b + 1, b + 2, a + 2])     # under
        faces.append([a + 2, b + 2, b + 3, a + 3])     # outer, the one you see
        faces.append([a + 3, b + 3, b + 0, a + 0])     # over
    faces.append([0, 3, 2, 1])
    e = (n - 1) * 4
    faces.append([e + 0, e + 1, e + 2, e + 3])
    return make_object(name, verts, faces, coll, smooth=False)


def _mast(name, coll, x, base_z, height, r, seg=10):
    """A lower mast, a top, and a topmast above it.

    A single tapered cylinder is a pole. What says MAST is the step at the
    hounds where the topmast is fidded on, and the platform round it -- the
    top -- which is the one piece of a rig you can pick out at any distance.
    Both are shape, not detail, and together they cost about 150 triangles
    against the 28 a bare tube was using.

    Built with real rings up its length rather than two, so `paint()` can put
    iron bands on it by height without any geometry for them at all."""
    out = []
    hounds = 0.56                                   # where the topmast starts
    # radius profile: full at the partners, taper to the hounds, STEP down for
    # the topmast, taper again to the truck.
    prof = [(0.00, 1.00), (0.22, 0.90), (0.44, 0.80), (hounds, 0.74),
            (hounds + 0.01, 0.50), (0.74, 0.42), (1.00, 0.26)]
    verts, faces = [], []
    for (t, k) in prof:
        z = base_z + (height - base_z) * t
        rr = r * k
        for i in range(seg):
            a = 2.0 * math.pi * i / seg
            verts.append((x + rr * math.cos(a), rr * math.sin(a), z))
    for ri in range(len(prof) - 1):
        o = ri * seg
        for i in range(seg):
            j = (i + 1) % seg
            faces.append([o + i, o + j, o + seg + j, o + seg + i])
    last = (len(prof) - 1) * seg
    for off, flip in ((0, True), (last, False)):
        for k in range(seg // 2 - 1):
            q = [off + k, off + k + 1, off + seg - 2 - k, off + seg - 1 - k]
            faces.append(q[::-1] if flip else q)
    out.append(make_object(name, verts, faces, coll, smooth=True, mirror=False))

    # The top: a platform round the hounds, wider abaft than forward, which is
    # the shape that reads at a glance.
    zt = base_z + (height - base_z) * hounds
    hw, fwd, aft, th = r * 3.2, r * 2.1, r * 3.4, r * 0.55
    quad = [(x - aft, -hw), (x + fwd, -hw * 0.72),
            (x + fwd, hw * 0.72), (x - aft, hw)]
    verts = [(qx, qy, zt) for (qx, qy) in quad] + \
            [(qx, qy, zt + th) for (qx, qy) in quad]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(name + "Top", verts, faces, coll,
                           smooth=False, mirror=False))
    return out


def _spar_athwart(name, coll, x, z, half_len, r, seg=8):
    """A yard: a spar lying ACROSS her, so it cannot come from `_tube`, which
    builds on the centreline. Spans both sides itself and is NOT mirrored."""
    verts, faces = [], []
    for y, rr in ((-half_len, r * 0.45), (0.0, r), (half_len, r * 0.45)):
        for i in range(seg):
            a = 2.0 * math.pi * i / seg
            verts.append((x + rr * math.cos(a), y, z + rr * math.sin(a)))
    for ring in range(2):
        o = ring * seg
        for i in range(seg):
            j = (i + 1) % seg
            faces.append([o + i, o + j, o + seg + j, o + seg + i])
    for off, flip in ((0, True), (2 * seg, False)):
        for k in range(seg // 2 - 1):
            q = [off + k, off + k + 1, off + seg - 2 - k, off + seg - 1 - k]
            faces.append(q[::-1] if flip else q)
    return make_object(name, verts, faces, coll, smooth=True, mirror=False)


def build_trim(p, stations, coll, tags=None, hull=None):
    """The lines that make a hull read as a WOODEN SHIP rather than a shape.

    Everything here is a horizontal timber and everything here is a sweep, so
    the cost is a section count and not a mesh: about 300 triangles on the
    brig, against the 3,300 she already carries. It buys the two strongest
    lines on any wooden hull -- the capping rail along the top and the wale
    along the side -- which is most of what was missing next to the reference.

    Sampled down to `trim_sections`. The loft has around a hundred stations
    because a HULL needs them at the bow; a timber running fore and aft does
    not, and the backbone learned the same lesson at 26 sections."""
    out = []
    D, depth = p["draft"], p["depth"]
    z_rail = depth - D
    per = len(stations[0])
    j_rail = tags.index("rail") if tags and "rail" in tags else per - 1

    n_want = p.get("trim_sections", 22)
    step = max(1, (len(stations) - 1) // (n_want - 1))
    idx = list(range(0, len(stations), step))
    if idx[-1] != len(stations) - 1:
        idx.append(len(stations) - 1)

    # --- the capping rail: a real timber lying ON the sheer ----------------
    # The loft already turns the bulwark over into a rail, but it is the
    # thickness of the planking and reads as an edge rather than as something
    # you could put a hand on. This is the hand-on-it version.
    if p.get("caprail", True):
        path = [stations[i][j_rail] for i in idx]
        out.append(_sweep_strake(path, p["beam"] * 0.030, depth * 0.032,
                                 p["name"] + "_Caprail", coll,
                                 drop=-depth * 0.010, taper=0.16))

    # --- the stem head: the one piece of carved work she carries -------------
    #
    # The bow was the weakest thing in her silhouette -- the planking simply
    # ran out to a point. Every boat in the reference has a stem standing
    # proud above the sheer, and it is the piece that says "this was built by
    # somebody" rather than "this was extruded". Twenty triangles.
    if p.get("stem_head", True):
        bow = stations[-1][j_rail]
        rise = depth * 0.16
        fwd = p["length"] * 0.020
        w = p["beam"] * 0.030
        x0, z0 = bow[0], bow[2]
        # FOUR points, not five. A five-sided profile caps with a five-gon,
        # and `report_topology` is a gate -- `export_fleet` refuses to run
        # while anything in the fleet carries an n-gon. Caught by the gate,
        # not by eye, which is the whole reason it exists.
        prof = [(x0 - fwd * 1.4, z0),
                (x0 + fwd * 0.6, z0),
                (x0 + fwd * 1.1, z0 + rise),
                (x0 - fwd * 1.2, z0 + rise * 0.74)]
        verts = [(x, -w, z) for (x, z) in prof] + [(x, w, z) for (x, z) in prof]
        n = len(prof)
        faces = [list(range(n - 1, -1, -1)), list(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n
            faces.append([i, j, n + j, n + i])
        out.append(make_object(p["name"] + "_StemHead", verts, faces, coll,
                               smooth=False, mirror=False))

    # --- the wale: the heavy strake down her side ---------------------------
    # Placed LOW on purpose, at a fifth of the freeboard. High up it would run
    # through the gun ports, and a timber that a gun fires through is not a
    # timber. Stated as a fraction so it holds its proportion on every rung.
    if p.get("wale", True):
        z_w = z_rail * p.get("wale_at", 0.42)
        path = [q for q in (_ring_at_z(stations[i], z_w, j_rail) for i in idx)
                if q is not None]
        if len(path) > 3:
            # Heavy on purpose. At a twentieth of her depth the first wale
            # measured 0.29 m on a 26 m brig and vanished at any distance --
            # a line that thin is a seam, not a timber. A real main wale is
            # the thickest thing on the topside and it is meant to be the
            # first thing you see.
            out.append(_sweep_strake(path, p["beam"] * 0.032, depth * 0.105,
                                     p["name"] + "_Wale", coll, taper=0.12))

        # A second, lighter wale down near the water. Real hulls carry two or
        # three; one on its own reads as a decal, and the pair is what gives
        # the topside somewhere to be BETWEEN. Thinner, so it is clearly the
        # junior of the two and the eye still knows which line is the main one.
        z_w2 = z_rail * p.get("wale2_at", 0.13)
        path2 = [q for q in (_ring_at_z(stations[i], z_w2, j_rail) for i in idx)
                 if q is not None]
        if len(path2) > 3:
            out.append(_sweep_strake(path2, p["beam"] * 0.024, depth * 0.058,
                                     p["name"] + "_Wale2", coll, taper=0.12))

    # --- timberheads: the posts standing above her rail ---------------------
    #
    # The chunkiest thing in Kevin's reference is the row of posts along the
    # rail, and they are not decoration -- they are the tops of her frames,
    # left long above the caprail so there is something to belay a rope to.
    # That is why they are spaced evenly along her and why they stop short of
    # the stem: a frame runs out where the planking closes.
    if p.get("timberheads", True):
        path = [stations[i][j_rail] for i in idx]
        n = len(path)
        every = max(2, n // p.get("timberhead_count", 9))
        w = scantling(p) * 1.30            # the same stick the rail is cut from
        rise = depth * 0.075
        for k in range(1, n - 1, every):
            x, y, z = path[k]
            verts = [(x - w, y - w * 0.5, z), (x + w, y - w * 0.5, z),
                     (x + w, y + w * 1.2, z), (x - w, y + w * 1.2, z),
                     (x - w * 0.72, y - w * 0.3, z + rise),
                     (x + w * 0.72, y - w * 0.3, z + rise),
                     (x + w * 0.72, y + w * 0.9, z + rise),
                     (x - w * 0.72, y + w * 0.9, z + rise)]
            faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                     [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
            out.append(make_object("%s_Timberhead%d" % (p["name"], k),
                                   verts, faces, coll, smooth=False))

    if hull is not None:
        out.extend(build_port_lids(p, hull, coll))
    return out


def build_port_lids(p, hull, coll):
    """A hinged lid over every gun port, swung open, found from the HOLES.

    The first version placed each lid from `p["port_x"]`, which is where the
    port was ASKED for -- and that is not where it ends up. The loft solves
    each jamb into loft x through the rake and the sheer, then makes the jambs
    into stations; so the nearest station to an authored centre is a JAMB, and
    a lid centred there sat half a port width off its own hole, every time.

    The opening itself is the only thing that knows where it is. Each port's
    lining is four flat-shaded quads forming a tunnel, sharing vertices with
    each other and with no other port -- so flood-filling the flat faces gives
    exactly one cluster per port, and its bounds give the hole. Nothing to keep
    in step, and it cannot drift when the loft changes.

    Twelve triangles each and mirrored, so the brig's fourteen ports cost 168
    and the three-decker's seventy-two cost 864 -- the only trim on the ship
    that scales with how heavily armed she is, which is the right thing for it
    to scale with."""
    out = []
    me = hull.data
    # Linings are the only flat-shaded faces on an otherwise smooth hull at
    # this point in the build; `paint()` flattens the rest afterwards.
    lining = [pg for pg in me.polygons if not pg.use_smooth]
    if not lining:
        return out

    # Flood fill over shared vertices: one component per opening.
    by_vert = {}
    for pg in lining:
        for v in pg.vertices:
            by_vert.setdefault(v, []).append(pg.index)
    seen, groups = set(), []
    faces_by_index = {pg.index: pg for pg in lining}
    for pg in lining:
        if pg.index in seen:
            continue
        stack, comp = [pg.index], []
        seen.add(pg.index)
        while stack:
            fi = stack.pop()
            comp.append(fi)
            for v in faces_by_index[fi].vertices:
                for nb in by_vert.get(v, ()):
                    if nb not in seen:
                        seen.add(nb)
                        stack.append(nb)
        groups.append(comp)

    lift = math.radians(p.get("port_lid_deg", 84.0))      # out, barely rising
    for gi, comp in enumerate(groups):
        vids = {v for fi in comp for v in faces_by_index[fi].vertices}
        co = [me.vertices[v].co for v in vids]
        x0, x1 = min(c.x for c in co), max(c.x for c in co)
        z0, z1 = min(c.z for c in co), max(c.z for c in co)
        y_out = max(c.y for c in co)
        ph = max(0.05, z1 - z0)
        hw = (x1 - x0) * 0.5
        xc = (x0 + x1) * 0.5
        th = ph * 0.10
        # Hinged along the HEAD of the port, a line running fore and aft, so
        # the lid swings in the Y-Z plane about that line.
        dy, dz = math.sin(lift), -math.cos(lift)
        fy, fz = y_out + ph * dy, z1 + ph * dz
        ty, tz = -dz * th, dy * th
        quad = [(y_out, z1), (fy, fz), (fy + ty, fz + tz), (y_out + ty, z1 + tz)]
        verts = [(xc - hw, qy, qz) for (qy, qz) in quad] + \
                [(xc + hw, qy, qz) for (qy, qz) in quad]
        faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                 [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        out.append(make_object("%s_PortLid%02d" % (p["name"], gi),
                               verts, faces, coll, smooth=False))
    return out


def build_stern(p, stations, coll, tags=None):
    """Close the back of her, and give the helm somewhere to stand.

    **Why the back was open.** The loft closes an end onto a centreline column
    and tiles the end plane -- but only as a single skin BELOW the deck. Above
    it there is just the wall's end grain, the cross-section of each bulwark,
    and nothing at all spanning between them. At the bow that is invisible,
    because the planking has closed to a point by then: the brig's forward
    station is 0.25 m wide at the rail. Her AFTMOST station is 1.615 m, so the
    same rule leaves a 3.2 m by 2 m hole across her stern that you can see the
    deck through. It was never a bug in the tiling; it is a stern that is
    wide, being closed by a rule written for an end that is narrow."""
    out = []
    if tags is None or "rail" not in tags or "deck" not in tags:
        return out
    j_rail, j_deck = tags.index("rail"), tags.index("deck")
    j_in = tags.index("rail_in") if "rail_in" in tags else j_rail
    aft = stations[0]
    L, B, depth = p["length"], p["beam"], p["depth"]
    z_deck = aft[j_deck][2]

    # --- the transom --------------------------------------------------------
    # Follows the outer profile from the deck up to the rail rather than being
    # one flat plate, so it keeps whatever flare or tumblehome she has back
    # there. Spans both sides itself, so it is NOT mirrored -- a face in the
    # mirror plane comes back with four faces on every edge.
    prof = [aft[k] for k in range(j_deck, j_rail + 1)]
    t = B * 0.030
    verts, faces = [], []
    n = len(prof)
    for layer in (0.0, t):
        for (x, y, z) in prof:
            verts += [(x + layer, -y, z), (x + layer, y, z)]
    for k in range(n - 1):
        a, b = k * 2, (k + 1) * 2
        faces.append([a, a + 1, b + 1, b])                       # aft face
        c, d = 2 * n + k * 2, 2 * n + (k + 1) * 2
        faces.append([d, d + 1, c + 1, c])                       # forward face
        faces.append([a, b, d, c])                               # port edge
        faces.append([c + 1, d + 1, b + 1, a + 1])               # starboard edge
    faces.append([0, 2 * n, 2 * n + 1, 1])                       # under
    e = (n - 1) * 2
    faces.append([e + 1, 2 * n + e + 1, 2 * n + e, e])           # over
    out.append(make_object(p["name"] + "_Transom", verts, faces, coll,
                           smooth=False, mirror=False))

    # --- the taffrail: the caprail carried across the stern -------------------
    y_r, z_r, x_r = aft[j_rail][1], aft[j_rail][2], aft[j_rail][0]
    hw, hh = B * 0.030, depth * 0.032
    verts = [(x_r - hw, -y_r - hw, z_r - hh), (x_r + hw * 2.2, -y_r - hw, z_r - hh),
             (x_r + hw * 2.2, y_r + hw, z_r - hh), (x_r - hw, y_r + hw, z_r - hh)]
    verts += [(x, y, z + hh * 2.0) for (x, y, z) in verts]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(p["name"] + "_Taffrail", verts, faces, coll,
                           smooth=False, mirror=False))

    # --- the quarterdeck ------------------------------------------------------
    # A raised deck aft is where the helm belongs, and it is also what gives
    # the after end of a ship its mass. Only on hulls that HAVE a deck to
    # raise, and long enough for a quarter of her to be worth walking up onto.
    if p.get("deck_z") is None or L < 18.0:
        return out
    z_q = z_deck + depth * 0.16
    x_end = stations[0][0][0] + L * p.get("quarterdeck_frac", 0.26)
    ring_idx = [i for i, r in enumerate(stations) if r[j_rail][0] <= x_end]
    if len(ring_idx) >= 3:
        # Six planks across, not one.
        #
        # Built two points wide, every face on the platform touched a boundary
        # edge -- so the deck's margin-plank rule, which finds the edge from
        # the topology, correctly decided the WHOLE quarterdeck was margin and
        # painted it dark. A strip one quad wide has no interior to be the
        # middle of. Giving it some also lets the plank variation show.
        nq = 6
        verts, faces = [], []
        for i in ring_idx:
            r = stations[i]
            # INTO the bulwark, not up to it. At 0.97 the platform stopped
            # 0.10 m short of the inner face and there was a slot of daylight
            # down each side of it.
            y_in = r[j_in][1] * 1.02
            for k in range(nq + 1):
                verts.append((r[j_rail][0], lerp(-y_in, y_in, k / nq), z_q))
        for c_i in range(len(ring_idx) - 1):
            for k in range(nq):
                a = c_i * (nq + 1) + k
                b = a + nq + 1
                faces.append([a, a + 1, b + 1, b])
        out.append(make_object(p["name"] + "_Quarterdeck", verts, faces, coll,
                               smooth=False, mirror=False, solidify=depth * 0.022))

        # --- the wheel ------------------------------------------------------
        # Gated at 15 m, which is not a number picked for the look: it is the
        # SAME length `ShipFit.Blocked` uses to refuse a wheel and quadrant to
        # a small hull. Below it she is steered by a tiller, and the asset
        # should not contradict what the yard says she can carry.
        if L >= 15.0:
            # Right aft, just forward of the taffrail -- which is where a
            # wheel actually is, and which is what puts air between the
            # helmsman and the mast. At `x_end - 0.10L` he stood 1.45 m abaft
            # the mizzen with its sail in his face.
            out.extend(_helm(p, coll, stations[0][0][0] + L * 0.075, z_q))
        # The platform's REAL forward edge -- the last station it actually
        # used -- not the nominal break that chose it.
        x_edge = stations[ring_idx[-1]][j_rail][0]
        out.extend(build_companion(p, stations, coll, tags, z_q, x_edge))
    return out


def build_companion(p, stations, coll, tags, z_q, x_edge):
    """The way up onto the quarterdeck, and the rail round its open edge.

    Every dimension here comes from one of two places: the SCANTLING, so the
    timber matches the rest of the ship's small work, or a PERSON, so the
    things a hand touches are the size a hand expects. Nothing is chosen to
    look right.

    And every piece lands on another piece. The rail stands on the platform's
    real edge and runs into the bulwark at its outboard end; the ladder's
    stringers are the rail's gap stanchions carried down; the opening in the
    rail IS the ladder's width, because it is the same number. That is what
    was missing -- the parts were all individually plausible and none of them
    were joined to anything."""
    out = []
    j_rail = tags.index("rail")
    j_deck = tags.index("deck")
    j_in = tags.index("rail_in") if "rail_in" in tags else j_rail
    depth = p["depth"]
    sc = scantling(p)

    ring = min(stations, key=lambda r: abs(r[j_rail][0] - x_edge))
    y_in = ring[j_in][1] * 1.02        # into the bulwark, same as the platform

    # One man wide. A companionway is not sized off the ship.
    hw = PERSON * 0.34
    post = sc * 0.60                   # half-section of a stanchion
    rail_h = PERSON * 0.56             # a handrail is a handrail on any hull

    # --- the rail across the open edge, as a starboard half -----------------
    # The sides need nothing: the ship's own bulwark is already the rail there.
    inner = hw + post                  # the gap stanchion's centre
    outer = y_in - post
    if outer > inner + sc:
        n_post = max(2, int(round((outer - inner) / (PERSON * 0.62))) + 1)
        for i in range(n_post):
            y = lerp(inner, outer, i / max(1, n_post - 1))
            verts = [(x_edge - post, y - post, z_q), (x_edge + post, y - post, z_q),
                     (x_edge + post, y + post, z_q), (x_edge - post, y + post, z_q)]
            verts += [(vx, vy, z_q + rail_h) for (vx, vy, vz) in verts]
            faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                     [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
            out.append(make_object("%s_QdStanchion%d" % (p["name"], i),
                                   verts, faces, coll, smooth=False))

        # Two horizontals -- a single top rail leaves a metre of nothing under
        # it and reads as a goalpost. Both run from the gap INTO the bulwark.
        for k, (zc, th, wd) in enumerate(((rail_h, sc * 0.42, sc * 0.75),
                                          (rail_h * 0.50, sc * 0.30, sc * 0.55))):
            verts = [(x_edge - wd, hw, z_q + zc - th),
                     (x_edge + wd, hw, z_q + zc - th),
                     (x_edge + wd, y_in + sc * 0.5, z_q + zc - th),
                     (x_edge - wd, y_in + sc * 0.5, z_q + zc - th)]
            verts += [(vx, vy, z_q + zc + th) for (vx, vy, vz) in verts]
            faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                     [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
            out.append(make_object("%s_QdRail%d" % (p["name"], k),
                                   verts, faces, coll, smooth=False))

    # --- the ladder, in the opening the rail leaves --------------------------
    # Its foot lands on the deck UNDER THE FOOT, not on the deck under the top
    # of the flight: she has sheer, and the two are not the same height.
    z_top_deck = ring[j_deck][2]
    rise = max(0.30, z_q - z_top_deck)
    steps = max(2, int(round(rise / 0.21)))
    run = rise / steps                          # about forty-five degrees
    x_foot = x_edge + run * steps
    foot_ring = min(stations, key=lambda r: abs(r[j_rail][0] - x_foot))
    rise = max(0.30, z_q - foot_ring[j_deck][2])
    steps = max(2, int(round(rise / 0.21)))
    run = rise / steps
    h = rise / steps

    nose = run * 0.28
    t_th = sc * 0.55
    # The quarterdeck itself is the top step, so the first TREAD is one rise
    # below it and the last is one rise above the deck.
    for i in range(1, steps):
        z = z_q - h * i
        x0 = x_edge + run * (i - 1)
        verts = [(x0, -hw, z), (x0 + run + nose, -hw, z),
                 (x0 + run + nose, hw, z), (x0, hw, z)]
        verts += [(vx, vy, z - t_th) for (vx, vy, vz) in verts]
        faces = [[3, 2, 1, 0], [4, 5, 6, 7],
                 [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        out.append(make_object("%s_Tread%d" % (p["name"], i), verts, faces,
                               coll, smooth=False, mirror=False))

    # The stringers ARE the gap stanchions carried down: same y, same section,
    # so the flight and the rail are visibly one piece of joinery. Head flush
    # with the platform, foot flat on the deck.
    z_foot = z_q - rise
    prof = [(x_edge - post, z_q),
            (x_edge + run * 0.9, z_q),
            (x_foot + nose, z_foot + h * 0.9),
            (x_foot + nose, z_foot - t_th)]
    verts = [(px, hw, pz) for (px, pz) in prof] + \
            [(px, hw + post * 2.0, pz) for (px, pz) in prof]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(p["name"] + "_Stringer", verts, faces, coll,
                           smooth=False))
    return out


def _helm(p, coll, x, z_deck, seg=10, spokes=8):
    """A ship's wheel on its pedestal. About 190 triangles.

    Human-sized, and only faintly bigger on a bigger ship -- a helm is worked
    by a pair of hands and does not scale with displacement the way the rest
    of her does. That is a WorldScale rule, not a modelling one."""
    out = []
    R = 0.55 + p["length"] * 0.006
    hx, hr = R * 0.075, R * 0.10
    cz = z_deck + R * 1.05

    verts, faces = [], []
    for i in range(seg):
        a = 2.0 * math.pi * i / seg
        cy, cs = math.cos(a), math.sin(a)
        for (dx, dr) in ((-hx, -hr), (hx, -hr), (hx, hr), (-hx, hr)):
            verts.append((x + dx, (R + dr) * cy, cz + (R + dr) * cs))
    for i in range(seg):
        j = (i + 1) % seg
        for k in range(4):
            m = (k + 1) % 4
            faces.append([i * 4 + k, i * 4 + m, j * 4 + m, j * 4 + k])
    out.append(make_object(p["name"] + "_HelmWheel", verts, faces, coll,
                           smooth=False, mirror=False))

    # Spokes, run out PAST the rim -- the handles are what make a ship's wheel
    # read as a ship's wheel and not as a cartwheel.
    #
    # Each spoke runs from a HUB radius outward, not from rim to rim through
    # the centre. A through-spoke's two broad faces are centred on the hub
    # whatever angle the spoke is at, so all of them landed on the same two
    # points and `report_topology` counted ten coincident face centres. Found
    # by the gate; completely invisible in the viewport.
    verts, faces = [], []
    w = R * 0.055
    r0, r1 = R * 0.16, R * 1.26
    for si in range(spokes):
        a = 2.0 * math.pi * si / spokes
        cy, cs = math.cos(a), math.sin(a)
        base = len(verts)
        for r in (r0, r1):
            for (dx, dw) in ((-hx, -w), (hx, -w), (hx, w), (-hx, w)):
                verts.append((x + dx, r * cy - dw * cs, cz + r * cs + dw * cy))
        for k in range(4):
            m = (k + 1) % 4
            faces.append([base + k, base + m, base + 4 + m, base + 4 + k])
        faces.append([base + 3, base + 2, base + 1, base + 0])
        faces.append([base + 4, base + 5, base + 6, base + 7])
    out.append(make_object(p["name"] + "_HelmSpokes", verts, faces, coll,
                           smooth=False, mirror=False))

    # The pedestal it turns on.
    pw, ph = R * 0.22, cz - z_deck - R * 0.55
    verts = [(x - pw, -pw, z_deck), (x + pw, -pw, z_deck),
             (x + pw, pw, z_deck), (x - pw, pw, z_deck)]
    verts += [(vx, vy, z_deck + max(0.12, ph)) for (vx, vy, vz) in verts]
    faces = [[3, 2, 1, 0], [4, 5, 6, 7],
             [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    out.append(make_object(p["name"] + "_HelmPost", verts, faces, coll,
                           smooth=False, mirror=False))
    return out


def build_sails(p, coll):
    """Her rig: square sails on yards, one tier on a boat and two on a ship.

    The first pass hung ONE sail per mast at a fixed fraction of the mast's
    height, and it broke in both directions. Its foot landed at 0.20 of the
    mast, which on the brig is below her rail, so the canvas CLIPPED THROUGH
    THE DECK -- and a single sail that size on a 46 m three-decker is a
    handkerchief on a cathedral.

    Both come from the same mistake: the sail was measured against the MAST
    and the mast is not what it has to clear or fill. It is measured against
    the rail it must stand above and the space between there and the masthead,
    which is a real quantity on every hull.

    Given THICKNESS by Solidify rather than by a second reversed copy of the
    grid: a sail is seen from both sides, and two grids in the same place
    would be exactly the overlapping faces this asset is not allowed to have.
    On a sheet, Solidify is the right tool -- it was the wrong one on a hull
    only because there it doubled a surface nobody could see the back of."""
    out = []
    if not p.get("mast_x"):
        return out
    B, D, depth = p["beam"], p["draft"], p["depth"]
    z_rail = depth - D
    # Ten cloths across and seven bands down. The old 6x5 is what made her read
    # as cardboard: a sail is a soft thing and it needs enough spans to CURVE,
    # both in the belly and round the cut of the foot. 164 triangles a sail
    # becomes 300 -- on a ship carrying 5,400 that is not the expensive part.
    nu, nv = 10, 7

    # A boat carries a single sail; a ship crosses a topsail above her course.
    # Tied to LENGTH rather than to a rung number, so an interpolated hull
    # gets the rig its size earns without anything being authored for it.
    tiers = 2 if p["length"] >= 22.0 else 1

    # --- the sight line, which is what actually sizes the rig ---------------
    #
    # Measured on the brig: the helmsman's eye sits at 3.78 m and the lowest
    # sail spanned 3.22 to 7.77 m. **His eye was inside the canvas.** He stood
    # 1.45 m abaft a mast with a sail hung in front of his face -- and the
    # chase camera looks straight down that same line, so the thing covering
    # the middle of the player's screen was the thing blinding the captain.
    # One fault, seen twice.
    #
    # So the foot of the lowest sail is DERIVED from where he stands: eye
    # height plus a clear margin. It cannot drift out of true by a tuning
    # mistake because it is not tuned, and every hull on the ladder gets a
    # helm that can see, whatever her freeboard.
    z_helm = (p["deck_z"] if p.get("deck_z") is not None else z_rail) + depth * 0.16
    z_eye = z_helm + 1.55                              # a 1.7 m helmsman
    foot0 = max(z_rail + depth * 0.10, z_eye + depth * 0.14)

    # The AFTMOST mast crosses no course.
    #
    # Even with the foot raised, a square sail on the mast immediately ahead
    # of the wheel is a wall across the one direction the helm has to look.
    # Real brigs set a fore-and-aft spanker back there for exactly this
    # reason: nothing square on the aftermost mast.
    aft_mast = min(range(len(p["mast_x"])), key=lambda i: p["mast_x"][i]) \
        if len(p["mast_x"]) >= 2 else -1

    for k, mx in enumerate(p["mast_x"]):
        h = p["mast_height"][k]
        span = max(depth * 0.6, h * 0.94 - foot0)      # hoist available
        # Course takes a little over half of it, topsail most of the rest,
        # with a gap between them for the top.
        cuts = [(0.00, 0.48), (0.56, 0.88)][:tiers]
        if k == aft_mast and len(cuts) > 1:
            cuts = cuts[1:]                            # topsail only
        for t_i_raw, (a, b) in enumerate(cuts):
            t_i = t_i_raw + (1 if (k == aft_mast and tiers > 1) else 0)
            z_bot, z_top = foot0 + span * a, foot0 + span * b
            # Narrower aloft, and wider at the foot than at the head, which is
            # what a square sail actually is.
            # **Inside her beam.** At 0.56 the yards measured 4.80 m against
            # a 3.90 m half-beam -- the canvas was 23% wider than the ship, so
            # from astern it hid the water down both sides and left nothing to
            # read her heading against.
            head = B * (0.42 if t_i == 0 else 0.34)
            foot = head * 1.10
            # A sail full of wind, not a sheet hung up to dry. The belly is
            # the whole character of the thing and it was set at a tenth of
            # the beam, which at this size is a crease.
            belly = B * (0.26 if t_i == 0 else 0.20)
            hoist = z_top - z_bot
            verts, faces = [], []
            for j in range(nv + 1):
                v = j / nv                       # 0 at the head, 1 at the foot
                halfw = lerp(head, foot, v)
                for i in range(nu + 1):
                    u = i / nu
                    su = math.sin(math.pi * u)          # 0 at each leech
                    # The foot is CUT, not straight: a square sail is gored
                    # away in the middle so it clears what is below it, and
                    # the leeches hang lower than the centre. Straight across,
                    # the bottom edge is the one line on the whole ship that
                    # is perfectly horizontal, and the eye finds it instantly.
                    z = lerp(z_top, z_bot, v) + hoist * 0.10 * su * v * v
                    # Belly: fullest amidships and about a third of the way
                    # down, easing to nothing at head, foot and both leeches.
                    bx = belly * su * math.sin(math.pi * min(1.0, v * 0.82 + 0.18))
                    verts.append((mx + bx, lerp(-halfw, halfw, u), z))
            for j in range(nv):
                for i in range(nu):
                    a_i = j * (nu + 1) + i
                    b_i = a_i + nu + 1
                    faces.append([a_i, a_i + 1, b_i + 1, b_i])
            tag = "%d%s" % (k + 1, "" if t_i == 0 else "T")
            out.append(make_object(p["name"] + "_Sail" + tag, verts, faces,
                                   coll, smooth=True, mirror=False, solidify=0.04))
            # The yard the sail is bent to.
            #
            # Without one a square sail is a billboard hanging in the air
            # beside a pole, which is exactly how she read: the sail is the
            # largest thing in her silhouette and it was not attached to the
            # ship. Forty-four triangles for the biggest gain in the rig.
            out.append(_spar_athwart(p["name"] + "_Yard" + tag, coll,
                                     mx, z_top, head * 1.08, B * 0.020))
        # A boom under the course, so the canvas is held at BOTH edges and
        # stops reading as a sheet pinned at the top only. No course, no boom.
        if p.get("boom", True) and k != aft_mast:
            out.append(_spar_athwart(p["name"] + "_Boom%d" % (k + 1), coll,
                                     mx, foot0, B * 0.42 * 1.10 * 1.04,
                                     B * 0.016))
    return out


def build_port_anchors(p, stations, tags, coll, hull):
    """An empty at every gun port, so cannons are FITTED to measured positions
    instead of typed ones -- the same thing the three-decker's battery levels
    already do for her decks. Guns are switched out as the ship is upgraded,
    so where they go belongs in the asset."""
    if not p.get("port_x") or tags is None or "sill0" not in tags:
        return []
    out = []
    rows = _port_rows(p)
    for ri in range(len(rows)):
        js, jh = tags.index("sill%d" % ri), tags.index("head%d" % ri)
        # Name the battery the way the ship does: a three-decker is fitted out
        # lower, then middle, then upper, so the guns arrive one deck at a
        # time and the anchors have to say which deck they belong to.
        deck = ("Lower", "Middle", "Upper")[ri] if len(rows) == 3 else ""
        for i, xc in enumerate(p["port_x"]):
            ring = min(stations, key=lambda r: abs(0.5 * (r[js][0] + r[jh][0]) - xc))
            x = 0.5 * (ring[js][0] + ring[jh][0])
            y = 0.5 * (ring[js][1] + ring[jh][1])
            z = 0.5 * (ring[js][2] + ring[jh][2])
            for side in (1.0, -1.0):
                e = bpy.data.objects.new(
                    "%s_%sPort%d%s" % (p["name"], deck, i + 1,
                                       "S" if side > 0 else "P"), None)
                e.empty_display_type = 'ARROWS'
                e.empty_display_size = p["port_width"]
                e.location = (x, y * side, z)
                coll.objects.link(e)
                e.parent = hull
                e.matrix_parent_inverse = hull.matrix_world.inverted()
                out.append(e)
    return out


# ------------------------------------------------------------- the raft -----

def build_raft(coll):
    """T1: lashed logs. Not a loft — it is a bundle."""
    verts, faces = [], []
    sides = 8
    lengths = [5.10, 5.45, 5.60, 5.65, 5.55, 5.40, 5.15]
    r = 0.24
    y0 = 0.0
    def cyl(axis, a, b, radius, cy, cz, offax=0.0):
        base = len(verts)
        for k in range(sides):
            th = 2 * math.pi * k / sides
            dy = radius * math.cos(th)
            dz = radius * math.sin(th)
            if axis == 'X':
                verts.append((a, cy + dy, cz + dz))
                verts.append((b, cy + dy, cz + dz))
            else:
                verts.append((cy + dy, a, cz + dz))
                verts.append((cy + dy, b, cz + dz))
        for k in range(sides):
            k2 = (k + 1) % sides
            faces.append([base + 2 * k, base + 2 * k + 1,
                          base + 2 * k2 + 1, base + 2 * k2])
        # Quad caps, not one n-gon per end. Nine logs x two ends was 18 n-gons,
        # the last of them in the fleet. `sides` must stay even for this.
        for off, flip in ((0, True), (1, False)):
            ring = [base + 2 * k + off for k in range(sides)]
            for k in range(sides // 2 - 1):
                q = [ring[k], ring[k + 1], ring[sides - 2 - k], ring[sides - 1 - k]]
                faces.append(q[::-1] if flip else q)
    # logs, laid fore-and-aft, half in the water
    n = len(lengths)
    for i, ln in enumerate(lengths):
        cy = (i - (n - 1) / 2.0) * (2 * r)
        cyl('X', -ln * 0.46, ln * 0.54, r, cy, -0.05)
    # two cross-beams lashed on top
    beam_r = 0.13
    span = n * r + 0.02
    for bx in (-1.55, 1.35):
        cyl('Y', -span, span, beam_r, bx, r - 0.05 + beam_r * 0.55)
    return make_object("T1_Raft_Hull", verts, faces, coll,
                       smooth=False, mirror=False)

# ------------------------------------------------------------ the fleet -----
# length = LOA of the hull (no bowsprit), beam = max breadth,
# draft = keel below the waterline, depth = keel to rail top amidships.

FLEET = [
 dict(name="T2_Skiff", tier=2, label="Fishing skiff",
      length=9.0, beam=2.9, draft=0.55, depth=1.45, midship=0.46,
      fullness=1.35, stations=20, n_under=5, n_top=4,
      topside=[(0.0, 0.93), (0.55, 1.00), (0.90, 0.99)],
      aft_end=0.42, aft_a=2.4, aft_b=0.55, aft_rise=0.28, sheer_aft=0.26,
      fwd_end=0.05, fwd_a=1.9, fwd_b=0.62, fwd_rise=0.95, sheer_fwd=0.40,
      rake_fwd=0.95, rake_aft=0.40, rake_k=2.0, planking=0.05,
      deck_z=None, gun_decks=[], plank_t=0.05,
      # No deck at all, so the inner planking is stopped by the boat's own
      # floor rather than by a deck: 0.25 m above the keel, which is where
      # her bottom boards would sit.
      inner_floor=-0.30,
      rudder=True, keel_batten=True,
      mast_x=[-0.3], mast_height=[6.5]),

 dict(name="T3_Sloop", tier=3, label="Coastal sloop",
      length=15.0, beam=4.8, draft=1.35, depth=2.55, midship=0.46,
      fullness=1.95, stations=24, n_under=5, n_top=4,
      topside=[(0.0, 0.97), (0.55, 1.00), (1.20, 0.94)],
      aft_end=0.40, aft_a=2.6, aft_b=0.50, aft_rise=0.06, sheer_aft=0.34,
      fwd_end=0.05, fwd_a=1.8, fwd_b=0.60, fwd_rise=0.90, sheer_fwd=0.55,
      rake_fwd=1.70, rake_aft=0.90, rake_k=2.0, planking=0.08,
      deck_z=0.75, gun_decks=[], plank_t=0.08,
      rudder=True, keel_batten=True, deck_spans=3,
      mast_x=[0.6], mast_height=[11.0], bowsprit=True),

 dict(name="T4_Brig", tier=4, label="Brig",
      length=26.0, beam=7.8, draft=2.50, depth=5.20, midship=0.47,
      # 18, not 26: the fourteen gun-port jambs are stations now, so the
      # cosine spacing only has to cover the ends and the gaps between ports.
      fullness=2.60, stations=18, n_under=6, n_top=5,
      # A real bulwark instead of a Solidify shell -- see _level_plan.
      plank_t=0.13,
      topside=[(0.0, 0.97), (0.90, 1.00), (2.70, 0.90)],
      aft_end=0.46, aft_a=2.8, aft_b=0.45, aft_rise=0.02, sheer_aft=0.50,
      fwd_end=0.07, fwd_a=1.9, fwd_b=0.60, fwd_rise=0.86, sheer_fwd=0.80,
      rake_fwd=2.80, rake_aft=1.50, rake_k=2.1, planking=0.13,
      deck_z=1.25, gun_decks=[1.95],
      # Seven ports a side -- a fourteen-gun brig, which is a real rating.
      # The sill is 0.70 m above the deck and the bulwark is 1.45 m the whole
      # length of her (deck and rail carry the same sheer), so a 0.58 m port
      # leaves 0.17 m of capping rail above it. That looks thin written down
      # and is right: on a real brig the port head is just under the cap.
      # Centres are BUILT x, evenly spaced 3.0 m, which is a gun's recoil room.
      port_height=0.58, port_width=0.68,
      port_x=[-8.6, -5.6, -2.6, 0.4, 3.4, 6.4, 9.4],
      mast_x=[-4.6, 4.2], mast_height=[13.5, 15.5],
      bowsprit=True, rudder=True, keel_batten=True),

 dict(name="T5_ShipOfTheLine", tier=5, label="Three-decker",
      length=46.0, beam=13.0, draft=5.40, depth=12.40, midship=0.48,
      fullness=3.20, stations=32, n_under=7, n_top=6,
      topside=[(0.0, 0.99), (1.20, 1.00), (7.00, 0.80)],
      aft_end=0.44, aft_a=3.0, aft_b=0.42, aft_rise=0.02, sheer_aft=1.40,
      fwd_end=0.08, fwd_a=2.0, fwd_b=0.56, fwd_rise=0.82, sheer_fwd=2.00,
      rake_fwd=4.80, rake_aft=2.70, rake_k=2.1, planking=0.24,
      deck_z=5.15, gun_decks=[1.30, 3.60, 5.90],
      plank_t=0.24,
      # Twelve a side on each of three decks: 72 ports, which is a first-rate.
      # 0.80 m tall because the upper battery's sill is 5.90 and the rail is
      # 7.00, and a port has to leave some capping rail above it.
      port_height=0.80, port_width=0.90,
      port_x=[-14.95, -12.05, -9.15, -6.25, -3.35, -0.45,
              2.45, 5.35, 8.25, 11.15, 14.05, 16.95],
      # The inner planking runs down to the LOWER GUN DECK, not to the upper
      # deck: two of her three batteries are below deck_z, and a port has to be
      # a hole through a wall. See _inner_floor.
      inner_floor=0.55,
      station_end=0.80, station_mid=1.45, deck_spans=4,
      rudder=True, keel_batten=True,
      mast_x=[-11.0, 1.0, 13.5], mast_height=[24.0, 28.0, 22.0],
      bowsprit=True,
      deck_levels=[("Hold", -4.60), ("LowerGunDeck", 0.55),
                   ("MiddleGunDeck", 2.85), ("UpperGunDeck", 5.15)]),
]

RAFT = dict(name="T1_Raft", tier=1, label="Log raft",
            length=5.65, beam=3.40, draft=0.29, depth=0.61,
            # A raft with a small square sail is exactly what a raft is. She
            # is not built by the loft, so she gets her mast here rather than
            # through build_fittings.
            mast_x=[-0.20], mast_height=[3.60])

# ----------------------------------------------------------------- look -----
#
# The whole fleet wears ONE material and ONE 64x64 texture.
#
# The hulls used to be a single flat brown with no UVs, which is why they read
# as grey blobs with holes in: there was no plank, no wale, no caprail and no
# canvas -- every surface on the ship was the same surface. The fix is not
# geometry. A palette gives every face a colour for the cost of two floats of
# UV, so a strake, a rubbing wale and a painted port lining are all free, and
# the ship still draws in one call.
#
# **Chosen by GEOMETRY, never by face index.** A face knows where it is: how
# high up the hull it sits, which way it points, how far along her it is. That
# is enough to decide what it is made of, and it keeps this decoupled from the
# loft's face order -- which the bmesh recalc round trip is free to reorder,
# and which differs on every one of the twenty rungs anyway.

PALETTE_DIR = "/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/Art/Ship/Hulls"
PALETTE_NAME = "seasick_palette"
PALETTE_GRID = 5                     # 5x5 swatches, 16 px each -> 80x80

# sRGB picker values, read off Kevin's reference boat: warm oiled wood, a
# desaturated near-black for the trim, pale canvas.
SWATCH = [
    # Planks are ordered DARK to LIGHT, and the order is load-bearing: the
    # course a face belongs to picks its tone by HEIGHT, so she darkens toward
    # the water the way a real hull does. Hashing the tone made the variety
    # read as random; a gradient makes the same variety read as intent.
    # The SPREAD is what shows, not the level. Squeezed into four near
    # identical tones the gradient came out a single muddy brown and the
    # variety Kevin asked to keep disappeared with it; opened back up, the
    # same mean reads as planking again.
    (0.44, 0.30, 0.19),   # 0  plank, at the water
    (0.51, 0.35, 0.22),   # 1
    (0.57, 0.40, 0.25),   # 2
    (0.63, 0.45, 0.29),   # 3  plank, at the sheer
    (0.86, 0.72, 0.51),   # 4  deck, light
    (0.80, 0.66, 0.46),   # 5  deck, mid
    (0.26, 0.21, 0.19),   # 6  wale and trim
    (0.71, 0.54, 0.33),   # 7  caprail
    (0.30, 0.24, 0.20),   # 8  backbone, stem, rudder
    (0.37, 0.26, 0.17),   # 9  below the waterline
    (0.70, 0.54, 0.35),   # 10 mast and spar
    (0.82, 0.77, 0.67),   # 11 canvas, warm and dirty rather than white
    (0.74, 0.64, 0.46),   # 12 rope
    (0.44, 0.14, 0.11),   # 13 port lining, painted red
    (0.19, 0.19, 0.20),   # 14 iron
    (0.80, 0.66, 0.42),   # 15 carved work
    # A sail is SEWN, out of cloths a couple of feet wide, and the seams are
    # the only thing that stops a square sail reading as a sheet of paper.
    # Three near-identical whites, picked by which cloth a face sits in --
    # geometry again, and no triangles at all.
    (0.76, 0.72, 0.63),   # 16 canvas, shaded cloth
    (0.79, 0.75, 0.66),   # 17 canvas, mid cloth
    (0.60, 0.51, 0.36),   # 18 rope and rigging
    (0.21, 0.20, 0.20),   # 19 iron band, for mast hoops and gammoning
    (0.60, 0.45, 0.29),   # 20 spar, shaded
    (0.72, 0.58, 0.30),   # 21 gilt on carved work
    (0.50, 0.34, 0.21),   # 22 spare
    (0.55, 0.50, 0.44),   # 23 spare
    (0.66, 0.47, 0.29),   # 24 spare
]

SW = {"plank_l": 0, "plank_m": 1, "plank_d": 2, "plank_w": 3,
      "deck_l": 4, "deck_m": 5, "wale": 6, "caprail": 7, "timber": 8,
      "boot": 9, "spar": 10, "canvas": 11, "rope": 12, "lining": 13,
      "iron": 14, "carved": 15,
      "cloth_a": 16, "cloth_b": 17, "rigging": 18, "band": 19,
      "spar_d": 20, "gilt": 21}

CLOTHS = [11, 16, 17]

PLANKS = [SW["plank_l"], SW["plank_m"], SW["plank_d"], SW["plank_w"]]


def swatch_uv(i):
    """Centre of swatch `i`. The centre, not a corner: a mipmap averages
    neighbours, and a UV on a swatch boundary fades into the one next door as
    the ship sails away from the camera."""
    n = PALETTE_GRID
    return ((i % n + 0.5) / n, (i // n + 0.5) / n)


def write_palette(save=True):
    """Bake the palette image. Sixteen flat swatches, no gradients -- the
    variation in the finished ship comes from which swatch a plank is pointed
    at, not from anything inside the texture."""
    n, cell = PALETTE_GRID, 16
    size = n * cell
    img = bpy.data.images.get(PALETTE_NAME)
    if img and (img.size[0] != size or img.size[1] != size):
        bpy.data.images.remove(img)
        img = None
    if img is None:
        img = bpy.data.images.new(PALETTE_NAME, size, size, alpha=False)
    # Colour space FIRST. Setting it on an image whose source is FILE -- which
    # is what saving makes it -- re-reads the file and throws away whatever is
    # in the buffer, so doing it after the swatches were written wiped them.
    img.colorspace_settings.name = "sRGB"
    px = [0.0] * (size * size * 4)
    for i, (r, g, b) in enumerate(SWATCH):
        cx, cy = (i % n) * cell, (i // n) * cell
        for y in range(cy, cy + cell):
            for x in range(cx, cx + cell):
                o = (y * size + x) * 4
                px[o], px[o + 1], px[o + 2], px[o + 3] = r, g, b, 1.0
    img.pixels = px
    # **Never call `update()` on a GENERATED image.** It re-runs the
    # generator, which fills it with `generated_color` -- black -- and throws
    # the swatches away. That wrote a black PNG to disk, and because saving
    # flips the image's source to FILE, every later call reloaded that black
    # file over the colours in memory. The whole fleet rendered near-black
    # while the material, the UVs and the node graph all read as correct.
    if save:
        os.makedirs(PALETTE_DIR, exist_ok=True)
        img.filepath_raw = os.path.join(PALETTE_DIR, PALETTE_NAME + ".png")
        img.file_format = "PNG"
        img.save()
    return img


def wood_material():
    """One material for the whole fleet: the palette, plugged straight into
    base colour, with the interpolation set to Closest.

    Closest is load-bearing. Linear filtering on a 16 px swatch bleeds its
    neighbour in around the edges, and since a face's UV is a single point the
    bleed would show up as the wrong colour entirely at distance."""
    m = bpy.data.materials.get("SeaSick_Hull")
    if m is None:
        m = bpy.data.materials.new("SeaSick_Hull")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = None
    for nd in nt.nodes:
        if nd.type == "TEX_IMAGE":
            tex = nd
            break
    if tex is None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.location = (-380, 260)
    tex.image = write_palette()
    tex.interpolation = "Closest"
    if bsdf:
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        # Oiled wood, not varnish and not chalk. High enough that the sea is
        # the shiny thing in frame and the ship is the solid thing.
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.62
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = 0.0
        if "Specular IOR Level" in bsdf.inputs:
            bsdf.inputs["Specular IOR Level"].default_value = 0.35
    return m


# --------------------------------------------------------------- painting ---

def _shuffle(*keys):
    """A stable hash over quantised coordinates. Deterministic, so a rung
    looks the same every bake and two hulls the same length plank the same
    way -- a plank that changes colour when you girdle her is a plank the eye
    reads as a bug."""
    h = 2166136261
    for k in keys:
        h = ((h ^ (int(k) & 0xFFFF)) * 16777619) & 0xFFFFFFFF
    return h


def _hull_swatch(p, c, nz, z_rail, lining):
    """What one face of the hull is made of, from where it sits.

    **A strake is a COURSE, not a coin toss per face.** The first pass hashed
    on height AND length together, so neighbouring faces in the same strake
    came out different colours and her whole side read as a checkerboard --
    noise, at the exact scale that reads as a broken texture rather than as
    planking. A plank runs the length of the ship; its tone is decided once,
    by how high up she it sits, and only occasionally broken."""
    if lining:
        return SW["lining"]
    z = c[2]
    # The capping rail: the only surface up there that faces the sky.
    if z > z_rail * 0.80 and nz > 0.55:
        return SW["caprail"]
    if z < -0.02:
        return SW["boot"]
    t = z / max(1e-4, z_rail)
    # ONE dark band, the sheer strake under the rail. The second band the
    # first pass painted low down is now a real timber -- see `build_trim` --
    # and a painted stripe under a physical wale is one dark line too many.
    if t >= 0.80:
        return SW["wale"]
    course = max(0.25, z_rail / 7.0)
    band = int(z / course)
    # **The tone is a GRADIENT, not a draw.** Which plank a strake is cut from
    # is decided by how high it sits: darkest at the water, lightest under the
    # rail. Drawing it from the four tones at random gave the same amount of
    # variety and read as noise, because nothing about it was going anywhere.
    k = min(len(PLANKS) - 1, max(0, int(t * len(PLANKS))))
    # One plank in five is a different stick of timber -- the variety Kevin
    # liked -- but it only ever moves ONE step off the gradient, so a strake
    # never jumps the whole range and the shading still reads as a hull.
    if _shuffle(band, c[0] / max(0.6, p["length"] / 12.0)) % 5 == 0:
        k = min(len(PLANKS) - 1, max(0, k + (1 if _shuffle(band) % 2 else -1)))
    return PLANKS[k]


def paint(ob, p, kind="hull"):
    """Point every face at a swatch. Adds a UV layer and nothing else.

    `kind` says what the object IS -- the geometry says the rest. A hull is
    the only one that has to think; a mast is a mast all the way up."""
    me = ob.data
    if not hasattr(me, "polygons") or not me.polygons:
        return ob
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")

    z_rail = p["depth"] - p["draft"]
    # Gun-port linings are the only faces `build_hull` leaves flat-shaded on an
    # otherwise smooth hull, so they can be identified before the whole hull
    # goes flat -- no change to the loft, and no second list to keep in step.
    linings = {i for i, pg in enumerate(me.polygons) if not pg.use_smooth} \
        if kind == "hull" else set()

    # The deck's MARGIN PLANK: the dark board that runs round the edge of a
    # laid deck, against which every other plank is cut. It is the cheapest
    # character on the whole ship -- no geometry at all, just a different
    # swatch on the faces that happen to be on the boundary.
    #
    # Found from the topology rather than from a distance: a boundary edge is
    # one used by a single face, which is exactly the deck's outline. The
    # centreline is excluded because the Mirror makes it a boundary too, and a
    # dark stripe down the middle is a king plank nobody asked for.
    margin = set()
    if kind == "deck":
        used = {}
        for pg in me.polygons:
            for ek in pg.edge_keys:
                used[ek] = used.get(ek, 0) + 1
        edge_on_hull = {ek for ek, n in used.items() if n == 1
                        and not (abs(me.vertices[ek[0]].co.y) < 1e-3
                                 and abs(me.vertices[ek[1]].co.y) < 1e-3)}
        for pg in me.polygons:
            if any(ek in edge_on_hull for ek in pg.edge_keys):
                margin.add(pg.index)

    # The object's own vertical extent, for anything banded by height.
    z_lo = min((v.co.z for v in me.vertices), default=0.0)
    span_z = max((v.co.z for v in me.vertices), default=1.0) - z_lo

    for pg in me.polygons:
        c, nrm = pg.center, pg.normal
        if kind == "hull":
            i = _hull_swatch(p, c, nrm.z, z_rail, pg.index in linings)
        elif kind == "deck":
            if pg.index in margin:
                # The darkest PLANK, not the backbone's near-black timber. A
                # margin is a board of the same wood laid the other way, and
                # at 0.30 luma it read as a hole cut round the edge of the
                # deck rather than as a plank.
                i = PLANKS[0]
            else:
                # Planks run fore and aft, so the variation runs ACROSS her.
                width = max(0.20, p["beam"] / 9.0)
                i = (SW["deck_l"] if _shuffle(c[1] / width) % 2 else SW["deck_m"])
        elif kind == "lid":
            i = SW["wale"]
        elif kind == "ladder":
            # Darker than the deck it stands on, or the treads disappear into
            # it -- which is exactly what happened when they were painted as
            # deck planking.
            i = PLANKS[0]
        elif kind == "carved":
            i = SW["carved"]
        elif kind == "caprail":
            i = SW["caprail"]
        elif kind == "wale":
            i = SW["wale"]
        elif kind == "canvas":
            # Which CLOTH this face sits in. A sail is sewn from strips a
            # couple of feet wide, and the seams are the only thing that stops
            # it reading as a sheet of paper -- three near-identical whites,
            # picked by position across her, and not one extra triangle.
            i = CLOTHS[int(abs(c[1]) / max(0.35, p["beam"] * 0.075))
                       % len(CLOTHS)]
        elif kind == "mast":
            # Iron bands up the spar. The mast is built with real rings along
            # its length precisely so this can be done by HEIGHT, with no
            # geometry for a band at all.
            t = ((c[2] - z_lo) / span_z) if span_z > 1e-6 else 0.0
            i = SW["band"] if (0.20 <= t <= 0.245 or 0.52 <= t <= 0.575
                               or 0.79 <= t <= 0.825) else SW["spar"]
        elif kind == "band":
            i = SW["band"]
        elif kind == "spar":
            i = SW["spar"]
        elif kind == "iron":
            i = SW["iron"]
        else:
            i = SW["timber"]
        u, v = swatch_uv(i)
        for li in pg.loop_indices:
            uv.data[li].uv = (u, v)

    # Flat, now that the linings have been read off.
    #
    # Smooth shading is why she had no planking: it averages every station into
    # its neighbours and turns 555 faces into one continuous sheet. Planking is
    # flat by nature -- a plank is a plank and the next one is a different
    # plank. This costs no triangles at all; it splits vertices on export.
    if kind in ("hull", "deck", "trim", "caprail", "wale", "carved",
                "lid", "band", "ladder"):
        for pg in me.polygons:
            pg.use_smooth = False
    me.update()
    return ob


# Scanned IN ORDER, so anything whose name contains another entry's suffix has
# to come first -- "_Mast1Top" contains "_Mast".
KIND_BY_SUFFIX = [
    ("_Hull", "hull"), ("_Deck", "deck"),
    ("_Sail", "canvas"),
    ("_Caprail", "caprail"), ("_Wale", "wale"), ("_StemHead", "carved"),
    ("_PortLid", "lid"), ("_Boom", "spar"), ("_Timberhead", "caprail"),
    ("_Transom", "hull"), ("_Taffrail", "caprail"), ("_Quarterdeck", "deck"),
    ("_HelmWheel", "trim"), ("_HelmSpokes", "spar"), ("_HelmPost", "trim"),
    ("Top", "trim"),
    ("_Band", "band"), ("_Knighthead", "trim"), ("_DolphinStriker", "trim"),
    ("_BowspritKnee", "trim"),
    ("_QdStanchion", "caprail"), ("_QdRail", "caprail"),
    ("_Tread", "ladder"), ("_Stringer", "ladder"),
    ("_Mast", "mast"), ("_Bowsprit", "mast"), ("_Yard", "spar"),
    ("_Backbone", "trim"), ("_Rudder", "trim"), ("_Channel", "trim"),
]


def paint_kind(name):
    """What a built object is, from what the generator called it. The names
    are already a taxonomy; this reads it rather than inventing a second one."""
    for suffix, kind in KIND_BY_SUFFIX:
        if suffix in name:
            return kind
    return "trim"


def dress(objs, p):
    """Give a hull's objects the material and their swatches, in one call.
    Everything that builds a ship goes through here."""
    mat = wood_material()
    for o in objs:
        if o is None or o.type != "MESH":
            continue
        if not o.data.materials:
            o.data.materials.append(mat)
        else:
            o.data.materials[0] = mat
        paint(o, p, paint_kind(o.name))
    return objs

# ---------------------------------------------------------------- build -----

def build_all():
    clear_fleet()
    mat = wood_material()
    built = []

    # T1 — the raft
    c = get_collection("T1_Raft")
    raft = build_raft(c)
    rig = [_tube("T1_Raft_Mast1", c, (RAFT["mast_x"][0], -0.10),
                 (RAFT["mast_x"][0], RAFT["mast_height"][0]),
                 RAFT["beam"] * 0.030, RAFT["beam"] * 0.018)]
    rig += build_sails(RAFT, c)
    built.append((RAFT, [raft] + rig, c))

    # T2..T5 — lofted hulls
    for p in FLEET:
        c = get_collection(p["name"])
        hull, stations, tags = build_hull(p, c)
        objs = [hull]
        if p.get("deck_z") is not None:
            objs.append(build_deck(p, stations, c, p["deck_z"], tags))
        objs.extend(build_fittings(p, stations, c, tags))
        objs.extend(build_trim(p, stations, c, tags, hull))
        objs.extend(build_stern(p, stations, c, tags))
        objs.extend(build_sails(p, c))
        build_port_anchors(p, stations, tags, c, hull)
        built.append((p, objs, c))

    for p, objs, c in built:
        dress(objs, p)

    # the three-decker's battery levels, as empties you can build decks off
    for p, objs, c in built:
        for label, z in p.get("deck_levels", []):
            e = bpy.data.objects.new(p["name"] + "_" + label, None)
            e.empty_display_type = 'SINGLE_ARROW'
            e.empty_display_size = p["beam"] * 0.35
            e.location = (0.0, 0.0, z)
            c.objects.link(e)
            e.parent = objs[0]
            e.matrix_parent_inverse = objs[0].matrix_world.inverted()

    # lay the fleet out beam to beam for a group photograph
    ref = get_collection("REFERENCE")
    y = 0.0
    for p, objs, c in built:
        y += p["beam"] * 0.5 + 2.5
        for o in objs:
            o.location.y = y
        c["lineup_y"] = y
        # a 1.7 m crew member at the bow of each, on the waterline
        crew = crew_ref(p["name"] + "_CrewRef",
                        p["length"] * 0.5 + 2.0, y, ref)
        y += p["beam"] * 0.5

    waterline(ref)
    return built


def crew_ref(name, x, y, coll):
    h, w, d = 1.70, 0.46, 0.30
    verts = [(x - d/2, y - w/2, 0), (x + d/2, y - w/2, 0),
             (x + d/2, y + w/2, 0), (x - d/2, y + w/2, 0),
             (x - d/2, y - w/2, h), (x + d/2, y - w/2, h),
             (x + d/2, y + w/2, h), (x - d/2, y + w/2, h)]
    faces = [[0,3,2,1],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]]
    return make_object(name, verts, faces, coll, smooth=False, mirror=False)


def waterline(coll):
    s = 90.0
    verts = [(-s, -6, 0), (s, -6, 0), (s, 62, 0), (-s, 62, 0)]
    ob = make_object("WaterlinePlane", verts, [[0,1,2,3]], coll,
                     smooth=False, mirror=False)
    m = bpy.data.materials.get("SeaSick_Water")
    if m is None:
        m = bpy.data.materials.new("SeaSick_Water")
        m.use_nodes = True
        b = m.node_tree.nodes.get("Principled BSDF")
        if b:
            b.inputs["Base Color"].default_value = (0.05, 0.19, 0.26, 1.0)
    ob.data.materials.append(m)
    return ob


if __name__ == "__main__" or True:
    RESULT = build_all()
    print("built:", [p["name"] for p, o, c in RESULT])


# ------------------------------------------------------------ topology ------
# The build is not finished when it looks right; it is finished when it PASSES.
# Kevin's rule for these assets: a mesh may intersect another object (a mast
# goes through a deck) but it may never contain two of its own faces in the
# same place, or two vertices, or an edge of no length. Every one of those is
# invisible in a viewport and expensive the first time somebody tries to edit,
# unwrap or weld the thing months later.


def audit(o, eps=1e-4):
    """Everything that makes a mesh unpleasant to inherit, counted."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    bm = bmesh.new()
    bm.from_mesh(me)
    r = {"verts": len(bm.verts), "faces": len(bm.faces)}

    grid = {}
    r["coincident_verts"] = 0
    for v in bm.verts:
        k = (round(v.co.x / eps), round(v.co.y / eps), round(v.co.z / eps))
        if k in grid:
            r["coincident_verts"] += 1
        grid[k] = v.index

    r["zero_len_edges"] = sum(1 for e in bm.edges if e.calc_length() < eps)
    r["repeated_verts"] = sum(1 for f in bm.faces
                              if len({v.index for v in f.verts}) != len(f.verts))
    r["zero_area_faces"] = sum(1 for f in bm.faces if f.calc_area() < 1e-7)

    seen = set()
    r["duplicate_faces"] = 0
    for f in bm.faces:
        k = tuple(sorted(v.index for v in f.verts))
        if k in seen:
            r["duplicate_faces"] += 1
        seen.add(k)

    cen = set()
    r["overlapping_faces"] = 0
    for f in bm.faces:
        c = f.calc_center_median()
        k = (round(c.x / eps), round(c.y / eps), round(c.z / eps))
        if k in cen:
            r["overlapping_faces"] += 1
        cen.add(k)

    r["ngons"] = sum(1 for f in bm.faces if len(f.verts) > 4)
    r["tris"] = sum(1 for f in bm.faces if len(f.verts) == 3)
    r["open_edges"] = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    r["non_manifold"] = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    r["triangles"] = len(bm.faces)
    bm.free()
    ev.to_mesh_clear()
    return r


# Open edges are ALLOWED and counted, not failed: the bulwark's bottom rim
# sits on the deck, and closing it back to the outer skin would put three
# faces on an edge. Everything else here is a defect.
FATAL = ("coincident_verts", "zero_len_edges", "repeated_verts", "zero_area_faces",
         "duplicate_faces", "overlapping_faces", "non_manifold", "ngons")


def report_topology(prefix="T"):
    cols = ["triangles", "coincident_verts", "zero_len_edges", "zero_area_faces",
            "duplicate_faces", "overlapping_faces", "ngons", "tris",
            "non_manifold", "open_edges"]
    head = ["tris", "coinV", "len0", "area0", "dupF", "ovlF", "ngon", "tri3",
            "nonmf", "open"]
    print("%-26s %s" % ("object", " ".join("%6s" % h for h in head)))
    bad, total = [], 0
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type != 'MESH' or not o.name.startswith(prefix) or "CrewRef" in o.name:
            continue
        r = audit(o)
        total += r["triangles"]
        print("%-26s %s" % (o.name, " ".join("%6d" % r[c] for c in cols)))
        for k in FATAL:
            if r[k]:
                bad.append((o.name, k, r[k]))
    print("TOTAL %d triangles" % total)
    if bad:
        print("*** TOPOLOGY DEFECTS ***")
        for n, k, v in bad:
            print("   %-26s %-20s %d" % (n, k, v))
    else:
        print("CLEAN: no coincident verts, no overlapping or duplicate faces, "
              "no n-gons, no non-manifold edges")
    return bad


# ---------------------------------------------------------------- export ----
# Measured mapping for this Blender/FBX/Unity chain, NOT assumed:
#   Blender (x, y, z)  ->  Unity (-x, z, -y)
# so the bow, drawn at Blender +X, lands on Unity -X and wants a 90 deg yaw
# fix in the engine -- which is exactly what SetupPaddleBoat.VisualYaw is, and
# exactly the sort of thing that costs a build. So the fix happens HERE:
# each hull is yawed -90 deg about Blender Z (bow +X -> -Y, which IS Unity +Z)
# and `bake_space_transform` burns that, the Z-up conversion and the unit
# scale into the vertices. The result imports with identity nodes at scale 1,
# bow on +Z, no correction needed anywhere downstream.

UNITY_ART = "/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/Art/Ship/Hulls"

EXPORT_FILES = {
    "T1_Raft": "hull_t1_raft.fbx",
    "T2_Skiff": "hull_t2_skiff.fbx",
    "T3_Sloop": "hull_t3_sloop.fbx",
    "T4_Brig": "hull_t4_brig.fbx",
    "T5_ShipOfTheLine": "hull_t5_shipoftheline.fbx",
}


def export_fleet(out_dir=UNITY_ART):
    import os
    os.makedirs(out_dir, exist_ok=True)
    vl = bpy.context.view_layer
    for o in bpy.data.objects:
        o.hide_set(False)
    done = []
    for cname, fname in EXPORT_FILES.items():
        objs = list(bpy.data.collections[cname].objects)
        roots = [o for o in objs if o.parent is None]
        saved = {o.name: (tuple(o.location), tuple(o.rotation_euler)) for o in roots}
        for o in bpy.data.objects:
            o.select_set(False)
        for o in roots:
            o.location = (0.0, 0.0, 0.0)
            o.rotation_euler = (0.0, 0.0, math.radians(-90.0))
        for o in objs:
            o.select_set(True)
        vl.objects.active = objs[0]
        vl.update()
        path = os.path.join(out_dir, fname)
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
            use_mesh_modifiers=True, mesh_smooth_type='FACE',
            global_scale=1.0, apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=True,          # burn it all into the vertices
            axis_forward='-Z', axis_up='Y',
            use_triangles=False, add_leaf_bones=False, path_mode='COPY')
        for o in roots:
            o.location, o.rotation_euler = saved[o.name]
        vl.update()
        done.append(fname)
    return done


# ---------------------------------------------------------- hydrostatics ----
# Displacement, the naval-architect way: sectional area at every station,
# integrated along the length. The buoyancy rig in Unity needs an HONEST mass
# -- the paddle steamer's was carried up from an older boat by a cube law and
# came out about a sixth of what her drawn volume displaces, which is why she
# floated 0.57 m high and needed a fudge constant to sit right. These hulls get
# the number the shape actually implies.


def _section_area(ring, z_top):
    """Half-section area from the keel up to z_top, m^2 (trapezoid on y dz)."""
    a = 0.0
    for k in range(len(ring) - 1):
        z0, y0 = ring[k][2], ring[k][1]
        z1, y1 = ring[k + 1][2], ring[k + 1][1]
        if z1 <= z0:
            continue
        if z0 >= z_top:
            break
        if z1 > z_top:                      # clip the last slice at the surface
            t = (z_top - z0) / (z1 - z0)
            y1 = y0 + (y1 - y0) * t
            z1 = z_top
        a += 0.5 * (y0 + y1) * (z1 - z0)
    return a


def _volume(stations, z_top):
    """Both sides, integrated along the length."""
    v = 0.0
    for i in range(len(stations) - 1):
        x0 = stations[i][0][0]
        x1 = stations[i + 1][0][0]
        a0 = _section_area(stations[i], z_top)
        a1 = _section_area(stations[i + 1], z_top)
        v += (a0 + a1) * (x1 - x0)          # 0.5*(a0+a1)*dx, doubled for 2 sides
    return v


def hydrostatics():
    """Displacement at the drawn waterline and volume to the rail, per hull."""
    rho = 1025.0
    rows = []
    for p in FLEET:
        coll = bpy.data.collections.new("_hydro_tmp")
        bpy.context.scene.collection.children.link(coll)
        try:
            hull, stations, _tags = build_hull(p, coll)
            v_disp = _volume(stations, 0.0)                    # to the waterline
            v_full = _volume(stations, p["depth"] - p["draft"])  # to the rail
        finally:
            # Always, not just on success: a hull left behind here is what
            # produced a stray `T2_Skiff_Hull.001` sitting inside the skiff.
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(coll)
        lwl = p["length"] - p.get("rake_fwd", 0) * 0.6 - p.get("rake_aft", 0) * 0.6
        cb = v_disp / max(1e-6, lwl * p["beam"] * p["draft"])
        rows.append((p["name"], v_disp, v_full, rho * v_disp / 1000.0, cb,
                     v_disp / v_full))
    # the raft is a bundle of cylinders, not a loft: solve it directly
    r, n, cz = 0.24, 7, -0.05
    lens = [5.10, 5.45, 5.60, 5.65, 5.55, 5.40, 5.15]
    import math as _m
    sub = 0.0
    for L in lens:                       # circular segment below z = 0
        d = -cz                          # depth of the water line above centre
        th = 2.0 * _m.acos(max(-1.0, min(1.0, -d / r)))
        seg = 0.5 * r * r * (th - _m.sin(th))
        sub += seg * L
    total = sum(_m.pi * r * r * L for L in lens)
    rows.insert(0, ("T1_Raft", sub, total, rho * sub / 1000.0,
                    sub / (5.65 * 3.40 * 0.29), sub / total))

    print(f"{'hull':<20}{'V_disp':>9}{'V_rail':>10}{'mass t':>9}"
          f"{'Cb':>7}{'float':>8}")
    for name, vd, vf, t, cb, fr in rows:
        print(f"{name:<20}{vd:>9.2f}{vf:>10.2f}{t:>9.1f}{cb:>7.3f}{fr:>8.3f}")
    return rows
