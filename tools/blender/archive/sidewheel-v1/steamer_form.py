"""Steamer hull form -- the single definition of the side-wheeler's hull.

PURE PYTHON, stdlib only, no bpy: `steamer.py` (Blender) lofts the mesh from
`HullForm.half_breadth`, and this file integrates the very same function into
the strip tables the C# physics reads (docs/steamer-spec.md section 3), so the
mesh IS the physics.

Ship frame (= Unity ship-local): +z bow, +y up, +x starboard, metres.
Origin: centreline, design waterline (y = 0), midpoint of the LWL (z = 0).

    python3 tools/blender/steamer_form.py          # report + write hullform.json
    python3 tools/blender/steamer_form.py --dry    # report only

How the form is put together
----------------------------
The hull is defined LEVEL-WISE, the way a lines plan's half-breadth plan is:
at every height y there is a waterline running from the stern profile
`z_aft(y)` to the stem profile `z_fwd(y)`, with a maximum half-breadth
`bmax(y)` (a superellipse midship section: the U), a short parallel body, and
fore and aft runs shaped `(1 - s^a)^b`. Low waterlines are shorter AND finer
than high ones, so the sections turn from U amidships into V at the ends on
their own. Above the waterline (a, b) blend towards a full deck plan by the
main-deck sheer line -- that is the bow flare -- and then HOLD, so the
forecastle side above that line is near-vertical: the knuckle.
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DESIGN_PATH = os.path.join(HERE, "steamer_design.json")
HULLFORM_PATH = os.path.join(REPO, "Assets", "_Project", "Resources",
                             "Steamer", "hullform.json")
RHO = 1025.0


def _lerp(a, b, t):
    return a + (b - a) * t


def _clamp(v, lo, hi):
    return lo if v < lo else hi if v > hi else v


def load_design(path=DESIGN_PATH):
    with open(path, "r") as f:
        return json.load(f)


class HullForm(object):
    def __init__(self, design=None):
        d = design if design is not None else load_design()
        self.d = d
        f = d["form"]
        self.f = f
        self.L = float(d["lwl"])
        self.B = float(d["beam"])
        self.T = float(d["draft"])
        self.half_l = 0.5 * self.L
        self.fb_stem = float(d["freeboardStem"])
        self.fb_waist = float(d["freeboardWaist"])
        self.fb_stern = float(d["freeboardStern"])
        self.bulwark = float(d["bulwarkHeight"])
        self.fc_bulwark = float(d["forecastleBulwark"])
        self.n_stations = int(f["stationCount"])
        self.n_levels = int(f["levelCount"])
        self.dz = self.L / self.n_stations
        # The forecastle break sits ON a strip boundary so no physics strip
        # straddles the 0.9 m step in deck height.
        self.break_z = -self.half_l + int(f["forecastleBreakStrip"]) * self.dz
        self.z_stem_deck = self.half_l + float(f["bowOverhang"])
        self.z_stern_deck = -self.half_l - float(f["sternOverhang"])
        self.stem_rake = float(f["bowOverhang"]) / self.fb_stem
        # The forecastle deck is flush with the main rail: main deck at the
        # stem = stem freeboard - bulwark height.
        self.main_at_stem = self.fb_stem - self.bulwark
        # Stern profile above the waterline: continuous slope through y = 0.
        self.stern_slope = (float(f["sternCut"]) * float(f["sternLinear"])
                            / self.T)                       # |dz/dy| at the WL
        self.stern_yv = float(f["sternVerticalAboveY"])
        self.stern_m = max(1.0, self.stern_slope * self.stern_yv
                           / float(f["sternOverhang"]))

    # ------------------------------------------------------------ decks ----
    def main_deck_y(self, z):
        """Main-deck sheer line at the side; forward of the break it carries on
        under the forecastle as the knuckle line."""
        if z >= 0.0:
            t = _clamp(z / self.z_stem_deck, 0.0, 1.2)
            return self.fb_waist + (self.main_at_stem - self.fb_waist) * t * t
        t = _clamp(z / self.z_stern_deck, 0.0, 1.2)
        return self.fb_waist + (self.fb_stern - self.fb_waist) * t * t

    def has_forecastle(self, z):
        return z >= self.break_z

    def deck_y(self, z):
        """The watertight deck at station z (forecastle deck forward of the break)."""
        y = self.main_deck_y(z)
        return y + self.bulwark if self.has_forecastle(z) else y

    def shell_top_y(self, z):
        """Top of the outer shell = rail."""
        y = self.main_deck_y(z) + self.bulwark
        return y + self.fc_bulwark if self.has_forecastle(z) else y

    # --------------------------------------------------------- profiles ----
    def _tau(self, y):
        return _clamp((y + self.T) / self.T, 0.0, 1.0)

    def z_fwd(self, y):
        """Stem profile: straight raked stem, rounded forefoot."""
        z = self.half_l + self.stem_rake * y
        if y < 0.0:
            z -= self.f["forefootCut"] * (1.0 - self._tau(y)) ** self.f["forefootExp"]
        return z

    def z_aft(self, y):
        """Cruiser-stern profile: buttock rising from the keel end, through the
        waterline at the AP with a continuous slope, vertical by stern_yv."""
        if y <= 0.0:
            u = 1.0 - self._tau(y)
            k = self.f["sternLinear"]
            return -self.half_l + self.f["sternCut"] * (k * u + (1.0 - k) * u * u)
        t = _clamp(y / self.stern_yv, 0.0, 1.0)
        return -self.half_l - self.f["sternOverhang"] * (1.0 - (1.0 - t) ** self.stern_m)

    def hull_keel_y(self, z):
        """Lowest point of the canoe body (no skeg) at station z."""
        T = self.T
        if self.z_aft(-T) <= z <= self.z_fwd(-T):
            return -T
        top = 12.0
        if z > 0.0:
            if z >= self.z_fwd(top):
                return top
            lo, hi = -T, top                 # z_fwd rises with y
            for _ in range(48):
                mid = 0.5 * (lo + hi)
                if self.z_fwd(mid) >= z:
                    hi = mid
                else:
                    lo = mid
            return hi
        if z <= self.z_aft(top):
            return top
        lo, hi = -T, top                     # z_aft falls with y
        for _ in range(48):
            mid = 0.5 * (lo + hi)
            if self.z_aft(mid) <= z:
                hi = mid
            else:
                lo = mid
        return hi

    def in_skeg(self, z):
        s = self.f["skeg"]
        return s["zAft"] <= z <= s["zFwd"]

    def keel_y(self, z, skeg=True):
        if skeg and self.in_skeg(z):
            return -self.T
        return self.hull_keel_y(z)

    # ---------------------------------------------------------- sections ----
    def bmax(self, y):
        hb = 0.5 * self.B
        if y <= 0.0:
            n = self.f["midshipExp"]
            u = 1.0 - self._tau(y)
            return hb * max(0.0, 1.0 - u ** n) ** (1.0 / n)
        return hb + self.f["topsideFlare"] * _clamp(y / self.fb_waist, 0.0, 1.0)

    def _ab(self, p, z, y):
        if y <= 0.0:
            t = self._tau(y)
            return _lerp(p["aK"], p["aW"], t), _lerp(p["bK"], p["bW"], t)
        t = _clamp(y / self.main_deck_y(z), 0.0, 1.0) ** self.f["flareExp"]
        return _lerp(p["aW"], p["aD"], t), _lerp(p["bW"], p["bD"], t)

    def half_breadth(self, z, y, skeg=True):
        """Half-breadth of the moulded surface at station z, height y. Defined
        up past the deck (the bulwark is the same surface carried on); callers
        that want the watertight body clip y at `deck_y(z)`."""
        if y < -self.T - 1e-9:
            return 0.0
        hb = 0.0
        zf = self.z_fwd(y)
        za = self.z_aft(y)
        if za < z < zf:
            zpf = self.f["parallelFwdZ"]
            zpa = self.f["parallelAftZ"]
            bm = self.bmax(y)
            if z > zpf:
                a, b = self._ab(self.f["fwd"], z, y)
                s = _clamp((z - zpf) / max(zf - zpf, 1e-6), 0.0, 1.0)
                hb = bm * max(0.0, 1.0 - s ** a) ** b
            elif z < zpa:
                a, b = self._ab(self.f["aft"], z, y)
                s = _clamp((zpa - z) / max(zpa - za, 1e-6), 0.0, 1.0)
                hb = bm * max(0.0, 1.0 - s ** a) ** b
            else:
                hb = bm
            if z > zpf:
                hb = max(hb, self.f["stemHalfThickness"])
        elif z == zf and z > 0.0:
            hb = self.f["stemHalfThickness"]
        if skeg and y <= 0.0 and self.in_skeg(z):
            hb = max(hb, self.f["skeg"]["halfThickness"])
        return hb

    def body_half_breadth(self, z, y):
        """The watertight body the physics floats: constant above the deck."""
        return self.half_breadth(z, min(y, self.deck_y(z)))

    # ------------------------------------------------------ hydrostatics ----
    def hydrostatics(self, nz=680, dy=0.02):
        """Fine numerical integration of the geometry at the design waterline."""
        L, T = self.L, self.T
        hz = L / nz
        ny = int(round(T / dy))
        hy = T / ny
        vol = mom_y = mom_z = 0.0
        vol_f = vol_a = 0.0
        awp = awp_z = it = 0.0
        zs = [-self.half_l + (i + 0.5) * hz for i in range(nz)]
        hb0s = []
        amax = 0.0
        for z in zs:
            a = my = 0.0
            for j in range(ny):
                y = -T + (j + 0.5) * hy
                w = 2.0 * self.half_breadth(z, y) * hy
                a += w
                my += w * y
            amax = max(amax, a)
            vol += a * hz
            mom_y += my * hz
            mom_z += a * z * hz
            if z >= 0.0:
                vol_f += a * hz
            else:
                vol_a += a * hz
            hb0 = self.half_breadth(z, 0.0)
            hb0s.append(hb0)
            awp += 2.0 * hb0 * hz
            awp_z += 2.0 * hb0 * z * hz
            it += (2.0 / 3.0) * hb0 ** 3 * hz
        lcf = awp_z / awp
        il = sum(2.0 * hb * hz * ((z - lcf) ** 2 + hz * hz / 12.0)
                 for z, hb in zip(zs, hb0s))
        bwl = 2.0 * max(hb0s)
        zq = 0.4 * L                                   # 40 % L forward of midships
        flare = self.body_half_breadth(zq, 99.0) / max(self.half_breadth(zq, 0.0), 1e-6)
        kb = T + mom_y / vol
        bm = it / vol
        gm = float(self.d["gm"])
        return {
            "volume": vol, "massKg": RHO * vol, "waterplane": awp,
            "kb": kb, "bm": bm, "kg": kb + bm - gm, "gm": gm,
            "lcbZ": mom_z / vol, "lcfZ": lcf, "iT": it, "iL": il,
            "bml": il / vol, "beamWL": bwl,
            "cb": vol / (L * bwl * T), "cwp": awp / (L * bwl),
            "cm": amax / (bwl * T), "cp": vol / (amax * L),
            "volumeFwd": vol_f, "volumeAft": vol_a,
            "foreAftRatio": vol_f / vol_a, "flareRatio": flare,
            "flareDeckHb": self.body_half_breadth(zq, 99.0),
            "flareWlHb": self.half_breadth(zq, 0.0),
        }

    # ------------------------------------------------------ strip tables ----
    def station_tables(self, z_samples=24, dy=0.01):
        """Spec section 3 strips. Every tabulated quantity is the MEAN over the
        strip's length (not the value at its centre), so sum(table * dz) is the
        hull's real volume / waterplane rather than a 13-point midpoint guess."""
        n_lo = (self.n_levels - 1) // 2          # 8 levels keel .. waterline
        n_hi = self.n_levels - 1 - n_lo          # 8 levels above, then +1.5 m
        out = []
        for i in range(self.n_stations):
            zc = -self.half_l + (i + 0.5) * self.dz
            zlist = [zc + ((k + 0.5) / z_samples - 0.5) * self.dz
                     for k in range(z_samples)]
            keel = min(self.keel_y(z) for z in zlist)
            deck = self.deck_y(zc)
            ys = [keel * (1.0 - float(k) / (n_lo - 1)) for k in range(n_lo)]
            ys[n_lo - 1] = 0.0
            ys += [deck * float(k) / n_hi for k in range(1, n_hi + 1)]
            ys = ys[:self.n_levels - 1] + [deck + 1.5]
            hbs = [0.0] * len(ys)
            area = [0.0] * len(ys)
            mom = [0.0] * len(ys)
            for z in zlist:
                a = m = 0.0
                prev = ys[0]
                hbs[0] += self.half_breadth(z, min(ys[0], deck))
                for k in range(1, len(ys)):
                    top = min(ys[k], deck)
                    if top > prev:
                        n = max(2, int(math.ceil((top - prev) / dy)))
                        h = (top - prev) / n
                        for j in range(n):
                            y = prev + (j + 0.5) * h
                            w = 2.0 * self.half_breadth(z, y) * h
                            a += w
                            m += w * y
                        prev = top
                    area[k] += a
                    mom[k] += m
                    hbs[k] += self.half_breadth(z, top)
            inv = 1.0 / z_samples
            out.append({
                "z": round(zc, 4), "dz": round(self.dz, 5),
                "keelY": round(keel, 4), "deckY": round(deck, 4),
                "y": [round(v, 4) for v in ys],
                "halfBreadth": [round(v * inv, 4) for v in hbs],
                "area": [round(v * inv, 5) for v in area],
                "momentY": [round(v * inv, 5) for v in mom],
            })
        return out

    def table_hydrostatics(self, stations):
        """Exactly what the C# side can re-derive from the tables at y = 0."""
        k0 = (self.n_levels - 1) // 2 - 1
        vol = my = mz = awp = it = awz = 0.0
        for s in stations:
            assert abs(s["y"][k0]) < 1e-9
            dz = s["dz"]
            vol += s["area"][k0] * dz
            my += s["momentY"][k0] * dz
            mz += s["area"][k0] * s["z"] * dz
            hb = s["halfBreadth"][k0]
            awp += 2.0 * hb * dz
            awz += 2.0 * hb * s["z"] * dz
            it += (2.0 / 3.0) * hb ** 3 * dz
        lcf = awz / awp
        il = sum(2.0 * s["halfBreadth"][k0] * s["dz"]
                 * ((s["z"] - lcf) ** 2 + s["dz"] ** 2 / 12.0) for s in stations)
        kb = self.T + my / vol
        bm = it / vol
        return {"volume": vol, "massKg": RHO * vol, "waterplane": awp,
                "kb": kb, "bm": bm, "kg": kb + bm - float(self.d["gm"]),
                "lcbZ": mz / vol, "lcfZ": lcf, "iT": it, "iL": il}

    # -------------------------------------------------------------- json ----
    def helm(self):
        z = float(self.d["helmZ"])
        return {"x": 0.0, "y": round(self.deck_y(z), 4), "z": z}

    def build_json(self):
        d = self.d
        st = self.station_tables()
        th = self.table_hydrostatics(st)
        fine = self.hydrostatics()
        r = lambda v, n=4: round(v, n)
        com_y = th["kg"] - self.T
        return {
            "lwl": d["lwl"], "loa": d["loa"], "beam": d["beam"],
            "beamOverGuards": d["beamOverGuards"],
            "draft": d["draft"], "depth": d["depth"],
            "volume": r(th["volume"]), "massKg": r(RHO * r(th["volume"]), 2),
            "waterplane": r(th["waterplane"]), "kb": r(th["kb"]), "bm": r(th["bm"]),
            "gm": d["gm"], "kg": r(th["kg"]), "lcbZ": r(th["lcbZ"]),
            "gyradiusRoll": d["gyradiusRoll"],
            "gyradiusPitch": r(d["gyradiusPitchOverL"] * self.L, 3),
            "gyradiusYaw": r(d["gyradiusYawOverL"] * self.L, 3),
            "com": {"x": 0.0, "y": r(com_y), "z": r(th["lcbZ"])},
            "wheelAxle": d["wheelAxle"],
            "wheelRadius": d["wheelRadius"], "wheelWidth": d["wheelWidth"],
            "wheelFloats": d["wheelFloats"],
            "wheelFloatDepth": d["wheelFloatDepth"],
            "wheelDesignDip": d["wheelDesignDip"],
            "rudder": d["rudder"], "rudderArea": d["rudderArea"],
            "helm": self.helm(),
            "funnelTopY": d["funnelTopY"],
            # Extras (JsonUtility ignores what the C# class does not declare).
            "lcfZ": r(th["lcfZ"]), "inertiaT": r(th["iT"], 2),
            "inertiaL": r(th["iL"], 1),
            "cb": r(fine["cb"]), "cwp": r(fine["cwp"]), "cm": r(fine["cm"]),
            "volumeFwd": r(fine["volumeFwd"], 2), "volumeAft": r(fine["volumeAft"], 2),
            "flareRatio": r(fine["flareRatio"], 3),
            "forecastleBreakZ": r(self.break_z),
            "stations": st,
        }, th, fine


