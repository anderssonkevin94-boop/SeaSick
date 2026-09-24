# Modular Raised Foredeck V1

Offline asset prototype, 24 September 2026. Unity, gameplay, existing hull
exports and saves were not edited. Review before integration.

## Design

A tapered forward platform follows the approved V3 bow, with an open lower
deck, centreline stairs, teal guards and timber uprights with short knees.
The upper side rails have two genuine gun openings. The aft guard leaves a
central stair entrance. No rail crosses that entrance.

Two independently fitted widths are provided:

| Variant | Required hull | Added triangles |
| --- | --- | ---: |
| narrow | W1-r2, V3 pointed bow | 852 |
| wide | W2-r1, wide V1 pointed bow | 876 |

Counts exclude hull and preview cannons. Both use the existing palette,
flat shading and 0.70 plank-seam pitch. No blanket subdivision or smoothing.
Each variant has identical local geometry on short and long hulls.

## Files

Each width folder contains:

- `models/`: five FBXs (Deck, Frame, Rails, Stairs, Seams).
- `manifest.json`: bow-local outline, part files, sockets and stair dimensions.
- `validation.json`: topology counts and static fit checks for both lengths.
- `short/ship.blend`, `long/ship.blend`: assembled scenes with preview guns hidden.
- `short/fit-review.blend`, `long/fit-review.blend`: actual existing guns visible.
- Each assembly has hero, clean, top, side and foredeck-detail PNG renders.

The root `export-verification.json` records the ten FBX round trips.
Preview renders use Blender Workbench, not game shaders or lighting.

## Attachment Contract

Units are the unchanged V8 authoring units, not calibrated game metres.
Blender axes: +X bow, +Y port, +Z up. Existing hull FBX convention maps position
`(x,y,z)` to game `(-y,z,x)`.

Every part has the SAME origin as its matching bow module: aft interface at
the ship's height datum, not the platform centre or walking surface. Parent
under that bow with identity local transform. Do not apply the bow offset
twice. Geometry is not scaled when adding or removing middle bays.

The upper walking surface is at bow-local Z=4.20. The stairs have eight treads,
1.32 clear tread width and a 2.88 longitudinal run. Stair endpoint sockets and
two cannon sockets are in each manifest. Cannon sockets face outboard;
their Blender Euler rotations must be converted along with the coordinates.
Blender socket empties are not included in the five mesh-only FBXs.

The existing unscaled cannon asset is used only for fit previews. Do not
import those duplicates as part of this add-on. Instantiate the existing
cannon prefab at the provisional sockets instead.

Reserve the stair footprint, approach from the main deck and central upper
landing against equipment placement. The compact hull's chimney is close to
the stair approach; it requires a crew-capsule/navigation test before release.
Under-platform space is not certified for crew headroom or weapon placement.
Reject stacking a second foredeck on the same bow. These decks do not fit the
older blunt bow or the other width family through object scaling.

## Verification and Limits

All five part meshes pass boundary-edge, overconnected-edge and degenerate-face
checks. Components are fitted solids, not a single boolean-unioned object;
structural joints intentionally intersect. Export verification checks triangle
and vertex counts, bounds, zero local export origins, `Col` vertex colors and
topology after reimport. Saved meshes match between short and long assemblies.

On all four assembled configurations, static surface tests detect no
stair/chimney intersections, and no cannon intersections with the add-on
rails, frame or stairs. Wheels-on-deck contact is excluded from this check.
These are static surface tests, not a containment or swept-volume proof.

Still required: crew movement and headroom tests, weapon recoil and aiming
envelopes, gameplay occupancy rules, collision meshes, nav links, stability
and buoyancy tuning, Unity materials and iPhone performance measurements.

## Reproduce

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_foredeck_v1.py -- narrow
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_foredeck_v1.py -- wide
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/verify_modular_foredeck_v1.py
```
