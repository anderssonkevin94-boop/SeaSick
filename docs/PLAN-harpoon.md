# PLAN — The bow harpoon

**Status 2026-10-04:** APPROVED by Kevin (decisions in §8). Phase 0 + 1 started. No other phase starts until the previous one is closed (§9). Written by the orchestrator session from a read-only survey of the codebase (facts cited inline).

Kevin: *"a harpoon gun on the front of the ship that is used to collect resources in the ocean, attach to enemies or to pull you in towards land … take it to the next level."*

---

## 1. The idea in one line

**The harpoon is the ship's hand.** It is a line from your bow to something in the world, and the line is physical. While it is attached, **steering stops being just steering and becomes handling a load.** That puts the harpoon inside pillar #1 (sailing feel) instead of beside it, and it feeds pillar #2: a clean, smooth reel is calm, while a yank, a jerk or a snapped line shows up on the smoothness meter (`Ship/SmoothnessMeter.cs:18` already counts lateral acceleration).

Nothing like it exists yet. A grep for harpoon, grapple, whale, tow or boarding hook finds nothing, so it is greenfield.

## 2. The core loop (same for every target)

1. **Spot.** A harpoonable thing inside the bow arc (**90° total, ±45° off the bow**, up to 35 m at L1) gets a small **hook marker in the world** (Kevin: "a little marker or something to show"). The nearest one is auto-picked and its marker is highlighted.
2. **Fire.** Tap the **🪝 Harpoon** button. The barb flies on a visible arc with rope trailing.
3. **Bite or miss.** A hit bites. A miss splashes and the line reels back empty (about 2 s, no cost).
4. **The line is live.** The winch reels on its own. The rope reads its tension at a glance: **slack sag → taut → strained (it creaks, frays and glows warm)**. You steer to keep it out of the red: ease off and point at the load.
5. **Resolve.** It is reeled home (salvage aboard, ship at the beach, raider alongside), **cut** (tap again), or **snapped** (too much strain for too long). After a snap or a cut, the gun **reloads for 5 s** (Kevin), then it's ready again. Ammo is unlimited for now.

One mechanic, many targets. Each new target is mostly a "what happens when it arrives" rule.

## 3. What it hooks

### A. Salvage at sea (phase 1)
- **Today:** flotsam, salvage crates, message bottles, kraken loot and castaways are collected by sailing over them or creeping alongside: `FlotsamCrate` (an `IOverboardTarget`), `SalvageSpawner` (6 m sail-over), `CastawaySpawner` (12 m and under 3 m/s).
- **With the harpoon:** hook them from 35 m and reel them in. It plugs into the existing `IOverboardTarget` path (`BeingHauled`, `HaulAnchor`, `OnHauled`), so every reward stays the same. It just turns a fiddly approach into a satisfying shot.
- **Weight matters.** A bottle comes in instantly. A crate drags. A heavy salvage chest (new, phase 5) pulls hard enough that you must slow down or it snaps.
- **The hold is finite (Kevin, 2026-10-03).** Harpoon salvage must clamp to the hold. Today `VoyageManager.ReturnCargo` never clamps (`:295`) while `AddLoot` does (`:247`). A full hold means the line reels the item to the rail and the card says "hold full", so you choose whether to jettison something.

### B. Pull yourself to land (phase 2)
- Hook a **beach anchor** (a rock or tree on a beach bearing, within 35 m of the bow). The winch pulls the ship in and **stops on its own at the 12 m landing shelf** (`AnchorController.landingDepth`) under the landing speed. The Land card then comes up as normal.
- **It refuses cliffs.** The barb only bites on bearings where `Island.HasBeachToward` is true. A cliff gives "the hook won't bite rock face" instead of a silent fail.
- **This fixes the landing frustration directly:** no more crawling about the shelf looking for the magic spot.
- **Kedging:** hook a rock and stop the engine, and the line holds you in place in a squall, like a quick anchor.
- **Slingshot turn (next level):** hook a rock as you pass and steer around it. The line pivots the bow round the rock in a tight, fast arc, a real sailing trick for weaving through reefs and headlands. Release by cutting.

### C. Hook an enemy (phase 3)
- **Raiders are kinematic.** They move by `transform.position` and have no Rigidbody (`EnemyShip.cs:963`), so the tether is code-driven:
  - A hooked raider is **dragged**: it loses speed, can't flee to `Return` and can't pull away.
  - Reeling draws the two hulls together.
- **Counterplay:** the raider's crew try to **cut the line**. A visible "axe" tell gives a few seconds; landing a cannon hit on them resets it.
- **Steer them (Kevin):** the line is also a tool on its own: hook and hold to **slow** a raider, or pull across its bow to **swing its heading** (spoil its broadside, turn it away from your weak side, drag it off its chase).
- **Payoff, BOARDING (new):** reeled alongside, the action card offers **"Board her"**. Your crew cross and fight, and you win their **cargo**. Today raiders drop nothing on death (`EnemyShip.cs:1189-1225`), so this is the first reason to win a fight beyond surviving it. Captured cargo goes into your (finite) hold.
- Later: tow a crippled raider home as **shipyard salvage** (parts, iron).

