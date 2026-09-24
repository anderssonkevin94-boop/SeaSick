# W2-r1 wide-hull validation

Branch `wide-hull` (worktree `SeaSick-wide`, from `modular-ships` d6d66ad),
2026-09-25. Validates Astra's `art-staging/modular-hull-wide-v1` kit ("W2-r1")
against the modular-ship module contract (`docs/MODULAR-SHIPS.md`,
`docs/SHIPYARD-API.md`), then wires it in and scaffolds a width-transition
module. Headless gates: `WideHullValidation.cs`, added to
`ModularShipSelfTest.RunWith` and run by `tools/modular-selftest.sh` and
`unity cmd eval`. All numbers below were independently derived from the kit
files (`manifest.json`, `interfaces.json`, `hydrostatics/*.json`,
`validation.json`, `export-verification.json`) using Blender in `--background`
mode (never the editor) and Python, then re-verified live by the headless
gates against the committed module JSON. **Result: PASS on every point.**
W2-r1 is wired into the prototype shipyard (Task B) and a placeholder
transition module + synthetic proof of the mechanism are in place (Task C).

## PASS/FAIL table

| # | Check | Result | Key numbers |
| - | --- | --- | --- |
| 1 | Interface profile equality | **PASS** | Stern_W2_forward == Midship_W2_aft == Midship_W2_forward == Bow_W2_aft, point-for-point, maxdiff 0.0 across all 22 points. Half-beam 5.80, deck 1.76, keel -2.75. Differs from W1-r2 (half-beam 4.64, keel -1.92) — mixed W1/W2 configs refused both directions (gates `w1-stern-w2-middle-rejected`, `w2-stern-w1-middle-rejected`). |
| 2 | Sockets/lengths | **PASS** | Stern 9.30, middle 6.00 (repeats), bow 10.98 incl. prow. Bow stem (waterline length datum) at X **8.45** — identical to W1-r2's, confirming the approved bow rake is geometrically unchanged, only the cross-section is wider/deeper. Wheel axle (0.72, 0, 0.35), chimney (assembled-midpoint, Z 1.76) — both unchanged. M1 rotor (swept 1.6222) fits the unchanged M1 pocket (limit 1.64). M1-L refused (gate `w2-m1l-oversized-wheel-rejected`). |
| 3 | Hydrostatic tables | **PASS** | Schema matches the W1-r2 loader exactly (same `HydroTable` fields). `sourceGeometrySha256` in the manifest == the table file's own field for all 3 sections (Stern/Midship/Bow), and the loader's own hash check passes (`w2-hydro-tables-load-and-hash-match`). Keel/deck range -2.75..1.76 on all three. Volume monotonic (enforced by `HydroTable.Valid()`, part of the load gate). Cross-check: Midship_W2's trapezoid area (Hull_Shell profile) x 6.00 = 294.82 u³ vs the table's 294.84 u³ — **0.005%**, well inside 2%. |
| 4 | Deck-gun slots, real cannon | **PASS** (with a documented caveat) | All 10 kit deck-slot empties (stern 2, middle 4, bow 4) individually pass the assembler's own clearance/passage test against the REAL cannon footprint (1.22 x 2.18 x 1.28, `art-staging/cannon-astra-v1`), derived crew passages included (gate `all-10-w2-deck-slots-pass-real-cannon`). Passage exclusion proven live, not a no-op (`real-cannon-in-passage-rejected`). **Caveat, proven not assumed**: the real cannon (2.18 long) is longer than the 2.0 u pitch between same-side DeckSlot_0/DeckSlot_1, so two real cannons on the same side of one bay overlap (`same-side-adjacent-slots-overlap-with-real-cannon`); only one gun pair per module is offered in `capacity.gunSlots`, same as W1-r2. |
| 5 | Capacity + mass, assembly, reshape, wheel dip | **PASS** | See below. |

`tools/modular-selftest.sh`: **`ModularShipSelfTest: 127 PASS, 0 FAIL`**
(97 pre-existing milestone-1 + shipyard gates, unchanged in substance — 4 of
their count/allow-list assertions were updated for the new module count and
W2-r1 allow-list entries — plus 30 new `WideHullValidation` gates).

## 1. Interface profile

