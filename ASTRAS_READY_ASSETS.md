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
| [Bow harpoon + gun lamp](#bow-harpoon--gun-lamp) | art-staging/harpoon-v1 | 2026-10-04 | Mount + barb in game (b479be1f), awaiting Kevin's look approval. Gun lamp: art in progress (sailing session), awaiting Kevin's look; code lane done (spot light + glowing lens, `HarpoonLamp`) |
| [Storage containers L1](#storage-containers-l1) | storage-slots-preview/export (kit mode) | 2026-10-03 | Imported + in game (f60bf054), edit-mode shots OK — awaiting Kevin's phone check on a real camp |
| [Ship with one rear module F18](#ship-with-one-rear-module-f18) | f-coaster-v18/timber-top-band | 2026-09-29 | Approved rear-module boat; F19 rear + middle also approved |

_Watch Tower Lvl 1 (V5) and Lumber Mill Lvl 1 (Timber Fan) were implemented 2026-09-28 (commit 415ff63); Kitchen Lvl 1 (V6, `art-staging/kitchen-grill-lvl1-v6`) on 2026-10-01 (`KitchenL1Import`); follow-ups are with Kevin._

## Bow harpoon + gun lamp

**Gun lamp replaces the nose lantern (Kevin's final design, 2026-10-04):** a hooded bullseye lantern bolted to the side of the harpoon gun, on `Swivel`, shining forward like a flashlight. It is the ship's ONLY bow light: the nose-block `BowLantern` (31e15558 and the villager commits before it) is removed from code and the kit post lantern (`Lantern_Bow_*`) is hidden on both bow variants, low and raised. Status: **art in progress (sailing session), awaiting Kevin's look.** The art adds three nodes to `HarpoonMount` under `Swivel`: `Lamp` (casing), `Lamp_Lens` (lens mesh, glows warm via a property block, `_Ambient` on the toon shaders) and `Lamp_Light` (an empty at the lens centre facing +Z). Code is in (`Ship/Harpoon/HarpoonLamp.cs`): a warm URP spot (40 deg, 28 m, no shadows, night/day from `SkyDirector.Night01` like the hull lanterns), with a fallback `Lamp_Light` beside the muzzle and a placeholder casing + lens when the art is missing; knobs are `HarpoonTuning.lamp*` in FeelLab. The old `BowLantern` description below is history: those assets are deleted once the lamp ships.

**Phase 0 of docs/PLAN-harpoon.md** (Kevin approved the plan 2026-10-04). Kevin liked the mount placement on the early shots; the lantern moved per his note: *"the lantern needs to be below the line of fire … add a little block that sticks out from the ship's nose and have the lantern hang off of it."*

Source: `art-staging/harpoon-v1/` (`build.py` modes for the mount/barb and `-- lantern`; `check.py` → `export-verification.json` / `lantern-verification.json`; `render.py`; `CONTRACT.md`; `RUN.md`; `rope-look.json`). In game: `Resources/Harpoon/HarpoonMount` (Swivel, Barb_Muzzle, Winch_Drum, Harpooner_Stand; 1,728 tris), `HarpoonBarb` (Line_Attach; 192 tris), `BowLantern` (nose block + chain + Lantern_Bow_Frame/_Glass under LanternBow_Pivot; 516 tris), `SS_Harpoon_*` materials, `rope-look.json` (slack #D2B48C / taut #FFFFFF / strained #FF6A30; widths .085/.075/.070). FBX in `Art/Harpoon/Models`. Importer `Dev/Editor/HarpoonImport`, shot tool `Dev/HarpoonLookShot`. (Superseded: `CoasterOutfitting` no longer hangs `BowLantern`; the gun lamp is the bow light.) The rope clears the nose block by 0.205 m and the swinging lantern by ≥0.465 m across ±45° (check.py).

Remaining: Kevin's look approval of the mount, barb and gun lamp; dead ahead at 10–12 m the line passes through the hull's stem cap (line routing, sailing session); the gun lamp art. Then the phone check, then remove this entry.

## Storage containers L1

**Approved 2026-10-03** (Kevin, via the orchestrator): the storage-slot containers preview, with the tarp moved back (open stall front) and the fire-cache groundsheet at 3.2 × 2.3 m. Design: no ground piles; island stores are UNLIMITED (Kevin 2026-10-03, "infinite stacking"), so the containers are the visual only: each slot shows one bundle of one resource (logs 5, stone 10, boards 10, sacks 12, pegs 4, dish trays 12, gear per item) and stays shown full past that.

Source: `art-staging/storage-slots-preview/export/` (`build.py -- hut kit`, `-- cache kit`, `-- fillkit`; `check.py`; `CONTRACT.md`; `RUN.md`). Exports: `StorageHutL1.fbx` (structure + 30 `Stock_<Family>_NN` anchors + `Marker_Pickup`, 5.2k tris), `FireCache.fbx` (structure + 10 anchors, 1.3k tris), `StorageFillKit.fbx` (167 shared fill steps `<Family>__Fill_[<Res>_]<k>`, 41k tris in total, worst case visible ≈28k hut / 8k fire). Previews: `review-hut-phone-high50.png`, `review-firecache-phone.png`; in-engine edit-mode shots via `StorageShot.Run`. Materials `SS_StorageL1_Wood/_Hemp/_Canvas_Endgrain_Stone` + food GameColor; the real food models are reused unchanged.

In game: `StorageL1Import.Run()` → `Art/StorageL1/`, `Resources/Settlement/storage_l1` + `firecache_l1`, `Resources/Kits/StorageL1/StorageFillKit`; `StorageSlotView` spawns steps lazily from the kit; the store hut falls back to the old model if the prefab is missing; the fire cache sits on the first clear bearing outside the supper ring (saved, footprint reserved), or is not drawn on a cramped camp.

Remaining: Kevin's phone check on his real save (hut readable from the camp camera, slots fill and stay full, fire cache placed clear of buildings and the supper ring, no ground piles), then remove this entry. Storehouse has no art yet (its new role = more/better runners, proposal with Kevin).

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
