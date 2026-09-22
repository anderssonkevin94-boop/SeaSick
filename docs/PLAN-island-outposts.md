# Islands as Outposts — plan

Written 2026-09-13 against `ships-into-unity` @ `5483817`. Design note published
as the artifact **"Islands as Outposts"**:
https://claude.ai/code/artifact/793fef57-7c91-48af-b6bc-5b7c0dd1c641

Kevin's brief: dock at an island -> bird's-eye view -> choose who disembarks ->
order them to harvest or make camp -> a campfire grows into a settlement ->
sail away leaving them working -> come back to progress, and decide whether to
collect crew, collect resources, or re-order and leave again. Islands become
investments.

**Verdict: the loop is right.** It gives an island a past, and it gives the bay
system a second buyer for berths (a hand ashore is an empty berth at sea —
`CrewAgent.Available` already gates every ship system, so no new stat).

## The two risks

1. **Unbounded absentee income** would make the optimal play "drop one man
   everywhere and sail in circles", turning the sea into a loading screen.
   **Fix: a camp has a stockpile ceiling** — exactly `Village.beachCapacity`
   (30 at home, +40 per storehouse). Campfire holds 10. The ONLY way to raise
   the ceiling is to build. Self-limiting with no timer and no decay curve.
2. **The sailing becomes transport.** The camp is where value accumulates; the
   sea is where it is at stake. Never let a camp ship cargo home by itself.
   Phase 2's provisions requirement is the main defence (see below).

**SETTLED 2026-09-13 — standing timber depletes and regrows slowly.** Each
outpost carries a wood stock the tick decrements; an exhausted camp stops
producing until it recovers. Without this, camps produce from nothing forever
and risk #1 returns through the side door. It also makes big wooded islands
genuinely worth more than small ones, and gives a reason to move on and return.

## Five decisions before any code

- **D1 — DONE 2026-09-13, `81d1d4e`, probe-green.** See the section at the end.
  Original statement of it: **Home is outpost zero.** Generalise `World/Village.cs` into a per-island
  `Outpost`; `Settlement` stays the ground survey; `Stockpile` becomes
  per-outpost. Do this FIRST. A day now, a week later.
- **D2 — The ledger is the truth; walking crew are a rendering of it.** An
  outpost is a few numbers + `lastTicked`, advanced in one closed-form step
  against `TimeOfDay.Seconds` on arrival or query. **The same rate applies while
  present** — otherwise watching beats leaving.
  *Acceptance probe (write it first): two hands, one in-game day, present vs
  absent, within 10 %, both measured off stockpile contents.*
- **D3 — Key an outpost by rounded world XZ, never by island index.** Islands
  are flood-fill-discovered in streamer order; the seed is stable, the ordering
  is not.
- **D4 — There is no save system.** Verified: `PlayerPrefs` appears only in
  `UI/HudVisibility.cs` and `Ship/ShipLadder.cs`, neither game state. Build the
  ledger as a plain serialisable struct from day one. A save must also carry the
  world seed + `worldOffset`, the ship's rung/fittings, and home.
  _Built 2026-09-21 — see "Save and load" at the end of this file. The ledger
  went in exactly as it was; the two things it was missing were building
  positions and the camp centre._
- **D5 — SETTLED 2026-09-13: a bottom sheet.** Orders live in a thumb-height
  sheet in the lower third, where `HudLayout`'s prompt slot already sits. Not a
  full overlay (it would hide the island you just flew up to look at) and not
  pure direct manipulation. One-handed, doesn't fight the HUD.

## The puppet model — how crew survive being left (settled 2026-09-13)

Kevin: *"they dont need to be spawned in the game if we can caluculate the
statistics of their progress right?"* Correct, and it is cheaper AND better.
**Invert which one is real: the outpost is always numbers, and a `CrewAgent` is
a puppet it puts on screen while the player is looking.** Then there is no
despawn event to get wrong — there is only "stop drawing".

**The row** (plain serialisable struct, no MonoBehaviour): position key, which
`CrewMemberDef`s are present, each one's order, stockpile, ceiling, buildings +
build progress, standing timber, provisions, `lastTicked`.

**`Tick(now)`** is a pure function of elapsed `TimeOfDay.Seconds` — no scene, no
terrain, no island loaded. Called on approach, on a minimap query, on save.
**Idempotent** — calling it twice in a frame must do nothing the second time.
Worth a test of its own.

Three moments:

- **Ordering writes the row, NOT cast-off.** Writing at departure gives you two
  sources of truth for the whole time the player stood watching, and that gap is
  where the desync lives.
- **While present**, agents walk and chop but the stockpile number comes from
  the tick. The man delivering a log is the *animation* of the tick having
  incremented. So felling must be DRIVEN by the tick: when the tick says a hand
  produced a log, a tree drops. Felling is an effect of production, not its
  cause.
- **On approach**, tick to now, then spawn bodies for whoever is there. Their
  positions are not saved and do not need to be — nobody can tell where a man
  was standing three days ago.

**Making the mesh agree after a long absence.** `SceneryWood.NodeHarvested`
takes an INDEX, so a camp would have to record which trees it felled. Instead:
**fell in deterministic order, nearest-to-camp outward, and store only a count.**
The whole visible state reproduces from one integer — and it buys a free detail
nobody has to author: a camp running twenty days sits in a widening ring of
stumps.

## Inventory — what exists

| Piece | State | Needs |
|---|---|---|
| `CameraRig/ChaseCamera.cs` `Overview` | have | The bird's-eye shot, hand-tuned (32° tilt, 36° lens, 165 m ground, 1.7 m man >= 13 px). Point it at any island. |
| `Crew/CrewAgent.cs` | bend | Walks ashore, claims, chops, carries. Carry to the CAMP, not the hold; despawn into ledger on cast-off. |
| `World/ResourceNode.cs` + `Terrain/SceneryWood.cs` | have | Real trees in the welded mesh, felled via `NodeHarvested`. |
| `World/Village.cs`, `Stockpile.cs` | bend | Capacity ledger + siting on unflattened ground. Make per-island (D1). |
| `World/BuildPlan.cs`, `BuildingFactory.cs` | have | Data catalogue, primitives at runtime. Add entries. |
| `World/Settlement.cs` + `Terrain/SettlementSite.cs` | bend | Run per island on first landing. |
| `World/Time/TimeOfDay.cs` | have | Tick source. Day = 180 s, so a camp-day is 3 real minutes. |
| `UI/MiniMap.cs` | bend | Becomes the portfolio view (Phase 4). |
| Crew selection UI | new | The only new interface. |
| Outpost ledger + tick | new | The only new system. Small. |
| Save/load | new | Does not exist (D4). |

## Phases

**Phase 1 — vertical slice.** One resource (timber), two buildings (campfire,
storehouse), no risk, no decay.
Anchor -> overview camera -> tap hands to send ashore -> tap ground to make camp
(the clearing FELLS its trees through `NodeHarvested`; you cannot flatten ground)
-> crew chop and carry to the camp -> cast off with hands ashore -> return ->
pile has grown -> load what the hold takes.
**Gate: does the return feel good?** Nothing else matters until it does.
*Out: stone, food, raiders, decay, housing, upgrade costs.*

**Phase 2 — the ceiling, the reason to build, and provisions.** Each building
moves exactly one number: storehouse = ceiling (10 -> 40); sawmill = rate;
shelter = supported hands.

**SETTLED 2026-09-13: hands beyond what a camp supports eat stores and can
starve.** Kevin chose the harsh option over idling. Consequences, and they are
real:

- **It needs a provisions resource, which does not exist.** "Stores" in the code
  today means what HOME keeps (`Village.StoreCapacity`), not food. The bay
  system's "stores burn as crew x days" is a comment, not a mechanic. So the
  second resource is **food, not stone** — the quarry moves back.
- **This is the strongest available answer to risk #2.** A camp that must be fed
  gives the sea a recurring job that is not collection. Accept the cost Kevin
  was warned about (every voyage is partly a supply run) because it buys the
  thing the whole design was most at risk of losing.
- **Failure must be gradual, not lethal.** Recommended ladder: stores out ->
  hands stop working and forage (rate ~0) -> after a long time they are found in
  poor shape / leave. Death only at the extreme, and never without warning.
- **The campfire IS the provisions gauge.** This answers the open "does the fire
  go out" question by unifying it: the fire dims as stores run low, so a camp's
  health is readable from the water before you anchor. No new UI.

**SETTLED 2026-09-13: food is LOCAL. No home food economy, no supply run — for
now.** Camps feed themselves off berries and wheat, both of which already exist
as art (the FLORA kit's crop and berry props; `TerrainSettings.farmedShare` =
0.35 puts real wheat fields on a minority of islands). The old note that
*"farming is a roll inside the bake, no gameplay reads it"* stops being true.

**The two-tier split, and it is the whole thesis in terrain art that is already
baked:**

- **Berries — common, scattered, low yield.** Subsistence. A camp survives on
  them; it does not grow. One or two hands.
- **Wheat — rare (about a third of islands), concentrated, high yield.** Surplus.
  A farmed island supports a real settlement.

**So an island TELLS YOU how many people it can hold, and it tells you from the
air.** A wheat field is visible from the overview; scattered berries are not much
to look at. That is the investment decision expressed in the landscape instead of
in a panel — and it costs no new art and no new system.

