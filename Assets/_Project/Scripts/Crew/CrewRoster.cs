using SeaSick.UI;
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

        public int BrokenCount
        {
            get
            {
                int n = 0;
                foreach (var c in All) if (c != null && c.Broken) n++;
                return n;
            }
        }

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

        void OnGUI()
        {
            // One slim line, and only when the crew are genuinely failing —
            // the state has to be legible before the player wonders why the
            // ship stopped answering.
            int able = AbleCount;
            if (able == CrewCount || CrewCount == 0) return;

            string msg = able == 0
                ? "no one is working the ship"
                : able == 1
                    ? "one hand still on their feet"
                    : $"{able} of {CrewCount} still on their feet";

            float weight = 1f - (float)able / CrewCount;
            UITheme.Banner(0.185f, msg,
                new Color(0.42f, 0.30f, 0.06f, Mathf.Lerp(0.45f, 0.82f, weight)));
        }
    }
}
