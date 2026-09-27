# Delivery on arrival (2026-09-27)

Kevin, 2026-09-27: *"I don't want time to play a role in any construction or
whatever. If it takes a villager x amount of time to walk somewhere, that's
just what it takes. A villager walking with that resource has that resource on
him and it gets where it gets when it gets there ... Only when a villager has
delivered the object will the object be counted."* Hunting: *"walking up to an
animal, jabbing it with its spear, picking it up and walking back. There
should be no arbitrary timer."* Unwatched camps: **invisible walkers**.

## The model

A trip is no longer a timer (`haulLeft`, `2 x leg x PathFactor / speed +
handle`). It is a little state machine on the hand (`OutpostHand.tripLeg`):

| leg | what he is doing | what changes in the books |
|---|---|---|
| `ToPickup` | walking to the source, empty-handed | nothing (the load is *planned*: it reserves room at the destination and the site's need, and is a *claim* on the source so two hands do not go for the same last log) |
| `AtPickup` | stationary work: cutting a tree/rock (Kevin's 5/8/10 s per unit), a 1 s stoop at a pile/rack/bay, a jab at an animal | at the END: **pickup** -- the source is decremented now (`PickUp`), clamped to what is really there; nothing there = the trip ends empty |
| `ToDrop` | carrying | nothing (the load is on him: `haulRes`/`haulCount`, `haulPicked`) |
| `AtDrop` | at the drop-off | **drop-off** -- store / station bay / build site / ship hold gains it now (`DepositHaul`); a full store keeps him standing there holding it |

**Stock changes only at pickup and drop-off events.** Stationary timers stay
where a villager stands still and works: cutting at the source, the bench
recipe timer ("56 s until spear"), clearing a plot, hammering a stocked site
(only while a builder is ON the site).

## Who moves the walker

- **Watched camp:** the body (`CampWorker`) is the walker. It walks its real
  `CampPath` route, writes its position into the hand (`wx/wz`), and calls
  `BodyArrived` / `BodyWorked` on the ledger. The ledger never advances a
  driven hand's leg (`OutpostHand.driven`, not saved).
- **Unwatched camp:** the same ledger `Step` advances each hand's leg
  headless at `WalkMetresPerSecond` (2.6 m/s, = `CampWorker.Speed`). The leg
  length is the camp's `CampPath` route when the grid exists (planned once per
  leg through `OutpostLedger.router`), else the straight line. No
  GameObjects. Cost: a few float ops per hand per step, one route plan per leg.
- **Hand-off:** body disabled -> the walker continues from the body's last
  position (leg re-measured). Arrival -> bodies are placed at their walkers'
  positions and carry what the walker carries.
- Walking is NOT scaled by hunger (`WorkFactor`): a walk takes what it takes.
  Stationary work still is.

## Hunting

Claim an animal (the herd count, `GameUnclaimed`), walk to it (the body: the
real beast; headless: the herd's last measured distance), a short fixed jab
(`JabSeconds`), the animal dies at the jab (`HuntKill`: herd -1, spear wear,
arrow), carry the carcass home, 4 Food + 1 Hide at the store on arrival. No
stalk timer, no kill-rate schedule. Limits: a beast present, a spear, room for
meat or hide.

## Save

Additive `OutpostHand` fields (JsonUtility, safe defaults): `tripLeg`,
`haulPicked`, `legLeft`, `workLeft`, `wHas`, `wx`, `wz`, `basket`. An old save
in mid-trip (`tripLeg == 0` while hauling) migrates to `ToDrop` with the load
picked (the old books took it at dispatch); a hunt not yet killed migrates to
`ToPickup`. Hands with no position start at the camp centre, empty-handed.

## Readouts

`TripDays` / `GatherTripPerDay` / `HuntTripPerDay` remain as DISPLAY
estimates only (distance / 2.6 m/s + cutting). Nothing books through them.
`DeliveredPerDay` is a measured moving average of real drop-offs.

## Removed

`haulLeft/haulDays/haulWalkDays/haulWorkDays` booking (fields kept for old
saves, unused), `HuntStalkSeconds`, `HuntDaysToKill`, the body's
`SecondsToDeposit`/`TripFactor` mime, the siting walk line.
