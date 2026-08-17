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
