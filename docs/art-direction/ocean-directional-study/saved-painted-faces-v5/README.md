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
