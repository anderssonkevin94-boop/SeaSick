using UnityEngine;

namespace SeaSick.Crew
{
    /// The ship's answer to "how much crew have I actually got right now?".
    ///
    /// Replaces MutinyController. Nothing here punishes the player directly —
    /// there is no escalation ladder and no seizing of the helm. The crew
    /// simply become unable to do the work, and every system that needs hands
    /// (sails, oars, guns) asks this class before it acts. Losing the ship is
    /// therefore something the player watches happen to their own capability,
    /// not something the game does to them.
    ///
    /// The captain always keeps the tiller. A crew that has entirely given up
    /// leaves a ship that still steers and still carries whatever canvas was
    /// last set — slow, blind and toothless, but never stranded.
    public class CrewRoster : MonoBehaviour
    {
        [Tooltip("Guns are worked by named crew, in this order. Anyone left over is a sail hand.")]
        [SerializeField] bool assignGunsInOrder = true;

        CrewAgent[] crew;

        /// Everyone aboard or ashore, in scene order — the gun assignment index.
        public CrewAgent[] All
        {
            get
            {
                if (crew == null || crew.Length == 0)
                    crew = GetComponentsInChildren<CrewAgent>(true);
                return crew;
            }
        }

        public int CrewCount => All.Length;

        /// Standing at a post and able to work it.
        public int AbleCount
        {
            get
            {
                int n = 0;
                foreach (var c in All) if (c != null && c.Available) n++;
                return n;
            }
        }

        /// How much of a full crew's work is actually getting done: time lost
        /// to the rail and the slowness of queasy hands, in one number.
        /// This is what sail changes and the oars scale against.
        public float Labour01
        {
            get
            {
                var all = All;
                if (all.Length == 0) return 0f;
                float sum = 0f;
                foreach (var c in all) if (c != null) sum += c.WorkRate01;
                return sum / all.Length;
            }
        }

        public float AverageSickness01
        {
            get
            {
                var all = All;
                if (all.Length == 0) return 0f;
                float sum = 0f;
                foreach (var c in all) if (c != null) sum += c.Sickness01;
                return sum / all.Length;
            }
        }

        public float WorstSickness01
        {
            get
            {
                float worst = 0f;
                foreach (var c in All)
                    if (c != null && c.Sickness01 > worst) worst = c.Sickness01;
                return worst;
            }
        }

        /// Nobody left who will lift anything.
        public bool AllDown => AbleCount == 0;

        public int BailingCount
        {
            get
            {
                int n = 0;
                foreach (var c in All) if (c != null && c.IsBailing) n++;
                return n;
            }
        }

        /// Put `wanted` hands on the buckets and return how many are actually
        /// there. **Spare hands go first, gun crews last, and the aftmost guns
        /// before the forward ones** — so a little water costs you nothing, and
        /// only a serious flood starts silencing the battery. Anyone ashore or
        /// over the side simply isn't available to ask.
        public int AssignBailers(int wanted)
        {
            var all = All;
            if (all.Length == 0) return 0;

            int placed = 0;
            for (int p = 0; p < BailPriority.Length && p < all.Length; p++)
            {
                var c = all[BailPriority[p]];
                if (c == null || !c.IsAboard) continue;

                if (placed < wanted)
                {
                    c.StartBailing();
                    if (c.IsBailing) placed++;
                }
                else c.StopBailing();
            }
            return placed;
        }

        /// Order to pull hands in. Index 4 is the spare (no gun), then the two
        /// aft guns, then the two forward ones.
        static readonly int[] BailPriority = { 4, 3, 1, 2, 0 };

        /// The crew member who works a given gun. Guns are manned by named
        /// people so the loss reads on a body — "that gun is silent because
        /// Pip is at the rail" — rather than as a number going down.
        public CrewAgent GunCrew(int gunIndex)
        {
            var all = All;
            if (all.Length == 0) return null;
            if (!assignGunsInOrder) return null;
            return gunIndex >= 0 && gunIndex < all.Length ? all[gunIndex] : null;
        }

        /// Is the person on this gun able to work it?
        public bool GunManned(int gunIndex)
        {
            var c = GunCrew(gunIndex);
            return c != null && c.Available;
        }

        // No HUD of its own. Who is bailing already reads three ways: the pips
        // in StatusHUD, the water panel, and five people visibly leaving the
        // guns to do it.
    }
}
