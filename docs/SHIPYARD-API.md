# Shipyard API (prototype) — for Astra's UI

Branch `modular-ships`, 2026-09-24, equipment + dry dock added 2026-09-25.
Backend for the first playable shipyard: low-deck W1-r2 ships, 0–3 middle
bays, timber or reinforced M1 wheel, deck cannons as real equipment. The
backend computes everything (dimensions, displacement, capacity,
compatibility); the UI only displays it. Namespace `SeaSick.Ship.Modular`.

## 1. The adapter your staged UI calls (A8)

`ShipyardRefitAdapter` has exactly the three methods of your
`SeaSick.UI.ModularYard.IShipyardRefit`. It does **not** reference your
interface; add `: IShipyardRefit` when you integrate.

```csharp
var yard = new ShipyardRefitAdapter();              // uses ShipyardService.Player each call

ShipConfiguration ReadCurrent();                     // a COPY of the live ship's configuration
string Validate(ShipConfiguration draft);            // null/"" = can be applied now; else one line per reason.
                                                     // Read-only. Cached by (draft JSON + live ship state):
                                                     // safe to call every 250 ms.
bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason);
                                                     // applies + saves; false = nothing changed (reason says why)
ShipyardReport Report(ShipConfiguration draft);      // the rich current-vs-proposed display (below)
```

Modal input blocking (gameplay half; call it from your `setWorldInputBlocked`):

```csharp
ShipyardSession.SetWorldInputBlocked(bool blocked);  // helm, chase-camera zoom, island camera, combat lock key, broadsides
bool ShipyardSession.WorldInputBlocked { get; }      // read this from UI-side world pickers
```

Ship-object events (bind your existing UI readers here):

```csharp
public static event Action<GameObject, GameObject> ShipyardService.PlayerShipReplaced; // (old, new)
public event Action<ShipConfiguration> ShipyardService.Refitted;                        // copy of the new config
```

`PlayerShipReplaced` fires after every successful refit and after a save's
configuration is applied. In this build the ship is rebuilt **in place**, so
`old == new` (see §10 for why); a reader that rebinds on it keeps working if
a later build does replace the object. Statics are reset each play.

No time-scale change. **UI files that should also check `WorldInputBlocked`**
(yours; not edited): `UI/Sheets/WorldPicker.cs` (add to `Busy()`),
`UI/Hand.cs` (`PickAt`), `UI/CampSiting.cs`, `UI/WallSiting.cs`
(`HandCursor.cs` follows `Hand`).

## 2. The service (`ShipyardService`, on the player's steamer)

`ShipyardService.Player` is the handle (null on the ladder ship). Everything
the adapter does goes through it.

| Member | Semantics |
| --- | --- |
| `static ShipyardService Player` | The player's shipyard; null when she is not the steamer. |
| `ShipConfiguration Current` | A copy. Mutating it changes nothing. |
| `bool ModularActive` | False = the untouched standard steamer (V8 art). True once refitted or loaded from a save with a configuration. |
| `ShipyardValidation Validate(ShipConfiguration draft)` | Pure. `ok`, `issues` [{code, message, partId}], `assembly`, `hullLengthM`, `overallLengthM`, `middleSections`, `rotorId`, `capacityCurrent/Draft`, weight allowances. |
| `ShipyardReport Report(ShipConfiguration draft)` | Pure. See §3. |
| `GameObject BuildPreview(ShipConfiguration draft, Transform parent[, out ShipyardValidation])` | A detached visual-only assembly (a `ModularShipView`, no colliders, no physics, no link to the live ship). **You own it and destroy it.** Built whenever the draft assembles inside the prototype policy, even if the cargo/crew would not fit (the validation says so); null otherwise. |
| `bool CanRefitNow(out string reason)` | Pure. See §5. |
| `ShipyardApplyResult ApplyRefit(ShipConfiguration expected, ShipConfiguration draft)` | Atomic. See §4. |
| `event Action<ShipConfiguration> Refitted` | After a successful apply, and after a save's configuration is applied on load (copy of the new one). |
| `IReadOnlyList<string> AllowedModuleIds(string kind)` | For your pickers (`ModuleKind.*`). Stern/Middle/Bow: the V3 W1-r2 part **and** the W1x expanded-beam part (2026-09-25, §9) — pick one family per ship, never mix; Rotor: timber, reinforced; Carrier: M1; Fitting: chimney; Equipment: the one deck cannon (2026-09-25, §15); UpperDeck: empty. |
| `ShipyardService.PersistPathOverride` | Probes only (redirects the persist step to a scratch file). |

Pure C# (no scene): `ShipyardPlanner.Validate(draft, library, referenceHull, snapshot)`,
`ShipyardPolicy.Check / AllowedModuleIds`, `ModularSave.Decode / Encode`.

## 3. `ShipyardReport` (A1/A5)

```csharp
public class ShipyardReport {
    public bool ok;
    public readonly List<Rejection> blocking;            // {string code, partId, message}
    public readonly List<ShipyardNote> warnings;         // {string code, message}
    public readonly List<ShipyardFigure> figures;        // below
    public readonly List<SectionOccupancy> sections;     // one per hull section of the DRAFT
    public string currentRotorId, proposedRotorId;
    public string refitNowBlockedBecause;                // "" = she can be refitted now
    public ShipyardFigure Figure(string id);
    public SectionOccupancy Section(string sectionKey);  // "stern", "middle[0]".., "bow"
    public string Summary();
}
public class ShipyardFigure { public string id, label, unit, note; public float current, proposed;
                              public bool available, provisional; public string Format(float v); }
public class SectionOccupancy {
    public string sectionKey, moduleId;
    public float aftZ, fwdZ;                 // ship frame, m (ends open-ended)
    public int holdCells, cargoCells;        // AUTHORED hold cells of this module / cargo attributed pro rata
    public int berths, crew;                 // AUTHORED berths of this module / hands standing on it
    public int gunSlots, guns;               // usable authored gun slots / fitted guns standing in it
    public List<string> equipment;           // "gun, starboard", "funnel", "deck load pile 1"
    public bool canRemove; public string reason;   // bind removalBlocker here; stern/bow never
}
```

`canRemove` for `middle[i]` = the draft without that bay validates against
the live ship (cargo volume and weight, berths, equipment); `reason` is the
first blocking message. Figures:

| id | meaning | provisional |
| --- | --- | --- |
| `hullLength` | drawn hull at the waterline, prow excluded (m) | |
| `overallLength` | stern interface to prow tip (m) | |
| `beam`, `depth` | shell beam, keel-to-deck (m) | |
| `sections` | middle bays | |
| `displacement` | as loaded now: lightship + cargo + crew + guns (t) | yes |
| `lightship` | sum of the modules' lightship masses (module data) — **the** mass: the sailing model is weighed with it too (t) | yes |
| `draft` | as loaded, above the keel, **from module station tables** (m); unavailable = above the deck line | yes |
| `simDraft` | the SAME mass floated on the sailing model's geometry — **reshaped reference hull (approximation)** (m) | yes |
| `loadLine` | displacement at the load line (t) | yes |
| `holdCells` | hold **volume** capacity = Σ sections' authored `holdCells` | yes |
| `weightAllowance` | cargo **weight** room with the hands now aboard (t) | yes |
| `cargoWeight` | what the cargo aboard weighs (t) | yes |
| `crewBerths` | Σ sections' authored `berths` | yes |
| `gunSlots` | Σ usable authored gun slots (validated: clearance, clear of crew passages) | yes |
| `deckSlots` | deck slots the hull reserves (reserved, not usable yet) | yes |
| `guns` | guns she carries: every `equipment.deck-gun` fitted (2026-09-25, §15) — explicit, not derived | yes |

Warnings today: `HOLD_SMALLER`, `FEWER_BERTHS`, `PROVISIONAL_TUNING`. A gun
pair the draft has no slot for is a BLOCKING `EQUIPMENT_WOULD_BE_LOST`, not a
warning (see §10 Capacity).
Numbers you get for the standard ships (headless self-test, gate
`capacity-is-sum-of-sections`):

| | Short | Long (today) | 2 bays | 3 bays |
| --- | --- | --- | --- | --- |
| hull / overall | 8.88 / 10.14 m | 11.88 / 13.14 m | 14.88 / 16.14 m | 17.88 / 19.14 m |
| mass (Σ module lightship = sim mass) | 21.87 t | 31.91 t | 41.95 t | 51.99 t |
| draft at that mass: tables / sim | 0.859 / 0.801 m | 0.844 / 0.861 m | 0.837 / 0.896 m | 0.832 / 0.919 m |
| hold cells / berths | 11 / 4 | 16 / 8 | 21 / 12 | 26 / 16 |
| gun slots / guns carried | 4 / 4 (pairs 1, 3) | 6 / 6 | 8 / 6 | 10 / 6 |
| cargo weight room (hands) | 5.27 t (4) · 4.91 t (8) | 8.0 t (8) | 12.09 t (8) | 16.18 t (8) |
| deck slots (reserved) | 6 | 10 | 14 | 18 |

Short's weight room (5.27 t ≈ 10 units) still binds before its volume (11 cells).

## 4. Side-effect guarantees

* `Validate`, `Report`, `BuildPreview`, `CanRefitNow`, `ReadCurrent`, and
  cancelling (just dropping the draft / destroying your preview) touch
  **nothing**: not the ship, not resources, not the save (gated headlessly:
  `validate-is-side-effect-free`).
* `ApplyRefit(expected, draft)`, in order: `STALE_DRAFT` if the live
  configuration is no longer `expected`; `CANNOT_REFIT_NOW` if §5 fails; a
  fresh `Validate` against the live ship; then the rebuild; then **persist
  through the game's own save routine** (`SaveGame.SaveTo(SaveGame.Path, "refit")`).
  If the rebuild throws she is rebuilt as she was (`APPLY_FAILED`); if the
  save fails she is rebuilt as she was (`SAVE_FAILED`). On false, ship and
  save are as they were. No resources are spent (no costs yet). In a probe
  session (`SaveGame.Suppressed`) persistence is skipped unless
  `PersistPathOverride` is set, so a probe never writes the player's save.

## 5. Rejection codes

Milestone-1 codes (docs/MODULAR-SHIPS.md §6) all still apply. Added:

