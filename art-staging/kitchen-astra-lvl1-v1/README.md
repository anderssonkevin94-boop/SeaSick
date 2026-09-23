# Kitchen L1 - V1 Handoff

First art-review version. Canvas prep shelter, open-front stone stove, iron cauldron, hanging utensils, towel, ingredient crate, and serving shelf. Staged art only, not imported into Unity.

## Files
- `kitchen-state-kit.fbx`: master with all inventory and work-state meshes. Initialize visibility; do not show every work state simultaneously.
- `kitchen-empty.fbx`: static empty/cold illustration with input/output/work meshes omitted. Use master for a functional refillable kitchen.
- `kitchen-review.fbx`: stocked cooking illustration, not a gameplay state controller.
- Source: `tools/blender/source/kitchen-astra-lvl1-v1.blend` relative to project root; saved in stocked cooking review state.
- `state-contract.json`: work/stock rules. `validation.json`: source bounds, topology checks and pot-to-canopy clearance. PNGs are art previews.

## Import
One source unit is one metre; fits Kitchen's 6.26 x 6.12 m plot and 4.18 m height allowance. Origin at ground level. Blender Z-up; FBX Y-up/-Z forward; source front is -Y. Use normal importer axis conversion only.
Use `Col` vertex colors (linear FBX export), a project vertex-color material, and imported flat normals. No texture dependencies. Do not smooth all normals.

## Modules and State
Permanent: Frame, Canopy, Prep_Bench, Utensils, Hearth, Burn_Logs, Cooking_Pot, Input_Crate, Serving_Shelf, Sign.
Input_Food_01..04 are removable ingredient groups. Output_Meal_01..06 are removable filled bowls. Slots are visual, not capacity or recipe yield. Food is represented generically as sacks/vegetables; no extra ingredient resource requirements are implied.
Work_Preparing, Work_Cooking, Work_Finished are mutually exclusive visual states, or all hidden when idle. Preparing puts ingredients on the board; Cooking fills the pot; Finished places a meal on the prep bench awaiting transfer. Displaying Finished deliberately leaves the pot empty.
Start only with real input Food; remove claimed input when starting. Finish existing work if the input crate empties. If output storage is full, keep finished work on the bench and do not start another job. Transfer without counting the same meal in both places. Existing game recipes, rates and inventory remain authoritative; no job code is supplied.
Inventory meshes have centered geometry pivots. Work and structure modules retain authored root-space placement. Record resting transforms before moving stock.

## Fire and Interaction
No solid flames, smoke, steam, particles, animation or dynamic lights. Burn_Logs are static charcoal geometry, not additional simulated fuel. Fire_Anchor and Steam_Anchor reserve VFX positions. Hearth/pot lie entirely in front of the canopy; tune effect spread so particles do not clip through it.
Input_Anchor, Output_Anchor, Prep_Anchor and Worker_Stand are references, not navigation targets or validated character poses. Check reach, collision, worker paths and terrain footing during integration. The pot lid is intentionally absent so cooking state is readable.
Input and serving areas are outside the tarp. Confirm their visibility from actual gameplay cameras; no all-angle visibility guarantee is implied.

## Initial Setup
Import master; hide Input_Food_, Output_Meal_ and Work_ meshes, then populate from game state. Keep container structures visible at zero stock. Add external VFX separately. Test empty, preparing, cooking and output-blocked states before replacing the runtime prefab.
