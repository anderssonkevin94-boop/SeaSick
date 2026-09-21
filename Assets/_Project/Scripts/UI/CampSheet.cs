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
    ///
    /// **It is a BAR until it is asked to be a sheet (2026-09-20).** Kevin, the
    /// first time he played the hands-on view: *"right now i cant playtest the
    /// rest of the steps because the ui is blocked by the menu."* The lower
    /// third was the right size for a menu that was the only way to give an
    /// order. It is the wrong size for a fallback lying across the island you
    /// are trying to take hold of — it swallowed every press in 36 % of the
    /// screen. So it sits folded along the bottom edge as one row: what this
    /// place is, and the one button that matters right now (make camp, never
    /// mind, or open the crew lists). It opens when asked and **folds itself
    /// away the moment a hand takes hold of the land or of a villager**,
    /// because at that moment it has said what it had to say.
    public class CampSheet : MonoBehaviour
    {
        [Tooltip("Fraction of the safe area the sheet may take WHEN OPEN. The island has to stay visible above it.")]
        [SerializeField] float heightFraction = 0.36f;

        /// Are the crew lists open? Folded is the resting state, and every
        /// new island starts folded.
        public static bool Expanded { get; private set; }
        Island shownFor;
        CameraRig.IslandCam islandCam;

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
            var isle = Subject();
            Showing = isle != null;

            if (isle != shownFor) { shownFor = isle; Expanded = false; }
            if (!Expanded) return;

            // Out of the way the moment somebody reaches for the island. The
            // lists are a way of giving an order; a hand on the land or on a
            // villager is another way of doing the same thing, and the two
            // should not be on screen together.
            if (islandCam == null) islandCam = GetComponentInParent<CameraRig.IslandCam>();
            if (islandCam == null) islandCam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            bool reaching = (islandCam != null && islandCam.Grabbing)
                            || (Hand.Instance != null && Hand.Instance.Holding);
            if (reaching) Expanded = false;
        }

        void OnGUI()
        {
            var isle = Subject();
            Showing = isle != null;
            if (!Showing) return;

            EnsureStyles();

            var safe = HudLayout.Safe;
            var outpost = Outpost.Of(isle);

            float pad = HudLayout.Unit;
            float lineH = HudLayout.Unit * 1.6f;
            float btnH = HudLayout.Unit * 2.2f;

            // The lists only mean anything once there is a camp or the drawing
            // of one; before that the bar's own button is the whole decision.
            bool hasLists = outpost != null && (outpost.HasCamp || outpost.Building)
                            && !CampSiting.Placing;
            bool open = Expanded && hasLists;

            // --- the bar: one row on a desk, two on a phone ------------------
            // On a phone the headline does not fit beside two buttons, so it
            // gets a line of its own above them.
            bool wide = HudLayout.Wide;
            float barH = (wide ? btnH : lineH + btnH) + pad * 0.8f;
            float openH = Mathf.Max(barH, safe.height * heightFraction);
            float h = open ? openH : barH;
            var sheet = FitBetweenTheClusters(safe, h);

            UIBlocker.Block(sheet);
            UITheme.Rect(sheet, UITheme.PanelSolid);

            var inner = new Rect(sheet.x + pad, sheet.y + pad * 0.4f,
                                 sheet.width - pad * 2f, sheet.height - pad * 0.8f);

            float toggleW = HudLayout.Unit * 7f;
            float actionW = HudLayout.Unit * 11f;
            float btnY = wide ? inner.y : inner.y + lineH;
            float right = inner.xMax;

            // The fold, furthest right where a thumb finds it.
            if (hasLists)
            {
                var tog = new Rect(right - toggleW, btnY, toggleW, btnH);
                if (GUI.Button(tog, open ? "▼  hide" : "▲  crew", UITheme.Button))
                    Expanded = !open;
                right = tog.x - HudLayout.Gap;
            }

            // The one thing this place is asking for right now.
            string say = null;
            if (outpost == null)
                say = Outpost.Surveying(isle) ? "looking over the ground…"
                                              : "no ground here will take a camp";
            else if (CampSiting.Placing)
            {
                var stop = new Rect(right - actionW, btnY, actionW, btnH);
                if (GUI.Button(stop, "✕   Never mind", UITheme.Button)) CampSiting.End();
                right = stop.x - HudLayout.Gap;
                string refusal = CampSiting.Refusal;
                say = string.IsNullOrEmpty(refusal)
                    ? "tap the ground inside the ring   ·   R turns it 45°"
                    : "✕ " + refusal;
            }
            else if (!outpost.HasCamp && !outpost.Building)
            {
                var btn = new Rect(right - actionW, btnY, actionW, btnH);
                if (GUI.Button(btn, "🔥   Make camp", UITheme.Button))
                    CampSiting.Begin(outpost, BuildPlans.Campfire,
                        motor != null ? motor.transform : null);
                right = btn.x - HudLayout.Gap;
                say = $"a fire where you want it — {BuildPlans.Campfire.cost} logs, "
                    + "cut by whoever you leave";
            }

            // What this place is; and under it (or instead of it, on a phone's
            // single text line) what it is asking.
            float textW = wide ? Mathf.Max(0f, right - inner.x) : inner.width;
            string headline = HeadlineFor(isle, outpost);
            if (wide)
            {
                bool two = !string.IsNullOrEmpty(say);
                GUI.Label(new Rect(inner.x, inner.y, textW, two ? btnH * 0.5f : btnH), headline, title);
                if (two) GUI.Label(new Rect(inner.x, inner.y + btnH * 0.5f, textW, btnH * 0.5f), say, body);
            }
            else
            {
                GUI.Label(new Rect(inner.x, inner.y, textW, lineH), headline, title);
                if (!string.IsNullOrEmpty(say))
                    GUI.Label(new Rect(inner.x, btnY, Mathf.Max(0f, right - inner.x), btnH), say, body);
            }

            if (!open) return;

            // --- open: the crew, which is the whole point of the sheet -------

            float y = btnY + btnH + HudLayout.Gap;

            // What the view is doing, in the unit the dock shot is authored in
            // (165 m), so what is on screen can be compared with the authored
            // number rather than guessed at. Per the project's own rule: for a
            // look call, draw the numbers on the picture.
            string view = anchor != null ? anchor.ViewReadout : null;
            if (!string.IsNullOrEmpty(view))
            {
                GUI.Label(new Rect(inner.x, y, inner.width, lineH),
                    view + "   ·   drag the land, wheel zooms, right-drag turns, End recentres", body);
                y += lineH;
            }

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

        // Where the sheet may lie, remembered from the last Repaint.
        float fitLeft = -1f, fitRight = -1f;
        bool fitAbove;

        /// **Along the bottom edge, between whatever already lives there.**
        ///
        /// The first fold-away bar ran the full width and `HudOverlapProbe` had
        /// it across the helm cluster inside a second (200 x 46 px, permanent)
        /// — the 36 % slab had been lying on the helm all along, it was just
        /// too big for anyone to think of it as an overlap. So the bar asks
        /// what the layout has issued in the band it wants and stops short of
        /// it on either side; and where that leaves too little room to read
        /// (a phone, whose bottom clusters are most of its width) it sits on
        /// top of them instead, at `HudLayout.BottomClustersTop`.
        ///
        /// Measured on Repaint and remembered, because `HudLayout.Issued` is
        /// rebuilt through the frame: by Repaint every panel has declared
        /// itself in the Layout pass, while on a mouse event at the top of the
        /// frame the list is half empty and the bar would jump under the
        /// pointer that is trying to press it.
        Rect FitBetweenTheClusters(Rect safe, float h)
        {
            if (Event.current.type == EventType.Repaint || fitLeft < 0f)
            {
                float top = safe.yMax - h;
                float left = safe.x, right = safe.xMax;
                var issued = HudLayout.Issued;
                for (int k = 0; k < issued.Count; k++)
                {
                    var r = issued[k];
                    if (r.yMax <= top || r.y >= safe.yMax || r.width <= 0f) continue;
                    if (r.center.x > safe.center.x) right = Mathf.Min(right, r.x - HudLayout.Gap);
                    else left = Mathf.Max(left, r.xMax + HudLayout.Gap);
                }
                fitAbove = right - left < safe.width * 0.6f;
                fitLeft = fitAbove ? safe.x : left;
                fitRight = fitAbove ? safe.xMax : right;
            }

            float bottom = fitAbove ? HudLayout.BottomClustersTop : safe.yMax;
            return new Rect(fitLeft, bottom - h, fitRight - fitLeft, h);
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
                else if (outpost.Ledger.BuildStarved)
                    sb.Append("   ·   NO TIMBER LEFT — nothing piled, nothing standing");
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
