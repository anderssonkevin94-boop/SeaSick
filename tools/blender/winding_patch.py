"""Reverse inside-out faces IN PLACE in a binary FBX: everything but the flipped polygons stays byte for byte.

    Blender -b --factory-startup --python tools/blender/winding_patch.py -- <in.fbx> <out.fbx>

Why: `check_winding.py --fix` re-exports through Blender, and that round trip is not faithful for every
file (Astra's 100x-scale kits come back with the scale moved onto the root, `Lcl Scaling 1 -> 100`). This
tool only decides WHICH faces to flip in Blender (`check_winding.analyse_mesh`, the same rule as --fix:
every island consistent and outward) and then rewrites the original file's bytes itself:

* `PolygonVertexIndex`: each flipped polygon's corners reversed (the end marker `~i` moved to the new last).
* every `ByPolygonVertex` layer (normals, UVs, colours; `Direct` rows or `IndexToDirect` indices) reordered
  the same way; the flipped corners' normals negated (shared `IndexToDirect` normals get a negated copy).
* `Edges` re-pointed at the same undirected edge in the reversed polygon.
* node end offsets recomputed, the footer's 16-byte alignment pad recomputed; every other byte copied.

Refuses (exit 2) on what it cannot do faithfully: an ASCII FBX, per-vertex normals on a mesh that needs a
flip, tangent/binormal layers, or a Blender mesh whose polygon count differs from the FBX geometry.
Verify every result with `fbx_diff.py` (DIFF_BAD 0) and `check_winding.py` (OK).
"""
import os
import struct
import sys
import zlib

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_winding as cw  # noqa: E402

MAGIC = b"Kaydara FBX Binary  \x00\x1a\x00"
ARRAY = {b"f": ("f", 4), b"d": ("d", 8), b"l": ("q", 8), b"i": ("i", 4), b"b": ("b", 1)}
SCALAR = {b"Y": 2, b"C": 1, b"I": 4, b"F": 4, b"D": 8, b"L": 8}


class Node:
    __slots__ = ("name", "props", "children", "sentinel")

    def __init__(self, name):
        self.name, self.props, self.children, self.sentinel = name, [], [], False


class Prop:
    """One property: its raw bytes (type code included) unless replaced by `values`."""
    __slots__ = ("code", "raw", "values", "encoding")

    def __init__(self, code, raw):
        self.code, self.raw, self.values, self.encoding = code, raw, None, 0

    def array(self):
        if self.values is not None: return self.values
        fmt, size = ARRAY[self.code]
        n, enc, clen = struct.unpack_from("<III", self.raw, 1)
        data = self.raw[13:13 + clen]
        if enc: data = zlib.decompress(data)
        self.encoding = enc
        return list(struct.unpack("<%d%s" % (n, fmt), data))

    def string(self):
        n = struct.unpack_from("<I", self.raw, 1)[0]
        return self.raw[5:5 + n]

    def scalar(self):
        return struct.unpack_from("<" + {b"I": "i", b"L": "q", b"D": "d", b"F": "f", b"Y": "h", b"C": "B"}[self.code], self.raw, 1)[0]

    def set_array(self, values):
        self.array()                        # learn the encoding first
        self.values = values

    def encode(self):
        if self.values is None: return self.raw
        fmt, size = ARRAY[self.code]
        data = struct.pack("<%d%s" % (len(self.values), fmt), *self.values)
        if self.encoding: data = zlib.compress(data, 9)
        return self.code + struct.pack("<III", len(self.values), self.encoding, len(data)) + data


def read_prop(buf, at):
    code = buf[at:at + 1]
    if code in SCALAR: end = at + 1 + SCALAR[code]
    elif code in ARRAY: end = at + 13 + struct.unpack_from("<I", buf, at + 9)[0]
    elif code in (b"S", b"R"): end = at + 5 + struct.unpack_from("<I", buf, at + 1)[0]
    else: raise ValueError("unknown FBX property type %r at %d" % (code, at))
    return Prop(code, buf[at:end]), end


