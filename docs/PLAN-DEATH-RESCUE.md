# Session prompt: death, rescue, repopulation and village defence

> Paste everything below the line into a fresh Claude Code session in `~/Desktop/SeaSick`.

---

We're building SeaSick's death, rescue and repopulation systems, and then villagers defending their camp in a raid. The design was agreed with me (Kevin) in a brainstorm on 2026-09-27. It's all written below, and my decisions are final unless something in the code makes one impossible. If that happens, stop and ask me; don't redesign it yourself.

## Before you write any code

1. Read your memory index. Open the newest handoff and `death-and-repopulation.md`. Also check `git status` and the current branch: another session may have just finished, so don't assume the state.
2. Read `docs/GDD.md` (§6 and the roster/upkeep parts), `docs/DEV-TOOLS.md` (including the UI-ownership rules), and `docs/DELIVERY-ON-ARRIVAL.md`.
3. Map what exists today. Use cheap agents with an explicit `model` on each (haiku for lookups). Find:
   - how hands are removed now (raids, fleet raiders, hunting);
   - `OutpostHand`, `OutpostLedger` (hunger debt, `recruitProgress`, housing);
   - `CrewAgent` (sickness, anger), `MutinyController`, the rail/puke behaviour;
   - the helm autopilot (floating stick → heading) and throttle;
   - the shipyard slot catalog;
   - the save format and its migration pattern;
   - the raid code in `Scripts/Combat/` (`RaidDirector`, `RaidParty`, `RaidWalker`, `RaidSite`, `RaidBanner`, `WatchtowerGun`) plus the lookout and tower postings;
   - how spears exist today (the store item, the hunter's carried spear, wear).
   - the Ledger UI.
4. Send me a short report: what exists, where each phase below plugs in, any conflicts, and a cost estimate. **Wait for my OK.** Anything over 10 minutes or 50k tokens needs my OK before it runs.

## The standing rules (all still apply)

- **iPhone first.** Portrait, one thumb, big buttons at the bottom. Desktop must still work. Check at 1080x2340 and 1920x1080.
- **I'm the playtest.** Compile-clean plus my play on the phone is the verification. Build to the phone only when I say "build".
- **Small phases.** One commit each, and each phase must be something I can playtest on its own.
- **Deliveries on arrival.** Anything carried is physical. A hand who goes down or dies drops their load where they fell, and nothing vanishes.
- **Buildings never move.** Placed tombstones are saved and restored exactly where they were placed.
- **D2 and offline play.** Nobody dies while I'm away. Raids stay frozen offline, and man overboard only happens while I'm sailing.
- **Tuning lives in a ScriptableObject** in `_Project/Settings` (a `LifeTuning` or similar). Every number below is a starting knob.
- **Save changes** follow the existing version/migration pattern. My current saves must load.
- **The GDD records the decisions:** a design section plus changelog entries, quoting me where I gave feedback.

## The design

### Deaths
- **What can kill someone:** raids, going overboard at sea, shipwreck, and rare hunting accidents. All of these can be seen coming and answered. **Starvation never kills anyone.**
- **Always downed before dead.** A downed hand lies where they fell with a lenient timer (start at ~3 min real time). A nearby free hand runs over and **drags them back to a hut**. That's two units out of action, but if the drag succeeds nobody is lost. When they reach the hut the downed hand recovers there over time. If nobody comes in time, they die.
- **The tombstone.** When someone dies, a small tombstone must be placed **before the player can do anything else**. It's a blocking siting flow near the camp, using the normal thumb-friendly placement. Once placed, it shows a **3-sentence life story**: part how they lived, part how they died. The story is built from real events logged on that person. Examples:
  - "Went over the rail twice and was hauled back twice."
  - "Pulled Anna from the sea."
  - "Went hungry through the long winter at Stonehaven."
  - "Fell defending the palisade in the third raid."
  - "Lost to the grey swell within sight of the ship."

  Use hand-written sentence templates with slots and pick them deterministically. Tombstones come in a few small visual variations.
- **The graveyard list.** A list you can open if you want (in the Ledger or the camp sheet: name, dates, how they died, tap to read the story). Otherwise the tombstones in the world are enough.

### Neglect (temporary playtest failsafe)
- **No desertion.** A hand who's angry from neglect (hunger debt, no bed, etc.) **stands at the fire and pouts for 5 minutes** (real time, a knob), then goes back to work. Show it clearly: a pose and a line on their row.
- **The floor.** A camp never drops below a minimum number of hands (start at 2) because of neglect.

### Repopulation (three layers)
1. **Hut + food growth.** This exists; keep it as the slow safety net.
2. **Recruits at sea.** Castaways and strangers found on islands, plus crew who washed ashore after going overboard (see below). You sail to them and bring them home.
3. **Ferrying hands between my own camps** by ship.

Maybe later: buying hands at trading posts. **Not now:** births and inherited traits. There will be too many hands to read everyone's stats; we may add it later.

### Man overboard (my favourite; this should feel great)
- **How someone goes over:** a **grip meter** per crew member on deck. Hard rolls, heeling, slamming into waves and sharp turns drain it, and calm sailing refills it. When someone at the rail runs out of grip, they go over. **Seasick crew go to the rail**, so bad sailing makes people sick, and sick people at the rail go over. Sea legs lower the risk. Storms and night raise it. Smooth sailing in calm water should mean zero risk.
- **The warning:** before falling, the person grabs the rail and shouts "Hold on!", with a phone vibration. That's about a second to ease the helm and save them.
- **The fall:** a splash, a shout of "MAN OVERBOARD!", a vibration, and a brief slow-down (~0.5 s). The swimmer's head bobs with a small light and **drifts on the real waves**. A countdown ring shows their time left (lenient: ~2–3 min in calm water, shorter in a storm or at night). When the swimmer is off screen, an **arrow at the screen edge** carries the same ring.
- **The ship does NOT stop by itself.** Heading and throttle stay mine. The assist: **tap the swimmer and the heading autopilot steers toward them**. I still control the throttle.
- **The pickup:** come within reach at low speed and a big **"Throw line" button** appears at the bottom of the screen. A crew member leaves their station, goes to the rail and hauls the swimmer in. That's two hands out of action, the same as dragging a downed hand on land. Come in too fast and you sail past them and have to circle back.
- **How it ends:**
  - **Rescued:** soaked and shaken (a sickness spike, off stations for a while) and a life event on them. It can also give a small sea-legs boost.
  - **Timer runs out near land:** they wash up on the nearest island as a **castaway** to fetch (this links to recruits at sea).
  - **Timer runs out in open water:** they die and the tombstone flow runs.
- **The first time is scripted:** calm water and a long timer, so I learn how to rescue before a storm ever tests me.
- **Cargo goes overboard too.** In rough water, deck cargo can slide off. It floats and drifts for a while before sinking, and the same pickup (tap to steer, throw the line) brings it back. A person and a crate going over together forces a choice.

### Shipyard modules (later phase, through the existing slot catalog)
| Module | Effect |
|---|---|
| **Bulwarks / rails** | Less grip loss at the rail |
| **Safety lines** | Almost nobody goes over, but stations work slower |
| **Lifebuoy rack** | Throw from further away, and the thrown buoy extends the swimmer's timer |
| **Scramble net** | Faster haul |
| **Jolly boat** (big ships) | Launches, rows out on its own and picks the swimmer up |
| **Lookout** | Earlier alert and a later timer start |

### Village defence in raids (built after all of the above)
What exists today: the raid party are **thieves**. They steal a unit at a time from the piles, break walls when there's no way in, and run when their ship sinks. They have no health, and villagers ignore them. This adds the first fighting on land.

- **The alarm.** When a raid starts, every hand stops what they're doing and drops any load where they stand (it gets picked up afterwards). **Tower lookouts stay at their posts** and keep shooting. Everyone else arms up or hides.
- **Spears.** Hunters already carry one and go straight to the fight. The spears in the store are real units: the closest hands walk to the store and each takes one, first come first served. If there are 3 in the store, 3 hands arm up and the rest hide. An iron spear beats a stone one.
- **The unarmed hide.** They run to the nearest hut and go in (the body disappears and the hut shows "3 hiding"). Huts are safe: raiders don't attack them for now. With no huts, they run to the far side of the camp from the raiders and crouch.
- **The fight.**
  - Raiders get health. A defender walks to the nearest raider and jabs, reusing the hunting jab pose (starting numbers: ~3 jabs with stone, 2 with iron).
  - Raiders fight back once attacked. A villager who takes enough hits goes **down**, and the death plan's drag-to-hut takes over.
  - Defenders stay near home: inside the walls, or within ~30 m of the fire when there are no walls. With walls, they gather at the gate or the breach. They never chase raiders to the beach.
  - A killed raider drops his loot where he falls.
  - When half the party is down, the rest run for the ship (the existing `Flee`).
  - Spears wear per kill, like hunting, and worn-out spears break.
- **The Fight / Hide-all switch.** The raid banner gets a big button that sends everyone into the huts, spear or not, for when you're outnumbered. Tapping it again sends the armed back out.
- **All clear.** Hiders come out, fighters carry their spears back to the store (hunters keep theirs), dropped loot and loads get collected, downed hands get dragged to huts, and then everyone goes back to work.
- **Raids grow with the camp.** The party size scales with the number of hands (and/or the camp's wealth); a knob, with a cap. Spears and walls are how you keep up.
- **What you see.** The raid banner reads "4 defending · 3 hiding · 1 spear left". Each row says why: *"defending, stone spear"*, *"hiding, no spear"*, *"down"*. The camp overview can set a goal like "Forge spears before the next raid".
- **Unchanged:** raids stay frozen while you're away; tower guns and lookout arrows work as today; the fight at sea is untouched.

## Build order (one commit and one phone test per phase; stop after each for my verdict)

1. **Life log + death pipeline.** A saved per-person event log, the Downed → Dead states, the story generator, and save migration. No new visuals yet beyond a basic downed pose.
2. **Downed + drag to hut** on land (raids, hunting accidents), with the lenient timer and recovery in the hut.
3. **Tombstone.** The forced placement flow, the story card, the visual variations, and the graveyard list.
4. **Pout + floor.** Replace neglect outcomes with pouting at the fire, and enforce the floor.
5. **Man overboard.** Grip, the warning, the fall, the swimmer, the ring and edge arrow, tap to steer, throw line, the outcomes, and the scripted first time.
6. **Cargo overboard.**
7. **Recruits at sea + ferrying** (castaways, washed-ashore crew, moving hands between camps).
8. **Shipyard modules.**
9. **Raiders get health and fight back.** Raider health, defender jabs, raiders hitting back, downed villagers going into the drag-to-hut flow, loot dropped where a raider falls, and the flee-at-half morale rule.
10. **The alarm, arming and hiding.** Dropping loads, taking spears from the store, hunters joining with their own, tower lookouts staying put, the unarmed hiding in huts (or crouching), and the defend-near-home radius and gathering at the gate or breach.
11. **The Fight / Hide-all switch, the banner counts and the row reasons.**
12. **All clear and raid scaling.** Returning spears, the post-raid clean-up, spear wear, and the party size growing with the camp.

## How to work

- Orchestrate in parallel. Split by files and editor ownership, give code agents their own worktrees, and bring patches back. Set `model` on every agent (haiku for lookup/staging, sonnet for spec'd implementation and docs, opus only for design questions or unknown bugs). Cap every agent and check on it about every 10 minutes.
- UI work follows the ownership rules in `docs/DEV-TOOLS.md`. If a piece belongs to Astra, write a "Send to Astra:" block for me to relay instead of building it.
- Compile-clean (`unity cmd console_status --json`) before every commit. Small commits.
- At the end of the session, write a handoff memory (what's built, what's unplayed, open knobs and traps) and update the GDD.
