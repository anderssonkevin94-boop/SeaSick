using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SeaSick.UI
{
    /// Lets IMGUI buttons claim screen space so the one-thumb helm doesn't
    /// steer the ship when the player is tapping a button.
    public static class UIBlocker
    {
        static readonly List<Rect> rects = new List<Rect>();
        static readonly List<string> owners = new List<string>();
        static int frame = -1;

        /// Every rect claimed this frame, and who claimed it. `HudOverlapProbe`
        /// reads these to find two live controls sitting on each other — the
        /// bug that had "come alongside" and "space · lock on" overlapping by
        /// 34 px, where whichever drew second in the mouse pass took the tap.
        public static IReadOnlyList<Rect> Claimed => rects;
        public static IReadOnlyList<string> Owners => owners;

        /// Call from OnGUI with the button's GUI-space rect.
        ///
        /// The caller's file and method arrive for free: the compiler fills
        /// them in as string literals, so naming who owns a rect costs no
        /// allocation and no change at any of the twenty call sites.
        public static void Block(Rect guiRect,
            [CallerFilePath] string file = null,
            [CallerMemberName] string member = null)
        {
            if (frame != Time.frameCount)
            {
                rects.Clear();
                owners.Clear();
                frame = Time.frameCount;
            }
            rects.Add(guiRect);
            owners.Add(Label(file, member));
        }

        static string Label(string file, string member)
        {
            if (string.IsNullOrEmpty(file)) return member ?? "?";
            int slash = file.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? file.Substring(slash + 1) : file;
            if (name.EndsWith(".cs")) name = name.Substring(0, name.Length - 3);
            return name + "." + member;
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
