"""Modular dry-dock slip v1, rebuilt from Astra's drydock-lvl1 parts.

Run:  Blender --background --python tools/blender/drydock_slip_v1.py [-- --render]

Source: art-staging/drydock-astra-lvl1-v1/drydock-lvl1.blend (Astra). Every
part here is one of her closed carpenter components, copied with its own
vertex colours and moved (translate / 90-degree turn) into a snapping piece.
Nothing is scaled. Two operations change a timber's length, never its
section or chamfers:
  * cut to length  - the verts of one end move together (a saw cut);
  * lengthen       - two cut copies butt end to end (a butt / scarf joint),
                     e.g. the 7.4 m+ cross-beams and the 9.4 m gantry beam.
The winch rope is the one soft part: its winch end follows the moved winch.

Pieces (source axes: metres, Z up, +Y toward land, sea at -Y, like hers):
  DryDock_SeaEnd  y 0 .. 1.5   open sea end, Sea_Entry at its seaward edge
  DryDock_Bay     y 0 .. 3.0   repeatable: both walkways + one cradle beam
  DryDock_Head    y 0 .. 4.5   gantry + winch, landing, shelter, timber stock
Each piece's origin is ground level, centred across the slip, at its SEAWARD
edge; its land edge is at y = lengthAlongSlipM. Snap_Sea / Snap_Land markers
carry both edges through the FBX transforms.
"""
import sys, math, json, os
from pathlib import Path
import bpy, bmesh
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
import steamer as ss  # noqa: E402  (render setup shared with the other staged assets)

SRC_REL = 'art-staging/drydock-astra-lvl1-v1/drydock-lvl1.blend'
SRC = Path(os.environ.get('DRYDOCK_SRC', ROOT / SRC_REL))
if not SRC.exists():  # worktrees do not carry the untracked staging folder
    SRC = Path('/Users/kevinandersson/Desktop/SeaSick') / SRC_REL
OUT = ROOT / 'art-staging/drydock-slip-v1'
UNITY = ROOT / 'Assets/_Project/Resources/Buildings/DryDockSlip'
RENDER = '--render' in sys.argv

DX = 1.10          # each walkway moves outward by this much (5.3 -> ~7.5 m clear)
PITCH = 0.5        # her deck-plank pitch
BAY = 3.0          # one bay = 6 planks = one ship middle section
SEA_LEN = 1.5
HEAD_LEN = 4.5
HEAD_SIDE = 3.0    # head: side walkways for 3 m, then the 1.5 m landing
GANTRY_Y = 0.9     # head-local y of the gantry centre line
STACK = 3          # keel-block cribbing height (her block, stacked)

# ---------------------------------------------------------------- source
bpy.ops.wm.open_mainfile(filepath=str(SRC))
MAT = bpy.data.materials['Drydock_Level_1_VertexColor']
MAT.use_fake_user = True


class Part:
    def __init__(self, module, verts, faces, cols):
        self.module, self.verts, self.faces, self.cols = module, verts, faces, cols
        self.mn = Vector([min(v[i] for v in verts) for i in range(3)])
        self.mx = Vector([max(v[i] for v in verts) for i in range(3)])
        self.c = (self.mn + self.mx) / 2
        self.size = self.mx - self.mn
        self.rgb = tuple(round(x, 2) for x in cols[0][0][:3])


PARTS = []
for mod in ['Walkways', 'Ship_Cradle', 'Lifting_Gantry', 'Work_Shelter', 'Timber_Stock']:
    ob = bpy.data.objects[mod]
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.transform(ob.matrix_world)
    lay = bm.loops.layers.color['Col']
    bm.verts.ensure_lookup_table()
    seen = set()
    for v in bm.verts:
        if v.index in seen:
            continue
        comp, stack = [], [v]; seen.add(v.index)
        while stack:
            x = stack.pop(); comp.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen:
                    seen.add(y.index); stack.append(y)
        ids = {x.index: i for i, x in enumerate(comp)}
        faces = {f for x in comp for f in x.link_faces}
        PARTS.append(Part(mod, [x.co.copy() for x in comp],
                          [[ids[l.vert.index] for l in f.loops] for f in faces],
                          [[tuple(l[lay]) for l in f.loops] for f in faces]))
    bm.free()


def find(mod, pred):
    got = [p for p in PARTS if p.module == mod and pred(p)]
    assert got, mod
    return got


def one(mod, pred):
    got = find(mod, pred)
    assert len(got) == 1, (mod, len(got), [(tuple(p.mn), tuple(p.mx)) for p in got])
    return got[0]


