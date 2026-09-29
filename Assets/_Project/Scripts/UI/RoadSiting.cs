using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.UI
{
    /// **Laying a road: tap to tap (2026-09-27).**
    ///
    /// Kevin: *"I'd rather place it myself."* The wall tool's shape
    /// (`WallSiting`), because it is the one his thumb already knows: tap
    /// the ground to put the road's start down; the next point is on the
    /// end of the thumb -- tap where the road should bend (or drag the
    /// point); ✓ puts that stretch in the queue AT ONCE and the road carries
    /// on from the bend; ✕ stops (whatever was confirmed stays queued).
    ///
    /// A start or bend tapped near an existing road end (standing or on
    /// order) snaps onto it, so the drawing joins the two into one line.
    /// Whether a stretch can be laid is `Outpost.CanPlaceRoad`, the same
    /// call `Outpost.SiteRoad` makes. A mode INSIDE `CampSiting` (same as
    /// the wall and the ladder), so every "is the player placing?" guard
    /// keeps its answer. Pure statics, no lifetime of its own.
    public static class RoadSiting
    {
        /// How far the next point starts from the one just put down.
        public const float StartOut = 4f;
        /// Thumb slack on the grab test, metres.
        public static float GrabRadius = 1.8f;
        /// A tap this close to an existing road end joins it.
        public static float SnapToEnd = 1.8f;

        static readonly Color Good = new Color(0.44f, 0.84f, 0.46f);
        const float GhostAlpha = 0.55f;

        public enum State { Off, NoPoint, Stretching }

        public static State Mode { get; private set; } = State.Off;
        public static bool Active => Mode != State.Off;

        /// The placement bar's hint before the start is down, and after.
        public const string PlantHint = "Tap where the road starts";
        public const string EndHint = "Tap where it ends";

        public static string Refusal { get; private set; } = "";
        static string priceLine = "";
        /// "12 stone · 30 m" for a good stretch, for the placement bar.
        public static string PriceLine => priceLine;

        public static bool CanConfirm => Mode == State.Stretching && valid;

        public static Vector3 ButtonsAt => Mode == State.Stretching ? Mid(a, b) : a;

        static Outpost outpost;
        static Vector3 a, b;
        static Vector3 heading = Vector3.right;
        static int confirmed;
        static bool valid;
        static bool dragging;
        static Vector2 grabOffset;
        static int plantedFrame = -1;
        static int confirmedFrame = -1;

        /// A→B cut into pieces no longer than `Outpost.MaxRoadSegment`.
        static readonly System.Collections.Generic.List<Vector3> points =
            new System.Collections.Generic.List<Vector3>();

        // =================================================================
        // ENTER AND LEAVE
        // =================================================================

        /// Arm the tool. Called by `CampSiting.Begin` for the road plan.
        public static void Begin(Outpost target)
        {
            End();
            if (target == null) return;
            outpost = target;
            Mode = State.NoPoint;
            confirmed = 0;
            valid = false;
            Refusal = "";
            plantedFrame = -1;
            heading = Vector3.right;
        }

        /// The build list's road card starts the tool through this.
        public static void Start(Outpost target)
        {
            if (target == null) return;
            CampSiting.Begin(target, BuildPlans.Road, Sheets.SheetBits.ShipTransform);
        }

        public static void End()
        {
            Mode = State.Off;
            outpost = null;
            dragging = false;
            valid = false;
            confirmed = 0;
            Refusal = "";
            priceLine = "";
            points.Clear();
            ClearGhost();
        }

        // =================================================================
        // THE FRAME
        // =================================================================

        /// One frame. False when the tool has ended and `CampSiting` should
        /// tear the mode down with it.
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
                if (Mode == State.NoPoint) Plant(Snap(ground));
                else b = Snap(ground);
            }

            if (Mode == State.Stretching) Evaluate();

            if (keys != null && (keys.enterKey.wasPressedThisFrame
                                 || keys.numpadEnterKey.wasPressedThisFrame))
                Confirm();

            return Mode != State.Off;
        }

        static void Plant(Vector3 at)
        {
            a = at;
            plantedFrame = Time.frameCount;
            b = Ground(a + Out() * StartOut);
            Mode = State.Stretching;
            Evaluate();
        }

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
        // HOLD AND DRAG THE NEXT POINT
        // =================================================================

        public static bool GrabsPoint(Vector2 screen)
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

        public static void DragTo(Vector2 screen)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (!dragging || Mode != State.Stretching) return;
            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 g)) return;
            b = Snap(new Vector3(g.x + grabOffset.x, g.y, g.z + grabOffset.y));
            Evaluate();
        }

        public static void EndDrag() { dragging = false; }

        // =================================================================
        // THE TESTS
        // =================================================================

        static void Evaluate()
        {
            Split();
            valid = points.Count >= 2;
            Refusal = "";
            priceLine = "";
            if (!valid) { Refusal = "too short for a road"; Redraw(); return; }
            int stone = 0;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                stone += BuildPlans.RoadCost(Flat(points[i], points[i + 1]));
                if (outpost.CanPlaceRoad(points[i], points[i + 1], out string why)) continue;
                valid = false;
                Refusal = why;
                break;
            }
            if (valid) priceLine = $"{stone} stone · {Flat(a, b):0} m";
            Redraw();
        }

        static void Split()
        {
            points.Clear();
            float len = Flat(a, b);
            if (len < Outpost.MinRoadSegment) return;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / Outpost.MaxRoadSegment - 0.001f));
            points.Add(a);
            for (int i = 1; i < pieces; i++) points.Add(Ground(Vector3.Lerp(a, b, i / (float)pieces)));
            points.Add(b);
        }

        // =================================================================
        // THE BUTTONS
        // =================================================================

        /// ✓ -- queue this stretch and carry on from its end.
        public static void Confirm()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (Mode != State.Stretching || !valid) return;
            if (confirmedFrame == Time.frameCount) return;
            confirmedFrame = Time.frameCount;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (outpost.SiteRoad(points[i], points[i + 1], out string why) != null) continue;
                // Refused at the last moment: carry on from the last piece
                // that did go in, and say why.
                if (i > 0)
                {
                    confirmed++;
                    a = points[i];
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
            b = Ground(a + heading * StartOut);
            Evaluate();
        }

        // =================================================================
        // THE DRAWING -- a chalk ribbon per piece, green or red.
        // =================================================================

        static GameObject ghost;
        static Vector3 drawnA, drawnB;
        static bool drawnValid, drawnAny;

        static void Redraw()
        {
            bool moved = !drawnAny || Flat(drawnA, a) > 0.01f || Flat(drawnB, b) > 0.01f;
            if (!moved && drawnValid == valid && ghost != null) return;
            if (moved || ghost == null)
            {
                ClearGhost();
                ghost = new GameObject("RoadSitingGhost");
                if (points.Count >= 2)
                    for (int i = 0; i + 1 < points.Count; i++)
                        RoadStrip.Make(ghost.transform, points[i], points[i + 1], outpost.GroundAt);
                else
                    RoadStrip.Make(ghost.transform, a, b, outpost.GroundAt);
                drawnA = a; drawnB = b; drawnAny = true;
            }
            BuildingFactory.Tint(ghost, valid ? Good : BuildingFactory.GhostRefused, GhostAlpha);
            drawnValid = valid;
        }

        static void ClearGhost()
        {
            if (ghost != null) Object.Destroy(ghost);
            ghost = null;
            drawnAny = false;
        }

        // =================================================================

        /// A tapped point: onto a road end if one is near, else the ground.
        static Vector3 Snap(Vector3 world)
        {
            if (outpost != null && outpost.RoadEndNear(world, SnapToEnd, out Vector3 end)) return end;
            return Ground(world);
        }

        static Vector3 Ground(Vector3 p)
            => outpost != null ? new Vector3(p.x, outpost.GroundAt(p), p.z) : p;

        static Vector3 Mid(Vector3 p, Vector3 q)
            => new Vector3((p.x + q.x) * 0.5f, Mathf.Max(p.y, q.y), (p.z + q.z) * 0.5f);

        static float Flat(Vector3 p, Vector3 q)
            => Mathf.Sqrt((p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z));
    }
}
