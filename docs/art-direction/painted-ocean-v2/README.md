# Graphic ocean and surface wake — revision 2

User requested the sharper shapes, smaller-scale detail, and coherent foam wake seen in reference B.

## Changes
- Ocean fragment shading: narrower anti-aliased pigment boundaries, three closer blue tones, smaller world-space shape variation influenced by wave normals, and thin broken crest accents. Restored small normal detail to 0.62. Production GraphicArt/OceanSurface material updated.
- SurfaceWake: bounded 96-row / 9-column world-space mesh follows the ship's actual path. Foam widens and fades over nine seconds, follows curves, clears on teleport/refit, and sizes from hull dimensions. Vertex heights come from one Burst OceanSampler batch, with persistent buffers disposed on destruction. No per-vertex immediate sampling or per-frame managed array allocation.
- SurfaceWake shader: cutout foam lace and irregular outer ribbons; night lighting and fog; shader placed in Resources to retain it in player builds.
- SpeedJuice: continuous smoky wake, shoulder and stern particles are silent; the new surface mesh handles continuous foam. Bow/beam impact spray remains, with reduced cruising bow spray.

Wave displacement, FFT, buoyancy, storm balancing, and foam simulation are unchanged. Claude's shoreward displacement addition remains present.

## Validation
Unity compiled and rendered both shaders without ShaderUtil errors. The review moves the existing ship along a controlled 8 m/s path over the live ocean, then restores its transform, velocity, motor, helm, and sky time pin. It tests appearance and surface following, not propulsion physics. Day and night are separate live lighting frames; portrait is 1080x2340, landscape 1920x1080. See wake-validation.txt and wake-day/night/portrait.png.

The review was iterated after inspecting the renders: initial oversized color patches and solid white wake strips were reduced. No scene was saved. Existing dirty scene state was preserved. Mobile performance has not been profiled.