**Consequence to be honest about:** with no supply run, Phase 2 loses the
recurring job it was going to give the sea. What defends risk #2 in the meantime
is the hold bottleneck, the stockpile ceilings, and the rule that a camp never
ships home by itself — plus raiders in Phase 3. If the sea ever starts feeling
like transport, **re-introducing provisioning from home is the lever**, and it
was designed to be added rather than designed out.

**Food behaves like timber:** a stock per outpost, decremented by the tick,
regrowing slowly. So a camp's SUSTAINABLE size is set by the island's food
regrowth rate — self-balancing, and it needs no cap anybody has to author.

**SETTLED 2026-09-13: hands get ANGRY, and that is all — for this stage.**
Kevin: *"a hand can refuse or leave if they are starving or very angry. but for
this stage of the game lets just have them be angry without leaving."* So the
ledger carries a per-hand mood that RISES under starvation and neglect and is
shown, with no consequence wired to it yet. The conditions for refusing or
leaving are a later pass and are explicitly NOT part of the island rework.

Two notes for whoever builds it:
- **Do not double-book the crew tint.** `CrewAgent`'s tint is seasickness and
  the shipped crew art is built around it (light on one mesh, colour on the
  other). Anger needs a different channel — recommend one behavioural tell: an
  angry hand does not come down to meet the boat.
- Mood is per-hand and belongs on the `CrewMemberDef` identity, not on the
  outpost row, so it travels with the man when he re-boards.

**Gate: two islands developed differently, and you can say why you'd sail to one.**
*Out: a fourth building, and stone.*

**Phase 3 — teeth.** Raiders exist already (`TerrainWorldPopulator`). An
unwatched camp can be raided; watchtower is the fourth building and earns its
place by preventing rather than producing. Island character (`Rock01`,
`Verdancy01`) sets danger — the richest islands are the worst to leave people on.
**Gate: a lost camp must read as the player's mistake, not a dice roll.**

**Phase 4 — the archipelago as a portfolio.** The minimap becomes a dashboard of
camp states. Free once 1-3 exist; it is a view onto the ledger.

## Traps this codebase will spring

- **Crew left ashore cannot stay as GameObjects.** Unparented from the ship;
  sail 3 km and their terrain streams out. Despawn into ledger rows on cast-off,
  respawn on approach. Largest implementation risk in Phase 1.
- **Nothing flattens ground, ever.** `TerrainHeight.Height` is a pure Burst
  function. Choose a site, measure its four corners, sit at the highest with a
  footing to the lowest (0.87–1.54 m at home). `Village` does this correctly.
- **Scenery is one welded mesh and the keep-out happens before the bake.** At an
  arbitrary island the trees are already there, so clear through
  `SceneryWood.NodeHarvested` at runtime.
- **"Which island am I at" ranks by SHORE GAP, not centre distance.** This was a
  shipped bug: 12 of 34 approach bearings had no landing prompt.
- **Tick on `TimeOfDay`, not wall clock** — `Scale` and `Paused` exist and dev
  tools use them; a desync will present as a balance problem.
- **Verify the artefact, not the rule that made it.** The D2 parity probe is
  only meaningful measured off stockpile contents.
- **The island rebuild and this feature are the same feature.** "Reads as a play
  space at 165 m of ground" is an acceptance criterion for the new island system
  (see the 2026-09-11 survey). Do not block on it — prototype Phase 1 on today's
  terrain and let the overview feed the island brief.

## Open, Kevin's calls

**Settled 2026-09-13:** D5 = bottom sheet · timber depletes and regrows ·
over-capacity hands eat stores and can starve · the campfire doubles as the
provisions gauge (falls out of the starvation call).

Still open:

1. **How many islands are worth settling? — TBD, Kevin's call, deferred.**
   Working default until he says otherwise: `Settlement.Capacity` decides per
   island rather than a global rule — a sandbank honestly holding one plot is a
   better story than a cap.

## Working method

Kevin, 2026-09-13: *"feel free to deligate and conduct different models to
optimize the creavie process."* Per [[orchestrate-and-delegate]]: orchestrate
the build, Opus for judgement-inside-a-spec, Sonnet for mechanical edits, and
**agents never touch the Unity MCP bridge** — editor work stays on the main
session.


## D1 as built — 2026-09-13, `81d1d4e`

`World/Outpost.cs` (was `Village.cs`, git-mv'd so the .meta GUID and the
history survive), per-island, found by `Outpost.Of(island)`. `Stockpile` is
per-island the same way via `Stockpile.Of` / `EnsureOn`. Both keep a static for
home and **only home may claim it** — `Outpost.Home`, `Stockpile.Instance` and
`Settlement.Home` were all "whoever enabled last", which is harmless with one
island and wrong with two.

Islands are surveyed **lazily and over frames**: `Outpost.BeginSurvey`, started
by `AnchorController.SurveyWhatIsNear` when an island comes into landing range.
Home is still surveyed eagerly by the populator because its clearing has to be
reserved before the scenery mesh is welded shut. `Outpost.Rules` is published
once by the world build (same pattern as `Island.TerrainHeight`).

Gate: `RunProbe.Outpost()` -> `Logs/OutpostProbe.txt`.

| | measured |
|---|---|
| home clearing | 30.00 m, unchanged, exactly on `Settlement.VillageAt` |
| home capacity | 30, unchanged |
| islands that take a camp | **28 of 33** |
| islands that refuse | 5 (low sandbanks under the 3.7 m survey floor) |
| worst single frame | **8.2 ms** |
| longest survey | 3.3 s (biggest island), begun on approach |
| biggest flat ground | 7.96 ha |

### Traps, all of which cost a run

- **The survey floor and the build floor are different numbers.** 3.7 vs 4.4.
  Passing the build floor to the survey found ground on **0 of 5** islands.
- **A timed-out Coplay call still runs** — the timeout is on the bridge. A
  "failed" probe had already sited the archipelago; the retry then reported
  what it found as though nothing worked.
- **A script recompile during play mode nulls every non-serializable field.**
  The height delegates are `System.Func`, so they vanish on domain reload and
  everything reads "not sited" with no error, while `playMode` still says true.
  After any edit: stop, compile, play again.
- **A probe that reports a difference cannot tell you the state.**
- **The band size is measured, not chosen** — the survey times its own first
  row, because guesses at the height function's cost were wrong both ways.
- **`SettlementRadiusFor` is not `HarbourSite.SearchRadiusFor`** and home stays
  on the latter, so this cannot move the village.

### Next

Phase 1 proper: the bottom sheet, crew selection, `make camp` felling its own
site through `SceneryWood.NodeHarvested`, crew carrying to the camp pile, and
the ledger + tick (D2) — the ledger is the piece to design first.

## THE BLUEPRINT PASS — 2026-09-19

Kevin, 2026-09-19: *"when building the campfire you can only build it within a
certain radius of the ship. after docking i have the option to build a campfire
and it will be a blueprint of the campfire. i get to press where on the island i
want it and it will remain a blueprint there until its built (imagine how the
forest does with their blueprint builds) then the people i send ashore will cut
down wood to build it. once its built and lit i can start doing other things."*

So **make camp is no longer instant**. The button hands you a ghost; you put it
somewhere within reach of the ship; it stands there as a drawing until the crew
have cut the wood for it.

### The four decisions this took

1. **The blueprint is a LEDGER ROW, not a scene object.** `PendingBuild` —
   plan id, world XZ, logs needed, logs delivered — lives in `OutpostLedger`
   beside the timber, because a camp you sited and then sailed away from has to
   go on being half built while its island is unloaded. `BuildSite` is the
   drawing OF that row and is rebuilt from it on every arrival, exactly like
   the parked crew bodies. Gated: `a-half-built-blueprint-round-trips`.

2. **One validity test for the ghost and the raise.** `Outpost.CanPlace` is
   what turns the ghost red and what the raise asks before it stands anything
   up. A ghost that goes green on one rule and a raise that refuses on another
   is the bug the whole shape exists to prevent, and the only way to keep it
   prevented is that there is one function.

3. **Picking is analytic against the height field.** NOT `Physics.Raycast`:
   `TerrainSettings.colliderRadius` is 1 chunk, so colliders exist within about
   128 m of the ship and nowhere else, while `IslandCam` frames up to 520 m of
   ground. A screen ray would pass clean through the far half of the island the
   player is looking at — a bug that appears only when somebody zooms out,
   which is exactly when they are choosing where to put something.
   `CameraRig/GroundPick.cs` marches the ray with a step that adapts to its
   height above the surface and bisects the crossing; measured 0.0 cm off a
   known point from 400 m up.

4. **The wood comes off the island, by two routes that cannot double-count.**
   A stationed hand is a row on `OutpostOrder.Build` and accrues arithmetically
   through the tick at the felling rate, drawn from `standing`. A visiting hand
   is a body and delivers through `CrewAgent.DropOff`, which now pays a
   blueprint before it fills a pile. The old rule holds unchanged: the ledger
   counts only the hands who LIVE there, and a hand walking about is by
   definition not one of them.

### The error the probe caught, which is the one worth remembering

The first version **felled the site at siting** and put those logs straight into
the build — "a site in thick timber starts part-paid" — which sounded like a
reason to care where you put it. A campfire costs four logs; the probe's spot
had four trees on it; **the camp finished the frame it was placed and there was
no blueprint at all.** A blueprint that can pay for itself is not a blueprint.

