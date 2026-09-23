# Low-Poly Forest Family V1

Six simple resource-tree shapes based on the approved 224-triangle hornbeam. Staged only; no Unity import, scene changes or procedural placement changes.

## Assets

The family-lineup.png image shows these in order from left to right:

| Tree FBX | Triangles | Shape |
| --- | --- | --- |
| Forest_Hornbeam.fbx | 224 | Approved narrow upright crown |
| Forest_Broadleaf.fbx | 224 | Lower, broader mature canopy |
| Forest_Slender.fbx | 176 | Taller and thinner silhouette |
| Forest_Leaning.fbx | 190 | Leaning trunk with offset crown |
| Forest_Uneven.fbx | 224 | Unequal shoulders and off-center top |
| Forest_Young.fbx | 160 | Smaller young tree with distinct proportions |

Each has a corresponding `<TreeName>_Stump.fbx`, 32 triangles. There are twelve FBXs total. These are visual variants, not new resource types or botanical simulations. All use one simple closed crown and a tapered trunk; no individual leaves, branches, alpha cards, textures or fine details.

## Placement and Harvesting

Meters. Each root is at ground center, Blender Z up, with 0.06m shallow embed. Tree dimensions vary intentionally; consult validation.json for measured bounds rather than forcing all trees to one size. Apply the SAME transform and uniform scale to the matching stump. Stump cut height is local Z=0.38m.

Each tree includes unique `<TreeName>__Ground`, `__Chop_Target` and `__Fell_Pivot` empties. The chop target is an axe-contact reference on the lower trunk, not a worker navigation destination. Fell_Pivot is a reference at (0,0,0.38), not a rig or animation. Use narrow trunk colliders and keep the lower trunk accessible to workers.

The FBX convention is -Z forward/Y up. Map imported axes deliberately. Resource yield, HP, growth, depletion and regrowth remain game-owned; visual crown size and mesh counts must not determine yield automatically.

These assets support standing-tree to stump replacement. No intermediate chopping meshes, baked wind/fall animation, rig, upper-trunk cut variant, colliders, navmesh or gameplay code is supplied. Rotating the full standing tree around Fell_Pivot would also rotate its roots; a proper cut-height falling state needs additional setup.

## Natural-Looking Distribution

Use local groups with one or two dominant shapes rather than distributing all six uniformly. Favor hornbeam/broadleaf as the core mix, use slender trees to break the skyline, and place young trees near clearings and forest edges. Use leaning and uneven forms sparingly. Rotate around the vertical axis and apply modest uniform scale variation (the review uses roughly 0.85-1.05). Leave irregular gaps and reachable harvesting approaches; do not arrange trees on a visible grid. Coordinate lean direction locally on exposed shores if desired.

The mixed-forest.png image is a twelve-tree review using these six real meshes. Placement suggestions are art direction only, not a implemented spawning algorithm.

## Mobile Rendering

Flat normals and Col vertex colors. Source meshes share Forest_Shared_VertexColor; assign one shared compatible game material across imports rather than accepting separate duplicated FBX materials. Trunk and canopy remain separate mesh objects for authoring; the integration pipeline can combine them for rendering when independent control is unnecessary. Reuse mesh assets and apply appropriate instancing/batching, culling and shadow-distance limits.

Each standing tree is under 250 triangles. This is not a device frame-rate guarantee: draw calls, visible instance count and shadows still need iOS profiling. No LOD/billboard assets are included. Do not reintroduce per-leaf geometry into this forest family.

## Files and Verification

- Blender: ../../tools/blender/source/forest-lowpoly-astra-v1.blend
- Generator: ../../tools/blender/forest_lowpoly_astra_v1.py
- family-lineup.png, mixed-forest.png, game-scale.png and one PNG per tree
- validation.json: triangle counts and bounds
- export-verification.json: FBX round-trip results

All twelve FBXs passed checks for manifold edges, nonzero face area, flat normals, vertex colors and matching triangle counts. Tree marker presence was checked. The saved Blender file opens on the mixed grove; individual source trees and stumps are hidden. Review copies/cameras are not exported. No Unity gameplay or on-device performance tests were run.