| Code | When | Example message |
| --- | --- | --- |
| `NOT_IN_PROTOTYPE` | raised deck, wider hull (W2), oversized wheel, an equipment module other than the one deck cannon, placeholder / incompatible-reference part, a fitting other than the chimney on `stern/Chimney` | "Raised decks are not part of the prototype shipyard yet." / "Oversized wheel (M1-L) is not part of the prototype shipyard: only the M1 timber and reinforced wheels are offered." |
| `WHEEL_REQUIRED` | no rotor | "A paddle steamer needs her wheel; choose a timber or reinforced M1 wheel." |
| `CARGO_WOULD_NOT_FIT` (partId `hold`) | cargo units > new hold cells | "She is carrying 16 loads and this ship's hold takes 11. Unload 5 first; nothing is thrown overboard." |
| `CARGO_WOULD_NOT_FIT` (partId `weight`) | cargo weight > new weight room | "Her cargo weighs 5.5 t and this ship can carry 5.4 t with 4 hands and her guns aboard. …" |
| `HANDS_ASHORE_NEED_HOME` (2026-09-25, `ApplyRefit` only) | hands aboard > new berths, and she is not at her home berth | "She must be at her home berth to land 4 hands ashore." |
| `EQUIPMENT_WOULD_BE_LOST` | something positioned on her (the funnel, a deck-load pile) has a place now and none on the draft; OR (2026-09-25) the draft's own `equipment` list still names a slot that no longer exists on the hull it describes (the assembler's `EQUIPMENT_SLOT_UNKNOWN`, translated to this friendlier code+message) | "Deck load pile 3 would have no place on this ship; the refit is refused rather than leave it behind." / "Deck cannon (Astra v1) at middle[0]/DeckSlot_1_1 would have no place on this ship (its slot is gone); take it off to the dry dock first." |
| `GUNS_NEED_CREW` (2026-09-25) | the draft's fitted guns × their own crew > its berths | "6 guns need 6 hands at the guns and this ship has berths for 4. Take 2 guns off to the dry dock first." |
| `NOT_IN_DRY_DOCK` (2026-09-25, `ApplyRefit` only) | the draft would take more of a module from the dock than is in stock | "There is no Deck cannon (Astra v1) in the dry dock to fit." |
| `DRY_DOCK_NOT_HERE` (2026-09-25, `ApplyRefit` only) | the draft would add to or take from the dock, and she is not at her home berth | "She must be at her home dry dock to move equipment to or from storage; a plain refit between slots does not." |
| `STALE_DRAFT` | live config ≠ `expected` | "The ship changed since this plan was drawn up. Look again and confirm." |
| `OVERLOADED` | the loaded mass would float her above the deck line (downflooding) — never clamped | "At 85.6 t she would float above her deck line and flood. Lighten her first." |
| `NO_HYDROSTATICS` | a hull section has no (valid, hash-matching) station table | |
| `SIM_OUT_OF_RANGE` (partId `sim`) | the SAILING MODEL, weighed with the same mass, would float outside its keel..deck range — never clamped (on Long it binds before `OVERLOADED`: 72.0 t vs 78.4 t) | "At 73.1 t her sailing model would float outside its keel-to-deck range. Lighten her first." |
| `NO_CAPACITY` | a hull section has no authored `capacity` block | |
| `NO_MASS_DATA` | a hull section has no `lightship` mass | |
| `CANNOT_REFIT_NOW` | §6 | the reason sentence |
| `APPLY_FAILED`, `SAVE_FAILED` | the rebuild / the save failed; she was put back | |
| `NO_REFERENCE_HULL` | the steamer's hull form is missing | |

**Surplus hands go ashore, never refused (2026-09-25, Kevin).** A refit whose
draft has fewer berths than she has hands aboard is no longer refused
(`CREW_WOULD_NOT_FIT` is retired): `ShipyardValidation.handsAshore` /
`ShipyardReport` carry the count instead, as a WARNING ("4 hands will go
ashore." / singular "1 hand will go ashore."), and `ApplyRefit` lands that
many at the HOME settlement through the same `Outpost.Station` path a player
uses to drop a hand at a camp by hand — they become real, named villagers on
the home ledger, never despawned. This only happens at the home berth
(`HANDS_ASHORE_NEED_HOME` otherwise, same shape as `DRY_DOCK_NOT_HERE`), and
atomically with the rebuild and the save: a failed rebuild or a failed save
recalls every landed hand and rolls back the home ledger with everything
else. `GUNS_NEED_CREW` is unaffected — it still refuses a draft whose fitted
guns need more hands than the draft's OWN berths, since going ashore only
ever reduces her crew, never grows it back to work guns she cannot crew.

**"Beam: standard / wide" (2026-09-25).** The shipyard screen's width toggle
swaps every hull section between the W1-r2 and W1x families at once (§9);
equipment never needs to move, because the two families share the same
slot ids (only their Y moved) — gated headlessly,
`width-toggle-equipment-slot-ids-identical-w1r2-w1x`.

Milestone-1 equipment codes (`EQUIPMENT_SLOT_UNKNOWN`, `EQUIPMENT_WRONG_KIND`,
`EQUIPMENT_CLASS_NOT_ALLOWED`, `EQUIPMENT_SLOT_TAKEN` — what the brief that
built this calls "SLOT_OCCUPIED", the same code — `EQUIPMENT_EXCEEDS_CLEARANCE`,
`EQUIPMENT_BLOCKS_PASSAGE`, `EQUIPMENT_OVERLAP`, docs/MODULAR-SHIPS.md §6)
still apply, unchanged, to `FitEquipment`/`RemoveEquipment`/`MoveEquipment`
and to a draft that carries equipment straight into `Validate`.

## 6. `CanRefitNow` reasons

"She is under way. Bring her to rest first." (speed > 0.3 m/s) · "She must be
anchored or alongside to be refitted." · "Not during a raid." · "Not while she
is in a fight." (combat lock) · "Cargo is being carried between the ship and
the stores. Wait for the hands to finish." · "<name> is ashore. Call the hands
back aboard first." · "A save is still loading." · module data / hull form missing.

## 7. What the UI must NOT do

* Touch the live ship's `ModularShipView`, `SteamerShip`, `PaddleDrive`,
  `HullFormBody` or any component on the ship. Build previews with `BuildPreview`.
* Mutate `Current` expecting a change (it is a copy) or keep a draft across a
  refit without re-reading (`TryApply` will say `STALE_DRAFT`).
* Compute any number itself (lengths, capacity, weight) — read the report.
* Offer ids that `AllowedModuleIds` does not list.
* Change `Time.timeScale` for the modal.

## 8. Minimal usage

```csharp
var yard = new ShipyardRefitAdapter();
var expected = yard.ReadCurrent();
var draft = expected; draft.middleIds.Add(ShipConfiguration.V3Middle);   // ReadCurrent is already a copy
ShipyardSession.SetWorldInputBlocked(true);
var preview = ShipyardService.Player.BuildPreview(draft, previewRoot);    // you destroy it
string problems = yard.Validate(draft);                                   // every 250 ms is fine
var report = yard.Report(draft);                                          // "hold 16 -> 20"
if (string.IsNullOrEmpty(problems) && yard.TryApply(expected, draft, out string why)) { /* done, saved */ }
Object.Destroy(preview);
ShipyardSession.SetWorldInputBlocked(false);
```

## 9. Prototype scope

Allowed: `hull.stern.w1r2.v3` **or** `hull.stern.w1x.v1`, 0–3 × the matching
`hull.middle.w1r2.v3` / `hull.middle.w1x.v1`, `hull.bow.w1r2.v3` **or**
`hull.bow.w1x.v1`, `wheel.rotor.m1.timber` / `wheel.rotor.m1.reinforced` on
`wheel.carrier.m1`, `fitting.chimney.v3` on `stern/Chimney`,
`equipment.cannon.astra.v1` on any deck-gun slot (2026-09-25). W1-r2 and W1x
(the expanded-beam family, 2026-09-25, validated in
docs/EXPANDED-HULL-VALIDATION.md — deck beam 12.08 vs 9.28, length/depth
unchanged) may each build a complete ship, but **never mix**: their join
profiles differ (`W1-r2` vs `W1x`), so `ShipAssembler`'s existing per-socket
standard check refuses a direct join between them, readably
(`JOIN_PROFILE_MISMATCH`) — no new policy code was needed for the one-width
rule beyond the family allow-list itself. Excluded: raised decks, the
still-unvalidated/superseded W2-r1 wide-and-deep family, the oversized
wheel, any other equipment, costs. Timber ↔ reinforced changes **only the
rotor's drawing**: same radius, same physics, no bonus. **Meshes are never
stretched** to make width/depth variants; only the PHYSICS data is
reshaped, and only from authored module data (A9).

## 10. How it works (and what is approximate)

**The standard ship is a configuration.** `Long()` is today's steamer. A
ship that was never refitted keeps the V8 art and today's physics exactly;
the save field stays empty. Refitting (even back to Long) switches her to the
modular drawing.

**Physics follows the assembled hull (D2 as revised, A3).** Three factors are
MEASURED from module data against the Long assembly (so Long is exactly
1, 1, 1 — gated field-for-field): length = stern aft end → bow **stem at the
waterline datum** (new bow socket role `hull.stem`, X 8.45 measured from
`Bow_W1/Hull_Shell.fbx`; the prow, rails, wheel, carrier and chimney never
count); beam = 2 × the join profile's shell half-breadth; depth = the
profile's keel-to-deck. `HullFormData.Reshaped(sL, sB, sD)` reshapes the
steamer's generated station tables by those factors, every field by its own
definition (documented in the method; e.g. mass ∝ sL·sB·sD, which the plan then
replaces with the module lightship sum (one mass source, below), BM ∝ sB²/sD, wheel
radius/dip and rudder area unchanged). The stern's fittings (wheel axle,
well, rudder, helm) are then pinned to the drawn stern, which moves by half
the change in length. **This is an approximation**: the assembled hull's
real sections are not the steamer's sections stretched. The proper next
step is per-module hydrostatic station tables (sectional area / half-breadth
/ moment per station, per level) exported with each hull module by the
Blender tools and summed along the assembly — the same table format
`hullform.json` already uses, so `HullFormBody` would not change.
With today's W1-r2 parts sB = sD = 1; a W2 or deeper module changes physics
with no code change once its join profile carries its numbers.

**No thrust change.** Top speed setting, wheel radius, dip, HandlingTuning,
JuiceTuning untouched. What changes follows from the existing code reacting
to the new hull: mass and inertia (HullFormBody), surge resistance from the
tables, `ShipMotor.HullLength` (turn-radius readouts, chase-camera framing,
grounding probes), combat capsule, wake/foam size, water clip.