The clearing is now felled when the build **completes**, which is both the fix
and the better story: a drawing stands among the trees it is going to take down,
and the camp appears in the wood rather than a gap appearing where a camp might
one day go. Gated both ways — `siting-fells-nothing` and
`making-camp-fells-its-own-site`.

### Numbers, all guesses, none played

| | value | why that |
|---|---|---|
| `CampSiting.SiteRadius` | 80 m | median island radius is 78 m and the default shot holds 165 m of ground: reaches a good part of a typical island from a ship lying off the beach, without letting you develop the far side of a big one from the water |
| `BuildPlans.Campfire.cost` | 4 logs | **one hand for one day** at `TimberPerHandPerDay` 4 — the only unit the player has. Gated: `one-hand-one-day` measures 1.00 days |
| `Outpost.CampClearingRadius` | 7.5 m | unchanged, and still the open look call: it took 4 trees down on Island_1 |

### Gates

`RunProbe.Ledger()` — 8 new, all green: build is path-independent over 3 game
days across 1 / 240 / 4 ragged calls; no overshoot; one hand one day; a builder
does not also fill the pile; a build draws on standing timber (**and the first
version of that gate was wrong — zeroing `standing` without `standingMax` let
regrowth refill the ground over the ten days being measured**); a stripped camp
builds at the rate it regrows; a half-built blueprint round-trips through JSON.

`RunProbe.Camp()` — 9 new, all green, driven end to end in play mode: a spot 6 m
off the surveyed clearing takes a camp; the pick lands on the ground; siting
writes a blueprint where it was put; a blueprint is not a camp, keeps nothing,
and is drawn on the ground; one hand and two game days lights the fire within
1.5 m of the spot; the drawing comes down; the hands go back to cutting.

### Still open

- **The look call, unchanged and now more important:** 7.5 m of clearing took
  four trees down. Kevin has not seen any of this — the ghost, the ring, the
  stakes, the log stack, the fire appearing in the wood.
- **Stationed hands still do not ANIMATE the build.** Parity is structural in
  the ledger; the bodies standing at a camp you are watching are parked, not
  working. Same gap as before this pass, one job larger.
- Other buildings reuse all of this the moment `BuildPlans.AtACamp` grows:
  `Site` takes any plan. Only the campfire is offered today.

## THE VIEW FOLLOWS THE DECISION — 2026-09-19

Kevin, same day: *"when the campfire blueprint has been set the camera moves to
focusing the campfire with a hight of 35 meters above it. using the keys allows
you to move the camera around freely across the island. zooming in and out
obviously zooms in and out. i also want to be able to press on one of the
villagers and have the camera follow them."*

### Height is not coverage, so the triangle lives in one place

The overview is authored in **metres of ground up the frame**, not in altitude,
and for a good reason: a distance means nothing without the lens. But a person
says "35 m above the fire". `ChaseCamera.OverviewGroundForHeight` inverts the
seat geometry — `seat = aim + dir·span·cos(tilt) + up·span·sin(tilt)`, so
`span = h / sin(tilt)` and coverage is `span · 2 · tan(fov/2)`. At the shipped
32° tilt and 36° lens, **35 m up is 43 m of ground**. Measured: the lens lands
35.0 m above the fire with it at viewport (0.50, 0.50).

### Focus, not pan

`IslandCam` gained a `focus` point that REPLACES the composed centre, and a
`following` transform that drives the focus every frame. Consequences that had
to be handled rather than discovered:

- **A focus sets `shot.free`.** `free` is also what switches off the slide that
  drags the frame until the ship is inside it — and a view centred on a
  campfire that then slid to include the ship is not centred on the campfire.
- **The pan clamp moved on to the RESULT.** It used to limit how far the frame
  had been walked from the composed centre; now the centre can be a fire or a
  crewman that is already off the island's middle, so what is clamped is where
  the frame ends up, against the island's own centre and radius.
- **END is the way back.** It releases the focus and the follow and hands the
  shot back to the composition the dock authored.

### The pick is a projection, not a raycast

The crew are 388-triangle characters with **no colliders**, and giving twenty of
them colliders so a camera can be aimed would be paying physics for an
interface. `IslandCam.PickCrew` projects every body near the island to the
screen on the frame a finger goes down and takes the nearest within 7% of screen
height. A tap that hits nobody lets go, and letting go leaves the camera where
it is rather than snapping back — releasing somebody should not move the view.

A followed hand who is parked (the ship sailed) or recalled goes inactive, and
the follow stops at their last position rather than chasing a switched-off body.

### The zoom floor had to come down

`minGround` was **40 m** and the camp view is 43 m of ground, so "zoom in" from
a blueprint bought 7% and read as broken. Now **18 m** — the lens about 15 m up,
a crewman a tenth of the frame. The shipped default (165 m) and the composed
dock shot are untouched; this is a limit, not a composition. `IslandCam` is
added at runtime by `AnchorController` and is NOT serialised in `Sea.unity`, so
the C# default is the live value — checked, because a serialised field beating
the initializer is this project's oldest silent trap.

### Gates — `RunProbe.Camp()`, 43 green, 0 failed, **run on the steamer**

`the-view-goes-to-what-was-sited` (0% off the middle) · `the-view-is-35-m-above-it`
(35.0 m) · `the-view-follows-who-you-press-on` (0% off) ·
`and-keeps-following-when-they-move` (2% after the hand walked 23 m) ·
`letting-go-hands-the-shot-back`.

Driven through `CampSiting.DropTheViewOn`, the same call the tap makes, so the
gate cannot pass against a copy of the behaviour. The pick itself is the one
half not gated — it needs a real tap.

### Also

`HelmInput` no longer touch-steers while the island view is engaged. The sheet
covers the bottom 36% of the screen and the steer zone is the bottom 45%, so
there was a band where a tap meant for the ground — siting, or pressing on a
crewman — also put the rudder over and left it there.

## PHASE 2 — POSITIONS, RESOURCES AND THE ORDERS LIST — 2026-09-19

Kevin: *"after sending the crew to the island and they've built the campfire
they should stand around the campfire. when they landed at the island the crew
currently assigned to the island's name pop up on a list to the right. when
pressing their name you get options: 1. assign -> (name of assignable position,
farm, blacksmith etc). 2. gather -> list of gatherable resources. 3. build ->
list of buildable buildings. crew on the island can gather resources up to 10
of each without a storage unit. they will pile them close to the campfire."*

Both of the calls this needed went the ambitious way: **all six kit buildings**,
and **assigned hands produce their building's output**.

### The gather list was already a fact about the island

`WorldSettings.kinds` has been giving every island one of **Timber, Stone, Ore,
Spice** since long before this, scattered as props with `ResourceNode`s on them
and unlocked by distance from home. Nothing had to be invented: the menu shows
what the populator put there, so two islands offer different work. What IS new
is the far side of a building — **Boards, Tools, Food, Meals** — which are made,
never found.

### The ledger went multi-resource

`timber` and `ceiling` became `stores` (a list of `OutpostStore`) and
`ceilingPer`, plus `stocks` (`OutpostStock`, what is left in the ground). A list
rather than a dictionary because `JsonUtility` cannot serialise a dictionary and
this object exists to be savable.

- **Ten of EACH, which is what Kevin asked for**, and it is what makes a second
  resource worth gathering rather than a competitor for the same ten slots.
- **Timber regrows; rock does not.** `regrowPerDay` is per resource per place,
  so a mining island is used up and a wooded one is farmed.
- Orders carry a **target**: `Gather` names a resource, `Work` names a building.
  `Cut` is gone.

### A position converts

Each plan carries `takes`, `makes`, `rate` and `position`. A sawyer turns timber
into boards, a smith turns ore into tools, a farmhand needs no input because the
field is the input, a cook turns food into meals. A hand assigned to a building
with nothing to work on produces nothing, deliberately: **a sawmill on an island
with no timber is a shed.**

### The six buildings wear the kit

`SettlementKitV1` had been sitting unwired since 2026-09-17.
`SettlementToResources` copies the seven prefabs the camp can raise into
`Resources/Settlement` (copies, not moves — the originals stay where the art
pass put them), and `BuildingFactory.Raise` dresses a plan that names one and
extrudes primitives for one that does not. **Both leave by the same door**, so
the blueprint, the ghost, the four-corner test and the footing are identical
either way — which is what let six authored buildings join a system built for
primitives without touching any of it.

**The footprints are measured, not guessed**: the game bounds out of the kit's
own `unity-import-validation.txt`, so the ground is tested at the corners the
building actually stands on and the blueprint is the size of the thing.

### The crew stand round the fire

They did not before: `Station` dropped each hand at a random point within 2.2 m
of wherever the camp centre was AT THE TIME — which for a hand left before the
fire was built was the blueprint — and never moved them again. `ArrangeHands`
computes an even ring at 2.9 m facing in, and re-runs whenever the camp changes:
on arrival, on stationing, when the fire is lit, when an order changes. **A hand
assigned to a building stands at that building instead**, which is the whole
visible difference between four idlers and a camp with a sawyer in it.

### What is gathered is piled beside the fire

`CampPiles` draws one stack per resource in a ring at 5.2 m — outside the crew's
2.9 m — with the angle taken from the resource NAME, so the same thing lands in
the same place at every camp and a player can read a camp from the air. Timber
and boards are cross-piled logs; everything else is a heap of sacks. Drawn from
the ledger and owning nothing, like `BuildSite` and the parked crew.

### The list on the right

