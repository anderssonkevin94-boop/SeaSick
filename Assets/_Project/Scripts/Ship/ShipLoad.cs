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

        /// Structure as a fraction of designed displacement. A wooden hull is
        /// heavy for her size; the rest is ballast, stores and cargo.
        const float LightshipFraction = 0.42f;
        /// Heights above the keel, as fractions of moulded depth.
        const float LightshipKG = 0.48f;   // timber is spread the whole depth
        const float BallastKG = 0.10f;     // shingle and pig iron, right down
        const float CrewKGf = 0.80f;       // people stand on decks, not in bilges

        readonly LadderNode n;
        public ShipLoad(LadderNode node) { n = node; }

        public int CargoUnits, Crew, Guns, GunLevel;
        public float FloodTonnes;
        /// Height of the guns above the keel, averaged. Set by the yard from
        /// the bays that carry them: a battery on an upper deck is the single
        /// most destabilising thing aboard.
        public float GunKGm;

        // --- the sums -----------------------------------------------------------

        public float LightshipKg => n.mass_kg * LightshipFraction;
        public float CargoKg => CargoUnits * CargoUnitKg;
        public float CrewMassKg => Crew * CrewKg;
        public float GunsKg => Guns * GunKg[Mathf.Clamp(GunLevel, 0, 3)];
        public float FloodKg => FloodTonnes * 1000f;

        /// Ballast is chosen so that with a FULL hold she floats exactly on her
        /// drawn waterline. That is what a designed waterline MEANS, and it
        /// gives the right story for free: empty she rides high, laden she is
        /// on her marks, overloaded she is under them.
        /// How many units her hold actually holds. Set by the yard from the
        /// bays she has, because ballast has to be computed against the hold
        /// she HAS, not against a guess at a typical one.
        public int FullCargoUnits;

        public float BallastKg => Mathf.Max(0f,
            n.mass_kg - LightshipKg - GunsKg - CrewMassKg
            - FullCargoUnits * CargoUnitKg);

        /// Where she floats with nothing in the hold, and with it full. The
        /// span between them is the trim the player can see.
        public float SinkageEmptyM => n.WaterlineForMass(TotalKg - CargoKg);
        public float SinkageFullM =>
            n.WaterlineForMass(TotalKg - CargoKg + FullCargoUnits * CargoUnitKg);

        public float TotalKg => LightshipKg + BallastKg + GunsKg + CrewMassKg
                              + CargoKg + FloodKg;

        /// Centre of gravity above the keel, metres. Guns high and ballast low
        /// is the whole of it — fill your upper decks with 18-pounders and she
        /// gets tender, which is a decision the bay board already lets you make.
        public float KGm
        {
            get
            {
                float d = n.depth;
                float m = 0f, mh = 0f;
                void Add(float kg, float h) { m += kg; mh += kg * h; }
                Add(LightshipKg, d * LightshipKG);
                Add(BallastKg, d * BallastKG);
                Add(GunsKg, GunKGm > 0.01f ? GunKGm : d * 0.55f);
                Add(CrewMassKg, d * CrewKGf);
                // Cargo stows low, in the hold, which is why a full hold makes
                // a ship STIFFER as well as deeper.
                Add(CargoKg, d * 0.22f);
                // Free water lies in the bilge — low, but it also destroys
                // stability by sloshing. The height is honest; the free-surface
                // effect is left for the damage model.
                Add(FloodKg, d * 0.06f);
                return m > 1f ? mh / m : d * 0.5f;
            }
        }

        /// Metacentric height. Positive is stable; below about 0.3 m she is
        /// tender and rolls slowly and far; negative and she will not stand up.
        public float GMm => n.km_above_keel_m - KGm;

        /// Where her waterline actually sits, relative to her drawn one.
        /// Positive means sunk past her marks.
        public float SinkageM => n.WaterlineForMass(TotalKg);

        /// Centre of mass in SHIP-local coordinates. The hull's origin is her
        /// drawn waterline, so the keel is at -draft.
        public Vector3 CentreOfMassLocal => new Vector3(0f, KGm - n.draft, 0f);
    }
}
