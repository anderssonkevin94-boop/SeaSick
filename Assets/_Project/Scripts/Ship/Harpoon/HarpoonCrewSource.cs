using SeaSick.Crew;
using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **The harpooner (2026-10-04, PLAN-harpoon §5, §8.7).** Kevin: *"a
    /// crew member mans it, and the captain fires it, slower, when nobody is
    /// free"*. Lives on the player's hull beside the `HarpoonGun`
    /// (`Combat.PlayerHull` adds both, so every player hull has one through
    /// every refit and load; raiders never do) and is the gun's
    /// `HarpoonGun.CrewSource`.
    ///
    /// **Who** is `HarpoonCrewRules.Pick`: a spare hand first, else a
    /// gunner from the disengaged side while the fight can spare him, else
    /// the captain. **Walk:** the hand is posted at the mount's
    /// `Harpooner_Stand` (`HarpoonGun.BowPost`) and walks there on deck
    /// (`CrewAgent.RelocateStation`, the gun shift's `Relocating` walk:
    /// coaster deck routing, `Available` false on the way); `atGun` is
    /// true once he stands there. While posted, `CrewAgent.HarpoonPosted`
    /// keeps the gun shift (`CannonBattery.RebalanceCrews`) off him.
    /// **Release:** when the gun goes idle he lingers
    /// `HarpoonCrewRules.ReleaseGraceSeconds` (demand back cancels it), then
    /// walks back to the post he came from; a borrowed gunner is handed
    /// straight back to the gun shift. **Rates:** his `WorkRate01`, and an
    /// accuracy that falls with his seasickness.
    ///
    /// Dropped at once (and replaced, or the captain fires) if he goes over
    /// the side, ashore, onto a haul, into the jolly boat or onto the
    /// buckets -- his post is put back under him so every round trip ends
    /// where he came from. A refit that re-posts him (`PostGunCrews`, the
    /// coaster's deck stations) is noticed and he is walked back to the bow;
    /// a moved mount re-posts him. Runtime only: nothing here is saved, so
    /// after a load nobody stands at the harpoon until it has work.
    ///
    /// `Man` runs every frame while the gun has work: the posted hand costs
    /// a few comparisons; the deck is scanned 4x a second at most, into
    /// reused arrays (no garbage).
    public class HarpoonCrewSource : MonoBehaviour, IHarpoonCrewSource
    {
        const float ScanInterval = 0.25f;
        /// (0.2 m)² -- "standing on the same spot" for a post.
        const float SameSpotSq = 0.04f;
        /// A rail to heave over from the bow post: out to his old side.
        const float BowRailOutboard = 0.8f;

        CrewRoster roster;
        CannonBattery battery;

        CrewAgent posted;
        bool postedWasGunner;
        int postedSide = GunCrewShift.None;
        Vector3 prevStation, prevRail, postedAt;
        Cannon prevGun;
        HarpoonCrewRules.ReleaseClock clock;
        float nextScan;

        // Reused scan buffers, sized to the roster.
        CrewAgent[] hands = System.Array.Empty<CrewAgent>();
        bool[] spare = System.Array.Empty<bool>(), free = System.Array.Empty<bool>();
        int[] side = System.Array.Empty<int>();
        float[] dist = System.Array.Empty<float>();

        /// The hand posted at the harpoon now (walking there or at it), or
        /// null: for probes and a later HUD line.
        public CrewAgent Harpooner => posted;

        void OnEnable()
        {
            HarpoonGun.CrewSource = this;
            nextScan = 0f;
        }

        void OnDisable()
        {
            Unpost();
            if (ReferenceEquals(HarpoonGun.CrewSource, this)) HarpoonGun.CrewSource = null;
        }

        bool Ours(HarpoonGun gun) => gun != null && gun.gameObject == gameObject;

        public HarpoonCrew Man(HarpoonGun gun)
        {
            if (!Ours(gun)) return HarpoonCrew.Captain;
            clock.Manned();
            Transform post = gun.BowPost;
            if (post == null) { Unpost(); return HarpoonCrew.Captain; }
            if (roster == null) roster = GetComponent<CrewRoster>();
            if (battery == null) battery = GetComponent<CannonBattery>();
            if (roster == null) { Unpost(); return HarpoonCrew.Captain; }

            if (posted != null && !CanServe(posted)) Unpost();

            // A spare who is posted stays; a borrowed gunner is re-checked
            // against the fight; nobody posted looks for somebody.
            float now = Time.time;
            if ((posted == null || postedWasGunner) && now >= nextScan)
            {
                nextScan = now + ScanInterval;
                Rescan(post);
            }

            if (posted == null) return HarpoonCrew.Captain;
            KeepPosted(post);

            bool atGun = posted.Available && FlatSq(posted.StationLocal - postedAt) < SameSpotSq;
            return new HarpoonCrew
            {
                hand = posted,
                captain = false,
                atGun = atGun,
                workRate = atGun ? posted.WorkRate01 : HarpoonTuning.captainWorkRate,
                accuracy01 = HarpoonCrewRules.Accuracy(posted.Sickness01),
            };
        }

        public void Release(HarpoonGun gun)
        {
            if (!Ours(gun) || posted == null) return;
            clock.Released(Time.time);
        }

        void Update()
        {
            if (posted == null) return;
            if (!CanServe(posted) || clock.Due(Time.time, HarpoonCrewRules.ReleaseGraceSeconds))
                Unpost();
        }

        /// Can still serve at the harpoon: aboard this deck, not on a haul
        /// or in the jolly boat (`CanCrewGun`), and not called to the
        /// buckets (bailing outranks the harpoon).
        static bool CanServe(CrewAgent h) => h != null && h.CanCrewGun && !h.IsBailing;

        void Rescan(Transform post)
        {
            var all = roster.All;
            int n = all.Length;
            if (hands.Length != n)
            {
                hands = new CrewAgent[n];
                spare = new bool[n]; free = new bool[n];
                side = new int[n]; dist = new float[n];
            }

            int current = -1, able = 0;
            Vector3 at = post.position;
            for (int i = 0; i < n; i++)
            {
                var h = all[i];
                hands[i] = h;
                bool ok = h != null && h.CanCrewGun;
                if (h != null && ReferenceEquals(h, posted))
                {
                    current = i;
                    spare[i] = !postedWasGunner;
                    side[i] = postedSide;
                    free[i] = false;
                    if (postedWasGunner && ok) able++;
                }
                else
                {
                    Cannon g = h != null ? h.Gun : null;
                    bool gunner = g != null;
                    spare[i] = !gunner;
                    side[i] = gunner && battery != null ? battery.SideOfGun(g) : GunCrewShift.None;
                    free[i] = ok && h.Available && !h.HarpoonPosted;
                    if (gunner && ok) able++;
                }
                if (h != null)
                {
                    Vector3 d = h.transform.position - at;
                    d.y = 0f;
                    dist[i] = d.magnitude;
                }
                else dist[i] = float.MaxValue;
            }

            int primary = battery != null ? battery.EngagedSide : GunCrewShift.None;
            int secondary = battery != null ? battery.EngagedSecondSide : GunCrewShift.None;
            int need = battery != null ? battery.HandsForFight : 0;
            int pick = HarpoonCrewRules.Pick(n, spare, free, side, dist,
                current, current >= 0 && CanServe(posted), primary, secondary, need, able);

            CrewAgent chosen = pick >= 0 ? hands[pick] : null;
            for (int i = 0; i < n; i++) hands[i] = null;   // hold no stale references
            if (ReferenceEquals(chosen, posted)) return;
            Unpost();
            if (chosen != null) Post(chosen, post);
        }

        void Post(CrewAgent hand, Transform post)
        {
            posted = hand;
            prevGun = hand.Gun;
            postedWasGunner = prevGun != null;
            postedSide = postedWasGunner && battery != null ? battery.SideOfGun(prevGun) : GunCrewShift.None;
            prevStation = hand.StationLocal;
            prevRail = hand.RailLocal;
            hand.HarpoonPosted = true;
            // Facing: a hand with no gun turns to face along the deck --
            // forward, over the gun -- not outboard with a cannon.
            hand.AssignGun(null);
            clock.Manned();
            SendToBow(post);
            // His gun is free now: let the gun shift cover it at once.
            if (postedWasGunner && battery != null) battery.RebalanceCrews();
        }

        /// The posted hand's spot stays the bow: a refit that re-posted him
        /// elsewhere (`PostGunCrews`, the coaster's deck stations) becomes
        /// his new "came from", and a moved mount moves his spot.
        void KeepPosted(Transform post)
        {
            if (FlatSq(posted.StationLocal - postedAt) > SameSpotSq)
            {
                prevStation = posted.StationLocal;
                prevRail = posted.RailLocal;
                if (posted.Gun != null)
                {
                    // Re-posted to a gun (the refit made him a gunner, or
                    // put him back on his own): he goes back to it later.
                    prevGun = posted.Gun;
                    postedWasGunner = true;
                    postedSide = battery != null ? battery.SideOfGun(prevGun) : GunCrewShift.None;
                    posted.AssignGun(null);
                }
                SendToBow(post);
                return;
            }
            Transform deck = posted.transform.parent;
            if (deck != null && FlatSq(deck.InverseTransformPoint(post.position) - postedAt) > SameSpotSq)
                SendToBow(post);
        }

        void SendToBow(Transform post)
        {
            Transform deck = posted.transform.parent;
            if (deck == null) return;
            Vector3 at = deck.InverseTransformPoint(post.position);
            float outward = prevRail.x != 0f ? Mathf.Sign(prevRail.x) : 1f;
            Vector3 rail = at + new Vector3(outward * BowRailOutboard, 0f, 0f);
            postedAt = at;
            posted.RelocateStation(at, rail);
        }

        /// Back to where he came from: his own post (a spare) or the gun
        /// shift's plan (a gunner). A hand who is not at his post (over the
        /// side, bailing) just has it put back under him.
        void Unpost()
        {
            // ReferenceEquals: a destroyed body still has to be forgotten.
            if (ReferenceEquals(posted, null)) return;
            var h = posted;
            bool wasGunner = postedWasGunner;
            posted = null;
            postedWasGunner = false;
            postedSide = GunCrewShift.None;
            clock.Manned();
            nextScan = 0f;
            if (h != null)
            {
                h.HarpoonPosted = false;
                if (prevGun != null) h.AssignGun(prevGun);
                h.RelocateStation(prevStation, prevRail);
            }
            prevGun = null;
            if (wasGunner && battery != null) battery.RebalanceCrews();
        }

        static float FlatSq(Vector3 v) => v.x * v.x + v.z * v.z;
    }
}
