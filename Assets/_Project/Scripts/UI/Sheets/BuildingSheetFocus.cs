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

        internal static bool IsBuilding(ISheet sheet) => sheet is StationSheet || sheet is FarmSheet
            || sheet is SiteSheet || sheet is WallSheet;

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
            if (!islandCam.Ready || !SheetHost.FrameOpen) return;
            if (islandCam.TiltNow < 60f)
            {
                islandCam.OrbitAbout(new Vector2(Screen.width*.5f,Screen.height*.5f),0f,60f-islandCam.TiltNow);
                islandCam.OrbitEnd();
            }
            if (!islandCam.VirtualPose(out _, out var rotation, out var fov)) return;

            Rect free = VisibleWorldRect(Screen.safeArea, SheetHost.FrameRect, MidnightLandHud.ResourcesRect,
                Screen.height, HudLayout.Wide, SheetHost.PanelScale);
            Vector2 target = free.center;
            // Leave breathing room above a building's ground-level selection ring.
            target.y = Mathf.Lerp(free.yMin, free.yMax, .42f);
            var uv = new Vector2(target.x / Screen.width, target.y / Screen.height);
            float ground = Mathf.Max(islandCam.MinGround, islandCam.Ground);
            Vector3 pivot = PivotFor(sheet.AnchorWorld, rotation, fov, ground, uv, (float)Screen.width / Screen.height);
            islandCam.PanToWorld(pivot, rig.OverviewHeightForGround(ground), .45f);
            pending = false;
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
