# Hornbeam Resource Tree V1

First 3D interpretation of the selected narrow hornbeam concept. Staged for visual approval, not imported into the game. It replaces neither the old trees nor the rejected B1 study automatically.

## Files

- Hornbeam.fbx: 6660 triangles, separate Hornbeam_Wood and Hornbeam_Canopy mesh objects.
- Hornbeam_Stump.fbx: 238 triangles, clipped from the same trunk and roots with a capped cut surface.
- hornbeam-front.png, hornbeam-back.png, game-angle.png, grove.png and stump.png: actual Blender renders, not generated concepts.
- ../../tools/blender/source/hornbeam-astra-v1.blend: saved visible single-tree review; stump and grove copies are hidden.
- ../../tools/blender/hornbeam_astra_v1.py: generator.
- validation.json and export-verification.json: measured bounds and round-trip checks.

## Scale and Placement

Meters, ground-centered root at (0,0,0), Blender Z up. Height above ground 7.35m; crown envelope approximately 2.65 x 2.77m. Roots extend about 0.083m below ground. Stump cut surface is 0.38m above ground. Use the same placement transform and uniform scale for tree and stump so their root footprints match.

Export is -Z forward/Y up; preserve imported transforms and explicitly map Blender marker coordinates to the game. Do not assume screenshots are gameplay scale approval.

## Harvesting Contract

- Hornbeam__Ground: (0,0,0), resource placement.
- Hornbeam__Chop_Target: (0,-0.19,0.95), visual axe contact reference, NOT the worker's navigation destination.
- Hornbeam__Fell_Pivot: (0,0,0.38), suggested cut-height pivot reference, NOT a rig or animation.

Keep the lower trunk reachable and assign a separate narrow trunk collider rather than using the entire crown bounds as an obstacle. Approach direction, worker spacing, tool animation, health, wood yield and regrowth remain game-owned. Mesh/leaf counts must not determine resource yield.

On completion, replace the standing tree with the stump at the same transform. No partial-chop or damaged intermediate states are included. Wood and canopy are separate for material control and hide/fade behavior. They are not rigged and have no baked falling or wind animation. A true cut-height falling animation needs an upper-trunk cut mesh or clipping treatment: rotating the full standing trunk around the marker alone also moves its roots. Do not claim that behavior is already implemented.

## Materials and Performance

Flat normals and Col vertex colors, no textures or alpha-tested cards. Opaque closed low-poly leaf fans overlap a small inner crown; these are deliberately overlapping foliage elements, not a single watertight union. Wood branch/root junctions are welded. Use the game's vertex-color shader and preserve hard faceting.

This is one visual variant with one detail level. The grove preview uses rotated/scaled instances of that same mesh, not five unique trees. No LOD meshes, impostors, wind weights, billboards, colliders or Unity prefabs are supplied. Dense mobile forests still require LOD/instancing/culling and on-device profiling before production deployment. The palette should be evaluated under the actual game's daytime/nighttime shaders.

Both FBXs passed reimport checks for manifold edges, nonzero face area, flat normals and Col vertex colors. Standing-tree marker presence was checked. Checks apply to individual closed mesh components; they do not imply all intersecting foliage was Boolean-unioned. No in-game harvesting, animation or performance tests were run.