`CampCrewList`: the hands who LIVE here, each row saying what they are doing,
and three verbs under whichever is open. It is the counterpart to `CampSheet`,
not a replacement — **the bottom sheet is about movement and this is about
work**, and keeping them apart is what stops either becoming a menu of
everything. Build hands straight to `CampSiting`, because where a building goes
is the one thing a list cannot ask.

**`HudOverlapProbe` caught it twice and was right both times.** First at a
hand-picked rect, sitting on 240x60 px of the minimap for thirty frames; then,
after being given a `Slot`, at the bottom of the screen on the helm — because
`ColumnOf` maps slots with an explicit switch and anything unlisted falls
through to bottom-right. **Ask the layout for a place; never pick one.**

### Gates

`RunProbe.Ledger()` — 28 green, pure arithmetic. Each resource has its own
ceiling; a full pile does not block another; rock does not regrow; **a worked-out
seam stops below the ceiling for ever** (one hectare of ore is 9 units against a
ceiling of 10); a sawyer makes boards and eats the timber; a forge with no ore
makes nothing; a farmhand needs no input; and all of it is still path-independent
over 8 days across 1 call and 320.

`RunProbe.Camp()` — 51 green, in play mode, on the steamer. The fire wears the
kit model; three hands stand 2.90–2.90 m from it with the closest pair 5.02 m
apart; ten timber draw ten logs 5.2 m from the fire; a sawmill goes up, a hand is
assigned, walks to it (4.4 m from the mill against 5.6 m from the fire) and turns
timber into boards.

### Two gates that were wrong, and both in the same way

A gate whose PREMISE the numbers destroy reads as a code failure. Seeding one
hectare of ore gave 9 units against a ceiling of 10, so "each resource reaches
its ceiling" failed on an island that had simply run out — and the fix was three
hectares plus a separate gate for the worked-out seam, which is a better pair.
Then "six timber draw six logs" failed at ten, because the camp already held the
four its own clearing gave it. **Measure against what the ledger holds, not
against what the test put in.**

### Still open

- Nothing eats `Food` yet, and `supports` on the shelter moves no number —
  starvation and over-capacity are the next pass, and the fields are declared.
- The kitchen chain (food -> meals) has no consumer either.
- Stationed hands still do not ANIMATE the work; they stand where they belong
  and the tick does the producing.
- **None of it has been played.** Every rate is a guess.

## WHAT PLAY FOUND — 2026-09-19/20

Kevin played it and brought back three things. All three were real; two were
bugs and one was a feature that had been designed on 2026-09-13 and never built.

### 1. "i never saw the people on the island"

**`Station` switched a hand off, and only `ShowHands(true)` ever switched one
on — and that fires when she ANCHORS.** So anybody left ashore while you were
already standing there winked out, and nothing brought them back until you had
sailed away and returned. Being watched is a state the outpost keeps now
(`Outpost.Watched`), so anything that adds a body can ask whether to draw it.

### 2. "i can tell that things get done, but no one is doing it"

Fair: they stood in a ring like ornaments while the pile filled itself.
**`CampWorker`** walks them out to the nearest standing tree, has them work it,
and carries something back to the fire — and **produces nothing at all**, which
is the entire design. A camp that paid differently while somebody watched it
would undo the reason the ledger exists.

It moves the transform and never touches `CrewAgent`'s state machine, which is
safe for one reason worth writing down: **a parked hand's state is `Station`,
and `Station` does nothing** — and `CrewAgent`'s walk cycle is driven by how
fast the body is ACTUALLY moving rather than by which state it is in, so the
legs come along for free. Measured: 5.8 m walked in four seconds of being
watched. Only exists while she is here; `ShowHands(false)` takes it away.

### 3. "the trees never disappear. i assume they would since they're cut down"

They did not, because gathering only ever decremented an abstract stock. The
plan settled this on 2026-09-13 — *fell deterministically, nearest-to-camp
outward, store only a COUNT* — and it was never built.

`OutpostLedger.timberTaken` is now the cumulative timber out of the GROUND, and
`Outpost.SyncFelling` takes trees down nearest-first until `treesFelled` matches
it. **One tree per log.** The stock is 40 logs a hectare against the ~230 trees a
hectare the scenery draws, so a worked-out camp thins its wood rather than
shaving the island: measured, stumps out to 19.8 m and standing timber from
22.3 m — a widening ring, exactly the picture the plan asked for.

Because it is a pure function of the ledger, a camp worked for twenty days while
you were elsewhere is found with twenty days of stumps on the next visit,
whatever the terrain streamer did in between.

### And rotation, asked for at the same time

*"i'd like an option to rotate them by 45 degrees in a complete rotation."*
**R turns the ghost, shift+R turns it back, eight steps to the circle.** The yaw
is carried on `PendingBuild`, so a blueprint comes back from a save facing the
way it was put down, and `CanPlace` tests the corners at that angle — a
rotation that lived only in the ghost would look right and build wrong.
Automatic until it is touched (door toward the middle of the camp, as the spiral
did), then frozen and turned from there — the same rule as `IslandCam.Driven`.
Measured: a shelter asked for 135° stands at 135°, 0.0° out.

### The interaction this created, which the probe caught

"Reported 4 logs out of the clearing, mesh says 8 came down." Building the fire
consumed four logs of standing timber, so `SyncFelling` felled four trees for
them — somewhere else — while `FellWithin` felled four more for the site. **The
same wood twice.** The clearing now counts toward `treesFelled`, which is the
story it always had: making camp fells the wood it stands on. And the gate was
sharpened to count the stumps INSIDE the clearing rather than the change across
the whole island, because now that the crew cut real trees to pay for a build,
those two numbers are honestly different.

### Gates

`RunProbe.Camp()` — **58 green, 0 failed**, in play mode on the steamer.
Three of three hands drawn and carrying a `CampWorker`; 5.8 m walked; 10 logs
cut takes 9 trees down; the clearing widens from the camp; a shelter stands
where it was turned.

### Still open after this

- Only TIMBER thins the island. Stone, Ore and Spice are `ResourceNode` props
  from the populator and are not yet removed as they are gathered — same trick,
  different mechanism, not done.
- Assigned hands stand at their building but do not animate the work there; the
  walking loop is for gatherers and builders.

### The second hole in the same bug — 2026-09-20

Found by the question "is this ready to play", not by the probe.

`SurveyWhatIsNear` is the only per-frame path that calls `ShowHands(true)`, and
it runs **only while she is UNDER WAY**. The survey takes seconds to finish
after the anchor is down — 7.9 s measured on Island_1 — so on a FIRST visit the
outpost does not exist at either moment that would have woken it, and `Watched`
stays false for as long as she lies there. Anybody stationed then is switched
off and never drawn: **the same fault Kevin reported, one door further in.**
`AnchorController.Update` now wakes the camp while Anchored or Ashore.

**And the gate had been green through both versions**, because the probe called
`ShowHands(true)` itself right after stationing — so it was measuring the
probe's own action, not the game's arrival path. The crutch is gone and the gate
is still green, which now means something.

## THE GOD'S-EYE ISLAND — 2026-09-20 (Phase 1 of the named loop)

Kevin: islands — *only* islands — should take their *"game feel, camera, and
movement"* from Black & White 2. Three calls, all his: **the Hand gives orders
and never produces** (D2 stands); **ship rungs get priced in camp-made goods,
home growth after** (the ladder is FREE today — `Shipyard` never reads a
`Stockpile` — which is the biggest hole in the loop and is Phase 2);
**villagers visibly do their work.** The loop as named is in `GDD.md` §6.

### What was built

- **The camera is `IslandCam` grown up, not a new rig.** Pivot / azimuth / tilt /
  ground; grab-the-land, zoom-to-cursor, orbit about what is under the press,
  fling, fly-to, a tilt that follows the zoom (through 165 m → 32° exactly, so
  an untouched view is still the shot Kevin flew). `ChaseCamera` is still the
  only writer of the camera; `IslandShot` gained `direct` and `clearance`.
- **`CameraRig/IslandInput` is the one device reader** — mouse, keys, and
  `Touchscreen` raw for pinch / twist / two-finger tilt / long-press. Every
  gesture lands on a public method the probes also call.
- **The Hand** (`UI/Hand`, `World/HandTargets`, `UI/HandCursor`): tree → gather
  timber, prop → gather that, building with a position → assign (and *that*
  building, via `CampWorker.PreferWorkplace`), blueprint → `Outpost.OrderBuild`
  (new: one hand, not everybody), fire → idle, ship → recall, an aboard hand on
  land → `Station` + the target's order in the same drop. One resolver serves
  the preview and the drop. No colliders anywhere: footprints, `TreeIndex`,
  projection picks, `GroundPick`.
- **Living villagers** (`CampWorker`, `VillagerActing`): a `Work` loop at the
  building, per-trade acting as bone overrides over Idle/Walk (no new clips),
  carrying to the pile, a stalled mill that READS as stalled, dangle / land.
  `CrewAgent.Puppeted` stops `Station` writing rotation and sickness onto a
  body something else is walking.

### What the probes found that was not the new code

- **`Outpost.Site` moved the camp onto every new blueprint.** `campCentre = at`
  for ANY plan — invisible while the fire was the only thing that could be
  sited, and wrong since the build list grew: the ring of hands, the piles, the
  felling order and the ledger's save key all followed a store hut 22 m away.
  `HandProbe` found it by dropping a man on the fire and being told "blueprint".
  **Only the fire says where the camp is** now.