**Two hydrostatic sources (H2–H4).** The REPORT floats the assembled hull
on Astra's per-module station tables (`Resources/ShipModules/Hydrostatics/
HullW1r2_v3/*.json`, referenced from each hull module's `hydrostatics` block
with the geometry SHA256, which the loader checks): each installed section's
`integratedVolumeU3` summed at one ship-local waterline (per-section
`offsetZU`, 0 today), linear between samples, 0 below the keel, never above
the deck line (→ `OVERLOADED`). Water density is `HullFormData.SeaWaterDensity`
(1025, the one place; `HullFormBody` reads it too). The LIVE PHYSICS keeps
running on the reshaped `HullFormData` (it needs half-breadth stations, the
tables have areas). Measured agreement for today's ship: 31.9 t floats at
**0.844 m** on the tables vs **0.861 m** in the sailing model (−1.9 %); the
table volume at the model's draft is 31.86 m³ vs the model's 31.13 m³
(+2.3 %); volume to the deck 76.46 m³. They agree for Long. Their GEOMETRIES
drift apart with length (the sailing model stretches the whole steamer while
the tables add real bays: ρ·V of the stretched form is 23.8 t for Short and
48.0 t for 3 bays vs 21.9 / 52.0 t of module lightship), but since
2026-09-24 there is **one mass** (below). The drawn datum (Z 0) sits 0.116 m
above the table waterline; the view hangs it on the physics waterline.
**Next step (proposed, not built):** generate `HullFormBody`'s station data
(half-breadth / area / moment per level) from the module tables — the export
would need half-breadth and moment per station as well as area — so the sim
and the report share one geometry source.

**One mass source (2026-09-24, Kevin).** Every plan, Long included, sets the
reshaped `HullFormData.massKg` to Σ installed modules' `lightship.massKg`
(`ShipyardPlanner.ModuleLightshipKg`: a hull section without one is refused
`NO_MASS_DATA`; wheel, carrier and chimney carry none today, so 0, and that
is kept). `HullFormBody.Configure` sets `rb.mass` from `massKg` and derives
the inertia tensor, heave/roll/pitch damping and roll/pitch stiffness from
that same number; `PaddleDrive` scales its engine torque from `rb.mass` as it
always did. The form's GEOMETRY (`volume`, design `draft`, stations) stays
the reshaped hull's, so she comes to rest wherever that mass floats her,
which is her design draft only on Long. That is intended. (`Submersion` =
immersed / design volume, so at rest it reads ≈ 0.92 on Short and ≈ 1.08 on
3 bays; it scales surge resistance and the rudder's wetness clamp.) Long's
sim mass is 31 906.60 kg vs today's 31 906.62 kg (gate `long-mass-is-todays`;
the untouched V8 ship keeps the hull form's own mass). Cargo, crew and gun
weights are still **checked, not felt** by the steamer's rigidbody.

### Provisional: two hydrostatic models

The shipyard report floats a ship on the module station tables; the game
sails her on the reshaped reference hull. Both are now weighed with the SAME
mass (above); what differs is geometry. Both drafts are in the report
(`ShipyardReport.tableDraftM` / `simDraftM`, and `…CurrentM`; figures `draft`
and `simDraft`), both PROVISIONAL. Static values at the lightship mass
(headless self-test, gate `two-hydrostatic-models-table`, which also checks
sim mass == module lightship for every length; the sim column is
HullFormBody's own strip sum, `AreaAt(level) × dz`, solved for the mass).
Timber and reinforced rotors carry no mass, so one row per length covers both:

| config | mass (Σ module lightship = sim) | table draft | sim static draft | Δ (sim − table) | Δ % | sim design draft |
| --- | --- | --- | --- | --- | --- | --- |
| 0 bays (Short) | 21.87 t | 0.859 m | 0.801 m | −0.059 m | −6.8 % | 0.861 m |
| 1 bay (Long, today) | 31.91 t | 0.844 m | 0.861 m | +0.017 m | +2.0 % | 0.861 m |
| 2 bays | 41.95 t | 0.837 m | 0.896 m | +0.059 m | +7.1 % | 0.861 m |
| 3 bays | 51.99 t | 0.832 m | 0.919 m | +0.087 m | +10.5 % | 0.861 m |

The sim column is now where the live physics actually comes to rest. Before
this change she rested at 0.861 m at every length, on ρ·V of the stretched
form. No length's lightship or full load leaves the sim's keel..deck range;
if one did, the refit is refused `SIM_OUT_OF_RANGE` (gate
`sim-out-of-range-is-blocking`). The DYNAMIC column (the keel's depth below
the local sea surface, moored at rest after each refit, at her rigidbody
mass) is printed by the play-mode probe (`Logs/ShipyardRefitProbe.txt`,
"PROVISIONAL two hydrostatic models", same columns + dynamic). The probe has
not been run yet. Nothing is gated on agreement.

**Lightship per module (H5).** Each hull module's `lightship.massKg` is
provisional data seeded so stern + middle + bow = today's 31 906.6 kg (hullform
massKg × 0.42³), split by each section's table volume to the deck: stern
12 498.2, middle 10 039.4, bow 9 369.0 kg. Wheel, carrier and chimney carry 0
(today's mass is one lump).

**Capacity (D9/A4): AUTHORED per module, provisional.** Each hull module
carries a `capacity` block (`ModuleSchema.CapacitySpec`). Every field is
marked `"provisional": true` in the data and has its own `source` line:

```json
"capacity": {
    "holdCells": {"value": 5, "provisional": true, "source": "..."},
    "berths":    {"value": 4, "provisional": true, "source": "..."},
    "gunSlots":  {"ids": ["DeckSlot_1_-1", "DeckSlot_1_1"], "provisional": true, "source": "..."},
    "rule": "how it was seeded"
}
```

A ship's hold cells and berths are the SUM over her installed sections;
nothing is derived from geometry any more. The weight model needs nothing
more per module: it reads `lightship.massKg` and the shared `ShipLoad`
weights. **Seeded once** (2026-09-24) so Long reproduces today exactly
(16 cells, 8 berths, 6 guns): hold cells = 16 split by each section's table
volume to the deck, largest remainder; berths = today's 8 deck stations
(`SteamerBootstrap.DeckStation`) counted in the section whose z range contains
them; gun slots = the port/starboard deck slot nearest today's gun socket
(`hullform.json` `gunSockets` × 0.42) in the section that contains it
(gate `capacity-seeds-match-todays-deck`):

| module | hold cells | berths | gun slots | today's gun it stands for |
| --- | --- | --- | --- | --- |
| `hull.stern.w1r2.v3` | 6 | 2 | `DeckSlot_2_-1`, `DeckSlot_2_1` | gun pair 3 (z −1.26 m, **6 mm aft of the stern/middle join**; a retune may give it to the middle) |
| `hull.middle.w1r2.v3` | 5 | 4 | `DeckSlot_1_-1`, `DeckSlot_1_1` | gun pair 2 (z 1.09 m) |
| `hull.bow.w1r2.v3` | 5 | 2 | `DeckSlot_1_-1`, `DeckSlot_1_1` | gun pair 1 (z 3.44 m) |

A listed gun slot counts only if it is one of the module's equipment slots
on a `deck.slot` socket, takes `equipment.deck-gun`, and has a non-empty
clearance box that lies inside the section (length; join profile
half-beam) and overlaps no crew passage of the ASSEMBLED ship (strict, the
same test the assembler applies to equipment). Gate:
`gun-slot-needs-clearance-and-clear-passage`. The seeded slots have
clearance 1.8 × 1.55 × 1.65 u at |y| 3.45, i.e. |y| 2.675..4.225, which
touches but does not overlap the passages' |y| ≤ 2.675.

**Guns are real equipment now (2026-09-25), not hull-form sockets.** See §15
for the full picture (module, presets, migration, dry dock, API). In short:
`capacity.guns` is the COUNT of fitted `equipment.deck-gun` items in the
assembly — no cap, no "nearest pair", nothing struck. `Long()`/`Short()`/
`WithMiddles(n)` fit the hull's 3 pairs on their AUTHORED slots explicitly
(bow and stern always, the middle pair on the first bay only — extra bays
buy slots, not guns, gate `extra-slots-grant-no-guns`); a v1 save (guns still
implicit) is migrated to the same explicit equipment on load. Each gun's own
`crew` (1, from `CannonBattery`/`CrewRoster.GunCrew`) and `massKg` (500,
`ShipLoad`'s lightest calibre) are read from its module, summed as
`gunsCrewNeeded`/`gunsWeightKg`; a draft whose guns need more hands than it
has berths is refused `GUNS_NEED_CREW` (gate `guns-need-berths-for-their-crew`),
never silently capped. `data.gunSockets` stays the fitted STARBOARD guns'
own positions (ship frame) as a courtesy copy for `Reshaped`/`Scaled` and
the self-test's hull-form comparisons; the LIVE battery is no longer fit
from it. `SteamerBootstrap.Man` is instead handed the plan's `fittedGuns`
directly (`BuildOptions.fittedGuns`, threaded from `ShipyardService.Options`)
and calls the new `CannonBattery.Fit(IList<CannonBattery.GunStation>)`,
which stands each gun exactly where its own slot put it with its own side —
no mirroring — so a side missing a gun (one sent to the dry dock, §15) gets
a correctly-counted, correctly-positioned battery instead of a phantom or
mis-positioned twin (2026-09-25, fixes the deviation noted at the end of
§15). The untouched V8 steamer (no refit yet) still calls the old
`CannonBattery.Fit(IList<Vector3>)` (starboard list mirrored to port)
exactly as before — that path, and her battery, are unchanged.

The **weight allowance stays the shared limit**: displacement at the load
line − lightship − crew × 90 kg − guns carried × 500 kg (`ShipLoad` weights;
the steamer's guns have no calibre, so the lightest, 500 kg, provisional),
floated on the module tables. The load line is a freeboard margin below the
deck line, anchored so that Long's full hold with 8 hands and 6 guns just
reaches it (0.738 m below the deck line). Every hull gets the same margin.
**Cargo weight is checked, not felt** (only the ladder ships' `ShipLoad`
puts it on the rigidbody).

**Guns without a slot are refused, not struck (decided 2026-09-24; the dry
dock, 2026-09-25, is how the "or change the rule" below happened).** Short
has slots for the stern and bow pairs only, not the middle's — so a draft
built by cloning the live config and just clearing `middleIds` still lists
the middle pair on a slot (`middle[0]/DeckSlot_1_-1`/`_1`) that no longer
exists, and is refused `EQUIPMENT_WOULD_BE_LOST` ("… would have no place on
this ship (its slot is gone); take it off to the dry dock first."), per the
agreed rule: reject changes that cannot safely retain existing equipment,
never discard. Guns CAN now be removed by hand (`RemoveEquipment`, into the
dry dock) — do that first (or start from the `Short()` preset, which never
references the middle pair) and the same hull shrink is valid; the 2 guns
sit in the dock, unlost, until fitted again. See §15.

The old derived capacity (volume and crew-strip ratios) is kept only as the
printed cross-check gate `derived-capacity-cross-check`. Authored − derived
is 0 / 0 on Short and Long; hold +1 / +2 and berths +2 / +2 on 2 / 3 bays.
Raised decks later: an UpperDeck module can carry its own `capacity` block
(berths, gun slots) and a second `deckY` level; depth for physics stays the
main hull's keel-to-deck.

**Placement (D3).** The `ModularShipView` hangs under the ship at authoring
Z 0 = the waterline and is shifted along the ship so the drawn rotor axle
sits on the physics axle (z) exactly. Drawn deck 0.88 m vs physics deck
0.819 m (6 cm); drawn axle 0.175 m vs physics axle 0.302 m above the
waterline (the drawn wheel dips 13 cm deeper than the physics wheel —
physics kept, per "no thrust change"). Uniform 0.5 m/u, no per-axis stretch.

**Refit in place (D4) — not a replaced ship object.** Kevin asked for a
refit that replaces the ship object. It is not done, deliberately: there is
no construction path that produces a player ship (she is the scene's
PlayerShip, converted in place by `SteamerBootstrap`; there is no prefab),
and many systems cache her in `Start` with no re-find and no setter — several
of them UI files this work must not edit. Instead the refit re-runs the
**same construction path** (`SteamerBootstrap.Assemble`, factored out of
`Convert`) on the same GameObject and Rigidbody, which by construction keeps
identity, damage fraction (`HullIntegrity.Integrity01` is a fraction and is
not touched), hold, crew bodies (re-posted, none cloned or stood down), pose,
anchor/berth and gangway. Replacing the object needs these rebinds first:

| System | Holds | Re-finds a destroyed ship? |
| --- | --- | --- |
| `UI/StatusHUD` (motor, hull), `UI/MiniMap`, `UI/NavigationAid` | ShipMotor | **No** (Start only) — UI, not editable here |
| `UI/ShipyardPanel` | Shipyard (serialized) | No — UI |
| `Audio/SeaAudio` | ShipMotor | **No** (Start only) |
| `Voyage/VoyageManager.ship`, `Voyage/SalvageSpawner.ship` | ShipMotor (serialized) | **No** (only if null at Start) |
| `CameraRig/ChaseCamera.target` | Transform (serialized) | No, but has a public `Target` setter |
| `Crew/CrewAgent` hands ashore | ship Transform, ShipHold, Gangway | **No** (private) |
| `Combat/EnemyShip.player` | ShipMotor | Yes (timed lazy lookup) |
| `UI/Sheets/SheetBootstrap` (Anchor, Hold, Roster, Motor), `UI/Sheets/Sheets.cs`, `UI/Sheets/ChartWatch`, `UI/HomeTab`, `World/ShipCargoSide` (hold, anchor, gangway), `World/CampLoading.Anchor` | statics / fields | Yes (`x != null ? x : Find…`) |
| `Ship/JuiceTuning.paddleFor` | static ShipMotor | Yes (compares) |
| `Save/SaveGame` | — | Yes (finds each time) |
| On-ship components (HelmInput, Bilge, Breakers, SpeedJuice, SurfaceWake, WindArrow, PaddleSound, SmoothnessMeter, GunneryReadout, TargetHUD, CombatLock, AnchorController, HullIntegrity, CannonBattery, PlayerHull, LanternSwing) | own GetComponent | would come with a cloned object, but their runtime state would not |

**UI-reader inventory** (every UI file that holds the player ship or its
components):

| File | Holds | On `PlayerShipReplaced` | Check `WorldInputBlocked` |
| --- | --- | --- | --- |
| `UI/StatusHUD.cs` | ShipMotor + HullIntegrity, found once in `Start` | **must rebind** | – |
| `UI/MiniMap.cs` | ShipMotor, `Start` | **must rebind** | – |
| `UI/NavigationAid.cs` | ShipMotor, `Start` | **must rebind** | – |
| `UI/Sheets/SheetBootstrap.cs` | static Anchor / Hold / Roster / Motor, lazy | clear the statics (lazy re-find handles a destroyed ship, not a swapped live one) | – |
| `UI/Sheets/Sheets.cs` | static anchor, lazy | clear | – |
| `UI/Sheets/ChartWatch.cs` | anchor, lazy | clear | – |
| `UI/HomeTab.cs` | anchor, lazy in `Update` | clear | – |
| `UI/Sheets/ShipSheet.cs` | via `SheetBits` | follows SheetBootstrap | – |
| `UI/ShipyardPanel.cs` | Shipyard (ladder ship; disabled on the steamer) | n/a | – |
| `UI/Hand.cs` | AnchorController/CrewRoster via GetComponent (on the ship) | comes with the ship | **yes** (`PickAt`) |
| `UI/CampToasts.cs` | added beside the ship by AnchorController | comes with the ship | – |
| `UI/Sheets/WorldPicker.cs` | – | – | **yes** (`Busy()`) |
| `UI/CampSiting.cs`, `UI/WallSiting.cs` | – | – | **yes** |
| `UI/HandCursor.cs` | follows `Hand` | – | via Hand |
| `UI/Sheets/ChartData.cs`, `UI/Sheets/PlaceLabel.cs` | read an anchor passed in | – | – |

## 11. Save (D-S)

`ShipSave.modular` (string, the configuration's own versioned JSON). Added
field, `SaveData.CurrentVersion` stays 1. Written only when `ModularActive`
(empty otherwise — an untouched game writes what it wrote before). Load:
empty/missing → standard steamer (and a refitted ship in the same session is
returned to it); present → prototype policy + assembler; unreadable, unknown
id or not buildable → standard steamer + console warning, never a refused
save. Applied in `SaveGame.Restore` as step 2a, before the hold (3) and the
crew (4a), so both land on the right deck.

## 12. Tests

* Headless: `tools/modular-selftest.sh` → `ModularShipSelfTest: 97 PASS, 0 FAIL`
  (45 milestone-1 + 52 shipyard: authored capacity (per-module blocks, seeds
  match today's deck, sums per length, slot validation, guns need berths,
  extra slots grant no guns, Short strikes a gun pair, derived cross-check),
  one mass (`long-mass-is-todays`, sim mass == lightship in the two-models
  table, `sim-out-of-range-is-blocking`), the two-models draft table, hydrostatic tables (load, prow never
  counted, volume-to-deck = Σ tables, monotonic draft solve, OVERLOADED,
  0 below keel, sailing-model cross-check, lightship seeds), section
  occupancy, policy accept/reject with codes,
  `Reshaped(1,1,1)` field-for-field, 31 per-field reshape rules, Long ≡
  reference, Short/Long/3-bay numbers, retention (cargo volume and weight,
  crew, funnel, synthetic gun and deck-load loss), report, purity, save-field
  round trip, old save → Long, unknown module → Long + warning).
* Play mode: `unity cmd eval --json --code 'RunProbe.ShipyardRefit();'` on
  the worktree project with the steamer selected → `Logs/ShipyardRefitProbe.txt`.

## 13. Open questions

1. Replace the ship object (Kevin's D4 revision) — needs the rebinds in §10
   first, three of them in UI files. Your call, Kevin/Astra.
2. A4 authored capacity is in (§10). A gun pair with no slot on the draft
   REFUSES the refit (Short is unbuildable while guns can't be removed by
   hand) — Kevin/Astra: add gun removal/stores, or change the rule? And:
   should guns move to the slot positions (they stand at the hull form's
   sockets today)?
3. Short has 4 berths (stern 2 + bow 2): with today's 8 hands aboard, the
   surplus 4 go ashore automatically now (2026-09-25, `HANDS_ASHORE_NEED_HOME`
   above); with ≤ 10 loads she is accepted with 4 guns.
4. Cargo weight is checked but has no physical effect on the steamer (the
   lightship mass now is felt: one mass source, §10).
5. Bow stem X 8.45 was measured by us from the FBX; Astra, please confirm or
   add it to the manifest. (Kevin's brief quoted 23.95 u for the Long hull:
   that is the V2 bow; the V3 raked bow at the waterline gives 23.75 u.)
6. The funnel on refitted ships sits at the assembled midpoint (V3 rule); on
   the untouched ship the deck load is laid out around the V8 art's funnel.
7. The V8 art's other parts (e.g. any smoke rig on `AstraSteamerVisual`) are
   hidden with it on a refitted ship.

## 14. Changed files

Since the guns-as-equipment commits above (unpaired-gun fix, 2026-09-25):
* `Assets/_Project/Scripts/Ship/CannonBattery.cs` — `GunStation` (position + side, no pairing assumed); new `Fit(IList<GunStation>)`; `PortCount`/`StarboardCount`/`TotalGuns`. The old `Fit(IList<Vector3>)` (starboard mirrored to port) is UNCHANGED, still what the untouched V8 steamer calls.
* `Assets/_Project/Scripts/Steamer/SteamerBootstrap.cs` — `BuildOptions.fittedGuns` (null = today's mirrored-socket path); `Man` fits the battery from it directly when set, no mirroring.
* `Assets/_Project/Scripts/Ship/Modular/Runtime/ShipyardService.cs` — `Options(plan)` passes `plan.fittedGuns` through.
* `Assets/_Project/Scripts/Ship/Modular/Shipyard.cs` — comment fix only (`data.gunSockets` is now a courtesy copy, not what the live battery reads).
* `Assets/_Project/Scripts/Ship/Modular/ShipyardSelfTest.cs` — `long-guns-3-port-3-starboard`, `short-guns-2-port-2-starboard`, `port-gun-removed-keeps-starboard-unmoved-at-3`.
* `Assets/_Project/Scripts/Dev/ShipyardRefitProbe.cs` — (b1) live check: remove the middle port gun only, battery reads 5 guns / 2 port / 3 starboard, then restore.

Since d6d66ad (guns as equipment + dry dock, 2026-09-25) — see §15:
* `Assets/_Project/Resources/ShipModules/Modules/equipment.cannon.astra.v1.json` — new: the real cannon module (mass/crew provisional).
* `Assets/_Project/Resources/ShipModules/Modules/hull.{stern,middle,bow}.w1r2.v3.json` — deck-gun `equipmentSlots` widened 1.55→2.3 u across (fits the real footprint); `CrewPassage_Main` narrowed 2.675→2.30 u to match; port-side (`_1`) deck-gun socket `yawDeg` 0→180 (muzzle to port).
* `Assets/_Project/Resources/ShipModules/Meshes/Equipment/Cannon_Astra_v1/Cannon.fbx` — new: copied from `art-staging/cannon-astra-v1/cannon.fbx` (no `.meta` yet).
* `Assets/_Project/Scripts/Ship/Modular/ModuleSchema.cs` — `ProvisionalFloat`; `EquipmentSpec.massKg`/`.crew`.
* `Assets/_Project/Scripts/Ship/Modular/ShipConfiguration.cs` — `SupportedSchemaVersion` 1→2; `EquipmentCannon`; `Short`/`Long`/`WithMiddles` fit the hull's 3 gun pairs explicitly; `MigratedToV2` (v1 save → explicit guns).
* `Assets/_Project/Scripts/Ship/Modular/Shipyard.cs` — guns: `FittedGun`, `ShipyardPlan.fittedGuns`/`gunsWeightKg`/`gunsCrewNeeded` replace `gunIdx`/`FitGuns`/`GunIndex`; `GunsStruck` and the old `EquipmentLost` gun-diff removed; `GUNS_NEED_CREW`, `NOT_IN_DRY_DOCK`, `DRY_DOCK_NOT_HERE` codes; `TranslateVanishedGunSlot` (EQUIPMENT_SLOT_UNKNOWN → friendlier EQUIPMENT_WOULD_BE_LOST for a gun); `ShipyardPolicy` allows the cannon; `ModularSave.Decode` calls `MigratedToV2`.
* `Assets/_Project/Scripts/Ship/Modular/DryDock.cs` — new: the pure dry-dock store + apply-time diff.
* `Assets/_Project/Scripts/Ship/Modular/ShipyardEquipment.cs` — new: pure `Fit`/`Remove`/`Move`/`Slots`/`DockPreview`, `EquipmentSlotView`, `DryDockRow`, `ShipyardEdit`.
* `Assets/_Project/Scripts/Ship/Modular/ShipyardReport.cs` — `dryDock` rows; `guns` figure note.
* `Assets/_Project/Scripts/Ship/Modular/Runtime/ShipyardService.cs` — `Dock`; `DryDockField`/`ApplyDryDockFromSave`; `ApplyRefit` gates + commits the dock diff; `FitEquipment`/`RemoveEquipment`/`MoveEquipment`/`EquipmentSlots`/`DryDockPreview`.
* `Assets/_Project/Scripts/Save/SaveData.cs`, `Save/SaveGame.cs` — `ShipSave.dryDock` (additive field, same pattern as `.modular`).
* `Assets/_Project/Scripts/Ship/Modular/ModularShipSelfTest.cs`, `ShipyardSelfTest.cs` — module count 12→13; gun/dry-dock gates rewritten for explicit equipment (`hull-shrink-with-dangling-guns-refused`, `hull-shrink-after-removing-guns-ok`, `dock-*`, `guns-need-berths-for-their-crew`); obsolete `gunIdx`/`GunsStruck`-era gates removed.
* `Assets/_Project/Scripts/Dev/ShipyardRefitProbe.cs` — the old outright "Short is refused" check replaced with the live remove-guns → shrink → dock → grow → refit-back → battery sequence (b2).
* `docs/MODULAR-SHIPS.md`, this file — updated for the above.

Since 79b734d (authored capacity + one mass source, 2026-09-24):
* `Assets/_Project/Resources/ShipModules/Modules/hull.{stern,middle,bow}.w1r2.v3.json` — provisional `capacity` blocks (seeded, §10).
* `Assets/_Project/Scripts/Ship/Modular/ModuleSchema.cs` — `CapacitySpec`, `ProvisionalInt`, `ProvisionalSlots`; `ModuleDef.capacity`.
* `Assets/_Project/Scripts/Ship/Modular/Shipyard.cs` — capacity = Σ authored section blocks (`SectionCapacities`, gun-slot validation, `FitGuns`, `WeightModel.CrewPerGun`), derived path removed; plan `data.massKg` = Σ module lightship (`ModuleLightshipKg`); `SIM_OUT_OF_RANGE`, `NO_CAPACITY`, `NO_MASS_DATA`; `GunsStruck` (warning) vs `EquipmentLost` by hull-form gun index; occupancy reads the authored numbers.
* `Assets/_Project/Scripts/Ship/Modular/ShipyardReport.cs` — `gunSlots`/`guns` figures from authored capacity, notes, one-mass wording.
* `Assets/_Project/Scripts/Ship/Modular/ShipyardSelfTest.cs` — 10 new gates; two-models table with one mass; Long gated field-for-field except mass (mass gated to 31 906.6 kg).
* `Assets/_Project/Scripts/Dev/ShipyardRefitProbe.cs` — gates rb.mass == module lightship and guns fitted == plan; mass/draft table with Δ and sim design draft in the dynamic section.
* `docs/MODULAR-SHIPS.md` — self-test count.

Since ea67f99 (the prototype backend):

Added
* `Assets/_Project/Scripts/Ship/Modular/Shipyard.cs` — pure core: codes, prototype policy, hull measurement, reshape plan, capacity, weight allowance, retention checks, deck-load layout, save-field codec.
* `Assets/_Project/Scripts/Ship/Modular/ShipyardReport.cs` — the current-vs-proposed report (A1) with per-section occupancy.
* `Assets/_Project/Scripts/Ship/Modular/ShipHydrostatics.cs` — Astra's table format, assembled-hull waterline solve (H2).
* `Assets/_Project/Scripts/Ship/Modular/ShipyardSelfTest.cs` — 31 headless shipyard gates.
* `Assets/_Project/Scripts/Ship/Modular/Runtime/ShipyardService.cs` — the MonoBehaviour API on the steamer; atomic in-place refit, persist, load path.
* `Assets/_Project/Scripts/Ship/Modular/Runtime/ShipyardRefitAdapter.cs` — Astra's three-method adapter + Report.
* `Assets/_Project/Scripts/Ship/Modular/Runtime/ShipyardSession.cs` — world-input block flag.
* `Assets/_Project/Scripts/Dev/ShipyardRefitProbe.cs` — play-mode probe.
* `docs/SHIPYARD-API.md` — this file.

Modified
* `Assets/_Project/Scripts/Steamer/HullFormData.cs` — `Reshaped(sL,sB,sD)`, `PinSternFittings`, `VolumeBelowDeck` (additive).
* `Assets/_Project/Scripts/Steamer/SteamerBootstrap.cs` — `ReferenceData()` and `Assemble(…, BuildOptions)` factored out of `Convert` (Standard options = today's behaviour); `Man`/`FitDeckLoad`/`DeckStation` take the options; adds the `ShipyardService`.
* `Assets/_Project/Scripts/Save/SaveData.cs` — `ShipSave.modular` (added field, no version bump).
* `Assets/_Project/Scripts/Save/SaveGame.cs` — capture the field; null-guard on read; apply it as restore step 2a.
* `Assets/_Project/Scripts/Crew/CrewAgent.cs` — read-only `HomeShip` (refit guard: hands ashore).
* `Assets/_Project/Scripts/Ship/HelmInput.cs`, `CameraRig/ChaseCamera.cs`, `CameraRig/IslandInput.cs`, `Combat/CombatLock.cs`, `Ship/CannonBattery.cs` — one-line `ShipyardSession.WorldInputBlocked` gate each (since 2026-10-03 `HelmInput` gates the boat stick + boost, and `ChaseCamera` gates the look stick; the old pinch/wheel zoom is gone).
* `Assets/_Project/Scripts/Ship/Modular/ModuleSchema.cs` — `SocketRole.HullStem`; `hydrostatics` and `lightship` blocks on a module.
* `Assets/_Project/Scripts/Ship/Modular/ModuleLibrary.cs` — loads and hash-checks the hydrostatic tables (Resources in Unity, disk headless).
* `Assets/_Project/Scripts/Steamer/HullFormBody.cs` — `waterDensity` default reads `HullFormData.SeaWaterDensity` (same 1025).
* `Assets/_Project/Resources/ShipModules/Modules/hull.{stern,middle}.w1r2.v3.json` — `hydrostatics` + provisional `lightship` blocks.
* `Assets/_Project/Scripts/Ship/Modular/ModularShipSelfTest.cs` — runs the shipyard gates; takes the hull form JSON.
* `Assets/_Project/Resources/ShipModules/Modules/hull.bow.w1r2.v3.json` — `Stem` socket (X 8.45, provisional); `hydrostatics` + `lightship`.

Delivered by Astra (copied into the worktree by the coordinator, not authored here)
* `Assets/_Project/Resources/ShipModules/Hydrostatics/HullW1r2_v3/{Stern_W1,Midship_W1,Bow_W1}.json`
* `docs/modular-reference/MODULAR-HYDROSTATICS-HANDOFF.md`, `docs/modular-reference/hull-v3-manifest.json` (updated with `hydrostatics` entries)
* `Assets/_Project/Scripts/Dev/Editor/RunProbe.cs` — `RunProbe.ShipyardRefit()`.
* `tools/modular-selftest.sh`, `tools/modular-selftest/Main.cs` — compile `HullFormData.cs`, pass `hullform.json` and a disk reader for the hydrostatic tables.
* `docs/MODULAR-SHIPS.md` — self-test count and a pointer here.

## 15. Equipment editing + dry dock (2026-09-25) — for Astra's UI

**The module.** `equipment.cannon.astra.v1` (kind `Equipment`, class
`equipment.deck-gun`), Astra's real kit
(`art-staging/cannon-astra-v1`, footprint 1.22 × 2.1816 × 1.2756 u, muzzle
toward authoring -Y at yaw 0 — a starboard slot's socket stays yaw 0, a
port slot's is yaw 180 so the drawn muzzle points the other way). Mesh at
`Resources/ShipModules/Meshes/Equipment/Cannon_Astra_v1/Cannon.fbx` (no
`.meta` yet — Unity writes one on first import; `ModularShipView` falls back
to a grey placeholder box until then, same as any other module). Mass (500
kg) and crew (1) are on `ModuleDef.equipment.massKg`/`.crew`
(`ProvisionalFloat`/`ProvisionalInt`, same shape as `capacity`'s fields) —
provisional, same numbers the implicit hull-form guns used. The real
footprint does not fit the milestone-1 deck-slot clearance
(1.8 × 1.55 × 1.65 u): every `equipment.deck-gun` slot on the three hull
modules was widened to 1.8 × **2.3** × 1.65 u, and each module's
`CrewPassage_Main` narrowed from |y| ≤ 2.675 to |y| ≤ **2.30** u to match
(still touches, does not overlap — the same relationship as before, just at
the new numbers). Both remain PROVISIONAL; retune freely.

**Guns are explicit `equipment` entries**, not hull-form sockets (§10).
`ShipConfiguration.SupportedSchemaVersion` is now **2**; a v1 document
(no `equipment`) is migrated on load (`ShipConfiguration.MigratedToV2`,
called from `ModularSave.Decode`) by fitting the SAME cannon on the bow's
and stern's authored pair, and on the first middle bay's if she had one —
exactly what `WithMiddles(n)` still builds fresh, since a v1 config could
only ever have been one of those. An old save with no `modular` field at
all is still untouched (`Long()`, as before).

**API** (on `ShipyardService`; pure — no ship/dock/save touched — unless
noted):

```csharp
ShipyardEdit FitEquipment(ShipConfiguration draft, string slotId, string moduleId);
ShipyardEdit RemoveEquipment(ShipConfiguration draft, string slotId);
ShipyardEdit MoveEquipment(ShipConfiguration draft, string fromSlotId, string toSlotId);
class ShipyardEdit { bool ok; string code, message; ShipConfiguration draft; }
    // draft = the edited copy on success, an unchanged copy of the INPUT on
    // failure (never your own reference either way). Fails with the same
    // codes ShipAssembler/ShipyardPolicy would refuse the result with
    // (EQUIPMENT_SLOT_UNKNOWN, EQUIPMENT_CLASS_NOT_ALLOWED,
    // EQUIPMENT_SLOT_TAKEN [= "SLOT_OCCUPIED"], EQUIPMENT_EXCEEDS_CLEARANCE,
    // EQUIPMENT_BLOCKS_PASSAGE, EQUIPMENT_OVERLAP, NOT_IN_PROTOTYPE) or
    // NOTHING_THERE (Remove/Move a slot with nothing fitted).

IReadOnlyList<EquipmentSlotView> EquipmentSlots(ShipConfiguration draft);
class EquipmentSlotView { string slotId, sectionKey, side /* "port"/"starboard" */, label /* "Middle bay 1, starboard gun" */;
                          string[] accepts; string occupantModuleId /* "" = empty */;
                          bool usable; string blockedReason /* "" = usable */; Vector3 positionM /* ship frame */; }
    // Every FIXED deck-gun slot of the draft (the free-placement deck AREA
    // is not enumerated here). `usable` is false only when the slot's
    // clearance overlaps a crew passage of THIS draft's assembly --
    // occupied is a separate question (occupantModuleId).

