"""New raised-middle variants with a wall on one or both faces (RAISED-SECTIONS.md §3).

Reuses Astra's raised-stern forward drop-edge kit (ForwardGuard, TwinStairs, StairGuards,
StairNosings, Door, PortalFrames) from art-staging/modular-width-raised-v1 both-raised.blend,
which already sits in the same whole-ship coordinate frame as the fully-connected raised
middle (art-staging/modular-raised-middle-v1, built by modular_raised_middle_v1.py). That
script's own union/trim setup is reproduced verbatim here to get a clean Midship_W1 shell,
then instead of deleting the wall/stair kit on the discarded Stern_W1 root, this script
reparents it onto Midship_W1:
  - wf (wall forward): translate +6.0 in X, no mirror ("as authored": the kit already faces
    forward/low-neighbour-ahead, matching the middle's own forward face).
  - wa (wall aft): mirror about the wall plane X=9.3 (interior/raised side flips, matching
    the middle's aft face where the raised deck is ahead of the wall, not behind it).
  - wb (both walls): wf as above; the aft face gets a DOOR-ONLY closure (bulkhead + door,
    no stairs) because measurement shows the stair kit's footprint (TwinStairs alone spans
    x 6.06-9.26, a 3.2 u run; the full kit incl. guards spans x 5.995-9.40, ~3.4 u) does not
    fit twice inside the 6 u middle with >=0.8 u of clear deck between (3.4*2=6.8 > 6.0).
    Forward end keeps the stairs (as-authored orientation, lower risk to reuse unmirrored).

Run per variant: Blender --background --python modular_raised_sections_v1.py -- <wa|wf|wb>
"""
import json
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import modular_raised_stern_v2 as revision
family, ship, ss, fore = revision.family, revision.ship, revision.ss, revision.fore

SRC = HERE.parents[1] / "art-staging/modular-width-raised-v1/both-raised.blend"
OUT_ROOT = HERE.parents[1] / "art-staging/modular-raised-sections-v1"

WALL_PLANE_AFT = 9.30      # Stern/Midship boundary in the shared whole-ship frame
MID_LEN = 6.0               # Midship section length; also the fwd-face translate offset
BEAM_HALF = 6.04  # matches the hull's own flat-walled band (Z 0.84-4.20, see close_face)
STAIR_Y_BANDS = [(-5.80, -4.62), (4.62, 5.80)]
WALL_Z = (1.70, 4.20)

STAIR_KIT = ["ForwardGuard", "TwinStairs", "StairGuards", "StairNosings", "PortalFrames"]
DOOR_ONLY_KIT = ["PortalFrames"]


def volume(name, x0, x1, y0, y1, z0, z1):
    b = ss.Builder()
    fore.box(b, x0, x1, y0, y1, z0, z1, "deck")
    ob = ship.mesh(name, b)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(ob.data); bm.free()
    return ob


def clean(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=3e-4)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=5e-4)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(ob.data); bm.free()
    for p in ob.data.polygons:
        p.use_smooth = False
    if ob.data.color_attributes.get("Col"):
        ob.data.color_attributes.active_color = ob.data.color_attributes["Col"]


def build_base():
    """Reproduces modular_raised_middle_v1.py's union+trim to get a clean Midship_W1 shell."""
    bpy.ops.wm.open_mainfile(filepath=str(SRC))
    tree_src = (HERE / "modular_width_inserts_v1.py").read_text()
    import ast
    tree = ast.parse(tree_src)
    fn = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "combined")
    exec(compile(ast.Module(body=[fn], type_ignores=[]), str(HERE / "modular_width_inserts_v1.py"), "exec"), globals())
    names = ["Stern_W1", "Midship_W1", "Bow_W1"]
    roots = {n: bpy.data.objects[n] for n in names}
    shells = [bpy.data.objects[n + "__Hull_Shell"] for n in names]
    whole, check = combined("Continuous_Hull", shells, True)
    for mat in shells[0].data.materials:
        whole.data.materials.append(mat)
    bpy.context.scene.collection.objects.link(whole)
    whole.data.color_attributes.active_color = whole.data.color_attributes["Col"]
    for ob in shells:
        bpy.data.objects.remove(ob, do_unlink=True)
    ship.boolean_into(whole, volume("Middle_Upper_Volume", 9.25, 15.35, -6.04, 6.04, 1.70, 4.20), "UNION")
    for sign in [-1, 1]:
        lo, hi = sorted([sign * 4.62, sign * 5.80])
        ship.boolean_into(whole, volume("Fill_Obsolete_Stair_Well", 6.055, 9.31, lo - .003, hi + .003, 1.70, 4.20), "UNION")
    clean(whole)
    chk = ship.check(whole)
    assert not any(chk[k] for k in ["boundary_edges", "overconnected_edges", "degenerate_faces"]), chk
    for name, lo, hi in [("Stern_W1", -100, 9.3), ("Midship_W1", 9.3, 15.3), ("Bow_W1", 15.3, 100)]:
        ob = family.trim(whole, name + "__Hull_Shell", lo, hi)
        ob.parent = roots[name]
        clean(ob)
    bpy.data.objects.remove(whole, do_unlink=True)
    return roots