- **Siting, finishing and cancelling all reset EVERYBODY's orders.** A sawyer
  was sent back to cutting timber each time a hut was finished. Now: making
  camp is everybody's job; a later building takes whoever is not in a position;
  completion and cancel send only the builders back to the wood.
- **Every order write teleported the whole camp** (`ArrangeHands`). A hand on
  his feet with a `CampWorker` is given somewhere to walk to instead.

### What the camera cost to get right (seven probe runs)

1. `direct` waited for the rig to "arrive" within 0.5 m — but a drag moves the
   seat every frame, so a grab a moment too early never got 1:1 at all (23 %
   stray). Now honoured at once, the leftover gap carried as a decaying offset.
2. The terrain yield makes the shot non-rigid, so a rigid solve drifts over
   hills (9 % at 165 m, 44 % at 20 m). `HoldUnder` re-casts through the pose as
   it NOW composes and closes the difference — and its first version cast
   through the frame's cached pose and applied the same fix three times over.
3. `IslandCam` had no execution order; eased paths drew last frame's zoom half
   the time. Pinned between `IslandInput` and the default.
4. Four failures were the PROBE: a latch compared with the authored zoom
   instead of what was on screen; a first touch measured while she was still
   being warped alongside; drags thrown into their own reach clamp by the
   previous section's fling; an orbit measured through its own hand-over.

### Gates — all green, 2026-09-20

`Ledger` 28 · `Camp` 58 (control, before and after) · `IslandInput` 15 ·
`IslandCam` 33 (grab 0.00 % / 0.01 %, zoom 0.00 %, orbit 0.00 %, first touch
0.20 %) · `Hand` 26 (240 re-drops pay what one order pays; half a day in the
air pays nothing; 0 bytes held) · `CampLife` 21.

### Still open

- **Kevin has not flown it.** `IslandCamTuner` (dev tools) has every feel value
  on a slider and dumps C# to paste back. `Feel.clearance` is the first one:
  4 m lets the close shot stay shallow; on a hilltop the yield still stands it
  up steeply.
- **Two-finger gestures have only had their arithmetic gated.** Needs a device.
- **`IslandCam` is green at BOTH shapes** (33/33 at 1531x937 and at 1080x2340).
  The portrait run found the throw was frame-rate dependent -- the velocity
  window was 80 ms, so under ~12 fps no second sample was ever inside it and
  letting go of the land threw nothing. The last two samples always count now
  unless stale. `HudOverlapProbe` with the Hand prompt up has NOT been run at
  either shape; `Hand` and `CampLife` have only been run at the desk shape.
- The reach is a 300 m disc round the SHIP (the streamer follows her) — the
  placeholder for Phase 3's influence ring.
- **`CampSheet` is a fold-away bar now** (Kevin's first play: *"i cant playtest
  the rest of the steps because the ui is blocked by the menu"*). Folded by
  default to one row along the bottom edge — 60 px of 1080, was 389 — carrying
  the headline and the one button that matters (make camp / never mind /
  ▲ crew); the ABOARD/ASHORE lists open on request and fold themselves the
  moment a hand takes the land or a villager. It fits BETWEEN the bottom HUD
  clusters, or on top of them where there is no room: `HudOverlapProbe` had the
  first full-width bar across the helm in a second, and the old slab had been
  lying on the helm all along. Portrait not yet looked at. The IMGUI lists
  still allocate per event.
- Pile positions are replicated in `CampWorker.PileSpot` from `CampPiles`.

## THE SECOND PLAY, AND THE SINK — 2026-09-20/21 (Phase 2 of the named loop)

Kevin flew Phase 1 and brought back four things; all four were real. Then
Phase 2 was built on top of them: the ladder costs something, and a camp's
pile can be carried down to the boat. Nothing below is committed yet.

### 1. "they gathered logs for it but it never built"

They had. Ten logs sat beside the fire while the builders walked past them to
cut fresh ones, and on a small island the fresh ones ran out at 6 of 24 — the
sawmill stood as a drawing for ever. Two faults, one symptom:

- **`OutpostLedger.Step` now hauls from the pile FIRST**, then cuts. A builder
  carries `HaulPerHandPerDay = 12` logs a day from the pile (three times the
  felling rate: the wood is already down and five metres away), and only what
  is left of his day goes on standing timber. `CampWorker` walks the same
  order — a builder goes to the pile while it has anything in it — so the
  animation stops contradicting the arithmetic.
- **Standing timber is the island's, not the clearing's.** The stock was seeded
  from the FLAT ground the survey found, which is a fact about where you can
  build and not about how much wood there is. `Outpost.WorkedHectares` seeds it
  from `0.6·πr²` of the island's disc (the rest is beach, rock and meadow),
  floored at the clearing.
- A build that can no longer finish says so: `OutpostLedger.BuildStarved`
  (nothing piled, nothing standing) makes the `CampSheet` headline read
  **NO TIMBER LEFT**, because a starved blueprint is otherwise indistinguishable
  from a slow one. And `BuildSite`'s log stack scales with `Fill01` rather than
  one log per delivery capped at sixteen — a sawmill wanting 24 used to show a
  full stack at two thirds and then sit there looking finished and unbuilt.

### 2. "works but its a bit too slow" — the wheel

`IslandInput.HandleWheel` divided by 120 regardless. Input System 1.11+
defaults to `ScrollDeltaBehavior.UniformAcrossAllPlatforms`, about one unit a
notch everywhere, so the zoom ran at a hundredth of its speed and was only
usable because a Mac's smooth scrolling sends a great many events. It now
divides only when the setting is the old platform-specific range. Feel:
`wheelStep` 1.22, `wheelMaxPerFrame` 4 (a trackpad flick reporting a dozen
notches is a lurch, not a zoom), `IslandCam.zoomRate` 1.8.

### 3. Trees fell at random while the villagers chopped somewhere else

`SyncFelling` dropped the ledger's trees nearest-first the moment they were
paid for, and `CampWorker` chose its own trunk — two pictures of one number.
**The fix is a WAIT, not a new chooser.** Which trees come down, and how many,
is still decided entirely by `fellOrder` from `CampCentre`, walked
front-first, because that is what lets a camp worked twenty days while you
were elsewhere be found with the right ring of stumps (D2). What changed is
who stands where and WHEN the front tree drops:

- Workers take the NEXT trees in the order — `Outpost.NextToFell` /
  `ClaimTree` / `ReleaseTree`, the k-th hand to ask gets the k-th standing
  entry, never two men on one trunk.
- Watched, the front tree falls only when its claimant is `Working` at it
  (`CampWorker.IsFellingNow`), or it has been owed longer than
  `Feel.fellGraceSeconds` (10 s — a tree that will not fall reads worse than
  one that falls unattended), or the backlog exceeds `cutters + 2`
  (`Feel.fellBacklogSlack`: the clock was scrubbed, or she has just arrived) —
  then the whole debt is taken at once. **The loop breaks rather than skips**,
  so the felled set never has a hole in it and stays reproducible from an
  integer. Unwatched, it is exactly what it always was.
- **Known gap:** a builder hauling from the pile is not `Cutting`, so he holds
  no claim and is not counted in `cutters`; the slack of two covers him today.

### 4. "i want villagers / items to retain some momentum if i drop them mid grab"

`Hand` keeps a ring of the hand's last twelve positions (`HeldMotion`, no
garbage) and lets go with the mean velocity of the last 100 ms — with the
sample before the last one always counted unless older than 350 ms, the same
frame-rate trap `IslandCam.MeanVelocity` fell into on the portrait run. Under
`throwMinSpeed` 1.5 m/s letting go is a PLACEMENT, exactly as before; above it
`CampWorker.Throw` gives ballistic flight (`Phase.Flying`, `throwRise` 0.35
of the speed upward, `throwMaxSpeed` 18). **A throw is a picture, never an
order.** The order is still resolved and written by `Hand.DropAt` at the
RELEASE point, in the release frame, so preview == commit holds; landing
writes nothing; and `TickFlight` stops the horizontal motion at the last point
that was over the island, so he cannot come down in the sea.

### The sink — `Ship/ShipPrices.cs`

Phase 2 as promised on 2026-09-20: the ladder is no longer free.

- Rungs 1–6 timber; 7–11 boards (+stone — a sawmill has to be standing and
  manned before rung 7 can be paid for at all); 12–16 boards + tools; 17–19
  tools + spice, the outer ring. Fits are priced on their own table
  (`ForFit`). **Every number is a first guess, none played** — set against
  the hold she has at the rung before, so a rung is one or two voyages, and
  to be tuned from the `SinkProbe` PACING table.
- `Shipyard.Move` and `Shipyard.Upgrade` bill through the `Purse`
  (`VoyageManager`, found once): **physical gate first, then the bill**, so
  "needs 12 boards" is never said about a hull that would hog. The refusal
  is a sentence. Both refuse anywhere but home — *"she has to be at her own
  pier"* — the stores are ashore, however full the hold is.
- **The design rule the file exists to hold:** price `Move` and `Upgrade`,
  NEVER `Apply`, `Undo` or the panel's dev buttons — that is the path every
  probe puts her on a rung by, and there is deliberately no "free for probes"
  switch. The bay `+`/`−` buttons (`AddCell`/`RemoveCell`) bill nothing: the
  board is what you do with the hull you paid for.