IReadOnlyList<DryDockRow> DryDockPreview(ShipConfiguration draft);
class DryDockRow { string moduleId, name; int inDockNow, inDockAfterApply; }
    // Per module id the dock holds now, or would after applying `draft`
    // from the LIVE ship's current configuration -- the same diff
    // ApplyRefit uses. Bind a picker's "in stock" / greyed-out state to this
    // (Fit does not itself check stock -- only ApplyRefit does, at the end).
```

`AllowedModuleIds(ModuleKind.Equipment)` → `["equipment.cannon.astra.v1"]`.
`ShipyardReport.dryDock` (filled by `ShipyardService.Report`, empty from the
pure `ShipyardReport.From`) carries the same rows as `DryDockPreview` for the
report screen. `Report`'s `guns` figure and every `SectionOccupancy.guns` /
`.equipment` ("gun, starboard" / "gun, port", one per fitted gun — no longer
"gun pair N") now read straight off the fitted equipment.

**The dry dock** (`DryDock`, pure, JSON, `ShipyardService.Dock` a copy of
the live one). Counted by module id, generic — not gun-specific. `ShipSave`
gets an ADDITIVE `dryDock` field next to `modular` (same pattern: empty
string while the dock is empty, `SaveData.CurrentVersion` unchanged, an
old/missing field is an empty dock). `ApplyRefit(expected, draft)`, after
`Validate` passes: diffs `expected.equipment` against `draft.equipment` **by
module id** (`DryDock.Diff` — a pure move to a different slot, same module,
same count, is not a delta and costs nothing); anything missing from the
draft goes INTO the dock, anything new comes FROM it. If the diff is
non-empty it additionally requires `AnchorController.AtHomeDock`
(`DRY_DOCK_NOT_HERE` otherwise) and enough stock for every "from the dock"
delta (`NOT_IN_DRY_DOCK` otherwise) — checked BEFORE the hull is rebuilt, so
either refusal leaves the ship untouched; a plain move needs only the usual
`CanRefitNow`. The hull rebuild and the dock update commit together right
before `Persist`; if the save fails, both roll back with the ship.

**Minimal usage** (fit a gun from the dock, on top of §8's pattern):

```csharp
var draft = yard.ReadCurrent();
var slots = svc.EquipmentSlots(draft);              // pick an empty, usable one
var edit = svc.FitEquipment(draft, slots[0].slotId, "equipment.cannon.astra.v1");
if (edit.ok) draft = edit.draft;                     // else show edit.message
var preview = svc.DryDockPreview(draft);             // grey out a module with 0 in stock
if (string.IsNullOrEmpty(yard.Validate(draft)) && yard.TryApply(expected, draft, out string why)) { /* done */ }
```

**Deviations from the brief that built this** (2026-09-25, noted so Kevin
can revisit): ~~`CannonBattery.Fit` still mirrors a single STARBOARD list to
port... a lone unpaired gun does not get a correctly-paired live
`CannonBattery` gun~~ — FIXED (2026-09-25): the battery is now fit from the
plan's `fittedGuns` directly, one gun per side, no mirroring (see above); a
lone unpaired gun draws correctly (`ModularShipView`, unchanged) AND fires
correctly (gates `long-guns-3-port-3-starboard`, `short-guns-2-port-2-starboard`,
`port-gun-removed-keeps-starboard-unmoved-at-3` in `ShipyardSelfTest`; live
check `battery: 5 guns, 2 port, 3 starboard (no mirrored phantom)` in
`ShipyardRefitProbe`). `equipment.cannon.astra.v1`'s `boundsMinU`/
`boundsMaxU` (the placeholder-box fallback) are estimated from
`export-verification.json`'s overall dimensions, not re-measured from the
imported FBX — fine for now since the mesh usually resolves, worth
confirming after import. A v1→v2 save migration always seats the middle
pair on the FIRST bay (matching what `WithMiddles` itself builds), not the
geometrically-nearest bay the old implicit rule would have chosen for a
2- or 3-bay ship refitted before this change shipped — no real save has ever
had that shape yet, so this was chosen for simplicity over exactness.

## §16 Raised deck (docs/RAISED-DECK.md), C# half, 2026-09-25 (Claude/Sonnet)

Brief section only; the full spec, numbers and self-test list live in
docs/RAISED-DECK.md secs 6-10 (data pass) and its "For the C# half" note.

- `ModuleDef.upperStructure` (new `UpperStructureSpec`, `ModuleSchema.cs`)
  and `JoinProfile.upperDeckZU` are read now; `hull.*.w1xr.v1.json`'s and
  `standards.json`'s existing fields were silently dropped before this.
- `ShipAssembler` refuses `RAISED_DECK_BAYS` (family `W1xR`, 0 or ≥3
  middles) and applies the `-0.84 u` chimney midpoint offset for family
  `W1xR` (a code constant, not a schema field — see the socket note in
  `hull.stern.w1xr.v1.json`). Mixing raised with W1x/W1-r2 was already
  refused by the existing `JOIN_PROFILE_MISMATCH` check (a raised socket's
  standard is `W1xR`, which no other family's socket names) — no new code
  needed there, only a self-test gate (`RaisedDeckValidation.cs`).
- `HullFormData.RaiseDeck(extraY)` appends one wall-sided top level to every
  station above its old `deckY`, and raises `deckY` itself — the single
  mechanism that both extends the freeboard/deck-immersion physics AND
  converts every consumer of `station.deckY` (crew posting, `DeckLoadPlan`,
  the helm) to the new walk height in one step; see `ShipyardPlan.walkDeckZU`
  for the informational plan value (1.76 / 4.20) and `RaisedDeckPhysics.cs`
  for how `ShipyardPlanner.PlanFor` finds the installed raised profile and
  calls it, and for the CoM/GM/roll-gyradius reweighting alongside it.
- `RaisedPresets.cs` mirrors `ExpandedPresets.cs` one level up: `RaisedLong()`
  (1 middle), `RaisedTwoBay()` (2 middles); no `RaisedShort()` — 0 middles is
  refused by `RAISED_DECK_BAYS`.
- `ShipyardDraft`/`ShipyardScreen` (UI/ModularYard): `IsRaisedDeck`,
  `RaisedDeckUnavailableReason()`, `SetRaisedDeck(bool)` mirror
  `IsWideBeam`/`SetWideBeam` one level up; a "Deck: Single / Raised" row next
  to "Beam: Standard / Wide", `Maximum`/`Minimum`/`RemovalReason()` clamp the
  +/- bay controls to 1-2 while raised, `SetWideBeam` refuses leaving wide
  while raised. **Not compiled by `tools/modular-selftest.sh`** (UI/ is out
  of its scope, same as before this change) — needs Kevin's Unity pass.

## §17 Raised SECTIONS (per-section deck level, docs/RAISED-SECTIONS.md), C# half, 2026-09-25 (Claude/Opus)

Worktree `/Users/kevinandersson/Desktop/SeaSick-modular`, branch
`modular-ships`, from f19e799 (§16's all-raised C# already merged). Unity
never launched this session either; `tools/modular-selftest.sh` went
174 → 189 PASS, 0 FAIL. Items NOT done this session (UI, art/data fixes,
probes) are in docs/RAISED-SECTIONS.md's own implementation-notes section.

- `RaisedSections.cs` (new): `ToIds(stern, middles[], bow)` — pure,
  `DeckLevel.Low`/`.Raised` per section → the module id the joint rule
  calls for (both-raised faces join connected/no-wall; otherwise the
  raised face gets its own end wall, the `wa`/`wf`/`wb` variants).
  `FromIds` is the inverse (read a configuration's ids back to levels,
  for the UI). Both operate entirely inside the W1x/W1xR id space — "raised
  needs wide beam" holds by construction, `Low` here is never the w1r2 id.
  `RaisedSectionsValidation.cs` enumerates all 60 level combinations for
  0-3 middles against the real `ShipAssembler` (59 assemble; the one
  refusal is 0 middles with both ends raised), checks 3 hand-built wrong
  pairings hit `JOIN_PROFILE_MISMATCH`, round-trips `FromIds`/`ToIds`, and
  adds a mixed-ship (raised stern, low middle, low bow) physics/GM report.
- `ShipAssembler.RAISED_DECK_BAYS` generalised (docs/RAISED-SECTIONS.md
  sec 5): now only refuses 0 middles with BOTH ends raised, not every
  raised ship outside 1-2 middles — the old cap predates per-section
  raising. 3 fully-connected raised middles now assembles (flagged
  unverified against Astra's art, which only ever rendered 1-2 bays).
- Chimney's `-0.84 u` midpoint offset now requires the WHOLE installed
  hull chain to be raised, not just the stern — on a mixed ship (raised
  stern, low rest) the stern's own Chimney socket already carries the
  plain midpoint. Z needed no change: each hull module's own socket
  already bakes in its own section's deck height (1.76/4.20), for every
  variant including the wall ones, so "Z = the deck of the section it
  stands in" was already correct by construction.
  **Not done**: the chimney/stairwell overlap check (sec 5's "must not
  overlap a stairwell, move it aft") — no geometry for it was wired up
  this session.
- Physics generalised to per-section (docs/RAISED-SECTIONS.md sec 6):
  `HullFormData.RaiseDeck` gained a `(extraY, fromZ, toZ)` overload that
  only raises stations inside the given range (the old `RaiseDeck(extraY)`
  is now a thin unbounded-range wrapper, so every all-raised caller is
  unaffected). `RaisedDeckPhysics.FindRaisedSectionRanges` gives one
  Z-range per raised hull section (module bounds → ship-frame metres →
  `ShipyardPlan.data`'s own frame via `viewOffset`, the same conversion
  the funnel bounds already used); `ShipyardPlanner.PlanFor` calls
  `RaiseDeck` once per raised section instead of once for the whole hull.
  Mass/CoM/gyradius needed NO change: `RaisedDeckPhysics.RaiseCoM` already
  summed every placed module's own `upperStructure` block (only raised
  modules carry one), so it was already per-section correct even under
  the old all-or-nothing deck-height code. `ShipyardPlan.walkDeckZU` stays
  informational, now "the highest deck anywhere on the ship" rather than
  "the deck" — each station's own `deckY` is the value every real
  consumer (crew, deck load, helm) reads.
- **Not done this session** (budget): the UI (`ShipyardDraft`/
  `ShipyardScreen` tap-to-toggle-per-section, task item 4), the art/data
  fixes (`VisualPart` rotation field, wall-count offset note, task item 3),
  and the probes (`ShipyardRefitProbe`, `ShipyardUiProbe`,
  `ModularShipPreview`, task item 6) — none of these were started. The
  existing all-raised UI (§16, `IsRaisedDeck`/`SetRaisedDeck`) still works
  unchanged; it simply does not yet expose per-section control.

## §18 Interior space budget per section (step 2, docs/SHIPYARD-SECTIONS-UI.md),
## C# half, 2026-09-25 (Claude/Opus)

Worktree `/Users/kevinandersson/Desktop/SeaSick-modular`, branch
`modular-ships`, HEAD 51f871b. Unity never launched this session either;
`tools/modular-selftest.sh` went 192 → 202 PASS, 0 FAIL. Backend only --
the Interior UI page (docs/SHIPYARD-SECTIONS-UI.md step 2) is a separate
agent's work, against the exact names below.

- `ShipConfiguration.layouts : List<SectionLayout>` (`SectionLayout { string
  section; int berths; }`, keyed like `equipment` -- "stern", "middle[0]",
  "bow"). Additive, no schemaVersion bump; a missing entry (every save
  written before this field existed included) is the section's AUTHORED
  default. `Clone`/`ValueEquals`/JSON round-trip all cover it (gated).
- `standards.json` gained `"berthSpaceUnits": 0.5` (one berth's cost, hold-
  cell units); `ModuleLibrary.BerthSpaceUnits` reads it, falling back to 0.5.
  `ModuleSchema.CapacitySpec` gained an optional `maxBerths` (`ProvisionalInt`,
  null = uncapped by floor area) -- no module authors one yet.
- A section's budget = authored `holdCells` + `berthSpaceUnits` * authored
  `berths` (hold-cell units); DEFAULT berths (no `layouts` entry) reproduce
  the authored `holdCells` EXACTLY, by construction (subtracting what was
  just added back cancels bit-for-bit) -- gated over every one of the 14
  hull modules in Resources/ShipModules/Modules, not just the 3 standard
  ones. `maxBerths` = `floor(budget / berthSpaceUnits)`, capped by the
  module's own authored `maxBerths` if it has one.
- `ShipyardPlanner.SectionSpaceFor(ModuleDef, ModuleLibrary, ShipConfiguration
  cfg, string sectionKey, out budgetUnits, out berthCost, out berths,
  out holdCells, out defaultBerths, out maxBerths)` is the ONE place this is
  computed; `cfg` may be null (= every section at its default). Both
  `ShipyardPlanner.SectionCapacities` (now `(asm, lib, cfg, viewZ, out
  missing)` -- the old 4-arg overload still exists, `cfg` null) and the new
  `ShipyardInterior.SectionSpace` call it, so the applied capacity (what
  `PlanFor`/`ApplyRefit` build the ship from) and the UI's own numbers can
  never disagree. `berths` is clamped to `[0, maxBerths]` even for an
  out-of-range `layouts` entry (a hand-edited save), so `holdCells` is never
  negative.
- Existing rules already see it, no change needed: `CheckRetention`'s
  `CARGO_WOULD_NOT_FIT` reads `draft.capacity.holdCells`; `GUNS_NEED_CREW`
  and `handsAshore` read `draftPlan.capacity.crewStations` -- both now sums
  of the layout-aware `SectionCapacity.holdCells`/`.berths`.
- `ShipyardInterior` (new, pure, same shape as `ShipyardEquipment`):
  `SectionSpace(ShipConfiguration draft, string sectionKey, ModuleLibrary
  lib) -> SectionSpaceView { string section; float budgetUnits; float
  berthCost; int berths; int holdCells; int defaultBerths; int minBerths;
  int maxBerths; string reason; }` (`reason` non-empty = unreadable: does
  not assemble, no such section, or `NO_CAPACITY`). `WithBerths(draft,
  sectionKey, berths, lib) -> ShipConfiguration`, clamped, an UNCHANGED copy
  on a bad section. Setting berths back to the section's default REMOVES its
  `layouts` entry (rather than writing a redundant one) so a round trip to
  default and back stays `ValueEquals` to an untouched draft.
- `ShipyardService.SectionSpace(draft, sectionKey)` / `.WithBerths(draft,
  sectionKey, berths)` are thin wrappers (fill in `Library`);
  `ShipyardRefitAdapter` mirrors both exactly for the UI, returning a
  `reason`-only view / an unchanged clone when there is no ship.
- `ShipConfiguration.ShiftMiddleKeys(ShipConfiguration cfg, int fromIndex,
  int delta)` (static; the config is the first argument -- the design doc's
  short-form call omits it): renumbers every `"middle[i]"` key `>=
  fromIndex` by `delta` (+1 insert, -1 remove) in BOTH `equipment` slot ids
  and `layouts[].section`, in place, leaving a key that would go negative
  untouched. `ShipyardPlanner.Occupancy`'s own middle-removability probe
  (`SectionOccupancy.canRemove`) now calls it instead of a hand-rolled
  equipment-only loop, so its layouts move with the bay too. The UI's own
  draft (`UI/ModularYard/ShipyardDraft.cs`, `InsertMiddle`/`RemoveSection`)
  may call this directly or keep an equivalent for `equipment` alone --
  either way the KEYING SCHEME must stay identical (both lists keyed
  exactly like `ShipAssembler.MiddleKey`).
- Save: no new field -- `layouts` rides inside the existing
  `ShipConfiguration` JSON the save's `ship.modular` field already carries
  (`ModularSave.Encode`/`Decode`), so it needed no code change; verified by
  a pure JSON-path self-test gate and a live save/load round trip in
  `ShipyardRefitProbe`.
- `ShipyardRefitProbe` gained a refit case with a non-default layout: stern
  all hold, `middle[0]` at max berths, BUILT through
  `SectionSpace`/`WithBerths` (never a hand-built `SectionLayout`), checked
  against the live `ShipyardPlan.sections` after `ApplyRefit` and against
  `voyage.HoldCapacity`, then round-tripped through a real save/load (the
  existing `ValueEquals` save gate now covers `layouts` for free; one more
  gate checks the hold capacity survives too).
- Self-test: 10 new gates --
  `interior-default-layout-reproduces-authored-capacity-every-module` (all
  14 hull modules), `interior-section-space-matches-planned-section`,
  `interior-with-berths-zero-is-all-hold`,
  `interior-with-berths-clamps-both-ways`,
  `interior-with-berths-back-to-default-clears-layouts-entry`,
  `interior-section-space-unknown-section-has-reason`,
  `shift-middle-keys-insert-renumbers-layouts-and-equipment`,
  `shift-middle-keys-remove-renumbers-layouts-and-equipment`,
  `layouts-json-round-trip`, `missing-layouts-field-is-defaults`.
- **Not done this session** (out of scope, explicitly the UI agent's):
  the Interior page itself (`UI/ModularYard/`), `InsertMiddle`/
  `RemoveSection`/`BeginSection`/`ResetSection` (step 1), and
  `ShipyardUiProbe`'s Interior +/- clamp coverage.

## §19 Slots API (2026-09-27, phase 1 of the slot shipyard) — for the phase-2 screen

Kevin's decisions: the player fills small plan grids with **modules** (cannon,
bunk, crate, bilge pump, lookout, repair bench); capacity = the **sum of the
fitted modules** (the sections' authored capacity blocks and `layouts` no longer
count for a slot ship). One gun port per side per deck above the Hold (the Hold
has none); cannons only in ports. Modules are items built at the dock (FREE +
INSTANT for now) and kept in the dry-dock store; removing a section or lowering
a deck returns its modules to the store (never stranded). Slots are the only
limit (no module-count cap). The stern has a FIXED 2-berth cabin that is part
of the hull, so an empty hull can sail. Old saves convert automatically:
today's Long = 6 cannons + 4 crates + 3 bunks = **16 cargo, 8 berths (2 cabin +
6), 6 guns on the same mounts, same mass and draft**. Design page:
`scratchpad/shipyard/index.html`.

### Data

| What | Where |
| --- | --- |
| Grids, cabin, deck heights, catalog list, dock levels 1–5 | `Resources/ShipModules/standards.json` → `slotModel` (`SlotModelDef`) |
| One file per buildable module | `Resources/ShipModules/Catalog/module.*.json` (`CatalogModule`) |
| Schema classes | `Scripts/Ship/Modular/SlotSchema.cs` |
| Pure rules (layout, migrate, normalize, check, totals) | `Scripts/Ship/Modular/SlotModel.cs` |
| Read model + draft (UI-facing) | `Scripts/Ship/Modular/ShipyardSlots.cs` |
| Gates (32) | `Scripts/Ship/Modular/SlotModelValidation.cs` (runs inside `tools/modular-selftest.sh`) |

- **Decks** (`SlotDeck`): 0 `Hold`, 1 `Deck`, 2 `Upper`, 3 `Top`. Hold + Deck are
  always open. Upper opens on a RAISED section (wide beam, W1xR) or under the
  foredeck on the standard bow; Top opens under a third-deck layer.
- **Grids** (per section kind × deck × beam): Hold stern 2 / mid 4 / bow 2
  (wide 3 / 6 / 3); Deck/Upper/Top stern 2 ports + 1, mid 2 ports + 2, bow
  2 ports (wide: +1 / +2 / +1 centre-row cells). Long = 8 + 9 = 17 cells,
  6 ports. Cell ids are stable per grid: `P0`, `S0` (the gun ports on decks
  1–3), `P1`, `S1`, `M0`, `M1`. Rows: `Port`, `Mid`, `Stbd`.
- **Catalog** (all PROVISIONAL): cannon (port only, 1 hand, 500 kg, dock I,
  `equipmentId` = `equipment.cannon.astra.v1`), bunk (2 berths, 150 kg, I),
  crate (4 cargo, 120 kg, I), bilge pump (Hold only, one per ship, II), lookout
  (top-most deck only, one per ship, III), repair bench (any cell incl. a port,
  1 hand, one per ship, IV). `placement`: `port` | `inner` (not a port) | `any`
  | `hold` | `topmost`. Build prices are data (`buildCost`), unused while free.
- **Dock levels** (`DockLimits.For(lib, level)` → `DockLevelDef`): I Slip 3
  sections, Hold+Deck, standard beam · II Wide slip + wide · III Crane 4
  sections, Upper on 3 · IV Covered yard 5 sections, Upper on all, Top on 2 ·
  V Master yard Top on all. Upgrade costs/fire level are data only. **Phase 1
  does not enforce the dock building's level**: `ShipyardService.DockLevel`
  defaults to `DockLimits.Unenforced` (5); phase 4 sets it from the building.

### Configuration (schema 3)

```csharp
public class ShipConfiguration {        // SupportedSchemaVersion = 3
    public List<SlotFit> fits;          // ADDITIVE; empty + ignored below schema 3
    public bool UsesSlots { get; }      // schemaVersion >= 3
}
public class SlotFit { string section; int deck; string cell; string moduleId; }  // "middle[0]", 1, "P0", "module.cannon"
```
- A new `ShipConfiguration` and every preset (`Long()`, `WithMiddles`, …) is
  still **schema 2** (legacy path unchanged). `SlotModel.Migrate(cfg, lib, out
  overflow)` makes it schema 3; `ModularSave.Decode(...)` now always returns a
  slot config (an old save → migrated; `out overflow` = catalog ids with no cell,
  which `ShipyardService` puts in the store). `ModularSave.StandardSlots(lib)` =
  the standard steamer as slots.
- In a slot config the deck-gun `equipment` is **derived** from the cannon fits
  (`SlotModel.Normalized`): each cannon in a port becomes an `EquipmentChoice` on
  that section/deck/side's authored gun slot, so the assembler, `FittedGun`s and
  `CannonBattery` work unchanged. A cannon in a port with no authored mount (e.g.
  a Top deck) still counts, warns `GUN_PORT_NO_MOUNT`, and is not drawn.
- `ShiftMiddleKeys` renumbers `fits` too. `DryDock.Diff` counts **fits** for
  slot configs (store keys are catalog ids; an old store's
  `equipment.cannon.astra.v1` is renamed `module.cannon` on load).
- Mass: non-cannon modules are in `lightshipKg` as (fitted − the standard
  fit-out 930 kg), because the hull masses were calibrated on today's steamer,
  which already carries that fit-out; cannons stay in `gunsWeightKg`. The fitted
  modules' total mass and centre are on `ShipyardPlan.fitMassKg / fitCentreM`
  (report figures `fitMass`, `fitCentreHeight`) — **report only: the sailing
  model's CoG does not follow them yet**.

### The draft (what the screen calls)

```csharp
// From the live ship:
ShipyardSlotDraft d = ShipyardService.Player.BeginSlotDraft();
// Detached (preview/tests): new ShipyardSlotDraft(lib, currentCfg, store, backend = null, dockLevel = -1)