`interfaces.json`'s `Hull_Shell` block has 4 named loops
(`Stern_W2_forward`, `Midship_W2_aft`, `Midship_W2_forward`, `Bow_W2_aft`),
22 points each. Compared pairwise point-for-point: **max absolute difference
0.0** (they are the literal same list, not just numerically close) —
comfortably inside the 1e-4 u bound. `standards.json` gained a `"W2"`
`joinProfiles` entry transcribing this (half-beam 5.8, deck 1.76, keel -2.75,
22 points), separate from the pre-existing `"W2-placeholder"` stub (kept,
still used by the existing `w2-placeholder-rejected` gate).

Because `"W2"` is a different id from `"W1-r2"` with different numbers, the
assembler's existing per-socket standard-equality check refuses any direct
W1-r2/W2 join automatically — proven both directions:
`w1-stern-w2-middle-rejected` (W1-r2 stern, W2 middle) and
`w2-stern-w1-middle-rejected` (W2 stern, W1-r2 middle), both
`JOIN_PROFILE_MISMATCH`.

## 2. Sockets and lengths

All manifest numbers match: Stern_W2 9.30 u, Midship_W2 6.00 u (repeatable),
Bow_W2 10.979999... u (10.98, prow included). Wheel axle stern-local
(0.72, 0, 0.35); chimney placement rule and Z (1.76) unchanged from W1-r2.

**Bow stem (waterline length datum).** W1-r2's bow measures its physics
length to a `hull.stem` socket at X 8.45 (the raked hull at the height datum,
prow excluded), not in Astra's manifest, measured by hand in 2026-09-24. The
same measurement was repeated here for Bow_W2 (Blender `--background`,
slicing `Bow_W2/Hull_Shell.fbx` at the module height datum Z 0 and at the
deck Z 1.76 — see "Measurement method" below): **X 8.45 at Z 0, X 9.418 at
Z 1.76 — identical to Bow_W1's** (8.45 / 9.42). The kit's README says the
approved V3 bow rake and 60-triangle prow are retained; this measurement
confirms the X-profile genuinely did not change, only Y (beam) and Z (keel)
did. Authored as a provisional `hull.stem` socket on `hull.bow.w2r1.v1`,
same as W1-r2's.

**M1 wheel fits unchanged.** The kit's own words ("Standard M1 wheel socket
and rotor/carrier geometry remain unchanged") were verified, not trusted:
Rotor/Carrier/Chimney FBX bounds measured from the kit are bit-for-bit the
same as W1-r2's own copies (rotor ±1.62/±2.3/±1.62 u, chimney height 5.12 u).
No new Rotor/Carrier/Chimney modules were authored — the existing
`wheel.rotor.m1.timber`/`.reinforced`, `wheel.carrier.m1` and
`fitting.chimney.v3` are reused as-is on W2-r1 ships. M1-L (oversized wheel)
is refused on the W2-r1 stern exactly as on W1-r2 (no stern authors an M1-L
pocket) — gate `w2-m1l-oversized-wheel-rejected`.

### Measurement method (module-local AABB and stem X)

`Bow_W2/Hull_Shell.fbx` etc. were re-imported into Blender
(`--background --factory-startup`, default `import_scene.fbx`). Blender's
importer applies its own axis convention from the FBX's embedded metadata,
which is a 90°-about-Z rotation relative to the kit's stated authoring axes
(verified empirically, not assumed): `authored.x = -measured.y`,
`authored.y = measured.x`, `authored.z = measured.z` (Z, up, is untouched).
This was confirmed three ways before trusting it: (a) the recovered stern
length (-0.23..9.30) and half-beam (±6.01, shell+rails) match the manifest;
(b) the recovered max-Z of every module (Stern_W2 3.652, Midship_W2 2.47,
Bow_W2 3.52) is bit-for-bit identical to the corresponding W1-r2 module's
own max-Z (unchanged fitting/rail/housing heights, exactly as the README
claims); (c) the recovered bow stem X (8.45, see above) matches W1-r2's own
independently-measured value. `boundsMinU`/`boundsMaxU` in the three W2-r1
module JSONs are these converted bounds.

## 3. Hydrostatic tables

