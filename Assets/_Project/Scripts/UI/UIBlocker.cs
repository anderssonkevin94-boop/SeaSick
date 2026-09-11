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
        /// The raw caller strings, as handed over by the compiler. Storing
        /// these instead of a formatted label is the whole point: see `Block`.
        static readonly List<string> files = new List<string>();
        static readonly List<string> members = new List<string>();
        static readonly List<string> owners = new List<string>();

        /// file → member → "HelmInput.OnGUI". Keyed on the literals themselves,
        /// which are compile-time constants from a fixed set of call sites, so
        /// after the first frame every label is a lookup and nothing is built.
        static readonly Dictionary<string, Dictionary<string, string>> labels =
            new Dictionary<string, Dictionary<string, string>>();

        static readonly char[] Separators = { '/', '\\' };
        static int frame = -1;

        /// Every rect claimed this frame, and who claimed it. `HudOverlapProbe`
        /// reads these to find two live controls sitting on each other — the
        /// bug that had "come alongside" and "space · lock on" overlapping by
        /// 34 px, where whichever drew second in the mouse pass took the tap.
        public static IReadOnlyList<Rect> Claimed => rects;

        /// Resolved on demand, because the probe is the only thing that reads
        /// them and it reads them once a repaint — not twenty times an event.
        public static IReadOnlyList<string> Owners
        {
            get
            {
                while (owners.Count < files.Count)
                {
                    int i = owners.Count;
                    owners.Add(Label(files[i], members[i]));
                }
                return owners;
            }
        }

        /// Call from OnGUI with the button's GUI-space rect.
        ///
        /// The caller's file and method arrive for free: the compiler fills
        /// them in as string literals, so naming who owns a rect costs no
        /// allocation and no change at any of the twenty call sites.
        ///
        /// **Storing the label, though, was not free.** This used to trim the
        /// path here — `new[] { '/', '\\' }`, two `Substring`s and a concat —
        /// on every one of ~20 call sites on EVERY IMGUI event, to build a
        /// debug string only `HudOverlapProbe` ever looks at. The raw literals
        /// cost nothing to keep; the formatting moved behind `Owners`.
        public static void Block(Rect guiRect,
            [CallerFilePath] string file = null,
            [CallerMemberName] string member = null)
        {
            if (frame != Time.frameCount)
            {
                rects.Clear();
                files.Clear();
                members.Clear();
                owners.Clear();
                frame = Time.frameCount;
            }
            rects.Add(guiRect);
            files.Add(file);
            members.Add(member);
        }

        static string Label(string file, string member)
        {
            if (string.IsNullOrEmpty(file)) return member ?? "?";
            if (!labels.TryGetValue(file, out var byMember))
                labels[file] = byMember = new Dictionary<string, string>();
            string key = member ?? "?";
            if (byMember.TryGetValue(key, out string cached)) return cached;

            int slash = file.LastIndexOfAny(Separators);
            string name = slash >= 0 ? file.Substring(slash + 1) : file;
            if (name.EndsWith(".cs")) name = name.Substring(0, name.Length - 3);
            cached = name + "." + key;
            byMember[key] = cached;
            return cached;
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
