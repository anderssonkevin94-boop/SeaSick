# W2-r1 Wide, Deep Hull Family

Offline Blender asset study, 24 September 2026. No Unity import or gameplay
changes. Previous W1-r2 assets remain preserved.

## Design and Budget

The working deck is 25 percent wider than W1-r2 (11.60 versus 9.28 authoring
units). Its height remains 1.76; the keel is lowered from -1.92 to -2.75.
These are independently generated cross-sections, not scaled assembled meshes.
Rails, posts, bolts, plank pitch, chimney, helm, wheel and housing keep their
existing sizes. Additional deck seams fill the wider deck. The approved pointed
V3 bow and its 60-triangle decorative prow are retained.

| Assembly | W1-r2 V3 | W2-r1 |
| --- | ---: | ---: |
| Short, no middle bay | 8,732 triangles | 8,972 triangles |
| Long, one middle bay | 9,694 triangles | 9,990 triangles |

Counts include the standard reinforced rotor, carrier, helm and chimney.
Flat shaded, vertex-colored geometry. These counts are not iPhone benchmarks.

## Compatibility Contract

- Hull interface: W2-r1 only. Do not connect W1-r2 and W2-r1 sections directly.
- Standard M1 wheel socket and rotor/carrier geometry remain unchanged.
- The larger M1-L wheel is NOT supported by this housing.
- No width-transition module is supplied.
- Partial decks, weapon fit and crew navigation remain future work.
- Deck-slot empties are provisional; they do not certify cannon clearance.

## Coordinates and Assembly

Use `manifest.json` for exact lengths, files and socket positions.
Units remain V8 authoring units, not calibrated game metres.
Blender: +X toward bow, +Y port, +Z up. Existing FBX conversion maps Blender
positions `(x,y,z)` to game `(-y,z,x)`; placement must be applied separately.

- Stern_W2: 9.30 long, origin at its aft datum, placed at X=0.
- Midship_W2: 6.00 long, first origin at X=9.30; repeats every 6.00.
- Bow_W2: approximately 10.98 including the decorative tip, placed at
  X=9.30 + 6.00 * middle-bay count. Its forward marker is not a join socket
  for extending the ship beyond the prow.
- Prow shares the Bow_W2 origin.
- Rotor and Carrier origins are axle-local. Place at stern-local
  (0.72, 0, 0.35). Animate Rotor only around Blender Y / game X.
- Chimney pivot is base-centre, placed at (assembled length / 2, 0, 1.76).

Connection faces are deliberately open; matching loops close on assembly.
The exploded library is a presentation, not assembly placement.
No intentional object-scale stretching is required.

## Files and Review

- `short/ship.blend`, `long/ship.blend`: assembled scenes and matching renders.
- `module-library.blend`, `exploded.png`: separate module presentation.
- `top.png`, `side.png`, `front.png`, `back.png`: short-ship orthographic review.
- `models/`: 20 FBX meshes with `Col` vertex colors.
- `manifest.json`: identifiers, placement and compatibility metadata.
- `validation.json`: matching interfaces and welded assembly checks.
- `export-verification.json`: independent saved-scene and FBX checks.

Checks passed for zero through three middle bays: no boundary edges,
overconnected edges or degenerate faces in the checked welded hull, rail,
ironwork and seam components. All 20 FBXs round-trip with matching counts,
bounds, zero export origins and vertex colors. Short and long scenes share
identical local module geometry. Wheel surfaces were checked every five degrees
(72 samples per assembly), excluding intentional bearing contact within radius
0.55. This sampling is not a continuous collision or containment proof.

Preview images use Blender Workbench, not Unity lighting. No colliders, LODs,
buoyancy, stability, balance, waterline or iOS performance validation is included.
The deeper stern extends below the standard wheel; these dry reviews show the
whole hull, including the portion intended to sit below water.

## Reproduce

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_hull_wide_v1.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_hull_family_v1.py -- wide
```
