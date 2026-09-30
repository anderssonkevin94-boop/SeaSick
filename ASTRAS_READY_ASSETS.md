# Astra’s Ready Assets

Approved art waiting for Claude to implement in SeaSick. This is the active handoff queue, not a catalogue of every experiment.

## Workflow

- Whenever Kevin approves an asset, add or update its entry here with the exact approved revision, source/export paths, previews, materials, implementation constraints and remaining checks.
- Approval means the artwork is accepted; it does not mean it has been installed or playtested in Unity.
- Keep one entry per asset. Later approved revisions replace the queued revision; do not choose a different version merely because its filename looks newer.
- Claude implements from the listed files, checks the result in game, then removes that asset’s entire entry and index row from this document. Keep source files and shared textures on disk.
- If implementation is partial or blocked, keep the entry and record what remains. Do not remove it just because an FBX has been imported.
- Future entries should use the same structure below. Paths are relative to this project root unless stated otherwise.

## Waiting for implementation

| Asset | Approved revision | Approved on | Status |
| --- | --- | --- | --- |
| [Sea discovery kit](#sea-discovery-kit) | sea-discovery-v1 | 2026-09-30 | Approved art — awaiting import and waterline/visibility checks |
| [Worker tools](#worker-tools) | worker-tools-v1 | 2026-09-30 | Approved art — awaiting import and animation checks |
| [Resource kit](#resource-kit) | resource-kit-v1 | 2026-09-30 | Approved art — awaiting import and gameplay checks |
| [Ship with one rear module F18](#ship-with-one-rear-module-f18) | f-coaster-v18/timber-top-band | 2026-09-29 | Approved rear-module boat; F19 rear + middle also approved |
| [Kitchen Lvl 1](#kitchen-lvl-1) | kitchen-chunky-lvl1-v4 | 2026-09-28 | Approved art — awaiting implementation and Unity checks |

_Watch Tower Lvl 1 (V5) and Lumber Mill Lvl 1 (Timber Fan) were implemented 2026-09-28 (commit 415ff63); follow-ups are with Kevin._

## Kitchen Lvl 1

**Approved package:** `art-staging/kitchen-chunky-lvl1-v4/`, approved by Kevin on 2026-09-28. Use V4's complete redesign, not the earlier kitchen revisions or concept drawings. The saved Blender source and exports are authoritative.

### Files

| Purpose | File |
| --- | --- |
| Complete game export | [kitchen-state-kit.fbx](art-staging/kitchen-chunky-lvl1-v4/kitchen-state-kit.fbx) |
| Alternative fixed structure only | [kitchen-empty.fbx](art-staging/kitchen-chunky-lvl1-v4/kitchen-empty.fbx) |
| Editable source, no crew | [kitchen-lvl1.blend](art-staging/kitchen-chunky-lvl1-v4/kitchen-lvl1.blend) |
| Approved visual target | [kitchen-hero.png](art-staging/kitchen-chunky-lvl1-v4/kitchen-hero.png) |
| Small-scale preview | [phone-preview.png](art-staging/kitchen-chunky-lvl1-v4/phone-preview.png) |
| Detailed implementation notes and marker coordinates | [README.md](art-staging/kitchen-chunky-lvl1-v4/README.md) |
| Geometry checks | [validation.json](art-staging/kitchen-chunky-lvl1-v4/validation.json) |
| Camera visibility checks | [camera-checks.json](art-staging/kitchen-chunky-lvl1-v4/camera-checks.json) |
| FBX round-trip verification | [export-verification.json](art-staging/kitchen-chunky-lvl1-v4/export-verification.json) |
| Shared wood texture | [wood-tile-512.png](art-staging/wall-textured-v3/textures/wood-tile-512.png) |
| Shared hemp texture | [rope-tile-512.png](art-staging/wall-textured-v3/textures/rope-tile-512.png) |

### Approved appearance and materials

Continuous shaped countertop with fitted board base, six exposed dishes on a stepped display, four ingredient bundles in a shallow tray, compact arched clay hearth and stone crown, terracotta cooking vessel, and smaller rear tarp canopy. Preserve the outward-facing cook and adjacent cooking/prep surfaces, clear rear access, and dish visibility. No iron. Existing preview crew is for scale only and is excluded from game exports.

2,174 triangles empty; 3,402 in the stocked cooking preview; 3,550 across all mutually exclusive states. Preserve UV0, `GameColor`, flat normals and separate stock/state objects.

| Material stem (suffixes may vary) | Unity material behavior |
| --- | --- |
| `SS_KitchenL1_Wood` | Shared wood color map × `GameColor`; white base tint |
| `SS_KitchenL1_Hemp` | Shared hemp color map × `GameColor`; white base tint |
| `SS_KitchenL1_Clay_Canvas_Food` | Vertex color only/white texture |

Reuse the shared 512px sRGB repeating maps with mipmaps and mobile compression. Use a texture-capable toon path and preserve mappings against blanket importer remaps; consult the [wall material handoff](art-staging/wall-textured-v3/CLAUDE_HANDOFF.md). Exclude studio cameras, lights, backdrop and review crew. Do not install both FBXs together: the state kit already includes the fixed structure.

### Implementation contract

- Replace the visual at `Settlement/kitchen_astra`, preserving prefab GUID, gameplay components, save identity and recipes. Keep `BuildPlan.Kitchen` footprint 6.26 × 6.12 m and 4.18 m height allowance; the model fits. Metres, Blender Z-up/front -Y; FBX Y-up/-Z forward. Verify Unity wrapper front +Z and scale.
- Preserve four `Input_Food_01..04` and six `Output_Meal_01..06` slots. Permanent tray, counter and terrace meshes are unnumbered. Initialize visibility from live inventory; do not show every variant on import.
- `Work_Preparing`, `Work_Cooking`, `Work_Finished` are mutually exclusive, or all hidden while idle. `Spoon_Tool` is working-only. Existing `StationStockView` supports these aliases and root-level numbered inventory slots; recheck current code.
- Keep existing level-one recipes: baked potato, grilled fish, grilled meat and roast carrots. Food shapes are generic display stock; static hearth logs do not add a fuel requirement.
- Keep `Worker_Stand` at source (0.08, 0.34, 0), facing `Prep_Anchor` (-0.06, -0.28, 1.075), with `Cook_Anchor` (0.84, 0.20, 1.16) beside the right shoulder. Ground-level feet; no raised platform. Prep and pot centers are 0.636 m and 0.773 m horizontally from the stand. Validate cooking turn/reach animation; measured proximity does not establish animated reach.
- `Cook_Anchor` and `Worker_Approach` are reference markers, not automatically consumed by current runtime. Preserve rear access and the pickup/dropoff markers. See package README for all coordinates and apply export-axis conversion.
- Fire/steam markers reserve effect positions; no VFX are included. Keep effects clear of canvas and timber.

Read current `BuildPlan`, `Recipe`, `StationStockView` and `CampWorker` before implementation.

### Remaining integration checks

- Test cooking/prep animations, reach to both surfaces, hauling and rear entry/exit.
- Test empty/full/working/blocked/partially unloaded states without duplicated meals.
- Match approved appearance under game lighting; verify mobile rendering costs, materials and phone readability. Studio previews use the staging-time game defaults of 28° tilt and 50° vertical FOV. All six dish centers passed front-angle visibility checks at -30°, 0°, +30° and the hero angle; verify actual camera orbit and roof occlusion in Unity.
- Test fire/steam clearance, collisions/pathing, slopes, construction preview, selection, save/load and upgrades.
- Checked rest-pose geometry has no tested crew/structure or ingredient/tray crossings. FBX round trips preserve triangles, UVs, colors and slot counts with no zero-area triangles. These are not Unity playtest results.

**Completion:** remove this entry and its index row only after implementation and relevant checks pass. Leave it queued with a status note if anything remains unresolved.

## Ship with one rear module F18

**Status 2026-09-30:** superseded in game by the modular coaster family (F30 hulls, commit ee49d41), which grew out of this F18/F19 line; kept here until Kevin confirms F30 is the approved successor.

Kevin approved F18 as the boat with one module at the back on 2026-09-29. Source: `art-staging/f-coaster-v18/timber-top-band/ship.blend`; exports and assembly manifest are in that folder's `models/` and `manifest.json`. Previews: `hero.png` and `empty-port-closed.png`. Kevin subsequently approved F19 with the raised middle module. Its source is `art-staging/f-coaster-v19/rear-and-middle/ship.blend`, with separate exports and manifest alongside it. F20 adds the raised bow as a separate variant awaiting review.

Warm timber top band, navy exterior, aligned structural ribs, a steering-only rear deck, turning stairs and lower gun bays with timber trim and hinged covers. `cannon_present` on each cover root drives the Blender cover and review cannon visibility. Bind this to equipment occupancy in Unity; drivers do not transfer through FBX.

Original F14 and frozen F10 remain preserved. Scale: 0.5 metres per source unit; source +X bow, +Y port, +Z up. Game mapping and individual export positions are in the manifest. Review cannons and crew are separate from ship exports.

Remaining integration: reusable wall sections/runtime stacking, width/height configuration checks, cannon recoil, crew colliders/navigation, lighting and iPhone playtesting. Current validation covers static art, sampled wheel clearance and export fidelity.

## Resource kit

Kevin approved `art-staging/resource-kit-v1/` on 2026-09-30. Five families: Timber, Boards, Stone, Ore, Brick.

- **Source:** `resource-kit.blend`; reproducible generator `tools/blender/resource_kit_v1.py`.
- **Exact game exports:** `{Timber,Boards,Stone,Ore,Brick}_{Unit,CarryUnit,Carry,Stack}.fbx` in that package (20 files). Import individual FBXs, not the Blender review layout.
- **Approved appearance:** `resource-kit-review.png`; reduced review `resource-kit-small.png`. Dark bark/light cut ends, golden flat boards, pale rough stone, dark ore with broad copper-colored facets, terracotta bricks. Preserve these large color regions and silhouettes.
- **Material:** single shared vertex-color material, `GameColor`, flat normals; no textures. Individual units range from 52 to 156 triangles. All 20 FBX round trips preserve triangle counts and colors; no tested boundary edges or zero-area faces in source exports. See `manifest.json`, `export-validation.json`.
- **Scale and pivots:** true metres. Unit/Stack bottom-centre, CarryUnit/Carry centre grip. CarryUnit is already resized for existing carrying dimensions. Do not multiply by old primitive scale again. Check FBX axis alignment; long stock boards/logs run along Blender Y, carry boards across the arms. Exact dimensions in manifest and README.
- **Runtime contract:** replace geometry in CampPiles/CargoVisual, VillagerActing and BuildSite while preserving runtime layout/count ownership. Boards must become planks rather than sharing the log visual. Use Unit for changing stock/build-site piles; CarryUnit repeated up to the existing six-item cap for carried loads. Preserve construction completion representation. Fixed Carry bundles and Stack examples are for review/static decoration, not replacements for dynamic counts. Ore currently travels in sacks; retaining that behavior is valid, and the ore carry mesh is optional.
- **Integration checks:** phone zoom and lighting, hand/face/shoulder clearance during work and walking, empty/partial/full stock, exact visible load counts, build progress, floating cargo, shared materials/batching. Existing carry anchors and sizes are documented in `README.md`. No gameplay or colliders included.

Keep queued until integrated and checked in game.

## Worker tools

Kevin approved `art-staging/worker-tools-v1/` on 2026-09-30. Seven models for six roles, preserving stone and iron spear variants.

- **Exact exports:** `Axe.fbx`, `Hammer.fbx`, `Saw.fbx`, `Hoe.fbx`, `StirPaddle.fbx`, `SpearStone.fbx`, `SpearIron.fbx` in the approved package.
- **Source:** `worker-tools.blend`; generator `tools/blender/worker_tools_v1.py`. Import individual FBXs, not the Blender review layout.
- **Visual target:** `worker-tools-review.png`, individual named PNGs; contact-sheet tools are independently framed, not shown at equal scale.
- **Materials:** one vertex-color material per mesh, preserve `GameColor` and flat normals. Warm wood, dark grips/iron, broad light working edges. No textures. 148–252 triangles each. All seven FBX round trips verified; source checks found no boundary edges or zero-area faces. See `manifest.json` and `export-validation.json`.
- **Tool frame:** fist-centred origin, game +Y up haft, +Z working face, X swing axis. Source maps game (x,y,z) to Blender (x,-z,y), exported Y-up/-Z-forward. Verify imported axes and mirrored-side posing before replacing primitives. Metres; parent to body at unit scale, not scaled deckhand bones. Do not apply old primitive dimensions as additional scaling.
- **Animation contract:** preserve VillagerActing.PoseTool reach/face values: Hammer .30/.10m; Axe .56/.13m; Saw .33/.055m; Hoe 1.02/.14m. Stir head at Y .66m. Preserve offhand offsets (.11m axe, .42m hoe). HunterProps spear haft extends .65m below / 1.15m above grip; retain resource-specific stone/iron selection. Existing procedural poses animate these rigid meshes; no new clips required.
- **Remaining checks:** all work strokes, saw contact, hoe ground strike, pot clearance, hand/face clearance, two-hand grips, spear carrying/jabs, mirrored worker sides, phone readability and shared-material batching. No gameplay/collider changes included. Full instructions in package README.

Keep queued until integrated and animation checks pass.

## Sea discovery kit

Kevin approved `art-staging/sea-discovery-v1/` on 2026-09-30.

- **Exact exports:** `MessageBottle.fbx`, `SalvageCluster.fbx`, `LashedBoardBundle.fbx`, `BrokenBoardShort.fbx`, `BrokenBoardLong.fbx`, `ReefSplitPeak.fbx`, `ReefLowLedge.fbx`, `ReefLeaningTeeth.fbx`.
- **Source:** `sea-discovery.blend`; generator `tools/blender/sea_discovery_v1.py`. Import individual FBXs, not the Blender review layout.
- **Approved previews:** `sea-discovery-review.png`, `sea-discovery-small.png`, individual named PNGs. These are independently framed Blender renders, not in-game water captures.
- **Geometry:** boards 32 triangles each; bundle 208; bottle 504; reefs 168 each; salvage cluster 1,508. All eight FBX round trips preserve triangle counts and GameColor. See `manifest.json` and `export-validation.json`.
- **Materials:** opaque new meshes use GameColor/flat normals with one shared vertex-color material. Bottle has separate transparent green glass and opaque parchment/cork. Blender Mix Shader does not transfer: explicitly remap glass to mobile URP transparency, alpha approximately .26–.35, green tint, no depth write or scene-color refraction. Verify sorting against ocean and fog.
- **Approved crate reuse:** SalvageCluster uses the unchanged Cargo_Box_Large mesh from `art-staging/ship-cargo-v2/ship-cargo.blend`, posed .22m lower with new broken boards. Preserve approved V2 colors/materials; original crate export remains authoritative for standalone use. Avoid duplicate shared crate assets.
- **Waterline/scale:** metres; FBX Y-up/-Z-forward. Bottle body approximately .34m diameter/.82m long, intentionally enlarged from placeholder for phone readability, tilted 14 degrees, root intended for existing surface +.1m offset. Boards are surface-centred, 1.25m/2.15m class. Verify art-child offsets against existing surface +.15m salvage posing; do not alter ocean physics.
- **Reef contract:** unit horizontal radius, origin mean waterline, .5m submerged skirt. Scale to existing Reef.Configure(radius), preserving hazard footprint, spawn clearance and separate runtime foam. Reefs stay static; check waves do not expose skirt bottoms.
- **Runtime ownership:** preserve MessageBottle drift/lifetime/hauling/resolution. Preserve SalvageSpawner batched probes/respawn. FlotsamCrate keeps resource-specific 1–3 CargoVisual units; replace decorative boards without changing resource identities/counts. SalvageCluster is a static composition, not a replacement for every live resource type.
- **Remaining checks:** bottle sorting and message visibility, phone-distance recognition, waterline offsets, crate fidelity, dynamic cargo counts, reef hazard/foam fit and phone performance. Full inspected contracts in package `README.md`. No scene, spawning, physics or collider changes included.

Keep queued until integrated and relevant checks pass.
