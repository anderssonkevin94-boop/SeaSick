# Spec: runners (store hut haulers)

Approved by Kevin 2026-10-02 (morning, with the supper/hauling batch), restated 2026-10-02 evening after the first build only did half of it. This file is the contract: every line is checked in-game on a copy of Kevin's latest save before the feature is called done, and the build notes say per line what he gets. Full design text: `docs/GDD.md`, "Runners and full loads".

## Acceptance checklist

1. A runner is a villager assigned to the store hut: 2 slots at store level 1, 4 from level 2. He pushes a wheelbarrow (about 3 armfuls).
2. Runners do **all** the moving in the camp: station inputs in, station outputs out, blueprint materials, dropped loads, ship transfers.
3. While at least one runner is on the island, a station worker **never** walks to the store or its piles for inputs. He stays at his bench, working or waiting ("waiting for hide · runners bringing it").
4. Every station is fed by runners: sawmill (logs), quarry (stone), kitchen (its ingredients), hunting lodge (bow and arrow inputs), forge, and any station added later.
5. Priority: an idle bench waiting for an input that exists in the camp comes first. Then blueprints, then outputs home only where there is room.
6. A runner never loops or jitters on a trip that can't finish. Two runners never chase the same job. With nothing to carry he waits calmly at the store ("Runner, waiting"), which is not idle.
7. A full store gives one clear alert that names the fix ("Store full of boards · build or upgrade a store hut"), never cut off.
8. A runner stays a runner until the player changes it. No path failure, mood, hunger, raid, supper or load ever removes the assignment. A runner who can't reach a spot shows as Stuck.
9. Without runners, hauling falls back to the old rules (workers and idle hands fetch).

## Status

- 2026-10-02 first build (7125adf): only lines 1 and part of 2 (rack → store) worked. Line 3 failed (workers still fetched), line 8 failed (a failed path search wiped the job).
- 2026-10-02 23:50, fix verified in the editor on Kevin's save a3 (day 512, Island_6, runners Mabel + Cass), skipped to 08:00:
  1 ✅ both stay Work@Storage · 2 ✅ potatoes → kitchen, baked potato/tools → store · 3 ✅ Edda never left the lodge · 4 ✅ kitchen fed (lodge/quarry had no recipe chosen, nothing to feed) · 6 ✅ runners stood still while waiting, no jitter · 8 ✅ (path give-up keeps the order) · 7 ⚠️ store-full alert exists, but Finch's status said "runners taking it away" while the store was full of fish (being fixed with two other false statuses: "Working" with no recipe chosen, "food emergency" with 90+ food).
  Lines 5 and 9 not exercised in this save.
