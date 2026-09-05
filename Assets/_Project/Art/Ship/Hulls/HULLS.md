# The progression hulls

Five hulls, each about 1.7x the last, for the build-and-upgrade ladder. As of
**2026-09-02 they are finished assets**, not shells: planked hull with a real
bulwark and capping rail, gun ports cut through with linings, deck, keel/stem/
sternpost backbone, rudder, masts, channels, bowsprit and a sail per mast.

Generated, not sculpted: `~/blender_objects/seasick_hulls.py` lofts every hull
from a parameter table and `~/blender_objects/ship_hulls.blend` is the result.
Re-run it and everything rebuilds, so **change the table, not the mesh**. That
rule used to end at "until you start detailing"; it does not any more. The
detail is generated too, which is why converting the other four hulls after the
brig cost parameters rather than four hand-modelling jobs — and why the fleet
can be re-cut for LODs by dropping station and level counts.

## The fleet

| | hull | LOA | beam | draft | keel→rail | crew long | what it is |
|---|---|---|---|---|---|---|---|
| T1 | log raft | 5.65 | 3.40 | 0.29 | 0.61 | 3.3 | seven lashed logs, awash |
| T2 | fishing skiff | 9.00 | 2.90 | 0.55 | 1.45 | 5.3 | open boat, one sail, oars |
| T3 | coastal sloop | 15.00 | 4.80 | 1.35 | 2.55 | 8.8 | first decked hull, bulwarks, a hold |
| T4 | brig | 26.00 | 7.80 | 2.50 | 5.20 | 15.3 | one gun deck, real cargo |
| T5 | three-decker | 46.00 | 13.00 | 5.40 | 12.40 | 27.1 | three batteries, a hold you can lose things in |

Metres. LOA includes the stem and counter rake, not a bowsprit. Depth is
amidships — every hull has sheer, so the ends stand higher (the three-decker's
bow rail is 2.0 m above her midship rail).

The same numbers live in `WorldScale.Fleet`, and `SetupHullLab` measures the
imported meshes against them. A hull that stops matching its number is a
failing check, not a shrug — see the traps below for why that gate exists.

**The current paddle steamer is 24.2 m**, so she stands just short of the brig.
`HullLab` puts her footprint in the row as an orange rectangle; that is the
only honest way to see which rung the player is already on.

## The three-decker's progression

Her hull is built **once**, at full height. The last three upgrades are not
hulls, they are batteries — fitting out the lower deck, then the middle, then
the upper. So the ladder is: raft → skiff → sloop → brig → three-decker's hull
→ one battery → two → three. Eight steps out of five hulls.

Empties in the FBX mark the levels (heights relative to the waterline):

| level | deck | gun port sills |
|---|---|---|
| hold floor | −4.60 | — |
| lower battery | +0.55 | +1.30 |
| middle battery | +2.85 | +3.60 |
| upper battery | +5.15 | +5.90 |

Rail at +7.00. The topmost battery's ports reach within 1.1 m of it, which is
exactly why a real three-decker has almost no side above her upper guns.
Deck-to-deck is 2.30 m; that is low, and historically correct — *Victory*'s
lower deck has 1.75 m of headroom.

Cargo lives below the lower battery: 5.95 m of depth over the full length.
That is the reason to want her.

## Conventions

**Blender**: bow **+X**, up **+Z**, beam **±Y**, metres, waterline at **z = 0**.
Same as `paddle_boat.glb`, so both models can be worked on the same way.

Each hull is a **starboard half with a live Mirror modifier**, plus a Solidify
that gives the planking thickness inward — so the outside face is the authored
hull and the stated beam is the real beam. Both are applied at export. Edit the
half; the other side follows.

**Unity**: the FBX arrives bow on **+Z**, up **+Y**, scale **1**, origin at the
waterline amidships. Nothing needs a rotation or a scale fix. Drop it straight
onto a ship object and the buoyancy plane is y = 0.

## Regenerating

In Blender (the MCP can run this directly):

```python
p = "/Users/kevinandersson/blender_objects/seasick_hulls.py"
g = {"__name__": "seasick_hulls", "__file__": p}
exec(compile(open(p).read(), p, "exec"), g)   # rebuilds all five
g["export_fleet"]()                            # writes the five FBX into Unity
```

