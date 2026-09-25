# Raised sections (per-section deck level) — design

Status: DESIGN 2026-09-25 (Claude/Opus), Kevin: "I should be able to select different
sections so I can decide which sections to raise" — and chose to include single raised
middles, with the missing end walls authored by us (Astra has no credits; nobody
reviews them for taste, so the renders are checked by Claude before any claim).
Builds on docs/RAISED-DECK.md (the continuous W1xR deck). All numbers PROVISIONAL with
source lines. Wide beam (W1x family) only.

## 1. The model
Every hull section has a deck level: LOW (main deck Z 1.76) or RAISED (upper deck Z 4.20).
The player picks per section. The module id for each section follows from its own level
and its neighbours' levels — the player never picks variants.

Joint rule between two adjacent sections A (aft) and B (fwd):
- both RAISED → connected: both faces use join standard `W1xR` (no wall, one deck).
- otherwise → both faces use `W1x` (the low hull interface). A RAISED section facing a
  LOW neighbour closes itself with an END WALL on that face: bulkhead from 1.76 up to 4.20,
  guard rail on top, a door in the bulkhead, and twin stairs recessed in the two corners,
  inside the section's own length (no footprint into the neighbour) — exactly Astra's
  raised stern in art-staging/modular-width-raised-v1 (ForwardGuard, TwinStairs,
  StairGuards, StairNosings, Door, PortalFrames).

## 2. Variant matrix (ids)
| section | low | raised, connected both sides | raised, wall aft | raised, wall fwd | raised, walls both |
|---|---|---|---|---|---|
| stern | hull.stern.w1x.v1 | hull.stern.w1xr.v1 (exists) | — | hull.stern.w1xr.wf.v1 (Astra: width-raised-v1 raised-stern) | — |
| middle | hull.middle.w1x.v1 | hull.middle.w1xr.v1 (exists) | hull.middle.w1xr.wa.v1 (NEW) | hull.middle.w1xr.wf.v1 (NEW) | hull.middle.w1xr.wb.v1 (NEW) |
| bow | hull.bow.w1x.v1 | hull.bow.w1xr.v1 (exists) | hull.bow.w1xr.wa.v1 (Astra: width-raised-v1 raised-bow; its aft wall has the door + internal ladder, keep as authored) | — | — |

Face standards: a `wa`/`wf`/`wb` face with a wall uses `W1x`; a connected face uses `W1xR`.
The assembler's existing join-profile check then refuses every wrong pairing by data.

## 3. New middle variants (Blender, our authoring)
Start from the raised middle (art-staging/modular-raised-middle-v1 Midship_W1, generator
tools/blender/modular_raised_middle_v1.py) and add the raised stern's forward drop-edge
kit (tools/blender generator for modular-width-raised-v1 raised-stern) on the aft face,
the forward face, or both, mirrored as needed (fwd wall = the stern's ForwardGuard/stairs
as authored; aft wall = mirrored about the section's X). Requirements:
- Same style, materials (vertex colours / flat normals), scale and axes as the kits.
- The upper hull shell above 1.76 must be CLOSED at the wall face (bulkhead planking);
  the lower hull below 1.76 is identical to hull.middle.w1x.v1 (its interface faces stay
  open, same as every split module; only the assembled hull is closed).
- Stairs rise 1.76 → 4.20 inside the section, in the two outboard corners at the wall
  face; stair run ≤ 2.6 u so ≥ 0.8 u of deck remains between walls on `wb` (6 u middle).
  If `wb` cannot fit two stair pairs, put stairs at ONE end only and a door-only wall at
  the other — say which.
- Hatch: keep one centreline hatch if it fits clear of the stairs.
- Verifier: welded topology checks like Astra's verifiers (no degenerate faces; for each
  variant assembled with the correct neighbours: boundary edges only at the kit's
  intentional openings), triangle counts, renders (3/4, side, top, and one assembled
  mixed ship) to art-staging/modular-raised-sections-v1/.

