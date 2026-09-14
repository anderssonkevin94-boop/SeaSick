# Island rebuild — graphic adventure reference

Implemented in Sea.unity using Blender MCP. This replaces the former conifer/shard treatment with a standalone asset library, terraced landforms and clustered woodland. It follows the lower reference's shape language; it is not a pixel-exact reconstruction of the illustrative harbour.

## Assets

`Assets/_Project/Resources/IslandAssets/` contains 29 standalone prefabs, each backed by its own FBX in `Resources/Flora/Storybook/`:

| Family | Prefabs | Treatment |
| --- | --- | --- |
| Broadleaf | Broad, Broad_B, Broad_C, Broad_Young | Spreading oak, slender pale trunk, wind-shaped coastal oak and young growth |
| Conifer | Spruce, Spruce_B, Spruce_Young | Full, slender blue-green and smaller young conifers |
| Tropical | Palm | Regenerated from the existing palm builder |
| Boulder | Boulder_0–3 | Asymmetric blunt blocks with slanted tops |
| Cliff | Cliff_0–2 | Larger planar rock modules, no needle apex |
| Bush | Scrub_0–2 | Low overlapping leaf clusters |
| Groundcover | Fern, Fern_B, Grass, Grass_B, Grass_Dry | Broad fronds, upright and dry meadow tufts |
| Fallen wood | Sticks, Driftwood | Forked branches in bark and weathered wood tones |
| Ore | Ore | Distinct mineral inclusions |
| Crops | Crop_0–2 | Regenerated wheat from the existing resource builder |

The library has 43 meshes including paired LODs. Prefabs carry IslandAsset identity, shared vertex-colour materials, LOD groups, and trigger bounds for resource families. Generator: `tools/blender/storybook_islands.py`. Editable Blender scene: `tools/blender/source/storybook-islands.blend`. Blender exports linear vertex colours; exporting sRGB caused double conversion and visibly pale vegetation in Unity. The original Blender scene/file is preserved.

## Gameplay integration

Trees are individual prefab instances, each recorded by SceneryWood. Harvest nodes attach to nearby tree instances through the existing crew workflow. Harvesting deactivates/removes that tree, without editing shared mesh geometry. Stone, ore and other resource props instantiate the matching standalone prefab. Decorative groundcover and rock dressing may still be batched; every source remains a separate reusable asset. Island resource budgets retain the existing economy rules.

One island-level scheduler handles tree LOD/culling; the standalone prefab LODGroup is disabled for those runtime instances to avoid competing visibility systems. Prefabs used separately retain normal LODGroup behaviour. The terrain shader supports instanced transforms. Device GPU performance has not been certified.

## Landforms

- Home grows from a nominal 74 m radius to 110 m, with an irregular perimeter, a longer/wider cove mouth and two wooded terraces behind the usable lowlands.
- Ramps on opposite sides connect the terraces. The new silhouette uses the same height function for rendering, collision, shoreline and crew queries.
- Other islands gain broader stepped inland relief. Their existing waterline is preserved by gating the relief above the shore; the home shoreline is intentionally redesigned.
- New foliage colours and planar rock lighting carry through near terrain and resource assets. The original ship models and sailing simulation are unchanged.
- The larger shore exposed a berth-finder bug: depth was checked along the boat centreline only. HarbourSite now checks across its established 24.2 × 8.4 m footprint before accepting a berth.

## Checks

- Runtime and editor C# compile cleanly; the terrain shader reports no Unity compile messages.
- Individual harvest test: selected tree disappears, neighbour stays active, shared source mesh is unchanged.
- Home: 158 individual trees. A 2 m sampled connectivity check from the settlement reaches all 158 over land with local slope at most 0.70; the highest connected sample is 43.1 m. This verifies terrain connectivity, not a complete autonomous crew voyage.
- HarbourProbe: 3.1 m minimum water under the tested footprint, afloat at the dock; about 0.89 ha of connected buildable ground. Full report: `storybook-harbour-validation.txt`.
- Desktop and portrait camera renders reviewed. These omit IMGUI and do not establish HUD validation.
- Reproducible checks: `unity --json command eval_file --file tools/validation/storybook-harvest.cs` and `storybook-access.cs`, in play mode. These temporarily harvest a tree; stop play afterwards. Reports are in this directory.

Rebuild through **SeaSick → Art → Build Storybook Island Prefabs** in edit mode after exporting from Blender. The setup applies the profile to the active scene's WorldArtStyle; the saved production scene uses `Flora/storybook_flora`. SceneryKit resets its caches at play startup, including with domain reload disabled.

## Visuals

- `storybook-asset-kit.png`: Blender contact sheet.
- `storybook-home-final.png`: redesigned home island in Unity.
- `storybook-island-final.png`: second generated island and original brig.
- `storybook-island-portrait.png`: portrait camera check.

## Variation refinement

Option B in `harbour-directions-v1.png` remains the design reference: rounded canopy clusters, restrained greens and broad rock planes. Types have distinct proportions and colours authored through Blender MCP, with full-turn orientation and width variation in Unity. Young trees remain smaller than mature trees. Temperate groves mix these families; tropical islands retain palms.

Bushes vary in spread and height. Rock variants vary aspect, slab thickness and warm/cool stone tones. Irregular under-tree clusters add shrubs, fern variants, grass and fallen sticks; a separate lowland pass adds meadow-edge tufts and sparse driftwood. All source assets have separate prefabs; decorative placements are batched, while trees retain individual harvesting.

Home beach rolling is attenuated below the meadow and bluff shaping starts above the beach. Grass uses a narrower, lightly irregular transition into clean meadow colour, supported by physical tufts. Upper shelf heights and outlines vary while retaining tested access ramps.

Current renders: `storybook-variety-home.png`, `storybook-variety-island.png`, `storybook-variety-portrait.png`. These are staged art cameras, not HUD tests. Mobile GPU performance remains unmeasured.

## Broad cliff planes and toon lighting

The home bluff now intersects nine oblique supporting planes with its terraces. This replaces the narrow shoreline-height multiplier that extruded tall relief into metre-wide vertical ribs. Supporting faces vary in angle, extent and slope. A broad eastern access slope links the wooded shelves. These are changes to the shared terrain height function, so collision and crew queries follow the visible terrain; no decorative cliff shell hides different collision geometry.

The shared terrain/scenery shader now uses two cel lighting bands with an antialiased threshold, warm direct light and cool shadow fill. It uses mesh normals rather than derivative triangle normals, which previously exposed thin terrain grid strips. The existing GraphicLight material control gates the treatment; the sailing and water shaders are unchanged. This is a Wind Waker-inspired lighting trial judged against option B, not an exact reproduction.

Validation: runtime/editor C# clean, Unity shader diagnostics empty, individual harvest passes, all 158 home trees connected at sampled slope <=0.70 (highest reached 43.1 m), berth footprint afloat at 3.1 m minimum depth. Final art camera renders: `toon-cliffs-review.png`, `toon-cliffs-low.png`, `toon-cliffs-portrait.png`. Camera renders omit HUD. The base terrain remains a heightfield, so shelf edges retain some triangulation; the narrow vertical ribbing is removed.
