# Forest Ground Palette Study

APPROVAL HOLD. Standalone Blender mockup only; no Unity integration.

Identical assets, positions, geometry, light and cameras to the first forest
mockup. Only the ground's existing `Col` corner-colour values are changed.

- Mossy greens follow overlapping groves rather than isolated circular discs.
- Irregular muted earth patches sit around roots and rock groups.
- Drier yellow-green areas occupy more open ground.
- A narrower worn route has broken, soft edges.
- Colour is sampled at shared vertices and interpolated across triangles,
  avoiding the original face-by-face checkerboard effect.

`forest-overview.png` and `forest-phone.png` are actual Blender renders.
`forest-ground-palette-v2.blend` contains the editable study.
`study-audit.json` records zero added geometry and zero added materials.
Generator: `tools/blender/forest_ground_palette_v2.py` at project root.

The terrain remains the same review-only mesh, not an island-mesh replacement.
This demonstrates a colour direction, not performance or shader parity in
Unity. Runtime terrain painting/blending and changes after harvesting would
need their own approved implementation. Original mockup files are preserved.