`HydroTable` (the loader's own schema, `ShipHydrostatics.cs`) reads the kit's
`hydrostatics/*.json` files without modification — same fields
(`schemaVersion`, `sourceGeometrySha256`, `validWaterlineZU`, `waterlineZU`,
`integratedVolumeU3`, plus ignored extras) as W1-r2's tables. Copied verbatim
into `Resources/ShipModules/Hydrostatics/HullW2r1_v1/`.

`sourceGeometrySha256` in each `hull.*.w2r1.v1.json`'s `hydrostatics` block
== the table file's own `sourceGeometrySha256` field, for all three sections
(64-hex-char SHA256, checked by hand and by `ModuleLibrary.LoadHydrostatics`'s
own hash check at load time — a mismatch would silently drop the table and
fail `w2-hydro-tables-load-and-hash-match`). Station/waterline counts: 66
waterlines each (Stern 199 stations, Midship 99, Bow 287). Volume strictly
monotonic in waterline (`HydroTable.Valid()`, part of the same gate). Valid
range -2.75..1.76 on all three (`w2-hydro-valid-range`).

**Independent cross-check.** Midship_W2's deck-level integrated volume
(294.8391501263652 u³) was checked against a trapezoid estimate: the shoelace
area of the `Midship_W2_aft` `Hull_Shell` profile (`interfaces.json`) x 6.00 u
bay length. The raw 22-point loop's *naive* shoelace (closing last point to
first) gives the wrong figure (42.60 u², a 13% miss) because point 0 is a
duplicate/attachment vertex out of ring order, not the true boundary start;
dropping it and closing the remaining 21-point ring through the deck line
gives 49.1374094413 u² x 6.00 = **294.82 u³ vs the table's 294.84 u³ — 0.005%
difference**, comfortably inside 2%. (Sanity-checked against W1-r2's own
Long lightship draft: the same solver, run on W1-r2's own tables, reproduces
the already-published 0.844 m to 3 decimal places.)

## 4. Deck-gun slots, real cannon footprint

The kit gives 10 deck-slot empties total (2 stern, 4 middle, 4 bow — same
count and layout pattern as W1-r2's, just at Y ±4.35 instead of ±3.45). Each
was tested with the REAL cannon (`art-staging/cannon-astra-v1`:
1.22 wide x 2.18 long x 1.28 high, confirmed against its own README) as the
slot's `clearanceSizeU` (zero-margin, same convention W1-r2 uses), run
through `ShipAssembler`'s own equipment-placement checks (bounds + crew
passage), on the Long W2-r1 assembly. **All 10 pass** (gate
`all-10-w2-deck-slots-pass-real-cannon`).

