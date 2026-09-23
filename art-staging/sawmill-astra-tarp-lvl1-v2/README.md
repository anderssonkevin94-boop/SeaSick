# Level 1 Tarp Sawmill

The sailcloth concept rebuilt around visible production: input rack front-left,
active sawhorses center, output rack front-right. Both racks sit forward of the
canopy. The asset fits the existing 7.56 x 5.85 metre plot and 3.84 height limit.
Blender coordinates: Z up, front -Y. Flat shading and Col vertex colors.

## Deliverables

- `sawmill-tarp-review.fbx`: assembled review with 4 stored logs, 6 planks,
  and a cutting log on the bench. This is staged inventory, not live data.
- `sawmill-tarp-empty.fbx`: empty building with racks and workstations.
- `sawmill-tarp-state-kit.fbx`: every individual stock slot and bench variant.
  All variants are included together for integration, NOT simultaneous display.
  Hide all stock and bench variants before initializing from actual game state.
- Blender source opens with the same staged review; alternative bench variants
  and unused inventory slots are hidden, not deleted.
- `state-contract.json`: names, review states and production-state rules.

## Integration Contract

Input_Log_01 through 06 and Output_Plank_01 through 12 are individually toggleable,
with centered pickup pivots under Input_Container and Output_Container.
These are display slots, not new gameplay capacities. Larger inventories need
an explicit capacity/display decision or a count indicator; do not silently cap
the visuals and present them as an exact total.

Bench_Anchor holds mutually exclusive Bench_Loaded, Bench_Cutting, Bench_Finished
and a separate Saw_Tool. Hide all three variants for an empty bench. A finished
bench displays one board, matching the current recipe's one-board yield.
Do not start a job without input stock. Loading removes that log from input.
Output-full retains finished work on the bench and blocks the next job.

Roof, stored resources and workshop fixtures are not baked into one mesh.
No production code, Unity prefab, recipe, navmesh or animation clips were changed.
Resource transfer and work animations still need runtime implementation.

## Validation

Manifold/degenerate-face checks cover each mesh, including rope loops. Bounds
are checked against the current build plot. Sampled rays from both racks toward
three front orthographic camera angles have zero canopy hits. This is not a
guarantee of unobstructed storage from every possible orbit; rear views may need
roof fading. Front renders include empty, ready, working, output-ready and
output-blocked states. FBX files are independently reimported and checked.
Blender Workbench previews do not verify Unity lighting or iOS performance.
