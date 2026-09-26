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
    /// ground takes it -- no ring round the ship, no pre-made clearing --
    /// and everything after it goes within `Outpost.TownRadius` of it.
    ///
    /// This is the interface half of that. It owns exactly three things — the
    /// preview ghost, the ring on the ground, and the tap — and it owns no
    /// rules: whether a spot will take a building is `Outpost.CanPlace`, which
    /// is the same call the real raise makes. **A ghost that goes green on one
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
        /// much ground round the landing. The siting rule is
        /// `Outpost.TownRadius`, round the town centre, once there is one.
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

        /// **...or a LADDER chain (2026-09-27)**: two points, its own tool
        /// (`LadderSiting`), forked exactly where the wall is.
        public const string LadderPlanId = "ladder";
        public static bool PlacingLadder => Placing && Instance.ladderMode;
        bool ladderMode;

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

        /// **Draw the clear line in the same slot `SitingButtons.Draw` would
        /// have put the refusal text in**, for a caller that does not own
        /// that private layout. Only called while the spot is valid, so it
        /// never collides with a refusal.
        public static void DrawClearLine(Vector3 world, int buttonCount, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var row = SitingButtons.Cluster(world, buttonCount);
            if (row.width <= 0f) return;
            var r = new Rect(row.center.x - HudLayout.Unit * 9f,
                row.yMax + SitingButtons.Gap * 0.5f,
                HudLayout.Unit * 18f, HudLayout.Unit * 1.4f);
            UITheme.Rect(r, UITheme.Panel);
            GUI.Label(r, line, UITheme.Small2Centered);
        }

        BuildPlan plan;
        Outpost outpost;
        Transform ship;
        GameObject ghost;
        LineRenderer ring;
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
        /// nothing the player does turns it -- see `Outpost.SnapPier`.
        public float Yaw => (IsPier || IsDryDock) ? snappedYaw : heldYaw + turns * TurnStep;

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

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

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

            // **The wall fork.** A palisade is a run of posts, not a
            // rectangle on the end of a thumb, so the ghost, the start
            // point and the yaw all belong to `WallSiting` from here. The
            // ring still gets built: the reach rule is the same rule.
            Instance.ladderMode = what.id == LadderPlanId;
            if (Instance.ladderMode)
            {
                LadderSiting.Begin(target);
                Instance.BuildRing();
                return;
            }

            Instance.wallMode = what.id == WallPlanId;
            if (Instance.wallMode)
            {
                WallSiting.Begin(target, what, shipTransform);
                Instance.BuildRing();
                return;
            }

            Instance.want = Instance.StartPoint();
            Instance.BuildRing();
        }

        /// **Where the drawing appears before anyone has moved it.**
        ///
        /// It has to appear somewhere: the thumb is not carrying it any more.
        /// A blueprint being moved starts on itself; anything else starts on
        /// the middle of the screen, which is what the player is looking at —
        /// pulled back inside the ring when the middle of the screen is off
        /// the reach, so the first thing the player sees is a legal spot and
        /// a live ✓ rather than a red refusal.
        Vector3 StartPoint()
        {
            if (moving && movingRow != null)
                return OnGround(movingRow.x, movingRow.z);

            Vector3 c = Centre();
            var cam = Camera.main;
            if (cam != null && GroundPick.FromScreen(cam,
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out Vector3 mid))
            {
                // No town centre yet (or this IS it): anywhere goes, so the
                // middle of the screen is the answer as it stands.
                if (!HasRing) return mid;
                var d = new Vector2(mid.x - c.x, mid.z - c.z);
                if (d.magnitude <= Outpost.TownRadius * 0.95f) return mid;
                d = d.normalized * (Outpost.TownRadius * 0.6f);
                return OnGround(c.x + d.x, c.z + d.y);
            }
            return c;
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
            // A wall's grab target is its loose post, not a footprint —
            // same question, asked of the tool that owns the answer.
            if (s.wallMode) return WallSiting.GrabsPost(screen);
            if (s.ladderMode) return LadderSiting.GrabsPost(screen);
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
        }

        /// Is the drawing on the end of a finger right now?
        public static bool Dragging => Placing && Instance.dragging;

        void Cancel()
        {
            if (wallMode) { wallMode = false; WallSiting.End(); }
            if (ladderMode) { ladderMode = false; LadderSiting.End(); }
            plan = default;
            moving = false;
            dragging = false;
            movingRow = null;
            if (outpost != null) outpost.IgnoreSite = null;
            outpost = null;
            Refusal = "";
            ClearLine = "";
            valid = false;
            onWall = false;
            if (ghost != null) { Destroy(ghost); ghost = null; }
            if (ring != null) { Destroy(ring.gameObject); ring = null; }
        }

        void Update()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (plan.id == null) return;
            if (outpost == null || ship == null) { Cancel(); return; }

            // **A wall run is somebody else's frame.** Escape, Enter, the
            // tap and the ghost all belong to `WallSiting`; when it says it
            // has finished, the whole mode goes down with it.
            if (ladderMode)
            {
                if (!LadderSiting.Tick(beganFrame)) { Cancel(); return; }
                Refusal = LadderSiting.Refusal;
                return;
            }
            if (wallMode)
            {
                if (!WallSiting.Tick(beganFrame)) { Cancel(); return; }
                Refusal = WallSiting.Refusal;
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

            // Desktop: Enter is the ✓.
            if (keys != null && (keys.enterKey.wasPressedThisFrame
                                 || keys.numpadEnterKey.wasPressedThisFrame))
                Confirm();
        }

        /// One 45° step. Public and static so the ↻ button and R press the
        /// same thing.
        public static void Rotate() { if (Instance != null) Instance.Turn(false); }

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
            ClearLine = "";
            if (valid && outpost != null)
            {
                outpost.CountObstructions(sited, at, Yaw, out int trees, out int rocks);
                ClearLine = FormatClearLine(trees, rocks);
                if (onWall)
                    ClearLine = string.IsNullOrEmpty(ClearLine) ? OnWallLine : OnWallLine + " · " + ClearLine;
            }

            Place(at);
            ShowGhost(true);
        }

        /// **The only way a building gets placed.** The ✓ button, or Enter.
        public static void Confirm()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Instance == null) return;
            if (Instance.wallMode) WallSiting.Confirm();
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
                : Instance.ladderMode ? LadderSiting.CanConfirm : Instance.valid);

        /// Where the drawing stands, for the buttons to sit under. For a
        /// wall that is the midpoint of the segment being stretched.
        public static Vector3 GhostAt =>
            Placing ? (Instance.wallMode ? WallSiting.ButtonsAt
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
            if (!Placing || Instance.wallMode || Instance.ladderMode) return;
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

        /// **The three thumbs under the drawing** — see `SitingButtons`. The
        /// mode draws them itself rather than the sheet doing it, because
        /// they are anchored to the ghost and the ghost is this file's.
        void OnGUI()
        {
            if (plan.id == null || outpost == null) return;
            if (wallMode) { WallSiting.DrawGUI(); return; }
            if (ladderMode)
            {
                LadderSiting.DrawGUI();
                if (!LadderSiting.Active) Cancel();
                return;
            }
            switch (SitingButtons.Draw(at, valid, Refusal))
            {
                case SitingButtons.Press.Cancel: Cancel(); break;
                case SitingButtons.Press.Rotate: Turn(false); Evaluate(); break;
                case SitingButtons.Press.Confirm: Commit(); break;
            }
            if (valid) DrawClearLine(at, 3, ClearLine);
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
            // The ring first: it is the rule the player can SEE, so it should
            // be the reason they are given when both are broken.
            if (outpost.TooFarFromTown(plan, p, out why)) return false;
            return outpost.CanPlace(plan, p, Yaw, out why);
        }

        /// The town centre once one is sited (fire or its blueprint), else
        /// the ship -- which with no ring only seeds where the ghost starts.
        Vector3 Centre()
            => outpost != null && outpost.HasCampCentre ? outpost.CampCentre
             : ship != null ? ship.position : Vector3.zero;

        /// **Is there a reach to draw?** Only once the town centre is down,
        /// and never while siting (or moving) the town centre itself -- the
        /// same two conditions `Outpost.TooFarFromTown` refuses on.
        bool HasRing => outpost != null && outpost.HasCampCentre
                        && plan.kind != BuildKind.Fire && plan.kind != BuildKind.Pier
                        && plan.kind != BuildKind.DryDock;

        /// **The siting ring, for anything that has to AGREE with it.**
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

        /// ...and how wide the arrival shot frames round it. Since 2026-09-23
        /// this refuses nothing: see `SiteRadius` and `Outpost.TownRadius`.
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

        /// The ring on the ground round the town centre: the reach, drawn where the
        /// decision is being made rather than written in the sheet. Faint on
        /// purpose (2026-09-23) -- it is a hint about the rule, and the ghost's
        /// red and the refusal line are what say no. None before the town
        /// centre is sited: there is no reach to show.
        ///
        /// Sampled against the height field so it climbs the beach instead of
        /// slicing through it, and built ONCE — she is anchored, so it does
        /// not move, and 96 evaluations of the height function every frame is
        /// not a thing to spend on a circle.
        void BuildRing()
        {
            if (!HasRing) return;
            var go = new GameObject("SiteRing");
            go.transform.SetParent(transform, false);
            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 0.6f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ring.material.SetColor("_BaseColor", new Color(0.74f, 0.83f, 0.90f, 1f));

            const int Segments = 96;
            ring.positionCount = Segments;
            var h = GroundPick.Height;
            Vector3 c = Centre();
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float x = c.x + Mathf.Cos(a) * Outpost.TownRadius;
                float z = c.z + Mathf.Sin(a) * Outpost.TownRadius;
                // Over water the field returns sea bed, so the ring would
                // disappear under the sea for most of its length. Clamp it to
                // just above sea level, which is where the player is looking
                // from anyway.
                float y = h != null ? Mathf.Max(h(x, z), 0.2f) : 0.2f;
                ring.SetPosition(i, new Vector3(x, y + 0.35f, z));
            }
        }
    }
}
