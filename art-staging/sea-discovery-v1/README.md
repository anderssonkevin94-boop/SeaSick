# Sea discovery V1 — approved 2026-09-30, awaiting integration

Eight exports: MessageBottle, BrokenBoardShort, BrokenBoardLong, LashedBoardBundle, SalvageCluster, ReefSplitPeak, ReefLowLedge, ReefLeaningTeeth. Source sea-discovery.blend, generator tools/blender/sea_discovery_v1.py. Import the individual FBXs; the Blender source includes a review layout.

## Contracts inspected

- MessageBottle.BuildVisual/Update: existing body is .12m wide/.44m tall, root follows surface +.1m and rotates 25deg/sec. New broad bottle is approximately .34m diameter/.82m long and lies at 14deg. Enlargement is an intentional readability proposal, not a silent change to pickup range. Preserve lifetime, haul/resolve state and drift. Verify bob offset and paper visibility in real ocean rendering.
- FlotsamCrate.BuildVisual: keeps 1–3 live CargoVisual units plus 1–2 surrounding boards. Keep those resource identities/counts; use new broken board variants as decoration. SalvageCluster is an art composition, not permission to replace all resource visuals with a crate.
- SalvageSpawner: current crate is 1.1m cube, board .35 x .15 x 2.2m, registered to batched ocean probes, surface +.15m. New boards are surface-centred, 1.25m/2.15m class, approximately .07m thick. Tune art-child vertical offset rather than altering ocean simulation; preserve existing respawn/lifetime/probe registry behavior.
- TerrainWorldPopulator.BuildReefs: Reef.Configure(radius) owns hazard range. New clusters each fit a unit horizontal radius and have mean-waterline origin with .5m submerged skirt. Scale to configured radius and keep existing hazard logic. Static rocks must not bob like cargo. Foam stays separate/runtime-owned. At very shallow/deep waves verify the underwater skirt does not expose its bottom. Align hazard and visible footprint; do not spawn rocks outside collision range.

## Materials and approved dependencies

Solid new meshes: GameColor, flat normals, one opaque vertex-color material, no textures. Bottle: transparent green glass shell and opaque parchment/cork as separate meshes. The Blender Mix Shader does NOT transfer as a working Unity shader: explicitly remap glass to a mobile URP transparent material (no refraction needed), alpha around .26–.35, green tint, no depth write, and keep the parchment opaque. Check sorting with ocean, fog and phone anti-aliasing. Glass must not turn the parchment invisible or require an expensive scene-color refraction pass.

SalvageCluster reuses the exact approved Cargo_Box_Large mesh loaded from art-staging/ship-cargo-v2/ship-cargo.blend. No crate redesign. Only its object pose changes (lowered .22m in the review cluster). Preserve its GameColor and approved material mapping. Original V2 export remains authoritative if installing the crate on its own; avoid duplicate resource assets.

Everything is metres except reef horizontal unit-radius convention. Source Z-up; FBX Y-up/-Z forward. Verify local axis conversion at import. New boards and bottle pivots suit water posing rather than standing on a floor. Do not apply old primitive dimensions as an additional scale.

## Review / integration checks

Images are individually framed Blender renders on a blue review background, NOT in-game ocean screenshots. Strong silhouette/color contrasts are designed for phone readability but must be tested at the game's actual camera distance. The whole reef including dark submerged portion is shown for inspection.

Check bottle sorting and scroll visibility, far-distance pickup readability, board buoyancy offsets, crate material fidelity, dynamic cargo counts, reef hazard footprint, foam fit and phone performance. No scene, water, spawning, gameplay or collider edits are included. Approved by Kevin on 2026-09-30 and queued in ASTRAS_READY_ASSETS.md.
