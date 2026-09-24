# Hull station tables: handoff to Claude

2026-09-24. Offline art export only; no Unity or backend files changed.

## Deliveries

Each module entry in these manifests now has a `hydrostatics` object with a
relative JSON file path, source-geometry SHA256, valid waterline range and counts:

- `art-staging/modular-hull-family-v3/manifest.json`: W1-r2 stern, middle, bow.
- `art-staging/modular-hull-wide-v1/manifest.json`: wide-family equivalents.

The wide data is for future integration, not permission to expose those modules.
No raised-section tables are included. Keep those disabled pending clearance fixes.

## Contract (schemaVersion 1)

All coordinates are Blender/module-local: +X bow, +Y port, +Z up.
The mesh object's parent placement is deliberately ignored. Tables measure only
`Hull_Shell`, excluding railings, iron trim, decorative prow, wheel/carrier,
chimney and equipment. Wheel-pocket recesses in the shell remain in the geometry.

- `waterlineZU[]`: ascending absolute heights above the module's height datum,
  NOT draft above keel and NOT world sea elevation.
- `stations[]`: ascending `xU` with `areaU2[]`, one immersed transverse area
  for each waterline. Areas include the whole port-to-starboard section, not half.
- `hullXRangeU`: actual shell extent. Do not extend the table to the decorative
  prow's socket/overall length or count that empty tail as buoyant hull.
- `validWaterlineZU`: keel to the family main-deck height. Do not extrapolate
  above the deck into presumed watertight upper volume.
- `integratedVolumeU3[]`: precomputed trapezoidal X integration for each height.
  Use as a fast upright lookup and reference check, not as mass.
- `sourceGeometrySha256`: provenance hash of local triangulated positions.
- `verification`: sampling settings, convergence result and analytic checks.

For scale k metres per authoring unit: x and height multiply by k, area by k^2,
volume by k^3. At the chosen k=0.5 these factors are 0.5, 0.25 and 0.125.
Sum installed module volumes at the same ship-local waterline. Solve for the
waterline against total mass and the backend's water density. Convert the result
to draft relative to keel. Carry section vertical offsets into the lookup if
future modules no longer share the same height datum.

At a height between samples, linear interpolation is approximate. Below keel,
volume is zero. Above the valid maximum, report overloading/downflooding or use a
deliberate flooding model; clamping must not silently validate an overloaded ship.
Unsolvable loads require a blocking reason.

## Scope and accuracy

The mesh-plane cross-sectional area is computed with directed segments and
Green's theorem, clipping at the queried waterline. This preserves concave
sections and cavities without guessing a convex envelope. Stations include all
mesh X breakpoints sampled just either side, plus <=0.0625 U regular spacing.
End planes are sampled 0.000001 U inside the actual shell; their negligible end
slivers are omitted. Reported shell bounds may differ from nominal join positions
by mesh floating-point tolerances; do not use them to place modules.

Checks: analytic box and triangular-prism partial immersion; closed-section
directed flux; nonnegative, monotonic area; X integration compared at maximum
spacing 0.125 versus 0.0625 U. Export fails at >0.2% convergence difference
(denominator floor is 1% of the module's maximum table volume near the keel).
This is a convergence check, not a claim of exact global error or naval validation.

These tables support upright, level flotation only. They are NOT enclosed cargo
volume, usable accommodation, material volume/mass, safe carrying allowance,
free-surface corrections, or a heel/trim stability model. Capacity contributions
remain separately authored backend data. Retain provisional flags on tuning.

## Regeneration

`tools/blender/export_hull_hydrostatics.py -- <asset-folder>` reads that folder's
`long/ship.blend`, writes its hydrostatics/*.json and augments its manifest.
The common hull-family generator now also attaches these tables on regeneration.
No mesh faces are added, no FBXs are changed, and no Unity import is needed.

## UI response to the backend handoff

The staged UI no longer infers live section occupancy. Bind its `removalBlocker`
delegate to the per-section result from `Report(draft)`. Missing live binding
refuses removal. The UI accepts defensive copies and commits with an expected
baseline. It closes after successful replacement and caches no live ship object.

Please finish `docs/SHIPYARD-API.md` with the concrete report/event signatures and
UI-reader inventory. Then Astra can wire provisional report values, existing UI
rebindings on `PlayerShipReplaced`, and `ShipyardSession` input gates without
guessing your still-in-progress API. Neither those hooks nor the backend's atomic
save/rollback behavior have been verified by Astra yet.
