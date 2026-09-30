using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Astra's sea discovery kit v1, Kevin approved 2026-09-30**
    /// (`art-staging/sea-discovery-v1`): message bottle, salvage cluster,
    /// lashed board bundle, two broken boards and three reef clusters.
    ///
    /// The eight FBXs are copied (shell `cp`, nothing else) to
    /// `Resources/Kits/Sea/`, where `SeaKit` loads them once. Run:
    /// `SeaSick.Dev.SeaKitImport.Run()`.
    ///
    /// - Import: true metres (`useFileScale` + `globalScale` 1, with a
    ///   fallback to ignoring the file scale if the measured size is off by
    ///   the usual Blender x100), flat normals imported as authored, no
    ///   animation, cameras, lights, colliders, blend shapes, tangents or
    ///   secondary UVs, no mesh compression, unreadable. FBX Y-up/-Z-forward
    ///   is Unity's own convention; the README's Blender frame lands as
    ///   Blender (x, y, z) -> Unity (-x, z, -y), so long boards run along
    ///   Unity Z, the reef and bottle heights along Y.
    /// - Materials, remapped by name: every opaque material (the shared
    ///   `SS_SeaDiscovery_GameColor` and the salvage cluster's four
    ///   `SS_Cargo_*` slots, all vertex-colour only) onto the ONE shared
    ///   `Art/Kits/Shared/GameColor.mat` (`SeaSick/Environment Toon`); the
    ///   bottle's `SS_Bottle_Glass` onto `Art/Kits/Sea/BottleGlass.mat`.
    ///   Blender's Mix Shader does not transfer.
    /// - `BottleGlass.mat`: mobile URP Unlit, transparent alpha blend, green
    ///   tint alpha .30 (README: .26-.35), ZWrite off, cull back, no shadow
    ///   caster, queue 3000. No scene-colour refraction (nothing here samples
    ///   `_CameraOpaqueTexture`), so it costs one extra blended draw. The
    ///   parchment and cork are a separate opaque mesh and stay visible
    ///   through it.
    ///
    /// Every model's measured size is checked against the Blender bounds in
    /// the README; a miss is flagged `!! MISMATCH` in the report and the
    /// result starts with `FAIL`. Idempotent.
    public static class SeaKitImport
    {
        const string ModelDir = "Assets/_Project/Resources/Kits/Sea";
        const string MatDir = "Assets/_Project/Art/Kits/Sea";
        const string GameColorPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";
        const string GlassPath = MatDir + "/BottleGlass.mat";
        const string GlassSlot = "SS_Bottle_Glass";

        /// Expected size in Unity axes (x, y up, z), metres, from the
        /// Blender bounds in the export (Blender y -> Unity z, z -> y).
        static readonly (string name, Vector3 size)[] Models =
        {
            ("MessageBottle", new Vector3(0.340f, 0.441f, 0.832f)),
            ("SalvageCluster", new Vector3(2.265f, 0.560f, 1.971f)),
            ("LashedBoardBundle", new Vector3(0.827f, 0.172f, 1.755f)),
            ("BrokenBoardShort", new Vector3(0.240f, 0.070f, 1.315f)),
            ("BrokenBoardLong", new Vector3(0.340f, 0.070f, 2.215f)),
            ("ReefSplitPeak", new Vector3(1.967f, 1.700f, 1.324f)),
            ("ReefLowLedge", new Vector3(1.953f, 1.010f, 1.062f)),
            ("ReefLeaningTeeth", new Vector3(1.855f, 1.600f, 0.933f)),
        };
        const float Tolerance = 0.05f;

        [MenuItem("SeaSick/Art/Import sea discovery kit")]
        public static string Run()
        {
            var log = new StringBuilder();
            bool ok = true;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var gameColor = AssetDatabase.LoadAssetAtPath<Material>(GameColorPath);
                if (gameColor == null) throw new System.Exception("missing shared material " + GameColorPath);
                var glass = MakeGlass(log);

                foreach (var (name, size) in Models)
                    ok &= ImportOne(name, size, gameColor, glass, log);

                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                log.AppendLine("EXCEPTION " + e.Message);
                ok = false;
            }
            string report = (ok ? "OK " : "FAIL ") + "[SeaKit]\n" + log;
            Debug.Log(report);
            return report;
        }

        // --- the glass ----------------------------------------------------------

        static Material MakeGlass(StringBuilder log)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.Exception("missing shader Universal Render Pipeline/Unlit");
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Kits", "Sea");
            var m = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            if (m == null) { m = new Material(shader) { name = "BottleGlass" }; AssetDatabase.CreateAsset(m, GlassPath); }
            m.shader = shader;
            var tint = new Color(0.22f, 0.68f, 0.40f, 0.30f);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", null);
            // URP transparent alpha blend, set by hand (no inspector script):
            // surface type, blend factors, no depth write, back faces culled.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            log.AppendLine($"glass: {GlassPath} on {shader.name}, alpha {tint.a:F2}, tint ({tint.r:F2},{tint.g:F2},{tint.b:F2}), ZWrite off, queue {m.renderQueue}");
            return m;
        }

        // --- one model ----------------------------------------------------------

        static bool ImportOne(string name, Vector3 want, Material gameColor, Material glass, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null
                && !System.IO.File.Exists(path))
            {
                log.AppendLine(name + ": !! missing " + path);
                return false;
            }

            Vector3 got = default;
            bool fits = false;
            foreach (bool fileScale in new[] { true, false })
            {
                Configure(path, fileScale, gameColor, glass);
                got = Measure(path);
                fits = Fits(got, want);
                if (fits) { log.AppendLine($"{name}: useFileScale={fileScale}"); break; }
            }

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            int tris = go.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null)
                .Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)(f.sharedMesh.GetIndexCount(i) / 3)));
            var mats = rends.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct();
            bool clean = rends.Length > 0 && rends.SelectMany(r => r.sharedMaterials)
                .All(m => m == gameColor || m == glass);
            bool glassRight = name != "MessageBottle" || rends.All(r =>
                r.name.StartsWith("Bottle_Glass") == r.sharedMaterials.Any(m => m == glass));
            bool extras = go.GetComponentsInChildren<Camera>(true).Length > 0
                || go.GetComponentsInChildren<Light>(true).Length > 0
                || go.GetComponentsInChildren<Collider>(true).Length > 0
                || go.GetComponentsInChildren<Animator>(true).Length > 0;

            log.AppendLine($"{name}: size {got.x:F3} x {got.y:F3} x {got.z:F3} m (want {want.x:F3} x {want.y:F3} x {want.z:F3}), "
                + $"{tris} tris, {rends.Length} mesh(es), mats [{string.Join(", ", mats)}]"
                + (fits ? "" : "  !! MISMATCH size")
                + (clean ? "" : "  !! stray material")
                + (glassRight ? "" : "  !! glass not on Bottle_Glass")
                + (extras ? "  !! has camera/light/collider/animator" : ""));
            return fits && clean && glassRight && !extras;
        }

        static void Configure(string path, bool fileScale, Material gameColor, Material glass)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model importer at " + path);
            mi.globalScale = 1f;
            mi.useFileScale = fileScale;
            mi.importNormals = ModelImporterNormals.Import;   // flat facets are authored
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
            // Start clean so the FBX's own (Blender-suffixed) names show.
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            foreach (var n in embedded)
            {
                var target = n.StartsWith(GlassSlot) ? glass : gameColor;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), target);
            }
            mi.SaveAndReimport();
        }

        /// The model's size in its own frame (metres), from the mesh bounds
        /// carried through every node's transform.
        static Vector3 Measure(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b.size : Vector3.zero;
        }

        static bool Fits(Vector3 got, Vector3 want)
        {
            // Absolute floor for the .07 m boards; relative otherwise.
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(got[i] - want[i]) > Mathf.Max(0.01f, want[i] * Tolerance)) return false;
            return true;
        }
    }
}
