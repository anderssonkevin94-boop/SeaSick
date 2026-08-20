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

## Probes

| Probe | Measures | Output |
| --- | --- | --- |
| `FFTUnit.cs` | The Stockham IFFT core against analytic sinusoids (known modes in, sinusoid out to 1e-5). Edit mode, no play needed. | `/tmp/seasick-fftunit.txt` |
| `SpectrumProbe2.cs` | The GPU sea against **oceanography**: Hs from field variance and Tp from a temporal PSD vs analytic JONSWAP integrals, for the three canonical (wind, fetch) triples. Edit mode. Slow (~2 min). | `/tmp/seasick-spectrum2.txt` |
| `ClipmapProbe.cs` | Vertex swim (re-anchors every ring under a frozen sea; pixels must not move) and altitude tiling shots. Play mode, OceanLab. | `/tmp/seasick-clipmap.txt`, `-tiling-*.png` |
| `DivergenceProbe.cs` | **The load-bearing gate**: rendered surface vs CPU sampler at 1000 points, five frozen instants, storm λ=1.2 — must be < 5 cm (measured 0.23). Also batch cost. Play mode, OceanLab. | `/tmp/seasick-divergence.txt` |
| `BuoyProbe.cs` | Proxy sloop: 60 s storm free-float (roll/rails/draft/capsize) + calm 2 m drop settle time. Play mode, OceanLab. | `/tmp/seasick-buoy.txt` |
| `BlendProbe.cs` | Calm→storm weather ramp smoothness (Hs every second; steps mean rebuild pops). Play mode, OceanLab. | `/tmp/seasick-blend.txt`, `-blend-*.png` |
| `SailShot.cs` | Sails the western deep in `Sea.unity`: draft statistics, camera clamp rate, camera/hull gap, roll/pitch, speed. Compare `/tmp/seasick-sail-baseline.txt` (the old kinematic system's final run). | `/tmp/seasick-sail.txt`, `-0..4.png` |
| `SprayDebug.cs` | Per-second spindrift emission budget log. | Unity log |
| `LoadProbe.cs`, `HudShot.cs`, `FogTest.cs`, `WarpOut.cs` | Older, still valid. | `/tmp/seasick-*.txt` |

(The old-ocean probes — SpectrumProbe, HeaveProbe, SeatingProbe, RegionProbe,
MountainProbe, HeadingProbe, StormMeasure/Shot/Picture, MatCheck, VoidTest,
ApplyWaveShape, AddMountainSeas — died with the Gerstner stack.)

`tools/pngprobe.py` prints mean sRGB for horizontal bands of a screenshot:
`python3 tools/pngprobe.py /tmp/seasick-sail-2.png`. Pure stdlib.

## Measurement traps this project has actually hit

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
- **`OceanSampler.SampleImmediate` is for one-shot, low-rate queries** (camera
  clamp, splash tests, random scatter like StormSpray). Anything continuous —
  floaters, enemy hulls, hull probes — belongs in `OceanProbeRegistry` or a
  `BuoyancyProbeSet`, which the physics driver folds into ONE batched Burst
  query per FixedUpdate.
