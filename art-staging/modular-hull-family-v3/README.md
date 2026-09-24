# V3 Bow: Raked Stem and Carved Prow

Offline Blender revision, 24 September 2026. Unity remains untouched.
V2 is preserved in `../modular-hull-family-v2/`.

## Design

The upper bow projects forward while the lower stem retreats, replacing the
nearly vertical nose with a sharper raked silhouette. The forward hull narrows
slightly. Hull, deck, rails, posts, seams and bands follow the same deformation.
No extra subdivisions were added to the existing bow.

A short carved timber prow continues the stem above the rail. It has a teal
upper accent, fitted dark iron tip, and a small brass diamond on each side.
The wooden tip sits inside the iron shoe without coincident exposed cap faces.
This is one optional 60-triangle mesh, separate from the main hull module.
It consists of fitted solids, not a single boolean-unioned physical solid.

The stern, middle bay, wheel, and module connection geometry are unchanged from
V2. This bow remains compatible with the W1-r2 interface. V1's older W1 hull
profile is not compatible. No gameplay integration or balance is included.

## Review

- `side.png`: straight-on short-ship silhouette.
- `bow-detail.png`: three-quarter detail of the new stem and decorative head.
- `top.png`, `front.png`: orthographic short-ship views.
- `short/ship.blend`, `long/ship.blend`: assembled Blender scenes.
- Both assembly folders contain hero, side and top images.
- `module-library.blend`, `exploded.png`: separated module presentation.
- `manifest.json`: authoritative module lengths, pivots and placement.
- `models/`: 20 individual vertex-colored FBXs, including `Bow_W1/Prow.fbx`.
- `validation.json` and `export-verification.json`: geometry and export checks.

## Budget

| Assembly | V2 | V3 |
| --- | ---: | ---: |
| Short | 8,672 triangles | 8,732 triangles |
| Long | 9,634 triangles | 9,694 triangles |

The difference is the 60-triangle prow. Geometry remains flat shaded.
These are model counts, not measured iPhone performance results.

## Coordinates and Pivots

Unchanged V8 authoring units, not calibrated game metres. Blender: +X bow,
+Y port, +Z up. Existing FBX conversion maps Blender `(x,y,z)` to game `(-y,z,x)`.
Each module's mesh origin is its aft interface at the ship height datum.

- Stern: length 9.30, placed at X=0.
- Middle: length 6.00; first bay at X=9.30, subsequent bays every 6.00.
- Bow: aft socket at local zero; length including decorative tip approximately
  10.98. Place at X=9.30 for short, X=15.30 for long.
- Total assembly length to decorative tip: approximately 20.28 / 26.28.
- Prow FBX shares the BOW module origin, not the tip or hull-root origin.
- Rotor and Carrier remain axle-local at stern socket `(0.72,0,0.35)`.
  Rotate Rotor only, about Blender Y / game X. M1 wheel sizes only.
- Chimney has a base-centre pivot and follows the assembled ship midpoint at
  Z=1.76. Its mesh is unchanged.
- Deck-slot empties remain provisional, not verified cannon or crew placements.

Assembly connection faces remain deliberately open; matching module loops close
when assembled and welded. No overlapping connection caps are supplied.
The exploded library is a presentation, not valid assembly placement.
Vertex color palette is in `Col`. Preview lighting is Blender Workbench, not Unity.

## Validation and Limits

The generator checks matching loops and closed welded assemblies with 0-3 bays.
The new prow mesh has no boundary edges, overconnected edges or degenerate faces.
The independent verifier checks saved-scene geometry, FBX counts/bounds/colors,
export pivots, and reassembled imported module geometry. Wheel clearance is
sampled at 72 rotation angles per ship, excluding intentional bearing contact
inside radius 0.55. This is not a continuous collision or containment proof.

No colliders, navigation, buoyancy, cannon recoil, Unity materials or iPhone
performance tests are included. Previous assets and game saves are unchanged.

## Reproduce

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_hull_family_v3.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_hull_family_v1.py -- v3
```

The V3 script extends V2 and the shared V1 assembly tools. Outputs remain here.
