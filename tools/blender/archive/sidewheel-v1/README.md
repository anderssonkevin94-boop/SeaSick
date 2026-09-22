# Side-wheel steamer, v1 (superseded 2026-09-19)

The Victorian side-wheel paddle steamer that `tools/blender/steamer.py` built
before the stern-wheeler. Kept because none of these files were ever committed:
without this copy the old ship would only exist in a render.

To go back: copy `steamer.py`, `steamer_form.py` and `steamer_design.json` up
into `tools/blender/`, copy `hullform.json` into
`Assets/_Project/Resources/Steamer/`, re-run

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        -P tools/blender/steamer.py

and revert `PaddleDrive.cs` and `SteamerBootstrap.cs` to their two-wheel form.
