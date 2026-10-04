"""Inside-out face guard for exported kit FBXs (2026-10-04).

The trap: Blender's approval renders draw BOTH sides of every face, so a stone whose faces are partly
wound inward looks perfect there. Unity's shaders cull back faces (`SeaSick/Environment Toon*` have no
`Cull` line, so Cull Back), and the same stone renders in game as shattered shards with gaps.

Usage (headless, one run at a time):
    Blender -b --factory-startup --python tools/blender/check_winding.py -- <fbx> [<fbx> ...]
    Blender -b --factory-startup --python tools/blender/check_winding.py -- --fix <in.fbx> <out.fbx>

The rule (topological, no heuristics): kits are flat-shaded, so every face has its own vertices. Faces
are welded by POSITION (1e-5 m) into islands across edges shared by exactly two faces. On a closed
island (no open edges) every shared edge must run in opposite directions in its two faces, and the
signed volume must be positive (normals out). A closed island that fails either test renders with
holes under back-face culling. Open islands (sheets: canvas, sails, single planes) are skipped:
which side shows is a design choice, not a winding error.

Exit code: 0 = clean, 1 = inside-out faces found, 2 = a file failed to import.
`--fix` re-orients every island outward (closed: by signed volume; open: keeps the majority's side),
reverses only the faces that need it, keeps their authored flat normals (negated with the face),
vertex colours, names, hierarchy, empties and transforms, and re-exports with Blender's default FBX
settings (-Z forward, Y up, FBX_SCALE_NONE, apply_unit_scale, baked animation as imported; the kits'
original settings), with colours linear when the source holds float colours above 1. The
output is re-checked; compare it with the input with tools/blender/fbx_raw_diff.py.

Also importable (exec) by kit check.py scripts: `winding_report(objects) -> list of error strings`.
"""
import bpy, sys
from collections import defaultdict

WELD = 1e-5        # metres in the object's local frame; kits are authored in metres
MIN_VOL = 1e-9     # |signed volume| below this = a degenerate island, ignored


def _key(co, s):
    return (round(co.x * s), round(co.y * s), round(co.z * s))


def analyse_mesh(me, weld=WELD):
    """Islands of a mesh welded by position. Returns (face_flip, islands):
    face_flip[i] = True if face i must be reversed to make its island consistent AND outward;
    islands = list of dicts {faces, closed, incons, volume, flips}."""
    s = 1.0 / weld
    vk = [_key(v.co, s) for v in me.vertices]
    edges = defaultdict(list)       # undirected key edge -> [(face, +1/-1)]
    for p in me.polygons:
        ks = [vk[i] for i in p.vertices]
        n = len(ks)
        for j in range(n):
            a, b = ks[j], ks[(j + 1) % n]
            if a == b: continue
            edges[(a, b) if a < b else (b, a)].append((p.index, 1 if a < b else -1))
    nbr = defaultdict(list)          # face -> [(other face, same_direction?)]
    open_faces = set()
    for e, fl in edges.items():
        if len(fl) == 2:
            (f0, d0), (f1, d1) = fl
            if f0 != f1:
                nbr[f0].append((f1, d0 == d1)); nbr[f1].append((f0, d0 == d1))
        elif len(fl) == 1:
            open_faces.add(fl[0][0])
        # >2 faces on one edge: non-manifold contact, not traversed
    nf = len(me.polygons)
    rel = [None] * nf               # flip relative to the island seed
    flip = [False] * nf
    islands = []
    verts = [v.co for v in me.vertices]
    for seed in range(nf):
        if rel[seed] is not None: continue
        rel[seed] = False; stack = [seed]; isl = []; incons = 0
        while stack:
            f = stack.pop(); isl.append(f)
            for g, same in nbr[f]:
                want = rel[f] ^ same    # same direction on the shared edge -> opposite flip
                if rel[g] is None:
                    rel[g] = want; stack.append(g)
                elif rel[g] != want:
                    incons += 1
        # count inconsistent edges as authored (relative to the faces as they are)
        authored_incons = sum(1 for f in isl for g, same in nbr[f] if same) // 2
        closed = not any(f in open_faces for f in isl)
        # signed volume after making the island consistent (rel applied)
        vol = 0.0; c = verts[me.polygons[isl[0]].vertices[0]]
        for f in isl:
            vs = [verts[i] - c for i in me.polygons[f].vertices]
            t = 0.0
            for i in range(1, len(vs) - 1):
                t += vs[0].dot(vs[i].cross(vs[i + 1]))
            vol += -t if rel[f] else t
        vol /= 6.0
        if closed:
            outward_flip = vol < 0                     # True: invert the whole island
        else:                                          # sheet: keep the side most faces already show
            outward_flip = sum(1 for f in isl if rel[f]) * 2 > len(isl)
        nflip = 0
        for f in isl:
            flip[f] = rel[f] ^ outward_flip
            nflip += flip[f]
        islands.append({'faces': len(isl), 'closed': closed, 'incons': authored_incons,
                        'conflicts': incons, 'volume': abs(vol), 'flips': nflip})
    return flip, islands


