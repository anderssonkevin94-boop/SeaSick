using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.UI
{
    /// **Siting mode: pick the spot.**
    ///
    /// Kevin's flow, 2026-09-19: anchored at an island, you choose to make
    /// camp, and instead of a camp appearing you get a blueprint on the end of
    /// your thumb. You put it where you like, it stays there as a drawing,
    /// and the hands you leave behind walk up from the landing and build it.
    /// Kevin, 2026-09-23: the town centre (the fire) goes ANYWHERE the
    /// ground takes it -- no ring round the ship, no pre-made clearing.
    /// Kevin, 2026-09-27: *"I want to do away with the 40 m building radius
    /// for the campfire... The whole island should be built if you want it
    /// to."* So does everything else now -- anywhere `Outpost.CanPlace`
    /// and `Outpost.TooFarFromTown` (reachability only, since this decision)
    /// take it. (The walk label that briefly replaced the ring -- "84 m from
    /// stores, ~70 s round trip" -- was dropped the same day: *"drop that
    /// information. I don't need to know to the second how long it takes."*)
    ///
    /// This is the interface half of that. It owns exactly three things — the
    /// preview ghost, the warmth line, and the tap — and it owns no rules:
    /// whether a spot will take a building is `Outpost.CanPlace`, which is
    /// the same call the real raise makes. **A ghost that goes green on one
    /// test and a raise that refuses on another is the bug this shape exists
    /// to prevent**, and the way to keep it prevented is that there is only
    /// one test.
    ///
    /// A MODE, like the sheet it is driven from: while it is running the
    /// island view still pans and zooms under it, because choosing where to
    /// put a camp is mostly looking at the island.
    public class CampSiting : MonoBehaviour
    {
        /// **How much ground the ARRIVAL shot frames round the ship, metres.**
        ///
        /// Until 2026-09-23 this was also a siting rule (the first campfire
        /// had to go inside it). Kevin: *"i want you to be able to choose
        /// where you want on the island to build your town center"* -- so it
        /// refuses nothing now. `AnchorController` still frames
        /// `RingCentre`/`RingRadius` on arrival, which with no camp is this
        /// much ground round the landing. There is no siting rule about
        /// distance any more (2026-09-27) -- see `Outpost.TooFarFromTown`.
        public static float SiteRadius = 80f;

        public static CampSiting Instance { get; private set; }

        /// Is the player placing something right now?
        public static bool Placing => Instance != null && Instance.plan.id != null;

        /// ...and is it a pier, which the sheet explains differently: there is
        /// no R to turn it, and the thing to tap is the beach.
        public static bool PlacingPier => Placing && Instance.IsPier;

        /// ...or a dry dock, sited the same way (no R, tap the shore) but
        /// only within reach of the home berth -- see `Outpost.SnapDryDock`.
        public static bool PlacingDryDock => Placing && Instance.IsDryDock;

        /// **...or is it a WALL, which is not one blueprint at all.**
        ///
        /// Kevin's connect-the-dots design (`docs/PLAN-fortress-harbour.md`
        /// D5) makes a palisade a RUN of segments the player walks round the
        /// camp, so it gets its own tool — `WallSiting` — rather than a
        /// fourth shape of "where does this rectangle go". This mode stays
        /// `Placing` for its whole life, though, so every guard elsewhere
        /// that asks whether the player is placing something (the Hand's
        /// pick-up, the tap's consequence, the camp bar) goes on getting the
        /// right answer without knowing walls exist.
        public static bool PlacingWall => Placing && Instance.wallMode;

        /// **The plan id that arms the wall tool.** The World half's
        /// `BuildPlans.Palisade.id`, named here rather than reached for, so
        /// this file compiles against the contract and not against the order
        /// the two halves happen to land in.
        public const string WallPlanId = "palisade";

        /// **Metres past the furthest wall post (or the island's own
        /// shore) the camera's own reach stands, 2026-09-27.** Slack for a
        /// tower snapping past the very end of a run (`WallTowerSnap`), and
        /// for the ghost's own footprint, so the camera does not stop
        /// exactly at an edge with the drawing hanging half off screen.
        public const float WallReachMargin = 20f;

        /// **...or a LADDER chain (2026-09-27)**: two points, its own tool
        /// (`LadderSiting`), forked exactly where the wall is.
        public const string LadderPlanId = "ladder";
        public static bool PlacingLadder => Placing && Instance.ladderMode;
        bool ladderMode;

        /// **...or a ROAD (2026-09-27)**: tap to tap, its own tool
        /// (`RoadSiting`), forked exactly where the wall is.
        public const string RoadPlanId = "road";
        bool roadMode;

        /// Why the spot under the pointer is refused, or "" if it is good.
        /// The sheet prints this; the ghost's colour says the same thing
        /// faster.
        public static string Refusal { get; private set; } = "";

        /// **"clears 4 trees, 1 rock" -- what a VALID spot costs in
        /// clearing (2026-09-23, the CLEAR-phase contract).** Printed where
        /// `Refusal` would otherwise be: the two never show at once, since
        /// this is only computed once the spot has already passed
        /// `Outpost.CanPlace`. Empty when the footprint stands clean.
        public static string ClearLine { get; private set; } = "";
        /// What `ClearLine` was last built from; -1 = nothing built.
        int clearTrees = -1, clearRocks = -1;
        bool clearOnWall;

        /// **"clears N trees, M rocks", singular/plural correct, or "" when
        /// nothing stands in the way.** Shared with `WallSiting`, which sums
        /// `Outpost.CountObstructionsWall` across a whole run before calling
        /// this once.
        public static string FormatClearLine(int trees, int rocks)
        {
            if (trees <= 0 && rocks <= 0) return "";
            var parts = new System.Collections.Generic.List<string>(2);
            if (trees > 0) parts.Add(trees == 1 ? "1 tree" : $"{trees} trees");
            if (rocks > 0) parts.Add(rocks == 1 ? "1 rock" : $"{rocks} rocks");
            return "clears " + string.Join(", ", parts);
        }

        BuildPlan plan;
        Outpost outpost;
        Transform ship;
        GameObject ghost;
        Vector3 at;
        bool valid;

        /// **Which way it faces, in eighths of a turn.**
        ///
        /// Kevin, 2026-09-19: *"with the buildings, i'd like an option to
        /// rotate them by 45 degrees in a complete rotation to get more
        /// freedom in how i build my settlement."* R turns it, shift+R turns
        /// it back, eight steps to the full circle.
        ///
        /// Automatic until it is touched — the door faces the middle of the
        /// camp, which is what the spiral did and what a building sited by
        /// hand should still do unless somebody says otherwise. The first
        /// press freezes that facing and turns from there. Same rule as
        /// `IslandCam.Driven`, and for the same reason: an untouched thing
        /// should be exactly what was composed for it.
        int turns;
        bool turned;
        float heldYaw;

        /// What the ghost is facing right now. A pier faces the sea and
        /// nothing the player does turns it -- see `Outpost.SnapPier`. A
        /// watchtower snapped onto the wall is the same idea (2026-09-27):
        /// it is part of the wall now, so it squares to the run rather than
        /// to whatever `R` last left it at -- see `Outpost.WallTowerYaw`.
        public float Yaw => (IsPier || IsDryDock) ? snappedYaw
            : onWall ? outpost.WallTowerYaw(at)
            : heldYaw + turns * TurnStep;

        bool IsPier => plan.kind == BuildKind.Pier;

        /// **A dry dock is sited by the shore too, and by the home berth.**
        /// `Outpost.SnapDryDock` walks to the waterline the same way
        /// `SnapPier` does, but refuses anything too far from `Dock.Home` --
        /// see `BuildPlans.DryDockMaxFromHome`.
        bool IsDryDock => plan.kind == BuildKind.DryDock;

        /// **A pier is sited by the beach, not by the thumb.** The pointer
        /// picks a stretch of shore; `Outpost.SnapPier` walks to the
        /// waterline, turns to face out and runs the planks out to deep
        /// water. What it chose -- the centre, the heading, the length --
        /// is held here and is what the ghost draws and the tap commits.
        float snappedYaw;
        /// The plan as it will be raised: `BuildPlans.Pier` at the length
        /// the beach asked for. Everything else uses `plan` unchanged.
        BuildPlan sited;
        /// Length the ghost was built at, so a pier that grows as the
        /// pointer moves along the beach gets a new ghost, not a stretched one.
        float ghostLength;
        Vector3 ghostAt;
        float ghostYaw;

        /// Eight steps to the circle.
        public const int Steps = 8;

        /// **One turn of the ↻ button, degrees.** Kevin asked for 45° steps;
        /// R on the keyboard and the button both step by this and nothing
        /// else names the number.
        public const float TurnStep = 360f / Steps;

        /// **Where the player last said "here".**
        ///
        /// Kevin, 2026-09-22: the drawing is no longer glued to the pointer
        /// and a tap no longer builds. A tap on open ground MOVES this point;
        /// everything else about the mode (the snap, the tests, the ghost) is
        /// recomputed from it every frame, so the camera can pan and zoom
        /// under a drawing that stays exactly where it was put.
        Vector3 want;

        void Awake()
        {
            Instance = this;
            anchor = GetComponent<SeaSick.Ship.AnchorController>();
        }

        void OnDestroy()
        {
            // A scene change, a save load or a ship swap takes this component
            // with it: the bottom bar (which outlives it) and the sub-tools'
            // statics (which outlive everything -- domain reload is off) must
            // not be left showing a placement nobody is running.
            if (barShown) { Sheets.ThumbBar.HidePlacement(); barShown = false; }
            WallSiting.End();
            RoadSiting.End();
            LadderSiting.End();
            if (Instance == this) Instance = null;
        }

        void OnEnable() { Sheets.Sheets.Changed += OnSheetChanged; }
        void OnDisable() { Sheets.Sheets.Changed -= OnSheetChanged; }

        /// **A sheet opening ends placement (2026-09-30).** The placement
        /// bar and a sheet both want the bottom of the screen and the
        /// player's attention; whatever opened the sheet is the newer
        /// decision. The frame siting began on is exempt: the build card
        /// that armed it closes its own sheet on that frame.
        void OnSheetChanged()
        {
            if (plan.id == null || !Sheets.Sheets.IsOpen) return;
            if (Time.frameCount <= beganFrame) return;
            Cancel();
        }

        // =================================================================
        // THE PLACEMENT BAR (2026-09-30, island UI restructure phase 1C)
        //
        // The ✕ ↻ ✓ discs under the ghost and the words under them are
        // gone -- Kevin's rule: no icons over world assets. Every placement
        // mode (a building, the first fire, a wall run, a road, a ladder)
        // drives the one bottom `ThumbBar` instead: title + hint on show,
        // the status line whenever the verdict CHANGES (never per frame),
        // Cancel always there -- which is also the phone's first way out of
        // a wall/road/ladder before its first point is down.
        // =================================================================

        SeaSick.Ship.AnchorController anchor;
        /// The island she was lying at when placing began. She leaving it
        /// (getting underway sets `CurrentIsland` null) ends placement.
        Island startIsland;

        bool barShown;
        /// Which step's hint the bar is showing (sub-tool state as an int,
        /// -1 for a building) -- so the hint is only pushed on a change.
        int barStep = int.MinValue;
        // The last status pushed, as its inputs, so it is only composed and
        // pushed when one of them changes.
        bool barValid;
        string barRefusal, barDetail, barWarmth;
        int barFetchT = -1, barFetchS = -1, barFetchB = -1;

        static readonly System.Action CancelAction = End;
        static readonly System.Action ConfirmAction = Confirm;
        static readonly System.Action TurnAction = Rotate;

        const string BuildingHint = "Drag it to move · tap the ground to jump there";
        const string ShoreHint = "Drag it along the shore · tap the beach to jump there";

        /// **The first fire on an island** -- the "Make camp" verb. A
        /// campfire that is not a MOVE of its own blueprint can only be that:
        /// a camp has one fire.
        bool MakingCamp => plan.id == BuildPlans.Campfire.id && !moving;

        void ShowBar()
        {
            string title, hint, confirm;
            System.Action turn = null;
            if (ladderMode) { title = "Place a ladder"; hint = LadderSiting.FootHint; confirm = "Build ladder"; }
            else if (roadMode) { title = "Lay a road"; hint = RoadSiting.PlantHint; confirm = "Build road"; }
            else if (wallMode) { title = "Build a wall"; hint = WallSiting.PlantHint; confirm = "Build wall"; }
            else if (MakingCamp) { title = "Make camp here"; hint = BuildingHint; confirm = "Light the fire"; turn = TurnAction; }
            else
            {
                string name = plan.id == BuildPlans.Campfire.id ? "campfire" : plan.label;
                title = (moving ? "Move the " : "Place the ") + name;
                bool shore = IsPier || IsDryDock;
                hint = shore ? ShoreHint : BuildingHint;
                confirm = moving ? "Move here" : "Build here";
                // A pier or a dry dock faces the sea whatever R says.
                if (!shore) turn = TurnAction;
            }
            Sheets.ThumbBar.ShowPlacement(title, hint, CancelAction, turn, ConfirmAction, confirm);
            barShown = true;
            barStep = ladderMode ? (int)LadderSiting.State.NoFoot
                    : roadMode ? (int)RoadSiting.State.NoPoint
                    : wallMode ? (int)WallSiting.State.NoPost : -1;
            // "" so a sub-tool's quiet first step pushes nothing more; the
            // null warmth forces a building's first verdict through.
            barRefusal = barDetail = "";
            barWarmth = null;
            barFetchT = barFetchS = barFetchB = -1;
            barValid = false;
            // Nothing has been judged yet: a quiet line, ✓ off until the
            // first verdict lands (the same frame for a building).
            Sheets.ThumbBar.SetPlacementStatus("", true, false);
        }

        void HideBar()
        {
            if (!barShown) return;
            barShown = false;
            Sheets.ThumbBar.HidePlacement();
        }

        /// A sub-tool's frame: its step's hint and its verdict, pushed only
        /// on a change.
        void SyncToolBar(int step, string stretchHint, bool stretching, bool ok,
            string refusal, string detail)
        {
            if (!barShown) return;
            if (step != barStep)
            {
                barStep = step;
                Sheets.ThumbBar.SetPlacementHint(stretching ? stretchHint
                    : ladderMode ? LadderSiting.FootHint
                    : roadMode ? RoadSiting.PlantHint : WallSiting.PlantHint);
            }
            if (!stretching)
            {
                // Nothing to judge before the first point is down.
                if (barRefusal == "" && barDetail == "" && !barValid) return;
                barValid = false; barRefusal = ""; barDetail = "";
                Sheets.ThumbBar.SetPlacementStatus("", true, false);
                return;
            }
            refusal ??= "";
            detail ??= "";
            if (barValid == ok && string.Equals(barRefusal, refusal) && string.Equals(barDetail, detail))
                return;
            barValid = ok; barRefusal = refusal; barDetail = detail;
            if (ok)
                Sheets.ThumbBar.SetPlacementStatus(
                    detail.Length > 0 ? "Good spot · " + detail : "Good spot", true, true);
            else
                Sheets.ThumbBar.SetPlacementStatus(Sentence(refusal), false, false);
        }

        /// A building's frame: "Good spot · clears 3 trees · hands fetch 6
        /// timber", or the refusal. Composed only when an input changed.
        void SyncBuildingBar()
        {
            if (!barShown) return;
            int fT = 0, fS = 0, fB = 0;
            if (valid) Shortfall(out fT, out fS, out fB);
            string refusal = valid ? "" : (Refusal ?? "");
            string warmth = valid ? WarmthLine : "";
            if (barValid == valid && string.Equals(barRefusal, refusal)
                && string.Equals(barDetail, ClearLine) && string.Equals(barWarmth, warmth)
                && barFetchT == fT && barFetchS == fS && barFetchB == fB)
                return;
            barValid = valid; barRefusal = refusal; barDetail = ClearLine; barWarmth = warmth;
            barFetchT = fT; barFetchS = fS; barFetchB = fB;

            if (!valid)
            {
                Sheets.ThumbBar.SetPlacementStatus(Sentence(refusal), false, false);
                return;
            }
            var sb = new System.Text.StringBuilder("Good spot");
            if (!string.IsNullOrEmpty(ClearLine)) sb.Append(" · ").Append(ClearLine);
            if (!string.IsNullOrEmpty(warmth)) sb.Append(" · ").Append(warmth);
            if (fT > 0 || fS > 0 || fB > 0)
            {
                sb.Append(" · hands fetch ");
                bool any = false;
                if (fT > 0) { sb.Append(fT).Append(' ').Append(ResWord(TimberRes)); any = true; }
                if (fS > 0) { if (any) sb.Append(", "); sb.Append(fS).Append(" stone"); any = true; }
                if (fB > 0) { if (any) sb.Append(", "); sb.Append(fB).Append(" brick"); }
            }
            Sheets.ThumbBar.SetPlacementStatus(sb.ToString(), true, true);
        }

        /// **What the builders will have to gather**: the price of this copy
        /// less what the camp can already spend, per material. Nothing for a
        /// move (already paid for) or the first fire (its logs come with the
        /// landing party, not out of a store that does not exist yet).
        void Shortfall(out int timber, out int stone, out int brick)
        {
            timber = stone = brick = 0;
            if (moving || MakingCamp || outpost == null) return;
            var l = outpost.Ledger;
            if (l == null) return;
            var priced = l.PriceOfNext(sited.id != null ? sited : plan);
            timber = Mathf.Max(0, priced.cost - l.SpendableOf(TimberRes));
            if (priced.stoneCost > 0) stone = Mathf.Max(0, priced.stoneCost - l.SpendableOf(Res.Stone));
            if (priced.brickCost > 0) brick = Mathf.Max(0, priced.brickCost - l.SpendableOf(Res.Brick));
        }

        string TimberRes => string.IsNullOrEmpty(plan.resource) ? Res.Timber : plan.resource;

        static string ResWord(string res) => res switch
        {
            Res.Timber => "timber",
            Res.Stone => "stone",
            Res.Brick => "brick",
            Res.Boards => "boards",
            _ => res.ToLowerInvariant(),
        };

        /// A refusal as the top card prints it: capitalised, never blank.
        static string Sentence(string why)
        {
            if (string.IsNullOrEmpty(why)) return "Can't build here";
            return char.IsLower(why[0]) ? char.ToUpperInvariant(why[0]) + why.Substring(1) : why;
        }

        /// **The frame placing began on.** Every tap whose press went down
        /// at or before it is somebody else's tap -- see `Update`.
        int beganFrame = -1;

        /// **Moving a blueprint that is already paid for**, rather than
        /// siting a new one. Kevin, 2026-09-21: the drawing's own panel
        /// offers *cancel* and *move*, and a move must not cost the wood
        /// again. The one flag the commit reads; everything else about the
        /// mode is identical.
        bool moving;

        /// **WHICH drawing is being carried (2026-09-22).** With a build
        /// queue "the pending one" stopped being an answer: the move begins
        /// from a `SiteSheet` that knows its own row, and `Outpost.Site`
        /// wants that row so it lifts the right one out of the queue.
        PendingBuild movingRow;

        /// **Thumb slack on the grab test, metres.**
        ///
        /// Kevin on the phone, 2026-09-23: *"when placing the blueprint I
        /// should be able to hold and drag it to move it around. now I can
        /// only click and press."* So a press that lands on the drawing
        /// carries it. "On the drawing" is measured on the GROUND — the
        /// ghost's footprint radius plus this — rather than against the
        /// ghost's colliders, because the ghost has none and because a thumb
        /// covers more ground than a small hut's roof.
        public static float GrabSlack = 1.2f;

        /// Is the drawing on the end of a finger right now? While this is
        /// true the press belongs to siting, not to the camera.
        bool dragging;

        /// Is this run a wall? Set once in `Begin` from the plan's id, and
        /// the only branch in this file: every wall-shaped question goes
        /// straight to `WallSiting` and every other line below is untouched.
        bool wallMode;

        /// Where the drawing sat relative to the ground point the finger went
        /// down on, in world XZ. Kept so a grab near the edge of the
        /// footprint does not snap the building's centre under the thumb.
        Vector2 grabOffset;

        /// Is the current placement a MOVE of the standing blueprint? The
        /// sheet says "never mind" differently for it: escaping a move
        /// leaves the drawing where it was.
        public static bool Moving => Placing && Instance.moving;

        /// Start placing. Harmless to call again while already placing.
        public static void Begin(Outpost target, BuildPlan what, Transform shipTransform)
            => Begin(target, what, shipTransform, false);

        public static void Begin(Outpost target, BuildPlan what, Transform shipTransform,
            bool movePending)
            => Begin(target, what, shipTransform,
                movePending && target != null && target.Ledger != null
                    ? target.Ledger.Pending : null);

        /// Start placing, moving `move` if it is a row already in the queue.
        public static void Begin(Outpost target, BuildPlan what, Transform shipTransform,
            PendingBuild move)
        {
            if (Instance == null || target == null) return;
            Instance.Cancel();
            Instance.outpost = target;
            Instance.plan = what;
            Instance.ship = shipTransform;
            Instance.turns = 0;
            Instance.turned = true;
            // The facing is fixed the moment siting begins: door toward the
            // camp from where the fire is, and from then on only R turns it
            // (Kevin, 2026-09-22: a drawing that swung round the fire as
            // the cursor moved was unwanted).
            Instance.heldYaw = Instance.outpost != null
                ? Instance.outpost.AutoYaw(Instance.outpost.CampCentre + Vector3.forward * 10f) : 0f;
            Instance.movingRow = move;
            Instance.moving = move != null;
            // **Widen the camera's own reach to the whole island (2026-09-27,
            // "the whole island should be built if you want it to").** The
            // ghost's reach test no longer refuses anywhere on the island
            // (`Outpost.TooFarFromTown` now only asks whether the ground is
            // walkable), but the camera the thumb points with has its own
            // clamp -- it was left wherever `DropTheViewOn` last put it, 35 m
            // above whatever was sited before, and a spot on the far shore is
            // simply off screen there. `IslandCam.ClampPivot` reads this for
            // exactly the life of this session; `Cancel` puts it back. Grown
            // from the wall's own reach too (2026-09-27, watchtowers-on-the-
            // wall), so a run that happens to run further than the shore's
            // measured radius is still on screen. Harmless (0 m) with no
            // camp centre or island yet.
            if (Instance.outpost != null && Instance.outpost.HasCampCentre)
            {
                CameraRig.IslandCam.ExtraReachCentre = Instance.outpost.CampCentre;
                float islandSpan = Instance.outpost.Island != null
                    ? Instance.outpost.Island.MaxRadius : 0f;
                CameraRig.IslandCam.ExtraReachRadius = Mathf.Max(
                    Instance.outpost.WallExtentFromCentre(), islandSpan) + WallReachMargin;
            }
            // The ghost must not be refused by the drawing it IS -- see
            // `Outpost.IgnoreSite`. Cleared in `Cancel`, which every exit
            // from this mode goes through.
            if (Instance.outpost != null) Instance.outpost.IgnoreSite = move;
            // A drawing being moved keeps the facing it had.
            if (move != null) Instance.heldYaw = move.yaw;
            // **The press that started this mode must not also finish it.**
            // See `IslandInput.TapDownFrame`: an IMGUI button is clicked in
            // `OnGUI`, after every `Update` of that frame, and the Input
            // System can report the very same release a frame later -- so
            // without this the release that opened siting mode was also the
            // tap that sited the building, under the button.
            Instance.beganFrame = Time.frameCount;
            Instance.startIsland = Instance.anchor != null ? Instance.anchor.CurrentIsland : null;

            // **The wall fork.** A palisade is a run of posts, not a
            // rectangle on the end of a thumb, so the ghost, the start
            // point and the yaw all belong to `WallSiting` from here.
            // Every fork shows the placement bar AT ONCE, so Cancel is on
            // screen before the first point is planted (the phone had no
            // way out of that step before 2026-09-30).
            Instance.ladderMode = what.id == LadderPlanId;
            if (Instance.ladderMode)
            {
                LadderSiting.Begin(target);
                Instance.ShowBar();
                return;
            }

            Instance.roadMode = what.id == RoadPlanId;
            if (Instance.roadMode)
            {
                RoadSiting.Begin(target);
                Instance.ShowBar();
                return;
            }

            Instance.wallMode = what.id == WallPlanId;
            if (Instance.wallMode)
            {
                WallSiting.Begin(target, what, shipTransform);
                Instance.ShowBar();
                return;
            }

            Instance.want = Instance.StartPoint();
            Instance.ShowBar();
        }

        /// **Where a fresh drawing appears on screen, as a fraction of the
        /// height down from the top (2026-09-30)**, when the placement card
        /// and bar have not been laid out yet (the frame placing begins on
        /// the bar is usually hidden under the build sheet). Screen centre
        /// sat too close to the bottom bar's reach on a portrait phone.
        public static float StartDownFraction = 0.4f;

        /// The screen point (Input-System space, origin bottom-left) a fresh
        /// drawing starts under: the middle of the free band between the top
        /// placement card and the bottom thumb bar when both are known, else
        /// `StartDownFraction` down the screen.
        static Vector2 StartScreenPoint()
        {
            float h = Screen.height;
            float guiY = h * StartDownFraction;
            var card = Sheets.ThumbBar.CardRect;
            var bar = Sheets.ThumbBar.Rect;
            if (card.height > 0f && bar.height > 0f && bar.yMin > card.yMax)
                guiY = 0.5f * (card.yMax + bar.yMin);
            return new Vector2(Screen.width * 0.5f, h - guiY);
        }

        /// **Where the drawing appears before anyone has moved it.**
        ///
        /// It has to appear somewhere: the thumb is not carrying it any more.
        /// A blueprint being moved starts on itself; anything else starts on
        /// the middle of the screen, which is what the player is looking
        /// at -- anywhere on the island is a fair start now (2026-09-27, no
        /// more ring to pull it back inside of).
        Vector3 StartPoint()
        {
            if (moving && movingRow != null)
                return OnGround(movingRow.x, movingRow.z);

            var cam = Camera.main;
            if (cam != null && GroundPick.FromScreen(cam,
                    StartScreenPoint(), out Vector3 mid))
                return mid;
            return Centre();
        }

        static Vector3 OnGround(float x, float z)
        {
            var h = GroundPick.Height;
            return new Vector3(x, h != null ? h(x, z) : 0f, z);
        }

        public static void End() { if (Instance != null) Instance.Cancel(); }

        // =================================================================
        // HOLD AND DRAG THE DRAWING
        //
        // `IslandInput` owns the devices; this owns the question "is that
        // press on the drawing?" and the answer "then here is where it went".
        // The decision is made ONCE, at press-down, and the touch is handed
        // over for its whole life — the same shape as `uiOwnedTouches` — so
        // the camera pan and the blueprint drag can never both be running.
        // =================================================================

        /// **Does a press here pick the drawing up?** Ground-picks the press
        /// point and measures the flat distance to the drawing. False when
        /// nothing is being sited, when the ray misses the ground, or when
        /// the press is out on the grass — in which case the press keeps
        /// doing what it did before (pan, or a tap that moves the ghost).
        public static bool GrabsGhost(Vector2 screen)
        {
            var s = Instance;
            if (s == null || s.plan.id == null) return false;
            // A press on the placement bar (or any claimed UI) is the UI's,
            // never a grab of the drawing behind it.
            if (UIBlocker.Blocked(screen)) return false;
            // A wall's grab target is its loose post, not a footprint —
            // same question, asked of the tool that owns the answer.
            if (s.wallMode) return WallSiting.GrabsPost(screen);
            if (s.ladderMode) return LadderSiting.GrabsPost(screen);
            if (s.roadMode) return RoadSiting.GrabsPoint(screen);
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return false;
            return s.NearGhost(g);
        }

        bool NearGhost(Vector3 g)
        {
            // The footprint as DRAWN: a pier's ghost is as long as the beach
            // asked for, which is not `plan.footprint`. Before the first
            // `Evaluate` there is no snapped plan yet, so fall back to the
            // plan itself.
            Vector2 f = sited.footprint.sqrMagnitude > 0.0001f
                ? sited.footprint : plan.footprint;
            float r = 0.5f * Mathf.Sqrt(f.x * f.x + f.y * f.y) + GrabSlack;
            // `want` is the point the player indicated; `at` is where the
            // ghost actually stands (the same point except for a snapped
            // pier). Either one being under the thumb is a grab.
            return Mathf.Min(Flat(g, want), Flat(g, at)) <= r;
        }

        static float Flat(Vector3 a, Vector3 b)
            => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        /// Take hold. Called at press-down, once, after `GrabsGhost` said yes.
        public static void BeginDrag(Vector2 screen)
        {
            var s = Instance;
            if (s == null || s.plan.id == null) return;
            if (s.wallMode) { s.dragging = true; WallSiting.BeginDrag(screen); return; }
            if (s.ladderMode) { s.dragging = true; LadderSiting.BeginDrag(screen); return; }
            if (s.roadMode) { s.dragging = true; RoadSiting.BeginDrag(screen); return; }
            s.dragging = true;
            s.grabOffset = Vector2.zero;
            if (GroundPick.FromScreen(Camera.main, screen, out Vector3 g))
                s.grabOffset = new Vector2(s.want.x - g.x, s.want.z - g.z);
        }

        /// Follow the finger. `Evaluate` here rather than only in `Update`
        /// so the ring and the colour are answering about the spot the
        /// drawing is on THIS frame, not the one it left.
        public static void DragTo(Vector2 screen)
        {
            var s = Instance;
            if (s == null || !s.dragging || s.plan.id == null) return;
            if (s.wallMode) { WallSiting.DragTo(screen); return; }
            if (s.ladderMode) { LadderSiting.DragTo(screen); return; }
            if (s.roadMode) { RoadSiting.DragTo(screen); return; }
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return;
            s.want = OnGround(g.x + s.grabOffset.x, g.z + s.grabOffset.y);
            s.Evaluate();
        }

        /// Let go. The drawing stays where it was dropped — dropping is not
        /// building, ✓ is still the only thing that builds.
        public static void EndDrag()
        {
            if (Instance == null) return;
            Instance.dragging = false;
            if (Instance.wallMode) WallSiting.EndDrag();
            if (Instance.ladderMode) LadderSiting.EndDrag();
            if (Instance.roadMode) RoadSiting.EndDrag();
        }

        /// Is the drawing on the end of a finger right now?
        public static bool Dragging => Placing && Instance.dragging;

        void Cancel()
        {
            HideBar();
            if (wallMode) { wallMode = false; WallSiting.End(); }
            if (ladderMode) { ladderMode = false; LadderSiting.End(); }
            if (roadMode) { roadMode = false; RoadSiting.End(); }
            plan = default;
            moving = false;
            dragging = false;
            movingRow = null;
            if (outpost != null) outpost.IgnoreSite = null;
            outpost = null;
            Refusal = "";
            ClearLine = "";
            clearTrees = clearRocks = -1;
            valid = false;
            onWall = false;
            CameraRig.IslandCam.ExtraReachCentre = null;
            CameraRig.IslandCam.ExtraReachRadius = 0f;
            if (ghost != null) { Destroy(ghost); ghost = null; }
        }

        void Update()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (plan.id == null) return;
            if (outpost == null || ship == null) { Cancel(); return; }
            // She has left the island placing began at (getting underway
            // clears `CurrentIsland`): the drawing has no camp to go to.
            if (anchor != null && startIsland != null && anchor.CurrentIsland != startIsland)
            { Cancel(); return; }

            // **A wall run is somebody else's frame.** Escape, Enter, the
            // tap and the ghost all belong to `WallSiting`; when it says it
            // has finished, the whole mode goes down with it.
            if (ladderMode)
            {
                if (!LadderSiting.Tick(beganFrame)) { Cancel(); return; }
                Refusal = LadderSiting.Refusal;
                bool on = LadderSiting.Mode == LadderSiting.State.Stretching;
                SyncToolBar((int)LadderSiting.Mode, LadderSiting.TopHint, on,
                    LadderSiting.CanConfirm, LadderSiting.Refusal, LadderSiting.PriceLine);
                return;
            }
            if (roadMode)
            {
                if (!RoadSiting.Tick(beganFrame)) { Cancel(); return; }
                Refusal = RoadSiting.Refusal;
                bool on = RoadSiting.Mode == RoadSiting.State.Stretching;
                SyncToolBar((int)RoadSiting.Mode, RoadSiting.EndHint, on,
                    RoadSiting.CanConfirm, RoadSiting.Refusal, RoadSiting.PriceLine);
                return;
            }
            if (wallMode)
            {
                if (!WallSiting.Tick(beganFrame)) { Cancel(); return; }
                Refusal = WallSiting.Refusal;
                bool on = WallSiting.Mode == WallSiting.State.Stretching;
                SyncToolBar((int)WallSiting.Mode, WallSiting.NextHint, on,
                    WallSiting.CanConfirm, WallSiting.Refusal, WallSiting.ClearLine);
                return;
            }

            var keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame) { Cancel(); return; }

            if (keys != null && keys.rKey.wasPressedThisFrame)
                Turn(keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);

            // **A tap on open ground MOVES the drawing. It does not build.**
            // Kevin, 2026-09-22: the only thing that builds is ✓.
            //
            // `IslandInput` only raises `TapThisFrame` for a press that went
            // down and came up inside the drag slop, and it does so on THIS
            // frame before this `Update` runs (`DefaultExecutionOrder(-50)`),
            // so `TapAt` is this frame's point. A tap whose press went down
            // before the mode existed belongs to whatever started it (the
            // build button), not to the ground — and a tap inside a claimed
            // rect (the sheet, or the three buttons below the ghost) is not a
            // tap on the ground at all.
            //
            // Only taps: a DRAG on the land is the camera pan, and stealing
            // it to slide the blueprint would take the island's one way of
            // looking around while siting.
            // ...and a press that is CARRYING the drawing is not a tap on the
            // ground either: `IslandInput` never raises one for it (the drag
            // owns the press from press-down to release), and this says so
            // twice rather than letting a drop double as a move.
            if (!dragging && IslandInput.TapThisFrame && IslandInput.TapDownFrame > beganFrame
                && !UIBlocker.Blocked(IslandInput.TapAt)
                && GroundPick.FromScreen(Camera.main, IslandInput.TapAt, out Vector3 ground))
                want = ground;

            Evaluate();
            SyncBuildingBar();

            // Desktop: Enter is the bar's confirm.
            if (keys != null && (keys.enterKey.wasPressedThisFrame
                                 || keys.numpadEnterKey.wasPressedThisFrame))
                Confirm();
        }

        /// One 45° step. Public and static so the bar's Turn and R press the
        /// same thing. Re-judged at once, so the status line is about the
        /// rectangle as it now faces.
        public static void Rotate()
        {
            var s = Instance;
            if (s == null || s.plan.id == null || s.wallMode || s.roadMode || s.ladderMode) return;
            s.Turn(false);
            s.Evaluate();
            s.SyncBuildingBar();
        }

        void Turn(bool back)
        {
            // Freeze whatever it was facing, then turn from there, so the
            // first press does not also swing it round to north.
            if (!turned) { turned = true; heldYaw = 0f; turns = 0; }
            turns = (turns + (back ? Steps - 1 : 1)) % Steps;
        }

        /// **Everything the mode knows, recomputed from `want`.**
        ///
        /// It still owns no rules: whether the spot will take the building is
        /// `Outpost.CanPlace`, the same call the raise makes. This runs every
        /// frame rather than only when `want` moves because the ground itself
        /// can change under a standing drawing (something else gets sited),
        /// and a ✓ that is live against a stale answer is the bug this file
        /// exists to prevent.
        void Evaluate()
        {
            at = want;
            sited = plan;
            string why;
            if (IsPier)
            {
                // The ring rule first, about the point the player actually
                // picked; then the snap, which replaces `at` with the pier's
                // centre and decides yaw and length.
                valid = false;
                if (!outpost.TooFarFromTown(plan, want, out why))
                {
                    bool snapped = outpost.SnapPier(want, out Vector3 centre,
                        out snappedYaw, out sited, out why);
                    // Even a refused snap says where it was trying to go, and
                    // a red ghost THERE explains the refusal better than one
                    // out on the grass.
                    at = centre;
                    if (snapped) valid = outpost.CanPlace(sited, at, snappedYaw, out why);
                }
            }
            else if (IsDryDock)
            {
                // No ring rule to check first: `Outpost.SnapDryDock` carries
                // its own reach test (close to the home berth), which is a
                // different centre than the town's and does not belong
                // behind `TooFarFromTown`.
                valid = false;
                bool snapped = outpost.SnapDryDock(want, out Vector3 centre, out snappedYaw, out why);
                at = centre;
                if (snapped) valid = outpost.CanPlace(sited, at, snappedYaw, out why);
            }
            else
            {
                // **A watchtower near a wall snaps onto it (2026-09-27).**
                // Within `Outpost.WallTowerSnap` of a wall post or line the
                // drawing jumps to that node and becomes part of the wall --
                // and the town reach does not apply to it. Anywhere else,
                // exactly as before.
                Vector3 node = want;
                onWall = plan.id == OutpostLedger.WatchtowerId
                    && outpost.FindWallNode(want, Outpost.WallTowerSnap, out node);
                if (onWall) at = node;
                valid = Test(at, out why);
            }
            // `Yaw` reads `at`, so the ghost and the test are always asking
            // about the same rectangle on the same ground.

            Refusal = why;

            // **The CLEAR-phase line (2026-09-23).** Only worth asking once
            // the spot has already passed `CanPlace` -- a refused ghost is
            // already saying why in `Refusal`, and a count of trees on
            // ground the player cannot build on would just be noise under
            // the same red ✕.
            // Rebuilt only when the counts change (2026-09-30): it used to be
            // a fresh string (and a list) every frame of siting.
            if (valid && outpost != null)
            {
                outpost.CountObstructions(sited, at, Yaw, out int trees, out int rocks);
                if (trees != clearTrees || rocks != clearRocks || onWall != clearOnWall)
                {
                    clearTrees = trees; clearRocks = rocks; clearOnWall = onWall;
                    ClearLine = FormatClearLine(trees, rocks);
                    if (onWall)
                        ClearLine = string.IsNullOrEmpty(ClearLine) ? OnWallLine : OnWallLine + " · " + ClearLine;
                }
            }
            else if (clearTrees != -1)
            {
                ClearLine = "";
                clearTrees = clearRocks = -1;
            }

            // **Warmth (2026-09-27).** A Hut only: whether this spot falls
            // inside `OutpostLedger.WarmHutRadius` and its residents would
            // draw the mood bonus, or not. (The walk label that sat beside
            // it is gone -- Kevin, 2026-09-27: "drop that information.")
            WarmthLine = "";
            if (valid && outpost != null && outpost.HasCampCentre && plan.id == BuildPlans.Hut.id)
                WarmthLine = WarmthLabel(outpost, at);

            Place(at);
            ShowGhost(true);
        }

        /// **"warm — near the fire" / "cold — far from the fire" (2026-09-27).**
        /// A Hut only: straight-line distance from `at` to the fire against
        /// `OutpostLedger.WarmHutRadius` -- the same measure a standing
        /// hut is judged by (`OutpostLedger.IsHandWarm`'s own row test), a
        /// ghost having no ground route yet worth asking `CampPath` for
        /// over a line this short. Never a refusal.
        public static string WarmthLabel(Outpost outpost, Vector3 at)
        {
            if (outpost == null) return "";
            float m = Island.FlatDistance(at, outpost.CampCentre);
            return m <= OutpostLedger.WarmHutRadius ? "warm — near the fire" : "cold — far from the fire";
        }

        /// The warmth label for the spot under the ghost right now (Hut
        /// only), or "".
        public static string WarmthLine { get; private set; } = "";

        /// **The only way a building gets placed.** The ✓ button, or Enter.
        public static void Confirm()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Instance == null) return;
            if (Instance.wallMode) WallSiting.Confirm();
            else if (Instance.roadMode) RoadSiting.Confirm();
            else if (Instance.ladderMode)
            {
                LadderSiting.Confirm();
                if (!LadderSiting.Active) Instance.Cancel();
            }
            else Instance.Commit();
        }

        /// Is the ✓ live? False draws it muted; `Refusal` says why.
        public static bool CanConfirm =>
            Placing && (Instance.wallMode ? WallSiting.CanConfirm
                : Instance.roadMode ? RoadSiting.CanConfirm
                : Instance.ladderMode ? LadderSiting.CanConfirm : Instance.valid);

        /// Where the drawing stands, for the buttons to sit under. For a
        /// wall that is the midpoint of the segment being stretched.
        public static Vector3 GhostAt =>
            Placing ? (Instance.wallMode ? WallSiting.ButtonsAt
                : Instance.roadMode ? RoadSiting.ButtonsAt
                : Instance.ladderMode ? LadderSiting.ButtonsAt : Instance.at) : Vector3.zero;

        /// **Is the watchtower being sited snapped onto the wall?** Read
        /// by the check (`WallTowerCheck`) as well as the label.
        bool onWall;
        public static bool OnWall => Placing && Instance.onWall;

        /// What the label under a snapped tower says.
        public const string OnWallLine = "on the wall";

        /// **Where the drawing is being asked about**, for a check that
        /// drives the real siting path: move it as a tap would, and
        /// re-evaluate now.
        public static void MoveTo(Vector3 world)
        {
            if (!Placing || Instance.wallMode || Instance.ladderMode || Instance.roadMode) return;
            Instance.want = world;
            Instance.Evaluate();
        }

        void Commit()
        {
            if (!valid) return;
            bool joinWall = onWall;
            Vector3 node = at;
            int wanted = outpost.Site(sited, at, Yaw, movingRow, out string siteWhy);
            if (wanted < 0)
            {
                // Refused at the last moment by a test the preview does not
                // run (something else was sited in the same frame). Say it
                // and stay in the mode rather than dropping the player back
                // to a sheet with no explanation.
                Refusal = siteWhy;
                return;
            }
            // Sited on a wall: a standing run through the node is split
            // there, so the tower stands between two runs, not on one.
            if (joinWall) outpost.JoinTowerToWall(node);

            DropTheViewOn(outpost);
            Cancel();
        }

        /// **Drop the view on to what was just sited**: Kevin's call, 35 m
        /// above it.
        ///
        /// The decision the player has just made is about that patch of
        /// ground, and leaving them at 165 m looking at a whole island makes
        /// them go and find it again.
        ///
        /// Public and static so the probe drives the same call the tap does.
        /// A gate that reached into the camera itself would be measuring its
        /// own copy of this.
        public static void DropTheViewOn(Outpost outpost)
        {
            if (outpost == null) return;
            var isleCam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            if (isleCam == null) return;
            isleCam.LookAt(outpost.CampCentre, CameraRig.IslandCam.BlueprintHeight);
        }

        /// The ground test, plus the one rule that is not about the ground.
        bool Test(Vector3 p, out string why)
        {
            // Reachability first (2026-09-27: all `TooFarFromTown` asks now)
            // -- it is the rule the player can see acted on (the walk label),
            // so it should be the reason they are given when both are broken.
            if (outpost.TooFarFromTown(plan, p, out why)) return false;
            return outpost.CanPlace(plan, p, Yaw, out why);
        }

        /// The town centre once one is sited (fire or its blueprint), else
        /// the ship -- which only seeds where the ghost starts.
        Vector3 Centre()
            => outpost != null && outpost.HasCampCentre ? outpost.CampCentre
             : ship != null ? ship.position : Vector3.zero;

        /// **The arrival shot's own framing point, for anything that has to
        /// AGREE with it.**
        ///
        /// Kevin on the phone, 2026-09-22: landing at a fresh island panned
        /// the camera to the middle of the island, while the only ground that
        /// would take the campfire was a ring around the SHIP, at the shore —
        /// frequently off the screen entirely. The camera was composing
        /// against `Outpost.ClearingCentre` and the ring against the ship, and
        /// nothing made the two ask the same question.
        ///
        /// So the rule lives here, once, and `AnchorController` frames what
        /// this returns rather than a second copy of the rule that can drift.
        public static Vector3 RingCentre(Outpost outpost, Transform ship)
        {
            if (outpost != null && outpost.HasCamp) return outpost.CampCentre;
            if (ship != null) return ship.position;
            return outpost != null ? outpost.CampCentre : Vector3.zero;
        }

        /// ...and how wide the arrival shot frames round it. Refuses nothing
        /// (2026-09-23) -- see `SiteRadius`.
        public static float RingRadius => SiteRadius;

        void Place(Vector3 p)
        {
            // A pier ghost is rebuilt when it moves, not just re-posed: its
            // posts were cut to the ground under THAT spot, and its length
            // is the beach's choice. Forty primitives, only on a frame the
            // snapped spot actually changed.
            if (ghost != null && IsPier
                && (!Mathf.Approximately(ghostLength, sited.footprint.x)
                    || (ghostAt - p).sqrMagnitude > 0.5f * 0.5f
                    || Mathf.Abs(Mathf.DeltaAngle(ghostYaw, Yaw)) > 2f))
            { Destroy(ghost); ghost = null; }
            if (ghost == null)
            {
                ghost = BuildingFactory.Ghost(sited, null, p, Quaternion.Euler(0f, Yaw, 0f), 0f, 0.5f);
                ghost.name = "SitingGhost";
                ghostLength = sited.footprint.x;
                ghostAt = p;
                ghostYaw = Yaw;
            }
            ghost.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Yaw, 0f));
            BuildingFactory.Tint(ghost,
                valid ? BuildingFactory.GhostChalk : BuildingFactory.GhostRefused, 0.5f);
        }

        void ShowGhost(bool on)
        {
            if (ghost != null && ghost.activeSelf != on) ghost.SetActive(on);
        }
    }
}
