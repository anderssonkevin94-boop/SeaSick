# Stern paddle ship V3

Reference: the rear three-quarter, rear, side and chimney views in the user's
Screenshot 2026-09-23 at 13.33.20.png. The crossed-out top view is not used.
No crew or cannons. This is a review asset; it has not replaced the gameplay ship.

## Deliverables

- Authoring scene: `tools/blender/source/stern-paddle-astra-v3.blend`
- Eight independently exported FBXs in this directory's `models/` folder.
- Rear three-quarter, rear, side and stern-detail PNG renders.
- `validation.json`: per-object topology and triangle counts.
- `export-verification.json`: rotation collision samples and FBX round-trip results.
- Generator: `tools/blender/stern_paddle_astra_v3.py`
- Validator: `tools/blender/verify_stern_paddle_astra_v3.py`

## Construction

Hull length is 23.2 m, maximum shell beam approximately 8.51 m. The shell is a
closed mesh with a downward-open recess in the stern. The aft deck rises over
the wheel and joins both hull cheeks and the teal stern rail. The recess roof,
walls and forward bulkhead are part of the hull shell rather than an attached
cover. The shell has 978 triangles.

The rotor has broad wooden rims, ten paddle boards, spokes and axle hubs.
The chimney has a stepped base and a hollow octagonal mouth. Railings, ironwork,
plank seams, chimney and helm are separate editable mesh groups.

## Assembly and animation

Blender: +X bow, +Y port, +Z up. Unity exports: +Z bow, +X starboard, +Y up.
Units are metres. Fixed FBXs share the ship origin.

Parent `Paddle_Frame` and `Paddle_Rotor` to a socket at Unity ship-local
`(0, 0.35, -10.83)`. Both module meshes use axle-local coordinates. Rotate only
the rotor about Unity local X (Blender local Y). The .blend contains the
assembled hierarchy and a one-turn demonstration over frames 1 through 121.

The nominal wheel radius is 1.62 m and blade width is 4.16 m. Upgrades must fit
the existing recess and axle interfaces; arbitrary larger wheels are not
guaranteed to fit. Test changed rotors through a full revolution.

## Verification and practical limits

All eight mesh groups have zero boundary edges, overconnected edges and
degenerate faces. The separate fittings use intentional mechanical contacts;
the topology check does not claim a boolean union of every fitting.

The independent check samples rotor/hull triangle intersections every 5 degrees
through 360 degrees: all 72 samples pass. Every exported FBX is re-imported and
checked for matching triangle count and a retained `Col` vertex-color layer.
This is sampled clearance verification, not a continuous collision proof.

Total: 5,750 triangles. Omitting the optional `Plank_Seams` object gives a
4,430-triangle simplified variant. Render passes use Blender studio lighting;
Unity shader appearance and device frame time still require in-game review.
No Unity scene, physics data, or production material was changed.

The reference is a stylized multi-view painting; this mesh is a measured visual
interpretation, not a recovered exact source mesh. Review the supplied views
before treating it as final production art.
