# Ship water interaction — 2026-09-29

Installed in the game. `PaddleDrive.Configure` adds/configures `ShipWaterEffects` on spawn and modular rebuild. No scene setup is required. Ship forces, ocean shader/simulation and camera settings are untouched.

## Behavior

- Actual rotor rotation and batched water heights drive blade entry/exit. F30 visual rotors have 10 low / 15 raised blades independently of the hydraulic blade count.
- Four chunky jet variants at entry, carried water on emerging blades, gravity-driven released droplets. Dry wheels do not emit. Reversing works.
- Bow crests use the current hull waterline profile, with an outside offset for the model's timber ribs.
- Wake stamps remain in world space, widen and fade with age, follow wave height/normal, and preserve the turn. Crest gaps are based on distance travelled, avoiding time-based flicker.
- Two dynamic mesh renderers, bounded pools (24 drops, 12 bursts, 48 wake rows), no per-particle objects. Uses the existing OceanProbeRegistry batch: 180 probes for low F30, 190 for raised.

## Tuning / rollback

`Assets/_Project/Resources/ShipWater/Settings.asset` owns enable, intensity, hysteresis, wake lifetime/spread, bow dimensions, water colors and surface lift. Turn **Effects Enabled** off to restore the old SurfaceWake and FoamEmitters automatically. Sound remains on the existing SpeedJuice system. The existing FEEL wake/spray multipliers still apply.

Pre-integration copies of the existing system files are in `art-staging/ship-water-integration/before`. Do not blindly restore those over newer work: the sole integration edit is the four-line hook in PaddleDrive.Configure. All other implementation files are new under Scripts/Ship/Water, Resources/ShipWater and Art/ShipWater.

Source meshes are from the prepared sea-interaction-v3 Blender kit. The editor menu `SeaSick/Art/Install ship water effects` regenerates the mesh assets from Art/ShipWater/ship-water-meshes.json and preserves existing settings.

## Verification

- Unity 6000.4.3f1: runtime and editor compile cleanly; shader/resource/hull interpolation self-check passed.
- Actual forward sailing produced blade entry/exit, live drops and bursts; finite bow/wake mesh vertices verified.
- Wheel lifted 5m clear of water: zero new entry/exit events and zero surviving drops/bursts after settling.
- Low → raised → low rebuilt the effect with 180 → 190 → 180 probes and one surface renderer.
- Disable removed exactly 180 effect probes and restored legacy wake; re-enable registered again.
- Reverse sailing at approximately -4.96m/s produced contacts, drops, bursts and a new trail.
- Final portrait and detail captures are real Unity Camera renders, not Blender illustrations. HUD omitted by capture tool.
- Tests used an isolated /tmp save directory and suppressed saving. Test camera, time, controls and configuration changes were confined to Play mode.

Phone-resolution readability reviewed in editor. Actual iPhone GPU/frame-time performance is not yet measured. These are visual effects over the existing water; they do not physically deform the ocean or simulate fluid volumes.
