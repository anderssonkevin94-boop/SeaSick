# Playtest feedback — running list

Kevin plays on the iPhone; each point he raises goes here as he says it, and is
ticked off when a build carrying the fix has been handed to him. **Open** items
are worked top to bottom unless he reorders them. Newest session at the top.

Status: `open` → `fixed (unplayed)` → `done` (he played it and it worked) or
`reopened`.

## 2026-09-23 (iPhone, build from the open editor)

| # | Feedback (his words, trimmed) | Status | What was done |
|---|---|---|---|
| 1 | Walls can't be built because trees/grass/stones cover the plot | fixed (unplayed) | CLEAR phase: any blueprint may sit over trees and resource rocks; villagers clear the plot before construction. Unclearable ground still refused. |
| 2 | Third palisade stems from the first post's origin; red lines everywhere | fixed (unplayed) | `Outpost.TooCloseLines` only matched shared posts start-to-start, so every chained piece (and ⭯) was refused. Chain now continues from the last post. |
| 3 | Drop the campfire area; choose the town centre anywhere; crew walk from where you docked | fixed (unplayed) | No pre-made clearing, no ship ring; `Outpost.TownRadius` 40 m once the centre stands (piers exempt); crew start at the gangway and walk. |
| 4 | Villagers should take initiative: gather and build blueprints without asking | fixed (unplayed) | `OutpostLedger.EnlistFree`: idle hands work the queue; explicit orders untouched. |
| 5 | Three-part wall + hut: one segment built, nothing else, though people are "assigned" (persisted after the first fix) | fixed (unplayed) | Reproduced in the editor: a fresh camp has no food, mood falls 0.5/day, work = mood/0.5, so ~2 game days (6 min) in every builder does exactly zero. Fed, the same queue builds end to end. Kevin chose: each hand dropped off brings 3 days of rations (`ProvisionDays`), starving hands work at 35% (`StarvingWorkFloor`) instead of 0, and free hands go hunting on their own when food runs out (`FeedFirst`). Site sheet says why a site is slow/stuck. |
| 6 | Landing zooms out to the whole island; too far, and the zoom is so slow it feels like waiting | fixed (unplayed) | Landing frames a close shot (ship + ~30 m, or the town at its 40 m reach) instead of the old 80 m ring / survey view; overview blend settles in ~2 s instead of ~5.6 s. |
| 7 | Fire half done before anyone cut wood or even reached it; gather first, then build | fixed (unplayed) | The ledger paid work by the clock while the crew were still walking up from the ship (new today). A hand set ashore in view now does no work in the books until its body starts its first job (`OutpostHand.walkingIn`, 120 s failsafe). Site sheet says "still on their way up from the ship". |
