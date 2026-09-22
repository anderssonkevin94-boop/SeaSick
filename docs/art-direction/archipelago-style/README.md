# Archipelago style — 2026-09-17

The production streamed terrain now shares the authored home island's linear sand (.78, .61, .33), grass (.24, .38, .065), and warm stone (.57, .50, .39) palette, painted surface treatment and cool-shadow form lighting. Existing home geometry and the common tree/scenery assets are retained.

Non-home inland terrain is interpolated over continuous 24 m triangular planes before its terrace profile. Straight terrace ramps replace rounded cliff transitions. The treatment fades into the original lower terrain between 8 and 18 m and leaves the existing waterline and low beaches intact. It preserves distinct island outlines and scale rather than cloning the home island. Exposed rock uses mesh face normals; grass and beaches retain smooth normals. Painted patches use world coordinates so they do not restart at chunk boundaries.

Production files:
- Assets/_Project/Scripts/Terrain/TerrainHeight.cs
- Assets/_Project/Art/Shaders/Terrain/TerrainVertexColor.shader
- Assets/_Project/Materials/GraphicArt/TerrainVertexColor.mat

The height function is shared by terrain generation and gameplay placement/depth queries. Inland shapes changed, so islands and their scenery must regenerate on entering Play. No Sea scene save or ocean edits were required.

Validation: see validation.txt. Three actual streamed islands were captured in the live Sea scene, including desktop and portrait. 6,000 world sample candidates were checked for finite heights and unchanged low beach/seabed values; rendered mesh vertices were compared against the gameplay height pipeline. This is not an exhaustive crew traversal or frame-time benchmark. The inland plane evaluation adds three base-height evaluations where enabled; open-water and low-shore queries use the original single evaluation.

Use SeaSick > Art > Review archipelago style in Play mode to regenerate the gallery. The tool temporarily changes the streaming target and sky time, then restores them. Screenshots show the game's actual terrain, not a separate art mockup.
