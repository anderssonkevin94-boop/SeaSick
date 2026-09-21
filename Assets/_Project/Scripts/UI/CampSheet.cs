using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
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
    ///
    /// **And it is where you LOAD (2026-09-20).** The fourth step of the loop
    /// — *return and load* — had no button anywhere in the game: a camp's pile
    /// could grow to its ceiling and stay there for ever. The bar's action slot
    /// now offers ⬆ Load once there is a camp, something in it and room
    /// aboard, and turns into ✕ Stop while the crew are carrying; the open
    /// sheet grows a row per kind so you can take the tools and leave the
    /// firewood. Siting and making camp keep the slot, because until there is
    /// a camp there is nothing to load. **The headline lists every kind now**,
    /// not just timber — a camp with a sawmill and a seam in it was reporting
    /// one of the three things it was holding. Every string on the bar is
    /// built only when what it says changes: `OnGUI` runs several times a
    /// frame and this one draws on all of them.
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
        VoyageManager voyage;
        ShipHold hold;
        Shipyard yard;

        GUIStyle title, row, body, rate;
        readonly StringBuilder sb = new StringBuilder();

        // --- every string this bar draws, built only when it changes ----------
        //
        // IMGUI runs OnGUI once per EVENT, so a line interpolated in the draw
        // is built three or four times a frame to say the same thing. See
        // `HudLabel` for the measurement that made this a house rule.
        //
        // The headline and the per-kind rows come off the SAME key -- the
        // camp's stores, its ceiling and its orders -- because they are two
        // renderings of one thing and a key each is a way for them to disagree.
        long headKey = long.MinValue;
        string headline = "";
        int storeRows;
        string[] storeRes = new string[0];
        string[] storeNames = new string[0];
        string[] storeCounts = new string[0];

        /// The rate column, keyed SEPARATELY from `storeRows` above: a rate is
        /// a property of the camp's ORDERS, and the rows are a property of its
        /// PILES. Folding them into one key would rebuild every rate string
        /// each time a hand drops a log, which is every tick, for a number
        /// that only moves when an order changes.
        string[] storeRates = new string[0];
        int rateFrame = -1;
        long ratesKey = long.MinValue;

        /// The target line under the store rows — read once a frame, since
        /// `TargetLine` caches internally but the call still walks outposts.
        int targetFrame = -1;
        string targetLine = "";

        /// The loading line, which moves while the crew carry.
        readonly HudLabel loadingText = new HudLabel();
        readonly HudLabel roomText = new HudLabel();
        readonly HudLabel refusalText = new HudLabel();

        // The view readout, asked for once a frame rather than once an event.
        // `IslandCam.Readout` formats a fresh string every time it is read, so
        // the cheapest thing this sheet can do about it is read it less often.
        int viewFrame = -1;
        string viewLine = "";

        /// The one line on the bar built out of a constant. It was
        /// interpolated in the draw, which is once per event for a sentence
        /// that can never change.
        static readonly string MakeCampLine =
            $"a fire where you want it — {BuildPlans.Campfire.cost} logs, "
            + "cut by whoever you leave";

        /// `CatchUp` is idempotent within a frame -- the tick advances on a
        /// grid of game time -- but it still walks the scene looking for a
        /// blueprint and a pile every time it is asked. Once a frame is all
        /// the honesty this needs.
        int caughtUpFrame = -1;

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
            voyage = Object.FindFirstObjectByType<VoyageManager>();
            // The hold is the ship's, and this sheet lives on her: the stack
            // at the stern has to grow as the pile by the fire comes down, or
            // the goods evaporate into a number half-way down the beach.
            hold = anchor != null ? anchor.GetComponent<ShipHold>() : null;
            if (hold == null) hold = Object.FindFirstObjectByType<ShipHold>();
            yard = Object.FindFirstObjectByType<Shipyard>();

            // Siting mode rides along with the sheet that starts it rather
            // than being a second thing to place in `Sea.unity`. The scene is
            // carrying other uncommitted work and every object added to it is
            // a merge nobody wants; this needs no serialised state.
            if (CampSiting.Instance == null) gameObject.AddComponent<CampSiting>();
            // The crew list rides along for the same reason: no serialised
            // state, and `Sea.unity` is carrying other people's uncommitted
            // work.
            if (GetComponent<CampCrewList>() == null) gameObject.AddComponent<CampCrewList>();
            // And the loader, which is only somewhere for the carrying
            // coroutine to live. Same rule again.
            if (GetComponent<CampLoading>() == null) gameObject.AddComponent<CampLoading>();
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

            if (isle != shownFor) { shownFor = isle; Expanded = false; showBlueprint = false; }
            // A tap on the fire: open the sheet straight onto the build list.
            if (BuildMenuRequest.Consume(out var tappedCamp, out var tapped)
                && isle != null && tappedCamp == Outpost.Of(isle))
            {
                Expanded = true;
                showBlueprint = false;
                var list = GetComponent<CampCrewList>();
                if (list != null && tapped != null && tapped.Id == BuildPlans.Campfire.id) list.OpenBuild();
            }
            // **A tap on the DRAWING.** Kevin, 2026-09-21: *"the blueprint
            // should be pressable where it states how many resources it
            // still needs and the option to cancel the build / move it."*
            if (BuildMenuRequest.ConsumeBlueprint(out var bpCamp)
                && isle != null && bpCamp == Outpost.Of(isle))
            {
                Expanded = true;
                showBlueprint = true;
                var list = GetComponent<CampCrewList>();
                if (list != null) list.CloseBuild();
            }
            // Nothing pending: there is no drawing to be a panel about.
            if (showBlueprint)
            {
                var camp = isle != null ? Outpost.Of(isle) : null;
                if (camp == null || camp.Ledger == null || camp.Ledger.pending == null)
                    showBlueprint = false;
            }
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
            ReturnSummary.Draw(outpost, TimeOfDay.Seconds);   // "while you were gone" card, on arrival

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

            // Everything the bar and the rows say about the stores, rebuilt
            // only when the stores have moved. Before the buttons, because
            // three of them read it.
            RefreshText(isle, outpost);

            // The fold, furthest right where a thumb finds it.
            if (hasLists)
            {
                var tog = new Rect(right - toggleW, btnY, toggleW, btnH);
                UIBlocker.Block(tog);
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
                UIBlocker.Block(stop);
                if (GUI.Button(stop, "✕   Never mind", UITheme.Button)) CampSiting.End();
                right = stop.x - HudLayout.Gap;
                string refusal = CampSiting.Refusal;
                if (string.IsNullOrEmpty(refusal))
                    say = CampSiting.PlacingPier
                        ? "tap the beach inside the ring   ·   the pier runs out to deep water"
                        : "tap the ground inside the ring   ·   R turns it 45°";
                else
                {
                    // The refusal changes as the cursor moves, which is a
                    // reason to rebuild it when it changes and not a reason to
                    // rebuild it on every mouse-move EVENT.
                    if (refusalText.Changed(refusal.GetHashCode()))
                        refusalText.Set("✕ " + refusal);
                    say = refusalText.Content.text;
                }
            }
            else if (!outpost.HasCamp && !outpost.Building)
            {
                var btn = new Rect(right - actionW, btnY, actionW, btnH);
                UIBlocker.Block(btn);
                if (GUI.Button(btn, "🔥   Make camp", UITheme.Button))
                    CampSiting.Begin(outpost, BuildPlans.Campfire,
                        motor != null ? motor.transform : null);
                right = btn.x - HudLayout.Gap;
                say = MakeCampLine;
            }
            // **Loading, which is the fourth step of the loop and had no button
            // at all until 2026-09-20.** It comes last in the chain on purpose:
            // siting and making camp are how a place becomes loadable, so a
            // slot they want is a slot that is not about carrying anything yet.
            else if (CampLoading.LoadingFrom(outpost))
            {
                var stop = new Rect(right - actionW, btnY, actionW, btnH);
                UIBlocker.Block(stop);
                if (GUI.Button(stop, "✕   Stop", UITheme.Button)) CampLoading.Cancel();
                right = stop.x - HudLayout.Gap;
                say = loadingText.Content.text;
            }
            else if (outpost.HasCamp && outpost.Ledger != null && outpost.Ledger.Total > 0)
            {
                int room = CampLoading.RoomAboard(voyage);
                if (room > 0)
                {
                    var btn = new Rect(right - actionW, btnY, actionW, btnH);
                    UIBlocker.Block(btn);
                    if (GUI.Button(btn, "⬆   Load", UITheme.Button))
                        CampLoading.Begin(outpost, voyage, hold);
                    right = btn.x - HudLayout.Gap;
                    say = roomText.Content.text;
                }
                // No room, so no button -- and the reason matters, because one
                // of the two is a decision the player can take and the other is
                // the end of the voyage. The deck-cargo toggle lives one row up
                // in the prompt stack (`AnchorController.DrawDeckCargoToggle`),
                // which is where this line is pointing.
                else if (voyage != null && !voyage.TakeDeckCargo)
                    say = "the hold is at her line — deck cargo takes more";
                else say = "she is stuffed — nothing more will fit aboard";
            }

            // What this place is; and under it (or instead of it, on a phone's
            // single text line) what it is asking.
            float textW = wide ? Mathf.Max(0f, right - inner.x) : inner.width;
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

            var l = outpost.Ledger;
            float rowH = HudLayout.Unit * 2.2f;
            float storeH = HudLayout.Unit * 1.9f;

            // **What she is standing over, kind by kind, with a way to take
            // just that one.** Above the crew columns because it is the
            // question you came back to answer: the ⬆ Load in the bar takes
            // the lot best-first, and this is how you take the tools and leave
            // the firewood.
            //
            // It yields to the crew, not the other way round: the lists are
            // what the sheet is FOR, so at least three names stay visible and
            // the rows take whatever is over. A phone gives up the view
            // readout for them instead — that line is a dev instrument and
            // this is the feature.
            if (viewFrame != Time.frameCount)
            {
                viewFrame = Time.frameCount;
                string now = anchor != null ? anchor.ViewReadout : null;
                viewLine = string.IsNullOrEmpty(now) ? ""
                    : now + "   ·   drag the land, wheel zooms, right-drag turns, End recentres";
            }
            string view = viewLine;
            bool showView = !string.IsNullOrEmpty(view) && (wide || storeRows == 0);
            float top = y + (showView ? lineH : 0f);
            float keepBelow = lineH + 3f * (rowH + 2f);      // headers + three names
            if (outpost.HasCamp) keepBelow += lineH;         // the target line, drawn after the stores
            int rows = Mathf.Clamp(
                Mathf.FloorToInt((inner.yMax - top - keepBelow) / storeH), 0, storeRows);

            // What the view is doing, in the unit the dock shot is authored in
            // (165 m), so what is on screen can be compared with the authored
            // number rather than guessed at. Per the project's own rule: for a
            // look call, draw the numbers on the picture.
            if (showView)
            {
                GUI.Label(new Rect(inner.x, y, inner.width, lineH), view, body);
                y += lineH;
            }

            // **The blueprint's own panel**, in the sheet's open area and in
            // place of the store rows: they answer different questions and a
            // sheet that tries to answer both at once is the menu-of-
            // everything this pair of panels exists to avoid.
            if (showBlueprint && l != null && l.pending != null)
            {
                rows = 0;
                y = BlueprintPanel(outpost, l, inner, y, lineH, btnH);
                UITheme.Rect(new Rect(inner.x, y + HudLayout.Gap * 0.5f, inner.width, 1f),
                    UITheme.Track);
                y += HudLayout.Gap;
            }

            if (rows > 0)
            {
                RefreshRates(l);

                float loadW = Mathf.Min(HudLayout.Unit * 6f, inner.width * 0.3f);
                float rateW = HudLayout.Unit * 5f;
                float remaining = inner.width - loadW - rateW - HudLayout.Gap;
                float nameW = remaining * 0.62f;
                float countW = remaining - nameW;
                bool canTake = CampLoading.RoomAboard(voyage) > 0 && !CampLoading.Busy;
                for (int k = 0; k < rows; k++)
                {
                    GUI.Label(new Rect(inner.x, y, nameW, storeH), storeNames[k], body);
                    GUI.Label(new Rect(inner.x + nameW, y, countW, storeH), storeCounts[k], body);
                    if (k < storeRates.Length && !string.IsNullOrEmpty(storeRates[k]))
                        GUI.Label(new Rect(inner.x + nameW + countW, y, rateW, storeH), storeRates[k], rate);
                    var lr = new Rect(inner.xMax - loadW, y + 1f, loadW, storeH - 2f);
                    UIBlocker.Block(lr);
                    GUI.enabled = canTake;
                    if (GUI.Button(lr, "load", row))
                        CampLoading.BeginOne(outpost, voyage, hold, storeRes[k]);
                    GUI.enabled = true;
                    y += storeH;
                }
                UITheme.Rect(new Rect(inner.x, y + HudLayout.Gap * 0.5f, inner.width, 1f),
                    UITheme.Track);
                y += HudLayout.Gap;
            }

            // **The target line.** Not the piles — where the voyage is
            // pointed. A camp that is only sited has no economy to aim, so it
            // says nothing until there is a fire.
            if (outpost.HasCamp)
            {
                if (targetFrame != Time.frameCount)
                {
                    targetFrame = Time.frameCount;
                    targetLine = TargetLine.ForSheet(voyage, yard);
                }
                if (!string.IsNullOrEmpty(targetLine))
                {
                    GUI.Label(new Rect(inner.x, y, inner.width, lineH), targetLine, body);
                    y += lineH;
                }
            }

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
                    UIBlocker.Block(r);
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
            AshoreLabels(l);
            // **By index, not by enumerator.** `Recall` takes the hand OUT
            // of `l.hands`, and a list modified inside its own `foreach`
            // throws -- which is exactly what the console caught twice
            // (`InvalidOperationException` out of this loop) the last time
            // somebody tapped a name in the ashore column.
            for (int k = 0; k < l.hands.Count; k++)
            {
                var ashore = l.hands[k];
                if (ashore == null) continue;
                if (j >= perCol) break;
                var r = new Rect(inner.x + colW + colGap, listTop + j * (rowH + 2f), colW, rowH);
                UIBlocker.Block(r);
                if (GUI.Button(r, ashoreNames[j], row))
                {
                    var him = Find(parked, ashore.name);
                    if (him != null && anchor != null
                        && outpost.Recall(him, anchor.transform))
                    {
                        if (roster != null) roster.Refresh();
                        // The list under this loop just got shorter; the
                        // rest of the column is redrawn next event.
                        break;
                    }
                }
                j++;
            }

            // **What the beds are doing, under the two columns.** The camp
            // recruits into the ashore column on its own now (see
            // `OutpostLedger.RecruitLine`), and a hand appearing out of
            // nowhere with nothing to explain it is a bug the player reports.
            // The overflow ellipsis shares the line: both are about the list
            // above them, and a sheet on a phone has one line to spare.
            bool over = l.hands.Count > perCol || (roster != null && i >= perCol);
            string foot = RecruitFoot(l, over);
            if (!string.IsNullOrEmpty(foot))
                GUI.Label(new Rect(inner.x, inner.yMax - lineH, inner.width, lineH), foot, body);
        }

        // --- the blueprint panel ---------------------------------------------

        /// Open, because the player tapped the drawing. Cleared when the row
        /// it is about is gone (raised, cancelled, or a different island).
        bool showBlueprint;
        readonly HudLabel blueprintTitle = new HudLabel();
        readonly HudLabel blueprintNeeds = new HudLabel();
        readonly HudLabel blueprintHands = new HudLabel();

        /// What the drawing still wants, who is at it, and the two things the
        /// player can do about it. Returns the y it finished at.
        float BlueprintPanel(Outpost outpost, OutpostLedger l, Rect inner,
            float y, float lineH, float btnH)
        {
            var p = l.pending;
            var plan = BuildPlans.Named(p.planId).WithLength(p.length);

            if (blueprintTitle.Changed(p.planId != null ? p.planId.GetHashCode() : 0))
                blueprintTitle.Set(plan.label + "   ·   blueprint");

            int timber = Mathf.Max(0, p.needed - p.done);
            int stone = Mathf.Max(0, p.stoneNeeded - p.stoneDone);
            if (blueprintNeeds.Changed(HudLabel.Key(timber, stone)))
                blueprintNeeds.Set(
                    timber == 0 && stone == 0 ? "everything it wants is here"
                    : stone == 0 ? $"needs {timber} more timber"
                    : timber == 0 ? $"needs {stone} more stone"
                    : $"needs {timber} more timber, {stone} more stone");

            int builders = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Build) builders++;
            if (blueprintHands.Changed(builders))
                blueprintHands.Set(builders == 0 ? "nobody is building it"
                    : builders == 1 ? "1 hand building" : builders + " hands building");

            GUI.Label(new Rect(inner.x, y, inner.width, lineH), blueprintTitle.Content, title);
            y += lineH;
            GUI.Label(new Rect(inner.x, y, inner.width, lineH), blueprintNeeds.Content, body);
            y += lineH;
            GUI.Label(new Rect(inner.x, y, inner.width, lineH), blueprintHands.Content, body);
            y += lineH;

            float bw = Mathf.Min(HudLayout.Unit * 9f, (inner.width - HudLayout.Gap) * 0.5f);
            var cancel = new Rect(inner.x, y, bw, btnH);
            var move = new Rect(inner.x + bw + HudLayout.Gap, y, bw, btnH);
            UIBlocker.Block(cancel);
            UIBlocker.Block(move);
            if (GUI.Button(cancel, "✕   Cancel", UITheme.Button))
            {
                // The wood and stone already carried here go back on the
                // pile; whoever was building goes idle by the fire.
                outpost.CancelPending();
                showBlueprint = false;
            }
            // **Move keeps what has been paid.** The drawing stays where it
            // is until the new spot is tapped, so escaping the move leaves
            // the camp exactly as it was.
            else if (GUI.Button(move, "✥   Move", UITheme.Button))
            {
                CampSiting.Begin(outpost, plan,
                    motor != null ? motor.transform : null, movePending: true);
                showBlueprint = false;
            }
            return y + btnH;
        }

        // The ashore column, labelled once per change rather than once per
        // event: `OutpostHand.name` plus a tag is a fresh string every read.
        string[] ashoreNames = new string[0];
        long ashoreKey = long.MinValue;

        void AshoreLabels(OutpostLedger l)
        {
            long k = 17;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                k = k * 31 + (h.name != null ? h.name.GetHashCode() : 0);
                k = k * 31 + (h.born ? 1 : 0);
            }
            if (k == ashoreKey && ashoreNames.Length >= l.hands.Count) return;
            ashoreKey = k;
            if (ashoreNames.Length < l.hands.Count) ashoreNames = new string[l.hands.Count];
            int n = 0;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                // **Who came out of a hut rather than off the ship.** Small,
                // because it stops being news the moment you have looked at
                // it -- but it is the only thing on screen that says the camp
                // grew while you were away.
                ashoreNames[n++] = h.born ? h.name + " ·new" : h.name;
            }
        }

        string recruitFoot = "";
        string recruitFrom = "";
        bool recruitOver;

        int recruitFrame = -1;

        string RecruitFoot(OutpostLedger l, bool overflow)
        {
            // `RecruitLine` interpolates a fresh string every read, so it is
            // read once a FRAME, not once an event -- the same trick the view
            // readout above plays for the same reason.
            if (recruitFrame == Time.frameCount && overflow == recruitOver) return recruitFoot;
            recruitFrame = Time.frameCount;
            string line = l.RecruitLine;
            if (line == recruitFrom && overflow == recruitOver) return recruitFoot;
            recruitFrom = line;
            recruitOver = overflow;
            recruitFoot = string.IsNullOrEmpty(line) ? (overflow ? "…" : "")
                : (overflow ? "…  " + line : line);
            return recruitFoot;
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

        /// **Every string this sheet draws, built only when it changes.**
        ///
        /// `OnGUI` runs once per EVENT — Layout, Repaint, and one more for
        /// every mouse move — so the headline was being interpolated three or
        /// four times a frame to produce the same sentence, and the per-kind
        /// rows would have been four more. The key is what the strings SAY:
        /// which camp, what it holds, what it can hold, and who is doing what.
        ///
        /// The loading line is keyed separately because it moves on its own
        /// clock while the crew carry, and the stores it reads are moving with
        /// it — one key for both would rebuild everything every 0.15 s.
        void RefreshText(Island isle, Outpost outpost)
        {
            // Settle the books before reading them, ONCE a frame. A stale
            // number is worse than none; four identical ticks are worse than
            // one, and the second one in a frame does nothing anyway.
            if (outpost != null && caughtUpFrame != Time.frameCount)
            {
                caughtUpFrame = Time.frameCount;
                outpost.CatchUp();
            }

            var l = outpost != null ? outpost.Ledger : null;
            long key = HeadKey(isle, outpost, l);
            if (key != headKey)
            {
                headKey = key;
                BuildHeadline(isle, outpost, l);
                BuildRows(l);
            }

            // The bar's own line while a load is running, and the one that
            // offers it. Both are read out of `HudLabel.Content.text`, so the
            // string handed to `GUI.Label` is the same instance every event —
            // which is what stops IMGUI regenerating its text mesh.
            if (CampLoading.LoadingFrom(outpost))
            {
                string what = CampLoading.Loading;
                int room = CampLoading.RoomAboard(voyage);
                if (loadingText.Changed(HudLabel.Key(CampLoading.Moved, room,
                        what != null ? what.GetHashCode() : 0)))
                    loadingText.Set($"loading — {CampLoading.Moved} aboard, "
                        + $"room for {room}   ·   {CampLoading.Lower(what)}");
            }
            else if (voyage != null)
            {
                int room = CampLoading.RoomAboard(voyage);
                if (roomText.Changed(HudLabel.Key(room, voyage.TotalHeld,
                        voyage.TakeDeckCargo ? 1 : 0)))
                    roomText.Set($"{voyage.TotalHeld} aboard, room for {room}"
                        + "   ·   ⬆ takes the lot, best first");
            }
        }

        /// What the player sailed back to read.
        ///
        /// **It lists every kind now.** It reported timber and only timber,
        /// which was the whole truth while an island had nothing else — and
        /// was actively misleading from the day a sawmill could turn that
        /// timber into boards and a seam could give up ore: a camp holding
        /// three things said it was holding one. `CampLoading.Summary` builds
        /// the list, so the bar, the piles and the loader all name a resource
        /// the same way.
        void BuildHeadline(Island isle, Outpost outpost, OutpostLedger l)
        {
            sb.Clear();
            sb.Append(isle.name);

            // A blueprint reports what it is waiting for, in logs, because
            // logs are what the player has to do something about. "Building"
            // on its own would be a progress bar with no verb attached.
            if (outpost != null && outpost.Building)
            {
                var p = l.pending;
                int building = l.HandsOn(OutpostOrder.Build);
                if (p.stoneNeeded > 0)
                {
                    // **Two prices, two counters, 2026-09-21.** A building
                    // costs logs AND stone now, and one blended bar would
                    // hide the only thing the player can act on: WHICH of
                    // the two the site is short of. So the line names the
                    // price once and then reports each part against it --
                    // "hut · 5 timber 2 stone · 3/5 · 0/2".
                    var plan = BuildPlans.Named(p.planId);
                    sb.Append("   ·   ")
                      .Append(string.IsNullOrEmpty(plan.label) ? "building" : plan.label)
                      .Append("   ·   ").Append(p.needed).Append(" timber ")
                      .Append(p.stoneNeeded).Append(" stone")
                      .Append("   ·   ").Append(p.done).Append("/").Append(p.needed)
                      .Append("   ·   ").Append(p.stoneDone).Append("/").Append(p.stoneNeeded);
                }
                else
                {
                    sb.Append("   ·   camp sited   ·   ").Append(p.done).Append(" / ")
                      .Append(p.needed).Append(" logs");
                }
                sb.Append("   ·   ").Append(building)
                  .Append(building == 1 ? " hand building" : " hands building");
                if (building == 0) sb.Append("   ·   nobody is building it");
                // Which pile is empty, not just that one is. A blueprint
                // stalled on stone will never move however much wood the
                // player cuts, and the old single message sent them to do
                // exactly that.
                else if (l.StoneStarved)
                    sb.Append("   ·   NO STONE LEFT — nothing piled, nothing standing");
                else if (l.TimberStarved)
                    sb.Append("   ·   NO TIMBER LEFT — nothing piled, nothing standing");
                headline = sb.ToString();
                return;
            }

            if (outpost != null && outpost.HasCamp && l != null)
            {
                sb.Append("   ·   ").Append(CampLoading.Summary(l));
                int cutting = l.HandsOn(OutpostOrder.Gather);
                sb.Append("   ·   ").Append(cutting)
                  .Append(cutting == 1 ? " hand cutting" : " hands cutting");
                if (l.Wood.standing < 1f) sb.Append("   ·   the wood is cut out");
                else if (l.Timber >= l.ceilingPer) sb.Append("   ·   the pile is full");
            }
            else sb.Append("   ·   no camp");
            headline = sb.ToString();
        }

        /// One row per kind the camp is actually holding. A kind that has been
        /// carried away entirely drops off the list, exactly as its pile
        /// beside the fire does.
        void BuildRows(OutpostLedger l)
        {
            storeRows = 0;
            if (l == null) return;
            if (storeRes.Length < l.stores.Count)
            {
                storeRes = new string[l.stores.Count];
                storeNames = new string[l.stores.Count];
                storeCounts = new string[l.stores.Count];
                storeRates = new string[l.stores.Count];
            }
            foreach (var s in l.stores)
            {
                if (s == null || s.whole <= 0 || string.IsNullOrEmpty(s.resource)) continue;
                storeRes[storeRows] = s.resource;
                storeNames[storeRows] = CampLoading.Lower(s.resource);
                storeCounts[storeRows] = s.whole + " / " + l.ceilingPer;
                storeRows++;
            }
        }

        /// The "+4/day" column, rebuilt at most once a frame and only when the
        /// RATES themselves move. Keyed apart from `headKey`/`BuildRows`
        /// above on purpose: a pile ticking up or down is not an order
        /// changing, and this column is about orders.
        void RefreshRates(OutpostLedger l)
        {
            if (rateFrame == Time.frameCount) return;
            rateFrame = Time.frameCount;

            if (l == null || storeRows == 0) { ratesKey = long.MinValue; return; }
            if (storeRates.Length < storeRes.Length) storeRates = new string[storeRes.Length];

            // Rounded to one decimal (×10) before it folds into the key, so a
            // sub-decimal twitch that never reaches the printed digit does
            // not cost a rebuild — the same idea as `HudLabel.Key`.
            long key = 0L;
            for (int k = 0; k < storeRows; k++)
            {
                int r = Mathf.RoundToInt(l.RatePerDay(storeRes[k]) * 10f);
                key ^= (long)(r + k) * 2654435761L ^ ((long)k * 83492791L);
            }
            if (key == ratesKey) return;
            ratesKey = key;

            for (int k = 0; k < storeRows; k++)
            {
                float perDay = l.RatePerDay(storeRes[k]);
                if (Mathf.Abs(perDay) < 0.05f) { storeRates[k] = ""; continue; }
                string sign = perDay > 0f ? "+" : "−";
                storeRates[k] = sign + Mathf.Abs(perDay).ToString("0.#") + "/day";
            }
        }

        // The island's name, hashed once. **`Object.name` allocates a fresh
        // string on every read** — it marshals out of native — so hashing it
        // per event would have made this key the thing it was written to
        // avoid. It cannot change without the island changing.
        Island keyedIsle;
        int isleKey;

        /// Everything the headline and the rows can say, in one number.
        long HeadKey(Island isle, Outpost outpost, OutpostLedger l)
        {
            if (isle != keyedIsle)
            {
                keyedIsle = isle;
                isleKey = isle != null ? isle.name.GetHashCode() : 0;
            }
            long k = isleKey;
            if (outpost == null || l == null) return k;
            k = k * 31 + (outpost.HasCamp ? 1 : 0);
            k = k * 31 + (outpost.Building ? 1 : 0);
            if (l.pending != null)
                k = k * 31 + l.pending.done * 397 + l.pending.needed
                           + l.pending.stoneDone * 1063 + l.pending.stoneNeeded * 7;
            // Whole units and the ceiling — what the line prints. The sub-unit
            // accrual moves every tick and changes nothing anybody can read.
            k = k * 31 + CampLoading.CountsKey(l);
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                k = k * 31 + (int)h.order;
                if (!string.IsNullOrEmpty(h.target)) k = k * 31 + h.target.GetHashCode();
            }
            // The two tails the headline adds, as the booleans they are drawn
            // from rather than the floats underneath them.
            k = k * 31 + (l.Wood.standing < 1f ? 1 : 0);
            k = k * 31 + (l.StoneStarved ? 2 : 0) + (l.TimberStarved ? 1 : 0);
            return k;
        }

        /// The headline as it is drawn, for a probe that has to check the bar
        /// agrees with the ledger. Cached: calling it in a loop costs nothing
        /// while nothing has moved, which is the property the gate measures.
        public string HeadlineFor(Island isle, Outpost outpost)
        {
            RefreshText(isle, outpost);
            return headline;
        }

        /// How many per-kind rows the open sheet has to offer, and what they
        /// say. For the same probe.
        public int StoreRowCount => storeRows;
        public string StoreRowText(int i) =>
            i >= 0 && i < storeRows ? storeNames[i] + "  " + storeCounts[i] : "";

        void EnsureStyles()
        {
            if (title != null) return;
            title = new GUIStyle(UITheme.Small2Centered) { alignment = TextAnchor.MiddleLeft };
            body = new GUIStyle(UITheme.Small2Centered) { alignment = TextAnchor.MiddleLeft };
            row = new GUIStyle(UITheme.Button);
            rate = new GUIStyle(body) { alignment = TextAnchor.MiddleRight };
        }
    }
}
