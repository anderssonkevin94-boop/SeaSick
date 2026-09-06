using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// A proper look at the CANVAS, not at the ship.
///
/// Named `CanvasShot` and not `SailShot` because `Dev/Editor/SailShot.cs`
/// already has that class — and being a MonoBehaviour inside an `Editor`
/// folder it can never have run, since `AddComponent` returns null there.
/// Two same-named classes in the two assemblies both compile, and reflection
/// picks whichever it reaches first, so this one has its own name.
///
/// `LadderShot` stands off at 1.15 lengths and frames the whole hull, which is
/// the right shot for a silhouette and the wrong one for judging a sail — at
/// that distance a course is a hundred pixels tall and "the sails are fine"
/// is not something anyone can actually see. This frames the RIG: the box the
/// sails themselves occupy, filling the frame, with the measured cut of every
/// sail printed beside the picture so the eye and the number agree.
///
/// Two things it deliberately controls, both of which have lied before:
///
///  * **Ambient.** A bluish ambient turned the canvas dark slate and sent a
///    previous session hunting a UV bug that did not exist. Flat neutral here,
///    saved and restored.
///  * **Where the sun is.** Canvas lit from behind reads grey whatever colour
///    it is, so the sun is put over the camera's shoulder for the shot.
///
/// Renders through its own camera to a RenderTexture, so no HUD, no IMGUI and
/// a portrait frame that matches the phone rather than the editor's Game view.
public class CanvasShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CanvasShot: not in play mode"); return; }
        var old = FindAnyObjectByType<CanvasShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("CanvasShot").AddComponent<CanvasShot>();
    }

    const int W = 1100, H = 1500;
    /// **A square sail's width runs ACROSS her beam, so a camera on the beam
    /// sees every sail edge-on.** The first sheet from this tool was shot from
    /// abeam, which is the natural place to photograph a hull, and it came back
    /// with the courses as narrow vertical strips — the exact complaint the
    /// re-cut was meant to answer, produced by the camera rather than by the
    /// cut. From ahead is where a square rig shows its face.
    ///   0 from ahead · 1 off the bow quarter · 2 abeam (profile: hoist, not cut)
    static readonly (int rung, int view)[] Shots =
    {
        (0, 0), (4, 0), (12, 0), (19, 0),
        (12, 1), (19, 1), (12, 2),
    };

    static string ViewName(int v) => v == 0 ? "from ahead"
                                   : v == 1 ? "off the bow quarter" : "abeam";

    StringBuilder sb;

    IEnumerator Start()
    {
        sb = new StringBuilder("=== CanvasShot ===\n");
        var yard = FindFirstObjectByType<Shipyard>();
        if (yard == null)
        {
            System.IO.File.WriteAllText("/tmp/seasick-sailshot.txt", "no Shipyard\n");
            Debug.LogError("CanvasShot: no Shipyard"); Destroy(gameObject); yield break;
        }
        int startedOn = yard.NodeIndex;

        // --- take the scene's lighting under control, and give it back ------
        var ambMode = RenderSettings.ambientMode;
        var ambLight = RenderSettings.ambientLight;
        var ambInt = RenderSettings.ambientIntensity;
        var fog = RenderSettings.fog;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.63f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = false;

        Light sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
        Quaternion sunWas = sun != null ? sun.transform.rotation : Quaternion.identity;

        // Our own camera, so nothing re-posts it behind our back the way
        // ChaseCamera does every LateUpdate.
        var camGo = new GameObject("SailShotCam");
        var cam = camGo.AddComponent<Camera>();
        var main = Camera.main;
        if (main != null)
        {
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.cullingMask = main.cullingMask;
            cam.farClipPlane = main.farClipPlane;
            cam.nearClipPlane = 0.05f;
        }
        cam.fieldOfView = 38f;
        cam.enabled = false;                    // we drive Render() by hand

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);

        foreach (var (rung, view) in Shots)
        {
            yard.Apply(rung);
            // Two frames: one for the swap, one for SailRig to reparent and
            // SetArea to scale. Shooting on the same frame as Apply catches
            // the rig mid-build.
            yield return null;
            yield return null;

            var n = yard.Node;
            var visual = FindVisual(yard.transform);
            var sails = new List<Transform>();
            if (visual != null)
                foreach (var t in visual.GetComponentsInChildren<Transform>())
                    if (t != visual && t.name.Contains("Sail")) sails.Add(t);

            if (sails.Count == 0)
            {
                sb.AppendLine($"rung {rung} {n.label}: NO SAILS FOUND");
                continue;
            }

            // The rig's box in WORLD space, from the sails only.
            Bounds rig = new Bounds(sails[0].position, Vector3.zero);
            foreach (var s in sails)
            {
                var mf = s.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { rig.Encapsulate(s.position); continue; }
                var b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                    rig.Encapsulate(s.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1,
                                    (c & 4) == 0 ? -1 : 1))));
            }

            // Measure every sail's CUT while we are here: width across her
            // beam, height, and the aspect that the re-cut was about.
            sb.AppendLine($"\nrung {rung,2}  {n.label}  ({n.length:F1} m, {n.beam:F1} m beam, "
                        + $"{n.masts} mast{(n.masts == 1 ? "" : "s")}, {sails.Count} sails)");
            foreach (var s in sails)
            {
                var mf = s.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // In the hull's own frame: x is across her, y up, z fore-aft.
                Vector3 lo = Vector3.one * float.MaxValue, hi = -lo;
                var b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var p = yard.transform.InverseTransformPoint(s.TransformPoint(
                        b.center + Vector3.Scale(b.extents, new Vector3(
                            (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1,
                            (c & 4) == 0 ? -1 : 1))));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
                float w = hi.x - lo.x, h = hi.y - lo.y;
                sb.AppendLine($"    {s.name,-22} {w,5:F2} m wide x {h,5:F2} m tall  "
                            + $"aspect {(h > 0.01f ? w / h : 0f),5:F2}  "
                            + $"foot {lo.y,5:F2} m  head {hi.y,5:F2} m");
            }

            // --- frame the rig, not the ship --------------------------------
            // BOTH extents, or the shot is useless. Solving for the rig's
            // HEIGHT alone put the camera 17.7 m off a brig whose rig is 10.5 m
            // deep: correct arithmetic, and the picture came back from between
            // her two masts with the island showing through the gap. A rig is
            // wider than it is tall now — that is the whole point of the re-cut
            // — so the fore-and-aft span is the binding constraint, not height.
            Vector3 dir =
                  view == 0 ? yard.transform.forward
                : view == 1 ? (yard.transform.forward * 0.78f
                             - yard.transform.right * 0.63f).normalized
                :             -yard.transform.right;
            Vector3 camRight = Vector3.Cross(Vector3.up, dir).normalized;

            float vfov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hfov = Mathf.Atan(Mathf.Tan(vfov) * W / (float)H);
            // Half-width of an axis-aligned box projected onto the camera's
            // right — the box does not have to be square to the view.
            float halfW = Mathf.Abs(rig.extents.x * camRight.x)
                        + Mathf.Abs(rig.extents.y * camRight.y)
                        + Mathf.Abs(rig.extents.z * camRight.z);
            float dist = Mathf.Max(rig.extents.y / Mathf.Tan(vfov),
                                   halfW / Mathf.Tan(hfov)) * 1.22f;
            Vector3 eye = rig.center + dir * dist + Vector3.up * (rig.size.y * 0.06f);
            camGo.transform.position = eye;
            camGo.transform.LookAt(rig.center);

            // Sun over the camera's shoulder: canvas lit, not silhouetted.
            if (sun != null)
                sun.transform.rotation = Quaternion.LookRotation(
                    Quaternion.AngleAxis(24f, Vector3.up) * (rig.center - eye).normalized
                    + Vector3.down * 0.55f);

            // NOT `WaitForEndOfFrame`: it never resumes when the editor's
            // Game view is not drawing, which is every headless run, and a
            // coroutine stuck there leaves no error to read. `cam.Render()`
            // is an explicit render into our own target and needs no such
            // handshake with the frame loop.
            yield return null;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string suffix = view == 0 ? "" : view == 1 ? "-quarter" : "-beam";
            string path = $"/tmp/seasick-sail-{rung:00}{suffix}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            sb.AppendLine($"    -> {path}  (rig box {rig.size.x:F1} x {rig.size.y:F1} x "
                        + $"{rig.size.z:F1} m, camera {dist:F1} m"
                        + $" {ViewName(view)})");
            // Written after every shot rather than at the end: a probe that
            // stalls half way should still say how far it got.
            System.IO.File.WriteAllText("/tmp/seasick-sailshot.txt", sb.ToString());
        }

        // --- put everything back --------------------------------------------
        if (sun != null) sun.transform.rotation = sunWas;
        RenderSettings.ambientMode = ambMode;
        RenderSettings.ambientLight = ambLight;
        RenderSettings.ambientIntensity = ambInt;
        RenderSettings.fog = fog;
        cam.targetTexture = null;
        Destroy(camGo);
        Destroy(rt); Destroy(tex);
        yard.Apply(startedOn);

        System.IO.File.WriteAllText("/tmp/seasick-sailshot.txt", sb.ToString());
        Debug.Log(sb.ToString());
        Destroy(gameObject);
    }

    /// The hull the yard last swapped in. Named by `Shipyard.SwapVisual`; take
    /// the first child that carries sails rather than assuming the name.
    static Transform FindVisual(Transform ship)
    {
        foreach (Transform c in ship)
        {
            if (!c.gameObject.activeInHierarchy) continue;
            foreach (var t in c.GetComponentsInChildren<Transform>())
                if (t.name.Contains("Sail")) return c;
        }
        return null;
    }
}