Then in Unity: `SetupHullLab.Execute()` — reimports, **measures**, rebuilds
`Assets/_Project/Scenes/HullLab.unity` and writes `/tmp/seasick-hulls.png` and
`/tmp/seasick-hulls-plan.png`.

## Traps this cost, all measured

- **The FBX arrived with mesh nodes at scale 100 and vertices at 1/100.** The
  overall size still came out right, so nothing looked wrong until something
  read `localScale`. `apply_scale_options='FBX_SCALE_ALL'` with
  `bake_space_transform=True` is what fixes it, not `global_scale`.
- **Blender +X lands on Unity −X.** Measured: `Unity(x,y,z) = (−Xb, Zb, −Yb)`.
  The bow drawn at +X therefore points **aft** in Unity — which is exactly what
  `SetupPaddleBoat.VisualYaw = 90` has been correcting all along. The export
  now yaws each hull −90° about Blender Z before baking, so the correction
  happens once, in the exporter, and never again downstream.
- **`axis_forward` on the exporter changed nothing Unity could see.** Do not
  trust the flag; measure the imported bounds.
- **Bounds alone cannot tell you which way a hull faces.** A symmetric hull
  measures the same either way round. `SetupHullLab` compares the *breadth at
  each end* instead — the bow is the fine one — and that is the check that
  caught the reversed hulls.
- **Uniformly spaced stations under-sample a bow.** The first lofts creased at
  the stem because all the curvature is in the last 15% of the length. Cosine
  spacing put the stations where the shape is.

## Float test — 2026-09-01

`RunProbe.Float()` (`HullFloatProbe`) rigs all five with buoyancy probes and
honest masses, floats them beside the paddle steamer through five sea states
and measures them. Three runs, all agreeing. Raw numbers in
`float-test-2026-09-01.txt`.

**The masses are the real ones**: 1025 kg/m³ times the volume each hull
displaces at her drawn waterline (`hydrostatics()` in the generator) — 4.4 t
raft, 5.3 t skiff, 45.6 t sloop, 270 t brig, 1881 t three-decker. Those are
plausible against real vessels (a 26 m brig was 200–300 t; *Victory* at 57 m
was 3500 t, and 46/57 cubed of that is 1840).

**They float where they were drawn**, within 2 cm, all five.

**And the probe rig follows one rule.** Solved independently, each hull wanted
her probes lifted 0.15 / 0.29 / 0.73 / 1.37 / 3.01 m above her drawn keel, on
drafts of 0.29 / 0.55 / 1.35 / 2.50 / 5.40. That is 0.52, 0.53, 0.54, 0.55,
0.56 of the draft — one ratio across a 19x range of hull. It is also the
paddle steamer's `ProbeLift`: 0.55 m on a 1.02 m draft is 0.54. That constant
has stood in the project as a measured one-off since 2026-08-28; it is a
property of the probe layout, and every hull needs it.

    probe keel = drawn keel + 0.55 x draft

**What ranks correctly, and what does not.** Bigger is meant to mean seaworthier.
Two measures say so and the rest do not:

| at Hs 27 m | raft | skiff | sloop | brig | 3-decker | |
|---|---|---|---|---|---|---|
| vertical accel, g | 2.8 | 2.1 | 2.0 | 1.5 | 1.2 | correct |
| deck under, % of time | 22 | 9 | 8 | 0 | 0 | correct |
| roll RMS, ° | 14.7 | 17.3 | 15.7 | 17.0 | 17.5 | flat/backwards |
| pitch RMS, ° | 7.2 | 4.2 | 5.0 | 4.0 | 7.0 | no order |

**Why roll cannot rank: the waves are far longer than any hull.** Measured
wavelength runs 33 m in a calm to 214–300 m in a storm, so LOA/wavelength for
the fleet is:

| | calm | lively | rough | heavy | wild |
|---|---|---|---|---|---|
| wavelength | 33 m | 88 m | 107 m | 300 m | 214 m |
| raft | 0.17 | 0.06 | 0.05 | 0.02 | 0.03 |
| 3-decker | 1.38 | 0.52 | 0.43 | 0.15 | 0.21 |

A hull only bridges, slams and resists a sea as LOA/wavelength approaches 1.
Below about 0.2 everything is a cork that follows the surface, and length
cannot matter. From "lively" upward every hull in the fleet is a cork. The one
state where the three-decker is long enough to bridge is the calm.

