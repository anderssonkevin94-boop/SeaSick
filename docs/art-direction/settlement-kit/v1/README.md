# Settlement kit — first design pass

Standalone Blender assets for later review and Unity implementation. Made through Blender MCP. The Unity project, scenes and Assets directory were not edited.

## Contents

- Five campfire stages: landing fire → stone ring/bench → tripod cooking camp → paved gathering court → permanent masonry-backed communal hearth with banner.
- Three huts: 2, 4 and 6 resident/bed positions. Real entry openings, 2.1 m bed frames and open central circulation.
- Three farms: 4, 6 and 8 planting slots. Each bed has its own crop mesh module and planting marker. One worker position tends a pair of opposing beds from the central aisle.
- Sawmill, storage, blacksmith and kitchen: distinct equipment and worker markers, open working fronts.

## Source and exports

- Editable generator: `tools/blender/settlement_kit_v1.py`
- Blender source: `tools/blender/source/settlement-kit-v1.blend`
- Fifteen independent GLB files: `tools/blender/exports/settlement-kit-v1/`
- Exact counts, placement footprints and local interaction positions: `manifest.json`
- Review images: `campfire-progression.png`, `hut-progression.png`, `farm-progression.png`, `workshops.png`, `interior-cutaway.png`

Static pieces are consolidated into logical mesh modules to avoid a renderer per plank. Roofs, doors, crop beds' wheat and campfire visuals remain separable. Roots are ground-level, centered on each structure. Geometry uses flat palette materials and broad facets. Individual exports exclude the review floor, lights, camera and scale character.

## Scale and later integration

Authored in metres against the saved 1.93 m weathered sailor. Local gameplay code currently has a 1.7 m person scale; if that convention is retained, uniformly scale both this kit and the sailor by 1.7 / 1.93 at integration. Do not rescale buildings alone. Door openings are approximately 1.3 m clear width and 2.2 m clear height before any global scale adjustment. Worker markers reserve a 0.55 m radius; the footprint in the manifest includes exterior attachments, but allow another 1.5 m around buildings for settlement circulation.

Blender coordinates are Z-up; GLB exports convert to Y-up. Marker positions in the manifest are explicitly Z-up. Marker extras identify role and facing direction. Bed markers are bed-surface references rather than standing spawn points. Farm worker spots serve a pair of beds; planting positions are distinct from standing positions.

These are static design assets. Gameplay upgrade replacement, worker reservation logic, colliders, navigation, crop growth states, doors, smoke/fire animation and final batching/material/shader integration remain deferred. Flames and wheat are visible static previews. Workers have geometric workspace allowances, not a tested in-game navigation implementation.

The previously accepted sailor is preserved separately at `tools/blender/source/crew-weathered-v2.blend` and `tools/blender/exports/crew-weathered-v2/male-coat-deckhand.glb` (2,210 triangles, unrigged).
