# Hornbeam V2 - Low-Poly Forest Resource

Supersedes the over-detailed V1 study. Staged for review only; nothing imported into Unity.

- Hornbeam.fbx: 224 triangles total. One continuous angular crown and one simple tapered trunk, separate mesh objects using the same vertex-color material.
- Hornbeam_Stump.fbx: 32 triangles. Matching lower-trunk geometry with a cut surface.
- No individual leaves, alpha cards, textures, interior branches or small decorative geometry.

## Placement and Harvesting

Meters. Root at ground center, Blender Z up. Tree height approximately 6.98m, crown envelope 2.62 x 2.54m, shallow root embed 0.06m. Stump cut is at Z=0.38m. Apply the same transform/uniform scale when swapping tree and stump.

Markers: Hornbeam__Ground=(0,0,0), Hornbeam__Chop_Target=(0,-0.19,0.95), Hornbeam__Fell_Pivot=(0,0,0.38). The chop target is an axe-contact reference, not a worker navigation destination. Use a simple trunk collider; do not block navigation using the full canopy bounds. Resource quantity, worker approach and harvesting behavior remain game-owned.

FBX exports -Z forward/Y up. Preserve imported axes deliberately when adapting Blender marker coordinates to Unity.

This is a static tree and replacement stump. No rig, wind weights, animations, falling upper-trunk variant, partial-chop state, colliders or gameplay scripts are supplied. The fall marker alone does not implement felling: rotating the whole standing model also rotates its roots. Use a swap/fade, or implement a properly cut upper-trunk state before animating a fall.

## Rendering and Mobile Use

Flat normals, Col vertex colors, one shared source material. Use the game's vertex-color shader without normal smoothing. Keep shared meshes/materials and use appropriate instancing/batching and culling for large forests. Separate trunk/canopy meshes can be combined by the integration pipeline when independent control is not needed. Triangle count alone does not guarantee frame rate; draw calls, shadows and visible instance count still need device profiling. No claim of iOS performance testing is made.

One tree shape is supplied. Grove renders are repeated, rotated and uniformly scaled instances, not distinct variants. Additional variants should keep this approximate 200-300-triangle complexity, not return to per-leaf geometry. No separate LOD or billboard is included.

## Files and Checks

- Blender: ../../tools/blender/source/hornbeam-astra-v2.blend
- Generator: ../../tools/blender/hornbeam_astra_v2.py
- hornbeam-front.png, hornbeam-back.png, game-angle.png, grove.png, stump.png
- validation.json and export-verification.json

Both FBXs passed round-trip checks for manifold edges, nonzero face areas, flat normals and vertex colors. Tree marker presence was checked. Saved Blender opens on the simplified tree; review grove instances and stump are hidden. No Unity integration was performed.
