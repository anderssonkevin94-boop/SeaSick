# Pier L1 V3

Latest pier revision. Supersedes V1/V2 visually; staged only, not imported into Unity.
Broad staggered boards, fewer heavy octagonal pilings, one hanging lantern. Flat shading and vertex color attribute `Col`. No textures, flame mesh, ladder or fender clutter.

## Files

- `Pier_Deck_2m.fbx`: supported section, 904 triangles.
- `Pier_Infill_2m.fbx`: section without posts or notches, 508 triangles.
- `Pier_Sea_End_2m.fbx`: supported end with lantern, 1384 triangles.
- `Pier_Piling_1m.fbx`: adjustable shaft, 28 triangles.
- `Pier_Shore_Ramp.fbx`: rigid 2.4m hinged ramp, 352 triangles.
- Blender source: `../../tools/blender/source/pier-astra-lvl1-v3.blend`.
- Generator: `../../tools/blender/pier_astra_lvl1_v3.py`.
- `validation.json`, `export-verification.json`, `snap-contract.json`: measured bounds, checks and local markers.

## Assembly and Coordinates

Meters. Blender +X land to sea, +Y width, +Z up. FBX exports -Z forward/Y up. Use imported named markers for orientation; do not assume Blender coordinate triples are Unity triples. Deck top is local height zero. Existing runtime pier root is 1.2m above mean water, not above shore terrain.

The 14m preview uses seven 2m bays starting at X=-7: supported, infill, supported, infill, supported, infill, sea end. Eight piling shafts total, at supported bay X+0.4 and Y=+/-1.24. Support spacing is 4m. Deck is nominally 3m wide, with small staggered plank overhangs; use measured bounds for clearance. Keep the existing centered root, configured length, berth, heading and sea-end contracts authoritative.

Each supported module provides unique `Pile_Left`/`Pile_Right` markers at Z=-0.74. Place shaft top there and scale only its local axial dimension to reach terrain with embed allowance. Preview shafts are 3.26m long, not terrain fitted. Do not stretch boards, post caps or lantern. Add a supported section near the end when a different module count requires it; never leave an arbitrarily long unsupported span.

Only 2m deck increments are supplied. Runtime nonmultiple lengths require a deliberate adapter or end segment, not silent length changes. Existing maximum length is 24m. Preserve game costs and shoreline validation.

Ramp pivot is `Hinge`; rigid length is 2.4m along local -X. Preview drop is 1m. Rotate to shore height. Unlike the existing procedural ramp's fixed horizontal run, this rigid model cannot accommodate a 2.5m drop: provide a longer ramp or reject that landing. Do not stretch it unknowingly.

## Lantern

Separate modules: `Pier_Sea_End_2m_Lantern_Post`, `_Lantern_Frame`, `_Lantern_Glow`. Unique anchor `Pier_Sea_End_2m__Light_Source` at Blender (0.4, 0.58, 1.30) in the end module.

The blend contains an amber emissive material and a warm point light (45W, radius 0.22m) at the assembled lantern. The actual light is review-only and is NOT exported in the mesh/empty FBX. In Unity, assign the glow mesh an emissive game-compatible material and add a warm point light at the imported anchor; tune intensity/range for the game's renderer. Blender watts are not a Unity intensity contract. Prefer short range and no real-time shadows on iOS; emission alone does not illuminate nearby surfaces. No flicker or day/night controller supplied.

Preview PNGs use Workbench lighting to inspect geometry and do not show physical illumination. Saved blend opens with assembled preview, water and camera; originals are hidden. Water and review objects are not asset exports.

## Validation

All five exported FBXs were reimported and passed checks for manifold edges, nonzero face area, flat normals, vertex colors and expected markers. These checks do not prove collision behavior or Unity shader compatibility; those require integration testing. The sample complete pier is 6196 triangles excluding review water. No game scripts or scenes changed.