def close_face(mid, world_plane, stairs):
    """Cap the Midship shell's open interface loop at world_plane between Z 1.70 and 4.20
    with flat bulkhead face(s), by bisecting the shell at Z=1.70 (to split the always-open
    lower interface from the closeable upper one, matching the existing edge already at
    Z=4.20) and edge-net-filling the resulting sub-loop. Direct boolean union of a solid
    slab against this shell kept producing non-manifold junctions, because the shell is
    itself open (non-manifold) at the interface being closed -- the EXACT solver is not
    reliable against a non-manifold operand there. A native bmesh cap avoids that.
    When stairs=True the two stair-corner Y bands (STAIR_Y_BANDS) are left uncapped."""
    shell = bpy.data.objects["Midship_W1__Hull_Shell"]
    local_plane = world_plane - mid.location.x
    bm = bmesh.new(); bm.from_mesh(shell.data)
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                            plane_co=(local_plane, 0, 1.70), plane_no=(0, 0, 1), dist=1e-6)
    # Boundary edges lying in the world_plane x-plane, at or above z=1.70 (the closeable band).
    loop_edges = [e for e in bm.edges if len(e.link_faces) == 1
                  and all(abs(v.co.x - local_plane) < 1e-3 and v.co.z >= 1.70 - 1e-4 for v in e.verts)]

    def band_edges(y0, y1):
        out = []
        for e in loop_edges:
            ys = sorted(v.co.y for v in e.verts)
            if ys[0] >= y0 - 1e-3 and ys[1] <= y1 + 1e-3:
                out.append(e)
        return out

    bands = [(-BEAM_HALF, BEAM_HALF)] if not stairs else [
        (-BEAM_HALF, STAIR_Y_BANDS[0][0]), (STAIR_Y_BANDS[0][1], STAIR_Y_BANDS[1][0]), (STAIR_Y_BANDS[1][1], BEAM_HALF)
    ]
    for y0, y1 in bands:
        edges = band_edges(y0, y1)
        if len(edges) < 2:
            continue
        verts = set()
        for e in edges:
            verts.update(e.verts)
        # Close the band with a straight edge along z=1.70 so edgenet_fill has a full loop.
        bottom = sorted([v for v in verts if abs(v.co.z - 1.70) < 1e-3], key=lambda v: v.co.y)
        new_edges = list(edges)
        for a, b in zip(bottom, bottom[1:]):
            new_edges.append(bm.edges.new((a, b)))
        res = bmesh.ops.edgenet_fill(bm, edges=new_edges)
        for f in res["faces"]:
            f.material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(shell.data); bm.free()


def take_kit(mid, parts, mirror):
    """Reparent+transform named RaisedStern__<part> meshes from Stern_W1 onto Midship_W1."""
    stern = bpy.data.objects["Stern_W1"]
    moved = []
    for ob in list(stern.children):
        if ob.type != "MESH":
            continue
        comp = ob.name.split("__")[-1]
        if comp not in parts:
            continue
        bm = bmesh.new(); bm.from_mesh(ob.data)
        for v in bm.verts:
            if mirror:
                v.co.x = 2 * WALL_PLANE_AFT - v.co.x
            else:
                v.co.x = v.co.x + MID_LEN
        if mirror:
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(ob.data); bm.free()
        loc = Vector(ob.location)
        if mirror:
            loc.x = 2 * WALL_PLANE_AFT - loc.x
        else:
            loc.x = loc.x + MID_LEN
        ob.location = loc
        ob.parent = mid
        ob.name = "RaisedSection__" + comp + ("_Aft" if mirror else "_Fwd")
        moved.append(ob)
    return moved