def object_report(o):
    """(bad_closed_islands, bad_faces, total_closed_islands, open_islands, open_faces_flipped)"""
    flip, islands = analyse_mesh(o.data)
    bad = [i for i in islands if i['closed'] and abs(i['volume']) > MIN_VOL and i['flips'] > 0]
    sheets = [i for i in islands if not i['closed']]
    return {'bad_islands': len(bad), 'bad_faces': sum(i['flips'] for i in bad),
            'closed': sum(1 for i in islands if i['closed']), 'sheets': len(sheets),
            'sheet_faces_off_majority': sum(i['flips'] for i in sheets), 'faces': len(o.data.polygons)}


def winding_report(objects):
    """Error strings for every mesh object with inside-out faces on a closed island."""
    errs = []
    for o in objects:
        if o.type != 'MESH': continue
        r = object_report(o)
        if r['bad_islands']:
            errs.append('%s: %d inside-out faces on %d of %d closed islands' % (o.name, r['bad_faces'], r['bad_islands'], r['closed']))
    return errs


def fix_object(o):
    """Reverse the faces analyse_mesh marks; keep each face's authored corner normals (negated)."""
    me = o.data
    flip, _ = analyse_mesh(me)
    if not any(flip): return 0
    try: cn = [tuple(c.vector) for c in me.corner_normals]            # Blender 4.1+
    except AttributeError:
        me.calc_normals_split(); cn = [tuple(l.normal) for l in me.loops]
    # per face, vertex -> authored normal
    keep = [{me.loops[li].vertex_index: cn[li] for li in p.loop_indices} for p in me.polygons]
    import bmesh
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.reverse_faces(bm, faces=[bm.faces[i] for i in range(len(flip)) if flip[i]], flip_multires=False)
    bm.to_mesh(me); bm.free(); me.update()
    out = []
    for p in me.polygons:
        k = keep[p.index]; sgn = -1.0 if flip[p.index] else 1.0
        for li in p.loop_indices:
            n = k[me.loops[li].vertex_index]; out.append((n[0] * sgn, n[1] * sgn, n[2] * sgn))
    me.normals_split_custom_set(out)
    return sum(flip)


def colour_mode(path):
    """'LINEAR' when the FBX stores float colours outside 0..1 (a float colour layer exported linear,
    e.g. the kitchen's 1.1 highlights), else 'SRGB' (Blender's default byte colours)."""
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(path)
    for o in root.elems:
        if o.id != b"Objects": continue
        for g in o.elems:
            for L in g.elems:
                if L.id == b"LayerElementColor":
                    for x in L.elems:
                        if x.id == b"Colors" and any(v > 1.0 + 1e-6 or v < -1e-6 for v in x.props[0]): return 'LINEAR'
    return 'SRGB'


def _import(path, colors='SRGB'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, colors_type=colors, anim_offset=0.0)
    return [o for o in bpy.context.scene.objects]


def export_default(path, colors='SRGB'):
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={'EMPTY', 'MESH', 'ARMATURE'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False, use_mesh_modifiers=True,
        mesh_smooth_type='FACE', use_tspace=False, add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        use_custom_props=False, colors_type=colors, path_mode='AUTO', embed_textures=False)


def main(argv):
    if argv and argv[0] == '--fix':
        src, dst = argv[1], argv[2]
        mode = colour_mode(src)
        objs = _import(src, mode); total = 0
        for o in objs:
            if o.type == 'MESH':
                n = fix_object(o)
                if n: print('WINDING FIXED %s: %d faces reversed' % (o.name, n))
                total += n
        acts = list(bpy.data.actions)       # keep a baked take's length (the FBX import leaves frames 1-250)
        if acts:
            sc = bpy.context.scene; fr = [a.frame_range for a in acts]
            sc.frame_start = int(round(min(f[0] for f in fr))); sc.frame_end = int(round(max(f[1] for f in fr))); sc.frame_set(sc.frame_start)
        export_default(dst, mode)
        errs = winding_report(_import(dst))
        print('WINDING FIX %s -> %s: %d faces reversed, %d errors after' % (src, dst, total, len(errs)))
        for e in errs: print('WINDING   ' + e)
        return 1 if errs else 0
    rc = 0
    for path in argv:
        try: objs = _import(path)
        except Exception as ex:
            print('WINDING ERROR %s: import failed: %s' % (path, ex)); rc = max(rc, 2); continue
        errs = winding_report(objs)
        print('WINDING %s %s' % ('FAIL' if errs else 'OK', path))
        for e in errs: print('WINDING   ' + e)
        if errs: rc = max(rc, 1)
    return rc


if __name__ == '__main__' and '--' in sys.argv:
    code = main(sys.argv[sys.argv.index('--') + 1:])
    sys.stdout.flush()
    import os; os._exit(code)
