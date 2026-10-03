# PLAN — DREDGE-style sailing movement + camera (2026-10-03)

Status: **PLAN ONLY — nothing in the game changed.** Kevin's decisions in (§4, 2026-10-03); waiting on his go for phase 0/1.
Kevin: "I don't like how the movement and camera work when sailing. I want you to do exactly
how DREDGE does their movement input and camera input … and plan how to remove the old."

---

## 1. How DREDGE does it

Every line is marked **[C]** confirmed (a source is listed in §7) or **[I]** inferred.
Black Salt never published numbers: speeds, turn rates, camera lag and pitch limits are unknown.
We tune those by feel, side by side with DREDGE on Kevin's phone (§5, phase 5).

### 1.1 Movement — one analog stick, boat-relative, nothing latches
| Aspect | DREDGE | Source |
|---|---|---|
| Controls | Keyboard W/A/S/D; gamepad **left stick**; phone: a **virtual stick that appears where you touch the left half** | [C] wiki, Black Salt interview |
| Stick meaning | Up/down = speed (forward / reverse), left/right = turn the hull. Speed **scales with how far you push** | [C] Apple session ("tuning movement speed based on how much players pushed the joystick"); boat-relative = [I] (see §4 Q1) |
| Release | No throttle memory: let go and the engine stops, she glides to a halt | [I] analog speed + no telegraph anywhere in the game |
| Turn at rest | Yes — A/D pivots the boat when stopped | [C] guide quote |
| Reverse | Exists (S / stick down), slow | [C] exists; speed unknown |
| Boost | **Haste** engine ability: a separate button, FOV pull + screen shake, overheats | [C] patch 1.2.0 ("Haste VFX" toggle) |
| Waves | Waves do **not** push the boat. "Where the player is should come from their choices." Speed is damped in big waves | [C] Game Developer interview |
| Tuning | Speed and turn rate are two separate multipliers (modders expose both) | [C] Nexus "Tweaks" mod |
| Assists | No autopilot, heading hold, tap-to-stop or telegraph | [I] none documented |

### 1.2 Camera — orbit stick, follows the hull by default, recenter button
| Aspect | DREDGE | Source |
|---|---|---|
| Rig | Third-person orbit around the boat at a fixed distance, no zoom found | [C] orbit; distance/zoom = [I] |
| Input | Gamepad **right stick**; phone: **drag on the right half**; mouse (or hold-to-look mode) | [C] |
| Rate, not position | Mouse movement is turned into "stick tilt" with a **max tilt**, so you can't flick the camera round instantly. That is deliberate tension ("you can't check behind you") | [C] Steam thread with dev reply |
| Follow | By default the camera **yaws with the boat's turning**. Settings → *Camera Follow Mode* detaches it | [C] patch 1.2.0 |
| Your offset | Holds until you **recenter** (R / middle mouse / gamepad Y). The recenter button only exists because the offset does not decay on its own | [C] recenter control exists; "no auto-decay" = [I] |
| One hand | Playable one-handed, because the camera follows the boat | [C] Can I Play That review |
| Settings | Sensitivity X/Y per device, invert X/Y, follow mode, hold-to-look, motion smoothing, Haste VFX off | [C] |

