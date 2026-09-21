using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// What one hand at an outpost has been told to do.
    ///
    /// **The order carries a TARGET now**, because "cut wood" was the only job
    /// there was and is not any more. Gathering needs to know what; working
    /// needs to know where.
    public enum OutpostOrder
    {
        /// Standing about. What a hand does when nobody has told it anything,
        /// and what it falls back to when its pile is full or its stock is
        /// gone.
        Idle,
        /// Taking something out of the ground: `target` is the resource.
        /// This was `Cut`, back when timber was all there was.
        Gather,
        /// Cutting and carrying wood into whatever is sited here but not yet
        /// built. **The same work at the same rate as gathering timber** --
        /// the only difference is where the logs land, which is what makes a
        /// half-built camp cost exactly what it looks like it costs.
        Build,
        /// Assigned to a building: `target` is the plan id. What they produce
        /// is the building's business, not theirs.
        Work,
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

        /// What they are gathering, or which building they are assigned to.
        /// Empty for Idle and for Build, which has only ever one thing to
        /// work on.
        public string target = "";

        /// **Carried, unused, on purpose.** Kevin's call 2026-09-13: a hand can
        /// eventually refuse or leave when starving or badly treated, but for
        /// this pass they only get angry. Nothing reads this yet; it is here
        /// from the start so the save format does not have to change when
        /// something does.
        public float mood = 1f;

        /// **Recruited, not shipped, 2026-09-21.** True for a hand
        /// `OutpostLedger.Step` grew from a full pile and an empty bed --
        /// they have a name and a row but no crew body yet. `Outpost` reads
        /// this to know which rows still need one raised for them; it
        /// defaults false so an existing save (every hand in it came off
        /// the ship) does not suddenly read as newborn.
        public bool born = false;

        /// What to call what they are doing, for the list on the right.
        public string Doing
        {
            get
            {
                switch (order)
                {
                    case OutpostOrder.Gather:
                        return string.IsNullOrEmpty(target)
                            ? "gathering" : "gathering " + target.ToLowerInvariant();
                    case OutpostOrder.Build: return "building";
                    case OutpostOrder.Work:
                        string post = BuildPlans.PositionAt(target);
                        return string.IsNullOrEmpty(post) ? "working" : post;
                    default: return "idle";
                }
            }
        }
    }

    /// Something the player has SITED here but nobody has finished building.
    ///
    /// **This is ledger state, not a scene object, and that is the whole
    /// point.** A blueprint you placed and then sailed away from has to still
    /// be there -- half built, with the logs that went into it -- when you come
    /// back three islands later and its terrain has streamed in and out twice.
    /// The ghost standing on the ground is DRAWN from this, the same way the
    /// crew bodies are drawn from the hand rows.
    ///
    /// Position is carried here rather than taken from the outpost's surveyed
    /// clearing because the player chose it. The survey says where a camp COULD
    /// go; this says where it is going.
    [System.Serializable]
    public class PendingBuild
    {
        public string planId;
        /// Where the player put it, world metres.
        public float x, z;
        /// Logs to finish it.
        public int needed;
        /// Logs in it. Whole logs only -- a half-carried log is not a thing
        /// anyone can see, so the fraction lives beside it.
        public int done;
        public float donePart;

        /// **The stone part, 2026-09-21.** Kevin: *"the buildings require
        /// wood and stone, so stone needs to be minable."* Same three fields
        /// as the timber part and paid by the same shape of arithmetic (haul
        /// from the pile, else quarry what stands), kept SEPARATE rather than
        /// folded into one number because the sheet has to be able to say
        /// which of the two a stalled blueprint is waiting on.
        ///
        /// **A save written before this existed restores all three as 0**,
        /// which reads exactly as "this one wanted no stone" -- so an old
        /// half-built hut finishes on the timber it was already owed.
        public int stoneNeeded;
        public int stoneDone;
        public float stoneDonePart;

        /// Which way it faces, world degrees. **Carried here rather than
        /// recomputed**, because since 2026-09-19 the player turns it by hand
        /// in 45-degree steps, and a blueprint that came back from a save
        /// facing a direction nobody chose would be a different building.
        public float yaw;
        /// Metres along the ridge, for a plan whose length the ground chose
        /// (a pier). 0 means the plan's own footprint. See `BuildPlan.WithLength`.
        public float length;

        public Vector3 At => new Vector3(x, 0f, z);
        /// Both parts, or the building is a drawing. A plan with no stone
        /// price is complete on its timber exactly as it always was.
        public bool Complete => done >= needed && stoneDone >= stoneNeeded;

        /// **One bar over both piles.** The drawing fills on what has been
        /// delivered against what it wants, timber and stone summed -- so a
        /// hut at 5/5 logs and 0/2 stone reads five sevenths built, which is
        /// the truth. When the stone price is zero this is the old
        /// `(done + donePart) / needed` to the bit.
        public float Fill01
        {
            get
            {
                float want = needed + stoneNeeded;
                if (want <= 0f) return 1f;
                return Mathf.Clamp01(
                    (done + donePart + stoneDone + stoneDonePart) / want);
            }
        }

        /// The timber part, on its own -- what the log stack beside the
        /// blueprint is drawn from.
        public bool TimberPaid => done >= needed;
        public bool StonePaid => stoneDone >= stoneNeeded;
    }

    /// **A building that stands here, and WHERE.**
    ///
    /// `OutpostLedger.built` is the list of plan ids and it is what every
    /// count reads; this is the row a save restores the object from. It is a
    /// separate list rather than a change to `built` because the probes write
    /// `built` by hand (`l.built.Add(id)`) and a schema that broke them all
    /// on the day the save arrived would be the save system's first bug.
    /// `Outpost.Raise` records one of these for everything it stands up, and
    /// `Outpost.Adopt` re-raises from it -- at the spot, not from the spiral,
    /// which would move every hut on load.
    [System.Serializable]
    public class BuiltBuilding
    {
        public string planId;
        /// World metres. Height is re-read from the field on load.
        public float x, z;
        /// World degrees, the way `PendingBuild.yaw` is.
        public float yaw;
        /// Metres along the ridge, the way `PendingBuild.length` is: 0 for
        /// a plan of its own size, the chosen length for a pier.
        public float length;

        public Vector3 At => new Vector3(x, 0f, z);
    }

    /// **Names for a hand nobody shipped, 2026-09-21.**
    ///
    /// A cast of twenty came off the manifest with names already; a hand
    /// recruited on the beach has to get one from somewhere. Picked
    /// deterministically off a hash of the camp's key and its roster size
    /// rather than `Random`, so the SAME camp reaching the SAME headcount
    /// twice -- once live, once replayed from a save -- names its newcomer
    /// the same both times.
    public static class VillagerNames
    {
        /// ~24 short storybook names, the register the manifest's own crew
        /// names are already in.
        static readonly string[] Names =
        {
            "Ash", "Bram", "Cass", "Dorrit", "Edda", "Finch", "Gale", "Hollis",
            "Ivo", "Jory", "Kess", "Lark", "Mabel", "Nye", "Orin", "Pell",
            "Quill", "Rowan", "Sable", "Tam", "Ursa", "Vane", "Wren", "Yara",
        };

        /// The name for the next hand this ledger recruits. Skips anybody
        /// already on the roster, so a small camp does not double up long
        /// before the 24 run out.
        public static string NextFor(OutpostLedger ledger)
        {
            int seed = ledger.keyX * 73856093 ^ ledger.keyZ * 19349663
                ^ ledger.hands.Count * 83492791;
            uint h = unchecked((uint)seed);
            for (int i = 0; i < Names.Length; i++)
            {
                string candidate = Names[(int)((h + (uint)i) % (uint)Names.Length)];
                if (ledger.Hand(candidate) == null) return candidate;
            }
            // All 24 taken by one camp: keep recruiting rather than stall
            // on a naming collision nobody designed for.
            return "Hand " + (ledger.hands.Count + 1);
        }
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
    /// It was built to be savable before there was a writer for it (D4),
    /// because retro-fitting serialisation onto live component state is the
    /// expensive version of this job; since 2026-09-21 `Save/SaveGame`
    /// writes it into the save file exactly as it is.
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

        public int HandsOn(OutpostOrder order, string target)
        {
            int n = 0;
            foreach (var h in hands)
                if (h != null && h.order == order && h.target == target) n++;
            return n;
        }

        public OutpostHand Hand(string who)
        {
            foreach (var h in hands) if (h != null && h.name == who) return h;
            return null;
        }

        // --- what it holds ---------------------------------------------------

        /// **What is on the ground here, per resource.**
        ///
        /// It used to be one integer called `timber`, because timber was the
        /// only thing an island had. Kevin, 2026-09-19: *"crew on the island
        /// can gather resources up to 10 of each without a storage unit."*
        /// So the ceiling is PER RESOURCE and the pile is a list.
        public List<OutpostStore> stores = new List<OutpostStore>();

        /// What is left in the ground, per resource. Seeded from the survey.
        public List<OutpostStock> stocks = new List<OutpostStock>();

        /// **What this place can keep OF EACH THING.** A campfire watches over
        /// ten of anything; a storehouse is how you raise it. This ceiling is
        /// the whole reason the loop does not become an idle game: hands fill
        /// it and stop, so the only way to get more out of an island is to
        /// invest in it.
        public int ceilingPer = CampfireCeiling;

        public OutpostStore Store(string resource, bool create = false)
        {
            foreach (var s in stores) if (s != null && s.resource == resource) return s;
            if (!create) return null;
            var made = new OutpostStore { resource = resource };
            stores.Add(made);
            return made;
        }

        public OutpostStock Stock(string resource, bool create = false)
        {
            foreach (var s in stocks) if (s != null && s.resource == resource) return s;
            if (!create) return null;
            var made = new OutpostStock
            {
                resource = resource,
                regrowPerDay = Res.RegrowPerDay(resource),
            };
            stocks.Add(made);
            return made;
        }

        public int CountOf(string resource)
        {
            var s = Store(resource);
            return s != null ? s.whole : 0;
        }

        /// Room left for this resource, in whole units.
        public int RoomFor(string resource) =>
            Mathf.Max(0, ceilingPer - CountOf(resource));

        /// Put whole units in, refusing what will not fit. Returns what was
        /// taken.
        public int Add(string resource, int n)
        {
            if (n <= 0) return 0;
            int took = Mathf.Min(RoomFor(resource), n);
            if (took <= 0) return 0;
            Store(resource, true).whole += took;
            return took;
        }

        /// Take whole units out. Returns what was actually there.
        public int Take(string resource, int n)
        {
            var s = Store(resource);
            if (s == null || n <= 0) return 0;
            int got = Mathf.Min(s.whole, n);
            s.whole -= got;
            return got;
        }

        /// Everything on the ground, all kinds together — what a hold has to
        /// have room for.
        public int Total
        {
            get
            {
                int n = 0;
                foreach (var s in stores) if (s != null) n += s.whole;
                return n;
            }
        }

        /// Timber, by name, because half the game still asks for it directly.
        public int Timber => CountOf(Res.Timber);

        /// The sub-log accrual on the timber pile. Only the probes care, and
        /// they care a great deal: it is what makes ticking often and ticking
        /// rarely agree.
        public float TimberPart()
        {
            var s = Store(Res.Timber);
            return s != null ? s.part : 0f;
        }

        /// **Timber taken out of the GROUND here, ever.** Not what is in the
        /// pile — what has been cut, including everything carried off by the
        /// ship and everything burnt into a building.
        ///
        /// This is the number the wood is drawn from. Kevin, playing it:
        /// *"the trees never disappear. i assume they would since they're cut
        /// down."* They did not, because gathering only ever decremented an
        /// abstract stock. The plan settled this on 2026-09-13 and it was
        /// never built: **fell deterministically, nearest the camp outward,
        /// and store only a COUNT** — then the whole visible state reproduces
        /// from one integer on any visit, whatever the terrain did in between.
        public float timberTaken;

        /// How many trees have been felled to represent that. Whole trees, so
        /// a visit that arrives to find the mesh untouched knows exactly how
        /// many to take down.
        public int treesFelled;

        /// **Declared now, consumed in a later pass.** Food is settled as
        /// local — berries and wheat off the island itself, no supply run —
        /// and over-capacity hands eat stores and can starve. None of that is
        /// wired: a farm now MAKES `Res.Food`, but nothing eats it yet.
        public float foodEaten;

        // --- upkeep: eating and recruiting, 2026-09-21 ------------------------
        //
        // GDD 6, Upkeep: "huts cap supported hands ... neglected hands get
        // angry." This is the first half of that -- feeding what is
        // already here, and growing the roster to fill the beds a hut
        // buys. The anger is still parked on `OutpostHand.mood`.

        /// Food one hand ashore eats per game day. Distinct from
        /// `FoodPerHandPerDay` above, which is what one FARMHAND produces --
        /// this is what every hand, farmhand or not, consumes. **A
        /// placeholder, never played.**
        public const float EatPerHandPerDay = 1f;

        /// Days of accumulated progress toward the next recruit spends.
        public const float DaysPerRecruit = 3f;

        /// Food the pile must hold before a recruit will start accruing --
        /// and what recruiting the hand actually spends.
        public const int RecruitFoodCost = 3;

        /// Days accrued toward the next hand. Reset (less `DaysPerRecruit`)
        /// each time a hand is born. Does not accrue without a free bed and
        /// food in the pile -- see `Step`.
        public float recruitProgress;

        /// **Shortfall, in days, that has gone unfed.** Nobody starves or
        /// leaves on this yet -- that is the parked "neglect/anger" feature
        /// GDD 6 names -- but the debt is counted from the day it is first
        /// owed, so the feature has something true to read when it is built.
        public float hungerDays;

        /// Beds this camp has, summed over every plan raised here.
        /// `built` and `raised` grow one entry per building together (see
        /// `Outpost.Raise`), so `built` alone is enough to count from.
        public int HousingCapacity
        {
            get
            {
                int n = 0;
                foreach (var id in built) n += BuildPlans.Named(id).houses;
                return n;
            }
        }

        /// Hands living here, housed or not -- `hands.Count` by another
        /// name, for the sheet.
        public int Housed => hands.Count;

        /// How far along the next recruit is, 0..1.
        public float RecruitProgress01 => DaysPerRecruit > 0f
            ? Mathf.Clamp01(recruitProgress / DaysPerRecruit) : 0f;

        /// One line for the sheet: what stands between this camp and its
        /// next hand.
        public string RecruitLine
        {
            get
            {
                int cap = HousingCapacity;
                if (cap <= 0) return "no beds";
                if (Housed >= cap) return $"{Housed} of {cap} beds";
                if (CountOf(Res.Food) < RecruitFoodCost) return "no food to feed a newcomer";
                float daysLeft = Mathf.Max(0f, DaysPerRecruit - recruitProgress);
                return $"{Housed} of {cap} beds · a new hand in {daysLeft:0.#} days";
            }
        }

        // --- what is built ---------------------------------------------------

        /// Plan ids raised here. `Outpost` owns the objects on the ground; this
        /// is what a save would restore them from.
        public List<string> built = new List<string>();

        public int CountBuilt(string planId)
        {
            int n = 0;
            foreach (var b in built) if (b == planId) n++;
            return n;
        }

        /// Where each building was stood up. See `BuiltBuilding`: `built` is
        /// the count, this is the spot. Written by `Outpost.Raise`, read by
        /// `Outpost.Adopt`, and by nothing else.
        public List<BuiltBuilding> raised = new List<BuiltBuilding>();

        public void RecordRaised(string planId, Vector3 at, float yaw, float length = 0f)
        {
            raised.Add(new BuiltBuilding
                { planId = planId, x = at.x, z = at.z, yaw = yaw, length = length });
        }

        public int CountRaised(string planId)
        {
            int n = 0;
            foreach (var b in raised) if (b != null && b.planId == planId) n++;
            return n;
        }

        /// What is sited here and not yet finished, or null.
        ///
        /// One at a time, deliberately. A camp with three half-built sheds in
        /// it is a camp that has told the player nothing about what it is
        /// doing.
        public PendingBuild pending;

        /// Is there a blueprint here waiting on wood?
        public bool Building => pending != null && !pending.Complete;

        /// Sited, paid for, and waiting for somebody to stand it up. The
        /// arithmetic can finish a building while its island is unloaded, so
        /// the raise happens when the scene next has somewhere to put it —
        /// see `Outpost.CatchUp`.
        public bool ReadyToRaise => pending != null && pending.Complete;

        // --- the clock -------------------------------------------------------

        /// `TimeOfDay.Seconds` this ledger has been advanced to. Double for the
        /// same reason TimeOfDay is: a float loses resolution over a session.
        ///
        /// **Only ever advanced in whole quanta** — that is what makes the
        /// arithmetic path-independent.
        public double lastTicked;

        /// **What happened while nobody was standing here.** Opened when the
        /// ship sails (`Outpost.ShowHands(false)`) and closed when she
        /// returns, so the game can say what the camp did in between. Never
        /// null: JsonUtility restores a reference-type field as a fresh
        /// default object on an old save, and a fresh `Absence` has
        /// `sinceSeconds == 0`, which `Open` already reads as "nothing open".
        public Absence away = new Absence();

        /// One open-ended record of an absence: what was gathered, made,
        /// eaten, raised and recruited between a departure and the next
        /// arrival. `[System.Serializable]` so it rides along inside the
        /// ledger's own JsonUtility save.
        [System.Serializable]
        public class Absence
        {
            /// `TimeOfDay.Seconds` the ship left. **0 means no absence is
            /// open** -- an old save, or a camp nobody has left yet.
            public double sinceSeconds;

            // Parallel lists rather than a dictionary: JsonUtility cannot
            // serialize one, and this is small enough that a linear find on
            // arrival costs nothing.
            public List<string> res = new List<string>();
            public List<float> got = new List<float>();

            public float eaten;
            public float hungryDays;

            /// Blueprint plan ids that went from building to `Complete`
            /// while away.
            public List<string> raised = new List<string>();
            /// Names of hands recruited while away.
            public List<string> born = new List<string>();

            /// Is there an absence in progress?
            public bool Open => sinceSeconds > 0.0;

            /// Worth showing the player at all, or just a quiet return.
            public bool Anything
            {
                get
                {
                    if (raised.Count > 0 || born.Count > 0) return true;
                    if (eaten > 0f || hungryDays > 0f) return true;
                    for (int i = 0; i < got.Count; i++) if (got[i] >= 1f) return true;
                    return false;
                }
            }

            /// Find or create this resource's row and add to it.
            public void Add(string resource, float amount)
            {
                if (string.IsNullOrEmpty(resource) || amount == 0f) return;
                for (int i = 0; i < res.Count; i++)
                {
                    if (res[i] != resource) continue;
                    got[i] += amount;
                    return;
                }
                res.Add(resource);
                got.Add(amount);
            }

            /// How many whole days this absence has run, as of `nowSeconds`.
            public float DaysAway(double nowSeconds)
            {
                if (!Open || TimeOfDay.DayLength <= 0f) return 0f;
                double elapsed = nowSeconds - sinceSeconds;
                return elapsed <= 0.0 ? 0f : (float)(elapsed / TimeOfDay.DayLength);
            }
        }

        /// Open a fresh absence record. The caller ticks the ledger up to
        /// `nowSeconds` FIRST (`Outpost.CatchUp` does), so nothing that
        /// happened before departure leaks into the record.
        public void BeginAbsence(double nowSeconds)
        {
            away = new Absence { sinceSeconds = nowSeconds };
        }

        /// Close the open absence and hand back what it holds. The caller
        /// ticks the ledger up to now FIRST, so the record covers the whole
        /// time she was gone, right up to this return. Returns null if
        /// there was nothing open (an old save, or two arrivals in a row).
        public Absence EndAbsence()
        {
            if (!away.Open) return null;
            var closed = away;
            away = new Absence();
            return closed;
        }

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

        /// Logs a hand fells in a day. **A guess, never played.** Still the
        /// unit everything else is priced against — see `Res.GatherRate`,
        /// which sets the other resources relative to it.
        public const float TimberPerHandPerDay = 4f;
        /// Logs a day one builder carries from the pile into a blueprint.
        /// Three times the felling rate: the wood is already down and it is
        /// lying five metres away. A guess like the rest.
        public const float HaulPerHandPerDay = 12f;
        /// **Stone a day one builder quarries out of standing rock**, when
        /// there is none piled to carry. Under the felling rate (4) because
        /// a boulder is four strikes where a tree is three -- the same
        /// relation `Res.GatherRate` already prices Stone at against Timber,
        /// rounded up to a whole number a player can count in days. **A
        /// guess, never played**, 2026-09-21.
        public const float StonePerHandPerDay = 3f;
        /// Food a day one farmhand brings in off a farm's field
        /// (`BuildPlans.Farm.rate`). Half again the felling rate: the wheat
        /// is planted in rows beside the camp, not found. **A guess, never
        /// played**, 2026-09-21.
        public const float FoodPerHandPerDay = 6f;

        /// **A build that cannot finish by itself.** Nothing in the pile and
        /// nothing left standing to cut: the drawing will wait for the wood to
        /// regrow, which is days per log. The sheet says so, because a stalled
        /// blueprint is otherwise indistinguishable from a slow one.
        public bool BuildStarved => TimberStarved || StoneStarved;

        /// The blueprint still wants logs and there are none to be had.
        public bool TimberStarved =>
            pending != null && !pending.Complete && !pending.TimberPaid
            && CountOf(Res.Timber) <= 0 && Wood.standing < 1f;

        /// **The same, for the stone part.** An island whose seam is worked
        /// out and whose pile is empty cannot finish a building however much
        /// wood is standing -- and a sheet that said "NO TIMBER LEFT" at it
        /// would be sending the player to cut trees they do not need.
        public bool StoneStarved
        {
            get
            {
                if (pending == null || pending.Complete || pending.StonePaid) return false;
                if (CountOf(Res.Stone) > 0) return false;
                var seam = Stock(Res.Stone);
                return seam == null || seam.standing < 1f;
            }
        }

        /// What a campfire watches over, of each thing. Settled at ten.
        public const int CampfireCeiling = 10;

        /// Timber-grade logs per hectare of the ground the camp works.
        /// **A guess, never played**, and deliberately far under the ~230
        /// trees a hectare the scenery actually draws: most of a wood is not
        /// worth felling, and a stock nobody can exhaust is not a stock.
        public const float StandingPerHectare = 40f;

        /// Share of the timber stock that comes back in a day. **A guess.**
        public const float RegrowthPerDay = 0.02f;

        /// Seed a fresh ledger for a camp on this ground.
        ///
        /// Timber comes from the surveyed area, because how much wood stands
        /// within reach is a property of the place. Anything else the island
        /// offers is added by `Outpost` once it knows what the populator put
        /// there — the ledger must not go looking at the scene.
        public static OutpostLedger For(Vector3 at, float workedHectares)
        {
            var l = new OutpostLedger();
            l.SetKey(at);
            l.ceilingPer = CampfireCeiling;
            l.SeedStock(Res.Timber, workedHectares);
            l.lastTicked = TimeOfDay.Seconds;
            return l;
        }

        /// Put a resource's stock on the ground here, sized by the worked area.
        public void SeedStock(string resource, float hectares)
        {
            var s = Stock(resource, true);
            s.standingMax = Mathf.Max(1f, hectares * Res.PerHectare(resource));
            s.standing = s.standingMax;
            s.regrowPerDay = Res.RegrowPerDay(resource);
        }

        /// **Put more of a resource in the ground here.** A farm's field:
        /// raising one adds `beds * unitsPerBed` of standing Food, planted
        /// and ready, and lifts the ceiling it regrows to by the same. Called
        /// from the raise hook, never from `Step`, so the ledger still learns
        /// about the scene only at the moments the scene tells it.
        ///
        /// Merges into a stock that already exists -- wild wheat gathered by
        /// hand and a farm's rows are one Food stock -- and the regrowth
        /// becomes the faster of the two, because a field that has been
        /// planted does not come back slower for having wild wheat beside it.
        /// `regrowPerDay` below zero leaves the stock's own rate alone.
        public OutpostStock AddStanding(string resource, float amount, float regrowPerDay = -1f)
        {
            var s = Stock(resource, true);
            if (amount > 0f)
            {
                s.standingMax += amount;
                s.standing = Mathf.Min(s.standingMax, s.standing + amount);
            }
            if (regrowPerDay >= 0f) s.regrowPerDay = Mathf.Max(s.regrowPerDay, regrowPerDay);
            return s;
        }

        /// What a raised plan adds to the ground: a farm's field, or nothing.
        /// One call for the raise hook, so the numbers stay on the plan.
        public OutpostStock AddField(BuildPlan plan)
        {
            if (plan.beds <= 0 || string.IsNullOrEmpty(plan.makes)) return null;
            return AddStanding(plan.makes, plan.FieldStanding, plan.bedRegrowPerDay);
        }

        /// The timber stock, which enough of the game asks for by name that it
        /// is worth not making everybody look it up.
        public OutpostStock Wood => Stock(Res.Timber, true);

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

        /// **Pay the stone part of the blueprint out of `labour` hand-days**,
        /// pile first and then the seam, spending what it uses. Shaped to
        /// match the timber block above line for line, because two parts of
        /// one price that are paid by different-looking arithmetic are two
        /// things that will drift.
        void PayStone(ref float labour)
        {
            if (pending == null) return;
            float roomS = (pending.stoneNeeded - pending.stoneDone) - pending.stoneDonePart;
            if (roomS <= 0f || labour <= 0f) return;

            var pile = Store(Res.Stone);
            if (pile != null && pile.whole > 0)
            {
                float canHaul = labour * HaulPerHandPerDay;
                int hauled = Mathf.FloorToInt(Mathf.Min(canHaul, Mathf.Min(pile.whole, roomS)));
                if (hauled > 0)
                {
                    pile.whole -= hauled;
                    pending.stoneDone += hauled;
                    roomS -= hauled;
                    labour -= hauled / HaulPerHandPerDay;
                }
            }

            var seam = Stock(Res.Stone);
            if (seam == null || roomS <= 0f || labour <= 0f) return;
            float want = labour * StonePerHandPerDay;
            float got = Mathf.Min(want, Mathf.Min(seam.standing, roomS));
            if (got <= 0f) return;
            seam.standing -= got;
            pending.stoneDonePart += got;
            int whole = Mathf.FloorToInt(pending.stoneDonePart);
            if (whole > 0)
            {
                pending.stoneDone += whole;
                pending.stoneDonePart -= whole;
            }
            labour -= got / StonePerHandPerDay;
        }

        /// **What a builder here should be fetching right now**: logs until
        /// the timber part is paid, then stone, then nothing. One answer, so
        /// the arithmetic (`Step`), the body (`CampWorker`) and the mime all
        /// agree about which material a man is carrying.
        public string BuilderWants
        {
            get
            {
                if (pending == null || pending.Complete) return null;
                if (!pending.TimberPaid) return Res.Timber;
                return pending.StonePaid ? null : Res.Stone;
            }
        }

        /// One quantum of work. The only place the outpost's state changes.
        void Step(float days)
        {
            // Regrowth first, so a camp that stripped its ground last step has
            // something to cut this one rather than the order of operations
            // deciding the answer.
            foreach (var s in stocks)
            {
                if (s == null || s.regrowPerDay <= 0f) continue;
                if (s.standing < s.standingMax)
                    s.standing = Mathf.Min(s.standingMax,
                        s.standing + s.standingMax * s.regrowPerDay * days);
            }

            // **Building comes before everything, and draws on the same
            // standing timber.** A camp that has not been built yet has
            // nowhere to stockpile TO -- the ceiling is what the fire watches
            // over and there is no fire -- so a hand told to build is not
            // choosing between two piles, they are the reason there will be
            // one.
            // Captured before the building block touches `pending`, so the
            // completion check below can tell "finished just now" from
            // "was already sitting there ready to raise".
            bool wasComplete = pending != null && pending.Complete;
            if (pending != null && !pending.Complete)
            {
                int builders = HandsOn(OutpostOrder.Build);
                if (builders > 0)
                {
                    float labour = builders * days;          // hand-days to spend
                    float roomB = (pending.needed - pending.done) - pending.donePart;

                    // **The pile first.** Kevin, 2026-09-20: *"they gathered
                    // logs for it but it never built."* They had: ten logs sat
                    // beside the fire while the builders walked past them to
                    // cut fresh ones, and on a small island the fresh ones ran
                    // out at 6 of 24 and the sawmill stood as a drawing for
                    // ever. Timber already cut is carried five metres, which
                    // is also why it goes in faster than timber still growing.
                    var pile = Store(Res.Timber);
                    if (pile != null && pile.whole > 0 && roomB > 0f && labour > 0f)
                    {
                        float canHaul = labour * HaulPerHandPerDay;
                        int hauled = Mathf.FloorToInt(Mathf.Min(canHaul, Mathf.Min(pile.whole, roomB)));
                        if (hauled > 0)
                        {
                            pile.whole -= hauled;
                            pending.done += hauled;
                            roomB -= hauled;
                            labour -= hauled / HaulPerHandPerDay;
                        }
                    }

                    // Then whatever is left of the day goes on cutting.
                    var wood = Wood;
                    float wantB = Mathf.Max(0f, labour) * TimberPerHandPerDay;
                    float gotB = Mathf.Min(wantB, Mathf.Min(wood.standing, roomB));
                    if (gotB > 0f)
                    {
                        wood.standing -= gotB;
                        timberTaken += gotB;
                        pending.donePart += gotB;
                        int wholeB = Mathf.FloorToInt(pending.donePart);
                        if (wholeB > 0)
                        {
                            pending.done += wholeB;
                            pending.donePart -= wholeB;
                        }
                        labour -= gotB / TimberPerHandPerDay;
                    }

                    // --- and then the stone, 2026-09-21 ----------------------
                    //
                    // **The second part of the price, in the same two steps
                    // and the same order**: what is already quarried and
                    // lying by the fire goes in at the haul rate, and only
                    // then does anybody take a pick to standing rock.
                    //
                    // It runs AFTER the timber out of whatever hand-days the
                    // timber part left over, which is what makes a plan with
                    // `stoneNeeded == 0` bit-identical to the old path: the
                    // block below sees `roomS <= 0` and returns having
                    // touched nothing. A builder therefore finishes the logs
                    // first and starts on the rock in the same tick -- the
                    // body walking out to a boulder follows, because
                    // `CampWorker` asks the ledger the same question
                    // (`BuilderWants`) the arithmetic just answered.
                    //
                    // Note `Stock(Res.Stone)` is read WITHOUT creating: an
                    // empty stock conjured here would be a seam of zero on
                    // an island with rocks on it, and `GatherSync` reads
                    // `standing < 1` as "worked out" and would hide every
                    // boulder on the island.
                    PayStone(ref labour);
                }
            }
            // The record of who's away doesn't care whether the raise was
            // seen -- `Outpost.FinishPending` handles standing the mesh up
            // separately, on the next `CatchUp`. This just notes that it
            // happened during the absence.
            if (pending != null && !wasComplete && pending.Complete) away.raised.Add(pending.planId);

            // --- gathering ---------------------------------------------------
            //
            // One pass per HAND rather than per resource, so two hands on the
            // same thing share one stock and one ceiling without this loop
            // having to know that they are two.
            foreach (var h in hands)
            {
                if (h == null || h.order != OutpostOrder.Gather) continue;
                if (string.IsNullOrEmpty(h.target)) continue;

                var stock = Stock(h.target);
                if (stock == null || stock.standing <= 0f) continue;

                var store = Store(h.target, true);
                float room = (ceilingPer - store.whole) - store.part;
                if (room <= 0f) continue;

                float want = Res.GatherRate(h.target) * days;
                float got = Mathf.Min(want, Mathf.Min(stock.standing, room));
                if (got <= 0f) continue;

                stock.standing -= got;
                if (h.target == Res.Timber) timberTaken += got;
                store.part += got;
                int whole = Mathf.FloorToInt(store.part);
                if (whole > 0) { store.whole += whole; store.part -= whole; }
                away.Add(h.target, got);
            }

            // --- working at a building ---------------------------------------
            //
            // **A position turns one thing into another.** A sawyer takes
            // timber and makes boards; a smith takes ore and makes tools; a
            // farmhand takes nothing at all, because the field is the input.
            // The conversion lives on the PLAN, so a building is the whole
            // description of what its job is worth.
            foreach (var h in hands)
            {
                if (h == null || h.order != OutpostOrder.Work) continue;
                if (string.IsNullOrEmpty(h.target)) continue;
                // Assigned to something that is not standing here. Can happen
                // to a saved hand whose building was never restored; produce
                // nothing rather than guessing.
                if (!built.Contains(h.target)) continue;

                var plan = BuildPlans.Named(h.target);
                if (string.IsNullOrEmpty(plan.makes) || plan.rate <= 0f) continue;

                var made = Store(plan.makes, true);
                float room = (ceilingPer - made.whole) - made.part;
                if (room <= 0f) continue;

                float want = Mathf.Min(plan.rate * days, room);
                if (want <= 0f) continue;

                // An input is consumed one for one, and a hand with nothing to
                // work on produces nothing. Deliberate, and the point of the
                // chain: a sawmill on an island with no timber is a shed.
                if (!string.IsNullOrEmpty(plan.takes))
                {
                    var from = Store(plan.takes);
                    float have = from != null ? from.whole + from.part : 0f;
                    want = Mathf.Min(want, have);
                    if (want <= 0f) continue;

                    from.part -= want;
                    while (from.part < 0f && from.whole > 0) { from.whole--; from.part += 1f; }
                    if (from.part < 0f) from.part = 0f;
                }
                // **No input means the ground is the input**, and if the
                // ground is tracked it is drawn down exactly as a gatherer
                // draws it: a farmhand harvests the standing Food that
                // raising the farm put there (`AddStanding`), and the field
                // grows back at the top of the next step. A ledger that has no
                // stock for what the building makes -- an older save, a
                // probe's bare farm -- is not bounded at all, as before.
                else
                {
                    var field = Stock(plan.makes);
                    if (field != null)
                    {
                        want = Mathf.Min(want, field.standing);
                        if (want <= 0f) continue;
                        field.standing -= want;
                    }
                }

                made.part += want;
                int whole = Mathf.FloorToInt(made.part);
                if (whole > 0) { made.whole += whole; made.part -= whole; }
                away.Add(plan.makes, want);
            }

            // --- upkeep: eating -----------------------------------------------
            //
            // Runs after production, so a farmhand's own harvest this same
            // quantum is there to be eaten from -- and every quantum, not
            // once a day, so D2 holds (ten days in one `Tick` call and ten
            // calls of one day each spend identical food).
            int eaters = hands.Count;
            if (eaters > 0)
            {
                var food = Store(Res.Food);
                float need = eaters * EatPerHandPerDay * days;
                float have = food != null ? food.whole + food.part : 0f;
                float eaten = Mathf.Min(need, have);
                if (eaten > 0f)
                {
                    food.part -= eaten;
                    while (food.part < 0f && food.whole > 0) { food.whole--; food.part += 1f; }
                    if (food.part < 0f) food.part = 0f;
                    foodEaten += eaten;
                    away.eaten += eaten;
                }
                // Nobody starves or leaves on this yet -- record the debt
                // for the neglect/anger pass and stop there.
                if (eaten < need) { hungerDays += days; away.hungryDays += days; }
            }

            // --- upkeep: recruiting ---------------------------------------------
            //
            // A free bed and food in the pile are both required before
            // progress accrues at all -- Kevin's call: recruiting should
            // read as something the camp EARNS, not a clock that runs
            // regardless. Checked against the pile AFTER eating, so a camp
            // that just fed its last hand on its last three Food does not
            // also recruit off the same three.
            if (Housed < HousingCapacity && CountOf(Res.Food) >= RecruitFoodCost)
            {
                recruitProgress += days;
                if (recruitProgress >= DaysPerRecruit)
                {
                    recruitProgress -= DaysPerRecruit;
                    Take(Res.Food, RecruitFoodCost);
                    string name = VillagerNames.NextFor(this);
                    hands.Add(new OutpostHand
                    {
                        name = name,
                        order = OutpostOrder.Idle,
                        born = true,
                    });
                    away.born.Add(name);
                }
            }
        }

        /// How full this resource's pile is, for anything drawing a gauge.
        public float Fill01(string resource) => ceilingPer > 0
            ? Mathf.Clamp01(CountOf(resource) / (float)ceilingPer) : 0f;

        /// Nothing more for this hand to do: their pile is full, or their
        /// stock is gone, or the thing they work at has nothing to work on.
        public bool Stalled(OutpostHand h)
        {
            if (h == null) return true;
            if (h.order == OutpostOrder.Build) return pending == null || pending.Complete;
            if (h.order == OutpostOrder.Gather)
            {
                var stock = Stock(h.target);
                return RoomFor(h.target) <= 0 || stock == null || stock.standing < 1f;
            }
            if (h.order == OutpostOrder.Work)
            {
                var plan = BuildPlans.Named(h.target);
                if (string.IsNullOrEmpty(plan.makes)) return true;
                if (RoomFor(plan.makes) <= 0) return true;
                if (!string.IsNullOrEmpty(plan.takes)) return CountOf(plan.takes) <= 0;
                // The field is the input: stripped bare is stalled, until it
                // grows back.
                var field = Stock(plan.makes);
                return field != null && field.standing <= 0f;
            }
            return true;
        }

        /// Put every hand here on the same order. Used when a blueprint goes
        /// down (everybody builds it) and when it is finished (everybody goes
        /// back to what an island is for).
        public void OrderAll(OutpostOrder order, string target = "")
        {
            foreach (var h in hands)
            {
                if (h == null) continue;
                h.order = order;
                h.target = target;
            }
        }
    }
}
