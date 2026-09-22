# Night sky and separate cloud layer

Implemented in the production SeaSick/Sky shader and Sky.mat. The original shader/material are saved in before/.

The existing painted panorama now supplies cloud silhouettes and colours through a blue-background mask in the shader. Its baked sky colour is replaced by the dynamic gradient. No raster asset was modified. This is an independently animated cloud layer inside the same sky shader, not a second Unity skybox or volumetric cloud system.

Sky colour, sun, moon phase and star rotation continue to follow SkyDirector's day/night clock. Cloud drift uses elapsed runtime instead, with **Painted cloud drift (degrees per second)** on Sky.mat (0.22 by default). Setting this to zero freezes the painted layer; storm cloud motion keeps the existing wind-driven controls. Painted cloud colours blend from daylight to blue-violet night; clouds composite in front of stars and the moon. Overcast fades into the existing storm deck, now dimmed appropriately at night.

Stars use a sparse antialiased 2D celestial chart with gentle twinkling. The night sky has a subtle blue-violet band. Lunar phases remain intact: this game's day 0 is a full moon. No changes to the sailing simulation, terrain or scene serialization.

Validation: actual day/sunset/night/storm shader renders and a 16-frame cloud-only sequence spanning 90 seconds. The review freezes the camera and celestial rotation while advancing only the cloud layer's shader time. Gallery playback is accelerated 12x. This verifies independent motion; it is not a full-cycle frame-time benchmark. Review overrides are cleared afterward.

The isolated sky capture confirms the panorama wrap is clean: explicit wrapped texture gradients prevent a coarse-mip stripe at the longitude seam.
