# Shipyard UX audit — 2026-09-26

Read-only audit against Kevin's standard: *"a clearer, more self-explanatory
structure — the player should never feel like they're missing out on features
or missing something because of poor UI and player feedback."* Phone portrait,
one thumb, first; desktop second.

**Scope read:** `Assets/_Project/Scripts/UI/ModularYard/{ShipyardScreen,
ShipyardSectionSheet,ShipyardDraft,ShipyardLiveBridge}.cs`,
`Assets/_Project/Resources/UI/ModularShipyard.uss`,
`Assets/_Project/Scripts/UI/Sheets/ShipSheet.cs`,
`Assets/_Project/Scripts/Ship/Modular/Shipyard.cs` (ShipyardCodes + every
refusal message), `Assets/_Project/Scripts/Ship/Modular/Runtime/
ShipyardService.cs` (`CanRefitNow`, `ApplyRefit`), `docs/SHIPYARD-SECTIONS-UI.md`,
`docs/GDD.md` sheet-HUD rules, `Logs/yard-shots/v2/*.png` (phone 1080x2340 and
desktop 1920x1080, both platforms, overview / structure / guns / interior /
raised-wide states).

**Two changes in flight, noted where relevant, not audited as bugs:**
Interior becomes a 2D cutaway (tap compartments cargo↔bunks, guns on the deck
line); the shipyard will require a player-built dry dock at the home berth
(ship model shown in the dock). See "For the two changes in flight" at the
bottom for what they must get right given what's found below.

---

## Top-level verdict

The overview and section-sheet *shape* (tiles → tap → Structure/Guns/Interior
pages) matches the design doc and is legible. The two things that break
Kevin's standard hardest:

1. **`ShipyardScreen`/`ShipyardSectionSheet` don't use the rest of the game's
   sheet-paging system** (`SheetHost.BandHeight`/`RowsThatFit`, the horizontal
   pager every other sheet in the game uses). The section sheet's own doc
   comment says *"the prototype's three pages are short enough not to need
   [paging]"* — the screenshots prove that's already false: the Guns page and
   the overview's summary/warnings both run off the bottom of the screen,
   on **both** phone and desktop, with no scroll and no page-split. Content
   that exists is simply invisible.
2. **The refit's own success has no visible feedback.** `Confirm()` sets
   `draft.Message = "Refit confirmed."` and the very next line closes the
   screen (`ShipyardScreen.cs:94`), so that message is never seen by anyone.
   A player who just changed their ship gets no acknowledgement at all.

Everything else below is smaller, but several items repeat a pattern the team
already diagnosed and fixed once (tooltip-only reasons are invisible on a
phone with no hover — the 2026-09-25 fix to Raise/Lower all) and then
re-introduced elsewhere.

---

## Screen: Discovery (from the ship sheet)

| Sev | Where | What the player experiences | Fix |
|---|---|---|---|
| Confusing | `ShipSheet.cs:135-139` `BuildActions` | The **"Shipyard"** button shows whenever `ShipyardService.Player != null` — it does **not** check `CurrentDock`/`AtHomeDock`. Tapping it while anchored off a random island, or alongside a non-home pier, opens the full-screen 3D modal, only to bury "Refits are done at your home berth (X)" inside the scrolling/overflowing warnings text (see next section). A one-thumb player pays a full-screen transition to learn nothing can be done here. | Greying the button outright is too strong (a player might want to preview), but at minimum surface `CanRefitNow`'s reason as a **visible line under the button**, not just inside the modal — same fix already applied to Raise/Lower all (`deckReason` label) should apply here too. |
| Polish | `ShipSheet.cs` | Nothing on the ship sheet hints a shipyard/refit is even a feature until the button appears (i.e. until `ShipyardService.Player` exists). No "why isn't there a Shipyard button" messaging for a ship that hasn't got the component yet. | Low priority — likely fine once the dry-dock requirement lands and replaces this gate anyway. |

## Screen: Overview (tiles, beam, deck, summary, confirm)

