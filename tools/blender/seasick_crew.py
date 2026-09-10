# SeaSick -- the crew. One body, one rig, a kit per role.
#
# Re-runnable:
#   exec(open('/Users/kevinandersson/Desktop/SeaSick/tools/blender/seasick_crew.py').read())
#
# WHY THIS ASSET IS SHAPED THE WAY IT IS
#
# `CrewAgent` puts the weather on their faces: seasickness is cosmetic only --
# they go green in a foul sea, get their colour back in a calm one, and heave
# on station without ever leaving it. So the head must READ at chase-camera
# distance and the skin must be its own material, tinted through the existing
# MaterialPropertyBlock on `_BaseColor`. That single fact rules out hoods,
# painted faces and one-material characters.
#
# Three rules, the same three the scenery is built on (seasick_style.py):
#   1. SILHOUETTE FIRST. Judged at 30 m against water. Stocky shoulders, a
#      clear neck gap, boots wider than shins -- the outline does the work.
#   2. FACETED FORM, SOFT COLOUR. Flat faces, per-corner vertex-colour
#      gradient. Dark under the jaw and in the armpit, warm on the shoulder
#      tops and the brow.
#   3. THE CHAMFER IS THE STYLE. Every box gets one cut corner. That bevel is
#      the whole difference between this and Minecraft: not a rounder box, a
#      CUT one, so a moving light finds an edge on every limb.
#
# Conventions, shared with the hulls: up +Z, metres, FORWARD IS +X (the export
# yaws -90 so the face lands on Unity +Z), origin between the heels on the
# ground. Two objects, because that IS the two submeshes Unity wants:
# CREW_Cloth (never tinted) and CREW_Skin (tinted green).

import bpy, bmesh, math
from mathutils import Vector, Quaternion

def to_unity(p):
    """Author with FORWARD = +X, because that is how a body is easiest to
    write. Ship it with forward = Blender -Y, because the fleet measured that
    -Y is what lands on Unity +Z. Doing the yaw here means the FBX needs no
    correction downstream and no VisualYaw in the engine -- exactly the sort of
    fix-in-the-engine that costs a build."""
    return Vector((p.y, -p.x, p.z))


COLL = "CREW"
ORIGIN = Vector((0.0, 300.0, 0.0))     # clear of ISLAND (y=0) and STYLE (y=150)

# ------------------------------------------------------------------ charter --
# Every landmark, once, in one place -- the same discipline as WorldScale.cs.
# H is not a choice: WorldScale.Person is 1.70 and the ship's rail, companion
# step and ladder rungs are all derived from it. If this figure is not 1.700
# tall to the top of the CAP, every hand-sized thing on the ship is wrong.

H       = 1.700          # total height, cap included
HEAD    = H / 5.0        # 0.340 -- the proportion call: five heads
BEVEL   = H * 0.010      # 0.017 -- one chamfer for the whole person

# ...heights, from the deck up
Z = dict(
    sole      = 0.000,
    ankle     = 0.150,   # boot top
    knee      = 0.470,   # 0.28 H, where a real knee is
    crotch    = 0.790,
    belt      = 0.985,
    ribs      = 1.130,
    shoulder  = 1.290,   # top of the chest block
    pit       = 1.245,   # shoulder JOINT centre -- arms hang from here
    chin      = 1.360,
    crown     = 1.660,   # top of the skull
    top       = 1.700,   # top of the cap/hair. chin -> top = 0.340 = one head
    elbow     = 0.955,
    wrist     = 0.705,
    fingertip = 0.590,
    brow      = 1.560,
    eye       = 1.518,
    nose      = 1.478,
)

# ...half-breadths (y) and half-depths (x)
W = dict(
    chest_y = 0.158, chest_x = 0.106,
    waist_y = 0.150, waist_x = 0.100,
    hip_y   = 0.136, hip_x   = 0.096,
    neck_y  = 0.058, neck_x  = 0.060,
    head_y  = 0.118, head_x  = 0.128,   # 0.236 wide: head is 0.47 of shoulder
    arm_y   = 0.050, arm_x   = 0.053,   # sleeve
    fore_y  = 0.046, fore_x  = 0.048,   # bare forearm -- sleeves are rolled
    hand_y  = 0.031, hand_x  = 0.053,
    thigh_y = 0.068, thigh_x = 0.082,
    shin_y  = 0.058, shin_x  = 0.068,
    boot_y  = 0.072,
)
Y = dict(arm = 0.192, leg = 0.086)      # limb centrelines
# shoulder outer = 0.196 + 0.054 = 0.250 -> 0.500 m across. A stocky sailor.

