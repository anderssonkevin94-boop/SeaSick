namespace SeaSick.Ship.Modular
{
    /// **The shipyard modal's hold on the world** (A8, gameplay half).
    /// Astra's modal calls `SetWorldInputBlocked(true)` when it opens and
    /// `false` when it closes. While blocked: the helm (stick, telegraph
    /// keys, row), the chase camera's pinch/wheel zoom, the island camera's
    /// gestures, the combat lock key and the broadside keys all do nothing.
    /// Time is NOT scaled. UI-side world pickers (WorldPicker, Hand,
    /// CampSiting, WallSiting) should also check `WorldInputBlocked`; they
    /// are Astra's files and were not edited (see docs/SHIPYARD-API.md).
    public static class ShipyardSession
    {
        public static bool WorldInputBlocked { get; private set; }

        public static void SetWorldInputBlocked(bool blocked) { WorldInputBlocked = blocked; }

        /// Statics outlive play mode here (domain reload is off).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() { WorldInputBlocked = false; }
    }
}
