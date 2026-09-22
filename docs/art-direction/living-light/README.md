# Living light — 2026-09-18

Production update: separate peach/gold sunrise and amber/lavender sunset palettes, a broader golden-hour blend, warm cloud tops with violet undersides, later-emerging stars, and cloud-filtered sun shafts.

Lighting colour affects the scene's actual sun, sky, ambient transition and fog through SkyDirector. AdventureLighting.asset stores the golden-hour elevation span and sunrise/sunset horizon and sunlight colours. The clock keeps running normally and the existing moon/night and weather systems remain active. Clear and twilight fog stay within the 1500 m streamed visibility limit.

Sun rays are atmospheric **sky scattering**, drawn in the sky shader and therefore hidden behind terrain, ships and buildings. They do not create volumetric beams between nearby objects or cast extra light onto terrain. Two cloud samples near the sun define the open angular regions; their light extends outward with soft distance falloff. As the separate cloud layer drifts, these openings change. Rays fade with overcast and sun elevation and disappear at night. The extra texture work is restricted to the sun's quarter of sky. No renderer feature, transparent world mesh, or new render target was introduced.

Control: **Cloud-filtered sun rays** on Assets/_Project/Art/Sky.mat (0.32). Set to zero to disable shafts. The current profile and shaders were backed up under before/ before this pass. AdventureLighting's Apply To Game switch restores the scene's original lighting palette; ray strength is controlled separately on Sky.mat.

Review: live Unity captures across seven times, matched rays-off/on shots at fixed camera/time/weather/cloud position, portrait, and storm-light checks. Shader errors checked after render. The same cloud position is held for the seven time comparisons to isolate lighting; normal gameplay clouds continue moving independently. Not a performance benchmark or an accelerated full gameplay simulation.
