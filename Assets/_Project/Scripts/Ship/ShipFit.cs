using UnityEngine;

namespace SeaSick.Ship
{
    /// What has been DONE to the ship, as opposed to what she is.
    ///
    /// The hull ladder decides her size and everything that follows from it.
    /// These tracks are bought separately and carry across every rung — a
    /// bigger rudder stays a bigger rudder when she is lengthened. That
    /// separation is the point: a player who wants a nimble trader and one who
    /// wants a slow gun platform can be on the same rung and sail differently.
    ///
    /// Each track is gated by something PHYSICAL rather than by a level
    /// number, so the reason a fitting is unavailable is always a fact about
    /// the ship: a second tier of sail needs a mast to set it on, and a heavier
    /// gun needs beam behind it for the recoil.
    ///
    /// **Quantity is not here.** How MANY guns and how many hands she carries
    /// is decided by the bays — see `Shipyard.AddCell` — because that is a
    /// question of space aboard, and space aboard is what the hull ladder is
    /// for. These five tracks are all quality-of-fitting, plus the one
    /// exception of sail plan, which is a count of tiers rather than of masts.
    public enum FitTrack { Rudder, SailPlan, SailArea, Guns, Crew }

    [System.Serializable]
    public class ShipFit
    {
        public const int MaxLevel = 3;

        [SerializeField] int rudder, sailPlan, sailArea, guns, crew;

        public int Level(FitTrack t) => t switch
        {
            FitTrack.Rudder => rudder,
            FitTrack.SailPlan => sailPlan,
            FitTrack.SailArea => sailArea,
            FitTrack.Guns => guns,
            _ => crew,
        };

        public void SetLevel(FitTrack t, int v)
        {
            v = Mathf.Clamp(v, 0, MaxLevel);
            switch (t)
            {
                case FitTrack.Rudder: rudder = v; break;
                case FitTrack.SailPlan: sailPlan = v; break;
                case FitTrack.SailArea: sailArea = v; break;
                case FitTrack.Guns: guns = v; break;
                default: crew = v; break;
            }
        }

        // --- what each track is called at each level -------------------------

        /// The row heading in the yard. The enum name is a C# identifier and
        /// reads like one; this is what the player is actually buying.
        public static string Label(FitTrack t) => t switch
        {
            FitTrack.Rudder => "rudder",
            FitTrack.SailPlan => "sail · number",
            FitTrack.SailArea => "sail · size",
            FitTrack.Guns => "guns · calibre",
            _ => "crew · training",
        };

        public static string Name(FitTrack t, int level) => t switch
        {
            FitTrack.Rudder => new[] { "tiller", "balanced rudder",
                                       "deep rudder", "wheel and quadrant" }[level],
            FitTrack.SailPlan => new[] { "courses", "topsails",
                                         "topgallants", "royals" }[level],
            FitTrack.SailArea => new[] { "working canvas", "deep courses",
                                         "a full suit", "a flying suit" }[level],
            FitTrack.Guns => new[] { "swivels", "4-pounders",
                                     "9-pounders", "18-pounders" }[level],
            _ => new[] { "pressed hands", "seasoned hands",
                         "prime seamen", "a crack crew" }[level],
        };

        // --- effects ---------------------------------------------------------
        // Deliberately modest per level. Five tracks at three levels each is
        // fifteen purchases; if any one of them doubled a stat the hull ladder
        // would stop mattering. Splitting the old single sail track in two did
        // NOT add speed: the 0.08 and 0.12 per level it carried are split
        // across the pair, so a fully-rigged ship is exactly as fast as she
        // was — she now has to buy both halves to get there.

        /// Rudder: how hard she comes about. This is the one the player feels
        /// most directly, because it fights the 1/length the hull imposes.
        public float TurnMultiplier => 1f + 0.18f * rudder;

        /// Sail: more tiers of canvas and bigger canvas on each are both more
        /// drive. Number is gated on masts, size on beam — see `Blocked`.
        public float SpeedMultiplier => 1f + 0.04f * sailPlan + 0.04f * sailArea;
        public float AccelMultiplier => 1f + 0.06f * sailPlan + 0.06f * sailArea;

        /// How much bigger each sail is drawn. A size upgrade the eye cannot
        /// see is a number in a panel, so `SailRig` scales the canvas about
        /// its own foot by this.
        public float SailAreaScale => 1f + 0.15f * sailArea;

        /// Gun calibre, as a uniform scale on the kit gun. Real guns of
        /// different weight of shot are close to geometrically similar, so one
        /// mesh scaled is honest rather than lazy.
        public float GunScale => 0.75f + 0.17f * guns;

        /// Crew training: how much of a full crew's work actually gets done in
        /// a sea. Feeds `CrewMemberDef.ironStomach`, which already halves the
        /// sickness rate at maximum — this is a hook that exists, not a new stat.
        public float IronStomach => Mathf.Clamp01(0.20f + 0.24f * crew);

        // --- gates ------------------------------------------------------------

        /// Beam a sail suit needs under it to stand up to. Sail area heels
        /// her, so the honest gate is her stiffness — and the exporter now
        /// emits real `bm_m` per rung, which IS that.
        ///
        /// Beam is used anyway, deliberately: BM falls on every raise (node
        /// 14 -> 15 drops 3.35 to 2.17), so a BM gate would REVOKE a suit the
        /// player had paid for the moment they added a deck, and nothing here
        /// is refunded. Beam never decreases along the ladder, so a beam gate
        /// can only ever open. Same physics, told in the one direction that
        /// does not take something away.
        static readonly float[] SailBeam = { 0f, 2.8f, 5.5f, 9.0f };
        static readonly float[] GunBeam = { 0f, 3.7f, 6.1f, 7.8f };

        /// Why the next level of a track cannot be had on this hull, or null.
        public string Blocked(FitTrack t, LadderNode n)
        {
            int next = Level(t) + 1;
            if (n == null) return "no hull";
            if (next > MaxLevel) return "nothing better exists";
            switch (t)
            {
                case FitTrack.SailPlan:
                    // One more tier of sail than she has masts is a tier with
                    // nowhere to go.
                    if (next > n.masts)
                        return $"she has {n.masts} mast{(n.masts == 1 ? "" : "s")}; "
                             + $"{Name(t, next)} need {next}.";
                    break;
                case FitTrack.SailArea:
                    if (n.beam < SailBeam[next])
                        return $"{Name(t, next)} would lay her over; that suit "
                             + $"wants {SailBeam[next]:F1} m of beam under it "
                             + $"and she has {n.beam:F1}. Girdle her.";
                    break;
                case FitTrack.Guns:
                    if (n.beam < GunBeam[next])
                        return $"{Name(t, next)} need {GunBeam[next]:F1} m of beam for the "
                             + $"recoil; she has {n.beam:F1}. Girdle her.";
                    break;
                case FitTrack.Rudder:
                    if (next >= 3 && n.length < 15f)
                        return "a wheel and quadrant wants a bigger ship than this.";
                    break;
            }
            return null;
        }

        /// Levels that a smaller hull can no longer carry are dropped back.
        /// Nothing is refunded and nothing is silently kept: a fitting she
        /// cannot support is a fitting she does not have.
        public void ClampTo(LadderNode n)
        {
            foreach (FitTrack t in System.Enum.GetValues(typeof(FitTrack)))
                while (Level(t) > 0)
                {
                    int have = Level(t);
                    SetLevel(t, have - 1);
                    if (Blocked(t, n) == null) { SetLevel(t, have); break; }
                }
        }
    }
}
