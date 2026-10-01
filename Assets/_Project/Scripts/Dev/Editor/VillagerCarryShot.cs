using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

/// **Edit-mode contact sheets of every carried load** (2026-10-01, Kevin:
/// "we need to calibrate each item the villagers are holding"), no play
/// mode needed. Same rig as `VillagerToolShot`: `CrewMember.prefab` high
/// above the world with `VillagerActing`, the Animator and `Step(dt)` driven
/// by hand, so the `Carry` clip plays at its walking rate and the load rides
/// the socket exactly as in the game (`CarryLook`).
///
/// `Measure()` logs the Carry clip's arms and head in the carry socket's own
/// frame (the numbers `CarryLook` is laid out against). `Run(dir)` writes
/// portrait JPG sheets (`carry-*.jpg`, rows = loads, columns = clip frames /
/// views) and `Eat(dir)` the reach -> hold dish -> eat sequence
/// (`eat-*.jpg`). Everything it creates is destroyed before it returns.
///
/// `unity cmd eval --json --code 'return VillagerCarryShot.Run("<dir>");'`
public static class VillagerCarryShot
{
    const string Prefab = "Assets/_Project/Prefabs/CrewMember.prefab";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic;
    const float Dt = 1f / 30f;

    sealed class Rig
    {
        public GameObject body, lightGo, camGo;
        public Animator anim;
        public VillagerActing acting;
        public MethodInfo step;
        public Camera cam;
        public RenderTexture rt;
        public Texture2D tex;
        public Transform spine;
        public Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

        public void Tick(float dt)
        {
            acting.Commanded(VillagerGaits.CarryClip);
            if (anim != null) anim.Update(dt);
            step.Invoke(acting, new object[] { dt });
        }

        public void Dispose()
        {
            if (camGo != null) { cam.targetTexture = null; Object.DestroyImmediate(camGo); }
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (tex != null) Object.DestroyImmediate(tex);
            if (lightGo != null) Object.DestroyImmediate(lightGo);
            if (body != null) Object.DestroyImmediate(body);
        }
    }

    static Vector3 Origin => new Vector3(0f, 1500f, 0f);

    static Rig Make(int cell)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (prefab == null) return null;
        var r = new Rig();
        r.body = (GameObject)Object.Instantiate(prefab);
        r.body.name = "VillagerCarryShot_Body";
        r.body.transform.SetPositionAndRotation(Origin, Quaternion.identity);
        r.anim = r.body.GetComponentInChildren<Animator>();
        r.acting = r.body.AddComponent<VillagerActing>();
        if (r.anim != null) { r.anim.Rebind(); r.anim.Update(0f); }
        foreach (var smr in r.body.GetComponentsInChildren<SkinnedMeshRenderer>())
            smr.forceMatrixRecalculationPerRender = true;
        r.step = typeof(VillagerActing).GetMethod("Step", Any);
        foreach (var t in r.body.GetComponentsInChildren<Transform>(true)) r.bones[t.name] = t;
        r.bones.TryGetValue("spine", out r.spine);

