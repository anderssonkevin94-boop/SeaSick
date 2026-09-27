namespace SeaSick.Ship.Overboard
{
    /// **"Sailing live" (D2, docs/PLAN-DEATH-RESCUE.md)** -- the one guard
    /// every overboard system checks before it does anything at all: never
    /// during `AwayProgress`'s offline catch-up, never while paused, and
    /// only while the ship herself is actually under way (not anchored, not
    /// docked, not mid-manoeuvre ashore).
    public static class Sailing
    {
        public static bool IsLive(AnchorController anchor)
        {
            if (UnityEngine.Time.timeScale <= 0f) return false;
            if (SeaSick.Save.AwayProgress.Running) return false;
            if (anchor == null) return true; // no anchor found: assume a skiff with none, sailing
            return anchor.CurrentState == AnchorController.State.Underway;
        }

        /// Roughly "night" for grip/swim-timer purposes -- no fine dusk/dawn
        /// blend, this only multiplies a risk knob.
        public static bool IsNight =>
            SeaSick.World.TimeOfDay.Hour < 6f || SeaSick.World.TimeOfDay.Hour >= 20f;

        /// 0..1, or 0 if nothing is tracking sea state yet.
        public static float Storminess01 =>
            SeaSick.Ocean.SeaStateController.Instance != null
                ? SeaSick.Ocean.SeaStateController.Instance.Storminess01 : 0f;
    }
}
