# SeaSick — Game Design Document

> Living document. Every AI task (Claude Code or Coplay) should reference this for design intent.
> Status: **v1 — core concept locked 2026-08-17**

## 1. High concept
A one-handed portrait mobile game. You lead an island tribe that must sail to distant islands for resources — but your people *hate* the sea. Sail smoothly to keep their stomachs settled, gather loot, and get home before seasickness turns the crew against you. Spend what you bring back on a better village and a better ship, then sail farther.

**Reference points:** Alto's Odyssey (one-thumb flow-state core), FTL (crew at stations), Cult of the Lamb (village + roster with traits), Sea of Thieves (manned-ship fantasy, scaled way down).

## 2. Pillars
1. **Sailing feels great.** One thumb, physical, wave-riding mastery. If sailing isn't fun, nothing else matters. Everything ships after this does.
2. **Sickness is the clock — and skill winds it.** Seasickness is the mission timer, but it accelerates with rough sailing (slamming waves, erratic turns) and slows with smooth sailing. Mastery visibly extends range before any upgrade does.
3. **The crew are people, not UI.** Sickness, panic, and mutiny are shown through whole-body acting on low-poly characters: green tint, staggering, abandoning a cannon mid-fight to puke over the railing. State is always readable from the gameplay camera.
4. **Home is worth the voyage.** The village (and roster) is the progression spine — every trip makes the tribe visibly better off.

## 3. Core gameplay loop
**Prep** (pick crew, potions, cargo target) → **Sail out** (steer smooth lines through waves, manage sail trim and crew sickness) → **Gather** at the island (simple at MVP) → **Sail home heavier** (worse handling, sicker crew, push-your-luck) → **Spend** on village buildings, ship parts, potions → repeat, farther.

Failure is soft: a too-sick crew mutinies and turns the ship home, ditching cargo to get there faster. You lose loot and time, never your save.

### Escalation stages (always visible before mutiny)
grumbling → refusing stations → someone grabs the wheel and turns for home → cargo goes overboard. The player should always see it coming.

## 4. Platform, player & controls
- **Platform:** mobile, portrait, one-handed. Sessions = one voyage (target 3–8 min).
- **Player role:** the captain — commands, never walks the deck. Crew executes.
- **Camera:** third-person chase, ~10–15 m behind/above the ship; close enough to read whole-body acting. Automatic brief push-ins on notable events (imminent puke, mutiny stage change). No manual camera.
- **Controls (one thumb):**
  - Drag on lower screen = rudder / heading
  - Speed emerges from wind angle + sail trim (not a throttle)
  - Tap crew/station = small contextual order menu (trim, hold, potion, treat)
  - Tap-and-hold (tentative) = precision/zoom view
- **Input tech:** Unity Input System, touch; pointer simulation in editor.

## 5. World & setting
Archipelago seen from the home island outward; farther islands = richer resources = longer, sicker voyages. **Distance is the difficulty curve.**

**Art direction:** low poly, stylized, warm and comedic. Body language over facial animation. Sickness reads at gameplay distance: skin tint shifts toward green, posture sags, walk becomes a stagger.

## 6. Key systems

### Sailing & ocean — MVP, build first
- Gerstner-wave ocean (custom, mobile-cheap). The same wave function drives water visuals, boat buoyancy/rocking, and the "roughness" input to sickness. One system, three jobs.
- Wind direction/strength; speed from sail trim vs wind angle.
- A **smoothness metric** computed from hull motion (impact accelerations, roll/pitch spikes) — this is the bridge between sailing skill and sickness rate, and the number we tune until sailing feels fair and fun.

