# W1 Modular Hull Family

24 September 2026. Blender asset prototype; no Unity imports or game changes.
Built from the approved V8 ship design and the first modular wheel study.
Both earlier asset sets remain unchanged.

## What to Review

- `short/hero.png`: stern directly connected to bow, no middle bay.
- `long/hero.png`: identical stern and bow with one six-unit middle bay.
- `exploded.png`: the actual three separate modules, not a concept painting.
- `short/ship.blend` and `long/ship.blend`: assembled review scenes.
- `module-library.blend`: exploded authoring scene with module parents/sockets.
- Each assembly also has side and top renders at matching orthographic scale.

The short boat has a compact, broad silhouette. The long boat gains working
deck area without scaling the end pieces, paddle wheel, plank width or fittings.
The wheel stays integrated into the stern, with a flat working deck and separate
fitted housing. No upper deck was added in this pass, to keep the hull comparison
clear. Partial decks remain available in the preceding wheel study.

## Modules and Budgets

Dimensions are unchanged V8 authoring units, NOT calibrated game metres.
Lengths below are hull station lengths; protruding wheel/fittings add to bounds.

| Module | Length | Triangles, including its fittings |
| --- | ---: | ---: |
| Stern_W1 | 9.30 | 17,210 |
| Midship_W1 | 6.00 | 4,006 |
| Bow_W1 | 8.65 | 8,738 |

| Assembly | Bays | Hull length | Total triangles |
| --- | ---: | ---: | ---: |
| Short | 0 | 17.95 | 28,300 |
| Long | 1 | 23.95 | 32,306 |

Assembly totals include the 1,616-triangle reinforced rotor, 260-triangle carrier,
and 476-triangle chimney. These are review/primary-ship meshes, not mobile LODs.
No iPhone GPU, frame-time, batching or draw-call measurements have been made.
The long ship adds 4,006 triangles, not a separately duplicated full hull design.

## Interface Contract

- Family W1: 9.28 deck beam at the shared section, deck Z=1.76, keel Z=-1.92.
  The curved bilge is slightly wider than the deck; 9.28 is not maximum bounds.
- Blender +X bow, +Y port, +Z up. No non-uniform scaling.
- Every module origin is its aft plane at the ship height datum, not the keel.
- Its forward socket is `(length, 0, 0)`.
- Module meshes use this same local origin, including housing and helm fittings.
- Inserting a middle bay translates everything ahead by 6.00 units.
- Source connection stations are X=-2.25 and X=3.75. They sit between posts,
  caps and straps rather than cutting decorative fittings in half.
- The middle region has a deliberately constant section. Bow/stern retain their
  shaped silhouettes; their central approaches were refitted to this standard.
- Hull, rail, longitudinal iron band and plank seam interface loops match.

Connected ends are intentionally OPEN, with no coincident hidden end caps.
They are not standalone watertight boats. At matching sockets, the assembled
surfaces close; an optional combined-mesh export can weld the coincident loops.
Do not render a module with an exposed connection as a complete vessel, and do
not use an individual open module as a watertight buoyancy volume.

## Assembly

Short: Stern at X=0, Bow at X=9.30.

Long: Stern at X=0, Middle at X=9.30, Bow at X=15.30.

For N bays, place each middle at `9.30 + 6*i`, then bow at `9.30 + 6*N`.
Zero through three middle bays have geometry assembly tests. Only zero and one
are presented as designed review configurations; longer ships need proportion,
performance and gameplay evaluation.

The unused middle module is hidden in the short Blender review scene. It is not
included in the short render or triangle count, but remains available to inspect.
The exploded library offsets pieces for presentation only; use `manifest.json`
for real socket positions, not the exploded object translations.

## Wheels, Fittings and Exports

`models/` contains 19 separate vertex-colored FBXs grouped by module and fittings.
Each module's parts are placed together at its module origin.

- The stern accepts the previous M1 standard timber or reinforced rotor.
- Wheel socket is stern-local Blender `(0.72, 0, 0.35)`.
- Rotor and Carrier FBXs are axle-local. Both use that socket; rotate only Rotor
  about Blender Y, or game X. The files are static; rotation is not baked into FBX.
- M1-L oversized housing is NOT supplied as a W1 stern module in this pass.
  Never insert the oversized rotor into this standard housing.
- Chimney pivot is its base centre. Place at `(hull_length/2, 0, 1.76)`.
- Provisional deck slots are empties in the blend and records in `manifest.json`.
  They are not yet validated for cannon clearance, recoil, passage or railing ports.
- FBXs retain the V8 axis convention: Blender position `(x,y,z)` maps to game
  `(-y,z,x)`, with +Z forward and +Y up in game. The export verifier accounts for
  the Blender FBX importer's additional axis transform.
- `Col` stores the existing vertex palette. Renders use Workbench, not Unity's
  material pipeline; don't replace the game's shader based on these previews.

## Verification

`validation.json`: per-mesh topology/budgets, matching interface point sets,
closed welded assemblies, and repeated-bay tests.

`export-verification.json`: independently reopened short/long scenes, identical
module mesh coordinates, all FBX triangle/vertex counts, bounds, vertex colors,
zero mesh export origins, and welded assemblies reconstructed from FBX for 0-3 bays.

The rotor is tested against visible fixed meshes at 72 angles per saved assembly.
Carrier contact inside radius 0.55 is excluded for intentional bearing contact.
These are sampled surface-intersection checks, not continuous collision proofs
or a buoyancy simulation. No Unity integration, colliders, crew navigation, ship
builder UI, combat placement, stability, performance balancing or LODs included.

## Reproduce

From the SeaSick project root with Blender 5.1.1:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_hull_family_v1.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_hull_family_v1.py
```

Outputs remain in this staging directory. No Unity project files are required.

## Next

Review these proportions before creating a wider/deeper family. Different beam
families need their own matching ends or authored transition modules, not stretched
W1 parts. Then fit partial deck modules and real cannon envelopes to approved hulls.