        r.lightGo = new GameObject("VillagerCarryShot_Light");
        var l = r.lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        r.lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        r.camGo = new GameObject("VillagerCarryShot_Cam");
        r.cam = r.camGo.AddComponent<Camera>();
        r.cam.orthographic = true;
        r.cam.clearFlags = CameraClearFlags.SolidColor;
        r.cam.backgroundColor = new Color(0.78f, 0.80f, 0.84f);
        r.cam.nearClipPlane = 0.05f;
        r.cam.farClipPlane = 20f;
        r.rt = new RenderTexture(cell, cell, 24);
        r.tex = new Texture2D(cell, cell, TextureFormat.RGB24, false);
        r.cam.targetTexture = r.rt;
        r.cam.aspect = 1f;
        r.cam.orthographicSize = 1.0f;
        return r;
    }

    /// The Carry clip's arms and head in the socket frame (body metres:
    /// x right, y up the spine, z forward), every 6th frame of a cycle.
    public static string Measure()
    {
        if (Application.isPlaying) return "stop play mode first";
        var sb = new StringBuilder();
        Rig r = null;
        try
        {
            r = Make(64);
            r.acting.Set(VillagerActing.Mode.Carry, Res.Timber, 2);
            for (int i = 0; i < 20; i++) r.Tick(Dt);
            var st = r.anim.GetCurrentAnimatorStateInfo(0);
            sb.AppendLine($"state len {st.length:F2}s carry={st.IsName("Carry")} bodyScale {r.body.transform.lossyScale.y:F2}");
            string[] names = { "hand.L", "hand.R", "forearm.L", "forearm.R", "upper_arm.L", "upper_arm.R", "head", "spine", "pelvis" };
            for (int f = 0; f < 36; f += 6)
            {
                Vector3 sock = r.spine.TransformPoint(VillagerActing.CarrySocketLocal);
                Quaternion inv = Quaternion.Inverse(r.spine.rotation);
                sb.Append($"f{f}: sock(body) {Fmt(r.body.transform.InverseTransformPoint(sock))} sockUp {Fmt(r.body.transform.InverseTransformDirection(r.spine.up))} sockFwd {Fmt(r.body.transform.InverseTransformDirection(r.spine.forward))}\n  ");
                foreach (var n in names)
                    if (r.bones.TryGetValue(n, out var b))
                        sb.Append($"{n} {Fmt(inv * (b.position - sock))} ");
                sb.AppendLine();
                for (int k = 0; k < 6; k++) r.Tick(Dt);
            }
            // The figure: feet to the top of the head, shoulder width.
            var bounds = new Bounds(r.body.transform.position, Vector3.zero);
            foreach (var smr in r.body.GetComponentsInChildren<SkinnedMeshRenderer>()) bounds.Encapsulate(smr.bounds);
            sb.AppendLine($"skinned bounds (body) min {Fmt(bounds.min - Origin)} max {Fmt(bounds.max - Origin)}");
            if (r.bones.TryGetValue("head", out var head))
                sb.AppendLine($"head bone (body) {Fmt(r.body.transform.InverseTransformPoint(head.position))}");
        }
        catch (System.Exception e) { sb.AppendLine("FAILED: " + e); }
        finally { r?.Dispose(); }
        return sb.ToString();
    }

    /// One portrait sheet per group of loads: rows = loads, columns = the
    /// clip at three frames from the front three-quarter, a side view, and
    /// a game-camera view from above.
    public static string Run(string outDir, string only = null)
    {
        if (Application.isPlaying) return "stop play mode first";
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        var loads = new List<(string res, int n)>
        {
            (Res.Timber, 2), (Res.Boards, 4), (Res.FineBoards, 4), (Res.Stone, 3), (Res.Ore, 3), (Res.Brick, 4),
            (Res.Iron, 3), (Res.Hide, 3), (Res.Tools, 2), (Res.Spear, 2), (Res.Bow, 2), (Res.Arrows, 12),
            (Res.Potato, 8), (Res.Carrot, 8), (Res.Apple, 5), (Res.Fish, 8), (Res.Meat, 3), (Res.Wheat, 8),
            (Res.Onion, 8), (Res.Spice, 8), (Res.Flour, 4), (Res.Food, 8), (Res.Game, 1),
            (Res.BakedPotato, 8), (Res.VegStew, 3), (Res.Bread, 6), (Res.GrilledFish, 4), (Res.HuntersStew, 8),
        };
        if (!string.IsNullOrEmpty(only))
            loads = loads.FindAll(l => only.Contains(l.res));
        const int Cell = 360, Cols = 3, RowsPerSheet = 6;
        Rig r = null;
        try
        {
            r = Make(Cell);
            int sheet = 0;
            for (int start = 0; start < loads.Count; start += RowsPerSheet, sheet++)
            {
                int rows = Mathf.Min(RowsPerSheet, loads.Count - start);
                var page = new Texture2D(Cols * Cell, rows * Cell, TextureFormat.RGB24, false);
                for (int i = 0; i < rows; i++)
                {
                    var (res, n) = loads[start + i];
                    r.acting.Set(VillagerActing.Mode.None);
                    for (int k = 0; k < 4; k++) r.Tick(Dt);
                    r.acting.Set(VillagerActing.Mode.Carry, res, n);
                    for (int k = 0; k < 12; k++) r.Tick(Dt);
                    int row = rows - 1 - i;     // Texture2D rows run bottom-up
                    Vector3 look = Origin + new Vector3(0f, 1.05f, 0.25f);
                    // 1: front three-quarter (his right), frame A.
                    Shoot(r, look + new Vector3(2.6f, 0.9f, 3.2f), look, page, 0, row, Cell, 1.0f);
                    for (int k = 0; k < 9; k++) r.Tick(Dt);
                    // 2: side view, frame B (feet passing).
                    Shoot(r, look + new Vector3(-4f, 0.25f, 0f), look, page, 1, row, Cell, 1.0f);
                    for (int k = 0; k < 9; k++) r.Tick(Dt);
                    // 3: the game camera (50 degrees down, from behind-left), frame C.
                    Shoot(r, look + new Vector3(-1.6f, 3.6f, -2.6f), look, page, 2, row, Cell, 1.0f);
                    sb.AppendLine($"{res} x{n}: {Describe(r.body.transform)}");
                }
                page.Apply();
                string file = Path.Combine(outDir, $"carry-{sheet + 1}.jpg");
                File.WriteAllBytes(file, page.EncodeToJPG(85));
                Object.DestroyImmediate(page);
                sb.AppendLine("wrote " + file);
            }
        }
        catch (System.Exception e) { sb.AppendLine("FAILED: " + e); }
        finally { r?.Dispose(); }
        return sb.ToString();
    }

    /// The eat interaction, in sequence: the reach, the dish in hand, the
    /// eat loop (four moments), the dish gone. Two views a moment.
    public static string Eat(string outDir, string dish = "VegStew")
    {
        if (Application.isPlaying) return "stop play mode first";
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        const int Cell = 420;
        Rig r = null;
        try
        {
            r = Make(Cell);
            r.lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f);   // light his front
            for (int k = 0; k < 10; k++) r.Tick(Dt);
            var moments = new List<(VillagerActing.Mode mode, float at, string label)>
            {
                (VillagerActing.Mode.Reach, 0.15f, "reach 0.15s"),
                (VillagerActing.Mode.Reach, 0.32f, "reach 0.32s"),
                (VillagerActing.Mode.Reach, 0.55f, "reach 0.55s"),
                (VillagerActing.Mode.Eat, 0.3f, "eat 0.3s"),
                (VillagerActing.Mode.Eat, 0.9f, "eat 0.9s"),
                (VillagerActing.Mode.Eat, 1.5f, "eat 1.5s"),
                (VillagerActing.Mode.Eat, 2.6f, "eat 2.6s"),
                (VillagerActing.Mode.None, 0.4f, "done"),
            };
            var page = new Texture2D(2 * Cell, moments.Count * Cell, TextureFormat.RGB24, false);
            var mClock = typeof(VillagerActing).GetField("modeClock", Any);
            VillagerActing.Mode was = VillagerActing.Mode.None;
            float t = 0f;
            for (int i = 0; i < moments.Count; i++)
            {
                var (mode, at, label) = moments[i];
                if (mode != was) { r.acting.Set(mode, dish, 1); was = mode; t = 0f; }
                r.acting.Commanded(0f);
                while (t < at - 1e-4f) { r.acting.Commanded(0f); if (r.anim != null) r.anim.Update(Dt); r.step.Invoke(r.acting, new object[] { Dt }); t += Dt; }
                int row = moments.Count - 1 - i;
                Vector3 look = Origin + new Vector3(0f, 1.05f, 0.2f);
                Shoot(r, look + new Vector3(-1.2f, 0.5f, 3.4f), look, page, 0, row, Cell, 0.62f);
                Shoot(r, look + new Vector3(-3.6f, 0.3f, 0.9f), look, page, 1, row, Cell, 0.62f);
                sb.AppendLine($"{label}: {Describe(r.body.transform)}");
            }
            page.Apply();
            string file = Path.Combine(outDir, "eat-sequence.jpg");
            File.WriteAllBytes(file, page.EncodeToJPG(85));
            Object.DestroyImmediate(page);
            sb.AppendLine("wrote " + file);
        }
        catch (System.Exception e) { sb.AppendLine("FAILED: " + e); }
        finally { r?.Dispose(); }
        return sb.ToString();
    }

    /// The load's bounds against the body (body metres).
    static string Describe(Transform body)
    {
        var b = new Bounds();
        bool any = false;
        foreach (Transform c in body)
        {
            if (!c.name.StartsWith("Carry_") && !c.name.StartsWith("Dish_") || !c.gameObject.activeInHierarchy) continue;
            foreach (var mr in c.GetComponentsInChildren<MeshRenderer>())
            {
                if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds);
            }
        }
        if (!any) return "no load";
        int count = 0;
        foreach (Transform c in body)
            if ((c.name.StartsWith("Carry_") || c.name.StartsWith("Dish_")) && c.gameObject.activeInHierarchy)
                count += c.GetComponentsInChildren<MeshRenderer>().Length;
        return $"{count} renderers, load min {Fmt(body.InverseTransformPoint(b.min))} max {Fmt(body.InverseTransformPoint(b.max))}";
    }

    static string Fmt(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

    static void Shoot(Rig r, Vector3 from, Vector3 at, Texture2D page, int col, int row, int cell, float size)
    {
        r.cam.orthographicSize = size;
        r.cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
        var prev = RenderTexture.active;
        try
        {
            r.cam.Render();
            RenderTexture.active = r.rt;
            r.tex.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
            r.tex.Apply();
            page.SetPixels(col * cell, row * cell, cell, cell, r.tex.GetPixels());
        }
        finally { RenderTexture.active = prev; }
    }
}
