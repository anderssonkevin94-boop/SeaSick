# V7 construction corrections

Blender-only revision. Unity and all game assets remain untouched.

The V6 transom used a uniform height offset while the hull used a weighted offset by strake. This produced visible open seams at the rear corners. V7 derives all rear perimeter vertices directly from the hull and cabin boundaries (20 matched vertices, checked before deformation). Both sides receive the same shaping function.

Removed the added stern beam framework, bolts and plaque. Retained the curved rear shell, cabin windows, rudder and hull-following trim. Rear trim is now sampled along the matching hull rows.

All longitudinal hull trim curves, including the coral stripe, are clipped against the six lower gunport frame footprints. The generator checks that no retained trim segment midpoint lies inside an opening footprint. Close-up rear and gunport renders are included for inspection.

Cannon layout remains three upper and three lower guns per side. The approved sail direction and fair hull profile are retained. This is a design study, not an imported or gameplay-validated asset.
