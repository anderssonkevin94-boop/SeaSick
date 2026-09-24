# Raised Bow V4: Centred Hatch

Offline revision. Unity untouched; V3 preserved.

The hatch and ladder return to the centreline (Blender Y=0). The door remains
on the starboard side (Y=-2.15). The lined internal access chamber connects the
offset doorway to the centred hatch. No exterior stairs are present.

Use each matching width's updated manifest, not V3 hatch/socket positions.
All other V3 replacement, pivot, material and coordinate conventions still
apply. Static meshes share the bow origin; moving door/hatch meshes are
hinge-local. The rear guard remains a separate removable component.

Narrow and wide packages include Blender scenes, renders, 14 FBXs per width,
manifests and validation. The independent export report verifies counts,
bounds, colors and shared geometry. Hull weld checks and sampled door/chimney
checks are retained. The changed interior still requires crew capsule and
traversal testing; no Unity navigation, animation clips or iOS test is included.

Rebuild with `tools/blender/modular_raised_bow_v4.py -- narrow` or `-- wide`.
Verify with `tools/blender/verify_modular_raised_bow_v1.py -- v4` in Blender.
