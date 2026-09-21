using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// **A one-frame request: "open the sheet on this building."**
    ///
    /// Kevin, 2026-09-21: *"once the campfire is done I want to press on the
    /// campfire to get the options menu for building."* `IslandInput`
    /// resolves a tap the same way the Hand's own cursor does (`HandTargets.
    /// Resolve`, via `Hand.Preview`) and, when the tap landed on a standing
    /// building rather than a villager, calls `Open` here instead of moving
    /// the follow camera. `CampSheet` is the one place that actually shows a
    /// menu, and it owns no input of its own -- it polls `Consume` at the top
    /// of its own `OnGUI`/`Update` and, on a hit for the outpost it is
    /// already showing, expands itself and switches to the building's row
    /// (the build list, for the fire).
    ///
    /// A static mailbox rather than an event so that a request raised before
    /// the sheet exists (scene still loading a frame late) is not silently
    /// lost -- it just sits here, for exactly one frame, until something
    /// reads it or the frame moves on. **One frame only**: `Consume` clears
    /// it the instant it is read, and `Outpost`/`Building` are also dropped
    /// once `Frame` falls behind `Time.frameCount`, so a sheet that is not
    /// polling every frame (a dev tool open, the sheet not yet spawned) never
    /// acts on a tap that happened two islands ago.
    public static class BuildMenuRequest
    {
        public static Outpost Outpost;
        public static Building Building;
        public static int Frame = -1;

        /// Raise the request. Safe to call every frame a tap lands on a
        /// building; only the LAST call in a frame survives, which is
        /// correct -- a frame has at most one tap.
        public static void Open(Outpost o, Building b)
        {
            if (o == null || b == null) return;
            Outpost = o;
            Building = b;
            Frame = Time.frameCount;
        }

        /// Read and clear. False (with both `out` params null) if nothing
        /// was raised this frame -- a request from an earlier frame that
        /// nobody consumed is stale and must not fire late.
        public static bool Consume(out Outpost o, out Building b)
        {
            if (Frame == Time.frameCount && Outpost != null && Building != null)
            {
                o = Outpost;
                b = Building;
                Outpost = null;
                Building = null;
                Frame = -1;
                return true;
            }
            o = null;
            b = null;
            return false;
        }
    }
}
