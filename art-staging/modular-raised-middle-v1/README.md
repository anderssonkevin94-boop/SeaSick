# Raised Middle Bay and Continuous Upper Deck

Staged only; no Unity or backend changes. Source +X bow, +Y port, +Z up.
Existing FBX axes map to game (-y,z,x). Preserve the existing 0.5 m/u
integration scale; all measurements here are authoring units.

## Dimensions and Scope
- Expanded family W1-center-expansion-r1, 12.08 beam, original lower hull depth.
- Raised middle length 6, deck height 4.20, matching the raised ends.
- Includes matching connected bow and stern replacements. Use ALL THREE
  connected variants together; do not mix with the original drop-edge ends.
- One and two repeated middle bays have passed welded hull topology checks.
- This is a through-bay, not a stand-alone raised island over a low deck.
  Exposed ends still require an access bulkhead/guard variant, not included here.

## What Changes at Connected Ends
Internal mating walls are removed by unioning the upper volume and splitting
at the exact module planes. Drop-edge guards and now-buried entrance doors
are removed. The stern stair wells are filled and stairs removed because
both adjoining decks are now at the same height. The original twin-stair
stern remains available in modular-width-raised-v1 for a low middle deck.
Both centered hatches remain. The chimney is an independent fitting moved
to the upper middle deck at the transform recorded in the manifest.

## Files and Assembly
- `continuous-upper-deck.blend`: editable assembled preview.
- `middle-isolated.png`, `assembled.png`, `side.png`, `two-middle.png`: actual renders.
- `Stern_W1`, `Midship_W1`, `Bow_W1`: FBX pieces for each connected replacement.
- `manifest.json`: mesh files, local transforms, triangle counts and hull checks.
- `Fittings/Chimney.fbx`: independent chimney, pivot at its base.

Root positions for one middle: stern X=0, middle X=9.3, bow X=15.3.
For each extra middle, add 6 units and move the bow forward 6 units.
Apply each FBX's recorded local position and rotation under its module root.
Do not import both the earlier shell and its connected replacement.
Split hull ends are intentionally open; only the assembled hull is closed.

## Not Gameplay-Certified
No swept crew capsule, headroom, stairs, ladder, recoil or cannon-service
clearance validation has been completed. Keep raised decks disabled in the
live build until that work is done. This is not the claimed crew-clearance fix.
No navigation, collision hulls, mass tuning or runtime upper-deck selection
is supplied. The expanded lower-family tables cover immersion below the
original deck only; upper structure mass is a separate backend concern.
