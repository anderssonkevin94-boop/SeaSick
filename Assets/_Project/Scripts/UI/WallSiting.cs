using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.UI
{
    /// **Siting a RUN of wall: connect the dots.**
    ///
    /// Kevin's own design, `docs/PLAN-fortress-harbour.md` D5, 2026-09-23:
    /// *"I have it pressed, move the other post around until I like it,
    /// press confirm and then continue."*
    ///
    /// So a wall is not one blueprint you put down; it is a chain of posts
    /// you walk round the camp. Plant the first post with a tap. The next
    /// post is on the end of your thumb — drag it, or tap where you want it,
    /// and the segment between the two is drawn live, green or red, with the
    /// refusal in the placement bar's status line. Build (✓) turns that segment into a site
    /// in the queue AT ONCE (so hands start hauling while you keep drawing)
    /// and the run continues from the post you just confirmed. ✕ stops.
    ///
    /// **This owns no rules.** Whether a segment can stand is
    /// `Outpost.CanPlaceWall`, which is the same call `Outpost.SiteWall`
    /// makes — the one-test rule `CampSiting` exists to hold, held again
    /// here. Where a post may sit is `Outpost.SnapPost`, so the obstacle
    /// cells the wall will block in `CampPath` are exact.
    ///
    /// **It is a mode INSIDE `CampSiting`**, not a second mode beside it.
    /// `CampSiting.Begin` hands the palisade plan here and keeps
    /// `CampSiting.Placing` true for as long as the run lasts, so every
    /// guard in the codebase that asks "is the player placing something?" —
    /// the Hand's pick-up, the tap's consequence, the camp bar's line — goes
    /// on getting the right answer without knowing walls exist.
    ///
    /// Pure statics, driven from `CampSiting`'s `Update` (which also drives
    /// the bottom placement bar for it, 2026-09-30): it has
    /// no lifetime of its own and nothing should be able to leave it running
    /// after siting ends.
    public static class WallSiting
    {
        /// The fire sheet's palisade row starts the tool through this hook
        /// (the world side must not reference UI). Everything that arms a
        /// build goes through `CampSiting.Begin`, so that is what it calls.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Wire()
        {
            Outpost.BeginWallSiting = camp =>
                CampSiting.Begin(camp, BuildPlans.Palisade, Sheets.SheetBits.ShipTransform);
        }

        // =================================================================
        // THE NUMBERS
        // =================================================================

        /// **How far out the next post starts from the one just planted.**
        /// One `Outpost.WallPostStep` — the shortest legal segment — so the
        /// new post is plainly attached to the last one and the player pulls
        /// it out to where they want it rather than finding it adrift.
        public const float StartOut = 2f;

        /// **Thumb slack on the grab test, metres.** The same ~1.5 m as the
        /// blueprint drag (`CampSiting.GrabSlack` over a small hut), and for
        /// the same reason: a post is a 0.3 m stick and a thumb is not.
        public static float GrabRadius = 1.5f;

        /// **How near the run's first post B has to come before ↻ offers to
        /// close the ring.** Three post-steps: close enough that the player
        /// was obviously aiming for it, far enough that the button is not
        /// flickering on every drag across the camp.
        public static float CloseRingWithin = 6f;

        /// What a segment that WILL build is drawn in. Not
        /// `BuildingFactory.GhostChalk`: chalk is "a drawing, not yet paid
        /// for", which is true of every blueprint on the island, and the
        /// question this tool is asking is the narrower one — *will this
        /// line stand here?* Green and red answer that and nothing else.
        static readonly Color Good = new Color(0.44f, 0.84f, 0.46f);

        // =================================================================
        // THE STATE MACHINE
        // =================================================================

        public enum State
        {
            /// Not running.
            Off,
            /// Armed, nothing planted: a tap on the ground plants post A.
            NoPost,
            /// A is planted and B follows the thumb. ✓ commits A→B.
            Stretching,
        }

        public static State Mode { get; private set; } = State.Off;

        /// Is the wall tool running? `CampSiting` is still `Placing` while
        /// it is — this says which of the two shapes of placing it is.
        public static bool Active => Mode != State.Off;

        /// The placement bar's hint before anything is planted...
        public const string PlantHint = "Tap the ground to plant the first post";
        /// ...and once a post is down and the next one is on the thumb.
        public const string NextHint = "Tap for the next post · Build lays this stretch · Cancel ends the wall";

        /// Why the segment as drawn is refused, or "" when it is good.
        /// `CampSiting.Refusal` mirrors this so the sheet and the bar print
        /// one answer.
        public static string Refusal { get; private set; } = "";

        /// **"clears 4 trees, 1 rock" for the whole run being drawn
        /// (2026-09-23).** A drag can already be several pieces
        /// (`Split`/`MaxWallSegment`), so this sums
        /// `Outpost.CountObstructionsWall` over every piece rather than
        /// asking only about A→B -- the number under the buttons should be
        /// what confirming THIS drag actually costs. Set only while `valid`,
        /// same rule as `CampSiting.ClearLine`.
        static string clearLine = "";

        /// That line, for the placement bar's status.
        public static string ClearLine => clearLine;

        /// Is ✓ live?
        public static bool CanConfirm => Mode == State.Stretching && valid;

        /// Where the ✕ ✓ cluster sits: the MIDPOINT of the segment being
        /// stretched, which is the thing being answered.
        public static Vector3 ButtonsAt =>
            Mode == State.Stretching ? Mid(a, b) : a;

        /// How many segments this run has already put in the queue. ✕ on a
        /// run with none cancels the tool; ✕ on a run with some just stops
        /// drawing and leaves them standing.
        public static int Confirmed => confirmed;

        static Outpost outpost;
        static Transform ship;
        static BuildPlan plan;

        /// The two posts of the segment being drawn. `a` is planted; `b` is
        /// the one on the thumb.
        static Vector3 a, b;
        /// The run's FIRST post, for ↻ "close the ring".
        static Vector3 first;
        static bool hasFirst;
        /// Which way the last confirmed segment ran, so the next post starts
        /// on ahead in the same direction rather than jumping to the side.
        static Vector3 heading = Vector3.right;

        static int confirmed;
        static bool valid;
        static bool dragging;
        static Vector2 grabOffset;

        /// The frame post A was planted on. The tap that planted it must not
        /// also move B — same trap as `CampSiting.beganFrame`.
        static int plantedFrame = -1;
        /// The frame ✓ last went through — see `Confirm`.
        static int confirmedFrame = -1;

        /// The split of A→B into pieces no longer than
        /// `Outpost.MaxWallSegment`: `posts[0]` is A, `posts[n]` is B, and
        /// every pair between them is one site. Rebuilt by `Evaluate`.
        static readonly System.Collections.Generic.List<Vector3> posts =
            new System.Collections.Generic.List<Vector3>();

        // =================================================================
        // ENTER AND LEAVE
        // =================================================================

        /// Arm the tool. Called by `CampSiting.Begin` when the plan is the
        /// palisade; never called from anywhere else, because everything
        /// that arms a build goes through that one door.
        public static void Begin(Outpost target, BuildPlan what, Transform shipTransform)
        {
            End();
            if (target == null) return;
            outpost = target;
            ship = shipTransform;
            plan = what;
            Mode = State.NoPost;
            confirmed = 0;
            hasFirst = false;
            valid = false;
            Refusal = "";
            plantedFrame = -1;
            heading = Vector3.right;
        }

        /// Put everything down. Idempotent; every exit goes through it.
        public static void End()
        {
            Mode = State.Off;
            outpost = null;
            ship = null;
            plan = default;
            dragging = false;
            valid = false;
            confirmed = 0;
            hasFirst = false;
            Refusal = "";
            clearLine = "";
            posts.Clear();
            ClearGhost();
        }

        // =================================================================
        // THE FRAME
        // =================================================================

        /// One frame of the tool. `beganFrame` is `CampSiting`'s: a tap whose
        /// press went down at or before it belongs to the button that armed
        /// the tool, not to the ground.
        ///
        /// Returns false when the tool has ended and `CampSiting` should tear
        /// the whole mode down with it.
        public static bool Tick(int beganFrame)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return Active;
            if (Mode == State.Off) return false;
            if (outpost == null) { End(); return false; }

            var keys = Keyboard.current;

            // Desktop: Enter is ✓, Escape is ✕. Both are exactly the button,
            // so neither can drift from what the thumb does.
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
                return !Stop();

            // A tap on open ground. In `NoPost` it plants A; in `Stretching`
            // it moves B — Kevin's lazy thumb: drag the post if you feel like
            // aiming, tap if you do not.
            //
            // A press that is CARRYING post B never raises a tap
            // (`IslandInput` hands the whole press to the drag), and a tap
            // inside a claimed rect — the sheet, the two buttons — is not a
            // tap on the ground at all.
            if (!dragging && IslandInput.TapThisFrame
                && IslandInput.TapDownFrame > beganFrame
                && IslandInput.TapDownFrame > plantedFrame
                && !UIBlocker.Blocked(IslandInput.TapAt)
                && GroundPick.FromScreen(Camera.main, IslandInput.TapAt, out Vector3 ground))
            {
                if (Mode == State.NoPost) Plant(Snap(ground));
                else b = RingMagnet(Snap(ground));
            }

            if (Mode == State.Stretching) Evaluate();

            if (keys != null && (keys.enterKey.wasPressedThisFrame
                                 || keys.numpadEnterKey.wasPressedThisFrame))
                Confirm();

            return Mode != State.Off;
        }

        /// Plant the first post of a segment and put the next one on the end
        /// of the thumb, one step out.
        static void Plant(Vector3 at)
        {
            a = at;
            if (!hasFirst) { first = a; hasFirst = true; }
            plantedFrame = Time.frameCount;
            b = Snap(a + Out() * StartOut);
            Mode = State.Stretching;
            Evaluate();
        }

        /// **Which way the next post starts off.** Along the run once there
        /// is a run — a wall being walked round a camp keeps going the way it
        /// was going — and along the camera's right for the very first
        /// segment, which is "to the right of what you are looking at" and
        /// is therefore on screen wherever the player has the island turned.
        static Vector3 Out()
        {
            if (confirmed > 0 && heading.sqrMagnitude > 0.0001f) return heading;
            var cam = Camera.main;
            if (cam == null) return Vector3.right;
            var r = cam.transform.right;
            r.y = 0f;
            return r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
        }

        // =================================================================
        // HOLD AND DRAG POST B
        //
        // `IslandInput` owns the devices and decides ONCE, at press-down,
        // whether a press belongs to the tool — the same shape as the
        // blueprint drag, routed through `CampSiting`'s four calls so there
        // is still exactly one place that question is asked.
        // =================================================================

        /// Does a press here pick post B up? Only while stretching: before
        /// there is a post there is nothing to grab, and the press should
        /// pan the island or plant A.
        public static bool GrabsPost(Vector2 screen)
        {
            if (Mode != State.Stretching) return false;
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return false;
            return Flat(g, b) <= GrabRadius;
        }

        public static void BeginDrag(Vector2 screen)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Mode != State.Stretching) return;
            dragging = true;
            grabOffset = Vector2.zero;
            if (GroundPick.FromScreen(Camera.main, screen, out Vector3 g))
                grabOffset = new Vector2(b.x - g.x, b.z - g.z);
        }

        /// Follow the finger, snapped every frame. Evaluated here as well as
        /// in `Tick` so the colour and the refusal are about the post's
        /// position THIS frame, not the one it left.
        public static void DragTo(Vector2 screen)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (!dragging || Mode != State.Stretching) return;
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return;
            b = RingMagnet(Snap(new Vector3(g.x + grabOffset.x, g.y, g.z + grabOffset.y)));
            Evaluate();
        }

        /// Let go. Dropping is not building; ✓ is still the only thing that
        /// puts a site in the queue.
        public static void EndDrag() { dragging = false; }

        public static bool Dragging => dragging;

        // =================================================================
        // THE TESTS
        // =================================================================

        /// **Everything the tool knows, recomputed from A and B.**
        ///
        /// The run A→B is split into pieces no longer than
        /// `Outpost.MaxWallSegment` (a segment is one site, so a long drag is
        /// several), every piece is put to `Outpost.CanPlaceWall`, and the
        /// first refusal is the one the player is shown — the nearest thing
        /// to "what is wrong with the wall I am drawing".
        static void Evaluate()
        {
            Split();
            valid = posts.Count >= 2;
            Refusal = "";
            clearLine = "";
            if (!valid) { Refusal = "the posts are on the same spot"; Redraw(); return; }

            for (int i = 0; i + 1 < posts.Count; i++)
            {
                if (CanPlace(posts[i], posts[i + 1], out string why)) continue;
                valid = false;
                Refusal = why;
                break;
            }

            // **The CLEAR-phase line, summed over the split.** Only worth
            // asking once every piece has already passed `CanPlaceWall` --
            // see `CampSiting.Evaluate`'s reasoning, same rule here.
            if (valid && outpost != null)
            {
                int trees = 0, rocks = 0;
                for (int i = 0; i + 1 < posts.Count; i++)
                {
                    outpost.CountObstructionsWall(posts[i], posts[i + 1], out int t, out int r);
                    trees += t;
                    rocks += r;
                }
                clearLine = CampSiting.FormatClearLine(trees, rocks);
            }
            Redraw();
        }

        /// A→B cut at the cap. Snapped ends, so the pieces meet exactly on
        /// the post grid and the obstacle cells line up.
        static void Split()
        {
            posts.Clear();
            Vector3 flatA = new Vector3(a.x, 0f, a.z);
            Vector3 flatB = new Vector3(b.x, 0f, b.z);
            float len = Vector3.Distance(flatA, flatB);
            if (len < 0.01f) return;

            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, MaxSegment) - 0.001f));
            posts.Add(a);
            for (int i = 1; i < pieces; i++)
            {
                Vector3 p = Vector3.Lerp(flatA, flatB, i / (float)pieces);
                Vector3 s = Snap(p);
                // A snap that lands back on the post before it would make a
                // zero-length piece, which is not a site — skip it and let
                // the neighbouring pieces span the gap.
                if (Flat(s, posts[posts.Count - 1]) < 0.01f) continue;
                posts.Add(s);
            }
            if (Flat(b, posts[posts.Count - 1]) < 0.01f) { posts.Clear(); return; }
            posts.Add(b);
        }

        // =================================================================
        // THE BUTTONS
        // =================================================================

        /// **✓ — build this segment and carry on.** Every piece of the split
        /// goes into the queue in order; then A becomes B, the next post
        /// appears one step on along the same line, and the state does not
        /// change. Kevin: *"press confirm and then continue."*
        public static void Confirm()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Mode != State.Stretching || !valid) return;
            // One press, one confirm: IMGUI and Enter can both land on a
            // frame, and a second ✓ in the same frame would site the fresh
            // 2 m stub nobody aimed.
            if (confirmedFrame == Time.frameCount) return;
            confirmedFrame = Time.frameCount;

            for (int i = 0; i + 1 < posts.Count; i++)
            {
                if (Site(posts[i], posts[i + 1], out string why)) continue;
                // Refused at the last moment by a test the preview does not
                // run. Say it and stay in the run rather than dropping the
                // player out with no explanation — the pieces already sited
                // stand, which is exactly what ✕ would have left them as.
                //
                // **And the run carries on from the last piece that DID
                // stand.** Leaving `a` on the first post after sited pieces
                // is how the next line came to grow out of the start of the
                // run (Kevin, 2026-09-23).
                if (i > 0)
                {
                    confirmed++;
                    Vector3 dd = new Vector3(posts[i].x - a.x, 0f, posts[i].z - a.z);
                    if (dd.sqrMagnitude > 0.0001f) heading = dd.normalized;
                    a = posts[i];
                    plantedFrame = Time.frameCount;
                    Evaluate();
                }
                Refusal = why;
                valid = false;
                Redraw();
                return;
            }

            confirmed++;
            Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
            if (d.sqrMagnitude > 0.0001f) heading = d.normalized;
            a = b;
            plantedFrame = Time.frameCount;
            b = Snap(a + heading * StartOut);
            Evaluate();
        }

        /// **✕ — stop.** A run that has put nothing in the queue cancels the
        /// whole tool (the player armed it by mistake); a run that has built
        /// something just stops drawing, and the sites it queued stand.
        /// Either way siting ends, which is what the player pressed ✕ for.
        ///
        /// Returns true when it ended the tool, which is always — the two
        /// cases differ only in what they leave behind.
        public static bool Stop()
        {
            End();
            return true;
        }

        /// **Close the ring**, and nothing else. There is no rotating a
        /// wall: a segment IS its two posts. Offered only when B is near the
        /// post the run started from -- the last segment of a circuit is the
        /// one that is fiddly to aim. No button drives it since the placement
        /// bar (2026-09-30); `RingMagnet` does the same job on the thumb.
        public static bool CanCloseRing =>
            Mode == State.Stretching && hasFirst && confirmed > 0
            && Flat(a, first) > 0.01f && Flat(b, first) <= CloseRingWithin;

        public static void CloseRing()
        {
            if (!CanCloseRing) return;
            b = first;
            Evaluate();
        }

        /// **Closing the ring without a button (2026-09-30).** The ⭯ disc
        /// under the segment went with the rest of the IMGUI siting
        /// controls, and the placement bar has no middle button for a wall.
        /// So post B landing within a thumb (`GrabRadius`) of the run's
        /// first post -- tapped or dragged -- lands ON it, which is the
        /// fiddly last segment of a circuit made exact.
        static Vector3 RingMagnet(Vector3 p)
        {
            if (!hasFirst || confirmed <= 0 || Flat(a, first) <= 0.01f) return p;
            return Flat(p, first) <= GrabRadius ? first : p;
        }

        // =================================================================
        // THE DRAWING
        //
        // **`BuildingFactory.WallGhost` per piece**, which is the same
        // drawing a wall SITE stands as — so the segment you are aiming and
        // the segment the hands then come and build are one description of
        // a palisade, not two that drift. Green when the run will build,
        // red when it will not: the colour is what the thumb reads, the
        // words under the buttons are for when it wants to know why.
        //
        // Rebuilt only when the posts MOVE, which the 2 m snap makes a rare
        // frame: dragging across a camp changes this a few times a second,
        // not sixty.
        // =================================================================

        static GameObject ghost;
        static Vector3 drawnA, drawnB;
        static bool drawnValid;
        static bool drawnAny;

        static void Redraw()
        {
            bool moved = !drawnAny
                || Flat(drawnA, a) > 0.01f || Flat(drawnB, b) > 0.01f;
            if (!moved && drawnValid == valid && ghost != null) return;

            if (moved || ghost == null)
            {
                ClearGhost();
                Build();
                drawnA = a;
                drawnB = b;
                drawnAny = true;
            }
            if (ghost != null)
                BuildingFactory.Tint(ghost, valid ? Good : BuildingFactory.GhostRefused,
                    GhostAlpha);
            drawnValid = valid;
        }

        static void Build()
        {
            if (posts.Count < 2) return;
            ghost = new GameObject("WallSitingGhost");
            // One ghost per PIECE of the split, so a drag past
            // `Outpost.MaxWallSegment` shows the run already cut into the
            // sites it will become — the player can see they are about to
            // pay for two.
            for (int i = 0; i + 1 < posts.Count; i++)
                BuildingFactory.WallGhost(ghost.transform, posts[i], posts[i + 1],
                    false, GhostAlpha);
            foreach (var r in ghost.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        const float GhostAlpha = 0.55f;

        static void ClearGhost()
        {
            if (ghost != null) Object.Destroy(ghost);
            ghost = null;
            drawnAny = false;
        }

        // =================================================================
        // THE CONTRACT
        //
        // Every call into `SeaSick.World`'s wall half goes through one of
        // these four wrappers. They exist so that the shape of that contract
        // — instance or static, argument order, what a refusal looks like —
        // is written down in exactly one place in this file and a change to
        // it is a one-line change here rather than a hunt.
        // =================================================================

        static Vector3 Snap(Vector3 world)
            => outpost != null ? outpost.SnapPost(world) : world;

        static float MaxSegment => Outpost.MaxWallSegment;

        static bool CanPlace(Vector3 p, Vector3 q, out string why)
        {
            if (outpost == null) { why = "this ground was never surveyed"; return false; }
            return outpost.CanPlaceWall(p, q, out why);
        }

        static bool Site(Vector3 p, Vector3 q, out string why)
        {
            if (outpost == null) { why = "this ground was never surveyed"; return false; }
            return outpost.SiteWall(p, q, out why) != null;
        }

        // =================================================================

        static Vector3 Mid(Vector3 p, Vector3 q)
            => new Vector3((p.x + q.x) * 0.5f,
                           Mathf.Max(p.y, q.y),
                           (p.z + q.z) * 0.5f);

        static float Flat(Vector3 p, Vector3 q)
            => Mathf.Sqrt((p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z));
    }
}
