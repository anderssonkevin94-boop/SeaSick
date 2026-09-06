using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SeaSick.Ship;

/// What the ART actually became once Unity had imported it.
///
/// `LadderCheck` asks whether the ladder loads and behaves. This asks the
/// question one step earlier and the one that has been open since the art was
/// made: did the meshes come in wearing the palette, or wearing Unity's
/// default grey? A hull that resolves, floats and sails and is the wrong
/// colour passes every probe there was.
public static class LadderImport
{
    const string Material = "Assets/_Project/Materials/SeaSick_Hull.mat";
    const string Palette = "Assets/_Project/Art/Ship/Hulls/seasick_palette.png";

    [MenuItem("SeaSick/Shipyard/Check the import")]
    public static void Check()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("LadderImport:");

        // --- the texture, because every swatch is 16 px of flat colour -------
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Palette);
        var ti = AssetImporter.GetAtPath(Palette) as TextureImporter;
        if (tex == null || ti == null) sb.AppendLine("  PALETTE MISSING at " + Palette);
        else
        {
            // A swatch is 16 px and every face samples ONE point of it, so
            // there is no minification to defend against and all three of
            // Unity's defaults — mipmaps, bilinear, compression — only smear
            // one swatch into the next.
            bool ok = !ti.mipmapEnabled && ti.filterMode == FilterMode.Point
                      && ti.npotScale == TextureImporterNPOTScale.None;
            sb.AppendLine($"  palette {tex.width}x{tex.height} "
                + $"mips {ti.mipmapEnabled} filter {ti.filterMode} "
                + $"npot {ti.npotScale} format {tex.format}"
                + (ok ? "" : "   <-- WRONG, swatches will bleed"));
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(Material);
        if (mat == null) sb.AppendLine("  MATERIAL MISSING at " + Material);
        else sb.AppendLine($"  material '{mat.name}' shader {mat.shader.name} "
                           + $"baseMap {(mat.GetTexture("_BaseMap") == null ? "NONE" : mat.GetTexture("_BaseMap").name)}");

        // --- every rung, and what its renderers are actually wearing --------
        int n = ShipLadder.Count;
        int dressed = 0, bare = 0, noLids = 0;
        for (int i = 0; i < n; i++)
        {
            var node = ShipLadder.Node(i);
            var go = Resources.Load<GameObject>(node.ResourcePath);
            if (go == null) { sb.AppendLine($"  N{i:00} MESH MISSING"); continue; }

            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            var mats = new HashSet<string>();
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                    mats.Add(m == null ? "<null>" : m.name);
            int tris = rends.Sum(r =>
            {
                var f = r.GetComponent<MeshFilter>();
                return f != null && f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0;
            });
            int lids = go.GetComponentsInChildren<Transform>(true)
                         .Count(t => t.name.Contains("PortLid"));
            bool wearing = mats.Count == 1 && mats.Contains("SeaSick_Hull");
            if (wearing) dressed++; else bare++;
            if (node.gun_rows > 0 && lids != node.ports_per_side * 2) noLids++;

            sb.AppendLine($"  N{i:00} {node.label,-18} {rends.Length,3} parts "
                + $"{tris,6} tris   lids {lids,3}/{node.ports_per_side * 2,-3} "
                + $"materials [{string.Join(", ", mats)}]"
                + (wearing ? "" : "   <-- NOT the palette"));
        }
        sb.AppendLine($"  {dressed}/{n} rungs wear SeaSick_Hull"
                      + (bare > 0 ? $", {bare} DO NOT" : "")
                      + (noLids > 0 ? $"; {noLids} have the wrong lid count" : ""));
        Debug.Log(sb.ToString());
    }

    /// Bind the palette material explicitly on every ladder FBX, and hold the
    /// palette's own import settings.
    ///
    /// **The remap is keyed on the name Unity gave the material, not the name
    /// Blender gave it.** The generator calls it `SeaSick_Hull`; the material
    /// description importer named the imported one `seasick_palette`, after
    /// the texture, so a remap written against the Blender name bound nothing
    /// at all and reported success. The names are read back off the imported
    /// model instead of assumed.
    [MenuItem("SeaSick/Shipyard/Bind the hull material")]
    public static void Bind()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Material);
        if (mat == null) { Debug.LogError("LadderImport: no material at " + Material); return; }

        // The atlas is 16 px swatches of flat colour and every face samples a
        // single point of one. Mipmaps average neighbours, bilinear bleeds
        // across the boundary, compression invents gradients, and NPOT
        // rescaling moves the swatches out from under the UVs — all four are
        // on by default and all four are wrong here.
        var ti = AssetImporter.GetAtPath(Palette) as TextureImporter;
        if (ti != null)
        {
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.anisoLevel = 0;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 256;
            ti.SaveAndReimport();
        }

        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[]
                 { "Assets/_Project/Resources/Ladder", "Assets/_Project/Resources/Kit" }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));

        int bound = 0, slots = 0;
        foreach (var path in paths)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            // **InPrefab, or the remap below is ignored.** With the location
            // set to External, Unity searches the project for a material of
            // that name and EXTRACTS one next to the model if it finds none —
            // which is what had been happening: every hull was wearing an
            // auto-generated `seasick_palette.mat` with no texture in it,
            // sitting in a Materials folder inside Resources, shipped in the
            // build and never opened by anybody. The remap only binds when the
            // material is the model's own.
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;

            // Whatever the model came in wearing, by name, is what gets
            // remapped — including the name the generator used, so this keeps
            // working if the importer's naming changes back.
            var names = new HashSet<string> { "SeaSick_Hull" };
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null)
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null) names.Add(m.name);

            foreach (var name in names)
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(
                    typeof(Material), name), mat);
                slots++;
            }
            EditorUtility.SetDirty(mi);
            mi.SaveAndReimport();
            bound++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"LadderImport: bound SeaSick_Hull on {bound} models, {slots} slots");
    }

    /// What the importer thinks the material slots are called, and where the
    /// material a renderer is actually using lives. Run when a remap is
    /// written into the .meta and changes nothing.
    [MenuItem("SeaSick/Shipyard/Why is the material wrong")]
    public static void Why()
    {
        const string path = "Assets/_Project/Resources/Ladder/n12_brig.fbx";
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        var sb = new System.Text.StringBuilder("LadderImport.Why:\n");
        sb.AppendLine($"  materialImportMode {mi.materialImportMode} "
                    + $"location {mi.materialLocation} search {mi.materialSearch}");
        foreach (var kv in mi.GetExternalObjectMap())
            sb.AppendLine($"  remap [{kv.Key.type.Name} '{kv.Key.name}'] -> "
                        + $"{(kv.Value == null ? "NULL" : AssetDatabase.GetAssetPath(kv.Value))}");
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            sb.AppendLine($"  sub-asset {o.GetType().Name} '{o.name}'");
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var r = go.GetComponentsInChildren<MeshRenderer>(true)[0];
        var m = r.sharedMaterial;
        sb.AppendLine($"  renderer '{r.name}' material '{(m == null ? "NULL" : m.name)}' "
                    + $"at '{(m == null ? "-" : AssetDatabase.GetAssetPath(m))}'");
        Debug.Log(sb.ToString());
    }

    /// Put a few rungs in a THROWAWAY scene, lit, so the import can be looked
    /// at rather than only counted. Never touches the scene you had open.
    [MenuItem("SeaSick/Shipyard/Preview the import")]
    public static void Preview()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager
            .NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                      UnityEditor.SceneManagement.NewSceneMode.Single);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.05f;
        sun.color = new Color(1f, 0.97f, 0.92f);
        sun.transform.rotation = Quaternion.Euler(46f, 32f, 0f);
        // Flat and neutral. A blue sky ambient turned every unlit face bluish,
        // which made a pale canvas sail look like slate and sent me hunting a
        // UV bug that was not there — the swatches were right all along.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.41f, 0.40f);

        float z = 0f;
        foreach (int i in new[] { 0, 6, 12, 19 })
        {
            var node = ShipLadder.Node(i);
            var src = Resources.Load<GameObject>(node.ResourcePath);
            if (src == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = $"N{i:00}_{node.label}";
            go.transform.position = new Vector3(0f, 0f, z);
            z += node.length + 8f;

            // Run the lids out, on the ship that has three decks of them. This
            // is the FIRST time the runtime hinge has met a real imported FBX:
            // it measures its own open direction, and until an exporter had
            // been through it there was no way to know it measured right.
            if (i != 19) continue;
            var ship = new GameObject($"N{i:00}_cleared");
            ship.transform.position = new Vector3(0f, 0f, z);
            // A plain clone, NOT PrefabUtility.InstantiatePrefab: `PortLids`
            // reparents each lid under a hinge it makes, and the editor will
            // not let you restructure a prefab instance — so the preview
            // reported 72 lids open while every one of them stayed shut.
            // `Shipyard.SwapVisual` uses Object.Instantiate, so this is also
            // what the game actually does.
            var vis = Object.Instantiate(src);
            vis.name = "HullVisual";
            vis.transform.SetParent(ship.transform, false);
            // A gun at every port she has: each gun deck, every bay. The
            // positions are built the way `Shipyard.GunPositions` builds them,
            // so this exercises the matching and not just the hinge.
            var guns = new List<Vector3>();
            for (int t = node.tiers - node.gun_rows; t < node.tiers; t++)
                for (int b = 0; b < node.bays; b++)
                    guns.Add(new Vector3(node.beam * 0.34f - 0.6f,
                                         node.tier_floor[t] + 0.47f, node.bay_x[b]));
            var lids = ship.AddComponent<PortLids>();
            lids.Fit(vis.transform, guns);
            Debug.Log($"LadderImport: N{i:00} lids {lids.LidCount}, "
                    + $"asked to open {lids.OpenCount} for {guns.Count} guns");
            z += node.length + 8f;
        }
        Debug.Log($"LadderImport: preview scene built");
    }

    /// Which swatch each part of a hull is actually pointing at, read off the
    /// imported mesh and sampled out of the imported texture.
    ///
    /// The UVs are single points at swatch centres, so this is the whole of
    /// "is it the right colour" — and it answers it without a screenshot,
    /// where lighting can make a pale sail look like a dark one.
    [MenuItem("SeaSick/Shipyard/Sample the swatches")]
    public static void Swatches()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Palette);
        var ti = AssetImporter.GetAtPath(Palette) as TextureImporter;
        bool wasReadable = ti.isReadable;
        if (!wasReadable) { ti.isReadable = true; ti.SaveAndReimport(); }
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Palette);

        var go = Resources.Load<GameObject>(ShipLadder.Node(12).ResourcePath);
        var sb = new System.Text.StringBuilder("LadderImport.Swatches (N12 brig):\n");
        const int grid = 8;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = f.sharedMesh;
            if (m == null || m.uv == null || m.uv.Length == 0) continue;
            var cells = new HashSet<int>();
            foreach (var uv in m.uv)
            {
                int cx = Mathf.Clamp((int)(uv.x * grid), 0, grid - 1);
                int cy = Mathf.Clamp((int)(uv.y * grid), 0, grid - 1);
                cells.Add(cy * grid + cx);
            }
            var parts = new List<string>();
            foreach (var c in cells.OrderBy(x => x))
            {
                var col = tex.GetPixelBilinear((c % grid + 0.5f) / grid,
                                               (c / grid + 0.5f) / grid);
                parts.Add($"{c}:({col.r:0.00},{col.g:0.00},{col.b:0.00})");
            }
            sb.AppendLine($"  {f.name,-28} {string.Join("  ", parts)}");
        }
        if (!wasReadable) { ti.isReadable = false; ti.SaveAndReimport(); }
        Debug.Log(sb.ToString());
    }

    /// Did the lids actually swing OUT? Measured off the two three-deckers the
    /// preview scene puts side by side — one shut, one cleared for action.
    ///
    /// This is the check the hinge could not have before the art was
    /// exported: `PortLids` works out its own open direction, and a component
    /// that decided wrong would report a tidy 72/72 while folding every lid
    /// into the ship.
    [MenuItem("SeaSick/Shipyard/Did the lids open outboard")]
    public static void LidReach()
    {
        var sb = new System.Text.StringBuilder("LadderImport.LidReach:\n");
        foreach (var name in new[] { "N19_Three-decker", "N19_cleared" })
        {
            var root = GameObject.Find("/" + name);
            if (root == null) { sb.AppendLine($"  {name}: not in the scene"); continue; }
            float reach = 0f, top = float.MinValue;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.Contains("PortLid")) continue;
                var f = r.GetComponent<MeshFilter>();
                var b = f.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1,
                        (c & 4) == 0 ? -1 : 1));
                    var p = root.transform.InverseTransformPoint(
                        r.transform.TransformPoint(corner));
                    reach = Mathf.Max(reach, Mathf.Abs(p.x));
                    top = Mathf.Max(top, p.y);
                }
                n++;
            }
            sb.AppendLine($"  {name,-18} {n,3} lids   furthest outboard "
                        + $"{reach:0.00} m   highest {top:0.00} m");
        }
        sb.AppendLine("  a lid that opened INBOARD would reach LESS than the shut one");
        Debug.Log(sb.ToString());
    }

    /// Put the working scene back. `Preview` replaces whatever is open with a
    /// throwaway; this returns to the sea without saving it.
    [MenuItem("SeaSick/Shipyard/Close the preview")]
    public static void Restore()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            "Assets/_Project/Scenes/Sea.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Single);
        Debug.Log("LadderImport: back in Sea.unity");
    }

    /// Menu-free entry point for the MCP bridge: bind, then report.
    public static void Execute()
    {
        Bind();
        Check();
    }
}
