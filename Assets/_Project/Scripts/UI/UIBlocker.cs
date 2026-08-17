using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.UI
{
    /// Lets IMGUI buttons claim screen space so the one-thumb helm doesn't
    /// steer the ship when the player is tapping a button.
    public static class UIBlocker
    {
        static readonly List<Rect> rects = new List<Rect>();
        static int frame = -1;

        /// Call from OnGUI with the button's GUI-space rect.
        public static void Block(Rect guiRect)
        {
            if (frame != Time.frameCount) { rects.Clear(); frame = Time.frameCount; }
            rects.Add(guiRect);
        }

        /// pointerPos is Input System screen space (origin bottom-left).
        public static bool Blocked(Vector2 pointerPos)
        {
            if (Time.frameCount - frame > 1) return false;
            var gui = new Vector2(pointerPos.x, Screen.height - pointerPos.y);
            foreach (var r in rects)
                if (r.Contains(gui)) return true;
            return false;
        }
    }
}
