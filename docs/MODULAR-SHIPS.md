# Modular ships — milestone 1 (foundation)

For Astra (art) and Kevin (design). Status 2026-09-24, branch `modular-ships`.

## 1. Purpose and scope

Players will build ships from compatible **stern + middle bays + bow**, then later
choose hull width/depth, partial upper decks, paddle wheels and deck equipment.
These are trade-offs, not a ladder. Milestone 1 builds only the foundation that
all of that stands on:

| Built in milestone 1 | Where |
| --- | --- |
| Data-driven module definitions (stable ids, versions, sockets, bounds, slots, passages) | `Assets/_Project/Resources/ShipModules/standards.json`, `.../Modules/*.json` |
| Schema classes (JsonUtility) | `Scripts/Ship/Modular/ModuleSchema.cs` |
| Library loader + validation | `Scripts/Ship/Modular/ModuleLibrary.cs` |
| Serializable ship configuration (what the player chose, nothing else) | `Scripts/Ship/Modular/ShipConfiguration.cs` |
| The one unit/axis conversion | `Scripts/Ship/Modular/ModularScale.cs` |
| Assembler: config + library → placements or reasons (pure C#) | `Scripts/Ship/Modular/ShipAssembler.cs` |
| View: draws an assembly, rotor on its own pivot | `Scripts/Ship/Modular/View/ModularShipView.cs`, `RotorSpin.cs` |
| Isolated test bench + scene builder | `Scripts/Dev/ModularShipBench.cs`, `Scripts/Ship/Modular/Editor/ModularShipTestSceneSetup.cs` |
| Self-test (45 milestone-1 gates + 59 shipyard gates, see docs/SHIPYARD-API.md; editor or headless) | `Scripts/Ship/Modular/ModularShipSelfTest.cs`, `tools/modular-selftest.sh` |

It is **isolated from the sailing game**: nothing in `Ship/`, `Steamer/`, `Save/`
or any scene was changed. The existing steamer, fleet ships and saves are untouched.

## 2. Coordinate and scale contract

**Authoring** ("V8 units", Blender): **+X bow, +Y port, +Z up.** All module data is
written in these units and axes.

**Game** (Unity, metres): +Z bow, +X starboard, +Y up.

**The conversion** (the existing V8 FBX export convention), in `ModularScale` only:

```
game = (-y, z, x) * metresPerUnit          metresPerUnit = 0.5 (standards.json)
```

Nothing else converts. Gameplay code never hard-codes 0.5; it reads
`ModuleLibrary.MetresPerUnit`.

**Origins.** A hull module's origin is its **aft connection plane at the ship
height datum** (Z = 0; deck is Z 1.76, keel Z −1.92). Every FBX part of one module
shares that origin. Rotor and carrier FBXs are **axle-local** (origin = axle centre).
The chimney's origin is its base centre.

**Verified on import (2026-09-24, batch Unity on this branch's own project).**
Unity imports Astra's FBXs with an identity root rotation and the convention
above already baked: each hull section runs **+Z from its aft face** (stern
0 → 9.30, middle 0 → 6.00, bow 0 → 10.98 including the prow), beam along X
(shell ±4.64, rails ±4.84), up along Y (keel −1.92). Rotors are axle-local
(M1 radius 1.62, M1-L 2.15, paddle width ±2.30 across X). So
`ModularShipView.visualAxisFix` stays identity. Assembled at 0.5 m/u: short
10.14 m, long 13.14 m, three bays 19.14 m; beam 4.85 m over rails; the wheel
overhangs 0.45 m aft of the stern interface. Any new delivery must import
the same way — the preview tool below prints these numbers for every mesh.

### Why 0.5 m per unit (decided)

Today's in-game steamer draws the same V8 mesh with a *per-axis* stretch
(`SteamerBootstrap.cs` ~161, `HullFormData` scaled by `PlaytestScale` 0.42):

| axis | in game | V8 authoring | factor |
| --- | --- | --- | --- |
| beam | 4.62 m | 9.36 | 0.494 |
| freeboard | 0.819 m | 1.76 | 0.465 |
| waterline length | 12.6 m | 23.95 | 0.526 |

Kevin plays and accepts that ship beside 1.7 m crew (`WorldScale.Person = 1.7`).
Astra's README forbids non-uniform scaling, so the uniform factor is the mean
of the three, **≈ 0.5**.

Consequences Astra should design against:

| Thing | Authoring | At 0.5 m/u |
| --- | --- | --- |
| Short ship (stern + bow), overall | 20.28 | **10.14 m** |
| Long ship (+1 bay), overall | 26.28 | **13.14 m** (today's steamer: 13.5 m loa) |
| Each middle bay | 6.00 | 3.00 m |
| Deck beam | 9.28 | 4.64 m |
| M1 wheel radius | 1.62 | **0.81 m** |
| M1 wheel overhang aft of the stern interface | 1.62 − 0.72 | 0.45 m |
| Rail top above deck (midship rails, measured Z 2.39) | 0.63 | ≈ 0.32 m |
| Chimney height (measured) | 5.12 | 2.56 m |
| Crew height | 3.4 | 1.7 m |
| Study raised deck: walking plane Z 4.42 over main deck 1.76 | 2.66 | **1.33 m headroom — less than a 1.7 m crew** ⚠ |

## 3. The module schema

One JSON file per module in `Resources/ShipModules/Modules/`, named `<id>.json`.
Plus one `standards.json`. Every top-level document has `schemaVersion`; a file
newer than the build understands is refused with a reason, never half-read.
Unknown fields are ignored (so data can gain fields ahead of code).

### standards.json

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Format version of this file (1). |
| `metresPerUnit` | The one authoring→metre factor (0.5). |
| `axisConvention` | Human-readable statement of the mapping above. |
| `maxMiddles` | Most middle bays allowed (3 = what Astra verified closed, 0–3). |
| `joinProfiles[]` | Hull cross-section interfaces: `id`, `version`, `status`, `profilePoints`, `halfBeamU`, `deckZU`, `keelZU`, `description`, `sourceNote`. |
| `mountStandards[]` | Wheel mounts: `id`, `version`, `status`, `nominalRadius`, `sweptRadius`, `paddleWidth`, `radiusLimit`, `pocketCeilingZU`, … |
| `slotClasses[]` | Vocabulary for fitting sockets and equipment slots (`fitting.chimney`, `deck.upper.placeholder`, `equipment.deck-gun`). |

### Module file (`ModuleDef`)

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Format version (1). |
| `id` | **Stable, dotted, never reused**, e.g. `hull.stern.w1r2.v3`. Configurations and saves store only this. |
| `version` | Content revision of this id. Bump for a *compatible* change (same interfaces, same socket meanings). An incompatible change gets a **new id**. |
| `kind` | `Stern`, `Middle`, `Bow`, `Rotor`, `Carrier`, `Fitting`, `UpperDeck`, `Equipment`. |
| `family` | Informational (e.g. `W1-r2`). **Compatibility never comes from family or file names** — only from socket standards. |
| `status` | `prototype` (real geometry, still reference), `placeholder` (no geometry; drawn as a grey box and flagged), `incompatible-reference` (kept only to test rules), later `approved`. |
| `displayName`, `description`, `source` | For people. `source` points at the manifest the numbers came from. |
| `lengthU` | Length along +X from the aft interface (hull sections). |
| `boundsMinU`, `boundsMaxU`, `boundsNote` | Module-local AABB (placeholder boxes, extents). Measured from the FBX where meshes exist. |
| `sockets[]` | `id`, `role`, `standard`, `posU` (module-local), `yawDeg`, `radiusLimit`, `placementRule`, `provisional`, `notes`. |
| `visuals[]` | `id`, `resourcePath` (Resources path, no extension), `placeholder`, `localPositionU`, `notes`. **Purely visual.** `localPositionU` (added 2026-09-25 for the W1x expanded-beam kit) is a module-local authoring offset this ONE part is instantiated at, on top of the module's own origin — zero (the default) for every single-piece visual authored so far; a MULTI-PART kit whose individual FBX pieces carry their own `manifest.json` `local_position` (e.g. the width-inserts family's split Port/Starboard/Insert/Core pieces) sets it per part instead. |
| `rotor` | Rotors: `mount`, `nominalRadius`, `sweptRadius`, `paddleWidth`. |
| `carrier` | Carriers: `mount`. |
| `fitting` | Fittings / upper decks: `socketClass` (which socket standard it plugs into), `originNote`. |
| `equipment` | Equipment: `equipmentClass`, `footprintU` (box: X along, Y across, Z up), `placeholder`. |
| `equipmentSlots[]` | `id`, `socketId`, `clearanceSizeU`, `classes[]`, `provisional`, `notes`. The clearance box has its **base centre at the socket**. |
| `passages[]` | Crew-passage exclusion boxes: `id`, `centreU`, `sizeU` (full size), `provisional`. |
| `physical` | Future mass / displacement / cargo / crew / thrust. **Every value `authored: false`.** They must never be derived from geometry or wheel size — a bigger wheel is not a faster ship. |

### Example: hull section (`hull.middle.w1r2.v3`, trimmed to one deck slot)

```json
{
    "schemaVersion": 1,
    "id": "hull.middle.w1r2.v3",
    "version": 1,
    "kind": "Middle",
    "family": "W1-r2",
    "status": "prototype",
    "displayName": "Midship_W1 (V3)",
    "description": "Repeatable 6.00 u middle bay.",
    "source": "art-staging/modular-hull-family-v3/manifest.json",
    "lengthU": 6.0,
    "boundsMinU": {"x": 0, "y": -4.85, "z": -1.92},
    "boundsMaxU": {"x": 6.0, "y": 4.85, "z": 2.47},
    "boundsNote": "Measured 2026-09-24: union of the module's FBX vertex bounds.",
    "sockets": [
        {"id": "AftSocket", "role": "hull.aft", "standard": "W1-r2", "posU": {"x": 0, "y": 0, "z": 0}},
        {"id": "ForwardSocket", "role": "hull.fwd", "standard": "W1-r2", "posU": {"x": 6.0, "y": 0, "z": 0}},
        {"id": "DeckSlot_0_1", "role": "deck.slot", "standard": "", "posU": {"x": 1.5, "y": 3.45, "z": 1.76}, "provisional": true}
    ],
    "visuals": [
        {"id": "Hull_Shell", "resourcePath": "ShipModules/Meshes/HullW1r2_v3/Midship_W1/Hull_Shell"},
        {"id": "Rails_Teal", "resourcePath": "ShipModules/Meshes/HullW1r2_v3/Midship_W1/Rails_Teal"},
        {"id": "Hull_Ironwork", "resourcePath": "ShipModules/Meshes/HullW1r2_v3/Midship_W1/Hull_Ironwork"},
        {"id": "Plank_Seams", "resourcePath": "ShipModules/Meshes/HullW1r2_v3/Midship_W1/Plank_Seams"}
    ],
    "equipmentSlots": [
        {"id": "DeckSlot_0_1", "socketId": "DeckSlot_0_1", "clearanceSizeU": {"x": 1.8, "y": 1.55, "z": 1.65},
         "classes": ["equipment.deck-gun"], "provisional": true}
    ],
    "passages": [
        {"id": "CrewPassage_Main", "centreU": {"x": 3.0, "y": 0, "z": 3.46}, "sizeU": {"x": 6.0, "y": 5.35, "z": 3.4}, "provisional": true}
    ],
    "physical": {
        "massKg": {"authored": false, "value": 0, "source": ""},
        "displacementM3": {"authored": false, "value": 0, "source": ""},
        "cargoCapacity": {"authored": false, "value": 0, "source": ""},
        "crewCapacity": {"authored": false, "value": 0, "source": ""},
        "thrustCoefficient": {"authored": false, "value": 0, "source": ""}
    }
}
```

(The real file also has the other three deck slots and the `DeckArea` free-placement
slot; omitted fields keep their defaults.)

### Example: rotor (`wheel.rotor.m1.timber`)

```json
{
    "schemaVersion": 1,
    "id": "wheel.rotor.m1.timber",
    "version": 1,
    "kind": "Rotor",
    "family": "M1",
    "status": "prototype",
    "displayName": "Timber wheel (M1)",
    "source": "art-staging/modular-ship-study-v1/A-timber/manifest.json",
    "boundsMinU": {"x": -1.62, "y": -2.3, "z": -1.62},
    "boundsMaxU": {"x": 1.62, "y": 2.3, "z": 1.62},
    "visuals": [
        {"id": "Rotor", "resourcePath": "ShipModules/Meshes/Wheels/M1_Timber/Rotor",
         "notes": "Axle-local (origin = axle centre). Rotate ONLY this, about game X."}
    ],
    "rotor": {"mount": "M1", "nominalRadius": 1.62, "sweptRadius": 1.6222284, "paddleWidth": 4.16}
}
```

### Example: equipment slot and equipment

```json
{"id": "CannonSocket_Port_1", "socketId": "CannonSocket_Port_1",
 "clearanceSizeU": {"x": 1.8, "y": 1.55, "z": 1.65},
 "classes": ["equipment.deck-gun"], "provisional": true}
```

```json
{
    "schemaVersion": 1,
    "id": "equipment.cannon.placeholder",
    "kind": "Equipment",
    "status": "placeholder",
    "displayName": "Cannon (placeholder)",
    "boundsMinU": {"x": -0.9, "y": -0.775, "z": 0},
    "boundsMaxU": {"x": 0.9, "y": 0.775, "z": 1.65},
    "equipment": {"equipmentClass": "equipment.deck-gun", "footprintU": {"x": 1.8, "y": 1.55, "z": 1.65}, "placeholder": true}
}
```

## 4. Sockets: roles and naming

| Role | On | Standard | Meaning |
| --- | --- | --- | --- |
| `hull.origin` | stern | — | Aft end of the ship (`AftSocket`, at 0). Not a join. |
| `hull.aft` | middle, bow | join profile | Joins the section behind. |
| `hull.fwd` | stern, middle | join profile | Joins the section ahead. |
| `hull.tip` | bow | — | Forward extreme incl. the prow (`ForwardSocket`, 10.98). Not a join. |
| `wheel` | stern | mount standard | Axle centre (`WheelModuleSocket`, 0.72, 0, 0.35); `radiusLimit` = largest swept radius the pocket takes. |
| `fitting.chimney` | stern | `fitting.chimney` | `placementRule: "assembled-midpoint"`: X is replaced by the midpoint between the stern's aft end and the bow tip; Y, Z from `posU` (0, 1.76). |
| `deck.upper` | stern | `deck.upper.placeholder` | Mount for a partial raised deck (placeholder interface). |
| `deck.slot` | any | — | A fixed equipment position (with an `equipmentSlots` entry). |
| `deck.area` | midship | — | Centre of a free-placement deck area (equipment adds `offsetU`). |

Socket ids keep Astra's Blender empty names (`DeckSlot_1_-1` = bay 1, starboard).
In a configuration they are **qualified by the section instance**:
`stern/Chimney`, `middle[0]/DeckSlot_1_1`, `bow/DeckSlot_0_-1`, and for slots on a
fitting `fitting:stern/UpperDeckMount/CannonSocket_Port_1`.

## 5. Standards are versioned interfaces

* **Join profiles.** `W1-r2` (V2/V3 hulls: 22-point loop, half-beam 4.64) and `W1`
  (V1: 28-point loop, half-beam 4.75). Same section names and lengths, **different
  shape**: they never join. The rule compares the profile ids on the two touching
  sockets; module names are never consulted.
* **Mounts.** `M1` (radius limit 1.64) and `M1-L` (radius limit 2.17). No W1-r2 stern
  offers M1-L yet.
* **Adding W2 (a broader family):** add a `joinProfiles` entry `{"id": "W2", ...}`
  with its measured loop, deliver stern/middle/bow modules whose hull sockets say
  `"standard": "W2"`. They will join each other and refuse W1-r2 automatically. To
  mix families, deliver an **adapter** section whose `hull.aft` says one profile and
  `hull.fwd` the other — no code change.
* **Adding M1-L:** deliver a stern module (new id, e.g. `hull.stern.w1r2m1l.v1`)
  whose `wheel` socket says `"standard": "M1-L", "radiusLimit": 2.17`, plus a carrier
  with `"mount": "M1-L"`. The oversized rotor then assembles on that stern only.
* A standard's `version` changes when its definition is refined but stays
  compatible; an incompatible change is a new id (as `W1` → `W1-r2`).

## 6. Compatibility rules and rejection codes

`ShipAssembler.Assemble(config, library)` checks everything and reports **every**
reason, not just the first. On any rejection `ok = false` and the placement lists
are **empty** — a half-built ship is never handed to a view or to gameplay; the
bench keeps showing the last valid ship.

| Code | When | Example message |
| --- | --- | --- |
| `CONFIG_SCHEMA_TOO_NEW` | config `schemaVersion` > supported | "This ship was saved by a newer version of the game (configuration format 99; this version reads up to 1). Update the game to open it." |
| `LIBRARY_INVALID` | standards could not load | "The ship module data could not be loaded: …" |
| `STERN_MISSING` / `BOW_MISSING` | no stern / no bow | "A ship needs exactly one stern section, at the aft end." |
| `STERN_WRONG_KIND` / `MIDDLE_WRONG_KIND` / `BOW_WRONG_KIND` | wrong kind in a position (bow first, two sterns, bow in the middle) | "Stern_W1 (V3) is a stern; a ship has exactly one, at the aft end. The middle[0] position needs a middle." |
| `TOO_MANY_MIDDLES` | more than `maxMiddles` | "This ship has 4 middle sections; the most the current hull family supports is 3." |
| `UNKNOWN_MODULE` | id not in the library | "No ship part called 'x' exists in the module library." |
| `SOCKET_MISSING` | a section lacks the join it needs | "… has no forward join, so nothing can be fitted ahead of it." |
| `JOIN_PROFILE_MISMATCH` | touching hull sockets name different profiles | "Midship_W1 (V1) joins with profile W1, but Stern_W1 (V3) needs W1-r2 — their cross-sections differ even though the section names may match, so the hull would not close." |
| `WHEEL_SOCKET_MISSING` | rotor/carrier on a stern with no wheel pocket | |
| `WHEEL_MOUNT_MISMATCH` | rotor mount ≠ the stern's wheel socket standard | "Oversized wheel (M1-L) needs an M1-L stern housing; this stern's wheel pocket is M1 (fits radius ≤ 1.64, this wheel sweeps 2.15)." |
| `WHEEL_TOO_LARGE` | mounts match but swept radius > pocket limit | "… sweeps a radius of X, but this stern's M1 pocket fits at most 1.64; the paddles would strike the housing." |
| `CARRIER_MISSING` | rotor without carrier | "A paddle wheel needs a carrier (the fixed bearings and frame) to hang in." |
| `CARRIER_MOUNT_MISMATCH` | carrier mount ≠ wheel socket standard | |
| `WRONG_KIND` | a non-rotor in `rotorId`, etc. | |
| `FITTING_SOCKET_UNKNOWN` / `FITTING_WRONG_KIND` / `FITTING_CLASS_MISMATCH` / `FITTING_SOCKET_TAKEN` | fitting rules | "Chimney (V3) fits a 'fitting.chimney' socket; 'UpperDeckMount' is a 'deck.upper.placeholder' socket." |
| `EQUIPMENT_SLOT_UNKNOWN` | slot does not exist on this ship | |
| `EQUIPMENT_WRONG_KIND` | not an Equipment module | |
| `EQUIPMENT_CLASS_NOT_ALLOWED` | slot does not take that class | |
| `EQUIPMENT_SLOT_TAKEN` | a fixed slot used twice | |
| `EQUIPMENT_EXCEEDS_CLEARANCE` | footprint (at socket + offset) not inside the slot's clearance box | "… does not fit the space reserved at DeckSlot_0_1 (needs A x B x C, the slot keeps 1.8 x 1.55 x 1.65 clear)." or "… would stick out of the space reserved at DeckSlot_0_1; move it back inside the slot's 1.8 x 1.55 x 1.65 clearance." |
| `EQUIPMENT_BLOCKS_PASSAGE` | reservation overlaps any crew-passage box | "Cannon (placeholder) at DeckArea would stand in the crew passage (middle[0]/CrewPassage_Main); the crew need that way kept clear." |
| `EQUIPMENT_OVERLAP` | reservation overlaps another equipment reservation | |

Reservations: equipment on a **fixed slot reserves the slot's whole clearance box**
(room to work the gun); equipment on a **deck area reserves its footprint**. Boxes
that only touch do not overlap.

Placeholders: modules with `status: "placeholder"` assemble but are listed in
`AssemblyResult.placeholders` so a UI can mark them; the view draws them as grey
boxes named `PLACEHOLDER <id>`.

The library itself refuses (and lists in `errors`): `LIB_SCHEMA_TOO_NEW`,
`LIB_DUPLICATE_ID`, `LIB_UNKNOWN_KIND`, `LIB_UNKNOWN_STANDARD`, `LIB_MISSING_ID`,
`LIB_PARSE_ERROR`, `LIB_BAD_SCALE`, `LIB_STANDARDS_MISSING`.

## 7. What an asset delivery must contain (Astra)

1. **One FBX per visual part**, in V8 authoring units, exported with the existing V8
   convention (Blender (x,y,z) → game (−y,z,x)).
2. **Shared origin**: every part of a hull module at the module's aft interface on
   the height datum. Rotor and carrier **axle-local**. Fittings at their stated pivot.
3. **No materials.** Colours in the `Col` vertex-colour attribute; the game assigns
   one shared vertex-colour material.
4. **A manifest** (as today's `manifest.json`) with lengths, every socket position,
   the interface standard on each join, wheel mount + radius limit, slot clearance
   boxes, and passage empties. We copy numbers from it into the module JSON, and
   cite it in `source`.
5. **A status** per module: prototype / placeholder / incompatible-reference.
6. **Do not**: scale non-uniformly to make variants; bake wheel rotation or animation
   into the rotor; put connection caps on join faces (they stay open and close on
   assembly); rely on names for compatibility (only the interface standard counts);
   bake a module's installed position into an axle-local FBX.
7. **Placeholders** are fine: a module JSON with `status: "placeholder"`, bounds, and
   sockets but no visuals. It shows as a grey box until the mesh arrives.

## 8. Swapping visuals without code changes

Visuals are only `visuals[].resourcePath`. To change what a module looks like:
drop the new FBX under `Assets/_Project/Resources/ShipModules/Meshes/...` and point
`resourcePath` at it (no extension), or overwrite the FBX in place. Placement, rules
and saves are unaffected because they never read meshes. Timber ↔ reinforced wheel
is exactly this: two rotor modules on the same M1 socket; the self-test proves only
the rotor's visual changes.

Import: an `AssetPostprocessor` (`Ship/Modular/Editor/ModularShipModelImport.cs`) sets
the importer on first import of anything in that folder: scale 1 / use file scale
(as `AstraPlaytestImport`), no materials, no cameras/lights/animation/blend shapes,
normals imported, axis conversion at the importer default. `SeaSick/Modular/Configure
Mesh Importers` re-applies it.

## 9. How the configuration joins the save later

`ShipConfiguration` is its own JSON document with its own `schemaVersion` (1). It
holds only ids and slot choices — no positions or derived numbers — so it survives
any art change. Milestone 1 does **not** touch `Save/`. The plan: add one field to
`SaveData.ShipSave`, e.g. `public string modularConfigJson;`. `SaveGame.Read`
already tolerates **added** fields, so `SaveData.CurrentVersion` stays 1 and every
existing save still loads (an old save simply has no modular config and keeps its
current ship). Loading runs `ShipConfiguration.FromJson` → `ShipAssembler.Assemble`;
a newer config schema is refused with `CONFIG_SCHEMA_TOO_NEW`, never guessed.

## 10. Deliberately NOT done in milestone 1

Physics, buoyancy, draft or waterline; mass, capacity, stability, propulsion or any
balance number; colliders; crew navigation; cannon firing or recoil; mesh welding at
joins; LODs; iPhone performance measurement; a player-facing ship builder UI;
integration with `ShipMotor`, `PaddleDrive`, `Shipyard`, `ShipLadder` or saves.

## 11. Open questions (Kevin / Astra)

1. **Raised-deck headroom.** At 0.5 m/u the study deck is 1.33 m above the main
   deck, less than a 1.7 m crew. The self-test shows the consequence: a cannon on
   the raised deck is rejected because its clearance sits inside the 1.7 m crew
   column over the main-deck passage (`upper-deck-cannon-hits-main-passage-headroom`).
   Raise the deck, narrow the passage there, or accept?
2. **M1-L needs a stern module.** The oversized wheel cannot fit any W1-r2 stern.
   Is an M1-L stern (pocket, housing, rail crown, carrier) wanted?
3. **Crew-passage width.** Provisional, retuned 2026-09-25 to fit Astra's real
   cannon kit (footprint 2.18 u across, wider than the milestone-1 placeholder):
   |y| ≤ 2.30 u (2.30 m wide, down from 2.675 u), 1.7 m tall, per section; stern
   from the housing edge X 3.16, bow to X 4.4; the deck-gun slot clearances
   widened to match (1.8 x 2.3 x 1.65 u, docs/SHIPYARD-API.md §15). Still just a
   number chosen to touch, not overlap, the wider slots -- Kevin: how wide should
   it really be? Note the **helm** (X 4.22–4.58, |y| ≤ 0.8) and the **chimney**
   stand on the centreline inside it (fittings are not checked against passages yet).
4. **Chimney on long ships.** The rule is "assembled midpoint" (V3 README). On a
   3-bay ship that puts it mid-bay; is that right, or should it sit on a fixed bay?
5. **Second bow.** Should the W1-r2 V2 bow (8.65, no prow) become a second bow option?
6. **Deck slots** are provisional (V3 README: not verified cannon/crew placements),
   and their clearance box is borrowed from the study's raised-deck cannon sockets.
7. **Upper-deck frame.** The study's partial deck was authored in a ship-level frame;
   its sockets were converted to stern-local X by +11.55 (study wheel socket −10.83
   vs stern 0.72). A real upper-deck module should be authored stern-local (or on its
   own interface).

## 12. Running the checks

* Headless (no Unity): `tools/modular-selftest.sh` → `ModularShipSelfTest: 134 PASS, 0 FAIL` (2026-09-25: +30 `ExpandedHullValidation` gates for the W1x expanded-beam family, docs/EXPANDED-HULL-VALIDATION.md).
* In an editor on this branch's project: `unity cmd eval --json --code 'return SeaSick.Ship.Modular.ModularShipSelfTest.Run();'`
  (also checks every `resourcePath` resolves through `Resources.Load` after import).
* Batch preview (the branch's own project only, never the main open editor):
  `Unity -batchmode -projectPath <worktree> -executeMethod
  SeaSick.Ship.Modular.EditorTools.ModularShipPreview.Run` — imports, prints every
  mesh's imported bounds/orientation, runs the self-test in Unity, creates the test
  scene and renders short/long/three-bay ships plus timber/reinforced stern close-ups
  to `Logs/modular-previews/`, then logs the oversized-wheel rejection
  (`Logs/modular-preview.txt`). ~20 s once imported.
* Test scene: menu **SeaSick/Modular/Create Test Scene** (or `-executeMethod
  SeaSick.Ship.Modular.ModularShipTestSceneSetup.CreateTestScene`), open
  `Assets/_Project/Scenes/Tests/ModularShipTest.unity`, press Play. Not in build settings.

## 13. Hull width families (2026-09-25)

Two hull families are validated and offered: **W1-r2** (the original,
deck beam 9.28) and **W1x** (`hull.*.w1x.v1`, deck beam 12.08, length and
depth unchanged — the "expanded" / "W1-center-expansion-r1" family, full
validation in docs/EXPANDED-HULL-VALIDATION.md). A ship may be built from
either family's stern/middle/bow, never a mix — `ShipAssembler`'s existing
per-socket join-profile check already refuses a direct W1-r2 ↔ W1x join
(`JOIN_PROFILE_MISMATCH`, readable), with no new assembler or policy
mechanism needed. W1x **supersedes** the earlier W2-r1 direction (branch
`wide-hull`, deck beam 11.6, deeper keel −2.75, independently modelled
cross-sections) as the width upgrade Kevin and Astra are taking forward;
W2-r1 stays validated-but-unexposed (its `hull.transition.w1r2-w2r1.v1`
placeholder and `W2`/`W2-placeholder` join profiles are dormant, not
deleted, in case that branch is revisited).

W1x's kit (`art-staging/modular-width-inserts-v1`) is **multi-part**: unlike
W1-r2's one-FBX-per-visual kits, several of its parts are split
Core/Port/Starboard/Insert pieces with their own `manifest.json`
`local_position`, carried in the new `VisualPart.localPositionU` field
(§3) and applied by `ModularShipView.Build` on top of the module's own
placement. The wheel/carrier/chimney are **unchanged** and reused as-is
(the kit's own words, verified independently — see the validation doc);
only the hull shell/rails/ironwork/plank-seams meshes are new per family.

**Raised decks — future contract (Astra, 2026-09-25).** Still
`NOT_IN_PROTOTYPE` (open question 1 above) on every hull family, W1x
included; `modular-width-raised-v1` and `modular-raised-middle-v1` were
**not** imported by the W1x validation work (out of scope). For whenever
raised decks are taken up, the rule Astra gave is: **reject raised ends
placed directly adjacent with no middle bay between them**; **never
combine the continuous raised-middle style with the older stair/drop-edge
end variants** on the same ship; **raised ends separated by a LOW middle
are a valid partial-deck option**, pending a traversal (stair/ramp) check
between the levels. This is a design contract for the next raised-deck
integration to implement against, not code that exists yet.
