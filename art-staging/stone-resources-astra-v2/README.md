# Low-Poly Stone Resources V2 - Organic Shapes

Six flat-shaded stone deposits matching the approved low-poly forest family.
Staged only. Nothing has been imported into Unity or changed in gameplay.
Replaces V1 for review: uneven rounded volumes instead of ring-built shoulders
and flat caps. V1 remains staged for comparison; do not import both families.

## Assets

| Asset | Full triangles | Depleted triangles | Silhouette |
| --- | ---: | ---: | --- |
| Stone_Field | 78 | 78 | Small solitary field stone |
| Stone_Broad | 76 | 74 | Broad low boulder |
| Stone_Split | 154 | 156 | Unequal pair with a break between them |
| Stone_Upright | 156 | 156 | Taller stone with a small companion |
| Stone_LowRidge | 232 | 234 | Three low stones across a wider footprint |
| Stone_Outcrop | 230 | 230 | Larger three-stone deposit |

Each name has an FBX and a matching `<Name>_Depleted.fbx`: twelve FBXs total.
One mesh object per asset, with separate closed components for clustered rocks.
All use one shared vertex-color material, opaque faces and flat normals.
No texture maps, shader displacement, hidden detail meshes or individual pebbles.

## Resource Contract

- Units are meters. Ground pivot is local (0,0,0), with 0.07 m below ground.
- Full and depleted models share the pivot. Apply the same position, rotation
  and uniform scale when swapping. Depleted models are low rock remnants,
  not progressive damage animations or a full disappearance state.
- `__Ground` is the ground reference. Full rocks also include `__Mine_Target`,
  a pickaxe-contact reference on the front. It is NOT a worker approach point.
  The game must choose a reachable stand position outside its collider.
- Source is Blender Z up. FBX export is -Z forward / Y up, as in the tree kit.
- Keep authored normals and the `Col` vertex-color attribute. Reuse a single
  compatible game material; do not create one Unity material per FBX.
- Resource quantity, health, mining time, selection highlighting, depletion,
  regeneration, collision and navigation are owned by the game. No colliders,
  gameplay scripts or resource-yield assumptions are included.
- The geometric size differences are visual variants, not six resource types.

Use Field and Broad most often. Mix occasional Split/LowRidge deposits near
forest edges and reserve Upright/Outcrop for larger resource landmarks. Leave
room for workers around the outer footprint. Do not randomly scatter all six
at equal frequency or force every shape to identical dimensions.

## Files and Verification

- Source: `../../tools/blender/source/stone-resources-astra-v2.blend`.
  Opens on the six full deposits; depleted meshes are hidden for review.
  Review positions are a lineup only; every FBX was exported at its own origin.
- Generator: `../../tools/blender/stone_resources_astra_v2.py`.
- `family-lineup.png`, `depleted-lineup.png`, `game-scale.png`, and six individual previews.
- `validation.json`: measured bounds, triangle counts and material/mesh counts.
- `export-verification.json`: all twelve exported FBXs imported back into Blender
  and checked for triangle counts, closed manifold geometry, nonzero face areas,
  positive volume, flat normals, vertex colors and required markers.

The images are Blender asset previews, not in-game screenshots. Low triangle
counts are not a device performance guarantee; instance counts, shadows, draw
calls and culling still require iOS profiling. No LODs or game import supplied.
