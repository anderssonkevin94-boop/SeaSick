# Island Foreland Study

This is a Blender-only composition prototype for a home island with a genuinely flat grassy foreland, broad beaches, and a sheltered southern inlet. It does not export or modify Unity assets.

The study uses a separate scene and source file, `tools/blender/source/island-foreland-study.blend`. The foreland is planar at **3.2 m**. The approved mountain surface is loaded as a linked study input and translated **25 m rearward** and to the 3.2 m foreland datum (`mountain.location = (0, 25, 3.2)`). The prototype shoreline spans approximately `x=-120..121`, `y=-103..130`; its southern inlet opens through the foreground, with turf kept inside the beach ring. The coastline is variable-width and smoothly interpolated: broad sand pockets shape the south and west, while narrow strands remain on the exposed north. Beach geometry has a broad, nearly flat dry band from **1.5–1.65 m** across the middle 50% of the shore width, a smooth upper bank across the upper 25%, and a lower 25% approach descending to the waterline at `-0.18 m`, over a sea plane at `0 m`. A subtle wet-sand tint follows the lower beach edge.

Review the three renders produced by the script:

- `island-foreland-overview.png` — overall island composition and mountain/foreland relationship
- `island-foreland-harbour.png` — foreground inlet, beach breadth, and sheltered approach read
- `island-foreland-plan.png` — shoreline, turf footprint, and inlet layout from above

The prototype footprint intentionally differs from the current runtime island and is only a visual direction study. Harbour navigability, berth clearance, water depth, collision, traversal, and runtime terrain integration are not validated here. Existing home-island and mountain findings inform later integration decisions; this study leaves Unity and gameplay data unchanged.

The current plateau phase keeps the mountain as one connected surface, with a lowered central approach, broader low shelves to the left and right, and a distinct upper bench. Chamfered lower and middle loop corners, together with uneven shoulder elevations, break broad walls into secondary inclined rock planes while preserving one continuous surface. The crown has an upper bevel, preserving a broad, natural summit while avoiding a mechanically flat top. These are art-direction geometry goals for the silhouette study. Game traversal, collision, harvesting, and runtime terrain behavior remain unvalidated.

Geometry review: the revised mountain source contains 217 vertices and 408 triangles. Blender geometry QA passed: zero negative or zero projected-area faces, minimum projected area 0.1974756746, 24 boundary edges matching the expected outer foot, and zero non-manifold edges. Concave quads behind the summit use an inward diagonal to prevent folded faces. Upper terrain carries an explicit `TerrainZone` face-domain tag for grass/rock assignment, while grass uses smooth normals and rock remains flat-faced. These source-mesh checks are separate from Unity validation, which has not been run.
