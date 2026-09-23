# Blacksmith / Forge L1 - Astra V1

Staged art only. Not imported into Unity. This is the first forge version.

## Files

- `forge-state-kit.fbx`: preferred integration source, containing every stock slot and all work variants. Do NOT display every work variant together. Initialize visibility explicitly.
- `forge-empty.fbx`: empty, idle static reference; missing stock meshes make it unsuitable as the interactive master.
- `forge-review.fbx`: four ore slots, three output tools, forging state. Static art-review example, not saved gameplay.
- Blender source: `../../tools/blender/source/forge-astra-lvl1-v1.blend`.
- `state-contract.json`: visibility and production-state guidance.
- `validation.json` and `export-verification.json`: source and FBX round-trip checks.
- PNGs: front, two three-quarter views, gameplay-size preview, and empty/heating/forging/output-full examples.

## Scale And Materials

One Blender unit is one meter. Root origin is ground level at the plot center.
Blender Z-up; FBX exported -Z forward / Y up. Fit the existing 6.53 x 5.85 m Blacksmith plot without enlarging it. See validation.json for exact bounds. Preview camera is excluded from FBX.

One vertex-color material, `Col` color attribute, flat normals, no textures. Use the game's vertex-color shader and preserve hard normals; do not recalculate smooth normals on import. Warm workpiece colors are not emissive and do not supply light.

## Modules

- Permanent: Frame, Canopy, Forge_Hearth, Coal, Bellows, Anvil, Workbench, Smith_Tools, Apron, Quench_Tub, Ore_Bin, Tool_Rack, Sign.
- `Input_Ore_01` through `05`: independently removable ore chunks in the uncovered bin.
- `Output_Tool_01` through `04`: independently removable finished hammers on the uncovered rack, representing the generic Tools resource.
- `Work_Heating`: billet in the hearth.
- `Work_Forging`: billet on the anvil.
- `Work_Finished`: cooled hammer head on the anvil awaiting collection/assembly abstraction.

The small decorative hammers and tongs at the back are the smith's permanent equipment, not saleable output. Coal and water are decorative; this asset introduces no extra fuel or water requirement. Bellows are a static separate module, not rigged folding geometry. The anvil and its stump are one module. Input and output mesh origins are centered for pickup integration, but character grips are not authored.

## Production States

Use the existing Ore-to-Tools recipe, timing and capacity from gameplay. Five ore meshes and four output meshes are visual fill levels, NOT recipe quantities, inventory capacities or output yield. Normalize actual inventory into visible slots in the game's adapter.

1. Idle: show inventory and no `Work_*` variant. Empty bins remain visible.
2. Heating: claim input once, reduce visible ore accordingly, show only Work_Heating.
3. Forging: show only Work_Forging. A claimed job can finish even if input storage is empty.
4. Finished: show only Work_Finished until transfer is accepted. If output is full, retain this state and block another job.
5. Transfer: hide the workpiece and update output slots atomically; never duplicate the same item in work and output displays.

Heating/forging are optional visual phases of the existing job, not new intermediate inventory resources. No production logic, state animator, crew animation or attachment pose is included.

## Effects And Markers

No solid flames, smoke, sparks, lights, particles or effects animation included.

- Fire_Anchor: above the coal bed.
- Smoke_Anchor: above the chimney outlet.
- Sparks_Anchor: anvil working surface.
- Input_Anchor / Output_Anchor: display reference points.
- Anvil_Anchor: bare anvil top.
- Worker_Stand: front working-position reference only.

Markers are Blender-local coordinates exported with the root, not world-space gameplay positions. Chimney and hearth sit fully forward of the canopy with source-verified horizontal clearance. Particle plume size and wind still need tuning to avoid canvas penetration. Worker_Stand is not a validated navigation or IK target.

## Integration Still Required

Claude should build the prefab, map vertex colors, preserve flat normals, add conservative colliders and navigation, connect stock and work states, and attach external VFX. Review approach paths and interaction poses with the actual crew and gameplay camera. Disable the previous procedural building visual when replacing it to avoid double geometry. Do not change economy values to match the number of art slots.

Mesh checks cover manifold edges, zero-area faces, vertex colors, flat shading, footprint bounds, frame/roof clearance and state contents after FBX round-trip. They do not substitute for in-engine collision, performance or gameplay tests. Joined modules intentionally contain separate constructed components; they are not single watertight boolean-unioned solids.
