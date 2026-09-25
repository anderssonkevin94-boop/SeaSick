#!/usr/bin/env python3
"""Author hull.{stern,middle,bow}.w1xr.v1.json from the w1x sources + measured data.

Pure Python (no bpy). Reads the w1x module JSONs, the raised kit's manifest,
and the measurement/hydrostatics results already produced by
export_raised_hydrostatics.py and measure_raised_modules.py, and writes the
three W1xR module JSONs plus the standards.json W1xR joinProfile.
"""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODDIR = ROOT / "Assets/_Project/Resources/ShipModules/Modules"
STANDARDS = ROOT / "Assets/_Project/Resources/ShipModules/standards.json"
KIT = ROOT / "art-staging/modular-raised-middle-v1"
HYDRO_DIR = ROOT / "Assets/_Project/Resources/ShipModules/Hydrostatics/HullW1xR_v1"

manifest = json.loads((KIT / "manifest.json").read_text())
measure = json.loads(Path("/tmp/raised_measurements.json").read_text())
mass = json.loads(Path("/tmp/mass_summary.json").read_text())
table_match = json.loads(Path("/tmp/table_match_clean.json").read_text())

DECK_Z = 4.20
OLD_DECK_Z = 1.76
BETWEEN_DECK = 2.44

MEASURED_2026_09_25 = "measured 2026-09-25 (RaisedDeckData, Blender --background, art-staging/modular-raised-middle-v1)"


def visuals_for(module_key, module_name):
    parts = manifest["modules"][module_key]
    out = []
    for part_name, p in parts.items():
        pos = p["position"]
        local = {"x": round(pos[0], 6), "y": round(pos[1], 6), "z": round(pos[2], 6)}
        yaw = 0.0
        notes = (f"manifest.json modules.{module_key}.{part_name}, {p['triangles']} triangles; "
                 f"{MEASURED_2026_09_25}: export-verification.json confirms triangle count and "
                 f"vertex-color/flat-normal export for this FBX.")
        # Hatch yaw, per docs/RAISED-DECK.md sec 2 (matches manifest rotation_radians[1]).
        if part_name.endswith("__Hatch"):
            yaw = -80.0 if module_key == "Stern_W1" else 80.0
            notes += " yawDegU from docs/RAISED-DECK.md sec 2 (matches manifest rotation_radians Y, -1.3963/+1.3963 rad)."
        vid = part_name.split("__", 1)[1] if "__" in part_name and not part_name.startswith(("Raised", "Carrier", "Rotor")) else part_name
        out.append({
            "id": part_name,
            "resourcePath": f"ShipModules/Meshes/HullW1xR_v1/{module_key}/{part_name}",
            "placeholder": False,
            "localPositionU": local,
            "yawDegU": yaw,
            "notes": notes,
        })
    return out


def move_sockets_up(sockets, join_facing_ids):
    out = []
    for s in sockets:
        s = copy.deepcopy(s)
        role = s["role"]
        if role in ("deck.slot", "deck.area"):
            s["posU"]["z"] = DECK_Z
            if s["id"] in ("DeckSlot_0_-1", "DeckSlot_0_1"):
                note = " NEW gun pair (spec sec 5): sockets/equipmentSlots already existed in the w1x source; added to capacity.gunSlots here. Provisional -- assembler check pending."
                s["notes"] = (s.get("notes") or "") + note
        elif role == "fitting.chimney":
            s["posU"]["z"] = DECK_Z
            s["notes"] = ("Z raised to 4.20 (upper deck). placementRule stays assembled-midpoint; the rule needs an "
                          "X offset of -0.84 u so a one-middle raised ship lands on the manifest's chimney_position "
                          "X 12.30 (today's unmodified midpoint formula gives 13.14 -- same offset a two-middle "
                          "raised ship needs per docs/RAISED-DECK.md sec 2, unverified by Astra for that case). "
                          "SocketDef has no offset field: this note is the spec; the C# half (assembler / "
                          "PlacementRule.AssembledMidpoint) must apply the -0.84 u X offset for family W1xR.")
        if role in ("hull.fwd", "hull.aft") and s["id"] in join_facing_ids:
            s["standard"] = "W1xR"
        out.append(s)
    return out


