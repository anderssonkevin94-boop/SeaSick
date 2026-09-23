# Stern paddle steamer V6

The working deck is level through the stern and helm area. A short wheel
housing replaces the warped deck: its crowned cover ends at a vertical
forward bulkhead, and the straight deck joints stop at that wall.
Cover panel joints run athwartships rather than extending the deck planks.
The existing bow sheer is retained.

The cover, forward bulkhead, hull cheeks and internal wheel cavity are one
closed Hull_Shell mesh. The external housing wall sits ahead of the cavity
wall, avoiding coplanar overlapping surfaces. The raised stern rail remains.
Paddle_Rotor and Paddle_Frame remain separate axle-local exports at Unity
socket position (0, 0.35, -10.83). No people or cannons.

Source: tools/blender/source/stern-paddle-astra-v6.blend
Generator: tools/blender/stern_paddle_astra_v6.py
Verification: tools/blender/verify_stern_paddle_astra_v6.py
FBXs: models/
The deck_housing.png view shows the new forward wall and level working deck.

Topology checks cover boundary edges, overconnected edges and degenerate
faces. Independent verification checks FBX triangle counts and Col colors,
72 sampled rotor/hull clearances, and frame clearance outside the intended
bearing region. See validation.json and export-verification.json.

V5 is preserved. Production Unity assets and shaders are unchanged.
Renders use Workbench vertex colors; Unity appearance and iOS performance
remain untested. These checks are not an exhaustive self-intersection audit.
