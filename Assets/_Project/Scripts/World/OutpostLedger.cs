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

    /// **How much of a day's ration this camp actually issues, 2026-09-22.**
    /// Kevin's knob: a camp you can run lean on purpose, not just one that
    /// runs out by accident. `OutpostLedger.EatMultiplier` reads it; nothing
    /// else needs to know the camp is short-rationing on purpose versus
    /// simply out of food.
    public enum Rations
    {
        Full,
        Half,
        None,
    }

    /// **Which of food or timber this camp leans on, 2026-09-22.** Kevin's
    /// second knob: "a camp you can point." Even is the old, unweighted
    /// arithmetic; the other two trade a fifth of one rate for a quarter of
    /// the other, cheap enough that a player can flip it and watch the
    /// numbers on the sheet move without the camp's shape changing.
    public enum WorkPriority
    {
        Even,
        FoodFirst,
        TimberFirst,
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
                        // Nobody "gathers game". He is out after the goats.
                        if (target == Res.Game) return "hunting";
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

        /// Furious, not just short-tempered. `OutpostLedger.AngryCount` counts
        /// this; nothing else reads it yet.
        public bool Angry => mood < 0.5f;

        /// One word for the row, or none. "angry" once `mood` has crossed
        /// the line `WorkFactor` starts docking labour at; "hungry" a while
        /// before that, when they are still pulling full weight but it has
        /// been going short; "" for a hand nobody has starved.
        public string MoodWord
        {
            get
            {
                if (Angry) return "angry";
                if (mood < 0.95f) return "hungry";
                return "";
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

        /// **The brick part, 2026-09-22.** Same three fields again, and
        /// zero on every plan there is -- see `BuildPlan.baseBrickCost` for
        /// why it exists before anything charges it. A save written before
        /// this existed restores all three as 0, which reads exactly as
        /// "this one wanted no brick", the same way the stone part did.
        ///
        /// **The one that cannot be paid out of the ground.** Timber is cut
        /// and stone is quarried where the blueprint stands; a brick was
        /// made at a quarry by somebody and is either on the pile or it is
        /// not. `OutpostLedger.PayBrick` has no seam half at all.
        public int brickNeeded;
        public int brickDone;
        public float brickDonePart;

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
        public bool Complete => done >= needed && stoneDone >= stoneNeeded
            && brickDone >= brickNeeded;

        /// **One bar over both piles.** The drawing fills on what has been
        /// delivered against what it wants, timber and stone summed -- so a
        /// hut at 5/5 logs and 0/2 stone reads five sevenths built, which is
        /// the truth. When the stone price is zero this is the old
        /// `(done + donePart) / needed` to the bit.
        public float Fill01
        {
            get
            {
                float want = needed + stoneNeeded + brickNeeded;
                if (want <= 0f) return 1f;
                return Mathf.Clamp01(
                    (done + donePart + stoneDone + stoneDonePart
                     + brickDone + brickDonePart) / want);
            }
        }

        /// The timber part, on its own -- what the log stack beside the
        /// blueprint is drawn from.
        public bool TimberPaid => done >= needed;
        public bool StonePaid => stoneDone >= stoneNeeded;
        public bool BrickPaid => brickDone >= brickNeeded;
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
        /// names are already in. They live in `Crew.CrewNames` since
        /// 2026-09-22: the ship's yard names her new berths out of the same
        /// hat, and two hats meant two people called Bo.
        static string[] Names => SeaSick.Crew.CrewNames.Pool;

        /// The name for the next hand this ledger recruits. Skips anybody
        /// already on this roster AND anybody already answering to that name
        /// anywhere else -- aboard, or at another camp -- so a hand carried
        /// onto the ship never meets his own name there.
        public static string NextFor(OutpostLedger ledger)
        {
            int seed = ledger.keyX * 73856093 ^ ledger.keyZ * 19349663
                ^ ledger.hands.Count * 83492791;
            uint h = unchecked((uint)seed);
            var used = SeaSick.Crew.CrewNames.InUse();
            for (int i = 0; i < Names.Length; i++)
            {
                string candidate = Names[(int)((h + (uint)i) % (uint)Names.Length)];
                if (ledger.Hand(candidate) == null && !used.Contains(candidate))
                    return candidate;
            }
            // All 24 spoken for: keep recruiting rather than stall on a
            // naming collision nobody designed for.
            for (int i = 1; i < 999; i++)
            {
                string candidate = "Hand " + i;
                if (ledger.Hand(candidate) == null && !used.Contains(candidate))
                    return candidate;
            }
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

        /// **How much of that wood has grown back**, in trees, fractions and
        /// all. Kevin, 2026-09-22: *"they should re-grow further away from
        /// camp, to help the camp not get overgrown."*
        ///
        /// A second count rather than a smaller `treesFelled`, because
        /// `timberTaken` is what has been cut EVER and the felling is driven
        /// off it: walking `treesFelled` backwards would only make the next
        /// tick take the same trees down again. So the two are kept apart --
        /// what was cut, and what has come back -- and the picture is the
        /// difference. Which trees come back is geometry and lives in
        /// `Outpost.DrawWood`: the far ones first, never the camp's own
        /// clearing.
        public float treesRegrown;

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

        /// **The ration the player has set, 2026-09-22.** Plain serialised
        /// field, not a property, so `JsonUtility` saves it the same free way
        /// it already saves `OutpostHand.order` -- an enum round-trips as its
        /// underlying int with no extra plumbing.
        public Rations rations = Rations.Full;

        /// What `rations` actually pays out, against `EatPerHandPerDay`.
        public float EatMultiplier => rations switch
        {
            Rations.Full => 1f,
            Rations.Half => 0.5f,
            _ => 0f,
        };

        /// **The work priority the player has set, 2026-09-22 -- a camp you
        /// can point.** Plain serialised field for the same JsonUtility
        /// reason as `rations`.
        public WorkPriority priority = WorkPriority.Even;

        /// What `priority` does to one resource's rate: Food and Timber trade
        /// a fifth for a quarter against each other; everything else, and
        /// `Even`, is untouched. Read by both the arithmetic (`Step`) and the
        /// readouts (`RatePerDay`/`MakeRatePerDay`) so the sheet never prints
        /// a number the tick would not pay.
        public float PriorityMultiplier(string resource)
        {
            if (priority == WorkPriority.Even) return 1f;
            bool boostFood = priority == WorkPriority.FoodFirst;
            if (resource == Res.Food) return boostFood ? 1.25f : 0.8f;
            if (resource == Res.Timber) return boostFood ? 0.8f : 1.25f;
            return 1f;
        }

        // --- upkeep: mood, 2026-09-22 ------------------------------------
        //
        // Kevin's design, settled: the campfire IS the provisions gauge and
        // failure is gradual -- stores run out, hands stop pulling full
        // weight and forage for themselves instead, and they get ANGRY.
        // That is the whole punishment; nobody leaves.

        /// How much a day wholly unfed knocks a hand's mood down. Two
        /// unfed days take a content hand (mood 1) to furious (mood 0).
        /// **A guess, never played.**
        public const float MoodDropPerHungryDay = 0.5f;

        /// How much a day fully fed brings mood back up. Four fed days
        /// walk a furious hand back to content. **A guess, never played.**
        public const float MoodRecoverPerFedDay = 0.25f;

        /// Days of food in the pile, per hand, that reads as a bright
        /// fire on `Health01`. **A guess, never played.**
        public const float DaysOfFoodForBrightFire = 3f;

        /// **How much of a day's work this hand actually does, applied to
        /// PRODUCTION only -- never to eating.** A hand at mood 0.5 or
        /// better works flat out; below that they spend the rest of the
        /// day foraging for themselves instead of the camp, scaling to
        /// nothing at mood 0. So a starving camp does not stop dead, it
        /// just gets slower, which is what makes the decline something the
        /// player can see coming and catch.
        public static float WorkFactor(OutpostHand h) =>
            h == null ? 0f : Mathf.Clamp01(h.mood / 0.5f);

        /// `WorkFactor`, except that **a hand bringing in food is never
        /// docked** -- foraging IS gathering food, so a starving camp told
        /// to gather berries or work its farm can still eat its way back.
        /// Without this a camp that ran out once could never recover: the
        /// hungrier they got the less food they brought in.
        public static float WorkFactorOn(OutpostHand h, string produces) =>
            produces == Res.Food ? (h == null ? 0f : 1f) : WorkFactor(h);

        /// Is anybody here going hungry right now -- the pile has nothing
        /// in it and there is somebody to feed. What `Step`'s eating block
        /// is about to find, a step early, for anything that wants to warn
        /// ahead of the tick rather than after it.
        public bool Hungry
        {
            get
            {
                if (hands.Count == 0) return false;
                var food = Store(Res.Food);
                return food == null || (food.whole == 0 && food.part <= 0f);
            }
        }

        /// **What `Outpost` pushes to `Campfire.health01`.** Three days of
        /// food banked, per hand, reads as a bright fire; an outpost with
        /// nobody home reads as full so an empty camp does not look like a
        /// dying one. **Rations-honest, 2026-09-22**: the bar wants fewer
        /// days' worth of food when a half ration means fewer days' worth is
        /// actually spent, and a camp on no rations at all never reads
        /// bright no matter how the pile is stacked -- nobody there is being
        /// fed, whatever is sitting beside the fire.
        public float Health01
        {
            get
            {
                if (hands.Count == 0) return 1f;
                if (rations == Rations.None) return 0f;
                return Mathf.Clamp01((CountOf(Res.Food) + (Store(Res.Food)?.part ?? 0f))
                    / (hands.Count * EatPerHandPerDay * EatMultiplier * DaysOfFoodForBrightFire));
            }
        }

        /// How many hands here are furious. What the sheet counts against
        /// the roster.
        public int AngryCount
        {
            get
            {
                int n = 0;
                foreach (var h in hands) if (h != null && h.Angry) n++;
                return n;
            }
        }

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

        /// **The build QUEUE, oldest first (2026-09-22).**
        ///
        /// Kevin, on the phone: *"I want to be able to place more blueprints
        /// at once."* It used to be one row and a refusal ("something is
        /// already going up"), which made the camp a one-decision-at-a-time
        /// place and put the player on a boat waiting for a shed.
        ///
        /// Order is the whole design: hands serve `sites[0]` until it is
        /// stocked, then `sites[1]`. A site leaves this list the moment it is
        /// RAISED (`Outpost.FinishReady`) or cancelled, never before -- the
        /// reservations and `CanPlace` read it to keep two drawings off the
        /// same ground.
        public List<PendingBuild> sites = new List<PendingBuild>();

        /// **The single-row save slot this queue replaced. Migration only.**
        ///
        /// `JsonUtility` cannot rename a key, so a save written before the
        /// queue carries its one blueprint here. `MigratePending` lifts it
        /// into `sites` and nulls this, and `Outpost.Adopt` calls that before
        /// anything reads the ledger. NOTHING ELSE MAY READ OR WRITE IT --
        /// the live answer is `Pending` / `Focus` / `sites`. It is still
        /// written by every save (as an empty object, exactly as it always
        /// was: `JsonUtility` cannot write null either), which is why an old
        /// build can still read a new save's camps.
        public PendingBuild pending;

        /// **Old save into new list.** Idempotent, and cheap enough to call
        /// from anywhere that is about to look at the queue.
        public void MigratePending()
        {
            if (sites == null) sites = new List<PendingBuild>();
            if (pending == null) return;
            // An empty row is what `JsonUtility` writes for "there was
            // nothing sited" -- see `Outpost.Adopt`, which has always had to
            // re-null it.
            if (!string.IsNullOrEmpty(pending.planId)) sites.Insert(0, pending);
            pending = null;
        }

        /// **The site the camp is working on**: the oldest one that is not
        /// yet stocked. Null when every queued site has all its materials in
        /// (they are then only waiting to be stood up). This is what
        /// `BuilderWants`, the haul target and the starvation lines are all
        /// about -- one answer, so the arithmetic and the bodies agree.
        public PendingBuild Focus
        {
            get
            {
                if (sites == null) return null;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && !sites[i].Complete) return sites[i];
                return null;
            }
        }

        /// The row a sheet means when it says "the blueprint" without naming
        /// one: the one being worked, else the oldest queued.
        public PendingBuild Pending
        {
            get
            {
                var f = Focus;
                if (f != null) return f;
                return sites != null && sites.Count > 0 ? sites[0] : null;
            }
        }

        /// How many drawings stand here.
        public int SiteCount => sites != null ? sites.Count : 0;

        /// Is there a blueprint here waiting on wood?
        public bool Building => Focus != null;

        /// Sited, paid for, and waiting for somebody to stand it up. The
        /// arithmetic can finish a building while its island is unloaded, so
        /// the raise happens when the scene next has somewhere to put it —
        /// see `Outpost.CatchUp`.
        public bool ReadyToRaise
        {
            get
            {
                if (sites == null) return false;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && sites[i].Complete) return true;
                return false;
            }
        }

        /// The oldest site with everything in it, or null. `Outpost` raises
        /// these one per call until there are none left.
        public PendingBuild FirstStocked
        {
            get
            {
                if (sites == null) return null;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && sites[i].Complete) return sites[i];
                return null;
            }
        }

        /// Is this plan already queued here? A camp keeps one of each, and
        /// that rule has to cover the drawings as well as the buildings or
        /// the queue is how you get two sawmills.
        public bool Queued(string planId)
        {
            if (sites == null || string.IsNullOrEmpty(planId)) return false;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i] != null && sites[i].planId == planId) return true;
            return false;
        }

        // --- raiders, 2026-09-22 -----------------------------------------------
        //
        // Phase 3 "teeth": a camp on an island with raiders offshore, nobody
        // watching it, with something piled, gets raided on a clock the sheet
        // can print in days. A manned watchtower stops the clock; an unmanned
        // one halves it. It is a mistake the player can see coming, not a
        // dice roll -- see `RaidLine`.

        /// Raiders patrolling this island right now. **Pushed in by
        /// `Outpost.CatchUp` before every tick**, like `ceilingPer` -- the
        /// ledger never computes this, it only reacts to it.
        public int raiders;

        /// Days of unwatched exposure banked toward the next raid.
        public float threat;

        /// Lifetime raid count.
        public int raids;

        /// Days of exposure a fresh camp can bank before a raid lands.
        /// **A placeholder, never played.**
        public const float DaysToRaid = 4f;

        /// Share of each pile's whole units a raid takes, at least one unit
        /// when the pile has any. **A placeholder, never played.**
        public const float RaidShare = 0.4f;

        /// Mood every hand loses when the camp is raided. **A placeholder,
        /// never played.**
        public const float RaidMoodHit = 0.25f;

        // --- what arrows buy, 2026-09-22 ---------------------------------------
        //
        // Kevin: *"build a fletcher's building as well for bow and arrow."*
        // Two effects, and both of them SPEND the arrows, because a good that
        // only accumulates is a number and not a decision.

        /// What a quiver is worth to a hunter: half again as many animals a
        /// day, at one arrow an animal. **A guess, never played.**
        public const float BowKillBonus = 1.5f;

        /// Arrows a posted lookout will loose at one raid.
        public const int VolleyArrows = 5;

        /// What each arrow loosed takes off the raid's share, as a fraction
        /// of the pile. Five arrows at a tenth each turns `RaidShare` from
        /// 0.4 into 0.2 -- a full volley halves what a raid carries off, and
        /// no volley leaves the old number untouched to the bit.
        public const float VolleyShareOff = 0.1f;

        /// **The lookout looses, and the raid carries less off.**
        ///
        /// The rule, in one sentence: *a posted lookout with arrows spends up
        /// to five of them, and every arrow spent takes a tenth off the share
        /// a raid takes.* No lookout, no watchtower or no arrows and nothing
        /// happens at all -- `RaidShare` stands, exactly as it did before the
        /// fletcher existed.
        ///
        /// Returns the arrows actually loosed, so the caller can say so.
        ///
        /// A posted lookout with arrows looses up to five. The away-clock
        /// raid spends them in `Raid()` against its share; the live raid
        /// spends them in `Combat.RaidParty.Begin`, two arrows a raider.
        public int LookoutVolley(int maxArrows = VolleyArrows)
        {
            if (!LookoutPosted) return 0;
            var quiver = Store(Res.Arrows);
            if (quiver == null || quiver.whole <= 0) return 0;
            int loosed = Mathf.Min(maxArrows, quiver.whole);
            quiver.whole -= loosed;
            return loosed;
        }

        /// Matches the id `BuildPlans.Watchtower` is being wired up with
        /// elsewhere -- kept as a string here rather than a reference to
        /// that plan, which may not exist yet.
        public const string WatchtowerId = "Watchtower";

        /// Has a watchtower been raised here at all -- built, not manned.
        public bool HasWatchtower => built.Contains(WatchtowerId);

        /// Labour standing lookout right now, clamped to one -- a single
        /// hand at full mood is all the guard a camp needs.
        public float Guard
        {
            get
            {
                float g = 0f;
                foreach (var h in hands)
                    if (h != null && h.order == OutpostOrder.Work && h.target == WatchtowerId)
                        g += WorkFactor(h);
                return Mathf.Clamp01(g);
            }
        }

        /// Days of exposure this camp banks per day, at its current orders.
        /// Zero with nobody offshore, nothing to take, or a manned lookout --
        /// a raid is never a clock running on a camp that cannot be raided.
        public float ThreatRatePerDay
        {
            get
            {
                if (raiders <= 0 || Total <= 0 || Guard >= 1f) return 0f;
                return (1f - Guard) * (HasWatchtower ? 0.5f : 1f);
            }
        }

        /// Days until the next raid at the current rate, or -1 when none is
        /// coming.
        public float DaysUntilRaid
        {
            get
            {
                float rate = ThreatRatePerDay;
                if (rate <= 0f) return -1f;
                return (DaysToRaid - threat) / rate;
            }
        }

        /// Is somebody standing lookout right now -- `Guard` at full, spelled
        /// out for a UI that wants a bool rather than the float it is graded
        /// from.
        public bool LookoutPosted => Guard >= 1f;

        /// **The clock's cadence with `Guard` forced to one value, 2026-09-22**
        /// -- not "until the next raid from here," which `DaysUntilRaid`
        /// already answers, but "how far apart raids land at this setting,"
        /// for a sheet that wants to show the player both ends of the choice
        /// at once. Mirrors `ThreatRatePerDay` term for term with `Guard`
        /// substituted, so the two can never disagree about what a manned
        /// lookout is worth.
        float ThreatRateAt(bool guarded)
        {
            if (raiders <= 0 || Total <= 0 || guarded) return 0f;
            return HasWatchtower ? 0.5f : 1f;
        }

        /// Days between raids as if nobody were watching at all.
        public float RaidDaysUnwatched
        {
            get
            {
                float rate = ThreatRateAt(false);
                return rate <= 0f ? float.PositiveInfinity : DaysToRaid / rate;
            }
        }

        /// Days between raids as if a lookout were manning the tower --
        /// which is to say never: a manned watch halts the clock outright,
        /// the same way `Guard >= 1f` already zeroes `ThreatRatePerDay`.
        public float RaidDaysIfWatched => float.PositiveInfinity;

        /// **What the sheet prints for this camp's raid risk**, or null when
        /// there is nobody offshore to make it a risk at all.
        public string RaidLine
        {
            get
            {
                if (raiders <= 0) return null;
                int n = raiders;
                string who = n == 1 ? "raider" : "raiders";
                if (Guard >= 1f)
                    return $"{n} {who} offshore   ·   the lookout keeps them off";
                if (Total <= 0)
                    return $"{n} {who} offshore   ·   nothing here to take";
                float d = DaysUntilRaid;
                string fix = HasWatchtower
                    ? "post a lookout"
                    : "a watchtower and a lookout stop it";
                return $"{n} {who} offshore   ·   a raid {d:0.#} days after you sail   ·   " + fix;
            }
        }

        /// **The raid itself.** Takes a share of every pile that has
        /// anything in it, records what was lost against the open absence,
        /// and knocks every hand's mood down -- the cost of nobody watching.
        void Raid()
        {
            // **The volley first, Kevin 2026-09-22.** The lookout is already
            // standing there -- with a quiver she does something about it.
            // Loosed BEFORE a single pile is touched, so the arrows spent are
            // not themselves part of what the raid takes.
            int loosed = LookoutVolley();
            float share = Mathf.Clamp(RaidShare - loosed * VolleyShareOff, 0f, RaidShare);

            foreach (var s in stores)
            {
                if (s == null || s.whole <= 0) continue;
                // Still at least one unit off any pile that has anything:
                // a volley blunts a raid, it does not turn one away.
                int took = Mathf.Max(1, Mathf.FloorToInt(s.whole * share));
                took = Take(s.resource, took);
                if (took > 0) away.AddRaided(s.resource, took);
            }
            foreach (var h in hands)
                if (h != null) h.mood = Mathf.Max(0f, h.mood - RaidMoodHit);
            raids++;
            away.raids++;
        }

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

            /// **Raiders, 2026-09-22.** Parallel lists like `res`/`got`, for
            /// the same JsonUtility reason -- what a raid took, per resource,
            /// while nobody was standing here to stop it.
            public List<string> raidRes = new List<string>();
            public List<int> raidGot = new List<int>();
            /// How many times this camp was raided during the absence.
            public int raids;

            /// Is there an absence in progress?
            public bool Open => sinceSeconds > 0.0;

            /// Worth showing the player at all, or just a quiet return.
            public bool Anything
            {
                get
                {
                    if (raised.Count > 0 || born.Count > 0) return true;
                    if (eaten > 0f || hungryDays > 0f) return true;
                    if (raids > 0) return true;
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

            /// Find or create this resource's raided row and add to it.
            public void AddRaided(string resource, int n)
            {
                if (string.IsNullOrEmpty(resource) || n == 0) return;
                for (int i = 0; i < raidRes.Count; i++)
                {
                    if (raidRes[i] != resource) continue;
                    raidGot[i] += n;
                    return;
                }
                raidRes.Add(resource);
                raidGot.Add(n);
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
        public bool TimberStarved
        {
            get
            {
                var f = Focus;
                return f != null && !f.TimberPaid
                    && CountOf(Res.Timber) <= 0 && Wood.standing < 1f;
            }
        }

        /// **The same, for the stone part.** An island whose seam is worked
        /// out and whose pile is empty cannot finish a building however much
        /// wood is standing -- and a sheet that said "NO TIMBER LEFT" at it
        /// would be sending the player to cut trees they do not need.
        public bool StoneStarved
        {
            get
            {
                var f = Focus;
                if (f == null || f.StonePaid) return false;
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
        /// **Pay the timber part of a site out of `labour` hand-days** --
        /// the pile first, then what is standing.
        ///
        /// Lifted out of `Step` whole when the queue arrived (2026-09-22):
        /// the block used to read `pending` directly, and a queue needs the
        /// same arithmetic pointed at whichever row is being served. Not one
        /// number changed in the move.
        void PayTimber(PendingBuild pending, ref float labour)
        {
            if (pending == null || labour <= 0f) return;
            float roomB = (pending.needed - pending.done) - pending.donePart;
            if (roomB <= 0f) return;

            // **The pile first.** Kevin, 2026-09-20: *"they gathered logs for
            // it but it never built."* They had: ten logs sat beside the fire
            // while the builders walked past them to cut fresh ones, and on a
            // small island the fresh ones ran out at 6 of 24 and the sawmill
            // stood as a drawing for ever. Timber already cut is carried five
            // metres, which is also why it goes in faster than timber still
            // growing.
            var pile = Store(Res.Timber);
            Haul(pile, ref pending.done, ref pending.donePart, ref roomB, ref labour);

            // Then whatever is left of the day goes on cutting.
            var wood = Wood;
            float wantB = Mathf.Max(0f, labour) * TimberPerHandPerDay;
            float gotB = Mathf.Min(wantB, Mathf.Min(wood.standing, roomB));
            if (gotB <= 0f) return;
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

        void PayStone(PendingBuild pending, ref float labour)
        {
            if (pending == null) return;
            float roomS = (pending.stoneNeeded - pending.stoneDone) - pending.stoneDonePart;
            if (roomS <= 0f || labour <= 0f) return;

            Haul(Store(Res.Stone), ref pending.stoneDone, ref pending.stoneDonePart,
                ref roomS, ref labour);

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

        /// **Pay the brick part out of `labour` hand-days -- from the pile
        /// and from nowhere else.**
        ///
        /// The haul half of `PayStone` with the seam half deleted rather than
        /// left empty, because there is no seam: nobody quarries a brick out
        /// of a hillside. A site short of brick therefore stalls until
        /// somebody makes some, which is the whole point of putting a good
        /// on the far side of a building.
        ///
        /// Runs last, out of whatever the timber and stone parts left, so a
        /// plan with `brickNeeded == 0` is bit-identical to the old path --
        /// `room <= 0` and it returns having touched nothing.
        void PayBrick(PendingBuild pending, ref float labour)
        {
            if (pending == null) return;
            float room = (pending.brickNeeded - pending.brickDone) - pending.brickDonePart;
            if (room <= 0f || labour <= 0f) return;

            Haul(Store(Res.Brick), ref pending.brickDone, ref pending.brickDonePart,
                ref room, ref labour);
        }

        /// **Carry from a pile into the blueprint, fractions and all.**
        ///
        /// Kevin, 2026-09-22: *"villagers carry 10 stone to a shelter that
        /// only has 0/2 continuously."* This block used to floor the carry to
        /// a whole unit and throw the remainder away:
        /// `FloorToInt(min(labour * HaulPerHandPerDay, ...))`. One quantum is
        /// `QuantumDays` (0.1) of a day and the haul rate is 12 a day, so one
        /// hand at FULL strength carries 1.2 units a quantum -- a hair over
        /// the one unit the floor needs. Dock that hand at all (`WorkFactor`
        /// scales with mood, and a hand goes "hungry" under 0.95) and the
        /// figure drops under 1.0, floors to **zero, every quantum, for
        /// ever**: the pile stays full, the counter never moves, and the
        /// bodies go on walking the load over because `BuilderWants` still
        /// says the site is short.
        ///
        /// Timber hid it, which is why it showed up on stone. A builder who
        /// hauls nothing falls through to CUTTING, and the cut accrues into
        /// `donePart` fractionally -- so the timber part always inched
        /// forward. Stone's seam is a third as rich (`ScatteredStoneShare`)
        /// and is usually worked out by the time a second building is sited,
        /// leaving the pile as the only source; brick has no seam at all.
        ///
        /// So the carry accrues into the same `*DonePart` field the cut uses,
        /// and the pile is debited in the same fractions through its own
        /// `part`. At full strength this is the old behaviour plus the
        /// remainder that used to be dropped; below it, it is the difference
        /// between slow
        /// and stopped. `room` and `labour` are spent by what was carried, so
        /// a site that wants 2 takes 2 out of a pile of 10 and the builder's
        /// remaining hand-days go on to the next part of the price.
        static void Haul(OutpostStore pile, ref int done, ref float part,
            ref float room, ref float labour)
        {
            if (pile == null || pile.whole <= 0 || room <= 0f || labour <= 0f) return;
            // Bounded by what is lying there, what the site still wants, and
            // how much of the day is left -- so carrying to a site that wants
            // 2 out of a pile of 10 delivers 2 and leaves 8 on the pile.
            float have = pile.whole + pile.part;
            float got = Mathf.Min(labour * HaulPerHandPerDay, Mathf.Min(have, room));
            if (got <= 0f) return;

            // **Off the pile in the same fractions it goes into the site**,
            // through `OutpostStore.part`, which exists for exactly this --
            // the sub-unit accrual gathering already uses. Units leaving the
            // ground therefore equal units entering the blueprint to the
            // fraction, which a floored debit against a fractional credit
            // would not.
            have -= got;
            pile.whole = Mathf.Max(0, Mathf.FloorToInt(have));
            pile.part = Mathf.Max(0f, have - pile.whole);

            part += got;
            int whole = Mathf.FloorToInt(part);
            if (whole > 0)
            {
                done += whole;
                part -= whole;
            }
            room -= got;
            labour -= got / HaulPerHandPerDay;
        }

        /// **What a builder here should be fetching right now**: logs until
        /// the timber part is paid, then stone, then nothing. One answer, so
        /// the arithmetic (`Step`), the body (`CampWorker`) and the mime all
        /// agree about which material a man is carrying.
        public string BuilderWants
        {
            get
            {
                // **The OLDEST unstocked site, and only that one.** The
                // queue's whole rule, in the one place every body reads.
                var p = Focus;
                if (p == null) return null;
                if (!p.TimberPaid) return Res.Timber;
                if (!p.StonePaid) return Res.Stone;
                return p.BrickPaid ? null : Res.Brick;
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

            // **And the stumps close over, from the outside in.** Kevin,
            // 2026-09-22. The same rate the timber stock regrows at, measured
            // against the trees that are down rather than against the stock's
            // ceiling, so a wood that was barely touched comes back slowly and
            // a stripped one comes back at the pace it was stripped. Held here
            // as a number only: `Outpost.DrawWood` is what decides the far
            // trees are the ones that come back, and it clamps this again
            // against the camp's own clearing once the ground can be seen.
            float woodRate = Res.RegrowPerDay(Res.Timber);
            if (treesFelled > 0 && woodRate > 0f && treesRegrown < treesFelled)
                treesRegrown = Mathf.Min(treesFelled,
                    treesRegrown + treesFelled * woodRate * days);
            else if (treesRegrown > treesFelled) treesRegrown = treesFelled;

            // **Building comes before everything, and draws on the same
            // standing timber.** A camp that has not been built yet has
            // nowhere to stockpile TO -- the ceiling is what the fire watches
            // over and there is no fire -- so a hand told to build is not
            // choosing between two piles, they are the reason there will be
            // one.
            // Captured before the building block touches `pending`, so the
            // completion check below can tell "finished just now" from
            // "was already sitting there ready to raise".
            // **One pass down the QUEUE, oldest first (2026-09-22).** The
            // hand-days are spent on `sites[0]` until it is stocked and only
            // then on `sites[1]`, in the same tick if there is a day left
            // over -- which is exactly "the haulers move on to the next one".
            // With one site queued this is the old block to the bit.
            float builders = 0f;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Build) builders += WorkFactor(h);
            if (builders > 0f && sites != null && sites.Count > 0)
            {
                float labour = builders * days;          // hand-days to spend
                for (int si = 0; si < sites.Count && labour > 0f; si++)
                {
                    var site = sites[si];
                    if (site == null || site.Complete) continue;

                    PayTimber(site, ref labour);
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
                    // block sees `roomS <= 0` and returns having touched
                    // nothing. A builder therefore finishes the logs first
                    // and starts on the rock in the same tick -- the body
                    // walking out to a boulder follows, because `CampWorker`
                    // asks the ledger the same question (`BuilderWants`) the
                    // arithmetic just answered.
                    PayStone(site, ref labour);
                    PayBrick(site, ref labour);

                    // The record of who's away doesn't care whether the raise
                    // was seen -- `Outpost.FinishReady` handles standing the
                    // mesh up separately, on the next `CatchUp`. This just
                    // notes that it happened during the absence.
                    if (site.Complete) away.raised.Add(site.planId);
                }
            }

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

                // **A hunter is the one hand whose stock and whose pile are
                // different things.** Game is counted in animals on the
                // ground; what he carries home is meat, and meat is Food. So
                // the take is metered in animals against the herd and paid in
                // `MeatPerAnimal` into the Food pile -- which is also why the
                // room he has to fill is the FOOD pile's, converted back into
                // animals before it can limit the kill.
                bool hunting = h.target == Res.Game;
                string into = hunting ? Res.Food : h.target;

                var store = Store(into, true);
                float room = (ceilingPer - store.whole) - store.part;
                if (room <= 0f) continue;
                if (hunting) room /= Res.MeatPerAnimal;

                float want = Res.GatherRate(h.target) * days * WorkFactorOn(h, into)
                    * PriorityMultiplier(into);

                // **A hunter with arrows, Kevin 2026-09-22.** *"build a
                // fletcher's building as well for bow and arrow."* A bow is
                // the difference between walking an animal down and taking
                // it at forty paces, so a quiver is worth `BowKillBonus` on
                // the kill rate -- and it is SPENT doing it, one arrow the
                // animal. The quiver is therefore a thing the camp burns
                // through, not a stock that sits there: stop making arrows
                // and the hunt quietly falls back to half again slower.
                //
                // The bonus is taken only as far as the arrows reach. A
                // hunter with two arrows left and four animals' worth of day
                // in him shoots two and walks the rest down, which is what
                // makes running dry read as a slope rather than a cliff.
                var quiver = hunting ? Store(Res.Arrows) : null;
                float arrowsHeld = quiver != null ? quiver.whole + quiver.part : 0f;
                if (hunting && arrowsHeld > 0f)
                {
                    float plain = want;
                    float armed = want * BowKillBonus;
                    // One arrow per animal taken, so the most the bow can add
                    // is the arrows in the quiver.
                    want = Mathf.Min(armed, plain + arrowsHeld);
                }

                float got = Mathf.Min(want, Mathf.Min(stock.standing, room));
                if (got <= 0f) continue;

                // Spend the quiver against what was actually killed, after
                // the herd and the larder have had their say -- a hunter
                // stopped by a full Food pile has not loosed an arrow.
                if (hunting && quiver != null && arrowsHeld > 0f)
                {
                    float spend = Mathf.Min(got, arrowsHeld);
                    quiver.part -= spend;
                    while (quiver.part < 0f && quiver.whole > 0) { quiver.whole--; quiver.part += 1f; }
                    if (quiver.part < 0f) quiver.part = 0f;
                }

                stock.standing -= got;
                if (h.target == Res.Timber) timberTaken += got;
                float paid = hunting ? got * Res.MeatPerAnimal : got;
                store.part += paid;
                int whole = Mathf.FloorToInt(store.part);
                if (whole > 0) { store.whole += whole; store.part -= whole; }
                away.Add(into, paid);
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

                float want = Mathf.Min(plan.rate * days * WorkFactorOn(h, plan.makes)
                    * PriorityMultiplier(plan.makes), room);
                if (want <= 0f) continue;

                // An input is consumed one for one, and a hand with nothing to
                // work on produces nothing. Deliberate, and the point of the
                // chain: a sawmill on an island with no timber is a shed.
                // **One input buys `plan.Yield` outputs, 2026-09-22.** It was
                // flatly one for one until the fletcher, who turns one log
                // into three arrows. `Yield` reads 1 for every plan that
                // never mentions it, so the sawmill, the forge, the kitchen
                // and the quarry come through this block spending exactly
                // what they spent before, to the bit.
                if (!string.IsNullOrEmpty(plan.takes))
                {
                    var from = Store(plan.takes);
                    float have = from != null ? from.whole + from.part : 0f;
                    float yield = plan.Yield;
                    // Clamp the OUTPUT by what the input can buy, not by the
                    // input itself -- one log left is three arrows, not one.
                    want = Mathf.Min(want, have * yield);
                    if (want <= 0f) continue;

                    float spent = want / yield;
                    from.part -= spent;
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
                // **Rations, 2026-09-22.** `EatMultiplier` is what `rations`
                // actually pays out against `EatPerHandPerDay` -- 1, a half,
                // or nothing. On `None` the pile is never touched at all, not
                // even if there is plenty sitting in it: it is a choice, not
                // a shortage.
                float need = eaters * EatPerHandPerDay * EatMultiplier * days;
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
                // **`None` is a hungry day by definition, 2026-09-22** --
                // there is no ration to have fallen short of, so `need`
                // itself is zero and the ordinary `eaten < need` test would
                // never fire. Nobody starves or leaves on this yet -- the
                // debt still just gets recorded for the sheet.
                bool starved = rations == Rations.None;
                if (starved || eaten < need) { hungerDays += days; away.hungryDays += days; }

                // **Mood, per quantum, so D2 (path independence) holds
                // exactly as the rest of `Step` does.** `fed01` is how much
                // of today's (rationed) need this quantum actually paid; a
                // hand not fully fed slides toward angry at
                // `MoodDropPerHungryDay`, scaled by how short they went, and
                // a hand fully fed climbs back at `MoodRecoverPerFedDay`. A
                // hand restored from an old save already has `mood = 1f`
                // (the field's default), which reads as a content hand with
                // no history to make up.
                //
                // **Half rations never recover, 2026-09-22** -- a hand fed
                // its full (halved) ration still grumbles rather than settling,
                // dropping at half `MoodDropPerHungryDay` instead of climbing.
                // `None` drops every hand at the full rate regardless of what
                // is sitting in the pile, same as the hunger-day accounting
                // above.
                float fed01 = need > 0f ? eaten / need : 1f;
                foreach (var h in hands)
                {
                    if (h == null) continue;
                    if (starved)
                        h.mood = Mathf.Max(0f, h.mood - MoodDropPerHungryDay * days);
                    else if (fed01 >= 1f)
                        h.mood = rations == Rations.Half
                            ? Mathf.Max(0f, h.mood - MoodDropPerHungryDay * 0.5f * days)
                            : Mathf.Min(1f, h.mood + MoodRecoverPerFedDay * days);
                    else
                        h.mood = Mathf.Max(0f, h.mood - MoodDropPerHungryDay * days * (1f - fed01));
                }
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

            // --- raiders, 2026-09-22 --------------------------------------------
            //
            // Only banks or bites while the ship is away -- with her there
            // the raiders are ships she can fight, not a clock on the camp.
            // A lookout on watch while you are home lets the camp relax
            // instead of just holding steady, which is why the threat comes
            // back down rather than only ever climbing.
            if (away.Open)
            {
                threat += ThreatRatePerDay * days;
                if (threat >= DaysToRaid)
                {
                    Raid();
                    threat -= DaysToRaid;
                }
            }
            else if (Guard >= 1f)
            {
                threat = Mathf.Max(0f, threat - days);
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
            // Nothing unstocked left in the QUEUE, not "nothing sited": a
            // builder whose site is stocked has the next drawing to serve.
            if (h.order == OutpostOrder.Build) return Focus == null;
            if (h.order == OutpostOrder.Gather)
            {
                var stock = Stock(h.target);
                // The hunter fills the Food pile, so a full Food pile is what
                // stops him -- and a herd below one animal is a herd he
                // cannot take one out of.
                if (h.target == Res.Game)
                    return RoomFor(Res.Food) <= 0 || stock == null || stock.standing < 1f;
                return RoomFor(h.target) <= 0 || stock == null || stock.standing < 1f;
            }
            if (h.order == OutpostOrder.Work)
            {
                // A lookout makes nothing and that is the job -- never
                // stalled for having nothing to show for standing watch.
                if (h.target == WatchtowerId) return false;
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

        /// **Net units per game-day this camp changes `resource` by, at its
        /// CURRENT orders, sign included.** Mirrors `Step` and `Stalled` term
        /// for term so the readout never disagrees with what a quantum
        /// actually pays -- a gatherer stalled on a full pile or a worked-out
        /// stock does not count, same as `Step` would skip them. Read-only,
        /// allocation-free: called once a frame per resource.
        public float RatePerDay(string resource)
        {
            float rate = 0f;

            foreach (var h in hands)
            {
                if (h == null) continue;

                if (h.order == OutpostOrder.Gather)
                {
                    // **A hunter reads on the Food line, in meat.** His
                    // target is Game and his rate is animals a day, so the
                    // readout would be in the wrong units on the wrong row
                    // if it took him at his word: half an animal a day is
                    // two Food a day, and Game itself never moves in a
                    // pile at all.
                    if (h.target == Res.Game)
                    {
                        if (Stalled(h)) continue;
                        // **The bow shows on BOTH lines, 2026-09-22.** A
                        // hunter with arrows kills half again as many animals
                        // and spends one apiece, so Food reads higher and
                        // Arrows reads as a drain. Mirrors `Step` term for
                        // term, which is the only way a readout stays honest
                        // about a good that is consumed rather than kept.
                        bool armed = CountOf(Res.Arrows) > 0;
                        float kills = Res.GatherRate(Res.Game)
                                      * WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
                        if (armed) kills *= BowKillBonus;
                        if (resource == Res.Food) rate += kills * Res.MeatPerAnimal;
                        else if (resource == Res.Arrows && armed) rate -= kills;
                        continue;
                    }
                    if (h.target != resource || Stalled(h)) continue;
                    rate += Res.GatherRate(resource) * WorkFactorOn(h, resource)
                        * PriorityMultiplier(resource);
                    continue;
                }

                if (h.order == OutpostOrder.Work)
                {
                    if (string.IsNullOrEmpty(h.target) || !built.Contains(h.target)) continue;
                    var plan = BuildPlans.Named(h.target);
                    if (plan.rate <= 0f || Stalled(h)) continue;
                    if (plan.makes == resource)
                        rate += plan.rate * WorkFactorOn(h, resource) * PriorityMultiplier(resource);
                    // Consumption scales with the same factor -- an angry
                    // worker draws down the input no faster than they make
                    // the output.
                    // Divided by the yield for the same reason `Step`
                    // divides: a fletcher making three arrows a day is
                    // drawing ONE log a day off the pile, and a readout that
                    // said three would have the player cutting twice what
                    // the bench can use.
                    else if (plan.takes == resource)
                        rate -= plan.rate * WorkFactorOn(h, plan.makes) / plan.Yield;
                }
                // Build hauls from the pile into the blueprint -- a transfer,
                // not production, so it never shows up here.
            }

            // Every quantum eats regardless of whether the pile can pay --
            // an empty pile just means they go hungry, and the drain is the
            // whole point of the readout. Scaled by `EatMultiplier`, 2026-09-22,
            // so the readout agrees with `Step`: a camp on half or no rations
            // does not drain a full ration it was never going to spend.
            if (resource == Res.Food && hands.Count > 0)
                rate -= hands.Count * EatPerHandPerDay * EatMultiplier;

            return rate;
        }

        /// Only the positive terms of `RatePerDay` -- what is being made or
        /// gathered, ignoring what it costs to make it. "How fast is this
        /// being produced," for the target line.
        public float MakeRatePerDay(string resource)
        {
            float rate = 0f;

            foreach (var h in hands)
            {
                if (h == null) continue;

                if (h.order == OutpostOrder.Gather)
                {
                    // **A hunter reads on the Food line, in meat.** His
                    // target is Game and his rate is animals a day, so the
                    // readout would be in the wrong units on the wrong row
                    // if it took him at his word: half an animal a day is
                    // two Food a day, and Game itself never moves in a
                    // pile at all.
                    if (h.target == Res.Game)
                    {
                        if (resource != Res.Food || Stalled(h)) continue;
                        // The bow, as `Step` and `RatePerDay` have it. This
                        // readout is the POSITIVE terms only, so the arrows
                        // it costs are deliberately not subtracted here --
                        // only the meat they buy is.
                        float kills = Res.GatherRate(Res.Game)
                                      * WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
                        if (CountOf(Res.Arrows) > 0) kills *= BowKillBonus;
                        rate += kills * Res.MeatPerAnimal;
                        continue;
                    }
                    if (h.target != resource || Stalled(h)) continue;
                    rate += Res.GatherRate(resource) * WorkFactorOn(h, resource)
                        * PriorityMultiplier(resource);
                    continue;
                }

                if (h.order == OutpostOrder.Work)
                {
                    if (string.IsNullOrEmpty(h.target) || !built.Contains(h.target)) continue;
                    var plan = BuildPlans.Named(h.target);
                    if (plan.rate <= 0f || plan.makes != resource || Stalled(h)) continue;
                    rate += plan.rate * WorkFactorOn(h, resource) * PriorityMultiplier(resource);
                }
            }

            return rate;
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
