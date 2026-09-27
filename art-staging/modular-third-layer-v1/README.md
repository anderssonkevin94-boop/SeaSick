# Third-Layer Shape Prototype

Staged only. Requires the expanded continuous second-deck variants in
`modular-raised-middle-v1`; not compatible with the older W2 deep family.

Three upper-only modules reuse the exact supporting roof footprint, without
duplicating the lower hull or wheel. Mount height 4.20 authoring units; rise
2.44; new walking surface 6.64. Root-local lower edge is Z=0. Existing export
axes map source (x,y,z) to game (-y,z,x); retain established asset scale.
The lower deck remains the floor, so upper shell bottom edges are intentionally
open. Source geometry does not imply a hollow, traversable full ship interior.

Use `third-layer-study.blend` and `assembled.png` for review. FBX parts and
local fitting transforms are recorded in `manifest.json`. Disable covered
second-deck rails/posts/plank details and obsolete hatch lids/helm/prow as in
the saved review scene. Chimney placement is raised, not mesh-scaled.
Hatches and ladder visuals are retained on the end modules. The middle is
a through section with no independent access hatch in this pass.

Optional joining-wall surfaces are exported separately and disabled in the
continuous assembly. They are not finished exposed-end bulkheads: partial
third decks still need safe access and edge guards before use. Only the full
three-module continuous arrangement is shown and checked here.

No crew capsule/headroom, navigation, swept hatches, cannon/recoil, stability,
mass balance, colliders or iPhone performance certification. Keep disabled in
the live shipyard. This is a geometry prototype, not unrestricted stacking.
Upper mass and center-of-gravity changes belong to the backend; do not count
upper volume as displacement below the original main deck.