**Crew passages are NOT in the kit** (its README explicitly says "crew
navigation... remain future work"). Derived from W1-r2's own passage rule,
scaled to the W2-r1 deck: `|y| <= deck-slot Y (4.35) - half the real cannon's
width (1.22/2 = 0.61) = 3.74`. This is the exact same rule W1-r2 uses
(`|y| <= slot Y - half the equipment's Y clearance`), just with W2-r1's slot
position and the REAL cannon's width in place of the placeholder's. Height
(3.4 u = 1.7 m crew) and, for the stern, the passage's forward-of-housing
start (X 3.16) are carried over unchanged (README: housing/ironwork sizes
unchanged). The bow's passage forward edge, unlike the stern's, IS
cannon-size-dependent ("to the forward edge of the last deck-slot
clearance"): with the real cannon's longer footprint it moves from W1-r2's
4.4 to **4.59**. Marked `provisional` in the module JSON, matching every
deck-slot/passage field on W1-r2. Proven live, not asserted: nudging a real
cannon into the passage via the middle bay's free `DeckArea` is refused
`EQUIPMENT_BLOCKS_PASSAGE` (gate `real-cannon-in-passage-rejected`).

**Caveat found, not assumed away.** The real cannon (2.18 u long) is longer
than the 2.0 u pitch between same-side `DeckSlot_0`/`DeckSlot_1` in one bay
(W1-r2's placeholder cannon, 1.8 u, left a 0.2 u gap; the real cannon
overlaps by 0.18 u). Proven with the assembler itself
(`same-side-adjacent-slots-overlap-with-real-cannon`, `EQUIPMENT_OVERLAP`).
Consequence: only ONE gun pair per module is designated in `capacity.gunSlots`
(same choice W1-r2 already makes), documented on every affected slot.

## 5. Capacity + mass, assembly, reshape factors, wheel dip

**Mass method (pick one, justify).** Two candidates were computed and
compared against the ratio's plausibility, not picked by fiat:

| Method | W2/W1 Long ratio | W2 Long lightship | Resulting draft as % of keel-to-deck depth |
| --- | --- | --- | --- |
| Hydrostatic-table volume-to-deck (chosen) | 940.54 / 611.67 = **1.5377** | 49 061.6 kg | **46.2%** |
| `Hull_Shell` mesh surface area | 757.44 / 604.42 = 1.2532 | 39 984.2 kg | 38.9% |

W1-r2 Long floats at 45.9% of its own keel-to-deck depth at lightship. The
volume-ratio mass reproduces that fraction almost exactly (46.2%) even though
W2-r1's absolute depth is 22.5% greater; the area-ratio mass gives a visibly
different loading fraction (38.9%). **Volume ratio chosen** and used
everywhere below. Per-module split: each section's own share of the
Long total's table volume-to-deck (documented per-file, `lightship.rule`).

| Module | Lightship (kg) | Hold cells | Berths | Gun slots designated |
| --- | ---: | ---: | ---: | --- |
| `hull.stern.w2r1.v1` | 19 889.6 | 10 | 2 | DeckSlot_2 pair |
| `hull.middle.w2r1.v1` | 15 379.7 | 8 | 5 | DeckSlot_1 pair |
| `hull.bow.w2r1.v1` | 13 792.3 | 7 | 2 | DeckSlot_1 pair |
| **Short (stern+bow)** | **33 681.9** | **17** | **4** | 2 pairs / 4 guns |
| **Long (+1 middle)** | **49 061.6** | **25** | **9** | 3 pairs / 6 guns |

Hold cells: `round(section table volume-to-deck / W1-r2's u³-per-hold-cell
(611.67/16 = 38.229))`. Berths: `floor(W1-r2 counterpart berths x W2/W1 deck-
area ratio (11.60/9.28 = 1.25))`. Gun slots: the designated pair per module
that passes the real-cannon/passage check (§4); DeckSlot_0 left off each
module to avoid the proven same-side overlap. All marked `provisional` with
a `source` line, same convention as W1-r2.

**Assembly, measurement, reshape factors.** Both Short and Long W2-r1
assemble (`w2-short-and-long-assemble`). `HullMeasure` on Long W2-r1 vs the
reference (W1-r2 Long): **sL = 1** (waterline length unchanged — same section
lengths AND the same stem X, §2), **sB = 1.25** (2 x 5.8 / 2 x 4.64, matches
the predicted 25%), **sD = 1.2255** ((1.76+2.75)/(1.76+1.92), matches
exactly) — gate `w2-reshape-factors`.

**Float check (tables, `AssemblyHydrostatics.SolveWaterline`, density 1025).**
Both Short and Long float within keel..deck at lightship AND at full load
(hold + berths manned + guns), no OVERLOADED:

| Config | Mass (kg) | Waterline Z (u) | Draft (m) |
| --- | ---: | ---: | ---: |
| Short, lightship | 33 681.9 | -0.623 | 1.063 |
| Short, full load | 44 541.9 | -0.076 | 1.337 |
| Long, lightship | 49 061.6 | -0.666 | 1.042 |
| Long, full load | 65 371.6 | -0.096 | 1.327 |

(Full load = lightship + hold cells x 500 kg + berths x 90 kg + guns x
500 kg, the shared `WeightModel.Default`.) Gates
`w2-short-floats-lightship-and-full-load`, `w2-long-floats-lightship-and-full-load`.

**Wheel-dip finding.** Wheel bottom (module datum) = axle Z (0.35, unchanged)
- nominal rotor radius (1.62) = **-1.27 u**. At every one of the four
conditions above the waterline (-0.62 to -0.10) sits well ABOVE the wheel
bottom (-1.27), so the wheel dips comfortably in all of them — gate
`w2-wheel-dips-at-lightship-and-full-load`. (An earlier hand-check with a
unit-conversion slip suggested the wheel might float clear at these masses;
re-derived correctly and cross-checked against W1-r2's own published 0.844 m
draft before trusting it — see "Measurement method" note in §3.) Nothing
implausible found here, but it is worth Kevin's eye once in Unity: W2-r1's
much deeper keel (-2.75 vs -1.92) with the SAME absolute wheel-axle Z means
the wheel sits proportionally shallower in the new hull than in W1-r2's, so
it is closer to floating clear than W1-r2's — a heavier loadout or a future
W2-r1-specific wheel housing would be worth watching.

## Transition module contract (Task C)

**No schema or assembler change was needed.** `SocketDef.standard` is
already per-socket, and `ShipAssembler` only ever compares the two TOUCHING
sockets at a join — never a whole-ship "one standard" invariant — so a
single hull module whose aft socket names one join profile and whose forward
socket names another is already assemblable, and non-transition modules
(whose aft and forward sockets always name the SAME profile) transitively
keep every run of consecutive non-transition sections on one standard. The
asked-for policy ("all hull sections share one interface standard unless
joined by a transition module") already falls out of the existing per-join
check with zero code changes; nothing new needed to be written to enforce it.

Proven with a SYNTHETIC in-memory module (`test.transition.synthetic.*`, not
committed, built and torn down inside `WideHullValidation.Body`):

* `w1-transition-w2-w2-assembles`: W1-r2 stern + [synthetic transition,
  aft=W1-r2/fwd=W2] + W2-r1 middle + W2-r1 bow assembles.
* `w2-transition-reversed-w1-assembles`: the mirror (W2-r1 stern +
  [synthetic transition, aft=W2/fwd=W1-r2] + W1-r2 middle + W1-r2 bow) also
  assembles — orientation is a property of the module, not hard-coded.
* `transition-wrong-orientation-rejected`: using the W1-r2-aft/W2-fwd
  transition the WRONG way round (behind a W2-r1 stern) is refused
  `JOIN_PROFILE_MISMATCH`, same rule as any mismatched pair.
* `transition-without-data-flags-missing-capacity-and-mass`: a BARE
  synthetic transition (no `capacity`/`hydrostatics`/`lightship`, same as the
  real placeholder below) assembles at the milestone-1 level but is flagged
  by `ShipyardPlanner.ModuleLightshipKg`/`SectionCapacities`
  (`NO_MASS_DATA`/`NO_CAPACITY` territory) and by
  `AssemblyHydrostatics.For` (`missing` set, `Ok` false) — the SAME generic,
  kind-agnostic code path every other hull section goes through. No
  special-casing was added or needed.
* `real-transition-placeholder-refused-not-in-prototype`: the REAL committed
  placeholder module (`hull.transition.w1r2-w2r1.v1`, below) is refused by
  `ShipyardPolicy.Check` with `NOT_IN_PROTOTYPE`, mentioning its placeholder
  status — it is not offered to players and never will be until it is added
  to an allow-list (which this work deliberately did not do).

**The committed placeholder** (`Assets/_Project/Resources/ShipModules/Modules/hull.transition.w1r2-w2r1.v1.json`):
`kind: "Middle"`, `status: "placeholder"`, no `visuals`/`equipmentSlots`/
`passages`/`hydrostatics`/`lightship`/`capacity`. Its aft socket names
`"W1-r2"`, its forward socket `"W2"`, `lengthU` 6.0 (a placeholder guess, a
middle-bay's worth — NOT a measurement). Not on any `ShipyardPolicy`
allow-list, so it cannot be built by a player; it exists only so the schema
names it and the gates above can exercise the mechanism.

### What Astra's real delivery must carry

For a `hull.transition.<a>-<b>.v1` module to become buildable (added to a
`ShipyardPolicy` allow-list — not done by this work), it needs, per the
existing module contract (`docs/MODULAR-SHIPS.md` §3, §7):

1. **Two join profiles, one per end**, each an EXISTING (or newly-registered)
   `standards.json` `joinProfiles` id — `hull.aft` names the profile behind
   it, `hull.fwd` the profile ahead. They must genuinely match the two
   sections it is meant to bridge, point-for-point within 1e-4 u at each end
   (the same equality check used for §1 above).
2. **A length** (`lengthU`) and **module-local AABB bounds**, measured from
   the real FBX the same way §2 above was (or supplied directly in a
   manifest, cross-checked the same way).
3. **A hydrostatic station table** in the SAME schema the loader already
   reads (`ShipHydrostatics.cs`'s `HydroTable`) — `sourceGeometrySha256`
   matching the module's own declared hash, monotonic volume, a valid
   keel..deck range for ITS OWN (transitional, so probably asymmetric)
   cross-section.
4. **A `capacity` block** (`holdCells`, `berths`, `gunSlots` — every field
   `provisional: true` with a `source` line, same as every other hull
   section) and a **`lightship` mass**, seeded the same way §5 above was
   seeded (a defensible ratio to an existing counterpart, justified, not
   guessed).

No other code path treats a transition module specially, and none should
need to — `ShipAssembler`, `AssemblyHydrostatics` and
`ShipyardPlanner.SectionCapacities`/`ModuleLightshipKg` already handle any
hull-kind module generically, transition or not, as proven above.

## Send to Astra

* **The `modular-hull-wide-v1` kit (validated here) PASSES every contract
  check, no changes needed from you on it.** One thing worth your eyes: the
  real cannon (1.22 x 2.18 x 1.28) is 0.38 u longer than the placeholder used
  to size the deck slots' 2.0 u pitch, so two guns can't stand side-by-side
  in one bay on the same side (only one pair per bay is offered — not
  blocking, just a heads-up in case it affects your deck layout intentions).
* **`modular-width-inserts-v1` (read 2026-09-25, your WIP, untracked,
  read-only for me) is a DIFFERENT thing from what I was asked to scaffold a
  transition module for, and doesn't line up with `modular-hull-wide-v1`.**
  It widens the EXISTING W1-r2 stern/middle/bow in place (Port/Starboard
  halves moved outward + insert strips), to beam **12.08** (not W2-r1's
  11.6), depth UNCHANGED (not W2-r1's deeper -2.75 keel), and its own README
  says "Use one consistent width throughout" — i.e. it is not designed to let
  one ship mix widths mid-length at all, which is the opposite of what a
  "transition module" is for. Kevin/you: is `modular-hull-wide-v1` (W2-r1,
  wider AND deeper, independently-modelled sections) or
  `modular-width-inserts-v1` ("W1-center-expansion-r1", wider only, insert
  technique on the existing sections) the one you want to take forward as
  "the wide hull"? They read like two different explorations of the same
  brief, not two parts of one plan, and only one of the two widths should
  probably become canonical. I did not touch, copy or wire in
  `modular-width-inserts-v1`'s assets; my Task C placeholder/schema work
  bridges W1-r2 to W2-r1 specifically and would need a new join profile +
  placeholder if `modular-width-inserts-v1`'s width is chosen instead
  (mechanically trivial — see "What Astra's real delivery must carry" above
  — but it is a different id/numbers, not a drop-in).
* Bow stem X (8.45) and the crew passages (§2, §4) are still not in your
  manifest/interfaces data for either kit; I measured/derived them by hand
  again, same as W1-r2's. If a future delivery could include them directly
  it would remove one manual-measurement step per hull family.

## What I still need to do in Unity (not done here — no Unity was launched)

1. Open the project, let it import the 17 new W2-r1 FBXs (no `.meta` files
   were committed — Unity generates them) and the placeholder's absence of
   meshes (none to import, by design).
2. Run `SeaSick/Modular/Configure Mesh Importers` (or confirm
   `ModularShipModelImport`'s `AssetPostprocessor` picks the new files up on
   first import) so they get the same "no materials/scale 1/file-scale-use"
   settings as the W1-r2 meshes.
3. `unity cmd eval --json --code 'return SeaSick.Ship.Modular.ModularShipSelfTest.Run();'`
   to also exercise `visual-parts-resolve` against the real imported
   `Resources.Load` (headless only checks the FBX exists on disk, not that
   Unity can load it as a mesh).
4. Menu **SeaSick/Modular/Create Test Scene**, Play, look at `WideShort()`/
   `WideLong()` (`WidePresets.cs`) next to the existing W1-r2 ships — this
   validation never rendered anything.
5. A sea trial (float on the water, not just the table solve above) would be
   the natural next probe once Kevin wants to spend the time — propose it
   before running, per the "Consult before expensive runs" rule.

## Commits

* `7f1de77` — standards.json W2 join profile + the three W2-r1 module JSONs,
  hydrostatics tables, meshes, `WidePresets.cs`, `ShipyardPolicy` allow-list +
  family check (`Shipyard.cs`), existing gate count/allow-list assertions
  updated for the (then) 15-module library. `97 PASS, 0 FAIL`.
* `2521ec0` — `WideHullValidation.cs` (Task A gates + Task C synthetic
  proof), wired into `ModularShipSelfTest`; the placeholder transition
  module (`hull.transition.w1r2-w2r1.v1.json`); gate count bumped to 16;
  this document. `127 PASS, 0 FAIL`.
