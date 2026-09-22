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
    /// your thumb. You put it somewhere within reach of the ship, it stays
    /// there as a drawing, and the hands you leave behind build it. Kevin,
    /// 2026-09-22: the reach is measured from the campfire once it stands.
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
        /// **How far from the camp centre you may site something, metres.**
        ///
        /// Kevin's rule: only within a certain radius of the camp. When a camp
        /// has been sited (HasCamp is true), the circle is drawn around the
        /// campfire (CampCentre); otherwise it is drawn around the ship. The
        /// number is a guess and wants a dial — the median island is 78 m in
        /// radius and the default island shot holds 165 m of ground, so 80 m
        /// from a campfire reaches a good part of a typical island without
        /// letting you develop the far side of a big one.
        public static float SiteRadius = 80f;

        public static CampSiting Instance { get; private set; }

        /// Is the player placing something right now?
        public static bool Placing => Instance != null && Instance.plan.id != null;

        /// ...and is it a pier, which the sheet explains differently: there is
        /// no R to turn it, and the thing to tap is the beach.
        public static bool PlacingPier => Placing && Instance.IsPier;

        /// Why the spot under the pointer is refused, or "" if it is good.
        /// The sheet prints this; the ghost's colour says the same thing
        /// faster.
        public static string Refusal { get; private set; } = "";

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
        public float Yaw => IsPier ? snappedYaw : heldYaw + turns * TurnStep;

        bool IsPier => plan.kind == BuildKind.Pier;

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
                var d = new Vector2(mid.x - c.x, mid.z - c.z);
                if (d.magnitude <= SiteRadius * 0.95f) return mid;
                d = d.normalized * (SiteRadius * 0.6f);
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

        void Cancel()
        {
            plan = default;
            moving = false;
            movingRow = null;
            if (outpost != null) outpost.IgnoreSite = null;
            outpost = null;
            Refusal = "";
            valid = false;
            if (ghost != null) { Destroy(ghost); ghost = null; }
            if (ring != null) { Destroy(ring.gameObject); ring = null; }
        }

        void Update()
        {
            if (plan.id == null) return;
            if (outpost == null || ship == null) { Cancel(); return; }

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
            if (IslandInput.TapThisFrame && IslandInput.TapDownFrame > beganFrame
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
                if (!TooFar(want, out why))
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
            else valid = Test(at, out why);
            // `Yaw` reads `at`, so the ghost and the test are always asking
            // about the same rectangle on the same ground.

            Refusal = why;

            Place(at);
            ShowGhost(true);
        }

        /// **The only way a building gets placed.** The ✓ button, or Enter.
        public static void Confirm() { if (Instance != null) Instance.Commit(); }

        /// Is the ✓ live? False draws it muted; `Refusal` says why.
        public static bool CanConfirm => Placing && Instance.valid;

        /// Where the drawing stands, for the buttons to sit under.
        public static Vector3 GhostAt => Placing ? Instance.at : Vector3.zero;

        void Commit()
        {
            if (!valid) return;
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

            DropTheViewOn(outpost);
            Cancel();
        }

        /// **The three thumbs under the drawing** — see `SitingButtons`. The
        /// mode draws them itself rather than the sheet doing it, because
        /// they are anchored to the ghost and the ghost is this file's.
        void OnGUI()
        {
            if (plan.id == null || outpost == null) return;
            switch (SitingButtons.Draw(at, valid, Refusal))
            {
                case SitingButtons.Press.Cancel: Cancel(); break;
                case SitingButtons.Press.Rotate: Turn(false); Evaluate(); break;
                case SitingButtons.Press.Confirm: Commit(); break;
            }
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
            if (TooFar(p, out why)) return false;
            return outpost.CanPlace(plan, p, Yaw, out why);
        }

        /// The centre of the siting circle: the campfire if a camp stands, else the ship.
        Vector3 Centre() => RingCentre(outpost, ship);

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

        /// ...and how wide it is. Same number `TooFar` refuses on.
        public static float RingRadius => SiteRadius;

        bool TooFar(Vector3 p, out string why)
        {
            Vector3 c = Centre();
            float d = Vector3.Distance(
                new Vector3(p.x, 0f, p.z), new Vector3(c.x, 0f, c.z));
            if (d > SiteRadius)
            {
                string from = outpost != null && outpost.HasCamp ? "camp" : "ship";
                why = $"too far from the {from} ({d:F0} m of {SiteRadius:F0})";
                return true;
            }
            why = "";
            return false;
        }

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

        /// The ring on the ground round the camp centre: the reach, drawn where the
        /// decision is being made rather than written in the sheet.
        ///
        /// Sampled against the height field so it climbs the beach instead of
        /// slicing through it, and built ONCE — she is anchored, so it does
        /// not move, and 96 evaluations of the height function every frame is
        /// not a thing to spend on a circle.
        void BuildRing()
        {
            var go = new GameObject("SiteRing");
            go.transform.SetParent(transform, false);
            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 1.1f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ring.material.SetColor("_BaseColor", new Color(0.62f, 0.78f, 0.92f, 0.6f));

            const int Segments = 96;
            ring.positionCount = Segments;
            var h = GroundPick.Height;
            Vector3 c = Centre();
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float x = c.x + Mathf.Cos(a) * SiteRadius;
                float z = c.z + Mathf.Sin(a) * SiteRadius;
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
