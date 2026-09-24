# Full-Length Raised Bow V2

Offline design revision, 24 September 2026. Unity untouched. Earlier foredeck
and raised-bow versions are preserved for comparison.

## Shape and Access

The entire bow section has an upper deck at Z=4.20. Its rear bulkhead sits
0.04 authoring units forward of the module boundary, retaining the original
lower hull join. There is no exterior staircase or stilt-supported platform.

A real doorway at the lower deck connects to a small lined chamber, internal
ladder and real hatch opening. Door and hatch are separate hinged meshes,
shown open for review. The interior is an access chamber, not a furnished or
fully navigable lower storey. Side rail openings remain available for weapons;
cannon fit on this expanded deck has not yet been revalidated.

Both narrow W1-r2 and wide W2-r1 versions include short and long assembled
Blender scenes, isolated-section views, access detail, top, side and hero views.

## Integration Contract

- Replace the entire matching low bow. Do not overlay either earlier foredeck.
- All static FBXs share the original bow-local origin. Parent at identity.
- Door and Hatch FBXs are hinge-local instead. Apply their `moving_parts`
  pivot offsets from the matching manifest; zero rotation is the closed pose.
- Blender door hinge axis is Z; hatch hinge axis is Y. Review angles are 65
  and 80 degrees respectively. These are poses, not authored animation clips.
- Blender +X is bow, +Y port, +Z up. Existing export position mapping to game
  coordinates is `(-y,z,x)`. Convert rotation axes consistently.
- Units remain V8 authoring units, not calibrated game metres.
- `DoorEntry`, `HatchExit` and `UpperDeckJoin` are declared in each manifest.
  Door-to-hatch traversal is a proposed gameplay link, not implemented AI.
- `RearGuard` is a separate removable part. Remove it when an adjoining raised
  deck matches this height. No raised middle module is delivered here yet;
  its interface and bulkhead treatment still need designing and verification.
- Do not scale the narrow model to produce the wide version.
- The independent chimney is moved aft on the short assembly so it does not
  pierce the newly extended upper deck. Placement rule is in the manifest.

## Files and Checks

Each family contains 13 mesh FBXs, `manifest.json`, `validation.json`,
`short/ship.blend`, `long/ship.blend`, and PNG review renders. The root
`export-verification.json` records FBX round-trip checks for both families.

The generator checks no overconnected edges or degenerate faces in the parts.
The combined hull shell welds closed on both ship lengths, including the
door/hatch chamber lining. Individual hull parts deliberately retain open aft
join loops. The chimney/raised-shell static surface test passes.

The export verifier checks geometry counts, local bounds, vertex colors,
topology and identical module-local geometry on both assembled ship lengths.
Separate timber/iron fittings intentionally meet or overlap at construction
joints. Portal-frame clearances, door/hatch swept volumes, crew capsule fit,
weapon recoil, navigation, colliders, buoyancy and iOS performance still need
gameplay testing. Preview lighting is Blender Workbench, not Unity.

## Reproduce

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_raised_bow_v2.py -- narrow
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_raised_bow_v2.py -- wide
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_raised_bow_v1.py -- v2
```
