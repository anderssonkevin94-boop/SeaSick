using SeaSick.CameraRig;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    // One composition request per selection. IslandCam owns easing and gesture cancellation.
    internal sealed class BuildingSheetFocus
    {
        ISheet selected;
        bool pending;
        IslandCam islandCam;
        ChaseCamera rig;

        /// **The sheets of things tapped in the world** -- every structure a
        /// finger can land on. The camera keeps them in view above the sheet
        /// (`Tick`) and, on the phone, their frame hugs its content at most
        /// half the screen high (`SheetHost.HugsContent`, Kevin 2026-09-30:
        /// *"I would like to see the building I'm pressing on when the menu
        /// pops up"*). The fire, the watchtower, the pier, ladders, roads and
        /// the dry dock joined the four building sheets that day.
        internal static bool IsBuilding(ISheet sheet) => sheet is StationSheet || sheet is FarmSheet
            || sheet is SiteSheet || sheet is WallSheet || sheet is CampfireSheet || sheet is LookoutSheet
            || sheet is PierSheet || sheet is LadderSheet || sheet is RoadSheet || sheet is DryDockSheet;

        public void Tick(ISheet sheet)
        {
            if (!ReferenceEquals(selected, sheet))
            {
                selected = sheet;
                pending = MidnightLandHud.Active && IsBuilding(sheet);
            }
            if (!pending) return;
            if (!MidnightLandHud.Active || sheet == null || !sheet.StillValid)
            { pending = false; return; }
            if (islandCam == null) islandCam = Object.FindFirstObjectByType<IslandCam>();
            if (rig == null) rig = Object.FindFirstObjectByType<ChaseCamera>();
            if (islandCam == null || rig == null) return;
            if (islandCam.Grabbing) { pending = false; return; }
            // A hugging frame is hidden for the frame it is measured in and
            // sits at the cap until then (`SheetHost.FrameSettled`): decide
            // against the frame the player will actually see, so a short
            // sheet (the Shelter's one card) does not move a camera that the
            // cap would have called hidden.
            if (!islandCam.Ready || !SheetHost.FrameOpen || !SheetHost.FrameSettled) return;
            pending = false;

            // **Leave the view alone unless the building is hidden (2026-09-29).**
            // Kevin, on the phone: "whenever i press on a building the camera
            // launches somewhere else so i have to drag myself back." The old
            // version always re-centred the building on a fixed spot of the
            // free area (and asked for a 60 deg tilt the lock drops), which at
            // the locked 28 deg is a long throw every tap. Now: if the building
            // already sits in the comfortable part of the view above the sheet,
            // nothing moves; otherwise the camera eases the SHORTEST way that
            // brings it just inside, at the current zoom.
            Rect free = VisibleWorldRect(Screen.safeArea, SheetHost.FrameRect, MidnightLandHud.ResourcesRect,
                Screen.height, HudLayout.Wide, SheetHost.PanelScale);
            Rect band = ComfortBand(free);
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            Vector3 anchor = sheet.AnchorWorld;

            if (!islandCam.VirtualPose(out var seat, out var rotation, out var fov)) return;
            bool visible = ScreenOf(anchor, seat, rotation, fov, aspect, out Vector2 now);
            if (visible && band.Contains(now)) return;

            // Moving after all: take hold first so the pose reasoned about is
            // the view's own, not the rig's (a no-op on the picture).
            islandCam.TakeHold();
            if (!islandCam.VirtualPose(out seat, out rotation, out fov)) return;
            visible = ScreenOf(anchor, seat, rotation, fov, aspect, out now);
            Vector2 target = visible
                ? new Vector2(Mathf.Clamp(now.x, band.xMin, band.xMax), Mathf.Clamp(now.y, band.yMin, band.yMax))
                : band.center;
            if (visible && (target - now).sqrMagnitude < 1f) return;

            float ground = Mathf.Max(islandCam.MinGround, islandCam.Ground);
            // `PivotFor` answers "centre the frame here and the anchor lands
            // there"; the difference of two answers is the shift that carries
            // the anchor from where it is now to `target`. Applied to the
            // frame's own middle, and never more than one frame of ground, so
            // a bad read can nudge the view but never launch it.
            Vector3 to = PivotFor(anchor, rotation, fov, ground, ToUv(target), aspect);
            Vector3 middle = islandCam.FocusPoint.HasValue ? islandCam.Pivot : to;
            Vector3 delta = visible
                ? to - PivotFor(anchor, rotation, fov, ground, ToUv(now), aspect)
                : to - middle;
            delta.y = 0f;
            delta = Vector3.ClampMagnitude(delta, ground);
            islandCam.PanToWorld(middle + delta, rig.OverviewHeightForGround(ground), .45f);
        }

        static Vector2 ToUv(Vector2 screen) => new Vector2(screen.x / Mathf.Max(1, Screen.width),
            screen.y / Mathf.Max(1, Screen.height));

        /// The part of the free area a building's FOOT should sit in: inset
        /// from the sides, and kept in the lower ~60% so the building itself
        /// (which stands up the screen from its foot) and its label fit above.
        internal static Rect ComfortBand(Rect free)
        {
            float x0 = Mathf.Lerp(free.xMin, free.xMax, .12f), x1 = Mathf.Lerp(free.xMin, free.xMax, .88f);
            float y0 = Mathf.Lerp(free.yMin, free.yMax, .12f), y1 = Mathf.Lerp(free.yMin, free.yMax, .62f);
            return Rect.MinMaxRect(x0, y0, Mathf.Max(x0 + 1f, x1), Mathf.Max(y0 + 1f, y1));
        }

        /// Where `world` lands on screen (camera pixels, bottom-left origin)
        /// for this pose. False when it is behind the lens.
        internal static bool ScreenOf(Vector3 world, Vector3 seat, Quaternion rotation, float fov, float aspect, out Vector2 screen)
        {
            Vector3 v = Quaternion.Inverse(rotation) * (world - seat);
            screen = default;
            if (v.z <= .01f) return false;
            float tan = Mathf.Tan(fov * Mathf.Deg2Rad * .5f);
            float x = v.x / (v.z * tan * aspect), y = v.y / (v.z * tan);
            screen = new Vector2((x + 1f) * .5f * Screen.width, (y + 1f) * .5f * Screen.height);
            return true;
        }

        // Inputs from HUD layout use GUI's top-left origin; return camera screen coordinates.
        internal static Rect VisibleWorldRect(Rect safe, Rect sheet, Rect bar, float screenHeight, bool wide, float scale)
        {
            float margin = 12f / Mathf.Max(.01f, scale);
            float left = safe.xMin + margin;
            float right = wide ? sheet.xMin - margin : safe.xMax - margin;
            float bottom = wide ? screenHeight - MidnightLandHud.NavigationRect.yMin + margin
                : screenHeight - sheet.yMin + margin;
            float top = screenHeight - bar.yMax - margin;
            return Rect.MinMaxRect(left, bottom, Mathf.Max(left+1f,right), Mathf.Max(bottom+1f,top));
        }

        internal static Vector3 PivotFor(Vector3 subject, Quaternion rotation, float fov, float ground, Vector2 viewport, float aspect)
        {
            float tan = Mathf.Tan(fov * Mathf.Deg2Rad * .5f);
            float span = ground / (2f * tan);
            Vector3 seat = subject - rotation * Vector3.forward * span;
            Vector3 direction = rotation * new Vector3((viewport.x-.5f)*2f*tan*aspect, (viewport.y-.5f)*2f*tan, 1f);
            var plane = new Plane(Vector3.up, subject);
            var ray = new Ray(seat,direction);
            if (!plane.Raycast(ray,out float distance)) return subject;
            return subject + (subject - ray.GetPoint(distance));
        }
    }
}
