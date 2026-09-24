# Forest Groundcover Mockup

APPROVAL HOLD: standalone Blender composition. No Unity assets or settings changed.

Uses the approved first low-poly forest pack's six tree variants and the
middle-weight grass, broadleaf, pebble and twig meshes, plus the existing
boulders and shrub-lobe shape. The rejected later four-tree batch is not used.
Meshes are shared between repeated objects; plant placement is clustered,
not evenly distributed. A winding open corridor and small clearing retain
readability between denser groves.

- `forest-overview.png`: wide composition.
- `forest-phone.png`: portrait gameplay-style composition, not an in-game capture.
- `forest-groundcover-mockup-v1.blend`: editable scene with hidden source library.
- `scene-audit.json`: instance counts and geometry totals.
- Generator: `tools/blender/forest_groundcover_mockup_v1.py` at repository root.

Terrain is review-only geometry with vertex-colour variation. It is not a
proposed terrain mesh replacement. Cycles lighting is illustrative, not Unity
shader parity or measured iPhone performance. No harvesting, colliders, LODs,
runtime scattering, batching, or gameplay navigation have been implemented.
