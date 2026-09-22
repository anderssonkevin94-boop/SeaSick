# Directional ocean study

Separate Unity scene: Assets/_Project/Scenes/DirectionalOceanStudy.unity. Open this scene and Play to animate the study. The Sea scene, FFT configuration, buoyancy, production ocean shader and production wake were not changed in this pass.

Four directional wave components use a second harmonic to peak their crests. Vertex displacement and fragment normals share the same wave function and derivative. Three related blue values follow face orientation; crest foam follows the rising side of peaks and breaks along crest direction. No noise-based pigment islands or closed contour highlights. Sharpness and amplitude can be adjusted on the study material.

The isolated scene is rendered on layer 31 to exclude gameplay scene objects. An initial unisolated capture exposed other-scene objects; the accepted captures exclude them. Materials and scene are saved separately. The editor returns to the original scene without saving it.

Verified: no ShaderUtil errors after actual rendering, 1920x1080 desktop, 1080x2340 portrait, 24 successive wave phases. motion.gif and index.html show 2.4 seconds, with a reset at the end. Render again through SeaSick > Art > Render directional ocean study.

Limits: visual prototype, not integrated with OceanSampler/FFT or storm/weather. Wake is a straight-course analytic study mask and the ship is stationary; it is not a replacement for the production trail-history system. The study has no shoreline/depth treatment. It does not yet reproduce the full reference composition or all foam detail. Collision/visual agreement and quality-tier performance must be addressed before replacing gameplay water.

## Wave variation and foam lifecycle revision
Two smooth travelling amplitude envelopes shorten and vary the wave groups. Their derivatives, plus the bent-phase derivative, are included in the shading normal. Foam formation depends on crest energy, appears briefly bright at the crest, then erodes into faint scraps. Age is computed analytically from crest passage, not a foam history buffer. Lifetime is capped below the wave phase wrap to avoid a live seam. No wake, shoreline or production changes in this revision.

The preceding study is preserved under previous/. The comparison page synchronizes both animations and can switch to reference B. Gradient verification and render results are in variation-validation.txt.

## Shape and lighting pass
Dominant 32 m swell amplitude 1.15 m, with successively smaller 14.5/5.4/2.6 m components. Sun-driven continuous face shading replaces stepped pigment bands. Height adds restrained crest color, while a simplified sky-gradient Fresnel reflection and broad sun highlight provide a water surface response. Default foam visibility is zero; the foam-restored still is 0.65. Both original and low-camera sequences render 24 phases. Opposite-sun still verifies that light and dark regions respond to illumination rather than fixed patterns.

The boat now samples the same analytic wave formula at four hull points as a visual study proxy. This is not a physics implementation. The prior pass is preserved in before-shape-light/. Shape-only rendering deliberately withholds the final illustrated foam treatment. See shape-light-validation.txt.

## Tapered wave face pass
Replaced the sinusoidal profile with a rounded triangular ridge plus an asymmetric term. Softly angular crest bends and travelling amplitude envelopes form tapered, overlapping faces. Analytic normals and the boat height proxy follow the new formula. Earlier second-harmonic/geometry sharpness descriptions above refer to historical passes; _Sharpness now affects only the provisional foam energy. Foam remains off by default. The old pass is archived in before-wedges/.

Daylight captures now explicitly disable and restore the global weather rose as well as night/sun globals so gameplay weather does not leak into the comparison. Original and low-camera views each cover 24 phases; desktop and portrait stills are included. The isolated study remains separate from gameplay. Rounded ridge derivative finite-difference check: maximum absolute error 3.373625268032754e-10 over 10,000 seeded samples. This is a formula check, not gameplay buoyancy or performance validation.

## Painted wave faces
Crisp pigment boundaries now classify the actual displaced wave normal and height into related blue regions, with screen-space antialiasing and restrained shading inside each face. Broad specular gloss was removed. Crest foam segments are shorter, use linear along-crest variation for less rounded ends, and leave much fainter remnants. Default rendered review includes foam at 0.85; faces-only.png isolates the pigment treatment. No independent noise texture drives the blue shapes. before-painted-faces/ preserves the prior render. These images remain a visual prototype, not production integration or a finished match to reference B.

## Varied crest foam (revision 6)
Preserves the approved revision 5 wave geometry and nearby blue-face shading. Each successive crest now has its own deterministic segment occupancy, length and width, with tapered slivers and occasional wider fragmented tips. The segment seed changes at the trough, outside visible foam. Residual foam was removed after the render revealed elongated streaks. Distance-based pigment contrast reduction from 100 to 420 metres cleans up the horizon; foam retains its distance fade. The study wake is unchanged. No new small-wave geometry was added in this focused pass. The comparison defaults to the untouched saved-painted-faces-v5 baseline.

## Torn crest ribbons (revision 7)
Replaced isolated wedge masks with thin ribbons measured in metres from the analytic ridge apex. Two scales of noise roughen their edges and open small holes; sparse flecks sit just behind the crest. Additional along-crest gaps avoid continuous white outlines. The study wake also has small ragged holes. Geometry, blue-face shading and distant contrast are unchanged. Revision 6 renders are archived in before-ribbon-foam/. The approved revision 5 snapshot remains untouched. Rendered both camera sequences and desktop/portrait stills; ShaderUtil reported no errors. Foam-off frame equality with revision 6: False.
Foam-off pixel difference mean RGB (0–255): [0.001349826388888889, 0.0007740162037037037, 0.00046103395061728396]. Pixel-identical rendering was not achieved; blue pigment and geometry source code were not changed.

## Solid crest ribbons (revision 8)
Retains revision 7 crest ribbon width, edge shape and placement; removes interior holes and detached trailing flecks. Wake holes and the faint translucent foam fallback are removed as well. Solid coverage keeps pixel-edge antialiasing. Geometry and blue shading unchanged. Original and low-camera sequences, desktop and portrait rendered without ShaderUtil errors. Prior renders in before-solid-ribbons/. Approved baseline unchanged.

## Thicker, sharper ribbons (revision 9)
Crest half-width increased from 0.075–0.175 m to 0.10–0.23 m. Narrowed profile edge transition and final coverage antialiasing for a crisp solid boundary. Crest paths, wave geometry and blue shading unchanged. Both camera sequences and desktop/portrait rendered without ShaderUtil errors. Previous renders in before-thicker-ribbons/.

## Surface-coupled foam (revision 10)
Removed per-component crest ribbons and their static noise masks. Fragment shading samples the total surface gradient 0.35 metres ahead/behind to estimate directional curvature and locate the combined ridge. Crest masks follow the same summed wave slope and height that drive blue faces, with clean coverage and height/cross-slope-dependent gaps. The wake coordinates also deform modestly with the total slope. Blue shading and wave geometry unchanged. Two additional surface evaluations per fragment add prototype GPU cost; production performance is not validated. Rendered desktop, portrait and both 24-frame motion sequences without ShaderUtil errors. Prior renders archived in before-surface-foam/. Approved baseline unchanged.