ShipyardSlotsView View();                     // everything the screen shows, plain data
List<ModuleOptionView> Options(string sectionKey, int deck, string cellId); // the add drawer

bool Place(string sectionKey, int deck, string cellId, string moduleId);  // empty cell, from the store
bool BuildAndPlace(string sectionKey, int deck, string cellId, string moduleId); // one undo step
bool Remove(string sectionKey, int deck, string cellId);                  // -> store
bool Move(string fromSection, int fromDeck, string fromCell,
          string toSection, int toDeck, string toCell);                   // filled target = swap
bool BuildModule(string moduleId);                                        // free, instant, undoable
bool AddSection(int index);            // 0 = behind the stern .. middleIds.Count = before the bow
bool RemoveSection(string sectionKey); // middles only; its modules -> store
bool RaiseDeck(string sectionKey);     // opens the next deck (Upper, then Top)
bool LowerDeck(string sectionKey);     // takes the top deck off; its modules -> store
void Undo();                           // one step, builds included
ShipyardSlotsView Validate();          // = View()
bool Apply();                          // builds + refit + save, atomic (ApplyRefit)

string Message, MessageCode;  bool Dirty, CanUndo, Applied;  int Changes;
event Action Changed;          IReadOnlyList<string> Builds;  DryDock Store();
```
Every command either changes the draft (one undo step, `true`) or refuses with
`Message`/`MessageCode` and changes nothing. After every change the draft is
normalized: anything with no place any more leaves the draft and shows up in the
store (`Message` says how many). Nothing touches the ship, store or save until
`Apply`.

### The read model (`ShipyardSlotsView`)

```
sections[]  SlotSectionView { key, kind, name ("Mid 1"), moduleId, wide, topDeck,
              canRemove/removeReason, canRaise/raiseReason, canLower/lowerReason,
              decks[4] SlotDeckView { deckIndex, name, unlocked, lockedReason, isNextRaise,
                used, total, cells[] SlotCellView { cellId, row (Port/Mid/Stbd),
                isGunPort, hasMount, moduleId or null } } }
