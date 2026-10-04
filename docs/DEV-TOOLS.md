# Dev tools

Everything here lives in `Assets/_Project/Scripts/Dev/Editor/` and is driven
through the **Unity CLI** (`unity cmd ...`, see CLAUDE.md) against the open
editor. Until 2026-09-21 it was driven through the Coplay MCP bridge with
`execute_script`; the notes below that mention Coplay describe that era and its
traps (fresh-assembly compile, focus requirement). The CLI's `eval` runs
against the project's already-compiled assemblies, so the fresh-assembly trap
is gone, and `RunProbe` launchers are called directly.

## The loop

Shell setup once: `export PATH="$HOME/.unity/bin:$PATH" UNITY_NO_BANNER=1 UNITY_NON_INTERACTIVE=1 UNITY_NO_PAGER=1`

1. Edit C# / shaders with normal file tools.
2. `unity cmd recompile` then poll `unity cmd recompile_status --json` — Unity
   does **not** pick up external edits without an explicit refresh.
3. `unity cmd console_status --json` — its compile-failure flag is **C# only**.
   A broken shader reports clean and then renders magenta; the real message is
   in `~/Library/Logs/Unity/Editor.log`, `grep "Shader error in"`.
4. `unity cmd eval --json --code 'SetupX.Apply();'` (or `eval_file --file`) to
   push values into the scene.
5. `unity cmd editor_play` → `unity cmd eval --json --code 'RunProbe.Surf();'`
   → read the probe's output file with Bash → `unity cmd editor_stop`.
   `unity cmd console --json level=error tail=20` reads the console;
   `capture_game_view` / `capture_scene_view` give a PNG for visual checks.

