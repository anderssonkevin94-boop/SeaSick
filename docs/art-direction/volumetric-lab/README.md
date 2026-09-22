# Volumetric sunlight lab

Open `Assets/_Project/Scenes/VolumetricLightLab.unity` in Unity and press Play. Toggle the atmosphere and moving camera using the test controls. Save your current scene work before switching scenes.

The prototype integrates scattering through a bounded 3D haze volume. Every raymarch sample queries the main directional light's actual shadow map; the camera depth texture stops the ray at opaque geometry. The arch and trees therefore cast shadows through space, not only onto surfaces. No gameplay scene, production renderer configuration, or production sky material was changed.

This is a visual feasibility test, using simple geometry and static lighting. It does not yet include moving cloud shadows, temporal reconstruction, low-resolution rendering, or the production ocean's transparency/depth integration. The test uses an opaque water stand-in. It renders 96 shadow samples per covered pixel at full resolution and is deliberately not production optimized. Screenshots and scrub frames are not GPU benchmarks.

Recommendation: consider restrained, local volumes around islands and strong low-angle sunlight as a desktop quality option. Do not enable this full-resolution implementation over the entire ocean or on mobile. An adoption decision needs target-device GPU timings of a half-resolution, depth-aware implementation and moving-view artifact checks. An unshadowed haze fallback can serve lower quality tiers.

Regenerate with Unity menu SeaSick > Art Reviews > Build and capture volumetric lab, outside Play mode. The tool creates only its own scene/materials, captures comparisons, and returns to the original scene.