- `VoyageManager` banks per resource (it always did — a dictionary); `SpendBanked`
  and `BeginVoyage` are public now so the yard pays through the same call the
  buildings use (the visible `Stockpile` comes down with the number) and a
  probe can press the button the player presses.

### Camp → hold — `World/CampLoading.cs`

Until now `OutpostLedger.Take` had zero callers: the loop's fourth step —
*return and load* — was a sentence in the GDD. **The player loads; a camp
never ships home by itself** (risk #2, "the sailing becomes transport"), so
nothing here runs off the tick — no `Update`, a coroutine only while a load is
in progress.

- `RoomAboard` is the one place that knows the marked line from the physical
  `MaxHold`; `LoadNow` moves units synchronously; `Begin` / `BeginOne` /
  `Cancel` drive the carry at 0.15 s a unit (a shade quicker than the 0.18 s
  unload, same scaled clock). One unit at a time, `Take` → `AddLoot` →
  visual, and a unit the hold refuses goes straight back on the ground —
  `CampLoadProbe` gates units-out == units-in per resource across a fill.
- Best-first order: Tools, Spice, Boards, Ore, Stone, Meals, Food, Timber — the
  chain's own ranking until `ShipPrices` is what sorts it.
- `CampSheet`: the folded bar's action slot offers **⬆ Load** once there is a
  camp, something in it and room aboard, and turns into **✕ Stop** while they
  carry; the open sheet grows a row per kind so you can take the tools and
  leave the firewood. The headline lists every kind, not just timber.
- `World/CargoVisual.cs` draws Boards, Tools, Food and Meals as their own
  shapes, coloured from `Res.Colour` — a hold of tools no longer looks like a
  hold of firewood.

### Still open

- **All of it is uncommitted and unplayed** — the prices most of all.
- **Phone-shape HUD overlap pass not run**: Load and the crew ▲ share the bar
  row; `HudOverlapProbe` at 1080x2340 with the bar in each of its states.
- The hauling builder's missing claim (above).
- Stone, Ore and Spice props are still not removed as they are gathered.

## Save and load — 2026-09-21

Kevin: *"When playtesting I need to test the progression of the game. I need
to be able to save in game and, when launching the game again, press New or
Load. This way I can build buildings, have them tweaked, and see the changes
next time I play."* Built as `Scripts/Save/` — three files, one JSON, no
slots. D4 said the ledger would be savable before there was a writer for it,
and it was: the ledger went into the file unchanged.

### What is saved (`SaveData`, `JsonUtility`, version 1)

`Application.persistentDataPath + "/seasick-save.json"`.

- `worldSeed` — `WorldSettings.seed`. **The asset had `seed: 0`**, so
  `TerrainWorldPopulator` never called `Random.InitState` and every launch
  re-rolled island kinds, props, reefs and raiders — a camp keyed to a spice
  island would have come back on a stone one. It is `260921` now (terrain and
  trees were already seeded, 1337). A save from another seed is refused with
  a console warning and the launch falls back to New; the file is left alone.
- `timeSeconds` — `TimeOfDay.Seconds`.
- `ship` — rung (`Shipyard.NodeIndex`), the five `FitTrack` levels, every
  non-empty bay cell as `{bay, tier, use}` (kept apart, because `Shipyard.Key`
  joins them with an underscore), position, yaw, and an anchor state:
  0 under way, 1 anchored off an island, 2 alongside at home. Ashore is saved
  as anchored; the crew come back aboard.
- `hold` and `banked` — per resource, from `VoyageManager`.
- `outposts` — per outpost the **whole `OutpostLedger`**, plus
  `Outpost.CampCentre` / `HasCampCentre` (they were private and unserialised),
  plus `isHome`. The ledger gained one list: **`raised`, a `BuiltBuilding`
  `{planId, x, z, yaw}` per building**, written by both `Outpost.Raise`
  overloads. `built` (the bare id list every count reads and every probe
  writes by hand) is untouched, so nothing else changed; `Adopt` raises from
  `raised` and spirals only for `built` rows nobody recorded a spot for.
  Without it huts would have moved on load — `Raise(plan)` re-derives spots
  from a golden-angle spiral. Home's storehouses (raised by `TryBuild`) are
  in `raised` and not in `built`, exactly as they were live.
- Not saved, on purpose: crew sickness (cosmetic), the steamer toggle, HUD
  prefs, felled-tree *lists* (`SyncFelling` re-fells nearest-first until
  `treesFelled` matches — the whole point of storing a count), surveyed
  islands nobody touched (the survey is lazy and repeatable).

### The key

The persistent identity of a camp is `OutpostLedger.keyX/keyZ`, rounded camp
XZ (D3, never an island index). On load the island is found by looking the
key up in the populator's flood-fill `LandMask` (24 m cells, with a one-ring
search for a key on a cell the scan called water), falling back to
`Island.Nearest`. Home is found as `Outpost.Home`, not by key.

### Restore order (`SaveGame.Restore`, a coroutine on `GameBoot`)

It is load-bearing and it is this:

0. Wait for `TerrainWorldPopulator.Done`, one more frame for every `Start`,
   and for `AnchorController.StartedDocked` (the spawn-time berthing writes
   the ship's pose on its own first frame; racing it loses).
1. **`TimeOfDay.Scrub(timeSeconds)` first.** Every ledger's `CatchUp` ticks
   from its `lastTicked` to *now*; a ledger saved on day 3 and ticked from
   day 0 pays out three days of phantom timber.
2. Ship: `Shipyard.Apply(rung)` (the free path every probe uses), then
   `Fit.SetLevel` per track and `ClampTo(Node)`, then the cells through the
   new `SetUseQuiet` and one `Refurnish` (one furnish, one `PushToGame`,
   which also tells the voyage the hold size). Skipped when the steamer has
   the hull (`SuppressApplyOnStart`).
3. Hold and stores: `VoyageManager.RestoreStores` (new seam). It rebuilds
   the stack at the stern and the piles on the beach from the numbers, since
   neither `ShipHold` nor `Stockpile` re-syncs by itself. It runs after
   `Start`, so `BeginVoyage`'s wipe has already happened.
4. Pose: cast off from the pier she boots tied to, then `SaveGame.Warp` —
   transform and rigidbody, still, on the water's own height, as
   `BerthAtHome` does.
5. Outposts: home adopts in place; any other camp's island is surveyed the
   way anchoring surveys it (`Outpost.BeginSurvey`, awaited on
   `Outpost.Surveying`), then **`Outpost.Adopt(ledger, campCentre, has)`**:
   takes down whatever stood there, installs the ledger, re-nulls a
   blueprint JsonUtility revived as an empty object (an empty `PendingBuild`
   has `needed == 0`, which reads as complete, which would raise a fire
   nobody sited), re-raises every `raised` row at its spot, spirals for
   unrecorded `built` rows, and calls `CatchUp` (blueprint, felling, piles).
   Then the bodies: each hand row is matched by `CrewMemberDef.displayName`
   to a `CrewAgent` under the ship and walked over by **`Outpost.Rehome`**,
   which is `Station` without the row-write and the refusal.
6. **The anchor last**, so the camp she lies off is awake to see her:
   `BerthAtHome` for state 2, `AnchorController.MoorAt(Island)` (new,
   public `DropAnchor`) for state 1.

### New / Continue

No title scene, so `Save/GameBoot` is an IMGUI overlay that installs itself
(`RuntimeInitializeOnLoadMethod`, only into a scene with a `VoyageManager`)
and freezes the game with `Time.timeScale = 0` — which also freezes
`TimeOfDay`, whose owner advances it by `deltaTime`. NEW VOYAGE deletes the
file; CONTINUE runs the restore; with no file there is one button. Sized off
`HudLayout.Unit`, so the same fraction of the screen in both shapes.

Bypasses: `GameBoot.Interactive = false` (a launcher), `-new` / `-continue`
on the command line (`GameBoot.Forced`), and **`GameBoot.Skip()`, which
`RunProbe.Call` now invokes before every play-mode probe**: it dismisses the
overlay as New *without* deleting the file and sets `SaveGame.Suppressed`,
so a probe that anchors forty times never writes the player's save. Every
existing probe therefore boots exactly as before, one reflection call later.

### When it saves

`SaveGame.Autosave` on anchor drop and coming alongside (`AnchorController`),
on cast-off (`VoyageManager.BeginVoyage`), when a building finishes
(`Outpost.FinishPending`), on `OnApplicationQuit` and pause; plus a SAVE
button in the settings drawer (`SaveGame.Save`, not gated on `Suppressed`).
Autosaves are no-ops until `GameBoot.Decided` — the `Start`-time
`BeginVoyage` would otherwise overwrite the save with a fresh world before
the player had pressed anything — and while a restore is running. One
console line per write and per read, with the path.

### The gate

`Dev/SaveProbe` (`RunProbe.Save`): rung 14, a rudder, three quarters and a
hold cell, 7 timber + 3 boards aboard, 9 timber banked, a camp on the
nearest beach with a store hut raised at a chosen yaw, a hut sited, two
hands on two orders, three trees felled → temp file → wipe (hands recalled,
fresh ledger adopted, rung 12, empty hold, under way 300 m off, clock at 0)
→ `SaveGame.Restore` → every field compared. Not yet run; the first run is
the first thing to do with this.

### Still open

- Unplayed. The overlay has not been looked at in either shape.
- `ManCrew` clones `have[0]` for extra berths, so two bodies can share a
  `displayName`; `Rehome` takes the first match and the clone stays aboard.
  Pre-existing, and the same thing happens live after `Station`.
- A camp whose island the survey now refuses is dropped with a warning
  rather than kept as a ghost row.

### A pier — 2026-09-21

Kevin: *"I'd like a pier asset to be buildable to make it easier to dock with
the island."* One more plan in the camp's Build list (`BuildPlans.Pier`,
last), and the first building that is not a box on the ground: planks on
posts, 3 m wide, from the beach out into water a hull can lie in. **It costs 8
timber** (5 while `PlaytestCostCap` is on), nobody works at it and it keeps
nothing — what it buys is a berth.