def take_door(mid, mirror):
    stern = bpy.data.objects["Stern_W1"]
    ob = bpy.data.objects.get("RaisedStern__Door")
    if ob is None:
        return None
    cp = ob.copy(); cp.data = ob.data.copy()
    bpy.context.scene.collection.objects.link(cp)
    bm = bmesh.new(); bm.from_mesh(cp.data)
    for v in bm.verts:
        v.co.x = (2 * WALL_PLANE_AFT - v.co.x) if mirror else (v.co.x + MID_LEN)
    if mirror:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(cp.data); bm.free()
    loc = Vector(cp.location)
    loc.x = (2 * WALL_PLANE_AFT - loc.x) if mirror else (loc.x + MID_LEN)
    cp.location = loc
    cp.parent = mid
    cp.name = "RaisedSection__Door_" + ("Aft" if mirror else "Fwd")
    return cp


def build_variant(mode):
    assert mode in ("wa", "wf", "wb")
    roots = build_base()
    mid = roots["Midship_W1"]
    out = OUT_ROOT / {"wa": "MiddleWA", "wf": "MiddleWF", "wb": "MiddleWB"}[mode]
    out.mkdir(parents=True, exist_ok=True)
    stair_ends = []
    if mode == "wa":
        close_face(mid, WALL_PLANE_AFT, stairs=True)
        take_kit(mid, STAIR_KIT, mirror=True)
        take_door(mid, mirror=True)
        stair_ends = ["aft"]
    elif mode == "wf":
        close_face(mid, WALL_PLANE_AFT + MID_LEN, stairs=True)
        take_kit(mid, STAIR_KIT, mirror=False)
        take_door(mid, mirror=False)
        stair_ends = ["fwd"]
    else:  # wb: stairs fwd, door-only aft
        close_face(mid, WALL_PLANE_AFT + MID_LEN, stairs=True)
        take_kit(mid, STAIR_KIT, mirror=False)
        take_door(mid, mirror=False)
        close_face(mid, WALL_PLANE_AFT, stairs=False)
        take_kit(mid, DOOR_ONLY_KIT, mirror=True)
        take_door(mid, mirror=True)
        stair_ends = ["fwd"]

    for ob in list(mid.children):
        if ob.type == "MESH":
            clean(ob)

    shell = bpy.data.objects["Midship_W1__Hull_Shell"]
    check = ship.check(shell)

    manifest = {
        "id": "hull.middle.w1xr." + mode + ".v1",
        "interface": "W1-center-expansion-r1",
        "mode": mode,
        "stair_ends": stair_ends,
        "length": MID_LEN,
        "deck_height": 4.20,
        "beam": 12.08,
        "source_axes": "+X forward, +Y port, +Z up",
        "export_axes": "game (-y,z,x)",
        "wall_faces": {"wa": ["aft"], "wf": ["fwd"], "wb": ["fwd (stairs)", "aft (door-only)"]}[mode],
        "hull_shell_check": check,
        "parts": {},
    }
    for ob in mid.children:
        if ob.type != "MESH":
            continue
        t = ship.check(ob)
        assert not t["overconnected_edges"] and not t["degenerate_faces"], (ob.name, t)
        path = out / (ob.name + ".fbx")
        ss.export_fbx(ob, str(path))
        manifest["parts"][ob.name] = {
            "file": str(path.relative_to(out)),
            "position": list(ob.location),
            "rotation_radians": list(ob.rotation_euler),
            "triangles": t["triangles"],
            "topology": t,
        }
    manifest["triangles"] = sum(v["triangles"] for v in manifest["parts"].values())
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2))

    hide = {o: o.hide_render for o in bpy.context.scene.objects if o.type == "MESH"}
    for o in hide:
        o.hide_render = o.parent != mid
    family.render(out / "threequarter.png", (0, -22, 16), (12.3, 0, 2.6), 30, (1400, 1000))
    family.render(out / "side.png", (12.3, -30, 2.6), (12.3, 0, 2.6), 24, (1400, 800))
    family.render(out / "top.png", (12.3, 0.01, 26), (12.3, 0, 2.6), 30, (1400, 1000))
    for o, h in hide.items():
        o.hide_render = h
    print("SECTION VARIANT", mode, "PASS", manifest["triangles"], check)
    return manifest


if __name__ == "__main__":
    # argv[0] after '--' is consumed by modular_foredeck_v1 as narrow/wide (must be "wide"
    # for this kit); this script's own mode is the next token.
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    mode = argv[1] if len(argv) > 1 else "wa"
    build_variant(mode)
