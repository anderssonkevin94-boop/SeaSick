# SeaSick — Option B graphic overhaul

Status: the core Option B treatment is implemented and applied to Sea.unity. See README.md for actual captures, checks and limitations. This document retains the broader acceptance plan; not every gate below has been validated, particularly full voyage/harvest/LOD motion, HUD and device GPU performance.

## Target and boundaries

Build a vibrant, clean, fully dimensional world with large colour shapes, sculpted rock faces, grouped foliage, warm ship materials and broad readable ocean highlights. Use option B as the primary reference, including its lighting. The generated boat, architecture and harbour layout are illustrative; the game's own ship designs and functional world layout take precedence.

Success is judged in moving gameplay, with the actual chase camera, rather than solely in a staged still. Preserve the physical sea: wave size, speed, displacement, buoyancy and regional progression are central to SeaSick. Simplify the way that motion is shaded.

Visual hierarchy:

1. Ship silhouette, sail plan and crew actions.
2. Nearby wave slopes, shoreline, landing access and usable structures.
3. Island masses and navigational landmarks.
4. Surface decoration. Remove or reduce detail that competes with the first three.

## Verified project facts

- Unity 6000.4.3f1, URP 17.0.1; desktop landscape and mobile portrait remain supported.
- Current gameplay uses the twenty-node `ShipLadder` and `Shipyard`, with `Sea.unity` serialized at node 12, the brig. The historical five-hull description in `HULLS.md` is not the current progression. `seasick_bays.py` builds/exports the modular ladder using the hull generator and writes its manifest.
- Fleet colours already come from a shared 128×128 palette atlas with semantic swatches and geometry-based UV assignments. Preserve that efficient system.
- Terrain and scenery share a vertex-colour shader with procedural albedo detail, perturbed normals and rock striation. A global change affects both terrain and trees; this coupling must be handled explicitly.
- Ocean shading includes sky reflection, subsurface colour, sun specular, simulated foam history, fresh crest foam, foam relief and sparkle. Many of B's improvements can happen in shading without changing simulation.
- Buildings create materials in `BuildingFactory`; changing only `.mat` assets would miss them. Scenery colour also originates in Blender generators and terrain generation.
- Scenery is batched into mesh cells with LODs; tree harvesting records vertex ranges in both LOD meshes through `SceneryWood`. Replacement art must preserve this pipeline.
- Blender MCP responds. Its current file is `/Users/kevinandersson/blender_objects/island_resources.blend`, containing island/resource/crew collections. Preserve that session; do not clear it to build a ship.
- Unity CLI connects through Pipeline 0.7.0-exp.1 when run with local network access. Do not reinstall a package that is already installed or launch a competing batch editor against the open project.
- Package manifest and lockfile already have unrelated modifications. Leave these intact.

## Art rules

| Surface | Treatment | Avoid |
|---|---|---|
| Open ocean | Cobalt/blue body, broad highlights that follow wave form, readable light/dark faces | Dense glitter, grain, flattening the physical swell |
| Shallows | Turquoise transition governed by real depth, clear shore separation | Uniform neon cyan around every island |
| Foam | Cream-white connected shapes, clear crests and wake, soft stable edges | Tiny sparkling holes, crawling noise, static painted lines |
| Rock | Large authored planes, warm lit faces and cool shadow faces | Random triangle colours, gravel-like normal detail |
| Foliage | Cohesive canopy groups, brighter upper planes and darker undersides | Confetti-like leaf detail, cone stacks, uniform spacing |
| Timber | Warm golden/brown families, deliberate darker structural bands | Every plank a different colour, near-black undersides |
| Sails | Cream canvas with large readable folds and restrained accents | Replacing rig types with the concept's small triangular sail |
| Buildings | Clear roof/wall/beam separation, warm local colour, distinct silhouettes | Excess small props, footprint changes that break placement |
| Crew | Readable clothing groups, preserved skin/sickness distinction and strong poses | Making every surface green or letting sails hide all acting |

Author a small palette sheet during the first Unity pass, with base, light and shadow roles. Treat colours as sRGB authoring values and verify conversion in Unity/Blender; the displayed concept is not a literal shader colour lookup. Use broad continuous lighting or gently softened tone bands, not hard bands that pop as the ship rolls. Reserve strong saturation and contrast for focal subjects. Keep shadows coloured and sufficiently open to see the deck.

## Milestone 0 — Reproducible baseline and isolated art scene

Deliverable: a saved baseline capture set and an `ArtDirectionLab` scene derived from the real Sea scene.