near = lambda a, b, t=0.03: abs(a - b) < t
PLANK_L = sorted(find('Walkways', lambda p: near(p.size.y, .48) and p.size.x > 1.6 and p.c.x < 0), key=lambda p: p.c.y)
PLANK_R = sorted(find('Walkways', lambda p: near(p.size.y, .48) and p.size.x > 1.6 and p.c.x > 0), key=lambda p: p.c.y)
PLANK_H = sorted(find('Walkways', lambda p: near(p.size.y, 1.5) and near(p.size.x, .48)), key=lambda p: p.c.x)
STRINGER = one('Walkways', lambda p: p.size.y > 10 and near(p.c.x, -4.13, .05))
STRINGER_IN = one('Walkways', lambda p: p.size.y > 10 and near(p.c.x, -2.97, .05))
PILE = one('Walkways', lambda p: near(p.c.x, -3.55) and near(p.c.y, 0) and p.mn.z < .01)
PILE_CAP = one('Walkways', lambda p: near(p.c.x, -3.55) and near(p.c.y, 0) and p.mn.z > .9)
BOLLARD = one('Walkways', lambda p: p.c.x < 0 and near(p.c.y, -4.7, .05) and near(p.mx.z, 1.57))
BOLLARD_CAP = one('Walkways', lambda p: p.c.x < 0 and near(p.c.y, -4.7, .05) and near(p.mx.z, 1.63))
XBEAM = one('Ship_Cradle', lambda p: p.size.x > 5 and near(p.c.y, 0))
RUNNER = one('Ship_Cradle', lambda p: p.size.y > 10 and p.c.x < 0)
KEEL = one('Ship_Cradle', lambda p: near(p.c.x, 0) and near(p.c.y, 0) and p.rgb[0] > .7)
SHORES = find('Ship_Cradle', lambda p: abs(p.c.x) > 1.4 and abs(p.c.x) < 2.3 and near(p.c.y, 0) and p.size.x < 1 and p.size.y < 1)
SHORE_PADS = [p for p in SHORES if p.rgb[1] > .5]
GANTRY = [p for p in PARTS if p.module == 'Lifting_Gantry']
SHELTER = [p for p in PARTS if p.module == 'Work_Shelter']
TIMBER = [p for p in PARTS if p.module == 'Timber_Stock']
assert len(SHORES) == 4 and len(SHORE_PADS) == 2 and len(PLANK_H) == 12

KEEL_REST_Z = round(KEEL.mx.z + (STACK - 1) * KEEL.size.z, 4)
SHORE_DZ = KEEL_REST_Z - max(p.mx.z for p in SHORE_PADS)   # pads meet the keel line

# ---------------------------------------------------------------- building
def set_col(me):
    ca = me.color_attributes
    ca.active_color = ca['Col']
    ca.render_color_index = ca.find('Col')


# ---------------------------------------------------------------- building
class Piece:
    def __init__(self, name, length):
        self.name, self.length = name, length
        self.bms = {}
        self.markers = []

    def bm(self, mod):
        if mod not in self.bms:
            b = bmesh.new(); b.loops.layers.color.new('Col'); self.bms[mod] = b
        return self.bms[mod]

    def put(self, part, move=(0, 0, 0), turn=0, cut=None, verts_fn=None, mod=None, mirror=False):
        """Copy `part`: optional cut (axis, lo, hi), quarter-turn about its own
        centre, then translate. cut moves each end's verts rigidly."""
        vs = [v.copy() for v in part.verts]
        if cut:
            ax, lo, hi = cut; i = 'xyz'.index(ax); mid = part.c[i]
            for v in vs:
                v[i] += (lo - part.mn[i]) if v[i] < mid else (hi - part.mx[i])
        if verts_fn:
            vs = [verts_fn(v) for v in vs]
        if mirror:   # left/right twin: mirror in x and keep outward winding
            vs = [Vector((-v.x, v.y, v.z)) for v in vs]
        if turn:
            R = Matrix.Rotation(math.radians(90 * turn), 3, 'Z'); c = Vector((part.c.x, part.c.y, 0))
            vs = [R @ (v - c) + c for v in vs]
        vs = [v + Vector(move) for v in vs]
        b = self.bm(mod or part.module); lay = b.loops.layers.color['Col']
        bv = [b.verts.new(v) for v in vs]
        for f, cs in zip(part.faces, part.cols):
            if mirror:
                f, cs = f[::-1], cs[::-1]
            face = b.faces.new([bv[i] for i in f])
            for l, c in zip(face.loops, cs):
                l[lay] = c

    def marker(self, name, p, role):
        self.markers.append((name, p, role))

    def build(self):
        root = bpy.data.objects.new(self.name, None)
        bpy.context.scene.collection.objects.link(root)
        root['lengthAlongSlipM'] = self.length
        objs = [root]
        for mod, b in self.bms.items():
            me = bpy.data.meshes.new(f'{self.name}_{mod}')
            b.to_mesh(me); b.free()
            set_col(me)
            me.materials.append(MAT)
            for p in me.polygons:
                p.use_smooth = False
            ob = bpy.data.objects.new(f'{self.name}_{mod}', me)
            bpy.context.scene.collection.objects.link(ob); ob.parent = root; objs.append(ob)
        for name, p, role in self.markers:
            m = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(m)
            m.parent = root; m.location = p; m['role'] = role; m.empty_display_size = .4; objs.append(m)
        self.objs = objs
        return root


