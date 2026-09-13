using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// What one hand has been told to do at an outpost.
    public enum OutpostOrder
    {
        /// Standing about. What a hand does when nobody has told it anything,
        /// and what it falls back to when the pile is full or the wood is cut.
        Idle,
        /// Felling timber into the outpost's pile.
        Cut,
    }

    /// One hand left at an outpost.
    ///
    /// Keyed by `name`, which is `CrewMemberDef.displayName`. They are a cast
    /// of twenty with names, not a pool, so the name IS the identity and it
    /// survives a save without an id table nobody would maintain.
    [System.Serializable]
    public class OutpostHand
    {
        public string name;
        public OutpostOrder order = OutpostOrder.Idle;

        /// **Carried, unused, on purpose.** Kevin's call 2026-09-13: a hand can
        /// eventually refuse or leave when starving or badly treated, but for
        /// this pass they only get angry. Nothing reads this yet; it is here
        /// from the start so the save format does not have to change when
        /// something does.
        public float mood = 1f;
    }

    /// **The outpost IS this object. The crew you can see are a rendering of
    /// it.**
    ///
    /// The whole design turns on that inversion. If the walking, chopping
    /// agents were what produced timber, then an island would only pay while
    /// the player stood and watched it — which is the exact opposite of a loop
    /// built around sailing away. So production is arithmetic over elapsed
    /// game time, and a crewman carrying a log is the animation of an
    /// increment that already happened.
    ///
    /// Plain serialisable data with no MonoBehaviour and no scene reference:
    /// an outpost has to keep working while its island is three kilometres
    /// astern and its terrain has streamed out, and it has to survive a save.
    /// There is no save system yet (see docs/PLAN-island-outposts.md, D4) —
    /// this is built to be savable before there is a writer for it, because
    /// retro-fitting serialisation onto live component state is the expensive
    /// version of this job.
    [System.Serializable]
    public class OutpostLedger
    {
        // --- identity --------------------------------------------------------

        /// Rounded world position of the camp, in metres.
        ///
        /// **Not an island index.** Islands are discovered by flood-fill in
        /// whatever order the streamer found them, so an index is not an
        /// identity and a camp keyed to one would silently move house after any
        /// change to the streamer. The seed is stable; the ordering is not.
        public int keyX, keyZ;

        public static int KeyOf(float v) => Mathf.RoundToInt(v);
        public void SetKey(Vector3 at) { keyX = KeyOf(at.x); keyZ = KeyOf(at.z); }
        public bool Matches(Vector3 at) => keyX == KeyOf(at.x) && keyZ == KeyOf(at.z);

        // --- who is here -----------------------------------------------------

        public List<OutpostHand> hands = new List<OutpostHand>();

        public int HandsOn(OutpostOrder order)
        {
            int n = 0;
            foreach (var h in hands) if (h != null && h.order == order) n++;
            return n;
        }

        // --- what it holds ---------------------------------------------------

        /// Whole logs on the ground. What the ship comes to collect.
        public int timber;

        /// Sub-log accrual, kept so a short tick is not rounded away to
        /// nothing. Without it, ticking often would produce less than ticking
        /// rarely, and the two have to agree — see `Tick`.
        public float timberPart;

        /// What this place can keep. A campfire holds ten; a storehouse is how
        /// you raise it. **This ceiling is the whole reason the loop does not
        /// become an idle game**: hands fill it and stop, so the only way to
        /// get more out of an island is to invest in it.
        public int ceiling = CampfireCeiling;

        /// Timber still standing within reach of the camp, in logs.
        ///
        /// Settled 2026-09-13: it depletes and regrows slowly. Without a stock
        /// a camp produces from nothing for ever and the ceiling is left doing
        /// all the balancing on its own.
        public float standing;
        public float standingMax;

        // --- food, carried but not yet wired ---------------------------------

        /// **Declared now, consumed in Phase 2.** Food is settled as local —
        /// berries and wheat off the island itself, no supply run — and
        /// over-capacity hands eat stores and can starve. None of that is wired
        /// here: this pass is the tick, and adding a resource whose per-island
        /// yield has no reader yet would be inventing numbers. The fields exist
        /// so the save format is already the right shape.
        public float food;
        public float foodPart;

        // --- what is built ---------------------------------------------------

        /// Plan ids raised here. `Outpost` owns the objects on the ground; this
        /// is what a save would restore them from.
        public List<string> built = new List<string>();

        // --- the clock -------------------------------------------------------

        /// `TimeOfDay.Seconds` this ledger has been advanced to. Double for the
        /// same reason TimeOfDay is: a float loses resolution over a session.
        ///
        /// **Only ever advanced in whole quanta** — that is what makes the
        /// arithmetic path-independent.
        public double lastTicked;

        // --- the numbers, none of which have been played ---------------------

        /// Seconds of game time in one step. A day is `TimeOfDay.DayLength`
        /// (180 s while testing), so a quantum is eighteen seconds of real time
        /// at the current setting.
        ///
        /// Everything advances in whole quanta and the remainder is carried, so
        /// **one call covering ten days and ten calls covering one day each
        /// produce bit-identical state.** That property is what lets the game
        /// tick a camp whenever it feels like — on arrival, on a map query, on
        /// save — without the answer depending on how often it asked.
        public const float QuantumDays = 0.1f;

        /// Logs a hand fells in a day. **A guess, never played.**
        public const float TimberPerHandPerDay = 4f;

        /// What a campfire watches over. Settled at ten.
        public const int CampfireCeiling = 10;

        /// Timber-grade logs per hectare of the ground the camp works.
        /// **A guess, never played**, and deliberately far under the ~230
        /// trees a hectare the scenery actually draws: most of a wood is not
        /// worth felling, and a stock nobody can exhaust is not a stock.
        public const float StandingPerHectare = 40f;

        /// Share of the stock that comes back in a day. **A guess, never
        /// played.** At 2% a stripped camp is back to half in about a month of
        /// game days, which is meant to be long enough to move on and come
        /// back rather than long enough to forget.
        public const float RegrowthPerDay = 0.02f;

        /// Seed a fresh ledger for a camp on this ground.
        public static OutpostLedger For(Vector3 at, float workedHectares)
        {
            var l = new OutpostLedger();
            l.SetKey(at);
            l.standingMax = Mathf.Max(1f, workedHectares * StandingPerHectare);
            l.standing = l.standingMax;
            l.ceiling = CampfireCeiling;
            l.lastTicked = TimeOfDay.Seconds;
            return l;
        }

        // --- the tick --------------------------------------------------------

        /// Bring this ledger up to `nowSeconds`.
        ///
        /// Safe and free to call as often as you like: it advances in whole
        /// quanta and leaves `lastTicked` on the quantum grid, so a second call
        /// in the same frame does nothing at all, and the state after any
        /// sequence of calls depends only on the elapsed time.
        ///
        /// Nothing here touches the scene, the terrain or a MonoBehaviour, so
        /// it works for an island that is not loaded — which is the point.
        public void Tick(double nowSeconds)
        {
            float dayLength = Mathf.Max(0.0001f, TimeOfDay.DayLength);
            double quantum = QuantumDays * dayLength;
            if (quantum <= 0.0) return;

            double elapsed = nowSeconds - lastTicked;
            if (elapsed <= 0.0)
            {
                // Time can run backwards when a dev tool scrubs the clock.
                // Re-anchor rather than bank a negative debt that would later
                // be paid out as a burst of free timber.
                if (elapsed < 0.0) lastTicked = nowSeconds;
                return;
            }

            long steps = (long)(elapsed / quantum);
            if (steps <= 0) return;

            // A camp left for a very long time still has to answer in one
            // frame. Ten thousand quanta is a thousand game days, far past any
            // session; beyond it the arithmetic has converged on the ceiling
            // anyway, so the clamp cannot change an outcome anyone will see.
            const long MaxSteps = 10000;
            long run = steps > MaxSteps ? MaxSteps : steps;

            for (long i = 0; i < run; i++) Step(QuantumDays);

            // Advance the FULL elapsed quanta even when the run was clamped,
            // or the ledger would owe the same debt again on the next call and
            // never catch up.
            lastTicked += steps * quantum;
        }

        /// One quantum of work. The only place the outpost's state changes.
        void Step(float days)
        {
            // Regrowth first, so a camp that stripped its ground last step has
            // something to cut this one rather than the order of operations
            // deciding the answer.
            if (standingMax > 0f && standing < standingMax)
                standing = Mathf.Min(standingMax, standing + standingMax * RegrowthPerDay * days);

            int cutters = HandsOn(OutpostOrder.Cut);
            if (cutters <= 0) return;

            float room = (ceiling - timber) - timberPart;
            if (room <= 0f) return;

            float want = cutters * TimberPerHandPerDay * days;
            float got = Mathf.Min(want, Mathf.Min(standing, room));
            if (got <= 0f) return;

            standing -= got;
            timberPart += got;

            int whole = Mathf.FloorToInt(timberPart);
            if (whole > 0)
            {
                timber += whole;
                timberPart -= whole;
            }
        }

        /// How much of the ceiling is taken, for anything drawing a gauge.
        public float Fill01 => ceiling > 0 ? Mathf.Clamp01(timber / (float)ceiling) : 0f;

        /// Nothing more to do here: the pile is full, or the wood is gone.
        public bool Stalled => timber >= ceiling || standing < 1f;
    }
}
