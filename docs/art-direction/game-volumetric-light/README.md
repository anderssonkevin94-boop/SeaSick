# In-game volumetric sunlight

SkyDirector owns a runtime VolumetricSunlight component. It loads SunVolumeSettings and SunVolume from Resources; no Sea scene re-save is required. Desktop defaults to Medium (32 shadow samples per covered pixel), mobile to Off. Low uses 16; High uses 48. The settings asset exposes range, density, strength and platform quality.

A camera-centred volume integrates directional sunlight against the real scene shadow map, with opaque depth clipping and a smooth distance/height falloff. Golden-hour strength fades toward midday, night, and heavy overcast. It composites before the depth-writing ocean so waves mask it correctly and water retains existing colour/fog. Consequently, this does not add new volumetric scattering along the camera-to-water segment. Existing sky shafts remain in the distant sky. Moving cloud shadows are not included.

This bounded implementation still renders at full resolution. It is not a half-resolution or temporally reconstructed production fog system. Device GPU timings have not been measured, so no frame-rate claim is made. Keep mobile disabled until device-specific profiling supports enabling it.

The saved prototype and its original comparisons remain in volumetric-lab. In-game captures here compare the same camera, clock and simulation frame with just the volume switched off/on.
