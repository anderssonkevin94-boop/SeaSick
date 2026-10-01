using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The open carry crate v1 (2026-10-01, `art-staging/carry-crate-v1`)**:
    /// the crate a villager carries small goods in (`CarryLook`, Kevin: "carry
    /// them up to 8 at a time in an open lid crate so you can see what
    /// they're carrying").
    ///
    /// Copies `CarryCrate.fbx` into `Resources/Kits/Carry/` and imports it the
    /// way `ResourceKitImport` does (true metres, axis conversion baked, flat
    /// authored normals, the shared `GameColor` material), keeping the
    /// hierarchy so the empties survive: `Carry_Socket` (origin, bottom
    /// centre), `Grip_L/R`, `Slot_0..7` (item base points on the inner floor,
    /// row 0..3 the FRONT row). Then checks the README's numbers in the
    /// imported frame: one mesh on GameColor with vertex colours, ~0.59 x
    /// 0.225 x 0.38 m, bottom on y = 0, `Slot_0` at (-0.161, 0.06, +0.0725).
    ///
    /// `unity cmd eval --json --code 'return SeaSick.Dev.CarryCrateImport.Run();'`
    /// Idempotent.
    public static class CarryCrateImport
    {
        const string Source = "art-staging/carry-crate-v1/CarryCrate.fbx";
        const string Dir = "Assets/_Project/Resources/Kits/Carry";
        const string Path = Dir + "/CarryCrate.fbx";
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
            var mf = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (mf.Length != 1) return $"want one mesh, found {mf.Length}";
            var mr = mf[0].GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial != mat) log.AppendLine("!! renderer is not on GameColor");
            if (!mf[0].sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                log.AppendLine("!! no vertex colours");
            var toRoot = prefab.transform.worldToLocalMatrix * mf[0].transform.localToWorldMatrix;
            var b = new Bounds();
            bool any = false;
            var mb = mf[0].sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                var v = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                var p = toRoot.MultiplyPoint3x4(v);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
            log.AppendLine($"mesh bounds in the crate frame: size {b.size:F3} min {b.min:F3}");
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t == prefab.transform || t == mf[0].transform) continue;
                Vector3 p = prefab.transform.InverseTransformPoint(t.position);
                log.AppendLine($"  {t.name} {p:F3}");
            }
            var slot0 = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Slot_0");
            if (slot0 == null) log.AppendLine("!! no Slot_0");
            else
            {
                Vector3 p = prefab.transform.InverseTransformPoint(slot0.position);
                // The v1 export lands turned 180 degrees about up (Slot_0 at
                // (+0.161, 0.06, -0.0725)); `CarryLook.LoadCrate` turns it
                // back. Anything else is a real mismatch.
                bool asAuthored = (p - new Vector3(-0.161f, 0.06f, 0.0725f)).magnitude <= 0.01f;
                bool turned = (p - new Vector3(0.161f, 0.06f, -0.0725f)).magnitude <= 0.01f;
                if (turned) log.AppendLine("Slot_0 lands turned 180 deg about up: CarryLook turns the crate back (ok)");
                else if (!asAuthored)
                    log.AppendLine($"!! Slot_0 at {p:F3}, README says (-0.161, 0.060, 0.0725): axis mapping differs");
            }
            if (Mathf.Abs(b.min.y) > 0.01f) log.AppendLine("!! bottom not on y = 0");
            AssetDatabase.SaveAssets();
            log.Insert(0, "[CarryCrate] imported " + Path + "\n");
            Debug.Log(log.ToString());
            return log.ToString();
        }

    }
}