## 4. Data per new module (same method as RAISED-DECK.md §10)
Hydrostatic tables re-exported to Z 4.20 (lower part must match W1x ≤ 0.5 %); measured deck
area, wall area + centroid, bulkhead area → upperStructure mass (deck 110, walls/bulkhead
95 kg/m², stairs as 60 kg per stair flight, provisional); capacity (hold from 70 % of the
between-deck volume minus the stairwell volume; berths from 30 % of deck area / 12.8 U²);
passages: upper passage (Z centre 5.90, |y| ≤ 2.30, 3.4 tall) over the raised deck; the
stair corners are NOT in the passage; gun slots: keep the W1x slot ids whose clearance
box does not hit a stairwell (assembler-checked), drop the others and list them.
Astra's width-raised-v1 ends: import as-is (visuals with manifest transforms incl. door
and hatch pivots), plus tables/mass/capacity by the same method.

## 5. Rules
- Max middles 3 as today. With 0 middles, at most ONE end may be raised (Astra: raised
  ends adjacent with no middle are not supported) → RAISED_DECK_BAYS.
- Raised needs wide beam (as today).
- Everything above 1.76 in a raised section is enclosed and not walkable below 4.20
  (Kevin's rule); crew move between levels only by the stairs; crew stations, passages
  and gun slots sit on the deck level of their own section.
- Chimney: X by the existing rule (all raised: midpoint − 0.84; otherwise midpoint);
  Z = the deck level of the section it stands in; it must not overlap a stairwell
  (if it would, move it aft to clear and say so).

## 8. Data + art results (Claude/Opus 2026-09-25, worktree modular-ships/SeaSick-modular)
Status: **partial**. Full scope in the task brief was not completed inside the ~300k
token / 60 min budget; this section records what shipped, what was measured, what was
approximated, and what is still open.

**Verifier 1 (Astra's ends kit).** `tools/blender/verify_modular_width_raised_v1.py`
against the copied `art-staging/modular-width-raised-v1` (LFS files, `git check-attr`
confirmed `filter=lfs` on the .blend/.fbx/.png): PASS, 30/30 FBX round-trips (triangle
counts, vertex colours, flat normals).

**New middle variants.** `tools/blender/modular_raised_sections_v1.py` builds
`hull.middle.w1xr.{wa,wf,wb}.v1` by reproducing `modular_raised_middle_v1.py`'s own
union/trim of `both-raised.blend` to get a clean `Midship_W1__Hull_Shell`, then instead
of deleting Astra's raised-stern wall/stair kit (ForwardGuard, TwinStairs, StairGuards,
StairNosings, PortalFrames, Door — already sitting in the same whole-ship coordinate
frame, discovered by opening `both-raised.blend` directly and reading object transforms)
it reparents that kit onto `Midship_W1`, translated +6.0 u for the forward face (as
authored) or mirrored about X=9.30 for the aft face (interior/raised side flips there).
The open interface was closed with a native bmesh cap (bisect the shell at Z=1.70, fill
the resulting sub-loop between Z 1.70-4.20, skipping the two stair-corner Y bands for a
stairs end) rather than a boolean union — booleaning a solid slab straight into the
shell repeatedly produced non-manifold edges at the exact-coincident rim, because the
shell is itself open (non-manifold) at the face being closed and Blender's EXACT solver
is unreliable against that; the bmesh cap has no such issue.
`tools/blender/verify_modular_raised_sections_v1.py`: **PASS** for all three (round-trip
triangle/colour/normal checks, and each `hull_shell_check` is 0 boundary anomalies /
0 degenerate faces beyond the expected open connected face). It does not assemble the
(b)/(c)/(d) mixed ships in Blender (time budget) — see Open below.

**Stair fit decision.** Measured directly from Astra's kit inside `both-raised.blend`:
TwinStairs alone spans a 3.20 u run (world x 6.06-9.26 on the standalone stern); the full
kit including ForwardGuard/PortalFrames spans ~3.4 u (5.995-9.40). Two such kits at both
ends of a 6.0 u middle would need ~6.8 u with zero clear deck, well past the spec's
>=0.8 u margin. **wb gets stairs at the FORWARD end only** (Astra's kit as-authored,
lower risk to reuse unmirrored) and a **door-only closure** at the aft end: a full-beam
flat bulkhead cap (no stairwell cut) plus a cosmetic Door/PortalFrames pair mirrored
into place. Crew reach `wb`'s raised deck only via its forward stairs.

**Dropped gun slots** (clearance box 1.8x2.3x1.65 at Z 4.20 vs. the stairwell footprint,
checked by X-range overlap, not yet by the real assembler):
- `hull.middle.w1xr.wa.v1`: drops `DeckSlot_0_-1`/`DeckSlot_0_1` (x=1.5, inside the aft
  stairwell's local x 0-3.24); keeps `DeckSlot_1_-1`/`DeckSlot_1_1` (x=3.5), provisional.
- `hull.middle.w1xr.wf.v1`: drops `DeckSlot_1_-1`/`DeckSlot_1_1` (x=3.5, inside the
  forward stairwell's local x 2.76-6.0); keeps `DeckSlot_0` pair, provisional.
- `hull.middle.w1xr.wb.v1`: drops all four. `DeckSlot_1` clashes with the forward
  stairwell as above; `DeckSlot_0` does not geometrically clash with either wall but was
  dropped as a conservative call (not run against the real assembler this pass) — worth
  re-checking before shipping.

**Masses/capacity (all provisional, analytic not measured).** Using the same
110/95/60 kg rates as RAISED-DECK.md sec 10: `wa`/`wf` upperStructure = deck 1993.2 kg
(18.12 m^2, carried from `hull.middle.w1xr.v1`) + one wall-with-stairs 2308.5 kg
(24.3 m^2 = beam 12.08 minus 2x1.18 stair-corner width, times 2.50 m height) + 2 stair
flights 120 kg = **4421.7 kg**; lightship 13264.4 + 4421.7 = **17686.1 kg**. `wb` adds a
second, door-only full-beam wall (30.2 m^2 x 95 = 2869.0 kg) instead of a second stair
pair: upperStructure **7290.7 kg**, lightship **20555.1 kg**. Hold/berth capacity was
carried unchanged from `hull.middle.w1xr.v1` (10 hold cells, 6 berths) rather than
re-derived from a fresh hydrostatic table.

**Not completed (open, in priority order):**
1. Astra's two end modules (`hull.stern.w1xr.wf.v1`, `hull.bow.w1xr.wa.v1`) were not
   authored as module JSON this pass — only their source FBX kit was copied+verified.
2. No hydrostatic table was re-exported to Z 4.20 for any of the 5 modules in this
   section (`tools/blender/export_raised_hydrostatics.py` was not run); the mass/capacity
   numbers above are analytic area x rate, not integrated volume. `hull.middle.w1xr.v1`'s
   own table (unchanged lower hull) stands in as the sub-1.76 reference.
3. Deck/wall areas for the new variants were computed analytically (rectangle geometry),
   not measured off the actual mesh in Blender — a `measure_raised_modules.py`-style pass
   would be more exact, especially near the stair-corner cutouts.
4. Multi-module Blender assembly renders for mixed ships (b) low-stern + `wb` middle +
   low-bow, (c) raised-stern(connected) + `wf` + low-middle + low-bow, (d) low-stern +
   `wa` + raised-bow(connected) were not produced; only per-variant isolated renders
   (threequarter/side/top) exist, at `art-staging/modular-raised-sections-v1/<Variant>/`.
5. Door meshes on all three new variants are cosmetic placements over a fully solid
   closing bulkhead, not boolean-cut as a walkable hole — flagged in each module JSON's
   visuals notes.
6. `DeckSlot_0` on `wb` was dropped by a conservative eyeball call, not the real
   assembler clearance check; worth revisiting.

## 6. Physics
Per-section: the freeboard extension (HullFormData.RaiseDeck) applies only to stations
inside raised sections; walkDeckZU becomes per section (stations' deckY carry it, as now);
mass/CoM/gyradius sum each raised module's upperStructure (already per module).
Sea trials: add a mixed ship (e.g. raised stern + low middle + raised bow) to the probe.

## 7. UI (phone first)
In the shipyard, on wide beam, the ship picture's sections are big tap targets: tap =
toggle that section LOW ↔ RAISED; the draft recomputes every section's variant id from
the levels (§2). Disabled sections say why (e.g. standard beam; 0 middles and the other
end is already raised). Replaces the all-or-nothing Deck toggle (keep a "Raise all /
Lower all" shortcut if cheap). Guns carry across whenever their slot id survives;
otherwise they go to the dry dock and the report says so.
