using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;

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
        AnchorController anchor;
        ShipMotor motor;

        void Start()
        {
            anchor = GetComponent<AnchorController>();
            motor = GetComponent<ShipMotor>();
        }

        /// **Phase 5a.** The scripted first man-overboard is bookkept per
        /// ship here — one roster, one voyage's worth of "how long have we
        /// been sailing calmly" (`FirstOverboard.Tick`). **Phase 6** ticks
        /// the cargo-lashing meter alongside it — same "one ship's worth of
        /// bookkeeping" reasoning.
        void Update()
        {
            if (anchor == null) anchor = GetComponent<AnchorController>();
            if (motor == null) motor = GetComponent<ShipMotor>();
            FirstOverboard.Tick(this, anchor, Time.deltaTime);
            CargoLashing.Tick(motor, anchor, Time.deltaTime);
            JollyBoatDispatch.Tick(this, motor, Time.deltaTime);
        }

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

        /// Forget the cached roster and count again.
        ///
        /// `All` caches on first access and never looked again, which was right
        /// while the crew were a fixed set placed in the scene. Once the ship
        /// can be given more berths mid-voyage, a roster that answers from a
        /// cache reports the crew she had when somebody first asked — and the
        /// guns are assigned FROM that list, so new hands would never be given
        /// a gun to work.
        public void Refresh() => crew = null;

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
        ///
        /// **2026-10-03: by the gun he is AT, not his berth number.** This
        /// was a fixed table of roster indices ({4,3,1,2,0}: the five berths
        /// of the authored gun ship), which stopped being true the moment
        /// gunners could walk to the engaged side (`CannonBattery.
        /// RebalanceCrews`): the "aft gun" hand might be standing at a
        /// forward gun on the other side. Now: every hand with no gun
        /// (`CrewAgent.Gun` null) in roster order, then the gunners by their
        /// CURRENT gun's ship-local z, aftmost first, ties to the higher
        /// index -- which reproduces the old table exactly on the authored
        /// four. One change on purpose: a big crew's extra spares used to
        /// come after every gunner (the table only knew berth 4); they are
        /// spare, so they now go before the guns, as the rule above says.
        public int AssignBailers(int wanted)
        {
            var all = All;
            if (all.Length == 0) return 0;

            if (bailOrder.Length != all.Length) bailOrder = new int[all.Length];
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Gun == null) bailOrder[n++] = i;
            int spares = n;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Gun != null) bailOrder[n++] = i;
            // Insertion sort of the gunners (a handful; no garbage).
            for (int a = spares + 1; a < n; a++)
            {
                int v = bailOrder[a];
                int b = a - 1;
                while (b >= spares && BailsBefore(all, v, bailOrder[b]))
                {
                    bailOrder[b + 1] = bailOrder[b];
                    b--;
                }
                bailOrder[b + 1] = v;
            }

            int placed = 0;
            for (int k = 0; k < n; k++) Consider(all[bailOrder[k]], wanted, ref placed);
            return placed;
        }

        int[] bailOrder = System.Array.Empty<int>();

        /// Gunner `x` goes to the buckets before gunner `y`: his gun is
        /// further aft, or level with `y`'s and he is the higher index.
        static bool BailsBefore(CrewAgent[] all, int x, int y)
        {
            float zx = all[x].Gun.transform.localPosition.z;
            float zy = all[y].Gun.transform.localPosition.z;
            if (!Mathf.Approximately(zx, zy)) return zx < zy;
            return x > y;
        }

        static void Consider(CrewAgent c, int wanted, ref int placed)
        {
            if (c == null || !c.IsAboard) return;
            if (placed < wanted)
            {
                c.StartBailing();
                if (c.IsBailing) placed++;
            }
            else c.StopBailing();
        }

        /// The crew member who works a given gun. Guns are manned by named
        /// people so the loss reads on a body — "that gun is silent because
        /// Pip is at the rail" — rather than as a number going down.
        /// 2026-10-03: this is the gun's HOME hand. In a fight gunners walk
        /// to the engaged side's empty guns (`CannonBattery.RebalanceCrews`),
        /// and a gun is manned by whoever stands at it, not by this answer.
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
