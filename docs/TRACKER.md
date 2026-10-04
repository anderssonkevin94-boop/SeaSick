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

Last on the phone and pushed: **f581a046** (2026-10-04).

## Sailing session: queue (in order)

1. Harpoon gun-lamp: code + art on HEAD (b8f6fb12, 3e1aeeda), beam on branch lamp-beam. 4 shots → Kevin's OK → land the beam. The CoasterOutfitting stale-module question is confirmed or fixed with evidence. Stale renders cleaned. **The next phone build waits on this.**
2. Loose-ends sweep:
   - RescueHud tap marker overlaps the bottom reserve (168×37 px, portrait).
   - Kraken items never verified in play: a cannonball on a raised arm, the warning camera, the edge chevron, the Settings row layout, the desktop ship height, the loot crate size.
   - Harpoon phase 1 untested paths: kraken loot, wreckage, a miss, the SmoothnessMeter during a reel vs a snap.
3. Stop. Harpoon phases 2-5 wait for Kevin's phase-1 verdict.

## Villager session: queue (in order)

1. Spit removal at Kevin's home island (branch spit-edit): before/after shots on a COPY of his save → Kevin's OK → land.
2. StationStockSelfTest: 19 of ~90 fail (walking legs). Baseline at f581a046, then fix to green.
3. Harpooner crew role (IHarpoonCrewSource; the captain fallback is live until then).
4. Farm-sheet bugs: a half-picked field reports the full yield; a crop change relabels a ripe harvest.
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

**Open question:** the Storehouse's look (Kevin asked whether an existing asset fits).

## Parked by Kevin (not loose ends; revisit deliberately)

- Full shipboard crew priority list (rescue → bail → engaged guns / harpoon → sails/oars). Only the cannon shift is built.
- Boost fuel cost, once a fuel system exists.
- Harpoon barb ammo (iron); ammo is unlimited for now.
- Harpoon phases 2-5 (land pull, raiders/boarding, crew upgrades, next-level), after his phase-1 verdict.
- Art queue (ASTRAS_READY_ASSETS.md): resource kit, worker tools, sea discovery kit; F18/F19 entry awaiting "F30 replaces it".
- Player "dredge" tool (the spit fix is a one-off).
