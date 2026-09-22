# Shader keep-alive materials

Nothing renders with these. They exist so the shaders the game looks up BY
NAME at runtime (`Shader.Find("Universal Render Pipeline/Particles/Lit")` in
SpeedJuice, StormSpray, Cannon, Impact, SelectionRing, HandCursor, the fleet
and world factories...) survive a player build.

A player build only ships shaders that some built asset references. The first
iPhone build (2026-09-22) had no material on the URP particle or unlit shaders,
so every `Shader.Find` came back null, `new Material(null)` threw in Start,
and the half-built components then threw NullReferenceException every frame.
Anything under Resources/ is always built, together with its shader; and the
KEYWORDS a material has set decide which shader_feature variants are compiled,
so there is one material per (shader, keyword set) the code enables at runtime.

Add a material here whenever new code does `Shader.Find` on a shader no scene
material uses, or enables a shader_feature keyword no material here has.
