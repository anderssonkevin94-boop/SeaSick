# Campfire L1 - V2 Handoff

V2 supersedes V1. Same approved campfire design, with all solid flame geometry removed.
Art staging only: this task does not import assets into Unity or change gameplay.

## Files

- `campfire-state-kit.fbx`: recommended master import. Contains the permanent model, three removable fuel logs, optional ember geometry, and markers. Initialize visibility explicitly; FBX visibility is not a runtime state controller.
- `campfire-cold.fbx`: static cold preview with a full fuel cradle; no embers or flames.
- `campfire-empty.fbx`: static cold preview with an empty fuel cradle. Not the master for a refillable implementation because stock meshes are omitted.
- Blender source: `tools/blender/source/campfire-astra-lvl1-v2.blend` relative to the project root. Opens in a cold, stocked review state.
- `state-contract.json`: state and inventory contract.
- `validation.json`: source geometry counts and bounds.
- Review PNGs are art previews, not screenshots of integrated gameplay.

## Scale and Materials

One authored unit is one metre. The model fits the existing 3.13 x 1.78 m plot and stays below 0.84 m tall. Root origin is ground level.
Blender source is Z-up. FBX is exported with Y-up and -Z forward; use the engine's normal FBX axis conversion, not an extra manual rotation. The seat is on source +X and fuel is on source -X.
Import authored normals and retain flat shading. Colors are stored in the `Col` vertex-color attribute, exported as linear color. Use a vertex-color-aware game material; there are no texture dependencies. Do not replace the mesh normals with averaged smooth normals.

## Modules and States

- Permanent: `Hearth`, `Cooking_Frame`, `Cooking_Pot`, `Seat`, `Fuel_Cradle`.
- `Burn_Logs`: separate charred logs inside the hearth. These are not storage slots; a future fuel-consumption system may control them separately.
- `Fuel_Log_01`, `Fuel_Log_02`, `Fuel_Log_03`: independently removable logs in the cradle. Display 0, 1, 2 or 3 logs by enabling the first N slots. The asset does not impose a three-unit gameplay capacity. Define how actual inventory maps to these display slots.
- `Embers`: optional orange coal geometry. Hide when cold; optionally show for ember-only or burning states. It is not an emissive material or particle system.
- No `Flames` object is present. No fire particles, smoke, light, sound or animation are included.

## Markers

- `Fire_Anchor`: attach external fire/smoke/light effects here. Check effect height against the hanging pot.
- `Fuel_Anchor`: fuel-storage reference, not a validated navigation destination.
- `Sit_Anchor`: seat reference facing source -X, not a rigged character animation.

All fuel meshes are siblings under the root and retain their authored placement. Their pivots are not carry-animation pivots. For hauling, use a separate carried-log visual or set a centered pivot while preserving the resting transform.

## Import Checklist

1. Import the V2 state kit, not V1, and apply the project's vertex-color material.
2. Start cold: hide `Embers`, disable external fire effects, and set fuel slots from real inventory.
3. Keep cold/ember/lit state independent of storage count; an active fire may outlast the last stored log.
4. Wire refill/consumption to authoritative gameplay inventory. No such behavior is contained in the model.
5. Test empty/full storage and cold/lit effects at gameplay camera distance. Check terrain placement and worker paths separately.
