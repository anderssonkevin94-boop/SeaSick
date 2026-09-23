# Low-Poly Forest Family V4 - Palm Replacement

Fourteen simple resource-tree shapes based on the approved 224-triangle hornbeam. Staged only; no Unity import, scene changes or procedural placement changes.

## Assets

The family-lineup.png shows two rows of seven in the following order. new-additions.png shows Straight Palm, Leaning Palm, Tall Palm, Short Palm from left to right.

| Tree FBX | Triangles | Shape |
| --- | --- | --- |
| Forest_Hornbeam.fbx | 224 | Approved narrow upright crown |
| Forest_Broadleaf.fbx | 224 | Lower, broader mature canopy |
| Forest_Slender.fbx | 176 | Taller and thinner silhouette |
| Forest_Leaning.fbx | 190 | Leaning trunk with offset crown |
| Forest_Uneven.fbx | 224 | Unequal shoulders and off-center top |
| Forest_Young.fbx | 160 | Smaller young tree with distinct proportions |
| Forest_Pine.fbx | 176 | Narrow dark-green tiered skyline accent |
| Forest_Coastal.fbx | 190 | Leaning trunk and low wind-shaped crown |
| Forest_Oak.fbx | 204 | Squat broad crown and stout trunk |
| Forest_Birch.fbx | 176 | Pale trunk and slender light-green crown |
| Forest_PalmStraight.fbx | 236 | Upright trunk with seven broad folded fronds |
| Forest_PalmLeaning.fbx | 236 | Curved coastal trunk with seven fronds |
| Forest_PalmTall.fbx | 236 | Taller skyline palm with seven fronds |
| Forest_PalmShort.fbx | 212 | Short compact palm with six fronds |

Each has a corresponding `<TreeName>_Stump.fbx`, 32 triangles. There are twenty-eight FBXs total. These are visual variants, not new resource types or botanical simulations. The first ten use one simple closed crown and a tapered trunk. Palms use six or seven broad closed frond meshes joined into a canopy object, with no individual leaflets, alpha cards, textures or fine details. Fronds converge at the trunk top; there is no botanical leaf subdivision or fruit.

## Placement and Harvesting

Meters. Each root is at ground center, Blender Z up, with 0.06m shallow embed. Tree dimensions vary intentionally; consult validation.json for measured bounds rather than forcing all trees to one size. Apply the SAME transform and uniform scale to the matching stump. Stump cut height is local Z=0.38m.

Each tree includes unique `<TreeName>__Ground`, `__Chop_Target` and `__Fell_Pivot` empties. The chop target is an axe-contact reference on the lower trunk, not a worker navigation destination. Fell_Pivot is a reference at (0,0,0.38), not a rig or animation. Use narrow trunk colliders and keep the lower trunk accessible to workers.

The FBX convention is -Z forward/Y up. Map imported axes deliberately. Resource yield, HP, growth, depletion and regrowth remain game-owned; visual crown size and mesh counts must not determine yield automatically.

These assets support standing-tree to stump replacement. No intermediate chopping meshes, baked wind/fall animation, rig, upper-trunk cut variant, colliders, navmesh or gameplay code is supplied. Rotating the full standing tree around Fell_Pivot would also rotate its roots; a proper cut-height falling state needs additional setup.

## Expansion Notes

The ten approved V2 tree profiles are retained unchanged. This is the current combined handoff. The user rejected the V3 additions: Forest_MatureBeech, Forest_TallFir, Forest_Rowan, Forest_DwarfPine. None of those four or their stumps are included here. Do not import V3. Earlier files are retained only for history. Import V4 as the current family, not in addition to duplicated V1/V2 trees. Four additional authored silhouettes use the same flat-shaded style, material contract and harvest markers. The names describe stylized visual archetypes, not botanical fidelity.

Use palms in irregular coastal groups with space between crowns. Mix straight and leaning forms as the core, tall palms sparingly, and short palms at the edges. Avoid using every type equally in every grove. Palm crowns have a simple opaque thickness and can be viewed from below without requiring a two-sided material.

Use pine in small clusters on higher ground, oak at inland clearings, birch in lighter groups near forest edges, and coastal trees sparingly along exposed shores. Avoid equally mixing every type everywhere. The review grove is an art preview, not an in-game screenshot.

## Natural-Looking Distribution

Use local groups with one or two dominant shapes rather than distributing all fourteen uniformly. Favor hornbeam/broadleaf as the core mix, use slender trees to break the skyline, and place young trees near clearings and forest edges. Use leaning and uneven forms sparingly. Rotate around the vertical axis and apply modest uniform scale variation (the review uses roughly 0.85-1.05). Leave irregular gaps and reachable harvesting approaches; do not arrange trees on a visible grid. Coordinate lean direction locally on exposed shores if desired.

The mixed-forest.png image is a twelve-tree review using the expanded real meshes. Placement suggestions are art direction only, not an implemented spawning algorithm.

## Mobile Rendering

Flat normals and Col vertex colors. Source meshes share Forest_Shared_VertexColor; assign one shared compatible game material across imports rather than accepting separate duplicated FBX materials. Trunk and canopy remain separate mesh objects for authoring; the integration pipeline can combine them for rendering when independent control is unnecessary. Reuse mesh assets and apply appropriate instancing/batching, culling and shadow-distance limits.

Each standing tree is under 250 triangles. This is not a device frame-rate guarantee: draw calls, visible instance count and shadows still need iOS profiling. No LOD/billboard assets are included. Do not reintroduce per-leaf geometry into this forest family.

## Files and Verification

- Blender: ../../tools/blender/source/forest-lowpoly-astra-v4.blend
- Generator: ../../tools/blender/forest_lowpoly_astra_v4.py
- new-additions.png, family-lineup.png, mixed-forest.png, game-scale.png and one PNG per tree
- validation.json: triangle counts and bounds
- export-verification.json: FBX round-trip results

All twenty-eight FBXs passed checks for manifold edges, nonzero face area, flat normals, vertex colors and matching triangle counts. Tree marker presence was checked. The saved Blender file opens on the mixed grove; individual source trees and stumps are hidden. Review copies/cameras are not exported. No Unity gameplay or on-device performance tests were run.