### Seasickness — MVP
- Per-crew-member meter. Rate = base (time at sea) + roughness (from smoothness metric) − modifiers (traits, cook, doctor, ship comfort upgrades).
- Visible stages on the body, not bars-first: green tint → queasy idle → puking (briefly abandons station) → mad (escalation stages above).
- **Potions:** settle (reset a crew member's meter) and endure (slow accumulation for a stretch). Bought/brewed at home; limited cargo slots.

### Crew & stations — MVP-lite (one crew member), full later
- Stations: helm (player-directed), sails, lookout, cannons (later), below-deck (cook/doctor, later).
- Crew as data from day one: ScriptableObject per villager — name, role, traits, sickness stats — so the roster layer slots in without rework.
- **Roster (post-MVP):** named persistent villagers; recruit from islands; breeding with heritable traits (Cult of the Lamb-style); sea legs grow with voyages; death/retirement. Example traits: Iron Stomach, Weak-Kneed, Fast Trimmer.

### Islands & gathering — MVP simple
Sail into anchor zone → gathering resolves (timer/auto at MVP) → cargo aboard, handling worsens with weight. Depth (choices on the island) comes later.

### Village / home base — post-MVP (menu-first)
Buildings as upgrade cards before they're 3D: housing (crew capacity), blacksmith (ship parts), hospital (heal sickness aftereffects, unlock doctor), farmland (food → cook effectiveness), apothecary (potions). Village becomes a walkable/visible scene only after the loop is proven.

### Ship upgrades — post-MVP
Modular parts on the ship prefab (hull, sails, rudder, comfort fittings, cargo hold). Player-provided modular 3D assets (hull, mast/sail, cannons, rudder) are the basis of the ship.

## 7. MVP milestone list
Each milestone is a committable, testable slice. Sailing first; work backward.

1. **Boat on water** — greybox portrait scene: Gerstner ocean, buoyant hull, one-thumb rudder drag, constant wind. *Exit test: steering around swells is fun with zero art.*
2. **Wind & trim** — speed from wind angle + trim order; smoothness metric computed and visualized (debug).
3. **Sickness on one crew member** — meter driven by smoothness + time; green tint + puke-at-railing behavior; potion as a tap order.
4. **The voyage loop** — depart home dock → reach island anchor → auto-gather → sail home → loot tally screen.
5. **Mutiny & escalation** — the four visible stages, turn-back, loot ditching.
6. **Crew of three** — stations, orders, per-member sickness; traits as data.
7. **Home screen economy** — menu-based village: spend loot on 2–3 buildings + a ship upgrade + potions that visibly change the next voyage.
8. **On-device build** — real phone, portrait, one-handed input polish pass. (Earlier if feel is in doubt — feel can only be judged on the phone.)

## 8. Out of scope (for now)
- Combat: enemy ships, sea monsters. Ship design leaves room for cannons; nothing is built until sailing is fun.
- Walkable 3D village.
- Breeding/genetics depth, multi-ship fleets, weather systems beyond wind, narrative/quests, monetization.

## 9. Open questions
- Which phone (iOS/Android) for device testing?
- Player-provided ship asset formats & import (pivots for rudder/sail rotation).
- Does the captain exist as a visible character on deck?

## 10. Decisions log
_Append-only._

- 2026-08-17 — Project created: Unity 6 (6000.4.3f1), URP 3D template, Input System package.
- 2026-08-17 — Platform locked: mobile, portrait, one-handed. Player is the commanding captain (no deck-walking avatar).
- 2026-08-17 — Sickness rate tied to sailing skill (roughness/smoothness metric), not just time — so the timer rewards engaging with sailing rather than punishing it.
- 2026-08-17 — Art: low poly stylized; whole-body acting instead of facial animation.
- 2026-08-17 — Combat deferred until sailing is proven fun.
- 2026-08-17 — Roster with persistent villagers/traits/breeding planned post-MVP; crew modeled as data (ScriptableObjects) from day one to allow it.
- 2026-08-17 — Development order: sailing first, work backward to base.
- 2026-08-17 — Device target: iPhone (iOS first). Default orientation set to Portrait.
- 2026-08-18 — **Cannons (first pass) + crew of five.**
  - **Four guns, two a side**, one forward and one aft of the mast at x ±1.45, z +4.8 / −1.2, deck height 2.05. First attempt at the full beam (±2.0) buried the carriages — **the hull tumbles home above the waterline**, so guns have to sit inboard with only the muzzle over the rail.
  - **Aiming is the tiller.** You steer to bring a side to bear and fire that broadside (`Q` / `E`, or the two buttons bottom-left showing loaded guns and a reload bar). This deliberately keeps combat *inside* the sailing rather than competing for the player's thumb.
  - Firing gives recoil, muzzle smoke, and a **ballistic `CannonBall`** that arcs, hits the sea, throws a splash and **stamps the wake buffer** — the mark stays on the water afterwards, which is exactly what that buffer was built to make free.
  - **Crew of five** (Bo, Mara, Pip, Tam, Ola), stations laid out clear of the gun positions.
  - **The supplied `player_ship_cannon.fbx` is still an empty 4KB file** and was never re-exported, so the guns are built from primitives like the islands, props and cargo. `Cannon.Build()` is the only place to change when a real mesh arrives.
  - **Not built yet:** nothing to shoot at. Enemy ships / sea monsters, damage on hit, and fleeing-vs-fighting are all still open — see the cannons design discussion earlier in this log.
- 2026-08-18 — **Landing polish.**
  - **One button.** "Land here" now anchors, warps the ship alongside, runs the plank out and sends the crew down it — the landing completes itself once the plank is actually down. Anchoring and disembarking were two taps for a single intention. Once the crew are back aboard the choice is "cast off" or send them ashore again.
  - **Crew always use the plank.** Trips between ship and shore are routed through waypoints (`PathToShip` / `PathToShore`): shore end → deck end → destination. Previously only the outbound walk used the plank and the return cut straight across open water. Ship-side waypoints are re-read every frame (`RefreshShipWaypoints`) because the ship rides the swell — otherwise the crew walk to where it used to be — and an `onPlank` flag makes them follow the timber's slope instead of the water underneath.
  - **Cargo clipping fixed.** The hull floor rises toward the transom, so a stack placed at amidships deck height was buried in the planking back aft — exactly the first layer, `perRow²` = 4 items, which matches the report of "the first 4 don't appear". Stack raised to y=2.95 and moved slightly forward.
- 2026-08-18 — **Physical harvesting: one tree is one log.** Gathering stopped being a rate draining from an abstract island and became work you watch happen.
  - **`ResourceNode`** on every prop: a tree *is* a unit of timber, a boulder *is* a unit of stone. Crew claim a node (so three of them don't converge on one tree), walk to it, strike it several times — it shakes on each blow — then it falls and they **carry the log back**, visible in their hands, and set it down in the hold. Island stock is exactly the number of things standing on it.
  - **`ShipHold`**: every unit in the hold is a real object stacked at the stern, so a full ship looks full and you can read the cargo at a glance. **`Stockpile`** on the home island grows as each unit is carried ashore on return, staggered so it reads as unloading rather than a number changing.
  - **Coming alongside**: dropping anchor now eases the ship into a berth ~11m off the shoreline and **runs a plank out** (`Gangway`) onto dry sand. The beach was raised to stand proud of the water (0.25m at the waterline rising to 2.4m) so there's something to land it on — previously the crew waded ashore.
  - Crew shore speed 3.2 → 6.5 m/s; node counts scaled to what a crew can actually walk (5–26 per island) and hold capacity 40 → 24 to match.
  - **Bug found in testing:** `Island.Extract` still ran the old logic that hid props as the count drained. With props now *being* the nodes, trees were vanishing unharvested and the island reported stock that no longer existed — 10 trees felled but only 6 logs. Verified after the fix: 11 nodes, 5 standing, 4 in the hold and 2 in crew hands.
- 2026-08-18 — **Hull works the sea; waves break against it.**
  - **Six float points instead of three.** With a bow and two stern points the hull was effectively rigid; sampling down the length (stem, fore pair, aft pair, transom) and fitting mean height / fore-aft / port-starboard lets the **bow ride up while midships is still in a trough**. Roll and pitch are clamped (±20° / ±16°) — the raw fit reached nearly 30° beam-on to a swell, which read as capsizing rather than working the sea.
  - **Impact spray**: `ShipMotor.LateralWaveAccel` exposes the sideways force on the hull, so a sea striking the beam throws a burst of spray **off the correct side**, and a bow slam (surf force reversing hard as the stem drops) throws one forward. Each impact also stamps foam into the wake buffer, so the mark **stays on the water** after the spray has gone.
  - Measured beam-on to a swell: roll range **39°**, ~15 spray impacts per 30 seconds.
- 2026-08-18 — **Living water: persistent wake, surface detail, shader-side foam.**
  - **The core problem was that the wake was an analytic shape bolted to the hull** — evaluated relative to the ship's *current* position and heading, so it rotated with the ship instead of being left behind. That's the tell that made the boat read as sliding over a surface.
  - **`WakeTexture`**: a 512² world-anchored RGBA buffer covering 420m around the ship. Each frame it scrolls to compensate for movement (snapped to texel boundaries so it never crawls), decays, and stamps whatever is touching the water. R = displacement, G = foam. The ocean shader samples it for both. **The wake is now history** — it stays where you sailed and curves through your turns. `WakeTexture.Splash()` is the public hook, so **cannonball splashes are essentially free** when combat lands.
  - **Surface detail**: the water previously had *no texture at all* — flat-shaded colour with a specular highlight, which is why it read as plastic. Added analytic ripple normals (4 directional wavelets, gradient computed in closed form, no texture fetch).
  - **Shader-side foam** replaces two hacks: crest foam from steepness/lift, shore surf from the island data already uploaded for wave attenuation, plus shallow-water tint. **`SeaStreaks` and `GustVisuals` are deleted** — both were quads laid on the surface that read as floating rectangles from any raised camera. Gusts are now drawn *by the water*: extra ripple and a darker scuffed patch, from an uploaded `_SS_Gusts` array.
  - Bugs found: wake stamps were applied **per-frame rather than per-second**, so accumulation scaled with framerate and saturated instantly; crest foam threshold was tuned before the waves got steeper and was firing across the whole sea. Also fixed a cluster of **initialisation-order NREs** (`WaveField`, `WindField`, `CrewAgent`) — Unity does not guarantee script order, so anything sampled by other systems must build on demand rather than trusting `Start` to have run.
- 2026-08-18 — **Wind becomes a gradient, not a gate.** The recurring "sailing back is unreasonably punishing / I have limited control" complaint, finally addressed at the root. Diagnosis: the wind was built as a *gate* (directions you cannot go) on the system that connects the player to all the content, so it blocked access rather than shaping it. Also acknowledged: doubling the map scale the previous session made this materially worse.
  - **Polar floor 0.34 → 0.55.** Head-to-wind is now merely slow, never a wall. Measured: pointing *straight at* an upwind target closes at **10.6 m/s** versus **10.9 m/s** on the optimal tack — so the zigzag is no longer required, while a beam reach still does 20.4 m/s vs 11.6 upwind. **Catching the wind is a reward; going against it is not a punishment.** maxSpeed 18 → 21.
  - **Oars** (`R`, or the button): 9 m/s, wind-independent, always available — the guaranteed way home and the answer to "limited control". Costs **2.1× crew sickness rate**, so it's a real decision, and it fits a tribe who hate the sea.
  - **Currents** (`CurrentField`): 5 fixed streams up to 6.4 m/s, added to `waterVelocity` so they carry the hull bodily. Foam streaks now **drift with them**, because an invisible current is just a mysterious shove. Learning where they run is worth something.
  - **Wind shadows**: sailing into an island's lee costs drive (measured 0.64× just off the shore, recovering by ~260m past it). Reach is measured from the shoreline, not the centre — otherwise a big island swallows its own shadow.
  - **Wind shifts**: wander ±28° → **±62°** on a slower cycle, so over a voyage the wind genuinely changes which islands are upwind and a beat out can become a reach home.
  - **"Ship too big for the water"**: peak wavelength 75m → ~45m and steepness 0.34 → 0.46, so a 21m hull rides *through* the sea instead of sitting on it; vertical/angular response and turn heel raised; camera pulled in and down (20m back, 13m up, ~16° tilt).
- 2026-08-18 — **Scale pass: islands are land masses.** Feedback that ship-to-land proportion was "way off".
  - **Bigger:** radius 13–72m → **45–200m**. The ship is 21m, so the smallest island is now ~4 ship-lengths across and the largest **~20** (measured 180–235m outline radius). Home island converted from the old authored dome to a generated 95m island.
  - **The real culprit for the broken proportion was prop scaling.** Trees were being scaled up with island size, so every island looked the same size regardless of radius. Props are now a **fixed real-world size** — a tree is a tree, and that's precisely what tells the eye how big the land is.
  - **More complex shapes:** outline noise went from 2 octaves to **3** (big lobes for bays and headlands, finer octaves to rough up the coast) and the range widened from 0.70–1.18× to **0.45–1.35×** radius — measured variation rose from 18–27% to **26–41%**. Sectors 30 → 46, rings 13 → 16. Added per-sector height variation so peaks form ridges and saddles rather than tidy cones.
  - **Fewer and further apart:** 16 islands → **10**, spread 120–520m → **280–1150m**, with genuine overlap rejection (islands are too big now to trust the spiral) and a 170m minimum of open water between shores.
  - Everything downstream had to grow with it: ocean tile 620m → **1500m** (260 quads), fog 220–480 → **600–1500**, minimap range 750 → 1500m, shore wave falloff 34 → 60m, home arrival radius 60 → 165m.
  - Three bugs found and fixed while doing it: per-sector height variation **tore the summit apart** (every sector shares the centre point but computed a different height there — now faded out near the centre and normalised around 1); the **chase camera clipped inside** the new 100m mountains (now lifted above terrain like it is above water); and the ship spawns inside the enlarged home radius, which **completed a 0-second voyage on startup** (now requires actually leaving before it can arrive).
- 2026-08-18 — **Autopilot can tack; upwind is properly viable.** Reported as "can't travel against the wind — even mutiny can't reach home". The mutiny clue was the diagnosis: `AutopilotTarget` steered *directly* at home, so an upwind target parked the ship in the no-go zone forever. A mutiny with home upwind was effectively a soft-lock.
  - **`ShipMotor.CourseFor()`** now returns a course that actually *reaches* a point: straight there when possible, otherwise beating on the best layline (`BestUpwindAngle`, computed from the polar — currently 50°), flipping tacks when the ship strays outside an upwind corridor. Shared with the navigation tape so the autopilot and the player's advice agree.
  - Two bugs found while testing it. **Cross-track error was measured against the ship-to-target line, which is by definition always perpendicular to its own normal — so it was always zero** and the tack never flipped on its own merits; it's now measured against the upwind corridor through the target. And beating/direct mode flickered without **hysteresis** (engage inside no-go + 3°, disengage only past no-go + 14°).
  - Wind is now sampled *before* steering each frame, since the autopilot needs it to decide whether to tack.
  - Polar floor raised: head-to-wind 0.22 → **0.34** (VMG upwind 2.4 → **5.1 m/s**), close-hauled band lifted. Drift reduced (`waveDrift` 1.3 → 1.0, `swellDrift` 5.5 → 2.5) so **leeway can never exceed what the ship makes head-to-wind** — during a big swell it previously could, making upwind travel genuinely impossible.
  - Verified worst case: parked 600 m **dead downwind** of home, bow into the wind, autopilot only — closed 600 → 283 m in 44 s, holding close hauled at 49°.
  - **Testing note (third time this pattern has bitten):** `MutinyController` reasserts `AutopilotTarget = null` every frame, silently overwriting the probe. Disable the controlling component when testing, and sample in `LateUpdate`.
- 2026-08-18 — **Minimap + solid foam.**
  - `MiniMap` (top-right, north-up): islands as discs **colour-coded by resource** (Timber green, Stone grey, Ore gold, Spice pink, bare rocks tan), home ringed orange, reefs as red dots, ship as a heading arrow at centre, and **the swell drawn as a moving band** — the storm is a thing you run from, so it belongs on the map. North-up rather than ship-up because it complements the bearing tape: the tape says where to point, the map says what's out there. Ship status panel now sits under it via `MiniMap.ReservedHeight`.
  - **Foam made solid.** Translucent billboards read as grey squares over dark water. Bow spray and the shoulder foam (the water actually being displaced) now use an opaque *lit* particle material so they catch the sun and read as thrown water with mass; since opaque can't alpha-fade they **shrink and tumble out of existence** instead. The long wake stays translucent so it still dissolves into the sea rather than popping.
- 2026-08-18 — **Navigation tape** (feedback: "there has to be a way to travel against the wind, I can't get back to base"). **Measured first: the physics was already fine.** Best upwind course is ~50° off the wind giving **8.3 m/s of velocity made good toward it**, and even head-to-wind nets +2.4 m/s. Nothing was blocking the player — the game simply never told them that the answer to "home is upwind" is "steer 50° off the wind and zigzag".
  - `NavigationAid` draws a bearing tape across the top: your bow (white), **the no-go zone shaded red**, the wind, home (cyan, labelled above), and **the best course to make ground — green, labelled below**. When home lies inside the no-go cone it picks the favoured layline (the smaller turn) and prompts to tack.
  - Labels alternate above/below the tape so they can't collide when the marks converge; off-screen marks get a direction arrow instead of silently pinning to the edge.
  - `waveDrift` 2.0 → 1.3 to ease the constant leeway that was taxing every upwind leg on top of the polar.
  - **Lesson: when a player says something is impossible, measure whether it is.** The fix here was an instrument, not a physics change — and changing the physics would have made the sailing worse.
- 2026-08-18 — **Organic layered islands + shore wave attenuation.**
  - **`IslandMeshBuilder`** generates each island as a radial mesh: a Perlin-noised outline (measured 18–27% radius variation, so no more circles), a height profile that slopes out of the sea across a beach then climbs inland, and triangles split into **three submeshes — sand / dirt / rock** by height, giving crisp low-poly banding. Three kinds: `SandOnly` (bare shelter rocks), `SandAndDirt`, `Mountainous` (peak at 0.52× radius).
  - **Cliffs are real geography.** Some angular sectors get a near-zero beach fraction, so the land drops sheer into the water there. `Island.HasBeachToward()` gates anchoring — the button reads *"sheer cliff — find a beach"* — so **which side you approach from now matters**. Observed spread: some islands landable on all 30 bearings, the most rugged only 20.
  - `Island` now stores the outline: `RadiusAt(bearing)`, `RadiusToward(pos)`, `MaxRadius`. Hull collision and prop placement use the real shoreline instead of a circle.
  - **Wave clipping fixed.** Waves shoaled straight through the islands because the ocean is one continuous surface. `WaveField.ShoreAttenuation` fades wave amplitude to zero within `shoreFalloff` (34m) of any shoreline, and the ocean shader applies the **identical** falloff from an uploaded `_SS_Islands` array — so the water you see and the water the ship floats on still agree. Verified: 1.06m waves 80m out, 0.00m at the shoreline.
- 2026-08-18 — **The sea moves the ship.** Waves previously only pushed *along the hull*, so a beam-on swell could not move the boat at all and sitting still in a storm did nothing.
  - Wave force is now a **2D world vector** from the surface gradient (sampled in x and z), not a scalar along the bow. It shoves the ship whichever way it faces. Along-hull component passes in full, across-hull is scaled by `lateralWaveScale` (0.4) because a hull resists a beam sea, then capped by `maxWaveAccel`.
  - **Stokes drift is modelled as the water mass moving, not a force.** Key insight after two failed attempts: adding drift to `velocity` let keel grip and sail drag eat it (a keel resists moving *through* water, not being carried *with* it), and adding it as a constant *acceleration* accumulated without limit. It is now a separate `waterVelocity` added at integration: `position += (velocity + waterVelocity) * dt`. Anchoring holds against it.
  - Measured with sails genuinely furled: a swell now carries the ship **42.5 m in 8 s at 7.3 m/s**. `waveDrift` / `swellDrift` are the dials (target speeds in m/s).
  - **Testing lesson:** the first three measurements were invalid — the probe set `SailSetting = 0` in `Update`, but `HelmInput.Update` reasserts full sail afterwards, so it was measuring ordinary sailing (~16 m/s) and tuning the wave cap changed nothing. Disable the input component and sample in `LateUpdate`. A physics number that refuses to respond to its own parameter means the harness is wrong, not the value.
- 2026-08-18 — **Camera tilt corrected to show the horizon.** 41° was too steep — with a 60° vertical FOV the frame spans tilt ±30°, so the horizon leaves the screen entirely above ~30°. Now 25m back / 19m up looking 20m ahead ≈ **22°**, putting the horizon about 13% down from the top while still looking down onto the deck.
- 2026-08-17 — **Three-quarter camera.** Moved to 24m back / 30m up, looking 10m ahead at water level — **~41° below horizontal, up from ~4°**. The old rig was almost at eye level with a 30m lookAhead, which flattens the angle toward the horizon and puts the rig in the player's face; the sails filled the screen. Now the deck, the crew and the surrounding water all read at a glance. **The tilt is (height − lookHeight) / (distance + lookAhead)** — that ratio, not height alone, is the dial. Shore-party framing decoupled from these values (keys off crew spread instead) so a high chase cam doesn't leave the harvest as specks. Trade-off to watch: a higher camera reduces perceived speed, so foam/streak density and FOV response may need a compensating nudge.
- 2026-08-17 — **Playability tuning** (feedback: in irons was a trap, storm too brief with too much warning, anchoring too slow).
  - **Polar loosened so upwind is slow, never a trap.** No-go zone 45° → 35°; head-to-wind drive raised 0.02 → 0.22 (measured: 1.0 m/s → 5.6 m/s), close-hauled 0.50 → 0.85 at 50°. `minTurnRate` 9 → 15 deg/s so a stalled ship can always steer out. Tacking still pays; being caught head-to-wind no longer strands you.
  - **Storm reshaped:** warning 37s → **~15s**, time inside the band ~24s → **~67s**. Band half-width 115 → 250m, front speed 9.5 → 7.5 m/s, spawn 470 → 360m, interval 70–110s.
  - **Anchoring and weighing are instant** (both timers 0, swell penalty removed). State flips in the same frame — verified Underway→Anchored and Anchored→Underway with no frames waited.
  - **Bug fixed:** the Ocean object had accumulated **two WaveField components**, both running `Update` and both writing the global shader uniforms every frame. Cause: an earlier setup script did `DestroyImmediate` + `AddComponent`, but `[RequireComponent]` on `OceanRenderer` silently blocked the destroy. Lesson: never destroy-and-re-add a required component to refresh defaults — set fields through `SerializedObject` instead. A duplicate-component sweep now covers the ocean and ship objects.
- 2026-08-17 — **GPU water displacement** — the ocean moved off the CPU entirely.
  - New `SeaSick/Ocean` shader (`Assets/_Project/Art/Shaders/Ocean.shader`) does the whole Gerstner sum, the swell front, and the hull's trough/bow-wave/Kelvin-wake in the **vertex stage**. Normals by central difference (three wave sums per vertex — free on a GPU).
  - `OceanRenderer` now builds a flat grid **once** and only snaps its transform to follow the ship. `WaveField.PushToGpu` uploads the same per-frame constants the CPU physics uses (`_SS_Waves` packed as direction·k, amplitude, phase−ωt), so the visible surface and the surface the ship floats on cannot drift apart. `HullDisplacement` uploads ship pose and hull/wake shape.
  - **Result: ocean CPU cost 12.9 ms → 0.02 ms** (~650×), *while* raising the mesh from 5,329 to 22,801 vertices, extent 460m → 620m, and the spectrum from 10 to 14 waves. The CPU now only samples the ~30 points physics actually needs.
  - Shader also fixed the fresnel direction (grazing angles reflect sky and read bright; looking down shows depth) and adds crest foam.
  - Mobile is no longer gated on this. Remaining CPU water cost is trivial.
- 2026-08-17 — **Spectrum ocean + hull displacement** (researched against shipped games — Sea of Thieves uses FFT water, not a few Gerstner waves; see docs sources in session notes).
  - **Wave spectrum replaces hand-placed waves.** 10 components spread logarithmically from 9m to 190m, energy peaking at a wind-driven peak wavelength, each with a random phase. **Directional spreading is physical**: long swell runs tight to the wind, short chop fans out — verified in a generated set where 78–167m waves clustered at 76–102° while 10–22m chop scattered to 24°, 140°, 155°. This is why it no longer looks like one marching set of rollers.
  - **Sea state drifts over minutes** (Perlin, 0.14 glassy → 1.15 rough), scaling every amplitude. Genuinely calm stretches now happen, and crew sickness follows the weather for free — you can wait out a rough patch.
  - **Hull displacement is real**: the ocean mesh queries `HullDisplacement` and deforms — a 1.25m trough under the hull, a bow wave that grows with speed, and a **Kelvin wake at the true 19.47° half-angle** (verified: crest lands at 14.2m abeam, 40m astern). Plus shoulder foam thrown out along the hull and spray that spikes when the bow drops onto a face.
  - **Performance:** `Displace` was recomputing k, ω and amplitude per wave *per vertex* — hoisting them into per-frame constants took the mesh rebuild from 12.9 ms to **8.5 ms**. Grid reduced to 72², normals now computed by central difference instead of Unity's `RecalculateNormals`, decorative systems use a 1-iteration `SampleHeightFast`. **Still ~half the 60fps budget — Burst/Jobs or a GPU vertex shader is required before mobile.** Note: frame rate measured in an unfocused editor is throttled to ~10fps and is not a valid signal; time work in milliseconds instead.
- 2026-08-17 — **Ocean swell, calm lens, and a real UI** (feedback: speed oscillated too much, didn't feel like an ocean, camera zoom caused motion sickness, UI ate the screen).
  - **Waves rebuilt as ocean swell**: primary wavelength 130m (≈6 ship lengths, was 48m) at gentler steepness, with short waves demoted to decoration. Long faces mean slow, rolling surges instead of rapid chop. Surf force also smoothed (`surfResponse`) and softened — measured peak surf accel fell from ±4.2 to ±1.0 m/s².
  - **`surfPower` = 22 is the master feel dial** (30 was too aggressive, 9 too subtle). `surfOvershoot` 1.25.
  - **Speed & control**: maxSpeed 18 (was 15), turn rates 9/34 (were 6/26) for more helm authority.
  - **Camera lens calmed**: total FOV swing cut from ~20° to ~5.5° and eased ~3× slower. FOV motion is the main cause of simulator sickness in a chase cam — keep it small.
  - **UI rebuilt** around a shared `UITheme` (one palette, one type scale sized off the short screen edge, shared bar/banner/button helpers). New compact `StatusHUD`: crew sickness pips top-left, hull + hold top-right, home distance bottom-left — everything else is contextual. Warning banners are slim, the tally is a styled panel with a real button, DevHUD's diagnostic wall is now **off by default (F1)**.
- 2026-08-17 — **Pace pass** (response to "movement still feels slow and not engaging"). Re-diagnosed: *slow* was a **scale** problem (a 21m ship at 15 m/s covers 0.7 ship-lengths/sec, with a camera 25m back and nothing near the lens) and *unengaging* was a **cadence** problem (one meaningful decision every ~20s; good games ask every 1–3s). Fixes:
  - **Wave riding** — ShipMotor samples the surface slope ahead of the bow: run down a face and you accelerate, climb one and you bog down. Turns the ocean into terrain and gives continuous, second-to-second steering texture. Surfing can carry you 1.5× past nominal top speed; overspeed drag reduced so boosts persist. Measured: speed swings **9.6 → 14.3 m/s** on the same heading purely from wave choice (surfAccel −3.0 to +4.2 m/s²). `surfPower` is the master feel dial (30).
  - **Camera down at the water** — 17m back / 6.5m up (was 25/12.5), FOV opens with speed² and punches +7° when surfing, never dips below the surface. This alone is most of the perceived-speed fix.
  - **SeaStreaks** — ~46 foam streaks lying on the water near the ship, recycled from astern to ahead. The ship moves past *them*, which is what an empty ocean was missing.
  - **SeaAudio** — wind, water rush and hull slap synthesized in code as filtered noise (no audio assets, no Coplay dependency). Wind rises in gusts, rush tracks speed and surf, hull slaps when the bow drops into a trough.
  - **Denser map** — islands now 120–520m (was 190–880m), so arrivals and decisions come far more often.
  - **Reefs** — 26 half-submerged hazards with foam warning rings scattered between the islands; hitting one at speed costs ~37% hull. Open water now demands attention everywhere.
  - Deferred: manual sail trim (adds control complexity to a one-thumb game; wave riding already supplies the moment-to-moment texture).
- 2026-08-17 — **Islands are solid, legible, and watchable.**
  - **Grounding & hull** (HullIntegrity): islands have a hard radius (+7m hull margin). Impacts above 2.5 m/s damage the hull (~29% at 14 m/s), stop the ship dead, and jolt the crew (sickness + anger spike). Damage costs top speed (−40% at zero hull) and adds wallow to the roughness metric, so a wrecked ship makes the crew sicker.
  - **Repairs**: careen ashore and rebuild using **Timber from the hold** (~12 timber for a full rebuild) — the first real sink for a resource, and it competes with harvest time. Verified 40% → 100%.
  - **Resource props**: each island wears its material — palm-ish trees (Timber), grey boulders (Stone), dark rock with glowing gold veins (Ore), flowering bushes (Spice). Shelter-only rocks stay bare. **Props deactivate as the island is harvested**, so you can see an island being stripped (14 trees → 1 at zero resource). Type is identifiable from open water.
  - **Shore-party camera**: ChaseCamera takes a PointOfInterest while crew are ashore, biasing toward the crew and backing off by their spread so the harvest stays on screen. Crew now also disembark on the shore arc **facing the ship**, and ride the wave surface while crossing open water instead of walking through mid-air.
  - Fixed: props were spawning inside the grass-hill dome (Island.SurfacePoint now takes the hill into account, not just the sand).
- 2026-08-17 — **Shelter loop** (answer to "everything feels random — what can I DO?"). The sea becomes a map of harbours and the player gets a verb.
  - **Anchoring** (AnchorController): near any island → drop anchor (3.5s; **×2.6 in a swell**) → send crew ashore → they harvest AND recover → recall → weigh anchor (2.8s). Ship holds station while anchored, helm disabled. IMGUI buttons register with UIBlocker so tapping never steers.
  - **Shore leave** (CrewAgent): crew unparent, walk to the beach, sickness drains to a **partial floor of 0.2** (full recovery awaits doctors) and anger cools. Transit neither heals nor harms. Anchored ships still rock — only land is truly safe.
  - **Procedural archipelago** (ArchipelagoGenerator): 15 islands, size and richness rising with distance; small ones (<21m) are pure shelter with no resources. Resource tiers unlock outward: Timber → Stone → Ore → Spice. Verified spread: r=14 barren rock at 207m … r=78 Spice island (68 units) at 895m.
  - **Swell as a moving front** (WaveField + SwellDirector): spatial band sweeping the world at 9.5 m/s — measured **7.5m waves inside vs 1.8m calm 600m ahead**, so it's visible on the horizon and outrunnable at 15 m/s. Warning shows countdown + nearest-shelter distance. Being caught spikes roughness (0.20 → 0.53) and fast-tracks mutiny.
  - **Hold**: 40-unit capacity across multiple resource types; tally reports the haul and cumulative stores.
  - Roughness ceilings retuned so ambient sea reads calm (0.34 → 0.20) and only real punishment climbs — this is what makes good sailing legible.
- 2026-08-17 — Drift + wind arrow pass (Sea of Thieves reference): ShipMotor velocity decoupled from heading — thrust builds along the hull, sideways slip decays via keelGrip (2.2/s), so turns carve and the stern slides (DriftAngleDeg exposed; −8° observed in hard turns). maxSpeed 15, accel 2.6. WindArrow: 3D arrow hovering above the stern, always points downwind, stretches + turns orange in gusts. Camera closer/lower (25/12.5) with fovSpeedBoost 10.
- 2026-08-17 — Sailing feel pass (response to "sailing is boring — just pressing W"): the sea became terrain. (1) WindField: base wind wanders ±28°, 6 gust patches drift downwind — ×1.45 speed inside but extra heel + roughness; visible as dark cat's-paw ripple scatters (GustVisuals). (2) RogueWaveDirector: transient big swell through WaveField every 28–50s with orange warning banner and sailor-speak direction call; bow-on is safe, beam-on punishes — emerges from the 3-point seating, no special code. (3) SalvageSpawner: 7 crates (+2 timber, steer within 6m) + 16 flotsam planks as speed reference. (4) SpeedJuice: bow spray + wake foam scaling with speed; ChaseCamera FOV opens up to +7° with speed². All verified in play; fun judgment awaits Kevin's playtest.
- 2026-08-17 — Milestone 5 built: mutiny escalation. CrewAgent.Anger01 rises while sickness ≥0.8, cools below 0.6 (so puking literally buys off the mutiny; a calm ride home cools tempers — both emergent). MutinyController stages from average anger: 1 grumble (banner) → 2 refuse stations (SailCap 0.6) → 3 seize helm (ShipMotor.AutopilotTarget steers home, rudder ignored) → 4 ditch cargo (3 timber per 6s via VoyageManager.DitchCargo). Anger shows as red flush on bodies. Docking clears everything. Verified end-to-end including a total-loss voyage (+0 timber tally).
- 2026-08-17 — Crew of three: Bo (deckhand, weak stomach), Mara (steerer, iron stomach 0.55), Pip (young deckhand 0.15) — varied skin tones and sizes, stations spread across the deck.
- 2026-08-17 — Ship speed up 50% per feel feedback: maxSpeed 8→12 m/s, acceleration 1.6→2.2.
- 2026-08-17 — Milestone 4 built: VoyageManager loop (Outbound → Gathering 6s at anchor radius → ReturnLeg with cargo → Tally at home; tap/space restarts; timber banks across voyages). Cargo penalty: −18% speed, −25% turn rate. Two islands in Sea.unity: Island_Home (orange beacon, z −75) and Island_Windward (gold beacon, ~300m downwind) — destination is downwind/down-swell so the loaded return leg is naturally the hard leg. Crew rests (sickness 0) while docked. UI is dev-grade IMGUI until the loop is feel-approved.
- 2026-08-17 — Milestone 3 built: CrewMemberDef ScriptableObject (name/role/ironStomach) + CrewAgent on a capsule-placeholder villager ("Bo", weak stomach, Assets/_Project/Settings/Crew_Bo.asset). Sickness rate = (base + roughRate*roughness²) * (1 − 0.5*ironStomach); stages: green tint (MaterialPropertyBlock) + sway from ~0.2, walk to rail and puke at 0.75 (relief −0.25), return to station. Verified full cycle in play mode. Beam-sea time-to-puke ≈ 40s, smooth sailing ≈ 2min — tune rates when voyages exist. Mutiny stages are M5.
- 2026-08-17 — Milestone 2 built: SmoothnessMeter (roughness 0..1 from heave rate + pitch/roll rate + lateral/centripetal accel, EMA-smoothed) on the ship; sail setting as the trim decision (W/S or up/down keys; SailSetting on ShipMotor, min steerage 0.15); DevHUD overlay (F1 toggles); horizon fog. Verified spread: down-swell cruise 0.12, beam sea 0.37, up-swell 0.39, hard turn at speed 0.41-0.45 — course choice is the dominant comfort skill (by design; throttle mainly matters before maneuvers). Sickness formula (M3) should amplify differences, e.g. rate ∝ roughness².
- 2026-08-17 — Milestone 1 built: Gerstner WaveField (single source of truth for water), CPU-displaced ocean tile following the ship, kinematic ShipMotor (3-point wave seating, wind-angle speed, turn heel, sail auto-trim, rudder visual), one-thumb HelmInput (bottom-45%-of-screen absolute tiller + A/D in editor), yaw-only ChaseCamera. Scene: Assets/_Project/Scenes/Sea.unity.
