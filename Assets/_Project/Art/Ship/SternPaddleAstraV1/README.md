# Stern Paddle Astra V1

Procedural first-pass model based on `art-staging/stern-paddle-astra-concept-v6`.
Crew and cannons are intentionally omitted.

## Files

- `Models/stern_paddle_hull.fbx` - fixed hull, open deck, chimney, helm, transom rail, and permanent socket rails.
- `Models/stern_paddle_module_frame.fbx` - non-rotating interchangeable cassette frame.
- `Models/stern_paddle_wheel.fbx` - rotating paddle wheel, authored around its axle at local origin.
- `tools/blender/source/stern-paddle-astra-v1.blend` - complete authoring scene and fitted hierarchy.
- `tools/blender/stern_paddle_astra_v1.py` - deterministic generator and exporter.

## Assembly

Unity ship-local coordinates use `+Z` bow, `+Y` up, and `+X` starboard.

- Hull origin: centerline, design waterline, midpoint of the 30 m LWL.
- Wheel module socket: `(0.0, 0.78, -16.86)` metres.
- Parent the frame and wheel at the socket with zero local position and rotation.
- Rotate only `stern_paddle_wheel.fbx` about its local axle.
- Replace the frame and wheel together for a visual wheel upgrade; the hull remains unchanged.

The `.blend` contains `WheelModuleSocket`, `WheelModuleFrame_Fitted`, and
`PaddleWheel_Rotating` arranged in this hierarchy.

## Geometry

- Hull: 7,124 triangles
- Cassette frame: 172 triangles
- Wheel: 436 triangles
- Fitted total: 7,732 triangles

The hull has a full watertight transom with no paddle notch. The teal stern rail
is transverse in plan and rises only along the vertical axis to clear the wheel.
Vertex colors use the existing `Col` attribute and SeaSick steamer palette.
