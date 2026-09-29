using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

/// **Edit-mode picture of every villager tool pose**, no play mode needed.
///
/// Instantiates `Prefabs/CrewMember.prefab` high above the world, adds
/// `VillagerActing`, and for Hammer / Chop / Saw / Hoe / Stir drives the
/// Animator and the component's `Step(dt)` (via reflection) through a
/// fade-in and then eight phases of one stroke, rendering a side view (from
/// the tool hand's side) and a front three-quarter view of each to PNGs. A
/// red cube marks the nominal work spot (`VillagerActing.NominalWork`); at
/// phase 0 the hammer face / axe edge / saw teeth / hoe blade should sit on
/// it, and the returned text says by how many centimetres it misses.
///
/// Chop and Hoe get a wider, higher frame: their lift carries the head well
/// above the villager's own, and the tight chest frame cropped it out at the
/// top of the swing -- which read as a tool flying off the hand.
///
/// Run (editor idle, not in play mode):
/// `unity cmd eval --json --code 'return VillagerToolShot.Run("/tmp/villager-tools");'`
/// Everything it creates is destroyed before it returns.
public static class VillagerToolShot
{
    const string Prefab = "Assets/_Project/Prefabs/CrewMember.prefab";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic;

    public static string Run(string outDir)
    {
        var sb = new StringBuilder();
        if (Application.isPlaying) return "stop play mode first";
        Directory.CreateDirectory(outDir);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (prefab == null) return "missing " + Prefab;

        var t = typeof(VillagerActing);
        MethodInfo step = t.GetMethod("Step", Any);
        FieldInfo clock = t.GetField("clock", Any);
        MethodInfo nominal = t.GetMethod("NominalWork", Any);
        if (step == null || clock == null || nominal == null)
            return "VillagerActing lost Step/clock/NominalWork -- update this shot";

        Vector3 origin = new Vector3(0f, 1500f, 0f);
        GameObject body = null, marker = null, lightGo = null, camGo = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        try
        {
            body = (GameObject)Object.Instantiate(prefab);
            body.name = "VillagerToolShot_Body";
            body.transform.SetPositionAndRotation(origin, Quaternion.identity);
            var anim = body.GetComponentInChildren<Animator>();
            var acting = body.AddComponent<VillagerActing>();
            if (anim != null) { anim.Rebind(); anim.Update(0f); }
            // Every shot is a manual cam.Render() inside one editor frame;
            // without this the skin keeps the frame's first skinning and the
            // arms never follow the pose (only the tool, a plain mesh, moves).
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;

            marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "VillagerToolShot_Work";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.localScale = Vector3.one * 0.05f;
            var red = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            red.SetColor("_BaseColor", Color.red);
            marker.GetComponent<MeshRenderer>().sharedMaterial = red;

            lightGo = new GameObject("VillagerToolShot_Light");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            camGo = new GameObject("VillagerToolShot_Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.80f, 0.84f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            const int W = 768, H = 768;
            rt = new RenderTexture(W, H, 24);
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.aspect = 1f;

            var modes = new[]
            {
                // Last two: ortho half-height and look height (m above the feet).
                (VillagerActing.Mode.Hammer, "Hammer_Period", "Hammer_Reach", "Hammer_Face", 1.05f, 0.95f),
                (VillagerActing.Mode.Chop, "Chop_Period", "Axe_Reach", "Axe_Face", 1.75f, 1.30f),
                (VillagerActing.Mode.Saw, "Saw_Period", "Saw_Reach", "Saw_Face", 1.05f, 0.95f),
                (VillagerActing.Mode.Hoe, "Hoe_Period", "Hoe_Reach", "Hoe_Face", 1.75f, 1.30f),
                (VillagerActing.Mode.Stir, "Stir_Period", null, null, 1.05f, 0.95f),
            };
            const int Phases = 8;
            const float Dt = 1f / 30f;

            foreach (var (mode, periodName, reachName, faceName, frame, lookY) in modes)
            {
                cam.orthographicSize = frame;
                float period = Const(t, periodName, 1f);
                acting.Set(mode);
                for (int i = 0; i < 30; i++)           // fade the old pose out and this one in
                {
                    if (anim != null) anim.Update(Dt);
                    step.Invoke(acting, new object[] { Dt });
                }

                Vector3 work = body.transform.TransformPoint(
                    (Vector3)nominal.Invoke(null, new object[] { mode }));
                marker.transform.position = work;
                Transform tool = body.transform.Find("Tool_" + mode);

                for (int ph = 0; ph < Phases; ph++)
                {
                    clock.SetValue(acting, period * ph / Phases);
                    if (anim != null) anim.Update(Dt);
                    step.Invoke(acting, new object[] { 0f });

                    if (ph == 0 && tool != null && reachName != null)
                    {
                        // The saw's "stroke" is a sine, 0 at phase 0: the
                        // blade sits centred on the spot, as the swings do.
                        Vector3 strike = tool.position
                            + tool.up * Const(t, reachName, 0f)
                            + tool.forward * Const(t, faceName, 0f);
                        sb.AppendLine($"{mode}: strike point misses the work by "
                            + $"{(strike - work).magnitude * 100f:0.0} cm "
                            + $"(tool up {Fmt(body.transform.InverseTransformDirection(tool.up))}, "
                            + $"strike side {Fmt(body.transform.InverseTransformDirection(tool.forward))})");
                    }
                    else if (ph == 0)
                        sb.AppendLine($"{mode}: tool {(tool != null ? "present" : "MISSING")}");

                    Vector3 look = origin + new Vector3(0f, lookY, 0.30f);
                    Shoot(cam, rt, tex, look + Vector3.right * 4f, look,
                        Path.Combine(outDir, $"{mode}_{ph}_side.png"));
                    Shoot(cam, rt, tex, look + new Vector3(2.4f, 1.2f, 3.2f), look,
                        Path.Combine(outDir, $"{mode}_{ph}_front.png"));
                }
            }
            sb.AppendLine($"wrote {modes.Length * Phases * 2} PNGs to {outDir}");
        }
        catch (System.Exception e)
        {
            sb.AppendLine("FAILED: " + e);
        }
        finally
        {
            if (camGo != null) { camGo.GetComponent<Camera>().targetTexture = null; Object.DestroyImmediate(camGo); }
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (tex != null) Object.DestroyImmediate(tex);
            if (lightGo != null) Object.DestroyImmediate(lightGo);
            if (marker != null)
            {
                var m = marker.GetComponent<MeshRenderer>().sharedMaterial;
                Object.DestroyImmediate(marker);
                if (m != null) Object.DestroyImmediate(m);
            }
            if (body != null) Object.DestroyImmediate(body);
        }
        return sb.ToString();
    }

    static float Const(System.Type t, string name, float fallback)
    {
        var f = name != null ? t.GetField(name, Any) : null;
        return f != null ? System.Convert.ToSingle(f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null)) : fallback;
    }

    static string Fmt(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

    static void Shoot(Camera cam, RenderTexture rt, Texture2D tex, Vector3 from, Vector3 at, string file)
    {
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
        var prev = RenderTexture.active;
        try
        {
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
        }
        finally { RenderTexture.active = prev; }
    }
}
