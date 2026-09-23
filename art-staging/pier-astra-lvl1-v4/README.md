# Pier L1 V4 - Whole-Metre Runtime Kit

Supersedes V3 for integration. No Unity files changed or imports performed. Broad offset planks, four chunky support pairs in the 15m review, hanging lantern and a dedicated sea-end cleat.

## Confirmed Coordinate Contract

- Deck width: exactly 3m outer envelope, Y=-1.5..1.5. Individual board ends remain staggered inside this envelope.
- Walking surface: local Z=1.2m above each deck module origin. Peg tops are flush.
- Deck root pivot: land end, at local water level; Blender +X points toward the sea, +Y spans the width, +Z is up.
- Snap_Land=(0,0,0), Snap_Sea=(length,0,0). Deck_Land/Deck_Sea mark the walking surface at Z=1.2.
- FBX is exported -Z forward/Y up. Map imported axes deliberately; the coordinates in JSON are Blender coordinates.

IMPORTANT: V3 had its walking surface at local zero. Do not add another 1.2m offset to V4. Place these module roots at mean water level. If retaining the existing runtime Pier root at deck height and centered along length, compensate on its art child: lower that child by 1.2m and shift it half the pier length toward land. Do not change the logical berth/SeaEnd/registry origin accidentally. The adapter owns this conversion.

## Modules

| FBX | Triangles | Use |
| --- | --- | --- |
| Pier_Deck_1m | 480 | Supported 1m bay, post notches and crosshead |
| Pier_Infill_1m | 340 | Unnotched 1m bay, no posts |
| Pier_Sea_End_1m | 1136 | Supported 1m end with lantern, fascia and cleat |
| Pier_Deck_2m | 648 | Supported 2m bay |
| Pier_Infill_2m | 508 | Unnotched 2m bay |
| Pier_Sea_End_2m | 1304 | Supported 2m end with lantern, fascia and cleat |
| Pier_Piling_1m | 28 | Plain octagonal stretchable shaft |
| Pier_Pile_Cap | 128 | Unscaled upper post/cap and iron band |
| Pier_Shore_Ramp | 352 | Deck-edge hinge with adjustable span |
| Pier_Ramp_Foot_Pad | 176 | Separate horizontal shore landing |

Snap whole-metre deck lengths using 1m and 2m pieces. Use one sea-end module at the end; all six deck options obey the same height/width convention. Supported bays REQUIRE separately placed caps and shafts at their Pile_Left/Right markers. Infill bays have no notches. Do not put caps through infill boards.

The 15m preview uses supported bays at X=0,4,8,13, infill between them, with a 1m infill at X=12. It is a visual layout, not structural engineering. For other lengths, choose support spacing deliberately rather than adding a pair every metre. Preserve the game's whole-metre snapping, maximum length and costs.

## Shaft and Separate Cap

Pier_Piling_1m is exactly 1m long along local -Z. Origin and Top are at (0,0,0), Bottom is (0,0,-1). Radius is 0.23m; no cap, fittings or band are embedded in the shaft.

Supported module attachment markers are (0.4,+/-1.24,0.46). Place BOTH shaft origin and cap origin at each marker. Scale only the shaft's local axial dimension to reach the sampled seabed plus the intended embed depth. Do not scale a common parent containing the cap.

Pier_Pile_Cap begins at its own local Z=0 and ends at Z=1.289. Once placed at Z=0.46 its top is Z=1.749, about 0.55m above the deck. Its lower portion completes the heavy upper post through the deck notch. Preserve unit scale. The two pieces meet at the shaft top plane.

## Adjustable Ramp: Fixed 2.4m Horizontal Run

A rigid 2.4m-long ramp cannot rotate to a 2.5m drop, nor retain a fixed 2.4m horizontal run as it rotates. This version provides an articulated AND length-adjustable span, supporting the entire requested 0.3-2.5m drop range. No three fixed variants are needed.

Hierarchy:

    Pier_Shore_Ramp                origin at land-end water level
      Ramp_Hinge                  local (0,0,1.2), actual deck-edge pivot
        Ramp_Span                 local origin zero, length-adjustable
          Ramp_Deck               meshes; neutral span extends toward -X
          Pier_Shore_Ramp__Toe     local (-2.4,0,0)

For a vertical DROP d from deck to pad walking surface, in Blender coordinates:

    hinge.rotation_Y = -atan2(d, 2.4)
    span.scale_X = sqrt(2.4^2 + d^2) / 2.4
    span.scale_Y = span.scale_Z = 1
    foot_pad.position = (-2.4, 0, 1.2 - d)
    foot_pad.rotation = identity
    foot_pad.scale = (1,1,1)

Equivalent slope angle range is about 7.13 to 46.17 degrees. Actual span length varies from 2.419 to 3.466m. Only Ramp_Span is scaled longitudinally; never rotate the entire water-level root, scale the foot pad, or stretch the ramp width/thickness. Board lengths and gaps intentionally expand longitudinally with the span; this is an adjustable art mesh, not a mechanical telescoping animation. Use corresponding imported local axes in Unity and verify using the Toe marker.

The 2.5m-wide foot pad is an independent asset: its origin is the ramp-to-pad contact at its TOP surface, not water level. It extends 0.64m farther landward along -X. This keeps the pad horizontal while the ramp slopes. Seat the pad/bearers on terrain; no beach mesh or automatic terrain adjustment is included. Reserve 3.04m total horizontal shore reach including the pad. Negative world elevations can occur at large drops; shoreline validity remains game logic.

Accessory pivots intentionally differ from deck roots: the shaft is top-origin, cap is shaft-join-origin, pad is walking-surface contact-origin, and the ramp's movable hinge is deck-edge-origin inside a water-level root.

## Mooring and Lantern

Both sea-end modules contain a cleat near the outer port-side corner. Mooring_Tie marker is (length-0.23,-1.12,1.38). It is the rope attachment point, NOT the ship berth center. Keep existing berth clearance and docking logic authoritative. No rope simulation is supplied.

Light_Source is (0.4,0.58,2.50). Glow panes have an amber emissive Blender material. Review scene includes a warm point light, but FBXs export only mesh/empty objects; Claude must add the game light at the marker and assign an appropriate emissive shader to the glow mesh. The cap beneath the lantern is still a separate required accessory.

## Validation and Sources

All ten FBXs were reimported and checked for manifold edges, nonzero face area, flat normals, vertex colors and markers. Ramp hierarchy and toe positions were checked after reimport at drops 0.3, 0.5, 1.2, 2.0 and 2.5m. Deck outer width is measured at 3m. These tests do not validate Unity shaders, colliders, pathfinding or seabed fitting.

- `validation.json`: mesh bounds and counts.
- `fit-verification.json`: ramp endpoint calculations and width contract.
- `export-verification.json`: FBX round-trip results.
- `snap-contract.json`: marker coordinates and assembly equations.
- Blender source: ../../tools/blender/source/pier-astra-lvl1-v4.blend
- Generator: ../../tools/blender/pier_astra_lvl1_v4.py

The saved Blender opens with the visible 15m review. Water, camera, light and review duplicates are not in the FBXs. Materials use flat shading and Col vertex colors. Keep the existing game vertex-color shader and do not smooth all normals. V1-V3 are retained but should not be mixed with V4 because the height convention changed.
