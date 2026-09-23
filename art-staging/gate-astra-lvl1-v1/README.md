# Gate L1

Staged art only. Nothing imported into Unity. Matches the approved palisade timber palette, faceted topology and 2.6m wall silhouette; taller gateposts identify the entrance. No roof, flags or extra props.

## Deliverables

- `Gate_L1.fbx`: complete closed gate with separate posts, lintel, frame hardware and two hinged leaves. 2700 triangles total.
- `../../tools/blender/source/gate-astra-lvl1-v1.blend`: saved review scene with neighboring palisades visible.
- `../../tools/blender/gate_astra_lvl1.py`: reproducible generator.
- Closed exterior/interior, open interior and palisade-context PNGs.
- Validation, export-verification and snap-contract JSON files.

## Placement

Units are meters. Blender X runs along the wall, +Y is camp interior, Z is up. Ground/root center is (0,0,0). Export uses -Z forward/Y up; use imported markers and transforms rather than copying Blender coordinates into Unity.

`Gate__Snap_Start` = (-1.5,0,0), `Gate__Snap_End` = (1.5,0,0). Nominal socket span is 3m. Posts are centered at X +/-1.28 and extend 0.35m below ground. Height 3.604m, clear width between posts 2.12m, lintel underside 3.085m. Lintel overhang extends to X +/-1.6. Snap palisade boundary sockets to the gate markers; do not add terminal stakes on top of the gateposts. Adjacent palisades shown in the blend are review-only and excluded from FBX.

Current BuildPlans.Gate has a 2m placeholder footprint; current RaiseWall accepts arbitrary segment length with local Z along the wall. This is NOT a drop-in 2m mesh. Reserve a 3m segment and adapt orientation using markers, or supply a separately designed narrow variant. Do not stretch the gate. Costs and placement rules remain game-owned and unchanged. Uneven endpoints need terrain preparation or a dedicated slope adapter, not shear.

## Opening

`Gate_Leaf_Left` is parented to `Gate_Hinge`; `Gate_Leaf_Right` is parented to `Gate_Hinge_Right`. Both include their moving straps, hinge barrels and handle. Origins are Blender (-1.02,-0.11,0) and (1.02,-0.11,0). Closed rotation is zero. Open left by -100 degrees and right by +100 degrees about Blender local Z; both swing OUTWARD into -Y. Use the corresponding imported local up axis in Unity, verified visually. Animate hinge parents, not mesh origins.

Reserve at least 1.15m radial clearance around the hinge on the outward side. No baked animation clips are supplied. Both leaves were checked against each other and frame posts/lintel for surface intersections at 5-degree increments across the opening range; this is a sampled art check, not continuous physics validation. Inspect colliders and traversal in-engine. Hinge fittings intentionally meet at their mechanical axis.

Current game behavior describes friendly traversal and hostile blocking; the existing gate is only a frame, with a selection collider retained on breached segments. The art does not implement faction traversal, opening triggers, colliders, navmesh updates or sounds. Keep the selection collider separate from movement obstruction. Synchronize leaf rotation and blocker state in the game adapter.

For the existing breached visual contract, hide Gate_Leaf_Left, Gate_Leaf_Right, Gate_Lintel, Gate_Frame_Hardware and Gate_Frame_Hardware_Right; keep Gate_Posts. No unique splintered destruction mesh or debris is supplied.

## Shading and Verification

All meshes are flat shaded with `Col` vertex colors and no textures. Assign the existing game vertex-color shader; do not rely on automatic FBX material conversion. Flat edges are intentional; do not smooth normals globally.

FBX was reimported and passed manifold-edge, nonzero-area, flat-normal, vertex-color, marker-presence and leaf-parent checks. Integration and gameplay tests are still required. Saved blend uses Workbench review lighting and opens with the closed gate and palisade context.
