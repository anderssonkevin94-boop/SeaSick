# W1x expanded-hull validation

Branch `expanded-hull` (worktree `SeaSick-expanded`, from `modular-ships`
a6c29f0 — guns as explicit equipment, dry dock, per-gun battery), 2026-09-25.
Validates Astra's EXPANDED hull family kit
(`art-staging/modular-width-inserts-v1`, standard `"W1-center-expansion-r1"`)
against the modular-ship module contract (`docs/MODULAR-SHIPS.md`,
`docs/SHIPYARD-API.md`), then wires it in. Headless gates:
`ExpandedHullValidation.cs`, added to `ModularShipSelfTest.RunWith` and run
by `tools/modular-selftest.sh`. Numbers below were independently derived
from the kit's own files (`manifest.json`, `hydrostatics/*.json`) with
Python and Blender in `--background` mode (never the editor), then
re-verified live by the headless gates against the committed module JSON.
**Result: PASS on every point.** Wired into the prototype shipyard (Task B).

This family is a **different thing** from the earlier `wide-hull` branch's
W2-r1 kit (`art-staging/modular-hull-wide-v1`): W1x **widens W1-r2 in
place** (port/starboard halves moved outboard + insert strips closing the
gap) — length and depth **unchanged**, beam 9.28 → 12.08. W2-r1 was an
**independently modelled**, wider **and deeper** hull (beam 11.6, keel
−2.75). Per Astra's instruction this session, W1x **supersedes** W2-r1 as
the width upgrade; no W2-r1 data, tables, meshes, depth or exposure appear
here, and standard W1-r2 is untouched.

## PASS/FAIL table

| # | Check | Result | Key numbers |
| - | --- | --- | --- |
| 1 | Interface profile equality | **PASS** | Stern fwd == Middle aft == Middle fwd == Bow aft, bit-for-bit (max diff 7.2e-15 u²) across all 66 waterlines, deck-level area 42.381 u². Half-beam 6.04, deck 1.76, keel −1.92 (deck/keel **identical** to W1-r2 — only beam differs). Mixed W1-r2/W1x configs refused, three ways: W1-r2 stern + W1x middle, W1x stern + W1-r2 middle, and a direct W1x stern → W1-r2 bow join (no middle). |
| 2 | Sockets/lengths, multi-part parts | **PASS** | Stern 9.30, middle 6.00 (repeats), bow 10.98 — bit-for-bit W1-r2's own lengths. Wheel axle stern-local (0.72, 0, 0.35), M1 rotor fits, M1-L refused, chimney assembled-midpoint at Z 1.76. All 50 wired visual parts (23 stern + 11 middle + 16 bow) carry the manifest's own `local_position` in the new `VisualPart.localPositionU` field, verified live against the loaded module JSON. `Standard_Bow_Halves` — **not needed**, not wired in (see §2 below). |
| 3 | Hydrostatic tables | **PASS** | Schema matches the W1-r2 loader exactly; `sourceGeometrySha256` in each module JSON == the table file's own field (all three, byte-for-byte); valid range −1.92..1.76 on all three — **identical to W1-r2's**, confirming depth is unchanged. Monotonic (enforced by `HydroTable.Valid()`). Independent cross-check: Midship's deck-level area (constant to 1e-13 across all 99 stations — a genuinely prismatic bay) × 6.00 u = 254.2867 u³ vs the table's own 254.2866 u³ — **0.00003%**, far inside 2%. |
| 4 | Gun slots + passages, real cannon | **PASS** | All 10 kit deck-slot empties (stern 2, middle 4, bow 4) pass the assembler's own clearance/passage test against the real cannon (`art-staging/cannon-astra-v1`, 1.22 × 2.1816 × 1.2756), at the DERIVED expanded positions (§4 below). Passage exclusion proven live (`w1x-real-cannon-in-passage-rejected`). |
| 5 | Capacity + mass, assembly, reshape, wheel dip | **PASS** | See below. |

`tools/modular-selftest.sh`: **`ModularShipSelfTest: 134 PASS, 0 FAIL`**
(104 pre-existing milestone-1 + shipyard gates, unchanged in substance — 4 of
their count/allow-list assertions updated for the new module count and W1x
allow-list entries — plus 30 new `ExpandedHullValidation` gates).

## 1. Interface profile

