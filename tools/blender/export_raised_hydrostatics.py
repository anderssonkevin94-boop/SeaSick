"""Wrapper around Astra's export_hull_hydrostatics.py for the raised-middle kit.

Astra's script (tools/blender/export_hull_hydrostatics.py) expects an assembled
`ship.blend` + a manifest with `modules`/`cross_section.deck_height`. The raised
kit instead ships per-part FBX files (art-staging/modular-raised-middle-v1), so
this script imports each Hull_Shell FBX directly (same default bpy.ops.import_scene.fbx
call the kit's own verifier uses -- no axis overrides, so the frame matches the
W1x tables) and calls her `export_module`/`self_test` helpers unmodified.

Run:
  blender --background --python tools/blender/export_raised_hydrostatics.py
"""
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "blender"))
import export_hull_hydrostatics as hydro  # noqa: E402

KIT = ROOT / "art-staging" / "modular-raised-middle-v1"
OUT = ROOT / "Assets" / "_Project" / "Resources" / "ShipModules" / "Hydrostatics" / "HullW1xR_v1"
DECK_Z = 4.20

MODULES = {
    "Stern_W1": KIT / "Stern_W1" / "Stern_W1__Hull_Shell.fbx",
    "Midship_W1": KIT / "Midship_W1" / "Midship_W1__Hull_Shell.fbx",
    "Bow_W1": KIT / "Bow_W1" / "Bow_W1__Hull_Shell.fbx",
}


def main():
    hydro.self_test()
    results = {}
    for name, fbx in MODULES.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(fbx))
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        assert len(meshes) == 1, (name, [o.name for o in meshes])
        obj = meshes[0]
        # A raw default FBX import lands with the hull's LENGTH along Y
        # (verified: Stern_W1's raw import has x in [-6.04,6.04] (beam),
        # y in [-9.3,0.03] (length)) -- not the +X-bow frame the W1x tables
        # and export_hull_hydrostatics.py (slices along X) assume. The W1x
        # tables were produced from an assembled ship.blend where hull parts
        # are already rotated into the ship's X-forward frame; there is no
        # equivalent assembled .blend for this per-part kit, so apply the
        # same 90 degree rotation about Z here (x'=-y, y'=x) to match it --
        # confirmed by reproducing the W1x Stern_W1 table's hullXRangeU
        # ([-0.025, 9.300]) from the raw Y-range magnitude before doing this
        # for real.
        obj.rotation_euler.z = 1.5707963267948966
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        out_path = OUT / (name + ".json")
        results[name] = hydro.export_module(obj, DECK_Z, out_path)
        print(name, "->", out_path, results[name])
    print("RAISED HYDROSTATICS PASS", OUT, flush=True)


if __name__ == "__main__":
    main()