### 1.3 The phone version (Black Salt's own port, Feb 2025, Apple Design Award)
- **Two floating thumbsticks, landscape**: left half = boat, right half = camera, both usable at once. [C]
- They worried two thumbs would cover the boat; players found it natural. [C]
- What they tuned: simultaneous inputs, thumb zones, **ignoring tiny finger lifts** (a short lift
  doesn't drop the stick), and speed proportional to push. [C]
- Big contextual action buttons on the right (Dock / Fish / Dredge / Use). [C]
- **Portrait is not supported** [I — every screenshot and note says landscape]. *This is the main
  conflict with our target (portrait, one thumb); see §4 Q2.*

### 1.4 Why it feels good (what we're actually copying)
1. **The input is the boat.** Push = go, how far = how fast, let go = stop. No hidden state
   (latched throttle, heading hold, autopilot) means no surprises.
2. **The camera never fights you.** It follows the hull on its own, so you can ignore it. When you
   do look around, it's slow and it stays where you left it until you recenter.
3. **Steering ignores the camera**, so looking around never changes what the stick does.
4. The physics serves the input (waves don't shove you, speed is capped and readable).

---

## 2. What we have now (and what it fights)
Floating stick in the bottom half (`TouchHelm`). X sets a direct rudder that springs back.
Y sets a **latched** throttle, so speed stays after release. Past the rim is burn, a tap stops,
and on release a **heading hold** servo keeps her course (`HelmInput`, `HelmTuning`,
`HandlingTuning.HeadingHold`). The camera (`ChaseCamera`, V2 springs) has **no orbit input**: it
leans, rises and swings on its own from the rudder, throttle, lock and kraken. The only sea camera
input is pinch / wheel zoom, which decays back. The order strip (`SeaHud`) shows the latched
throttle.

How that differs from DREDGE: the latched throttle, the hold, tap-stop and the burn rim are hidden
state. The camera can't be looked around. Lock and kraken framing are capped "so the stick never
reads mirrored", which is a constraint that vanishes once steering is boat-relative.

---

## 3. Target design for SeaSick (portrait-first DREDGE)

### 3.1 Phone (portrait, 1080×2340)
```
┌──────────────────────┐
│ top bar              │
│                      │
│   CAMERA ZONE        │  one-finger drag = orbit (rate-capped), double-tap = recenter
│   (upper ~45%)       │
│                      │
├ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┤
│   BOAT ZONE          │  floating stick appears under the thumb:
│   (lower ~55%)       │  up/down = speed ∝ push, left/right = turn, release = engine off
│         ◯            │
│ [slim speed strip] [⚡]│  ⚡ = Haste/burn button (bottom-right, thumb-reachable) — §4 Q4
└──────────────────────┘
```
- **One thumb is enough** (the camera follows the hull). **Two thumbs work** because both zones
  track their own finger (like DREDGE). That keeps our one-thumb rule and still copies DREDGE.
- Lift grace: a lift shorter than about 0.1 s doesn't drop the stick (copies Black Salt's
  "tiny finger lifts" fix).
- Taps still go to their current owners first: RescueHud marker → CombatLock (tap a ship) →
  UI. A tap in the boat zone no longer means "stop", because releasing already stops her.

### 3.2 Desktop (1920×1080)
| Key | Action |
|---|---|
| W/S, ↑/↓ | Speed forward / reverse (held = full; released = engine off) |
| A/D, ←/→ | Turn (pivots at rest) |
| Mouse drag (LMB, outside UI) | Orbit camera (rate-capped like DREDGE) |
| C / middle mouse | Recenter camera (R stays rowing; §4 Q5) |
| Shift | Haste / burn |
| Space, Q/E, G | Unchanged (anchor/lock, broadsides, settings) |

### 3.3 Camera behaviour
- `yaw = hullHeading + userYawOffset` (follow mode, default). Follow lag is a single spring.
  The V2 lean/rise/juice stays layered on top.
- `userYawOffset` / `userPitchOffset` change only from orbit input, at most `orbitMaxDegPerSec`.
  They persist until recenter (DREDGE). Pitch is clamped (for example 10–45°).
- Recenter: the offset eases to 0 over about 0.4 s.
- Settings: Follow mode on/off (detached = the world yaw stays fixed), orbit sensitivity, invert X/Y.
- Lock, kraken SeaFocus and the island overview keep their priority over the sea pose. Their
  "never mirrored" swing caps can relax, because steering no longer depends on the camera.
- Pinch zoom at sea: **removed** (DREDGE has a fixed distance; a two-finger gesture would collide
  with two-thumb play). The island view keeps its pinch. §4 Q3.

### 3.4 Physics (stays ours, gets DREDGE's priorities)
- `ShipMotor` / `PaddleDrive` stay. Only their inputs change: `Rudder` = stick X (with a curve),
  `ThrottleOrder` = stick Y each frame (no latch, negative = astern).
- Heading hold becomes a **pure physics stabiliser** owned by the motor. It runs while turn input
  is zero (for example `motor.SteerInputActive`), so the `HelmInput.HoldAllowed` coupling goes.
  It's kept only if waves still nudge the heading in play. Otherwise it's deleted (DREDGE has none).
- Release → coast-down uses the existing `HandlingTuning` coast knobs. Retune; no new physics.

---

## 4. Decisions (Kevin, 2026-10-03)
1. Stick frame: **boat-relative** — up/down = speed, left/right = turn.
2. Layout: **portrait two-zone** (§3.1) — lower = boat stick, upper = camera drag; one thumb suffices.
3. Pinch zoom at sea: **removed** (island keeps its pinch).
4. Burn / overdrive: **Haste-style boost button** (bottom-right; Shift on desktop).
5. Rowing on R: **kept** — recenter goes on C / middle mouse instead.

---

## 5. Build + removal plan (small steps, each on the phone before the next)

**Phase 0 — prerequisites (no code)**
- Kevin answers §4 (and optionally runs the DREDGE check in §6).
- The **kraken lane's uncommitted `ChaseCamera.cs` + `FeelLab.cs` edits are committed first**. No
  controls work starts on top of a dirty `ChaseCamera`.
- Baseline: record the current helm/camera FeelLab JSON, so the old feel is recoverable from git.

**Phase 1 — movement swap** (one opus agent owns the editor; about 40–60k tokens, ~30 min)
- NEW `S/Ship/SeaStick.cs` (plain class): floating stick, zone rect, own finger id, dead zone,
  lift grace, analog `Vector2`, IMGUI/UITK ring visual. It **replaces `TouchHelm.cs`** (deleted).
- REWRITE `S/Ship/HelmInput.cs` **keeping the class name**. ~35 probes call
  `FindAnyObjectByType<HelmInput>()` and `.enabled=false`, so renaming would mean touching all of them.
  - Delete: the autopilot (Kp/Kd), the latch, tap-stop, the burn rim, `ReadStick`/`ReadStickDirect`,
    `SailTo` and its seam (no callers), `RudderOrder`/`ThrottleOrder01`/`DirectMode`/`BurnRate01`
    (no users).
  - Keep the API: `SteerToward` (RescueHud, SeaHud), `AllStop` (AnchorController), `StickInUse`,
    `Breakers`, `OrderWord`, and the stand-down gates (`IslandCam.Engaged`,
    `ShipyardSession.WorldInputBlocked`, `UIBlocker`, menus/modal for the visual).
  - `HoldAllowed` → moved to the motor (§3.4); update `ShipMotor.cs:990` and `PaddleDrive.cs:622`.
- NEW `S/Ship/SailControlTuning.cs` (static, FeelLab-visible): stick radius, dead zone, turn curve,
  throttle curve, reverse cap, lift grace.
- SHRINK `S/Ship/HelmTuning.cs` to the hold knobs, or delete it with the hold.
- `SeaHud` order strip: show the live speed (no latched order word or burn stretch). The first-use
  hint changes to "drag to sail" using the new stick.
- FeelLab: drop the `HelmTuning.*` input rows, add the `SailControlTuning` rows, bump `RebasedKeys`
  so stale saved helm values die.
- Gate: compile clean, a ~2 min editor portrait smoke test, then a phone build. **Kevin plays.**

**Phase 2 — camera orbit** (one opus agent; ~40k tokens)
- `ChaseCamera`: add `userYawOffset`/`userPitchOffset`, follow / detached, recenter. Replace
  `PlayerZoom` (:792) with `SeaCameraInput` (NEW plain class: upper-zone drag + double-tap, mouse
  drag, R / middle mouse). Keep a `ZoomScale` getter at 1, because `ShipyardUiProbe` asserts it.
- Retune `UpdateSeeing`'s rudder lean (:881), since the rudder dynamics changed in phase 1.
- Relax `camLockSwingDeg` / `lockMaxSwingDegPortrait` / `SeaFocusSwingDeg` caps and their
  "stick never mirrors" comments.
- Settings: Follow mode, orbit sensitivity, invert X/Y (PlayerPrefs, not the save).
- Gate: same as phase 1 + **Kevin plays**.

**Phase 3 — desktop + Haste**: the WASD analog feel, mouse orbit, R recenter, Shift/⚡ burn button,
C / middle-mouse recenter (R keeps rowing). Check `HudOverlapProbe` in both shapes (`RunProbe.ViewPhone()` / `ViewDesk()`).

**Phase 4 — dead code + docs sweep** (one sonnet agent, no editor; ~25k tokens)
- `HudLayout`: delete the dead slots `Helm`, `HelmActions`, `Lock`, `Broadside`, and `Wheel`
  if it's unused. Fix `ColumnOf` / `SlotCount`, then recheck the `SettingsPanel` drawer sizing.
- Stale comments: `RescueHud.cs:25-27,270` (hard-coded "TouchHelm.cs:220"), `SeaHud.cs:43`,
  `ChaseCamera.cs:229`, `DriveProbe.cs:29`, `WakeProbe.cs:36`.
- `ShipMotor.EffectiveRudder()` / `PaddleDrive.Helm()` `AutopilotTarget` rule: keep, since
  probes and `SteerToward` use it.
- Docs: rewrite GDD §4 controls (:25-39, incl. the stale "No manual camera" :30); update §6 kraken
  :181 and the decisions log (one line, superseding :826-833 "widen, never rotate"). DEV-TOOLS
  :1296-1303 (obsolete telegraph note) and :1529-1546 (SailTo seam) get deleted.
  `PLAN-island-outposts.md:463` and `SHIPYARD-API.md:491,497,625` get touched up.
- `KrakenDodgeProbe` + the swat tell: re-run the dodge numbers with the new steering (the tell
  forms "where the ship is heading").

**Phase 5 — spec checklist (hard rule)**: a line-by-line check against §3 on Kevin's real save,
phone portrait + desktop. Then a feel pass with DREDGE open on the same phone (§6), tuned only
through FeelLab.

### 5.1 Removal map
| File | Fate |
|---|---|
| `S/Ship/TouchHelm.cs` | **Delete** (replaced by `SeaStick`) |
| `S/Ship/HelmInput.cs` | **Gut + rewrite**, same class name and public seams |
| `S/Ship/HelmTuning.cs` | **Delete** the input knobs; the hold knobs move with the hold, or go too |
| `S/Ship/HandlingTuning.cs` `HeadingHold` | Keep or delete per phase-1 play; drop its `HelmTuning` reads |
| `S/Ship/ShipMotor.cs`, `S/Steamer/PaddleDrive.cs` | Keep; replace the `HoldAllowed` lookups (:990, :622) |
| `S/CameraRig/ChaseCamera.cs` | Keep; replace `PlayerZoom`, add orbit/follow/recenter, relax caps |
| `S/Ship/JuiceTuning.cs` | Keep; prune `cam*` knobs that no longer apply, add orbit knobs |
| `S/UI/Sheets/SeaHud.cs` + `SeaHud.uss` | Keep; redo the order strip + hint; keep `HelmShowing`/`HelmRect` (edge markers, CombatHud read them) |
| `S/UI/HudLayout.cs` | Delete the dead slots |
| `S/UI/Sheets/GestureHints.cs` `Stick` | Reuse for the new hint |
| `S/Dev/FeelLab.cs` | Swap rows, bump rebase |
| `IslandCam`, `IslandInput`, `TwoFinger`, `GroundPick`, `CombatLock`, `RescueHud`, `AnchorController`, all probes | **Untouched** (beyond comments) |
| Save files | Untouched (they store no helm/camera data) |

### 5.2 Traps to respect
- Island view and sea share `ChaseCamera.LateUpdate`. The orbit offset must be zeroed or blended
  out under `Overview`, or the island camera inherits a twisted yaw.
- New input must stand down under `IslandCam.Engaged`, `ShipyardSession.WorldInputBlocked`,
  `UIBlocker.Blocked`, `GameMenus.Current` and the shipyard modal, or touches leak into other modes.
- `HelmInput` writes the motor every `Update`. `AllStop()` must reset input state, not the motor.
- The editor "Continue" loads slot m1, not the newest save. Copy the wanted autosave over m1
  before testing on Kevin's save.

---

## 6. Two-minute DREDGE check (if Kevin owns it on iPhone)
1. Drag the camera (right half) 90° to the side, then push the boat stick straight **up**.
   The boat goes where its bow points = boat-relative; it turns toward where the camera looks = camera-relative.
2. Push half way, let go: does she glide to a stop, or keep going?
3. Look sideways, then sail and turn for 10 s: does the camera drift back behind her by itself?
4. Can you pinch-zoom? Does it play in portrait at all?
5. Is there a boost button on screen?

## 7. Sources
- Mobile wiki: https://dredge.wiki.gg/wiki/DREDGE_(mobile)
- Black Salt mobile interview: https://www.blacksaltgames.com/dredge-mobile-interview/
- Mobile FAQ: https://www.blacksaltgames.com/dredge-mobile-announcement-faq/
- Apple "Meet with Apple" session 247: https://developer.apple.com/videos/play/meet-with-apple/247/
- Patch 1.2.0 (Camera Follow Mode, Haste VFX): https://blacksaltgames.com/dredge-update-v1-2-0-wildlife-photo-mode
- Game Developer interview (waves, speed cap): https://www.gamedeveloper.com/design/trawling-in-the-deep-how-black-salt-games-made-spooky-fishing-rpg-i-dredge-i-
- Controls: https://www.gamepressure.com/dredge-pc/controls/z910aa6 · accessibility: https://caniplaythat.com/2023/05/10/dredge-accessibility-review/
- Mouse-as-stick thread: https://steamcommunity.com/app/1562430/discussions/0/6620894968768965936
- PCGamingWiki (sensitivity X/Y, invert X/Y): https://www.pcgamingwiki.com/wiki/Dredge
- Unfetchable (worth a manual read): https://www.reddit.com/r/dredge/comments/1l4d5df/mobile_version_controls/
