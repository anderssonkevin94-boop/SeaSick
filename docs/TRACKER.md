# TRACKER: what's open and what's closed

Owned by the orchestrator session. Kevin's rule (2026-10-04): **close features before opening new ones; no loose ends.**

A feature is **CLOSED** only when every one of these holds:
- It's landed and on the phone.
- Kevin has played its checklist and given a verdict.
- His bugs are fixed, or he has parked them himself.
- The GDD and DEV-TOOLS are updated.
- No TODOs or debug leftovers remain.
- Any queue entry (ASTRAS_READY_ASSETS.md) is removed.

Sessions end every report with a **"loose ends:"** line. The orchestrator copies those lines here.

## In flight

| Feature | State | Waiting on | Owner |
|---|---|---|---|
| Harpoon phase 0: art (bow mount, barb, rope look, 🪝 + marker mock) | in progress; Blender scripts being written in art-staging/harpoon-v1 | Blender window from sailing, then shots → Kevin's approval | Villager |
| Harpoon harpooner crew role (IHarpoonCrewSource on the gun-shift map) | queued after phase 0 shots; seam agreed (HarpoonCrew / IHarpoonCrewSource / BowPost) | phase 0 | Villager |
| Farm-sheet bugs: half-picked field reports full yield; crop change relabels ripe harvest | Kevin: FIX (2026-10-04) | after harpooner role | Villager |
| Harpoon phase 1: hook & reel salvage, markers, 90° arc, snap + 5 s reload, captain-fallback crew seam | in progress | session | Sailing |

| App icon (Kevin's ship-at-sunset art) | committed bc7fd04a, set as the default icon | the next phone build | Orchestrator |

## On the phone, awaiting Kevin's verdict (build 74116cd7)

| Feature | Checklist lives in | Owner |
|---|---|---|
| Kraken steps 1-4 | orchestrator relay, 2026-10-03 | Sailing (kraken session ended) |
| DREDGE controls steps 1-3 (stick, camera, boost punch); bake FEEL values if Kevin tuned | PLAN-dredge-controls.md | Sailing |
| Villager groups 3-4 (Problems chip/list, runner aging, food draft rules) | orchestrator relay | Villager |
| Storage containers + infinite island stacking (closes the ASTRAS queue entry) | orchestrator relay, 11 points | Villager |
| Storehouse runner perks (L1-L3, placeholder model) | orchestrator relay | Villager |
| Fog removed, Explore removed, whole-island landing party, berries (1 Food a bush), honest counts | orchestrator relay | Villager / Sailing |
| Ore: guaranteed on meanR ≥ 100 m islands, 1 in 2 elsewhere | GDD | Sailing |
| Landing fixes: home cast-off, card flicker, "Raider near" reason | orchestrator relay | Villager |
| Cannon crew shift to the engaged side | GDD + GunShiftProbe | Villager |

## Known loose ends (each needs a fix or Kevin's "park it")

- Harpoon: lantern DECIDED (Kevin 2026-10-04): it hangs from a short beam over the bow, just below the harpoon gun, lighting the way forward. In the phase 0 art (villager); closes when Kevin approves the shots.

- Cannon shift: nothing on screen shows the 3 s wait. Untested: switching the locked side mid-walk, and the ~6 s walk on a big hull.
- Landing: false "Sheer cliff" off bays and headlands; islets under 3000 m² have no landing.
- Problems list: the farm has no line. Alert-strip chips are 36 units tall, under the 44 pt touch target.
- RescueHud world tap marker overlaps the bottom strip by 168×37 px in portrait (pre-existing).
- Storehouse has no model (placeholder hut). L3 is gated on fire II because Campfire III doesn't exist.
- Storage: edit-mode lighting looked muddier than the Blender preview. Confirm on the phone.

## Parked by Kevin (not loose ends; revisit deliberately)

- Full shipboard crew priority list (rescue → bail → engaged guns / harpoon → sails/oars). Only the cannon shift is built.
- Boost fuel cost, once a fuel system exists.
- Harpoon barb ammo (iron); ammo is unlimited for now.
- Art queue (ASTRAS_READY_ASSETS.md): resource kit, worker tools, sea discovery kit; F18/F19 entry awaiting "F30 replaces it".