The kit ships no `interfaces.json` point-loop (unlike the W2-r1 kit), so
the profile-equality check uses the richer per-x sectional-area data each
`hydrostatics/*.json` carries (a `"stations"` array of `{xU, areaU2[66]}`,
outside the runtime `HydroTable` schema, which only reads the flat
`waterlineZU`/`integratedVolumeU3` pair). Computed with Python, outside
this runtime: the deck-level (last-waterline) area at Stern's forward-most
station (x=9.300), Midship's aft (x≈0) and forward (x=6.000) stations, and
Bow's aft station (x≈0) are **bit-for-bit identical** across all 66
waterlines (max |diff| 7.2×10⁻¹⁵ u², floating-point noise) — 42.381 u² at
the deck. `standards.json` gained a `"W1x"` `joinProfiles` entry (half-beam
6.04, deck 1.76, keel −1.92, `profilePoints` left **0**: this kit supplies
no loop-point data, unlike W2-r1's `interfaces.json`; the field is
informational only, read by no code path).

Because `"W1x"` is a different id (and a different `halfBeamU`) from
`"W1-r2"`, `ShipAssembler`'s existing per-socket standard-equality check
refuses any direct join, automatically — no new assembler or policy code.
Proven three ways: `w1r2-stern-w1x-middle-rejected` (W1-r2 stern + W1x
middle), `w1x-stern-w1r2-middle-rejected` (the mirror), and
`w1x-stern-w1r2-bow-direct-join-rejected` (a W1x stern joined straight to a
W1-r2 bow, no middle at all — the zero-middle case the one-consistent-width
rule must also cover). All three `JOIN_PROFILE_MISMATCH`.

## 2. Sockets, lengths, multi-part parts, Standard_Bow_Halves

Manifest lengths match W1-r2 exactly: Stern 9.30 u, Midship 6.00 u
(repeatable), Bow 10.979999... u (10.98). Wheel axle stern-local
(0.72, 0, 0.35); chimney placement rule/Z unchanged. The kit's own words
("wheel, carrier, housing, helm and chimney not scaled") are verified, not
trusted: the STERN module's `Carrier`/`Rotor` manifest entries sit at
exactly the wheel socket (0.72, 0, 0.35) and were **excluded** from the
wired module (the existing `wheel.rotor.m1.*`/`wheel.carrier.m1` modules
are reused unchanged, same as every W1-r2 ship) — including them would have
duplicated the wheel visually. `Housing_*`/`Helm` triangle counts (648,
472, 142, 280) match the manifest exactly (Blender re-import, below) and
Astra's own `fbx-roundtrip-check.txt` independently states the same
("housing, helm, chimney... coordinates unchanged").

