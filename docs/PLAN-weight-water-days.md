# Weight, Water and Days

_Plan drafted 2026-08-19. Replaces seasickness as the core pressure. Milestone 1 and the whole ocean/weather pass are built; milestones 2–4 are not._

## Where this stands (2026-08-20)

**Done:** milestone 1 (weight, freeboard, green water, bailing, jettison) · wind removed as physics · regional sea state · compass + speed, all banner UI deleted · mountain seas · storm seas with crossing wave trains · **storm sky and light**.

**Storm sky and light — built 2026-08-20.** `SkyDirector` owns one storminess number and drives a procedural sky (`SeaSick/Sky`), the sun, ambient, fog, the ocean's palette, the spray and the camera framing from it. Measured home vs 1500m west: storminess 0.04 → 1.00, sun 1.12 → 0.43, fog 577–1454m → 128–547m, overcast 0.14 → 0.90. Spindrift is torn off measured crests at 110/s; mist is a low haze.

The camera diagnosis in the old note was **wrong on two counts** and is worth remembering: the rig was at 22°, not 41°, and the tilt was not the problem. `ChaseCamera` forced the ship's y to 0, so the lens was **pinned to mean sea level and never followed the ship vertically** — the horizon stayed nailed and the boat bobbed through the frame. It now rides the swell and drops 9m closer to the water in a storm: ship-in-frame spread 0.0218 → 0.0126 of screen height per metre of heave, lens 8.2m above her instead of 18.1m.

**Wave shape reworked 2026-08-20.** Feedback: sharp, unison crests, and *"the larger a wave, the wider it needs to be ... more like a noise map than a row of waves going by."* The base spectrum's directional spread was `peakL/wavelength`, pinning the longest waves to ±9° of the wind; amplitude was weighted by an energy curve that made 71m waves the tallest and 170m waves the smallest. Now amplitude rises in proportion to wavelength (so every wave is equally steep and a tall crest must be a long one), directions are stratified in mirrored pairs across a wide arc, and a `choppiness` factor scales only the horizontal displacement so crests round off at unchanged height. Row-ness 0.35–0.76 → **1.07 everywhere**; crest skew 0.34 → 0.06.

**The western deep made epic 2026-08-20.** The sea read flat because the storm waves ran `amp/L` around 0.011 — a 0.6° face, which no amount of amplitude spread over 250m can make dramatic. `stormAmplitude` 12.7 → **40m** with wavelengths 120–340m, giving **40m of measured hull heave** at 1500m west. Storm trains are gated by `StormAmount01` so the home shelf is untouched — this is the "section of the sea" that is a large epic ocean.

`choppiness` is now self-limiting: Gerstner folds on *horizontal* steepness only, so chop is clamped to hold `chop × totalSteepness × farScale × seaState` under `foldLimit`. **Size is therefore free** — raise `stormAmplitude` as far as the design wants and the surface can never turn inside out.

**Hull seating fixed 2026-08-20 (`569b03c`).** Reported as *"sometimes my boat is all the way over the water and sometimes its all the way under ... following a different set of waves ... happens in the choppy waters"* — which was an exact statement of the cause. `ShipMotor` low-passed the hull's height toward the surface with a 0.18s time constant, and a lag's error scales with how fast its target moves: the water under her now runs at **19 m/s vertically**. Measured seating error **−2.23…+3.39 m, RMS 1.60 m** against 1.3m of freeboard. Now eased *then clamped* to `maxSeatError` 0.30m: **−0.64…+0.71 m, RMS 0.32 m**. Also `Cull Off` on the ocean, and the swell front added to the anti-fold clamp.

---

## ⚠ START HERE — the one open bug

**A hard near/far brightness step in the storm reads as a false waterline with the ship beneath it.** Measured **43.6% grey above the edge against 8.8% below**, the near water flat at 8.7% over a large area. This is what makes the ship *look* sunk even now that the seating is correct, and it is what Kevin last saw.

- **Proved it IS the ocean, not a hole:** tinting the water magenta filled the region. No missing geometry, no culling fault.
- **Did not close it:** `_StormDeep` lifted nearly 4× (0.058 → 0.200), `_SkySoft` 0.30 → 0.65, fresnel exponent flattened from 3.0 to `lerp(3.0, 1.4, storm)`, storm ambient floor raised, fog ramp widened (**reverted** — speculative and changed nothing; storm fog is back at the tuned 70–430m).
- **Why it stalled:** tint-and-photograph does not work on a moving sea. In one controlled run with the ship pinned, the same far-water patch read 50.8%, 31.5%, 24.5% and 28.1% grey across four frames. That is noise, not signal.
- **Do this next:** build a harness that **pins the wave phase** (freeze the time the field is sampled at, or step it manually) so two tinted frames are pixel-comparable, then tint one term at a time. Weak signal so far points at the **foam term** — with `_StormCrest` tinted blue the near water came back B=163.8 against R=66.5 — but that is one unrepeatable frame and is not evidence yet.
- Pixel measurement tool: **`python3 tools/pngprobe.py <shot.png>`** — prints mean sRGB for horizontal bands of a screenshot. Use it instead of arguing about what a compressed PNG looks like; the shading maths predicted 36% grey where the render was delivering 9%.

