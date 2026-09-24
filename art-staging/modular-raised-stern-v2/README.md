# Raised Stern V2: Inset Twin Stairs

Offline Blender art review only. No Unity assets or scenes modified.
V1 remains available alongside this version.

## Geometry

- Narrow and wide variants, retaining the original 9.30-unit stern length.
- Two stair wells inside the existing hull outline. Eight steps each, ascending
  aft from the forward bulkhead. Rise 0.305, tread run 0.40, width 1.144.
- Upper deck Z 4.20; lower walking deck Z 1.76. Authoring units are not yet
  calibrated to game metres or validated against crew navigation dimensions.
- Original center hatch, offset door, extended iron braces and wheel retained.
- Each staircase is one closed stepped solid. Deck and bulkhead are cut away
  at the wells; forward guard and plank seams are trimmed at the openings.
- Separate TwinStairs, StairGuards and StairNosings meshes accompany the shell.

## Files and Placement

Each width folder includes FBX parts, manifest, validation, preview PNGs and
stern-only/ship.blend plus both-raised/ship.blend assembly reviews.
Static parts use the unchanged stern module origin. Blender +X points toward
the bow, +Y port, +Z up; export game mapping is (-y,z,x).
Door and hatch remain hinge-local; use manifest moving_parts pivots.
New stair top and bottom socket coordinates are recorded in each manifest.
The standard M1 wheel and carrier reuse the existing kit, not duplicate exports.

## Validation and Limits

Generator checks both assembled hull joins for boundary/nonmanifold edges,
checks part degeneracy, stair envelope, and wheel clearance at 72 rotations.
The FBX verifier checks round-trip topology, bounds and vertex colors, and
that the stern geometry is identical in the two review assemblies.
Crew traversal, colliders, animation and phone performance remain untested.
Same-height upper module joins still need dedicated joining geometry.

Regenerate with tools/blender/modular_raised_stern_v2.py -- narrow or -- wide.
Verify with tools/blender/verify_modular_raised_stern_v1.py -- v2.