**Multi-part parts.** Unlike W1-r2's one-whole-mesh-per-visual kits, this
kit splits several visuals into Core/Port/Starboard/Insert pieces, each
with its own `manifest.json` `local_position` (Blender axes) — e.g. the
stern's outer shoulders (`Port__Hull_Shell` etc.) are the *original*
W1-r2 piece translated +1.4 u in Y (README: "port/starboard halves move
outward 1.4 units each"); the bow's Port/Starboard pieces are *authored
counterparts* with `local_position` **zero** (README: "not purely
translated standard halves" — the taper is baked into the mesh). This is a
schema gap the existing `VisualPart` (one `resourcePath`, always at the
module origin) cannot represent, so `VisualPart` gained a
`localPositionU` field (Vector3, default zero — every existing single-piece
visual reads unchanged) and `ModularShipView.Build` now offsets each part
by it. Gate `w1x-multipart-visuals-local-position-matches-manifest` checks
all 50 wired parts' `localPositionU` against the manifest, live, against the
LOADED module JSON (not the source manifest a second time) — 23 stern + 11
middle + 16 bow, Carrier/Rotor excluded from the stern's 25.

**Blender re-import (calibration, existence, triangles).** Blender 5.1.1,
`--background --factory-startup`, `import_scene.fbx` default settings.
Calibrated first against the ALREADY-DEPLOYED `HullW1r2_v3/Stern_W1` FBXs
(known-good `boundsMinU`/`boundsMaxU`): the importer applies a 90°-about-Z
rotation relative to the kit's stated authoring axes, `authored.x =
-measured.y, authored.y = measured.x, authored.z = measured.z` — the SAME
convention the `wide-hull` branch found for its own (different) kit,
re-derived independently here rather than assumed. All 61 files the kit
lists on disk exist (50 wired hull parts + the stern's Carrier/Rotor,
verified but not wired — reused unchanged, see below — + the 8-part
`Standard_Bow_Halves` set + the shared `Fittings/Chimney.fbx`, neither
of the last two wired in either — see below). Of the 52 parts the kit's
own `manifest.json.modules` block declares (the 50 wired hull parts plus
Carrier/Rotor), **every one's triangle count, re-imported and
re-triangulated in Blender, matches the manifest's declared count exactly,
with zero exceptions**; `Standard_Bow_Halves` and the shared chimney
(neither referenced by `manifest.json.modules`, both unwired — see below)
were not independently re-triangulated. Re-importing the
individual **Port**/**Starboard** `Hull_Shell` pieces in isolation,
however, found their measured X-ranges disagreeing with each other by
exactly 2.8 u (the added-beam figure) — a per-file FBX axis-metadata
inconsistency in those two files, not a real geometry difference (a pure
Y-translation cannot change X range; confirmed physically implausible, and
inconsistent with the chimney cross-check below, which only reproduces the
kit's own number if lengths are unchanged). **`boundsMinU`/`boundsMaxU` in
the three module JSONs are therefore DERIVED, not Blender-measured**: X/Z
carried over from W1-r2 (justified independently by the chimney-midpoint
proof below, not just the README's claim), Y = half-beam 6.04 + the same
0.21 u rail overhang W1-r2's own bounds already show. Flagged in each
module's `boundsNote`.

**Independent length proof.** The kit's `manifest.json.fittings.chimney_
position` is `[13.14, 0, 1.76]` for a one-middle-bay assembly. Running the
"assembled-midpoint" rule on `hull.stern.w1x.v1` + `hull.middle.w1x.v1` +
`hull.bow.w1x.v1` reproduces X 13.14 **exactly** (gate
`w1x-chimney-midpoint-matches-kit-manifest-and-todays-long`) — which is
also bit-for-bit today's W1-r2 Long chimney position (6.57 m at 0.5 m/u).
This independently confirms the expanded hull's assembled LENGTH is
unchanged, using Astra's own manifest number, not just the README's prose
claim.

**`Standard_Bow_Halves` — not needed, not wired in.** The kit's
`bow_rule` field says: *"Select Standard_Bow_Halves for standard width,
authored expanded Bow_W1 halves for expansion."* This folder (8 FBX,
`Bow_W1__Port/Starboard__*__Standard.fbx`) is a **fallback set for
reconstructing a standard-WIDTH bow on the same file rig** — e.g. if a
future tool wants to build a W1-r2-width bow interchangeably with this
kit's Insert/Core convention. `hull.bow.w1x.v1`'s own `parts[]` list never
references it, and the EXPANDED bow already has its own (authored) Port/
Starboard halves. Not copied into `Resources/`.

## 3. Hydrostatic tables

`HydroTable` (`ShipHydrostatics.cs`) reads the kit's `hydrostatics/*.json`
unmodified — same fields as W1-r2's own tables (`schemaVersion`,
`sourceGeometrySha256`, `validWaterlineZU`, `waterlineZU`,
`integratedVolumeU3`; extra fields such as `stations`/`method`/`limits` are
present but ignored, exactly like W1-r2's committed tables). Copied
verbatim into `Resources/ShipModules/Hydrostatics/HullW1x_v1/`.

`sourceGeometrySha256` in each `hull.*.w1x.v1.json`'s `hydrostatics` block
== the table file's own field, for all three sections (checked by Python
before committing, and by `ModuleLibrary.LoadHydrostatics`'s own hash check
at load time — gate `w1x-hydro-tables-load-and-hash-match`). 66 waterlines
each (Stern 203 stations, Midship 99, Bow 317 — kit `manifest.json`
`stationCount`). Volume strictly monotonic (`HydroTable.Valid()`). Valid
range **−1.92..1.76 on all three — bit-for-bit W1-r2's own range**
(gate `w1x-hydro-valid-range-unchanged-from-w1r2`), independently
confirming the kit's "depth does not change" claim from the table data
itself, not just the README.

**Independent cross-check.** Midship's deck-level sectional area is
**constant to 1×10⁻¹³** across every one of its 99 stations (x = 0 to
6.00 u) — i.e. the bay is genuinely prismatic, as its "repeatable 6.00 u
bay" description implies. One station's deck area (42.381151... u², at
x=3.0) × 6.00 u = 254.2867 u³ vs the table's own integrated deck volume,
254.2866 u³ — **0.00003%**, comfortably inside the 2% bound (gate
`w1x-midship-deck-area-cross-check-within-2pct`).

## 4. Gun slots + crew passages

Astra's accepted provisional STANDARD layout (this session's brief):
passage half-width |y| ≤ 2.30 u, gun clearance 2.3 u wide, current gun
centres (Y = ±3.45 on every W1-r2 section). **Derivation chosen for the
wider deck: same passage half-width, gun slots move OUTBOARD with the deck
edge by +1.4 u** (Y = ±4.85), **not** widening the passage.

**Justification.** The kit's own design (§1/§2 above) adds the extra 2.8 u
of beam entirely OUTBOARD of the original W1-r2 centreline: the Core
piece (stern) and the centreline crew corridor are untouched; the
Port/Starboard shoulders (and, on the stern/middle, their attached deck-slot
sockets) move outward by exactly 1.4 u per side, the SAME amount the hull's
own outer shell moved. Moving the gun slots by that identical 1.4 u keeps
them anchored to the deck edge geometry they were originally authored
against, and — critically — **never narrows the crew passage** (Astra's
rule): the passage stays at its existing, already-verified width. The
alternative (widening the passage to track the beam) was rejected: nothing
requires the crew corridor to widen just because the gun deck did, and
keeping it at its proven width avoids re-opening the passage-tuning
question (open question 3, `docs/MODULAR-SHIPS.md`) for a section that
did not change centrally.

**Consequence, checked not assumed.** The slot clearance box (1.8 × 2.3 ×
1.65, unchanged size, same as every real-cannon W1-r2 slot) at Y 4.85 has
its inner edge at 4.85 − 2.3/2 = **3.70**, clearing the unchanged passage
half-width (2.30) by **1.40 u** — a generous margin, unlike W1-r2's own
design where the two figures are EQUAL (zero gap, "touch, not overlap";
`docs/SHIPYARD-API.md` §14). Gate `w1x-passage-gap-is-generous-not-zero`.
All 10 slots (stern DeckSlot_2 pair, middle DeckSlot_0/1 pairs, bow
DeckSlot_0/1 pairs) pass the assembler's own clearance+passage test with
the REAL committed cannon module (`equipment.cannon.astra.v1`, already in
the shipyard — no synthetic footprint module was needed, unlike the
`wide-hull` branch's validation, since guns are explicit equipment at this
branch's base) — gate `all-10-w1x-deck-slots-pass-real-cannon`. Negative
control: nudging a cannon into the passage via the middle bay's free
`DeckArea` is still refused `EQUIPMENT_BLOCKS_PASSAGE`
(`w1x-real-cannon-in-passage-rejected`) — the passage is proven to still
bind, not just left alone.

The bow's DeckSlot_0/1 (X 1.5/3.5) sit close to the aft join, where the
hull is at full beam (the taper toward the stem starts further forward,
past X ≈ 8.4); this mirrors how W1-r2's own bow slots are already treated
(provisional, not kit-measured) and is flagged the same way.

## 5. Capacity + mass, assembly, reshape factors, float, wheel dip

**Mass method.** Two candidates, same choice `wide-hull` made and for the
same reason:

| Method | Long ratio (expanded/W1-r2) | Long lightship | Draft as % of keel-to-deck depth |
| --- | --- | --- | --- |
| Hydrostatic-table volume-to-deck (chosen) | 815.687 / 611.671 = **1.3335** | 42 548.7 kg | **45.0%** |
| Geometric beam ratio (sB) | 12.08 / 9.28 = 1.3017 | 41 533.7 kg | ~44.1% (not used) |

W1-r2 Long floats at **45.9%** of its own keel-to-deck depth at lightship
(0.844 m of 1.84 m). The volume-ratio mass reproduces that fraction almost
exactly (45.0%); a naive beam-ratio mass would be visibly lower. **Volume
ratio chosen.** The two ratios differ (1.3335 vs 1.3017) because the real
insert geometry is not an affine width-scale of the original cross-section
— an expected, not contradictory, finding: `HullMeasure`'s purely
GEOMETRIC `sB` (used to reshape the PHYSICS hull, `HullFormData.Reshaped`)
and this hydrostatic-table volume ratio (used only for mass/capacity
SEEDING) are deliberately different numbers for different purposes, same
separation `wide-hull` documented for its own kit.

Per-module split: each section's own share of the Long total's table
volume-to-deck (`lightship.rule`, cited per file).

| Module | Table deck volume (u³) | Lightship (kg) | Hold cells | Berths | Gun slots designated |
| --- | ---: | ---: | ---: | ---: | --- |
| `hull.stern.w1x.v1` | 333.66 | 17 404.8 | 9 | 2 | DeckSlot_2 pair |
| `hull.middle.w1x.v1` | 254.29 | 13 264.4 | 7 | 5 | DeckSlot_1 pair |
| `hull.bow.w1x.v1` | 227.74 | 11 879.5 | 6 | 2 | DeckSlot_1 pair |
| **Short (stern+bow)** | 561.40 | **29 284.3** | **15** | **4** | 2 pairs / 4 guns |
| **Long (+1 middle)** | 815.69 | **42 548.7** | **22** | **9** | 3 pairs / 6 guns |

Hold cells: `round(section table volume-to-deck / W1-r2's u³-per-hold-cell
(611.671/16 = 38.229))`. Berths: `floor(W1-r2 counterpart berths × beam
ratio (12.08/9.28 = 1.302))` — stern/bow both `floor(2×1.302)=2`
(unchanged), middle `floor(4×1.302)=5`. Gun slots: the SAME designated
pair W1-r2's own module uses (DeckSlot_2 on the stern, DeckSlot_1 on
middle/bow), moved outboard per §4, chosen for continuity with an existing
ship rather than re-derived from scratch — no wider ship exists yet to
match a real gun position to. All marked `provisional` with a `source`
line, same convention as W1-r2.

**Assembly, measurement, reshape factors.** Both `ExpandedShort()` and
`ExpandedLong()` assemble (`w1x-short-and-long-assemble`). `HullMeasure` on
Long vs the reference (W1-r2 Long): **sL = 1.0000** (lengths unchanged —
confirmed both by the manifest's own per-module `length` fields and the
chimney cross-check above), **sB = 1.3017** (2×6.04 / 2×4.64, matches the
predicted 12.08/9.28 exactly — this is the GEOMETRIC join-profile ratio,
distinct from the 1.3335 volume-ratio used for mass, §5 above), **sD = 1**
(keel/deck unchanged) — gate `w1x-reshape-factors`.

**Float check** (tables, `AssemblyHydrostatics.SolveWaterline`, density
1025 kg/m³). Both Short and Long float within keel..deck at lightship AND
at full load (hold + berths manned + guns, `WeightModel.Default`: 500 kg
cargo unit, 90 kg crew, 500 kg/1 crew per gun — matching
`equipment.cannon.astra.v1`'s own authored `massKg`/`crew`):

| Config | Mass (kg) | Waterline Z (u) | Draft (m) | % of depth |
| --- | ---: | ---: | ---: | ---: |
| Short, lightship | 29 284.3 | −0.236 | 0.842 | 45.8% |
| Short, full load | 39 144.3 | 0.239 | 1.080 | 58.7% |
| Long, lightship | 42 548.7 | −0.264 | 0.828 | 45.0% |
| Long, full load | 57 358.7 | 0.231 | 1.075 | 58.4% |

(Full load = lightship + hold cells×500 + berths×90 + guns×500.) No
`OVERLOADED` in either case — gates `w1x-short-floats-lightship-and-full-
load`, `w1x-long-floats-lightship-and-full-load`.

**Wheel-dip.** Wheel/rotor are UNCHANGED (§2): axle Z 0.35, nominal rotor
radius 1.62 (reinforced M1, `standards.json`), so wheel bottom = 0.35 −
1.62 = **−1.27 u**, the SAME threshold as today's W1-r2. Every one of the
four waterlines above (−0.264 to 0.239) sits well above −1.27, so the
wheel dips comfortably in all of them — gate
`w1x-wheel-dips-at-lightship-and-full-load`. (Sanity check: this same
solver, run on W1-r2's own committed tables at today's 31 906.6 kg,
reproduces the already-published 0.844 m draft to 3 decimal places — the
unit-conversion bug this caught during development, before the fix, gave
0.141 m; fixed and re-verified against that published figure before
trusting any expanded-hull number.)

**Lightship vs today's Long.** 42 548.7 kg vs 31 906.6 kg — **+33.4%**,
matching the chosen volume ratio (1.3335) exactly by construction.

## What I still need to do in Unity (not done here — no Unity was launched)

1. Open the project, let it import the 50 new W1x FBXs (no `.meta` files
   committed — Unity generates them) and confirm
   `ModularShipModelImport`'s `AssetPostprocessor` applies the same
   settings W1-r2's meshes get.
2. `unity cmd eval --json --code 'return
   SeaSick.Ship.Modular.ModularShipSelfTest.Run();'` to also exercise
   `visual-parts-resolve` against the real imported `Resources.Load`
   (headless only checks the FBX exists on disk).
3. **Visually confirm `localPositionU` is applied correctly** — this is
   the one piece of this delivery that genuinely could not be checked
   without rendering: the offset math is unit-tested
   (`w1x-multipart-visuals-local-position-matches-manifest`,
   `ModularScale`'s existing axis-conversion tests) but the multi-part
   assembly (Core + translated Port/Starboard + Insert strips closing the
   gap) has never been drawn. Menu **SeaSick/Modular/Create Test Scene**,
   Play, look at an `ExpandedShort()`/`ExpandedLong()` ship (`ExpandedPresets.cs`)
   next to a W1-r2 one.
4. Confirm the derived `boundsMinU`/`boundsMaxU` Y values (6.25, not
   Blender-measured — §2) don't visibly clip the placeholder-box fallback
   if any part fails to import.
5. A sea trial (float on the water, not just the table solve above) —
   propose the cost before running, per "Consult before expensive runs".

## Commits

* Meshes/hydrostatics copy + module JSONs (`hull.{stern,middle,bow}.w1x.v1.json`),
  `VisualPart.localPositionU` + `ModularShipView` offset, `standards.json`
  `W1x` join profile, `ExpandedPresets.cs`, `ShipyardPolicy` allow-list +
  family check.
* `ExpandedHullValidation.cs` (Task A gates), wired into `ModularShipSelfTest`;
  gate-count updates; this document; `docs/SHIPYARD-API.md` §9 +
  `AllowedModuleIds` row; `docs/MODULAR-SHIPS.md` §3/§13.

(Exact hashes: see `git log --oneline` on this branch — commits are ordered
so each one keeps the selftest green, per the task's rules.)

## Send to Astra

* **The `modular-width-inserts-v1` kit (validated here) PASSES every
  contract check.** No changes needed from you on the geometry, hydrostatics
  or manifest data itself.
* **Two of your kit's own FBX files (`Stern_W1__Port__Hull_Shell.fbx` and
  `Stern_W1__Starboard__Hull_Shell.fbx`) disagree with each other on their
  own X-extent by exactly 2.8 u** when re-imported in isolation via Blender
  `--background` (see "Blender re-import" above) — almost certainly a
  per-file FBX axis-metadata inconsistency from the export step (a pure Y
  translation cannot change X range), not a real geometry difference; I
  worked around it by deriving bounds analytically instead of trusting a
  per-part Blender AABB, but if you re-export those two files with the same
  axis metadata as their siblings it would let a future validation measure
  bounds directly instead of deriving them.
* **`profilePoints` for the new `W1x` join profile is 0** (unmeasured) in
  `standards.json` — this kit doesn't ship an `interfaces.json` point-loop
  the way `modular-hull-wide-v1` (W2-r1) did. It isn't read by any code
  path today, but if a future check ever wants it, that's the field to
  fill in.
* **`Standard_Bow_Halves` is confirmed not needed** for this delivery (see
  §2) — the expanded bow's own Port/Starboard halves are used throughout.
  If that folder was meant to support a DIFFERENT future use (e.g. mixing
  standard- and expanded-width bows on the same rig), let me know what
  that use is; nothing in this kit's own manifest or README pointed to one.
* Per Kevin's decision this session, **`W1x` supersedes `W2-r1`/`wide-hull`
  as the width direction to build on.** `wide-hull`'s branch/worktree and
  its validated W2-r1 data are left untouched (not merged, not deleted) —
  Kevin's call on what happens to that branch.
* **Raised decks stay out of scope** here, per instruction, but I've
  recorded your stated future rules in `docs/MODULAR-SHIPS.md` §13 (reject
  raised ends with no middle between them; never combine the continuous
  raised-middle style with the older stair/drop-edge ends; raised ends
  separated by a LOW middle are a valid partial-deck option pending a
  traversal check) so the next person who picks up raised decks has them
  in writing.
