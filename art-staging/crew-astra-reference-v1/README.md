# Reference deckhand: first visual prototype

Based on Screenshot 2026-09-19 at 11.34.27.png: navy cap, short hair,
plain linen shirt, rolled cuffs, blue trousers and tall leather boots.
The small unseen face is an interpretation, not reference-confirmed.

Source: tools/blender/source/crew-astra-reference-v1.blend
Generator: tools/blender/crew_astra_reference_v1.py
Working-pose and neutral FBX files are in this directory.
Rear-reference, front, neutral-front and 160x196 inspection renders included.

2,714 triangles per figure. Cloth and skin are separate meshes.
Exports store white vertex shades on skin, matching CrewVertexColor's tint
contract. Assign skin _BaseColor sRGB #D99259 (linear value in validation.json)
and white _BaseColor on clothing. The Blender review source stores warm skin
vertex colors for Workbench display; the generator neutralizes them on export.

This is an unrigged proportion/costume prototype, not an animation-ready
asset. Garment shells are fitted separate islands, not finished deformation
topology. No skin weights, animations, collider, prefab or Unity integration.
The pose is authored statically, not produced by a skeleton.

Mesh checks cover open edges, overconnected edges and degenerate faces;
they do not prove that separate costume shells have no intersections.
FBX round-trip checks validate triangle count, colors and neutral skin shade.
Unity lighting and actual gameplay readability still require an in-game test.
Existing crew assets were not modified.
