# Harpoon v1 build (phase 0 of docs/PLAN-harpoon.md). Run (see RUN.md):
#   Blender -b art-staging/lumber-mill-triangular-lvl1-v2/lumber-mill.blend --python art-staging/harpoon-v1/build.py
# The mill blend is opened READ-ONLY (never saved) only to copy its approved material node setups + packed rope tile.
# Writes ONLY into art-staging/harpoon-v1/: HarpoonMount.fbx, HarpoonBarb.fbx, BowLantern.fbx, harpoon-v1.blend.
# `-- lantern` (Kevin's bow-lantern change, 2026-10-04): builds everything into the blend as usual but exports ONLY
# BowLantern.fbx, so the imported mount/barb FBXs stay byte-identical.
# Contract: CONTRACT.md.  Blender metres, Z up, forward = -Y (Unity +Z).
import bpy, math, sys, json
from mathutils import Vector, Matrix
exec(compile(open('/Users/kevinandersson/Desktop/SeaSick/art-staging/harpoon-v1/geo.py').read(), 'geo.py', 'exec'))
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
LANTERN_ONLY = 'lantern' in ARGS

scene = bpy.data.scenes.new('SeaSick • Harpoon v1'); bpy.context.window.scene = scene
def cp(src, new):
    m = bpy.data.materials[src].copy(); m.name = new; return m
timber = cp('SS_LumberL1_Canvas_Endgrain_Stone.003', 'SS_Harpoon_Timber')     # GameColor only (the hull is untextured painted facets)
rope = cp('SS_LumberL1_Hemp.003', 'SS_Harpoon_Rope')                          # rope tile x GameColor (same tile as Art/WallL1/Textures/rope-tile-512.png)
iron = cp('SS_LumberL1_Canvas_Endgrain_Stone.003', 'SS_Harpoon_Iron')          # GameColor only, a touch of sheen
for n in iron.node_tree.nodes:
    if n.type == 'BSDF_PRINCIPLED':
        n.inputs['Roughness'].default_value = 0.45; n.inputs['Metallic'].default_value = 0.35
MATS = [timber, rope, iron]
for m in MATS:
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and not n.image.packed_file: n.image.pack()

def coll(name):
    c = bpy.data.collections.new(name); scene.collection.children.link(c); return c
def empty(name, loc, parent, c, size=.12):
    o = bpy.data.objects.new(name, None); c.objects.link(o); o.empty_display_size = size
    o.parent = parent; o.location = Vector(loc) - (parent.matrix_world.translation if parent else Vector()); o.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update(); return o

# ------------------------------------------------------------------ HarpoonMount (origin = deck surface on the swivel axis)
CM = coll('HarpoonMount • game')
root = bpy.data.objects.new('HarpoonMount', None); CM.objects.link(root); root.empty_display_size = .3
PLINTH = 0.16           # static timber plinth top = turntable axis origin
LIFT = 0.36             # v1b: everything above the post raised so the line clears the rising nose cap (see CONTRACT)
BARREL_Z = 1.36 + LIFT  # barrel axis 1.72 m above the deck
MUZZLE_Y = -0.80        # muzzle face
DRUM = Vector((0, 0.42, 0.70 + LIFT)); DRUM_R = 0.15; ROPE_R = 0.045
STAND = Vector((0, 0.86, 0.30))   # raised harpooner step, 0.30 m

# static base: plinth, 4 bolts, curved harpooner step (arc plate behind the gun, follows the stand at any yaw)
b = MB()
b.prism((0, 0, 0), (0, 0, PLINTH), 0.42, 0.40, n=8, slot='timber', color=TIMBER_DK, rot=math.pi / 8)
for k in range(4):
    a = math.pi / 4 + k * math.pi / 2; p = Vector((math.cos(a) * .355, math.sin(a) * .355, PLINTH))
    b.prism(p, p + Vector((0, 0, .045)), .036, .03, n=6, slot='iron', color=IRON)
b.sector(0.60, 1.06, math.radians(90 - 58), math.radians(90 + 58), 0.0, STAND.z, n=8, slot='timber', color=DECK)
base = b.finish('Mount_Base', MATS, CM, parent=root)

