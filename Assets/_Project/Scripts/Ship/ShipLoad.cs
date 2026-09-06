using UnityEngine;

namespace SeaSick.Ship
{
    /// Everything aboard, and where it sits.
    ///
    /// This is the piece the ship was missing. `rb.mass` used to be the hull's
    /// displacement and nothing else — cargo, crew, guns and flood water
    /// weighed NOTHING — and the sinking a player saw came from three authored
    /// constants pushed into a `SeatOffset`. The ship was not heavier; she had
    /// been asked to look heavier.
    ///
    /// With real mass, nothing has to be authored. `BuoyantBody` already
    /// applies buoyancy per probe from displaced volume, so a heavier ship
    /// settles until she displaces her own weight — Archimedes does the work
    /// that three constants were imitating. And because buoyancy is applied at
    /// each probe's own position, heeling her submerges the lee side more and
    /// rights her: the stability model is emergent, not something to code.
    ///
    /// What this class owns is therefore only the BOOKKEEPING: what is aboard,
    /// what it weighs, and how high it sits. The two numbers that come out —
    /// total mass and the height of the centre of gravity — are the two the
    /// physics needs.
    public class ShipLoad
    {
        // --- what things weigh -------------------------------------------------
        // Chosen so the numbers a player already sees stay legible. A cargo
        // unit is what one hand carries aboard and stows, and half a tonne is
        // a barrel of salt beef or a decent log.
        public const float CargoUnitKg = 500f;
        public const float CrewKg = 90f;                 // a hand and their kit
        /// Gun and carriage by calibre — swivel, 4, 9, 18 pounder.
        static readonly float[] GunKg = { 500f, 1200f, 2000f, 3200f };
        /// Pig iron and shingle in one bay. Denser than cargo by a good
        /// margin, which is the whole point of buying it: a ballast cell is
        /// dead weight you WANT, and it costs you three units of hold.
        public const float BallastPerCellKg = 4000f;

        /// Roll radius of gyration as a fraction of beam. 0.33-0.40 B is the
        /// standard range for a laden hull; 0.38 sits inside it. This is the
        /// one number here that is a rule of thumb rather than a measurement,
        /// and it only scales roll PERIOD — it does not touch her stability.
        const float RollGyradius = 0.38f;

        /// Structure as a fraction of designed displacement. A wooden hull is
        /// heavy for her size; the rest is ballast, stores and cargo.
        ///
        /// **Raised from 0.42 to 0.88 on 2026-09-06, and the reason is that
        /// 0.42 left FIFTY-EIGHT PER CENT of her displacement as ground
        /// ballast sitting at a tenth of her depth.** Over half her mass was
        /// nailed to the bottom, so nothing the player stowed could move her
        /// centre of gravity: measured across every stowage a brig can be
        /// given, GM ranged 1.99 to 2.11 and her roll period did not leave
        /// 4.1-4.2 s. At 0.88 the ballast is a realistic ~12% and the brig
        /// sits near GM 1.1, inside the 0.6-1.2 m band real sailing warships
        /// ran at.
        ///
        /// **This cannot change how she FLOATS**, which is why it is safe:
        /// `BallastKg` is the plug that makes the sum come to `mass_kg`, so
        /// `TotalKg` reduces to `mass_kg - FullCargo + Cargo + ...` with the
        /// lightship term cancelling out entirely — UNLESS the plug clamps at
        /// zero, which is what `Overweight` reports.
        const float LightshipFraction = 0.88f;
        /// Heights above the keel, as fractions of moulded depth.
        const float BallastKG = 0.10f;     // shingle and pig iron, right down
        const float CrewKGf = 0.80f;       // people stand on decks, not in bilges
        /// How much of the structure is the underwater body — keel, floors,
        /// deadwood, lower planking — as against the lighter topsides.
        const float HullBelowShare = 0.65f;

        readonly LadderNode n;
        public ShipLoad(LadderNode node) { n = node; }

