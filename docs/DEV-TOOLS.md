# Dev tools

Everything here lives in `Assets/_Project/Scripts/Dev/Editor/` and is driven
through the Coplay MCP bridge with `execute_script`.

## The loop

1. Edit C# / shaders with normal file tools.
2. `execute_script RefreshOnly.cs` — Unity does **not** pick up external edits
   without an explicit `AssetDatabase.Refresh`.
3. `check_compile_errors` — **C# only**. A broken shader reports "No compile
   errors" and then renders magenta; the real message is in
   `~/Library/Logs/Unity/Editor.log`, `grep "Shader error in"`.
4. `execute_script <setup script>` to push values into the scene.
5. `play_game` → `execute_script <probe>` → read its output file with Bash →
   `stop_game`.

**Never edit anything under `Assets/` while a probe coroutine is running** —
Unity auto-refreshes, the domain reloads, and the coroutine dies silently while
`execute_script` still reports success.

Each `execute_script` compiles a **fresh assembly**, so a MonoBehaviour created
in one call cannot be found by type in the next. Probes write results to a file.

## Setup scripts — these fight the serialization trap

Unity bakes a component's field values into the `.unity` file when the component
is added, and those beat the C# initialisers forever. Materials do the same with
shader property defaults. Re-run these after changing any default.

