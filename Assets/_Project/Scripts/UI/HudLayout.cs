using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.UI
{
    /// Where everything on the HUD is allowed to be.
    ///
    /// Before this existed, fourteen `OnGUI` methods each read
    /// `Screen.width`/`Screen.height` and picked their own corner, and several
    /// picked the same one. Measured at the shipping portrait aspect
    /// (1080x2340, `UITheme.Unit` = 20) the sea foam tuner sat on the minimap
    /// and the ship panel, the Home tab sat on top of the open Yard panel, the
    /// clarity tuner and the dev HUD both sat on the crew pips, and the combat
    /// lock prompt overlapped the "come alongside" button by 34 px — which is
    /// a mis-tap on a control that puts the ship somewhere.
    ///
    /// The fix is not better numbers in fourteen places. It is that the
    /// numbers live in ONE place: this class owns the screen, hands out rects,
    /// and the enum below is the whole HUD read top to bottom. Adding a panel
    /// means adding a slot, and it lands under the thing above it rather than
    /// on top of it.
    ///
    /// **The same trap the rest of the project has:** `StatusHUD` already had
    /// `RightColumnBottom` so `PerfHUD` would not hand-copy its height, and
    /// `HomeTab` carried a comment saying "the Yard tab sits at 0.30 of screen
    /// height and is 2.0 units tall" — a constant copied out of another file,
    /// which is a constant that drifts. Both are gone; both panels take a slot.
    ///
    /// ## How a slot resolves
    ///
    /// A panel calls `Place(slot, width, height)` and gets a rect. The y comes
    /// from the heights of the slots ABOVE it in the same column, as those
    /// panels last reported them. Heights therefore lag by a frame — which is
    /// invisible, and buys the one property that matters: script execution
    /// order does not decide the layout. A slot that stops drawing (the flood
    /// row, a tab that hides itself at the pier) goes stale within a frame and
    /// stops reserving space, so the column closes up behind it.
    ///
    /// ## The safe area is respected
    ///
    /// Every zone is inset into `Screen.safeArea`, so the top-right column
    /// starts under a notch and the bottom clusters sit above a home
    /// indicator, rather than being drawn under either.
    public static class HudLayout
    {
        /// The whole HUD, in the order it stacks. Column membership is the
        /// prefix; order within a column is the order of declaration.
        public enum Slot
        {
            // --- top-left, downward ---
            Crew,

            // --- top-right, downward ---
            Map,
            Wind,
            Ship,
            /// The hold readout chip + its backpack panel (2026-09-28, Kevin:
            /// "I need some way of easily seeing my resources while I'm at
            /// sea"). Right after `Ship` so it stacks directly under the
            /// hull/cargo panel rather than under the minimap or the perf
            /// counter.
            Hold,
            Perf,
            /// The island's crew and their orders, while she lies at a camp.
            /// **In the column, not at a rect of its own** -- the first
            /// version picked its own corner and `HudOverlapProbe` caught it
            /// sitting on 240x60 px of the minimap for thirty frames.
            CampCrew,

            // --- the left tab rail, downward ---
            RailSettings,
            RailYard,
            RailHome,
            /// The pause button (2026-09-26). Last in the rail, so it does
            /// not push the three tabs above it down when a scene has no
            /// `VoyageManager` yet and the chip stays hidden -- see
            /// `PauseChip`. Thumb-reachable for the same reason the rest of
            /// the rail is: this column already sits at `RailTop01` of the
            /// safe area on purpose.
            RailPause,

            // --- bottom-left, upward ---
            Nav,
            Broadside,

            // --- bottom-right, upward ---
            /// The combat lock button (2026-09-27, Kevin: "the click to lock
            /// button should appear somewhere better on the screen where
            /// it's easier to press"). Declared FIRST in this column, so it
            /// sits nearest the safe area's bottom edge and the point-of-sail
            /// panel + oars/ease row stack ABOVE it -- the button a fight
            /// asks you to press keeps the thumb's best real estate, rather
            /// than sharing the shifting bottom-centre prompt slot the way
            /// it did before this HUD had it.
            Lock,
            Helm,
            HelmActions,

            // --- bottom-centre, upward ---
            /// The steering wheel and the engine lever, as one cluster.
            ///
            /// **A column of its own, and the reason is the thumb.** The
            /// wheel has to be where a hand holding the phone can reach it
            /// without letting go, which is the bottom MIDDLE -- not a corner,
            /// where the old telegraph buttons were and where Kevin could not
            /// use them. One slot holds both controls because they are one
            /// instrument: splitting them into two slots would have stacked
            /// the lever above the wheel, and the lever belongs beside it.
            Wheel,
        }

        enum Column { TopLeft, TopRight, Rail, BottomLeft, BottomRight, BottomCentre }

        static Column ColumnOf(Slot s) => s switch
        {
            Slot.Crew => Column.TopRight,
            Slot.Map or Slot.Wind or Slot.Ship or Slot.Hold or Slot.Perf
                or Slot.CampCrew => Column.TopRight,
            Slot.RailSettings or Slot.RailYard or Slot.RailHome or Slot.RailPause => Column.Rail,
            Slot.Nav or Slot.Broadside => Column.BottomLeft,
            Slot.Wheel => Column.BottomCentre,
            _ => Column.BottomRight,
        };

        /// Is this a desk window or a phone held upright?
        ///
        /// The HUD does not have a "mode" it is put into — it asks the window
        /// what shape it is and lays itself out accordingly, which is what
        /// keeps the phone working now that landscape is the shape the game
        /// runs in. Resize the Game view and it re-lays out on the next frame.
        public static bool Wide => Safe.width >= Safe.height;

        /// Where the tab rail starts, as a fraction of the safe area's height.
        ///
        /// **Two numbers, because the scarce axis swaps.** Upright, height is
        /// abundant and the tabs belong down where a thumb reaches — 0.30.
        /// On a desk, height is the axis that runs out: at 1920x1080 a rail
        /// starting at 0.30 leaves the drawer 412 px of panel, and the foam
        /// tuner alone wants about 560, so a tuner opened on a computer would
        /// have been cut off at the bottom. Starting at 0.14 gives it 585 and
        /// costs nothing, because nothing reaches for a tab with a thumb on a
        /// screen you drive with a mouse.
        ///
        /// It is still ONE place: "0.30 of screen height" used to be written
        /// into two files and half-copied into two more.
        public static float RailTop01 => Wide ? 0.26f : 0.30f;

        const int SlotCount = (int)Slot.Wheel + 1;
        static readonly float[] heights = new float[SlotCount];
        static readonly float[] widths = new float[SlotCount];
        static readonly int[] seenAt = new int[SlotCount];

        public static int Unit => UITheme.Unit;
        public static float Pad => Unit * 0.7f;
        /// The breathing space between two panels in the same column.
        public static float Gap => Unit * 0.5f;

        /// The safe area, in GUI space (origin top-left, y grows down).
        public static Rect Safe
        {
            get
            {
                var s = Screen.safeArea;
                // A zero-size safe area happens in some editor states; fall
                // back to the whole screen rather than collapsing the HUD.
                if (s.width < 1f || s.height < 1f)
                    return new Rect(0f, 0f, Screen.width, Screen.height);
                return new Rect(s.x, Screen.height - s.yMax, s.width, s.height);
            }
        }

        /// A slot holds its place for one frame after it last reserved.
        ///
        /// **This took three attempts and the probe caught every one.**
        ///
        /// One frame was right, then wrong, then right again, and the reason
        /// is the rule it depends on. Most of these panels are repaint-guarded
        /// — they skip every event that is not a Repaint, because formatting
        /// strings on the discarded passes was once the largest allocator in
        /// the game. While they also RESERVED only on Repaint, a panel could
        /// go several frames between placements while permanently on screen,
        /// so a one-frame window expired underneath it: `slot:Ship` drew
        /// INSIDE `slot:Map` at (916..1066, 14..86) against (826..1066,
        /// 14..254), and the Yard tab landed exactly on the Settings tab.
        ///
        /// Widening it to a quarter of a second in REAL time fixed neither and
        /// broke something else: real time keeps running while the editor
        /// stalls, so one blocked call left every slot stale and the settings
        /// drawer sized itself against an empty screen — 752..2316 instead of
        /// stopping at 2156, straight across the anchor prompt and the nav
        /// line. A layout that depends on wall-clock is a layout that breaks
        /// whenever anything hitches.
        ///
        /// The real fix was upstream: every panel now RESERVES on every event
        /// and only DRAWS on Repaint (reserving is two float compares; it is
        /// the text meshes that cost). With that true, a live panel is seen
        /// every single frame, one frame is the right window again — and
        /// because it counts frames rather than seconds, no stall can age a
        /// slot out from under the panel that owns it.
        static bool Live(int i) => Time.frameCount - seenAt[i] <= 1;

        /// How tall a slot was when it last drew, or zero if it has stopped.
        public static float HeightOf(Slot s) => Live((int)s) ? heights[(int)s] : 0f;

        /// Every rect handed out this frame, for `HudOverlapProbe`. A slot's
        /// entry is the rect it was ACTUALLY given, not what the stacking
        /// arithmetic says it should have been — which is the difference
        /// between a check that can fail and one that agrees with itself.
        public static IReadOnlyList<Rect> Issued => issuedRects;
        public static IReadOnlyList<string> IssuedTo => issuedNames;

        static readonly List<Rect> issuedRects = new List<Rect>();
        static readonly List<string> issuedNames = new List<string>();
        static int issuedFrame = -1;

        /// Put a panel on the record without giving it a slot.
        ///
        /// The compass tape and the toast are centred rather than stacked, and
        /// they draw labels rather than buttons — so they reach neither
        /// `Place` nor `UIBlocker`, and `HudOverlapProbe` could not see them.
        /// A panel the check cannot see is a panel the check cannot clear.
        public static Rect Declare(string name, Rect r)
        {
            if (issuedFrame != Time.frameCount)
            {
                issuedFrame = Time.frameCount;
                issuedRects.Clear();
                issuedNames.Clear();
            }
            // One entry per name per frame: IMGUI runs OnGUI several times a
            // frame and a panel declares itself on each pass.
            r = AvoidSheet(r);
            int at = issuedNames.IndexOf(name);
            if (at >= 0) issuedRects[at] = r;
            else { issuedNames.Add(name); issuedRects.Add(r); }
            return r;
        }

        // --- the sheet ------------------------------------------------------
        //
        // The island HUD is two toolkits: this one, and the UI Toolkit panel
        // that draws the sheets. Neither can see the other by laying out, so
        // one of them has to be told — and since 2026-09-22 the sheet is the
        // easy one to be told about, because it is a FIXED region of the
        // screen (the bottom third upright, the right third on a desk) rather
        // than a card that resizes itself to its content.

        /// Where the open sheet is, in this class's own space (GUI, origin
        /// top-left), or an empty rect. Set by `SheetHost` every frame it
        /// places the frame; read here and nowhere else.
        public static Rect SheetRect => SheetOpen ? sheetRect : new Rect();
        static Rect sheetRect;
        static int sheetFrame = -1;

        /// True while a sheet is open and has reported its frame. It goes
        /// false on its own within a frame of the sheet closing — the same
        /// staleness rule every slot here follows, so a sheet host that stops
        /// ticking cannot leave a hole in the HUD forever.
        public static bool SheetOpen => Time.frameCount - sheetFrame <= 1
                                        && sheetRect.width > 1f && sheetRect.height > 1f;

        /// Called by `SheetHost` once per frame while a sheet is open.
        public static void ClaimSheet(Rect r)
        {
            sheetRect = r;
            sheetFrame = Time.frameCount;
        }

        /// **Nothing the HUD issues may land on the sheet.**
        ///
        /// Applied in `Declare`, which every rect passes through — slots,
        /// the compass tape, the toast, the anchor prompt — so a panel does
        /// not have to know the sheet exists to stay off it. The push is
        /// along the axis the sheet is docked on: upright it sits across the
        /// bottom, so a bottom cluster goes UP; on a desk it is a column on
        /// the right, so the minimap column goes LEFT.
        ///
        /// It never pushes a panel off the safe area. A HUD squeezed to
        /// nothing is worse than an overlap, and `HudOverlapProbe` will say
        /// so in the one case where it happens.
        static Rect AvoidSheet(Rect r)
        {
            if (!SheetOpen) return r;
            var s = sheetRect;
            if (!r.Overlaps(s)) return r;

            var safe = Safe;
            if (Wide)
            {
                float x = s.xMin - Gap - r.width;
                r.x = Mathf.Max(safe.x, x);
            }
            else
            {
                float y = s.yMin - Gap - r.height;
                r.y = Mathf.Max(safe.y, y);
            }
            return r;
        }

        /// Reserve a panel's place in its column and get the rect to draw in.
        public static Rect Place(Slot slot, float width, float height)
        {
            int i = (int)slot;
            heights[i] = height;
            widths[i] = width;
            seenAt[i] = Time.frameCount;

            var col = ColumnOf(slot);
            var safe = Safe;
            float pad = Pad;
            float offset = 0f;

            // The distance past the column's own edge, from everything that
            // stacks before this slot and is still drawing.
            for (int j = 0; j < SlotCount; j++)
            {
                if (j == i || ColumnOf((Slot)j) != col) continue;
                // Declaration order IS stacking order, in both directions:
                // a top column stacks down from its edge, a bottom column
                // stacks up from its edge, and either way the slots declared
                // first are the ones nearer that edge.
                if (j > i || !Live(j) || heights[j] <= 0f) continue;
                offset += heights[j] + Gap;
            }

            Rect r;
            switch (col)
            {
                case Column.TopLeft:
                    r = new Rect(safe.x + pad, safe.y + pad + offset, width, height);
                    break;
                case Column.TopRight:
                    r = new Rect(safe.xMax - pad - width, safe.y + pad + offset, width, height);
                    break;
                case Column.Rail:
                    r = new Rect(safe.x + pad, safe.y + safe.height * RailTop01 + offset,
                                 width, height);
                    break;
                case Column.BottomLeft:
                    r = new Rect(safe.x + pad, safe.yMax - pad - offset - height, width, height);
                    break;
                case Column.BottomCentre:
                    // Centred on the SAFE area, not on the screen: on a
                    // notched phone held in landscape the two are 100+ px
                    // apart, and a wheel you steer by is the last thing on the
                    // HUD that may sit off the middle.
                    r = new Rect(safe.x + (safe.width - width) * 0.5f,
                                 safe.yMax - pad - offset - height, width, height);
                    break;
                default:
                    r = new Rect(safe.xMax - pad - width, safe.yMax - pad - offset - height,
                                 width, height);
                    break;
            }

            return Declare(slot.ToString(), r);
        }

        /// The top of the bottom clusters — the lowest a centred prompt may
        /// reach without landing on the helm or the broadside buttons. Both
        /// columns are checked because a centred prompt is wide enough to
        /// cross either one in the editor's landscape view, where the same
        /// panels sit far closer together than they do on a phone.
        public static float BottomClustersTop
        {
            get
            {
                var safe = Safe;
                float left = 0f, right = 0f, centre = 0f;
                for (int j = 0; j < SlotCount; j++)
                {
                    if (!Live(j) || heights[j] <= 0f) continue;
                    var c = ColumnOf((Slot)j);
                    if (c == Column.BottomLeft) left += heights[j] + Gap;
                    else if (c == Column.BottomRight) right += heights[j] + Gap;
                    // The wheel is the tallest thing at the bottom of the
                    // screen and it is CENTRED, which is exactly where a
                    // centred prompt goes. Leaving it out of this sum is how
                    // the anchor prompt would have landed on the helm.
                    else if (c == Column.BottomCentre) centre += heights[j] + Gap;
                }
                float reserve = Mathf.Max(Mathf.Max(left, right), centre);
                // **The frame before anything has reserved.** On the first pass
                // after a scene load or a domain reload, whichever panel draws
                // first sees an empty screen — and a panel that sizes itself
                // against an empty screen fills it. The settings drawer did
                // exactly that: 752..2316 instead of stopping at 2156, straight
                // across the anchor prompt and the nav line.
                //
                // So when the bottom of the screen looks empty, trust what was
                // there last frame rather than the emptiness. It can only be
                // wrong for as long as the clusters really are gone, and they
                // reserve again the moment they draw.
                if (reserve <= 0f) reserve = lastBottomReserve;
                else lastBottomReserve = reserve;
                float top = safe.yMax - Pad - reserve;

                // **Upright, the sheet is the bottom of the screen.** It
                // takes the bottom third, so a centred prompt that stopped at
                // the clusters would still land on it. `AvoidSheet` would
                // move the prompt afterwards, but everything that sizes
                // itself against this line (the settings drawer) needs the
                // honest answer BEFORE it picks a height.
                if (SheetOpen && !Wide) top = Mathf.Min(top, sheetRect.yMin - Gap);
                return top;
            }
        }
        static float lastBottomReserve;

        /// The left rail's width — every tab is the same size, so a thumb
        /// finds them without looking.
        static float lastRailStack;

        public static float RailWidth => Unit * 5.4f;
        public static float RailButtonHeight => Unit * 2.8f;

        /// Where a panel opened FROM the rail begins: under the whole rail,
        /// not under its own tab.
        ///
        /// Opening under its own tab is what the Yard panel used to do, and it
        /// put a 500 px wide panel straight across the Home tab beneath it.
        /// The settings drawer would have inherited the same fault the moment
        /// it opened — its tab sits at y 702..742 and the Yard tab at 752..792,
        /// so a panel starting at 752 covers it exactly. The rail is a column
        /// of controls that stay reachable; a panel belongs below all of it.
        public static float RailPanelTop
        {
            get
            {
                var safe = Safe;
                float top = safe.y + safe.height * RailTop01;
                float stack = 0f;
                for (int j = 0; j < SlotCount; j++)
                {
                    if (ColumnOf((Slot)j) != Column.Rail) continue;
                    if (!Live(j) || heights[j] <= 0f) continue;
                    stack += heights[j] + Gap;
                }
                // Same cold-start rule as BottomClustersTop: a drawer that
                // opens before the tabs have reserved opens ON them.
                if (stack <= 0f) stack = lastRailStack;
                else lastRailStack = stack;
                return top + stack;
            }
        }

        /// The right edge of a panel currently open against the left edge —
        /// the settings drawer, the yard — so that centred things centre in
        /// what is LEFT of the screen instead of underneath it.
        ///
        /// `HudOverlapProbe` reported this one as PERMANENT rather than a
        /// transient, which is what made it worth fixing properly: the open
        /// drawer runs x 14..454 and the anchor prompt is centred at x
        /// 340..740, so they cross by 114 px for as long as the drawer is
        /// open. Stopping the drawer above the bottom CLUSTERS was not enough,
        /// because the prompt stack sits above that line, not below it.
        public static float LeftPanelRight
        {
            get => Time.frameCount - leftPanelFrame <= 1 ? leftPanelRight : Safe.x;
            private set { leftPanelRight = value; leftPanelFrame = Time.frameCount; }
        }
        static float leftPanelRight;
        static int leftPanelFrame = -1;

        /// Called by a panel that occupies the left edge, every frame it is open.
        public static void ClaimLeftPanel(Rect r)
        {
            if (Time.frameCount - leftPanelFrame > 1 || r.xMax > leftPanelRight)
                LeftPanelRight = r.xMax;
        }

        /// A rect centred horizontally in the space that is actually free.
        public static Rect Centred(float y, float width, float height)
        {
            var safe = Safe;
            float left = Mathf.Max(safe.x, LeftPanelRight + Gap);
            float room = safe.xMax - left;
            // If a panel has taken so much of the width that centring in the
            // rest would squeeze the control below a thumb, centre on the
            // whole screen anyway and let the probe complain: a prompt too
            // small to hit is worse than a prompt that overlaps.
            if (room < Unit * 12f) { left = safe.x; room = safe.width; }
            width = Mathf.Min(width, room - Pad * 2f);
            return new Rect(left + (room - width) * 0.5f, y, width, height);
        }

        /// The compass tape's place: centred at the top, between the crew pips
        /// and the minimap.
        ///
        /// It stays CENTRED and gives up width instead of shifting sideways,
        /// because the middle of the tape is the bow — an off-centre compass
        /// is a compass that lies about where the ship is pointing. What it
        /// yields to the columns is span, which only costs a couple of degrees
        /// of visible arc.
        public static Rect TopCentre(float preferredWidth, float height)
        {
            var safe = Safe;
            float centre = safe.x + safe.width * 0.5f;

            float leftEdge = safe.x + Pad + WidestLive(Column.TopLeft) + Gap;
            float rightEdge = safe.xMax - Pad - WidestLive(Column.TopRight) - Gap;

            float room = 2f * Mathf.Min(centre - leftEdge, rightEdge - centre);
            float w = Mathf.Max(Unit * 8f, Mathf.Min(preferredWidth, room));

            // Under the top edge with a line's room above it, which is where
            // the 'home' pip's label sits.
            return Declare("Compass", new Rect(centre - w * 0.5f,
                                               safe.y + Pad + Unit * 1.2f, w, height));
        }

        /// Where a transient "that happened" line goes: high and centred,
        /// under the compass tape.
        ///
        /// Deliberately NOT the prompt slot. A toast that competes with the
        /// buttons loses every time the player is near an island or holding a
        /// lock, which is most of the time anything is worth saying. And
        /// deliberately not the middle of the screen, where `SalvageSpawner`
        /// used to put it at 0.62 of screen height — that is the water you are
        /// steering by, and it is also where `Bilge` drew its jettison button.
        public static Rect ToastRow(float height, float width = 0f)
        {
            var safe = Safe;
            if (width <= 0f) width = Unit * 16f;
            return Declare("Toast", Centred(safe.y + safe.height * 0.22f, width, height));
        }

        static float WidestLive(Column col)
        {
            float widest = 0f;
            for (int j = 0; j < SlotCount; j++)
                if (Live(j) && heights[j] > 0f && ColumnOf((Slot)j) == col)
                    widest = Mathf.Max(widest, widths[j]);
            return widest;
        }
    }
}
