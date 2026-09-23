# Farm L1 - V1 Handoff

Six raised wheat beds, a low canvas-covered tool station, a decorative water butt and seed box, and a removable harvest display. First art-review version; not imported into the game.

## Files
- `farm-state-kit.fbx`: master with ALL crop variants. They overlap until initialized; never show all at once.
- `farm-bare.fbx`: static bare beds, no crops or harvested stock.
- `farm-ripe.fbx`: static fully mature crops and full harvest basket.
- Source: `tools/blender/source/farm-astra-lvl1-v1.blend` relative to project root. Opens in a deliberately mixed-stage art-review pose, not live game state.
- `state-contract.json`: stage and output rules. `validation.json`: source mesh checks and actual bounds. PNGs are art previews.

## Import and Height Change
One source unit is one metre. Fits the existing Farm footprint of 4.66 x 4.69 m. Root is ground level. Blender Z-up; FBX Y-up/-Z forward. Source front is -Y. Use normal importer conversion only.
IMPORTANT: the tool canopy makes this roughly 2.02 m tall, above the old Farm ridge metadata of 1.01 m. Review placement, selection, culling and building-height metadata against the actual validated bounds before integrating. Do not vertically compress the art to the old ridge.
Use `Col` vertex colors (linear export), a compatible game material, and imported flat normals. No textures or automatic smoothing.

## Crop State
Each Bed_01..06 has permanent `_Soil` plus mutually exclusive `_Sprout`, `_Growing`, `_Ripe` meshes. Bare = all three crop meshes hidden. Soil includes the timber edging and is always visible.
These are visual stages, not new production rules. Bind them to actual standing crop/harvest/regrowth state. The existing farm expects six beds. Use the supplied Bed_01_Anchor..Bed_06_Anchor transforms rather than assuming the old regular bed spacing fits this arrangement.
Do not spawn the previous farm's procedural crop meshes on top of these crop variants. No crop animation, growth clock, wind shader, harvesting code or navmesh is provided.

## Harvest Display
Harvest_Basket is always present. Harvest_Sheaf_01..04 are independently removable output slots. Empty = all four hidden. Display slots are not storage capacity; map actual harvested Food to them explicitly. Standing ripe crops and harvested basket stock are distinct states and must not be double-counted.
Seed_Box and Water_Butt are decorative props, not simulated inventories or new seed/water requirements. The water butt is empty geometry, with no water surface or fluid shader.

## Markers and Limits
Bed anchors are crop centers, NOT worker feet positions. Harvest_Anchor is inside the basket; Worker_Approach is a suggested approach only. Paths, collision, crop interactions and picking distances need gameplay validation. The compact row gaps are not claimed to accommodate a full character capsule; use bed-edge interactions or adjust navigation deliberately.
The canopy covers only the rear tools, not the six crop beds or front harvest basket. Inspect from actual gameplay cameras. Roof ropes and materials are static geometry.

## Initial State
Import master, show all Soil objects, hide all crop variants and Harvest_Sheaf objects, then populate them from authoritative game state. The mixed stages in the Blender file are for review only.
