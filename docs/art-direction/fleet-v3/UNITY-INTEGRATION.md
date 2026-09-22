# Approved fleet — Unity integration

The user authorized implementation of all 20 fleet-v3 ships on 2026-09-17. This supersedes the older art-only restriction for these ships. Claude's existing ocean and other project changes were left in place; the open, dirty Sea scene was not saved or replaced.

## Runtime path

- `Resources/Ships/FleetV3/Ship01.prefab` through `Ship20.prefab`: native Unity prefabs, with shared palette material and baked mesh assets under `Art/FleetV3`.
- `ShipLadder` loads `Ships/FleetV3/ladder` before the legacy manifest. Node IDs stay 0–19 and existing bay identities / upgrade move order remain intact.
- `Shipyard` loads `FleetVisual`, replacing the old ship-13-only exception. It sizes the hull collider, buoyancy envelope and wave response from the new dimensions.
- Cannon totals: **0, 0, 0, 2, 2, 4, 4, 6, 6, 8, 10, 12, 14, 16, 16, 16, 18, 20, 22, 24**. These fitted cannons are included with hull upgrades; battery quantity is no longer an independently added/removed bay use on this fleet. Calibre and crew training remain available.
- Cannon carriages and barrels retain the approved meshes. Runtime instances use the existing Cannon behaviour for aiming, recoil, smoke, reload and projectiles. Opposing cannon pairs share one named gunner; the 24-gun endgame requires 12 gunners, within the existing 20-person roster limit. Readiness still requires an available gunner.
- Sails, yards and canvas detailing export together and trim about mast stations. The authored sail size stays fixed to avoid clipping the rigging; sail-area fittings retain their speed/acceleration effects.
- Source render-only sailors, lights, camera and studio water are excluded. Existing live crew are positioned on the deck / stern. Authored furniture is retained and duplicate legacy kit furniture is suppressed.
- Waterline is Unity Y=0, bow +Z, units metres. No global scale change is applied to the hulls or existing people.

## Rebuild

Run `tools/blender/export_fleet_unity.py` in background Blender. It reads the approved `tools/blender/source/fleet-v3/*.blend` files and writes modular `.fleetmesh` files plus metadata to `tools/blender/exports/fleet-v3-unity/`. In Unity choose **SeaSick → Art → Import approved fleet V3**. Re-import preserves native asset GUIDs.

The fleet-specific two-sided vertex-colour shader renders both canvas faces and the source deck faces correctly. It does not alter the crew, terrain or ocean shaders.

## Verification and limits

`unity-import-validation.txt` records imported triangle/gun/sail counts. `unity-prefab-validation.txt` checks all 20 prefabs, palette shaders, barrel references, and nondecreasing cannon counts and hull dimensions. `unity/runtime-validation.txt` exercises the real Shipyard.Apply path, all 20 stages, live cannon firing, sail pivots and hull colliders, and verifies that the live PlayerShip uses the new art.

Unity desktop (1920×1080) and portrait (1080×2340) review renders are in `unity/`. Studio review renders exclude HUD; live renders use the gameplay camera.

Buoyancy uses the existing measured hydrostatic curves rescaled by each hull's length, beam and draft. This is a calibrated gameplay proxy, **not a fresh watertight displacement integration of the decorative hull**. Full storm balancing and mobile LOD/performance tuning remain separate validation work. These are detailed player-ship assets; do not spawn the entire full-detail fleet as distant traffic without LODs.

The live Sea check loaded stage 13 with 14 cannons at the dock: measured root height 0.16 m, GM 1.35 m and load sinkage approximately zero. Three full runtime validation passes completed. The open dirty Sea scene remained unsaved and Unity was returned to edit mode.
