# Stern paddle steamer V5

Higher-detail primary-asset pass. Previous versions remain unchanged.
No production Unity scenes, physics, shaders or prefabs were modified.

## Design changes
- Extended the stern hull 0.75 units aft around the existing rotor socket.
- Shape-preserving curved hull stations and twelve vertical profile bands.
- Raised central stern with eased shoulders, coordinated deck and transom.
- One continuous beveled cap rail with short uprights, replacing double rails.
- Dark paddle drum, twelve boards, 32-segment wooden rims and edge bevels.
- Transom plank joints, quieter deck seams, beveled fittings and chimney.
- Teal vertex paint avoids the Boat Toon shader's blue-panel recolor rule.

## Deliverables
- Source: tools/blender/source/stern-paddle-astra-v5.blend
- Generator: tools/blender/stern_paddle_astra_v5.py
- Independent checks: tools/blender/verify_stern_paddle_astra_v5.py
- FBX components: models/
- Review renders: rear34.png, rear.png, side.png, stern_detail.png

The Blender scene contains the assembled ship and wheel animation.
Fixed FBXs share their hull origin. Paddle_Rotor and Paddle_Frame use
axle-local origins; place both at Unity position (0, 0.35, -10.83).
Wheel rotation is around Unity X, Blender Y. Replacement rotors must fit
the existing opening and bearing dimensions. No people or cannons.

## Validation
33,172 triangles, including 4,820 optional plank-seam triangles.
All component meshes have zero boundary edges, overconnected edges and
degenerate faces. Every FBX round-trip preserves triangle count and Col.
72 sampled rotor orientations show no hull intersection and no frame
intersection outside the intentional bearing region (radius 0.55).
Rail vertex colors do not trigger the shader recolor predicate.
Full results: validation.json and export-verification.json.

Checks are sampled, not a continuous clearance proof or an exhaustive
self-intersection audit. Beveled mechanical parts intentionally meet.
Review images use Blender Workbench, not actual Unity lighting.
Unity/iOS performance and final in-game shader appearance remain untested.
No LODs were generated in this pass.

The current Boat Toon shader has no texture input, so plank joints remain
optional geometry rather than introducing an unrequested shader change.