| Sev | Where | What the player experiences | Fix |
|---|---|---|---|
| **Blocker** | `ShipyardScreen.cs:76-96`, confirmed in `Logs/yard-shots/v2/desk-overview.png` | The overview's `scroll` element (`yard-options`) is a **plain `VisualElement`, not an actual `ScrollView`**, and the screen never uses the game's own band-height/paging arithmetic. On desktop, the summary row (Length/Hold/Berths/Guns/Draft/Cargo allowance) is already clipped mid-word by the footer, and the warnings label — the one place `CanRefitNow`'s and `CannotConfirm`'s reasons surface — has **no room to render at all** in this screenshot. A warning that exists in the model can be completely absent on screen with no indication it's being cut. | Route the overview through `SheetHost`/`SheetKit`'s paging (or add a real scroll view) the same way every other sheet in the game already does; the game already has the "band height" arithmetic built for exactly this problem. |
| Confusing | `ShipyardScreen.cs:141` badge text / `RefreshOverview` | `CanRefitNow` reasons (not at home berth, under way, mid-raid, hands ashore, cargo transfer in progress) are appended into the same `warnings` label as advisory warnings ("2 guns will go to the dry dock") with **no visual distinction** — same font, same color, one joined by `\n`. A blocking reason and an FYI read identically. | Split into two labels/rows: blocking (red/ember) vs advisory (neutral), same idiom the hull-integrity block in `ShipSheet` already uses ("hull sound" vs "no timber aboard"). |
| Confusing | `ShipyardScreen.cs:249-254` `InsertTile` | The "+" tile is **hidden**, not disabled-with-reason, once `Count >= Maximum` (`RefreshTiles` line 206). A player at max length just sees the tile vanish with zero explanation anywhere on screen — no "maximum length for this hull" text exists on the overview at all once the tile is gone (the message only exists as a `Refuse()` string that fires if `InsertMiddle` is somehow still called). | Keep the tile, disabled, with the reason visible underneath — same pattern used for Raise/Lower all and Remove-this-section. |
| Polish | `ShipyardScreen.cs:64` | The **Undo** button is icon-only with a tooltip (`"Undo last change"`) — invisible on a phone with no hover, the exact problem the 2026-09-25 review fixed for Raise/Lower all but didn't apply here. First-time players have no way to learn what the icon does; when disabled (`CanUndo` false), it gives zero feedback why. | Either a text label alongside the icon, or a one-line "nothing to undo yet" under it when disabled, mirroring `deckReason`. |
| Polish | `ShipyardScreen.cs:239-246` tile status line | `"guns 2 · berths 4 · hold 5"` is small, lowercase, dense, and unlabeled (no header row explaining what the three numbers are) — legible once you know the game, opaque on first sight. Compare the raised-deck chip, which got an explicit fix for exactly this kind of "buried in a sentence" problem (comment at `ShipyardScreen.cs:266-276`). | Same treatment: a short header or slightly larger icon+number pairing. |

## Screen: Section sheet — Structure page

| Sev | Where | What the player experiences | Fix |
|---|---|---|---|
| Confusing → **near-invisible text** | `phone-sheet-structure-raised.png` | The bottom-of-page note (`SheetKit.Note(draft.Message)`, `ShipyardSectionSheet.cs:160`) renders as a tiny pencil icon with text that is both **very low contrast against the dark background and appears to have its leading character clipped** ("📝 …guns …to the dry dock" — the "2" that should read "2 guns will go to the dry dock" is barely legible in the screenshot). This is exactly the kind of message ("2 guns will go to the dry dock") the player needs to see before confirming a raise/lower. | Check `SheetKit.Note`'s contrast token on the sea/yard theme and its left padding/icon overlap; verify live at 1080x2340. |
| Fine | `ShipyardSectionSheet.cs:107-160` | Deck Low/Raised segmented control, "Remove this section" with its blocked reason printed as visible text (not tooltip-only, unlike Undo above) — this one got the fix. Stern-only wheel picker is clear. | — |

## Screen: Section sheet — Guns page

| Sev | Where | What the player experiences | Fix |
|---|---|---|---|
| **Blocker** (same root cause as the overview) | `phone-sheet-guns.png`, `desk-sheet-guns.png` | Four gun-slot rows plus the "Dry dock: cannon x2." caption do not fit the fixed sheet band. The **4th row is cut off mid-sentence on both phone and desktop**, and the "Dry dock: …" line that names how many spare cannons are available is **entirely off-screen** in both screenshots. A section with more slots (e.g. a wide/raised middle) would be worse. This directly contradicts "never feel like they're missing a feature" — the dry-dock stock count, the one number that tells the player whether tapping an empty slot will do anything, is invisible. | Same fix as the overview: page this body (`SheetKit.SheetPager` — already built and used elsewhere) instead of assuming it always fits. |
| Confusing | `ShipyardSectionSheet.cs:213-216` row status strings | Fine wording individually ("Fitted — tap to send to the dry dock", "Empty — no gun in the dry dock"), but the row stays **tappable-looking** (same button chrome) even when disabled/unusable (`s.usable == false`), relying on `SetEnabled` alone for the disabled visual — worth a live check that a disabled row reads clearly as unavailable rather than just slightly duller text on a busy background. | Verify contrast/backdrop on a disabled `yard-gun-row` live; add a stronger disabled state if it's subtle. |
| Feature invisible in UI | whole page | Nowhere in the shipyard does the UI explain **how a cannon gets into the dry dock in the first place**. The only path shown is removing one already fitted. If cannons are meant to be built/bought, that path is not discoverable from here at all. | Confirm the intended acquisition path and surface it (even just a one-line hint: "built at the blacksmith" or similar), or accept this is future scope and say so explicitly in the empty-slot message instead of a flat "no gun in the dry dock". |

