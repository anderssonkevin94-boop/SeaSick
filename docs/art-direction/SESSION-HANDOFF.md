## Update — fleet implemented in Unity, 2026-09-17

The user explicitly authorized all 20 ships to be implemented while Claude was idle. This supersedes the older “leave Unity alone” restriction **for the fleet**. Integration is complete and verified: native prefabs under `Assets/_Project/Resources/Ships/FleetV3`, supporting meshes/material under `Assets/_Project/Art/FleetV3`, and runtime Shipyard/SailRig/Cannon wiring. Gun counts never decrease, stages 4–20 are armed, and stage 13 is the approved new 14-gun model.

Read `docs/art-direction/fleet-v3/UNITY-INTEGRATION.md` before further ship work. All 20 runtime swaps and cannon-firing checks passed three times. Desktop/portrait Unity renders and a live Sea capture are in `docs/art-direction/fleet-v3/unity/`. The existing ocean source changes and other Claude work were not edited. The open Sea scene had unsaved changes and was preserved; Unity was returned to edit mode. No shared-tree commit was made. Hydrostatics are dimensional adaptations of the existing calibrated hull proxy; comprehensive storm balance and mobile LOD tuning remain to be done.

---

# Session handoff — 2026-09-17

## Read first: active scope and user constraints

The user asked to save and prepare for a new session. Current work is complete as a first design pass; do not start further changes until directed.

**LEAVE UNITY ALONE.** User has Claude working on ocean code. No Unity calls, imports, Assets edits or changes to ocean/gameplay code are authorized by the current asset task. All recent assets remain standalone until the user explicitly authorizes implementation. Use **Blender MCP** for modeling. Preserve the user's active Blender scene by restoring it after automation. Never overwrite the ship or island design with unrelated work.

User prefers delegation of simpler bounded tasks to cheaper models to conserve usage. A Luna agent audited settlement dimensions/layouts during this session. No active delegated work remains.

## Art direction

Reference **B — Graphic adventure**: clean, deliberate faceted shapes; vibrant coastal palette; honey timber, cream plaster/canvas, terracotta roofs, teal/coral accents. Avoid noisy realism, arbitrary geometry, intersections, repetitive rock dressing and visible gaps. Everything should have a purpose and readable silhouette.

Main reference: `/Users/kevinandersson/Downloads/exec-7df6874c-ff1b-4c3b-bd7d-098b54a492b8.png` (use the lower B panel).
Ship reference: `docs/art-direction/ship-concept/v6/ship-reference.png`.
Latest standalone ship: `tools/blender/source/adventure-brig-design-v8.blend`; previews in `docs/art-direction/ship-concept/v8/`.
Earlier sections of `docs/art-direction/README.md` describe historical implemented work, not authorization to change Unity now.

## Saved sailor — accepted for now

User accepted the general low-poly style and requested a weathered male seafarer: beard, rust watch cap, smaller eyes, brown coat, longer torso and shorter legs. No scalp hair. Thick coat; hidden torso removed to avoid clipping. This design is accepted for now and saved for later import.

- Blender: `tools/blender/source/crew-weathered-v2.blend`
- GLB: `tools/blender/exports/crew-weathered-v2/male-coat-deckhand.glb`
- Generator: `tools/blender/crew_weathered_v2.py`
- Preview: `docs/art-direction/crew-concept/weathered-v2/character.png`
- Notes: `docs/art-direction/crew-concept/weathered-v2/README.md`
- **2,210 triangles**, approximately **1.93 m**, one vertex-colour material, unrigged static model. Rigging, deformation checks, collisions and gameplay integration remain undone.

## Latest task: settlement kit — first pass delivered, awaiting user feedback

User requested starter settlement assets matching reference B and proportional to the sailor, with room for assigned workers. They clarified the counts are **5 / 3 / 3 total stages**, not additional upgrades.

Fifteen independent assets are built and exported:

| Asset | Stages/capacity | Triangles |
|---|---|---|
| Campfires | 5: landing fire; stone ring/bench; tripod cooking camp; paved gathering court; permanent masonry-backed hearth/banner | 160 / 396 / 480 / 884 / 1,272 |
| Huts | 2 / 4 / 6 resident beds | 882 / 1,170 / 1,414 |
| Farms | 4 / 6 / 8 planting slots | 1,644 / 2,340 / 3,036 |
| Sawmill | 3 worker spots, log rack and saw equipment | 1,116 |
| Storage | 1 worker spot, shelves, crates and packing station | 1,592 |
| Blacksmith | 2 worker spots, forge/anvil/quench tub | 1,072 |
| Kitchen | 3 worker spots, stove/prep/serving awning | 1,058 |

