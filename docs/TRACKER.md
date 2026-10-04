# TRACKER: what's open and what's closed

Owned by the orchestrator session. Kevin's rule (2026-10-04): **close features before opening new ones; no loose ends.**

A feature is **CLOSED** only when every one of these holds:
- It's landed and on the phone.
- Kevin has played its checklist and given a verdict.
- His bugs are fixed, or he has parked them himself.
- The GDD and DEV-TOOLS are updated.
- No TODOs or debug leftovers remain.
- Any queue entry (ASTRAS_READY_ASSETS.md) is removed.

Unapproved visuals stay on a branch until Kevin OKs the shots. Sessions end every report with **"loose ends:"**.

Last on the phone and pushed: **c08ee639** (2026-10-04, 19:23): the farm rework + glyph sweep, the Storehouse merged into the store hut, the harpooner role, station work in place, L2 walls + kitchen/tower/mill winding fixed, pathing, kraken no-cancel + HUD framing/chevron. Before it: 0053f72f (fire button, gun lamp, docking/castaways, spit, arcs, stall lines, icon).

## Sailing session: queue (in order)

- ~~Lamp at noon~~: it was the firing arcs, FIXED 0053f72f, on the phone.
- ~~Villagers path into buildings~~: LANDED 32c252b4 (wall-clear FreeSpot included). Kevin's a3 save, 40 trips: longest pin 3.6→0.5 s, 0 overlaps, watchtower OutputSpot reached; 2 long trips hit the probe's 60 s clock while still walking. Not on the phone yet.
- Kraken/harpoon SweepCheck: LANDED (07666fc0..dab3a75b): ship framed above the combat row with her hull clear (camShipHull01), edge markers above the reserve, RescueHud steer zones off reserved panels, swats can't be cancelled (arm_hit PASS in play: windup 2.50/2.60 s, one slam, 0.35 damage). Portrait + desktop ship_height/chevron PASS. Loot (Kevin 2026-10-04): meat 1.6 m / ink 0.9 m, NO harpoon ring on the haul (button still targets it) LANDED 95afbab6, loot_size 9/0. Kraken surfacing astern frames from far out: Kevin keeps it (closed). Not on the phone yet.
- Top-down camera zoom (Kevin 2026-10-04, DREDGE): sea camera backs off with the downward tilt, 1.4x at pitchMaxDeg (FEEL topDownZoom/topDownZoomCurve) LANDED 95afbab6. Phone 32->37.5->45 m, desk 31->37->48 m at 0/15/30 deg; ship clear of the bottom HUD at every tilt; kraken framing still overrides at full tilt (ship_height 6/0). Not on the phone yet.
- RescueHud steer zone in the desk Wheel slot (villager's probe, 152x36 px permanent): half-built HudLayout.Issued read; FIXED 30936da0 (OverlapsReserved/TryIssued, also HarpoonMarkers + edge-marker stack). Desk + phone census 0 overlaps.
- ~~L2 walls look shattered~~: inside-out faces. FIXED 562ac008 (WallL2 wall + gate, KitchenL1, TowerL2, GrainMillL1; winding only, vertex delta 0, in-engine shots solid); guard `tools/blender/check_winding.py` e06ab2b0. Sweep DONE (469 FBXs, 8 faulty): Wheelbarrow, L2 sawmill, blacksmith, gull patched in place (tools/blender/winding_patch.py) and landed; 4 unused faulty models left unfixed (DEV-TOOLS). CLOSED.
0. **Kevin's phone bug (harpoon phase 1):** the fire button is unreliable, and taps hit the ship. Fire only from ONE FIXED on-screen button (≥64 pt, above ⚡); marker taps REMOVED (indicator only); every tap reader gated on the button rect; HarpoonTapCheck. In progress. Ships with the lamp build.
1. Harpoon gun-lamp: code + art on HEAD (b8f6fb12, 3e1aeeda). **Kevin APPROVED with tweaks** (one continuous beam; warmer yellow-orange) → re-shoot check → land the beam (lamp-beam). CoasterOutfitting stale-module: CONFIRMED + FIXED (on the phone). Stale renders cleaned. **The next phone build waits on this.**
2. Loose-ends sweep:
   - RescueHud tap marker overlaps the bottom reserve (168×37 px, portrait).
   - Kraken items never verified in play: a cannonball on a raised arm, the warning camera, the edge chevron, the Settings row layout, the desktop ship height, the loot crate size.
   - Harpoon phase 1 untested paths: kraken loot, wreckage, a miss, the SmoothnessMeter during a reel vs a snap.
3. Stop. Harpoon phases 2-5 wait for Kevin's phase-1 verdict.

## Villager session: queue (in order)

- ~~Castaway/docking fix + un-kill Bo/Pip/Ola + spit (C)~~: ON PHONE 0053f72f.
- ~~Station walk-around~~: LANDED 02b3fe1e (next build).
- ~~STOREHOUSE MERGED INTO THE STORE HUT~~: LANDED 0ac21313..7929870a, StorehouseMergeSelfTest 56/56, refund +25 timber +4 stone (next build). Store hut L1/L2/L3 carry runner posts + perks; the Storehouse is removed (build list, code); one-time save repair removes his Storehouse with a refund and reassigns its runners. The "Storehouse look" task is CANCELLED.
- Sweep additions: HOME BERTH eyebrow; smoke shot in play; flour auto-pause proposal (ask Kevin).
1. Spit removal at Kevin's home island (branch spit-edit): before/after shots on a COPY of his save → Kevin's OK → land.
2. StationStockSelfTest: GREEN 93/93 on branch stationstock-green (old failures: day/night harness gap since 8099268f + stale timings; no regression) -- awaiting landing. Minor ordering note (top-up vs hammering) in DEV-TOOLS.
3. ~~Harpooner crew role~~: LANDED 85dda591/905ef499/b6ac9c59, HarpoonCrewRules 29/29 (next build). Sweep: the bow-rail heave spot in rough sea.
4. **FARM SHEET REWORK (Kevin: "broken, needs to be reworked")**: tofu □ glyphs (check all sheets), the overlapping plot-detail view, no actions/Assign on the grid; plus the 2 farm bugs + farm Problems lines. Mockup → Kevin's OK → build.
5. Loose-ends sweep:
   - Cannon-shift cue during the 3 s wait, plus its untested cases.
   - Problems list: farm lines; alert chips ≥44 pt.
   - False "Sheer cliff" off bays.
   - IngredientLine worn-tool display.
6. Stop.

Landed, not on the phone yet (rides the next build): the stall lines name every missing input and tool, worn tools show their life ("saw blade · 90% left"), multi-spot benches (c77f1f60/5d31cc42); probe updates 5abe3f90.

## Kraken session ("Resource stacking"): finished

Its kraken steps 1-4 are on the phone. Its unverified items moved to the Sailing sweep above.

## Waiting on KEVIN

**Open questions:**
- Kraken loot: bigger crates (branch kraken-loot-scale) + smaller markers floating ABOVE targets?
- Kraken surfacing astern pulls the camera out to ~140 m (pre-existing). Does it bother him in play?

**Sailing: QUEUE DONE (54d4fef8)**, landed for the next build: kraken loot bigger + no marker, top-down zoom 1.4x (FEEL), the HUD reserve race fix, winding sweep closed (469 FBX; wheelbarrow, L2 sawmill, blacksmith, gull patched in place). Idle. Harpoon phase 2 waits on Kevin's phase-1 verdict.
**Villager next:** its sweep (crop icons fill the card, chart subtitle/labels truncation, desk header under Move, cannon-shift cue, 44 pt chips, sheer cliff, IngredientLine, repair timber stays aboard, HOME BERTH, smoke shot, flour auto-pause proposal, station-anim polish, harpooner in rough sea).


**Play verdicts** (on the phone):
- Kraken 1-4.
- DREDGE controls 1-3; FEEL values to bake if he tuned any.
- Harpoon phase 1 (does the snap tuning feel right?).
- Cargo fix: home shows T~41-45 / S26 / O20; the crew carry cargo ashore.
- Storehouse runner perks.
- Villager groups 3-4 (Problems list).
- Fog/Explore removal + whole-island landing party + berries + honest counts.
- Ore rule.
- Landing fixes.
- Cannon crew shift.
- New app icon.

**Decided 2026-10-04:**
- Repair timber stays aboard at home (keep a repair stock). → Villager.
- Islets under 3000 m² without a landing: fine as is.
- Storage containers look fine on the phone.

- Storehouse: MERGED into the store hut (Kevin); his Storehouse is removed with a refund.

## Parked by Kevin (not loose ends; revisit deliberately)

- Full shipboard crew priority list (rescue → bail → engaged guns / harpoon → sails/oars). Only the cannon shift is built.
- Boost fuel cost, once a fuel system exists.
- Harpoon barb ammo (iron); ammo is unlimited for now.
- Harpoon phases 2-5 (land pull, raiders/boarding, crew upgrades, next-level), after his phase-1 verdict.
- Art queue (ASTRAS_READY_ASSETS.md): resource kit, worker tools, sea discovery kit; F18/F19 entry awaiting "F30 replaces it".
- Player "dredge" tool (the spit fix is a one-off).
