# Graphic adventure sky — Unity integration

Implemented after the user's explicit authorization on 2026-09-17.

The approved panorama is imported as `Assets/_Project/Art/SkyboxB/GraphicAdventureDay.png` and assigned to the existing `Assets/_Project/Art/Sky.mat`. Sea already references this material, so no scene rewrite was needed. Existing unsaved scene changes were preserved.

The active `SeaSick/Sky` shader now supports an optional graphic-adventure layer. This retains the original SkyDirector material identity, sun/moon/stars, day/night palettes, directional storm escape cues and weather control. The standalone staging shader is not the runtime shader: using it directly would bypass the weather system.

The artwork fades in above sun elevation 0.06–0.28 and fades out across overcast 0.18–0.70. Distant storm bearings also suppress it. The original gradient handles the sea horizon and lower hemisphere. Procedural fair-weather cloud coverage is reduced by the same weight, avoiding two overlapping cloud styles. The panorama's longitude join and poles use the staging preview's blend treatment.

Controls on Sky.mat: `_AdventureStrength` (1 enabled, 0 legacy sky), `_AdventureRotation`, `_AdventureSeamWidth`, and `_AdventurePanorama`. The default shader strength is zero for materials that have not explicitly opted in. No ocean, sailing or weather-controller code was changed.

Texture: 1774 × 887 native, sRGB, Clamp, bilinear, mipmaps, uncompressed to retain subtle gradients. This is an LDR painted daytime backdrop, not a new HDR lighting environment. SkyDirector continues to drive ambient and directional lighting.

Validation:
- Sky shader compiled successfully in Unity on Metal.
- Daytime image comparison confirms the artwork is visible.
- Storm, night and dusk comparisons with the layer enabled/disabled are effectively identical (mean channel difference below 0.001/255).
- Landscape and portrait sky renders are included.
- `live-validation.txt` records the runtime SkyDirector material check; `live.png` shows the live scene using a temporary sky-facing review camera.

Tools: **SeaSick → Art → Import graphic adventure sky**, **Validate graphic adventure sky**, and **Capture live graphic adventure sky** (Play mode). Validation restores the original sky material and shader globals; it does not save the user's scene.
