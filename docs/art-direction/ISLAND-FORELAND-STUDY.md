# Island Foreland Study

This is a Blender-only composition prototype for a home island with a genuinely flat grassy foreland, broad beaches, and a sheltered southern inlet. It does not export or modify Unity assets.

The study uses a separate scene and source file, `tools/blender/source/island-foreland-study.blend`. The foreland is planar at **3.2 m**. The approved mountain surface is loaded as a linked study input and translated **25 m rearward** and to the 3.2 m foreland datum (`mountain.location = (0, 25, 3.2)`). The prototype shoreline spans approximately `x=-120..121`, `y=-103..130`; its southern inlet opens through the foreground, with turf kept inside the beach ring. The coastline is variable-width and smoothly interpolated: broad sand pockets shape the south and west, while narrow strands remain on the exposed north. Beach geometry has a broad, nearly flat dry band from **1.5–1.65 m** across the middle 50% of the shore width, a smooth upper bank across the upper 25%, and a lower 25% approach descending to the waterline at `-0.18 m`, over a sea plane at `0 m`. A subtle wet-sand tint follows the lower beach edge.

Review the three renders produced by the script:

- `island-foreland-overview.png` — overall island composition and mountain/foreland relationship
- `island-foreland-harbour.png` — foreground inlet, beach breadth, and sheltered approach read
- `island-foreland-plan.png` — shoreline, turf footprint, and inlet layout from above

The prototype footprint intentionally differs from the current runtime island and is only a visual direction study. Harbour navigability, berth clearance, water depth, collision, traversal, and runtime terrain integration are not validated here. Existing home-island and mountain findings inform later integration decisions; this study leaves Unity and gameplay data unchanged.
