"""Measure raised-kit geometry for docs/RAISED-DECK.md sec 6/data + module JSON authoring.

Per module (Stern_W1, Midship_W1, Bow_W1):
  - upper-deck plank mesh footprint area (U^2 and m^2, upward-facing triangles only)
  - Hull_Shell surface area above Z 1.76 (U^2) and its area centroid Z (U)
  - deck-level (Z=1.76) half-breadth (max |y| of Hull_Shell at that height, U)
Also: SHA-256 of the Chimney.fbx triangle soup (same digest recipe
export_hull_hydrostatics.export_module uses) vs the existing v3 chimney mesh,
to decide fitting.chimney.v3 reuse vs a new fitting.chimney.raised.v1.

Same axis handling as export_raised_hydrostatics.py: a raw default FBX import
has hull length along Y: rotate 90 deg about Z (x'=-y, y'=x) before measuring
so Z stays up and results read in the module's +X-bow frame.

Run: blender --background --python tools/blender/measure_raised_modules.py
"""
import hashlib
import json
import math
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
KIT = ROOT / "art-staging" / "modular-raised-middle-v1"
DECK_Z = 1.76

MODULES = {
    "Stern_W1": {
        "hull_shell": KIT / "Stern_W1" / "Stern_W1__Hull_Shell.fbx",
        "planks": KIT / "Stern_W1" / "RaisedStern__UpperPlanks.fbx",
    },
    "Midship_W1": {
        "hull_shell": KIT / "Midship_W1" / "Midship_W1__Hull_Shell.fbx",
        "planks": KIT / "Midship_W1" / "RaisedMiddle__UpperPlanks.fbx",
    },
    "Bow_W1": {
        "hull_shell": KIT / "Bow_W1" / "Bow_W1__Hull_Shell.fbx",
        "planks": KIT / "Bow_W1" / "RaisedBowV2__PlankDetails.fbx",
    },
}


def import_rotated(fbx):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    assert len(meshes) == 1, (fbx, [o.name for o in meshes])
    obj = meshes[0]
    obj.rotation_euler.z = math.pi / 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return obj


