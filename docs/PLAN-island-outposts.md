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
