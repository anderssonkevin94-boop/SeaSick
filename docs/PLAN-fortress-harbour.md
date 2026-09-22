# The fortified harbour — plan

Written 2026-09-23 (00:45) against `ships-into-unity` @ `7820c7e`, the first
night the game ran on Kevin's iPhone. **Read this before starting any phase;
update it when a phase lands or a decision changes.**

Kevin's brief, with his screenshot of a camp on a wooded island: *"I'm having
grand dreams of building the islands up like something from Age of Empires. I
see in my mind walls, watch towers, a big dock with friendly boats (fishing and
military). Enemies come by sea and drop foes on land that try to get through
the walls."* Then: *"I absolutely love all of this."*

## The line we hold

**You are the ship, not a general.** Age of Empires hands you a hundred units
to click; this game hands you one hull and a camp that runs itself (the B&W2
half, `docs/PLAN-island-outposts.md`). The fortress is something you BUILD and
PROVISION between voyages; when the raid comes you fight it from the water
while the walls and towers do their part ashore. Sailing stays pillar #1
(GDD §2). No unit selection, no rally points, no click-to-attack ashore: hands
are given jobs through their sheet as today, defences act on their own.

Phone first (CLAUDE.md): every new control is a thumb gesture or a sheet page;
every new thing on the island must survive `Mobile_RPAsset` with a dozen
raiders, a dozen hands and the animals on screen.

## What already exists (reuse, do not rebuild)

- **Raids by sea, live:** `Combat/RaidDirector` picks the moment, `EnemyShip`
  (`Duty.Raid`) runs for the beach, `RaidParty` lands `RaidWalker`s that steal
  from `CampPiles`; `RaidSite`, `RaidBanner`. Raiders wear the fleet's five mid
  hulls (`FleetVisual`, `FleetGun`).
- **A tower with a gun:** `World/BuildPlans` watchtower + `Combat/WatchtowerGun`
  (fires while a lookout is posted; raiders shoot towers first). Arrows from
  the fletcher (GDD decisions log 2026-09-21).
- **Harbour:** `World/Dock`, `Pier`, `PierDocks`, `HarbourSite.Find`, the pier
  as a placeable plan (`CanPlacePier`), `AnchorController` berthing.
- **Walking on land:** `World/CampPath` (grid A*, slope-blocked; props are NOT
  obstacles yet) used by `CampWorker.Walk`. Raid walkers do not use it yet.
- **Build pipeline:** `OutpostLedger.sites` queue, STOCK-then-BUILD phases,
  `Outpost.CanPlace`/`Clear` with named refusals, `CampSiting` (tap / drag /
  ✕ ↻ ✓), `BuildingFactory`, sheets (`FireSheet` build page, `SiteSheet`).
- **Friendly hulls:** the ship ladder's 20 rungs and the fleet FBX set are the
  same meshes a fishing boat or a patrol launch would wear.

## Phases — each one playable on the phone on its own

### Phase 1 — Walls and gates  (first; "raiders hitting a wall is the moment the vision becomes real")
- **Siting:** a LINE tool in `CampSiting`: press on the ground, drag, release
  → a run of wall segments from A to B, snapped to a 2 m step, each segment a
  site in the queue (cost per metre: timber palisade first, stone wall as a
  later tier). A gate is a wall piece placed on an existing wall (tap a
  segment → its sheet → "make this a gate"). Corners/ends get a post.
- **Obstacles:** walls (and, at last, rocks/buildings) become blocked cells in
  `CampPath`; a gate is passable for hands, closed to raiders. Rebuild the
  affected cells on raise/cancel, not the whole grid.
- **Raiders vs walls:** `RaidWalker` moves on `CampPath` like a hand. A party
  with no open route paths to the NEAREST wall segment and breaks it (segment
  HP, hits per second per raider; a palisade holds N raider-seconds). Breached
  segment → passable, drawn broken, re-queued as a repair site the hands
  stock and build like anything else.
- **Acceptance:** a camp ringed by a palisade with one gate; a live raid lands,
  the party walks to the wall, breaks a segment, gets in, steals; the hands
  rebuild it after. Every step visible from the deck.

### Phase 2 — Towers
- Wall tower plan (goes ON a wall like a gate). A crewed tower (one hand,
  "on watch") fires `WatchtowerGun`-style at raiders inside its ring; the ring
  is drawn on the ground while the tower's sheet is open. Range and rate per
  tier; arrows from the fletcher raise both. Raiders target towers first
  (already the rule) and can burn a tower down (repair site again).
- **Acceptance:** two towers covering the gate stop a 4-man party without the
  ship's help; a 12-man party gets through.

### Phase 3 — The harbour
- Pier → dock upgrade (bigger footprint, two berths, storehouse ceiling bonus).
- **Fishing boat** (small fleet hull, friendly): leaves at dawn, returns at
  dusk with food onto the pile; a hand crews it (the boat is that hand's
  "job"). Lost if a raider ship catches it at sea → an event on the chart.
- **Patrol launch** (mid hull, a gun): guards the bay, engages raider ships on
  approach; its hand is its crew. `IFriendly` exists for this.
- **Acceptance:** a camp feeds itself from the sea; a raid is met on the water
  before it lands.

### Phase 4 — Tiers (the town grows in visible stages)
- Camp → hamlet → walled town → harbour: rung-style pricing (GDD §7, "the
  sink") applied to buildings; palisade → stone wall; watchtower → bastion;
  pier → dock → quay. Higher tiers unlock as the camp's population/ledger
  crosses lines the sheet states plainly.

## Decisions — SETTLED by Kevin, 2026-09-23 00:50
- **D1 yes** — wall cost per metre: 1 log / 2 m of palisade, hauled like any site; stone wall is a later tier.
- **D2 yes** — raiders never climb; they break a segment or use an open gate.
- **D3 yes** — gates are automatic: open for hands, closed to raiders, nothing to toggle.
- **D4 yes** — rocks become blocked cells for everyone; trees are felled at raise time as today.
- **D5, Kevin's own design — connect the dots.** *"I have it pressed, move
  the other post around until I like it, press confirm and then continue."*
  Arm the wall plan → tap the ground to plant the FIRST post → the NEXT post
  follows the thumb (press and drag it, exactly like dragging a blueprint;
  the segment between the two posts is drawn live, green/red with the
  refusal text) → ✓ confirms that segment and the run continues from the
  confirmed post (the next post appears already attached) → ✕ ends the run
  (a run with no confirmed segment cancels the tool). Each confirmed segment
  is its own site in the queue at once, so hands can start hauling while you
  keep drawing. A post is shared between adjoining segments. **Gates are
  placed on existing walls:** tap a built segment → its sheet → "make this a
  gate" (a gate site that replaces the segment when built).
- Wall segment length: post-to-post, free length, capped at ~12 m so a
  segment is one site; longer drags split at the cap. Snap the next post to
  the 2 m step of `CampPath` cells so the obstacle cells are exact.

## Verification
Kevin plays it on the phone (he is the probe). Compile via
`tools/compilecheck.sh`; build via the recipe in `docs/DEV-TOOLS.md` ("the
game on an iPhone"). Probes only for what cannot be playtested (raider path
cost with 12 walkers on `CampPath`: a `CostProbe`-style frame-time read).
