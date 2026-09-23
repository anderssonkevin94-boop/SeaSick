# Storage Hut L1 - V1 Handoff

First art-review version. Not imported into the game. Roof uses canvas, matching the level-one family. This is the camp Storage plan, not the home Storehouse.

## Files
- `storage-state-kit.fbx`: master import, with all removable stock. Initialize visibility from real inventory; all stock is included for review.
- `storage-empty.fbx`: static empty version without stock meshes. Use the master for refillable storage.
- Source: `tools/blender/source/storage-astra-lvl1-v1.blend` from project root.
- `state-contract.json`: exact stock names and integration limitations.
- `validation.json`: source topology and bounds checks.
- PNGs: staged art previews, not gameplay integration.

## Import
One source unit is one metre. Fits the existing 6.46 x 5.14 m Storage plot, below 3.84 m. Root is at ground level. Blender is Z-up; FBX exports Y-up/-Z forward. Apply normal importer axis conversion only. Source front is -Y.
Use `Col` vertex colors (linear FBX colors), imported flat normals, and a project vertex-color material. No image textures required. Do not smooth all normals.

## Inventory
- Permanent modules: Platform, Frame, Canopy, Ropes, Front_Racks, Shelving, Sign.
- Stock_Timber_01..05: removable logs in the left front rack.
- Stock_Boards_01..08: removable planks in the right front rack.
- Stock_Food_01..03: removable sacks on the left shelving.
- Stock_Cargo_01..02: generic removable crates on the right shelving. Resource binding is intentionally unspecified.
- All stock has centered geometry pivots and retains its authored resting placement. Save resting transforms before moving or animating it.
- Start empty: disable every Stock_ mesh. Populate according to authoritative inventory. Display slots are not gameplay capacity; define count mapping separately.
- The front racks remain outside the canopy. Interior shelves may be partly occluded at high camera angles; no all-angle inventory readability is claimed.
- Not every game resource has dedicated art yet. Do not invent ore, stone, tools, arrows, bricks, or meal counts from the generic crates. Add explicit mappings or additional displays in integration.

## Interaction
Entry marks the central approach; Storage_Anchor is a reference inside the shelter. Markers are not validated navigation destinations. Check step traversal, worker clearance, collisions, terrain footing, and camera readability in game. Roof ropes run beside the shelter, not across the front aisle.
This building stores resources; there is no processing bench, recipe, production animation, inventory script, or Unity prefab included.