## Screen: Section sheet — Interior page

| Sev | Where | What the player experiences | Fix |
|---|---|---|---|
| Fine | `phone-sheet-interior.png` | Berths −/+ with min/max range stated, a budget bar, hold cells derived, the delta line ("Same as the current fit."), and the plain-language rule ("Every berth this section carries costs hold space…") — this is the clearest page in the whole flow: numbers, range, consequence, all visible, in plain language. Model this page's structure for the rest. | — |
| Note (for the incoming 2D cutaway) | — | This page's berths-only framing (no cargo-vs-bunks visualization) is exactly what the in-flight 2D cutaway is meant to replace — see below. | — |

## Confirm / Cancel / success feedback / undo

| Sev | Where | What the player experiences | Fix |
|---|---|---|---|
| **Blocker** | `ShipyardScreen.cs:94` `confirm = Command(footer, "Confirm refit", () => { if (draft.Confirm()) Close(); });` | `ShipyardDraft.Confirm()` sets `Message = "Refit confirmed."` and fires `Changed` — but the very same call immediately closes the screen, so **that confirmation text is never rendered to the player**. Tap Confirm → screen vanishes → back on the ship sheet, with no toast, no sound cue mentioned in code, nothing saying the refit actually happened. Compare: every failure path (`StaleDraft`, `ApplyFailed`, `SaveFailed`, `CannotRefitNow`) DOES stay on screen and show its message via `message.text` — so failure has feedback and success does not, which is backwards for player trust in a system that just changed their ship. | Show the confirmation (even a brief "Refit confirmed" beat before closing, or a toast/notification after closing on the ship sheet) rather than silently succeeding. |
| Confusing | `ShipyardScreen.cs:186-191` `confirm.tooltip` / `message.text` | The blocked-reason IS mirrored into the visible `message` label (good, this one avoids the tooltip-only trap) — but it's truncated to a generic "Refit blocked - see details" once the underlying reason exceeds 100 characters (`RefreshOverview` line 190), and the full reason only appears in the tooltip (invisible on phone). Several real refusal strings in `Shipyard.cs`/`ShipyardService.cs` are well over 100 characters (e.g. the `GUNS_NEED_CREW` message, the cargo-overload message). | Wrap/scroll the message instead of truncating to a tooltip-only fallback, or shorten the underlying strings — but don't hide the reason behind hover on the platform that has no hover. |
| Polish | `ShipyardDraft.Undo()` | Undo has no redo and no visible history ("undid: raised Mid 1" etc.) — acceptable for a single-level undo, but worth confirming Kevin doesn't want more than one step back given how many taps a full refit can take. | Low priority; flag only. |

## Refusal-message review (`ShipyardCodes` + every `Refuse()`/`message =` string)

Tone and plain language are **consistently good** — this is a real strength of
the backend. Examples: *"She is under way. Bring her to rest first."*,
*"{name} is ashore. Call the hands back aboard first."*, *"Take 2 guns off to
the dry dock first."*, *"Unload 4 first; nothing is thrown overboard."* — all
short, second-person-adjacent, name the fix, no jargon, no error codes shown
to the player (codes stay internal to `ShipyardCodes`). No refusal message
reviewed uses a raw code or a technical term.

