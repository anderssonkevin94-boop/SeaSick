using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Dev
{
    /// The live instrument for "the ocean skipped and my boat fell into it".
    ///
    /// A frame-rate number ALONE cannot answer that complaint, because two
    /// unrelated faults produce it and they want opposite fixes: the PICTURE
    /// stalling (a frame hitch — everything stops, the world moves on, the
    /// hull arrives somewhere new), versus the WATER stepping while the
    /// picture stays perfectly smooth (the sampled surface the hull floats on
    /// is not the surface being drawn). Kevin's report — "it skips in the
    /// ocean animation but not in the game" — is the second one, and a bare
    /// fps counter would have shown 60 and told him nothing.
    ///
    /// HitchProbe measures these same quantities over a timed run and writes a
    /// file. This is the version you keep on screen while you sail, so a jolt
    /// you FEEL has a number beside it the moment you feel it.
    ///
    /// The rows:
    ///   fps — smoothed, with the worst recent frame time beside it. A pop
    ///         that lines up with a spike here was the renderer.
    ///   sea — how far behind the DRAWN surface the SAMPLED one is
    ///         (OceanTime.Now − OceanSampler.SurfaceTime). The hull floats on
    ///         the sampled surface, so this is the boat's view of the water,
    ///         in milliseconds of staleness. Steady-state is the readback
    ///         ring's latency; it widens exactly when frames spike, which is
    ///         why the two faults get confused. Shown in FRAMES as well as
    ///         milliseconds, and the frames figure is the one to quote: with
    ///         the Game view unfocused the editor throttles play mode to a
    ///         dead-steady 10 fps, which inflates every millisecond reading
    ///         here about sixfold while leaving the frame count correct.
    ///   pop — the worst one-frame change in the gap between the hull and the
    ///         water under it, peak-held with a decay. Riding a wave, however
    ///         big, barely moves this: the ship follows the surface, so the
    ///         gap stays put. It only jumps when the surface TELEPORTS under
    ///         her — which is precisely the complaint. A dot means the
    ///         spectrum was rebuilt just before the peak, i.e. the sea itself
    ///         changed shape under the boat rather than merely arriving late.
    ///
    /// Drawn through UITheme so it sits with the rest of the HUD instead of
    /// looking like a debug overlay, stacked under the ship panel on the right
    /// edge. Repaint-guarded and HudLabel-cached: an OnGUI panel that formats
    /// strings on every event is the single largest allocator this project has
    /// measured, and a GC pause drops a frame — which would make this
    /// instrument cause the very thing it is here to measure.
    public class PerfHUD : MonoBehaviour
    {
        [Tooltip("Off hides the readout however the settings drawer is set. The player-facing switch is the 'performance readout' row in Settings.")]
        [SerializeField] bool show = true;

        /// The component's own switch AND the player's. This is an instrument
        /// somebody may want on screen for a whole session, so it lives with
        /// the HUD options rather than with the tuners — it has no knobs, it
        /// only tells you things.
        bool Showing => show && SeaSick.UI.HudVisibility.Perf;

        [Tooltip("How long the peak frame time and peak pop hold before decaying away, seconds.")]
        [SerializeField] float holdSeconds = 3f;

        [Tooltip("A pop is blamed on a spectrum rebuild if one landed within this many seconds before it.")]
        [SerializeField] float rebuildBlameWindow = 0.35f;

        [Tooltip("How often the readout may rebuild its text, Hz. Not scene-serialized: this component bootstraps itself.")]
        [SerializeField] float refreshHz = 4f;
        float nextRefresh;

        /// Installs itself after scene load unless a scene already carries
        /// one.
        ///
        /// A dev instrument that has to be wired into a .unity file is an
        /// instrument that is missing from the other two scenes, and that
        /// bakes its defaults into the scene the day it is added — this
        /// project's most-repeated trap, and the reason a tuning value edited
        /// in C# so often does nothing. Bootstrapping means the code on disk
        /// stays the truth, and OceanLab and TerrainLab get the readout for
        /// free. A component placed by hand still wins outright: this only
        /// ever fills a gap.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // A dev instrument has no business in a release build: it costs
            // an IMGUI panel and an ocean sample every frame, and it
            // contaminates the CostProbe baseline it exists to inform.
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            if (FindAnyObjectByType<PerfHUD>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("PerfHUD");
            go.AddComponent<PerfHUD>();
            DontDestroyOnLoad(go);
        }

        ShipMotor motor;
        float motorSearchDue;

        float fps = 60f;
        float peakMs;
        float seaLagMs = -1f;
        float seaLagFrames = -1f;
        float smoothFrameMs = 16.7f;
        float pop;
        bool popFromRebuild;

        float prevGap;
        bool haveGap;
        int lastRebuildCount = -1;
        float sinceRebuild = 999f;

        readonly HudLabel fpsText = new HudLabel();
        readonly HudLabel seaText = new HudLabel();
        readonly HudLabel popText = new HudLabel();

        void Update()
        {
            if (!Showing) return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-5f);
            float frameMs = dt * 1000f;

            // Smoothed so the number is readable rather than a blur, but the
            // PEAK is held raw — an average frame time hides exactly the
            // one-frame stall this is looking for.
            fps = Mathf.Lerp(fps, 1f / dt, 1f - Mathf.Exp(-dt * 4f));
            float decay = Mathf.Exp(-dt / Mathf.Max(holdSeconds, 0.1f));
            peakMs = Mathf.Max(peakMs * decay, frameMs);

            sinceRebuild += dt;
            var ocean = OceanRenderer.Instance;
            if (ocean != null)
            {
                int rc = ocean.SpectrumRebuilds;
                if (lastRebuildCount >= 0 && rc != lastRebuildCount) sinceRebuild = 0f;
                lastRebuildCount = rc;
            }

            // Smoothed denominator, not this frame's dt: dividing a lag by
            // the very frame time that spiked to cause it reads the sawtooth
            // backwards.
            smoothFrameMs = Mathf.Lerp(smoothFrameMs, frameMs, 1f - Mathf.Exp(-dt * 2f));
            seaLagMs = OceanSampler.Ready
                ? (float)(OceanTime.Now - OceanSampler.SurfaceTime) * 1000f
                : -1f;
            seaLagFrames = seaLagMs < 0f ? -1f : seaLagMs / Mathf.Max(smoothFrameMs, 0.1f);

            TrackPop(decay);
        }

        /// The gap between the hull and the water under it, differenced frame
        /// to frame. One SampleImmediate per frame is well inside the ~8/frame
        /// budget that call documents.
        void TrackPop(float decay)
        {
            if (motor == null)
            {
                // Retried on a timer, never every frame: FindFirstObjectByType
                // in an Update that runs before the ship exists would scan the
                // scene sixty times a second for nothing.
                if (Time.unscaledTime < motorSearchDue) return;
                motorSearchDue = Time.unscaledTime + 1f;
                motor = FindAnyObjectByType<ShipMotor>();
                if (motor == null) return;
            }

            if (!OceanSampler.Ready) return;

            Vector3 p = motor.transform.position;
            float gap = p.y - OceanSampler.SampleImmediate(p).height;

            if (haveGap)
            {
                float jump = Mathf.Abs(gap - prevGap);
                float held = pop * decay;
                if (jump > held)
                {
                    pop = jump;
                    popFromRebuild = sinceRebuild < rebuildBlameWindow;
                }
                else pop = held;
            }
            prevGap = gap;
            haveGap = true;
        }

        void OnGUI()
        {
            // IMGUI runs OnGUI once per EVENT, not per frame. Everything this
            // panel does on a non-Repaint event is computed and thrown away,
            // and the strings built on the way are garbage. StatusHUD measured
            // 14.4 KB a frame before it took this guard.
            if (!Showing) return;

            // Re-key the labels a few times a second, not every frame. The
            // keys are rounded numbers, but two of them DECAY (peak frame
            // time, pop) and the fps integer jitters at high frame rates, so
            // "only rebuild when the displayed value moves" was rebuilding
            // three text meshes most frames -- measured 62 KB a frame from
            // this instrument alone, which made it a cause of the GC pauses
            // it exists to show.
            int u = HudLayout.Unit;
            float w = u * 9.0f;
            float rowH = u * 1.25f;
            float inner = u * 0.6f;
            float h = inner * 2f + rowH * 3f;

            // The right-hand column places this, so it lands under whatever is
            // actually drawing above it — and closes up when the minimap is
            // switched off. It used to derive its y from a public constant on
            // StatusHUD, which was one hand-copy better than a magic number
            // and still meant two files had to agree.
            var panel = HudLayout.Place(HudLayout.Slot.Perf, w, h);
            if (SeaSick.UI.Sheets.MidnightLandHud.Active)
            {
                var resources = SeaSick.UI.Sheets.MidnightLandHud.ResourcesRect;
                panel.x = resources.x;
                panel.y = Mathf.Max(panel.y, resources.yMax + u);
            }

            // Reserved above, drawn below: the rows are what cost, not the rect.
            if (Event.current.type != EventType.Repaint) return;

            bool due = Time.unscaledTime >= nextRefresh;
            if (due) nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, refreshHz);

            float x = panel.x, y = panel.y;
            UITheme.Rect(panel, UITheme.Panel);

            float ry = y + inner;
            DrawRow(x + inner, ry, w, rowH, fpsText, FpsKey(), BuildFps, due,
                UITheme.Ramp(Mathf.InverseLerp(60f, 20f, fps)));

            ry += rowH;
            DrawRow(x + inner, ry, w, rowH, seaText,
                HudLabel.Key(Mathf.RoundToInt(seaLagMs), Mathf.RoundToInt(seaLagFrames * 10f)), BuildSea, due,
                // Ramped on FRAMES, not milliseconds: the frame count is what
                // stays honest under the editor's unfocused-play throttle.
                // 3 frames is the ring's designed latency, 7 is trouble.
                seaLagMs < 0f ? UITheme.TextDim
                              : UITheme.Ramp(Mathf.InverseLerp(3f, 7f, seaLagFrames)));

            ry += rowH;
            DrawRow(x + inner, ry, w, rowH, popText,
                HudLabel.Key(Mathf.RoundToInt(pop * 100f), popFromRebuild ? 1 : 0), BuildPop, due,
                // 0.75 m is HitchProbe's own "this is a pop, not a wave"
                // threshold; the ramp is anchored to it so the two
                // instruments agree about what counts as bad.
                UITheme.Ramp(Mathf.InverseLerp(0.10f, 0.75f, pop)));
        }

        long FpsKey() => HudLabel.Key(Mathf.RoundToInt(fps), Mathf.RoundToInt(peakMs));

        void BuildFps() => fpsText.Set($"fps {fps:F0}   ▲{peakMs:F0} ms");
        void BuildSea() => seaText.Set(seaLagMs < 0f
            ? "sea   —"
            : $"sea {seaLagFrames:F1} fr  {seaLagMs:F0} ms");
        void BuildPop() => popText.Set(popFromRebuild ? $"pop {pop:F2} m ·" : $"pop {pop:F2} m");

        /// Formats only when the DISPLAYED value moves, which is the whole
        /// point of HudLabel — a steady 60 fps costs no string at all.
        static void DrawRow(float x, float y, float w, float h,
            HudLabel label, long key, System.Action build, bool due, Color tint)
        {
            if (due && label.Changed(key)) build();
            var prev = GUI.color;
            GUI.color = tint;
            GUI.Label(new Rect(x, y, w, h), label.Content, UITheme.Small);
            GUI.color = prev;
        }
    }
}
