# Modular Hull Family V2: Simpler End Curves

24 September 2026. Correction to V1 after review of the crowded stern and bow
geometry. V1 renders, models and Blender scenes are preserved for comparison.
No Unity assets, scenes, gameplay or project settings were changed.

## Changes

- 16 deliberate lengthwise stations replace the densely subdivided old profile.
  The extremely close stations at the stern are gone.
- A simple monotonic stern taper replaces the quarter-ellipse's tight end turn.
- The aft keel no longer kicks upward immediately before the transom.
- The side rail is level aft: no extra dip and rise behind the working deck.
- The bow uses a single quadratic taper and coordinated deck/keel rise, without
  intermediate control-point shoulders.
- The hull section has straight upper sides, removing the subtle outward/inward
  reversal. Flat shading is retained.
- Rail edge highlights use an explicit eight-point chamfered section, not an
  automatic bevel modifier on the whole mesh.
- Unnecessary automatic bevels on compound trim/fittings and the shell are gone.
- Deck cross divisions reduced from 32 to 8. Plank width remains unchanged;
  planks are represented by the separate seam geometry, not dense deck loops.
- Wheel, axle placement, hull lengths, module lengths and deck height retained.

This is a shape and construction revision, not a decimation pass over the old
mesh. A few closely spaced stations remain at the functional wheel-pocket and
housing boundaries, where their positions define real geometry.

## Counts

Counts include all visible meshes in the review assemblies, including fittings.

| Assembly | V1 triangles | V2 triangles | Reduction |
| --- | ---: | ---: | ---: |
| Short | 28,300 | 8,672 | 69.4% |
| Long | 32,306 | 9,634 | 70.2% |

| V2 module | Shell triangles | All module parts |
| --- | ---: | ---: |
| Stern | 866 | 4,502 |
| Middle | 114 | 962 |
| Bow | 428 | 2,138 |

Independent wheel/carrier/chimney fittings account for the remaining assembly
triangles. These numbers exclude the temporary wireframe overlay used solely
for `topology.png`. Geometry reduction is not a measured iOS performance claim.

## Review Files

- `stern-detail.png` and `bow-detail.png`: close views of the corrected ends.
- `topology.png`: actual hull mesh edges, not a generated concept image.
- `short/hero.png`, `long/hero.png`: whole-ship comparison.
- Both assembly folders also contain `side.png`, `top.png` and `ship.blend`.
- `module-library.blend`: exploded module view; `exploded.png` is its render.
- `models/`: 19 separate vertex-colored FBXs.
- `manifest.json`: pivots, placements, module lengths and sockets.
- `validation.json`, `export-verification.json`, `comparison.json`: measured checks.

## Compatibility and Assembly

The revised join profile is **W1-r2**. Use all hull modules from this V2 directory;
do not mix its bow/middle/stern meshes with V1, despite unchanged dimensions and
folder labels. Their cross-sectional contours are different.

Dimensions remain V8 authoring units, not calibrated game metres.
Blender axes: +X bow, +Y port, +Z up.
Every hull module's mesh origin is its aft connection plane at the ship datum.

- Stern length 9.30, middle length 6.00, bow length 8.65.
- Short: stern X=0, bow X=9.30; hull length 17.95.
- Long: stern X=0, middle X=9.30, bow X=15.30; hull length 23.95.
- For N middle bays: middle i at X=9.30+6*i, bow at X=9.30+6*N.
- Deck at the interfaces is Z=1.76, keel Z=-1.92, deck beam 9.28.
- Connection ends are intentionally open without overlapping end caps. Matching
  assembled loops close and can be welded. A single module is not a watertight boat.
- The middle is hidden, not deleted, in the short review blend.
- Exploded scene translations are presentation-only; use the manifest to assemble.

The M1 timber/reinforced wheels remain compatible; supplied rotor is reinforced.
Rotor and Carrier FBXs are axle-local; place at stern-local `(0.72,0,0.35)`.
Rotate Rotor only, about Blender Y / game X. No wheel animation is baked into FBX.
The oversized M1-L rotor still requires its dedicated housing and is not approved
for this standard stern. Chimney pivot is its base centre, placed at
`(hull_length/2,0,1.76)`. Helm is included at the stern module origin.

Exports follow the V8 convention: Blender position `(x,y,z)` maps to game
`(-y,z,x)`. Module components share their module-local origin. Socket empties
are in Blender and the manifest, not the individual mesh FBXs.
Vertex palette is stored in `Col`; Workbench previews are not Unity lighting.
Deck-slot empties remain provisional, without cannon, crew or recoil validation.

## Verification

- Matching interface loops across all three revised modules.
- No open edges, overconnected edges or degenerate faces after welding the
  corresponding assembled shell/rail/band/seam meshes.
- Identical module geometry in the short and long saved scenes.
- All 19 FBX round trips preserve vertex/triangle counts, bounds, color attribute
  and zero export origins.
- FBX assemblies tested with zero, one, two and three middle bays.
- Both ships' wheels tested at 72 rotation angles against visible fixed meshes.
  Intentional bearing contact inside radius 0.55 is excluded. Sampled surface
  checks are not continuous collision or containment proofs.
- No Unity, iPhone, collision-volume, navigation or buoyancy testing performed.

## Reproduce

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_hull_family_v2.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_hull_family_v1.py -- v2
```

The V1 generator now exposes a geometry configuration hook; its default geometry
is unchanged. V2 uses the shared splitting, assembly and export machinery with
new curves and fittings. Re-running V1 still writes only to its own V1 folder.
