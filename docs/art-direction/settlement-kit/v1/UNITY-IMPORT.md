# Unity import — 17 September 2026

Imported fifteen settlement models and the accepted weathered sailor as FBX models and reusable static visual prefabs:

- `Assets/_Project/Art/SettlementKitV1/Prefabs/`
- `Assets/_Project/Art/CrewWeatheredV2/Prefabs/WeatheredSailor.prefab`
- Review scene: `Assets/_Project/Scenes/SettlementAssetReview.unity`
- Review image: `unity-import-review.png`
- Verification: `unity-import-validation.txt`

Every prefab has an identity root with its model uniformly scaled by `1.7 / 1.93`; buildings and sailor retain their relative proportions. Measured sailor height is 1.678 m (the source's 1.93 m height was approximate). Raw FBXs retain their authored metre scale. The included Unity `manifest.json` expresses marker positions, facing vectors, reservation radii and footprints in scaled prefab-local coordinates. Blender `(x,y,z)` becomes Unity `(-x,z,-y)` in this export pipeline. Resident markers remain bed-surface positions, not standing spawn points.

The shared `VertexPalette.mat` uses the existing URP crew vertex-colour shader with white tint. Material palettes are baked to linear vertex colours; each mesh has one material slot. Roofs, door leaves, crop beds and fire meshes remain separate, as do entry, resident, planting and worker marker transforms. No package dependencies were added.

Visual validation caught inward-facing roof planes previously masked by Blender's double-sided rendering. Only exported roof faces were corrected. Unity removes forty zero-area flame-tip triangles from each campfire; the source geometry audit records these exactly. Source GLBs and Blender files are unchanged.

To repeat: run `tools/blender/export_unity_settlement.py` through Blender MCP, refresh Unity, then use **SeaSick → Art → Import settlement and weathered sailor**. The exporter restores the artist's Blender scene. The importer builds the separate review scene and restores the prior active scene without saving it.

These are visual assets ready for placement. The sailor is unrigged; colliders, navigation, worker assignment, building upgrades and animated flames/crops remain separate gameplay work. Existing runtime crew/buildings are unchanged. The ship and island design studies were not part of this settlement import.
