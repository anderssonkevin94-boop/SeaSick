# Harpoon v1 review renders. Run AFTER build.py (see RUN.md):
#   Blender -b art-staging/harpoon-v1/harpoon-v1.blend --python art-staging/harpoon-v1/render.py [-- only <name>]
#   `-- only lantern` = just the BowLantern shots (review-lantern-*.png); every shot shows the new lantern once it is built.
# The REAL base coaster (CoasterFamily.Base = SternLow + BowLow + RotorLow) is rebuilt read-only from the game's
# art-staging/f-coaster-runtime/kit.json; the bow cannons are the real cannon-astra-v1/cannon.fbx at the game's 1.35 u scale.
# Never saves the blend. Writes review-*.png into art-staging/harpoon-v1/.
import bpy, math, json, sys
from mathutils import Vector, Matrix, Euler
exec(compile(open('/Users/kevinandersson/Desktop/SeaSick/art-staging/harpoon-v1/geo.py').read(), 'geo.py', 'exec'))
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ONLY = ARGS[ARGS.index('only') + 1] if 'only' in ARGS else None
SEA_Z = 0.20
LOOK = json.load(open(HV + '/rope-look.json'))
scene = bpy.context.scene

def vc_mat(name, attr='GameColor', rough=.7):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); a = nt.nodes.new('ShaderNodeAttribute'); a.attribute_name = attr
    nt.links.new(a.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = rough; return m
def flat_mat(name, rgb, rough=.5, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (*rgb, 1); b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = strength
    return m
def srgb(hexs):
    h = hexs.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= .04045 else ((x + .055) / 1.055) ** 2.4 for x in c)

ctx = bpy.data.collections.new('Review context'); scene.collection.children.link(ctx)
hullmat = vc_mat('Review_Hull_VC')
# Kevin 2026-10-04: the bow lantern moved onto a beam (BowLantern, built by build.py); the game hides the kit's old one.
LANT = bpy.data.objects.get('BowLantern')
hull = load_kit(coll=ctx, mat=hullmat, skip=('Lantern_Bow_Frame', 'Lantern_Bow_Glass') if LANT else ())
# bow cannons: the game's cannon at the BowLow Gun_0_-1 / Gun_0_1 sockets (source u (2.1, -+3.46, 2.11), yaw 0 / 180)
cannons = []
for side, yaw in ((-1, 0), (1, 180)):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=ROOT + '/art-staging/cannon-astra-v1/cannon.fbx')
    new = [o for o in bpy.data.objects if o not in before]
    r = next(o for o in new if o.name.startswith('Cannon_Root'))
    for o in new:
        for c in o.users_collection: c.objects.unlink(o)
        ctx.objects.link(o)
        if o.type == 'MESH':
            if o.data.color_attributes: o.data.color_attributes[0].name = 'GameColor'
            o.data.materials.clear(); o.data.materials.append(hullmat)
    r.location = src_to_review((2.1, side * 3.46, 2.11)); r.rotation_euler = (0, 0, math.radians(yaw - 90)); r.scale = (0.675,) * 3
    cannons.append(r)
bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, SEA_Z)); sea = bpy.context.object; sea.name = 'Sea'
sea.data.materials.append(flat_mat('Review_Sea', (0.015, 0.06, 0.075), rough=.25))
for c in sea.users_collection: c.objects.unlink(sea)
ctx.objects.link(sea)

# place the mount on the bow
mount = bpy.data.objects['HarpoonMount']; mount.location = MOUNT_AT
swivel = bpy.data.objects['Swivel']; muzzle = bpy.data.objects['Barb_Muzzle']
barb = bpy.data.objects['HarpoonBarb']
def barb_copy(name):
    root = bpy.data.objects.new(name, None); ctx.objects.link(root)
    for ch in barb.children:
        o = ch.copy(); ctx.objects.link(o); o.parent = root
    return root
