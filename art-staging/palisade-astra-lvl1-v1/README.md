# Palisade Kit L1 - Astra V1

Staged art only, not imported into Unity. Plain sharpened stakes, two rear timber rails and sparse rope lashings. No stone footings, roof, spikes separate from stakes, walkway or gate. Gate remains a separate asset.

## Files

Seven independent FBX pieces, each with its own root, one mesh and two named boundary sockets:

- `Palisade_Straight_2m_A.fbx`: 2 m straight, eight stakes.
- `Palisade_Straight_2m_B.fbx`: second 2 m silhouette/color variation.
- `Palisade_Straight_1m.fbx`: 1 m straight, four stakes.
- `Palisade_Corner_Left.fbx`: three-stake +90-degree turn with mitred backing rails.
- `Palisade_Corner_Right.fbx`: mirrored -90-degree turn.
- `Palisade_Terminal.fbx`: 0.25 m terminal with taller stake and bindings.
- `Palisade_Filler_025m.fbx`: 0.25 m single-stake section with rail stubs.

Source: `../../tools/blender/source/palisade-astra-lvl1-v1.blend`. Source scene is a spaced review gallery, NOT the exported transforms. Individual FBXs were exported at local origin before arranging the gallery. PNGs include kit overview, an assembled L from both sides and gameplay scale.

## Dimensions And Materials

Meters, Blender Z-up. Straight pieces run from local X=0 to X=length along Y=0. Ground Z=0. Rails are on +Y (camp-facing side). FBX -Z forward / Y up; use imported socket transforms rather than assuming Blender axes survive unchanged in Unity.

Stake pitch is 0.25 m, height varies deliberately from 2.39 to 2.60 m. No buried extension is supplied; uneven terrain requires placement handling. One vertex-color material with `Col`, flat normals, no textures. Preserve hard normals and use the game's vertex-color shader. Every piece is static, without collisions, rigging or animations.

## Snapping

`snap-contract.json` is the exact local-coordinate contract. Each marker is named `<PieceName>__Snap_Start` or `<PieceName>__Snap_End` to avoid Blender numeric suffixes. Socket positions are at cell boundaries, not on stake centers. Match sockets, not bounding boxes. Adjacent straight sections do not duplicate endpoint stakes.

Left corner sockets: Start=(-0.375,0,0), End=(0,+0.375,0). Right corner sockets: Start=(-0.375,0,0), End=(0,-0.375,0). Corners consume 0.375 m of BOTH meeting legs; they are not zero-length overlays on top of full straight runs.

Verified left-turn example in Blender coordinates:

1. Straight A root at (0,0,0), rotation Z=0.
2. Left corner root at (2.375,0,0), rotation Z=0.
3. Straight B root at (2.375,0.375,0), rotation Z=90 degrees.
4. Terminal root at (2.375,2.375,0), rotation Z=90 degrees.

Right corner has its incoming rail on -Y, so reverse neighboring straights to preserve the rail side. Verified example: right corner at origin; incoming straight root=(-0.375,0,0), Z=180 degrees, using its Start socket at the corner Start. Outgoing straight root=(0,-2.375,0), Z=90 degrees, using its End socket at the corner End. Socket names are references, not a mandatory travel direction. Do not negative-scale the assets to fix orientation.

Terminal occupies another 0.25 m. It is not an extra log centered on an already occupied endpoint. Omit it where the run continues or meets a future gate. Filler also occupies 0.25 m. Rails butt together at sockets; visible carpenter's seams are intentional.

## Runtime Work Still Required

Existing walls support arbitrary player-drawn lengths and angles. This kit directly covers straight runs in 0.25 m increments and 90-degree turns only. Do not silently snap gameplay endpoints or change charged length to fit the art. Claude must handle residual lengths and other corner angles in the visual adapter, with fitted procedural stakes/rails or additional authored pieces. Do not stretch the sharpened posts or an entire module to fill a remainder.

Keep current PalisadeCost and segment durability authoritative. Visible stake count is decorative, NOT resource count: the game prices timber per length. Collision should normally use a conservative segment collider rather than a collider for every stake or rope. Add navigation obstruction and damage-state behavior through the existing wall system. No damaged or destroyed-state meshes are included.

Source and FBX checks verify flat normals, colors, manifold edges, nonzero face areas, marker presence and matching left/right socket positions. These are not in-game collision, arbitrary-angle joins or performance tests. Rails and ropes are constructed components touching or entering their supporting timber, not a boolean-unioned solid.
