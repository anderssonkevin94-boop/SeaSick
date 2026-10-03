# Bow harpoon v1 — FBX contract (phase 0, docs/PLAN-harpoon.md)

Files (all in `art-staging/harpoon-v1/`): `HarpoonMount.fbx`, `HarpoonBarb.fbx`, source `harpoon-v1.blend`, `rope-look.json`, `export-verification.json` (check.py output), `review-*.png`.

## Axes and scale
- Authored in **metres**, Blender Z up, **forward = −Y**. Exported like the kitchen/storage kits (`axis_forward='-Z', axis_up='Y', colors_type='LINEAR'`, triangulated). Unity sees Blender (x, y, z) as (−x, z, −y), so the mount's forward is **Unity +Z** and up is +Y.
- Every empty has identity rotation in the mount frame (check.py verifies that local −Y = forward and +Z = up after re-import).
- Import like `KitchenL1Import`: preserveHierarchy, file scale, import normals (flat-shaded faces).

## HarpoonMount (1,728 tris; budget 2,500)
```
HarpoonMount            root, origin = deck surface on the swivel axis
  Mount_Base            STATIC mesh: plinth (0.16 m) + 4 bolts + arc-shaped harpooner step behind the gun (0.30 m, radius 0.60–1.06, ±58° around the back)
  Swivel                empty, YAW PIVOT at (0, 0, 0.16) on the turntable axis; rotate about its local up (Unity Y)
    Swivel_Body         mesh: turntable ring, post, yoke, fat stubby barrel, stock + T grip, drum cheeks + axle, rope lead, fairlead eye
    Barb_Muzzle         empty at the muzzle face (0, −0.80, 1.72) m (Unity (0, 1.72, 0.80)); forward = Unity +Z. Barb spawn + line start
    Winch_Drum          empty at the drum centre (0, 0.42, 1.06); SPIN about its local X
      Winch_Coil        mesh: drum core, flanges, 5 fat rope coils, crank (spins with the drum)
    Harpooner_Stand     empty at (0, 0.86, 0.30) on the raised step, behind the grip; identity = facing the gun (Unity +Z)
```
- Everything that turns is under `Swivel`, including the stand, which rides round the arc step. At ±45° the stand stays on the step (the step covers ±58°).
- **Loaded barb (code-side):** parent a `HarpoonBarb` instance to `Barb_Muzzle` at local zero and identity rotation. The barb's origin is 0.25 m forward of its tail, so the shaft sits in the bore and the head sticks out ahead of the muzzle (see review-hero.png). There is no static barb in the mount, so there is nothing to double up.
- Barrel axis 1.72 m above the deck. The muzzle clears the rail cap over the whole ±45° arc (check.py sweeps −45…45 in 5° steps: no hull intersections, no vertex in either gun-port clearance box).

## HarpoonBarb (192 tris; budget 400)
```
HarpoonBarb             root = the point that goes at Barb_Muzzle; tip toward −Y (Unity +Z)
  Barb_Mesh             timber shaft (r 0.045), brass collar, fat iron diamond head (0.26 wide), two swept-back flukes (0.38 span), iron rope eye
  Line_Attach           empty at the eye centre (0, +0.335, 0) (Unity (0, 0, −0.335)); the line ends here
```
Length is 0.95 m from the eye to the tip. The tip is 0.80 m ahead of the root.

## Placement on CoasterFamily.Base (bow module `hull.bow.f30.low.v1`, prefab `ShipModules/Meshes/FCoaster/BowLow`)
- **Bow-local source units** (+X bow, +Y port, +Z up, 0.5 m/u): **(6.3, 0, 2.11)**. That is on the centreline, on the foredeck floor (top 2.11 u = 1.055 m), 3.15 m forward of the bow module's aft socket.
- **Unity, BowLow prefab local** (u, as imported by `CoasterKitImport`, x starboard, y up, z forward): **(0, 2.11, 6.3)**, rotation identity. The mount is in metres, so parent it so that its lossyScale is 1 (e.g. localScale 2 under a 0.5-scaled u-space parent).
- **Metres from the bow module origin:** (0, 1.055, 3.15). On Base (no middle) the bow starts 9.8 u (4.9 m) forward of the stern origin, so that is 8.05 m forward of the stern origin.
- **Footprint:** the static base spans x ±0.90 m and from 0.80 m forward of the axis to 1.06 m behind it, which is bow-local x 4.18–7.9 u. Its whole underside is flush on `Foredeck_Floor` (max gap 3 mm).
- **Clear of:**
  - The P0/S0 gun clearance boxes (`Gun_0_±1`, x 1.075–3.125 u, |y| ≥ 1.985 u).
  - Both cannons at every yaw.
  - The foredeck `Hatch` marker (3.45 u): 0.36 m from the nearest base edge.