totals      SlotTotalsView { guns, gunPorts, crew (aboard), berths, cargo (aboard),
              cargoCap, cells, cellsUsed, draftM (NaN = unknown), fitMassT }
blockers[]  SlotIssueView { code, text, fixId }     // stop Confirm
warnings[]  SlotIssueView { code, text, fixId }
store[]     StoreRowView { moduleId, name, description, inStore (after this draft),
              aboard, locked, dockLevel, onePerShip, placement, cargo, berths, crew, massKg }
dockLevel, dockName, dirty, canUndo, canApply, changes, message, messageCode,
builds[], refitNowBlockedBecause
```
`fixId`: `remove:<section>/<deck>/<cell>`, `build:<moduleId>`, `add-bunk`,
`unload`, `lighten`, `upgrade-dock` ("" = no fix button).
`ModuleOptionView { moduleId, name, action, reason, inStore }`, `action` =
`place` | `build-place` | `move-here` (one-per-ship fitted elsewhere) |
`not-here` | `locked`.

### Codes

Blockers (plus every existing code: `CARGO_WOULD_NOT_FIT`, `OVERLOADED`, …):
`SLOT_WRONG_KIND`, `MODULE_LOCKED`, `ONE_PER_SHIP`, `DOCK_LEVEL`, `NO_CATALOG`,
`MODULE_UNKNOWN`. Warnings: `GUNS_SHORT_OF_HANDS` (cannons > hands aboard, capped
by berths — replaces the `GUNS_NEED_CREW` blocker for slot ships),
`GUN_PORT_NO_MOUNT`, and the existing `HANDS_ASHORE`. Command-only refusals:
`SLOT_OCCUPIED`, `SLOT_UNKNOWN`, `DECK_LOCKED`, `NOT_IN_STORE`, `TOO_MANY_MIDDLES`.

### Backend seam

`IShipyardSlotsBackend { ReadCurrent(); ReadStore(); DockLevel; CrewAboard;
CargoAboard; Report(draft); TryApply(expected, draft, builds, out reason) }` —
`ShipyardService` implements it; `ShipyardService.ApplyRefit(expected, draft,
builds)` is the new overload (builds join the store in the same atomic step; the
old 2-argument call still works and migrates a v2 draft). `ShipyardService.Current`
is always a slot config now.

### Legacy screen (phase 1 left it in place)

The old `UI/ModularYard` screen keeps working on the slot config: `ShipyardDraft`
normalizes every edit, its `FitGun`/`RemoveGun` fit/remove a cannon in the port
that mount serves, `RemoveSection` sends the section's fits to the store. Its
Interior berths slider (`layouts`) has **no effect** on a slot ship — phase 2
replaces it with the grids.

### The slot screen (phase 2+3, 2026-09-27)

`ShipyardLiveBridge.Open()` (dry dock sheet, Ship sheet, Ledger) now opens
`ShipyardModal.OpenSlots(ShipyardService.Player, ...)` ->
`UI/ModularYard/Slots/ShipyardSlotsScreen` (tabs Ship / Workshop / Dock, strip,
section card + slot grid, drawer, press-and-hold drag, totals, blocker bar,
pinned Confirm -> `ShipyardSlotDraft.Apply`). `ShipyardSlotsAdapter.Build`
maps `ShipyardSlotsView` + `YardUiState` -> `YardVm`; view cell ids are
`"<section>/<deck>/<cell>"` (same as a `remove:` fix id). `SlotCellView.along`
(additive) gives the grid its columns. No 3D preview and the live ship is not
hidden (only world input is blocked). The old screen (`ShipyardScreen` +
`ShipyardPreview`) is reachable only with the dev flag
`ShipyardModal.UseLegacyScreen = true`.