---

**Still open, and Kevin's call:**
- Is 40m the right size, or should the deep go further? One number: `WaveField.stormAmplitude`.
- The chase camera gets as close as **1.7m above the hull** when the ship is on a crest and the lens in a trough. Dramatic, or too close? `ChaseCamera.stormDrop` (currently 9m) is the dial.
- Green water is now near-constant out west ("water aboard — bailing" most of the time). That is the weight/water loop doing its job in a huge sea, but it has never been balanced against water this big.
- `ShipMotor.angularResponse` (3.6, τ ≈ 0.28s) lags pitch and roll the same way the vertical lag did. Left alone deliberately: the memory records that the rotation Slerp's time constant is load-bearing for `Knockdown` and `AddRecoilRoll` feel, so changing it would disturb tuned combat behaviour.

**Not yet playtested by hand.** Weight, water, no-wind sailing, regional seas, mountain seas and storms have all been verified by probe and screenshot, but nobody has actually sailed a voyage with them.

## The change

Seasickness stops being the clock. Three pressures take its place:

- **Weight** — cargo makes the ship heavy and wet. Greed is the risk.
- **People** — crew can go over the side, and be lost.
- **Days** — time passes visibly, and the settlement eats.

Seasickness itself stays as **cosmetic characterisation only**: green tint, queasy sway, the occasional heave over the rail in genuinely rough weather, at zero mechanical cost. Reserved as the hook for a future "crew happiness" system.

## Why

The sickness meter filled because time passed at sea, and sailing *is* the sea — so the core clock taxed engagement with pillar #1. Sailing well only changed the rate, never the direction. It was also **subtractive with an off-screen cure**: it removed verbs and the fix was to stop playing and go to a beach.

The replacement inverts the shape. **Weight punishes greed, not time.** An empty boat can sail forever; the danger appears exactly when you have something worth losing, and every response to it happens at the helm.

---

## Milestone 1 — Weight and water

**Goal:** a laden boat feels heavy, wet and committed, and shipping water is the new failure gradient.

- **Freeboard.** `CargoLoad01` (exists, already fed by `VoyageManager`) sinks the hull visibly — up to ~0.5 m at full load. The deck sits at 2.05 m, so that is a quarter of the freeboard: the cheapest and most readable change in the whole plan.
- **Green water.** Low freeboard × `SmoothnessMeter.Roughness01` puts water over the rail. New `Bilge01` (0..1) on the ship.
- **The spiral.** Bilge water adds to `HullIntegrity.Wallow01`, which already feeds `SmoothnessMeter` — so a wet ship rolls more and ships more water. **Governor: cap the bilge term at ~0.35 of wallow** so it stays a spiral you can sail out of rather than a death sentence.
- **Bailing.** Crew bail, through `CrewRoster` — a bailing hand is not `Available`, so the guns go quiet. Start at ~0.02 bilge/s per crew member: four hands clear a full bilge in ~12 s, and are off the guns for all of it.
- **Jettison.** One tap puts ~5 units over the side for immediate freeboard. The escape valve. (`VoyageManager.DitchCargo` returns — this time as a player action, not a mutiny.)
- **Handling.** Replace the flat `−18% speed / −25% turn` with something physical: turn *rate* falls only a little, but momentum through a turn rises a lot — sideslip, carry, longer to take way off. **Heavy must mean committed, not merely slow.**

> **Exit test:** is a full boat in a rising sea *more tense and more fun* than an empty one?
>
> This is the make-or-break of the entire plan. If a loaded ship is simply worse to sail, players will rationally under-load to keep the game fun and the whole greed loop inverts. Stop and fix this before building anything on top of it.

### Built 2026-08-19 — implementation notes

