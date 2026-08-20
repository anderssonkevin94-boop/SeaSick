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
| `SetupStormSky.cs` | Sky material + render settings, `SkyDirector`, `StormSpray`, the **ocean's storm palette on the material asset**, and targeted pushes for `ChaseCamera`, `SpeedJuice` and `ShipMotor` seating. Targeted, not blanket, where a component has hand-tuned scene values. |
| `ApplyWaveShape.cs` | `WaveField`'s whole spectrum, by full reset. Reports what landed. |

## Probes

| Probe | Measures | Output |
| --- | --- | --- |
| `SailShot.cs` | Sails the deep. **Hull seating error**, camera clamp rate, camera/hull gap, surface vertical speed. Takes 5 screenshots. | `/tmp/seasick-sail.txt`, `-0..4.png` |
| `SpectrumProbe.cs` | Per-wave wavelength/direction/steepness/amplitude, plus **row-ness** (across-wind slope ÷ along-wind — 1.0 is a noise field, 0 is corrugated) and **crest skew**. Averages 32 lines and samples 160² with the accurate height sampler. | `/tmp/seasick-spectrum.txt` |
| `HeaveProbe.cs` | Hull heave, pitch and roll at two distances. **Pins the sea state** so runs are comparable. | `/tmp/seasick-heave.txt` |
| `SeatingProbe.cs` | The inverse-displacement solver against a converged damped solution. Ship pinned. | `/tmp/seasick-seating.txt` |
| `StormPicture.cs` | Sky/light/fog/spray state, and a back-to-back camera A/B normalised per metre of heave. | `/tmp/seasick-stormpicture.txt` |
| `MatCheck.cs` | Reads back what the ocean material and the `_SS_*` globals **actually** hold at runtime. Use whenever a shader change appears to do nothing. | Unity log |
| `VoidTest.cs` | Tints `_StormDeep` / `_StormShallow` / `_StormCrest` one at a time with the ship pinned, to find which term paints a region. **Currently unreliable — see below.** | `/tmp/seasick-term-*.png` |
| `SprayDebug.cs` | Per-second spindrift emission budget log. | Unity log |
| `MountainProbe`, `RegionProbe`, `LoadProbe`, `HeadingProbe`, `StormMeasure` | Older, still valid. | `/tmp/seasick-*.txt` |

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