def walkways(pc, y0, y1, seed=0):
    """Both side walkways over [y0, y1): her planks cycled with their own
    end jitter, her two stringers per side cut to length."""
    n = int(round((y1 - y0) / PITCH))
    for k in range(n):
        yc = y0 + (k + .5) * PITCH
        for src, side in ((PLANK_L, -1), (PLANK_R, 1)):
            p = src[(k + seed) % len(src)]
            pc.put(p, (side * DX, yc - p.c.y, 0))
    for s in (-1, 1):
        for st in (STRINGER, STRINGER_IN):
            pc.put(st, (s * DX, 0, 0), cut=('y', y0, y1), mirror=s > 0)


def pile(pc, x, y):
    """Her piling + teal cap plate, at walkway x (sign) / any y."""
    for p in (PILE, PILE_CAP):
        pc.put(p, (x - p.c.x, y - p.c.y, 0))


def bollard(pc, side, y):
    for p in (BOLLARD, BOLLARD_CAP):
        pc.put(p, (side * DX, y - p.c.y, 0), mirror=side > 0)


def runners(pc, y0, y1):
    for s in (-1, 1):
        pc.put(RUNNER, (0, 0, 0), cut=('y', y0, y1), mirror=s > 0)


PILE_X = abs(PILE.c.x) + DX
BEAM_END = PILE_X - PILE.size.x / 2   # cross-beam tenons into the walkway piles


def cradle(pc, y):
    """One keel-support cross-beam: two cut halves of her beam butted under
    the keel stack, three of her keel blocks cribbed, her two angled shores."""
    dy = y - XBEAM.c.y
    pc.put(XBEAM, (0, dy, 0), cut=('x', -BEAM_END, 0))
    pc.put(XBEAM, (0, dy, 0), cut=('x', 0, BEAM_END))
    for i in range(STACK):
        pc.put(KEEL, (0, y - KEEL.c.y, i * KEEL.size.z), turn=i % 2)
    for p in SHORES:
        pc.put(p, (0, y - p.c.y, SHORE_DZ))
    pc.marker('Keel_Rest', (0, y, KEEL_REST_Z), 'keel line: hull bottom rests here (keelRestZ)')


# SeaEnd: open channel, bollards at the seaward corners.
sea = Piece('DryDock_SeaEnd', SEA_LEN)
walkways(sea, 0, SEA_LEN, seed=0)
runners(sea, 0, SEA_LEN)
for s in (-1, 1):
    pile(sea, s * PILE_X, 0.5)
    bollard(sea, s, 0.8)
sea.marker('Sea_Entry', (0, 0, 0), 'open sea end; ships enter moving +Y')
sea.marker('Snap_Sea', (0, 0, 0), 'seaward edge of this piece')
sea.marker('Snap_Land', (0, SEA_LEN, 0), 'landward edge; next piece origin goes here')

# Bay: repeatable.
bay = Piece('DryDock_Bay', BAY)
walkways(bay, 0, BAY, seed=3)
runners(bay, 0, BAY)
for s in (-1, 1):
    pile(bay, s * PILE_X, BAY / 2)
cradle(bay, BAY / 2)
bay.marker('Snap_Sea', (0, 0, 0), 'seaward edge of this piece')
bay.marker('Snap_Land', (0, BAY, 0), 'landward edge; next piece origin goes here')

# Head: side walkways, gantry over the channel, then a full-width landing.
head = Piece('DryDock_Head', HEAD_LEN)
walkways(head, 0, HEAD_SIDE, seed=9)
# side stringers carry on under the landing
for s in (-1, 1):
    for st in (STRINGER, STRINGER_IN):
        head.put(st, (s * DX, 0, 0), cut=('y', HEAD_SIDE, HEAD_LEN), mirror=s > 0)
