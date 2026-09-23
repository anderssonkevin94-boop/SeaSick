# Pier L1 - Astra V1

Staged artwork only, not imported into Unity. Simple timber decking, octagonal pilings, mooring pins and rope details. No railing, roof, cargo clutter or implied storage. Preview water is NOT an exported asset or a replacement water shader.

## Files

- `Pier_Deck_2m.fbx`: repeatable 2 x 3 m deck bay, two mooring-post heads, under-deck stringers and crosshead.
- `Pier_Sea_End_2m.fbx`: same bay with end fascia and separate rope-detail mesh. Replaces the last regular bay; do not overlay both.
- `Pier_Piling_1m.fbx`: plain 1 m piling shaft, top-origin. Extend only this part vertically to meet the seabed.
- `Pier_Shore_Ramp.fbx`: rigid 2.4 x 2.4 m ramp with a top-surface hinge at local origin. Rotate the entire ramp, not individual planks.
- Source: `../../tools/blender/source/pier-astra-lvl1-v1.blend`.
- `snap-contract.json`: local reference coordinates and unique marker names.
- `validation.json` and `export-verification.json`: source and FBX topology/color checks.
- PNGs: 14 m assembled preview, exposed support structure, sea-end detail and gameplay scale.

The Blender source opens on the 14 m review assembly. Original export modules are hidden at origin. Review copies share mesh data. Do not export the entire review scene as a prefab: it includes duplicate module sources, a camera and a water slab.

## Critical Coordinate Contract

One Blender unit is one meter. Blender +X runs land to sea, +Y crosses the deck, +Z is up. Deck walking surface is local Z=0, NOT Z=1.2. FBX -Z forward / Y up.

The existing `Pier.cs` expects a centered root with length along Unity local +X, width along local Z and deck at local Y=0. Runtime already places that root 1.2 m above mean water. Do NOT add another 1.2 m to the imported visual. Verify imported socket directions when parenting under that root.

For length L in 2 m increments, place bay roots at X=-L/2 + 2*i. Seven bays make 14 m; twelve make 24 m. Use the sea-end bay only at the final index. Each bay's Snap_Land=(0,0,0), Snap_Sea=(2,0,0), so endpoints remain exactly -L/2 and +L/2. Ramp is additional landward geometry, outside that berth-length measurement, matching the current procedural approach.

All marker names are `<PartName>__<Marker>`, recorded in JSON. Use imported local marker transforms rather than hard-coded assumptions about FBX axis conversion.

## Pilings And Shore Ramp

Each deck bay supplies two pile anchors at X=0.375, Y=+/-1.30, Z=-0.50. Head geometry stops there. The unit shaft extends from local Z=0 to Z=-1; its pivot stays at the top. Place its root at the anchor and scale its vertical axis to the required depth. Do not stretch the mooring caps, decking or rope.

The review uses 3.5 m shafts below the anchors: bottoms are 4.0 m below the deck, equivalent to 2.8 m below mean water. This is only a demonstration, not terrain sampling. Production must query terrain at each support, respect the existing depth clamp and embed supports as appropriate. On very shallow ground, adapt/hide lower geometry rather than using a negative shaft scale. Stakes are spaced every 2 m in this artwork, not the previous procedural 3 m interval.

Ramp review drop is 1.0 m, with rigid length 2.4 m: angle=-asin(1/2.4) around Blender Y. Hinge is at the land endpoint. This is a FIXED sloped length, unlike the old procedural ramp's fixed horizontal run. To fit actual shore height, solve the ramp angle and toe position from terrain. A 2.4 m ramp cannot cover a 2.5 m vertical drop; provide a longer ramp or reject/adjust that landing in the adapter. Do not silently distort planks or alter siting/economy to hide this limit.

## Materials And Gameplay

Flat normals, one `Col` vertex-color material, no textures. Preserve hard normals, use the game's vertex-color shader. Rope coils are decorative, not inventory or dynamic mooring lines. No animations, colliders, buoyancy, navigation or docking logic included.

Claude should replace only the old pier visual, keeping Pier.Configure, actual saved length, heading, SeaEnd, Berth, dock registration and siting rules authoritative. The asset supplies no substitute berth logic. Add deck/ramp collision, navigation and shoreline adaptation, then test with the actual ship and crew. Keep the central walking strip clear. No in-game import or code changes were made here.

Validation covers manifold edges, nonzero face area, flat shading, vertex colors and markers after FBX reimport. It does not certify terrain fit, crew traversal, ship clearance or physical engineering. Components intentionally meet at construction joints rather than forming a single boolean-unioned mesh.
