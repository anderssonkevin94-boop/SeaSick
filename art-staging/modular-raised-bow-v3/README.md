# Raised Bow V3: Starboard Access and Continuous Ironwork

Offline Blender revision. Unity remains untouched; V2 is preserved.

## Changes

- Door centre moves to starboard (Blender Y=-2.15), the right side when looking
  forward from the lower deck. The access chamber, ladder and hatch move with it.
- The existing port and starboard lower hull straps continue to the upper deck.
  Their original end caps are removed and the same mesh is extended, preserving
  the lower width, alignment and material. Four matching upper bolts are added.
- Door, hatch, socket and hinge placements are updated in each manifest.

## Handoff

Use the matching `narrow` (W1-r2) or `wide` (W2-r1) package as a whole replacement
bow at its original module transform, not an overlay. Each includes 14 FBXs,
short/long Blender review scenes, PNGs, manifest and validation report.

Static meshes use the bow origin. Door and Hatch mesh files use their hinge
origins; apply the `moving_parts` offsets before rotating. Zero degrees closes
them. Coordinates remain Blender +X forward, +Y port, +Z up; game positions
map to (-y,z,x). V8 authoring units are not calibrated game metres.

The prior V2 README explains the access chamber, removable RearGuard, chimney
placement rule and limitations. Those rules still apply, except that access
now sits at Y=-2.15. Do not reuse V2 access socket or pivot positions.

## Checks

Both assembled lengths retain a closed welded hull shell. Parts are checked
for overconnected edges and degenerate faces. The door/chimney surface test
samples 23 angles from 0 through 110 degrees on both assemblies. No contacts
were detected. This is a sampled static test, not continuous collision proof.

`export-verification.json` verifies FBX counts, bounds, colors, topology and
shared module-local geometry across the two lengths. Crew capsule fit,
navigation, weapon/recoil clearance and iPhone performance remain untested.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_raised_bow_v3.py -- narrow
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_raised_bow_v3.py -- wide
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_raised_bow_v1.py -- v3
```