- **`CargoLoad01` → `CargoLoad`**, deliberately unclamped. 1.0 is the marked line; `VoyageManager.overloadLimit` (1.6) is the physical maximum. Scene hold capacity is **24**, not the 40 in code — a serialized override, as ever.
- **`TakeDeckCargo`** on `VoyageManager` is the greed switch, surfaced in the anchor panel and reset each voyage. Off, the shore party stops at the line; on, they keep loading. **Nobody overloads by accident** — it is a decision made in harbour that you then live with all the way home.
- **Green water is sampled at the rails**, not from a crest-versus-mean comparison. The first attempt compared the highest wave along the hull to the rail height and produced *exactly zero* ingress at every load, which turned out to be correct physics for the wrong model: **a hull that follows the waves quasi-statically can never ship water, because the rail that dips is always the one over the lower water.** Green water comes from the parts of the motion that do *not* follow the surface — vertical lag, heel in a turn, a gust laying her over, the kick of a broadside — all of which are already baked into the final transform. So `SampleGreenWater` samples the sea *at* the rail points and compares against where those rails actually ended up.
- **The rail height was measured, not guessed — twice.** After fixing the model, green water *still* read exactly zero at every load, so a probe was written to report the **margin** rather than the outcome: the signed gap between the sea and the rail. It came back at **−1.97 m** — the sea's closest approach in a working sea was two metres below the rail, with peak waves reaching only 0.88 m above the hull origin against a rail at 2.35 m. **The deck simply rides too high for the sea to ever touch it**, at any load. `railHeight` is now 1.30 m (the effective waist by the scuppers, not the cap rail) and sink at the line went 0.34 → 0.50 m, closing the 1.6 m that was missing. *Measure the margin, not the outcome — a zero tells you nothing about how close it came.*
- **The serialization trap, again.** All five freeboard fields were new this session, and saving the scene baked their first values into `Sea.unity`, where they beat the C# initializers forever. Re-tuning in code changed nothing until the values were pushed through `SerializedObject` and verified by grepping the `.unity` file.
- **Ingress is capped** (`maxEffectiveImmersion` 0.45 m). A 7.5 m swell front can put metres over the rail, and uncapped that swamps her faster than anyone can react to. At the ceiling five hands bailing still lose ground slowly, so a swell means jettison or find shelter — an emergency, not a coin flip.
- **Handling is momentum, not percentages.** Top speed only −10% at the line (she still runs before the wind); the weight is felt through a `heaviness` term that slows acceleration and *weakens keel grip*, so she crabs through turns and takes far longer to shed way. Overload terms are roughly double the laden ones throughout.
- **Sickness demoted.** It no longer accumulates — it eases toward a target derived from current roughness (22 s up, 45 s down), so crew go green in a squall and get their colour back in calm. `State.Broken`, the work-rate penalty and the walk to the rail are gone: they heave **where they stand** and stay available. `rowingStrain` deleted.
- **Regression the sink caused, found by screenshot:** `SpeedJuice` pins every foam emitter at a fixed height on the hull (the shoulders at +0.15m). That was safe while the ship always floated at one depth — with `SinkDepth` up to ~0.9m the emitters ended up *underwater*, and a loaded ship grew a flat grey sheet through her waist. They now hang off a `FoamEmitters` root that is lifted by `SinkDepth` each frame. **Anything pinned to a fixed height on the hull is now suspect** — the hull moves vertically in a way it never used to.
- **Verified:** freeboard 0 → 0.34 m at the line → 0.71 m at 1.6×; forcing a 0.85 bilge puts all five hands on buckets, silences **both** broadsides, and clears in 13 s — crippled, not doomed; jettisoning 20 units took fill 1.58 → 0.75 and sink 0.72 m → 0.27 m.

## Milestone 2 — Man overboard

**Goal:** losing a person is the sharpest risk in the game, and getting them back is a piece of seamanship.

- **Trigger.** Risk per crew member per second from `CargoLoad01 × Roughness01 × lateral acceleration`. `SmoothnessMeter` already computes the lateral term specifically for carving turns. Target: effectively zero in calm water at any load; roughly one person per 90 s when full, rough, and carving hard.
- **Telegraph.** A visible stagger ~1 s before they go, so it reads as earned rather than random.
- **The swimmer.** Floats, shouts, drifts with `CurrentField` (already exists). Screen marker so they are never simply lost.
- **The rescue.** Come back within ~90 s, get inside ~4 m **with way off the ship** (under ~3 m/s) and a line goes over; hauling them aboard takes ~4 s and occupies two hands. You cannot scoop someone up at fifteen knots — the rescue is turn, return, slow, close carefully.
- **The trap.** Turning hard while heavy in a big sea is exactly what threw them over. A rescue attempt can cost a second person. Emergent, and worth keeping.
- **Floor.** Never let drowning take the crew below a playable number. The ship must always be sailable.
- **Possible revival:** `ShipMotor.AutopilotTarget` and its hard-won upwind tacking are currently dead — a "come about" assist that tacks back to the swimmer is a natural use for them.

> **Exit test:** is the rescue tense, or annoying?
>
> If coming about feels like fighting the controls rather than fighting the sea, the trigger rate or the window is wrong — not the idea.

