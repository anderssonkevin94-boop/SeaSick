# Ship Segment Handoff

All files below are staged assets, not imported into Unity. Existing game and
Claude worktree are untouched. Latest width family is W1-center-expansion-r1;
do not substitute the older W2-r1 wide/deep family.

## Review-Ready Assets
- Standard lower bow/middle/stern: `modular-hull-family-v3` (W1-r2).
- Standard raised bow with starboard door and centered hatch: `modular-raised-bow-v4/narrow`.
- Standard raised stern with twin inset stairs: `modular-raised-stern-v2/narrow`.
- Width expansion lower sections: `modular-width-inserts-v1`.
- Matching expanded raised bow and stern replacements: `modular-width-raised-v1`.
- Continuous raised middle and matching connected end replacements: `modular-raised-middle-v1`.
- Shipyard building visual: `drydock-astra-lvl1-v1`, empty, no vessel included.

The width expansion adds 2.8 authoring units of beam, from 9.28 to 12.08,
without changing length, depth or wheel size. Middle bays remain six units long.
Lower inserts are separate meshes; upper ends are welded replacement sections.
Use every package's manifest and README for actual transforms and limitations.

## Checks
Lower kit: welded hull topology for 0, 1 and 2 middle bays; FBX triangle/color/normal
round trips; unchanged wheel and housing geometry; new immersed-area tables.
Expanded raised ends: assembled hull topology, source part topology, 72 wheel
rotation clearance samples, and exported FBX triangle/color/normal verification.
These are geometry checks, not Unity, navigation or iPhone performance tests.

## Supported Initial Scope
Standard or expanded width applied across the whole ship; authored lower middle
bays control length. Low or raised end sections can be chosen independently,
with a low middle between raised ends. Preserve recorded door/hatch hinge pivots.
Never stretch meshes at runtime. Keep the existing conversion from asset units
to world scale and the backend as the only capacity/displacement calculator.

## Still Needed
- The raised middle now has one/two-bay continuous-deck geometry tests, but
  exposed raised-middle ends, zero-middle raised-end adjacency and crew
  clearance remain unvalidated. Keep all raised decks disabled in gameplay.
- Runtime width selection, replacement registration, proper colliders and LODs.
- Crew traversal, cannon clearance and slot validation, camera/input behavior.
- Backend mass/capacity tuning, including the added upper structure's mass.
- Refit persistence, damage/cargo/crew transfer, and iPhone testing.

For displacement use the lower expanded kit's new hydrostatic tables, never
the old W1 or W2 tables. They cover upright immersion below the original main
deck only, not stability, upper-deck flooding or cargo capacity.

The drydock is an uncalibrated visual starter. It has not been fit-tested against
every ship size. Do not infer vessel limits from its raw Blender dimensions.

Latest explicit answers to Claude are in `ASTRA-TO-CLAUDE-SHIP-REPLY.md`.
