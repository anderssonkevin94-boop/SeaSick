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

        /// file → member → "SeaStick.Draw". Keyed on the literals themselves,
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

        /// **The sheet is not an IMGUI control, so it cannot call `Block`.**
        ///
        /// `SheetHost` draws the sheet with UI Toolkit, and a runtime UI
        /// Toolkit panel does not stop anything else from reading
        /// `Mouse.current` / `Touchscreen.current` — it only consumes events
        /// for scripts that ask an `EventSystem` or `panel.Pick`. Nothing in
        /// `CameraRig` does, so a swipe that started on the card panned the
        /// island underneath it (Kevin, phone, 2026-09-22).
        ///
        /// It is read here rather than pushed from `SheetHost` on purpose:
        /// `Block`'s list is cleared by whichever claimant calls first in a
        /// frame, so a registrant outside the IMGUI pass has to land at
        /// exactly the right point in the frame to survive. Pulling the two
        /// statics the host already publishes has no such ordering to get
        /// wrong, and no staleness — `FrameRect` is written in `LateUpdate`,
        /// before the next frame's input read.
        ///
        /// It is deliberately NOT added to `Claimed`: that list is the
        /// overlap probe's inventory of INTERACTIVE IMGUI controls, and the
        /// sheet frame covers a third of the screen, so every control the
        /// probe found inside it would read as a collision. `HudLayout`
        /// already keeps the IMGUI HUD out of the frame via `ClaimSheet`.
        ///
        /// GUI space (origin top-left), which is what `FrameRect` already is.
        public static bool SheetBlocked(Vector2 guiPoint)
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return true;
            if (Sheets.SeaLedger.IsOpen) return true;   // the sea drawer's scrim, likewise
            // The thumb bar and the placement card (zero rects while hidden;
            // placement shows them with no camp, so this is not gated on Active).
            if (Sheets.ThumbBar.Blocks(guiPoint)) return true;
            // The sea combat row (Fire port / Lock / Fire stbd, phase 6) and its note.
            if (Sheets.CombatHud.Blocks(guiPoint)) return true;
            // The sea HUD (phase 6): top bar, alert chip, helm row, and the
            // action card while it is a button.
            if (Sheets.SeaHud.Blocks(guiPoint)) return true;
            if (Sheets.MidnightLandHud.Active && (Sheets.MidnightLandHud.NavigationRect.Contains(guiPoint)
                || Sheets.MidnightLandHud.ResourcesRect.Contains(guiPoint))) return true;
            return Sheets.SheetHost.FrameOpen
                && Sheets.SheetHost.FrameRect.Contains(guiPoint);
        }

        /// pointerPos is Input System screen space (origin bottom-left).
        public static bool Blocked(Vector2 pointerPos)
        {
            var gui = new Vector2(pointerPos.x, Screen.height - pointerPos.y);
            if (SheetBlocked(gui)) return true;
            if (Time.frameCount - frame > 1) return false;
            foreach (var r in rects)
                if (r.Contains(gui)) return true;
            return false;
        }
    }
}