swivel = empty('Swivel', (0, 0, PLINTH), root, CM, .25)
Z = LIFT
s = MB()
s.prism((0, 0, PLINTH), (0, 0, PLINTH + .08), .31, .31, n=16, slot='iron', color=IRON)              # turntable ring
s.prism((0, 0, PLINTH + .08), (0, 0, .98 + Z), .25, .21, n=8, slot='timber', color=TIMBER, rot=math.pi / 8)   # chunky post
s.prism((0, 0, .98 + Z), (0, 0, 1.04 + Z), .28, .28, n=8, slot='iron', color=IRON, rot=math.pi / 8)    # yoke plate
for x in (-.215, .215):
    s.box((x, 0, 1.25 + Z), (.09, .44, .42), slot='timber', color=TIMBER_DK)                            # yoke cheeks
s.prism((-.30, 0, BARREL_Z - .03), (.30, 0, BARREL_Z - .03), .055, n=8, slot='iron', color=IRON)       # trunnion pin
# stubby FAT barrel: breech knob -> breech -> barrel -> brass-banded muzzle swell
s.prism((0, .46, BARREL_Z), (0, .33, BARREL_Z), .07, .11, n=8, slot='iron', color=IRON)
s.prism((0, .33, BARREL_Z), (0, .04, BARREL_Z), .16, .16, n=12, slot='iron', color=IRON)
s.prism((0, .04, BARREL_Z), (0, -.60, BARREL_Z), .15, .14, n=12, slot='iron', color=IRON)
s.prism((0, -.60, BARREL_Z), (0, MUZZLE_Y, BARREL_Z), .18, .175, n=12, slot='iron', color=BRASS)
s.prism((0, MUZZLE_Y + .001, BARREL_Z), (0, MUZZLE_Y - .002, BARREL_Z), .09, .09, n=12, slot='iron', color=(0.01, 0.01, 0.01, 1), caps=True)  # dark bore disc
# chunky stock + T grip (the harpooner's hands)
s.frustum_y(.30, .74, .20, .16, .15, .13, 1.27 + Z, 1.18 + Z, slot='timber', color=TIMBER)
s.prism((-.25, .76, 1.18 + Z), (.25, .76, 1.18 + Z), .048, n=8, slot='timber', color=TIMBER_DK)
# winch drum supports + axle (static on the swivel)
for x in (-.33, .33):
    s.box((x, .33, .58 + Z), (.08, .40, .44), slot='timber', color=TIMBER_DK)                           # drum cheeks
s.box((0, .25, .40 + Z), (.74, .10, .08), slot='timber', color=TIMBER_DK)                               # crossbar into the post
s.prism((-.40, DRUM.y, DRUM.z), (.40, DRUM.y, DRUM.z), .04, n=8, slot='iron', color=IRON)
# rope lead: drum top -> between the cheeks under the barrel -> fairlead eye under the muzzle swell
top = DRUM + Vector((0, -.04, DRUM_R + ROPE_R - .005)); FZ = BARREL_Z - .18 - .075
s.tube([top, (0, .27, 1.00 + Z), (0, .16, 1.10 + Z), (0, -.25, FZ), (0, -.68, FZ)], .03, n=6)
s.torus((0, -.68, FZ), (0, 1, 0), .05, .018, nR=8, nr=4, slot='iron', color=IRON)                     # fairlead eye
s.box((0, -.68, BARREL_Z - .18 + .005), (.05, .05, .07), slot='iron', color=IRON)                       # eye strap to the swell
body = s.finish('Swivel_Body', MATS, CM, parent=swivel, origin=(0, 0, PLINTH))

muzzle = empty('Barb_Muzzle', (0, MUZZLE_Y, BARREL_Z), swivel, CM)
drum = empty('Winch_Drum', DRUM, swivel, CM)
w = MB()
w.prism((-.235, 0, 0), (.235, 0, 0), .11, n=12, slot='timber', color=TIMBER_DK)                       # drum core
for x in (-.26, .26):
    w.prism((x - .025, 0, 0), (x + .025, 0, 0), .245, n=16, slot='timber', color=TIMBER)               # flanges