def move_passages_up(passages):
    out = []
    for p in passages:
        p = copy.deepcopy(p)
        p["centreU"]["z"] = DECK_Z + 1.70
        p["notes"] = ("Z raised with the deck (4.20 + 1.70 = 5.90); X/Y extents UNCHANGED from the w1x passage "
                      "(never narrowed, per docs/RAISED-DECK.md sec 4) -- height stays 3.4 u. " + p.get("notes", ""))
        out.append(p)
    return out


def build_capacity(w1x_capacity, hold_delta, berth_delta, extra_gun_ids, upper_vol, deck_cap_u2, module_key):
    cap = copy.deepcopy(w1x_capacity)
    new_hold = cap["holdCells"]["value"] + hold_delta
    new_berths = cap["berths"]["value"] + berth_delta
    cap["holdCells"] = {
        "value": new_hold,
        "provisional": True,
        "source": (f"w1x {module_key} holdCells ({cap['holdCells']['value']}) + round(0.7 x upperVol / 38.229): "
                   f"upperVol = this module's OWN W1xR hydrostatic table integrated to 4.20 minus to 1.76 "
                   f"({upper_vol:.3f} U^3, measured, not the sec-5 deck-waterplane x 2.44 estimate) -> "
                   f"round(0.7 x {upper_vol:.3f} / 38.229) = {hold_delta} -> {cap['holdCells']['value']} + {hold_delta} = {new_hold}."),
    }
    cap["berths"] = {
        "value": new_berths,
        "provisional": True,
        "source": (f"w1x {module_key} berths ({cap['berths']['value']}) + floor(0.3 x deckArea / 12.8 U^2): "
                   f"deckArea = measured W1xR deck-cap footprint ({deck_cap_u2:.3f} U^2, Blender) -> "
                   f"floor(0.3 x {deck_cap_u2:.3f} / 12.8) = {berth_delta} -> {cap['berths']['value']} + {berth_delta} = {new_berths}."),
    }
    ids = list(cap["gunSlots"]["ids"])
    for gid in extra_gun_ids:
        if gid not in ids:
            ids.append(gid)
    cap["gunSlots"] = {
        "ids": ids,
        "provisional": True,
        "source": (cap["gunSlots"]["source"] + (f" PLUS the new {extra_gun_ids} pair (upper deck, Z 4.20, spec sec 5): "
                   "sockets/equipmentSlots already existed in the w1x source; provisional, assembler check pending." if extra_gun_ids else "")),
    }
    cap["rule"] = ("Seeded 2026-09-25 (RaisedDeckData): w1x module capacity + the raised deltas from docs/RAISED-DECK.md sec 5, "
                   "using this module's own measured W1xR hydrostatic table and Blender deck footprint (not the sec-5 estimate numbers). PROVISIONAL, tune freely.")
    return cap


