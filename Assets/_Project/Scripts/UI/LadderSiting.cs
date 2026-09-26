using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.UI
{
    /// **Siting a ladder chain: tap the foot, tap the top (2026-09-27).**
    ///
    /// Kevin: *"is there any way to implement a ladder-platform-ladder-
    /// platform structure to ascend the mountains?"* Same connect-the-dots
    /// hand as the wall tool (`WallSiting`), two points instead of a run:
    /// tap the FOOT (walkable ground below the cliff), and the TOP follows
    /// the thumb -- tap where it goes, or drag it (or drag the foot). The
    /// whole chain is drawn live, green or red, with the refusal in words
    /// under the buttons ("the top isn't walkable ground", "too far", "not a
    /// cliff — just walk"). ✓ queues it and the tool ends; ✕ backs out.
    ///
    /// **This owns no rules.** Whether it can stand is
    /// `Outpost.CanPlaceLadder`, the same call `Outpost.SiteLadder` makes;
    /// the drawing is `LadderLayout.Draw`, the same shape the site and the
    /// finished chain stand as.
    ///
    /// A mode INSIDE `CampSiting` (which stays `Placing` while it runs), the
    /// way the wall tool is. Pure statics driven from `CampSiting`.
    public static class LadderSiting
    {
        /// Arm the tool, the way the fire sheet's ladder row does: through
        /// `CampSiting`, the one door every build goes through.
        public static void Start(Outpost camp)
            => CampSiting.Begin(camp, BuildPlans.Ladder, Sheets.SheetBits.ShipTransform);

        public static float GrabRadius = 1.8f;
        static readonly Color Good = new Color(0.44f, 0.84f, 0.46f);
        const float GhostAlpha = 0.55f;

        public enum State { Off, NoFoot, Stretching }
        public static State Mode { get; private set; } = State.Off;
        public static bool Active => Mode != State.Off;

        public const string FootHint = "tap the foot of the cliff";
        public static string Refusal { get; private set; } = "";
        /// "6 timber · 9 m up" for a good chain.
        public static string PriceLine { get; private set; } = "";
        public static bool CanConfirm => Mode == State.Stretching && valid;
        public static Vector3 ButtonsAt => Mode == State.Stretching ? foot : Vector3.zero;

        static Outpost outpost;
        /// As tapped: `foot` the first point, `top` the second. The rule
        /// orders them by height; the drawing follows the rule.
        static Vector3 foot, top;
        static bool valid;
        static bool dragging;
        static bool draggingFoot;
        static Vector2 grabOffset;
        static int plantedFrame = -1;
        static int confirmedFrame = -1;
        static Vector3 evalFoot, evalTop;
        static bool evaluated;

        public static void Begin(Outpost target)
        {
            End();
            if (target == null) return;
            outpost = target;
            Mode = State.NoFoot;
        }

        public static void End()
        {
            Mode = State.Off;
            outpost = null;
            dragging = false;
            valid = false;
            evaluated = false;
            Refusal = "";
            PriceLine = "";
            ClearGhost();
        }

        /// One frame. False when the tool has ended.
        public static bool Tick(int beganFrame)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return Active;
            if (Mode == State.Off) return false;
            if (outpost == null) { End(); return false; }

            var keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame) { End(); return false; }

            if (!dragging && IslandInput.TapThisFrame
                && IslandInput.TapDownFrame > beganFrame
                && IslandInput.TapDownFrame > plantedFrame
                && !UIBlocker.Blocked(IslandInput.TapAt)
                && GroundPick.FromScreen(Camera.main, IslandInput.TapAt, out Vector3 ground))
            {
                if (Mode == State.NoFoot) Plant(ground);
                else top = ground;
            }

            if (Mode == State.Stretching) Evaluate();

            if (keys != null && (keys.enterKey.wasPressedThisFrame
                                 || keys.numpadEnterKey.wasPressedThisFrame))
                Confirm();

            return Mode != State.Off;
        }

        static void Plant(Vector3 at)
        {
            foot = at;
            plantedFrame = Time.frameCount;
            // The top starts a few metres toward the camera's forward: up the
            // screen, which is usually up the cliff in the island view.
            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            top = foot + fwd * 4f;
            top.y = outpost != null ? outpost.GroundAt(top) : top.y;
            Mode = State.Stretching;
            evaluated = false;
            Evaluate();
        }

        /// **For a check driving the real path**: both ends at once, as two
        /// taps would, then re-evaluate now.
        public static void SetEnds(Vector3 footAt, Vector3 topAt)
        {
            if (Mode == State.Off) return;
            foot = footAt;
            top = topAt;
            Mode = State.Stretching;
            evaluated = false;
            Evaluate();
        }

        // --- drag either end ----------------------------------------------------

        public static bool GrabsPost(Vector2 screen)
        {
            if (Mode != State.Stretching) return false;
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return false;
            return LadderLayout.Flat(g, top) <= GrabRadius || LadderLayout.Flat(g, foot) <= GrabRadius;
        }

        public static void BeginDrag(Vector2 screen)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Mode != State.Stretching) return;
            dragging = true;
            grabOffset = Vector2.zero;
            draggingFoot = false;
            if (GroundPick.FromScreen(Camera.main, screen, out Vector3 g))
            {
                draggingFoot = LadderLayout.Flat(g, foot) < LadderLayout.Flat(g, top);
                Vector3 end = draggingFoot ? foot : top;
                grabOffset = new Vector2(end.x - g.x, end.z - g.z);
            }
        }

        public static void DragTo(Vector2 screen)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (!dragging || Mode != State.Stretching) return;
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return;
            var p = new Vector3(g.x + grabOffset.x, g.y, g.z + grabOffset.y);
            if (draggingFoot) foot = p; else top = p;
            Evaluate();
        }

        public static void EndDrag() { dragging = false; }

        // --- the test -----------------------------------------------------------

        /// Asked only when an end has moved: the rule runs one ground-only
        /// route search, which is not a per-frame cost.
        static void Evaluate()
        {
            if (outpost == null) return;
            if (evaluated && LadderLayout.Flat(evalFoot, foot) < 0.05f && LadderLayout.Flat(evalTop, top) < 0.05f)
                return;
            evaluated = true;
            evalFoot = foot;
            evalTop = top;
            Vector3 f = foot, t = top;
            valid = outpost.CanPlaceLadder(ref f, ref t, out string why);
            Refusal = valid ? "" : why;
            PriceLine = valid ? $"{BuildPlans.LadderCost(t.y - f.y)} timber · {t.y - f.y:0} m up" : "";
            Redraw(f, t);
        }

        public static void Confirm()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Mode != State.Stretching || !valid || outpost == null) return;
            if (confirmedFrame == Time.frameCount) return;
            confirmedFrame = Time.frameCount;
            var row = outpost.SiteLadder(foot, top, out string why);
            if (row == null) { Refusal = why; valid = false; return; }
            End();
        }

        public static void DrawGUI()
        {
            if (Mode == State.Off) return;
            if (Mode == State.NoFoot) { SitingButtons.Hint(FootHint); return; }
            // ✕ / ✓ under the foot; the words say the price when it is good,
            // the reason when it is not.
            switch (SitingButtons.Draw(ButtonsAt, valid, Refusal, false, ""))
            {
                case SitingButtons.Press.Cancel: End(); break;
                case SitingButtons.Press.Confirm: Confirm(); break;
            }
            if (valid) CampSiting.DrawClearLine(ButtonsAt, 2, PriceLine);
        }

        // --- the drawing --------------------------------------------------------

        static GameObject ghost;

        static void Redraw(Vector3 f, Vector3 t)
        {
            ClearGhost();
            if (outpost == null) return;
            // Draw the chain even when it is refused (as long as there is
            // something to draw): red says "not here" better than nothing.
            if (t.y < f.y) { var s = f; f = t; t = s; }
            if (LadderLayout.Flat(f, t) < 0.5f || t.y - f.y < 0.5f || LadderLayout.Flat(f, t) > 30f) return;
            var shape = LadderLayout.Plan(outpost.GroundAt, f, t);
            ghost = LadderLayout.Draw(null, shape, "LadderSitingGhost", false);
            var r = ghost.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            BuildingFactory.Tint(ghost, valid ? Good : BuildingFactory.GhostRefused, GhostAlpha);
        }

        static void ClearGhost()
        {
            if (ghost != null)
            {
                var mf = ghost.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
                Object.Destroy(ghost);
            }
            ghost = null;
        }
    }
}