for k, x in enumerate((-.18, -.09, 0, .09, .18)):
    w.torus((x, 0, 0), (1, 0, 0), DRUM_R, ROPE_R, nR=12, nr=6, twist=k * .5)                           # fat coils
w.prism((.40, 0, 0), (.44, 0, 0), .05, n=8, slot='iron', color=IRON)                                    # crank boss
w.box((.42, 0, -.09), (.04, .06, .22), slot='iron', color=IRON)                                         # crank arm
w.prism((.44, 0, -.18), (.58, 0, -.18), .035, n=6, slot='timber', color=TIMBER)                        # crank handle
w.v = [Vector(p) + DRUM for p in w.v]   # built drum-local; finish() wants mount coords (else the coil lands at the deck origin)
coil = w.finish('Winch_Coil', MATS, CM, parent=drum, origin=DRUM)
stand = empty('Harpooner_Stand', STAND, swivel, CM, .2)

# ------------------------------------------------------------------ HarpoonBarb (origin = shaft tail = sits at Barb_Muzzle when loaded; tip -Y)
CB = coll('HarpoonBarb • game')
broot = bpy.data.objects.new('HarpoonBarb', None); CB.objects.link(broot); broot.empty_display_size = .2
BARB_TUCK = 0.25
g = MB()
g.prism((0, 0.0, 0), (0, -.40, 0), .045, .045, n=8, slot='timber', color=TIMBER)                      # short timber shaft
g.prism((0, -.36, 0), (0, -.43, 0), .065, .065, n=8, slot='iron', color=BRASS)                          # brass collar
# fat arrowhead: flat diamond bipyramid, broad (x) and thick (z)
H0, H1, HW, HT = -.43, -.80, .13, .055
g.add([(0, H0, 0), (HW, H0 - .06, 0), (0, H0 - .06, HT), (-HW, H0 - .06, 0), (0, H0 - .06, -HT), (0, H1, 0)],
      [(0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 1), (5, 2, 1), (5, 3, 2), (5, 4, 3), (5, 1, 4)], 'iron', IRON)
# two flukes (toggle barbs) swept back from the head shoulders
for sx in (1, -1):
    t = .032
    pts = [(sx * .06, -.50, 0), (sx * .13, -.47, 0), (sx * .19, -.30, 0)]
    v = [(x, y, z - t) for x, y, z in pts] + [(x, y, z + t) for x, y, z in pts]
    f = [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]
    if sx < 0: f = [tuple(reversed(x)) for x in f]
    g.add(v, f, 'iron', IRON)
g.torus((0, .085, 0), (1, 0, 0), .05, .02, nR=10, nr=5, slot='iron', color=IRON)                       # rope eye (ring in the YZ plane)
g.box((0, .03, 0), (.03, .04, .06), slot='iron', color=IRON)                                            # eye shank into the shaft
bar = g.finish('Barb_Mesh', MATS, CB, parent=broot)
bar.data.transform(Matrix.Translation((0, BARB_TUCK, 0)))   # tail 0.25 m behind the origin: loaded at Barb_Muzzle, the shaft sits in the bore
line = empty('Line_Attach', (0, .085 + BARB_TUCK, 0), broot, CB, .06)   # centre of the rope eye

# ------------------------------------------------------------------ BowLantern (root = the mount's origin; geo.py has the numbers)
CL = coll('BowLantern • game')
lroot = bpy.data.objects.new('BowLantern', None); CL.objects.link(lroot); lroot.empty_display_size = .3
# static: the beam (flat top, slight taper), an iron strap where it leaves the stem, an iron band + eye at the tip
bm_ = MB()
r0, r1 = L(0, 0, BEAM_Z0), L(0, 0, BEAM_Z1)
bm_.frustum_y(r0.y, r1.y, BEAM_W0, BEAM_H0, BEAM_W1, BEAM_H1, beam_mid_y(BEAM_Z0) - MOUNT_M[1], beam_mid_y(BEAM_Z1) - MOUNT_M[1],
              slot='timber', color=TIMBER)