**The sea has height but no texture.** Steepness (Hs/wavelength) measures
0.030 / 0.029 / 0.058 / 0.050 / 0.126 — a real ocean runs 0.02–0.05 and breaks
near 0.14, so only the storm is steep. From a deck at 4 m, "rough" (Hs 6.2 m)
is glass. This is NOT the shader/sampler parity bug it looks like: beads pinned
to the sampler's height sit exactly half-submerged in the rendered surface in
every shot.

**The paddle steamer does not move.** Through all five states she reads roll
max 0.2–3.9°, heave RMS 0.00–0.03 m, vertical accel 0.0–0.1 g. In the 27 m sea
that rolls the new hulls past 30° she rolls 3.9°. She is heavily damped, her
inertia box (4.4 x 3 x 13) is far smaller than her 24.2 x 8.44 hull, and her
19.2 t is about a thirteenth of what her shape displaces.

## The wave fix — 2026-09-02

`TuneWindSea.Execute()` added a **wind-sea band** to the spectrum: a third
gaussian train, deliberately short, in cascade 1 (16–64 m) — the band the two
swells and the JONSWAP peak all leave empty at this game's wave heights.
0.40 m at 18 m in a calm rising to 5.00 m at 44 m in a storm, each at about
0.11 steepness, under the 0.14 breaking limit. Re-measured with the same probe:
`float-test-2026-09-02-windsea.txt`.

**The mechanism is length-averaging, not steepness.** A hull averages a wave
over her own length. The storm swell is *steeper* than anything added here —
64 m over 470 m is 23°, against the new band's 16° — but every hull in the
fleet spans a tenth of it or less, so all of them sit on one uniform tilt and
follow it identically. At 44 m the three-decker spans 1.05 wavelengths and
averages them away while the raft spans 0.13 and takes the whole ride.

| measured | before | after |
|---|---|---|
| wavelength, lively | 88 m | 56 m |
| wavelength, heavy | 300 m | 150 m |
| LOA/λ span, lively | 0.06–0.52 | 0.10–0.83 |
| LOA/λ span, heavy | 0.02–0.15 | 0.04–0.31 |
| storm Hs (measured) | 27.1 m | 27.7 m |

Wave height was preserved — that was the constraint. The primary 64 m / 470 m
train is untouched.

**What ranks now**, raft → three-decker at Hs 27.7 m:

| | raft | skiff | sloop | brig | 3-decker |
|---|---|---|---|---|---|
| vertical accel, g | 3.5 | 2.7 | 2.5 | 1.6 | 1.3 |
| rail under, % | 16 | 11 | 8 | 0 | 0 |
| deck under, % | 21 | 10 | 9 | 0 | 0 |

Both were already ordered before; both separate harder now (the raft/three-decker
acceleration ratio went 2.3x to 2.7x), and the same ordering now appears in
"rough" as well, which is where the game is actually played.

**Roll still does not rank, and this stopped trying to make it.** Roll is
driven by wave slope across the BEAM, so it wants wavelengths near 13 m, not
near 46 — and at 13 m the steepness limit caps a train at about 1.5 m, nothing
beside a 64 m sea. The three-decker will always roll most here because she is
the beamiest thing in the water.

**Shortening the crossing swell was tried and reverted.** Moving it from 24 m
at 170 m to 12 m at 90 m cleaned up pitch ordering, but measured storm Hs fell
27.1 → 20.7 m and the raft went from having her deck under 22 % of the time to
4 %. Trading the storm's teeth for one column of ordering is a bad deal. The
reasoning and the numbers are in `TuneWindSea`.

**Open: the new short waves may not be DRAWN as far out as they are
simulated.** In the close shots the surface beads (pinned to the sampler's
height) are cleanly half-submerged near the camera and sit fully proud of the
water 40–60 m out. That is the signature of clipmap tessellation, not a
spectrum problem: `innerCellSize 0.5` over 8 rings reaches 64 m cells, and a
34 m wave needs about four cells per wavelength to be drawn at all. The physics
has the wave everywhere; the mesh may only render it close in. Worth checking
against `OceanQuality_PC.clipmapRings` before judging the look.

## Game-ready topology — 2026-09-02

Kevin's rule for these assets: a mesh may intersect another object (a mast goes
through a deck) but **it may never contain two of its own faces in the same
place**, or two vertices, or an edge of no length. `report_topology()` in the
generator enforces it and `export_fleet()` refuses to run while anything trips.
All five now report CLEAN.