**Siting rule: the player picks the beach; the beach picks the pier.** From
the tapped point `Outpost.SnapPier` walks downhill to the waterline (uphill
if the tap was wet), takes the downhill direction there as the heading, puts
the land end 3 m back up the sand (refused if that is not above water, or the
beach rises more than 0.5 m per metre), then runs the sea end out from 14 m a
metre at a time to 24 m until there is 2.5 m of water under it — and refuses
with *"the water here is too shallow for a pier"* if there never is. R does
nothing; the ghost is drawn at the snapped spot, at the chosen length,
rebuilt as the pointer moves along the shore. `CanPlace` asks the same
question of the answer, so blueprint, raise and a saved row all go through
one test. The deck stands at 1.2 m above mean water (`BuildPlans.PierDeck`),
never terrain-relative; each post is cut to the ground under it (sea bed
clamped at −6 m). The length rides in the plan (`BuildPlan.WithLength`) and in
the ledger row (`length` on `PendingBuild` and `BuiltBuilding`), so
`Adopt` re-raises a pier at its own spot, heading and length; an old save
without the field reads 0 and gets the 14 m plan. **Docking is not wired
here**: a raised pier's `Pier` component (`SeaEnd`, `Heading`, `Berth`) goes
out through `Outpost.RegisterPierDock` / `UnregisterPierDock`, one line for
the dock registry to hook when it lands.

### Wheat and the farm — 2026-09-21