## Milestone 3 — Day and night

**Goal:** time exists, is visible without any UI, and costs something.

- **Procedural sky, not an imported one.** A gradient dome driven by a single `TimeOfDay01`, with a sun disc, a moon and stars fading in. Built rather than imported because the sea has to match the sky: `Ocean.shader` currently has **hardcoded `_DeepColor` / `_ShallowColor`**, and a static cubemap would leave the water the same blue at midnight. We need the time-of-day system either way; procedural also costs no cubemap memory on mobile and animates by rotating rather than cross-fading two skyboxes.
- **One value drives everything:** sky colours, directional light rotation / colour / intensity, ambient, fog colour and density, and the ocean's own palette. One number, guaranteed consistent.
- **Night has teeth.** Visibility drops: islands, reefs, raiders and the storm front all get harder to read. A ship's lantern gives a small radius. **Sailing at night is the greedy option**, exactly like overloading.
- **Cycle length ~4 minutes real time**, so a 5–8 minute voyage spans one to two days and dawn and dusk come round often — they are the prettiest and the most readable states.

> **Exit test:** can you tell the time of day from the water alone, and does night actually change how you sail?

## Milestone 4 — The settlement demands

**Goal:** the payoff, and the reason days are worth counting.

- **One number to start: food per day.** The village consumes it. You bring it home or you do not. Resist adding a second resource until this one is proven.
- **Excess builds.** Buildings change what a voyage *is* — bigger hold, faster harvest, better pumps, more crew.
- **Failure is soft and lands where it hurts.** An underfed village loses people, which shrinks your crew, which means fewer hands to bail and fight. Reversible, and it bites the part of the game we actually care about.
- Menu-based at first. The GDD already says the village becomes a walkable scene only once the loop is proven.

> **Exit test:** does *"how many days is this taking?"* become a question you ask yourself while sailing?

## Milestone 5 — Combat rejoins the spine

Combat's whole justification was that it spent the sickness clock. It now needs a new cost, or fighting becomes free:

- Hull damage **increases water ingress**. A holed ship with a full hold is the nightmare scenario, and it is a good one.
- Fighting laden is dangerous; fighting light is fine. Another dial on the same greed.
- Raiders become opportunists that prefer loaded ships.

---

## What happens to what we already have

| System | Fate |
| --- | --- |
| `SmoothnessMeter` | **Re-pointed** — drives water shipped and man-overboard risk instead of sickness |
| `CrewRoster` | **Kept unchanged** — bailing and rescue are simply new reasons to be unavailable |
| `ShipMotor.SailOrder` + crewed trim | **Kept** — a crew busy bailing cannot trim, which now matters more than it did |
| `HullIntegrity.Wallow01` | **Extended** — gains a bilge-water term |
| `CargoLoad01` handling | **Reworked** — physical mass rather than flat percentages |
| `VoyageManager.DitchCargo` | **Restored** — as a player action this time |
| `Ocean.shader` colours | **Driven** by `TimeOfDay01` instead of hardcoded |
| `CrewAgent` sickness | **Demoted** to cosmetic: tint, sway, occasional heave, no cost |
| `CrewAgent.Broken`, work-rate penalty | **Cut** |
| `ShipMotor.AutopilotTarget` | **Possibly revived** as the man-overboard "come about" assist |

New: `TimeOfDay` / sky dome, `Bilge`, `ManOverboard`, `Settlement`.

## Decided 2026-08-19

- **Crew loss is not permanent.** A man lost over the side is out for the remainder of the voyage and turns up again at home. The cost is real — you sail on short-handed, with fewer hands to bail and a gun unmanned — without permadeath in a five-minute loop. Build it so the recovery path is a *branch*, not an absence, so switching permanence on later is one flag rather than a rewrite.
- **You can overload.** `holdCapacity` stops being a hard cap and becomes a **marked line** — a Plimsoll line painted on the hull, with the waterline creeping up toward it as you load. Past it you are in deck-cargo territory: the extra rides visibly on deck (`ShipHold` already builds cargo stacks), the effects go **disproportionate rather than linear**, and a big enough sea can sweep deck cargo over the side. The greed can eat exactly what it was greedy for.

## Still open

1. **Does starving the village really shrink the crew?** It is the sharpest consequence available, and the most punishing.
2. **Do we keep the name?** Seasickness becomes cosmetic under this plan.

## Order, and why

Weight and water first: it is the core loop change, it touches only the ship, and its exit test decides whether the rest of the plan is worth building. Man overboard second, because it is triggered by the state milestone 1 creates. Day and night third — cheap, high impact, and it establishes the unit the settlement needs. The settlement last, because a voyage has to have a duration before that duration can be priced.
