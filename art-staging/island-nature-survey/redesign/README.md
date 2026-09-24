# Island 2 nature redesign

## Composition And Grounding Pass (2026-09-24)

- Replaced uniform small plant scatter with asymmetric tree, boulder, and plant
  groups, with clear ground between them. Grove centres have larger crowns and
  edge trees are smaller. Coastal palms occupy patches rather than every low site.
- Broader moss and earth patches tie the vegetation together. Soil breaks up flat
  rock shelves; steep cliff faces remain exposed stone.
- Nature-only shadow receiver offset reduces facet self-shadow artifacts; cast
  shadows remain enabled. Other islands' materials retain their original settings.
- `NatureGrounding` seats scenery and dressed resource props against the actual
  streamed terrain triangle planes, not the continuous height function. Contact
  points are embedded by about 4.5 cm. Meshes update when terrain LOD changes;
  collapsed harvested/cleared ranges remain collapsed and regrowth is supported.
- Geometry: 112,276 near triangles / 35,404 distant triangles across 27 scenery
  cells, about 6.2% fewer near triangles than pass 1. No extra tree resource nodes.
- Regression: 7,561 actual mesh contact points checked at terrain steps 1, 2,
  and 4; largest measured gap was -0.043 m (below ground). Independent collider
  ray checks in 18 chunks agreed with the surface sampler. Felling/regrowth
  across terrain LOD changes passed. All 253 tree IDs and logical positions
  remain unchanged; only visual ground seating changes vertically.
- Validation: `grounding-regression.txt`, `validation.txt`. Actual Unity previews:
  `pass2-forest.png`, `pass2-forest-phone.png`, `pass2-sea-east.png`.
- Pass-1 rollback snapshot and pre-pass desktop save:
  `art-staging/backups/island2-before-composition-20260924/`.
- Save audit: the desktop file changed from a 17:35 snapshot to a 17:41 quit-save
  during this session (about 3 seconds of game time, ship pose/track and ledger
  tick changes; both contain no built camp). The newer live file was NOT replaced.
  Both versions are preserved as `desktop-save.json` and `desktop-newer-1741.json`.
  The subsequent protected restore checks loaded the earlier desktop snapshot
  and the developed iPhone backup into temporary Play state only. The phone's
  saved data was not written. The iPhone camp restored all 7 buildings / 19 walls.
- Grounding applies to this island's nature batches and replaced resource props,
  not buildings, ship fittings, or the authored home island. This is not a global
  terrain/gameplay-height rewrite and does not claim on-device performance results.

## Original Integration

Implemented in `Assets/_Project/Scenes/Sea.unity` for Island_2 at (660, 0, 81).
The scene object `Island 2 Nature` enables the treatment. Other islands retain
their existing scatter and materials. The empty home island stays the home island.

## Scope

- Approved V4 tree library, including palms, with cheaper distant meshes.
- Middleweight groundcover and new shore, cliff, and woodland accents.
- Island-local grass, dry ground, moss, and earth colour variation.
- Stone and timber resource visuals replaced without changing resource roots.
- Nature is designed without building placement masks. Normal gameplay clearance
  still removes foliage beneath construction when a settlement is restored.
- No terrain heights, coastline, world seed, resource yields, building layout,
  or player save files intentionally changed.

## Verification

- Runtime template validation: 46 templates, outward winding, no degenerate faces.
- All 253 scenery tree positions and index order match the original scatter.
- Felling collapses the new mesh; regrowth restores it.
- 27 scenery cells: 119,700 LOD0 triangles and 36,040 LOD1 triangles in total,
  excluding terrain, other islands, buildings, and separate resource nodes.
- One 256 x 256 ground colour texture. Shared scenery material and cell batching.
- Unity renders checked at landscape and portrait aspect ratios; shader error check passed.
- Backed-up iPhone save restored in save-suppressed, time-frozen Play mode:
  Campfire, Pier, Storage, Farm, Blacksmith, Sawmill, Fletcher and 19 walls present.
- These are editor checks, not measured iPhone frame times. No phone build installed.

## Rollback

Exit Play mode, disable the `Island 2 Nature` GameObject, and enter Play again.
The original scenery templates and ground shading will be used on the next world
generation. Do not use live disabling as a complete rollback of an already built world.

Pre-change backup:
`art-staging/backups/island2-before-nature-20260924/`

This contains `project-before.tar.gz`, `desktop-save.json`, and `iphone-save.json`.
For a source rollback, inspect and restore only the affected source/scene files
from the archive; do not overwrite unrelated work made since the backup.
The snapshots are backups, not replacements for the user's active saves.

## Rebuild Art Data

`tools/blender/export_island2_nature.py` exports the approved source libraries to
`art-staging/island-nature-kit-v1/unity-kit.json`. The imported copy is
`Assets/_Project/Art/AstraPlaytest/NatureIsland2/kit.json`.
The source library FBXs and Blender files remain in art-staging.

`forest-desktop.png` and `nature-close.png` are actual Unity captures after the
temporary saved-camp restore. The nature layout itself does not account for buildings.
