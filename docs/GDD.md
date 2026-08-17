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
- 2026-08-17 — Drift + wind arrow pass (Sea of Thieves reference): ShipMotor velocity decoupled from heading — thrust builds along the hull, sideways slip decays via keelGrip (2.2/s), so turns carve and the stern slides (DriftAngleDeg exposed; −8° observed in hard turns). maxSpeed 15, accel 2.6. WindArrow: 3D arrow hovering above the stern, always points downwind, stretches + turns orange in gusts. Camera closer/lower (25/12.5) with fovSpeedBoost 10.
- 2026-08-17 — Sailing feel pass (response to "sailing is boring — just pressing W"): the sea became terrain. (1) WindField: base wind wanders ±28°, 6 gust patches drift downwind — ×1.45 speed inside but extra heel + roughness; visible as dark cat's-paw ripple scatters (GustVisuals). (2) RogueWaveDirector: transient big swell through WaveField every 28–50s with orange warning banner and sailor-speak direction call; bow-on is safe, beam-on punishes — emerges from the 3-point seating, no special code. (3) SalvageSpawner: 7 crates (+2 timber, steer within 6m) + 16 flotsam planks as speed reference. (4) SpeedJuice: bow spray + wake foam scaling with speed; ChaseCamera FOV opens up to +7° with speed². All verified in play; fun judgment awaits Kevin's playtest.
- 2026-08-17 — Milestone 5 built: mutiny escalation. CrewAgent.Anger01 rises while sickness ≥0.8, cools below 0.6 (so puking literally buys off the mutiny; a calm ride home cools tempers — both emergent). MutinyController stages from average anger: 1 grumble (banner) → 2 refuse stations (SailCap 0.6) → 3 seize helm (ShipMotor.AutopilotTarget steers home, rudder ignored) → 4 ditch cargo (3 timber per 6s via VoyageManager.DitchCargo). Anger shows as red flush on bodies. Docking clears everything. Verified end-to-end including a total-loss voyage (+0 timber tally).
- 2026-08-17 — Crew of three: Bo (deckhand, weak stomach), Mara (steerer, iron stomach 0.55), Pip (young deckhand 0.15) — varied skin tones and sizes, stations spread across the deck.
- 2026-08-17 — Ship speed up 50% per feel feedback: maxSpeed 8→12 m/s, acceleration 1.6→2.2.
- 2026-08-17 — Milestone 4 built: VoyageManager loop (Outbound → Gathering 6s at anchor radius → ReturnLeg with cargo → Tally at home; tap/space restarts; timber banks across voyages). Cargo penalty: −18% speed, −25% turn rate. Two islands in Sea.unity: Island_Home (orange beacon, z −75) and Island_Windward (gold beacon, ~300m downwind) — destination is downwind/down-swell so the loaded return leg is naturally the hard leg. Crew rests (sickness 0) while docked. UI is dev-grade IMGUI until the loop is feel-approved.
- 2026-08-17 — Milestone 3 built: CrewMemberDef ScriptableObject (name/role/ironStomach) + CrewAgent on a capsule-placeholder villager ("Bo", weak stomach, Assets/_Project/Settings/Crew_Bo.asset). Sickness rate = (base + roughRate*roughness²) * (1 − 0.5*ironStomach); stages: green tint (MaterialPropertyBlock) + sway from ~0.2, walk to rail and puke at 0.75 (relief −0.25), return to station. Verified full cycle in play mode. Beam-sea time-to-puke ≈ 40s, smooth sailing ≈ 2min — tune rates when voyages exist. Mutiny stages are M5.
- 2026-08-17 — Milestone 2 built: SmoothnessMeter (roughness 0..1 from heave rate + pitch/roll rate + lateral/centripetal accel, EMA-smoothed) on the ship; sail setting as the trim decision (W/S or up/down keys; SailSetting on ShipMotor, min steerage 0.15); DevHUD overlay (F1 toggles); horizon fog. Verified spread: down-swell cruise 0.12, beam sea 0.37, up-swell 0.39, hard turn at speed 0.41-0.45 — course choice is the dominant comfort skill (by design; throttle mainly matters before maneuvers). Sickness formula (M3) should amplify differences, e.g. rate ∝ roughness².
- 2026-08-17 — Milestone 1 built: Gerstner WaveField (single source of truth for water), CPU-displaced ocean tile following the ship, kinematic ShipMotor (3-point wave seating, wind-angle speed, turn heel, sail auto-trim, rudder visual), one-thumb HelmInput (bottom-45%-of-screen absolute tiller + A/D in editor), yaw-only ChaseCamera. Scene: Assets/_Project/Scenes/Sea.unity.
