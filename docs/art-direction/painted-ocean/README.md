# Painted ocean — 2026-09-17

Integrated into the existing SeaSick/Ocean shader and the GraphicArt/OceanSurface material already referenced by Sea.unity. No scene save required.

Broad blue/turquoise pigment planes follow wave height and normals, with soft derivative-aware boundaries. Tinted reflections preserve color at grazing angles; broad satin highlights replace fine sparkle. Simplified cream foam uses the existing simulated breaking/wake/shore fields. Surface normal detail is reduced only in shading. Painted strength defaults to zero for other materials; the production graphic material enables it.

Vertex displacement (including Claude's shoreward swell), FFT, buoyancy, foam accumulation, storm balancing, hull cutout, and ocean clock are unchanged by this art pass. Existing weather and night palettes continue to drive the surface. There are no added texture reads.

`calm-before.png` and `calm.png` are same-frame live Unity desktop captures; `calm-portrait.png` checks portrait rendering. The night-palette capture isolates the shader's night response; it is not a full night lighting test. ShaderUtil reported no errors after actual rendering. Stronger sea-state rendering was explored but not accepted as a visual validation because the editor reload disturbed the live simulation; no weather settings were saved.

Reapply the authored material via SeaSick > Art > Apply painted ocean. WaterArtReview contains the temporary-camera capture utility. The scene's existing unsaved state was preserved.

A fresh Play session also passed the wider ocean view (`ocean.png`) and isolated storm palette rendering (`ocean-storm-palette.png`). The source material persisted painted strength 1 after import. `ocean-before.png` is the saved material before reapplying the same settings, not the original look.
