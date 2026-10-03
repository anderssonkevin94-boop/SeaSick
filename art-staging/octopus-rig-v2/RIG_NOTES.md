# Octopus rig v2: longer arms

v2 is v1 with the 7 arms lengthened about 1.42x along their own centrelines (Kevin approved this on 2026-10-03). The head core, mantle and eyes are unchanged. v1 is left untouched in `../octopus-rig-v1/`.

The bone names, hierarchy, curl convention and front axis are the same as v1. Read v1's RIG_NOTES for the full contract. Only the differences are listed here.

Rebuild:

```
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python scripts/build_rig.py -- --factor 1.45
$B -b --factory-startup --python scripts/poses_measure_render.py -- --save
$B -b --factory-startup --python scripts/export_verify.py
```

## How the arms were lengthened
I used the coordinator's method with one refinement: the stretch is applied by **translating cross-sections**, not by deforming with bone transforms.

- **Factor F = 1.45** on bones 01-09.
- Every arm vertex has an arc position s along its arm, the same smoothed field used for the v1 weights. It is moved to the matching point on the lengthened centreline, `phi(s) = s + (F-1)*g(s)`. Here g ramps the stretch in with a smoothstep over the first 8 cm, so the arm/mantle junction is not pulled.
- Each cross-section is a pure translation, so arm thickness is unchanged and nothing is scaled.
- **Suckers** move as one rigid translation: the surface displacement at their own arc position. Their shape is exact; they just spread apart along the arm.
- New joints are placed with the same mapping. Bone directions are unchanged, so the rolls and rest bends are identical to v1 (`arm_rest_bends.json` matches v1).
- The weights are re-bound with the same arc-position tents, so they keep the same relative position between joints.

Why translation and not the bone-deform method: it gives an exact arc-length stretch with no shrinkage at the joints, and the suckers stay rigid by construction.

Resulting arm lengths. The effective factor is 1.42 because the first 8 cm ramps in.

| arm | v1 length (m) | v2 length (m) |
|---|---|---|
| Arm0 | 0.558 | 0.791 |
| Arm1 | 0.527 | 0.746 |
| Arm2 | 0.525 | 0.744 |
| Arm3 | 0.596 | 0.846 |
| Arm4 | 0.604 | 0.857 |
| Arm5 | 0.473 | 0.668 |
| Arm6 | 0.602 | 0.855 |

Rest bounding box is now 1.323 x 1.220 x 0.724 m (was 1.0 x 0.936 x 0.617).

**Height check.** The water is at Z 0.1 and the head dome top is at 0.343 in the surfaced poses, so the head stands 0.243 above the water. The highest point of P1 is at Z 0.585, which is 0.485 above the water: **2.0x the head height**. F = 1.45 was kept because it already meets the 1.5-2x target.

## Asset integrity
- Vertex, face and loop counts are 2222 / 4186 / 12558, the same as a fresh import of Meshy's FBX. The UV layer `UVMap` is identical value by value. Same single material.
- The 4 PNGs are byte-identical copies, checked with `cmp`.
- **The mantle shell and both eyes (137 verts) are bit-identical to the original.** The 190 verts weighted 100% to Body (mantle, eyes, head core) have a **max displacement of 0**.
- In total, 306 of 2222 verts are untouched. The moved verts are arm verts and suckers; the largest move is 0.204 m, at an arm tip.

## Rig checks (re-run on v2)
- 72 bones, same names.
- Re-imported FBX: every bone has scale 1.0000. Objects have scale 1 and rotation 0.
- 0 unweighted verts, at most 3 influences, all normalised.
- Curl convention on all 70 bones: positive local X curls toward the suckers.
  - The 41 bones that have suckers line up with them at min 0.989 (mean 0.998).
  - The 29 bones without suckers line up with the sucker direction carried along from neighbouring bones at min 0.924.
  - Behaviour test (+15 deg on each bone alone): every tail moved toward the suckers, min 0.924.

## Deformation (same metrics as v1)
| pose | edge max | edge min | flipped | sucker drift max | interpenetration |
|---|---|---|---|---|---|
| bind | 1.00 | 1.00 | 0 | 0 | none |
| P1 surfaced idle | 1.91 (Arm6_00/Body junction) | 0.54 | 0 | 0.031 | none |
| P2 windup | 1.46 | 0.38 | 0 | 0.079 (Arm0_00 base sucker, 5 mm on a 64 mm root) | none |
| P3 slam | 1.33 | 0.52 | 0 | 0.030 | none |
| P4 extreme curl | 2.22 (web between Arm2_00 and Arm3_00) | 0.45 | 0 | **0.187** (Arm2_05, 4.5 mm) | none |

Curl sweep on Arm2 (uniform N deg per bone on bones 02-09): 0 flipped faces at 20-50 deg. Sucker drift was 0.06 / 0.09 / 0.13 / 0.16 at 20 / 30 / 40 / 50 deg. On the longer arms, the inner side of a tight curl pulls in under linear blend skinning, and rigid suckers there lift off by a few mm. I tried two alternatives: suckers copying the surface blend (worse: P4 0.206), and blending suckers on the first two bones (worse in P1). So the v1 scheme stays.

## Poses
Poses are stored in the blend as actions `Pose_P1..P4`. **`Octopus_Poses.fbx`** carries `Pose_P1`, `Pose_P2` and `Pose_P3` as takes, frames 1-5 (the same pose held). After re-import the takes are named `OctopusRig|OctopusRig|Pose_Pn`. Same export settings as the rest FBX, plus baked animation.

- **P1 surfaced idle:** Arm1-5 tower in S-curves with hooked tips around the head. Arm0 and Arm6, the front pair, stay low so the face stays clear (`P1_face34.png`). Arm pose angles come from a per-arm search: at least 7 cm from the head and at least 9 cm between arm centrelines.
- **P2 windup:** Arm0 rises to Z 0.60 and leans back over the head, tip above the mantle at y 0.23.
  - **Honest issue:** a pose with the tip *behind* the mantle (y >= 0.30) does exist. The arm then lies along the mantle top with only about 1.5 cm gap (the 5.5 cm centreline clearance minus arm radius), and its height drops to Z 0.27. That reads as draped, not wound up.
  - Even at 1.42x, the arm cannot both rise high and drop behind a 0.34 m tall, back-leaning mantle while staying clear of it. I chose the high windup.
  - Also visible: suckers bunch at the sharply bent root.
- **P3 slam:** Arm0 extended forward, tip on the water at y -0.68 (v1 reached -0.43). It arcs about 10 cm above the water.
- **P4 extreme curl:** Arm2 lifted, then 35-60 deg per bone. Suckers on the inside of the coil jam together (see the drift above).

Renders in `renders/`: `bind_front34.png`, `P1_front34.png`, `P1_face34.png` (face not covered), `P1_side.png`, `P2_windup_front34.png`, `P3_slam_front34.png`, `P4_curl_closeup.png`.

## Known weak spots (in addition to v1's)
1. The texture now stretches about 1.45x along the arms. The UVs are untouched as required, so sucker-free skin texture is elongated. The Meshy texture is mostly flat colour, and it reads fine in the renders.
2. P2 cannot reach behind the mantle without draping on it (see above).
3. Tight curls lift inner-side suckers by 2-4.5 mm (above).
4. Same as v1: 7 arms, a tip-bone kink at `_08`->`_09`, and web stretch when one arm lifts hard. The Unity step should confirm that the octopus faces +Z and that +X curls toward the suckers.
