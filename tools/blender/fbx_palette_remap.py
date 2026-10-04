"""Remap vertex-colour VALUES in a binary FBX, in place: every other byte stays as it was.

    Blender -b --factory-startup --python tools/blender/fbx_palette_remap.py -- <in.fbx> <out.fbx> \
        r,g,b=r,g,b [r,g,b=r,g,b ...] [--tol 0.004] [--list]

Each `from=to` pair (0..1 floats, the values as stored in the file) replaces every colour in every
`LayerElementColor/Colors` array within `--tol` of `from` (alpha kept). The `ColorIndex` arrays, the
geometry, the winding and everything else are copied byte for byte (the node writer is
`winding_patch.py`'s). `--list` prints each geometry's distinct colours and their corner counts and
writes nothing. Our kits store a small palette (`IndexToDirect`), so a remap is a palette edit.

Used for the level 2 wall's stone (Kevin 2026-10-04: "match the stone color of the wall to the stone
you see on the beach"). Verify with `fbx_diff.py`: only `colour_mismatch` on the remapped corners.
"""
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import winding_patch as wp  # noqa: E402


def parse_args(argv):
    src, dst, pairs, tol, listing = None, None, [], 0.004, False
    rest = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--tol": tol = float(argv[i + 1]); i += 2; continue
        if a == "--list": listing = True; i += 1; continue
        rest.append(a); i += 1
    src = rest[0]
    if not listing: dst = rest[1]
    for p in rest[1 if listing else 2:]:
        f, t = p.split("=")
        pairs.append((tuple(float(x) for x in f.split(",")), tuple(float(x) for x in t.split(","))))
    return src, dst, pairs, tol, listing


def main(argv):
    src, dst, pairs, tol, listing = parse_args(argv)
    buf, version, wide, top, footer = wp.load(src)
    objects = wp.child_of_top(top, b"Objects")
    hits = Counter()
    for g in objects.children:
        if g.name != b"Geometry": continue
        name = g.props[1].string().split(b"\0")[0].decode()
        for L in g.children:
            if L.name != b"LayerElementColor": continue
            cp = wp.child(L, b"Colors").props[0]
            vals = cp.array()
            n = len(vals) // 4
            if listing:
                idx_node = wp.child(L, b"ColorIndex")
                use = Counter(idx_node.props[0].array()) if idx_node is not None else Counter(range(n))
                print("PALETTE %s: %s" % (name, ", ".join(
                    "(%.3f,%.3f,%.3f)x%d" % (vals[i * 4], vals[i * 4 + 1], vals[i * 4 + 2], use[i]) for i in range(n))))
                continue
            out = list(vals)
            changed = False
            for i in range(n):
                c = vals[i * 4:i * 4 + 3]
                for f, t in pairs:
                    if max(abs(c[k] - f[k]) for k in range(3)) <= tol:
                        out[i * 4:i * 4 + 3] = list(t)
                        hits[(f, t)] += 1
                        changed = True
                        break
            if changed: cp.set_array(out)
    if listing: return 0
    wp.save(dst, version, wide, top, footer)
    for (f, t), k in hits.items():
        print("REMAP (%.3f,%.3f,%.3f) -> (%.3f,%.3f,%.3f): %d palette entries" % (f + t + (k,)))
    missing = [f for f, t in pairs if not any(ff == f for ff, _ in hits)]
    for f in missing: print("REMAP (%.3f,%.3f,%.3f): NOT FOUND" % f)
    print("REMAP %s -> %s" % (src, dst))
    return 1 if missing else 0


if __name__ == "__main__" and "--" in sys.argv:
    try:
        code = main(sys.argv[sys.argv.index("--") + 1:])
    except SystemExit as e:
        print(e); code = 2
    sys.stdout.flush()
    os._exit(code)