| Script | What it pushes |
| --- | --- |
| `SceneDefaults.cs` | `ResetToCodeDefaults(Component)` — copies every serialised value off a freshly constructed instance, skipping object references. The general fix; copy it into new setup scripts. |
| `SetupStormSky.cs` | Sky material + render settings, `SkyDirector`, `StormSpray`, and targeted pushes for `ChaseCamera` and `SpeedJuice`. Targeted, not blanket, where a component has hand-tuned scene values. |
| `SetupOceanScene.cs` | **The cutover scene surgery on `Sea.unity`**: builds the new Ocean root (renderer + clipmap + physics driver + region field + weather + ripple stub), strips missing-script stubs, gives the ship its Rigidbody/probe set, and resets `BuoyantBody` to code defaults. Re-run after changing any BuoyantBody default. |
| `SetupOceanLab.cs` | Builds `OceanLab.unity` (the ocean stack's test scene: free camera, proxy sloop, weather controller) and the OceanQuality/SeaState assets. |
| `SetupAndroidGraphics.cs` | Locks Android to Vulkan only (the FFT is compute; GLES3.0 has none). |
| `SetupSeaTerrain.cs` | **The island cutover on `Sea.unity`**: deletes the legacy World object, turns `Island_Home` into the bare `HomePoint`, adds the `Terrain` object (`TerrainStreamer` + `TerrainShoreField` + `TerrainWorldPopulator`) on the ship, creates `WorldSettings`, and picks `TerrainSettings.worldOffset` so real land sits under the old home position with water at the spawn. Re-run after changing terrain defaults. |
| `SetupTerrainLab.cs` | Builds `TerrainLab.unity` (the island generator's test scene): the `TerrainSettings` asset, the 2D map visualiser quad (doubles as a water stand-in at y=0), a 7×7 main-thread `TerrainChunkPreview` over the western island, the runtime `TerrainStreamer`, sun and camera. Also pushes the terrain material. |
| `WestTrace.cs` | Sails due west into the deep and decomposes every retarding force per second — sail budget vs surf, base hull drag and plow — plus the metric the player actually feels: **what fraction of her speed a wave costs and how long she needs to get it back**. The instrument for "she goes from 30 m/s to 5". | `/tmp/seasick-westtrace.txt` |
| `SetMaxSpeed.cs` | One-off push of `ShipMotor.maxSpeed` (scene-serialised) and nothing else. Edit the constant, run it. | — |
| `SetupPaddleBoat.cs` | **The paddle boat cutover on `Sea.unity`**: strips the sloop's hull/sail/rudder, drops the new boat in at the measured waterline offset, corrects the FBX grandchild axes, binds the wheels by measured position, hangs the lanterns on pivots, turns `windDriven` off, reshapes the buoyancy probes and mass for this hull, rescales speed/freeboard/camera, moves cargo amidships and stands a helmsman at the wheel. Idempotent — re-run after changing any constant in it. |
| `SetPaddleBoatImport.cs` | Import scale for `paddle_boat.fbx` (1.7 = 12.1 m overall). |
| `SetBeachSlope.cs` | One-off push of `WorldSettings.beachMaxSlope` into the asset. Edit the constant, run it. The value comes off `BeachProbe`'s distribution. | — |
| `TuneStormFeel.cs` | Storm-feel values that are **scene-serialised** and therefore unreachable from C# defaults: `ChaseCamera.stormDrop`/`stormPullIn` and `ShipMotor.acceleration`. Note the scene frames the camera at distance 20 / height 13, not the code defaults 25/19 — subtract the storm values from those, not from the defaults. |

## Probes

| Probe | Measures | Output |
| --- | --- | --- |
| `FFTUnit.cs` | The Stockham IFFT core against analytic sinusoids (known modes in, sinusoid out to 1e-5). Edit mode, no play needed. | `/tmp/seasick-fftunit.txt` |
| `SpectrumProbe2.cs` | The GPU sea against **oceanography**: Hs from field variance and Tp from a temporal PSD vs analytic JONSWAP integrals, for the three canonical (wind, fetch) triples. Edit mode. Slow (~2 min). | `/tmp/seasick-spectrum2.txt` |
| `ClipmapProbe.cs` | Vertex swim (re-anchors every ring under a frozen sea; pixels must not move) and altitude tiling shots. Play mode, OceanLab. | `/tmp/seasick-clipmap.txt`, `-tiling-*.png` |
| `DivergenceProbe.cs` | **The load-bearing gate**: rendered surface vs CPU sampler at 1000 points, five frozen instants, storm λ=1.2 — must be < 5 cm (measured 0.23). Also batch cost. Play mode, OceanLab. | `/tmp/seasick-divergence.txt` |
| `BuoyProbe.cs` | Proxy sloop: 60 s storm free-float (roll/rails/draft/capsize) + three calm 2 m drops from pinned phases, scored on draft **overshoot** and **late ringing RMS**. The old "settle time" criterion was replaced 2026-08-21 — see the traps below. Play mode, OceanLab. | `/tmp/seasick-buoy.txt` |
| `BlendProbe.cs` | Calm→storm weather ramp smoothness (Hs every second; steps mean rebuild pops). Play mode, OceanLab. | `/tmp/seasick-blend.txt`, `-blend-*.png` |
| `SailShot.cs` | Sails the western deep in `Sea.unity`: draft statistics, camera clamp rate, camera/hull gap, roll/pitch, speed. Compare `/tmp/seasick-sail-baseline.txt` (the old kinematic system's final run). | `/tmp/seasick-sail.txt`, `-0..4.png` |
| `BuryProbe.cs` | **Deck-burial gate**: sails hard into head seas at forced severity 1.0, wave phase pinned (`OceanTime.Scrub(500)`), 60 s. Deck must stay dry: poopDeckUnder 0%, deckOverMax < 0. Play mode, Sea.unity. | `/tmp/seasick-bury.txt` |
| `StallProbe.cs` | **Sailing-speed gate**: head seas at severity 0.40 and 0.75, plow drag toggled ON/OFF over the same water. Reports mean way vs target, distance made good, stalls/min, recovery time, and peak plow against the sail's authority. Also a calm sails-furled leg that checks plow really is silent at rest. Play mode, Sea.unity. | `/tmp/seasick-stall.txt` |
| `BuryTrace.cs` | BuryProbe's run as a time series instead of a verdict — ship y, sampled surface, batched surface, draft, submersion, reserve, plow, speed, pitch, roll. The sampler-vs-batch column is the one that says whether a wild draft number is a sinking ship or a lying instrument. Play mode, Sea.unity. | `/tmp/seasick-burytrace.txt` |
| `RippleStressProbe.cs` | **Ripple-needle gate**: two legs (driving + splash spam, and stalled in a storm), GPU readback scored on **neighbour gradient** and texels riding the clamp — not magnitude, which the Step clamp makes unfalsifiable. Gate: gradient < 0.35 m/texel, zero at clamp, zero non-finite. Play mode, Sea.unity. | `/tmp/seasick-ripplestress.txt` |
| `SprayDebug.cs` | Per-second spindrift emission budget log. | Unity log |
| `NoiseProbe.cs` | **Terrain noise gate** (edit mode): deterministic, seed-sensitive, no mirroring across the origin, fBm range/mean at 1/3/8 octaves, continuity at 80 km. Exports the map quad at 1 and N octaves. | `/tmp/seasick-noise.txt`, `-noise-*.png` |
| `HeightProbe.cs` | **Height pipeline gate** (edit mode): land ratio vs `landRatio`, open ocean always on a seabed below 0, beach band walkable (≤1.5 m/m), blend adds no discontinuities, `worldRadius` clamp drowns everything outside. Exports all four visualiser stages. | `/tmp/seasick-height.txt`, `-height-*.png` |
| `ChunkProbe.cs` | **Chunk mesh gate** (edit mode): vertex heights equal the height function exactly, normals unit, +X/+Z shared edges bit-identical in position AND normal, LOD-2 vertices a subset of LOD-1. Renders the lab camera to PNG without play mode. | `/tmp/seasick-chunk.txt`, `-chunk.png` |
| `StreamProbe.cs` | **Streaming gate** (play mode, TerrainLab): 2 km sail at 12 m/s, then coverage, unload band, collider ring, LOD assignment, pool bound, seams over every loaded same-LOD pair, and steady-state main-thread cost (< 10 ms worst). ~3 min. | `/tmp/seasick-stream.txt`, `-stream.png` |
| `ShoreProbe.cs` | **Terrain→ocean gate** (play mode, Sea.unity): severity forced to 1.0; surface RMS at deep water / shoreline / land must be intact / <10 % / 0; CPU shore factor vs exact height. Shot of the beach. | `/tmp/seasick-shore.txt`, `-shore.png` |
| `WorldProbe.cs` | **Populator gate** (play mode, Sea.unity): islands found, home + Stockpile, centres on land, outline at the waterline, beaches, props grounded, reefs/monsters in water, raiders, spawn landable. | `/tmp/seasick-world.txt` |
| `IslandShot.cs` | Look shots of the home island from the sea, the beach, overhead and 900 m east, with the ship pinned so the streamer stays centred. Play mode, Sea.unity. | `/tmp/seasick-island-0..3.png` |
| `CalmWaterShot.cs` | **Chop-floor gate**: forces the gentlest weather the game produces and measures surface RMS inshore against deep water. Sheltered water must still move (RMS > 2 cm) while staying a real shelter. | `/tmp/seasick-calm.txt`, `-calm-inshore.png` |
| `ResetOceanTime.cs` | Undoes what the last probe pinned — `OceanTime.Paused`, `OceanTime.Scale`, a forced sea state. All static, all survive leaving play mode. Run between probes. | — |
| `StormDeckShot.cs` | **Hull-water-clip A/B**: freezes her kinematic at a pinned wave phase with the sea held 0.50 m over the deck, then shoots the same frame with the clip on and off. The rig exists because the first version *waited for a natural crest* and fired when the water was 0.47 m BELOW the deck — an A/B on a condition that never happened. | `/tmp/seasick-deck-clip-on.png`, `-off.png` |
| `InspectBoatParts.cs` | What the boat's parts are doing in the scene: local rotation, **bounds size** and ship-local centre. Bounds size is what tells a lantern hanging down from one lying sideways — a position dump cannot see a rotation fault. | `/tmp/seasick-boatparts.txt` |
| `PaddleProbe.cs` | **The paddle boat's gate**: resting draft and freeboard light and laden in a pinned calm; that she makes way and the wheel rim speed matches it; that the wheels are genuinely differential at helm; and that with the throttle shut and the helm over they counter-rotate and she comes round on the spot. Play mode, Sea.unity. | `/tmp/seasick-paddle.txt` |
| `InspectPaddleBoat.cs` | Dumps what Unity actually made of an imported FBX — hierarchy, local transforms, per-part world bounds, and which way the bow points. `Execute` for the boat, `ExecuteCannon` for the gun. Edit mode. | `/tmp/seasick-paddleboat.txt` |
| `BeachProbe.cs` | **Beach-slope tuning instrument**: re-measures the shore rise per metre over 12 m inland on all 46 bearings of every discovered island, and reports the landable fraction and the count of unlandable islands at five candidate `beachMaxSlope` values, plus the full percentile distribution. Choose the threshold by reading it once instead of rebuilding the world per candidate. Play mode, Sea.unity. | `/tmp/seasick-beach.txt` |
| `TerrainPerfProbe.cs` | **Terrain cost ledger**: loaded vs portrait-frustum-visible chunks/verts/tris, shadow-pass load, collider tris, mesh memory, and the streamer's main-thread cost over 24 chunk crossings with the per-crossing distribution. Only device-independent quantities — GPU ms stays a device measurement, like `PerfProbe`. Play mode, Sea.unity. | `/tmp/seasick-terrainperf.txt` |
| `CrossingProbe.cs` | 40 chunk crossings in 20 s with per-crossing replan breakdown — the quick way to tell a one-off first-use cost from a systemic one (it's what found the 10 ms first-release lazy init). Play mode, TerrainLab. | `/tmp/seasick-crossing.txt` |
| `LoadProbe.cs`, `HudShot.cs`, `FogTest.cs`, `WarpOut.cs` | Older, still valid. | `/tmp/seasick-*.txt` |

(The old-ocean probes — SpectrumProbe, HeaveProbe, SeatingProbe, RegionProbe,
MountainProbe, HeadingProbe, StormMeasure/Shot/Picture, MatCheck, VoidTest,
ApplyWaveShape, AddMountainSeas — died with the Gerstner stack.)

`tools/pngprobe.py` prints mean sRGB for horizontal bands of a screenshot:
`python3 tools/pngprobe.py /tmp/seasick-sail-2.png`. Pure stdlib.

## Measurement traps this project has actually hit

- **`check_compile_errors` does not see Burst errors.** A method-level
  `[BurstCompile]` on a struct-returning static made Burst treat it as an entry
  point (BC1064) and silently dropped every job that called it to Mono — for
  two steps of work. Grep the console for "Burst error" after touching job
  code; the class-level attribute is all a job needs for inlining.
- **`enabled = false` does not stop `Awake()`.** Disabling the old
  `ArchipelagoGenerator` component left the whole legacy archipelago alive
  (minimap dots, hull damage, intersecting meshes) until the GameObject itself
  was deactivated. Only Start/Update are gated by `enabled`.
- **Coplay's script compiler dies on any diagnostic, including XML-doc
  warnings.** A `<stage>` or `<octaves>` inside a `///` comment produces the
  opaque "CSharpResources.resources" failure. No angle brackets in probe doc
  comments; no local functions; no `$"{x:F2}"` interpolation — `BuoyProbe.cs`
  style only.
- **A loaded asset keeps its old in-memory values when you change a C# default.**
  `jobsInFlight` stayed 4 through two "is it 2 now?" runs. Changing a field
  initialiser on a ScriptableObject only affects assets that don't already
  have the field — push the value from a script or edit the asset.
- **A one-off first-use cost looks like a systemic spike if you only sample the
  worst frame.** The streamer's first release cost ~10 ms (lazy init in the
  pool/collider path) and landed on the first border crossing every run;
  sub-phase timers blamed whatever ran there. `CrossingProbe` (many crossings,
  per-crossing numbers) separated "first only" from "every time" in 20 s.
  Warm such paths on the load frame.
- **The editor steals the main thread when job workers saturate the cores.**
  With 4 height jobs + collider bakes in flight on an 8-core Mac, spikes of
  10–20 ms attach to trivial code. Keep `jobsInFlight` small (2) and measure
  steady state, not warm-up. Concretely, 2026-08-25: one TerrainPerfProbe run
  charged **9.28 ms to a single `Mesh.ApplyAndDisposeWritableMeshData`**, which
  would have been a real 60 fps blocker; the identical run minutes later put
  the same call at **1.07 ms**. Never accept a single worst-frame sample from
  the editor as a finding — run it twice and compare the distributions.
- **`DivergenceProbe` is currently RED, and was red before the paddle boat.**
  Measured 2026-08-25: **26.32 cm max / 15.40 cm mean** on a tree with the
  ocean changes stashed, against **26.43 / 13.07** with them — statistically
  the same, so the chop floor is not the cause. The documented baseline was
  **0.23 cm**, so something between then and now broke it and it is not the
  boat. Symptoms point at readback lag rather than a formula mismatch: the
  first timestamp of every run reports "readback never caught up", and repeat
  runs of identical code scatter (26 cm, then zero samples, then 147 cm) where
  a real formula divergence would be consistent. **Bisect before trusting any
  ocean parity result, and do not read a single run as a finding.**
- **Unity refuses to reparent a child out of a prefab instance, and
  `SetParent` fails SILENTLY.** Both lantern pivots spent a whole build
  rotating empty GameObjects while the lanterns sat unmoved under `Details`,
  looking rigid. `PrefabUtility.UnpackPrefabInstance` first if a setup script
  is going to restructure an imported model.
- **`Quaternion.FromToRotation` leaves the roll arbitrary.** Aiming a
  lantern's long axis down says nothing about how it is rolled about that
  axis, which is why the chain ring came out flat and sticking sideways out
  of its bracket. Constrain two axes, not one (`SetupPaddleBoat.Aim`).
- **Counter-scaling a parent to keep a child's size also scales the child's
  POSITION.** Shrinking `HelmStand` to keep the wheel human-sized moved the
  wheel from z -6.36 to -3.18 and dragged the helmsman with it. Scale the
  thing itself, never its parent — and remember a mesh offset from its own
  origin walks across the deck when scaled about that origin.
- **A single deck height is a lie on any real hull.** The paddle boat's deck
  has camber and sheer and spans 0.67 m; crew, guns and cargo were all placed
  at its maximum and floated by up to half a metre. Sample the deck mesh under
  each thing's own feet. The same applies sideways — the deck narrows toward
  the bow, so a fixed inboard offset put the fore guns out through the railing.
- **Blender's FBX export converts axes for top-level objects but not for
  GRANDCHILDREN.** With `bake_space_transform=True`, `PaddleWheel` (a direct
  child) came out correctly at `(-Xb, Zb, -Yb)`, while `HelmWheel` and both
  lanterns (children of a child) came out at `(-Xb, Yb, Zb)` — Y and Z never
  swapped. Visually this is not an obvious rotation, it is a part sitting at
  deck level and offset sideways: the helm wheel was 0.84 m out to starboard
  instead of 0.84 m up on its stand. `SetupPaddleBoat` corrects any part at
  depth 2 or more. **Dump the hierarchy and read the numbers after any FBX
  import** — `InspectPaddleBoat` exists for exactly this, and it caught both
  this and a separate 1/100 scale error on the first import.
- **Ship tuning authored as ABSOLUTE METRES does not survive a change of
  hull.** Swapping the 21 m sloop for a 12 m paddle boat carried over
  `sinkAtMarkedLine` 0.50 / `sinkPerOverload` 0.72 (authored against ~2 m of
  freeboard) and `maxSpeed` 18 — which is 35 knots. The first run measured her
  at 20.4 m/s with her deck buried in her own bow wave. Anything in metres or
  m/s on the ship — freeboard sinks, camera distance and height, camera storm
  offsets, gun positions, crew stations, cargo stack origin — has to be
  rescaled with the hull, and every one of them is scene-serialised.
- **`QualitySettings.names` is not indexed by quality level.** TerrainPerfProbe
  printed `names[GetQualityLevel()]` and reported "PC" while play mode was
  genuinely on the Mobile tier (level 0, shadowDistance 40, 2 cascades — the
  Mobile row's values, not the PC row's). Identify the tier by a setting that
  actually differs between the tiers, never by the name lookup.

- **Pin the sea state.** `SeaState01` drifts 0.14–1.15 over minutes and scales
  every amplitude. The same build gave 23.5m and 8.4m of storm heave.
- **Pin the wave phase too, for anything visual.** Tint comparisons are
  worthless while the waves move: one far-water patch read 50.8%, 31.5%, 24.5%
  and 28.1% grey across four frames of the same pinned-ship test. **`VoidTest`
  needs this before it can settle the open brightness bug.**
- **Beware circular verification.** Checking whether the camera went underwater
  by comparing it against the same `SampleHeight` the clamp uses proves nothing.
- **Normalise against what changed.** A camera A/B was invalid until expressed
  per metre of heave, because the sea grew 3× between the two windows.
- **Drive the component, never the state it owns**, and wait a frame.
- **The editor Game view is landscape; the target is portrait.** Cramped HUD in
  a screenshot is often an aspect artifact.
- Unfocused editor runs at ~10 fps — not a valid perf signal, and it changes
  frame-rate-dependent behaviour.
- **Probes must pin the weather now, not `SeaState01`.** Use
  `SeaStateController.ForceSeverity()` (and release it), and pin wave phase
  with `OceanTime.Scrub`/`Paused` — the ocean is a pure function of
  (settings, seed, OceanTime), so scrubbing is exact.
- **The serialization trap ate another day, twice, during the rewrite.** A
  `BuoyantBody` drag default changed in code did nothing because the scene had
  the old value; the probe read a 4.07× force discrepancy that exactly matched
  the stale/new ratio. When a physics change appears to do nothing, or a
  measured coefficient is a suspicious clean multiple of your model, re-run
  `SetupOceanScene` (or `SceneDefaults.ResetToCodeDefaults`) before debugging
  the physics.
- **The Coplay script compiler crashes formatting its own diagnostics** on some
  probe files ("Could not find any resources appropriate for the specified
  culture…") and cannot tell you the real error. If a probe fails with the
  culture-resource stack: the file has a genuine compile error against
  Coplay's Roslyn even though Unity compiles it. Rewrite the probe in plain,
  boring C# (string.Format, no fancy syntax) — `BuoyProbe.cs` is the template.
  Do NOT try to run editor-assembly probes via a launcher: MonoBehaviours in an
  Editor folder cannot be AddComponent'ed; Coplay's fresh-assembly compile is
  precisely what makes probe MonoBehaviours legal at runtime.
- **Play mode runs the PHONE tier.** The active build target is iOS, and
  entering play mode applies its per-platform default quality (level 0 =
  Mobile, so the ocean runs N=128, 5 rings). Edit-mode probes (FFTUnit,
  SpectrumProbe2) use the editor's current level instead (PC, N=256). This is
  a feature — gameplay always rehearses the phone — but know which tier the
  probe you just ran actually measured.
- **"Past full probe submersion" is not "deeply buried".** A keel-line
  buoyancy probe is ~96% submerged just floating: on the sloop the stem sits
  **4 cm** from its own full-submersion depth at the float equilibrium
  (`radius*0.5` = 0.55 m against a resting depth of 0.508 m), load-invariant,
  because `SeatOffset` moves the waterline too. Any term keyed on that
  threshold fires in flat water — reserve was measured active on 38% of steps
  at anchor in a calm. Solve the static float before choosing an onset.
- **A velocity threshold on a live sea measures the sea.** BuoyProbe scored
  the calm drop as "|vy| < 0.12 m/s for 0.5 s"; the calm sea's own orbital
  motion is that same order, so one unchanged build returned 1.04 s, 2.49 s,
  5.93 s and never-settled — and a FAIL from it sent this session hunting a
  regression that did not exist. Score transients on a quantity that
  subtracts the water (draft = surface − hull), pin the phase, repeat the
  drop, and gate on an average (RMS) rather than a threshold crossing.
  Overshoot repeated to ±0.02 m where settle time ranged over 8 s.
- **Run acceptance probes in a FRESH play session.** BuryProbe run straight
  after StallProbe inherited a ship 8 km away in a forced sea state and
  reported nonsense. One probe per session, or reset everything the previous
  one touched.
- **Two probes disagreeing is data.** When BuryProbe said 44 m of draft and
  BuryTrace said 1.8 m under the same setup, the difference was BuryProbe's
  extra 8 s settle — it was measuring 8 s further into the run. Diff the
  setups before believing either.
- **A clamp in the code can make a gate unfalsifiable.** RippleStressProbe
  asserted `max|offset| < 3 m` while the Step kernel clamped every texel to
  ±2 m: the assertion could not fail, and it reported PASS through a bug the
  player could see. When writing a gate, check that the thing being asserted
  is not already guaranteed by a clamp, saturate or Clamp01 upstream — and
  measure the shape of the artifact (here a gradient: a needle is steepness,
  not height), not just its size.
- **Read-modify-write across compute threads is the default hazard here.**
  Two ripple-sim bugs in two sessions were both this: the leapfrog Step
  updating `Curr` in place, then `Inject` running one thread per IMPULSE doing
  read-modify-write over its own footprint, so overlapping impulses raced and
  a texel could silently lose a whole impulse its neighbour got — a
  single-texel cliff. Dispatch one thread per **destination texel** and loop
  the sources; then every texel has exactly one writer.
- **Explicit schemes need real CFL margin, not a clamp at the limit.** The
  ripple sim ran `(c dt/dx)^2` clamped at 0.45 against a hard 2D limit of 0.5,
  and both quality tiers sat there every frame. Sub-step to hold the Courant
  number at 0.5 instead — it preserves the authored wave speed where lowering
  the speed or the clamp would not.
- **Severity 1.0 runs are chaotic and stop being an A/B.** At mountainous sea
  states StallProbe returned the same number with the variable ON and OFF
  (2.9 m/s, 66 m both) in one run and 2.9 vs 10.4 in the next. Before tuning
  against a high-severity number, check the OFF leg still differs from the ON
  leg — if it does not, the run is measuring chaos, not the change.
- **Saving the scene freezes EVERY field on a component, including ones you
  just added.** Three tuning runs were wasted to this: `Sea.unity` held
  `maxPlowDecel 6` / `dynamicLiftCoeff 0.04` while the source said 2.5 / 0.12,
  because an unrelated `SaveScene` had snapshotted the new fields at their
  first values. The differences "measured" between those runs were noise. Any
  session that adds a `[SerializeField]` must push it with
  `SceneDefaults.ResetToCodeDefaults` (TuneStormFeel now does) before
  believing a single measurement.
- **`OceanSampler.SampleImmediate` is for one-shot, low-rate queries** (camera
  clamp, splash tests, random scatter like StormSpray). Anything continuous —
  floaters, enemy hulls, hull probes — belongs in `OceanProbeRegistry` or a
  `BuoyancyProbeSet`, which the physics driver folds into ONE batched Burst
  query per FixedUpdate.
