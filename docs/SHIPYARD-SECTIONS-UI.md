# Shipyard: section-by-section editing — design

Status: DESIGN 2026-09-25 (Claude/Opus), from Kevin's UI feedback:
"ship overview → press on a section → options for that section (remove, build up,
add/remove cannons…) → save before moving on; keep adding segments to make it longer;
removing a section is done by selecting it; all segments have options for what they
contain (sleeping quarters, storage, cannon ports)." Kevin chose: steps 1 + 2 now,
voyage stores LATER (Interior = berths + hold only), guns stay deck guns on slots.
GDD rules that apply: "one object, one sheet, one decision"; "no scrolling: pages you
swipe between"; iPhone portrait, one thumb, big targets; desktop second.

## Step 1 — the flow (UI only, existing capabilities)
**Overview (shipyard home):** 3D preview on top; directly under it a strip of big
section TILES, thumb-reachable: `[Stern] [+] [Mid 1] [+] [Mid 2] [+] [Bow]` — "+" tiles
insert a middle at that position (hidden at max length / disabled with reason). Each
tile: name + tiny status (deck level, guns n, berths n, hold n). Below: whole-ship
summary (length, hold, berths, guns, draft, cargo allowance, stability meter), ship-wide
settings (Beam standard/wide), the report's warnings ("4 hands will go ashore",
"2 guns will go to the dry dock"), and [Confirm refit] [Cancel]. No scrolling on phone —
if it doesn't fit, split into swipe pages.
**Section sheet (tap a tile):** the preview highlights that section (tint its renderers,
dim the rest) and frames it; a bottom sheet with swipe pages:
- Structure: Deck Low/Raised (existing ToggleSection + its reason), "Remove this
  section" (middles only; stern/bow say why not), stern also: paddle wheel pick.
- Guns: this section's slots port/starboard (existing EquipmentSlots/Fit/Remove), dry
  dock count shown.
- Interior (step 2): berths vs hold with +/−, a budget bar.
- Each page shows this section's effect on the ship (report Section(key) + deltas).
- [Done] keeps the changes in the draft and returns to the overview; [Reset section]
  restores just this section from the draft state it had when the sheet opened.
Nothing touches the ship until [Confirm refit] (the existing atomic ApplyRefit).
Old controls (middle count +/−, Raise all/Lower all row, gun list) move into this flow;
keep "Raise all / Lower all" only on the overview if it fits without scrolling.

**Draft API additions (UI/ModularYard/ShipyardDraft.cs):**
- `bool InsertMiddle(int index)` — inserts a middle (width/level matching neighbours via
  RaisedSections: a new section is LOW unless both neighbours are raised) at `index`;
  renumbers `middle[i]` keys ≥ index in equipment AND layouts (step 2) by +1.
- `bool RemoveSection(string sectionKey)` — middles only; guns on it → dry dock
  (existing pattern); later keys renumber −1 (equipment + layouts).
- `DraftSnapshot BeginSection(string key)` / `void ResetSection(string key, DraftSnapshot s)`.

## Step 2 — Interior: space budget per section (backend + UI page)
Each section has a SPACE BUDGET in hold-cell units (1 unit = 38.229 U³, today's cell).
A berth costs 0.5 unit (a bunk + its share of access ≈ half a cargo cell) — PROVISIONAL,
one constant in standards.json (`berthSpaceUnits`). Budget = authored holdCells +
0.5 × authored berths, so the DEFAULT layout of every module reproduces today's numbers
exactly (probes' capacity expectations unchanged). Player sets berths per section
(0 … maxBerths); hold = floor(budget − 0.5 × berths). maxBerths = floor(budget / 0.5)
capped by a per-module authored `maxBerths` if present (floor area), else by budget.
Guns use deck slots, not interior space.

**Data/config (Ship/Modular):**
- `ShipConfiguration.layouts : List<SectionLayout>`; `SectionLayout { string section;
  int berths; }` keyed like equipment ("stern", "middle[0]", "bow"); missing = default.
  Additive field, no schemaVersion bump (old saves = defaults). Clone/ValueEquals/JSON
  round-trip include it.
- Capacity (ShipyardPlanner.PlanFor / SectionCapacities) uses the layout.
- `ShipyardService.SectionSpace(ShipConfiguration draft, string sectionKey)` →
  `SectionSpaceView { string section; float budgetUnits; float berthCost; int berths;
  int holdCells; int defaultBerths; int minBerths; int maxBerths; string reason; }`
  (pure; reason non-empty when the section can't be edited).
- `ShipConfiguration WithBerths(ShipConfiguration draft, string sectionKey, int berths)`
  pure helper returning a new draft (clamped), on ShipyardService (and the adapter).
- Existing rules do the rest: crew aboard > berths → hands go ashore (report says so);
  cargo held > hold cells → CARGO_WOULD_NOT_FIT; guns need crew → GUNS_NEED_CREW.
- Self-test gates: defaults reproduce authored capacity for every module; clamp; layouts
  renumber on insert/remove; JSON round-trip; old config without layouts = defaults.

**UI Interior page:** berths [−] n [+], hold n (derived), a budget bar, the effect line
("+2 berths, −1 hold"), refusal reason if any.

## Probes
ShipyardUiProbe: overview tiles present, tap tile → sheet with pages, Done/Reset,
InsertMiddle at 0/end with gun keys renumbered, RemoveSection of a middle with guns,
Interior +/− clamps. ShipyardRefitProbe: a refit with a non-default layout (e.g. stern
all hold, middle max berths) applies, capacity matches, round-trips through the save.
