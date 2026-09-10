using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeaSick.World;

/// Imports the five progression hulls, MEASURES them against WorldScale.Fleet,
/// and stands them in a row on the waterline so the progression can be seen
/// rather than argued about.
///
/// The measuring is not ceremony. The last Blender import arrived at 1/100
/// scale and nobody noticed until a boat was placed beside a tree, so this
/// reads the bounds off every imported mesh and fails loudly on any hull whose
/// length is not the length it was drawn at.
public static class SetupHullLab
{
    const string ArtDir = "Assets/_Project/Art/Ship/Hulls";
    const string ScenePath = "Assets/_Project/Scenes/HullLab.unity";

    struct Spec
    {
        public string file, label;
        public float loa, beam, draft, depth;
        public Spec(string f, string l, float lo, float b, float dr, float de)
        { file = f; label = l; loa = lo; beam = b; draft = dr; depth = de; }
    }

    static readonly Spec[] Fleet =
    {
        new Spec("hull_t1_raft",           "T1 log raft",      WorldScale.Fleet.RaftLoa,  WorldScale.Fleet.RaftBeam,  WorldScale.Fleet.RaftDraft,  0.61f),
        new Spec("hull_t2_skiff",          "T2 fishing skiff", WorldScale.Fleet.SkiffLoa, WorldScale.Fleet.SkiffBeam, WorldScale.Fleet.SkiffDraft, WorldScale.Fleet.SkiffDepth),
        new Spec("hull_t3_sloop",          "T3 coastal sloop", WorldScale.Fleet.SloopLoa, WorldScale.Fleet.SloopBeam, WorldScale.Fleet.SloopDraft, WorldScale.Fleet.SloopDepth),
        new Spec("hull_t4_brig",           "T4 brig",          WorldScale.Fleet.BrigLoa,  WorldScale.Fleet.BrigBeam,  WorldScale.Fleet.BrigDraft,  WorldScale.Fleet.BrigDepth),
        new Spec("hull_t5_shipoftheline",  "T5 three-decker",  WorldScale.Fleet.ShipLoa,  WorldScale.Fleet.ShipBeam,  WorldScale.Fleet.ShipDraft,  WorldScale.Fleet.ShipDepth),
    };