| | tris | was |
|---|---|---|
| T1 raft | 444 | 252, with 18 n-gons |
| T2 skiff | 2,564 | 1,760 |
| T3 sloop | 2,696 | 2,384 |
| T4 brig | 3,328 | 5,950 |
| T5 three-decker | 6,448 | 4,208 |

The brig **halved** because a Solidify modifier had been building a second
complete hull inside the first: 2,572 of her 5,950 triangles, to line seven
0.68 m holes. The others grew because they gained the detail she already had.

**The section is a J.** Up the outside from the keel, over a real capping rail,
and back down an inner face as far as `inner_floor`. Thickness exists exactly
where the eye can reach it and nowhere else. `inner_floor` is the lowest deck
that carries guns — the brig's one battery is above her deck so it stops there,
but two of the three-decker's are BELOW her upper deck, and stopping at the
upper deck would make her lower ports holes in a single sheet you could see
clean through the ship.

**A gun port is one face row.** No level may sit between a sill and its head:
that is what keeps the lining at four quads and the jamb at one. The
three-decker broke this — her filler level at 1.54 m landed inside her lower
battery's band of 1.30-2.10 and split those ports in two, so the lining spanned
a diagonal instead of an edge. 48 non-manifold edges. `_level_plan` now strips
anything inside a band and `_weld_bulwark` raises a named error if it recurs.

**Loops earn their place.** Port jambs ARE stations, not extra ones (station
counts dropped to pay for them), and the stations between them are graded fine
at the ends and coarse amidships — spacing ran 0.17 m beside 2.12 m before that,
and a smooth-shaded 2 m quad next to a 0.7 m one wrinkles visibly. Underwater
loops space by ARC LENGTH, not evenly in height: even-in-height put three loops
within 0.08 m of each other on the flat by the waterline and gave the whole turn
of the bilge one loop.

**Both ends close onto a centreline column**, not an n-gon cap, and the end
plane is TILED rather than covered twice: single skin to y=0 below the deck, the
wall's end grain above it. The inner face is deliberately NOT run in to the
centreline — three surfaces would meet along its forward edge, which is a
T-junction and non-manifold however it is wound. What is left is a slot at the
stem, 0.23 m on the brig, filled by the backbone timber.

**Normals are recalculated as a build step**, checked by signed volume, rather
than by winding 28 lining faces per hull by inspection.

**Gun-port linings shade FLAT.** Smooth, they average into the vertex normals
of the outer skin all round each opening; on the three-decker's 72 ports that
read as a quilt of diamonds across her whole side. A lining points into its
tunnel and has no business bending the planking around it.

### What is deliberately left open

- One boundary loop per side at the deck line, where the bulwark's bottom sits
  on the deck. Closing it back to the outer skin is a T-junction.
- The stem/sternpost slot above, covered by the backbone.
- Four triangles per hull at the keel convergence at each end. A stem
  converging to a point IS a triangle; forcing a quad there makes a folded,
  zero-area one.

### Displacements moved, and no hull changed shape

Arc-length sampling follows the section where even-in-height cut the corner at
the bilge, so the polygon is fuller and truer:

| | was | now |
|---|---|---|
| skiff | 5.19 | 5.20 |
| sloop | 44.48 | 45.13 |
| brig | 263.84 | 269.56 |
| three-decker | 1835.11 | 1880.80 |

`HullFloatProbe`'s Spec table carries these. If one moves, move the other —
`SetupFleetShip` has the same table.

### Sailing one

`RunProbe.T1()` .. `T5()` put a hull under the player in `Sea.unity`, with her
honest mass, `FleetLayout` probes at 0.55 x draft, damping at the tuned ratio,
`windDriven` ON and the sail wired to `mastPivot` so it trims.
`RunProbe.Steamer()` puts the paddle steamer back. Known rough edges: crew and
lanterns still stand at the paddle steamer's deck positions, the sails have no
material of their own, and the camera is tuned for a 24 m ship.

## Left to do

Rig and detail, per hull: keel, stem and sternpost battens; gun ports cut on
the marked levels; masts and channels; rudder; hatches; head and beakhead on
T5. The hull grids are clean quads with horizontal loops running the full
length, so a port row is a face selection along one loop.