### Authoritative files

- Blender scene: `tools/blender/source/settlement-kit-v1.blend`
- Generator: `tools/blender/settlement_kit_v1.py`
- Separate GLBs: `tools/blender/exports/settlement-kit-v1/` (15 files)
- Documentation: `docs/art-direction/settlement-kit/v1/README.md`
- Exact footprints, marker roles/positions and triangle counts: `docs/art-direction/settlement-kit/v1/manifest.json`
- Geometry clearance audit: `tools/blender/audit_settlement_exports.py`
- Audit results: `docs/art-direction/settlement-kit/v1/export-audit.json`

### Final review images

Under `docs/art-direction/settlement-kit/v1/`:
- `campfire-progression.png`
- `hut-progression.png`
- `farm-progression.png`
- `workshops.png` (left to right: sawmill, storage, blacksmith, kitchen)
- `interior-cutaway.png` (six-bed hut and blacksmith, roofs hidden for review)
- `campfire_05.png`, `farm_03.png`

`first-pass/` contains superseded previews; do not present them as final. Earlier blacksmith/hut closeups and overview were moved there after the roof fix.

### Verification and remaining integration work

All 15 final GLBs were verified against the manifest. Huts have exactly 2/4/6 resident markers; farms have 4/6/8 planting markers. No detected standing-body geometry conflicts (0.4 m test radius, 1.93 m height) or overlapping 0.55 m worker reservations in the static exported geometry. These checks are not runtime navigation tests.

Static geometry is consolidated: buildings generally have 2 mesh modules, huts 3, farms 5/7/9 (separate crops per bed). Roofs, doors and fire visuals remain independently addressable. Shared palette materials still mean multiple material primitives per module; final batching/atlas/shader integration is deferred.

Roots are ground-level, local units metres. GLBs are Y-up; manifest marker positions and facing vectors are explicitly Blender Z-up. Asset footprints include exterior attachments; reserve additional settlement circulation around them. Beds are 2.1 m long; bed markers reference the bed surface, not a standing spawn point. Farms use one worker location per pair of opposing beds to avoid overlapping workers.

The game's existing person scale is 1.7 m, while this kit deliberately matches the 1.93 m saved sailor. At integration, decide whether to apply uniform 1.7/1.93 scale to BOTH character and structures. Do not independently shrink the buildings.

No colliders, navigation, worker assignment code, upgrades, rigging, crop growth, smoke/fire animations or Unity imports were implemented. Fire and wheat are static preview geometry. Final building design is not yet approved—the user asked to save after seeing the first pass.

### Modeling/rendering notes

The initial long Blender render call timed out at the tool's 300-second limit, but Blender continued working. Waited for completion before further modeling; did not launch concurrent mutations.

A final roof patch removed coplanar shingle-overlap seams, re-exported all 15 assets, saved the Blender file and rerendered hut/workshop images plus the interior cutaway. The main generator includes the corrected shingle offsets. `tools/blender/settlement_roof_review.py` records that one-time patch: **do not rerun it blindly**, since its vertex offsets would accumulate. To regenerate, use `settlement_kit_v1.py` through Blender MCP.

The original artist scene was restored after every modeling operation. No Unity operations were performed. No Git commit was made; do not stage/commit unrelated concurrent changes.

## Next session

Read this handoff and the settlement README, inspect the final four progression/workshop images and cutaway, then follow the user's next requested refinements. Preserve the accepted sailor and keep assets outside Unity until explicitly authorized.

## Skybox integration — 2026-09-17

The user subsequently authorized importing the staged reference-B skybox after Claude finished. The panorama is now bound to the existing Sky.mat through an optional clear-day layer in SeaSick/Sky. SkyDirector's identity and all ocean/weather code were preserved. The layer fades out for dusk/night and storms; regression captures are in `docs/art-direction/skybox-b-unity/`. Read that folder's README for controls and verification. The live SkyDirector check passed. Unity started in Play mode and was returned to Play mode after import; existing dirty scene changes were not saved or replaced.
