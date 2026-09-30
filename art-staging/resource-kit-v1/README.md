# Resource kit V1 — approved 2026-09-30, awaiting integration

Five matching resource families: Timber, Boards, Stone, Ore, Brick. Twenty FBXs: each family has Unit, CarryUnit, Carry, Stack. Approved by Kevin and queued in ASTRAS_READY_ASSETS.md. No changes to live Unity assets.

## Runtime inspected

- CampPiles.BuildUnit / DrawUnit / BuildLog / BuildCairnBlock / BuildBrickCourse: stock units change with inventory. Existing boards incorrectly share the log silhouette; this kit separates round logs from flat boards. Keep current inventory semantics and placement ownership.
- VillagerActing.EnsureCarry / BuildLogs / BuildPlanks / BuildStones / BuildBricks: load counts repeat individual meshes up to MaxVisibleCarry=6. Existing anchor is side-dependent (0.16*side, 1.42, 0.10) with +/-6 degree yaw. Keep that animation/anchor contract; verify hand and face clearance in motion.
- BuildSite: drawn logs represent completion fraction and cap at 16. Do not replace this with a permanently full stack.
- CargoVisual uses CampPiles.BuildUnit. Preserve runtime lashing/attachments and use the single unit, not a decorative full pile.

## Import contract

Source `resource-kit.blend`; generator `tools/blender/resource_kit_v1.py`. Use individual FBXs; the blend file contains a review layout, lights and floor which must not be imported as game art. Unit geometry was exported at the origin before review layout placement. Units are true metres. Bottom-centre origins for Unit/Stack, centre grip origins for CarryUnit/Carry. Long timber/stock boards run along Blender Y, mapped through FBX Y-up/-Z-forward; check wrapper orientation after import. Carried boards run across the arms, unlike shoulder logs.

One material with GameColor vertex colors, flat normals, no texture dependency. Preserve colors and use the game's vertex-color material. No colliders, spawning or gameplay changes included. If colliders are needed, use simple proxy shapes, not triangle meshes.

Unit baseline sizes follow existing pile code: log 1.6m x approximately 0.24m diameter; boards 1.6m x 0.25m x 0.075m; irregular stones/ore about 0.5m wide and 0.31m high; bricks 0.36m x 0.18m x 0.12m. CarryUnit exports are already resized: log 1.16m long, board 0.46 x 0.13 x 0.035m, stone/ore approximately 0.2m, brick 0.17 x 0.10 x 0.085m. Do not apply old primitive dimensions as another scale multiplication.

`Carry` assets are fixed review bundles, not substitutions for changing load counts. `Stack` assets are static review/decorative examples. Compose changing piles and carried loads from Unit / CarryUnit, preserving counts and existing placement increments. Ore is currently carried using sacks: its carry rock is an optional art alternative, not a requested gameplay change. Reuse approved sacks where that behavior is retained.

## Readability

Timber: dark round bark with light cut ends. Boards: long flat golden faces, darker beveled edges. Stone: pale, low angular chunks. Ore: dark angular chunks with broad copper-colored faces (stylized resource cue, not a new ore type). Brick: orderly terracotta courses with dark chamfers and visible gaps. No tiny grain, cracks or mineral glitter to disappear on phones.

Review PNGs use equal camera scale across resource families, with a reduced-size contact sheet. They are Blender renders, not proof of readability in the actual game. Before approval/import: inspect the review. After approval: verify in-game lighting, phone zoom, carry animation clearance, empty/partial/full stock, build progress, floating cargo and batching/material use.

`manifest.json` records dimensions, triangles and geometry checks. Approval recorded in ASTRAS_READY_ASSETS.md on 2026-09-30.
