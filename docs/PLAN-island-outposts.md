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

- **D1 — Home is outpost zero.** Generalise `World/Village.cs` into a per-island
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
