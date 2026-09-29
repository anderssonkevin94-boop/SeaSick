# Large fish — approved model and swimming animation

A 5.9m chunky sea fish: deep teal back, turquoise dorsal stripe, pale belly, golden eyes, wide swept fins and upright forked tail. Designed for the game's faceted wood/landscape style and an overhead phone camera.

- `large-fish.blend`: editable model, five-bone rig, looping swim action, review lighting.
- `large-fish-rigged.fbx`: mesh and rig only; embedded 2-second swim animation, 24fps, no root translation.
- `fish-preview.png`, `fish-top.png`: Blender review renders, not in-game underwater captures.
- `validation.json`: geometry and animation summary.
- Rebuild: `tools/blender/large_fish_v1.py`.

Unity handoff: import at metres, Generic rig, enable Loop Time on Swim_Slow_Loop. The model faces Blender +X; align the imported art child to the movement root's +Z after checking its orientation. Vertex palette is baked into GameColor; use the project's vertex-color art material instead of making a draw call per palette color. Materials are also included as a fallback. No textures required. Do not import the review plane or lights from the blend file.

Suggested first placement: one fish cruising alongside and slightly ahead of the ship, fully submerged, at 0.5–1m below the local surface at its highest dorsal point. Offset the root by the dorsal height too. Use smooth broad turns; vary swim playback modestly with travel speed. No collider needed for ambient scenery.

The current ocean may hide submerged opaque meshes: actual visibility must be tested with its depth/transparency handling. This asset does not modify the ocean shader or install spawning/AI. If the opaque water occludes it completely, the renderer needs an underwater visibility solution; moving the fish above the surface is not a correct fix. Test the pale dorsal stripe at phone size before adding groups of fish.

Approved by Kevin on 2026-09-29 and queued in ASTRAS_READY_ASSETS.md for Claude to integrate.