bm_.box(L(0, beam_mid_y(COLLAR_Z), COLLAR_Z), (beam_w(COLLAR_Z) + .04, .10, beam_h(COLLAR_Z) + .04), slot='iron', color=IRON)        # root strap
bm_.box(L(0, beam_mid_y(PIVOT_U[2]), PIVOT_U[2]), (beam_w(PIVOT_U[2]) + .03, .12, beam_h(PIVOT_U[2]) + .03), slot='iron', color=IRON)  # tip band
bm_.box(L(0, PIVOT_U[1] + .015, PIVOT_U[2]), (.04, .06, .05), slot='iron', color=IRON)                     # eye under the band
beam = bm_.finish('BowLantern_Beam', MATS, CL, parent=lroot)
# swinging: everything under the pivot (LanternSwing turns it)
pivot = empty('LanternBow_Pivot', L(*PIVOT_U), lroot, CL, .1)
PV = L(*PIVOT_U)
ch = MB()
ch.torus(PV + Vector((0, 0, -.035)), (1, 0, 0), .035, .012, nR=6, nr=3, slot='iron', color=IRON)        # link 1 (in the eye)
ch.torus(PV + Vector((0, 0, -.085)), (0, 1, 0), .035, .012, nR=6, nr=3, slot='iron', color=IRON)        # link 2 (turned 90)
chain = ch.finish('LanternBow_Chain', MATS, CL, parent=pivot, origin=PV)
kf, kg = kit_part('BowLow', 'Lantern_Bow_Frame'), kit_part('BowLow', 'Lantern_Bow_Glass')
fr = MB(); kept = 0
for tris in kit_islands(kf):
    lo, hi = island_bbox(kf, tris)
    if lo[1] >= LANTERN_KEEP_YMIN and hi[2] <= LANTERN_KEEP_ZMAX:
        add_kit_island(fr, kf, tris, LANTERN_SHIFT, 'iron'); kept += 1
frame = fr.finish('Lantern_Bow_Frame', MATS, CL, parent=pivot, origin=PV, recalc=False)
gl = MB()
for tris in kit_islands(kg): add_kit_island(gl, kg, tris, LANTERN_SHIFT, 'timber')
glass = gl.finish('Lantern_Bow_Glass', MATS, CL, parent=pivot, origin=PV, recalc=False)
bpy.context.view_layer.update()
print('LANTERN bounds root-frame Blender', [round(min((o.matrix_world @ v.co)[k] for o in (beam, chain, frame, glass) for v in o.data.vertices), 4) for k in range(3)],
      [round(max((o.matrix_world @ v.co)[k] for o in (beam, chain, frame, glass) for v in o.data.vertices), 4) for k in range(3)])
print('LANTERN kit islands kept', kept, '(expect 8: bottom plate, 4 bars, top plate, roof, hook post)')

# ------------------------------------------------------------------ save + export
for sc in list(bpy.data.scenes):
    if sc != scene: bpy.data.scenes.remove(sc)
try: bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
except Exception as e: print('purge', e)
bpy.ops.wm.save_as_mainfile(filepath=HV + '/harpoon-v1.blend', copy=True)
kw = dict(use_selection=True, axis_forward='-Z', axis_up='Y', colors_type='LINEAR', use_triangles=True, add_leaf_bones=False,
          bake_anim=False, object_types={'MESH', 'EMPTY'})
for r, fbx in (((lroot, 'BowLantern.fbx'),) if LANTERN_ONLY else ((root, 'HarpoonMount.fbx'), (broot, 'HarpoonBarb.fbx'), (lroot, 'BowLantern.fbx'))):
    for o in scene.objects: o.select_set(False)
    for o in [r] + list(r.children_recursive): o.select_set(True)
    bpy.context.view_layer.objects.active = r
    bpy.ops.export_scene.fbx(filepath=HV + '/' + fbx, **kw); print('EXPORTED', fbx)
def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
print('BUILD_OK mount tris', sum(tris(o) for o in root.children_recursive if o.type == 'MESH'),
      'barb tris', sum(tris(o) for o in broot.children_recursive if o.type == 'MESH'),
      'lantern tris', sum(tris(o) for o in lroot.children_recursive if o.type == 'MESH'), '(budget 600)')
