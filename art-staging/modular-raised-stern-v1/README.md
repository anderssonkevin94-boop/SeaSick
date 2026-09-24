# Raised Stern V1

Offline asset prototype. Unity, gameplay code and existing assets untouched.

## Design

Full stern quarterdeck at height 4.20, supported by a continuous hull. The helm
is raised onto this deck. Lower iron straps continue upward with matching
bolts. A forward-facing offset door leads through an internal lined passage
to a centreline hatch and ladder. No external staircase or stilts.

The M1 paddle rotor, carrier and axle location remain unchanged. The previous
low housing roof is absorbed into the new shell; buried housing fittings are
removed. The wheel clearance pocket is preserved. This does not support the
oversized M1-L wheel.

## Packages

`narrow` matches W1-r2; `wide` matches W2-r1. Do not mix widths or scale one to
fit the other. Each includes 13 FBX meshes, manifest, validation, isolated
stern/access/back views, and two long-ship Blender scenes:

- `stern-only/ship.blend`: raised stern, low middle and low bow.
- `both-raised/ship.blend`: raised stern and V4 raised bow around a low middle.

The separate rotor and carrier are reused from the existing hull package;
they are shown in Blender but are not duplicate exports in this package.

## Coordinates and Pivots

Replace the complete original stern module, not an overlay. Static meshes
retain its original origin. Stern length is 9.30 authoring units; forward
connection remains at X=9.30. Blender +X bow, +Y port, +Z up. Game position
mapping remains (-y,z,x). These are V8 authoring units, not calibrated metres.

Door and Hatch FBXs instead use hinge-local origins. Apply the offsets and
axes in `moving_parts`. Their mirrored stern opening angles have the opposite
sign from the bow. Zero degrees is closed; review poses are not animations.

`ForwardGuard` is separate. Remove it only when a validated matching raised
middle deck is available; upper joining geometry and bulkhead treatment are
not yet implemented. Current review scenes use one low middle bay. Direct
raised-stern/raised-bow attachment without a middle bay is not certified.

`SternDoorEntry`, `SternHatchExit` and `SternUpperDeckJoin` are metadata sockets.
Crew traversal is a proposed link, not implemented navigation. The stern door
uses the ship's starboard side, even though it faces the opposite direction
from the bow doorway.

## Verification and Limits

Assembled hull shells weld closed with no overconnected edges in both review
configurations. Part meshes are checked for degenerate and overconnected
geometry. Individual hull/interface trim meshes intentionally retain aft or
forward open join loops. The wheel is checked against the hull at 72 angles,
five degrees apart; this is not a continuous collision proof.

`export-verification.json` checks both widths' exported counts, bounds, colors,
topology, zero export origins and identical local geometry in both assemblies.
No crew headroom, swept door/hatch collision, cannon/recoil, navigation,
collider, buoyancy, Unity lighting or iOS performance tests are included.

Rebuild `tools/blender/modular_raised_stern_v1.py -- narrow` and `-- wide` in
Blender after generating the V4 bow files. Verify with
`tools/blender/verify_modular_raised_stern_v1.py`.
