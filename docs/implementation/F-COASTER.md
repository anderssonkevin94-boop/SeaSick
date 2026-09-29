# F coaster implementation — 2026-09-29

Approved scope: one fixed 4.64 m beam; stern, zero to three repeatable middle bays, pointed bow; independently selectable base/second deck. Widening and third decks remain future work. Preserve F28/F29 art, reserved helm deck, stern-height-driven wheel, straight planks, framed gun ports, compact ladders, warm lanterns and cool ambient lighting.

Implementation sequence:
1. Export a reusable runtime kit from the approved Blender sources, with separate rotor, covers, lantern glass and conditional end connections. Keep source studies unchanged.
2. Register a new versioned module family with authored dimensions, gun sockets, capacity and wheel interfaces. Retain legacy definitions for save compatibility.
3. Adapt the section-based shipyard preview/edit/confirm flow for fixed width and two levels. Keep atomic refit and equipment retention.
4. Bind deck surfaces, crew routes, ladder links, cannon footprints, helm and paddle drive to the same assembly coordinates. Exposed walls have entrances; enclosed cannon decks remain accessible. Storage/berths use abstract capacity.
5. Integrate restrained warm lantern lighting and soft shaded vertex materials.
6. Verify compilation, assembly combinations, save/refit behavior, crew/gun placement, sailing, and desktop/portrait rendering. Record actual results and limitations here.

## Implemented

- New `hull.*.f30.*` family, using native Unity meshes and prefabs. The approved Blender studies remain unchanged. Runtime source: F29 stern/base middle/base bow, F19 raised middle, F26 complete raised bow. The importer derives a connected stern with matching side caps when the next section is raised.
- Fixed 4.64 m beam; zero to three middle sections; one or two decks per section. The stern controls paddle radius independently (1.015 m / 1.675 m). Future width and third-deck options are hidden/refused for this family.
- Raised stern gun room below the helm. Base and upper middle/bow cannon slots, explicit equipment, hinged lids, animated aiming/recoil and projectile muzzle alignment. Empty enclosed ports close; open-deck cannon gaps remain open by design.
- Conditional exposed walls, doorways, compact ladders, curved side transitions, connected upper decks and sealed stern gun-room floor. Named source meshes remain available for inspection and navigation; rigid sections render in combined material batches.
- Ship-local floor navigation with 22 cm crew clearance, cannon/wall obstacles, headroom filtering and ladder connections. Plank seams are bridged with a 2.5 cm tolerance. Spare crew use the working decks, not the helm. Ladder traversal currently uses the existing character movement animation.
- Storage/berths remain capacity allocations. Cargo accounting and transfer targets work; the old deck-pile visuals are hidden for this boat so they cannot intersect its gun rooms and passages.
- Warm lantern pools and emissive glass, navy/wood vertex materials, main-light shadows and cool ambient fill. The chase camera clears the taller stern. Existing world lighting, weather and pixel treatment remain in effect.
- The home dry dock opens the 3D section builder. Choose a section, then One deck/Two decks; use the plus signs to insert middles. Cannon pages show lower/upper deck and port/starboard. Cannons can be moved from storage or built free in this prototype; construction is applied with the confirmed refit and is cancelled with the draft. Interior pages adjust abstract hold/berths.
- New voyages start with a raised stern, one low middle, low bow and six cannons. Existing saves convert hull sections on load while retaining length and raised sections; the stern becomes raised. Guns which cannot be fitted go to dry-dock storage. Previous non-cannon slot fittings are retained in storage while their gameplay capacity uses the new abstract layout. Save migration does not discard held resources.

## Verification

Recorded in `art-staging/f-coaster-runtime/integration-results.txt`, `verification.txt`, and `routes.txt`:

- 60/60 hull/deck combinations: assembly, full batteries, hydrostatic data, capacities and configuration save round trips.
- 648/648 gun-station-to-helm routes, including actual floor height/proximity checks.
- Existing modular self-test: 263 passed, zero failures.
- Runtime and editor compilation: zero errors.
- Live sailing, paddle rotation, six-gun battery, firing and muzzle alignment.
- Real dock refit through the UI adapter: inserted middle, independent upper deck, new cannon; original gun references retained.
- Injected failure after assembly restored the original ship and cannon art.
- Full game save/read/restore of the seven-gun refit matched its configuration.
- A copy of the latest existing voyage restored successfully with three middles, two fitted cannons and four stored cannons. Original saves were copied to `art-staging/f-coaster-runtime/save-backup-2026-09-29` before testing. Tests used `/tmp/seasick-coaster-playtest`.
- Desktop/portrait builder and night lighting visually inspected.

## Rebuild and play

Runtime is independent of Blender. Open `Assets/_Project/Scenes/Sea.unity` and press Play with the steamer selected. Refits happen at a built dry dock at the home berth. New voyage shows the six-cannon starting arrangement; Continue preserves the saved section count.

To regenerate art: run `tools/blender/export_coaster_runtime.py` with Blender, then `SeaSick/Ship/Import approved F coaster` in Unity. To regenerate module definitions: `python3 tools/build_coaster_definitions.py`. `CoasterVerification.Check()` and `.Routes()` are editor checks; schedule the latter with `EditorApplication.delayCall` when calling it through Pipeline, since it exceeds that bridge's synchronous timeout.

The ship uses the existing sailing model, reshaped for its dimensions, plus provisional numerical hydrostatics and mass/capacity balance. It has been sailed in the editor, not performance-tested on an actual iPhone. Exact underwater collision and flotation matching, animated ladder climbing, widened hulls and additional storeys remain future work.
