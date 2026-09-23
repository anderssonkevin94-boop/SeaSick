# Stern steamer V8

Focused correction to V7: the aft hull now follows a quarter-ellipse into
the transom, removing the straight tail and curvature reversal. Additional
stations resolve the rounded return. The side rail and iron band follow it.
The fitted casing, lid, wheel, helm and working-deck design are unchanged.

Source: tools/blender/source/stern-paddle-astra-v8.blend
Generator: tools/blender/stern_paddle_astra_v8.py
Verifier: tools/blender/verify_stern_paddle_astra_v8.py
Exports: models/
Close view: stern_detail.png

Fixed components share the hull origin. Rotor and frame remain axle-local
at Unity socket (0, 0.35, -10.83), with rotor rotation around Unity X.
Topology results are in validation.json. Export/color round trips and 72
sampled wheel-clearance checks are in export-verification.json.

V7 is preserved. No production Unity assets or shaders were changed.
Review renders use Workbench; final Unity appearance and iOS performance
remain untested. Clearance tests are sampled, not a continuous proof.
