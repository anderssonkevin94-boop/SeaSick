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

## 8. Data + art results (Claude/Opus 2026-09-25, second pass, worktree SeaSick-modular)
Status: **done, awaiting Kevin's in-game look.** The first pass (3e4fc44) passed its topology
verifier but failed visually (wb a featureless slab with the stair kit ~20 u away in the wrong
frame; wa an open shell with a main-deck rail). Rebuilt from scratch; every claim below was
checked on a render.

**Method** (`tools/blender/modular_raised_sections_v1.py`, no booleans). Import frame proven by
opening both source .blends directly (stern root x 0, middle root x 9.3, bow root x 15.3; the
connected middle's parts match `middle-isolated.png`). Astra's forward wall is baked into her
raised-stern `Hull_Shell`: bulkhead at stern x **9.26** (0.04 inside the 9.30 face), stair notches
|y| 4.62-5.80 x 6.06-9.26, a sloped companion tunnel from the door to a hatch room open to the
deck. Wall piece = her stern shell for stern x >= 6.0, z >= 1.70, translated by (6.0 - 9.3);
aft wall = that piece mirrored (x' = 6 - x). The middle's connected shell supplies the lower hull
(z <= 1.70) and the plain upper box; pieces welded with Astra's `combined()` weld (T-joints split).
Fixes needed on the way: her topsides tumble in aft of stern x 6.55 (|y| 6.02 at x 6.0) -> squared
to 6.04 in the piece; her topside vertex colour is darker than the middle's -> repainted with the
middle's colour ramp; the hatch room/tunnel replaced by a straight 0.8 u door alcove and the deck
opening planked over (**no hatch in a middle**: it sat at local x 3.3-4.0, where the chimney lands on
a one-middle ship). Rails, posts, deck/side/bulkhead plank seams are regenerated per variant (deck
lanes stop at the notches); stairs, stair guards, nosings, wall guard, door frame and door are her
meshes re-based; the door keeps its hinge pivot as the object transform.

**Stair layout.** Stairs rise from the low deck (1.76) at the wall face to the upper deck (4.20)
3.2 u inboard, in both outboard corners. wf: local x 2.76-5.96; wa: 0.04-3.24; **wb: stairs FORWARD
only** (two 3.2 u runs do not fit 6 u), aft wall door-only (her piece with the notches capped flat,
continuous guard rail). Wall kit x-extents: inside [0, 6] except the wall guard rail cap (-0.10 /
6.10, centred on the wall) and the open door leaves (1.3 u outside the face, as Astra authored).

**Renders** (`tools/blender/render_raised_sections_v1.py`, art-staging/modular-raised-sections-v1/):
per variant `<Label>/threequarter-fwd|aft.png, side.png, top.png`, `SternWF|BowWA/threequarter.png`;
assemblies `assembled-{a,b,c,d}-{threequarter,threequarter-aft,side,top}.png` with (a) SternWF + low
middle + BowWA, (b) low stern + wb + low bow, (c) connected raised stern + wf + low middle + low bow,
(d) low stern + wa + connected raised bow. Low W1x pieces come from their source
`modular-width-inserts-v1/expanded-ship.blend` (main tree, read-only; same geometry as
HullW1x_v1). Welded Hull_Shell of all four ships: **0 open / 0 overconnected / 0 degenerate**.

**Hydrostatics** (`Hydrostatics/HullW1xRSections_v1/*.json`, module-local shell, deck_z 4.20):
| module | V(1.76) vs W1x | V(4.20) U^3 |
|---|---|---|
| MiddleWF / WA | 254.067 vs 254.287 (0.086%) | 409.205 |
| MiddleWB | 254.028 vs 254.287 (0.102%) | 405.699 |
| SternWF | 333.384 vs 333.662 (0.083%) | 566.760 |
| BowWA | 227.646 vs 227.739 (0.041%) | 413.954 |

**Mass / capacity** (measured on the shell ABOVE 1.76 -- the w1xr.v1 modules counted walls from 0.84,
so they double count the W1x band; these do not): deck 110, walls+bulkheads+notch walls 95 kg/m^2,
60 kg/flight. upperStructure / lightship / hold / berths: wf, wa 4135.5 / 17399.9 kg / 10 / 6;
wb 4928.4 / 18192.8 / 10 / 6; SternWF 6794.4 / 24199.2 / 13 / 4; BowWA 4849.6 / 16729.1 / 9 / 3.

**Gun slots dropped** (clearance box vs stairwell, re-derived by the verifier): wf DeckSlot_1 pair
(keeps DeckSlot_0); **wa both pairs (none left)** -- DeckSlot_1's box starts at x 2.6 < 3.24;
wb DeckSlot_1 pair (keeps DeckSlot_0; the aft door wall has no stairwell); **SternWF DeckSlot_2
pair (none left)** -- box to x 6.4 > 6.06; BowWA keeps all four.

**Verifiers.** `tools/verify_raised_modules.py` 133/133 PASS (all five: face standards, drop set ==
stairwell hits, passages clear of stair corners, mass arithmetic, table gate <= 0.5 %, sha, FBX
presence, shell and assembly topology, wall-kit extents). `tools/blender/verify_modular_raised_sections_v1.py`:
74/74 FBX round-trip PASS.

**Open.** (1) Hatch on Astra's ends is pitched -80 deg about Y; VisualPart has yaw only, so the pitch is
baked into the exported hatch mesh (pivot recorded) -- the older hull.stern/bow.w1xr.v1 JSONs put
-80/+80 into yawDegU, which rotates about the wrong axis in game. (2) wa and SternWF carry no guns.
(3) Doors are open-posed leaves sticking 1.3 u into the low neighbour's deck (Astra's authoring).
(4) No .meta files (Unity not launched). (5) Chimney must still avoid Astra's end hatches (C# rule).

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

## 9. C# implementation (Claude/Opus, 2026-09-25, third pass, worktree SeaSick-modular)
See docs/SHIPYARD-API.md §17 for the file-by-file summary. Done: item 1 (the
id mapping, `RaisedSections.cs`, 60/60 combinations gated against the real
assembler), item 2's physics (per-section `RaiseDeck`, mass/CoM already
per-section via the existing `upperStructure` summing, chimney X/Z), item 5
(the stale module-count gates now derive from the loaded library).
`tools/modular-selftest.sh`: 189 PASS, 0 FAIL.

**Not done, this session** (budget/scope):
- Item 3 (art/data fixes): the `VisualPart` rotation field for the connected
  raised stern/bow hatches, and the wall-count-from-1.76 offset note. Neither
  Blender nor Unity ran this session; a blind JSON+schema edit to a rotation
  axis with no way to render and check it risked costing Kevin more
  debugging time than it saved.
- Item 4 (UI): the shipyard's tap-per-section toggle. `RaisedSections.cs`
  (`ToIds`/`FromIds`) is written so this is now mostly plumbing --
  `ShipyardDraft` reads `FromIds` off the current config, a tap flips one
  section's `DeckLevel`, `RecomputeIds` regenerates all three ids, and the
  draft re-assembles -- but `ShipyardScreen`'s actual tap targets and
  disabled-reason strings were not written.
- Item 6 (probes): `ShipyardRefitProbe`, `ShipyardUiProbe`,
  `ModularShipPreview` were not extended, same reason as §11's own "not
  done" in docs/RAISED-DECK.md -- they need Play mode/Editor state this
  worktree's headless harness cannot exercise, and Unity was never launched.
- The chimney/stairwell overlap check (sec 5's last sentence) -- the X/Z
  positioning itself is correct (§17), but nothing checks a chimney against
  a stairwell's own footprint or moves it aft.