def read_node(buf, at, wide):
    hdr = "<QQQB" if wide else "<IIIB"
    end, nprops, plen, nlen = struct.unpack_from(hdr, buf, at)
    at += struct.calcsize(hdr)
    if end == 0: return None, at                         # the null record
    node = Node(buf[at:at + nlen]); at += nlen
    p_end = at + plen
    for _ in range(nprops):
        p, at = read_prop(buf, at); node.props.append(p)
    if at != p_end: raise ValueError("property list length mismatch in %r" % node.name)
    if at < end:
        null = 25 if wide else 13
        while at < end - null:
            child, at = read_node(buf, at, wide); node.children.append(child)
        if buf[at:end] != b"\0" * null: raise ValueError("bad nested-list sentinel in %r" % node.name)
        node.sentinel = True
        at = end
    return node, at


def write_node(out, node, wide):
    hdr = "<QQQB" if wide else "<IIIB"
    start = len(out)
    out += b"\0" * struct.calcsize(hdr)
    out += node.name
    props = b"".join(p.encode() for p in node.props)
    out += props
    for c in node.children: write_node(out, c, wide)
    if node.sentinel: out += b"\0" * (25 if wide else 13)
    struct.pack_into(hdr, out, start, len(out), len(node.props), len(props), len(node.name))


def load(path):
    buf = open(path, "rb").read()
    if not buf.startswith(MAGIC): raise SystemExit("WINDING PATCH refused: %s is not a binary FBX" % path)
    version = struct.unpack_from("<I", buf, 23)[0]
    wide = version >= 7500
    at, top = 27, []
    while True:
        node, at = read_node(buf, at, wide)
        if node is None: break
        top.append(node)
    return buf, version, wide, top, buf[at:]


def save(path, version, wide, top, footer):
    out = bytearray(MAGIC + struct.pack("<I", version))
    for n in top: write_node(out, n, wide)
    out += b"\0" * (25 if wide else 13)
    # Footer: 16-byte id, zero pad to a 16-byte boundary (16 when already aligned), version, zeros, magic.
    fid, rest = footer[:16], footer[16:]
    k = 0
    while k < len(rest) and rest[k] == 0: k += 1
    tail = rest[k:]
    out += fid
    pad = ((len(out) + 15) & ~15) - len(out)
    out += b"\0" * (pad or 16) + tail
    open(path, "wb").write(bytes(out))


def child(node, name):
    for c in node.children:
        if c.name == name: return c
    return None


def flips_by_geometry(path, top):
    """{geometry id: [polygon indices to reverse]}, decided in Blender on the imported meshes."""
    objs = cw._import(path, cw.colour_mode(path))
    objects = child_of_top(top, b"Objects"); conns = child_of_top(top, b"Connections")
    model_by_name, geom_ids = {}, set()
    for n in objects.children:
        if n.name == b"Model": model_by_name[n.props[1].string().split(b"\0")[0].decode()] = n.props[0].scalar()
        elif n.name == b"Geometry": geom_ids.add(n.props[0].scalar())
    geom_of_model = {}
    for c in conns.children:
        if c.props[0].string() == b"OO":
            a, b = c.props[1].scalar(), c.props[2].scalar()
            if a in geom_ids: geom_of_model[b] = a
    out = {}
    for o in objs:
        if o.type != "MESH": continue
        flip, _ = cw.analyse_mesh(o.data)
        idx = [i for i, f in enumerate(flip) if f]
        if not idx: continue
        mid = model_by_name.get(o.name)
        if mid is None or mid not in geom_of_model:
            raise SystemExit("WINDING PATCH refused: no FBX geometry for Blender object %r" % o.name)
        gid = geom_of_model[mid]
        if gid in out and out[gid][1] != idx:
            raise SystemExit("WINDING PATCH refused: shared geometry flips differently (%r)" % o.name)
        out[gid] = (o.name, idx, len(o.data.polygons))
    return out


def child_of_top(top, name):
    for n in top:
        if n.name == name: return n
    raise SystemExit("WINDING PATCH refused: no %r section" % name)