loaded = barb_copy('Loaded_Barb')
if LANT:   # same root spot as the mount; glass glows, a warm point light like the game's (CoasterOutfitting)
    LANT.location = MOUNT_AT; bpy.context.view_layer.update()
    lglass = bpy.data.objects['Lantern_Bow_Glass']; lglass.data.materials.clear()
    lglass.data.materials.append(flat_mat('Review_Lantern_Glow', (1.0, .55, .2), rough=.4, emit=(1.0, .55, .23), strength=6.0))
    lamp = bpy.data.objects.new('Review_Lantern_Light', bpy.data.lights.new('Review_Lantern_Light', 'POINT')); ctx.objects.link(lamp)
    lamp.data.energy = 40; lamp.data.color = (1, .55, .23); lamp.data.shadow_soft_size = .1
    lamp.location = sum((lglass.matrix_world @ v.co for v in lglass.data.vertices), Vector()) / len(lglass.data.vertices)
barb.location = (0, 40, -20)   # park the original out of shot

# lighting
w = scene.world or bpy.data.worlds.new('W'); scene.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs[0].default_value = (0.55, 0.68, 0.85, 1); bg.inputs[1].default_value = 0.9
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); ctx.objects.link(sun)
sun.data.energy = 3.2; sun.data.angle = math.radians(8); sun.rotation_euler = Euler((math.radians(48), 0, math.radians(35)))
cd = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cd); ctx.objects.link(cam); scene.camera = cam
try:
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 16; scene.cycles.use_denoising = True
except Exception as e: print('engine', e)
scene.render.image_settings.file_format = 'PNG'; scene.view_settings.view_transform = 'Standard'

def set_yaw(deg):
    swivel.rotation_euler = (0, 0, math.radians(deg)); bpy.context.view_layer.update()
    loaded.matrix_world = muzzle.matrix_world.copy()
def shot(name, target, pitch, yaw, dist, w_, h_, lens=35):
    if ONLY and ONLY not in name: return
    t = Vector(target); d = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(pitch)), math.cos(math.radians(yaw)) * math.cos(math.radians(pitch)), math.sin(math.radians(pitch))))
    cam.location = t + d * dist; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler(); cd.lens = lens
    scene.render.resolution_x = w_; scene.render.resolution_y = h_; scene.render.resolution_percentage = 100
    scene.render.filepath = HV + '/' + name; bpy.ops.render.render(write_still=True); print('RENDERED', name)
# yaw convention for shot(): 0 = camera BEHIND the ship (+Y side, looking toward the bow), 180 = ahead of the bow.

M = MOUNT_AT
set_yaw(0)
shot('review-phone-high50.png', (0, -5.6, 1.0), 50, 0, 21, 540, 1170, lens=30)
shot('review-hero.png', M + Vector((0, 0, .8)), 20, 145, 4.6, 1200, 900, lens=40)
set_yaw(-40)
shot('review-bow-ports.png', M + Vector((0, 2.0, .6)), 42, -25, 7.5, 1000, 1000, lens=35)
set_yaw(0)