def build_module(w1x_id, w1xr_id, module_key, kind, join_facing_ids, extra_gun_ids):
    w1x = json.loads((MODDIR / f"{w1x_id}.json").read_text())
    m = measure[module_key]
    ms = mass[module_key]
    tm = table_match[module_key]
    upper_vol = tm["volume_w1xr_full_to_420"] - tm["volume_w1xr_at176"]
    hold_delta = round(0.7 * upper_vol / 38.229)
    berth_delta = int((0.3 * m["deckCapFootprintU2"]) // 12.8)

    d = copy.deepcopy(w1x)
    d["id"] = w1xr_id
    d["version"] = 1
    d["family"] = "W1xR"
    d["status"] = "prototype"
    d["displayName"] = w1x["displayName"].replace("(expanded, W1x)", "(raised deck, W1xR)")
    d["description"] = (f"Raised-deck variant of {w1x_id}: same W1x lower hull (below Z 1.76) with a flush upper "
                         f"deck at Z 4.20 (between-deck 2.44 u = 1.22 m). Visuals ONLY from the raised kit "
                         f"(art-staging/modular-raised-middle-v1); never mixed with the w1x/w1r2 shell. See docs/RAISED-DECK.md.")
    d["source"] = "art-staging/modular-raised-middle-v1/manifest.json"
    d["boundsMaxU"] = dict(d["boundsMaxU"])
    d["boundsMaxU"]["z"] = round(m["hullShellZMaxU"], 4)
    d["boundsNote"] = (f"X/Y UNCHANGED from {w1x_id} (lower hull/beam untouched by this kit). Z max = the raised "
                        f"Hull_Shell's measured top (deck cap), {round(m['hullShellZMaxU'], 4)} u -- "
                        f"{MEASURED_2026_09_25}.")

    d["sockets"] = move_sockets_up(d["sockets"], join_facing_ids)
    d["visuals"] = visuals_for(module_key, module_key)
    if "passages" in d and d["passages"]:
        d["passages"] = move_passages_up(d["passages"])
    if "equipmentSlots" in d and d["equipmentSlots"]:
        es = copy.deepcopy(d["equipmentSlots"])
        for e in es:
            if e["socketId"] in ("DeckSlot_0_-1", "DeckSlot_0_1"):
                e["notes"] = (e.get("notes") or "") + " NEW gun pair added to capacity here (spec sec 5); provisional, assembler check pending."
        d["equipmentSlots"] = es

    d["hydrostatics"] = {
        "resourcePath": f"ShipModules/Hydrostatics/HullW1xR_v1/{module_key}",
        "sourceGeometrySha256": tm.get("sourceGeometrySha256") or None,
        "validWaterlineZU": [-1.9199999570846558, DECK_Z],
        "offsetZU": 0,
        "notes": (f"Claude/Opus 2026-09-25 (RaisedDeckData, export_raised_hydrostatics.py wrapping Astra's "
                  f"export_hull_hydrostatics.py on the raised kit's {module_key}__Hull_Shell.fbx, deck_z=4.20). "
                  f"Table-match gate vs {w1x_id}'s table below Z 1.76: volume at 1.76 "
                  f"w1x {tm['volume_w1x_at176']} U^3 vs w1xr {tm['volume_w1xr_at176']} U^3 "
                  f"({tm['vol_reldiff_pct']}% relative diff, gate <=0.5%); max sectional-area relative diff "
                  f"{tm['max_area_reldiff_pct']}% (near x={tm['worst_x']:.3f}, a small-absolute-area bow-tip/"
                  f"stern-step interpolation-grid artifact after excluding bulkhead step-transition bands -- see "
                  f"docs/RAISED-DECK.md Data results). Both PASS the 0.5% gate. Integrated volume to 4.20: "
                  f"{tm['volume_w1xr_full_to_420']} U^3.")
    }
    # Fill the real sha256 from the exported table file.
    table = json.loads((HYDRO_DIR / f"{module_key}.json").read_text())
    d["hydrostatics"]["sourceGeometrySha256"] = table["sourceGeometrySha256"]

    d["lightship"] = {
        "massKg": ms["lightshipMass"],
        "provisional": True,
        "rule": (f"{ms['w1xMass']} kg ({w1x_id} lightship) + {ms['upperMass']} kg (measured upper structure: "
                 f"deck {ms['deckM2']} m^2 x 110 kg/m^2 = {ms['deckMass']} kg, plus topside walls {ms['wallM2']} m^2 "
                 f"x 95 kg/m^2 = {ms['wallMass']} kg) = {ms['lightshipMass']} kg. Deck area = the Hull_Shell's own "
                 f"flat top cap footprint at Z {round(m['hullShellZMaxU'],2)} (the structural deck), NOT the "
                 f"decorative UpperPlanks/PlankDetails trim mesh (its footprint is only "
                 f"{m['upperDeckPlankTrimFootprintM2']:.3f} m^2, a small fraction of the deck plan -- it is plank-seam "
                 f"detailing over part of the deck, not the deck surface). Wall area excludes the deck-cap band "
                 f"(centroid z > shell z-max - 0.05) so it is not double counted. {MEASURED_2026_09_25}."),
    }
    d["upperStructure"] = {
        "massKg": ms["upperMass"],
        "centroidZU": ms["upperCentroidZU"],
        "deckMassKg": ms["deckMass"],
        "deckAreaM2": ms["deckM2"],
        "wallMassKg": ms["wallMass"],
        "wallAreaM2": ms["wallM2"],
        "wallAreaCentroidZU": m["topsideWallAreaCentroidZU"],
        "source": (f"Blender measurement 2026-09-25 (RaisedDeckData, tools/blender/measure_raised_modules.py). "
                   f"centroidZU = mass-weighted: (deckMassKg*4.20 + wallMassKg*wallAreaCentroidZU) / massKg. "
                   f"Not read by ModuleDef yet (JsonUtility drops unknown fields on read) -- the C# half (sec 6, "
                   f"HullFormData mass/CoM) should add a matching field and read this."),
    }

    d["capacity"] = build_capacity(w1x["capacity"], hold_delta, berth_delta, extra_gun_ids, upper_vol, m["deckCapFootprintU2"], module_key)

    return d


def main():
    stern = build_module("hull.stern.w1x.v1", "hull.stern.w1xr.v1", "Stern_W1", "Stern",
                          join_facing_ids={"ForwardSocket"}, extra_gun_ids=[])
    middle = build_module("hull.middle.w1x.v1", "hull.middle.w1xr.v1", "Midship_W1", "Middle",
                           join_facing_ids={"AftSocket", "ForwardSocket"},
                           extra_gun_ids=["DeckSlot_0_-1", "DeckSlot_0_1"])
    bow = build_module("hull.bow.w1x.v1", "hull.bow.w1xr.v1", "Bow_W1", "Bow",
                        join_facing_ids={"AftSocket"},
                        extra_gun_ids=["DeckSlot_0_-1", "DeckSlot_0_1"])

    for w1xr_id, d in [("hull.stern.w1xr.v1", stern), ("hull.middle.w1xr.v1", middle), ("hull.bow.w1xr.v1", bow)]:
        path = MODDIR / f"{w1xr_id}.json"
        path.write_text(json.dumps(d, indent=4) + "\n")
        print("wrote", path)

    # standards.json: add the W1xR joinProfile.
    standards = json.loads(STANDARDS.read_text())
    if not any(j["id"] == "W1xR" for j in standards["joinProfiles"]):
        standards["joinProfiles"].append({
            "id": "W1xR",
            "version": 1,
            "status": "prototype",
            "profilePoints": 0,
            "halfBeamU": 6.04,
            "deckZU": 1.76,
            "keelZU": -1.92,
            "description": ("Hull cross-section interface of the raised-deck family (continuous flush upper deck "
                            "at Z 4.20 over the W1x lower hull, between-deck 2.44 u = 1.22 m). deckZU stays the "
                            "flotation/depth datum (1.76, same as W1x -- underwater form unchanged, sDepth == 1); "
                            "upperDeckZU is the NEW flush-deck datum. Never joins W1x/W1-r2 directly (no partial "
                            "raised ends)."),
            "sourceNote": "art-staging/modular-raised-middle-v1/manifest.json (deck_height 4.2, beam 12.08) + docs/RAISED-DECK.md.",
        })
        standards["joinProfiles"][-1]["upperDeckZU"] = 4.20
        STANDARDS.write_text(json.dumps(standards, indent=4) + "\n")
        print("wrote", STANDARDS)


if __name__ == "__main__":
    main()