GDD 6: *"food is local … wheat feeds a settlement."* **Wheat on the island is
gathered by hand; a farm is wheat that grows by the camp.** The Farm is the
sawmill's shape (`position = "farmhand"`, `makes = Food`, one Work order with
`target = "Farm"`) and takes nothing from the piles: its input is a FIELD,
described on the plan — `beds = 6`, `unitsPerBed = 4`, `bedRegrowPerDay =
0.25`, `bedSpacing = 1.8 m`, `FieldStanding = 24`. When the farm is raised
the coordinator calls `ledger.AddField(plan)` (= `AddStanding(Res.Food, 24,
0.25)`), which creates or grows the camp's Food stock: `standingMax += 24`,
`standing += 24`, regrowth the faster of what was there and 0.25/day. Wild
wheat gathered by hand and a farm's rows are one Food stock. In `Step`, a
Work order whose plan has no `takes` now draws its `makes` down from the
stock of that resource exactly as a gatherer does (`want = min(rate·days,
room, standing)`), and the field regrows at the top of the next quantum —
both per-quantum, so D2 holds: ten days in one call and ten calls of a day
give the same food. A ledger with no Food stock (a probe's bare farm, an
older save) is unbounded, as before, so `LedgerProbe`'s
`a-farmhand-needs-no-input` is untouched. `Stalled` reports a farmhand on a
stripped field. The numbers: `OutpostLedger.FoodPerHandPerDay = 6` (the
farm's `rate`), and 24 × 0.25 is also 6 a day, so **one farm keeps exactly
one farmhand busy**; a second strips it in four days and then shares the
regrowth. The kit's beds are separable — each farm FBX is one structure mesh
plus one `farm_NN_Crop_MM` module per bed with a `Plant_slot_MM` empty — and
`BuildingFactory.NameBeds` renames them `Bed_00..` / `BedSlot_00..` on raise
(`BedsOf(t)` / `BedSlotsOf(t)`). `farm_01` wears four; `farm_02` is the
six-bed model and is a one-line `prefab` switch once it is copied into
`Resources/Settlement`. Nothing eats Food yet; `foodEaten` is still declared
and unread. **All guesses, none played.**

### Huts fill themselves — 2026-09-21

Food now gets eaten, and a camp grows its own crew. `BuildPlan.houses` (Hut =
2, everything else 0, including the campfire and the home storehouse) sums
over `built` into `OutpostLedger.HousingCapacity`; `Housed` is `hands.Count`.
Inside `Step`, after production, every hand ashore eats `EatPerHandPerDay`
(1) Food a day from the pile — per quantum, so D2 still holds — and an
unfed quantum banks its shortfall in `hungerDays` rather than starving or
evicting anybody; that is still the parked neglect/anger feature GDD 6
names, just with somewhere true to read from once it is built. Then, only
while `Housed < HousingCapacity` and the pile holds at least
`RecruitFoodCost` (3) Food, `recruitProgress` accrues in days; at
`DaysPerRecruit` (3) it spends the 3 Food and appends a new `OutpostHand`
with `order = Idle`, `born = true` (so `Outpost` knows this row has no crew
body yet — that is its job, not the ledger's), and a name from the new
`VillagerNames` list (~24 storybook names), picked off a hash of the
camp's key and roster size so the same camp reaching the same headcount
twice — live, or replayed from a save — names its newcomer the same both
times. `RecruitLine` gives the sheet one line ("3 of 4 beds · a new hand in
1.2 days" / "no beds" / "no food to feed a newcomer"). All new fields
default sensibly for an old save (`recruitProgress`, `hungerDays` at 0,
`born` false). **Placeholder numbers, none played** — `Outpost.cs`,
`CampWorker.cs` and the UI still need to give the born rows a body and show
`RecruitLine` on the sheet.

## THE FELT CLOSE — 2026-09-22

Kevin agreed the loop lacked a felt close: you sailed back and the pile was
bigger, and nothing said so. Three things, in the order the plan named them,
all compile-clean and **none played yet**.

### "While you were gone" — `OutpostLedger.Absence`, `UI/ReturnSummary.cs`

The ledger keeps one `Absence` record (`away`, public, so JsonUtility saves
it inside the ledger; an old save reads it back as a fresh object with
`sinceSeconds == 0`, which is "nothing open"). `Step` writes into it on every
quantum — units gathered or made per resource, food eaten, days short,
blueprints that went to `Complete`, hands recruited by name — and it is
reset on every DEPARTURE, so it is always "since she last left".
`Outpost.ShowHands` is the one place that knows the transition: `!Watched →
true` is arrival, `Watched → false` is departure. Every caller already runs
`CatchUp()` first, so the record covers exactly the absence. `EndAbsence`
hands the record to `Outpost.LastReturn` only if `Anything` is in it.

The card is drawn from `CampSheet.OnGUI` in `HudLayout.ToastRow` (0.22 of
the safe height, word-wrapped, panel-solid) for 14 s or until tapped.
**Not the prompt slot**: `Prompts.Rank.Toast` loses to the anchor prompt,
which is up at exactly the moment this has something to say. **The clock
runs from the first draw, not the arrival**: arrival fires at landing range
while she is still under way, and a card timed from the water's edge could
be gone before she anchored to read it.

### The target line — `Ship/TargetLine.cs`

The yard priced a rung against the home bank alone; the sheet said nothing.
Now both say where the voyage is pointed: *next: Long sloop — 24 boards and
8 stone · still to make 15 boards · ~5 days across your camps*. "In hand"
counts what is banked, aboard (`VoyageManager.AmountOf`) and piled in any
camp (`Outpost.PiledAcrossCamps`); what is left is divided by the summed
make-rate of every camp (`Outpost.MakeRateAcrossCamps`), and the longer of
the two resources' waits is the number. Three tails: *gathered — sail it
home*, the days, or *nobody is making boards*. The sheet draws the whole
line under the store rows; the yard appends the tail to its own price line.
Cached on a key of every number in it, like everything else on the bar.

### Per-resource rates — `OutpostLedger.RatePerDay`

Each store row gains a right-aligned `+4/day` / `−1.5/day` column. The rate
is derived by walking the hands with the same terms `Step` and `Stalled`
use — a gatherer on a full pile or a worked-out stock counts for nothing,
a sawyer is `+rate` boards and `−rate` timber, every hand eats one Food a
day whether or not the pile can pay — so the column cannot disagree with
what a quantum actually pays. Keyed apart from the pile rows on purpose: a
rate is a property of the ORDERS, and rebuilding it each time a log lands
would be the per-event string building the sheet was rewritten to stop.
This is the "production readout" the plan asked for, per resource rather
than per building: the sheet has no building list, and a hand tapping a
building already gets its own panel.

### Still open

- **All three unplayed.** The wording, the 14 s, the card's height on a
  phone, and whether "~5 days" reads as a promise or an estimate.
- Sailing OUT of landing range without anchoring never calls
  `ShowHands(false)` (pre-existing), so a fly-past leaves the hands drawn
  and `Watched` true; the next real departure by cast-off still opens the
  absence correctly, but a camp only ever passed never gets one.
- Then: upkeep consequences (dim fire, hunger, anger — `hungerDays` and
  `Absence.hungryDays` are the numbers to read), raiders / watchtower.

## UPKEEP CONSEQUENCES — 2026-09-22

Kevin skipped the playtest of the felt close and said build on. The design
was already his (2026-09-13): **the campfire is the provisions gauge,
failure is gradual, hands get angry and that is all.** Built as written,
in one file plus three hooks.

- **The fire.** `OutpostLedger.Health01` = food in the pile over three
  days' eating for everyone here (`DaysOfFoodForBrightFire = 3`), 1 for an
  empty camp so a place with nobody home does not read as dying.
  `Outpost.FeedTheFire` pushes it to `Campfire.health01` after every tick;
  the light is found once among `built`. `Campfire` already dimmed to 0.32,
  never out, for exactly this.
- **Mood, per hand, per quantum.** `OutpostHand.mood` (carried unused
  since 09-13) now slides down 0.5 a day while unfed, scaled by how short
  the quantum went, and back up 0.25 a day when fully fed. Two unfed days:
  furious. Four fed days: content. Old saves read 1.
- **Work.** `WorkFactor(h) = clamp01(mood / 0.5)` scales gathering,
  working and building (builders are a sum of factors now, not a count).
  A camp does not stop dead, it slows, which is what makes the decline
  catchable. **A hand bringing in food is never docked** (`WorkFactorOn`):
  foraging IS gathering food, and without the exemption a camp that ran
  out once could never eat its way back. `RatePerDay` mirrors all of it.
- **The tell.** Headline: *NO FOOD — they forage instead of working*, or
  *2 hands are angry*. Ashore row: `name · gathering timber · hungry`
  (mood < 0.95) / `· angry` (mood < 0.5). No crew tint (that channel is
  seasickness). The behavioural tell the plan recommended — an angry hand
  does not come down to meet the boat — is NOT built.

**Placeholders, none played:** 0.5 / 0.25 / 3 days / the 0.5 anger line.
Still open: mood lives on the ledger row, so it resets when a hand
re-boards (the plan wanted it on `CrewMemberDef`); the return card does not
mention anger; no probe gates any of it.

## TEETH — 2026-09-22 (Phase 3)

Kevin said build on. The gate was written on 2026-09-13: **a lost camp must
read as the player's mistake, not a dice roll.** So a raid is a CLOCK, not a
roll, and the sheet prints the date.

- **Who raids.** The raiders that already patrol islands
  (`TerrainWorldPopulator.BuildRaiders`, one per resource island, eight in
  the world). `EnemyShip.CountAt(island)` counts the ALIVE ones whose
  `Home` is this island and `Outpost.CatchUp` pushes that into
  `OutpostLedger.raiders` before every tick, like `ceilingPer`. The ships
  are the authority: sink the raider and the camp is safe; the minimap
  already draws its patrol ring.
- **When.** Only while the ship is away (`away.Open` — the same record the
  return card reads) and only while something is piled (`Total > 0`).
  `threat` banks `ThreatRatePerDay` per day; at `DaysToRaid` (4) `Raid()`
  fires and the clock restarts. While she is there, raiders are ships she
  can fight, and a lookout on watch lets `threat` decay a day per day.
- **What.** 40 % of every pile's whole units (`RaidShare`, at least one),
  through `Take`, into `Absence.raidRes/raidGot` so the card says *raiders
  took 12 timber, 4 boards* (*raided 2 times — they took …*). Every hand
  loses 0.25 mood. Buildings are untouched — first pass.
- **The watchtower.** `BuildPlans.Watchtower`: 12 timber + 6 stone (capped
  by `PlaytestCostCap`), `position = "lookout"`, takes and makes nothing.
  The kit has no tower, so it is extruded 2.6 × 2.6 × 7.5 m — a tall shed,
  placeholder. Manned (`Guard` = summed `WorkFactor` of hands on the
  lookout, so an angry lookout is half a lookout) the rate is 0; unmanned
  it is halved. `Stalled` exempts the lookout, since making nothing is the
  job.
- **The tell.** Headline: *1 raider offshore · a raid 4 days after you sail
  · a watchtower and a lookout stop it* / *post a lookout* / *the lookout
  keeps them off* / *nothing here to take*.

**Placeholders, none played:** 4 days, 40 %, 0.25, the tower's price and
shape. **Still open:** raider deaths are not saved (a reload brings every
raider back); island character does not set danger (no `Rock01` /
`Verdancy01` on `Island` — the plan's "richest islands are the worst to
leave people on" is unbuilt); a raider is never seen to land; `raiders` on
an unloaded island is the count at the last visit; no probe gates any of
it; a real tower model.

## THE LIVE RAID — 2026-09-22

Kevin, on the clock raid: *"i like the idea of raids … but i want them to
be more interactive, to happen when i as the player is there. they can
still beach and try to steal resources while i fight the ships … the watch
towers should have a canon in them that fires on the raiders — the raiders
will also fire on / try to destroy the watch towers."* And: keep raids
while away too, to make defences worth building — frequency to be decided
after he has felt this one.

- **Trigger — `Combat/RaidDirector`** (static, ticked from `Outpost.Update`
  only while `Watched && HasCamp`, forgotten on departure). 25 s after
  you land at a camp with something piled, if the island's raider is alive
  and not already raiding (`EnemyShip.IdleAt`), it is sent to the nearest
  shore with 5 m of water (`Outpost.ShoreNear`: sixteen headings from the
  fire, half-metre steps to 120 m, shortest wins; `shore` is 1.5 m back up
  the sand). Once per visit.
- **The run — `EnemyShip.Duty.Raid`.** Goal is the water point; the alert
  / chase / stand-down transitions are suspended; the island `Avoid` bend
  and `KeepClear` island shove are skipped for her HOME island (reef, hull
  and player shoves stay). Within 8 m of the point or under 3 m of water
  she is `Beached`: throttle to zero, still rides the sea, still fires.
  `Health01 < 0.5` → `EndRaid` (recall the party, `Duty.Return`).
- **The party — `RaidParty` + `RaidWalker`.** Three bodies from
  `BornVillager.Make("raider", null)` (unparented: the ship rides the
  swell), spaced along the shore. Each walks to the most valuable pile
  (`CampLoading.BestFirst`), takes one unit through `ledger.Take` — loot
  leaves the books only in a raider's hands — carries it (`VillagerActing
  .Carry`) to the shore, repeats. Eight units or 120 s → the ship
  withdraws. Ship sunk → they drop what they carry back on the pile
  (`ledger.Add`) and walk into the sea. `ledger.raids++` on a recall.
- **The tower — `WatchtowerGun`** on every raised Watchtower
  (`BuildingFactory.Arm`; stripped from blueprint ghosts, which register
  nothing). `IHittable` (6 hp, capsule up the tower) and `IFriendly`, a
  marker the player's battery (`CannonBattery.NearestHostile`), lock and
  aim assist skip — a player ball can still physically hit it. A `Cannon`
  on top yaws to the nearest raider in 90 m and, **only while a lookout is
  on watch**, fires every 4 s. Raiders in `TryFire` now choose the nearest
  of the player and the towers, preferring a tower while raiding. A dead
  tower goes through `Outpost.Demolish`: out of `built`, its `raised` row
  out, its lookout idle, the object destroyed.
- **The tell — `RaidBanner`** in the toast row from `CampSheet`: with a
  lookout, *the lookout: RAIDERS making for the beach* the moment she
  turns; the live *RAID — 3 ashore · 4 taken · sink the ship*; then for 8 s
  the result (*got away with 5* / *fled with nothing* / *you sank them —
  the loot is back on the pile*).

**Numbers, all guesses:** 25 s, 3 bodies, 8 units, 120 s, 6 hp, 4 s, 90 m,
half hull. **Still open:** the beached hull bobs at sea level rather than
sitting in the sand; a raider approaching from the far side can clip her
home island's shore (the shove is off for the whole island, not just near
the site); withdrawing from a beach pops her back to the island radius in
one frame; `RaidLine` on the sheet still talks about the away clock; the
tower has no real model and its gun uses URP/Lit; no probe.

## RAIDERS IN THE FLEET'S CLOTHES — 2026-09-22

Kevin: *"how expensive would it be to implement the 5 mid versions of those
ships as the enemy ships?"* Cheap: `FleetVisual.Build(node)` already stands
a stage up from `Resources/Ships/FleetV3`, and the raider's red tint was
already a property block over whatever renderers it had.

- `EnemyShip.Spawn(..., int ladderNode)`; `BuildFromFleet` builds the
  stage under the hull root, records each gun port's muzzle as a child
  transform BEFORE stripping `FleetVisual`/`FleetGun`/colliders, tints
  every renderer but the sails (name or parent contains "Sail"), sizes
  `length` and `hitRadius` off `ShipLadder.Node`. A volley fires at most
  `maxShotsPerVolley` (4) from the target's side. No `Cannon` components,
  so no recoil puff this pass. The player-clone look survives behind
  node −1.
- **Float height: none.** The fleet art is authored waterline at Y = 0 and
  `RideSea` already sets the origin on the wave; `freeboard` would have
  lifted her out.
- `TerrainWorldPopulator.BuildRaiders` maps ring-from-home onto nodes 7..11
  (Long sloop → Guild escort); with eight raiders on the nearest islands
  the live world posted nodes 7, 8 and 9.
- Verified live at noon from a probe camera: `raider-armed-escort.png`.

**Open:** sails do not animate on raiders; speed is not per stage; the
five mid hulls are 67k–89k triangles each — fine on the desktop, and the
phone wants a lower `maxRaiders` or a distance cull before it is measured.