    /// Which way does the imported hull actually point? Measured, because
    /// the exporter's axis flags did not change what arrived.
    public static void Diagnose()
    {
        string path = $"{ArtDir}/hull_t5_shipoftheline.fbx";
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var b = MeshBounds(go);
        var sb = new StringBuilder();
        sb.AppendLine($"bounds size {b.size}  centre {b.center}");
        foreach (var t in go.GetComponentsInChildren<Transform>())
            sb.AppendLine($"  node {t.name}  pos {t.localPosition}  rot {t.localEulerAngles}  scale {t.localScale}");
        // long axis is X here; measure the breadth (Z) at each end of it
        float cutHi = b.max.x - b.size.x * 0.12f, cutLo = b.min.x + b.size.x * 0.12f;
        float hi = 0f, lo = 0f;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
            var m = f.transform.localToWorldMatrix;
            foreach (var v in f.sharedMesh.vertices)
            {
                var q = m.MultiplyPoint3x4(v);
                if (q.x >= cutHi) hi = Mathf.Max(hi, Mathf.Abs(q.z));
                if (q.x <= cutLo) lo = Mathf.Max(lo, Mathf.Abs(q.z));
            }
        }
        sb.AppendLine($"breadth at +X end {hi * 2f:F2} m, at -X end {lo * 2f:F2} m  " +
                      $"-> bow (the fine end) is on {(hi < lo ? "+X" : "-X")}");
        Debug.Log(sb.ToString());
    }

    public static void Execute()
    {
        AssetDatabase.Refresh();
        var log = new StringBuilder();
        log.AppendLine("=== HullLab ===");

        // 1. import settings, then measure what actually arrived
        var models = new List<GameObject>();
        bool ok = true;
        foreach (var s in Fleet)
        {
            string path = $"{ArtDir}/{s.file}.fbx";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogError($"HullLab: no importer at {path}"); ok = false; continue; }
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.importAnimation = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.isReadable = true;          // ScaleRuler-style probing needs it
            imp.SaveAndReimport();

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) { Debug.LogError($"HullLab: {path} did not import"); ok = false; continue; }
            models.Add(go);

            Bounds b = MeshBounds(go, true);
            // Blender bow +X / up +Z becomes Unity forward +Z / up +Y, and the
            // beam lands on X. Length is therefore Z, beam X, height Y.
            float loa = b.size.z, beam = b.size.x, height = b.size.y;
            // Which way is she pointing? A hull that imports at the right
            // SIZE facing the wrong way is the trap that costs a build: the
            // bow is the fine end, the transom the full one, so compare the
            // breadth at the two ends instead of trusting the exporter.
            float bowB = EndBreadth(go, +1), sternB = EndBreadth(go, -1);
            bool bowFwd = s.file.Contains("raft") || bowB < sternB;

            bool pass = Mathf.Abs(loa - s.loa) < 0.05f && Mathf.Abs(beam - s.beam) < 0.05f && bowFwd;
            if (!pass) ok = false;
            log.AppendLine($"{(pass ? "PASS" : "FAIL")} {s.label,-18} " +
                           $"LOA {loa,6:F2} m (drew {s.loa:F2})  beam {beam,5:F2} m (drew {s.beam:F2})  " +
                           $"height {height,5:F2} m  = {WorldScale.InCrew(loa):F1} crew long  " +
                           $"bow{(bowFwd ? "" : " NOT")} on +Z (ends {bowB:F2}/{sternB:F2} m)");
        }

        // 2. the scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var sun = Object.FindAnyObjectByType<Light>();
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.36f, 0.42f, 0.46f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.18f, 0.20f);

        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Waterline";
        water.transform.localScale = new Vector3(24f, 1f, 24f);
        water.GetComponent<Renderer>().sharedMaterial = Mat("HullLab_Water", new Color(0.055f, 0.20f, 0.27f), 0.15f);

        var root = new GameObject("Fleet");

        // the ship already in the game, as a footprint on the water: the new
        // hulls only mean something measured against what is there now
        var hullMat = Mat("HullLab_Wood", new Color(0.30f, 0.20f, 0.125f), 0.72f);
        var crewMat = Mat("HullLab_Crew", new Color(0.92f, 0.84f, 0.60f), 0.8f);
        var markMat = Mat("HullLab_Mark", new Color(0.90f, 0.66f, 0.24f), 0.7f);

        // The ship already in the game goes IN the row, not beside it: 24.2 m
        // lands between the sloop and the brig, and that is the only way to
        // see which rung the boat the player already sails is standing on.
        float x = 0f;
        for (int i = 0; i < models.Count; i++)
        {
            var s = Fleet[i];

            if (i == 3)   // the anchor length's slot, ahead of the brig
            {
                x += 8.44f * 0.5f + 3.5f;
                var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mark.name = "anchor_length_24.2m_footprint";
                mark.transform.SetParent(root.transform);
                mark.transform.localScale = new Vector3(8.44f, 0.08f, WorldScale.ShipLength);
                mark.transform.position = new Vector3(x, 0.04f, 0f);
                mark.GetComponent<Renderer>().sharedMaterial = markMat;
                x += 8.44f * 0.5f;
            }

            x += s.beam * 0.5f + 3.5f;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(models[i], root.transform);
            inst.name = s.file;
            inst.transform.position = new Vector3(x, 0f, 0f);
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = hullMat;

            // a 1.7 m crew member on the waterline at each bow
            var crew = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            crew.name = s.file + "_crew_1.7m";
            crew.transform.SetParent(root.transform);
            crew.transform.localScale = new Vector3(0.45f, WorldScale.Person * 0.5f, 0.45f);
            crew.transform.position = new Vector3(x, WorldScale.Person * 0.5f, s.loa * 0.5f + 2.4f);
            crew.GetComponent<Renderer>().sharedMaterial = crewMat;
            x += s.beam * 0.5f;
        }

        var cam = Object.FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.46f, 0.56f, 0.64f);
            cam.farClipPlane = 900f;

            cam.orthographic = false;
            cam.fieldOfView = 40f;
            cam.transform.position = new Vector3(x * 0.5f - 26f, 30f, -46f);
            cam.transform.rotation = Quaternion.Euler(23f, 33f, 0f);
            Shoot(cam, "/tmp/seasick-hulls.png");

            cam.orthographic = true;
            // half-HEIGHT, so the longest hull has to fit it, not the row
            cam.orthographicSize = Mathf.Max(WorldScale.Fleet.ShipLoa * 0.58f,
                                             x * 0.5f * 9f / 16f + 2f);
            cam.transform.position = new Vector3(x * 0.5f, 120f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Shoot(cam, "/tmp/seasick-hulls-plan.png");
            cam.orthographic = false;
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        log.AppendLine($"scene: {ScenePath}   shots: /tmp/seasick-hulls.png + -plan.png");
        log.AppendLine(ok ? "ALL HULLS MEASURE AS DRAWN" : "*** A HULL DID NOT MEASURE AS DRAWN ***");
        Debug.Log(log.ToString());
    }

    /// Breadth over the outer 12% of the hull at one end (+1 = the +Z end).
    static float EndBreadth(GameObject go, int sign)
    {
        Bounds b = MeshBounds(go, true);
        var all = go.GetComponentsInChildren<MeshFilter>();
        bool anyHull = System.Array.Exists(all, IsHull);
        float cut = sign > 0 ? b.max.z - b.size.z * 0.12f : b.min.z + b.size.z * 0.12f;
        float wide = 0f;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
            if (anyHull && !IsHull(f)) continue;      // a bowsprit is not a bow
            var m = f.transform.localToWorldMatrix;
            foreach (var v in f.sharedMesh.vertices)
            {
                var p = m.MultiplyPoint3x4(v);
                if (sign > 0 ? p.z >= cut : p.z <= cut) wide = Mathf.Max(wide, Mathf.Abs(p.x));
            }
        }
        return wide * 2f;
    }

    /// A fitting is not the hull. Once the brig grew masts, a bowsprit and a
    /// rudder, measuring the whole imported FBX made her 31.48 m long and
    /// 18.32 m tall against a 26.00 x 5.20 hull, and this gate failed her for
    /// it -- correctly, on the wrong quantity. The check is about the HULL, so
    /// it measures the hull: the mesh whose name ends `_Hull`. Everything
    /// else in the file is rig, and rig is meant to overhang.
    static bool IsHull(MeshFilter f) => f.name.EndsWith("_Hull");

    static Bounds MeshBounds(GameObject go, bool hullOnly = false)
    {
        var all = go.GetComponentsInChildren<MeshFilter>();
        var filters = hullOnly && System.Array.Exists(all, IsHull)
            ? System.Array.FindAll(all, IsHull) : all;
        bool any = false;
        Bounds b = new Bounds();
        foreach (var f in filters)
        {
            if (f.sharedMesh == null) continue;
            var mb = f.sharedMesh.bounds;
            // local to the prefab root
            var m = f.transform.localToWorldMatrix;
            var c = m.MultiplyPoint3x4(mb.center);
            var e = mb.extents;
            var ax = m.MultiplyVector(new Vector3(e.x, 0, 0));
            var ay = m.MultiplyVector(new Vector3(0, e.y, 0));
            var az = m.MultiplyVector(new Vector3(0, 0, e.z));
            var ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                                  Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                                  Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            var one = new Bounds(c, ext * 2f);
            if (!any) { b = one; any = true; } else b.Encapsulate(one);
        }
        return b;
    }

    static Material Mat(string name, Color c, float smoothCut)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f - smoothCut);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f - smoothCut);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        Directory.CreateDirectory("Assets/_Project/Materials/HullLab");
        string p = $"Assets/_Project/Materials/HullLab/{name}.mat";
        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(p));
        return m;
    }

    static void Shoot(Camera cam, string path)
    {
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
    }
}
