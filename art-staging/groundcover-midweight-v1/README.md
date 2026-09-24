# Groundcover Middle-Weight Study

APPROVAL HOLD. This is an independent Blender study, not imported into Unity.
The original `groundcover-reference-v1` study is unchanged.

The camera, lighting, render resolution, placement, plant density, tree, shrubs,
large rocks, and ground are retained from the original. Only reusable detail
meshes change. Leaves retain their upper silhouette, crease and two bend
sections; simpler closed undersides eliminate two vertices per leaf. Pebbles
use fewer rings/sides; twig branches use four sides instead of five.

| Mesh | Original triangles | Middle-weight triangles |
| --- | ---: | ---: |
| Each boulder | 52 | 52 |
| Grass fan | 112 | 84 |
| Sparse grass | 80 | 60 |
| Tall grass | 96 | 72 |
| Broadleaf rosette | 112 | 84 |
| Pebble | 52 | 26 |
| Twig | 48 | 36 |

Arrangement: 1,704 -> 1,386 triangles (18.7% reduction), excluding review ground.
Grass, rosettes, pebbles and twig together: 1,116 -> 798 (28.5% reduction).
This does not imply the same percentage improvement in frame time: object
count, materials, shadows and render settings have intentionally not changed.
No iPhone or Unity performance claim is made.

- `groundcover-midweight-v1.blend`: complete editable scene and hidden master library.
- `close-view.png`, `gameplay-view.png`: matched renders of the lighter meshes.
- `compare.html`: original/middle-weight wipe comparison at both camera distances.
- `mesh-audit.json`: counts and closed-mesh/zero-area checks; all passed.
- Generator: `tools/blender/groundcover_midweight_v1.py` at project root.

All reusable meshes are flat shaded, closed and vertex coloured with `Col`.
Existing approval holds and integration caveats in the original README apply.
