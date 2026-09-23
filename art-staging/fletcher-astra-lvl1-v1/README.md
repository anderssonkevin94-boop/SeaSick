# Fletcher L1 - Astra V1

Staged art only. Not imported into Unity. First fletcher version.

## Preferred Source

- `fletcher-state-kit.fbx`: integration master. Contains ALL input/output slots and mutually exclusive work variants. Initialize visibility explicitly; never show all work variants together.
- `fletcher-empty.fbx`: empty/idle static reference. Does not contain the stock meshes, so is not the interactive master.
- `fletcher-review.fbx`: four timber slots, three arrow bundles and Shaping. Static visual example, not a gameplay save.
- Source: `../../tools/blender/source/fletcher-astra-lvl1-v1.blend`.
- `state-contract.json`: visibility/state guidance.
- `validation.json` and `export-verification.json`: source and FBX round-trip checks.
- PNGs show front, both three-quarter views, gameplay scale and empty/shaping/fletching/output-full examples.

## Scale And Rendering

One Blender unit is one meter. Ground-level root at the plot center, Blender Z-up; FBX -Z forward / Y up. Fits the existing 4.84 x 4.93 m Fletcher plot and 3.2 m height allowance. Exact bounds in validation.json. No review camera in FBX.

Single vertex-color material using `Col`, flat normals, no textures. Preserve hard normals and vertex colors on import; use the game's vertex-color shader. No fire, particles, lights or other VFX included.

## Modules

Permanent: Frame, Canopy, Shaving_Horse, Workbench, Hand_Tools, Feather_Supplies, Bow_Rack, Display_Bows, Timber_Cradle, Arrow_Stand, Sign.

- `Input_Timber_01` to `05`: individually removable logs.
- `Output_Arrows_01` to `04`: independently removable bundles of three arrows, one bundle per stand compartment.
- `Work_Shaping`: blank clamped on the shaving horse.
- `Work_Fletching`: shafts and loose fletching on the bench jig.
- `Work_Finished`: completed arrows on the bench.

Stock-mesh origins are centered. They are display slots, not authored crew grip poses. Shaving_Horse includes a static treadle/clamp; it is not rigged or animated. Bows, feathers, glue and hand tools are decorative workshop equipment, not extra input resources or produced bows. Empty stock storage remains visible.

## Gameplay State Contract

Use the existing Timber-to-Arrows recipe, timing and yield. The five logs and four bundles are normalized fill indicators, NOT inventory capacity or batch quantity. Three modeled arrows in a bundle and two on the workbench do not define recipe yield. Economy code remains authoritative.

1. Idle: show actual inventory fill, no Work_* variant.
2. Shaping: claim input once, update displayed timber, show only Work_Shaping.
3. Fletching: show only Work_Fletching. Input reaching zero does not cancel an already claimed job.
4. Finished/output blocked: show only Work_Finished; retain it if output cannot accept the job and do not start another job.
5. Transfer: atomically remove the work display and update output bundles. Never duplicate one job in both displays.

These phases are optional visual stages of the current job, not new intermediate resources. No inventory or production code supplied.

## Markers And Integration

Input_Anchor and Output_Anchor identify stock displays. Shaping_Anchor and Fletching_Anchor identify work surfaces. Worker_Stand is only a standing reference; it is not a tested navigation or seated shaving-horse pose. All are exported with the root and use its local coordinate system.

Claude still needs to build the prefab, map materials, add conservative colliders/navigation, implement visibility and inventory mapping, and validate actual crew approaches and animation poses. Replace/disable the old procedural visual to avoid double geometry. Review thin strings and shafts at the actual game camera; no LODs are supplied.

Source checks cover footprint, roof/frame clearance, flat normals, manifold edges and nonzero face area. FBX checks repeat topology/color/state checks after reimport. These are not in-engine performance, collision or animation tests. Constructed modules intentionally contain separate connected or touching components rather than boolean-unioned solids.
