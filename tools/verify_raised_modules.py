#!/usr/bin/env python3
"""Re-check the W1xR module JSON invariants (docs/RAISED-DECK.md sec 4, 6, 9).

Checks, per hull.{stern,middle,bow}.w1xr.v1.json:
  1. No deck.slot / deck.area / passage below Z 4.20 (join/hull.origin/hull.tip/
     hull.stem/wheel/chimney sockets are allowed below -- they are not deck
     surfaces the crew walks on).
  2. gunSlots.ids (capacity) is a superset of the corresponding w1x module's ids.
  3. lightship.massKg == w1x massKg + upperStructure.massKg (rule arithmetic).
  4. upperStructure.massKg == deckMassKg + wallMassKg, and centroidZU is the
     mass-weighted average of the deck (at 4.20) and wall (at wallAreaCentroidZU).
  5. Hydrostatics table-match gate: below Z 1.76 the W1xR table's integrated
     volume matches the W1x table's (interpolated) within 0.5%.
  6. join-facing sockets use standard "W1xR" (stern ForwardSocket, middle
     AftSocket+ForwardSocket, bow AftSocket).

Exits nonzero on any failure; prints PASS/FAIL per check.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODDIR = ROOT / "Assets/_Project/Resources/ShipModules/Modules"
HYDRO_W1X = ROOT / "Assets/_Project/Resources/ShipModules/Hydrostatics/HullW1x_v1"
HYDRO_W1XR = ROOT / "Assets/_Project/Resources/ShipModules/Hydrostatics/HullW1xR_v1"

UPPER_DECK_Z = 4.20
NON_WALKING_ROLES = {"hull.origin", "hull.aft", "hull.fwd", "hull.tip", "hull.stem", "wheel", "fitting.chimney"}

MODULES = [
    ("hull.stern.w1x.v1", "hull.stern.w1xr.v1", "Stern_W1", {"ForwardSocket"}),
    ("hull.middle.w1x.v1", "hull.middle.w1xr.v1", "Midship_W1", {"AftSocket", "ForwardSocket"}),
    ("hull.bow.w1x.v1", "hull.bow.w1xr.v1", "Bow_W1", {"AftSocket"}),
]

failures = []


def check(label, cond, detail=""):
    status = "PASS" if cond else "FAIL"
    print(f"[{status}] {label}" + (f" -- {detail}" if detail and not cond else ""))
    if not cond:
        failures.append(label)


def interp(waters, vals, z):
    if z <= waters[0]:
        return vals[0]
    if z >= waters[-1]:
        return vals[-1]
    for i in range(len(waters) - 1):
        if waters[i] <= z <= waters[i + 1]:
            t = (z - waters[i]) / (waters[i + 1] - waters[i])
            return vals[i] + t * (vals[i + 1] - vals[i])
    return vals[-1]


def main():
    for w1x_id, w1xr_id, table_name, join_ids in MODULES:
        w1x = json.loads((MODDIR / f"{w1x_id}.json").read_text())
        d = json.loads((MODDIR / f"{w1xr_id}.json").read_text())
        print(f"\n== {w1xr_id} ==")

        # 1. no deck surface below the upper deck
        bad_sockets = [s["id"] for s in d["sockets"]
                       if s["role"] not in NON_WALKING_ROLES and s["posU"]["z"] < UPPER_DECK_Z - 1e-6]
        check(f"{w1xr_id}: no deck.slot/deck.area socket below Z {UPPER_DECK_Z}", not bad_sockets, bad_sockets)

        bad_passages = [p["id"] for p in d.get("passages", []) if p["centreU"]["z"] - p["sizeU"]["z"] / 2 < UPPER_DECK_Z - 1e-6]
        check(f"{w1xr_id}: no passage extending below Z {UPPER_DECK_Z}", not bad_passages, bad_passages)

        # 2. gun slot ids superset of w1x's
        w1x_gun_ids = set(w1x["capacity"]["gunSlots"]["ids"])
        w1xr_gun_ids = set(d["capacity"]["gunSlots"]["ids"])
        check(f"{w1xr_id}: capacity.gunSlots.ids superset of {w1x_id}'s", w1x_gun_ids <= w1xr_gun_ids,
              w1x_gun_ids - w1xr_gun_ids)

        # slot ids (sockets) superset check too
        w1x_slot_ids = {s["id"] for s in w1x["sockets"] if s["role"] == "deck.slot"}
        w1xr_slot_ids = {s["id"] for s in d["sockets"] if s["role"] == "deck.slot"}
        check(f"{w1xr_id}: socket ids superset of {w1x_id}'s deck.slot ids", w1x_slot_ids <= w1xr_slot_ids,
              w1x_slot_ids - w1xr_slot_ids)

        # 3/4. mass arithmetic
        us = d.get("upperStructure")
        check(f"{w1xr_id}: has upperStructure block", us is not None)
        if us:
            expect_upper = round(us["deckMassKg"] + us["wallMassKg"], 2)
            check(f"{w1xr_id}: upperStructure.massKg == deckMassKg + wallMassKg",
                  abs(us["massKg"] - expect_upper) < 0.5, f"{us['massKg']} != {expect_upper}")
            expect_centroid = (us["deckMassKg"] * UPPER_DECK_Z + us["wallMassKg"] * us["wallAreaCentroidZU"]) / us["massKg"]
            check(f"{w1xr_id}: upperStructure.centroidZU is the mass-weighted average",
                  abs(us["centroidZU"] - expect_centroid) < 0.01, f"{us['centroidZU']} != {expect_centroid:.4f}")
            expect_lightship = round(w1x["lightship"]["massKg"] + us["massKg"], 2)
            check(f"{w1xr_id}: lightship.massKg == w1x massKg + upperStructure.massKg",
                  abs(d["lightship"]["massKg"] - expect_lightship) < 0.5,
                  f"{d['lightship']['massKg']} != {expect_lightship}")

        # 5. table match gate (<=0.5% at Z 1.76)
        tw1x = json.loads((HYDRO_W1X / f"{table_name}.json").read_text())
        tw1xr = json.loads((HYDRO_W1XR / f"{table_name}.json").read_text())
        Va = interp(tw1x["waterlineZU"], tw1x["integratedVolumeU3"], 1.76)
        Vb = interp(tw1xr["waterlineZU"], tw1xr["integratedVolumeU3"], 1.76)
        reldiff = abs(Va - Vb) / Va
        check(f"{w1xr_id}: hydrostatic table matches {w1x_id} below Z 1.76 (volume, <=0.5%)",
              reldiff <= 0.005, f"{reldiff*100:.4f}%")
        check(f"{w1xr_id}: hydrostatics.validWaterlineZU caps at {UPPER_DECK_Z}",
              abs(d["hydrostatics"]["validWaterlineZU"][1] - UPPER_DECK_Z) < 1e-6)
        check(f"{w1xr_id}: hydrostatics.sourceGeometrySha256 matches the exported table's",
              d["hydrostatics"]["sourceGeometrySha256"] == tw1xr["sourceGeometrySha256"])

        # 6. join-facing sockets use standard W1xR
        bad_join = [s["id"] for s in d["sockets"] if s["id"] in join_ids and s.get("standard") != "W1xR"]
        check(f"{w1xr_id}: join-facing sockets {sorted(join_ids)} use standard W1xR", not bad_join, bad_join)

    # standards.json has the W1xR joinProfile
    standards = json.loads((ROOT / "Assets/_Project/Resources/ShipModules/standards.json").read_text())
    w1xr_profile = next((j for j in standards["joinProfiles"] if j["id"] == "W1xR"), None)
    check("standards.json: W1xR joinProfile exists", w1xr_profile is not None)
    if w1xr_profile:
        check("standards.json: W1xR keelZU == -1.92", abs(w1xr_profile["keelZU"] - (-1.92)) < 1e-6)
        check("standards.json: W1xR deckZU == 1.76", abs(w1xr_profile["deckZU"] - 1.76) < 1e-6)
        check("standards.json: W1xR upperDeckZU == 4.20", abs(w1xr_profile.get("upperDeckZU", -1) - 4.20) < 1e-6)

    print(f"\n{len(failures)} failure(s)." if failures else "\nALL CHECKS PASS")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
