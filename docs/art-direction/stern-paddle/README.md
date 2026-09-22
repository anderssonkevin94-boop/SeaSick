# Stern-wheel steamer — the approved look (2026-09-19)

`00-reference-sheet.png` is the concept Kevin chose out of
`art-staging/stern-paddle-integrated-hull-v2/`, and the ship was built to it.
Everything else here is the ship that came out.

| | |
|---|---|
| `profile`, `bow-three-quarter`, `stern-three-quarter`, `astern`, `plan` | the generator's own Workbench renders — flat studio light, no sea. They answer whether the MESH is right |
| `in-game-chase`, `in-game-deck`, `in-game-stern` | Sea.unity, her own light, her guns and hands where the game put them. This is the question that actually gets answered by eye, and it is a different one |

Regenerate the first set with

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        -P tools/blender/steamer.py -- --renders <DIR>

and the second with `RunSteamerProbe.Shot()` in play mode. The engineering
contract is `docs/steamer-spec.md`; the side-wheeler she replaced is archived
runnable at `tools/blender/archive/sidewheel-v1/`.