runners(head, 0, HEAD_SIDE)
nl = int(round((2 * (PILE_X + .9)) / PITCH))           # landing planks, full width
x0 = -(nl - 1) * PITCH / 2
land_y = (HEAD_SIDE + HEAD_LEN) / 2
for i in range(nl):
    p = PLANK_H[i % len(PLANK_H)]
    head.put(p, (x0 + i * PITCH - p.c.x, land_y - p.c.y, 0))
half = x0 - PITCH / 2
for yy in (HEAD_SIDE + .25, HEAD_LEN - .25):              # two cross stringers, butted halves
    for lo, hi in ((half, 0), (0, -half)):
        head.put(STRINGER, (0, 0, 0), turn=0, cut=('y', lo, hi),
                 verts_fn=lambda v, yy=yy: Vector((v.y, yy - (v.x - STRINGER.c.x), v.z)))  # quarter turn
for s in (-1, 1):
    pile(head, s * PILE_X, GANTRY_Y + 1.1)
    pile(head, s * PILE_X, land_y)
    pile(head, s * 1.55, land_y)
    bollard(head, s, HEAD_LEN - .4)
gdy = GANTRY_Y - 2.4
BEAM = max(GANTRY, key=lambda p: p.size.x)
STRIP = max((p for p in GANTRY if p is not BEAM), key=lambda p: p.size.x)
ROPE = one('Lifting_Gantry', lambda p: p.size.x > 2 and p.size.x < 3)
for p in GANTRY:
    if p in (BEAM, STRIP):
        e = p.mx.x + DX
        head.put(p, (0, gdy, 0), cut=('x', -e, 0))
        head.put(p, (0, gdy, 0), cut=('x', 0, e))
    elif p is ROPE:   # the rope's winch end follows the winch
        head.put(p, (0, gdy, 0), verts_fn=lambda v: Vector((v.x + (DX if v.x > 1.5 else 0), v.y, v.z)))
    else:
        sx = DX if p.c.x > 1.5 else (-DX if p.c.x < -1.5 else 0)
        head.put(p, (sx, gdy, 0))
for p in SHELTER:
    head.put(p, (-DX, 1.2 - (-2.17), 0))
for p in TIMBER:
    head.put(p, (DX, 1.3 - (-3.15), 0))   # clear of the landing pile cap
head.marker('Interaction_Point', (0, land_y, .99), 'shipyard interaction, on the landing')
head.marker('Snap_Sea', (0, 0, 0), 'seaward edge of this piece')
head.marker('Snap_Land', (0, HEAD_LEN, 0), 'land edge of the slip')

PIECES = [sea, bay, head]

# ---------------------------------------------------------------- scene + export
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for p in PIECES:
    p.build()

OUT.mkdir(parents=True, exist_ok=True)
UNITY.mkdir(parents=True, exist_ok=True)
for p in PIECES:
    bpy.ops.object.select_all(action='DESELECT')
    for ob in p.objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = p.objs[0]
    for d in (OUT, UNITY):
        bpy.ops.export_scene.fbx(filepath=str(d / f'{p.name}.fbx'), use_selection=True,
                                 object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                                 bake_anim=False, use_triangles=True, colors_type='LINEAR',
                                 mesh_smooth_type='FACE', use_custom_props=True)


def clear_width():
    lo, hi = -1e9, 1e9   # narrowest over every piece (head: side walkways only)
    for ob in [o for pc in PIECES for o in pc.objs]:
        if ob.type != 'MESH' or not ob.name.endswith('Walkways'):
            continue
        for v in ob.data.vertices:
            if .8 < v.co.z < .995 and abs(v.co.x) > 3 and not (ob.name.startswith('DryDock_Head') and v.co.y > HEAD_SIDE - 1e-3):
                if v.co.x < 0: lo = max(lo, v.co.x)
                else: hi = min(hi, v.co.x)
    return round(hi - lo, 3)


