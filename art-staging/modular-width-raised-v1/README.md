# Expanded Raised End Sections

Offline asset package. No Unity or Claude worktree changes.

Companion to `modular-width-inserts-v1`: raised bow and raised stern replacements
for the same 12.08-unit expanded beam. These replace the complete corresponding
low end section, never overlay it. Low middle bays remain unchanged.

The bow retains the right-side door, centerline hatch, internal passage and
continuous braces. The stern retains its centerline hatch, unchanged M1 wheel
and twin stairways inside the hull silhouette, with no forward footprint extension.

`both-raised.blend` is the assembly review. Each end has isolated and access
renders and individually exported FBX parts. The manifest records moving pivots,
local transforms, geometry checks and files. Source +X forward, +Y port, +Z up;
the existing FBX conversion is game (-y,z,x). Keep existing authoring-unit scale.
Apply recorded local positions and rotations after FBX import. Door and hatch
review poses are open, with hinge meshes unchanged in size.

Static shell pieces are welded for these replacement variants. The lower kit
retains its explicit split pieces; these upper variants are authored expanded
replacements, not runtime-scaled upper meshes.

Not a claim that every possible ship configuration is done: a raised middle bay,
same-height adjacent upper joins, navigation, colliders, capacity balance and
runtime integration are not supplied. Review assembly uses a low middle bay
between raised ends. Do not remove that middle bay and assume the two upper
bulkheads/guards automatically connect. Hydrostatic station data in the lower
width kit applies below the original main deck; upper structure mass must still
be added by the backend. No new balance numbers are invented here.