        /// Height of the structure's own centre of gravity above the keel.
        ///
        /// **Not a flat fraction of moulded depth**, which is what it was, and
        /// that rule does not survive the ladder: 0.48 x depth is 2.50 m on the
        /// brig, which is right, and 5.95 m on the three-decker — ABOVE her
        /// metacentre at 5.63 — so her own timber made her unstable and she
        /// rolled with a 21-second period. Depth outruns KM as she grows (the
        /// brig's depth/KM is 1.52, the three-decker's 2.20), so no single
        /// fraction can serve both ends of the ladder.
        ///
        /// Split her where the timber actually is instead. The underwater body
        /// is the heavy part and is centred about half her draft; the topsides
        /// are lighter planking and are centred half way up her freeboard.
        /// That scales with the two things that actually differ between a boat
        /// and a first-rate, and it needs no cap to keep her upright.
        float LightshipKGm
        {
            get
            {
                float free = Mathf.Max(0.01f, n.depth - n.draft);
                return HullBelowShare * (0.5f * n.draft)
                     + (1f - HullBelowShare) * (n.draft + 0.5f * free);
            }
        }

        public int CargoUnits, Crew, Guns, GunLevel;
        public float FloodTonnes;
        /// Bays the player has filled with pig iron.
        public int BallastCells;

        // --- WHERE it all sits ------------------------------------------------
        //
        // Each of these is a height above the keel and a station along her,
        // both set by the yard from the CELLS the thing actually occupies.
        // Every one falls back to the old constant when it is left at zero, so
        // a caller that only knows quantities still gets the behaviour it
        // always had — `GunKGm` already worked this way and the rest now match.
        //
        /// Height of the guns above the keel, averaged. A battery on an upper
        /// deck is the single most destabilising thing aboard.
        public float GunKGm;
        public float GunLCGm;
        /// Where the units ACTUALLY ABOARD are stowed — not the mean of every
        /// hold cell she owns. Six barrels in an eighteen-cell hold sit in the
        /// six lowest of them, and that is a different centre of gravity.
        public float CargoKGm, CargoLCGm;
        public float CrewKGm, CrewLCGm;
        public float BallastKGm, BallastLCGm;

        // --- the sums -----------------------------------------------------------

        public float LightshipKg => n.mass_kg * LightshipFraction;
        public float CargoKg => CargoUnits * CargoUnitKg;
        public float CrewMassKg => Crew * CrewKg;
        public float GunsKg => Guns * GunKg[Mathf.Clamp(GunLevel, 0, 3)];
        public float FloodKg => FloodTonnes * 1000f;
        /// Pig iron the player has loaded, over and above her ground ballast.
        public float PlacedBallastKg => BallastCells * BallastPerCellKg;

        /// Ballast is chosen so that with a FULL hold she floats exactly on her
        /// drawn waterline. That is what a designed waterline MEANS, and it
        /// gives the right story for free: empty she rides high, laden she is
        /// on her marks, overloaded she is under them.
        /// How many units her hold actually holds. Set by the yard from the
        /// bays she has, because ballast has to be computed against the hold
        /// she HAS, not against a guess at a typical one.
        public int FullCargoUnits;

        public float BallastKg => Mathf.Max(0f, BallastWanted);

        /// What the plug would be before it is clamped. Negative means her
        /// designed fit-out already weighs more than she was drawn to
        /// displace, so there is no ballast left to take out and she simply
        /// floats deep.
        float BallastWanted => n.mass_kg - LightshipKg - GunsKg - CrewMassKg
                             - FullCargoUnits * CargoUnitKg;

        /// She has run out of ballast to give back. Past this point the
        /// lightship fraction DOES start to affect how she floats, and the
        /// "a full hold puts her on her marks" invariant no longer holds.
        public bool Overweight => BallastWanted < 0f;

        /// Where she floats with nothing in the hold, and with it full. The
        /// span between them is the trim the player can see.
        public float SinkageEmptyM => n.WaterlineForMass(TotalKg - CargoKg);
        public float SinkageFullM =>
            n.WaterlineForMass(TotalKg - CargoKg + FullCargoUnits * CargoUnitKg);

        public float TotalKg => LightshipKg + BallastKg + PlacedBallastKg
                              + GunsKg + CrewMassKg + CargoKg + FloodKg;

        /// Centre of gravity above the keel, metres. Guns high and ballast low
        /// is the whole of it — fill your upper decks with 18-pounders and she
        /// gets tender, which is a decision the bay board already lets you make.
        public float KGm { get { var w = Weigh(); return w.kg > 1f ? w.h : n.depth * 0.5f; } }