**Never edit anything under `Assets/` while a probe coroutine is running** —
Unity auto-refreshes, the domain reloads, and the coroutine dies silently while
the launching command still reports success.

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
| `WallL1Import.cs` (menu SeaSick/Art/Import level 1 wall) | **The level 1 wall (2026-09-28, `art-staging/wall-textured-v3`)**: importer settings for `Art/WallL1/Models` + `Textures`, the four `SS_WallL1_*` materials on `SeaSick/Environment Toon Textured` (remapped BY NAME), and the seven `Resources/Palisade/Palisade_*` wrappers rebuilt in place (paths + GUIDs kept; frame measured from the FBX markers; +Z along, rails -X, post centred on `__Post_Center`). Idempotent -- re-run after re-exporting the FBXs. The kit lives outside `Art/AstraPlaytest` so `AstraPlaytestImport` never remaps it. Gates are not touched. |
| `SetupAndroidGraphics.cs` | Locks Android to Vulkan only (the FFT is compute; GLES3.0 has none). |
| `SetupSeaTerrain.cs` | **The island cutover on `Sea.unity`**: deletes the legacy World object, turns `Island_Home` into the bare `HomePoint`, adds the `Terrain` object (`TerrainStreamer` + `TerrainShoreField` + `TerrainWorldPopulator`) on the ship, creates `WorldSettings`, and picks `TerrainSettings.worldOffset` so real land sits under the old home position with water at the spawn. Re-run after changing terrain defaults. |
| `SetupTerrainLab.cs` | Builds `TerrainLab.unity` (the island generator's test scene): the `TerrainSettings` asset, the 2D map visualiser quad (doubles as a water stand-in at y=0), a 7×7 main-thread `TerrainChunkPreview` over the western island, the runtime `TerrainStreamer`, sun and camera. Also pushes the terrain material. |
| `WestTrace.cs` | Sails due west into the deep and decomposes every retarding force per second — sail budget vs surf, base hull drag and plow — plus the metric the player actually feels: **what fraction of her speed a wave costs and how long she needs to get it back**. The instrument for "she goes from 30 m/s to 5". | `/tmp/seasick-westtrace.txt` |
| `SetMaxSpeed.cs` | One-off push of `ShipMotor.maxSpeed` (scene-serialised) and nothing else. Edit the constant, run it. | — |
| `SetupPaddleBoat.cs` | **The paddle boat cutover on `Sea.unity`**: strips the sloop's hull/sail/rudder, drops the new boat in at the measured waterline offset, corrects the FBX grandchild axes, binds the wheels by measured position, hangs the lanterns on pivots, turns `windDriven` off, reshapes the buoyancy probes and mass for this hull, rescales speed/freeboard/camera, moves cargo amidships and stands a helmsman at the wheel. Idempotent — re-run after changing any constant in it. |
| `SetPaddleBoatImport.cs` | Import scale for `paddle_boat.fbx` (1.7 = 12.1 m overall). |
| `SetBeachSlope.cs` | One-off push of `WorldSettings.beachMaxSlope` into the asset. Edit the constant, run it. The value comes off `BeachProbe`'s distribution. | — |
| `TuneStormSea.cs` | The storm-sea values that are **scene-serialised**: `RegionField.farScale` (1.5 -> 1.0, because a post-hoc amplitude gain is the wrong instrument once the west has a storm spectrum of its own -- multiplying an authored 45 m sea by 1.5 asks for 67 m, which needs 150 m of water before the depth limit allows it) and `breakFraction`, written explicitly rather than left to a C# initialiser. Saves the scene. |
| `TuneLivingSea.cs` | **The living-sea weather values, all scene-serialised**: wires `SeaState_Rough` into `SeaStateController`, pushes the Hs-space knobs (`shelfCalmHs`/`shelfLivelyHs`/`setDepth`/`setPeriod`/`wanderPeriod`/`skyHsStart`/`skyHsFull`/`squallHs`/`stormSteady`/`cellShape`), widens `RegionField.stormNear`/`stormFar` to 500/2800, adds `WeatherField` if absent and pushes every one of its values too. Reads all of it back off a fresh `SerializedObject` and prints the severity→Hs ladder. Re-run after changing any weather default — this bit twice in one session: `tileMetres` and `driftSpeed` stayed at the values the component was added with through two rounds of retuning, and only the octave periods (a local inside `Bake()`) actually moved. |
| `TuneSurf.cs` | The surf term's three material values (`_SurfStrength` 0.95, `_SurfBreakFrac` 0.78, `_SurfSwashDepth` 3), pushed onto `OceanSurface.mat` and read back off the asset. New shader properties are safe on the day they are added — absent from the .mat, so the material takes the shader default — and stop being safe the moment anyone touches the material in the inspector or the shader's defaults move. `_SurfBreakFrac` is a FRACTION of `RegionField.breakFraction`, not an absolute: the ratio of local wave height to depth can only ever reach `breakFraction` (that is what the depth cap enforces), so the knob means "how nearly the cap has to be binding before there is white water". |
| `TuneWaterline.cs` | The two scene-serialised values behind "she floats above the water and the wheels spin like a food processor": re-pushes the buoyancy probe rig via `SetupPaddleBoat.PushProbes` (so the `ProbeLift` constant reaches the scene) and `PaddleDrive.maxVisualRate`. Targeted rather than a full `SetupPaddleBoat` re-run, which would also push `maxSpeed`, the camera and the freeboard back to that file's constants. |
| `TuneStormFeel.cs` | Storm-feel values that are **scene-serialised** and therefore unreachable from C# defaults: `ChaseCamera.stormDrop`/`stormPullIn` and `ShipMotor.acceleration`. Note the scene frames the camera at distance 20 / height 13, not the code defaults 25/19 — subtract the storm values from those, not from the defaults. |

## Probes

| Probe | Measures | Output |
| --- | --- | --- |
| `RecipeGraph.Validate()` | **The guarantee**: walks every resource, recipe, fire level, building upgrade and ship rung and refuses them all if anything cannot be reached from a fresh camp with only the ground to gather from. Run via `unity cmd eval --json --code 'return SeaSick.World.Economy.RecipeGraph.Validate().ToString();'` from the shell; also logs on load in dev/editor builds. Call `RecipeGraph.WriteMarkdown("docs/PRODUCTION-CHAINS.md")` to regenerate the chain documentation (also called automatically on the same load). Errors are **hard**: a cycle, an item used as an input, a raw thing with a recipe, a fire level or upgrade that asks for something unreachable before it, or an id that does not exist. Warnings are **soft**: a fire level that unlocks nothing new. | console; `docs/PRODUCTION-CHAINS.md` (generated) |
| `StationStockSelfTest.Run()` | **Station-stock + trip-timing gate** (`SeaSick.World`, plain C#, edit mode, no play needed): storage-hub arithmetic across `OutpostLedger.Stations.cs`, `OutpostLedger.cs` and `StationStock.cs` — ~68 gates in sections (stations: orders/D2/conservation/capacities/repeat-stop/demolish/spendable-vs-bay/store-ceiling; build sites: whole-armful stocking, true counts, 0% until stocked+cleared, D2 at several tick sizes, old-save surplus; trip timing incl. twice-as-far-twice-as-long; gather trips: store↔source, D2, ceiling, full-store helps/hauls; clearing a plot in seconds); see "station stock: bays, benches, racks, orders, armful hauling" below. `RunProbe.Ledger()`'s own gates were re-baselined the same session for trip-timed gathering. Run via `unity cmd eval --json --code 'return SeaSick.World.StationStockSelfTest.Run();'`. | console |
| `GunCrewShift.SelfTest()` | **Gunners-walk-to-the-engaged-side rules** (`SeaSick.Ship`, plain C#, no play needed, 2026-10-03): 12 cases of `GunCrewShift.Assign` -- full crew moves nobody, short crew crosses fore-to-fore, fight over -> home guns, both sides (locked side first, spares to the other), overboard cover, rescued hand never bumps the cover, one-sided ship, stability, destroyed gun, coaster 4-a-side. Covers the plan only; the walk, the 3 s hysteresis and manning-on-arrival live in `CannonBattery.TickCrewShift` / `CrewAgent.RelocateStation` and need play mode. Run via `unity cmd eval --json --code 'return SeaSick.Ship.GunCrewShift.SelfTest();'`. | eval result |
| `PierVisual.SelfCheck(pierRoot)` | **Astra's pier kit v4 laid out right** (`SeaSick.World`): module/cap/shaft counts, walking surface at both ends vs the root's deck, first/last snap vs `Pier.LandEnd`/`SeaEnd` (sea end off by the whole-metre rounding only), ramp Toe vs the kit's equations, pad on the Toe, every shaft's foot at ground-0.3 (clamped `PostDeepest`), caps on shafts, lantern on `Light_Source`; ends PASS/FAIL, or says "box pier fallback" plus `PierVisual.LastFailure`. Needs `Resources/Pier/<Part>.prefab` for all ten parts. Raise one with `BuildingFactory.Raise(BuildPlans.Pier.WithLength(15), null, new Vector3(x, BuildPlans.PierDeck, z), Quaternion.Euler(0, yaw, 0), 0)` in play mode, or pass a standing pier's transform. `PierVisual.Describe(n)` prints the bay layout; `PierVisual.ResetKit()` after re-importing without a domain reload. | eval return string |
| `FFTUnit.cs` | The Stockham IFFT core against analytic sinusoids (known modes in, sinusoid out to 1e-5). Edit mode, no play needed. | `/tmp/seasick-fftunit.txt` |
| `SpectrumProbe2.cs` | The GPU sea against **oceanography**: Hs from field variance and Tp from a temporal PSD vs analytic JONSWAP integrals, for the three canonical (wind, fetch) triples. Edit mode. Slow (~2 min). | `/tmp/seasick-spectrum2.txt` |
| `CascadeFadeProbe.cs` (`Scripts/Dev/`) | **How the sea thins with distance.** Reads per-cascade RMS straight off the displacement textures, then walks outward a metre at a time asking the SHIPPED rule what a vertex there is weighted, and reports surface RMS against distance plus **the biggest single-metre fall in it** — a step function has one, a ramp has none. Backed by a photograph: one sea-level frame with the phase pinned, scored for high-frequency contrast row by row, every row a known ground distance. Never restates the rule: it calls `OceanClipmap.WeightsAt` if that exists and otherwise reads each ring's `_Ocean_CascadeWeights` off the live MaterialPropertyBlock, so one file measures the old scheme and the new one. `Execute` = PC tier, `ExecuteMobile` = phone; **run them one at a time**, two instances at once fight over the clipmap. Play mode, OceanLab. | `/tmp/seasick-cascadefade-<tier>.txt`, `-<tier>.png` |
| `ClipmapProbe.cs` | Vertex swim (re-anchors every ring under a frozen sea; pixels must not move) and altitude tiling shots. Play mode, OceanLab. | `/tmp/seasick-clipmap.txt`, `-tiling-*.png` |
| `DivergenceProbe.cs` | **The load-bearing gate**: rendered surface vs CPU sampler at 1000 points, five frozen instants, storm λ=1.2 — must be < 5 cm (measured 0.23). Reports a **three-way split** — `env` (RegionField C# vs HLSL), `disp` (readback vs live texture) and `total` (adds the Newton inversion) — because one number could be any of the three and was read as the wrong one for a fortnight. Disables `SeaStateController` for the run; `ForceSeverity` does NOT stop it reaching `SetSettings`. Fails loudly on a readback stall instead of skipping the instant. Play mode, OceanLab. | `/tmp/seasick-divergence.txt` |
| `TwoAxisTrace.cs` (`Scripts/Dev/`) | **Is the sea more than one number?** Scrubs `OceanTime` over six hours and asks the SHIPPED rule (`SeaStateController.BlendAt` for the anchor sea, `ApplyAxes` for the two axes -- never a copy of either) what the sea is at each moment. The headline is the one number that matters: samples bucketed by Hs to a couple of per cent, and **the spread of wind-against-swell angle inside a bucket**, which with one degree of freedom is zero by construction. Also the correlation between the two axes (two axes that track each other are one axis in a hat), how much of the time the wind runs with / across / against the swell, that the axes move CHARACTER and not size (total swell variance at a fixed severity must not drift), and purity -- the same (place, time) twice must give the same sea or `Scrub` has stopped making probes repeatable. Play mode, Sea.unity. | `/tmp/seasick-twoaxis.txt` |
| `LivingSeaTrace.cs` | Measures the two things "the ocean feels alive" means, **both invisible to a screenshot**. (1) *Gradual over distance*: sweeps west and reports the **ratio of wave height gained per 100 m**, because the eye reads height as a ratio — a sea that doubles every 200 m is a wall however smooth its curve. (2) *Never static, never repeating*: scrubs `OceanTime` over half an hour at one spot and reports the spread, the drift rate and the closest the trace comes to repeating itself. Also the patch field's span, its slick events (count, spacing, duration, % of time) becalmed **and under way at 10 m/s**, and the storm cells across distance and across an hour. Reads the shipped rule through `SeaStateController.TargetHsAt`/`SetFactor` rather than re-deriving it. Play mode, Sea.unity. | `/tmp/seasick-livingsea.txt` |
| `BuoyProbe.cs` | Proxy sloop: 60 s storm free-float (roll/rails/draft/capsize) + three calm 2 m drops from pinned phases, scored on draft **overshoot** and **late ringing RMS**. The old "settle time" criterion was replaced 2026-08-21 — see the traps below. Play mode, OceanLab. | `/tmp/seasick-buoy.txt` |
| `BlendProbe.cs` | Calm→storm weather ramp smoothness (Hs every second; steps mean rebuild pops). Play mode, OceanLab. | `/tmp/seasick-blend.txt`, `-blend-*.png` |
| `SailShot.cs` | Sails the western deep in `Sea.unity`: draft statistics, camera clamp rate, camera/hull gap, roll/pitch, speed. Compare `/tmp/seasick-sail-baseline.txt` (the old kinematic system's final run). | `/tmp/seasick-sail.txt`, `-0..4.png` |
| `BuryProbe.cs` | **Deck-burial gate**: sails hard into head seas at forced severity 1.0, wave phase pinned (`OceanTime.Scrub(500)`), 60 s. Deck must stay dry: poopDeckUnder 0%, deckOverMax < 0. Play mode, Sea.unity. | `/tmp/seasick-bury.txt` |
| `WaterlineProbe.cs` | **Where she actually sits**, in metres against the landmarks that make "she floats on top of the water" checkable: designed waterline above water (the ship origin *is* the designed waterline), keel depth, deck height, the paddle-wheel axle, and the blades' bite depth against the drawn 1.32 m. Measured **at rest and at full throttle**, because the two answers want opposite fixes — a static offset is displacement, a speed-dependent one is dynamic lift. Sea pinned with `ForceHs`, averaged over several seconds, and it ends on a screenshot of the pinned calm because the complaint is visual. Play mode, Sea.unity. | `/tmp/seasick-waterline.txt`, `-.png` |
| `SteamerProbe.cs` (`Scripts/Dev/`, launched by `RunSteamerProbe`) | **The stern-wheel steamer's sea trials**, six modes, ONE PER PLAY SESSION: FLOAT (on her marks: origin against the sampled surface, trim, heel, wheel dip, and the book against the station tables), DECAY (roll and pitch kick), DRIVE (rest to full ahead, coast-down, full astern), TURN (from rest on the race, then the circle at speed), SWAY (local Hs 3.5 on three headings), STORM (the same at Hs 9). She is a RUNTIME conversion of PlayerShip, so the preference must be set BEFORE Play: `RunSteamerProbe.Select()` in edit mode. Contract and gates: `docs/steamer-spec.md`. Play mode, Sea.unity. | `/tmp/seasick-steamer-MODE.txt` |
| `SteamerShot.cs` (`Scripts/Dev/`, NOT `Dev/Editor/` — same trap as `ShaderStrip`, and `IslandShot` is still sitting in the Editor folder with it) | Five look shots of the steamer **in the game's own light, with her guns and hands where the game put them**. The generator's Blender renders are Workbench with a studio lamp and no sea; they say whether the mesh is right and nothing about whether she reads as a ship in this game, which is the question Kevin actually answers. Play mode, Sea.unity. | `/tmp/seasick-steamer-shot-*.png`, `-shot.txt` |
| `ShaderStrip.cs` (now `Scripts/Dev/`, NOT `Dev/Editor/` — a MonoBehaviour in an `Editor` folder cannot be `AddComponent`-ed and every sheet died on the null) | **Contact sheets of the water shader**, because it will not assemble in your head while the sea drifts and you cannot hold two states side by side. `states.png`: calm/rough/heavy/mountainous at the SAME pinned wave phase, seed, camera and sun — only the spectrum differs. `layers.png`: one sea with the shading stacked a term at a time (body, +subsurface, +sky/glitter, +foam) via the `_SS_LayerOff` dev global. Ship stays in frame on purpose — a 1.6 m sea and a 65 m sea look identical without something of known size in the picture. Rendered through the MAIN camera into a RenderTexture, which is also what drops the HUD. `DaySheet()` is the time-of-day sheet: night / dawn / sunrise / noon / sunset / dusk at one pinned sea, wave phase and camera, with only the hour changing. It drives `SkyDirector.pinTime` by reflection rather than aiming the light, because SkyDirector reasserts the light's rotation from TimeOfDay every LateUpdate — and it pins `TimeOfDay.Day` too, since the moon's bearing and phase both hang off the day number and a 180 s day rolls over mid-session. `SunAngles()` is a third sheet: sun elevation 8/16/28/50 degrees at one sea state, which is how the scene's 50-degree sun was caught never being in frame. Play mode, Sea.unity. | `/tmp/seasick-shader-states.png`, `-layers.png`, `-suns.png`, `-day.png` |
| `WeatherSheet.cs` (`Scripts/Dev/`) | **Contact sheets for the two things the 2026-08-28 realism pass changed about how the sea READS.** `Axes()` / `AxesLively()`: row A turns the wind sea 0/45/90/135/180/270 against a swell left exactly where the anchor put it; row B runs the SHIPPED `ApplyAxes` at +0/+30/+60/+90 s of game time **rendered at one pinned wave phase** (the axes take a time argument, the phase comes from `OceanTime`, so only the weather moves); row C is an orthographic camera straight overhead, once shipped and once with foam suppressed. `Patches()` / `PatchesLively()`: row A forces the patch field UNIFORM at its low end, at 1, and at its high end (`patchRangeLo == patchRangeHi`, so the field's own value stops mattering and no rebake is needed — the bias solve only sets a mean, and a constant field has no mean to get wrong); row B is the shipped field from straight above with **the field itself rendered underneath the water it made**, and every overhead pixel binned by the field value at its exact spot AND by its distance from the camera. **Two things make it a measurement rather than a picture:** a CONTROL tile that redraws the same sea (must come out at 0 — the first version scored 0.051 against a 0.084 signal because the hull was still settling and the wake sim and spray run off `Time.deltaTime`, not `OceanTime`), and a second control that advances only the WAVE PHASE by the same 30 s, which is the yardstick every weather difference has to beat. Scores each tile for mean luminance, HF grain, and structure-tensor orientation **with a coherence figure** — below ~0.05 the angle is the arctangent of two noise terms and means nothing. Play mode, Sea.unity. | `/tmp/seasick-weather-axes{,-lively}{,-topdown}.png/.txt`, `-patches{,-lively}.png/.txt` |
| `SurfProbe.cs` (`Scripts/Dev/`) | **How much whitewater is there, as a function of how deep the water is?** An ORTHOGRAPHIC camera looks straight down across a real shoreline with the `_SS_FoamOnly` dev global on, so every pixel IS the shader's own `foamAmt` and every pixel maps to a world position by arithmetic instead of by projection; pixels are then binned by the depth under them, read from the same shore grid the shader samples. The gate is the SHAPE of the curve, not its height — foam must RISE as the water shoals, and anything else is the `foamAmt * env` sign error still being there. Two sea states, because the surf zone's WIDTH should scale with the sea (a bigger sea breaks further out), so one state could not tell a depth-driven term from a band painted at the beach. Finds a real shoreline and warps the ship to it (**the shore grid follows the ship**) and asserts the grid covers the frame before believing a number. **It also culls to the ocean's own layer and clears to blue**: the first run read the beach and called it foam — in 0–1 m of water the displaced surface dips below the seabed, terrain shows through, terrain knows nothing about `_SS_FoamOnly`, and its sand came back as 0.09 mean / 0.85 peak in the one bin the gate turns on. Under `_SS_FoamOnly` water is exactly grey, so anything that is not grey is not water. **The before and the after come off the SAME FRAME**: `_SS_SurfOff` takes the surf term out of the shipped shader, so both columns are one sea at one wave phase rather than two builds an hour apart. And each sea state pins at a LATER instant than the last — pinning them all at one instant scrubs the clock backwards relative to the rebuild it just caused, `SeaStateController`'s throttle goes negative and returns early for the rest of the run, and the probe measures the first sea state twice while reporting it under the second one's name (it did exactly that; only "declared nominalHs 4" under a heading saying Hs 14 gave it away). It asserts the rebuild arrived and reports measured 4×RMS beside the declared height. Note the values are framebuffer, so sRGB-encoded — a monotone transform of `foamAmt`, which is all the SHAPE of the curve needs. Play mode, Sea.unity. | `/tmp/seasick-surf.txt`, `-surf-<hs>.png` (foam), `-nosurf.png`, `-shaded.png`, `-deck.png` |
| `CrestProbe.cs` (`Scripts/Dev/`, `RunProbe.Crest`) | **Does the open sea break, and are its tops sharp?** Two questions no existing probe can answer, for the same structural reason: `WaveSizeProbe`, `DivergenceProbe` and every other measurement of "the sea" reads the CPU sampler, and the sampler carries **cascades 0 and 1 only** (`DisplacementReadback.PhysicsCascades`). The sharpness the eye reads lives in cascade 2 and the foam is computed entirely on the GPU, so anything measured through the sampler is blind to both **by construction** — and would report "no change" after a change that is obvious on screen. So this measures the shipped ARTEFACTS: the cascade-2 displacement/derivative textures read back off the GPU (steepness, and the fraction of the band actually folding), and the shader's own `foamAmt` off the framebuffer with `_SS_FoamOnly` on and the camera culled to the ocean layer. The before/after comes off ONE FRAME via `_SS_FoamOldJ`, never from two runs — two runs of identical code have been measured 50.8 % and 24.5 % apart on the same water. **Its three-way split is the reason this pass got anywhere**: `_SS_FoamChannel` writes one of the foam's inputs to the screen in turn (j / breaking / env / weights / turb / fresh / residual / fade / dist), and `fade` reading a flat **0.00** is what exposed the ocean running the mobile tier under a dropdown that said PC. Also walks `foamThreshold` and watches the buffer's SPREAD — a wash has p95 == p50, a pattern does not — and reports spume crests/s against the gate. Asserts >150 m of water before believing any of it. Play mode, Sea.unity. | `/tmp/seasick-crest.txt`, `-crest-<hs>-foam-before/after/foldonly.png`, `-shaded.png`, `-flat.png`, `-J.png` |
| `SprayRigCheck.cs` (`Scripts/Dev/`, `RunProbe.SprayRig`) | **Is her spray thrown from her, or from inside her?** Walks four rungs of the ladder (skiff, sloop, brig, ship of the line — the fault is at BOTH ends and in opposite directions, so one rung proves nothing) and measures where `SpeedJuice`'s emitters actually sit in the hull frame after a real `Shipyard.Apply`. Reads the transforms, never re-runs the fractions that placed them: a check that re-applies the rule under test can only ever agree with it. Two gates — every sideways thrower at or beyond the inboard ellipse `HullWaterClip` keeps the sea out of (0.41 × beam, 0.40 × length), and nothing born below the waterline as she settles. Play mode, Sea.unity. | `/tmp/seasick-sprayrig.txt` |
| `HomeTabProbe.cs` (`Scripts/Dev/`, `RunProbe.HomeTab`) | **Does the Home tab get her home, or only move her?** Presses `AnchorController.BerthAtHome` from a kilometre out at 8 m/s with cargo aboard and then WATCHES: on the berth, lying still, along the pier, `AtHomeDock` true, the voyage closed on its own, and the cargo accounted for. Putting the hull on the berth is the easy half and the half a check stops at — `LandProbe` had to learn that after a geometry survey said every island was landable while you could not gather a log. **Its cargo gate measures where the timber ENDED UP, not where it was**: the first version gated on the hold still being full afterwards and failed a working button, because arriving at the berth completes the voyage and completing a voyage is what unloads her. No screenshot mode — the tab is IMGUI, `ScreenCapture` does not resolve from the runtime assembly here, and the RenderTexture route every other shot tool uses does not draw IMGUI at all. Play mode, Sea.unity. | `/tmp/seasick-hometab.txt` |
| `SetupHomeTab.cs` (`Scripts/Dev/Editor/`) | Puts `HomeTab` on whatever object already carries `StatusHUD`, so the permanent HUD stays one object rather than gaining a GameObject per control. Wiring through `SerializedObject`. Cannot run in play mode. | console |
| `SeaStateWiring.cs` (`Scripts/Dev/Editor/`) | **Which sea-state assets is the controller actually blending?** `SeaStateController` holds its anchors as serialized references and every tool reaches them through `Resources.Load`; nothing enforces that those are the same objects. Prints each anchor's asset path beside what Resources finds, whether they are the same object, their foam values, and the live blend's — so the three can be compared instead of assumed equal. Written when the live blend read foamThreshold 0.50 at both Hs 14 and Hs 55, which the anchors cannot produce; it proved the wiring innocent and the in-memory anchors corrupted. | console |
| `FoamMatCheck.cs` (`Scripts/Dev/Editor/`) | Reads the ocean material's foam values back **off the material at runtime**. A `.mat` snapshots a shader property's default when the property is created and a later change never reaches it — `_StormDeep` read a stale colour for a whole session that way. When a shading term measures as doing nothing, the first question is whether the number you think you set ever arrived. | console |
| `SeaFoamTuner.cs` (`Scripts/Dev/`, in Sea.unity, right-hand panel) | Drives the sea's white water and crest sharpness by hand with the numbers on screen — four things kept separable because they fail in different directions: FOLD (`_FoamJThreshold`, `_FoamSnap` — how readily the shader calls water breaking), TRAIL (the persistent buffer's threshold, half-life, injection and selectivity), SPUME (the particle gate, as a fraction of the peak foam this sea is making) and SHARPNESS (`crestSharpen.z`, cascade 2 only — **free**, because that band is not in the physics readback; x and y are not offered because they cost a Newton iteration and the budget is spent). Keys 7/8/9 mute fold/trail/spume, O prints a paste-able block, Home resets — deliberately clear of `WaterClarityTuner`'s 1/2/P/⌫. Writes the **live blend only, never the sea-state assets**, and is inert until a slider actually moves: an always-on overlay that re-asserts what it seeded from silently stops the foam following the weather. **Press O before leaving play mode** — runtime material edits do not survive it. | `/tmp/seasick-foam.txt` |
| `SetupSeaFoam.cs` (`Scripts/Dev/Editor/`) | Wires the foam/crest pass into Sea.unity and reports whether the shaders compiled — `check_compile_errors` is C# only, and a broken COMPUTE shader is worse than a magenta one: the dispatch is skipped in silence and the textures keep what was in them, which reads as a frozen sea. Every sea-state and material value is **written, not read-modify-written**, because a Resources `ScriptableObject` IS the asset, a runtime write to it survives play mode, `ImportAsset(ForceUpdate)` does not undo it, and the next `SaveAssets` writes the corruption to disk. It did once; the four sea states came back out of git. Material values are read back after writing. Cannot run in play mode. | console |
| `StallProbe.cs` | **Sailing-speed gate**: head seas at severity 0.40 and 0.75, plow drag toggled ON/OFF over the same water. Reports mean way vs target, distance made good, stalls/min, recovery time, and peak plow against the sail's authority. Also a calm sails-furled leg that checks plow really is silent at rest. Play mode, Sea.unity. | `/tmp/seasick-stall.txt` |
| `BuryTrace.cs` | BuryProbe's run as a time series instead of a verdict — ship y, sampled surface, batched surface, draft, submersion, reserve, plow, speed, pitch, roll. The sampler-vs-batch column is the one that says whether a wild draft number is a sinking ship or a lying instrument. Play mode, Sea.unity. | `/tmp/seasick-burytrace.txt` |
| `RippleStressProbe.cs` (`Scripts/Dev/`) | **Ripple-needle gate**: two legs (driving + splash spam, and stalled in a storm), GPU readback scored on **neighbour gradient** and texels riding the clamp — not magnitude, which the Step clamp makes unfalsifiable. Gate: gradient < 0.35 m/texel, zero at clamp, zero non-finite. `RunProbe.Ripple()`. **`RunProbe.RippleJitter()` is the one that can FAIL**: it sets `DynamicWaterSim.DebugDtJitter` to 0.4 and runs the driving leg on both sides of `DynamicWaterSim.FixedTimestep`. A steady dt is the condition under which the variable-sub-step bug does not happen, and every remote run is pinned at a dead-steady 10 fps — so the plain gate is green either way. Measured 2026-09-17: old `dt/steps` **2.398 m/texel with 20 465 texels on the clamp**, fixed sub-step **0.043**, 55x. Lived in `Dev/Editor/` until 2026-09-17 and therefore had never run at all. Play mode, Sea.unity. | `/tmp/seasick-ripplestress.txt` |
| `WaveSizeProbe.cs` | **How big, and CAN SHE CLIMB IT.** Per sea state over a verified deep-water patch: Hs from **20 km of pooled transects** (the trustworthy one) and from the 1600 m patch (kept for continuity, +-25%); **face angle** from the surface normal; then the same wavelength / height / face-angle analysis again on a **low-passed profile with the chop below 121 m filtered out** -- the mountain alone, which is what "can she climb it" is actually about. Reports **face length in boat lengths** and **seabed clearance**. Hunts genuinely deep water down a ladder from -110 m and reports the seabed it found. Forces the PC ocean tier. Play mode, Sea.unity. | `/tmp/seasick-wavesize.txt` |
| `RideProbe.cs` | **Can she ride it.** WaveSizeProbe says what the sea is; this says what the SHIP does in it. Finds genuine deep water (same depth ladder — a probe on a shelf measures the depth limit, not the storm), forces the storm and sails her bow-on for 60 s, reporting pitch and roll distributions against `ShipMotor`'s soft limits and how often those limits push back, the chase camera's height over the water against its own floor, and speed against her maximum (a long face is a gravity ramp). Reads every limit off the live components rather than hardcoding them. Play mode, Sea.unity. | `/tmp/seasick-ride.txt` |
| `SeaProfileProbe.cs` | **Where the sea is mountainous, and where it lies down.** Walks west from home in 400 m steps to 9 km with severity forced to 1.0, moving the ship to each station (the shore grid and the terrain streamer both follow it), and reports seabed, envelope, measured Hs and **which term is binding** — depth limit, shore falloff, region, or nothing. Every other probe measures one spot; "mountainous seas out west only" is a claim about geography. It is also the standing check that the depth limit is not quietly flattening water the player sails through. Play mode, Sea.unity. | `/tmp/seasick-seaprofile.txt` |
| `WaveShot.cs` | Five sea-level looks at ONE pinned instant of the storm sea with the ship in frame for scale: astern, from the deepest trough toward the highest crest, from that crest, beam-on, and a high three-quarter that shows the wavelength pattern and where the displacement fade cuts in. Phase pinned so a re-run shoots the same water. Forces the PC ocean tier. Play mode, Sea.unity. | `/tmp/seasick-wave-0..4.png`, `-waveshot.txt` |
| `SprayDebug.cs` | Per-second spindrift emission budget log. | Unity log |
| `ReadbackDiag.cs` | The clock against the readback ring: `OceanTime.Now` vs `OceanSampler.SurfaceTime` per frame across a deliberate BACKWARD scrub and a forward one, plus whether the loaded assembly actually has the fix in it. The instrument for "the stamp is not following the scrub". Play mode, OceanLab. | `/tmp/seasick-readbackdiag.txt` |
| `NoiseProbe.cs` | **Terrain noise gate** (edit mode): deterministic, seed-sensitive, no mirroring across the origin, fBm range/mean at 1/3/8 octaves, continuity at 80 km. Exports the map quad at 1 and N octaves. | `/tmp/seasick-noise.txt`, `-noise-*.png` |
| `HeightProbe.cs` | **Height pipeline gate** (edit mode): land ratio vs `landRatio`, open ocean always on a seabed below 0, beach band walkable (≤1.5 m/m), blend adds no discontinuities, `worldRadius` clamp drowns everything outside. Exports all four visualiser stages. | `/tmp/seasick-height.txt`, `-height-*.png` |
| `ChunkProbe.cs` | **Chunk mesh gate** (edit mode): vertex heights equal the height function exactly, normals unit, +X/+Z shared edges bit-identical in position AND normal, LOD-2 vertices a subset of LOD-1. Renders the lab camera to PNG without play mode. | `/tmp/seasick-chunk.txt`, `-chunk.png` |
| `StreamProbe.cs` | **Streaming gate** (play mode, TerrainLab): 2 km sail at 12 m/s, then coverage, unload band, collider ring, LOD assignment, pool bound, seams over every loaded same-LOD pair, and steady-state main-thread cost (< 10 ms worst). ~3 min. | `/tmp/seasick-stream.txt`, `-stream.png` |
| `LeeProbe.cs` (`Scripts/Dev/`) | **What the sea does as you come in on an island.** A transect from open water to the beach: at every station the depth, the envelope EACH CASCADE gets, and what that leaves of each band in metres. Exists to separate the envelope's two quite different reasons to flatten the sea near land — the per-island radial DISC (`MaxRadius` + 60 m, which knows nothing about the seabed) and the DEPTH terms (which are the physical ones) — so you can see which is doing it. Built round three traps it hit first time: the shore grid only covers the SHIP (so it runs the transect toward her and every station says whether it was in the grid — outside, depth is a 1e9 sentinel that must never be averaged in); a 14 m patch cannot see a 515 m swell (band heights come from per-cascade RMS read off the displacement textures, the patch RMS is kept and labelled as the chop measurement it is); and a probe that assumes a coordinate lands on an island. Ends with a walk into genuinely shallow water reporting the envelope at 12/8/4/2/1 m depth against the breaking cap. Play mode, Sea.unity. | `/tmp/seasick-lee.txt` |
| `ShoreProbe.cs` | **Terrain→ocean gate** (play mode, Sea.unity): severity forced to 1.0; surface RMS at deep water / shoreline / land must be intact / <10 % / 0; CPU shore factor vs exact height. Shot of the beach. | `/tmp/seasick-shore.txt`, `-shore.png` |
| `WorldProbe.cs` | **Populator gate** (play mode, Sea.unity): islands found, home + Stockpile, centres on land, outline at the waterline, beaches, props grounded, reefs/monsters in water, raiders, spawn landable. | `/tmp/seasick-world.txt` |
| `RaidTrace.cs` (`Scripts/Dev/`) | **Do raiders stay off the land, and does the landing party stand on it?** (Kevin 2026-09-26: ships through the island, raiders never seen ashore.) Samples every raider's bow/centre/stern keel against `Island.TerrainHeight` five times a second (a beached raider is exempt only within 30 m of her own landing) and every `RaidWalker`'s feet. `RaidTrace.Begin()`, then `RaidTrace.Raid("Island_2")` sends that island's raider at its camp now (skips the 25 s clock), `RaidTrace.Report()` returns `hull aground n/N (patrol)`, `(raiding)`, `walkers on land`, worst ground-above-keel. Before the fix on Kevin's save: 572/5255, 401/401, 240/1719; after: 0/2104, 0/190, 231/231. Play mode, Sea.unity; set the save-dir override first. | `Logs/raid-trace.txt` |
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

- **Swing a weather knob ABOUT what the asset authored; never replace it.**
  This rule had to be learnt three times in one afternoon on the two-axis
  pass, and each time it presented as a different bug. Letting the swell's
  energy split run to 45 % in the SHORT train took WaveSizeProbe's storm face
  angle from 15.1 to 18.0 deg median and 56.6 to 78.0 worst, while the
  SWELL-ONLY face angle barely moved -- the tell that the mountain was fine
  and energy had slid down into the short train. Letting it run the other way,
  to 98 % in one train, was safe for steepness and made the sea long-crested,
  which reads HIGH on a one-dimensional transect: measured Hs 60 -> 77 m
  against a declared 65, the number the depth cap is written against. And
  swinging the two swell trains' crossing angle about ZERO rather than about
  the authored 45 deg did the same thing for a subtler reason -- Perlin spends
  most of its time near its middle, so the trains averaged 28 deg apart, the
  sea narrowed, and Hs read 77 m again with total variance never having moved.
  **A measured Hs that rises while total variance is constant is a directional
  spread problem, not an energy one.**

- **Indexing a `float3` inside a Burst hot path costs a factor of four, and
  only `DivergenceProbe`'s timing line says so.** `RegionFieldParams.
  EvaluateCascades` used to loop `for (int c = 0; c < 3; c++)` and index
  `patchCoupling[c]` / `result[c]`. Taking the address of a float3 to index it
  spills it to the stack and stops Burst vectorising everything around it, and
  this function runs EIGHT TIMES PER QUERY inside the sampler's Newton loop.
  Measured on the shipped storm: `SampleBatch` **0.38 -> 1.72 ms** per 1000
  queries against a 0.4 ms budget, from nothing but the indexing — while the
  accuracy gate stayed green at 2.49 cm with 0 of 5000 over, so the only red
  thing was the millisecond count. Rewritten as three-wide arithmetic with no
  `[c]` anywhere it came back at **0.305 ms**, faster than the loop version had
  ever been. **Bisect a red timing line the same way you would a red error
  line** — the baseline was reading 0.38 ms that session where it had read 0.26
  an hour earlier, so the machine drifts and only a same-session A/B is worth
  anything.

- **A probe that measures where the SHIP ISN'T measures nothing, and it fails
  green as often as red.** `ShoreProbe` and `CalmWaterShot` were both written
  when the ship spawned at home, and `PlaytestStart` now puts her at
  (-2400, 725) in 180 m of water. The shore grid follows the ship, so
  ShoreProbe's test points south of the WORLD ORIGIN had no grid over them at
  all: `ShoreFactor` returned "outside, sea untouched" and it reported three red
  gates -- worst grid error 1.000, 1.14 m of waves on dry land -- with nothing
  wrong with the ocean. CalmWaterShot was worse, because it PASSED: it called
  wherever the ship was "inshore" and so compared open water with open water,
  ratio 0.795 where the number it was written to catch was 0.24. **Fixed
  2026-08-29**: both now FIND what they need (nearest island, then walk the
  bearing to the ship for a real shoreline / the shallowest anchorage), WARP the
  ship there so the grid follows, wait for the streamer, and **assert coverage
  before believing a measurement** -- ShoreProbe's first gate is now
  `grid-covers-the-test-points`, checking the 1e9 "no seabed known" sentinel at
  all three stations. Results after: grid error 1.000 -> 0.019, and the calm
  ratio 0.795 -> 0.270. CalmWaterShot also gained a second gate, because
  "inshore RMS > 0.02" alone cannot tell shelter from open water.

- **`Allocator.Temp` is valid for ONE FRAME, so it dies the moment a probe
  learns to wait.** `TerrainCurveLut.Bake(..., Allocator.Temp)` was fine in the
  old ShoreProbe, which never yielded between baking the curve and using it. Add
  a `WaitForSeconds` for the streamer and you get an `ObjectDisposedException`
  from inside the height function seconds later, which reads like a corrupted
  terrain rather than an allocator lifetime. Use `Allocator.Persistent` and
  dispose it on every exit path.

- **Indexing a `float3` inside a Burst hot path costs a factor of four, and
  only `DivergenceProbe`'s timing line says so.** `RegionFieldParams.
  EvaluateCascades` used to loop `for (int c = 0; c < 3; c++)` and index
  `patchCoupling[c]` / `result[c]`. Taking the address of a float3 to index it
  spills it to the stack and stops Burst vectorising everything around it, and
  this function runs EIGHT TIMES PER QUERY inside the sampler's Newton loop.
  Measured on the shipped storm: `SampleBatch` **0.38 -> 1.72 ms** per 1000
  queries against a 0.4 ms budget, from nothing but the indexing — while the
  accuracy gate stayed green at 2.49 cm with 0 of 5000 over, so the only red
  thing was the millisecond count. Rewritten as three-wide arithmetic with no
  `[c]` anywhere it came back at **0.305 ms**, faster than the loop version had
  ever been. **Bisect a red timing line the same way you would a red error
  line** — the baseline was reading 0.38 ms that session where it had read 0.26
  an hour earlier, so the machine drifts and only a same-session A/B is worth
  anything.

- **`ShoreProbe` and `CalmWaterShot` are both mis-sited, and have been since
  the spawn moved.** Both were written when the ship started at home:
  `ShoreProbe` looks for the shoreline along the line south of the world
  ORIGIN, and `CalmWaterShot` calls wherever the ship happens to be "inshore".
  `PlaytestStart` now spawns her at (-2400, 725) in 180 m of water, so the
  shore grid — which follows the ship — never covers ShoreProbe's test points
  (measured 2026-08-28: `shore-factor-grid` worst error 1.000, `land-flat` rms
  1.14 m, three of its gates red with nothing wrong with the ocean), and
  CalmWaterShot compares open water against open water (inshore/offshore ratio
  0.795, where the number it was written to catch was 0.24). **Both are red or
  meaningless for reasons that predate any ocean change** — bisect before
  believing either. The fix in each is to warp the ship to the water under
  test and let the streamer catch up, the way `SeaProfileProbe` already does.

- **A directional light's yaw is the direction the light TRAVELS, so the sun is
  at the opposite azimuth.** `ShaderStrip.SunAngles` set the light's yaw to the
  camera's heading to put the sun in shot and produced four identical tiles
  with no sun in any of them — it was squarely behind the camera. `+180`. The
  same sheet then showed the jade subsurface term lighting up for the first
  time, because that effect is backlit and had never been pointed at.

- **Pausing `OceanTime` silently freezes the SPECTRUM as well as the waves.**
  `SeaStateController` throttles its rebuild on `OceanTime.Now -
  lastRebuildTime`; with the clock paused that difference stays zero forever
  and a forced sea state never reaches the water. Any probe that wants several
  sea states at one pinned phase must unpause, change the state, let it land,
  and only then scrub and freeze — in that order.

- **A dev shader global must be phrased as what it turns OFF.** An unset global
  reads as ZERO, so `_SS_LayerOff` gives the shipped look when nothing binds it
  and a probe that forgets to reset can only fail loudly. Phrased as
  `_SS_LayerOn` the same forgetfulness would ship an ocean with its reflections
  switched off and nothing would say so.

- **Lowering a buoyancy probe rig makes the hull float HIGHER, not lower.** The
  probes sit deeper for a given hull position, so they make more lift, so the
  equilibrium moves up — measured at almost exactly 1:1 in the wrong direction
  (−0.57 m of rig gave +0.59 m of hull). The rig goes **up** for her to settle
  **down**. Worth a five-minute measurement before reasoning about which way any
  buoyancy geometry should move.

- **"It only looks wrong at speed" is not evidence that speed causes it.** She
  looked like she was planing on her own bow lift in a 20 m/s calm-water
  screenshot. `WaterlineProbe` measured 0.57 m high at rest and 0.55 m at
  19.7 m/s: a static displacement error the whole time. Measure the at-rest case
  before blaming the dynamic term.

- **A threshold in a metric goes stale the moment the thing it measures is
  re-centred.** `LivingSeaTrace` counted crossings of 0.5 to time how fast
  roughness patches pass. Then the bake was biased so the field sits near 0.72
  — the water's default state is the sea as authored and a patch is a slick
  passing through it — and the probe promptly reported "one patch every ten
  minutes" for a field turning over every three. Counting crossings of the
  trace's *own mean* fixed the units and was still wrong in kind: what a player
  sees is a slick **arriving and leaving**, so the probe counts events, their
  length and what fraction of the time they cover. **Measure the event, not the
  statistic that happened to correlate with it.**

- **A new envelope term is certified for free unless the probe makes it vary.**
  `DivergenceProbe`'s sample disc is 600 m and the shipped weather tile is
  4096 m, so the patch field was very nearly constant across it and both twins
  would have agreed on it whatever the formula said. The probe now builds its
  own field on a **220 m** tile and prints the field's span, flagging it when
  the spread is too small to gate — the same reason it over-drives choppiness
  by 1.25.

- **A probe that hardcodes the value it is checking against reports on the
  code it was WRITTEN against, not the code being measured.** RideProbe baked
  in `pitchLimit = 16`; after the scene was changed to 26 it went on printing
  "past the limit on 11.4% of frames" against a limit that no longer existed.
  Read serialised values off the live component (reflection is fine in a
  probe) and print them, so the report says which build it measured.
- **A soft limit firing is not the same as a soft limit BINDING.** The pitch
  limit was measured active on 11.5% of frames in the storm sea, which looked
  like the spring holding her bow out of the water. Raising it 16 -> 26 moved
  peak pitch 18.9 -> 19.1 degrees: nothing. She tops out around 19 on her own,
  because a 20.9 m hull follows the mean slope under its length, not the
  steepest point of a face, and the steepest faces are brief. Change one thing
  and re-measure before believing a mechanism.

- **`StallProbe` warps to a HARDCODED (-1500, 0, 0)** and is therefore not
  measuring a sea anybody chose — the same fault WaveSizeProbe's own comment
  records from its first run. Against the storm sea it returned `maxRoll=0
  maxPitch=0` at every severity and plow-ON identical to plow-OFF within
  0.1 m, which is not chaos, it is a ship that is not in the waves. Any probe
  that needs open water must FIND it (see WaveSizeProbe's depth ladder), and
  any probe that reports an attitude of exactly zero in a storm is reporting
  its own setup, not the sea.

- **A metric that does not separate the thing you are claiming is a lying
  metric.** The storm sea measured "face angle median 8.7 deg, wavelength
  275 m" while it was in fact 485 m rollers with wind chop riding on them:
  zero-upcrossing counts every ripple on the side of a mountain, and the
  median slope is dominated by the chop because the chop covers most of the
  AREA. The mountain was visible only in the p90. Low-pass the profile to the
  band you are actually talking about (WaveSizeProbe filters below 121 m) and
  report both, or the number will describe the texture and be read as the
  shape.
- **A patch a few wavelengths across cannot measure Hs.** Two runs of
  identical code returned Hs 47.78 and 37.20 over a 1600 m patch, because a
  500 m sea puts only about three wavelengths across it and the RMS estimator
  has a ~25% standard error there. Neither run was wrong and chasing the
  difference would have been chasing noise. Pool long transects instead --
  five 4 km lines is forty wavelengths.
- **Rayleigh spread means the TYPICAL wave is nothing like Hs.** A broad-band
  sea's median wave height is about 0.59 x Hs, so an Hs 40 m sea at 485 m has
  17 m typical waves (a 6 degree face) and rare 64 m monsters (23 degrees).
  Quoting Hs and then reasoning about "the wave" as if it were that tall
  overestimates the typical face by nearly a factor of two. Narrowing the
  spectral band moves the median toward 0.71 x Hs and makes the rollers
  uniform; that is a look decision as much as a numbers one.
- **The clipmap follows `Camera.main`, not whatever camera you just made.**
  A screenshot probe that spawns its own camera 900 m from the ship leaves the
  rings centred on the ship, so the shot looks out through a ring boundary and
  its skirt -- which reads as a hard diagonal seam across the water and looks
  exactly like an ocean bug. Set `OceanClipmap.FollowOverride` to the shot
  camera and restore it after.

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
- **An unbound texture SILENTLY SKIPS A WHOLE COMPUTE DISPATCH.** This is what
  turned `DivergenceProbe` red for a fortnight, and it was never an ocean bug.
  `RegionField.Publish` bound `_Ocean_ShoreTex` only when a shore grid existed,
  and OceanLab has no terrain. A fragment shader tolerates that — the
  `_Ocean_ShoreRect.w` guard means it never samples the texture — but a
  compute dispatch validates every declared resource up front, refuses, and
  writes NOTHING. The verify kernel returned zeros for every query, so the
  probe was comparing the CPU sampler against an empty buffer and reporting the
  difference as an ocean parity failure. The tell is in `Editor.log`, never in
  the probe: `Compute shader (OceanVerify): Property (_Ocean_ShoreTex) at
  kernel index (0) is not set`. **Bind every resource an .hlsl declares, even
  the ones a guard stops you sampling**, and grep the log for "is not set"
  whenever a compute result is suspiciously zero or constant.
- **The readback ring ranked its slots by TIMESTAMP, so a backward scrub wedged
  it forever.** `DisplacementReadback` picked `Latest`/`Previous` as the two
  largest `s.time`. That is indistinguishable from correct while OceanTime only
  advances — and permanently broken the moment anything scrubs backwards: the
  fresh slot's time is lower than the stale ones, so it never becomes Latest,
  so it counts as free, so the next Tick recycles it. Physics then rides a
  surface frozen at the highest time the ring ever saw. Probes scrub backwards
  constantly. This is the whole explanation of the famous scatter — a run whose
  targets happened to be forward passed, one starting past the last target gave
  zero samples, one landing mid-range gave a partial nonsense number. Slots are
  now ranked by **completion order** (`seq`). Ranking anything by a clock a
  probe is allowed to rewind is a bug waiting for a probe.
- **A probe that "skips" a bad instant reports a passing average.** The old
  DivergenceProbe printed "readback never caught up" and carried on, so a run
  that measured nothing at all still produced a summary line and a verdict.
  Any instant a probe cannot measure must make the run FAIL, loudly, and say
  it is an instrument failure rather than a sea state.
- **`SeaStateController.ForceSeverity` does not stop it writing settings.**
  It pins severity, but `Update` still reaches `ocean.SetSettings(blend)` on
  every rebuild and hands the renderer its own drifting blend — so a probe that
  calls `SetSettings` itself is overwritten within a frame and measures a sea
  nobody chose. Disable the component for the run, then restore it.
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
- **A domain reload mid-play restores UnityEngine.Object fields and drops
  everything else, and `Start` does not run again.** Anything under `Assets/`
  changing while you are playing recompiles and reloads the domain — the same
  event that kills a running probe coroutine. What comes back is asymmetric:
  `Transform[]` survives (object references), a plain C# array like
  `OceanProbeRegistry.Handle[]` comes back **null**, and every `static`
  collection — the registry's own list included — is emptied. `SalvageSpawner`
  spent a session throwing a NullReferenceException on EVERY frame from this:
  it guarded on the half that survives, passed, and dereferenced the half that
  did not, spamming ~2000 lines/second and growing `Editor.log` to 1.2 GB. A
  component that builds runtime state in `Start` must ask **"is my state whole
  now?"** in `Update` and rebind if it is not — "did `Start` finish?" is only
  true of the first frame.

---

## 2026-08-30 — deck, land, harbour and the docked view

### `DeckStandProbe` (`RunProbe.Stand`, works in edit mode too)
Signed gap between each crew body's lowest vertex and the planking under it,
in SHIP-LOCAL space from mesh vertices — world bounds are axis-aligned, so a
heeled ship reports a phantom sink that is really just roll. Also reports the
gap in the bailing pose, which keeps the station's `y` while stepping inboard
onto a cambered deck.

**Its first version was circular and read 0.000 m everywhere.** It asked the
deck for its highest vertex within 1.2 m — the same rule `SetupPaddleBoat`
placed the crew by — so it could only agree with itself. Interpolating the
actual triangle under the boot showed three of six standing 7.1 cm in the air.
**A gap of exactly zero is a symptom, not a pass.**

### `TuneIslands.Flats`
Largest CONTIGUOUS buildable area per island: 25 m pass to find islands, 4 m
raster each, 4-connected labels, exact Euclidean distance transform inside the
biggest patch. Reports area AND inscribed circle, because they are not
interchangeable — a contour-following ribbon carries hectares and holds no
compound anywhere along it.

Percentages from a dart-thrower cannot answer this at all: one field and a
thousand patches give the same number.

### `TuneIslands.HomeCandidates`
Ranks islands to be home on three things that pull against each other: small
enough to see whole (the chase camera's far clip is 600 m, so anything over
about 185 m in radius cannot be framed), big enough to settle, and in
possession of a harbour. Prints the `worldOffset` that makes one home —
sampling is `noise(p + worldOffset)`, so `newOffset = centre + oldOffset`.

### `WoodProbe` (`RunProbe.Wood`)
Reads the BAKED scenery mesh — trees found by trunk colour, a run of
consecutive trunk-brown vertices being one tree. Density in rings, spacing,
and the tree line. **Never re-run the placement rules to check placement.**
Note the tree line is the highest trunk BASE; the highest trunk VERTEX is the
top of a trunk and reads ~4.5 m high.

### `HarbourProbe` (`RunProbe.Harbour`)
Reports the dock site from the SAME `HarbourSite` finder the populator builds
from, then measures the dock as built: deck height, pile feet, ground
clearance under the decking, water under her whole hull box, daylight to the
pier, face-normal direction, and whether the dock is even on the home island.
Also gates the docked camera against the framing Kevin flew by hand
(azimuth / centre / span / lens).

### `DockCamTuner` (scene object, `active` off)
Fly the docked overview by hand: right-drag swing/tilt, scroll zoom, IJKL pan,
U/O lens, P saves the numbers, backspace resets. The readout is drawn ON
SCREEN so a screenshot carries every number needed to reproduce the shot.

**Kept on purpose.** Composition is a judgement, not a measurement. Three
rounds of choosing a tilt and framing radius by arithmetic produced three
defensible views and none of them the wanted one; one round of Kevin flying it
settled it. For a look-or-feel question, build the tuner and hand it over.

### Traps this session added to the pile
- **(2026-10-03) A launch crash in `il2cpp_init` is a stale incremental Xcode build, not the code.**
  The app died ~4 s after every launch (`il2cpp::vm::SetupGCDescriptor`,
  `KERN_PROTECTION_FAILURE`, termination "Invalid Page") although `codesign --verify`
  passed and only two IMGUI files had changed. `rm -rf Builds/DerivedData` plus a full
  `xcodebuild` (~9 min) fixed it. It came on the third incremental build in a row, on a
  near-full disk. `phone-build.sh` lists the `.ips`; if the top frames are il2cpp init,
  rebuild clean before suspecting a commit. Also: `devicectl install` fails with "Failed
  to allocate RSD device" while the phone sleeps; retry it once the phone shows as
  `connected`. Launch fails while the phone is locked.
- **Winding and normals are two separate decisions.** Every dock face drew
  correctly and was lit from behind — ambient-only near-black. `Cross(b-a,e-a)`
  on a deck top is X×Z = −Y. A screenshot only says "dark".
- **`Camera.main` is the MINIMAP camera**, 120 m up looking straight down.
- **`Screen.height` inside `LateUpdate` is the Game view** (422 px here); an
  editor-context probe reads something else entirely (937). Never key framing
  to it — use a FRACTION of screen height.
- **`ScreenCapture` is not in the runtime Dev assembly**, and an Editor-
  assembly MonoBehaviour cannot be `AddComponent`ed at play time. Render the
  camera to a RenderTexture synchronously instead.
- **A square raster with a radius test** reaches 1.41× the radius at its
  corners — that is how a 322 m harbour search from a 151 m island built the
  home dock on the neighbour.
- **The serialization trap again, twice, and worse than documented:**
  `overviewTilt` read 56 in a running build whose source said 38, and the
  field appears NOWHERE in `Sea.unity` — grepping the scene said "absent"
  while the live component said 56. Only the running object knows. Push with
  `SerializedObject` and read back.

## 2026-08-30 (later) — the voyage loop and the first buildings

### `VillageProbe` (`RunProbe.Village`)
Where the village clearing is, whether it is empty, and — the half worth
having — **whether reserving it moved the rest of the wood**. Trees are found
by trunk colour off the BAKED mesh, exactly as `WoodProbe` finds them. Then it
bakes the same island a second time with no keep-out at all and matches the two
trunk sets: every tree outside the clearing must be in both, at the same place.
`IslandScenery` walks one `System.Random` through the grid in order, so this is
the only way to know a keep-out did not reshuffle the forest. Last run: 267/267
matched at 0.000 m, clearing cost 31 trees.

### `VillageProbe.Build` (`RunProbe.VillageBuild`) and `.Shot` (`RunProbe.VillageShot`)
`Build` raises storehouses until the clearing refuses one and reports, for each,
its distance from the clearing centre and the **drop between its highest and
lowest footprint corner** — the gap the footing has to bridge, because nothing
here flattens ground. `Shot` writes `/tmp/village-dock.png` (the game's own
docked camera) and `/tmp/village-plan.png` (straight down on the clearing).

### `LoopProbe` (`RunProbe.Loop`)
One whole voyage in ten seconds: cast off, warp out, load past capacity, warp
back, come alongside, land the haul, build. **It moves the ship and lets the
real systems run** — it never calls `CompleteVoyage` or sets a phase itself. A
check that drove the state machine by hand would prove only that the state
machine can be driven by hand. It is what caught the frame-one voyage.

### `HomePanelShot` — REMOVED 2026-09-26
Used to grab the "VOYAGE COMPLETE" home panel. The panel is gone (Kevin:
*"voyage complete still shows up. I don't want that one there at all. It
doesn't serve a purpose for the game."*) and the tool went with it.

### Traps this session added to the pile
- **The editor's Game view is landscape and this game is not.** Ground projected
  through the live camera put the village at viewport x 0.18 — comfortably in
  frame — and at the shipping 900×1500 the same point is at **x −0.26, off the
  left edge**. Force `cam.aspect` before judging any framing, and remember the
  docked shot in portrait is a narrow wedge: ±43 m across the pier axis against
  252 m inland.
- **A distance test cannot tell "not yet arrived" from "not yet placed."** The
  voyage completed on frame one because the ship sits at her scene position for
  the frames before the populator has built the dock and moved her onto it —
  483 m from a berth that does not exist, which is indistinguishable from a ship
  that has sailed. Gate on having *been* somewhere, not on being far from it.
- **A "safe" bound is not free.** Siting the village within a conservative 70 m
  of the frame centre instead of the measured 42 m half-width sounded harmless
  and picked a clearing that holds exactly one building. Measure the real
  constraint; a guessed one is wrong in both directions at once.

### `LandProbe` (`RunProbe.Land` and `RunProbe.LandSail`)
`Land` stands a virtual ship 20 m off every island on 36 bearings and asks the
**same three questions `AnchorController` asks, in that order** — which island
does it think it is beside, is the ship inside that island's reach, does that
island have a beach here. Reporting only the last would say "beach: yes" on a
shore where no prompt ever appears, because the first question already picked a
different island. It also measures the shore slope straight off the height
field, so the island's own `hasBeach` has something independent to disagree
with.

`LandSail` sails the gathering half for real: cast off, stand off the nearest
island with resources, press LAND, and watch for thirty seconds. It presses the
same buttons the player presses and never calls `SendAshore` itself — which is
the only reason it caught the failure below, where the prompt worked perfectly
and nothing happened afterwards.

## 2026-09-08 — the swallowing, and the probe that could finally see it

### `SwallowProbe` (`Scripts/Dev/`, `ExecuteNew4/Old4/New12/Old12`)
**Does the sea swallow her, at a severity the player actually sails?** Reports
in units of HER OWN FREEBOARD (awash > 0.3×fb, swallowed > 1×fb, worst rail
depth ×fb), because "she went under" is a fraction of the hull, not a number
of metres. SEGMENTED: six 15 s windows, each re-warped to the same spot with
`OceanTime` re-pinned to the same instants and **her heading set into the
seas at the warp** — one continuous run diverges chaotically from the pinned
start and two legs stop being comparable. Old legs restore the pre-scaling
constants by reflection, so one build carries both. Also prints the terms
that explain the result: forward way against the motor's target,
`SeaResistance01`, and the plow / dynamic-lift accelerations off
`BuoyantBody`'s debug surface. Writes `/tmp/seasick-swallow.txt`.

### Traps this session added to the pile
- **BuryProbe's severity-1.0 gate is not an A/B for small hulls.** Two
  control runs of IDENTICAL physics: poopDeckUnder 4.7% and 10.6%,
  deckOverMax 20.6 m and 43.4 m. The documented severity-1.0 chaos trap,
  now with its control measurement. Measure gameplay complaints at gameplay
  severities.
- **A segmented probe that warps without setting the HEADING measures a
  turn, not a passage.** Left at identity, the autopilot needed up to 9 s
  to come round, the 15 s window caught her beam-on and mid-transient, and
  the leg reported 43% swallowed with rails 6.8 freeboards under — none of
  it what the run claimed to measure. `seaResistance 0.87` was the tell:
  she was 60 degrees off the sea the whole time.
- **"Rail probe deeper than one freeboard" partly measures the SEA'S
  geometry, not the ship's failure.** With ~8 m waves against a 15 m hull,
  a bow rail rides a freeboard under whenever she knifes a steep face even
  while her mid-body tracks the surface to 0.3 m RMS — and a bow slicing a
  crest LOOKS RIGHT. Gate on the worst depth and the deep tail; the shallow
  crossings are big-sea geometry.
- **Slowing her down is the fix the vertical axis cannot buy.** Every
  vertical remedy measured this session failed characteristically: scaled
  reserve buoyancy brings matching damping that GLUES her to the falling
  back of every wave (swallowed 2.8% → 8.4%); stronger/earlier bow lift
  reproduces the stern-under failure (poop 40.5% vs the area-fix's 46.8%);
  a clamp at a third of freeboard holds her INSIDE crests, the crests strip
  her way (7.9 → 1.8 m/s), and a parked boat is washed over (swallowed
  UP at 21.3%). The knob that worked is `ShipMotor`'s hull-relative
  head-sea rule.
- **A raised force ceiling proves nothing while the force never reaches
  it.** `maxPlowDecel` 4 → 5.08 changed mean way by exactly nothing,
  because plow's measured mean was 0.24 m/s² — the term barely fires in a
  lively sea. Print the term before tuning its cap (SwallowProbe now does).

### `WakeShot` (`Scripts/Dev/`)
**Does she leave a wake, and does the bow throw water?** Sails the player
ship at full throttle across a gentle sea long enough for the diverging wake
arms to develop, reports the new emitters are actually alive (live particle
counts for the wake arms / bow / shoulders — a misconfigured emitter reads
zero), then shoots a high stern-quarter frame through the MAIN camera into a
RenderTexture (the clipmap follows `Camera.main`, so its own camera is set as
`OceanClipmap.FollowOverride` for the shot and restored after; IMGUI HUD never
reaches the texture). Runs the course ALONG the seas, not into them — a
head-sea run lets her surf the faces past hull speed and strings the wake
into dots. Play mode, Sea.unity. Writes `/tmp/seasick-wake-shot.png/.txt`.

## 2026-09-08 — "the water lags in heavy weather"

### `CostProbe` (`Scripts/Dev/`)
**What does heavy water COST, against calm, per frame?** One session, same
spot, same course at full throttle: 15 s at severity 0.15 then 15 s at 1.0,
reading `ProfilerRecorder` markers and counters every frame (PlayerLoop,
BehaviourUpdate, physics, particles, Camera.Render, Gfx.WaitForPresent,
GUI.Repaint, GC.Collect, GC Allocated In Frame, SetPass/Triangles), plus the
live particle count across every system, `OceanSampler.ImmediateCalls` per
frame (new counter), and the GPU frame time where the platform reports it.
Prints p50/p95/max per leg and the p50 ratio. Markers stay honest under the
editor's unfocused 10 fps throttle; the frame-time row does not. Writes
`/tmp/seasick-cost.txt`.

The 2026-09-08 answer: heavy water cost **+1.06 ms/frame of script Update
and nothing else** (GPU flat at 4.3 ms) — all of it `StormSpray`'s 24
main-thread `SampleImmediate` crest samples, the one continuous consumer not
on `OceanProbeRegistry`. Separately, weather-independent: **77 KB of garbage
a frame** → 12 ms `GC.Collect` pauses, attributed (see below). After: heavy
Update 2.36 → 1.43 ms (= calm), garbage 77 → 17.5 KB/frame, GUI.Repaint
2.4 → 0.9 ms.

### Getting Coplay's `get_worst_cpu_frames` / `get_worst_gc_frames` to say anything
They read the editor profiler, which records nothing until it is switched
on. An `execute_script` with `UnityEditorInternal.ProfilerDriver.enabled =
true` (scratch `EnableProfiler.cs`) before the play session is all it takes;
then they attribute allocation and time per MonoBehaviour method without
deep profiling. This is how the 77 KB was traced to its scripts in one call.

### Traps this session added to the pile
- **`HitchProbe` driven from Coplay cannot see frame cost.** The unfocused
  editor throttles play to a flat 10 fps: 301 frames at 100.00 ms, `capped
  60` notwithstanding. It still measures the readback lag and hull pops
  correctly (ring flat at 2 frames, no pops) — which is how it ruled the
  "water stepping" fault OUT for this complaint. Cost needs markers.
- **A GUILayout panel that is OPEN allocates its whole layout tree on every
  IMGUI event.** `ShipyardPanel` — sixty controls — was scene-serialized
  `open = true` and drawn every frame at sea: **55 KB a frame**, the single
  largest garbage source, none of it visible in any ocean number. A `bool`
  default in code cannot close it (serialization trap); it now closes on
  casting off. Any GUILayout panel must be gated on *when it is needed*.
- **A "rebuild only when the displayed value moves" HUD rebuilds every frame
  if the value DECAYS.** `PerfHUD` keyed on a peak-held, decaying frame time
  and a decaying pop, so three text meshes were regenerated most frames —
  62 KB a frame from the instrument meant to show GC pauses. Keys are now
  re-evaluated at 4 Hz. Check what a key does between events, not just
  whether it is rounded.
- **`FindFirstObjectByType` in `Update` is a scene scan per frame.**
  `Shipyard.Update` did it for a VoyageManager that never moves: 0.24–1.4 ms
  and allocation, every frame. Cache, re-find only while null.
- **`OceanProbeRegistry.Handle.sampledFrame`** exists so a consumer that
  moves its handles every frame can tell a fresh sample from a stale one —
  Update runs several times between physics steps, and overwriting
  `position` before the driver has sampled it silently pairs a height with
  the wrong spot.

## The HUD (2026-09-11)

### `HudOverlapProbe` — does anything on the screen sit on top of anything else

Self-installs in the editor; no scene wiring. Logs `HUD CENSUS` (every rect on
screen, with the file and method that claimed it) and `HUD OVERLAP` (any two
rects that cross).

**It reads what the frame DREW, never what the layout says.** Its inputs are
`UIBlocker.Claimed` — every interactive control, named for free by
`[CallerFilePath]`/`[CallerMemberName]` — and `HudLayout.Issued`, the rects the
layout actually handed out. The test is rectangle intersection, which shares no
arithmetic with the stacking that produced them. Re-deriving each panel's rect
from `Screen.width` would have agreed with `HudLayout` by construction and
could never have caught `HudLayout` being wrong. It caught it three times.

**Run it at the phone's aspect.** `Dev/Editor/PortraitGameView.Execute()` sets
the Game view to 1080x2340. The editor window is landscape and the game is not;
a clean report from a landscape window certifies a screen no player will see,
and the report says so in its own output when the aspect is over 1.

**Read the persistence line, not just the fact.** Each pair reports at 1, 30 and
300 frames. `FIRST TIME` is usually the cold-start frame — the pass before
anything has reserved its space, where whichever panel draws first sizes itself
against an empty screen. `30 FRAMES` is a real overlap. This distinction is the
whole value: the same three lines meant "harmless" and "broken" on consecutive
runs, and only the count told them apart.

### Traps this pass added to the pile

- **A check whose exemption covers the failure mode is a check that stops
  before the broken thing.** The probe's first version treated one rect
  containing another as intentional nesting — a button inside its panel. The
  actual bug was the ship panel drawn INSIDE the minimap (916..1066, 14..86
  against 826..1066, 14..254), and the exemption swallowed it silently.
  Containment is now innocent only when one side is a control; two placed
  panels containing each other is always a fault.
- **A probe that only speaks when it fails cannot be told apart from a probe
  that is not running.** Hence `HUD CENSUS`: a clean run now carries its own
  evidence of what was in front of it. Same shape as `SailShots` printing a
  perfect table of ten seats while photographing one seat ten times.
- **Print the fullest cycle, and make the record per-STATE.** IMGUI runs
  several GUI cycles per frame and the repaint-guarded panels draw in only
  some, so a census on a timer lands on a partial cycle — 13 rects out of 34,
  missing the minimap, the ship panel, the crew pips and the compass. A single
  global high-water mark then silenced the census for the drawer-open state,
  which legitimately has fewer rects than the list.
- **Reserve on every event, draw only on Repaint.** The repaint guard still
  earns its keep (formatting on discarded passes was once the largest
  allocator in the game) but claiming a rect costs two float compares.
  Reserving only on Repaint made the layout depend on FRAME RATE: a panel
  could go several frames between placements while permanently on screen, so
  the slot above expired and the one below stacked at zero.
- **A layout must not expire on wall-clock.** Widening that window to 0.25 s
  of real time broke it differently — real time runs while the editor stalls
  (any blocking Coplay call does this), so one stall left every slot stale and
  the settings drawer sized itself against an empty screen, 752..2316 instead
  of stopping at 2156, across the anchor prompt and the nav line. Count
  frames, not seconds.
- **Coplay MCP calls need the Unity window FOCUSED.** Unfocused, the editor
  throttles and `check_compile_errors`, `stop_game` and `execute_script` all
  time out at 60 s. Unity also defers script compilation entirely while in
  play mode. The working loop is: activate Unity, stop play, wait for
  `Library/ScriptAssemblies/Assembly-CSharp.dll` to change, then play.

### `tools/compilecheck.sh` covers BOTH assemblies now (2026-09-11)

It excluded `*/Editor/*` — 109 files, and that is where every probe launcher,
setup script and shot tool in this project lives. A green run was silently
skipping all of them. It now builds `Assembly-CSharp-Editor` as well,
referencing the runtime assembly it just built rather than the possibly-stale
one in `Library/ScriptAssemblies`, so an editor script is checked against the
game code as it is right now.

**It was proved able to fail** by appending a reference to a nonexistent type
to an editor file, watching it report `errors 1` with the file and line, then
restoring. Do that again if you ever touch the script: a check that only ever
passes is worse than no check at all, and this project has already shipped one
of those (`git bundle verify`, which certified a backup missing every piece of
art).

Reach for it BEFORE the Unity focus trick. It answers in seconds and does not
need the editor, the bridge, or Kevin's window focus.

**2026-09-27: it now compiles with Unity's defines, and works from a worktree.**
Two more ways it had been lying, both fixed:

- **No scripting defines.** It passed no `-define:` at all, so every
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` member did not exist to it, and
  `ShipyardRefitProbe` failed with three CS0117s (`TestFaultStage`,
  `FaultBeforePersist`) that Unity never reported. It now reads the `-define:`
  lines from Unity's own response file for the **editor** build of each
  assembly, `Library/Bee/artifacts/<hash>E.dag/Assembly-CSharp.rsp` and
  `…/Assembly-CSharp-Editor.rsp`: 144 and 142 defines, including
  `UNITY_EDITOR`, `DEBUG` and `UNITY_IOS`. The two lists differ: the editor
  assembly gets `NET_4_6` and `UNITY_EDITOR_ONLY_COMPILATION`. The first line
  of output names the file the defines came from. If it says `FALLBACK`, no
  response file was found and it used a hard-coded 13 (UNITY_EDITOR, DEBUG,
  UNITY_IOS, …), so let Unity compile once. `DEVELOPMENT_BUILD` is *not* an
  editor define. It exists only in the player dag (`<hash>PDevDbg.dag`), and
  the editor gets the same code through the `UNITY_EDITOR ||` half.
- **Zero sources in a worktree.** The `-not -path "*/worktrees/*"` filter
  matched the checkout's own path under `.claude/worktrees/<name>/`, so it
  compiled nothing. (In a worktree it then died on the missing `Library`; with
  one present it would have printed `clean.`) The filter now matches paths
  relative to the project root, and an empty source list exits 2, never
  `clean.`. A worktree has no `Library` of its own, so it borrows the main
  checkout's reference DLLs and defines (it prints that it did). Its sources
  are always its own.

Re-proved on 2026-09-27 as described above: one unknown type in
`ShipyardRefitProbe.cs` → `Assembly-CSharp errors 1` with file and line;
one in `Dev/Editor/AddBilge.cs` → `Assembly-CSharp-Editor errors 1`; both
exit 1, both reverted. What it still does NOT check is a **release player
build**, where neither `UNITY_EDITOR` nor `DEVELOPMENT_BUILD` is defined.
`ShipyardRefitProbe.cs:378` uses those guarded members without a guard of its
own, so a non-development build would fail on it.

### `RunProbe.ViewDesk()` / `ViewPhone()` — check the HUD in both shapes

Landscape became first-class on 2026-09-11 without the phone being retired, so
there are two shapes the HUD must be right in and a clean run at one says
nothing about the other — `HudLayout.RailTop01` alone is 0.14 wide and 0.30
upright, and every panel hanging off the rail moves with it. `HudOverlapProbe`
prints which shape it ran at and names the one still to check.

`GameViewSize` holds the reflection both go through. Setting `cam.aspect` is
not a substitute: the IMGUI HUD lays out from `Screen.width`/`Screen.height`,
which reads the Game VIEW, and nothing about the camera moves it.

### And a reminder that cost this session an hour

`execute_script` **cannot compile a file that names project types.** It builds
a fresh assembly without the project's reference set and fails with no readable
diagnostic — here it simply timed out three times and logged nothing at all.
`SetLandscapeMode` names `ChaseCamera` and `PlayerSettings`; it works through
`RunProbe.Landscape()` and not otherwise. **Add a launcher entry rather than
handing a real file to `execute_script`.** This is written down twice already
and was still the thing that went wrong.

**Restart play after changing the Game view's aspect.** `HudOverlapProbe`
counts frames, not truth, and its counter only ever goes up — so a
configuration that overlapped for a few hundred frames while the panels
re-settled into a new shape reaches the `permanent` line and stays reported
even after it resolves. Measured 2026-09-11: switching desk to phone with the
drawer open reported the anchor prompt across the drawer by 269 px as
permanent, while the census on the same run showed them comfortably clear, and
a clean start at either aspect reported nothing at all.

## 2026-09-11 (later) — render settings, the timestep, and probes reading URP

`ProjectSettings/TimeManager.asset`'s `Maximum Allowed Timestep` was 0.333 s —
up to 16 physics steps replayed after one hitch, each one running the ocean
batch. It is now 0.15. A consequence: with the unfocused editor throttled to
~10 fps, a frame over 150 ms now loses simulation time instead of catching up
on the next tick. Any probe measuring speed or distance against wall-clock
(`DriveProbe`, `SeaTrialProbe`, `LoopProbe`, `CostProbe`) should be run with
the editor focused, or have its numbers read as game-time rather than
real-time.

`Application.targetFrameRate` is now pinned to 60 on desktop at load, by
`World/FramePacing.cs`. Probes that pin the rate themselves still save and
restore it around the run.

`TerrainPerfProbe` now reads shadow distance and cascade count off the URP
asset instead of the legacy `QualitySettings` fields, which URP overrides —
it was reporting 40 m / 2 cascades while the renderer actually ran 50 m / 4.

New `RunProbe` launchers: `Divergence`, `Perf`, `FFT`, `Strip`, `StripSun`,
`StripDay`.

### `DivergenceProbe` and `PerfProbe` moved to `Scripts/Dev/` (2026-09-11)

Both were MonoBehaviours in `Dev/Editor/`, so `Execute()` logged "Can't add
script behaviour ... because it is an editor script" and measured NOTHING -- the
same fault that killed every `ShaderStrip` sheet on 08-28, and it went unnoticed
because nobody reads the console for a probe that "ran". `RunProbe.Divergence()`
/ `RunProbe.Perf()` launch them now. The one editor API (`AssetDatabase` loading
`OceanVerify.compute`) is `#if UNITY_EDITOR`-guarded.

**The parity gate is RED, and has been since before today.** First run after
the move: 5 of 5000 points over the 6.5 cm gate, p99.9 12.8 cm, ONE point at
1130 cm, `env` max 0.000267 (unchanged), `disp` max 2.45 cm. A stash of
today's sampler change reproduced the failure byte for byte, so it predates
this session -- most likely the 09-09 `crestSharpen` pass (the sampler's
Newton count tracks steepness, and 7 iterations was already "no headroom").
The signature is a handful of outliers with the envelope unmoved: Newton not
converging on a folding crest, where `det ~ 0` is clamped and the step goes
wild. An 11 m error under the hull is a teleport. Fix belongs in
`SampleJobs.cs` (step clamp and/or adaptive envelope refresh), gated here.

### The parity gate, taken from 11 m to 11 cm (2026-09-11, later)

`RunProbe.Divergence()` in OceanLab, storm at +25 % choppiness, five instants,
5000 points. Every number below is from that run; the file is
`/tmp/seasick-divergence.txt`.

| sampler | over gate | worst | mean | SampleBatch |
|---|---|---|---|---|
| as found (7 plain Newton steps) | 5 | 1130 cm | 0.575 cm | 0.305 ms |
| + step clipped to 2|r| | 10 | 580 cm | 0.418 | 0.259 |
| backtracking on every step | 3 | 212 cm | 0.268 | 0.191 |
| Newton trusted off-fold, FoldDet 1e-3 | 4 | 241 cm | 0.297 | 0.175 |
| + best-so-far, damped second start | **3** | **11 cm** | **0.250** | **0.290** |

What each row taught, so nobody re-walks it: clipping every step throttles
the legitimate large steps steep water needs (one instant went 1.5 -> 107 cm);
backtracking on every step stalls points plain Newton converges (Newton is
not monotone in |r|); a fold threshold of 0.05 diverts near-fold points that
converge fine; and the point that never improved from p = q (14 m residual)
needed a different START, not a better step.

**The gate is still 3 of 5000, and that is the honest state.** The probe now
prints each outlier with the sampler's final residual (`OceanSample.residual`,
new): the three are at fold TIPS with 3-20 cm residuals -- the readback
disagrees with the live texture by up to 2.45 cm (`disp`), and at a fold tip
that is enough for the query to have no exact inverse in the CPU's field. A
fourth point converged to 0.1 mm and is still 2.1 m off: a genuine
multi-valued fold, where the renderer's vertex is another sheet. It is
reported, not gated -- a question with two answers cannot fail for giving one.
The gate's PASS now counts non-converged outliers only.

Cost: 0.290 ms of 0.4 (early exit pays for up to ten steps; the second start
is what took it back up from 0.175). `SeaStateController`'s "no headroom"
comments now say 0.29 of 0.4.

### Phase 3 of the perf pass: the CPU budget (2026-09-11, night)

- **`SampleImmediate` callers moved onto `OceanProbeRegistry`**: raiders, sea
  monsters, cannonballs and walking crew each hold one handle, sampled in the
  physics driver's Burst job. 14 immediate calls a frame at sea (11 of them
  raiders + monsters, ~42 us each, budget 8) -> the ship's own handful. A
  cannonball's SPLASH placement stays immediate (one call, on the frame it
  dies). Crew cache their island for 0.5 s and sample terrain at 10 Hz
  towards where they will be, so the height never lags the walk.
- **The physics driver's arrays are grow-only** (power-of-two capacity):
  with balls and crew registering and unregistering, the count moved every
  few frames and each move was a native dispose + alloc.
- **`HullIntegrity` grounding** reads the shore grid `RegionField` already
  keeps (16 m texels, exact seabed heights): open water costs 0 managed
  terrain evaluations (was 1/frame), aground at most 6 (was 29). The 8 m
  trust band below the touch threshold is a reasoned bound, not measured:
  `ShoreProbe` can nail it if a skerry ever gets sailed through.
- **IMGUI**: every runtime string that depends on a value is a `HudLabel`
  now, rebuilt on change, in `HelmInput`, `AnchorController`,
  `VoyageManager` (once per landing / building), `ShipyardPanel` (once per
  ship change), `CannonBattery`, `Bilge`, `SettingsPanel`. `UIBlocker`
  formats its owner label once ever instead of per call per event.
  `HudOverlapProbe`'s census log is opt-in (`logCensus` / `ForceCensus`):
  35-70 KB and up to 68 ms on the frame it fired, every 5 s, in every
  editor GC measurement this project has ever taken.
- **Ripple sim**: `Inject` dispatches the impulses' bounding box, not the
  whole 512^2 field; `ScrollRT` swaps references instead of copying 2 MB
  twice a frame; the sim goes quiescent (one clear, then no dispatches)
  after `6.91/damping` s without an impulse; the ship lookup retries at
  1 Hz instead of scanning the scene every LateUpdate.
- `Shader.PropertyToID` cached in `RegionField.Publish` (and the 24-island
  array uploads only on change), `OceanRenderer`, `DynamicWaterSim`,
  `HorizonField`, `WaterClarityTuner`. `TerrainStreamer` reuses two
  stopwatches. `BuoyantBody` transforms each probe once per step.
  Unthrottled `FindFirstObjectByType` in `CombatLock`, `EnemyShip`,
  `HomeTab`, `ShipyardPanel`, `Bilge`, `DynamicWaterSim` retry at 1 Hz.

### Phase 4 of the perf pass: the GPU and the mobile tier (2026-09-12)

- **The FFT is an LDS Stockham now** (`FFT.compute`, `FFTCompute.cs`): one
  dispatch per axis per field instead of 2*log2(N) -- 32 dispatches a frame
  became 4, and each row/column touches main memory twice instead of 16
  times. Same twiddle sign, no normalisation, result lands in `data`.
  `RunProbe.FFT()` (`FFTUnit`, edit mode) now runs 15 cases at N=64/128/256
  -- impulses on both axes (the transpose trap), a cosine pair, three pairs,
  and a flat spectrum -> N^2 delta -- against a CPU DFT: **PASS 15/15, worst
  relative error 4.4e-5**. The harness had a bug of its own: `GetData` with
  no layer index returns ONE layer of an array readback; gather per layer.
  The 512 kernels ask 16 KB of LDS, exactly the GLES 3.1 minimum, so they
  are separate kernels and only picked when N > 256.
- **The initial spectrum is computed once per texel, not twice**: `F(-k)`
  at texel i is `F(+k)` at the mirror texel, so `CalcAmplitude` writes an
  R32 scratch and `CalcInitialSpectrum` gathers its own and its mirror's.
  Bit-exact. And the rebuild is **sliced**: one cascade per frame, three
  frames per rebuild, H0 written in place so the evolve chain always reads
  a complete field; the first build after enable is whole. Probes that
  `SetSettings` and wait >= 3 frames need nothing (all of them wait more).
- **Clipmap**: `cellsAcross` is a tier field (PC 128, Mobile 64 -- the
  phone drew 75k verts at PC density; now 25k). Every ring is FOUR quadrant
  renderers under the ring transform with tight bounds, so the camera
  culls the sea behind it for the first time (every AABB used to contain
  the camera). Skirt segments equal the cells they span. Mobile
  `displacementFadeDistance` 350 -> 700. `CascadeFadeProbe` still reflects
  the component's private `cellsAcross` and `GetComponent<MeshRenderer>()`
  on `RingN` (now the parent) -- both read stale on the phone tier; fix
  when that probe is next used.
- **Foam runs on the cascade-1 grid** (`FoamAccumulate.compute`): 0.5 m per
  texel instead of 8 m, which was point-sampling the 32 m cascade at one
  sample per 64 of its texels -- aliasing noise, not folding, and 8-16 m
  trail blobs. The buffer tiles at 128 m now; `Ocean.shader` and
  `SampleJobs.cs` (`invPatch.y`) read it that way. The 5-tap blur spreads
  0.5 m a step instead of 8; judge on screen before widening it (a second
  tap ring, not reweighting). **Visual change -- Kevin's eye.**
- **`FoamNoise` is an integer hash** (was `frac(sin())`, up to 24 sin per
  water pixel) and the two unconditional calls sit behind a provable upper
  bound on `foamAmt`, so clear water skips them. **Visual change** in the
  foam's grain.
- **Two global keywords on `Ocean.shader`**: `_SEASICK_DEBUG` carries the
  six `_SS_*` dev uniforms and the 11-way channel chain; the shipped
  variant has none of them. `CrestProbe`, `SurfProbe`, `ShaderStrip`,
  `WeatherSheet`, `ShoalShot`, `WaterClarityTuner`, `SeaFoamTuner` enable it
  while they run. Last writer wins; a probe finishing while a tuner is
  ticked switches the tuner's mutes off until re-ticked. `_HULL_CLIP`
  carries the hull `clip()` (a discard makes the whole shader late-Z on
  tilers); `HullWaterClip.Push` drives it with `active`.
- Mist: `MistCeiling = 8` in code because `maxMist: 16` is serialised in
  Sea.unity; spawn band pushed to 1.0-2.4 x sampleRadius.

### Two things learnt gating Phase 4 (2026-09-12)

- **`CrestProbe`'s Hs 55 shaded shot shows a black hole with skirt curtains
  hanging in it.** It is NOT the quadrant clipmap: a stash bisect back to the
  pre-quadrant mesh gives the identical frame. The probe's camera sits inside
  a 55 m wave and photographs the ring skirts from below. The foam numbers
  (its actual purpose) are unaffected; the shot is a known limitation of a
  deck-level camera in a mountain sea. The quadrant bounds now carry a 100 m
  horizontal margin anyway (`HorizontalDisplacementMargin`): a footprint-tight
  box could cull a quadrant whose displaced vertices are still in view.
- **`_SEASICK_DEBUG` is refcounted** (`Dev/SeaDebugKeyword.Acquire/Release`).
  With seven owners and last-writer-wins, `CrestProbe` enabled the keyword,
  disabled the tuners for its run, and their `OnDisable` switched it off
  under it: "0 water pixels of 393216". Statics survive leaving play mode,
  so a `SubsystemRegistration` hook clears the owner set each session.
- **`CrestProbe.running` is a static that survives a stopped run**; the next
  launch is refused as "already running". Clear it by reflection (a scratch
  script) or finish the run. Its output file carries a "did not finish"
  sentinel from the first frame -- wait for the sentinel to go, not for the
  file to exist.

Foam after the grid change, `CrestProbe` (screen side, deck level): Hs 14
mean 0.011, 2.9 % of the water above 0.1 (was 0.035 / 6.6 % on 09-09); Hs 55
mean 0.15, 45 % above 0.1 (was 41 %). Finer trails, less milk. Kevin's eye.

## 2026-09-20 — the island loop's gates, and the god's-eye island

Two probes have gated the whole outpost loop since 2026-09-13 and were never
listed here; their reasoning lives in `docs/PLAN-island-outposts.md`. They are
listed now because four more joined them and somebody other than their author
has to be able to run the set.

| Launcher | What it answers | Mode | Output |
| --- | --- | --- | --- |
| `RunProbe.Ledger()` (`Dev/LedgerProbe.cs`) | **Is the camp's arithmetic the same however often it is asked?** Path independence over 1 / 240 / ragged calls, idempotence, per-resource ceilings, rock does not regrow, a worked-out seam stops below its ceiling, the sawyer / smith / farmhand conversions, and a JSON round trip including a half-built blueprint. Pure arithmetic, no scene — **`CallEditor`**. Run it first: it takes a second and everything else assumes it. | edit | `Logs/LedgerProbe.txt` |
| `RunProbe.Camp()` (`Dev/CampProbe.cs`) | **Press the button and watch.** Sails to a real island, lands, sites a blueprint from a ground pick, builds it with one hand over two game days, and then measures the ARTEFACTS: trees down in the mesh, hands ringed at 2.90 m, piles 5.2 m out, the sawmill chain, a shelter standing at the angle it was turned to, the view 35 m above what was sited, follow-a-crewman, leaving and returning. ~3 min. **The control for anything that touches `Outpost`, `IslandCam`, `CampSiting` or `CrewAgent`.** | play | `Logs/CampProbe.txt` |
| `RunProbe.IslandCam()` (`Dev/IslandCamProbe.cs`) | **Does the land stay in the hand?** An untouched view is still 32° / 165 m; the first touch does not pop; a grabbed point stays under the cursor across a 40 %-of-screen drag (at 165 m and 20 m of ground); zoom holds what it is aimed at; a 90° orbit keeps its pivot; the lens is never under the ground over an azimuth × zoom × transect sweep; the pivot stays in both reach discs; fly-to lands centred; a fling stops and a press kills it. **Prints which transform each streaming system follows** — the reach clamp is justified by `TerrainStreamer.target` being the ship, and that is a scene fact, not a code fact. | play | `Logs/IslandCamProbe.txt` |
| `RunProbe.IslandInput()` (`Dev/IslandInputProbe.cs`) | **The arithmetic of two fingers**, which is the only part of touch the editor can gate: a pure pinch has no twist, a pure twist has no zoom, shared vertical motion is found and opposed motion rejected, the tap / drag / long-press / double-tap classifier answers identically at 422 px and 2340 px of screen height, and the wheel round-trips. The gestures themselves need a device. | edit | `Logs/IslandInputProbe.txt` |
| `RunProbe.Hand()` (`Dev/HandProbe.cs`) | **Does every drop write the row it promised, in the frame it happens — and does D2 survive the Hand?** Drives only `Hand.PickAt/PickUp/Preview/DropAt/Cancel` with screen points projected from known world positions. Pick-up + cancel leaves the ledger JSON byte-identical; tree → Gather timber, prop → Gather that, sawmill → Work, shelter → refused with a reason, blueprint → Build, ship → recalled, an aboard hand on the ground → stationed; preview equals commit (**a frame apart** — inside one frame they share a memo and would compare a value with itself); 240 re-drops of one order across a day pay what one order pays; holding a man for half a day pays nothing; steady hover allocates nothing. | play | `Logs/HandProbe.txt` |
| `RunProbe.CampLife()` (`Dev/CampLifeProbe.cs`) | **Are they seen to work, and does it change no number?** A sawyer walks to his mill and stays at his trade; an order on one hand moves the others < 0.1 m that frame; a walking body faces the way it is going (the `ActBody` rotation fight); `Saw` while working and `None` at the door of a stalled mill; held → dangles and does not drift, put down → back to work within 2 s; clock paused, 30 s of villagers leaves the ledger JSON and `FelledInMesh()` identical — **with a companion gate that they actually walked**, so it cannot go green on a frozen camp. ~90 s. | play | `Logs/CampLifeProbe.txt` |

`Dev/IslandCamTuner.cs` is the look-and-feel half, for the reason `DockCamTuner`
was kept: **composition is a judgement.** Every `IslandCam.Feel` value on a
slider, a live readout, and *dump C#* writes paste-ready initialisers to
`Logs/IslandCamFeel.txt`. `IslandCam` stands aside for any open dev tool
*except* this one (`IslandCam.TunerAttached`) — otherwise opening the tuner
switches off the camera it is there to tune.

### Traps this pass added

- **Parallel agents and the editor do not mix.** Four subagents edited disjoint
  files at once and verified with `tools/compilecheck.sh`; the editor was left
  strictly alone until all four had landed, because one auto-refresh mid-edit
  compiles a half-written tree and the domain reload kills whatever was
  running. Contracts first (compiling stubs with exact signatures), then the
  streams, then Unity.
- **A shared file is a shared namespace of locals.** The one compile break of
  the pass was a `waited` added to `CampProbe.Run()` colliding with a `waited`
  600 lines above it in the same coroutine — found by another stream's
  compile check, not by the author's.
- **A gate that measured a teleport fails the day the teleport is fixed.**
  `and-goes-and-stands-there` read the sawyer's position one frame after
  `Assign`, which was only ever true because every order write snapped the
  whole camp to its spots. He walks now; the gate waits for him, thresholds
  unchanged. When behaviour improves under a gate, check what the gate was
  really observing before calling it a regression.
- **`Attach` on every order write must not re-seed state.** `PuppetsToWork`
  runs after each order and `CampWorker.Attach` used to reset `home` to where
  the body stood — discarding the spot `ArrangeHands` had computed one line
  earlier. Only a fresh worker is seeded.
- **`direct` cannot switch on at "blend > 0.98".** The overview blend is an
  exponential; at 0.98 the rendered seat is still metres short of the computed
  one, so skipping the low-pass there is a visible jump on first touch. The
  rig snaps the blend to 1 past 0.98 and `direct` waits until the rig has
  actually arrived.
- **The input convention is the code's, not CLAUDE.md's.** No script uses the
  `.inputactions` asset; all read `Keyboard/Mouse/Pointer.current`, and
  `IslandInput` reads `Touchscreen.current` raw. Note `TouchPhase` is ambiguous
  between `UnityEngine` and `UnityEngine.InputSystem` — qualify it.

## 2026-09-21 — the save

### `SaveProbe` (`RunProbe.Save`)

Play mode, `Sea.unity`, `Logs/SaveProbe.txt`. Builds a rung, a fitting,
bays, a hold, stores, and a camp on the nearest beach (store hut at a chosen
yaw, a sited hut, two hands on two orders, three felled trees); writes it to
a temp file through `SaveGame.SaveTo`; wipes the live scene back to a fresh
boot; runs the same `SaveGame.Restore` the CONTINUE button runs; compares
every field. Gates are named for the thing that must be equal
(`a-saved-camp-comes-back-where-it-stood`, `no-phantom-backlog-was-paid`).
It never touches the player's own file.

### The New / Continue overlay, and every other probe

`GameBoot` freezes the game with `Time.timeScale = 0` until somebody picks.
`RunProbe.Call` invokes `GameBoot.Skip()` by reflection before every play-mode
probe, so a probe finds the world running on a fresh voyage and autosaves
are off for the session (`SaveGame.Suppressed`). **A probe launched any
other way** (an editor script calling `Execute` directly) will sit frozen
until it calls `GameBoot.Skip()` itself, or sets `GameBoot.Interactive =
false` before play.

### Traps this pass added to the pile

- **`JsonUtility` cannot say null.** A null class-typed field comes back as
  a default-constructed object. `OutpostLedger.pending` is one: empty, it
  has `needed == 0`, which reads as complete, which raises a campfire nobody
  sited. `Outpost.Adopt` re-nulls it. Any new nullable row needs the same.
- **`WorldSettings.seed` was 0**, and everything that read the world through
  `UnityEngine.Random` re-rolled per launch while looking perfectly stable
  inside one session. A determinism claim has to be checked across two
  launches, not two frames.
- **`ShipHold` and `Stockpile` do not follow the numbers.** Both are manual
  stacks; a restore rebuilds them unit by unit.
- **A restore must not re-ask a SITING rule that reads state the load has
  not restored yet** (2026-09-27, "I have to re-build my dry dock every
  time"). `SaveGame.Apply` adopts the camps (step 5) BEFORE it re-points
  `Dock.Home` at the player's pier (5b); `Adopt` re-raises every saved row
  through `CanPlace`, and `CanPlaceDryDock`'s "within 40 m of `Dock.Home`"
  was answered against the harbour -- every dry dock was DROPPED on load,
  its `raised` row with it, while `built` kept counting it (four in Kevin's
  save). `Outpost.adoptingRows` now skips that rule for saved rows, and
  `ReconcileSpecialRows` drops `built` entries nothing stands for (piers and
  dry docks have no spiral fallback), because the copy cap counts `built`.
  Anything new that `CanPlace` reads from a global (home berth, fire level,
  a tech) must answer the same during `Adopt`, or be skipped there. The
  `Outpost.Adopt: ... DROPPED` warning is the tell -- grep for it after a
  load.

## 2026-09-22 — the game on an iPhone

### `tools/build-ios.sh [--dev]`
Headless iOS player. Runs `SeaSick.Dev.Build.IOS` (`Scripts/Dev/Editor/Build.cs`)
in batchmode and writes an Xcode project to `Builds/iOS/` (gitignored), in
APPEND mode so the signing team Kevin picked in Xcode survives a rebuild.
Then in Xcode: `open Builds/iOS/Unity-iPhone.xcodeproj`, select the phone
next to the scheme, Run. `--dev` makes a development player (the on-screen
Development Console, profiler attach). Refuses while the editor is open —
batchmode cannot share the project.

To read the phone's log from the Mac (this RELAUNCHES the app, and killing
the capture closes it, so say so if Kevin is holding the phone):
`xcrun devicectl device process launch --console --terminate-existing --device <udid> com.kevinandersson.seasick`,
udid from `xcrun devicectl list devices`.

### Traps this session added to the pile
- **Unity exits 0 without doing anything.** After the macOS 27 update Rosetta 2
  was gone and the editor logged "Canceling DisplayDialog: Rosetta 2 isn't
  installed" and returned success. The script checks that the `.xcodeproj`
  exists, not the exit code. `softwareupdate --install-rosetta` is Kevin's to run.
- **The update also deleted `Library/`.** First batchmode run reimports
  everything (~15 min); `tools/compilecheck.sh` cannot run until it has, it
  needs `Library/ScriptAssemblies`. Package compile errors about
  `UnityEditor.GUID` on that first pass are the API updater doing its job and
  go away on the second Bee pass — read the LAST "Tundra build failed" block,
  not the first.
- **`Shader.Find` returns null in a player.** A build only ships shaders some
  built asset references. No material in the project used the URP particle or
  unlit shaders, so SpeedJuice, StormSpray, Cannon, SelectionRing... all did
  `new Material(null)` in Start and then threw every frame — the red console
  on the first phone build. Fix: `Resources/Shaders/Keepalive/` holds one
  do-nothing material per (shader, keyword set) the code enables at runtime;
  its README says when to add one. Keyword variants matter as much as the
  shader: `_ALPHATEST_ON` on the cutout material is what keeps the round
  spray from coming back as squares.
- **The steamer is a PlayerPrefs switch.** `SteamerBootstrap.Selected` defaulted
  to the ladder ship, so a fresh device sailed the brig. It now defaults ON
  outside the editor; the editor menu still opts in.
- **Throttle on a phone is the boat stick** (2026-10-03, DREDGE controls; was the telegraph arrows, then a latched stick): up/down = speed, release = engine off. See "Sea controls (2026-10-03)" below.
- **iOS kills an app whose main thread blocks for 10 s while it backgrounds**
  (0x8BADF00D "scene-update watchdog", 2026-09-25: the phone auto-locked
  during the one-frame world build). `TerrainWorldPopulator` now builds in
  `frameBudgetMs` (50) slices -- flood fill, shoreline marches, then island by
  island with `IslandScenery.BuildSliced` stopping between scatter rows -- and
  swaps its own `Random.State` around each slice so the islands are identical
  (fingerprint A/B vs the one-frame build: same hash). Editor: worst frame
  13-16 s -> ~0.85 s (home island's dock + village siting), world ready after
  ~25-30 s wall instead of ~16 s. The log line `TerrainWorldPopulator: N
  islands in S slices, work W ms, max slice M ms (phase)` is the number to
  read on the phone. Consequences: `Done` comes seconds after play starts, so
  anything waiting on it needs a real-time wait of 60-90 s (WorldProbe had
  10 s of scaled time); `SaveGame.Restore` waits with no clock while the
  build runs (`Failed` ends it), because a clock timeout there fell through to
  NEW and the first autosave would eat the save. `frameBudgetMs = 0` gives the
  old one-frame build for an A/B.


## 2026-09-23 — station stock: bays, benches, racks, orders, armful hauling

### Plain-C# self-tests without the editor: `tools/selftest-outside-editor/run.sh` (2026-10-04)

When another session holds the editor, or to find WHEN a gate broke:

```
tools/selftest-outside-editor/run.sh SeaSick.World.StationStockSelfTest.Run SeaSick.Ship.GunCrewShift.SelfTest
tools/selftest-outside-editor/run.sh -c 0edd86c9 SeaSick.World.StationStockSelfTest.Run   # any commit
```

It compiles Assembly-CSharp with Unity's Roslyn, the editor build's defines and
the Library reference DLLs (the `compilecheck.sh` recipe) from the working tree
or from `git archive <commit>` (`.cs` files only), then runs each named static,
argument-less method on Unity's bundled .NET (`Host.cs`). Pass = `true`, or a
string starting `PASS` / ending `ALL PASS`; exit code = failed methods, 2 = the
build failed. ~10 s per run. Works from a worktree (borrows the main Library).
Static state is fresh each run, like a domain reload.

Limits: managed code only. `JsonUtility` is replaced by the managed stand-in
`tools/modular-selftest/JsonShim.cs`; `Debug.Log` prints to stdout;
`Resources.Load` throws and the `*Tuning` getters fall back to code defaults
(the same numbers when the asset is missing). Anything needing a scene, a
ScriptableObject instance or a native module reports `THREW` -- run that one in
the editor. `-c` builds old sources against TODAY's package DLLs, so commits far
back can fail to build.

Used 2026-10-04 to bisect StationStockSelfTest: green at 0edd86c9, 17 fails
from 8099268f (the day/night ladder: ledgers ticked from second 0 slept until
01:00, then worked at 1.2x), plus stale expectations from c6e38515 (store
top-up), 734b3dc1 (0.75 m/s off-screen walk), 3485b85c (hunger floor 0.8).
Fixed to 93/93 on branch stationstock-green; the run now holds the camp awake
at scale 1 (`CampLifeTuning.OverrideForTest`, test-only).

Seen while tracing it, not changed (no rule broken): the builder processed first in a
quantum can start a ~50 s store top-up trip in the same quantum his partner's
last stone lands, because his "anything left to fetch for the site?" check runs
before that delivery. The site is then hammered by one builder while he walks.
Watch for it if Kevin reports builders wandering off a just-stocked site.

### `StationStockSelfTest.Run()` (`SeaSick.World`, plain C#, edit mode, no scene)
Gate for the storage-hub rules in `OutpostLedger.Stations.cs`'s doc block,
`StationStock.cs`, and — since the same day's later phone session — the
walked-distance trip timing in `OutpostLedger.cs` / `OutpostLedger.Stations.cs`:
the store (fire-square or storage building) is the hub, every gathered unit
goes to it, a production station owns real bay/bench/rack stock, nothing on a
bench works without a player order, a build site stocks whole armfuls capped
at need before a hammer swings, and every trip — station haul, site delivery,
plain gather — costs the walk it actually takes, not a flat rate. Builds bare
`OutpostLedger`s (a quarry, a build site, or gatherers, each given an explicit
store/source/site position so its trip times are known) and ticks them through
`Tick`, the same door the game uses. Run via
`unity cmd eval --json --code 'return SeaSick.World.StationStockSelfTest.Run();'`
(namespace confirmed against the live source — `SeaSick.World`, not a bare
`SeaSick` guess). Returns true / logs `ALL PASS` when every gate holds; logs
an error report naming each failed gate otherwise.

**~68 gates**, grown from the original 11 across three sessions (station stock
→ build sites → trip timing / gather trips / clearing) — too many to list one
by one here; the source's own doc comment above `Run()` carries the exact
gate list, lettered `(a)` through `(l)`. By section:

- **Stations.** Orders (`PlaceOrder` / count / repeat / stop) accepted and
  respected; D2 for hauling, not just build labour (ten 1-day ticks = one
  10-day tick = nine uneven ticks, within 1 brick of each other); conservation
  (stone anywhere + bricks anywhere sums to what was put in, every tick); bay
  and rack capacities never exceeded even backed up against a low store
  ceiling (store full → rack fills → bench blocks, `BenchState.Finished`
  rather than discarding output); a repeat order running past any count until
  stopped, and at most the job already on the bench finishing after the stop;
  a hand removed mid-haul (`RemoveHand`) putting its armful down rather than
  losing it; demolishing the first of two same-plan stations (the survivor
  keeps its own stock and becomes ordinal 0, the dead row empties and reads
  `!IsLive`); `Take` / `SpendableOf` drawing store, racks and finished
  benches but never a bay (`CountOf` still shows the bay); and the store's
  ceiling holding while a gatherer and a hauler both fill it at once, every
  unit accounted.
- **Build sites.** A 5-timber/3-stone/2-builder site, from a stocked store
  and from bare ground (cut-and-quarry-it-yourself), at three tick sizes:
  delivered + in-arms never above the cost (no over-delivery, ever); the
  shown counts are the true whole delivered units (no fractional pours);
  `Progress01` / hammering stay at 0% until every material reads full AND
  the plot is cleared; every unit conserved; at most 4 trips for 5 timber
  (armful 2) + 3 stone (armful 3); and **D2 across several tick sizes** —
  0.3/1/4-day spans, each compared fine (0.1-day steps), coarse (one tick)
  and ragged. A separate gate replays an **old save's over-delivered row**
  (8/5 logs, 4/3 stone, a stray fractional `donePart`) and checks the
  surplus goes back to the store on load instead of staying phantom stock.
- **Trip timing.** A 100 m store↔site trip books exactly the walked-distance
  formula (`2 × leg × PathFactor / WalkMetresPerSecond + HandleSeconds`); a
  cutting trip adds `n × Playtest.CutSecondsPerLog` on top; and — the gate
  Kevin's rule actually predicts — **twice-as-far-twice-as-long**: a site
  200 m out takes about twice the single trip's time and about twice as long
  to stock as one at 100 m (measured on the 0.1-day quantum, so the gate
  allows 1.7–2.1×, not exactly 2×).
- **Gather trips.** A plain Gather order booked as a timed trip (2 logs in
  the same formula's seconds, not a flat daily rate); D2 at 1.0- and
  1.7-day spans (fine/coarse/ragged); two gatherers never pushing the store
  past its ceiling with every log accounted for; and the full-store
  behaviour — a gatherer's stall reason says the store is full, and he
  either helps build (a site queued) or hauls for the stations (a manned
  station wanting the same resource) until there's room, then resumes
  gathering on his own.
- **Clearing.** Two trees off a build plot cost 10 s of builder time (5 s
  each), one rock costs 8 s, and the rest of an 18 s quantum still goes on
  building once the plot reads cleared.

**`RunProbe.Ledger()`'s gates were re-baselined the same session** for
trip-timed gathering (`LedgerProbe.cs`): the old flat
`Res.GatherRate` / `TimberPerHandPerDay` per-day accrual is gone from the read
side too, so "logs a hand gathers a day" now reads `GatherTripPerDay(res)` (an
armful per walked trip); the depletion gate's "but-not-instantly" floor is
derived from trip count (`rounds - 1` armfuls at the trip's own seconds)
rather than a hardcoded day count; and the sawmill gate now measures against
`timberTaken` — logs that actually left the ground — instead of a rate bound
that trip-timed gathering had made loose enough to pass with nothing sawn.

## 2026-09-27 — delivery on arrival: trips are walked (docs/DELIVERY-ON-ARRIVAL.md)

**The trip-timing bullets above are HISTORY.** Kevin, 2026-09-27: no time or
equation in carrying; a load counts only when the villager has delivered it.
There is no trip timer any more (`haulLeft`/`haulDays`/`haulWalkDays`/
`haulWorkDays` are dead fields kept for old saves; `PathFactor` survives only in
display estimates). New invariants every probe/self-test must respect:

- **Stock changes only at PICKUP and DROP-OFF events.** A source (store pile,
  rack, bay, standing stock, the herd, the ship's hold) loses the load when the
  walker FINISHES the work at it (`FinishPickup` → `PickUp`, clamped to what is
  really there); the destination (store, bay, site, hold) gains it when the
  walker ARRIVES (`DepositHaul`). In between, `haulPicked` says it is on him.
  A load he is still walking out to fetch is only PLANNED: it reserves room at
  the destination (`InFlightTo`, `RoomFor`, `NetShort`) and is a claim on the
  source (`Claimed` / `StoreFree` / `FieldFree` / `RowFree`), but no count moves.
- **`CarriedOf(res)` counts picked loads only.** Conservation checks are
  `source + CarriedOf + destination` — a planned load is still in its source.
- **Stationary work keeps a timer**: cutting at the source (tuning asset
  seconds per unit), the 1 s pickup stoop, the spear jab (`JabSeconds`), the
  bench recipe, clearing, hammering. Clearing and hammering and the bench run
  only while the hand is AT the site/bench (`WalkTo`; a driven body within
  `OnSiteMetres`).
- **Walking is not scaled by hunger.** `WorkFactor` scales stationary work only;
  `walkingIn` no longer zeroes it.
- **Who walks**: a watched camp's `CampWorker` (`OutpostHand.driven`, set by
  `BodyAt` every frame, cleared by `BodyReleased` in `OnDisable`); otherwise the
  ledger's invisible walker in `Step`, at 2.6 m/s along the leg's `CampPath`
  length (`OutpostLedger.router`, set by `Outpost.CatchUp`; straight line when
  there is no grid). `Tick` still advances whole quanta, so one long tick and
  many short ones still land on the same books for an UNWATCHED camp (D2 holds
  there); a watched camp is paced by its bodies and is not bit-comparable.
- **Measured vs forecast.** `DeliveredPerDay(res)` is measured (moving average
  of real store drop-offs, not saved). `RatePerDay`/`MakeRatePerDay`/
  `GatherTripPerDay`/`HuntTripPerDay`/`TripDays` are forecasts for the sheets
  and must never book anything.

**Traps.** (1) A ledger with no centre and no placed ends walks
`DefaultLegMetres` legs — give test ledgers `SetCentre`. (2) A hunt is a claim
on the herd count (`GameUnclaimed`), not on a `Claimed` source; the kill IS
the pickup. (3) Re-ordering a hand mid-trip no longer lands his load: whatever
pass owns him now walks it there; only `RemoveHand` / station teardown still
force a picked load into the store (it exists), and a planned load is simply
cancelled (`CancelPlanned`). (4) `StationStockSelfTest` (all sections) and `LedgerProbe.Execute()` were
run ALL PASS on 2026-09-27 OUTSIDE the editor — the runtime assembly compiled
with Unity's csc and run on Unity's bundled `dotnet` (8.0) with a console
`ILogHandler`, JsonUtility round trips stubbed to identity (JsonUtility is
native; the save gates still need the editor). Pure-ledger code runs fine that
way; anything touching the scene, `Resources` (caught in `EconomyTuning`) or
`JsonUtility` does not.

New gates in `StationStockSelfTest` (o): `store-grows-only-at-dropoff` (a busy
camp stepped per quantum for 3 days; no step's store growth exceeds that
step's drop-offs) and `watched-vs-unwatched-one-day` (the same two-cutter camp
ticked alone vs driven by stand-in bodies walking 0.1 s frames; timber within
20 % / 4 logs).

### Edit-mode render check for a villager's pose or props
No play mode and no dedicated probe file needed for a one-off look at what
`VillagerActing` does to a body. Instantiate the `CrewMember` prefab
(`Assets/_Project/Prefabs/CrewMember.prefab`) into a scratch edit-mode scene,
get-or-add `VillagerActing`, call `Set(mode, carrying, count)` for whatever
pose or armful you want to check, then **drive the rig by hand** — edit mode
never calls `Update`/`LateUpdate` on its own, so `Animator.Update(dt)` and
`VillagerActing`'s own per-frame bend (a private method; call it by
reflection, `BindingFlags.NonPublic | BindingFlags.Instance`) both need
pumping manually, about 20 frames at a small `dt` so the Idle/Walk clip
settles and the bend has a written pose to bend. Point a throwaway `Camera`
at the body, render to a `RenderTexture` (the same route every other shot
tool uses — see `ShaderStrip.cs` above), read it out and write the PNG into
`Temp/`, then **delete the camera, the render texture and the instantiated
body** — nothing this makes should survive the call, or the next one finds a
leftover villager standing in the scene.

**The trap: Astra's rig is not drawn at 1:1.** The deckhand's bones carry
roughly a **92× scale** baked in (the art pipeline's unit conversion was never
reconciled with Unity's metre), so anything that reads a bone's local
position as metres — a hand tool's offset, a crouch amount, any prop size
inferred from the rig — is off by two orders of magnitude unless it is
explicitly converted through the bone's own scale first; `cbe3930` fixed
exactly this for hand tools (axes the size of trees) and the crouch offset
(0.26 m read as ~24 m). And the rig's two clips (`Idle`, `Walk`) are
**limb-only** — baked sine tracks over `root, hips, chest, head, arm_L,
arm_R, leg_L, leg_R`, no spine/pelvis/chest detail of their own — so a render
meant to check a torso bend or a head pose is checking `VillagerActing`'s own
bone layer, not the Animator clip; a bone the bend never touches keeps
whatever the LAST frame before the render happened to write to it, which is
why the frame count and settle time above matter (`VillagerActing.cs`'s own
doc comment: "any bone left exactly as we wrote it is restored before the
next bend").

## Working alongside Astra (2026-09-24)

Two sessions build this game at once: Astra (art, and from 2026-09-24 the
UI) and the systems session. The editor is ONE shared resource; git is
not. The rules that keep them from tripping each other:

- **Files.** Astra owns `Assets/_Project/Scripts/UI/**`, her art under
  `Assets/_Project/Art/AstraPlaytest/**` and `Art/Ship/SternPaddleAstra*`,
  the `Resources/Settlement/*_astra.prefab` / `Resources/Palisade` /
  `Resources/Pier` wrappers, and `art-staging/` + `tools/blender/`. The
  systems session owns `World/`, `Ship/`, `Steamer/`, `Ocean/`, `Dev/`,
  `Save/`, `Combat/`, `Terrain/`. Neither edits the other's files; a
  value the UI needs from the economy is read through the ledger's public
  getters (`OutpostLedger.Stations.cs` doc block) and a missing getter is
  ASKED for, not added from the UI side.
- **The editor.** Only one session drives Unity (compile, play, capture,
  build) at a time. The other writes code in its own `git worktree` and
  does not compile until the editor is handed over. Phone builds
  (`BuildPipeline.BuildPlayer` from the open editor, see "the game on an
  iPhone") are the systems session's; a recompile mid-build breaks it.
- **Git.** Small commits on `ships-into-unity`, each after a clean compile
  (`unity cmd console_status --json`); pull before starting, commit before
  handing the editor back.
- **Sheet facts the UI side needs.** Every building sheet is one template
  (`UI/Sheets/StationSheet.cs`): worker slot → what's coming in → what
  it's making + the ORDER (recipe rows, ∞/5/10/20/50/100 chips, STOP) →
  what's going out → why it's stopped → upgrade, poured into band-high
  pages (`SheetHost.BandHeight`); `FarmSheet.cs` is the same frame without
  chips. Buttons are built once and re-texted on the 0.25 s refresh (a
  rebuilt element loses the tap it was in the middle of). The HUD's "44"
  is PANEL UNITS: ~2.05 game-view px per unit at 1080x2340, so a 44-unit
  button is ~90 px ≈ 30 pt on a 3x iPhone; a true 44 pt is ~65–70 units
  and roughly halves what fits on a page. Kevin has said the buttons will
  change. `WorldPicker` tries every raycast hit nearest-first (scenery
  trees carry ~15 m trigger boxes); a building's tap sphere comes from
  `Pickable.EnsureAll`.

### Sea controls (2026-10-03)

DREDGE-style sailing input (docs/PLAN-dredge-controls.md; the old tap-to-sail `SailTo` seam, the `TouchHelm` stick and the telegraph are all deleted).

- **Where the knobs live** (all on the phone's FEEL panel): `Ship/SailControlTuning.cs` (boat stick: zone, ring radius, dead zone, lift grace, turn / throttle curves, reverse cap, key ramp, boost idle), `CameraRig/SeaCameraTuning.cs` (look rates, dead zone, pitch limits, recenter, follow lag, double-tap), `Ship/BoostTuning.cs` (boost punch: FOV kick, shake, haptic, surge).
- **Settings → CAMERA** is PlayerPrefs, not the save: keys `seasick.seacam.follow`, `.sens`, `.invx`, `.invy` (`CameraRig/SeaCameraPrefs.cs`).
- **Smoke-test trap (a): keyboard events from eval only land with Game-view focus.** `InputSystem.QueueStateEvent` on the Keyboard does nothing once the editor loses focus (the editor disables the Keyboard device on focus loss). Drive `HelmInput`'s `testRudder` field (or its stick state) and `ChaseCamera`'s `userYaw` / `userPitch` by reflection instead.
- **Smoke-test trap (b): `capture_game_view` returns 1280x720 and has no IMGUI / UITK HUD in it.** To check that the ⚡ button and the order strip are where they should be, read `SeaHud.BoostRect` / `SeaHud.HelmRect` instead of the picture.
- **Trap (c): git 2.23 + sparse worktrees.** Never run `git config core.sparseCheckout` (it writes the SHARED config and flips every worktree and the main checkout); use `git -c core.sparseCheckout=true ...` per command.

### Bow harpoon (2026-10-04, phase 1)

The ship's hook and reel (docs/PLAN-harpoon.md; GDD §6 "The bow harpoon"). The code is in `Ship/Harpoon/`: `HarpoonGun` (state machine, line physics, the pull), `HarpoonLine` (the rope), `HarpoonMount` (fitting/barb art or stand-ins, bow-stem placement), `HarpoonRegistry`, `IHarpoonable`, `HarpoonTypes` (crew seam). The UI is lane B's: `SeaHud` 🪝 button and `HarpoonMarkers`.

- **Where the knobs live:** `Ship/Harpoon/HarpoonTuning.cs`, on the phone's FEEL panel. They cover reach/arc, wind-up, barb speed/arc/bite, lead error, the winch (speed, ease, spring, damper, water drag), the tension bands, snap tension and hold, reload, the pull on the ship, captain rate and walk wait.
- **A target on demand (editor eval, play mode, at sea):** `unity cmd eval --json --code 'return SeaSick.Ship.Harpoon.HarpoonGun.DevSpawnTargetAhead("crate", 22f);'`. The kinds are `crate` (2 timber), `heavy` (4 stone, mass ~2: the one to snap), `loot`, `flotsam` and `bottle`. Then fire with `SeaSick.Ship.Harpoon.HarpoonGun.Player.FireOrCut()`, and read `.State`, `.Tension01`, `.Band`, `.LastEventWord` and `.HoldFull`. Castaways only come from `CastawaySpawner` (its Commit only accepts its own `current`), so they cannot be spawned from here.
- **Delivery is the target's own `IOverboardTarget.OnHauled`**, with `BeingHauled` held true for the whole reel. That keeps `RescueHud`'s sail-over and the jolly boat off a hooked load. The gun moves the load kinematically and parks `HaulAnchor` on the same spot, so the target's own `MoveTowards(HaulAnchor)` is a no-op whichever Update runs first.
- **Trap: a destroyed target behind an interface.** `IHarpoonable.Transform` on a destroyed MonoBehaviour throws instead of returning null. Test with `HarpoonGun.Gone` (it casts to `UnityEngine.Object`), never with `t.Transform == null`.
- **Trap: the servo hull cancels a raw force by the next step.** `ShipMotor.ExternalPull` is read as a lower target speed plus `ExternalPullYaw` on the servo path, and as a real force at `ExternalPullPoint` (at centre-of-mass height, so it never heels her) in `PaddleDrive`.
- **Not harpoonable in phase 1:** `SalvageSpawner`'s timber-crate clusters (plain transforms with their own 6 m sail-over, not `IOverboardTarget`).

## 2026-09-27 — the economy tuning file and its FEEL dials

**Where every camp number lives:** `Assets/_Project/Settings/Resources/EconomyTuning.asset` (`World/Economy/EconomyTuning.cs`). Plan prices + hammer seconds, fire and upgrade prices, rate multipliers, copy caps, per-copy step, recipes, cut seconds, rock yields, meat/hide, spear life, warmth. The live multipliers are `World/Economy/EconomyFeel.cs` statics, registered in `Dev/FeelLab.cs` (`TypeFullNames` + `Ranges`), so they show on the phone's FEEL panel. GDD §6 "Economy numbers" has the table and the reasoning.

Traps:
- **The asset wins over the code.** The static tables (`BuildPlans`, `Techs`, `Recipes`) keep the same numbers as defaults, but a row in the asset overrides its code value. Change a price in the ASSET (or change both); a code-only edit is silently overridden. After a code-side change, right-click the asset → *Capture tables from code* to rewrite its tables.
- **Tables are pushed once**, at `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` (`EconomyTuning.EnsureApplied`). Editing fire/upgrade/cap/recipe rows during play does nothing until the next play; plan prices and hammer seconds are read per call and do follow. `.asset` edits made outside the editor also need `AssetDatabase.Refresh` (see "Asset edits need refresh").
- **FEEL's saved values beat the asset.** FeelLab restores its PlayerPrefs on startup, so a meat/hide number set on the phone persists over a changed asset until FEEL → Reset.
- **Edit-mode tools see code defaults** (no play → nothing pushed), which is why the code and the asset must stay in step; `RecipeGraph.Validate` runs on the code tables.
- **The site timer is hammer-only.** `PendingBuild.built` accrues only in `OutpostLedger.PayBuild` (stocked + cleared + a Build-order hand not walking a load), scaled by `EconomyTuning.CrewSpeed(N)`. `OutpostLedger.SiteLine` / `WhoIsOn` (`OutpostLedger.SiteCrew.cs`) are the site sheet's words; they READ the hauling fields on `OutpostHand` (`Hauling`, `haulRes`, `haulCount`, `haulFrom`, `haulTo`) and change nothing.

## 2026-10-01 — buildings are obstacles (`CampPath.Solids`)

- **`BuildingSolidsBake.Run()`** (`Dev/Editor`, menu SeaSick/Dev/Bake Building Solids): rasterises each kit prefab's named walk-blocking parts (below 1.3 m, 0.25 m cells, greedy rectangles) into `World/BuildingSolids.Baked.cs`, keyed by `BuildPlan.prefab`, and prints every access marker's clearance (`Temp/solids-bake.txt`). Code, not prefab data, so an importer re-run can't drop it; **re-run it when a building model changes shape** (or a new kit building arrives: add its part stems to `Table`). Unbaked buildings block their footprint less 0.2 m.
- **`BuildingSolidsProbe`** (play mode, camp in view): `Snapshot()` (buildings vs ledger rows, bodies inside a box), `Map(png)` (grid + boxes + lanes picture), `Walk(planId)` (borrows a villager and walks the REAL `CampWorker.Walk` to every marker of every building of that plan, counting arrivals and steps inside a box). 2026-10-01 on Kevin's Day 206 camp: 44 legs over 10 building types plus 7 with the mill turned 90°, all arrived, 0 steps inside a box.
- **`WalkSlipProbe`** (`Scripts/Dev`, play mode, 2026-10-01): foot skate + turning. `Begin()`, wait, `Report()`: per locomotion state the STANCE foot's horizontal speed (lower foot bone, planted, in the body's parent frame so a deck counts as ground) mean/p50/p95, body speed and playback rate, sideways speed vs facing, yaw rate, and the first 90+ degree turn as a trace. `Drive(n, "WalkBrisk"|"Walk"|"WalkTired"|"Run"|"RunScared"|"Carry")` borrows n villagers (CampWorker off) and walks them round a 6 m/5 m triangle with the real `CampWorker.Walk`; `Release()`/`End()`. Before/after the planted-feet walk (Kevin's Day 307 camp): brisk slip mean 0.93 -> 0.07-0.14 m/s, carry 2.23 -> 0.20, sideways p95 2.0 -> 0.002 m/s, the same at the 1.5x cadence. Gait speeds live in `World/VillagerGaits.cs` (measured clip speeds x `Cadence`). Trap: editing a .cs while playing with the editor focused recompiles mid-play and wipes the probe's statics -- stop play first.
- Trap: `capture_game_view --save_path` must be inside the project and lands under `Assets/` -- move it out and delete `Assets/Temp` + `.meta`.
- Trap: Continue loads `SaveSlots.ResolveActivePath()` (the remembered active slot, often `m1`), not the newest file -- copy the save you want over every slot name in the override dir.