The **only** message-quality issue found: refusals that block **Confirm**
(`Overloaded`/`SimOutOfRange`: *"Lighten her first."*) have **no in-shipyard
path to act on them** — there's no unload control inside this modal. The
player must Cancel, leave the shipyard, find the manifest's cargo tab, unload,
then reopen the shipyard and redo their changes (the draft is not preserved
across a full close/reopen as far as this reading of `ShipyardDraft`'s
lifecycle shows — it's constructed fresh per `ShipyardLiveBridge.Open()` call).
That's a real loop-breaking friction point, not a wording problem.

| Sev | Where | Fix |
|---|---|---|
| Confusing | `ShipyardCodes.Overloaded`/`SimOutOfRange` refusal path, no unload control in `ShipyardScreen` | Either let the shipyard show current cargo/weight and point at it, or accept the round-trip and say so explicitly ("Cancel this refit, unload cargo from the manifest, then come back") rather than the terser "Lighten her first." which implies an action available on this screen. |

## Features that exist in the backend but are invisible or hard to find in the UI

- **Dry-dock stock and how it fills** — `Report().dryDock` is read (gun count),
  but nothing in the UI shows the dry dock as a *place* (inventory, other
  stored hull parts if any) — only a derived count for cannons. This is
  explicitly what the in-flight dry-dock feature is meant to fix; flagging so
  it isn't lost.
- **`RaisedDeckUnavailableReason` / per-section `SectionUnavailableReason`
  logic is rich** (needs wide beam, needs ≥1 middle bay, needs a legal
  assembly) but the UI only ever shows the single top reason string — a
  player blocked by "needs wide beam" who then sets wide beam and is still
  blocked by "needs ≥1 middle bay" sees a **new, different, one-at-a-time**
  reason each time they retry, never the full list up front.
- **`report.blocking` vs `report.warnings`** — the backend already
  distinguishes hard blocks from soft warnings (`RefreshWarnings` reads both
  into one undifferentiated list). The type distinction exists in the data and
  is thrown away in the UI (see the "Confusing" overview finding above).
- **Hands-ashore consequence** (`ShipyardValidation.handsAshore`,
  `HandsAshoreNeedHome`) is computed and gates Confirm, but the ONLY place a
  player learns "surplus hands will go ashore" is the generic warnings/blocking
  text — there's no per-crew-member visibility (who, how many) the way the
  gun-loss warning at least names a count.
- **Wheel choice (Timber vs Reinforced)** has no stat comparison shown
  anywhere in the sheet (no speed/handling delta) — a player picks blind
  between two buttons with identical-looking chrome.

## Desktop vs phone

Both platforms share the same overflow/paging bugs (guns page 4th row cut,
overview summary/warnings cut) — this is a layout-system problem, not a
phone-only or desktop-only one, since `ShipyardScreen` never adopted the
band-height paging the rest of the HUD uses regardless of aspect. Desktop's
wider layout (`yard-wide`) otherwise reads fine: two-column preview + panel,
same controls, same text. No desktop-only defects found beyond the shared one.

## For the two changes in flight

**Interior → 2D cutaway (tap compartments cargo↔bunks, guns on the deck line):**
- Must NOT inherit the no-paging bug — a compartment-tap UI with several rows
  of compartments needs the same band/page treatment the current Interior page
  (correctly) never needed because it was short. Budget for a longer list once
  compartments replace one −/+ control.
- Should carry forward the one thing the current Interior page gets right:
  a plain-language consequence line and a visible min/max range **per tap
  target**, not just a global note.
- Since guns move onto the deck line here, make sure the Guns page's own
  slot-picking UI and this cutaway don't end up telling two different stories
  about where a gun physically is (currently Guns page already has "Middle
  bay 1, starboard, forward" — confirm the cutaway reuses the exact same
  slot naming, not a second scheme).

**Player-built dry dock (shipyard only works with one at the home berth):**
- The "Shipyard" button on the ship sheet (finding above, Discovery table)
  will need a THIRD gate beyond "component exists" and "at home berth": dry
  dock built. Get the visible-reason treatment right from the start here —
  don't let it join Undo/the "+" tile in the tooltip-only trap.
- `CanRefitNow` will need a new reason string for "no dry dock at your home
  berth" — write it in the same plain, actionable voice as its neighbors
  (e.g. *"Build a dry dock at your home berth to refit her."*), and make sure
  it surfaces BEFORE the full-screen modal opens, not buried in warnings text
  like today's "refits are done at your home berth" reason is.
- Showing the ship model *in* the dock is a strong opportunity to replace the
  disconnected "PREVIEW"/"SECTION" badge-in-a-generic-viewport framing with
  something that reads as "this is really happening at a place," reinforcing
  the "never feel like you're missing a feature" goal by grounding the whole
  screen in the world rather than a floating 3D preview.

---

## Summary of what to fix first (ordered)

1. Route `ShipyardScreen`/`ShipyardSectionSheet` bodies through the game's
   existing sheet-paging system — this single fix resolves the guns-page
   cutoff, the overview's clipped summary/warnings, and future-proofs the
   Interior cutaway and the dry-dock gate's extra text.
2. Give "Confirm refit" visible success feedback before/after closing.
3. Split blocking reasons from advisory warnings visually on the overview.
4. Kill the remaining tooltip-only disabled states (Undo, hidden "+" tile at
   max length) using the pattern already proven for Raise/Lower all.
5. Fix the low-contrast/clipped note text seen in
   `phone-sheet-structure-raised.png`.
6. Gate the "Shipyard" button itself on `CanRefitNow`/dry-dock, with a visible
   reason, instead of only discovering the block after opening the modal.
