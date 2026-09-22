# Deckhand C — game mesh candidate

Separate Blender-only reduction of the approved proportion concept.

- LOD0: 4,670 triangles, 2,435 Blender vertices; approximately 89% fewer triangles than the 42,486-triangle concept.
- LOD1: 2,335 triangles.
- One mesh and one material per GLB; one embedded 64×64 palette texture.
- Palette UVs, baked object transforms, origin at ground centre; glTF uses Y-up.
- Export JSON verified: one scene, one mesh, one material and expected index counts per file. GPU vertex counts may exceed Blender counts due to normal/UV splits.
- Both close-up and small-size Blender renders reviewed.

Files: tools/blender/exports/crew-game-v1/deckhand-c-lod0.glb and deckhand-c-lod1.glb. Blender source: tools/blender/source/crew-deckhand-game-v1.blend.

This is a static mesh candidate, not an animation-ready character. Rigging, neutral-pose preparation, joint topology/weights and deformation tests remain. No Unity access or imports performed.
