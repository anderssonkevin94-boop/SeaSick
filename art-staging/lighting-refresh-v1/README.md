# Lighting refresh - 2026-09-24

Applied to the main SeaSick project, separate from the modular-ship worktree.
No scene, save, model, UI or settlement state edited. Original copies of all
five edited files are in before/.

## Changes

- TerrainVertexColor.shader and EnvironmentToon.shader: preserve URP's finite
  light range and spot-angle attenuation in the stylised lamp contribution.
  Multiplying distanceAttenuation by squared distance cancels inverse-square
  dimming but retains its smooth range envelope. Previously all lamps used a
  fixed ~22m fade regardless of Unity's actual culling radius (often 10-14m).
- Mobile_Renderer.asset: Forward+ replaces Forward, eliminating the four-light
  per-renderer selection that can give adjacent terrain chunks different lamps.
  Additional-light shadows remain disabled. No lights, resolution, shadow maps
  or post-processing effects were added. Clustered lighting still needs device
  profiling; rendering more of the existing overlapping lamps can cost more.
- AdventureLighting.asset and its C# defaults: near-neutral daylight
  (1,.98,.94), intensity 1.16 instead of yellow (1,.89,.73), intensity 1.12.
- Terrain highlight multipliers changed to near-neutral rather than a second
  yellow tint. Golden-hour palette and warm lamp colours are unchanged.

## Verification

C# runtime/editor compile passed. Isolated desktop Unity renders exercise both
actual shaders and check adjacent-pixel continuity at the real light radius.
The mobile-quality test also renders six overlapping lamps across two separate
terrain meshes. Results and images are in review/ and review-mobile/.

Results: both shaders' largest adjacent-pixel step near the lamp range boundary
was 4/255; the six-lamp terrain join was 1/255. Shader error checks passed.
The offscreen mobile-profile capture uses a temporary renderScale=1 (not saved):
at 0.8, the offscreen test produced discontinuities even with Unity's stock Lit
shader. That capture limitation is not proof of correct scaled rendering on the
phone. The production Mobile_RPAsset remains at its original 0.8 render scale.

These are controlled rendering tests on Mac, not an iPhone GPU profile or a
capture of the player's settlement. Check the same settlement by day/night
and pan across it on device before accepting the visual/performance result.
A phone update requires a new Unity iOS export; an old Xcode export will not
include these changes.

To revert, restore only the corresponding files from before/ to their original
locations after checking for newer edits. Mobile_Renderer.asset belongs in
Assets/Settings; AdventureLighting.asset in Assets/_Project/Resources;
AdventureLighting.cs in Assets/_Project/Scripts/World; the shaders in
Assets/_Project/Art/Shaders/Terrain and World respectively.