- **Blocks:** the crew passage `Passage0` forward of x ≈ 4.2 u (the nose). Only the harpooner needs to go there; the stand is reached from aft.
- **Line clearance** (check.py casts a ray from the muzzle to sea-level targets at 10/20/35 m on every 5° bearing):
  - Clear from ±10° to ±45°.
  - Dead ahead (|yaw| ≤ ~4°), the line crosses the **bow lantern post** (`Lantern_Bow_Frame`) at every range.
  - Within ±5° at 10 m, it also crosses the stem cap.
  - Fix options for Kevin or the sailing session: move the bow lantern off the centreline (a hull edit), or route the LineRenderer over a stem point when it is blocked.

## Materials (3 slots on every mesh, same order)
| Slot | Material | Setup |
|---|---|---|
| 0 | `SS_Harpoon_Timber` | GameColor vertex colour only. The coaster hull is untextured painted facets, so the harpoon matches it. |
| 1 | `SS_Harpoon_Rope` | Rope tile × GameColor. The tile is `Assets/_Project/Art/WallL1/Textures/rope-tile-512.png`, the same image packed as V3_rope-tile-512.png. |
| 2 | `SS_Harpoon_Iron` | GameColor only, slight sheen (metallic 0.35, roughness 0.45). |

- **Palette = the coaster's own vertex colours (linear):**
  - Cream timber (0.58, 0.31, 0.10)
  - Dark planking (0.23, 0.09, 0.03)
  - Deck (0.47, 0.26, 0.12)
  - Strap iron (0.04, 0.05, 0.06)
  - Brass band (0.32, 0.25, 0.13), used only on the muzzle swell and the barb collar
- **Iron: YES, on the ship.** The no-iron rule is for level-1 *buildings*. The coaster already carries forged iron straps and an iron lantern, and its cannons have black iron barrels. So the barrel, turntable ring, yoke plate, bolts, barb head and flukes are dark iron, and the timber is the hull's cream and dark brown.
- **Rope line look:** `rope-look.json` holds the LineRenderer widths, slack/taut/strained tints, sag, strained glow pulse, and the drum coil spec.

## BowLantern (Kevin 2026-10-04; ≤ 600 tris)
The bow lantern moved off its post (the post stood in the line of fire dead ahead) onto a short timber beam out of the stem, hanging under the beam's tip, ahead of the stem and under the harpoon's line over the whole ±45° arc. Numbers live in `geo.py` (BowLow-local metres, Unity axes; the line math is in its comments).
```
BowLantern              root = the MOUNT's origin (deck on the swivel axis): game places it at BowLow-local (0, 1.055, 3.15) m, identity
  BowLantern_Beam       STATIC mesh: chunky beam z 4.72 (buried in the stem) -> 5.62 m, flat top 2.14 m, 0.22x0.20 -> 0.18x0.17;
                        iron strap at the stem (z 4.91), iron band + eye at the tip
  LanternBow_Pivot      empty = chain top at (0, 1.94, 5.50) m bow-local = root-local Unity (0, 0.885, 2.35). LanternSwing goes here.
    LanternBow_Chain    two iron links
    Lantern_Bow_Frame   the kit's own lantern body + hook post, unchanged shapes (8 islands; the deck foot, post, knee and gallows arm dropped)
    Lantern_Bow_Glass   the kit's glass, unchanged (the game's light loop finds *Lantern*_Glass)
```
- Lantern hook top 1.82 m, glass centre ~1.34 m (≈1.1 m over a calm sea), bottom 1.065 m; front 5.71 m, aft edge 5.30 m (the stem cap's front is 5.035 m).
- Game side (`CoasterOutfitting.BowLantern`): low bow only; disables the kit's `Lantern_Bow_*` renderers (only if they sit where BowLow's do), instantiates the prefab, paints it with the hull paint/glass, adds `LanternSwing` to the pivot; the existing warm light (range 4, intensity 1.6, unchanged) is parented to the pivot so it swings with the glass. Prefab missing → the kit lantern stays.
- A raised bow keeps its kit lantern (y 4.4–5.6 m) until the harpoon has a raised-bow placement (`HarpoonMount.BowModuleMountU` puts it on the lower foredeck).
