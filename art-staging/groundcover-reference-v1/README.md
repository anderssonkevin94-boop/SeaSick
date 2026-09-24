# Groundcover Reference Study 01

APPROVAL HOLD: standalone art study only. Do not import into Unity until Kevin
explicitly approves. No Assets, scenes, prefabs, shaders, or project settings
were changed for this study.

## Files

- `groundcover-reference-v1.blend`: editable scene with the reference arrangement,
  reusable asset library, and separate preview ground/camera/lighting collection.
- `close-view.png` and `gameplay-view.png`: actual Blender renders, not image-generation concepts.
- `mesh-audit.json`: triangle/vertex counts, closed-mesh and zero-area checks.
- Generator: `tools/blender/groundcover_reference_study_v1.py` at project root.

## Art Direction

Broad muted-grey boulders, narrow arcing grass, pale broadleaf rosettes, a few
pebbles and a fallen twig. Place vegetation at rock/trunk bases in irregular
groups. Leave most ground empty. Soil colour belongs to the ground surface,
not a floating disc under every object. The tree reuses the approved first
low-poly forest pack's hornbeam geometry with a study-only palette adjustment.

The rock library contains three silhouettes, plus a pebble. Grass has three
profiles. Shared mesh instances are used in the arrangement. The master assets
are in the hidden `Reusable assets - ground pivots` collection; enable that
collection to inspect/edit them, then hide it again for the composed preview.
All dimensions are Blender metres, Z-up. Meshes are flat-shaded and carry the
`Col` corner-colour attribute through one shared matte vertex-colour material.
Leaves are closed thin meshes, not transparent cards.

## Integration Notes For Later

- Blender lighting is a target, not proof of Unity shader parity. The ground
  uses a review-only continuous coloured mesh; terrain colour blending still
  needs a separate implementation and approval.
- Rocks are visual resources only. Harvest logic, depleted variants, collision,
  navigation obstacles, and resource identifiers are not wired or exported.
- Decorations must not inherit resource colliders. Suppress decoration in paths
  and build footprints; reduce density and remove tiny plants/pebbles at distance.
- Do not distribute this whole cluster across every tree. Use a few clustered
  arrangements, rotations and scale ranges with substantial negative space.
- The preview uses Cycles. It does not benchmark iPhone performance, draw calls,
  terrain cost, LOD transitions, or actual in-game viewing distance.
- This is a first physical reconstruction for review, not a claimed pixel-exact
  match to the reference or a release-ready integrated asset pack.

The saved Blender file opens on the composed scene. Existing Blender documents
were not overwritten and Unity was not controlled during this work.
