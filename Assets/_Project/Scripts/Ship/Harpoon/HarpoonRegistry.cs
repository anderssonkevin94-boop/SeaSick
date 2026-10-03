using System.Collections.Generic;

namespace SeaSick.Ship.Harpoon
{
    /// **Every harpoonable thing afloat right now.** Targets add themselves
    /// when they spawn and remove themselves in `OnDestroy`, so the gun walks
    /// a short list each frame instead of searching the scene.
    public static class HarpoonRegistry
    {
        static readonly List<IHarpoonable> all = new List<IHarpoonable>();
        public static IReadOnlyList<IHarpoonable> All => all;

        public static void Add(IHarpoonable t)
        {
            if (t != null && !all.Contains(t)) all.Add(t);
        }

        public static void Remove(IHarpoonable t) => all.Remove(t);
    }
}