# The rig, defined HERE with the mesh so a joint can never drift from the
# geometry it bends. EIGHT bones, and no elbow or knee -- at 50 px a bending
# limb and a swinging one are the same picture, and a straight limb is one box
# instead of two. Single-bone hard weights: flat faces stay flat, nothing to
# weight-paint, and it is still one skinned mesh per material.
JOINTS = dict(
    root =(0, 0, 0.0),
    hips =(0, 0, Z["crotch"]),
    chest=(0, 0, Z["belt"]),
    head =(0, 0, Z["chin"] - 0.030),
    arm_L=(0, Y["arm"], Z["pit"]),   arm_R=(0, -Y["arm"], Z["pit"]),
    leg_L=(0, Y["leg"], Z["crotch"]), leg_R=(0, -Y["leg"], Z["crotch"]),
)

# ----------------------------------------------------------------- colours --
# sRGB. Skin is EXACTLY CrewAgent.healthyTint -- the asset must not disagree
# with the code that tints it.
C = dict(
    skin   =(0.87, 0.65, 0.48), skin_d=(0.55, 0.36, 0.26),
    sick   =(0.55, 0.78, 0.45),
    linen  =(0.80, 0.76, 0.68), linen_d=(0.55, 0.51, 0.44),
    wool   =(0.26, 0.29, 0.35), cap    =(0.21, 0.24, 0.30),
    leather=(0.34, 0.22, 0.13), boot   =(0.19, 0.13, 0.09),
    hair   =(0.24, 0.17, 0.11), eye    =(0.10, 0.09, 0.10),
    shade  =(0.08, 0.11, 0.16),
    rule_r =(0.75, 0.12, 0.10), rule_w=(0.85, 0.84, 0.80),
)


def srgb_to_lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def scale(c, k):
    return tuple(min(1.0, max(0.0, x * k)) for x in c)


def shade(c, f):
    """Darken AND cool (shadow is blue), or brighten AND warm (sun is not)."""
    if f < 1.0:
        return mix(scale(c, f), C["shade"], (1.0 - f) * 0.45)
    return mix(scale(c, f), (1.0, 0.95, 0.80), (f - 1.0) * 0.28)


def lit(col, up=0.26, down=0.30, ao=0.0):
    """The style rule as a function: a face's tone is decided by which way it
    POINTS, so a chamfer is visible without a texture or a normal map. `ao`
    darkens a whole part that lives in shadow (under a hem, inside an armpit)."""
    base = shade(col, 1.0 - ao) if ao else col

    def rule(n, p):
        t = max(-1.0, min(1.0, n.z))
        return shade(base, 1.0 + (up * t if t > 0 else down * t))
    return rule


def grad(lo_col, hi_col, z0, z1, up=0.22, down=0.26):
    """A COURSE with a gradient down it -- the strake rule, applied to a leg."""
    def rule(n, p):
        t = max(0.0, min(1.0, (p.z - z0) / (z1 - z0)))
        c = mix(lo_col, hi_col, t)
        s = max(-1.0, min(1.0, n.z))
        return shade(c, 1.0 + (up * s if s > 0 else down * s))
    return rule


# ----------------------------------------------------------------- builder --