# ------------------------------------------------------------------ rope at three tensions
for o in [loaded] + list(loaded.children): o.hide_render = True   # the barb is out on the line
img = next((n.image for n in bpy.data.materials['SS_Harpoon_Rope'].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
def rope_mat(state):
    st = LOOK['states'][state]; m = bpy.data.materials.new('Review_Rope_' + state); m.use_nodes = True; nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0; mix.inputs[7].default_value = (*srgb(st['tint']), 1)
    if img:
        tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; nt.links.new(tx.outputs['Color'], mix.inputs[6])
    else: mix.inputs[6].default_value = (.75, .6, .4, 1)
    nt.links.new(mix.outputs[2], b.inputs['Base Color']); b.inputs['Roughness'].default_value = .8
    if st['emissionIntensity'] > 0:
        b.inputs['Emission Color'].default_value = (*srgb(st['emission']), 1); b.inputs['Emission Strength'].default_value = st['emissionIntensity']
    return m
# ------------------------------------------------------------------ BowLantern shots (`-- only lantern`): the line out DEAD AHEAD over it
if LANT:
    def line_out(yaw, rng, state):
        set_yaw(yaw); a = muzzle.matrix_world.translation.copy()
        fwd = (muzzle.matrix_world.to_3x3() @ Vector((0, -1, 0))); fwd.z = 0; fwd.normalize()
        end = a + fwd * rng; end.z = SEA_Z + .02; d = (end - a).normalized()
        fb = barb_copy('Lantern_Shot_Barb')
        fb.matrix_world = Matrix.Translation(end) @ d.to_track_quat('-Y', 'Z').to_matrix().to_4x4() @ Matrix.Translation((0, -.40, 0))
        bpy.context.view_layer.update()
        tail = bpy.data.objects.get('Line_Attach'); la = fb.matrix_world @ (tail.matrix_local.translation if tail else Vector())
        st = LOOK['states'][state]; span = (la - a).length; sag = st['sagFractionOfSpan'] * span
        pts = [a.lerp(la, i / 40) - Vector((0, 0, 4 * sag * (i / 40) * (1 - i / 40))) for i in range(41)]
        mb = MB(); mb.tube(pts, st['widthM'] / 2, n=6)
        return mb.finish('Lantern_Shot_Rope', [rope_mat(state)] * 3, ctx), fb
    rope_o, fb = line_out(0, 20.0, 'taut')   # 20 m: dead ahead at 10-15 m the line already grazes the stem cap (CONTRACT)
    PW = bpy.data.objects['LanternBow_Pivot'].matrix_world.translation.copy(); MZ = muzzle.matrix_world.translation.copy()
    shot('review-lantern-phone-high50.png', (0, -5.6, 1.0), 50, 0, 21, 540, 1170, lens=30)
    shot('review-lantern-side-deadahead.png', MZ.lerp(PW, .5) + Vector((0, -.9, -.2)), 6, -90, 6.5, 1000, 800, lens=35)
    shot('review-lantern-close.png', PW + Vector((0, 0, -.25)), 14, -140, 3.4, 800, 1000, lens=40)
    rope_o.hide_render = True
    for o in [fb] + list(fb.children): o.hide_render = True
    set_yaw(0)

float_barb = barb_copy('Floating_Barb')
for state, yaw in (('slack', 22), ('taut', 22), ('strained', 22)):
    set_yaw(yaw); a = muzzle.matrix_world.translation.copy()
    fwd = (muzzle.matrix_world.to_3x3() @ Vector((0, -1, 0))).normalized()
    end = a + fwd * 14.0; end.z = SEA_Z + .02
    d = (end - a).normalized()
    float_barb.matrix_world = Matrix.Translation(end) @ d.to_track_quat('-Y', 'Z').to_matrix().to_4x4() @ Matrix.Translation((0, -.40, 0))
    bpy.context.view_layer.update()
    tail = bpy.data.objects.get('Line_Attach'); la = float_barb.matrix_world @ (tail.matrix_local.translation if tail else Vector())
    st = LOOK['states'][state]; span = (la - a).length; sag = st['sagFractionOfSpan'] * span
    pts = []
    for i in range(21):
        t = i / 20; p = a.lerp(la, t) - Vector((0, 0, 4 * sag * t * (1 - t)))
        if st.get('restOnWater') and p.z < SEA_Z + .03: p.z = SEA_Z + .03
        pts.append(p)
    mb = MB(); mb.tube(pts, st['widthM'] / 2, n=6)
    ob = mb.finish('Rope_' + state, [rope_mat(state)] * 3, ctx)
    shot('review-rope-%s.png' % state, a.lerp(la, .45), 48, 10, 13, 720, 900, lens=35)
    ob.hide_render = True
for o in [float_barb] + list(float_barb.children): o.hide_render = True

# ------------------------------------------------------------------ barb close-up (isolated, neutral backdrop)
for o in list(ctx.objects):
    if o.type == 'MESH' and o.name != 'Sea': o.hide_render = True
for o in mount.children_recursive + (LANT.children_recursive if LANT else []): o.hide_render = True
sea.data.materials[0] = flat_mat('Review_Backdrop', (0.30, 0.34, 0.38), rough=.9)
barb.location = (0, 0, 1.0); barb.rotation_euler = (0, 0, math.radians(-25)); bpy.context.view_layer.update()
shot('review-barb.png', (0, -.10, 1.0), 28, 125, 2.3, 900, 700, lens=50)
print('RENDER_OK')
