using UnityEngine;

namespace SeaSick.World
{
    /// The one-line seam between a raised pier and the dock registry:
    /// a pier that stands registers a `Dock` on its own GameObject, so
    /// the ship can come alongside it the way she does at home, and the
    /// dock goes when the pier does.
    public static class PierDocks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Wire()
        {
            Outpost.RegisterPierDock = b =>
            {
                var pier = b != null ? b.GetComponent<Pier>() : null;
                if (pier == null) return null;
                return Dock.Create(b.gameObject, pier.LandEnd, pier.SeaEnd, pier.Berth,
                                   pier.HeadingDir, pier.SeaEnd.y, BuildPlans.PierBerthDepth);
            };
            Outpost.UnregisterPierDock = b =>
            {
                // Usually the GameObject is already dying and takes the dock
                // with it; this is for a pier removed while it stands.
                var d = b != null ? b.GetComponent<Dock>() : null;
                if (d != null) Dock.Remove(d);
            };
        }
    }
}
