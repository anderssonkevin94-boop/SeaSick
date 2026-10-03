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
/// **Jogging (2026-10-03, Kevin: "make sure that the runners are a lot
/// faster than the normal villager and that they always use their
/// wheelbarrow").** Every pushed row now runs at `VillagerGaits.Barrow`
/// (2.04 m/s; the road row x1.3) on the `Run` legs with the arms held on
/// the grips (`VillagerActing.ApplyBarrowGrip`), stepped in the game's
/// order (Animator, acting, barrow). A pushed row also FAILS when the legs
/// are not on `Run` or either fist is more than `GripGapMax` off the grip on
/// its side (2026-10-03: paired by side -- the rig's hand.L is his right).
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
    /// Most either fist may be off the grip on its side while pushing, metres.
    const float GripGapMax = 0.06f;

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
            const int Cell = 400, Cols = 4;
            rt = new RenderTexture(Cell, Cell, 24);
            var tex = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
            made.Add(tex);
            cam.targetTexture = rt;
            cam.aspect = 1f;

            // grade = rise per metre along his walk (+Z), cross = rise per metre to his right (+X).
            var states = new List<(string name, string res, int n, float speed, float grade, float cross)>
            {
                ("jog empty", null, 0, VillagerGaits.Barrow, 0f, 0f),
                ("jog logs x6", Res.Timber, 6, VillagerGaits.Barrow, 0f, 0f),
                ("jog planks x10", Res.Boards, 10, VillagerGaits.Barrow, 0f, 0f),
                ("jog bricks x24", Res.Brick, 24, VillagerGaits.Barrow, 0f, 0f),
                ("jog stone x12", Res.Stone, 12, VillagerGaits.Barrow, 0f, 0f),
                ("jog potatoes x8", Res.Potato, 8, VillagerGaits.Barrow, 0f, 0f),
                // The Storehouse's bigger barrows (2026-10-03): +1 and +2
                // armfuls (`Res.BarrowArmful(r, n)`), drawn whole, flat jog.
                ("jog logs x8 (+1)", Res.Timber, Res.BarrowArmful(Res.Timber, 1), VillagerGaits.Barrow, 0f, 0f),
                ("jog logs x10 (+2)", Res.Timber, Res.BarrowArmful(Res.Timber, 2), VillagerGaits.Barrow, 0f, 0f),
                ("jog stone x11 (+1)", Res.Stone, Res.BarrowArmful(Res.Stone, 1), VillagerGaits.Barrow, 0f, 0f),
                ("jog stone x14 (+2)", Res.Stone, Res.BarrowArmful(Res.Stone, 2), VillagerGaits.Barrow, 0f, 0f),
                ("jog planks x14 (+1)", Res.Boards, Res.BarrowArmful(Res.Boards, 1), VillagerGaits.Barrow, 0f, 0f),
                ("jog planks x18 (+2)", Res.Boards, Res.BarrowArmful(Res.Boards, 2), VillagerGaits.Barrow, 0f, 0f),
                ("jog potatoes x20 (+1)", Res.Potato, Res.BarrowArmful(Res.Potato, 1), VillagerGaits.Barrow, 0f, 0f),
                ("jog potatoes x24 (+2)", Res.Potato, Res.BarrowArmful(Res.Potato, 2), VillagerGaits.Barrow, 0f, 0f),
                ("jog logs x10 on a road, jog x1.2", Res.Timber, 10, VillagerGaits.BarrowAt(1.2f) * 1.3f, 0f, 0f),
                ("jog logs on a road x1.3", Res.Timber, 6, VillagerGaits.Barrow * 1.3f, 0f, 0f),
                ("parked logs x6", Res.Timber, 6, 0f, 0f, 0f),
                ("parked stone x5", Res.Stone, 5, 0f, 0f, 0f),
                ("jog logs uphill 15deg", Res.Timber, 6, VillagerGaits.Barrow, 0.268f, 0f),
                ("jog logs downhill 15deg", Res.Timber, 6, VillagerGaits.Barrow, -0.268f, 0f),
                ("jog stone across 10deg", Res.Stone, 12, VillagerGaits.Barrow, 0.05f, 0.176f),
                ("parked logs uphill 12deg + across 8deg", Res.Timber, 6, 0f, 0.213f, 0.14f),
                ("parked stone downhill 12deg - across 8deg", Res.Stone, 5, 0f, -0.213f, -0.14f),
            };
            bool fail = false;
            var verts = new Dictionary<Mesh, Vector3[]>();
            var page = new Texture2D(Cols * Cell, states.Count * Cell, TextureFormat.RGB24, false);
            made.Add(page);
            for (int s = 0; s < states.Count; s++)
            {
                var (name, res, n, speed, grade, cross) = states[s];
                if (res == null) acting.Set(VillagerActing.Mode.None);
                else acting.Set(VillagerActing.Mode.Carry, res, n);
                // The ground: a plane through his feet at the row's start
                // (flat rows: the old level quad), as the barrow sees it.
                Vector3 o0 = body.transform.position;
                float G(float x, float z) => Origin.y + grade * (z - o0.z) + cross * (x - o0.x);
                barrow.GroundOverride = G;
                body.transform.position = new Vector3(o0.x, G(o0.x, o0.z), o0.z);
                // Walk (or stand) long enough for the tilt, the clip and the
                // speed filter to settle; the barrow's ground contact is
                // checked on every frame of the second half (the walk cycle).
                var low = new Low();
                for (int k = 0; k < 30; k++)
                {
                    var bp = body.transform.position + body.transform.forward * speed * Dt;
                    bp.y = G(bp.x, bp.z);
                    body.transform.position = bp;
                    // The game's order: the walker commands, the Animator
                    // writes the run, the acting layer lays the grip arms on
                    // it, then the barrow follows his fists (DefaultExecutionOrder 100).
                    acting.Commanded(speed);
                    if (anim != null) anim.Update(Dt);
                    step.Invoke(acting, new object[] { Dt });
                    barrow.Step(Dt, true);
                    if (k >= 15) Contact(body.transform, G, verts, low);
                }
                Vector3 o = body.transform.position;
                var nrm = new Vector3(-cross, 1f, -grade).normalized;
                ground.transform.SetPositionAndRotation(new Vector3(o.x, G(o.x, o.z), o.z),
                    Quaternion.FromToRotation(Vector3.up, nrm) * Quaternion.Euler(90f, 0f, 0f));
                int row = states.Count - 1 - s;
                Vector3 look = o + new Vector3(0f, 0.6f, 0.75f);
                Shoot(cam, rt, tex, look + new Vector3(4f, 0.2f, 0f), look, page, 0, row, Cell, 1.05f);
                Shoot(cam, rt, tex, look + new Vector3(2.4f, 1.4f, 3.0f), look, page, 1, row, Cell, 1.05f);
                Shoot(cam, rt, tex, look + new Vector3(-1.6f, 3.6f, -2.6f), look, page, 2, row, Cell, 1.05f);
                // Close-up of the tray, high front three-quarter: clipping shows here.
                Vector3 tray = o + body.transform.rotation * new Vector3(0f, 0.55f, 0.95f);
                Shoot(cam, rt, tex, tray + new Vector3(1.6f, 2.2f, 1.9f), tray, page, 3, row, Cell, 0.5f);

                var bt = body.transform;
                string grips = "no grips";
                float gripGap = float.MaxValue;
                if (barrow.Grips(out var gl, out var gr))
                {
                    Vector3 fl = bones.TryGetValue("hand.L", out var hl) ? hl.TransformPoint(VillagerActing.ToolGripLocal) : Vector3.zero;
                    Vector3 fr = bones.TryGetValue("hand.R", out var hr) ? hr.TransformPoint(VillagerActing.ToolGripLocal) : Vector3.zero;
                    // Each fist against the grip on ITS side of him (the rig's
                    // hand.L is his right hand): the worse of the two.
                    float Side(Vector3 w) => Vector3.Dot(w - bt.position, bt.right);
                    Vector3 gLeftSide = Side(gl) < Side(gr) ? gl : gr, gRightSide = Side(gl) < Side(gr) ? gr : gl;
                    Vector3 fLeftSide = Side(fl) < Side(fr) ? fl : fr, fRightSide = Side(fl) < Side(fr) ? fr : fl;
                    gripGap = Mathf.Max(Vector3.Distance(gLeftSide, fLeftSide), Vector3.Distance(gRightSide, fRightSide));
                    grips = $"gripL {F(bt.InverseTransformPoint(gl))} gripR {F(bt.InverseTransformPoint(gr))} | fistL {F(bt.InverseTransformPoint(fl))} fistR {F(bt.InverseTransformPoint(fr))} gap {gripGap:0.000}";
                }
                bool running = anim != null && anim.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Run");
                float feet = float.MaxValue;
                foreach (var fb in new[] { "foot.L", "foot.R", "toe.L", "toe.R" })
                    if (bones.TryGetValue(fb, out var f)) feet = Mathf.Min(feet, f.position.y - G(f.position.x, f.position.z));
                // Kevin's rule, no clipping ever: the wheel touches (never under),
                // nothing of the barrow or its load is under the ground, and
                // parked it stands on its legs.
                bool parked = speed <= 0f;
                var why = new List<string>();
                if (!barrow.Showing) why.Add("not showing");
                if (low.wheelMin < -0.005f || low.wheelMax > 0.01f) why.Add($"wheel {low.wheelMin:0.000}..{low.wheelMax:0.000} not in [-0.005, 0.010]");
                if (low.all < -0.005f) why.Add($"barrow/load {low.all:0.000} under the ground");
                if (parked && (low.legs < -0.005f || low.legs > 0.01f)) why.Add($"parked legs {low.legs:0.000} not on the ground");
                if (!parked && low.legs < 0.01f) why.Add($"pushed legs {low.legs:0.000} drag");
                if (!parked && !running) why.Add("legs not on Run");
                if (!parked && gripGap > GripGapMax) why.Add($"fists {gripGap:0.000} m off the grips (max {GripGapMax})");
                if (why.Count > 0) fail = true;
                sb.Append($"tray fit {TrayFit(bt)} | ");
                sb.AppendLine($"{(why.Count == 0 ? "ok" : "FAIL (" + string.Join("; ", why) + ")")} {name}: showing={barrow.Showing} acting={acting.Current} run={running} pushGait={acting.PushGait} clipArms={acting.BarrowArms} {grips} wheelLow {low.wheelMin:0.000}..{low.wheelMax:0.000} legsLow {low.legs:0.000} barrowLow {low.all:0.000} footBone {feet:0.00}");
            }
            barrow.GroundOverride = null;
            sb.Insert(0, fail ? "BARROW CONTACT FAIL\n" : "BARROW CONTACT PASS (wheel on the ground every frame, nothing under it, legs down when parked)\n");
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

    /// **The load against wheelbarrow A's real inside** (`art-staging/wheelbarrow-v2/
    /// layout_fit.py` planes): every load renderer's mesh-bounds corners in the
    /// tray's Load frame. pen > 0 = through a wall / rail / the floor; gap = the
    /// closest any corner comes to them (want >= 0.01).
    static string TrayFit(Transform body)
    {
        Transform load = null;
        foreach (var t in body.GetComponentsInChildren<Transform>(true))
            if (t.name == "Load" && t.parent != null && t.parent.name == "Tilt") { load = t; break; }
        if (load == null) return "no load";
        float worst = -9f; int items = 0;
        foreach (var mf in load.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            items++;
            var m = load.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var mb = mf.sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                float y = Mathf.Min(Mathf.Max(p.y, 0f), 0.2819f), tt = y / 0.27f;
                float xm = 0.27f + 0.015f * tt, z0 = -0.295f, z1 = 0.295f + 0.25f * tt - 0.002f;
                if (y >= 0.219f) xm -= 0.008f;
                if (y >= 0.225f) { z0 += 0.008f; z1 -= 0.008f; }
                if (p.y >= 0.282f) z1 = 0.295f;   // above the rim: stay over the floor
                worst = Mathf.Max(worst, Mathf.Max(Mathf.Max(Mathf.Abs(p.x) - xm, z0 - p.z), Mathf.Max(p.z - z1, -p.y)));
            }
        }
        return items == 0 ? "empty" : $"{items} items pen {worst:0.000} gap {-worst:0.000}";
    }

    /// Lowest true vertex (mesh data, edit mode reads non-readable meshes)
    /// over the ground under it, over the frames sampled: the wheel, the
    /// barrow body (its legs are its lowest part), and everything incl. the load.
    sealed class Low { public float wheelMin = float.MaxValue, wheelMax = float.MinValue, legs = float.MaxValue, all = float.MaxValue; }

    static void Contact(Transform body, System.Func<float, float, float> g, Dictionary<Mesh, Vector3[]> verts, Low low)
    {
        var br = body.Find("RunnerBarrow");
        if (br == null || !br.gameObject.activeInHierarchy) return;
        float wheel = float.MaxValue;
        foreach (var mf in br.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            if (!verts.TryGetValue(mesh, out var vs)) verts[mesh] = vs = mesh.vertices;
            var m = mf.transform.localToWorldMatrix;
            float lo = float.MaxValue;
            foreach (var v in vs)
            {
                var w = m.MultiplyPoint3x4(v);
                lo = Mathf.Min(lo, w.y - g(w.x, w.z));
            }
            low.all = Mathf.Min(low.all, lo);
            bool isWheel = mf.name == "Wheel" || (mf.transform.parent != null && mf.transform.parent.name == "Wheel");
            if (isWheel) wheel = Mathf.Min(wheel, lo);
            else if (mf.transform.parent != null && mf.transform.parent.name == "Tilt") low.legs = Mathf.Min(low.legs, lo);
        }
        if (wheel < float.MaxValue)
        {
            low.wheelMin = Mathf.Min(low.wheelMin, wheel);
            low.wheelMax = Mathf.Max(low.wheelMax, wheel);
        }
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