### D. The kraken (phase 5, APPROVED: "you can shoot the kraken, if you want to")
- The current design is "drive it off": a swat tell, guns, escape at 120 m (`KrakenTuning`).
- **The harpoon option is the sleigh ride.** Hook the head and it **dives and drags the whole ship**. The line screams, and you steer to keep it out of the red while it tires.
- **Hold on long enough** and it surfaces spent, for a big ink and meat haul. **Snap or cut**, and you've lost the barb and it's angry.
- It's a set piece, only from the L3 heavy harpoon, and needs Kevin's yes because it changes the kraken fight.

### E. Food at sea (phase 5)
- `LargeFish` (5.9 m, scenery only, no collider: `SeaLife/LargeFish.cs:6-8`) becomes **game**: hook it and reel it for Fish and Meat.
- This finally gives voyages a food source that isn't the hold or a shoal crawl. Sharks and big rays come later.

## 4. Phone controls (fits the DREDGE scheme)

Today's screen (`SailControlTuning.zoneTopFrac` 0.55, `SeaHud`, `CombatHud`):
- the bottom 55% is the boat stick
- the top 45% is look and drag
- **⚡** boost sits at the bottom right
- the combat row appears above the strip during a fight

**The harpoon:**
- **One button, 🪝, sitting just above ⚡ on the bottom-right edge.** That spot is free today, it's a thumb's reach from the stick, and it registers in `UIBlocker` like `BoostRect` so it never starts the stick.
  - It shows only when there is a valid target in the arc, labelled with it: "🪝 crate · 22 m".
  - While the line is out it becomes **✂ Cut**.