class Build:
    def __init__(self):
        self.v, self.f, self.rule = [], [], []
        self.vb = []                    # bone name per vertex
        self.bone = "chest"

    def tri(self, a, b, c, rule):
        if len({a, b, c}) < 3:
            return
        self.f.append((a, b, c))
        self.rule.append(rule)

    def add_bm(self, bm, rule):
        base = len(self.v)
        bm.verts.ensure_lookup_table()
        for vt in bm.verts:
            self.v.append(vt.co.copy())
            self.vb.append(self.bone)
        for fc in bm.faces:
            idx = [base + vt.index for vt in fc.verts]
            for k in range(1, len(idx) - 1):
                self.tri(idx[0], idx[k], idx[k + 1], rule)
        bm.free()

    def box(self, x0, x1, y0, y1, z0, z1, rule, bev=None, taper=1.0,
            vert_only=True, bone=None):
        """A chamfered box. `taper` narrows the TOP in y and x -- that is how a
        forearm, a boot toe and a cap get their shape without another rule."""
        prev, self.bone = self.bone, (bone or self.bone)
        dx, dy, dz = x1 - x0, y1 - y0, z1 - z0
        b = BEVEL if bev is None else bev
        b = min(b, 0.17 * min(abs(dx), abs(dy), abs(dz)))
        bm = bmesh.new()
        cx, cy = (x0 + x1) * 0.5, (y0 + y1) * 0.5
        for sz in (0, 1):
            k = 1.0 if sz == 0 else taper
            for sy in (0, 1):
                for sx in (0, 1):
                    bm.verts.new((cx + (x0 - cx if sx == 0 else x1 - cx) * k,
                                  cy + (y0 - cy if sy == 0 else y1 - cy) * k,
                                  z0 if sz == 0 else z1))
        bm.verts.ensure_lookup_table()
        v = bm.verts
        for a, bb, c, d in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1),
                            (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            bm.faces.new((v[a], v[bb], v[c], v[d]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        if b > 1e-4:
            # Only the UPRIGHT edges. They are the ones that draw the outline
            # of a standing figure, so they are the only ones a 50 px crew
            # member can see -- and they cost half of bevelling all twelve.
            ed = [e for e in bm.edges
                  if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-6] if vert_only \
                 else bm.edges[:]
            bmesh.ops.bevel(bm, geom=bm.verts[:] + ed + bm.faces[:],
                            offset=b, segments=1, profile=0.5,
                            affect='EDGES', clamp_overlap=True)
        bm.normal_update()
        self.add_bm(bm, rule)
        self.bone = prev

    def mirror_box(self, *a, **k):
        """Both sides, one call. y is measured on the LEFT (Blender +Y) and
        negated for the right, so `bone=("arm_L", "arm_R")` reads in order."""
        bn = k.pop("bone", None)
        bl, br = bn if isinstance(bn, (tuple, list)) else (bn, bn)
        self.box(*a, bone=bl, **k)
        args = list(a)
        args[2], args[3] = -a[3], -a[2]
        self.box(*args, bone=br, **k)

    def wedge(self, pts, faces, rule):
        base = len(self.v)
        for p in pts:
            self.v.append(Vector(p))
        for fc in faces:
            for k in range(1, len(fc) - 1):
                self.tri(base + fc[0], base + fc[k], base + fc[k + 1], rule)

    def emit(self, name, coll, mat, rig=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(to_unity(p)) for p in self.v], [], list(self.f))
        me.update()
        for pg in me.polygons:
            pg.use_smooth = False           # flat: the chamfer must SHOW
        ca = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER')
        for pg in me.polygons:
            n, rl = pg.normal, self.rule[pg.index]
            for li in pg.loop_indices:
                p = me.vertices[me.loops[li].vertex_index].co
                ca.data[li].color = (*srgb_to_lin(rl(n, p)), 1.0)
        ob = bpy.data.objects.new(name, me)
        # The placement lives on ONE object. Giving the mesh ORIGIN *and*
        # parenting it to a rig that is also at ORIGIN offsets it twice, and
        # the armature bind then sees a 300 m mismatch and leaks it into Z
        # through every bone rotation -- 80 m of it, from a rig that measures
        # perfect. Same family as the matrix_parent_inverse trap: parent, and
        # let exactly one transform own the position.
        ob.location = (0.0, 0.0, 0.0) if rig is not None else ORIGIN
        coll.objects.link(ob)
        me.materials.append(mat)
        if rig is not None:
            # single-bone HARD weights: one group per bone, weight 1, no
            # blending. Flat faces stay flat and there is nothing to paint.
            byb = {}
            for i, bn in enumerate(self.vb):
                byb.setdefault(bn, []).append(i)
            for bn, idx in byb.items():
                ob.vertex_groups.new(name=bn).add(idx, 1.0, 'REPLACE')
            ob.parent = rig
            ob.modifiers.new("Armature", 'ARMATURE').object = rig
        return ob


def vc_material(name, tint=(1.0, 1.0, 1.0)):
    """Vertex colour x tint -- the same product the Unity shader computes, so
    Blender's material preview shows what the engine will draw. The skin mesh
    stores GREY, so previewing it raw shows a grey man and every look call
    lies about the asset."""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        b.inputs["Roughness"].default_value = 0.90
        if "Specular IOR Level" in b.inputs:
            b.inputs["Specular IOR Level"].default_value = 0.15
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type, mul.blend_type = 'RGBA', 'MULTIPLY'
        mul.inputs["Factor"].default_value = 1.0
        nt.links.new(vc.outputs["Color"], mul.inputs[6])
        mul.inputs[7].default_value = (*srgb_to_lin(tint), 1.0)
        nt.links.new(mul.outputs[2], b.inputs["Base Color"])
    return m


# -------------------------------------------------------------------- body --
# HOW BIG IS HE ACTUALLY? A 1.70 m crew member on a 24 m ship, seen from the
# chase camera about 35 m astern, is roughly FIFTY PIXELS TALL on a 540-wide
# portrait frame. That one number decides this whole asset:
#
#   at 50 px, one pixel is 34 mm of him.
#
# A nose 30 mm proud is sub-pixel. A brow ridge, a mouth line, a rolled cuff,
# a collar, a hair band, a belt -- every one of those is smaller than a pixel
# and costs triangles to render nothing. What survives at 50 px is the
# SILHOUETTE and four or five big blocks of colour, and that is all this asset
# is allowed to be. Detail added above that line does not read as detail; it
# reads as noise, and up close it reads as asset pack.
#
# So the whole figure is fourteen chamfered boxes. There is no knee and no
# elbow -- limbs swing from the hip and the shoulder, which is exactly what
# Minecraft does and exactly what "move arms and legs slightly" needs.

def build_cloth(b, mid=False):
    ya, yl = Y["arm"], Y["leg"]

    b.mirror_box(-0.082, 0.152, yl - W["boot_y"], yl + W["boot_y"],
                 Z["sole"], Z["ankle"], lit(C["boot"], up=0.30, down=0.22),
                 taper=0.90, bone=("leg_L", "leg_R"))

    # ONE leg, ankle to hip. A knee joint is 20 mm of shading at this size.
    b.mirror_box(-W["shin_x"], W["shin_x"], yl - W["shin_y"], yl + W["shin_y"],
                 Z["ankle"] - 0.012, Z["crotch"] + 0.014,
                 grad(shade(C["wool"], 0.66), shade(C["wool"], 1.20), 0.15, 0.79),
                 taper=1.20, bone=("leg_L", "leg_R"))

    # seat and shirt: two blocks, and the line between them is the strongest
    # horizontal on the figure, which is what makes him read as dressed.
    b.box(-W["hip_x"], W["hip_x"], -W["hip_y"], W["hip_y"],
          Z["crotch"] - 0.030, Z["belt"], lit(C["wool"]), taper=1.02,
          bone="hips")
    b.box(-0.103, 0.103, -0.150, 0.150, Z["belt"] - 0.006, Z["shoulder"],
          lit(C["linen"]), taper=1.02, bone="chest")

    # sleeve to the elbow -- the split from bare arm is a COLOUR edge, and a
    # colour edge is the one kind of detail that survives being small.
    b.mirror_box(-W["arm_x"], W["arm_x"], ya - W["arm_y"], ya + W["arm_y"],
                 Z["elbow"] + 0.020, Z["pit"] + 0.014,
                 grad(shade(C["linen"], 0.86), C["linen"], 0.95, 1.28),
                 taper=1.06, bone=("arm_L", "arm_R"))

    # flat cap: crown and brim. The brim is the only strong horizontal above
    # the shoulders and it is doing most of the work of saying "sailor".
    b.box(-0.136, 0.136, -0.126, 0.126, Z["top"] - 0.086, Z["top"],
          lit(C["cap"], up=0.34, down=0.20), taper=0.84, bone="head")
    b.box(0.126, 0.212, -0.114, 0.114, Z["top"] - 0.082, Z["top"] - 0.062,
          lit(C["cap"], up=0.36, down=0.18), taper=0.86, bone="head")

    if mid:
        # braces: four thin unbevelled straps. High contrast on a pale shirt,
        # so unlike a nose they DO survive at 50 px -- the only piece of
        # costume detail that earns its triangles.
        for sx in (1, -1):
            b.mirror_box(sx * 0.100, sx * 0.114, 0.056, 0.092,
                         Z["belt"] - 0.004, Z["shoulder"] - 0.004,
                         lit(C["leather"], up=0.30, down=0.26), bev=0.0,
                         bone="chest")
        b.mirror_box(-0.112, 0.112, 0.056, 0.092, Z["shoulder"] - 0.028,
                     Z["shoulder"] + 0.006,
                     lit(C["leather"], up=0.32, down=0.24), bev=0.0,
                     bone="chest")


# The skin mesh stores LIGHT, not colour. CrewAgent turns the crew green by
# pushing `_BaseColor` through a MaterialPropertyBlock, which only works if
# the material's base colour is free to be a hue -- so a skin tone baked into
# the vertices would fight a tint system that is already shipped. Paint the
# skin in greys and let `_BaseColor` supply the hue: multiply the two and you
# get skin; push _BaseColor toward green and every shadow comes with it.
#
# Cloth is the other way round -- vertex colour IS the colour there, and its
# _BaseColor stays white. See Art/Shaders/Crew/CrewVertexColor.shader.
# 0.82, not 1.0: a multiplier that starts at white has nowhere to go when a
# face points up -- every lit face clamps and the faceting disappears. This
# leaves headroom, so the range across the head comes out 0.61 (under the cap
# brim, cooled) to 1.00 (the crown), and _BaseColor supplies the rest.
SHADE = (0.82, 0.82, 0.82)


def build_skin(b, mid=False):
    ya = Y["arm"]

    # head: ONE block, tapered so it is not a cube, and no neck -- a 90 mm
    # neck is under three pixels and all it does is make the head wobble.
    b.box(-0.122, 0.122, -0.112, 0.112, Z["chin"] - 0.030, Z["crown"],
          lit(SHADE, up=0.26, down=0.26), taper=1.02, bone="head")

    # Two dark patches for eyes, flat and unbevelled. At 50 px this is the
    # entire face, and it is enough: it says which way he is looking, which is
    # the only thing a face has to say from 35 m.
    b.mirror_box(0.118, 0.124, 0.028, 0.080, Z["eye"] - 0.014, Z["eye"] + 0.010,
                 lit(scale(SHADE, 0.20), up=0.10, down=0.06), bev=0.0,
                 bone="head")

    # bare arm, elbow to fingertip, one box. Rolled sleeves are why the sea
    # shows on him at all -- this block and the head are the tint targets.
    b.mirror_box(-W["fore_x"], W["fore_x"], ya - W["fore_y"], ya + W["fore_y"],
                 Z["fingertip"] + 0.014, Z["elbow"] + 0.030,
                 grad(scale(SHADE, 0.80), SHADE, 0.60, 0.99),
                 taper=1.14, bone=("arm_L", "arm_R"))

    if mid:
        # a nose, and only a nose. It is the one feature that changes the
        # OUTLINE of the head rather than its colour, so it is the one that
        # survives longest as he gets smaller.
        b.box(0.108, 0.148, -0.026, 0.026, Z["nose"] - 0.010, Z["brow"] - 0.024,
              lit(SHADE, up=0.34, down=0.30), bev=0.005, taper=0.60,
              bone="head")


def build_ruler(b):
    """The numbers he must agree with, standing beside him IN THE SHOT. Scale
    cannot be judged from a screenshot unless the ruler is in the screenshot."""
    x = -0.52
    b.box(x - 0.020, x + 0.020, -0.90, -0.86, 0.0, H,
          lit(C["rule_w"], up=0.1, down=0.1), bev=0.004)
    for k in range(17):
        z = 0.1 * (k + 1)
        hit = abs(z - 0.952) < 0.001
        b.box(x - (0.052 if hit else 0.030), x + (0.052 if hit else 0.030),
              -0.90, -0.86, z - 0.006, z + 0.006,
              lit(C["rule_r"] if hit else C["rule_w"], up=0.1, down=0.1), bev=0.003)
    b.box(-0.30, 0.30, 0.42, 0.46, 0.916, 0.952,
          lit(C["rule_r"], up=0.24, down=0.20), bev=0.012)          # ship's rail
    for sx in (-0.26, 0.26):
        b.box(sx - 0.020, sx + 0.020, 0.422, 0.458, 0.0, 0.922,
              lit(C["rule_r"], up=0.20, down=0.24), bev=0.006)
    b.box(-0.30, 0.30, 0.46, 0.78, 0.0, 0.580,
          lit(C["rule_w"], up=0.24, down=0.24), bev=0.014)          # companion step


# --------------------------------------------------------------- assembly ---
# Eight bones. The tail of each limb bone is where that limb ENDS, so a bone
# can never drift from the geometry it drives -- the hulls' joinery rule 1,
# applied to a skeleton: place a piece off what it lands on.

BONES = [
    # name,    head,                          tail,                     parent
    ("root",  (0, 0, 0.0),                   (0, 0, Z["crotch"]),        None),
    ("hips",  (0, 0, Z["crotch"]),           (0, 0, Z["belt"]),         "root"),
    ("chest", (0, 0, Z["belt"]),             (0, 0, Z["shoulder"]),     "hips"),
    ("head",  (0, 0, Z["chin"] - 0.030),     (0, 0, Z["crown"]),        "chest"),
    ("arm_L", (0,  Y["arm"], Z["pit"] + 0.014), (0,  Y["arm"], Z["fingertip"] + 0.014), "chest"),
    ("arm_R", (0, -Y["arm"], Z["pit"] + 0.014), (0, -Y["arm"], Z["fingertip"] + 0.014), "chest"),
    ("leg_L", (0,  Y["leg"], Z["crotch"] + 0.014), (0,  Y["leg"], Z["sole"]), "hips"),
    ("leg_R", (0, -Y["leg"], Z["crotch"] + 0.014), (0, -Y["leg"], Z["sole"]), "hips"),
]


def build_rig(coll):
    old = bpy.data.objects.get("CREW_Rig")
    if old:
        ad = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        if ad.users == 0:
            bpy.data.armatures.remove(ad)
    ad = bpy.data.armatures.new("CREW_Rig")
    rig = bpy.data.objects.new("CREW_Rig", ad)
    rig.location = ORIGIN
    coll.objects.link(rig)
    vl = bpy.context.view_layer
    vl.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    made = {}
    for name, h, t, par in BONES:
        eb = ad.edit_bones.new(name)
        eb.head, eb.tail = to_unity(Vector(h)), to_unity(Vector(t))
        eb.use_connect = False
        if par:
            eb.parent = made[par]
        made[name] = eb
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return rig


def swing(pb, pitch=0.0, roll=0.0, yaw=0.0):
    """Pose a bone by an angle in the ARMATURE'S OWN axes, not the bone's.

    A bone pointing straight down has a roll Blender chose, so "rotate about
    local X" means nothing you can predict -- guessing it is how a walk cycle
    comes out swinging sideways. R_bone = M^-1 . R_world . M converts an
    honest world rotation into the basis the pose actually wants, and the
    armature is unrotated, so world axes ARE armature axes.

    Body forward is Blender -Y after to_unity, so PITCH (a leg swinging fore
    and aft) is a rotation about world X, and YAW is about Z.
    """
    from mathutils import Matrix
    r = (Matrix.Rotation(math.radians(yaw), 3, 'Z')
         @ Matrix.Rotation(math.radians(pitch), 3, 'X')
         @ Matrix.Rotation(math.radians(roll), 3, 'Y'))
    m = pb.bone.matrix_local.to_3x3()
    pb.matrix_basis = (m.inverted() @ r @ m).to_4x4()


def clip(rig, name, frames, fps, tracks, loop=True):
    """Bake a dict of {bone: fn(phase) -> (pitch, roll, yaw, dz)} into an
    action. Sampled, not curve-fitted: the FBX bakes anyway, and a sampled
    sine cannot drift from the one the design is written in."""
    act = bpy.data.actions.get(name)
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    step = max(1, frames // 12)
    keys = list(range(0, frames + 1, step))
    if keys[-1] != frames:
        keys.append(frames)
    for f in keys:
        ph = (f % frames) / float(frames) if loop else f / float(frames)
        for bone, fn in tracks.items():
            pb = rig.pose.bones[bone]
            pitch, roll, yaw, dz = fn(ph)
            swing(pb, pitch, roll, yaw)
            pb.location = (0.0, 0.0, 0.0)
            if dz:
                # A bone's local Y runs head->tail, and this rig has bones
                # pointing BOTH ways -- legs and arms hang down, hips and
                # chest stand up. Assuming one direction inverted the walk's
                # hip bob and floated him 67 mm off the deck. Read the
                # direction off the bone instead of assuming it.
                bn = pb.bone
                up = 1.0 if bn.tail_local.z > bn.head_local.z else -1.0
                pb.location = (0.0, dz * up, 0.0)
            pb.keyframe_insert("rotation_quaternion", frame=f + 1)
            pb.keyframe_insert("location", frame=f + 1)
    # Blender 4.4+ moved fcurves behind action slots; keys default to BEZIER
    # anyway, so reach for them only if this build still exposes them.
    for fc in getattr(act, "fcurves", []):
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'
    return act


TAU = math.pi * 2.0


def make_clips(rig, fps=24):
    """Two clips. They drive BONES ONLY and carry no root motion, because
    CrewAgent already owns the object transform -- it walks them to a station
    and leans them into an axe swing. An animation that moved the root would
    fight code that is already shipped."""
    out = {}

    # IDLE -- 4 s. "Slightly" is the whole brief: a breath, a little weight
    # shift, arms that hang and drift. Nothing here exceeds five degrees.
    n = fps * 4
    out["Crew_Idle"] = clip(rig, "Crew_Idle", n, fps, {
        "hips":  lambda t: (0, 0, 0, 0.006 * math.sin(TAU * 2 * t)),
        "chest": lambda t: (1.1 * math.sin(TAU * t), 0,
                            1.6 * math.sin(TAU * t + 1.1), 0),
        "head":  lambda t: (0.8 * math.sin(TAU * t + 2.0), 0,
                            4.0 * math.sin(TAU * t + 0.4), 0),
        "arm_L": lambda t: (3.4 * math.sin(TAU * t + 0.3), 1.5, 0, 0),
        "arm_R": lambda t: (3.0 * math.sin(TAU * t + 0.9), -1.5, 0, 0),
        "leg_L": lambda t: (1.0 * math.sin(TAU * t + 0.6), 0, 0, 0),
        "leg_R": lambda t: (-1.0 * math.sin(TAU * t + 0.6), 0, 0, 0),
    })

    # WALK -- 1 s, one full stride, and the hip height is SOLVED, not tuned.
    #
    # The legs are straight (no knee), so a swung leg is short by L(1-cos a)
    # and the naive fix is to drop the hips by that. That is wrong, and it
    # buried the boot 42 mm in the deck: the deepest point of a foot is not
    # under the ankle, it is the TOE, 152 mm forward of it. Swinging the
    # trailing leg BACK brings its toe TOWARD vertical, so that foot gets
    # LONGER while the ankle gets shorter.
    #
    # Deepest point below the hip, for a leg at angle a and a sole corner dx
    # forward of the ankle, is  L*cos a + |dx*sin a|, worst at the toe. Set the
    # hip to exactly that and the boot grazes the deck at every frame by
    # construction -- the same discipline as deriving the sail foot from where
    # the helmsman's eye is, rather than tuning until it looks right.
    #
    # It comes out as a small RISE at full stride, not a drop, which is the
    # honest answer for a knee-less leg. The bounce a real walk gets from the
    # knee is spent instead on a weight shift the hips CAN do: a roll.
    A = 18.0
    LEG = Z["crotch"] + 0.014
    TOE = 0.152                      # boot sole, forward of the ankle

    def hip_lift(a_deg):
        a = math.radians(a_deg)
        return LEG * (math.cos(a) - 1.0) + TOE * abs(math.sin(a))

    n = fps
    out["Crew_Walk"] = clip(rig, "Crew_Walk", n, fps, {
        "hips":  lambda t: (2.5, 2.2 * math.sin(TAU * t), 0,
                            hip_lift(A * math.sin(TAU * t))),
        "chest": lambda t: (-1.5, -3.0 * math.sin(TAU * t),
                            3.5 * math.sin(TAU * t), 0),
        "head":  lambda t: (0, 0, -2.0 * math.sin(TAU * t), 0),
        "arm_L": lambda t: (-0.8 * A * math.sin(TAU * t), 2.0, 0, 0),
        "arm_R": lambda t: (0.8 * A * math.sin(TAU * t), -2.0, 0, 0),
        "leg_L": lambda t: (A * math.sin(TAU * t), 0, 0, 0),
        "leg_R": lambda t: (-A * math.sin(TAU * t), 0, 0, 0),
    })

    rig.animation_data.action = out["Crew_Idle"]
    return {k: (int(v.frame_range[0]), int(v.frame_range[1])) for k, v in out.items()}


def build(ruler=True, mid=False, rig=True):
    for ob in list(bpy.data.objects):
        if ob.name.startswith("CREW_") and ob.type == 'MESH':
            me = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if me and me.users == 0:
                bpy.data.meshes.remove(me)
    coll = bpy.data.collections.get(COLL)
    if coll is None:
        coll = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(coll)

    rg = build_rig(coll) if rig else None
    bc = Build(); build_cloth(bc, mid)
    bs = Build(); build_skin(bs, mid)
    oc = bc.emit("CREW_Cloth", coll, vc_material("SS_CrewCloth"), rig=rg)
    os_ = bs.emit("CREW_Skin", coll, vc_material("SS_CrewSkin", C["skin"]), rig=rg)
    if ruler:
        br = Build(); br.emit  # noqa
        br = Build(); build_ruler(br)
        br.emit("CREW_Ruler", coll, vc_material("SS_CrewRule"))

    bpy.context.view_layer.update()
    zs = [(o.matrix_world @ v.co).z for o in (oc, os_) for v in o.data.vertices]
    out = dict(tris=len(oc.data.polygons) + len(os_.data.polygons),
               cloth=len(oc.data.polygons), skin=len(os_.data.polygons),
               height=round(max(zs) - min(zs), 4))
    if rg:
        out["clips"] = make_clips(rg)
        out["bones"] = len(rg.data.bones)
    return out


# ------------------------------------------------------------------ views ---

# He faces Blender -Y once `to_unity` has yawed him, so a camera in FRONT of
# him stands at azimuth -90. Framing him at az 0 photographs his flank and at
# az 90 his back -- both of which happened.
FRONT, FLANK, BACK = -90.0, 0.0, 90.0


def look(target=(0.0, 0.0, 0.95), az=FRONT - 32.0, el=8.0, dist=3.4,
         shading='MATERIAL'):
    """Frame him. Blender's view direction is view_rotation @ (0,0,-1), so the
    azimuth is COMPUTED, never guessed. `person_px` reports how tall 1.70 m
    comes out, so "is this too much detail" is answered at the real size."""
    t = Vector(target) + ORIGIN
    a, e = math.radians(az), math.radians(el)
    eye = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
    got = None
    for area in bpy.context.screen.areas:
        if area.type != 'VIEW_3D':
            continue
        sp = area.spaces.active
        sp.shading.type = shading
        sp.shading.color_type = 'VERTEX'
        sp.shading.light = 'STUDIO'
        sp.shading.show_cavity = (shading == 'SOLID')
        sp.overlay.show_overlays = False
        r3d = sp.region_3d
        r3d.view_perspective = 'PERSP'
        r3d.view_location = t
        r3d.view_distance = dist
        r3d.view_rotation = eye.to_track_quat('Z', 'Y')
        area.tag_redraw()
        fov = 2.0 * math.atan(36.0 / sp.lens)
        big = 2.0 * dist * math.tan(fov * 0.5)
        h_m = big if area.height >= area.width else big * area.height / area.width
        got = dict(region=(area.width, area.height),
                   person_px=round(H / h_m * area.height))
    return got


def frame(f):
    bpy.context.scene.frame_set(f)
    bpy.context.view_layer.update()
    return f


def use(name):
    rig = bpy.data.objects["CREW_Rig"]
    rig.animation_data.action = bpy.data.actions[name]
    return name


# ----------------------------------------------------------------- export ---

CREW_DIR = "/Users/kevinandersson/Desktop/SeaSick/Assets/_Project/Art/Crew"


def export(out_dir=CREW_DIR, fname="crew.fbx"):
    """One FBX: two meshes, the armature, and every action as a take.

    The -90 yaw the fleet needs is already burnt into the vertices by
    `to_unity`, so this only has to convert Z-up to Y-up -- which is what
    bake_space_transform does safely. path_mode STRIP, because there is no
    texture: the colour is in the mesh.
    """
    import os
    os.makedirs(out_dir, exist_ok=True)
    vl = bpy.context.view_layer
    for o in bpy.data.objects:
        o.select_set(False)
    objs = [bpy.data.objects[n] for n in ("CREW_Rig", "CREW_Cloth", "CREW_Skin")]
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    vl.objects.active = objs[0]

    # EXPORT FROM THE ORIGIN. `ORIGIN` only exists to stand the crew clear of
    # the islands in this shared blend file, but the armature carries it, and
    # baking animation bakes it in with everything else. The rest pose still
    # looks perfect -- which is the trap -- and then the first frame the
    # Animator evaluates teleports every crew member 300 m off the ship.
    # Same move export_fleet makes with the hulls: zero it, export, put it back.
    rig = bpy.data.objects["CREW_Rig"]
    saved = tuple(rig.location)
    rig.location = (0.0, 0.0, 0.0)
    vl.update()
    path = os.path.join(out_dir, fname)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'ARMATURE'},
        use_mesh_modifiers=False, mesh_smooth_type='FACE',
        global_scale=1.0, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
        axis_forward='-Z', axis_up='Y', use_triangles=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='STRIP')
    rig.location = saved
    vl.update()
    return dict(path=path, bytes=os.path.getsize(path), exported_from=(0.0, 0.0, 0.0))


def contact_sheet(action, frames, spacing=0.80):
    """Bake N frames of a clip into static meshes laid out along the direction
    of travel. A walk cycle cannot be judged one frame at a time, and it cannot
    be judged from a description -- this is the one picture that shows whether
    the feet plant and the arms counter-swing."""
    for o in list(bpy.data.objects):
        if o.name.startswith("SHEET_"):
            m = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if m and m.users == 0:
                bpy.data.meshes.remove(m)
    coll = bpy.data.collections[COLL]
    use(action)
    for i, f in enumerate(frames):
        frame(f)
        dg = bpy.context.evaluated_depsgraph_get()
        for n in ("CREW_Cloth", "CREW_Skin"):
            ev = bpy.data.objects[n].evaluated_get(dg)
            no = bpy.data.objects.new("SHEET_%02d_%s" % (i, n),
                                      bpy.data.meshes.new_from_object(ev))
            no.location = ORIGIN + Vector((0.0, -i * spacing, 0.0))
            coll.objects.link(no)
    for n in ("CREW_Cloth", "CREW_Skin", "CREW_Ruler", "CREW_Rig"):
        if n in bpy.data.objects:
            bpy.data.objects[n].hide_set(True)
    return look(target=(0.0, -spacing * (len(frames) - 1) * 0.5, 0.86),
                az=FLANK, el=5, dist=spacing * len(frames) * 0.78)


def clear_sheet():
    for o in list(bpy.data.objects):
        if o.name.startswith("SHEET_"):
            m = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if m and m.users == 0:
                bpy.data.meshes.remove(m)
    for n in ("CREW_Cloth", "CREW_Skin", "CREW_Ruler", "CREW_Rig"):
        if n in bpy.data.objects:
            bpy.data.objects[n].hide_set(False)


def floor_check():
    """Does the boot ever go through the deck? A knee-less leg is the whole
    reason to ask, and it is invisible at fifty pixels until it is not."""
    out = {}
    for a in bpy.data.actions:
        if not a.name.startswith("Crew_"):
            continue
        use(a.name)
        n = int(a.frame_range[1])
        vals = []
        for f in range(1, n + 1):
            frame(f)
            dg = bpy.context.evaluated_depsgraph_get()
            lo = 1e9
            for nm in ("CREW_Cloth", "CREW_Skin"):
                ob = bpy.data.objects[nm]
                ev = ob.evaluated_get(dg)
                me = ev.to_mesh()
                lo = min(lo, min((ob.matrix_world @ v.co).z for v in me.vertices))
                ev.to_mesh_clear()
            vals.append(lo)
        out[a.name] = (round(min(vals), 4), round(max(vals), 4))
    use("Crew_Idle"); frame(1)
    return out
