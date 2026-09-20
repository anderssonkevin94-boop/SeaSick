using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// The orders sheet: who stays, who sails, and what the camp is holding.
    ///
    /// **A bottom sheet, settled with Kevin 2026-09-13.** He asked for a menu;
    /// the house rules say no banners, `HudLayout` owns every slot and
    /// `Prompts` owns exactly one contextual button. A full-screen overlay
    /// would also hide the island you just flew up to look at, which is the
    /// one thing the bird's-eye exists for. So it takes the lower third —
    /// thumb height, where the prompt slot already lives — and leaves the top
    /// two thirds showing the place you are deciding about.
    ///
    /// It is a MODE, not a layer: it appears only while she is anchored at an
    /// island that is not home, and nowhere else. That is what lets it break
    /// the one-prompt rule without breaking the HUD.
    ///
    /// Tap a name to move that hand between the ship and the shore. There is
    /// no drag, no long press and no confirmation — one thumb, one tap, and
    /// the consequence is reversible until you weigh anchor.
    public class CampSheet : MonoBehaviour
    {
        [Tooltip("Fraction of the safe area the sheet may take. The island has to stay visible above it.")]
        [SerializeField] float heightFraction = 0.36f;

        AnchorController anchor;
        CrewRoster roster;
        ShipMotor motor;

        GUIStyle title, row, body;
        readonly StringBuilder sb = new StringBuilder();

        /// Set while the sheet is on screen, so anything else drawing in the
        /// bottom of the frame can stand aside rather than overlap it. The HUD
        /// has been caught doing that before.
        public static bool Showing { get; private set; }

        void Awake()
        {
            anchor = GetComponentInParent<AnchorController>();
            if (anchor == null) anchor = Object.FindFirstObjectByType<AnchorController>();
            roster = Object.FindFirstObjectByType<CrewRoster>();
            motor = Object.FindFirstObjectByType<ShipMotor>();

            // Siting mode rides along with the sheet that starts it rather
            // than being a second thing to place in `Sea.unity`. The scene is
            // carrying other uncommitted work and every object added to it is
            // a merge nobody wants; this needs no serialised state.
            if (CampSiting.Instance == null) gameObject.AddComponent<CampSiting>();
            // The crew list rides along for the same reason: no serialised
            // state, and `Sea.unity` is carrying other people's uncommitted
            // work.
            if (GetComponent<CampCrewList>() == null) gameObject.AddComponent<CampCrewList>();
        }

        /// The island she is lying at, if this sheet has anything to say.
        Island Subject()
        {
            if (anchor == null) return null;
            if (anchor.CurrentState != AnchorController.State.Anchored
                && anchor.CurrentState != AnchorController.State.Ashore) return null;
            var isle = anchor.CurrentIsland;
            if (isle == null || isle.IsHome) return null;
            return isle;
        }

        void Update()
        {
            // Keep `Showing` honest outside OnGUI as well, so anyone reading it
            // gets the same answer whatever order things run in.
            Showing = Subject() != null;
        }

        void OnGUI()
        {
            var isle = Subject();
            Showing = isle != null;
            if (!Showing) return;

            EnsureStyles();

            var safe = HudLayout.Safe;
            float h = safe.height * heightFraction;
            var sheet = new Rect(safe.x, safe.yMax - h, safe.width, h);

            UIBlocker.Block(sheet);
            UITheme.Rect(sheet, UITheme.PanelSolid);

            float pad = HudLayout.Unit;
            var inner = new Rect(sheet.x + pad, sheet.y + pad * 0.6f,
                                 sheet.width - pad * 2f, sheet.height - pad * 1.2f);

            var outpost = Outpost.Of(isle);

            // --- the line that says what this place is ----------------------
            float lineH = HudLayout.Unit * 1.6f;
            var head = new Rect(inner.x, inner.y, inner.width, lineH);
            GUI.Label(head, HeadlineFor(isle, outpost), title);

            // What the view is doing, in the unit the dock shot is authored in
            // (165 m), so what is on screen can be compared with the authored
            // number rather than guessed at. Per the project's own rule: for a
            // look call, draw the numbers on the picture.
            string view = anchor != null ? anchor.ViewReadout : null;
            if (!string.IsNullOrEmpty(view))
                GUI.Label(new Rect(inner.x, head.yMax, inner.width, lineH),
                    view + "   ·   arrows pan, +/− zoom, tap a hand to follow, End recentres", body);

            float y = head.yMax + (string.IsNullOrEmpty(view) ? 0f : lineH) + HudLayout.Gap;

            // Nothing to command yet: the ground is still being looked at, or
            // it will not take a camp. Say which — an empty sheet is
            // indistinguishable from a broken one.
            if (outpost == null)
            {
                var wait = new Rect(inner.x, y, inner.width, HudLayout.Unit * 2.7f);
                GUI.Label(wait, Outpost.Surveying(isle)
                    ? "looking over the ground…"
                    : "no ground here will take a camp", body);
                return;
            }

            // --- siting: the blueprint is on the end of your thumb ----------
            if (CampSiting.Placing)
            {
                var stop = new Rect(inner.x, y, inner.width, HudLayout.Unit * 2.7f);
                if (GUI.Button(stop, "✕   Never mind", UITheme.Button)) CampSiting.End();
                string refusal = CampSiting.Refusal;
                GUI.Label(new Rect(inner.x, stop.yMax + HudLayout.Gap, inner.width, lineH),
                    string.IsNullOrEmpty(refusal)
                        ? "tap the ground inside the ring   ·   R turns it 45°, shift+R back"
                        : "✕ " + refusal, body);
                return;
            }

            // Nothing sited and nothing built: the decision is still to be
            // made. This is the only button on the sheet that spends nothing
            // — what it costs is the wood the crew will cut for it.
            if (!outpost.HasCamp && !outpost.Building)
            {
                var btn = new Rect(inner.x, y, inner.width, HudLayout.Unit * 2.7f);
                if (GUI.Button(btn, "🔥   Make camp", UITheme.Button))
                    CampSiting.Begin(outpost, BuildPlans.Campfire,
                        motor != null ? motor.transform : null);
                var note = new Rect(inner.x, btn.yMax + HudLayout.Gap, inner.width, lineH);
                GUI.Label(note, $"put a fire where you want it — {BuildPlans.Campfire.cost} logs, "
                    + "cut by whoever you leave behind", body);
                return;
            }

            // --- the crew, which is the whole point of the sheet -------------

            var l = outpost.Ledger;
            float rowH = HudLayout.Unit * 2.2f;
            float colGap = HudLayout.Gap;
            float colW = (inner.width - colGap) * 0.5f;

            GUI.Label(new Rect(inner.x, y, colW, lineH), "ABOARD", body);
            GUI.Label(new Rect(inner.x + colW + colGap, y, colW, lineH), "ASHORE", body);
            y += lineH;

            float listTop = y;
            float listH = inner.yMax - listTop;
            int perCol = Mathf.Max(1, Mathf.FloorToInt(listH / (rowH + 2f)));

            // Aboard: tap to leave them here.
            int i = 0;
            if (roster != null)
            {
                foreach (var hand in roster.All)
                {
                    if (hand == null || !hand.IsAboard) continue;
                    if (i >= perCol) break;
                    var r = new Rect(inner.x, listTop + i * (rowH + 2f), colW, rowH);
                    if (GUI.Button(r, hand.DisplayName, row))
                    {
                        if (outpost.Station(hand) && roster != null) roster.Refresh();
                    }
                    i++;
                }
            }

            // Ashore: tap to take them back.
            int j = 0;
            var parked = outpost.Parked();
            foreach (var ashore in l.hands)
            {
                if (ashore == null) continue;
                if (j >= perCol) break;
                var r = new Rect(inner.x + colW + colGap, listTop + j * (rowH + 2f), colW, rowH);
                if (GUI.Button(r, ashore.name, row))
                {
                    var him = Find(parked, ashore.name);
                    if (him != null && anchor != null
                        && outpost.Recall(him, anchor.transform) && roster != null)
                        roster.Refresh();
                }
                j++;
            }

            if (l.hands.Count > perCol || (roster != null && i >= perCol))
                GUI.Label(new Rect(inner.x, inner.yMax - lineH, inner.width, lineH),
                    "…", body);
        }

        static CrewAgent Find(CrewAgent[] all, string who)
        {
            foreach (var a in all) if (a != null && a.DisplayName == who) return a;
            return null;
        }

        /// What the player sailed back to read.
        string HeadlineFor(Island isle, Outpost outpost)
        {
            sb.Clear();
            sb.Append(isle.name);

            // A blueprint reports what it is waiting for, in logs, because
            // logs are what the player has to do something about. "Building"
            // on its own would be a progress bar with no verb attached.
            if (outpost != null && outpost.Building)
            {
                outpost.CatchUp();
                var p = outpost.Ledger.pending;
                int building = outpost.Ledger.HandsOn(OutpostOrder.Build);
                sb.Append("   ·   camp sited   ·   ").Append(p.done).Append(" / ")
                  .Append(p.needed).Append(" logs");
                sb.Append("   ·   ").Append(building)
                  .Append(building == 1 ? " hand building" : " hands building");
                if (building == 0) sb.Append("   ·   nobody is building it");
                return sb.ToString();
            }

            if (outpost != null && outpost.HasCamp && outpost.Ledger != null)
            {
                // Bring it up to now before reporting: a stale number is worse
                // than none, and the tick is free when nothing has elapsed.
                outpost.CatchUp();
                var l = outpost.Ledger;
                sb.Append("   ·   ").Append(l.Timber).Append(" / ").Append(l.ceilingPer)
                  .Append(" timber");
                int cutting = l.HandsOn(OutpostOrder.Gather);
                sb.Append("   ·   ").Append(cutting)
                  .Append(cutting == 1 ? " hand cutting" : " hands cutting");
                if (l.Wood.standing < 1f) sb.Append("   ·   the wood is cut out");
                else if (l.Timber >= l.ceilingPer) sb.Append("   ·   the pile is full");
            }
            else sb.Append("   ·   no camp");
            return sb.ToString();
        }

        void EnsureStyles()
        {
            if (title != null) return;
            title = new GUIStyle(UITheme.Small2Centered) { alignment = TextAnchor.MiddleLeft };
            body = new GUIStyle(UITheme.Small2Centered) { alignment = TextAnchor.MiddleLeft };
            row = new GUIStyle(UITheme.Button);
        }
    }
}