def patch_geometry(g, flip, nblender, label):
    pvi_p = child(g, b"PolygonVertexIndex").props[0]
    pvi = pvi_p.array()
    polys, s = [], 0
    for k, v in enumerate(pvi):
        if v < 0: polys.append((s, k + 1)); s = k + 1
    if len(polys) != nblender:
        raise SystemExit("WINDING PATCH refused: %s has %d FBX polygons, Blender made %d" % (label, len(polys), nblender))
    flipset = set(flip)
    perm = list(range(len(pvi)))            # new corner slot -> old corner slot
    edge_slot = list(range(len(pvi)))       # old corner slot -> new slot of the same undirected edge
    new = list(pvi)
    for p in flip:
        a, b = polys[p]; n = b - a
        verts = [pvi[k] if pvi[k] >= 0 else ~pvi[k] for k in range(a, b)]
        rev = verts[::-1]
        for j in range(n):
            new[a + j] = rev[j] if j < n - 1 else ~rev[j]
            perm[a + j] = b - 1 - j
            edge_slot[a + j] = a + ((n - 2 - j) % n)
    pvi_p.set_array(new)
    flipped_slot = [False] * len(pvi)
    for p in flip:
        a, b = polys[p]
        for k in range(a, b): flipped_slot[k] = True

    e = child(g, b"Edges")
    if e is not None and e.props:
        e.props[0].set_array([edge_slot[x] for x in e.props[0].array()])

    for L in g.children:
        if not L.name.startswith(b"LayerElement"): continue
        mapping = child(L, b"MappingInformationType")
        if mapping is None: continue
        mapping = mapping.props[0].string()
        if L.name in (b"LayerElementTangent", b"LayerElementBinormal"):
            raise SystemExit("WINDING PATCH refused: %s has %s" % (label, L.name.decode()))
        if mapping != b"ByPolygonVertex":
            if L.name == b"LayerElementNormal":
                raise SystemExit("WINDING PATCH refused: %s has %s normals" % (label, mapping.decode()))
            continue                         # ByPolygon / AllSame / ByVertice layers keep their order
        ref = child(L, b"ReferenceInformationType").props[0].string()
        data_name = {b"LayerElementNormal": b"Normals", b"LayerElementUV": b"UV", b"LayerElementColor": b"Colors"}.get(L.name)
        if data_name is None: continue
        width = {b"Normals": 3, b"UV": 2, b"Colors": 4}[data_name]
        data_p = child(L, data_name).props[0]
        w_node = child(L, b"NormalsW") if data_name == b"Normals" else None
        is_normal = data_name == b"Normals"
        if ref == b"Direct":
            rows = data_p.array()
            out = []
            for k in range(len(pvi)):
                r = rows[perm[k] * width:(perm[k] + 1) * width]
                if is_normal and flipped_slot[k]: r = [-x for x in r]
                out.extend(r)
            data_p.set_array(out)
            if w_node is not None:
                w = w_node.props[0].array(); w_node.props[0].set_array([w[perm[k]] for k in range(len(pvi))])
        else:                                # IndexToDirect
            idx_name = {b"Normals": b"NormalsIndex", b"UV": b"UVIndex", b"Colors": b"ColorIndex"}[data_name]
            idx_p = child(L, idx_name).props[0]
            idx = idx_p.array()
            new_idx = [idx[perm[k]] for k in range(len(pvi))]
            if is_normal:
                rows = data_p.array(); w = w_node.props[0].array() if w_node is not None else None
                rows = list(rows)
                for k in range(len(pvi)):
                    if not flipped_slot[k]: continue
                    src = new_idx[k]
                    rows.extend(-x for x in rows[src * width:(src + 1) * width])
                    if w is not None: w.append(w[src])
                    new_idx[k] = len(rows) // width - 1
                data_p.set_array(rows)
                if w is not None: w_node.props[0].set_array(w)
            idx_p.set_array(new_idx)
    return len(flipset)


def main(argv):
    src, dst = argv[0], argv[1]
    buf, version, wide, top, footer = load(src)
    flips = flips_by_geometry(src, top)
    objects = child_of_top(top, b"Objects")
    total = 0
    for g in objects.children:
        if g.name != b"Geometry": continue
        gid = g.props[0].scalar()
        if gid not in flips: continue
        name, idx, nb = flips[gid]
        n = patch_geometry(g, idx, nb, name)
        print("WINDING PATCHED %s: %d faces reversed" % (name, n))
        total += n
    save(dst, version, wide, top, footer)
    print("WINDING PATCH %s -> %s: %d faces reversed" % (src, dst, total))
    return 0


if __name__ == "__main__" and "--" in sys.argv:
    try:
        code = main(sys.argv[sys.argv.index("--") + 1:])
    except SystemExit as e:
        print(e); code = 2
    sys.stdout.flush()
    os._exit(code)
