using System.Collections.Generic;
using System.Reflection;
using SeaSick.World;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **What the palisade costs the frame (2026-09-30).** Launchers for
    /// `unity cmd eval`, used to measure the wall-batching change: frame the
    /// camp, read `UnityStats`, hide every wall renderer (segments, gates and
    /// the chain's posts), read again. Play mode only; nothing in the game
    /// calls it.
    ///
    /// Recipe: `RunProbe.SetSaveDirOverride(copy)` + `RunProbe.ViewPhone()`,
    /// `editor_play`, `WallDrawCost.Continue()`, wait for the load,
    /// `WallDrawCost.Frame(45)`, a few frames, `WallDrawCost.Stats()`,
    /// `WallDrawCost.Walls(false)`, a few frames, `Stats()` again. Clear the
    /// override after.
    public static class WallDrawCost
    {
        static readonly List<Renderer> hidden = new List<Renderer>();

        /// CONTINUE the save the override points at, as the Home menu does.
        public static string Continue()
        {
            var boot = Object.FindFirstObjectByType<Save.GameBoot>();
            if (boot == null) return "no GameBoot";
            var m = typeof(Save.GameBoot).GetMethod("Decide", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(boot, new object[] { Save.GameBoot.Choice.Continue, false });
            return "continued";
        }

        static Outpost WalledCamp()
        {
            foreach (var chain in Object.FindObjectsByType<WallChain>())
            {
                var camp = chain.GetComponent<Outpost>();
                if (camp != null) return camp;
            }
            return null;
        }

        /// Close any sheet and put the island camera over the walled camp,
        /// `ground` metres of ground up the frame, snapped (no ease).
        public static string Frame(float ground)
        {
            UI.Sheets.Sheets.Close();
            var camp = WalledCamp();
            var cam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            if (camp == null || cam == null) return "camp=" + (camp != null) + " cam=" + (cam != null);
            cam.LookAtGround(camp.CampCentre, ground);
            cam.SnapToTarget();
            return "framed " + camp.name + " at " + camp.CampCentre;
        }

        /// Show or hide every wall renderer: segments (both states' pieces
        /// and any batched mesh), gates, and the chain's posts.
        public static string Walls(bool show)
        {
            if (show)
            {
                foreach (var r in hidden) if (r != null) r.enabled = true;
                int n = hidden.Count;
                hidden.Clear();
                return "shown " + n;
            }
            hidden.Clear();
            var roots = new List<Transform>();
            foreach (var s in Object.FindObjectsByType<WallSegment>()) roots.Add(s.transform);
            foreach (var c in Object.FindObjectsByType<WallChain>())
            {
                var posts = c.transform.Find("WallPosts");
                if (posts != null) roots.Add(posts);
            }
            foreach (var t in roots)
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
            return "hid " + hidden.Count + " renderers under " + roots.Count + " roots";
        }

        /// The Game view's last frame, plus the wall's own census.
        public static string Stats()
        {
            int segs = 0, active = 0, subs = 0;
            foreach (var s in Object.FindObjectsByType<WallSegment>())
            {
                segs++;
                foreach (var r in s.GetComponentsInChildren<MeshRenderer>())
                    if (r.enabled) { active++; subs += r.sharedMaterials.Length; }
            }
            foreach (var c in Object.FindObjectsByType<WallChain>())
            {
                var posts = c.transform.Find("WallPosts");
                if (posts == null) continue;
                foreach (var r in posts.GetComponentsInChildren<MeshRenderer>())
                    if (r.enabled) { active++; subs += r.sharedMaterials.Length; }
            }
            return $"draws={UnityStats.drawCalls} setpass={UnityStats.setPassCalls} "
                + $"srpBatcherDraws={UnityStats.srpBatcherDrawCalls} shadowCasters={UnityStats.shadowCasters} "
                + $"tris={UnityStats.triangles} | wall segs={segs} activeRenderers={active} submeshes={subs}";
        }

        /// **The A/B**: `WallVisual.Merge` on or off, and every chain
        /// redrawn with it, so both drawings are measured in the same frame
        /// state (a Continue lands at a different hour each launch, and the
        /// night's torches alone move the count by ~100). Reflection:
        /// both are internal to the game assembly.
        public static string SetMerge(bool merge)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            typeof(WallVisual).GetField("Merge", flags).SetValue(null, merge);
            var redraw = typeof(WallChain).GetMethod("RedrawAll", BindingFlags.NonPublic | BindingFlags.Instance);
            int n = 0;
            foreach (var c in Object.FindObjectsByType<WallChain>()) { redraw.Invoke(c, null); n++; }
            return "merge=" + merge + " chains=" + n;
        }

        /// Frame the first breached segment close, `ground` metres up the
        /// frame, to see its broken pieces.
        public static string FrameBreach(float ground)
        {
            UI.Sheets.Sheets.Close();
            var cam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            foreach (var s in Object.FindObjectsByType<WallSegment>())
                if (s.Breached && cam != null)
                {
                    cam.LookAtGround(s.Midpoint, ground);
                    cam.SnapToTarget();
                    return "breach " + (s.IsGate ? "gate " : "run ") + s.Length.ToString("0.0") + " m at " + s.Midpoint;
                }
            return "no breach";
        }

        public static string Shot(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
    }
}