- Restore the running editor's Pipeline connection, enumerate its actual CLI commands, then use supported commands to inspect scene state, refresh/import and execute existing capture helpers. CLI command names must be discovered rather than assumed.
- Save baseline captures of the node-12 brig approaching home, sailing across open water and alongside the dock. Also capture a skiff and the final ship of the line from chase and side views.
- Record camera transforms/FOV, viewport, ship node/load, world seed/location, time of day and weather. Use fixed ocean time for paired stills; restore it before motion tests.
- Record frame-time distribution, draw calls, geometry and memory using existing probes at matched settings. Use the same hardware and warmed scene for comparisons.
- Create copies of materials/settings affected by the experiment. Audit runtime material construction and shared global shader inputs so the lab does not silently alter production scenes. Provide one repeatable editor setup entry point and a clear restore path.

Gate: baseline is visually inspected, repeatable, and uses the actual current ships. No claims of before/after improvement without paired images.

## Milestone 1 — Ocean, sky and lighting

Largest expected visual impact; finish this before rebuilding the asset library.

Files: `Art/Shaders/Ocean/Ocean.shader`, `Materials/OceanSurface.mat`, `Scripts/World/SkyDirector.cs`, `Scripts/World/Time/TimeOfDay.cs`, `Art/Shaders/Sky.shader`, relevant scene-serialized values.

1. Establish B's clear daylight: warm direct light, cool open shadows, blue sky gradient, gentle distant haze. Keep ship-to-sea value separation.
2. Tune deep/shallow/subsurface colour and broad reflection/specular response together. Reduce fine shading noise without removing the wave normals needed to read sailing conditions.
3. Simplify foam appearance, beginning with relief/sparkle and fragment-side breakup. Preserve actual crest onset, foam history, wake and shoreline inputs. Keep near/far continuity and antialiased edges.
4. Test moving water at low and grazing camera angles; inspect horizon shimmer, banding and reflection behaviour. Avoid screen-space effects as the first solution.
5. Extend the palette to dusk, night and storm lighting. Storms can remain dark and threatening while the deck, foam and wave faces stay readable. Lanterns retain a useful contrast at night.

Gate: actual brig-on-water view approaches B, waves retain their shape in motion, calm/storm distinction remains obvious, and no simulation parameters changed.

## Milestone 2 — Terrain and foliage

Files: `TerrainVertexColor.shader`, `TerrainVertexColor.mat`, `TerrainSettings`, `TerrainChunkMesher`, `IslandScenery`, `SceneryKit`, `SceneryWood`, and `tools/blender/seasick_style.py` / relevant resource generators.

1. Separate terrain-specific detail controls from scenery treatment where needed, preserving batched rendering. Test reducing albedo breakup, normal roughness and striation independently rather than flattening everything at once.
2. Rebalance grass, sand and rock colours under the established lighting. Avoid double-darkening from baked vertex gradients plus real-time shadows.
3. Rework a small kit first: one conifer family, one broadleaf family, one boulder family and one cliff/shore rock family. Use large silhouette-defining forms and coherent planes. Preserve tree scale and root origins.
4. Improve scenery grouping and open-space rhythm without changing resource economy or making landings inaccessible. If terrain geometry still reads as rounded ramps, treat cliff shaping as a separate, measured follow-up rather than disguising it with random colour facets.
5. Carry replacements through both LODs, growth/felled/collected states where applicable, and harvesting. Keep cliffs consistent with collidable terrain and shore depth; do not build a decorative island unrelated to playable land.

Gate: one real shoreline matches B's massing and colour hierarchy; landing, harvesting and LOD transitions work; no new forest of individual renderers.

## Milestone 3 — Preserve and restyle the ships

Files: `tools/blender/seasick_hulls.py`, `tools/blender/seasick_bays.py`, `Art/Ship/Hulls/seasick_palette.png`, `Materials/SeaSick_Hull.mat`, current ladder FBXs and runtime furniture materials.

Start with the node-12 brig as the reference ship. Keep the maritime construction that defines this game: long hull proportions, sheer, bulwarks, gun-port rows, deck levels, masts, yards, bowsprit and existing sail plans. Improve warmth, colour separation, silhouette clarity and selective edge detail. Do not turn the fleet into scaled versions of the concept boat.

Order:

1. Recolour existing semantic palette roles; verify plank bands, wales, caprails, canvas and metal remain distinct from chase distance. Preserve atlas dimensions, swatch indices and UV centres.
2. Refine material lighting only where the standard material fails the target; preserve shadows and depth rendering. Unify furniture, cannon, lids, cargo and lantern treatment with the hull.
3. Adjust generator-controlled visual detail only when the new lighting exposes a specific weakness. Keep fine construction where it explains a ship; simplify repeated detail that only produces shimmer.
4. Work on workspace copies through Blender MCP. Compare repo and external generator versions before selecting the source of truth. Keep generator changes reproducible; do not make mesh-only edits that the next build erases.
5. Propagate the approved brig treatment to all twenty ladder nodes through the generator. The skiff, sloop, brig and multi-deck warship must remain visibly distinct. Historical raft/steamer assets are secondary unless still used by a live gameplay path.

Preservation checks: imported length/beam/draft, origin/waterline, bow direction, scale, deck/battery levels, node identifiers, sockets/empties and runtime part names. Preserve displacement manifest and buoyancy layout for a material-only pass. If any geometry change affects displacement or collider shape, stop that change for a separate gameplay-impact review rather than silently retuning physics.

Gate: all twenty nodes render with the shared style; representative early/middle/final nodes sail, upgrade and retain fitting anchors, port-lid motion, sail trim and crew access. Generator topology checks remain clean.

## Milestone 4 — Harbour, crew and effects

Files: `BuildingFactory`, `BuildPlan`, `DockBuilder`, `IslandPropFactory`, `CargoVisual`, crew shaders/materials and `tools/blender/seasick_crew.py`; relevant wake/spray/impact and UI theme code.

- Restyle one dock and two existing building types first. Preserve footprints, entrances, grounding and gameplay identifiers. Use clearer roofs, stronger structural colour separation and modest accents; preserve the game's village identity rather than copying the board's anchor signage.
- Bring resource piles, barrels and cargo into the same material family. Keep gatherable and stored states readable.
- Recolour crew first; assess proportions only at the actual gameplay camera. If mesh changes are needed, preserve rigs, animation compatibility, feet/deck contact and station positions. Confirm sickness tint is legible against the new palette.
- Match spray, wake, smoke and impacts to the clean shape language. Effects should reinforce motion without covering the crew or turning storms into white screens.
- Reconcile UI colour and contrast after the world palette is stable. Retain the no-full-width-banner rule and the adaptive desktop/portrait layout; this milestone is not a UI layout redesign.

Gate: the complete playable harbour view feels coherent, and arrival, docking, gathering, loading and departure remain readable.

## Milestone 5 — World rollout and final verification

- Extend the common material/shading rules to the remaining regions while retaining biome identity. Match distant horizon geometry and fog to nearby islands.
- Promote verified lab values/assets into the Sea scene through repeatable setup/import operations; serialized scene/material values must match intended settings, not merely new C# or shader defaults.
- Capture paired before/after images in `docs/art-direction/` at 1920×1080 and 1080×2340. Include noon harbour, open sea, dusk, night and storm; include early, brig and final hull views.
- Run motion checks for water shimmer, camera movement, LOD popping, clipping, shadow transitions and foam stability. Check sail/crew occlusion on the tallest rigs in portrait.
- Compile runtime and editor C# using the project checks; explicitly inspect shader import errors and target-player rendering. A green C# compile is not a shader pass.
- Use existing `HudOverlapProbe`, `PerfProbe`, `TerrainPerfProbe`, hull/import checks and relevant ship/shore probes rather than creating duplicate infrastructure. Repeat physical tests only where changes touch those systems or reveal a regression.
- Performance target: remain within 5% of the warmed baseline median and p95 frame time on the same setup, with no material memory/draw-call growth without a measured reason. Treat this as an initial regression budget, not a claimed mobile certification; validate on actual target hardware when available.
- Make verified milestone commits and update the GDD art direction to the resulting implementation. Keep package changes outside art commits. Preserve baseline assets/captures until the rollout is verified.

## Decision rules and completion

Proceed in order: baseline → ocean/light → shore kit → brig/fleet → harbour/crew → world rollout. Iterate locally until each gate passes; a new approval is not required for every routine reversible step within this selected direction. Surface any major departure from the selected style or a change to gameplay geometry before committing to it.

Prioritize the ocean and lighting over model quantity. Prefer existing assets when material changes get them close to B. Use deterministic Blender generator edits for this fleet and kit; a newly generated generic ship would lose the existing structure and functional anchors.

Completion means the style is present in the actual Sea scene, across representative ships, weather and both aspect ratios, with validated motion and performance. A attractive lab screenshot alone is an intermediate result.
