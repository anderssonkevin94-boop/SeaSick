# Stern steamer V7: fitted machinery casing

Implements the approved two-view study, saved as approved-design-study.png.
Prior model versions and production Unity assets are unchanged.

## Construction
- Flat working deck with a compact rounded timber casing around the rotor.
- Casing body and curved transom wall are joined into the closed hull shell.
- Separate gently crowned lid with rounded corners and a small overhang.
- Two continuous iron straps turn over the lid onto the forward face.
- Framed wooden access panel with four corner fasteners.
- Helm moved forward to leave clearance in front of the casing.
- Raised stern rail remains on the hull perimeter; no warped deck ramp.
- No people or cannons.

## Deliverables
Source: tools/blender/source/stern-paddle-astra-v7.blend
Generator: tools/blender/stern_paddle_astra_v7.py
Verifier: tools/blender/verify_stern_paddle_astra_v7.py
FBX components: models/
Interior views: deck_housing.png and housing_front.png
Exterior views: stern_detail.png, rear34.png, rear.png and side.png

Fixed FBX components share the hull origin. Paddle_Rotor and Paddle_Frame
remain axle-local at Unity socket (0, 0.35, -10.83), rotating around Unity X.
Lid and housing fittings stay fixed when the rotor is exchanged.

## Checks and limits
See validation.json for exact mesh triangle counts and topology checks.
All components pass boundary, overconnected-edge and degenerate-face checks.
export-verification.json records FBX color/count round trips and 72 rotor
orientations tested against the hull and frame outside the bearing region.
Rail colors avoid the existing Boat Toon shader's panel-recolor predicate.

Review renders use Blender Workbench, not final Unity lighting. Actual iOS
performance and in-game shader appearance remain untested. Tests do not
constitute an exhaustive self-intersection or continuous-clearance proof.
No LODs or Unity integration were added in this pass.
