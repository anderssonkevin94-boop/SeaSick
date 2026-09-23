# Palisade Kit V2 - Runtime Contract Revision

Use this folder instead of V1. Includes copies of the matching V2 gate exports, so this is the complete handoff. Staged only: no Unity scripts, scenes or pier files changed.

## Pieces

| File | Length | Triangles | Contents |
| --- | --- | --- | --- |
| Palisade_Run_1m_A.fbx | 1m | 368 | Four stakes at 0.25m pitch; two rear rails |
| Palisade_Run_1m_B.fbx | 1m | 368 | Different baked heights and lean |
| Palisade_Run_1m_C.fbx | 1m | 368 | Third height/lean pattern |
| Palisade_Filler_050m.fbx | 0.5m | 196 | Two stakes and rear rails |
| Palisade_Filler_025m.fbx | 0.25m | 110 | One stake and rear rails |
| Palisade_Post.fbx | 0.4m footprint | 132 | 0.38m square shaft, 0.4m cap, 2.91m high |
| Palisade_Breached_1m.fbx | 1m | 328 | Four jagged stumps, including leaning remnants; short broken rail ends |
| Gate_L1.fbx | 3m | 2708 | Square posts, lintel and two articulated leaves |
| Gate_Breached_3m.fbx | 3m | 264 | Same two square posts; no leaves, lintel or hinge hardware |

No fixed-angle corner pieces are required. Full-height stake range is 2.39-2.59m. Stake bases extend to -0.08m for shallow embed. Post base extends to -0.08m; no terrain fitting is supplied.

## Axes and Pivots

Meters. In Blender, +X follows the wall, +Y is the rear/rail side, +Z is up. Every module root is at its START end on ground, not its midpoint. FBX uses -Z forward/Y up; preserve the imported hierarchy and map its axes deliberately to the runtime wall's local Z. Do not treat Blender Z-up values as Unity coordinates verbatim.

Each run/filler has unique `<Part>__Snap_Start` at (0,0,0) and `__Snap_End` at (length,0,0). Stake centers are 0.125 + i*0.25m. Rail ends are exactly X=0 and X=length, with no extra endpoint stakes. All A/B/C variants tile together at the same pitch. Stake lean remains inside the nominal longitudinal bounds.

The standalone post also has a start-edge root: its center marker `Palisade_Post__Post_Center` is (0.2,0,0). Place that CENTER marker on each bend/end point; do not put the root directly on the bend. Shaft occupies X=0.01..0.39 and Y=-0.19..0.19, cap X=0..0.4 and Y=-0.2..0.2. Run depth is bounded by Y=-0.14..0.183 including fittings, so the post is wider than the run. Square posts do not change with the bend angle.

## Arbitrary Lengths and Bends: Adapter Work Still Required

The game supports any real segment length from 0.5 to 12m. A finite collection of 1m/0.5m/0.25m meshes cannot exactly tile, for example, 1.37m. Do not round the saved game length or stretch the gate to conceal this mismatch.

For exact quarter-metre lengths, concatenate runs and fillers. For a residual shorter than 0.25m, use an additional filler and clip its triangles to the exact endpoint plane, cap the cut faces and preserve vertex colors. Alternatively, rebuild that final stake/rail section procedurally. Do this once when constructing the visual, not every frame. The modular rear rails remain separate mesh objects to simplify fitting. Gameplay costs remain based on the original segment length, never visual stake count.

At an arbitrary bend, place the post center on the node, then fit each incident run against the post's oriented square SHAFT footprint. A single centerline offset does not fit the full width of an angled rail. Clip/subtract the footprint from the terminal stake/rail geometry, with the seam slightly inside the post to hide numerical gaps. Avoid clipping against the wider cap, which is above the rails. Keep a consistent interior rail side along the boundary. Very acute or reversing incident runs can overlap each other beyond any small post; those layouts need the wall adapter's overlap/merge policy. No finite post can hide every possible near-reversing angle by itself.

This pack supplies the requested geometry, not the runtime trimming/angle adapter. The existing endpoint/selection/repair logic must be retained. Do not claim arbitrary-angle integration is complete merely by swapping FBXs.

## Breached Segment

Palisade_Breached_1m has the same start origin and nominal 1m span. It represents damaged material in the missing middle third, not the whole original segment. Keep intact portions on either side; tile/trim this damaged module over the exact middle-third interval. A segment's third will often not be a quarter-metre multiple, so use the same residual clipping policy. Stumps are decorative and have no colliders. Navigation and damage state remain game-owned; do not obstruct the breach with the original wall blocker.

## Gate

The gate root is X=0 at ground; end is X=3. Built-in post centers are X=0.2 and 2.8. Do not add standalone posts over them. Height is 3.57m; clear shaft-to-shaft opening is 2.22m. Gate__Snap_Start=(0,0,0), Gate__Snap_End=(3,0,0), Gate__Passage=(1.5,0,0).

Gate_Leaf_Left is under Gate_Hinge at (0.48,-0.11,0). Gate_Leaf_Right is under Gate_Hinge_Right at (2.52,-0.11,0). In Blender local Z, closed is zero, left open is -100 degrees, right open is +100 degrees. Both swing outward toward -Y. Reserve at least 1.15m radial sweep per hinge and test the actual navigation/collider setup. Hinge pivots intentionally differ from the module's start-end pivot.

Gate_Breached_3m replaces the whole intact gate at the exact same transform. Both posts remain, leaves and lintel are absent. No baked animation clips, gate controller, collider, VFX or destruction physics are included.

## Shading and Validation

Same warm timber/cutwood palette as V1. Flat normals, Col vertex colors, no textures. Use the game's vertex-color material; preserve flat shading. Rear rails use wooden pegs instead of the V1 rope loops to keep depth within the square-post envelope.

All nine FBXs passed reimport geometry checks: manifold edges, nonzero face area, flat normals, vertex colors and expected markers. Gate leaf parenting was checked; both leaves were tested against posts/lintel and each other at 5-degree increments. These are sampled art checks, not in-engine physics certification. Run source checks enforce no geometry extends beyond each nominal X interval. Arbitrary-length clipping and arbitrary-angle joins have NOT been implemented or verified in Unity.

## Sources

- ../../tools/blender/source/palisade-astra-lvl1-v2.blend
- ../../tools/blender/source/gate-astra-lvl1-v2.blend
- ../../tools/blender/palisade_astra_lvl1_v2.py
- ../../tools/blender/gate_astra_lvl1_v2.py
- Gate previews and verification: ../gate-astra-lvl1-v2/

The Blender files contain visible review layouts; the FBXs retain local start origins. No review cameras are exported. V1 files remain available but are superseded for this handoff. Rebuilding the gate source writes its own folder; refresh the two gate copies in this package after regenerating it.
