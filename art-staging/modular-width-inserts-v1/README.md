# Ship Width Inserts, First Review

Staged outside Unity. The approved W1-r2/V3 originals are untouched.
This is the lower-hull width prototype, not a backend integration or an upper-deck kit.

## Design
- A single expansion adds 2.8 authoring units of beam: 9.28 -> 12.08.
- Length and depth do not change. This is independent of inserting six-unit length bays.
- Middle: port/starboard halves move outward 1.4 units each; new deck and bottom strips close the gap.
- Stern: a fixed central core preserves the complete wheel housing and M1 mount.
  Outer shoulders move outward; two inserts connect them to that core.
- Bow: center inserts taper toward the single approved stem. The expanded bow
  halves are authored counterparts, not purely translated standard halves.
  The carved prow remains unchanged. Standard bow halves are supplied separately.
- Rails, fittings, housing, wheel, helm and chimney are not scaled to make the ship wider.
  Bow rail curvature is authored with the expanded shoulder; there is no runtime deformation.

## Files
- `expanded-ship.blend`: assembled ship with one middle bay, individual pieces retained.
- `exploded-parts.blend`, `exploded.png`: explanatory layout, NOT game placement.
- `assembled.png`, `top.png`, `side.png`, `front.png`, `back.png`: actual mesh renders.
- `models`: individual vertex-colored FBX parts using the existing ship export convention.
- `manifest.json`: lengths, part transforms, triangle totals, hull checks and hydrostatic references.
- `hydrostatics`: immersed sectional area versus waterline, sampled from expanded hull geometry only.

## Assembly Contract
Source +X forward, +Y port, +Z up. FBX uses the existing conversion to game
(-source Y, source Z, source X). These are existing ship authoring units;
retain the game's established asset-to-world scale rather than interpreting them as metres.

Each part FBX exports mesh-local coordinates. Apply its `local_position`
from the manifest (converted to game axes) under its module root. This is
especially important for the translated outer halves and wheel axle.
The module root is the aft longitudinal connection plane, at the existing height datum.
Module order: Stern, zero or more Middle, Bow. Use one consistent width throughout.
Chimney is an independent fitting. Gun-slot positions remain provisional; no new
capacity, crew, weight or cannon-slot balance is invented by this asset package.

Individual split hull pieces have deliberately open mating boundaries. The assembled
hull is checked after welding, including short, one-middle and two-middle configurations.
No hidden mating caps are added. Production assembly may merge static parts to reduce
draw calls; the review exports retain the logical pieces for inspection.

Hydrostatic tables are upright, level-immersion approximations and do not calculate
stability, usable cargo volume, weight or crew capacity. Do not reuse the old W1
tables for this shape. Raised decks need matching width inserts in a subsequent pass.
No colliders, navigation, LODs, runtime widening UI, or Unity registration are included.
