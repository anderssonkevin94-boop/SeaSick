# Crew Shelter L1 - V1 Handoff

First art-review version for the camp Hut/shelter, not a production building. Canvas roof and sides, two bedrolls, personal chests, tied entrance flaps, and an unlit lantern. Art staging only; no Unity import.

## Files
- `shelter-state-kit.fbx`: master containing BOTH mutually exclusive entrance states. Do not leave both visible.
- `shelter-open.fbx`: ready-to-view open version, no closed curtain mesh.
- `shelter-closed.fbx`: static closed version, no gathered entrance flaps.
- Source: `tools/blender/source/shelter-astra-lvl1-v1.blend` relative to project root. Saved open.
- `state-contract.json`: entrance-state contract. `validation.json`: topology and bounds checks. PNGs are staged previews.

## Import
One source unit is one metre; fits the existing 4.84 x 4.93 m Hut plot and 3.81 m height limit. Root is at ground level. Blender is Z-up; FBX exports Y-up/-Z forward. Use normal importer axis conversion only. Source entrance faces -Y.
Use the `Col` vertex-color attribute (linear exported colors), a compatible game shader, and imported flat normals. No textures. Do not average normals across the asset.

## Modules
Foundation, Frame, Lower_Walls, Canopy, Canvas_Walls, Ropes, Porch_Details and Lantern are permanent.
Exactly one of Entrance_Open / Entrance_Closed must be visible; default is open. These are mesh swaps, not animated or rigged cloth. The closed curtain is not a physics door.
Bed_Left / Bed_Right and Chest_Left / Chest_Right are separate furnishing modules. Bedrolls remain present when no resident is home; they are not an occupancy counter. Chests are personal decorative props, not camp inventory slots.
Lantern has opaque pale glass and no emission, light, fire, or particle system. Do not infer a night-state controller.

## Markers and Gameplay
Entry and Interior are reference points only. Sleep_Left and Sleep_Right mark bed surfaces; source head direction is +Y. No sleeping characters, animation rig, navmesh, collision, or housing code is included.
Two beds are an art choice matching the current two-bed Hut field; gameplay remains authoritative. Do not rewrite capacity, save data, or resident assignments during import.
Check the threshold, central aisle, character clearance, and ground placement in game. Guy ropes lie outside the living space. Roof/sidewalls intentionally obscure some interior from high angles; no all-angle interior visibility is promised.

## Import Checklist
1. Import master FBX with vertex colors and flat normals.
2. Show Entrance_Open, hide Entrance_Closed.
3. Retain furnished beds/chests unless explicitly changing art state.
4. Implement sleep/occupancy, cloth transitions, collision, and lantern effects separately if wanted.
5. Test walking/bed poses and gameplay camera readability before replacing the runtime prefab.
