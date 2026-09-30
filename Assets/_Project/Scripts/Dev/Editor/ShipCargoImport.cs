using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Astra ship cargo, Kevin approved 2026-09-29**: the six display props
    /// `Ship.ShipCargoDisplay` stands on the coaster's deck.
    ///
    /// - Barrels `Cargo_Barrel_Large` / `_Small` and sacks `Cargo_Sack_Cream`
    ///   / `_Ochre` from `art-staging/ship-cargo-v1`; crates `Cargo_Box_Large`
    ///   / `_Small` from `art-staging/ship-cargo-v2` (the V1 crates are
    ///   rejected). The FBXs are copied (shell `cp`) to
    ///   `Resources/Kits/Cargo/`; this only sets them up and checks them. Run:
    ///   `SeaSick.Dev.ShipCargoImport.Run()`.
    /// - True metres, bottom-centre pivots, NOT the ship's 0.5 authoring
    ///   scale. Astra's kits have come out of Blender both ways (the node
    ///   turn + x100 on the node, or baked into the vertices: `KitAxisFix`),
    ///   so each model tries file scale on/off x axis bake on/off and keeps
    ///   the first that measures to the manifest AND stands upright on y = 0.
    /// - Material: every `SS_Cargo_*` slot (wood, iron, cloth, rope) onto the
    ///   ONE shared `Art/Kits/Shared/GameColor.mat` (`SeaSick/Environment
    ///   Toon`, colour from the `GameColor` vertex paint) -- the same material
    ///   the sea kit's `SalvageCluster` already wears for the very same
    ///   `SS_Cargo_*` slots, so the crate on deck and the crate in the water
    ///   read as one thing, and the whole deck load batches. The README's
    ///   per-surface PBR responses (iron metallic .8, cloth/rope .92 rough)
    ///   are not reproduced: the mobile toon look has no metal term and the
    ///   props are a metre tall on a phone screen; the vertex paint already
    ///   separates iron hoops from staves.
    /// - Meshes stay readable: `ShipCargoDisplay` merges each prop's material
    ///   slots into one submesh once at load (one draw per prop, not four).
    ///
    /// Sizes are checked against `manifest.json` (Blender x, y, z -> Unity
    /// x, z, y); a miss is `!! MISMATCH` and the report starts with `FAIL`.
    /// Idempotent.
    public static class ShipCargoImport
    {
        const string ModelDir = "Assets/_Project/Resources/Kits/Cargo";
        const string GameColorPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";

        /// Expected size in Unity axes (x, y up, z), metres: v1 barrels and
        /// sacks, v2 crates, from each package's `manifest.json`.
        static readonly (string name, Vector3 size)[] Models =
        {
            ("Cargo_Barrel_Large", new Vector3(0.653f, 0.850f, 0.653f)),
            ("Cargo_Barrel_Small", new Vector3(0.526f, 0.620f, 0.526f)),
            ("Cargo_Sack_Cream", new Vector3(0.561f, 0.678f, 0.442f)),
            ("Cargo_Sack_Ochre", new Vector3(0.500f, 0.496f, 0.401f)),
            ("Cargo_Box_Large", new Vector3(0.820f, 0.560f, 0.640f)),
            ("Cargo_Box_Small", new Vector3(0.560f, 0.440f, 0.470f)),
        };
        const float Tolerance = 0.03f;

        [MenuItem("SeaSick/Art/Import ship cargo (Astra)")]
        public static string Run()
        {
            var log = new StringBuilder();
            bool ok = true;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var gameColor = AssetDatabase.LoadAssetAtPath<Material>(GameColorPath);
                if (gameColor == null) throw new System.Exception("missing shared material " + GameColorPath);
                foreach (var (name, size) in Models)
                    ok &= ImportOne(name, size, gameColor, log);
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                log.AppendLine("EXCEPTION " + e.Message);
                ok = false;
            }
            string report = (ok ? "OK " : "FAIL ") + "[ShipCargo]\n" + log;
            Debug.Log(report);
            return report;
        }

        static bool ImportOne(string name, Vector3 want, Material gameColor, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".fbx";
            if (!System.IO.File.Exists(path))
            {
                log.AppendLine(name + ": !! missing " + path);
                return false;
            }

            Bounds got = default;
            bool fits = false;
            string how = "";
            foreach (bool bake in new[] { true, false })
                foreach (bool fileScale in new[] { true, false })
                {
                    Configure(path, fileScale, bake, gameColor);
                    got = Measure(path);
                    how = $"useFileScale={fileScale} bakeAxis={bake}";
                    if (Fits(got, want)) { fits = true; goto done; }
                }
            done:

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var filters = go.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            int tris = filters.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)(f.sharedMesh.GetIndexCount(i) / 3)));
            int subs = filters.Sum(f => f.sharedMesh.subMeshCount);
            bool colours = filters.Length > 0 && filters.All(f => f.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color));
            bool clean = rends.Length > 0 && rends.SelectMany(r => r.sharedMaterials).All(m => m == gameColor);
            bool extras = go.GetComponentsInChildren<Camera>(true).Length > 0
                || go.GetComponentsInChildren<Light>(true).Length > 0
                || go.GetComponentsInChildren<Collider>(true).Length > 0;
            var s = got.size;
            log.AppendLine($"{name}: {how}, size {s.x:F3} x {s.y:F3} x {s.z:F3} m (want {want.x:F3} x {want.y:F3} x {want.z:F3}), "
                + $"base y {got.min.y:F3}, centre xz {got.center.x:F3},{got.center.z:F3}, {tris} tris, {filters.Length} mesh(es) / {subs} submesh(es)"
                + (fits ? "" : "  !! MISMATCH size/pivot")
                + (colours ? "" : "  !! no GameColor vertex colours")
                + (clean ? "" : "  !! stray material")
                + (extras ? "  !! has camera/light/collider" : ""));
            return fits && colours && clean && !extras;
        }

        static void Configure(string path, bool fileScale, bool bake, Material gameColor)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model importer at " + path);
            mi.globalScale = 1f;
            mi.useFileScale = fileScale;
            mi.bakeAxisConversion = bake;
            mi.importNormals = ModelImporterNormals.Import;   // authored normals
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
            mi.isReadable = true;                             // merged once at runtime
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            foreach (var n in embedded)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), gameColor);
            mi.SaveAndReimport();
        }

        /// Bounds in the model root's frame (metres), through every node.
        static Bounds Measure(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// Size within 3 % (1 cm floor), base on y = 0, centred over the origin.
        static bool Fits(Bounds got, Vector3 want)
        {
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(got.size[i] - want[i]) > Mathf.Max(0.01f, want[i] * Tolerance)) return false;
            return Mathf.Abs(got.min.y) < 0.02f && Mathf.Abs(got.center.x) < 0.05f && Mathf.Abs(got.center.z) < 0.05f;
        }
    }
}
