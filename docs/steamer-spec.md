# Steamer — engineering contract

The **stern-wheel** paddle steamer that sails side-by-side with the ladder
ship. Rebuilt 2026-09-19 to the approved reference sheet in
`art-staging/stern-paddle-integrated-hull-v2/`: a broad oak workboat with one
big paddle recessed inside her stern, an open gun deck, a centreline chimney
and an aft helm. The side-wheeler she replaced is archived, runnable, under
`tools/blender/archive/sidewheel-v1/`.
Approved plan: `~/.claude/plans/in-the-seasick-game-indexed-bubble.md`.
This file is the contract between the hull generator (Python/Blender) and the
physics (C#). Change it here first, then in code.

## 1. Frames and units

Unity ship-local: **+Z bow, +Y up, +X starboard**, metres, kg, seconds.
Origin: centreline, **design waterline (y = 0)**, **midpoint of the LWL (z = 0)**.
Helm sign: `Rudder = +1` turns her to starboard (positive yaw about +Y).
Blender authoring frame (repo convention): bow +X, up +Z, beam ±Y, waterline z = 0.

## 2. Seed particulars (the study may move them; everything reads the JSON)

| | |
|---|---|
| LWL / LOA | 30.0 / **32.2 m, measured to the transom** — the aft shape hangs off a virtual profile end 0.6 m further aft, and reporting LOA to *that* read 32.8 for a ship you can only see 32.2 m of |
| Hull beam (WL) | 11.0 m. **L/B ≈ 2.93**: she is this broad because the paddle lives *inside* her, not on sponsons beside her. The beam that used to be paddle guards is hull now, and 2.93 is the reference sheet's own ratio |
| Draft / depth amidships | 2.05 / 4.0 m (deck 1.95 m above WL at the waist) |
| Freeboard | 3.9 m at the stem, 1.95 m waist, 2.25 m at the stern; fair sheer between. **No forecastle** — one flush open deck end to end |
| Volume | 300–450 m³ (Cb ≈ 0.62); mass = 1025 × volume exactly, so she floats on her marks |
| Waterplane coefficient | 0.70–0.86 |
| LCB | **2.0 to 5.5 % L aft** of midships. A band with a floor as well as a ceiling: a stern-wheeler is *meant* to carry her buoyancy aft, under the wheel, and her entry fine. Fore/aft immersed volume 15–30 % apart for the same reason |
| Form | fine V entry with strong flare (deck half-breadth at 40 % L forward of midships ≥ 2.0 × the waterline half-breadth there); boxy U midbody (Cm ≈ 0.90); **squared transom** with real width and real depth — the analytic plan would close to a point, so the loft is cut at `form.transomZ` and what is left is the transom; skeg aft; bilge keels |
| Stability | **GM target 3.6 m** → `KG = KB + BM − GM`, giving KG 2.21 m above the keel (0.16 m above the waterline). Roll gyradius **6.2 m**, which parks T_roll ≈ 6.6 s between the wind sea ≤ 5.3 s and the swell ≥ 10 s. Pitch gyradius 0.25 L, yaw 0.26 L. **Both numbers are consequences of the beam, and they were paid for once already:** a hull this broad has BM ≈ 4.7 m off the tables, so the first pass held GM at 2.0 and put KG at *deck level* — and a top-heavy ship heels **outward** in a turn, which measured 27.6°. Raising GM drops KG onto the waterline where a lateral force below it barely heels her at all; the gyradius then has to rise with it to keep the roll period. 0.56 B is generous, and justified by where her mass actually is: a 4.6 m wheel right aft and six guns out at ±4.5 m |
| Wheel | **ONE stern wheel on the centreline**, axle at (0, +0.72, −13.2), radius 1.62 m, width 4.6 m, 8 floats 0.9 m deep, design dip 0.9 m. It turns in a **well** cut into her stern between two hull cheeks: half-width 2.4 m, forward bulkhead at z = −11.3, floor at y = −1.05, roof the raised stern platform |
| Rudder | centre of effort (0, −1.0, −10.6), **area 16 m² as a gang of four blades** at x = ±1.3 and ±3.4, each 2.4 m × 1.65 m. Forward of the wheel, standing in the race it pulls past — see §5. One blade of 16 m² on a 2 m draft would be 8 m long, so the drawing and the number agree only as a gang; with 7 m² she needed an 8.5-length turning circle |
| Topsides | no cabin and no cargo: one open planked deck with 1.0 m bulwarks and a sage-teal cap rail; a raised stern platform (+0.62 m from z = −9.6 aft) roofing the paddle well; **one slim octagonal chimney on the centreline** at z = 0, top 7.0 m above WL; helm right aft at z = −8.6; six guns, three a side, at z = +8.2 / +2.6 / −3.0 |

**The paddle well is below the waterline and the physics knows it.**
`HullForm.net_half_breadth` subtracts the well from every section it
integrates, so volume, waterplane, KB, LCB and LCF are the hull she is
*drawn* as. One consequence is tabulated honestly rather than hidden: a
notched waterplane is two strips, and the point-strip model of §4 has one
sample offset per station, so the strip tables' BM (≈ 4.89 m) comes out below
the true geometric BM (≈ 5.33 m). `KG` is derived from the **table** BM, so
the GM the physics actually delivers is the 2.0 m asked for.

## 3. `Assets/_Project/Resources/Steamer/hullform.json` (read with `JsonUtility`)

```jsonc
{
  "lwl": 34.0, "loa": 36.0, "beam": 7.6, "beamOverGuards": 12.4,
  "draft": 2.2, "depth": 4.2,
  "volume": 0.0, "massKg": 0.0,            // massKg = 1025 * volume
  "waterplane": 0.0, "kb": 0.0, "bm": 0.0, // kb above keel, metres
  "gm": 2.0, "kg": 0.0, "lcbZ": 0.0,
  "gyradiusRoll": 5.4, "gyradiusPitch": 8.5, "gyradiusYaw": 8.8,
  "com":   {"x": 0, "y": 0, "z": 0},        // (0, kg - draft, lcbZ)
  "wheelAxle": {"x": 0.0, "y": 0.72, "z": -13.2}, // ONE wheel, on the centreline
  "wheelRadius": 1.62, "wheelWidth": 4.6, "wheelFloats": 8,
  "wheelFloatDepth": 0.9, "wheelDesignDip": 0.9,
  "rudder": {"x": 0, "y": -1.0, "z": -10.6}, "rudderArea": 7.0,
  "helm":  {"x": 0, "y": 0, "z": 0},        // where a helmsman's feet go, on the planking
  "gunSockets": [ {"x": 0, "y": 0, "z": 0} ],  // STARBOARD only; CannonBattery.Fit mirrors them
  "funnelTopY": 7.5,
  "stations": [                               // 13, evenly spaced along the LWL, aft to bow
    { "z": -15.7, "dz": 2.615,               // strip centre and strip length; sum(dz) = lwl
      "keelY": -2.0, "deckY": 2.5,           // local keel and deck-edge heights
      "y":          [ ... 17 levels, keelY .. deckY + 1.5, ascending ],
      "halfBreadth":[ ... half-breadth at each level (constant above deckY) ],
      "area":       [ ... FULL-section immersed area (both sides) below each level; constant above deckY: the deck is watertight, water on deck adds nothing ],
      "momentY":    [ ... first moment of that area about y = 0, i.e. integral of y dA; centroid = momentY/area ]
    }
  ]
}
```
`volume`, `waterplane`, `kb`, `bm`, `lcbZ` are integrated from the same station tables at level y = 0, so the C# side can re-derive and assert them.

## 4. `HullFormBody` — strip buoyancy (C#, `SeaSick.Steamer`)

Each station is two **half-strips** (port, starboard). Sample point and force arm are the same lateral offset `xs = 0.57735 × halfBreadth(y=0)` of that station (min 0.3 m) — that choice makes the transverse waterplane moment of a wall-sided strip exact (`xs² = hb²/3`), so BM, and with the CoM from the JSON, GM, come out of the geometry.

Per fixed step, per half-strip `(i, side)`:
1. `P = TransformPoint(side·xs, 0, z_i)`; `h` = sampled raw surface height at P (registry handle).
2. Local immersion level `yw = (h − P.y) / max(up.y, 0.5)`.
3. `V = 0.5 · area_i(yw) · dz_i` (table lerp, clamped to the ends); centroid height `yc = momentY_i(yw)/area_i(yw)`.
4. Surface slope at the strip from the hull's **own samples**, never from `sample.normal` (that carries sub-metre chop the hull cannot feel): longitudinal = central difference of station-mean heights along z; transverse = (h_stbd − h_port)/(2·xs). Convert to world XZ gradient `g`, clamp `|g| ≤ 0.6`.
5. Buoyancy `F = ρ g V · (up_world − g)` applied at `TransformPoint(side·xs, yc, z_i)`. The horizontal part is the Froude–Krylov force: surfing, wave surge and sway are emergent and length-averaged. There is no reserve, plow, dynamic-lift, burial-clamp or attitude-limit term. Flare in the tables is the reserve.
6. Vertical damping `−c_h · w_i · (v_P.y − v_water.y)` at P, `w_i` = the strip's share of waterplane, `v_water` = `AmbientFlow + sample.velocity`; only while `0 < V`. `c_h` from heave ζ = 0.35 (`2ζ√(ρ g Aw · m)`). At init compute the roll and pitch damping those strip dampers already give (Σ c·arm²) and add hull-frame torques for the remainder to reach **roll ζ 0.45, pitch ζ 0.55** against `K_roll = m g GM`, `K_pitch = ρ g I_L + …` (read off the tables), scaled by submersion 0..1.
7. Cross-flow (sway) per **station** at `(0, lateralForceY, z_i)`, `lateralForceY = −0.5·draft` default: with `u` = along-keel and `v` = athwart water-relative velocity at that point, immersed lateral area `A_i = (yw_mean − keelY_i)⁺ · dz_i · skeg_i` (`skeg_i` = 1.4 on the aft three stations, 1.0 elsewhere — directional stability):
   `F_lat = −ρ A_i ( ½ Cd v|v| + Clβ |u| v + k_lin v )`, defaults `Cd 1.1`, `Clβ 0.9`, `k_lin 0.15`.
8. Surge resistance at the CoM along the flattened keel, water-relative `u`: `R = −m (a1 u + a2 u|u|)`, defaults `a1 0.05`, `a2 0.006`, scaled by submersion. (Top speed is where `PaddleDrive`'s thrust meets this.)
9. Yaw damping torque `−c_yaw · r` with `c_yaw = 0.25 · I_yaw` default, on top of what the cross-flow strips give.
10. Publish every step: `Submersion` (immersed volume / design volume, clamped 0..1), `MeanWaterHeight`, `MaxDeckImmersion = max(yw − deckY_i)`, `BurialDepth`-equivalent, `FkSurgeAccel`, `FkSwayAccel` (FK horizontal force / mass on the keel and athwart axes, 0.4 s EMA), `DesignVolume`, `HalfStripLevel(i, side)`. Mirror the first four into the disabled `BuoyantBody.PublishExternal(...)`.
11. Until `OceanSampler.Ready` and every handle has been sampled once: apply nothing and hold the rigidbody kinematic-free by zeroing gravity's effect (`rb.AddForce(-Physics.gravity * rb.mass)`), so she does not fall through the sea on frame one.

Rigidbody: `mass = massKg`, `centerOfMass = com`, `inertiaTensor = m·(kPitch², kYaw², kRoll²)` as (x, y, z), `inertiaTensorRotation = identity`, gravity on, interpolate, linear damping 0, angular damping 0.05.

Sampling: `HullFormProbeFeeder` `[DefaultExecutionOrder(-100)]` writes handle positions in `FixedUpdate` (the driver runs at −90 and stamps `handle.sample`; `HullFormBody` runs at −80 and reads same-step samples). Register in `OnEnable`, unregister in `OnDisable` (the static registry survives play sessions with domain reload off). `PaddleDrive` adds two handles for the wheel the same way (fore and aft of the axle at ±0.8 m, y = 0, on the centreline).

## 5. `PaddleDrive` (C#, `SeaSick.Steamer`)

Inputs read from `ShipMotor`: `Throttle` (rate-limited order, −1..1), `Rudder`, `Anchored`.
Derived constants (from `topSpeed 15.5`, `slipAtTop 0.25`, `bollardAccel 2.6`, and `HullFormBody`'s `a1`, `a2`):
- `Reff = wheelRadius − 0.5·designDip`; `ωmax = topSpeed / (1 − slipAtTop) / Reff`.
- Thrust `T = K · Δu|Δu| · f(dip)`, `Δu = ω·Reff − u_wheel` (water-relative along-keel speed of the hull at the wheel). `K` solves `T = m(a1 v + a2 v²)` at `v = topSpeed`, `Δu = slipAtTop·ωmax·Reff`, `f = 1`.
- `τmax = m · bollardAccel · Reff`. Shaft: `J ω̇ = clamp(Kp(ωcmd − ω), ±τmax) − T·Reff`, `J` chosen so an unloaded wheel spins up in ≈ 1.4 s.
- `f(dip)`: `dip` = mean sampled level at the wheel − (axleY − R) in hull-local metres; `f = smoothstep(0, designDip, dip)`, then falls linearly to 0.6 between 1.8× design dip and axle-deep. A lifted wheel has no load and races to the governor; a buried one lugs.
- Command: `ωcmd = Throttle · ωmax`, clamped ±ωmax, astern scaled by `asternFraction 0.45`. There is **no differential**: one wheel on the centreline has none to give.
- Thrust applied at `(0, axleY − Reff, axle.z)` along `transform.forward`. Zero while `Anchored`.
- **Rudder, and with it the whole of her low-speed handling.** The wheel is the aftmost thing on her and the rudder stands in what it is pulling through, so the blade is given not her way through the water but
  `u_rudder = u + raceGain · (ω·Reff − u) · f(dip)`, `raceGain 0.55`.
  Then `δ = Rudder·35°`; `F = ½ ρ A_r · 2.4·sin δ·cos δ · u_rudder|u_rudder| · rudderGain`, applied at `rudder` along `−right` for positive δ (pushes the stern to port → bow to starboard). With no way on, the race *is* the inflow: ring her ahead with the helm over and she comes round on the spot; drift with the engine stopped and the helm does nothing. That is true of the real thing and is the honest price of the single wheel.
- Publish to `ShipMotor.PublishExternal(FkSurgeAccel⁺, FkSwayAccel, 0)`.
- Visuals: one wheel transform rotated about local X by a visually-geared rate `ω_vis = m·tanh(ω/m)`, `m = 3.2 rad/s`; `WheelRate` stays honest. Each step the wheel is loaded, `DynamicWaterSim` gets a `Stamp` on the centreline astern proportional to `|T|`.

## 6. Tuning gates (probes, M3/M4)

FLOAT draft within 3 cm of drawn, wheel dip 0.9 ± 0.1 m · DECAY T_roll 6.5–8 s, ζ 0.4–0.5, pitch no overshoot · DRIVE top 15–16 m/s, 0→90 % ≈ 8 s, astern works · TURN **from rest with the telegraph a quarter ahead** she comes round inside 2.0 L, and over the FIRST 4 s the rudder's inflow is visibly larger than her way (the race is doing the steering — measured over the settled part of the turn instead, it is guaranteed not to be, because by then she is up to the speed the wheel drives); circle ≤ 3.5 L at speed, heel ≤ 8°, turn builds in ≈ 0.5 s · SWAY at local Hs 3.5 roll RMS < 2.5°, no green water at severity 0.60, ≥ 80 % of calm speed into a head sea.

The old TURN gate — "pivots in her own length with the telegraph at stop" —
was a measurement of two wheels turning against each other and cannot be met
by any single-wheel ship. It is replaced, not relaxed.
