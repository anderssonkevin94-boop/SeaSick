using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The runner's wheelbarrow (2026-10-02; v2 variant A since Kevin
    /// approved it, `art-staging/wheelbarrow-v2/Wheelbarrow_A.fbx`; v1 in
    /// `art-staging/wheelbarrow-v1`)**: the timber barrow a store runner
    /// pushes (`World.RunnerBarrow`). Same node names / markers as v1, wider
    /// tray walls (0.71 m outside, the 0.54 x 0.59 m floor is unchanged).
    ///
    /// Copies the FBX over `Resources/Kits/Carry/Wheelbarrow.fbx` (same GUID) into `Resources/Kits/Carry/` next to the
    /// carry crate and imports it exactly the way `CarryCrateImport` does
    /// (true metres, axis conversion baked, authored normals, no animation /
    /// tangents / colliders, the shared `GameColor` material), keeping the
    /// hierarchy so the empties survive: `Wheelbarrow` (root = the ground
    /// under the pusher's hips) > `Tilt_Pivot` (on the axle) >
    /// `Wheelbarrow_Body`, `Wheel`, `Grip_L/R`, `Load_Anchor`, `Slot_0..11`.
    /// Every face and the vertex colours are kept as delivered (760 tris; v1 500).
    /// Then checks the README's numbers in the imported frame: two meshes on
    /// GameColor with vertex colours, ~0.71 x 0.71 x 1.57 m (v1: 0.60 x 0.70 x 1.56), bottom on y = 0,
    /// `Tilt_Pivot` at (0, 0.24, 1.55), `Slot_0` at (-0.18, 0.43, 1.116).
    /// `RunnerBarrow` reads the markers from the imported model, so a
    /// half-turned import (the crate's) is reported here and turned back
    /// there.
    ///
    /// `unity cmd eval --json --code 'return SeaSick.Dev.WheelbarrowImport.Run();'`
    /// Idempotent.
    public static class WheelbarrowImport
    {
        const string Source = "art-staging/wheelbarrow-v2/Wheelbarrow_A.fbx";
        const string Dir = "Assets/_Project/Resources/Kits/Carry";
        const string Path = Dir + "/Wheelbarrow.fbx";
        const string MaterialPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";

        public static string Run()
        {
            var log = new StringBuilder();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string src = System.IO.Path.Combine(root, Source);
            if (!File.Exists(src)) return "missing " + src;
            Directory.CreateDirectory(System.IO.Path.Combine(root, Dir));
            File.Copy(src, System.IO.Path.Combine(root, Path), true);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) return "missing shared material " + MaterialPath;
            var mi = AssetImporter.GetAtPath(Path) as ModelImporter;
            if (mi == null) return "no model at " + Path;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();
            foreach (var n in AssetDatabase.LoadAllAssetsAtPath(Path).OfType<Material>().Select(m => m.name).Distinct().ToList())
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            mi.SaveAndReimport();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (mfs.Length != 2) log.AppendLine($"!! want two meshes (body + wheel), found {mfs.Length}");
            int tris = 0;
            var b = new Bounds();
            bool any = false;
            foreach (var mf in mfs)
            {
                tris += mf.sharedMesh.triangles.Length / 3;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mr.sharedMaterial != mat) log.AppendLine($"!! {mf.name} is not on GameColor");
                if (!mf.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                    log.AppendLine($"!! {mf.name} has no vertex colours");
                else
                {
                    // Environment Toon takes the vertex colour as LINEAR albedo.
                    // build.py's palette is linear and must be exported with
                    // colors_type='LINEAR'; the default sRGB encoding lifts the
                    // dark frame 0.17 -> 0.45 and the barrow washes out to beige.
                    float darkest = mf.sharedMesh.colors.Min(c => Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                    log.AppendLine($"  {mf.name} darkest vertex colour {darkest:0.000} (linear palette: 0.150 wheel hub / 0.170 frame)");
                    if (darkest > 0.3f) log.AppendLine($"!! {mf.name} vertex colours look sRGB-encoded (washed out): re-export with colors_type='LINEAR'");
                }
                var toRoot = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var v = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = toRoot.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            log.AppendLine($"{tris} tris; bounds in the barrow frame: size {b.size:F3} min {b.min:F3} max {b.max:F3}");
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t == prefab.transform) continue;
                Vector3 p = prefab.transform.InverseTransformPoint(t.position);
                log.AppendLine($"  {t.name} {p:F3} (parent {t.parent.name}, local rot {t.localEulerAngles:F1}, scale {t.lossyScale:F2})");
            }
            Transform Find(string n) => prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            var pivot = Find("Tilt_Pivot");
            var slot0 = Find("Slot_0");
            if (pivot == null || slot0 == null || Find("Wheel") == null || Find("Grip_L") == null)
                log.AppendLine("!! a marker is missing (Tilt_Pivot / Wheel / Grip_L / Slot_0)");
            else
            {
                Vector3 pp = prefab.transform.InverseTransformPoint(pivot.position);
                Vector3 s0 = prefab.transform.InverseTransformPoint(slot0.position);
                bool asAuthored = (pp - new Vector3(0f, 0.24f, 1.55f)).magnitude <= 0.01f
                                  && (s0 - new Vector3(-0.18f, 0.43f, 1.1162f)).magnitude <= 0.01f;
                bool turned = (pp - new Vector3(0f, 0.24f, -1.55f)).magnitude <= 0.01f;
                if (asAuthored) log.AppendLine("markers land as the README says (barrow in front, +Z)");
                else if (turned) log.AppendLine("barrow lands turned 180 deg about up: RunnerBarrow turns it back (ok)");
                else log.AppendLine($"!! Tilt_Pivot at {pp:F3}, Slot_0 at {s0:F3}: axis mapping differs from the README");
            }
            if (Mathf.Abs(b.min.y) > 0.01f) log.AppendLine("!! bottom not on y = 0");
            AssetDatabase.SaveAssets();
            log.Insert(0, "[Wheelbarrow] imported " + Path + "\n");
            Debug.Log(log.ToString());
            return log.ToString();
        }
    }
}
