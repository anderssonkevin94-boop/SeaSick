# Camp Pug - Astra V1

Female camp companion, intentionally missing her anatomical RIGHT eye. That side has a healed, closed eyelid, not a socket, wound or eyepatch. Only the LEFT side has an eyeball. Do not mirror or add a replacement right eye.

## Deliverables

- `camp-pug.fbx`: static model with separately removable green neckerchief.
- `camp-pug-no-neckerchief.fbx`: same character without the accessory.
- Source: `../../tools/blender/source/camp-pug-astra-v1.blend`.
- `front-three-quarter.png`, `front.png`, `right-healed-eye.png`, `rear.png`, `game-scale.png`: review renders.
- `validation.json` and `export-verification.json`: source and round-trip mesh checks.

Staged art only. Nothing imported into Unity. This is a static character model, NOT a rigged or animated gameplay pet.

## Scale And Axes

Meters, ground-level root, about 0.61 m tall including the oversized storybook head. Exact dimensions are in validation.json. Blender Z-up, facing -Y. Anatomical right is -X; anatomical left is +X. From the front, the closed lid appears on the viewer's left.

FBX exports use -Z forward / Y up. Verify the prefab's forward direction before assigning movement; this character does not use the older fauna generator's custom yaw convention. Head_Anchor and Ground_Anchor are reference empties, not bones or collision shapes.

## Mesh And Material

Flat shading, one vertex-color material, `Col` colors, no textures. Preserve hard normals and vertex colors when importing and assign the game's vertex-color shader. Catchlight is geometry, not a dynamic reflection.

Body is a single connected remeshed/simplified anatomical surface, including head, torso and four legs. Curled_Tail, Folded_Ears, Muzzle, Left_Eye, Right_Healed_Eyelid, Brow_Folds and Neckerchief are separate modules. Small face details intentionally touch or enter the underlying skin surface. Topology checks do not mean every separate detail has been boolean-unioned into the body.

## Integration Still Required

No skeleton, weights, blendshapes, walk/idle animations, LODs, colliders, navigation, AI or audio are supplied. The static triangulated body is an art model, not deformation-tested animation topology. Review/retopologize joint loops as needed before skinning. Animate a left-eye blink only; preserve the healed right side. Rigging the tail and accessory will require deliberate weights, not rigid automatic parenting alone.

Claude should preserve the approved appearance, scale against actual crew, set up a quadruped rig and poses, and test movement and ground contact in-game. Keep imports separate from other fauna until that work is ready.

Source validation checks connected body geometry, flat normals, manifold edges and zero-area faces. FBX checks validate mesh/color retention and intentional eye modules. These do not substitute for rigging or in-game performance tests.
