# SeaSick — Graphic adventure daytime skybox

Standalone art package matching the lower **B — Graphic adventure** panel of the reference: saturated blue, warm cream-white cumulus, pale blue cloud shading and coastal haze. No land, ocean, sun disc or scenery is baked in.

**No files were imported into Unity. No Unity editor, Assets, scenes, materials, lighting or project settings were changed for this task.**

## Contents

- `graphic-adventure-day-panorama.png` — original generated 1774 × 887, 2:1 equirectangular, sRGB/LDR panorama. Native resolution is retained; this is not a 4K or HDR texture.
- `GraphicAdventureSky.shader` — standalone Unity skybox shader with tint, exposure and rotation. It blends the longitudinal join and flattens the polar caps, so the generated image does not need to have pixel-identical borders. The raw PNG alone is not certified seamless in a stock panoramic shader.
- `preview.html` — self-contained 360° WebGL viewer using the same seam/pole treatment. Open directly in a browser; drag to look around and scroll to change field of view. The image is embedded to avoid local-file texture restrictions.
- `skybox-preview.png` — browser-rendered preview of the spherical sky.
- `generation-prompt.txt` — exact art prompt used with the built-in image-generation tool. The supplied A/B reference was used for style; only B was requested.

## Later Unity import — not performed

1. Copy the PNG and shader into a dedicated art folder under Assets when implementation is authorized.
2. Import the PNG as a **Default / 2D** texture, sRGB on, alpha source none, wrap **Clamp**, filter **Bilinear**, mipmaps on, maximum size 2048 or higher. Retain the 2:1 aspect ratio; do not force power-of-two resizing.
3. Make a material using **SeaSick/Skybox/Graphic Adventure Day** and assign the PNG to Sky panorama. Start with white tint, exposure multiplier 1, rotation 0, wrap blend width 0.025.
4. Assign that material as the environment skybox only when ready to integrate. Existing directional lighting should provide the sun; no sun disc is painted into this texture.
5. Coordinate with Claude before changing SkyDirector or weather blending. This is the clear-day layer, not a replacement for the game's storm/day-night logic. The shader has not been compiled or tested in Unity, intentionally.

The viewer validates the sphere mapping and wrap treatment outside Unity. Unity lighting, reflection capture, texture compression, shader compilation and weather integration remain untested until implementation is authorized.