def check(form, fine):
    """Spec section 2 acceptance ranges. Returns a list of failures."""
    L = form.L
    lcb_pct_aft = -fine["lcbZ"] / L * 100.0
    bal = abs(fine["volumeFwd"] - fine["volumeAft"]) / max(fine["volumeFwd"], fine["volumeAft"])
    rows = [
        ("volume 250-300 m3", 250.0 <= fine["volume"] <= 300.0, "%.1f" % fine["volume"]),
        ("Cwp 0.68-0.75", 0.68 <= fine["cwp"] <= 0.75, "%.3f" % fine["cwp"]),
        ("LCB 0-1.5 %L aft", 0.0 <= lcb_pct_aft <= 1.5, "%.2f %%" % lcb_pct_aft),
        ("bow flare >= 2.0 @ 0.4L fwd", fine["flareRatio"] >= 2.0, "%.2f" % fine["flareRatio"]),
        ("fore/aft volume within 15 %", bal <= 0.15, "%.1f %%" % (bal * 100.0)),
    ]
    return rows


def report(form, js, th, fine):
    L = form.L
    p = []
    p.append("STEAMER HYDROSTATICS  (fine integration | from the 13 strip tables)")
    p.append("  LWL %.1f  LOA %.1f  B(wl) %.2f  B(guards) %.1f  T %.2f  D %.2f"
             % (L, form.z_stem_deck - form.z_stern_deck, fine["beamWL"],
                form.d["beamOverGuards"], form.T, form.d["depth"]))
    for k, unit in (("volume", "m3"), ("massKg", "kg"), ("waterplane", "m2"),
                    ("kb", "m"), ("bm", "m"), ("kg", "m"), ("lcbZ", "m"),
                    ("lcfZ", "m"), ("iT", "m4"), ("iL", "m4")):
        p.append("  %-11s %12.3f | %12.3f %s" % (k, fine[k], th[k], unit))
    p.append("  GM %.2f (target)   com.y %.3f   BML %.1f m"
             % (fine["gm"], th["kg"] - form.T, fine["bml"]))
    p.append("  Cb %.3f  Cwp %.3f  Cm %.3f  Cp %.3f"
             % (fine["cb"], fine["cwp"], fine["cm"], fine["cp"]))
    p.append("  LCB %.2f %% L aft   V fwd %.1f / V aft %.1f  (ratio %.3f)"
             % (-fine["lcbZ"] / L * 100.0, fine["volumeFwd"], fine["volumeAft"],
                fine["foreAftRatio"]))
    p.append("  flare @ z=+%.1f: deck hb %.2f / WL hb %.2f = %.2f"
             % (0.4 * L, fine["flareDeckHb"], fine["flareWlHb"], fine["flareRatio"]))
    p.append("  freeboard: stem %.2f  waist %.2f  stern %.2f   forecastle break z=%.2f (%.0f %% LOA)"
             % (form.deck_y(form.z_stem_deck), form.deck_y(0.0),
                form.deck_y(form.z_stern_deck), form.break_z,
                (form.z_stem_deck - form.break_z) / (form.z_stem_deck - form.z_stern_deck) * 100))
    p.append("  helm (%.1f, %.2f, %.1f)" % (js["helm"]["x"], js["helm"]["y"], js["helm"]["z"]))
    p.append("  stn     z   keelY  deckY  hb(WL)  hb(deck)  area(WL)")
    k0 = (form.n_levels - 1) // 2 - 1
    for i, s in enumerate(js["stations"]):
        p.append("  %2d %6.2f  %5.2f  %5.2f  %6.3f  %7.3f  %8.3f"
                 % (i, s["z"], s["keelY"], s["deckY"], s["halfBreadth"][k0],
                    s["halfBreadth"][-1], s["area"][k0]))
    p.append("  ACCEPTANCE")
    rows = check(form, fine)
    for name, ok, val in rows:
        p.append("    %-30s %-10s %s" % (name, val, "ok" if ok else "FAIL"))
    return "\n".join(p), [n for n, ok, _ in rows if not ok]


def main(argv):
    form = HullForm()
    js, th, fine = form.build_json()
    text, fails = report(form, js, th, fine)
    print(text)
    # Tables vs geometry must agree, or the strip model floats a different ship.
    assert abs(th["volume"] - fine["volume"]) / fine["volume"] < 0.01, "table volume drifted"
    assert abs(th["waterplane"] - fine["waterplane"]) / fine["waterplane"] < 0.01
    assert not fails, "acceptance failed: " + ", ".join(fails)
    if "--dry" not in argv:
        os.makedirs(os.path.dirname(HULLFORM_PATH), exist_ok=True)
        with open(HULLFORM_PATH, "w") as f:
            json.dump(js, f, indent=1)
        print("wrote " + HULLFORM_PATH)


if __name__ == "__main__":
    main(sys.argv[1:])
