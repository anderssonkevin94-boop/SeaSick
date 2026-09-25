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

## Step 2 implementation (Claude/Opus, 2026-09-25, worktree SeaSick-modular)

Backend shipped, `docs/SHIPYARD-API.md` §18 has the full write-up. In short, for
reconciling against the UI:

- `ShipConfiguration.layouts : List<SectionLayout>` (`SectionLayout { string section;
  int berths; }`), additive, no schemaVersion bump. `ValueEquals`/`Clone`/JSON round-trip
  all cover it. Missing = the section's authored default (reproduces today's hold cells
  bit-for-bit, gated over all 14 hull modules).
- `standards.json` carries `berthSpaceUnits: 0.5`; `ModuleLibrary.BerthSpaceUnits` reads
  it (falls back to 0.5). `CapacitySpec.maxBerths` (optional `ProvisionalInt`) is the
  per-module floor-area cap; none of today's modules set it, so `maxBerths` is
  `floor(budget / berthSpaceUnits)` everywhere right now.
- `ShipyardPlanner.SectionSpaceFor(ModuleDef, ModuleLibrary, ShipConfiguration cfg,
  string sectionKey, out budgetUnits, out berthCost, out berths, out holdCells,
  out defaultBerths, out maxBerths)` is the ONE computation both
  `ShipyardPlanner.SectionCapacities` (now `(asm, lib, cfg, viewZ, out missing)`, cfg may
  be null = all-default; the old 4-arg overload still exists) and
  `ShipyardInterior.SectionSpace` call -- so the UI's numbers and the applied capacity
  can never drift apart. `berths` is clamped to `[0, maxBerths]` even for a bad/hand-
  edited `layouts` entry.
- `ShipyardService.SectionSpace(draft, sectionKey)` / `.WithBerths(draft, sectionKey,
  berths)` are exactly the signatures above; `ShipyardRefitAdapter` mirrors both.
  `WithBerths` REMOVES the section's `layouts` entry when the clamped value equals the
  default, rather than writing a redundant one, so a round trip to default and back
  stays `ValueEquals` to a draft that was never touched (matters for `ShipyardDraft.Dirty`).
- `ShipConfiguration.ShiftMiddleKeys(ShipConfiguration cfg, int fromIndex, int delta)`
  (static, takes the config as the first argument -- the doc's abbreviated call above
  omits it) renumbers `"middle[i]"` keys `>= fromIndex` by `delta` in BOTH `equipment`
  slot ids and `layouts[].section`, in place. `ShipyardPlanner`'s own middle-removal path
  (`Occupancy`'s `canRemove` probe) now uses it instead of a hand-rolled equipment-only
  loop. The UI's `ShipyardDraft.InsertMiddle`/`RemoveSection` may call this directly, or
  keep its own equivalent for `equipment` -- either way the KEYING SCHEME (both lists
  keyed exactly like `ShipAssembler.MiddleKey`) must stay in step with this file.
- `ShipyardRefitProbe` gained a live refit case: stern all hold + middle[0] at max
  berths, built through `SectionSpace`/`WithBerths` (never hand-built `layouts`),
  checked against the applied `ShipyardPlan.sections` and round-tripped through a real
  save/load (`ValueEquals` now covers `layouts`, so the existing save gate already
  proves it; a dedicated gate checks the hold capacity too).
- `tools/modular-selftest.sh`: 10 new gates (`interior-*`, `shift-middle-keys-*`,
  `layouts-json-round-trip`, `missing-layouts-field-is-defaults`), 202/202 green
  (was 192).
