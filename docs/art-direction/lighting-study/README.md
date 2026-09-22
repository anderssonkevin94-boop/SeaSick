# Adventure lighting — 2026-09-17

Saved production profile: `Assets/_Project/Resources/AdventureLighting.asset`. SkyDirector loads this profile at startup. Uncheck **Apply To Game** to restore the original scene lighting values. Existing scene fields remain unchanged; the previous SkyDirector source and its serialized live values are saved under `before/`.

The new clear-day treatment uses warm sunlight (1, .89, .73), intensity 1.12, a lower sun arc (vertical direction scaled by .72 then normalized), cool equatorial ambient (.36, .43, .60), darker ground fill (.19, .24, .33), and reduced sky fill (.68). Clear fog starts at 950 m and ends at the existing 1500 m limit (clearing nearby air without revealing scenery beyond the streamed ground). The sun direction is shared by the actual light and sky/ocean shader globals and stays consistent as weather changes. The day clock still runs normally.

The profile changes clear daylight; existing dawn/dusk and night palettes and storm weather logic remain. Clear-night lighting and full-storm colour, brightness, ambient and fog are checked against the old settings. Daytime storm shadows follow the new sun arc. Existing shadows are longer because of the lower light; no screen-space occlusion, bloom or exposure effects were added in this pass.

The comparison gallery uses actual Unity camera renders at a pinned day fraction of .38 and clear sky. Each before/after pair is rendered synchronously, with unchanged geometry, camera and simulation time. Ship view is a fixed camera behind the live ship, not a new gameplay camera setting. Portrait is also captured. Night and storm-light captures are separate checks, not full storm simulation tests.

Repeat with **SeaSick > Art > Compare adventure lighting** while Sea is playing. Review controls are temporary and restored afterward. No Sea scene save was needed.
