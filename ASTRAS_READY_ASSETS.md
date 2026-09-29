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
| [Large swimming fish](#large-swimming-fish) | large-fish-v1 + swim loop | 2026-09-29 | Approved art and animation — awaiting Unity integration |
| [Ship cargo — barrels, sacks and crates](#ship-cargo--barrels-sacks-and-crates) | barrels/sacks V1, crates V2 | 2026-09-29 | Approved art — awaiting Claude import and Unity checks |
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

Kevin approved F18 as the boat with one module at the back on 2026-09-29. Source: `art-staging/f-coaster-v18/timber-top-band/ship.blend`; exports and assembly manifest are in that folder's `models/` and `manifest.json`. Previews: `hero.png` and `empty-port-closed.png`. Kevin subsequently approved F19 with the raised middle module. Its source is `art-staging/f-coaster-v19/rear-and-middle/ship.blend`, with separate exports and manifest alongside it. F20 adds the raised bow as a separate variant awaiting review.

Warm timber top band, navy exterior, aligned structural ribs, a steering-only rear deck, turning stairs and lower gun bays with timber trim and hinged covers. `cannon_present` on each cover root drives the Blender cover and review cannon visibility. Bind this to equipment occupancy in Unity; drivers do not transfer through FBX.

Original F14 and frozen F10 remain preserved. Scale: 0.5 metres per source unit; source +X bow, +Y port, +Z up. Game mapping and individual export positions are in the manifest. Review cannons and crew are separate from ship exports.

Remaining integration: reusable wall sections/runtime stacking, width/height configuration checks, cannon recoil, crew colliders/navigation, lighting and iPhone playtesting. Current validation covers static art, sampled wheel clearance and export fidelity.

## Ship cargo — barrels, sacks and crates

Kevin approved the barrel and sack designs from V1, then the rebuilt V2 crates, on 2026-09-29. Import the exact revisions below. V1 crates are rejected.

- Editable source: `art-staging/ship-cargo-v1/ship-cargo.blend`.
- Approved objects: `Cargo_Barrel_Large`, `Cargo_Barrel_Small`, `Cargo_Sack_Cream`, `Cargo_Sack_Ochre`. Individual FBX and GLB exports with these names are in the same directory.
- Preview: `art-staging/ship-cargo-v1/cargo-set.png`; package notes and bounds: `README.md`, `manifest.json`; GLB attribute checks: `export-validation.json`.
- Real metres, bottom-centred pivots. Do not apply the ship authoring 0.5 scale. Preserve UV0, normals and linear GameColor; use vertex-colour-aware PBR materials with the package's wood, iron, cloth and rope responses.
- **Approved crates:** `Cargo_Box_Large` and `Cargo_Box_Small` from `art-staging/ship-cargo-v2/ship-cargo.blend`; individual matching FBX/GLB files in that folder. Use V2 only. Dark timber frames, recessed light panels, a closed continuous shell and no projecting lid battens.
- Crate preview: `art-staging/ship-cargo-v2/cargo-deck-group.png`; exact bounds/counts in `manifest.json`, checks in `validation.json`, notes in `README.md`. Each crate has one material primitive and 1,444 triangles. The barrel/sack copies in V2 have verified identical exported geometry, normals, UVs and colors to approved V1.
- Still required: Unity import/material verification, cargo inventory display sockets, walk/ladder/cannon clearance, simple collision and navigation integration, and phone readability/performance. Use simple collision shapes where appropriate; do not substitute mesh colliders for the high-detail render mesh. Models are not yet installed in the game. Claude should retain this queue entry until integration and relevant checks pass.

## Large swimming fish

Kevin approved the fish and its swimming animation on 2026-09-29. Use `art-staging/large-fish-v1/`.

- **Game export:** `large-fish-rigged.fbx` — one skinned mesh, five bones, 940 triangles, 506 weighted vertices.
- **Editable source:** `large-fish.blend`; generator: `tools/blender/large_fish_v1.py`.
- **Approved previews:** `fish-preview.png`, `fish-top.png`, animated `fish-swimming.gif`. These are Blender renders, not in-game underwater captures.
- **Animation:** `Swim_Slow_Loop`, two seconds at 24fps, no root translation. Use Generic rig and Loop Time; use the imported clip's full duration rather than assuming frame offsets. FBX round-trip confirmed movement and matching loop endpoints.
- **Materials:** preserve `GameColor` vertex colors and faceted normals; deep teal/turquoise back, pale dorsal stripe and belly, gold eyes. Prefer the existing vertex-color art shader and consolidate palette submeshes during import where practical. No textures required.
- **Scale/orientation:** approximately 5.9m long, metres. Source faces Blender +X; FBX uses Y-up/-Z export-forward. Verify and align the imported art child to the gameplay movement root's +Z.
- **Behavior:** intended to swim below the water. Suggested initial treatment is a single ambient fish with broad turns and restrained speed; spawning/movement are not included. Keep the entire dorsal fin below the locally sampled surface.
- **Remaining checks:** import rig/clip/materials, test underwater visibility with the actual ocean shader, check phone silhouette and performance, then connect movement/spawning. Do not fake submersion by floating the model above opaque water. No gameplay collider or combat behavior has been requested.
- **Notes/checks:** `README.md`, `validation.json`, `export-check.json` in the package.

Keep this entry until the fish is integrated and the underwater/animation checks pass.
