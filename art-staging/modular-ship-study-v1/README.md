# Modular Ship Study 01

Offline Blender prototype, 24 September 2026. Not imported into Unity.
The approved V8 ship remains unchanged. No gameplay files, scenes, shaders,
ProjectSettings, or production assets were edited by this study.

## Review

- A-timber: the standard timber rotor, closely following the approved stern.
- B-reinforced: iron-faced timber rotor. Same radius, axle, carrier and housing
  as A; the rotor alone can be exchanged.
- C-oversized: 33% greater nominal diameter, with the same paddle width and
  axle position. Requires the M1-L stern pocket, housing, rail crown and carrier.
  It is NOT compatible with the stock M1 pocket.
- C-oversized-deck: C plus a partial raised working deck, supports, stairs,
  relocated helm and four provisional cannon sockets.

Each folder contains an assembled `ship-study.blend`, separate FBX meshes in
`models/`, four actual Blender renders, `manifest.json`, and `validation.json`.
Views: `ship.png`, `stern.png`, `rear.png`, `side.png`.
Open a blend to inspect the assembly. Frames 1-120 preview the rotating wheel.
Renders use Workbench vertex colors, not the Unity shader or lighting.

## Geometry Budget

| Part | Triangles |
| --- | ---: |
| Timber rotor A | 912 |
| Reinforced rotor B | 1,616 |
| Oversized rotor C | 1,616 |
| Each carrier | 260 |

The old V8 rotor had 5,408 triangles. The replacement rotors use flat shading,
24-segment rims and 12 paddles. The complete review ships retain the existing
high-detail V8 hull and fittings, roughly 31-32k triangles; these are NOT new
mobile LOD targets. Increasing wheel diameter adds no triangles in this study.
Exact per-part and total counts are recorded in each validation report.

## Coordinates and Assembly

Dimensions retain the V8 authoring scale. Do not assume the numbers have been
calibrated to final game metres or crew size.

- Blender: +X bow, +Y port, +Z up.
- Rotor and Carrier mesh origins: axle centre, installed at (-10.83, 0, 0.35).
- Blender rotation: rotor local Y. Carrier stays fixed.
- FBX: existing V8 export convention, bow becomes game +Z, up game +Y.
- Game socket position: (0, 0.35, -10.83), rotor rotation about game X.
- All other FBXs use the hull origin and should be installed at zero.
- Rotor and Carrier FBXs are axle-local; their Blender placement is deliberately
  not baked into the FBX. Position both at the named wheel socket.
- Partial-deck helm relocation IS baked into its fixed mesh.
- Socket empties remain in the blend; their data is also in `manifest.json`.
  Individual mesh FBXs do not carry socket empties.

## Compatibility Contract

| Mount | Nominal rotor radius | Paddle width | Pocket ceiling | Pocket forward wall |
| --- | ---: | ---: | ---: | ---: |
| M1 | 1.62 | 4.16 | 2.17 | X = -8.95 |
| M1-L | 2.15 | 4.16 | 2.72 | X = -8.40 |

Ceilings are absolute Blender Z. Swept radii include paddle thickness:
approximately 1.62223 and 2.15168. Minimum overhead pocket clearance is
approximately 0.198 and 0.218 authoring units respectively.

M1-L grows only the stern wheel accommodation, not the whole ship or deck.
The working deck stays flat. The oversized wheel extends farther below the
existing hull; immersion, draft, thrust, engine power and stability are not
simulated or approved. It is a visual/mechanical fit prototype, not a speed claim.

The housing is still joined to the hull shell in this first study. Production
modularity will require extracting a watertight stern module with a shared
forward interface. Hull length/beam/draft segments are not built yet.
Do not use arbitrary non-uniform scaling to create those variants.

## Partial Deck

The optional deck covers only part of the ship. Its walking plane is Z = 4.42;
the main working deck is Z = 1.76. Stair treads connect those heights.
Four cannon-location empties mark possible edge slots with a central passage.
These are layout placeholders, not tested cannon fit, recoil, railing cutouts,
crew navigation or working gameplay placement. The stern service area stays
separate from the raised deck. Cannon ports and navigation follow asset approval.

## Verification and Limits

- Closed manifold edges, no overconnected edges or degenerate faces per mesh.
  Joined objects can contain separate fitted solids; this is not proof of one
  physically continuous solid or elimination of all internal contact faces.
- Rotor checked against all fixed meshes at 72 angles, every 5 degrees.
- Carrier checks exclude the inner 0.55 radius for intentional bearing contact.
- No detected sampled wheel surface intersections. Sampling is not a continuous
  collision proof and BVH surface tests do not diagnose every containment case.
- Separate export verifier checks all FBX triangle/vertex counts, mesh bounds,
  vertex-color presence, zero export origins, linear wheel animation, and that
  A/B non-rotor geometry is identical.
- No iPhone frame-time, draw-call, physics, navigation or Unity visual validation.

## Reproduce

From the SeaSick project root, with Blender 5.1.1:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/modular_ship_study_v1.py -- A-timber
```

Repeat for `B-reinforced`, `C-oversized`, and `C-oversized-deck`.
Then run `tools/blender/verify_modular_ship_study_v1.py` using the same Blender
background command. All output stays under this staging directory.
Source dependency: `tools/blender/source/stern-paddle-astra-v8.blend` and its
existing generator/helpers. Do not overwrite that approved baseline.

## Next Decision

Approve the wheel family and partial-deck proportions before extracting modular
hull sections. Next proof should compare short/broad and long/narrow hulls with
fixed-width seam standards and separate bow/stern adapters. Mass and propulsion
rules belong in gameplay data, independent of the visual mesh variants.
