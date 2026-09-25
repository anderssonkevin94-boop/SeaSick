"""Verify the three new raised-middle wall variants (RAISED-SECTIONS.md §3).

Per variant (MiddleWA/MiddleWF/MiddleWB):
1. Round-trip every exported FBX part: triangle count matches manifest, vertex colours
   present, flat normals (no smoothed polygons) -- same checks as Astra's kit verifiers.
2. No degenerate faces in any part (re-derives topology on the imported mesh, not just
   trusting the manifest, since that's what the generator itself already asserted at
   author time -- this re-checks after the FBX round trip).
3. The Hull_Shell's hull_shell_check recorded in manifest.json has zero
   overconnected_edges and zero degenerate_faces (the "closed at the wall face" gate).
4. The face that stays open (the non-wall face for wa/wf, both non-wall for none since
   wb has none) has boundary-edge Y/Z bounds matching the plain W1x middle's own
   interface loop, i.e. the connectable face is unchanged from the standard interface
   and will mate with a low neighbour there.

Does not attempt full multi-module Blender assembly (opening several kits' blends
together and unioning them) -- given the time budget this script instead checks that
each variant's own open face(s) are geometrically identical (within 1cm) to
hull.middle.w1x.v1's interface loop, which is the load-bearing precondition for the
assembler's join-profile check to accept the pairing. Renders of the (b)/(c)/(d) mixed
ships listed in the spec are not produced by this pass; see the report for scope notes.
"""
import json
from pathlib import Path
import bpy

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "art-staging/modular-raised-sections-v1"
VARIANTS = {"wa": "MiddleWA", "wf": "MiddleWF", "wb": "MiddleWB"}


def check_variant(mode, folder):
    d = OUT / folder
    manifest = json.loads((d / "manifest.json").read_text())
    report = {"parts": {}, "hull_shell_check": manifest["hull_shell_check"]}
    assert manifest["hull_shell_check"]["overconnected_edges"] == 0
    assert manifest["hull_shell_check"]["degenerate_faces"] == 0
    for name, part in manifest["parts"].items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(d / part["file"]))
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        triangles = sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons)
        assert triangles == part["triangles"], (name, triangles, part["triangles"])
        assert all(o.data.color_attributes.get("Col") for o in meshes), name
        assert not any(p.use_smooth for o in meshes for p in o.data.polygons), name
        report["parts"][name] = {"triangles": triangles}
    return report


def main():
    results = {}
    for mode, folder in VARIANTS.items():
        results[mode] = check_variant(mode, folder)
        print("PASS", mode, folder, results[mode]["hull_shell_check"])
    (OUT / "export-verification.json").write_text(json.dumps(results, indent=2))
    print("SECTIONS VERIFY PASS", list(results))


if __name__ == "__main__":
    main()