- **Choosing a target:** the nearest in the arc is auto-picked and its marker is highlighted. **Markers are indicators only (2026-10-04, after Kevin's phone test: a marker tap over the bow opened the Ship sheet):** they take no taps; the fixed 🪝 button is the only way to fire. A tap on a ship still locks it, as today (`CombatLock`).
- **No aiming.** The barb auto-leads the target. Skill is in **bringing the bow round** to put it in the arc and **managing the line** afterwards, which is sailing, not thumb-aiming.
- **One-thumb conflict:** "let go = engine stops". Tapping 🪝 means lifting the thumb off the stick, so the engine stops for a moment. That's fine, even good: firing happens in a calm beat, and the winch then reels while you steer again.
- **Desktop:** **F** fires and cuts (F is free; Space, Q/E, R, C, G and H are taken).
- The first time, a hint pill says "Hook it · tap 🪝". Text always wraps and is never cut off.

## 5. Ship, crew and economy

- **Mount:** every ship gets a basic **bow harpoon from the start**, so it's part of sailing and not a shop item.
  - The bow grid today has only two gun-port cells, P0 and S0 (`SlotSchema`, `standards.json`), and no bow-centre slot.
  - So the L1 mount is a **fixed bow fitting** outside the grid. Upgrades become a new `bow` placement in the shipyard: **L2 steam winch** (faster reel, more pull, run off the paddle engine), **L3 heavy harpoon** (kraken, whale-class game, raider boarding at range).
- **Ammo: UNLIMITED for now (Kevin 2026-10-04, revisit later).** The barb-as-iron-ammo idea below is PARKED; don't build it.
- *(Parked)* **Barbs are ammo, and iron is the cost.**
  - A new Item **Harpoon barb** is made at the Blacksmith's Forge from **1 iron + 1 boards, fire II** (the same rung as the iron spear; `RecipeGraph.Validate` will check it).
  - Reeled-in barbs come back. **Snapped or cut lines lose the barb.**
  - The ship starts with a small stock, and a basic stone-tipped barb is free and infinite but weak, so you're never locked out before iron.
  - This gives iron its first sea-side use and makes ore islands matter more.
- **Kraken ink**, unused today, is a candidate for a **line treatment**: "inked line", stronger before it snaps. Phase 5.
- **Harpooner (pillar #3, crew are people):**
  - A named hand walks to the bow and works the gun. They fit into the shipboard priority list next to the guns: rescue → bail → engaged guns / **harpoon** → sails/oars.
  - A seasick harpooner's shots wander.
  - **With nobody free, the captain fires it from the helm, slower.** It never soft-locks.

## 6. Build phases (each one goes to the phone on its own)

| Phase | What Kevin gets | Main files / systems | Owner (session) |
|---|---|---|---|
| **0 · Look** | Bow-mount and barb art, rope look (in-engine screenshots, phone size); 🪝 button mockup in the real HUD | Blender headless (mount, barb), `LineRenderer` rope with sag, HUD mock | Villager (did the storage art) |
| **1 · Hook & reel** | Fire, bite or miss, live line with tension colours, auto-reel, cut, snap. Salvage targets: flotsam, crates, bottles, kraken loot, castaways. Hold clamp | New `Ship/Harpoon/` (HarpoonGun, HarpoonLine, IHarpoonable); `IOverboardTarget` bridge; `SeaHud` button; `VoyageManager` clamp | **Sailing** (owns the controls, HUD and motor) |
| **2 · To the land** | Beach hook pulls you to the shelf and stops, cliff refusal, kedge hold, slingshot turn | Pull force on the hull (`rb.AddForce`; the servo-driven non-steamer hull needs a target override, `ShipMotor.cs:1090-1111`), `AnchorController`, `HasBeachToward` | Sailing |
| **3 · Raiders** | Hook a raider (drag, can't flee), line-cut counterplay, reel alongside, **Board her** for their cargo | `EnemyShip` tether state, boarding fight (crew), loot into the hold | Sailing (tether) + Villager (boarding crew fight) |
| **4 · Crew & upgrades** | Harpooner crew role in the priority list (if not already in phase 1), shipyard `bow` placement + L2 winch / L3 heavy. (Barb ammo PARKED) | `CrewAgent` / `CrewRoster`, `SlotSchema`, catalog | Villager |
| **5 · Next level** | Big fish as game, heavy wreck chests, kraken sleigh ride (if approved), inked line, raider tow home | `LargeFish`, new salvage, `Kraken*` | Split once 1–4 are played |

**Running it in parallel:** phase 0 (art, villager) runs alongside phase 1 (code, sailing). They don't share files, and phase 1 uses a placeholder barb until the art lands. Phase 4's economy (villager) can start as soon as phase 1's `IHarpoonable` and barb-count API exists. Unity is shared: one play session at a time, handed over as usual.

## 7. Risks and traps

- **The motor fights the pull.** Non-steamer hulls are servo-driven toward a target speed (`ShipMotor.cs:1090-1111`), so a raw force gets cancelled. The pull must feed the servo (target velocity or ceiling) and not just add force. Steamer hulls (`PaddleDrive`) take real forces cleanly.
- **Seasickness from the line.** Snaps and yanks spike lateral acceleration. That's intended as a cost, but the reel itself must be eased (spring-damper, no step changes) or every pickup makes the crew sick.
- **Landing speed and the impact bill.** The pull must stop under `approachSpeedLimit` (6.5 m/s) and well under the 2.5 m/s hull-damage bill at the shore (`HullIntegrity.HoldOffTheLand`).
- **Kinematic raiders.** Everything about towing a raider is our code, not physics. Keep it simple: drag, then close the gap.
- **Taps:** 🪝 must not steal the ship-lock tap or the camera double-tap. Markers need ~60 pt hit radii like `CombatLock`.
- **Hold overflow** from salvage, raider cargo and big game. Clamp everywhere; the ship is not infinite.
- **Scope creep.** Phases 1 and 2 alone are a complete feature. Everything after waits for Kevin's play verdict.

## 8. Kevin's decisions (2026-10-04)

1. **Aiming:** auto-target the nearest in the bow arc (marker taps removed 2026-10-04: markers are indicators, firing only from the fixed button), and the barb leads itself. **Show a marker** on targets. The **bow arc is 90° total (±45°)** for now.
2. **Ammo: unlimited** for now. Revisit later; barb items are parked.
3. **Lines snap** under held strain. After a snap the gun **reloads for 5 s**.
4. **Every ship has a basic bow harpoon from the start.** Upgrades come later in the shipyard.
5. **Raiders:** hook them to **slow them or change their heading**, and reel them alongside to **board for cargo**.
6. **Kraken: yes**, you can harpoon it if you want to (phase 5 set piece).
7. **A crew member mans the harpoon.** The captain fires it, slower, if nobody is free. Build the crew role in phase 1 (simple version), so the gun is never crewless by design.

## 9. Closing rule: no loose ends (Kevin, 2026-10-04)

Kevin: *"I don't want to be spread too thin … make sure that we close features we're implementing. I don't want loose ends that will trip us up down the line."*

- **A phase is DONE only when ALL of these hold:**
  - It's landed.
  - It's on the phone.
  - Kevin has played its checklist and given a verdict.
  - Every bug he found is fixed or explicitly parked by him.
  - The GDD and DEV-TOOLS are updated.
  - No TODOs, dead code or debug leftovers are in the diff.
  - The probes or self-tests it added pass.
  - `docs/TRACKER.md` marks it closed.
- **No next phase starts until the current one is DONE.** The one exception: art for the next phase (phase 0 work) may run in parallel, because it is separate files.
- **Every session ends each report with a "loose ends" line:** anything unfinished, untested or assumed. If it's empty, it says "none". The orchestrator copies these into `docs/TRACKER.md`.
