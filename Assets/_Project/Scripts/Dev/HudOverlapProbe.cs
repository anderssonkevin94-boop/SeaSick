using System.Collections.Generic;
using System.Text;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Dev
{
    /// Does anything on the HUD sit on top of anything else?
    ///
    /// **It measures what was DRAWN, not what the layout says.** This project's
    /// most repeated mistake is a check that re-runs the rules it is checking:
    /// `DeckStandProbe` asked the deck for its highest vertex using the same
    /// rule that placed the crew there and reported a gap of exactly 0.000 m
    /// for all six, three of whom were 7 cm in the air. Re-deriving each
    /// panel's rect from `Screen.width` here would have exactly that shape —
    /// it would agree with `HudLayout` by construction and could never catch
    /// `HudLayout` being wrong.
    ///
    /// So the inputs are the rects the frame actually produced:
    ///
    ///   `UIBlocker.Claimed` — every INTERACTIVE control, with the file and
    ///       method that claimed it. These are the ones that matter most: two
    ///       overlapping buttons is a mis-tap, and a mis-tap on this HUD
    ///       anchors the ship or fires a broadside.
    ///   `HudLayout.Issued` — every panel the layout placed, as the rect it
    ///       was actually handed.
    ///
    /// The test itself — do two rectangles intersect — shares no arithmetic
    /// with the stacking that produced them, so a wrong column mapping, a
    /// stale height that never expires, or a panel that quietly grew past its
    /// slot all show up as a real failure.
    ///
    /// ## Judge it at the PHONE's aspect
    ///
    /// The editor Game view is landscape and the game is not. Every one of the
    /// overlaps this probe was written to catch is worse in portrait, and two
    /// of them (the foam tuner on the minimap, the clarity tuner on the crew
    /// pips) exist at both. `ChaseCamera.PortraitAspect` is the shipping
    /// aspect; a clean report from a landscape Game view certifies a screen no
    /// player will see. The report prints the aspect it ran at for exactly
    /// that reason.
    ///
    /// Leave it on while playing: it costs one pass over about twenty rects
    /// and says nothing until something overlaps. It reports each distinct
    /// pair once, so a permanent overlap does not fill the console.
    [DefaultExecutionOrder(10000)]   // after every other OnGUI, so the frame is complete
    public class HudOverlapProbe : MonoBehaviour
    {
        [Tooltip("Off costs nothing. On, it watches every frame and logs the first time any two HUD rects overlap.")]
        [SerializeField] bool watch = true;

        [Tooltip("Ignore overlaps smaller than this many pixels on their shorter side — a shared 1 px border is not a mis-tap.")]
        [SerializeField] float ignoreBelowPx = 3f;

        [Tooltip("Draw every claimed rect as an outline, so the layout can be seen as well as reported.")]
        [SerializeField] bool drawOutlines = false;

        [Tooltip("Ignore the first seconds of a run. The HUD's slots learn each other's heights over a frame or two, so the very first frame a column appears can stack wrong and then be right forever after — a real transient, but not a layout fault.")]
        [SerializeField] float settleSeconds = 1.5f;

        [Tooltip("Re-print the census this often even when nothing changed, so a report carries STEADY-STATE positions rather than only the frame a panel first appeared.")]
        [SerializeField] float censusEverySeconds = 5f;

#if UNITY_EDITOR
        /// Installs itself in the editor, for the same reason the settings
        /// drawer does: a check nobody remembers to add to a scene is a check
        /// that does not run. Editor only — this never reaches a build.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<HudOverlapProbe>() != null) return;
            new GameObject("HudOverlapProbe").AddComponent<HudOverlapProbe>();
        }
#endif

        /// How many frames each pair has been overlapping.
        ///
        /// "It overlapped" and "it overlaps" are different bugs and this used
        /// to report them identically. A pair seen ONCE is a cold-start
        /// transient — the frame before anything has reserved its space, where
        /// whichever panel draws first sees an empty screen. A pair seen every
        /// frame is a layout that is actually wrong. Reporting at 1, 30 and 300
        /// frames tells them apart without filling the console.
        readonly Dictionary<string, int> seenFor = new Dictionary<string, int>();
        readonly HashSet<string> reported = new HashSet<string>();
        readonly List<Rect> rects = new List<Rect>();
        readonly List<string> names = new List<string>();

        /// The set of owners the last census was printed for.
        int censusKey = 0;
        float nextCensus;

        /// The fullest frame seen lately, and when that record was set.
        ///
        /// IMGUI runs several GUI cycles per frame and the repaint-guarded
        /// panels only draw in some of them, so a census taken on a timer
        /// lands on a PARTIAL cycle most of the time — 13 rects out of 34,
        /// with the minimap, the ship panel, the crew pips and the compass
        /// all missing. The overlap test itself runs on every repaint and so
        /// sees the full cycles regardless; it is the printed EVIDENCE that
        /// was misleading, and a report that shows a third of the screen is a
        /// report that proves nothing about the other two thirds.
        int bestRects;
        float bestAt;

        /// Everything drawn this frame, both sources in one list.
        void Gather()
        {
            rects.Clear();
            names.Clear();

            var claimed = UIBlocker.Claimed;
            var owners = UIBlocker.Owners;
            for (int i = 0; i < claimed.Count; i++)
            {
                rects.Add(claimed[i]);
                names.Add(i < owners.Count ? owners[i] : "?");
            }

            var issued = HudLayout.Issued;
            var slots = HudLayout.IssuedTo;
            for (int i = 0; i < issued.Count; i++)
            {
                rects.Add(issued[i]);
                names.Add("slot:" + (i < slots.Count ? slots[i] : "?"));
            }
        }

        void OnGUI()
        {
            if (!watch) return;
            if (Event.current.type != EventType.Repaint) return;

            Gather();
            Census();
            if (Time.timeSinceLevelLoad < settleSeconds) return;

            for (int a = 0; a < rects.Count; a++)
                for (int b = a + 1; b < rects.Count; b++)
                {
                    // A button inside its own panel is nesting, not collision
                    // — but only when one side is a CONTROL. Two placed panels
                    // that contain one another is always a fault: slots tile,
                    // they never sit inside each other.
                    //
                    // The first version exempted every containment, and it
                    // promptly hid the real thing: the ship panel drew inside
                    // the minimap at (916..1066, 14..86) against the map's
                    // (826..1066, 14..254), and the probe said nothing. A check
                    // whose exemption covers the failure mode is a check that
                    // stops before the broken thing.
                    bool bothPanels = names[a].StartsWith("slot:") && names[b].StartsWith("slot:");
                    if (!bothPanels
                        && (Contains(rects[a], rects[b]) || Contains(rects[b], rects[a]))) continue;

                    var hit = Intersection(rects[a], rects[b]);
                    if (hit.width < ignoreBelowPx || hit.height < ignoreBelowPx) continue;
                    // The same panel placing itself twice in a frame is not an
                    // overlap with itself.
                    if (names[a] == names[b]) continue;

                    Report(names[a], rects[a], names[b], rects[b], hit);
                }

            if (drawOutlines) DrawOutlines();
        }

        /// **Say what was actually seen, so silence means something.**
        ///
        /// A probe that only speaks when it fails cannot be told apart from a
        /// probe that is not running — and this project has been caught by
        /// exactly that shape twice: `SailShots` printed a perfect table of ten
        /// seats while photographing one seat ten times, and `git bundle
        /// verify` said "complete history" about a backup missing every piece
        /// of art. The fix both times was to report what the run ACTUALLY
        /// produced rather than what was asked of it.
        ///
        /// So: every time the set of things on screen changes — she anchors,
        /// the drawer opens, the flood row appears — one line naming every
        /// rect it can see. A clean run then carries its own evidence that the
        /// whole HUD was in front of it.
        void Census()
        {
            int key = 17;
            for (int i = 0; i < names.Count; i++) key = key * 31 + names[i].GetHashCode();

            // The record is per HUD STATE, not global. Opening the drawer on a
            // tool shows FEWER rects than the list does, and a single global
            // high-water mark would have silenced the census for exactly the
            // state worth looking at.
            if (key != censusKey
                || Time.realtimeSinceStartup - bestAt > Mathf.Max(2f, censusEverySeconds * 2f))
                bestRects = 0;
            if (rects.Count >= bestRects) { bestRects = rects.Count; bestAt = Time.realtimeSinceStartup; }
            else return;   // a partial cycle: the check still ran, it is just not worth printing
            bool due = Time.unscaledTime >= nextCensus;
            if (key == censusKey && !due) return;
            censusKey = key;
            nextCensus = Time.unscaledTime + Mathf.Max(1f, censusEverySeconds);

            var sb = new StringBuilder();
            // The frame number is in the header because two censuses sharing
            // one frame is the difference between "the HUD is flickering" and
            // "IMGUI ran its GUI cycle twice", and those want opposite
            // reactions. Without it, a partial census is unreadable.
            sb.AppendLine($"HUD CENSUS — {rects.Count} rects, frame {Time.frameCount}, "
                        + $"screen {Screen.width}x{Screen.height} "
                        + $"(aspect {(float)Screen.width / Mathf.Max(1, Screen.height):F3}), "
                        + $"UITheme.Unit {UITheme.Unit}");
            for (int i = 0; i < rects.Count; i++)
                sb.AppendLine($"    {names[i],-38} {Fmt(rects[i])}");
            if (rects.Count == 0)
                sb.AppendLine("    NOTHING. Either the HUD is not drawing or this probe is not seeing it — "
                            + "a clean overlap run means nothing from here.");
            Debug.Log(sb.ToString());
        }

        void Report(string an, Rect ar, string bn, Rect br, Rect hit)
        {
            string key = string.CompareOrdinal(an, bn) < 0 ? an + "|" + bn : bn + "|" + an;
            seenFor.TryGetValue(key, out int n);
            n++;
            seenFor[key] = n;
            // Speak at 1 (it happened), 30 (it is not a transient) and 300
            // (it is permanent), and stay quiet in between.
            if (n != 1 && n != 30 && n != 300) return;
            string persistence = n == 1
                ? "FIRST TIME — may be the cold-start frame; wait for a 30-frame line before believing it"
                : n == 30 ? "30 FRAMES — not a transient, this is a real overlap"
                          : "300 FRAMES — permanent";

            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            var sb = new StringBuilder();
            sb.AppendLine("HUD OVERLAP");
            sb.AppendLine($"  {an}   {Fmt(ar)}");
            sb.AppendLine($"  {bn}   {Fmt(br)}");
            sb.AppendLine($"  overlap {hit.width:F0} x {hit.height:F0} px at ({hit.x:F0}, {hit.y:F0})");
            sb.AppendLine($"  {persistence}");
            sb.AppendLine($"  screen {Screen.width}x{Screen.height} (aspect {aspect:F3}), "
                        + $"UITheme.Unit {UITheme.Unit}");
            if (aspect > 1f)
                sb.AppendLine("  NOTE: this is a LANDSCAPE view. The game is portrait — "
                            + "judge it at ChaseCamera.PortraitAspect before believing a clean run.");
            Debug.LogWarning(sb.ToString());
        }

        static string Fmt(Rect r) => $"x {r.x:F0}..{r.xMax:F0}   y {r.y:F0}..{r.yMax:F0}";

        static bool Contains(Rect outer, Rect inner) =>
            inner.x >= outer.x - 1f && inner.xMax <= outer.xMax + 1f
            && inner.y >= outer.y - 1f && inner.yMax <= outer.yMax + 1f;

        static Rect Intersection(Rect a, Rect b)
        {
            float x = Mathf.Max(a.x, b.x);
            float y = Mathf.Max(a.y, b.y);
            return new Rect(x, y, Mathf.Min(a.xMax, b.xMax) - x, Mathf.Min(a.yMax, b.yMax) - y);
        }

        void DrawOutlines()
        {
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                var c = names[i].StartsWith("slot:")
                    ? new Color(0.45f, 0.78f, 0.92f, 0.9f)
                    : new Color(0.95f, 0.75f, 0.25f, 0.9f);
                UITheme.Rect(new Rect(r.x, r.y, r.width, 1f), c);
                UITheme.Rect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
                UITheme.Rect(new Rect(r.x, r.y, 1f, r.height), c);
                UITheme.Rect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
            }
        }

        /// So a run can be re-armed without leaving play mode.
        public void Rearm() { reported.Clear(); seenFor.Clear(); }
    }
}
