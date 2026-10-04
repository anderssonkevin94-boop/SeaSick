using SeaSick.CameraRig;
using SeaSick.UI;
using SeaSick.UI.Sheets;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// <summary>
    /// **The harpoon's world markers** (`docs/PLAN-harpoon.md` §2 step 1 and
    /// §4): one small ring with a hook in it over every harpoonable thing in
    /// the bow arc (`HarpoonGun.InArc`), at its `HookPoint`. The auto-picked
    /// `Target` -- the one the fixed harpoon button fires at, and whose name
    /// it shows -- is filled, larger, and wears a bright halo ring.
    ///
    /// **Indicators only (2026-10-04, after Kevin's phone):** a marker
    /// takes no tap and claims no `UIBlocker` rect. Kevin tapped the marker
    /// floating over a close target -- often right over the bow -- as if it
    /// were the fire button: it only re-picked the target, and the same tap
    /// went on through to the world and opened the Ship sheet. Firing is the
    /// fixed button's alone (`SeaHud`), the nearest target is picked for
    /// him, and the stick, the camera and a ship lock all see a thumb on a
    /// marker as a thumb on open sea.
    ///
    /// **Camera**: the chase camera's own `Camera` (`ChaseCamera` is the sea
    /// view), found once and kept; `Camera.main` is only the fallback, since
    /// docs/DEV-TOOLS.md warns it has been the minimap camera.
    ///
    /// Shown under the same gates as `SeaEdgeMarkers` (`SeaHud.HelmShowing`,
    /// not docked, no menu, shipyard, sheet or loading screen), while the gun
    /// can take a new target (ready or reloading), and never under another
    /// piece of the sea HUD. IMGUI, self-installing, no allocation per frame
    /// (the three textures are built once).
    /// </summary>
    public sealed class HarpoonMarkers : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<HarpoonMarkers>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("HarpoonMarkers");
            go.AddComponent<HarpoonMarkers>();
            DontDestroyOnLoad(go);
        }

        const int MaxMarkers = 8;
        /// Ring sizes in `HudLayout.Unit`s.
        const float RingUnits = 2.8f, TargetScale = 1.3f;
        /// The target's halo, as a multiple of its own ring.
        const float HaloScale = 1.45f;

        static readonly Color Pearl = new Color32(232, 242, 246, 255);
        static readonly Color Ice = new Color32(164, 210, 232, 255);
        static readonly Color Dark = new Color32(11, 23, 32, 255);
        static readonly Color Backdrop = new Color(0.04f, 0.07f, 0.10f, 0.45f);

        struct Slot
        {
            public Vector2 gui;       // GUI space (origin top-left), screen px
            public bool isTarget;
        }

        readonly Slot[] slots = new Slot[MaxMarkers];
        int count;

        ChaseCamera chase;
        Camera seaCamera;
        float nextCameraSearch;

        Texture2D ringTex, discTex, haloTex;

        Camera SeaCamera()
        {
            if (seaCamera != null) return seaCamera;
            if (chase == null && Time.unscaledTime >= nextCameraSearch)
            {
                nextCameraSearch = Time.unscaledTime + 1f;
                chase = FindFirstObjectByType<ChaseCamera>();
            }
            if (chase != null) seaCamera = chase.GetComponent<Camera>();
            return seaCamera != null ? seaCamera : Camera.main;
        }

        /// The gun, when markers may show: it can take a new target and the
        /// sea HUD is up (the edge markers' gates).
        static bool Gate(out HarpoonGun gun)
        {
            gun = HarpoonGun.Player;
            if (gun == null || !gun.Available) return false;
            if (gun.State != HarpoonState.Ready && gun.State != HarpoonState.Reloading) return false;
            if (!SeaHud.HelmShowing || MidnightLandHud.Active || !ChartData.UnderWay) return false;
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return false;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen) return false;
            if (SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return false;
            var loading = SeaSick.UI.Menus.LoadingScreen.Instance;
            if (loading != null && !loading.Finished) return false;
            return !SheetHost.FrameOpen && SeaSick.UI.Sheets.Sheets.Current == null;
        }

        // ------------------------------------------------------------ draw

        void OnGUI()
        {
            // Drawing only, once a frame, on the repaint.
            if (Event.current.type != EventType.Repaint) return;
            count = 0;
            if (!Gate(out var gun)) return;
            var cam = SeaCamera();
            if (cam == null) return;
            var arc = gun.InArc;
            if (arc == null || arc.Count == 0) return;

            int u = HudLayout.Unit;
            EnsureTextures();
            float ring = u * RingUnits;
            var target = gun.Target;
            float margin = ring;

            for (int i = 0; i < arc.Count && count < MaxMarkers; i++)
            {
                var t = arc[i];
                if (t == null || (t is Object o && o == null)) continue;
                Vector3 sp = cam.WorldToScreenPoint(t.HookPoint);
                if (sp.z <= 0f) continue;
                float gx = sp.x, gy = Screen.height - sp.y;
                if (gx < margin || gx > Screen.width - margin || gy < margin || gy > Screen.height - margin) continue;

                bool isTarget = ReferenceEquals(t, target);
                float size = isTarget ? ring * TargetScale * HaloScale : ring;
                var box = new Rect(gx - size * 0.5f, gy - size * 0.5f, size, size);
                // Never under another piece of the sea HUD or the combat row.
                if (HudHit(box)) continue;
                slots[count++] = new Slot { gui = new Vector2(gx, gy), isTarget = isTarget };
            }

            var prevColor = GUI.color;
            // The picked one last, so it sits on top where two crowd.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < count; i++)
                    if (slots[i].isTarget == (pass == 1)) DrawMarker(slots[i], ring);
            GUI.color = prevColor;
        }

        /// True when `r` lands on the sea HUD, the combat row, or any panel the
        /// layout has reserved (the bottom stack's Wheel slot included).
        static bool HudHit(Rect r)
        {
            if (SeaHud.Overlaps(r)) return true;
            if (CombatHud.Visible && CombatHud.Rect.Overlaps(r)) return true;
            var issued = HudLayout.Issued;
            for (int i = 0; i < issued.Count; i++)
                if (issued[i].Overlaps(r)) return true;
            return false;
        }

        void DrawMarker(in Slot m, float ring)
        {
            float size = m.isTarget ? ring * TargetScale : ring;
            var r = new Rect(m.gui.x - size * 0.5f, m.gui.y - size * 0.5f, size, size);
            if (m.isTarget)
            {
                // The halo: "this is the one the button fires at".
                float halo = size * HaloScale;
                GUI.color = Ice;
                GUI.DrawTexture(new Rect(m.gui.x - halo * 0.5f, m.gui.y - halo * 0.5f, halo, halo),
                    haloTex, ScaleMode.StretchToFill, true);
            }
            GUI.color = m.isTarget ? Ice : Backdrop;
            GUI.DrawTexture(r, discTex, ScaleMode.StretchToFill, true);
            GUI.color = m.isTarget ? Dark : Pearl;
            GUI.DrawTexture(r, ringTex, ScaleMode.StretchToFill, true);
        }

        // ------------------------------------------------------- textures

        void EnsureTextures()
        {
            if (ringTex == null) ringTex = BuildRing();
            if (discTex == null) discTex = BuildDisc();
            if (haloTex == null) haloTex = BuildHalo();
        }

        const int N = 128;
        const float Grid = 24f;

        /// The ring and the hook, white with a dark outline, on the 24-unit
        /// grid `SeaGlyph.HookStrokes` is drawn on. The white takes
        /// `GUI.color`.
        static Texture2D BuildRing()
        {
            const float ringR = 10.6f, ringHalf = 0.9f, hookHalf = 1.1f, outline = 0.9f;
            float pxPerUnit = N / Grid;
            var strokes = SeaGlyph.HookStrokes;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float gx = (x + 0.5f) / N * Grid;
                    float gy = (1f - (y + 0.5f) / N) * Grid;
                    var p = new Vector2(gx, gy);
                    float sd = Mathf.Abs(Vector2.Distance(p, new Vector2(Grid * 0.5f, Grid * 0.5f)) - ringR) - ringHalf;
                    for (int s = 0; s < strokes.Length; s++)
                    {
                        var path = strokes[s];
                        for (int i = 1; i < path.Length; i++)
                            sd = Mathf.Min(sd, SegmentDistance(p, path[i - 1], path[i]) - hookHalf);
                    }
                    float inner = Mathf.Clamp01(0.5f - sd * pxPerUnit);
                    float outer = Mathf.Clamp01(0.5f - (sd - outline) * pxPerUnit);
                    byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(14f, 255f, inner));
                    px[y * N + x] = new Color32(c, c, c, (byte)Mathf.RoundToInt(outer * 255f));
                }
            return Make(px);
        }

        /// A plain ring, white with a dark outline (no hook): the target's halo.
        static Texture2D BuildHalo()
        {
            const float ringR = 10.8f, ringHalf = 0.75f, outline = 0.7f;
            float pxPerUnit = N / Grid;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float gx = (x + 0.5f) / N * Grid - Grid * 0.5f;
                    float gy = (y + 0.5f) / N * Grid - Grid * 0.5f;
                    float sd = Mathf.Abs(Mathf.Sqrt(gx * gx + gy * gy) - ringR) - ringHalf;
                    float inner = Mathf.Clamp01(0.5f - sd * pxPerUnit);
                    float outer = Mathf.Clamp01(0.5f - (sd - outline) * pxPerUnit);
                    byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(14f, 255f, inner));
                    px[y * N + x] = new Color32(c, c, c, (byte)Mathf.RoundToInt(outer * 255f));
                }
            return Make(px);
        }

        /// A soft-edged white disc, the marker's backing.
        static Texture2D BuildDisc()
        {
            const float radius = 11.4f;
            float pxPerUnit = N / Grid;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float gx = (x + 0.5f) / N * Grid - Grid * 0.5f;
                    float gy = (y + 0.5f) / N * Grid - Grid * 0.5f;
                    float d = Mathf.Sqrt(gx * gx + gy * gy) - radius;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d * pxPerUnit) * 255f));
                }
            return Make(px);
        }

        static Texture2D Make(Color32[] px)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        void OnDestroy()
        {
            if (ringTex != null) Destroy(ringTex);
            if (discTex != null) Destroy(discTex);
            if (haloTex != null) Destroy(haloTex);
        }
    }
}
