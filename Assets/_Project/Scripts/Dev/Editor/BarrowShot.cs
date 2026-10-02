using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

/// **Edit-mode contact sheet of the runner's wheelbarrow** (2026-10-02,
/// `RunnerBarrow`), no play mode needed. Same rig as `VillagerCarryShot`:
/// `CrewMember.prefab` high above the world on a ground quad, `VillagerActing`
/// + `RunnerBarrow` driven by hand while the body really walks forward, so
/// the `Carry` clip, the tilt, the wheel spin and the tray load are exactly
/// the game's. Rows = states (pushing empty, logs, planks, bricks, stone,
/// crate of potatoes, parked with logs, parked empty); columns = his right
/// side, front three-quarter, the game camera (50 degrees down). Logs the
/// grips against his fists and the wheel / leg heights per row.
///
/// `unity cmd eval --json --code 'return BarrowShot.Run("<dir>");'`
/// Everything it creates is destroyed before it returns.
public static class BarrowShot
{
    const string Prefab = "Assets/_Project/Prefabs/CrewMember.prefab";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic;
    const float Dt = 1f / 30f;
    static Vector3 Origin => new Vector3(0f, 1500f, 0f);

    public static string Run(string outDir)
    {
        if (Application.isPlaying) return "stop play mode first";
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        var made = new List<Object>();
        RenderTexture rt = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (prefab == null) return "no " + Prefab;
            var body = (GameObject)Object.Instantiate(prefab);
            made.Add(body);
            body.name = "BarrowShot_Body";
            body.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            var anim = body.GetComponentInChildren<Animator>();
            var acting = body.AddComponent<VillagerActing>();
            var barrow = body.AddComponent<RunnerBarrow>();
            if (anim != null) { anim.Rebind(); anim.Update(0f); }
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;
            var step = typeof(VillagerActing).GetMethod("Step", Any);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            made.Add(ground);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.SetPositionAndRotation(Origin, Quaternion.Euler(90f, 0f, 0f));
            ground.transform.localScale = new Vector3(60f, 60f, 1f);
            var gmat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            made.Add(gmat);
            gmat.SetColor("_BaseColor", new Color(0.47f, 0.56f, 0.38f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

            var lightGo = new GameObject("BarrowShot_Light");
            made.Add(lightGo);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            var camGo = new GameObject("BarrowShot_Cam");
            made.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.80f, 0.84f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            const int Cell = 400, Cols = 3;
            rt = new RenderTexture(Cell, Cell, 24);
            var tex = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
            made.Add(tex);
            cam.targetTexture = rt;
            cam.aspect = 1f;

            var states = new List<(string name, string res, int n, float speed)>
            {
                ("push empty", null, 0, 1.29f),
                ("push logs x3", Res.Timber, 3, VillagerGaits.Carry),
                ("push planks x6", Res.Boards, 6, VillagerGaits.Carry),
                ("push bricks x8", Res.Brick, 8, VillagerGaits.Carry),
                ("push stone x5", Res.Stone, 5, VillagerGaits.Carry),
                ("push potatoes x8", Res.Potato, 8, VillagerGaits.Carry),
                ("parked logs x3", Res.Timber, 3, 0f),
                ("parked empty", null, 0, 0f),
            };
            var page = new Texture2D(Cols * Cell, states.Count * Cell, TextureFormat.RGB24, false);
            made.Add(page);
            for (int s = 0; s < states.Count; s++)
            {
                var (name, res, n, speed) = states[s];
                if (res == null) acting.Set(VillagerActing.Mode.None);
                else acting.Set(VillagerActing.Mode.Carry, res, n);
                // Walk (or stand) long enough for the tilt, the clip and the
                // speed filter to settle.
                for (int k = 0; k < 30; k++)
                {
                    body.transform.position += body.transform.forward * speed * Dt;
                    acting.Commanded(speed);
                    barrow.Step(Dt, true);
                    if (anim != null) anim.Update(Dt);
                    step.Invoke(acting, new object[] { Dt });
                }
                Vector3 o = body.transform.position;
                ground.transform.position = new Vector3(o.x, Origin.y, o.z);
                int row = states.Count - 1 - s;
                Vector3 look = o + new Vector3(0f, 0.6f, 0.75f);
                Shoot(cam, rt, tex, look + new Vector3(4f, 0.2f, 0f), look, page, 0, row, Cell, 1.05f);
                Shoot(cam, rt, tex, look + new Vector3(2.4f, 1.4f, 3.0f), look, page, 1, row, Cell, 1.05f);
                Shoot(cam, rt, tex, look + new Vector3(-1.6f, 3.6f, -2.6f), look, page, 2, row, Cell, 1.05f);

                var bt = body.transform;
                string grips = "no grips";
                if (barrow.Grips(out var gl, out var gr))
                {
                    Vector3 fl = bones.TryGetValue("hand.L", out var hl) ? hl.TransformPoint(VillagerActing.ToolGripLocal) : Vector3.zero;
                    Vector3 fr = bones.TryGetValue("hand.R", out var hr) ? hr.TransformPoint(VillagerActing.ToolGripLocal) : Vector3.zero;
                    grips = $"gripL {F(bt.InverseTransformPoint(gl))} gripR {F(bt.InverseTransformPoint(gr))} | fistL {F(bt.InverseTransformPoint(fl))} fistR {F(bt.InverseTransformPoint(fr))}";
                }
                float wheelLow = float.MaxValue, minY = float.MaxValue;
                var br = bt.Find("RunnerBarrow");
                if (br != null)
                    foreach (var mr in br.GetComponentsInChildren<MeshRenderer>())
                    {
                        float y = mr.bounds.min.y - o.y;
                        minY = Mathf.Min(minY, y);
                        if (mr.name == "Wheel" || mr.transform.parent.name == "Wheel") wheelLow = Mathf.Min(wheelLow, y);
                    }
                float feet = float.MaxValue;
                foreach (var fb in new[] { "foot.L", "foot.R", "toe.L", "toe.R" })
                    if (bones.TryGetValue(fb, out var f)) feet = Mathf.Min(feet, f.position.y - o.y);
                sb.AppendLine($"{name}: showing={barrow.Showing} acting={acting.Current} clipArms={acting.BarrowArms} {grips} wheelLow {wheelLow:0.000} barrowLow {minY:0.000} footBone {feet:0.00}");
            }
            page.Apply();
            string file = Path.Combine(outDir, "barrow-sheet.jpg");
            File.WriteAllBytes(file, page.EncodeToJPG(88));
            sb.AppendLine("wrote " + file);
            cam.targetTexture = null;
        }
        catch (System.Exception e) { sb.AppendLine("FAILED: " + e); }
        finally
        {
            for (int i = made.Count - 1; i >= 0; i--) if (made[i] != null) Object.DestroyImmediate(made[i]);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
        }
        return sb.ToString();
    }

    static string F(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

    static void Shoot(Camera cam, RenderTexture rt, Texture2D tex, Vector3 from, Vector3 at, Texture2D page, int col, int row, int cell, float size)
    {
        cam.orthographicSize = size;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
        var prev = RenderTexture.active;
        try
        {
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
            tex.Apply();
            page.SetPixels(col * cell, row * cell, cell, cell, tex.GetPixels());
        }
        finally { RenderTexture.active = prev; }
    }
}