        /// Longitudinal centre of gravity, in ship-local z, measured from the
        /// hull origin — which is amidships at her drawn waterline.
        ///
        /// Reported as `TrimM` rather than used raw, because the hull origin
        /// is NOT her centre of buoyancy and the manifest carries no LCB. What
        /// can be said honestly is where she sits relative to being evenly
        /// stowed, and that is what the player is actually changing.
        public float LCGm { get { var w = Weigh(); return w.kg > 1f ? w.z : 0f; } }

        /// One pass over everything aboard: total mass, and the height and
        /// station of its centre. Every category is (weight, how high, how far
        /// along), so adding one is adding a line.
        (float kg, float h, float z) Weigh()
        {
            float d = n.depth;
            float m = 0f, mh = 0f, mz = 0f;
            void Add(float kg, float h, float z)
            { m += kg; mh += kg * h; mz += kg * z; }

            // The hull herself is centred, by construction — she is what the
            // stations were lofted around. Her being 42% of the mass is also
            // why moving cargo shifts trim a little and not absurdly.
            Add(LightshipKg, LightshipKGm, 0f);
            Add(BallastKg, d * BallastKG, 0f);
            Add(PlacedBallastKg, BallastKGm > 0.01f ? BallastKGm : d * BallastKG,
                BallastLCGm);
            Add(GunsKg, GunKGm > 0.01f ? GunKGm : d * 0.55f, GunLCGm);
            Add(CrewMassKg, CrewKGm > 0.01f ? CrewKGm : d * CrewKGf, CrewLCGm);
            Add(CargoKg, CargoKGm > 0.01f ? CargoKGm : d * 0.22f, CargoLCGm);
            // Free water lies in the bilge — low, but it also destroys
            // stability by sloshing. The height is honest; the free-surface
            // effect is left for the damage model.
            Add(FloodKg, d * 0.06f, 0f);
            return m > 1f ? (m, mh / m, mz / m) : (m, d * 0.5f, 0f);
        }

        /// The station an EVENLY stowed ship's centre of gravity lands on.
        /// Subtracted from `LCGm` so that stowing her evenly reads as no trim
        /// at all, whatever her stations happen to be numbered.
        public float NeutralLCGm
        {
            get
            {
                float dead = TotalKg - LightshipKg - BallastKg - FloodKg;
                return TotalKg > 1f ? dead * n.BayMeanX / TotalKg : 0f;
            }
        }

        /// How far her centre of gravity is from evenly stowed, along her.
        /// Positive is weight aft: bow up, slow to answer, tracks straight.
        /// Negative is down by the head: she gripes and wants to broach.
        public float TrimM => LCGm - NeutralLCGm;

        /// Metacentric height. Positive is stable; below about 0.3 m she is
        /// tender and rolls slowly and far; negative and she will not stand up.
        public float GMm => n.km_above_keel_m - KGm;

        /// How long one full roll takes, in seconds — and the ONLY part of
        /// stability the player can feel without an instrument.
        ///
        /// `T = 2*pi*k / sqrt(g*GM)`. A stiff ship snaps back and forth and a
        /// tender one wallows, so the same fact that says "she is close to
        /// going over" also says "she rolls beautifully" — which is exactly
        /// the trap it was in life. Zero when she has no positive stability
        /// left, because there is no period to speak of: she just goes.
        public float RollPeriodS
        {
            get
            {
                float gm = GMm;
                if (gm <= 0.01f) return 0f;
                return 2f * Mathf.PI * (RollGyradius * n.beam)
                       / Mathf.Sqrt(9.81f * gm);
            }
        }

        /// Where her waterline actually sits, relative to her drawn one.
        /// Positive means sunk past her marks.
        public float SinkageM => n.WaterlineForMass(TotalKg);

        /// Centre of mass in SHIP-local coordinates. The hull's origin is her
        /// drawn waterline, so the keel is at -draft, and +z is forward.
        ///
        /// The fore-aft term is `TrimM`, not `LCGm`: see `NeutralLCGm`. Using
        /// the raw LCG would give every ship on the ladder a permanent list by
        /// the stern purely because her bays are not numbered symmetrically.
        public Vector3 CentreOfMassLocal =>
            new Vector3(0f, KGm - n.draft, TrimM);
    }
}
