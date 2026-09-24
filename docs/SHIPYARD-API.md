# Shipyard API (prototype) — for Astra's UI

Branch `modular-ships`, 2026-09-24. Backend for the first playable shipyard:
low-deck W1-r2 ships, 0–3 middle bays, timber or reinforced M1 wheel. The
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
| `IReadOnlyList<string> AllowedModuleIds(string kind)` | For your pickers (`ModuleKind.*`). Stern/Middle/Bow: the V3 W1-r2 part; Rotor: timber, reinforced; Carrier: M1; Fitting: chimney; UpperDeck/Equipment: empty. |
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
    public int holdCells, cargoCells;        // share of the hold (by table volume to the deck) / cargo attributed pro rata
    public int berths, crew;                 // deck stations in it / hands posted there
    public List<string> equipment;           // "gun pair 2", "funnel", "deck load pile 1"
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
| `lightship` | sum of the sections' lightship masses (module data) (t) | yes |
| `draft` | as loaded, above the keel, **from module station tables** (m); unavailable = above the deck line | yes |
| `simDraft` | what the live physics floats her at — **reshaped reference hull (approximation)** (m) | yes |
| `loadLine` | displacement at the load line (t) | yes |
| `holdCells` | hold **volume** capacity (today's HoldCells) | yes |
| `weightAllowance` | cargo **weight** room with the hands now aboard (t) | yes |
| `cargoWeight` | what the cargo aboard weighs (t) | yes |
| `crewBerths` | crew stations | yes |
| `gunSlots` | usable gun/equipment slots (0: nothing can be fitted yet) | |
| `deckSlots` | deck slots the hull reserves (reserved, not usable yet) | yes |
| `guns` | guns she carries (from her hull form) | |

Warnings today: `HOLD_SMALLER`, `FEWER_BERTHS`, `PROVISIONAL_TUNING`.
Numbers you get for the three standard ships (headless self-test):

| | Short | Long (today) | 3 bays |
| --- | --- | --- | --- |
| hull / overall | 8.88 / 10.14 m | 11.88 / 13.14 m | 17.88 / 19.14 m |
| sailing model lwl, mass | 9.42 m, 23.8 t | 12.60 m, 31.9 t | 18.97 m, 48.0 t |
| module lightship (tables) → draft | 21.9 t → 0.86 m | 31.9 t → 0.84 m | 52.0 t → 0.83 m |
| hold cells / berths | 11 / 4 | 16 / 8 | 24 / 14 |
| cargo weight room (hands) | 4.3 t (4) · 3.9 t (8) | 8.0 t (8) | 16.2 t (8) |
| deck slots / guns | 6 / 6 | 10 / 6 | 18 / 6 |

Short's weight room (4.3 t ≈ 8 units) binds before its volume (11 cells).

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
| `NOT_IN_PROTOTYPE` | raised deck, wider hull (W2), oversized wheel, any equipment, placeholder / incompatible-reference part, a fitting other than the chimney on `stern/Chimney` | "Raised decks are not part of the prototype shipyard yet." / "Oversized wheel (M1-L) is not part of the prototype shipyard: only the M1 timber and reinforced wheels are offered." |
| `WHEEL_REQUIRED` | no rotor | "A paddle steamer needs her wheel; choose a timber or reinforced M1 wheel." |
| `CARGO_WOULD_NOT_FIT` (partId `hold`) | cargo units > new hold cells | "She is carrying 16 loads and this ship's hold takes 11. Unload 5 first; nothing is thrown overboard." |
| `CARGO_WOULD_NOT_FIT` (partId `weight`) | cargo weight > new weight room | "Her cargo weighs 5.5 t and this ship can carry 5.4 t with 4 hands and her guns aboard. …" |
| `CREW_WOULD_NOT_FIT` | hands aboard > new berths | "8 hands are aboard and this ship has stations for 4. Land 4 first." |
| `EQUIPMENT_WOULD_BE_LOST` | something positioned on her (the funnel, a gun pair, a deck-load pile) has a place now and none on the draft | "Gun pair 2 would have no place on this ship; the refit is refused rather than leave it behind." |
| `STALE_DRAFT` | live config ≠ `expected` | "The ship changed since this plan was drawn up. Look again and confirm." |
| `OVERLOADED` | the loaded mass would float her above the deck line (downflooding) — never clamped | "At 85.6 t she would float above her deck line and flood. Lighten her first." |
| `NO_HYDROSTATICS` | a hull section has no (valid, hash-matching) station table | |
| `CANNOT_REFIT_NOW` | §6 | the reason sentence |
| `APPLY_FAILED`, `SAVE_FAILED` | the rebuild / the save failed; she was put back | |
| `NO_REFERENCE_HULL` | the steamer's hull form is missing | |

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

Allowed: `hull.stern.w1r2.v3`, 0–3 × `hull.middle.w1r2.v3`, `hull.bow.w1r2.v3`,
`wheel.rotor.m1.timber` / `wheel.rotor.m1.reinforced` on `wheel.carrier.m1`,
`fitting.chimney.v3` on `stern/Chimney`. Excluded: raised decks, wider hulls
(their definitions need validation), the oversized wheel, deck equipment,
costs. Timber ↔ reinforced changes **only the rotor's drawing**: same radius,
same physics, no bonus. **Meshes are never stretched** to make width/depth
variants; only the PHYSICS data is reshaped, and only from authored module data (A9).

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
definition (documented in the method; e.g. mass ∝ sL·sB·sD, BM ∝ sB²/sD, wheel
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
(+2.3 %); volume to the deck 76.46 m³. They agree for Long. They drift apart
with length: Short lightship 21.9 t (modules) vs 23.8 t (sailing model), 3
bays 52.0 t vs 48.0 t (±8 %), because the sailing model stretches the whole
steamer while the tables add real bays. The drawn datum (Z 0) sits 0.116 m
above the table waterline; the view hangs it on the physics waterline.
**Next step (proposed, not built):** generate `HullFormBody`'s station data
(half-breadth / area / moment per level) from the module tables — the export
would need half-breadth and moment per station as well as area — so the sim
and the report share one geometry source.

### Provisional: two hydrostatic models

The shipyard report floats a ship on the module station tables; the game
sails her on the reshaped reference hull. Both drafts are in the report
(`ShipyardReport.tableDraftM` / `simDraftM`, and `…CurrentM`; figures `draft`
and `simDraft`), both PROVISIONAL while the models differ. Static values
(headless self-test, gate `two-hydrostatic-models-table`; the sim column is
HullFormBody's own strip sum, `AreaAt(level) × dz`, solved for the mass).
Timber and reinforced rotors carry no mass of their own, so one row per
length covers both:

| config | lightship (modules) | table draft | sim static draft | Δ | Δ % | sim at its own mass |
| --- | --- | --- | --- | --- | --- | --- |
| 0 bays (Short) | 21.87 t | 0.859 m | 0.801 m | −0.059 m | −6.8 % | 23.85 t → 0.861 m |
| 1 bay (Long, today) | 31.91 t | 0.844 m | 0.861 m | +0.017 m | +2.0 % | 31.91 t → 0.861 m |
| 2 bays | 41.95 t | 0.837 m | 0.896 m | +0.059 m | +7.1 % | 39.97 t → 0.861 m |
| 3 bays | 51.99 t | 0.832 m | 0.919 m | +0.087 m | +10.5 % | 48.03 t → 0.861 m |

The sim floats every length at exactly its design draft at its own mass (its
mass is ρ·V of the stretched form by construction); the tables say a longer
W1-r2 ship at the modules' lightship sits slightly shallower. The DYNAMIC
column — the keel's depth below the local sea surface, moored at rest after
each refit, at her actual rigidbody mass — is printed by the play-mode probe
(`Logs/ShipyardRefitProbe.txt`, "PROVISIONAL two hydrostatic models"); it has
not been run yet. Nothing is gated on agreement.

**Lightship per module (H5).** Each hull module's `lightship.massKg` is
provisional data seeded so stern + middle + bow = today's 31 906.6 kg (hullform
massKg × 0.42³), split by each section's table volume to the deck: stern
12 498.2, middle 10 039.4, bow 9 369.0 kg. Wheel, carrier and chimney carry 0
(today's mass is one lump).

**Capacity (D9) — derived today, NOT yet authored per module (A4 pending).**
Hold cells = 16 × (volume below deck ÷ Long's), floor, min 4. Berths = 8 ×
(usable deck strip ÷ Long's), whole pairs. Weight room (A2) = displacement at
the load line − lightship − crew × 90 kg − guns × 500 kg, using `ShipLoad`'s
weights (`CargoUnitKg` 500, `CrewKg` 90; the steamer's guns have no calibre,
so the lightest, 500 kg — provisional), all floated on the module tables. The
load line is a freeboard margin below the deck line, anchored so Long's full
hold with 8 hands and 6 guns just reaches it (0.738 m below the deck line);
the same margin applies to every hull. **Cargo
weight is checked, not felt**: cargo mass does not reach the steamer's
physics today (only the ladder ships' `ShipLoad` puts it on the rigidbody).
Raised decks later: an UpperDeck module adds deck area (berths, slots) and a
second `deckY` level; depth for physics stays the main hull's keel-to-deck.

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

* Headless: `tools/modular-selftest.sh` → `ModularShipSelfTest: 87 PASS, 0 FAIL`
  (45 milestone-1 + 42 shipyard: the two-models draft table, hydrostatic tables (load, prow never
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
2. A4 authored per-module capacity blocks are not implemented. Proposed seed
   (sums to today on Long): hold stern 6 / middle 5 / bow 5; berths 3 / 2 / 3;
   gun slots stern `DeckSlot_2_±1`, middle `DeckSlot_1_±1`, bow `DeckSlot_0_±1`
   (Long = 6 = today's guns). Consequence to decide: Short would then have 4
   gun slots for her 6 hull-form guns and every Short refit would be refused
   `EQUIPMENT_WOULD_BE_LOST` until guns move from the hull form to module slots.
3. Short has 4 berths: with today's 8 hands aboard, Short is refused
   (`CREW_WOULD_NOT_FIT`) until 4 are landed.
4. Cargo weight is checked but has no physical effect on the steamer.
5. Bow stem X 8.45 was measured by us from the FBX; Astra, please confirm or
   add it to the manifest. (Kevin's brief quoted 23.95 u for the Long hull:
   that is the V2 bow; the V3 raked bow at the waterline gives 23.75 u.)
6. The funnel on refitted ships sits at the assembled midpoint (V3 rule); on
   the untouched ship the deck load is laid out around the V8 art's funnel.
7. The V8 art's other parts (e.g. any smoke rig on `AstraSteamerVisual`) are
   hidden with it on a refitted ship.

## 14. Changed files since ea67f99

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
* `Assets/_Project/Scripts/Ship/HelmInput.cs`, `CameraRig/ChaseCamera.cs`, `CameraRig/IslandInput.cs`, `Combat/CombatLock.cs`, `Ship/CannonBattery.cs` — one-line `ShipyardSession.WorldInputBlocked` gate each.
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
