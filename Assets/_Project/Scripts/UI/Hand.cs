using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// **The player's hand on the island.** Pick somebody up, put them down
    /// on a thing, and that is their job.
    ///
    /// **The Hand gives orders and never produces.** A drop writes the
    /// outpost's ledger row through the same `Outpost` calls the crew list
    /// uses, in the frame it happens; picking up and holding write nothing.
    /// A camp pays the same whether it is watched, fidgeted with or left
    /// (D2 in `docs/PLAN-island-outposts.md`), and nothing here may change
    /// that.
    ///
    /// Reads no devices. `CameraRig.IslandInput` decides what a press means
    /// and calls the methods below; the probes call the same ones.
    ///
    /// ## The two rules that shape every line of this
    ///
    /// **Ordering writes the row, and nothing else does.** `DropAt` calls
    /// `Outpost.CatchUp()` BEFORE it writes, so the time that elapsed is
    /// credited under the order the hand was already on, and only then is the
    /// new one written. That is what makes two hundred re-drops across a day
    /// pay exactly what one drop and a day of absence pays. Holding somebody
    /// for half a day changes nothing at all: their row is still in the
    /// ledger, still producing, because a man in the air is a man who has
    /// been told something, not a man who has stopped.
    ///
    /// **A steady cursor allocates nothing.** Every list this would rebuild —
    /// the parked bodies, the roster, what the island can grow — is cached and
    /// refreshed on a PRESS rather than on a frame, and the words in the
    /// prompt are rebuilt only when the words change. See `HandTargets`, which
    /// holds the same line.
    public class Hand : MonoBehaviour
    {
        public static Hand Instance { get; private set; }

        /// Whoever is in the Hand, or null.
        public Crew.CrewAgent Held { get; private set; }
        public bool Holding => Held != null;

        /// **Tunables, as plain statics.** This component is added at runtime
        /// (`IslandCam.Awake`), so a serialised field on it is never editable
        /// and a `[SerializeField]` here would be a dial nobody can turn.
        public static class Feel
        {
            /// Where on a body the cursor is aimed, and how tall they are.
            /// Both in metres, both the crew rig's own numbers.
            public static float ChestHeight = 0.9f;
            public static float VillagerHeight = WorldScale.Person;

            /// **Below this share of screen height a villager cannot be
            /// grabbed on purpose.** At 520 m of ground a person is four
            /// pixels tall, and a pick radius with a floor under it would
            /// swallow every land-grab that started anywhere near the camp —
            /// so at that zoom the answer is "you are not pointing at
            /// anybody", and the drag pans the island instead.
            public static float PickMinScreen01 = 0.007f;

            /// The grab radius is three quarters of how tall they look, held
            /// between these two shares of screen height: never a pixel hunt,
            /// never a net over the whole camp.
            public static float PickRadiusOfHeight = 0.75f;
            public static float PickRadiusMin01 = 0.015f;
            public static float PickRadiusMax01 = 0.05f;

            /// A tap that means "follow them" is generous, because getting it
            /// wrong costs a camera move and nothing else.
            public static float FollowRadius01 = 0.07f;

            /// How far off the island a body may be and still be one of this
            /// island's people. The shore party walks out of the ship.
            public static float IslandMargin = 120f;

            /// How high the held body floats over the ground it would land on.
            /// A share of the view, so they clear the trees at any zoom, with
            /// a floor so they clear a person's head close in.
            public static float LiftOfView = 0.035f;
            public static float LiftMin = 1.5f;

            /// How hard the body is pulled after the cursor, per second.
            public static float Spring = 16f;
        }

        /// **How far ABOVE the pointer the body hangs, as a share of screen
        /// height** — and therefore where the drop actually lands, because the
        /// target is the ground under the offset point and not under the
        /// thumb.
        ///
        /// Zero for a mouse, which is what a mouse wants: the cursor is a few
        /// pixels and hides nothing. `IslandInput` raises it for touch, where
        /// the thumb is over the very thing being aimed at. Owned by the input
        /// layer because only the input layer knows which device is driving.
        public static float TouchOffset01;

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // --- what it is looking at -------------------------------------------

        Ship.AnchorController anchor;
        Crew.CrewRoster roster;
        CameraRig.IslandCam view;
        Camera lens;
        HandCursor cursor;

        Ship.AnchorController Anchor()
        {
            if (anchor == null) anchor = GetComponent<Ship.AnchorController>();
            if (anchor == null) anchor = Object.FindFirstObjectByType<Ship.AnchorController>();
            return anchor;
        }

        Crew.CrewRoster Roster()
        {
            // The SAME instance `CampSheet` refreshes: the roster caches its
            // crew array and the guns are assigned from it, so a second one
            // would be a second answer to "who is aboard".
            if (roster == null) roster = Object.FindFirstObjectByType<Crew.CrewRoster>();
            return roster;
        }

        CameraRig.IslandCam View()
        {
            if (view == null) view = GetComponent<CameraRig.IslandCam>();
            if (view == null) view = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            return view;
        }

        Camera Lens()
        {
            if (lens == null) lens = Camera.main;
            return lens;
        }

        /// The hull the Hand hands people back to. The Hand lives on the ship.
        /// Named `Hull` rather than `Ship` because `SeaSick.Ship` is a
        /// namespace this file reads types out of.
        public Transform Hull
        {
            get
            {
                var a = Anchor();
                return a != null ? a.transform : transform;
            }
        }

        /// The camp she is lying at, or null.
        public Outpost Camp
        {
            get
            {
                var a = Anchor();
                var isle = a != null ? a.CurrentIsland : null;
                return isle != null ? Outpost.Of(isle) : null;
            }
        }

        /// What was last resolved under the cursor. Drawn by the prompt and
        /// read by the probe; never re-resolved for either, because a second
        /// resolve is a second answer.
        public HandTarget Current => memo;

        // =====================================================================
        // Picking somebody up
        // =====================================================================

        Crew.CrewAgent[] parked;
        Crew.CrewAgent[] crew;
        Outpost cachedFor;

        /// Who is under this screen point. `forPickup` uses the tight,
        /// zoom-aware radius (and refuses when a villager is too small on
        /// screen to grab on purpose); otherwise the generous follow radius.
        ///
        /// **Projection, not physics.** The crew are 388-triangle characters
        /// with no colliders — giving twenty of them one so that a cursor can
        /// be aimed would be paying the physics engine for an interface. The
        /// lists are cached and refreshed on a press, so the hover that runs
        /// every frame costs two `WorldToScreenPoint`s a body and nothing
        /// else.
        public Crew.CrewAgent PickAt(Vector2 screen, bool forPickup)
        {
            var cam = Lens();
            if (cam == null) return null;
            var camp = Camp;

            if (forPickup || parked == null || crew == null || !ReferenceEquals(camp, cachedFor))
                Refresh(camp);

            Vector3 isleAt = Vector3.zero;
            float isleR = -1f;
            var isle = camp != null ? camp.Island : null;
            if (isle != null) { isleAt = isle.transform.position; isleR = isle.Radius; }

            scanBest = float.MaxValue;
            scanPicked = null;
            float h = Screen.height;

            Scan(parked, screen, forPickup, cam, isleAt, isleR, h, false);

            // Crew on her deck are pickable too — that IS how somebody gets
            // ashore. Only while she is lying still: a hand plucked off a
            // moving ship would be a man overboard.
            var an = Anchor();
            bool moored = an != null
                && (an.CurrentState == Ship.AnchorController.State.Anchored
                    || an.CurrentState == Ship.AnchorController.State.Ashore);
            if (moored) Scan(crew, screen, forPickup, cam, isleAt, isleR, h, true);

            return scanPicked;
        }

        float scanBest;
        Crew.CrewAgent scanPicked;

        void Scan(Crew.CrewAgent[] list, Vector2 screen, bool forPickup, Camera cam,
            Vector3 isleAt, float isleR, float screenH, bool shipCrew)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                var a = list[i];
                if (a == null || ReferenceEquals(a, Held)) continue;
                if (!a.gameObject.activeInHierarchy) continue;
                if (shipCrew && !a.IsAboard) continue;

                Vector3 at = a.transform.position;
                if (isleR > 0f && Island.FlatDistance(at, isleAt) > isleR + Feel.IslandMargin)
                    continue;

                Vector3 foot = cam.WorldToScreenPoint(at);
                Vector3 head = cam.WorldToScreenPoint(at + Vector3.up * Feel.VillagerHeight);
                if (head.z <= 0f || foot.z <= 0f) continue;          // behind the lens

                float tall = Mathf.Abs(head.y - foot.y);
                if (forPickup && tall < Feel.PickMinScreen01 * screenH) continue;

                float limit = forPickup
                    ? Mathf.Clamp(Feel.PickRadiusOfHeight * tall,
                        Feel.PickRadiusMin01 * screenH, Feel.PickRadiusMax01 * screenH)
                    : Feel.FollowRadius01 * screenH;

                float k = Feel.ChestHeight / Mathf.Max(0.01f, Feel.VillagerHeight);
                float cx = Mathf.Lerp(foot.x, head.x, k) - screen.x;
                float cy = Mathf.Lerp(foot.y, head.y, k) - screen.y;
                float d2 = cx * cx + cy * cy;
                if (d2 > limit * limit || d2 >= scanBest) continue;
                scanBest = d2;
                scanPicked = a;
            }
        }

        /// Re-read who is where. On a PRESS, not on a frame: `Outpost.Parked`
        /// is a `GetComponentsInChildren` and the hover runs sixty times a
        /// second.
        void Refresh(Outpost camp)
        {
            cachedFor = camp;
            parked = camp != null ? camp.Parked() : null;
            var r = Roster();
            crew = r != null ? r.All : null;
        }

        // =====================================================================
        // Holding
        // =====================================================================

        Transform heldParent;
        Vector3 heldLocalPos;
        Quaternion heldLocalRot;
        bool heldActive;
        bool heldPuppeted;
        Vector3 bodyAt;

        /// Lift this hand. Writes nothing.
        ///
        /// The body is unparented as it comes up, because she rolls at anchor
        /// and a man dangling from the cursor must not roll with her. Where he
        /// was is remembered exactly, so `Cancel` is a true undo.
        public bool PickUp(Crew.CrewAgent a)
        {
            if (a == null || Held != null) return false;

            var tr = a.transform;
            heldParent = tr.parent;
            heldLocalPos = tr.localPosition;
            heldLocalRot = tr.localRotation;
            heldActive = a.gameObject.activeSelf;
            heldPuppeted = a.Puppeted;

            Held = a;
            heldName = HandTargets.NameOf(a);

            if (!heldActive) a.gameObject.SetActive(true);
            // Something else is driving this body now, and `CrewAgent.ActBody`
            // has to stop writing its rotation for as long as that is true.
            a.Puppeted = true;
            var worker = CampWorker.Of(a);
            if (worker != null) worker.PickedUp();
            var act = VillagerActing.On(a);
            if (act != null) act.Set(VillagerActing.Mode.Dangle);

            tr.SetParent(null, true);
            bodyAt = tr.position;
            return true;
        }

        string heldName;

        /// Called every frame while holding, with where the pointer is.
        public void HoldAt(Vector2 screen)
        {
            if (Held == null) return;
            PushView();
            var t = TargetAt(screen);

            float lift = Mathf.Max(Feel.LiftMin, Feel.LiftOfView * HandTargets.ViewGround);
            Vector3 want = t.point + Vector3.up * lift;
            float k = 1f - Mathf.Exp(-Feel.Spring * Mathf.Max(0f, Time.unscaledDeltaTime));
            bodyAt = Vector3.Lerp(bodyAt, want, k);
            Held.transform.position = bodyAt;

            Draw(t);
        }

        /// A bare hover, nothing held. `IslandInput` may or may not call this;
        /// the cursor has to work while holding whatever it does.
        public void HoverAt(Vector2 screen)
        {
            if (Held != null) return;
            PushView();
            Draw(TargetAt(screen));
        }

        int shownFrame = -1;

        void Draw(HandTarget t)
        {
            EnsureCursor();
            if (cursor == null) return;
            cursor.Show(t, HandTargets.ViewGround);
            shownFrame = Time.frameCount;
        }

        /// What is under this screen point and what letting go would do.
        public HandTarget Preview(Vector2 screen) => TargetAt(screen);

        // --- the one resolve, memoised per frame ------------------------------

        HandTarget memo;
        int memoFrame = -1;
        Vector2 memoScreen = new Vector2(float.NaN, float.NaN);

        HandTarget TargetAt(Vector2 screen)
        {
            // The drop point is the ground under the OFFSET point, not under
            // the thumb -- and the preview has to ask about the same point the
            // drop will, or the cursor promises one thing and the drop does
            // another.
            Vector2 s = screen;
            s.y += TouchOffset01 * Screen.height;

            if (memoFrame == Time.frameCount
                && memoScreen.x == s.x && memoScreen.y == s.y) return memo;

            var v = View();
            Ray ray = v != null
                ? v.ScreenRay(s)
                : (Lens() != null ? Lens().ScreenPointToRay(s)
                                  : new Ray(Vector3.up * 100f, Vector3.down));

            memo = HandTargets.Resolve(Camp, Held, ray, Hull);
            memoFrame = Time.frameCount;
            memoScreen = s;
            return memo;
        }

        void PushView()
        {
            var v = View();
            if (v != null) HandTargets.ViewGround = v.Ground;
        }

        // =====================================================================
        // Letting go
        // =====================================================================

        /// Let go here. True if an order was written or the body was set
        /// down; false with `why` if it was refused (the hand stays held).
        ///
        /// **`CatchUp` first, then the write, in this frame.** Everything that
        /// elapsed belongs to the order the hand was already on; only what
        /// happens after this instant belongs to the new one. Get that
        /// backwards and re-issuing the same order all day quietly pays
        /// differently from issuing it once and sailing away, which is D2 and
        /// is not negotiable.
        public bool DropAt(Vector2 screen, out string why)
        {
            why = "";
            if (Held == null) { why = "nobody in hand"; return false; }

            var t = TargetAt(screen);
            if (!t.Allowed)
            {
                why = string.IsNullOrEmpty(t.refusal) ? "nothing to do there" : t.refusal;
                return false;
            }

            var a = Held;
            var camp = Camp;

            if (t.kind == HandTarget.Kind.Ship)
            {
                // Ship's crew put back on the ship: nothing happened. Not even
                // a `CatchUp`, because nothing about the camp changed.
                var existing = camp != null ? camp.HandNamed(heldName) : null;
                if (existing == null) { Cancel(); return true; }

                camp.CatchUp();
                if (!camp.Recall(a, Hull)) { why = "she will not take them back"; return false; }
                var r = Roster();
                if (r != null) r.Refresh();
                // Back at a post: `CrewAgent` owns this body again.
                CampWorker.Remove(a);
                heldPuppeted = false;
                Release(VillagerActing.Mode.None);
                Refresh(camp);
                return true;
            }

            if (camp == null) { why = "no ground here will take a camp"; return false; }

            camp.CatchUp();

            var row = camp.HandNamed(heldName);
            if (row == null)
            {
                // Still on the ship's books. Landing them IS the order, and it
                // happens before the order so the row exists to write into.
                if (!camp.Station(a)) { why = "site a camp first"; return false; }
                var r = Roster();
                if (r != null) r.Refresh();
                row = camp.HandNamed(heldName);
            }

            Apply(camp, row, t, a);
            SetDown(camp, a, t.point);
            Release(VillagerActing.Mode.Land);
            Refresh(camp);
            return true;
        }

        /// **What a drop means, as one switch.** Every branch is a call the
        /// crew list already makes, so a Hand order and a menu order cannot
        /// come out different.
        static void Apply(Outpost camp, OutpostHand row, HandTarget t, Crew.CrewAgent a)
        {
            if (row == null) return;
            switch (t.kind)
            {
                case HandTarget.Kind.Tree:
                    camp.OrderGather(row, Res.Timber);
                    break;
                case HandTarget.Kind.Node:
                    camp.OrderGather(row, t.resource);
                    break;
                case HandTarget.Kind.Workplace:
                    camp.Assign(row, t.building != null ? t.building.Id : t.planId);
                    // Which sawmill, out of two. The ledger knows a sawyer by
                    // plan id and that says nothing about which door he walks
                    // to, so the body is told separately -- and it is show
                    // only, which is why it is not in the ledger.
                    var w = CampWorker.Of(a);
                    if (w != null) w.PreferWorkplace(t.building);
                    break;
                case HandTarget.Kind.Fire:
                    camp.OrderIdle(row);
                    break;
                case HandTarget.Kind.Blueprint:
                    camp.OrderBuild(row);
                    break;
                // Bare ground writes NOTHING. Moving somebody across the camp
                // is not an order, and a row rewritten here would be a day's
                // work quietly restarted.
            }
        }

        /// Stand the body where it was let go.
        ///
        /// Last, after every ledger write: `OrderGather`, `Assign` and
        /// `Station` all call `ArrangeHands`, which stands the whole camp back
        /// in its ring. Putting the body down first would have it teleported
        /// out from under the player's own thumb.
        void SetDown(Outpost camp, Crew.CrewAgent a, Vector3 at)
        {
            var tr = a.transform;
            if (tr.parent != camp.transform) tr.SetParent(camp.transform, true);

            at.y = camp.GroundAt(at);
            tr.position = at;
            Vector3 face = camp.CampCentre - at;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f)
                tr.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);

            // `Station` switches a body off unless the camp is being watched,
            // and the one thing that is certainly true here is that somebody
            // is watching: they just put him there.
            if (!a.gameObject.activeSelf) a.gameObject.SetActive(true);

            // The landing pose goes on BEFORE the worker is handed the body,
            // so a hand dropped from ten metres up is seen to arrive rather
            // than to appear already walking. `Restore` sets it again a moment
            // later and that is the same value, not a second decision.
            var landing = VillagerActing.On(a);
            if (landing != null) landing.Set(VillagerActing.Mode.Land);

            // Gives him a walking body if he has none, and re-homes the one he
            // has to where he now stands -- dropped here, so work from here.
            camp.PuppetsToWork();
            var w = CampWorker.Of(a);
            if (w != null) w.PutDown(at);
        }

        /// Put them back where they were. Writes nothing.
        public void Cancel()
        {
            var a = Held;
            if (a == null) return;
            Held = null;

            var tr = a.transform;
            tr.SetParent(heldParent, false);
            tr.localPosition = heldLocalPos;
            tr.localRotation = heldLocalRot;
            if (a.gameObject.activeSelf != heldActive) a.gameObject.SetActive(heldActive);

            var w = CampWorker.Of(a);
            if (w != null) w.PutDown(tr.position);
            Restore(a, VillagerActing.Mode.None);
        }

        void Release(VillagerActing.Mode mode)
        {
            var a = Held;
            Held = null;
            if (a != null) Restore(a, mode);
        }

        /// Hand the body back to whoever owns it.
        ///
        /// **`Puppeted` is not simply cleared.** `CampWorker` sets it for as
        /// long as it is driving a villager about the camp, so clearing it
        /// under a live worker would let `CrewAgent.ActBody` start fighting
        /// the sawyer for his own rotation. Under a worker it stays true;
        /// otherwise it goes back to whatever it was before the lift.
        void Restore(Crew.CrewAgent a, VillagerActing.Mode mode)
        {
            var w = CampWorker.Of(a);
            a.Puppeted = w != null ? true : heldPuppeted;
            var act = VillagerActing.On(a);
            if (act != null) act.Set(mode);
            heldName = null;
            if (cursor != null) cursor.Hide();
        }

        // =====================================================================
        // The cursor and the prompt
        // =====================================================================

        void EnsureCursor()
        {
            if (cursor != null) return;
            cursor = GetComponent<HandCursor>();
            if (cursor == null) cursor = gameObject.AddComponent<HandCursor>();
        }

        void LateUpdate()
        {
            PushView();

            // She cast off with somebody in the air, or a dev tool took the
            // view. Put them back rather than leaving a body hanging over an
            // island that is about to stream out.
            if (!CameraRig.IslandCam.Engaged)
            {
                if (Held != null) Cancel();
                if (cursor != null) cursor.Hide();
                return;
            }

            // Nobody asked for a cursor this frame -- the pointer left the
            // window, or `IslandInput` is not calling `HoverAt` at all. A ring
            // left lying on the grass under nothing is worse than no ring.
            if (cursor != null && Held == null && shownFrame != Time.frameCount)
                cursor.Hide();
        }

        readonly HudLabel line = new HudLabel();
        string lineFrom;
        bool lineAllowed;

        /// The verb, in the one slot the HUD keeps for "what is the game
        /// asking me to do". It outranks the anchor while somebody is held,
        /// because with a man dangling from the cursor that is the only
        /// question on screen.
        ///
        /// It goes in the prompt slot rather than beside the cursor for a
        /// reason that is about phones: on a one-handed portrait screen the
        /// thumb is ON the thing being aimed at, so text drawn there is text
        /// nobody can read.
        void OnGUI()
        {
            if (Held == null) return;
            // Bid on EVERY event. A caller that bids only on Repaint owns the
            // slot on repaint frames and has lost it by the mouse-up.
            if (!Prompts.Claim(Prompts.Rank.Hand)) return;
            if (Event.current.type != EventType.Repaint) return;

            var t = memo;
            bool ok = t.Allowed;
            string src = ok ? t.verb : t.refusal;
            if (string.IsNullOrEmpty(src)) return;

            // Rebuilt only when the words change: IMGUI generates a text mesh
            // for every string it is handed, and the verb under a still cursor
            // is the same four words for as long as it is still.
            if (ok != lineAllowed || !ReferenceEquals(src, lineFrom))
            {
                lineFrom = src;
                lineAllowed = ok;
                line.Set(ok ? src : "✕  " + src);
            }

            var stack = Prompts.Begin();
            GUI.Label(stack.Next(HudLayout.Unit * 2.7f), line.Content, UITheme.Small2Centered);
        }
    }
}
