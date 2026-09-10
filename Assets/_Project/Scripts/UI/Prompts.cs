using UnityEngine;

namespace SeaSick.UI
{
    /// The one place a contextual prompt may appear: bottom centre, just above
    /// the helm and the broadside buttons, and never more than one at a time.
    ///
    /// Four systems used to draw there and none of them knew about the others.
    /// `AnchorController` put "come alongside" at `h − 6.9u`, `CombatLock` put
    /// "space · lock on" at `h − 8.2u`, and at 1080x2340 those two rects
    /// overlap by 34 px — so a tap meant for the lock could come alongside
    /// instead. `Bilge` and `SalvageSpawner` both wrote into the middle of the
    /// water at 0.60 and 0.62 of screen height.
    ///
    /// **Priority, not position, is what a caller chooses now.** Everything
    /// asks for the slot; the highest bidder gets it and the rest stay silent.
    /// That is a design statement as much as a layout one: at any moment there
    /// is exactly one thing the game is asking you to do, and coming alongside
    /// outranks locking a gun, which outranks throwing cargo over the side.
    ///
    /// ## Why the winner is decided from LAST frame
    ///
    /// Unity does not order `OnGUI` between components, so "highest bid so far
    /// this frame" would hand the slot to whoever happened to run first. The
    /// winner is therefore the highest bid of the PREVIOUS frame, which every
    /// caller can agree on regardless of order. The cost is that a new prompt
    /// appears one frame late — invisible — and the gain is that within a
    /// frame the answer is stable across IMGUI's Layout/Repaint/mouse passes,
    /// which is what `GUI.Button` needs to register a press at all.
    ///
    /// ## Callers must offer on EVERY event
    ///
    /// Bidding is free (an integer compare), so bid before the usual
    /// `Event.current.type != EventType.Repaint` guard. A caller that only
    /// bids on Repaint owns the slot on repaint frames and loses it on the
    /// mouse-up that would have pressed its button.
    public static class Prompts
    {
        /// Higher wins. Named rather than numbered so the ranking reads as the
        /// design decision it is.
        public static class Rank
        {
            /// A line of text about something that just happened. Loses to
            /// anything the player can act on.
            public const int Toast = 10;
            /// Throw cargo over the side — always available, never urgent.
            public const int Jettison = 20;
            /// Lock a target, release a lock.
            public const int Combat = 30;
            /// Land, come alongside, cast off, recall the crew. This is the
            /// one that moves the ship, so it outranks everything.
            public const int Anchor = 40;
        }

        static int frame = -1;
        static int bestThisFrame = int.MinValue;
        static int winner = int.MinValue;

        static void Sync()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            winner = bestThisFrame;
            bestThisFrame = int.MinValue;
        }

        /// Bid for the slot. Returns true if this caller owns it and may draw.
        /// Call it on every event, not only on Repaint.
        public static bool Claim(int priority)
        {
            Sync();
            if (priority > bestThisFrame) bestThisFrame = priority;
            // On the very first frame nothing has bid yet, so the leading bid
            // of this frame takes it rather than the slot standing empty.
            if (winner == int.MinValue) return priority == bestThisFrame;
            return priority >= winner;
        }

        /// How wide a prompt should be: a thumb's target, capped so it never
        /// becomes the full-width banner this HUD does not use.
        public static float Width =>
            Mathf.Min(HudLayout.Safe.width * 0.68f, HudLayout.Unit * 20f);

        /// Rows stack UPWARD from just above the helm and the broadside
        /// buttons, so the first row a caller asks for is the one nearest the
        /// thumb and the explanation sits above the button rather than under
        /// it.
        public struct Stack
        {
            float bottom;

            public Stack(float bottom) { this.bottom = bottom; }

            /// The next row up. Width defaults to the standard prompt width.
            public Rect Next(float height, float width = 0f)
            {
                if (width <= 0f) width = Width;
                bottom -= height;
                var r = HudLayout.Centred(bottom, width, height);
                bottom -= HudLayout.Gap;
                return r;
            }

            /// Skip a row's worth of space without drawing in it.
            public void Skip(float height) => bottom -= height + HudLayout.Gap;
        }

        /// Open a stack at the top of the bottom clusters.
        public static Stack Begin() =>
            new Stack(HudLayout.BottomClustersTop - HudLayout.Gap);
    }
}