def tri_soup(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    return [[tuple(mesh.vertices[i].co) for i in t.vertices] for t in mesh.loop_triangles]


def tri_area(tri):
    a, b, c = [Vec(*p) for p in tri]
    return 0.5 * ((b - a).cross(c - a)).length


class Vec:
    __slots__ = ("x", "y", "z")

    def __init__(self, x, y, z):
        self.x, self.y, self.z = x, y, z

    def __sub__(self, o):
        return Vec(self.x - o.x, self.y - o.y, self.z - o.z)

    def cross(self, o):
        return Vec(self.y * o.z - self.z * o.y, self.z * o.x - self.x * o.z, self.x * o.y - self.y * o.x)

    @property
    def length(self):
        return math.sqrt(self.x * self.x + self.y * self.y + self.z * self.z)


def upward_footprint_area(tris):
    total = 0.0
    for tri in tris:
        a, b, c = [Vec(*p) for p in tri]
        n = (b - a).cross(c - a)
        if n.z > 0:
            total += 0.5 * n.length
    return total


def shell_area_above(tris, z0, cap_z=None, cap_tol=0.05):
    # cap_z excludes the flat deck-cap band (its own footprint area is the
    # deck, not a topside WALL -- see deck_cap_footprint) so the returned
    # area/centroid describe the topside walls only, per spec sec 6.
    total = 0.0
    weighted_z = 0.0
    for tri in tris:
        zc = sum(p[2] for p in tri) / 3.0
        if zc <= z0:
            continue
        if cap_z is not None and zc > cap_z - cap_tol:
            continue
        area = tri_area(tri)
        total += area
        weighted_z += area * zc
    centroid_z = weighted_z / total if total > 0 else float("nan")
    return total, centroid_z


def half_breadth_at(tris, z0):
    best = 0.0
    for tri in tris:
        pts = tri
        for i in range(3):
            a, b = pts[i], pts[(i + 1) % 3]
            if (a[2] - z0) * (b[2] - z0) <= 0 and a[2] != b[2]:
                t = (z0 - a[2]) / (b[2] - a[2])
                y = a[1] + t * (b[1] - a[1])
                best = max(best, abs(y))
    return best


def geometry_hash(tris):
    return hashlib.sha256(json.dumps(tris, separators=(",", ":")).encode()).hexdigest()


def deck_cap_footprint(tris, deck_z, tol=0.05):
    # The flush upper deck is the flat top cap of the Hull_Shell mesh itself
    # (its z max == deck_z, e.g. 4.20 -- confirmed per-module below). The
    # UpperPlanks/PlankDetails FBX is decorative plank-seam trim laid over
    # part of it (its own footprint is a small fraction of the deck plan --
    # measured and reported separately), not the structural deck surface, so
    # it is the wrong mesh for the sec 6 "deck area x 110 kg/m^2" mass rule.
    cap_tris = [t for t in tris if min(p[2] for p in t) > deck_z - tol]
    return upward_footprint_area(cap_tris)


def main():
    results = {}
    for name, files in MODULES.items():
        shell = import_rotated(files["hull_shell"])
        shell_tris = tri_soup(shell)
        shell_z_max = max(p[2] for t in shell_tris for p in t)
        area_above, centroid_z = shell_area_above(shell_tris, DECK_Z, cap_z=shell_z_max)
        half_breadth = half_breadth_at(shell_tris, DECK_Z)
        deck_cap_u2 = deck_cap_footprint(shell_tris, shell_z_max)

        planks = import_rotated(files["planks"])
        plank_tris = tri_soup(planks)
        plank_footprint_u2 = upward_footprint_area(plank_tris)

        results[name] = {
            "hullShellZMaxU": shell_z_max,
            "topsideWallAreaU2": area_above,
            "topsideWallAreaM2": area_above * 0.25,
            "topsideWallAreaCentroidZU": centroid_z,
            "deckLevelHalfBreadthU": half_breadth,
            "deckLevelHalfBreadthM": half_breadth * 0.5,
            "deckCapFootprintU2": deck_cap_u2,
            "deckCapFootprintM2": deck_cap_u2 * 0.25,
            "upperDeckPlankTrimFootprintU2": plank_footprint_u2,
            "upperDeckPlankTrimFootprintM2": plank_footprint_u2 * 0.25,
        }
        print(name, json.dumps(results[name], indent=2))

    # Chimney hash: raised kit's Fittings/Chimney.fbx vs the existing v3 chimney.
    raised_chimney = import_rotated(KIT / "Fittings" / "Chimney.fbx")
    raised_hash = geometry_hash(tri_soup(raised_chimney))
    v3_path = (ROOT / "Assets/_Project/Resources/ShipModules/Meshes/Fittings_v3")
    v3_fbx = list(v3_path.glob("*himney*.fbx"))
    chimney_compare = {"raisedChimneySha256": raised_hash, "raisedTriCount": len(tri_soup(raised_chimney))}
    if v3_fbx:
        v3_obj = import_rotated(v3_fbx[0])
        v3_tris = tri_soup(v3_obj)
        v3_hash = geometry_hash(v3_tris)
        chimney_compare.update({
            "v3File": str(v3_fbx[0].relative_to(ROOT)),
            "v3Sha256": v3_hash,
            "v3TriCount": len(v3_tris),
            "match": v3_hash == raised_hash,
        })
    else:
        chimney_compare["v3File"] = None
        chimney_compare["match"] = None
    results["chimney"] = chimney_compare
    print("CHIMNEY", json.dumps(chimney_compare, indent=2))

    out = Path("/tmp/raised_measurements.json")
    out.write_text(json.dumps(results, indent=2))
    print("MEASURE PASS", out)


if __name__ == "__main__":
    main()
