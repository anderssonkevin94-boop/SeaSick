# Drydock Level 1

Staged review asset only. No Unity changes. No ship included.

An open timber refit slip with broad side walkways, five keel supports,
angled hull pads, lifting gantry, manual winch, canvas work shelter and
separate repair timber stock. Flat shaded, shared vertex-color material.

## Files
- `drydock-lvl1.blend`: editable source, preframed for inspection.
- `drydock-lvl1.fbx`: triangulated model and named reference markers.
- `preview.png`, `opposite.png`: actual Blender renders.
- `validation.json`: triangle counts and source coordinate convention.

## Integration
Source metres, Z up, sea entrance toward -Y; FBX converts to Y up.
Root origin is ground level at the middle of the slip. Walking surface is
0.99 m above that origin. Approximate footprint 9 x 12 m; central clear
width between walkways is 5.3 m, with angled supports intruding below hull.
This is a starter visual, not a fit-tested dock for every modular ship.
Place at a shore with the open end facing water, adapting foundations to
terrain. Do not stretch geometry to accommodate every vessel size.

`Ship_Center` is a visual cradle reference, not a buoyancy or spawn contract.
`Sea_Entry` and `Interaction_Point` are placement hints; navigation and
shipyard UI hookup are not implemented. No colliders, navigation, lights,
VFX, animations or gameplay logic are provided.

Use the `Col` vertex colors with the game's compatible vertex-color shader.
`Timber_Stock` is independent and can be hidden; it is not a capacity value.
Closed carpenter components intentionally meet/intersect at structural joints.
Source mesh validation checks manifold components and nondegenerate faces.