manifest = {
    'source': SRC_REL + ' (Astra, drydock level 1); parts moved/turned, timbers cut or butt-joined, never scaled',
    'units': 'metres',
    'sourceAxes': 'Blender Z up; FBX exported Y up (axis_forward -Z, axis_up Y) like the source',
    'slipAxis': '+Y toward land (source/Blender axes; sea end at -Y, same as the source). Read direction through the Snap_Sea -> Snap_Land markers after import rather than assuming the FBX axis mapping.',
    'assemblyOrder': 'DryDock_SeaEnd, DryDock_Bay x N, DryDock_Head; each next piece origin = previous Snap_Land',
    'pieces': {
        p.name: {
            'file': f'{p.name}.fbx',
            'lengthAlongSlipM': p.length,
            'originNote': 'ground level (z 0), centred across the slip (x 0), at the SEAWARD edge; piece spans y 0..lengthAlongSlipM',
        } for p in PIECES},
    'channelClearWidthM': clear_width(),
    'walkwayHeightM': 0.99,
    'keelRestZM': KEEL_REST_Z,
    'bayLengthM': BAY,
    'recommendedBays': 'max(2, ceil((shipLengthM - 1.5) / 3.0))  -- the stern may overhang the open 1.5 m sea end; the bow must stay off the head (gantry)',
    'recommendedBaysExamples': {str(L): max(2, math.ceil((L - SEA_LEN) / BAY)) for L in (10.1, 13.14, 16.14, 19.14)},
    'shipPlacement': 'hull centreline x 0, keel bottom at keelRestZM, bow at y = 1.5 + 3.0*N - 0.25 (slip-root space), stern toward the sea',
    'markers': {p.name: {n: [round(c, 3) for c in pos] for n, pos, _ in p.markers} for p in PIECES},
    'markerNotes': 'Keel_Rest is at each bay cradle centre (y 1.5); Interaction_Point on the head landing; Sea_Entry at the SeaEnd seaward edge',
    'triangles': {},
}
for p in PIECES:
    manifest['triangles'][p.name] = sum(len(pl.vertices) - 2 for ob in p.objs if ob.type == 'MESH' for pl in ob.data.polygons)
(OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2))
(UNITY / 'manifest.json').write_text(json.dumps(manifest, indent=2))
print('MANIFEST', json.dumps({k: manifest[k] for k in ('channelClearWidthM', 'keelRestZM', 'triangles')}))

# ---------------------------------------------------------------- renders
if RENDER:
    scene = bpy.context.scene
    ss.setup_render(scene); scene.view_settings.exposure = .5
    scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
    cam = bpy.data.objects.new('Review', bpy.data.cameras.new('Review'))
    scene.collection.objects.link(cam); scene.camera = cam; cam.data.type = 'ORTHO'
    (OUT / 'renders').mkdir(exist_ok=True)
    hull_me = None

    def hull(L, y_bow):
        me = bpy.data.meshes.new('Hull'); b = bmesh.new(); lay = b.loops.layers.color.new('Col')
        bmesh.ops.create_cube(b, size=1)
        for v in b.verts:
            v.co = Vector((v.co.x * 6.04, y_bow - L / 2 + v.co.y * L, KEEL_REST_Z + (v.co.z + .5) * 2.0))
        for f in b.faces:
            for l in f.loops: l[lay] = (.80, .82, .86, 1)
        b.to_mesh(me); b.free(); set_col(me); me.materials.append(MAT)
        ob = bpy.data.objects.new('Hull', me); scene.collection.objects.link(ob); return ob

    def assemble(n):
        made, y = [], 0.0
        for pc in [sea] + [bay] * n + [head]:
            for ob in pc.objs:
                if ob.type != 'MESH': continue
                c = bpy.data.objects.new(ob.name + '_i', ob.data); scene.collection.objects.link(c)
                c.location = (0, y, 0); made.append(c)
            y += pc.length
        return made, y

    for pc in PIECES:
        for ob in pc.objs: ob.hide_render = True
    for n, L in ((2, 13.14), (4, 13.14), (6, 19.14)):
        made, total = assemble(n)
        bow = SEA_LEN + BAY * n - .25
        made.append(hull(L, bow))
        y_lo = min(0.0, bow - L)
        mid = Vector((0, (y_lo + total) / 2, 1.5)); span = max(total - y_lo, 12)
        for view, eye, scale in (('34', Vector((span * .9, -span * .55, span * .8)), span * 1.05),
                                 ('top', Vector((0, 0, 60)), span * 1.1),
                                 ('sea', Vector((0, -40, 1.6)), 14)):
            if view == 'sea' and n != 4: continue
            cam.location = mid + eye if view != 'sea' else Vector((0, -40, 2.2))
            tgt = mid if view != 'sea' else Vector((0, 0, 2.2))
            cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
            if view == 'top': cam.rotation_euler = (0, 0, math.pi / 2)   # slip runs left(sea) -> right(land)
            cam.data.ortho_scale = scale
            scene.render.filepath = str(OUT / 'renders' / f'slip_{n}bays_{view}.png')
            bpy.ops.render.render(write_still=True)
        for ob in made: bpy.data.objects.remove(ob, do_unlink=True)
print('DONE')
