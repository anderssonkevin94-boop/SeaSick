# Gameplay ocean integration

Ports the approved study's stepped blue faces and combined-surface crest treatment onto the production FFT water, rather than replacing the simulation with the study's four analytical waves. Palette thresholds account for the moving sun; color height is normalized by local sea height. Existing shore tint, refraction, surf, hull clipping, weather/night palette and fog remain. Solid wake shapes replace the previous lace mask; the existing sampled wake mesh/history is retained.

Crest detection uses screen derivatives converted to a world-space slope Hessian, avoiding additional cascade texture fetches. It selects the negatively curved direction and gates coverage by normalized wave height. Shading attenuates the smallest FFT band so high-frequency normals do not overwhelm the graphic faces. Wave displacement, OceanSampler, spectrum, weather balancing and buoyancy code are unchanged.

The gameplay waves are longer/smoother than the study and still need visual tuning. This is a first integration, not an exact match. Mobile device performance and full storm sailing are not validated by the desktop renders. Night/storm images are lighting checks only.

Apply persistent material settings: SeaSick > Art > Apply approved ocean style. Live screenshots: SeaSick > Art > Capture gameplay ocean. Review restores sky controls and temporary clipmap follow override. No scene save is required. Pre-integration files are retained in before/; the approved revision-5 snapshot and revision-10 study remain intact.

Known visual gap from the approved study: live FFT foam remains denser and more fragmented in some areas, and wave faces are longer/smoother. The first integration is saved for review; do not describe it as an exact visual match. Night and storm lighting were rerendered after fixing the review to execute SkyDirector.LateUpdate immediately; clock and weather values are restored afterward.

## Corrected shared-surface implementation
Supersedes the first integration above. Screen-derivative foam amplification was removed. Long/mid FFT slope is sampled at fixed world offsets; the finest simulation band no longer drives graphic pigment/crest detection. Clean crest direction follows the approved study instead of switching principal axes between small ripples. The graphic shoreline path no longer reintroduces the old foam noise.

The actual four pointed-wave profiles from the approved study now add 70% strength detail to the rendered height AND OceanFieldData's sampled height, normal and vertical velocity. Detail fades by wet mask and smoothstep(0,8,depth) in both twins, so it vanishes in the shallows without the broad home-region shelter suppressing all shape offshore. It is evaluated at displaced world XZ, after FFT inversion on CPU. This is a real surface change and can alter sailing feel; it is not merely a shader palette change. Storm spectra and their existing forces were not retuned.

GraphicWaveDetail.hlsl and GraphicWaveDetail.cs are twins. GraphicWaveParity checks 1024 GPU/CPU samples and analytic vertical velocity. OceanVerify.compute includes the detail in its expected GPU height. Full FFT inversion/readback divergence has not been rerun in this pass; the standalone parity result does not claim that. Clipmap distant geometry fades still differ from near-field physics as before.

The filtered slope and analytic detail add GPU work, and the detail adds Burst sampler work. Mobile device performance and extended storm sailing remain unvalidated. The review was restarted in a fresh Play session after script reloads. GameOceanMotionReview records 24 actual live frames; it is not a separate scene or mocked water. Approved study assets and saved baseline remain unchanged.

## Leading crest caps
Foam direction now uses a smoothly weighted phase-velocity direction from the three largest analytic detail waves, including their bent phase gradients and changing crest weights. The combined-surface ridge mask is offset forward by 0.65 of its half-width (about 0.13–0.21 m); curvature and height still limit caps to developed crests. This is the motion of the added detail waves, not a full inversion of FFT energy propagation. No height, CPU physics, palette or wake changes in this pass. CPU/GPU parity and actual desktop/portrait renders passed again, with 24 live motion frames recorded. Previous images preserved under before-leading-crests/.
