using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// What has been built on ONE island, where the next thing goes, and how
    /// much that place can keep.
    ///
    /// Was `Village`, and was home-only. It is per-island now because a
    /// campfire on any shore is the same object as the home settlement at an
    /// earlier stage -- **home is outpost zero**. The alternative was two
    /// parallel building systems, and every feature after this one would have
    /// been written twice.
    ///
    /// `Settlement` measures the GROUND -- the largest contiguous piece of
    /// buildable land on the island. This owns what stands on it. They are
    /// separate because the ground is measured once and never changes, while
    /// what is built on it grows across a session.
    ///
    /// **Nothing here flattens anything.** `TerrainHeight.Height` is a pure
    /// function of world position running in Burst jobs on streamed chunks,
    /// so there is no per-building data it could consult and no pad to cut.
    /// A site is CHOSEN on ground that is already flat enough, its corners
    /// are measured, and the building sits at its highest corner with a
    /// footing deep enough to bridge down to its lowest.
    public class Outpost : MonoBehaviour
    {
        /// The home settlement. Kept as a static because the voyage loop is
        /// still berth-to-berth and the panel asks about home specifically --
        /// but home is now one outpost among many, not the only one there can
        /// be. Anything asking about the island the ship is AT must use `Of`,
        /// never this.
        public static Outpost Home { get; private set; }

        static readonly List<Outpost> all = new List<Outpost>();
        public static IReadOnlyList<Outpost> All => all;

        /// Summed `MakeRatePerDay` for `resource` over every camp that
        /// exists -- "how fast is this being made, across the whole
        /// archipelago," for the target line. Allocation-free.
        public static float MakeRateAcrossCamps(string resource)
        {
            float rate = 0f;
            foreach (var o in all)
                if (o != null && o.HasCamp && o.Ledger != null)
                    rate += o.Ledger.MakeRatePerDay(resource);
            return rate;
        }

        /// Summed pile of `resource` sitting in every camp's stores right
        /// now -- what has already been made and is just waiting for a
        /// voyage to carry it home.
        public static int PiledAcrossCamps(string resource)
        {
            int total = 0;
            foreach (var o in all)
                if (o != null && o.HasCamp && o.Ledger != null)
                    total += o.Ledger.CountOf(resource);
            return total;
        }

        /// Islands whose ground has been surveyed and found wanting.
        ///
        /// A refusal has to be remembered or it is not lazy at all: `Of`
        /// returns null for a refused island exactly as it does for one nobody
        /// has looked at, so without this every anchoring at a barren rock
        /// re-runs the full raster. Cleared with the world -- see
        /// `ForgetSurveys`.
        static readonly HashSet<Island> refused = new HashSet<Island>();

        /// True once the ground here has been looked at, whatever the answer.
        public static bool Surveyed(Island isle)
            => isle != null && (Of(isle) != null || refused.Contains(isle));

        /// Drop what was learned about the old world. Statics outlive play mode
        /// here (domain reload is off), and an island reference from a previous
        /// session is a destroyed object that still hashes.
        public static void ForgetSurveys()
        {
            refused.Clear();
            surveying.Clear();
        }

        [Tooltip("What open ground keeps before anything is built. ZERO everywhere: an island with nobody on it keeps nothing, and the campfire is what first gives a place a ceiling at all.")]
        [SerializeField] int openCapacity = 30;

        [Tooltip("Metres between buildings -- room to walk round one, which is what a village looks like from above.")]
        /// **Air between two buildings' footprints, metres.**
        ///
        /// Kevin, 2026-09-23: *"I can't place blueprints on a lot of areas
        /// that look clear."* This was 6 m, and it is added to the
        /// CIRCUMSCRIBED circle of each footprint on BOTH sides -- so two
        /// 8x5 huts (half-diagonal 4.7 m each) needed 12.4 m between their
        /// centres, which on a phone screen is two huts that look a whole
        /// hut apart and still refuse. The footprint is already generous
        /// (a circle round a rectangle), so the constant only has to be a
        /// path between them. 1.5 m is a man with a log.
        [SerializeField] float spacing = 1.5f;

        readonly List<Building> built = new List<Building>();
        readonly List<Vector4> reserved = new List<Vector4>();   // xyz = point, w = radius

        Settlement site;
        System.Func<float, float, float> height;
        float minHeight;
        Island island;

        /// Which island this outpost is on. Every lookup keys off this rather
        /// than off an index: islands are discovered by flood-fill in streamer
        /// order, so an index is not an identity.
        public Island Island => island != null ? island : (island = GetComponent<Island>());

        public bool IsHome => Island != null && Island.IsHome;

        /// Surveyed and ready to build on. False on an island whose ground has
        /// been looked at and found wanting -- a crag with no flat.
        public bool Sited => site != null && height != null;

        /// The cleared ground this outpost stands in.
        ///
        /// At home it is handed to the scenery bake as a keep-out BEFORE the
        /// trees go in, because the scenery is several hundred trees welded
        /// into one mesh and there is no taking one out afterwards. Anywhere
        /// else the trees are already standing when the player chooses to
        /// build, so the clearing gets cut at runtime through the harvest path
        /// instead -- which is the better story anyway: making camp fells the
        /// wood it stands on.
        public Vector3 ClearingCentre { get; private set; }
        public float ClearingRadius { get; private set; }

        /// **What this place actually is.** The buildings and the crew you can
        /// see are a rendering of this; see OutpostLedger.
        ///
        /// Seeded when the ground is surveyed, because the worked area is what
        /// sets how much timber is standing within reach.
        [SerializeField] OutpostLedger ledger;
        public OutpostLedger Ledger => ledger;

        /// Bring the ledger up to now. Free to call as often as you like --
        /// the tick advances on a fixed grid of game time, so asking twice in
        /// a frame does nothing the second time.
        public void CatchUp()
        {
            if (ledger == null) return;
            // Nobody watching, nobody walking: an unwatched camp is all
            // arithmetic (`OutpostHand.walkingIn`).
            if (!Watched)
                foreach (var h in ledger.hands) if (h != null) h.walkingIn = false;
            // **One definition of the ceiling, and it is what stands on the
            // ground.** The ledger could have carried its own and drifted from
            // the buildings the moment a storehouse went up; instead the
            // buildings ARE the ledger's ceiling, pushed in before every tick.
            ledger.ceilingPer = KeepsOfEach;
            // A camp restored from a save written before stone was a price
            // has no seam in its books. See `EnsureStoneStock`.
            EnsureStoneStock();
            // An island with a herd on it has game in its books. Same reason
            // as the stone: a save written before hunting existed has none.
            EnsureGameStock();
            // The fire may have been lit (or restored) since the survey, and
            // the boulders belong round it. No-op after the first call.
            PlaceCampStone();
            ReconcileWood();
            ReconcileCrops();
            ReconcileGame();
            // The raiders are ships, and the ships are the authority: the
            // ledger is told how many are offshore and never guesses. Sink
            // them and the clock stops.
            ledger.raiders = Combat.EnemyShip.CountAt(Island);
            ledger.Tick(TimeOfDay.Seconds);
            FeedTheFire();

            // The arithmetic can finish a building on an island nobody is
            // looking at, so the raise cannot live in the tick -- it needs a
            // scene to put something in. It happens here instead, which is
            // called on arrival, and whose whole job is "make the world agree
            // with the ledger".
            if (ledger.ReadyToRaise) FinishReady();
            EnsureBlueprints();
            SyncFelling();
            GatherSync.Sync(this);   // stone, ore and spice props go as the seam is worked
            SyncHarvest();
            SyncHunting();
            // The piles beside the fire are drawn from the stores, so they
            // want to exist wherever the stores are being looked at.
            if (HasCamp) CampPiles.EnsureOn(this);
            // A hut that filled itself while nobody was here has people in it
            // now. Same reason the raise cannot live in the tick: the
            // arithmetic recruits, but only a scene can put a body in.
            EnsureBornBodies();
        }

        /// **The campfire is the provisions gauge** (settled 2026-09-13, wired
        /// 2026-09-22). Three days of food for everyone here is a bright fire;
        /// an empty pile is embers. Pushed here, after the tick, because this
        /// is the one place the scene is made to agree with the ledger -- and
        /// the light is only found once, since it never moves.
        Campfire fire;
        void FeedTheFire()
        {
            if (fire == null)
            {
                foreach (var b in built)
                {
                    if (b == null) continue;
                    fire = b.GetComponentInChildren<Campfire>(true);
                    if (fire != null) break;
                }
                if (fire == null) return;
            }
            fire.health01 = ledger.Health01;
        }

        /// **While the trees can be seen, they are the authority on whether
        /// there is timber.** (2026-09-21)
        ///
        /// The ledger's stock is an abstraction seeded off hectares
        /// (`OutpostLedger.SeedStock`) so that a camp can be worked with the
        /// island unloaded; the wood is ~230 trees a hectare against the
        /// stock's 40. Left alone the two disagree in both directions: the
        /// books say "cut out" with a hundred trees standing round the fire,
        /// so a builder swings at a tree that will never fall; or a small
        /// islet's mesh runs out first and the books go on paying logs out of
        /// nothing while the hands potter. Kevin's ask -- *gather the
        /// resources necessary, as long as those resources exist on the
        /// island* -- only means anything if "exist" is what he can see.
        ///
        /// So, whenever the wood is loaded, the stock may never exceed the
        /// trees standing, and a stock that has run dry with trees still up
        /// is refilled from them. Both pulls are toward the mesh and
        /// idempotent, so ticking a watched camp and ticking it unwatched
        /// still land on the same books (D2); what changes is that the
        /// island's stock IS its trees, one log each, which is what
        /// `FellOwed` already assumed. Unloaded, nothing here runs and the
        /// abstraction carries on as before.
        void ReconcileWood()
        {
            var wood = WoodHere();
            if (wood == null || wood.TreeCount == 0) return;
            int standing = 0;
            for (int i = 0; i < wood.TreeCount; i++)
                if (!wood.TreeAt(i).felled) standing++;

            var stock = ledger.Wood;
            if (stock.standing > standing) stock.standing = standing;
            else if (stock.standing < 1f && standing >= 1)
                stock.standing = Mathf.Min(Mathf.Max(1f, stock.standingMax), standing);
        }

        /// **The herd is the authority on game, the way the trees are on
        /// timber.** (2026-09-22)
        ///
        /// A Game stock is counted in ANIMALS, and an animal is a thing you
        /// can walk up to and look at -- so the same two pulls the wood gets
        /// apply here, and for the same reason. The books may never claim
        /// more goats than are standing on the crags, or a hunter would stalk
        /// something that was never there; and a stock that has run out with
        /// animals still alive is refilled off them, or a herd that bred back
        /// while the island was unloaded would be invisible to the camp.
        /// Both pulls are toward the mesh and both are idempotent, so a
        /// watched camp and an unwatched one land on the same books.
        ///
        /// The ceiling comes off the herd too, and only ever UP: a wood's
        /// ceiling is a fact about the hectares, but nothing ever told us how
        /// many animals this island is meant to carry except the most we have
        /// ever seen on it.
        void ReconcileGame()
        {
            var stock = ledger != null ? ledger.Stock(Res.Game) : null;
            if (stock == null) return;
            var fauna = FaunaHere();
            if (fauna == null) return;
            int alive = AliveHere(fauna);
            if (alive <= 0) return;

            if (stock.standingMax < alive) stock.standingMax = alive;
            if (stock.standing > alive) stock.standing = alive;
            else if (stock.standing < 1f) stock.standing = alive;
        }

        /// Live animals on this island right now. `Animal.Die` takes itself
        /// out of the `FaunaLod`, but it flops for three seconds before it is
        /// destroyed, so a dead one can still be in the list for a frame --
        /// counting it would be counting a carcass as a goat.
        static int AliveHere(FaunaLod fauna)
        {
            var animals = fauna.Animals;
            if (animals == null) return 0;
            int n = 0;
            for (int i = 0; i < animals.Count; i++)
            {
                var a = animals[i];
                if (a != null && !a.Dead) n++;
            }
            return n;
        }

        /// Is there a camp here at all, or only ground that would take one?
        /// The fire is the difference.
        public bool HasCamp => CountOf(BuildPlans.Campfire.id) > 0;

        /// Where the camp itself is: the fire, or the blueprint of one.
        ///
        /// NOT `ClearingCentre`. The clearing is what the survey found and
        /// what the scenery bake was told to keep out of; the camp is where
        /// the PLAYER put it, which since the blueprint pass is a point they
        /// chose off the ground with a ring round the ship. They coincide at
        /// home and need not anywhere else.
        public Vector3 CampCentre => hasCampCentre ? campCentre : ClearingCentre;
        Vector3 campCentre;
        bool hasCampCentre;

        /// **How far from the town centre anything else may be sited, metres.**
        ///
        /// Kevin, 2026-09-23: *"i want you to be able to choose where you
        /// want on the island to build your town center"* -- and once it
        /// stands, the rest of the town goes NEAR it. The town centre itself
        /// (the campfire, the first thing sited) has no reach rule at all:
        /// anywhere `CanPlace` takes is fine. A guess; the dial.
        public const float TownRadius = 40f;

        /// **The one reach rule**, asked by `SiteFresh` (so the raise and the
        /// queue agree) and by `CampSiting` (so the ghost and the ring agree).
        /// False, with nothing to say, while there is no town centre yet or
        /// when the thing being sited IS the town centre.
        public bool TooFarFromTown(BuildPlan plan, Vector3 at, out string why)
        {
            why = "";
            if (!hasCampCentre || plan.kind == BuildKind.Fire) return false;
            // A pier goes where the deep water is, which for a town sited
            // inland (2026-09-23, "anywhere on the island") can be well
            // past the reach -- and the harbour is the plan's Phase 3.
            if (plan.kind == BuildKind.Pier) return false;
            float d = Island.FlatDistance(at, campCentre);
            if (d <= TownRadius) return false;
            why = $"too far from the town centre ({d:F0} m, {TownRadius:F0} m is the limit)";
            return true;
        }

        /// **Move the camp's centre, and everything keyed to it.** The
        /// ledger key follows (see `SiteFresh`), and a path grid baked round
        /// the old centre is thrown away: since 2026-09-23 the town centre
        /// can be anywhere on the island, so the grid built on arrival
        /// (centred on the survey's clearing) may not even cover it.
        void SetCampCentre(Vector3 at)
        {
            bool moved = !hasCampCentre || Island.FlatDistance(at, campCentre) > 8f;
            campCentre = at;
            hasCampCentre = true;
            if (ledger != null) ledger.SetKey(campCentre);
            if (moved)
            {
                var grid = GetComponent<CampPath>();
                if (grid != null) grid.Invalidate();
            }
        }

        /// Has the player put a fire (or its blueprint) down here, or is
        /// `CampCentre` still the survey's guess? A save carries the answer:
        /// a loaded camp whose centre fell back to the clearing would key
        /// itself somewhere nobody lives.
        public bool HasCampCentre => hasCampCentre;

        /// **The drawings standing here, one per queued site.** Rebuilt
        /// from the ledger whenever the island is loaded -- never the only
        /// copy. Since 2026-09-22 a camp can have several (Kevin: *"I want
        /// to be able to place more blueprints at once"*), so this is a list
        /// keyed to `OutpostLedger.sites` by the row each `BuildSite` holds.
        readonly List<BuildSite> blueprints = new List<BuildSite>();

        /// The drawing for this row, or null.
        BuildSite BlueprintFor(PendingBuild row)
        {
            if (row == null) return null;
            for (int i = 0; i < blueprints.Count; i++)
                if (blueprints[i] != null && blueprints[i].Row == row) return blueprints[i];
            return null;
        }

        /// Take one drawing down and forget it.
        void RetireBlueprint(PendingBuild row)
        {
            for (int i = blueprints.Count - 1; i >= 0; i--)
            {
                var b = blueprints[i];
                if (b == null) { blueprints.RemoveAt(i); continue; }
                if (row != null && b.Row != row) continue;
                b.Retire();
                blueprints.RemoveAt(i);
            }
        }

        /// Every drawing down (a save arriving, the island leaving).
        void RetireAllBlueprints()
        {
            for (int i = 0; i < blueprints.Count; i++)
                if (blueprints[i] != null) blueprints[i].Retire();
            blueprints.Clear();
        }

        /// Is something sited here and waiting on wood?
        public bool Building => ledger != null && ledger.Building;

        /// **Site a plan: put the blueprint down.**
        ///
        /// This is the moment the player commits an island to something, and
        /// it costs nothing but the decision. It writes the row that makes the
        /// blueprint real for as long as it takes — whether or not anybody is
        /// watching — and it puts every hand here on to building it, because
        /// until there is a fire there is no pile for a cutter to cut into.
        ///
        /// **It does NOT fell the site, and the first version did.** Clearing
        /// the ground at siting sounded right and quietly broke the feature:
        /// a campfire costs four logs, the probe's spot had four trees on it,
        /// and the camp finished the frame it was placed — no blueprint, no
        /// crew, nothing to come back to. The ground is cleared when the thing
        /// is BUILT, which is also the better story: a blueprint stands among
        /// the trees it is going to take down.
        ///
        /// Returns the logs still wanted, or -1 with a reason.
        public int Site(BuildPlan plan, Vector3 at, out string why)
            => Site(plan, at, AutoYaw(at), out why);

        public int Site(BuildPlan plan, Vector3 at, float yaw, out string why)
            => Site(plan, at, yaw, false, out why);

        /// **Site it, or MOVE what is already sited.**
        ///
        /// Kevin, 2026-09-21: the blueprint's own panel offers *move*, and a
        /// drawing that has had three logs carried to it must still have them
        /// when it lands somewhere else -- the wood was cut and carried, and
        /// picking the drawing up does not put it back in the tree. So with
        /// `keepProgress` the pending row for THE SAME PLAN is lifted whole:
        /// only x, z and yaw change, `done/donePart/stoneDone/stoneDonePart`
        /// ride along, and a refusal at the new spot leaves the old row and
        /// its drawing exactly where they were.
        public int Site(BuildPlan plan, Vector3 at, float yaw, bool keepProgress, out string why)
            => Site(plan, at, yaw, keepProgress && ledger != null ? ledger.Pending : null, out why);

        /// **Site it, or MOVE the row `moving` names.**
        ///
        /// With a queue the mover has to say WHICH drawing it is carrying --
        /// "the pending one" stopped being an answer on 2026-09-22. The row
        /// is lifted OUT of the queue for the tests below (so the ground it
        /// is standing on does not refuse it to itself), and goes back at
        /// the same place in the order if the new spot says no: a refused
        /// move must leave the camp exactly as it was, position in the queue
        /// included.
        public int Site(BuildPlan plan, Vector3 at, float yaw, PendingBuild moving, out string why)
        {
            if (ledger == null) { why = "this ground was never surveyed"; return -1; }
            ledger.MigratePending();
            PendingBuild carried = null;
            int carriedAt = -1;
            if (moving != null && moving.planId == plan.id)
            {
                carriedAt = ledger.sites.IndexOf(moving);
                if (carriedAt >= 0)
                {
                    // Its plot, as it stands, before it leaves the queue.
                    SnapshotDown(moving, snapTrees, snapRocks);
                    carried = moving;
                    ledger.sites.RemoveAt(carriedAt);
                    RetireBlueprint(carried);
                }
            }
            int placed = SiteFresh(plan, at, yaw, out why);
            if (carried != null)
            {
                var landed = ledger.sites.Count > 0 ? ledger.sites[ledger.sites.Count - 1] : null;
                if (placed < 0 || landed == null)
                {
                    ledger.sites.Insert(Mathf.Clamp(carriedAt, 0, ledger.sites.Count), carried);
                    EnsureBlueprints();
                    return placed;
                }
                // The new row was appended; put it back where the old one
                // stood in the queue, then pour the old row's progress in.
                ledger.sites.RemoveAt(ledger.sites.Count - 1);
                ledger.sites.Insert(Mathf.Clamp(carriedAt, 0, ledger.sites.Count), landed);
                landed.done = Mathf.Min(carried.done, landed.needed);
                landed.donePart = carried.donePart;
                landed.stoneDone = Mathf.Min(carried.stoneDone, landed.stoneNeeded);
                landed.stoneDonePart = carried.stoneDonePart;
                // The third part travels with the other two. Nothing charges
                // brick yet, so today this always moves 0 -- see
                // `BuildPlan.baseBrickCost`.
                landed.brickDone = Mathf.Min(carried.brickDone, landed.brickNeeded);
                landed.brickDonePart = carried.brickDonePart;
                // The labour travels with the materials: picking a
                // half-raised frame up and setting it down eight metres
                // away does not un-build it.
                landed.phased = true;
                landed.built = Mathf.Min(carried.built, landed.LabourNeeded);
                // **The clearing does NOT travel** (2026-09-23): it was work
                // on the old ground. The new plot was counted fresh by
                // `SiteFresh`; the old plot's stumps go back to the camp.
                ReturnToCamp(snapTrees, snapRocks);
                SyncFelling();
                GatherSync.Sync(this);
                var drawn = BlueprintFor(landed);
                if (drawn != null) drawn.Refresh(landed);
                // Paid in full already? Then moving it finishes it.
                if (ledger.ReadyToRaise) FinishReady();
                return Mathf.Max(0, landed.needed - landed.done);
            }
            return placed;
        }

        int SiteFresh(BuildPlan plan, Vector3 at, float yaw, out string why)
        {
            if (ledger == null) { why = "this ground was never surveyed"; return -1; }
            ledger.MigratePending();
            // **The queue replaced the refusal, 2026-09-22.** "Something is
            // already being built here" is gone: a camp can hold as many
            // drawings as there is ground for them. What survives is the
            // one-of-each rule, which now has to cover the drawings too --
            // otherwise the way to get two sawmills is to site one twice.
            if (CountOf(plan.id) > 0) { why = $"there is already a {plan.label} here"; return -1; }
            if (ledger.Queued(plan.id)) { why = $"a {plan.label} is already going up here"; return -1; }
            // **The belt, 2026-09-23** -- the sheet already hides a locked
            // plan; this refuses it too, so nothing can queue a station the
            // fire has not opened yet by some other path in.
            if (!ledger.PlanUnlocked(plan.id)) { why = ledger.PlanLockReason(plan.id); return -1; }
            if (!CanPlace(plan, at, yaw, out why, out float lo, out float hi)) return -1;
            // Near the town centre, once there is one (2026-09-23).
            if (TooFarFromTown(plan, at, out why)) return -1;

            // **Only the FIRE says where the camp is.** Until 2026-09-20 every
            // siting moved the camp centre -- harmless while the campfire was
            // the only thing that could be sited, and wrong from the day the
            // build list grew: site a store hut 22 m off and the ring of hands,
            // the piles, the order the wood is felled in and the ledger's SAVE
            // KEY all moved onto the drawing of a hut. `HandProbe` found it by
            // dropping a man on the fire and being told he was on a blueprint.
            Vector3 spot = at;
            spot.y = hi;
            bool isTheCamp = plan.kind == BuildKind.Fire || !hasCampCentre;
            if (isTheCamp)
            {
                // The key moves to where the player put it, and it moves NOW --
                // before any wood is counted. A ledger keyed to the survey's
                // clearing and then filled by a camp forty metres away is a
                // camp that will not be found again after a save.
                SetCampCentre(spot);
            }
            var row = new PendingBuild
            {
                planId = plan.id,
                x = spot.x,
                z = spot.z,
                yaw = yaw,
                // A pier's length was chosen by the beach, not the plan;
                // anything else comes back at its own size (0).
                length = plan.kind == BuildKind.Pier ? plan.footprint.x : 0f,
                needed = Mathf.Max(0, plan.cost),
                // **The second half of the price, 2026-09-21.** Zero on the
                // campfire, so the first thing anybody builds is paid in
                // logs exactly as it always was.
                stoneNeeded = Mathf.Max(0, plan.stoneCost),
                // **The third part of the price, 2026-09-22.** Zero on every
                // plan there is; it exists so an upgrade can ask for brick
                // without re-threading the blueprint. See
                // `BuildPlan.baseBrickCost`.
                brickNeeded = Mathf.Max(0, plan.brickCost),
                // Born into the two phases; `MigratePending` only has work
                // to do on rows that came out of an older save.
                phased = true,
            };
            // **Count the plot, 2026-09-23**, before the row joins the
            // queue (so the registry and the camp's order are still the ones
            // without it). Trees and rocks never refuse a siting.
            TakeFootprint(row);
            // Newest goes last: the queue is served oldest first.
            ledger.sites.Add(row);
            SyncFelling();
            GatherSync.Sync(this);

            // Making camp is everybody's job: there is no fire yet to idle
            // by. A LATER building still orders nobody who HAS an order
            // (Kevin, 2026-09-21: "when assigning someone a task everyone
            // assumes that task") -- but since 2026-09-23 the IDLE ones take
            // it up on their own (Kevin: "i want villagers to take
            // initiative"), see `OutpostLedger.EnlistFree`.
            if (plan.kind == BuildKind.Fire) ledger.OrderAll(OutpostOrder.Build);
            else if (ledger.EnlistFree() > 0 && Watched) PuppetsToWork();
            EnsureBlueprints();
            // A plan that costs nothing is finished the moment it is sited.
            // Nothing does today; the dev path (`MakeCamp`) reaches the same
            // door by paying the cost outright.
            if (ledger.ReadyToRaise) FinishReady();
            why = "";
            return Mathf.Max(0, row.needed - row.done);
        }

        /// Logs that came out of the ground the last thing built here stands
        /// on. Reported rather than returned because the felling now happens
        /// when the build FINISHES, which can be days after the player sited
        /// it and on a frame nobody asked a question on.
        public int LastClearingFelled { get; private set; }

        /// Draw the blueprint if the ledger says there is one and nothing is
        /// drawing it. Called on arrival, so a camp you sited and sailed away
        /// from is standing there half built when you get back.
        void EnsureBlueprints()
        {
            if (ledger == null || !Sited) return;
            ledger.MigratePending();

            // Drawings whose row has left the queue (raised, cancelled,
            // moved) come down first, so the loop below never sees two
            // objects claiming one spot.
            for (int i = blueprints.Count - 1; i >= 0; i--)
            {
                var b = blueprints[i];
                if (b == null) { blueprints.RemoveAt(i); continue; }
                if (b.Row != null && ledger.sites.Contains(b.Row)) continue;
                b.Retire();
                blueprints.RemoveAt(i);
            }

            foreach (var row in ledger.sites)
            {
                if (row == null || string.IsNullOrEmpty(row.planId)) continue;
                if (row.Complete) continue;             // it is waiting to be raised
                if (BlueprintFor(row) != null) continue;

                var plan = PlanFor(row.planId, row.length);

                // **A wall site's drawing is its own line.** It is not
                // tested against `CanPlace` at all -- a segment was tested
                // by `CanPlaceWall` when it was drawn, and re-testing it
                // here against the rectangle rules would refuse every
                // segment in a run to the one beside it.
                if (row.isWall)
                {
                    var wallSite = BuildSite.PlaceWall(this, plan, row.postA, row.postB,
                        row.planId == BuildPlans.Gate.id);
                    wallSite.Bind(row);
                    wallSite.Refresh(row);
                    blueprints.Add(wallSite);
                    continue;
                }

                Vector3 at = row.At;
                at.y = height(at.x, at.z);
                // It is in the queue, so `Clear` would refuse it to itself.
                var wasIgnoring = IgnoreSite;
                IgnoreSite = row;
                bool ok = CanPlace(plan, at, row.yaw, out _, out float lo, out float hi);
                IgnoreSite = wasIgnoring;
                if (!ok)
                {
                    // The ground moved under a saved blueprint (a terrain
                    // parameter changed between sessions), or the site next
                    // to it in the queue is what the test tripped on. Draw it
                    // anyway at the height the field gives now: refusing to
                    // draw it would leave a row nobody can see, act on or
                    // cancel.
                    lo = hi = at.y;
                }
                at.y = hi;
                var site = BuildSite.Place(this, plan, at,
                    Quaternion.Euler(0f, row.yaw, 0f), hi - lo);
                site.Bind(row);
                site.Refresh(row);
                blueprints.Add(site);
            }
        }

        /// The wood is in: take the drawing down and stand the thing up.
        /// **Stand up everything in the queue that is paid for.** Oldest
        /// first, and as many as are ready: a camp that was away for a week
        /// can come back to two finished buildings.
        void FinishReady()
        {
            if (ledger == null) return;
            ledger.MigratePending();
            for (int guard = 0; guard < 32; guard++)
            {
                var row = ledger.FirstStocked;
                if (row == null) return;
                if (!RaiseRow(row)) return;
            }
        }

        /// The wood is in: take the drawing down and stand the thing up.
        /// False when the ground refused it -- the row stays in the queue
        /// and the player can move it.
        bool RaiseRow(PendingBuild row)
        {
            if (ledger == null || row == null) return false;
            var plan = PlanFor(row.planId, row.length);
            Vector3 at = row.At;
            if (height != null) at.y = height(at.x, at.z);

            // The blueprint goes first. `Raise` reserves the ground it stands
            // on, and the drawing is not a reservation -- but leaving it up
            // for a frame beside the real thing is two buildings in one place,
            // which is exactly what a player reports as a duplicate.
            RetireBlueprint(row);

            // **And the ROW comes out of the queue before the raise.** Since
            // 2026-09-22 `Clear` treats every queued site as occupied ground
            // (so two drawings cannot overlap), and a site still in the list
            // would refuse the building it IS. Put back on a refusal.
            int wasAt = ledger.sites.IndexOf(row);
            if (wasAt >= 0) ledger.sites.RemoveAt(wasAt);

            // **A wall row is raised along its LINE, not on its plot.**
            // Everything above is the same (the drawing comes down, the row
            // leaves the queue so nothing refuses itself); what differs is
            // that the thing standing up has two ends.
            if (row.isWall)
            {
                var seg = RaiseWall(row);
                if (seg == null)
                {
                    ledger.sites.Insert(Mathf.Clamp(wasAt < 0 ? 0 : wasAt, 0, ledger.sites.Count), row);
                    EnsureBlueprints();
                    return false;
                }
                Save.SaveGame.Autosave("a " + plan.label + " was raised");
                return true;
            }

            var b = Raise(plan, at, row.yaw);
            if (b == null)
            {
                // Refused on ground it was green on when it was sited. Rather
                // than silently eating the wood, keep the row: the blueprint
                // comes back next frame and the player can move it.
                ledger.sites.Insert(Mathf.Clamp(wasAt < 0 ? 0 : wasAt, 0, ledger.sites.Count), row);
                EnsureBlueprints();
                return false;
            }

            // The fire is the camp; anything else is a building AT the camp.
            // See `Site`.
            Vector3 stoodAt = b.transform.position;
            if (plan.kind == BuildKind.Fire || !hasCampCentre)
                SetCampCentre(stoodAt);
            ledger.built.Add(plan.id);
            ledger.ceilingPer = KeepsOfEach;

            // **The ground is cleared now, not when it was sited.** At home
            // the village clearing is reserved before the scenery is baked;
            // anywhere else the trees are standing when the player chooses, so
            // they come down through the same path the crew fell them by --
            // and what comes down is a camp appearing in the wood rather than
            // a gap appearing where a camp might one day go.
            //
            // **Only the FIRE fells a ring now, 2026-09-23.** Every other
            // building's plot was cleared by the hands before it could be
            // raised (`PendingBuild.Cleared` gates `Complete`), and its
            // footprint is in the clearing registry from here on -- a 7.5 m
            // ring felled at the raise was the old instant clearing, and it
            // was not reproducible on a reload either. The fire keeps it:
            // making camp is the one raise with nobody yet to clear for it.
            LastClearingFelled = 0;
            var wood = GetComponentInChildren<Terrain.SceneryWood>();
            EnsureClearing(false);   // the registry as of this raise
            if (wood != null && plan.kind == BuildKind.Fire)
                LastClearingFelled = wood.FellWithin(stoodAt, CampClearingRadius,
                    i => i < siteTree.Length && siteTree[i]);
            if (LastClearingFelled > 0)
                ledger.Add(Res.Timber, LastClearingFelled);

            // **The clearing counts toward the wood the ledger has already
            // cut.** Building the fire consumed four logs of standing timber,
            // and `SyncFelling` would take four trees down for them --
            // somewhere else, while these four came down here. That is the
            // same wood twice: the probe measured four logs reported and eight
            // trees gone. The site IS where that wood came from, which was the
            // story all along ("making camp fells the wood it stands on").
            ledger.treesFelled += LastClearingFelled;
            // Everybody goes back to cutting. With the fire lit there is
            // finally somewhere to cut INTO.
            //
            // The BUILDERS go idle, by the fire -- **unless there is another
            // drawing waiting**, which since the queue arrived is the usual
            // case: a crew that downed tools because the FIRST of three
            // buildings went up would be a queue nobody could use.
            if (!ledger.Building)
                foreach (var h in ledger.hands)
                    if (h != null && h.order == OutpostOrder.Build)
                    { h.order = OutpostOrder.Idle; h.target = ""; }
            // And they stand round it, which is the moment the camp stops
            // being a clearing and starts being somewhere people are.
            ArrangeHands();
            CampPiles.EnsureOn(this);
            // A building standing up is the moment Kevin asked to be kept:
            // *"build buildings, have them tweaked, and see the changes next
            // time I play."*
            Save.SaveGame.Autosave("a " + plan.label + " was raised");
            return true;
        }

        /// A ledger row's plan at the length the row recorded. Only a pier
        /// records one; every other row says 0 and gets the plan as priced.
        static BuildPlan PlanFor(string id, float length)
            => PlanNamed(id).WithLength(length);

        // --- the pier and the dock ------------------------------------------
        //
        // **HOOK for the dock registry (one-line wiring, coordinator).** A
        // pier is where the ship ties up, and the thing that knows how to be
        // tied up to is `Dock`, which is being given a runtime create/remove
        // API in another branch. This file does not call it: it raises the
        // pier, hands the `Building` (which carries a `Pier` component with
        // `SeaEnd`, `Heading` and `Berth`) to whoever is listening, and tells
        // the same listener when the pier is torn down -- through
        // `Pier.OnDestroy`, so every Destroy path (`Adopt`, a scene unload)
        // reports exactly once. Expected wiring:
        //
        //   Outpost.RegisterPierDock   = b => Dock.Create(b.GetComponent<Pier>().Berth,
        //                                    b.GetComponent<Pier>().Heading, b.transform);
        //   Outpost.UnregisterPierDock = b => Dock.Remove(...);
        //
        /// Called once per pier RAISED (never for a ghost or a blueprint).
        public static System.Func<Building, Dock> RegisterPierDock;
        /// Called once per registered pier when its GameObject is destroyed.
        public static System.Action<Building> UnregisterPierDock;

        /// Look a plan up by the id a ledger row carries. A save restores ids,
        /// not structs. Lives on `BuildPlans` now, because the ledger has to
        /// ask the same question when it works out what an assigned hand
        /// makes.
        static BuildPlan PlanNamed(string id)
        {
            var p = BuildPlans.Named(id);
            return string.IsNullOrEmpty(p.id) ? BuildPlans.Campfire : p;
        }

        /// Give up on what is sited here. The wood already in it is gone --
        /// it was cut and carried, and there is nowhere to put it back.
        public bool CancelPending() => CancelPending(ledger != null ? ledger.Pending : null);

        /// **Give up on ONE drawing**, named. With a queue "the pending one"
        /// is not an answer: cancelling the storehouse must not touch the
        /// shelter in front of it. Everything else -- the refund, the idling,
        /// the autosave -- is what it always was.
        public bool CancelPending(PendingBuild p)
        {
            if (ledger == null || p == null) return false;
            ledger.MigratePending();
            // What this plot had cleared goes back to the camp's books once
            // the row is gone (see `ReturnToCamp`).
            SnapshotDown(p, snapTrees, snapRocks);
            if (!ledger.sites.Remove(p)) return false;
            ReturnToCamp(snapTrees, snapRocks);
            SyncFelling();
            GatherSync.Sync(this);
            var plan = PlanFor(p.planId, p.length);
            // **What was carried here comes back on to the pile.** Kevin,
            // 2026-09-21: giving a build up is a decision, not a punishment.
            // Whole units only -- half a log in somebody's arms is not a
            // thing the pile can hold, and the same rounding the drawing
            // draws by (`done`, not `done + donePart`) is the one the player
            // has been watching all along.
            ledger.Add(Res.Timber, p.done);
            if (p.stoneDone > 0) ledger.Add(Res.Stone, p.stoneDone);
            if (p.brickDone > 0) ledger.Add(Res.Brick, p.brickDone);
            // Whoever was building goes idle by the fire -- **only when the
            // queue is empty**. With another drawing still standing the crew
            // has somewhere to go, and downing tools would punish the player
            // for cancelling the second of two.
            if (!ledger.Building)
                foreach (var h in ledger.hands)
                    if (h != null && h.order == OutpostOrder.Build)
                    { h.order = OutpostOrder.Idle; h.target = ""; }
            RetireBlueprint(p);
            Save.SaveGame.Autosave("the " + plan.label + " was given up");
            return true;
        }

        /// Light the fire, here and now, with no blueprint and no wood.
        ///
        /// **This is the DEV path, not the player's.** Since the blueprint
        /// pass the player sites a camp (`Site`) and the crew build it; this
        /// still exists because a probe that has to sail, land, site, wait out
        /// a build and then measure something else is a probe that measures
        /// the build every time it runs. It goes through exactly the same two
        /// steps the slow way does, so it cannot drift from it: site it at the
        /// surveyed clearing, then pay for it out of nothing.
        ///
        /// Returns the logs that came out of the clearing, or -1 with a reason.
        public int MakeCamp() => MakeCamp(out _);

        /// As above, and says WHY when it refuses.
        ///
        /// The first version folded "no ground here", "already a camp" and
        /// "nowhere inside the clearing will take it" into a single -1, which
        /// is a number you cannot debug from. Three refusals that mean
        /// different things must not share a return value.
        public int MakeCamp(out string why)
        {
            if (!Sited) { why = "the ground here was never surveyed"; return -1; }
            if (HasCamp) { why = "there is already a camp here"; return -1; }

            // Where the spiral would have put it. `Site` wants a point, and
            // the surveyed clearing is the answer to "somewhere sensible" --
            // which is the question the player is answering by hand now.
            Vector3 at = ClearingCentre;
            if (height != null) at.y = height(at.x, at.z);

            // A fire the player already sited is the one paid for; otherwise
            // site one at the clearing.
            PendingBuild row = CampfireRow();
            if (row == null)
            {
                if (Site(BuildPlans.Campfire, at, out why) < 0) return -1;
                row = CampfireRow();
            }

            if (row != null)
            {
                // **Every phase a row goes through, not just the first.**
                // Until 2026-09-23 this paid the MATERIALS only -- which was
                // the whole of "finished" before the build became stock-then-
                // build (7820c7e: `built >= LabourNeeded`) and plots had to be
                // cleared first (ac51ae1: `Cleared`). Paid but never built or
                // cleared, `Complete` stayed false, `FinishReady` found
                // nothing to raise, and this said "the fire would not stand
                // there" about a fire that was never asked to. The fire fells
                // its own ring at the raise (`RaiseRow`), so the plot's trees
                // come down there, the same as the slow way.
                row.done = row.needed;
                row.donePart = 0f;
                row.stoneDone = row.stoneNeeded;
                row.stoneDonePart = 0f;
                row.brickDone = row.brickNeeded;
                row.brickDonePart = 0f;
                row.clearDone = row.ClearTotal;
                row.built = row.LabourNeeded;
                FinishReady();
            }
            if (!HasCamp)
            {
                why = row == null ? "the fire was sited but its row is gone"
                    : !row.Complete ? "the fire is paid for but not complete"
                    : "the fire would not stand there";
                return -1;
            }

            if (ledger != null) ledger.lastTicked = TimeOfDay.Seconds;
            why = "";
            return LastClearingFelled;
        }

        /// The campfire's row in the queue, or null.
        PendingBuild CampfireRow()
        {
            if (ledger == null) return null;
            ledger.MigratePending();
            foreach (var r in ledger.sites)
                if (r != null && r.planId == BuildPlans.Campfire.id) return r;
            return null;
        }

        // --- the crew who stay ------------------------------------------------

        /// Leave this hand here.
        ///
        /// **The body is PARKED, not destroyed.** The ledger is what makes the
        /// hand real — it produces whether or not anything is drawn — but
        /// rebuilding a crewman from nothing on the way back would throw away
        /// an authored, named, tinted character to save a deactivated
        /// GameObject. So the body is unparented from the ship, stood at the
        /// camp, and switched off: no Update, no renderer, no physics, and
        /// nothing under it to fall through when the terrain streams out.
        ///
        /// The ship must be told to recount afterwards — see `CrewRoster`.
        public bool Station(Crew.CrewAgent hand)
        {
            // A blueprint is enough to be left behind for. That IS the flow:
            // you site a camp, you leave hands, and what they do first is
            // build the thing you sited. Requiring a finished fire here would
            // have made the feature impossible to reach.
            if (hand == null || !(HasCamp || Building)) return false;
            string who = hand.DisplayName;
            if (HandNamed(who) != null) return false;

            // **Where they step ashore** (2026-09-23), read BEFORE anything
            // moves them. Kevin: *"the crew you drop off will have to walk
            // from where you've docked."*
            Vector3 landing = LandingFor(hand);

            // They may be mid-errand ashore with a tree claimed. Drop it
            // first, or the node stays claimed by a body nobody can see and
            // no other hand will ever work it.
            hand.ReturnAboard();

            ledger?.hands.Add(new OutpostHand
            {
                name = who,
                // Whatever the camp is doing. A hand left at a half-built camp
                // who defaulted to cutting would stand there filling a pile
                // that does not exist yet.
                order = Building ? OutpostOrder.Build : OutpostOrder.Gather,
                target = Building ? "" : Res.Timber,
            });

            // **Rations from the ship** (Kevin, 2026-09-23): every hand left
            // ashore brings `ProvisionDays` of food, so the first buildings
            // go up before the camp has to feed itself. Known hole: dropping
            // and recalling the same man repeatedly books it again.
            ledger?.Add(Res.Food, Mathf.RoundToInt(OutpostLedger.ProvisionDays
                * OutpostLedger.EatPerHandPerDay));

            hand.transform.SetParent(transform, true);
            // Off only when nobody is here to see them. Leaving somebody
            // ashore in front of you and watching them wink out is the bug
            // this line is the whole of.
            hand.gameObject.SetActive(Watched);
            if (Watched)
            {
                // **They walk up from the landing** (2026-09-23) instead of
                // appearing in the fire ring. Stood on the beach first, then
                // given a worker (whose `home` seeds from where the body is),
                // THEN arranged -- a live worker is TOLD its ring spot by
                // `ArrangeHands` and walks there on `CampPath`, where a body
                // without one would be put there outright. A Build order
                // sends them to the blueprint the same way.
                hand.transform.position = landing;
                // No work in the books until the body gets there
                // (`OutpostHand.walkingIn`).
                var fresh = HandNamed(who);
                if (fresh != null) fresh.walkingIn = true;
                PuppetsToWork();
                ArrangeHands();
            }
            else ArrangeHands();
            return true;
        }

        /// **Where a hand leaving the ship first stands on this island.**
        /// Already ashore: where they are. Aboard: the foot of the gangway if
        /// it is run out (the point `AnchorController.SendAshore` lands a
        /// shore party on), else the island's shore point toward the ship.
        /// Spread a pace per hand already living here so a boatload does not
        /// arrive as one body. On the ground, never under it.
        Vector3 LandingFor(Crew.CrewAgent hand)
        {
            Vector3 p = hand.transform.position;
            bool onLand = hand.IsAshore && height != null && height(p.x, p.z) > 0.1f;
            if (!onLand)
            {
                var ship = hand.transform.parent != null ? hand.transform.root : null;
                var plank = ship != null ? ship.GetComponentInChildren<SeaSick.Ship.Gangway>() : null;
                if (plank != null && plank.Ready) p = plank.LandingPoint;
                else if (Island != null) p = Island.ShorePoint(0, 1, ship != null ? ship.position : p);
                else p = CampCentre;

                int n = ledger != null ? ledger.hands.Count : 0;
                float a = n * 2.39996323f;                  // golden angle
                p += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.9f + 0.35f * (n % 4));
            }
            if (height != null) p.y = Mathf.Max(height(p.x, p.z), 0f);
            return p;
        }

        /// Take this hand back aboard. The ledger stops counting them here.
        public bool Recall(Crew.CrewAgent hand, Transform ship)
        {
            if (hand == null || ship == null) return false;
            var row = HandNamed(hand.DisplayName);
            if (row == null) return false;
            // Puts down any armful first -- a haul never leaves with the hand.
            ledger.RemoveHand(row);
            hand.transform.SetParent(ship, true);
            hand.gameObject.SetActive(true);
            // The body was MOVED, not walked -- the state machine has to agree
            // with where the transform is rather than try to path to it.
            hand.PutBackOnStation();
            hand.Rest();
            return true;
        }

        public OutpostHand HandNamed(string who)
        {
            if (ledger == null || string.IsNullOrEmpty(who)) return null;
            foreach (var h in ledger.hands) if (h != null && h.name == who) return h;
            return null;
        }

        /// The parked bodies belonging to this outpost, active or not.
        public Crew.CrewAgent[] Parked() => GetComponentsInChildren<Crew.CrewAgent>(true);

        /// The body wearing this name here, or null.
        public Crew.CrewAgent BodyNamed(string who)
        {
            if (string.IsNullOrEmpty(who)) return null;
            foreach (var a in Parked()) if (a != null && a.DisplayName == who) return a;
            return null;
        }

        /// **Give a row born here a body.** Kevin: *"let's have the huts fill
        /// themselves."*
        ///
        /// The ledger recruits (see `OutpostHand.born`); this is the other
        /// half -- the same authored figure the ship's hands wear, cloned the
        /// way `Shipyard.ManCrew` clones, parked in the fire ring like any
        /// stationed hand, and switched off unless somebody is here to see
        /// him. He is NOT on the ship's books: no berth, no roster row, and
        /// nothing aboard knows he exists until he is carried there.
        public Crew.CrewAgent SpawnVillager(string who)
        {
            if (string.IsNullOrEmpty(who)) return null;
            var already = BodyNamed(who);
            if (already != null) return already;

            var a = Crew.BornVillager.Make(who, transform);
            if (a == null)
            {
                Debug.LogWarning("Outpost.SpawnVillager: nobody in the scene to copy for " + who);
                return null;
            }
            a.gameObject.SetActive(Watched);
            ArrangeHands();
            if (Watched) PuppetsToWork();
            return a;
        }

        /// **Every born row has a body.** Free to call as often as you like:
        /// it reads the rows, and does nothing at all unless one of them is
        /// born and bodiless -- which is the ordinary case on every tick.
        ///
        /// Called from `CatchUp` (so a camp that recruited while the player
        /// was away has people in it the moment she comes back) and from
        /// `ShowHands`, which is the moment they are looked at.
        public void EnsureBornBodies()
        {
            if (ledger == null || ledger.hands == null) return;
            Crew.CrewAgent[] bodies = null;
            foreach (var h in ledger.hands)
            {
                if (h == null || string.IsNullOrEmpty(h.name)) continue;
                if (!h.born) continue;
                if (bodies == null) bodies = Parked();
                bool have = false;
                foreach (var a in bodies)
                    if (a != null && a.DisplayName == h.name) { have = true; break; }
                if (have) continue;
                if (SpawnVillager(h.name) != null) bodies = Parked();
            }
        }

        /// **Take a villager born here away with the ship.**
        ///
        /// `Recall` is for a hand the ship already owns: his berth was never
        /// given up, so taking him back costs nothing. A villager born at the
        /// camp is a new mouth on a fixed number of hammocks, so this is the
        /// one crossing that can be refused -- and it is refused with the
        /// number, because "she carries five" is the sentence that tells the
        /// player to go and buy quarters.
        ///
        /// On success the row goes (he left the island) and the body becomes
        /// ship's crew: reparented under the hull, told which deck is his, and
        /// counted by `CrewRoster` like anybody else.
        static Crew.CrewRoster RosterOn(Transform ship) =>
            ship == null ? null
                : (ship.GetComponentInParent<Crew.CrewRoster>()
                   ?? ship.GetComponentInChildren<Crew.CrewRoster>(true));

        /// **Is there a hammock free?** Null when there is; otherwise the
        /// sentence the Hand's prompt shows before the player even lets go,
        /// so a refused carry is something you can see coming.
        ///
        /// Counts the bodies that are actually switched on: `ManCrew` leaves
        /// the hands a bigger rung once had lying inactive under the hull,
        /// and a count that included them would tell a half-crewed ship she
        /// was full.
        public static string BerthRefusal(Transform ship)
        {
            if (ship == null) return "no ship alongside";
            var roster = RosterOn(ship);
            var yard = ship.GetComponentInParent<SeaSick.Ship.Shipyard>()
                       ?? ship.GetComponentInChildren<SeaSick.Ship.Shipyard>(true);
            int aboard = 0;
            if (roster != null)
                foreach (var c in roster.All)
                    if (c != null && c.gameObject.activeSelf) aboard++;
            int berths = yard != null ? Mathf.Max(1, yard.Berths) : int.MaxValue;
            return aboard >= berths ? "no berth aboard — she carries " + aboard : null;
        }

        public bool CarryAboard(Crew.CrewAgent hand, Transform ship, out string why)
        {
            why = "";
            if (hand == null || ship == null || ledger == null)
            { why = "nobody to take aboard"; return false; }
            var row = HandNamed(hand.DisplayName);
            if (row == null) { why = "he is not one of this camp's"; return false; }

            why = BerthRefusal(ship);
            if (!string.IsNullOrEmpty(why)) return false;
            why = "";

            var roster = RosterOn(ship);
            // Read BEFORE he is reparented, or the post is computed with him
            // already counted among the hands it is meant to stand clear of.
            Vector3 station = Crew.BornVillager.FreeStation(ship);

            ledger.RemoveHand(row);        // armful put down first
            row.born = false;
            CampWorker.Remove(hand);
            hand.transform.SetParent(ship, true);
            hand.gameObject.SetActive(true);

            if (station == Vector3.zero) station = hand.transform.localPosition;
            hand.BoardShip(ship, station);
            hand.Rest();
            roster?.Refresh();
            ArrangeHands();
            return true;
        }

        /// Show the hands who live here, or put them away again.
        ///
        /// Called when the ship arrives and when she leaves: a camp you are
        /// standing in front of should have people in it, and a camp three
        /// kilometres astern should cost nothing at all.
        /// Is the ship here and looking at this camp?
        ///
        /// **`Station` switches a hand off, and only `ShowHands(true)` ever
        /// switches one on** — which fires when she ANCHORS. So a hand left
        /// while you were already standing there vanished and nothing brought
        /// them back until you had sailed away and returned. Kevin, playing
        /// it: *"i never saw the people on the island."*
        ///
        /// Being watched is now a state the outpost keeps, so anything that
        /// adds a body can ask whether to draw it.
        public bool Watched { get; private set; }

        /// The "while you were gone" record from the most recent arrival, or
        /// null if there is nothing to show (never left, or nothing
        /// happened worth a card). Set by `ShowHands(true)`.
        public OutpostLedger.Absence LastReturn { get; private set; }

        /// `Time.unscaledTime` when `LastReturn` was set, so the UI can time
        /// how long its card has been sitting there.
        public float ReturnedAt { get; private set; }

        /// The UI has shown (or the player dismissed) the card.
        public void DismissReturn() => LastReturn = null;

        public void ShowHands(bool visible)
        {
            bool arriving = visible && !Watched;
            bool leaving = !visible && Watched;
            Watched = visible;
            // Before the sweep below, or a villager recruited while she was
            // away is spawned switched-off and stays that way until the next
            // arrival.
            if (visible) EnsureBornBodies();
            foreach (var a in Parked())
            {
                if (a == null) continue;
                // Only the ones this outpost actually owns. A crewman walking
                // ashore from the ship is parented elsewhere and is not ours
                // to switch off.
                if (HandNamed(a.DisplayName) == null) continue;
                if (a.gameObject.activeSelf != visible) a.gameObject.SetActive(visible);
            }
            // Arriving is the one moment their positions are looked at, and
            // orders may have changed while nobody could see them.
            if (visible) { ArrangeHands(); PuppetsToWork(); }
            if (arriving)
            {
                // `CatchUp()` has already run (every caller ticks before
                // showing), so the ledger is current and this closes off
                // exactly the time she was gone.
                var rec = ledger?.EndAbsence();
                if (rec != null && rec.Anything)
                {
                    LastReturn = rec;
                    ReturnedAt = Time.unscaledTime;
                }
            }
            if (!visible)
            {
                foreach (var a in Parked()) CampWorker.Remove(a);
                // **Settle the felling before we stop looking.** While the
                // camp is watched the mesh is allowed to lag the ledger by a
                // few trees and a few seconds, because a tree comes down when
                // a man swings at it rather than when the arithmetic says so.
                // The moment nobody is here that licence ends: with the
                // workers gone this drops everything still owed, nearest-first,
                // so she never sails leaving a debt the next visit would pay
                // as trees vanishing out of a wood nobody is standing in.
                //
                // It has to be HERE rather than in `StowCampHands`, which
                // catches up first and lowers the flag second -- at that point
                // the camp is still watched and the lag is still legal.
                SyncFelling();
                SyncHarvest();
                // Open the record of what happens while she's gone. `CatchUp`
                // has already run (every caller ticks before hiding), so
                // nothing before this moment leaks into the absence.
                if (leaving) ledger?.BeginAbsence(TimeOfDay.Seconds);
                if (leaving) Combat.RaidDirector.Forget(this);
            }
        }

        /// Put a walking, carrying body on every hand who is drawn.
        ///
        /// **Animation only.** See `CampWorker`: it produces nothing, because
        /// a camp that paid differently while somebody watched it would undo
        /// the whole reason the ledger exists.
        public void PuppetsToWork()
        {
            if (!Watched) return;
            foreach (var a in Parked())
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                if (HandNamed(a.DisplayName) == null) continue;
                CampWorker.Attach(this, a);
            }
        }

        /// **Stand everybody where they belong.**
        ///
        /// Kevin, 2026-09-19: *"after sending the crew to the island and
        /// they've built the campfire they should stand around the campfire."*
        /// They did not: `Station` dropped each hand at a random point inside
        /// 2.2 m of wherever the camp centre was AT THE TIME, which for a hand
        /// left before the fire was built was the blueprint, and which never
        /// moved afterwards.
        ///
        /// So the ring is computed, not scattered, and it is recomputed
        /// whenever the camp changes: evenly spaced round the fire, facing in.
        /// A hand ASSIGNED to a building stands at that building instead —
        /// which is the whole visible difference between a camp of four idlers
        /// and a camp with a sawyer in it.
        ///
        /// **A body on its feet is TOLD where it belongs; a body that is not
        /// is PUT there.** Every order method calls this, so the teleport it
        /// used to be snapped the whole camp back to the ring each time
        /// anybody was given a job — four people jumping because one of them
        /// was reassigned. A hand with a live `CampWorker` gets `SetHome` and
        /// walks; a hand that is switched off, has no worker, or is being seen
        /// for the first time on arrival is still placed outright, because
        /// there is nothing to watch the walk and a camp must be standing in
        /// its ring the frame the player looks at it.
        public void ArrangeHands()
        {
            if (ledger == null) return;
            var bodies = Parked();

            // Count the ones who belong to the fire, so the ring is spaced by
            // how many are actually standing in it rather than by how many
            // live here.
            int atFire = 0;
            foreach (var h in ledger.hands)
                if (h != null && WorkplaceOf(h) == null) atFire++;
            atFire = Mathf.Max(1, atFire);

            int i = 0;
            foreach (var a in bodies)
            {
                if (a == null) continue;
                var row = HandNamed(a.DisplayName);
                if (row == null) continue;

                Vector3 spot;
                Vector3 lookAt;
                var post = WorkplaceOf(row);
                if (post != null)
                {
                    // Just outside the building's own footprint, on the side
                    // facing the fire, so a worker reads as belonging to the
                    // shed without standing inside its walls. The maths lives
                    // in `CampWorker.WorkSpot` so that the spot a hand is PUT
                    // and the spot a hand WALKS to cannot drift apart.
                    spot = CampWorker.WorkSpot(this, post);
                    lookAt = post.transform.position;
                }
                else
                {
                    float a2 = (i / (float)atFire) * Mathf.PI * 2f;
                    spot = CampCentre + new Vector3(
                        Mathf.Cos(a2) * FireRingRadius, 0f, Mathf.Sin(a2) * FireRingRadius);
                    lookAt = CampCentre;
                    i++;
                }

                if (height != null) spot.y = height(spot.x, spot.z);

                var worker = a.gameObject.activeInHierarchy ? CampWorker.Of(a) : null;
                if (worker != null)
                {
                    // On their feet and being watched: this is a change of
                    // where they belong, not a change of where they are.
                    worker.SetHome(spot, lookAt);
                    continue;
                }

                a.transform.position = spot;

                Vector3 face = lookAt - spot;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f)
                    a.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }
        }

        /// **Take the wood down to match what has been cut** -- and, while
        /// somebody is standing here watching, take down the tree a man is
        /// actually swinging at.
        ///
        /// One tree per log, nearest the camp outward. The stock is 40 logs a
        /// hectare against the ~230 trees a hectare the scenery draws, so even
        /// a worked-out camp only thins its wood -- which is the picture the
        /// plan asked for: *a camp running twenty days sits in a widening ring
        /// of stumps.*
        ///
        /// Driven by `ledger.treesFelled` against `ledger.timberTaken`, so it
        /// is a pure function of the ledger and works on any visit however the
        /// terrain streamed in between. Cheap: it does nothing at all unless
        /// somebody has cut something since the last call.
        ///
        /// ## What changed 2026-09-20, and what did NOT
        ///
        /// Kevin, playing it: *"when collecting wood they seem to cut at
        /// random areas while other, random trees disappear, not wanted
        /// behavior."* Both halves were true. A tree came down the instant the
        /// ledger's count rose, nearest-first, while each `CampWorker` walked
        /// independently to whatever trunk happened to be nearest him -- so the
        /// tree a man was chopping and the tree that vanished were never the
        /// same tree.
        ///
        /// The fix is a WAIT, not a new chooser. **Which trees come down, and
        /// how many, is still decided entirely by the ledger** -- the order is
        /// still `fellOrder`, still measured from `CampCentre`, still walked
        /// front-first -- because that is what lets a camp worked for twenty
        /// days while you were three islands away be found with the right ring
        /// of stumps whatever the terrain streamer did in between (D2). What
        /// is new is WHEN the front tree drops while the camp is being watched:
        ///
        /// - **(a)** the man who claimed it is standing at it swinging, or
        /// - **(b)** it has been owed longer than `Feel.fellGraceSeconds` --
        ///   he is still walking to it, and after ten seconds a tree that will
        ///   not fall reads worse than one that falls unattended, or
        /// - **(c)** the ledger has run more than `cutters + fellBacklogSlack`
        ///   trees ahead of the mesh, which means somebody scrubbed the clock
        ///   or she has just arrived: take the whole debt at once.
        ///
        /// Unwatched, or with nobody cutting, this is exactly what it always
        /// was: drop everything owed, nearest-first, in one call.
        ///
        /// **The loop BREAKS rather than skipping**, and that is the load-
        /// bearing line. If a man on the second tree could drop his while the
        /// first still stood, the felled set would have a hole in it, and a
        /// hole is not reproducible from an integer -- come back after the
        /// island streamed out and the mesh would fell the prefix instead and
        /// show you a different wood. So the front of the order is the only
        /// tree that can ever fall next, and the claim table hands the front of
        /// the order to somebody.
        ///
        /// ## What changed 2026-09-22
        ///
        /// Kevin: *"the trees seem to appear anew after loading in."* They
        /// did. This used to open with `if (want <= treesFelled) return;` --
        /// an early-out that reads the ledger's two counts as a DEBT, and a
        /// settled debt as nothing to do. That is true of the wood it fells
        /// and false of the wood it has already felled: a mesh arrives from
        /// the terrain streamer (or from `Adopt`) with every tree standing and
        /// the debt already settled, so the one thing that could have thinned
        /// it again never ran and a worked-out camp stood in fresh forest.
        ///
        /// So the shape is now: **draw the wood the ledger describes, then
        /// pay down whatever is still owed.** `DrawWood` is the pure function
        /// the comment above always claimed this was -- give it the ledger and
        /// the geometry and it puts the same trees down on any visit, however
        /// the terrain streamed in -- and `FellOwed` below is unchanged, still
        /// the only thing that moves `treesFelled`, still bound by the grace
        /// and the claim table while somebody is watching.
        public void SyncFelling()
        {
            if (ledger == null) return;

            // The plots first: they decide which trees the camp's order may
            // hold at all.
            SyncClearing();
            var wood = WoodHere();
            if (wood == null || wood.TreeCount == 0) return;
            BuildFellOrder(wood);
            PinRegrowth();
            DrawWood(wood);

            int want = Mathf.FloorToInt(ledger.timberTaken);
            if (want <= ledger.treesFelled) { owedSince = -1f; return; }

            PruneClaims();

            int cutters = claimHands.Count;
            int backlog = want - ledger.treesFelled;

            // Nobody here to see it, nobody cutting, or the arithmetic has run
            // so far ahead that waiting would read as a bug rather than as a
            // man walking: settle the whole debt now.
            if (!Watched || cutters == 0 || backlog > cutters + Feel.fellBacklogSlack)
            {
                FellOwed(wood, want, true);
                owedSince = -1f;
                return;
            }

            if (owedSince < 0f) owedSince = Time.unscaledTime;
            FellOwed(wood, want, false);
            if (ledger.treesFelled >= want) owedSince = -1f;
        }

        /// The one loop that takes trees down. `atOnce` is the old behaviour:
        /// everything owed, front to back, no questions. Otherwise the front
        /// tree has to be earned -- see `SyncFelling`.
        void FellOwed(Terrain.SceneryWood wood, int want, bool atOnce)
        {
            bool overdue = !atOnce && Time.unscaledTime - owedSince > Feel.fellGraceSeconds;

            while (ledger.treesFelled < want && fellCursor < fellOrder.Length)
            {
                int i = fellOrder[fellCursor];
                if (wood.TreeAt(i).felled) { fellCursor++; continue; }

                if (atOnce) FlushedTrees++;
                else
                {
                    bool atIt = SomebodyChopping(i);
                    if (!atIt && !overdue) break;
                    if (!atIt)
                    {
                        // The grace ran out. One tree per expiry, then the
                        // clock starts again -- a stretch where nobody is
                        // cutting should dribble, not empty the wood.
                        FlushedTrees++;
                        overdue = false;
                    }
                }

                wood.FellForLedger(i);
                ledger.treesFelled++;
                fellCursor++;
                owedSince = Time.unscaledTime;
            }

            // The island ran out of trees before the ledger ran out of logs.
            // Stop asking: the stock is the authority on how much wood there
            // was, and the mesh is only the picture of it.
            if (fellCursor >= fellOrder.Length) ledger.treesFelled = want;

            // The mesh and the books agree again, so record where `DrawWood`
            // would have to start from. Felling front-first and skipping what
            // is already down keeps the felled set a PREFIX of the order,
            // which is the invariant the whole file rests on.
            drawnWood = wood;
            drawnDown = DownWanted(wood);
        }

        /// **The trees that are down right now, as a count off the front of
        /// the order** -- the one number the picture is made of.
        ///
        /// Kevin, 2026-09-22: the wood regrows away from camp. Because the
        /// order is nearest-the-camp-first and the felled set is always a
        /// prefix of it, "the far ones come back first" is not a second
        /// ordering at all: it is the same prefix, SHORTER. Same trick
        /// `GatherSync` plays with the boulders, and the same payoff -- the
        /// whole visible state still reproduces from integers plus geometry.
        ///
        /// Two floors under it:
        /// - the camp's own clearing (`Feel.campClearing`) never regrows while
        ///   there is a fire here, which is the ask -- *"to help the camp not
        ///   get overgrown"* -- and
        /// - nothing beyond what was ever cut, obviously.
        int DownWanted(Terrain.SceneryWood wood)
        {
            int cut = Mathf.Clamp(ledger.treesFelled, 0, wood.TreeCount);
            int keptClear = Mathf.Min(HasCamp ? clearingTrees : 0, cut);
            int down = ledger.treesFelled - Mathf.FloorToInt(ledger.treesRegrown);
            return Mathf.Clamp(down, keptClear, cut);
        }

        /// The ledger accrues regrowth with no idea where the camp is
        /// (`OutpostLedger.Step`), because an island can be worked unloaded.
        /// The moment the ground is here, the clearing gets its say: regrowth
        /// that would eat into it is not banked for later, it never happened.
        void PinRegrowth()
        {
            if (!HasCamp) return;
            float cap = Mathf.Max(0, ledger.treesFelled - clearingTrees);
            if (ledger.treesRegrown > cap) ledger.treesRegrown = cap;
        }

        /// **Make the wood show what the ledger says.** Fell the prefix, stand
        /// up everything behind it, and do neither unless something moved.
        ///
        /// Cheap enough for the every-frame path it sits on: `DownWanted` is
        /// arithmetic, and the pass over the order only runs when the count
        /// changed or the mesh is one this outpost has not drawn on yet -- a
        /// fresh mesh from the streamer, or the order rebuilt because the camp
        /// moved. Nothing here counts as CUTTING: `FlushedTrees` is untouched
        /// and no log is paid, because this wood came down in a session the
        /// ledger already booked.
        void DrawWood(Terrain.SceneryWood wood)
        {
            if (fellOrder == null) return;
            int down = DownWanted(wood);
            bool fresh = !ReferenceEquals(drawnWood, wood);
            if (!fresh && down == drawnDown) return;

            // Beyond the high-water mark nothing can be felled, so there is
            // nothing to stand up there either. A mesh nobody has drawn on
            // gets the full pass once.
            int high = fresh ? fellOrder.Length : Mathf.Max(down, drawnDown);
            for (int k = 0; k < high && k < fellOrder.Length; k++)
            {
                int i = fellOrder[k];
                if (k < down) wood.FellForLedger(i);
                else wood.Restand(i);
            }

            drawnWood = wood;
            drawnDown = down;
            // A tree that just stood back up is the next one a man walks to,
            // and the cursor may be parked past it.
            fellCursor = 0;
        }

        /// **The order trees come down in: nearest the camp, outward.**
        ///
        /// Rebuilt when the wood changes under it (the island streamed out and
        /// back) and when the camp MOVES -- which it can, because only the
        /// fire says where the camp is and the fire is raised where the player
        /// sited it. An order measured from a stale centre would fell a ring
        /// round somewhere nobody lives.
        void BuildFellOrder(Terrain.SceneryWood wood)
        {
            // **Plots own their trees, 2026-09-23.** Every tree inside a
            // raised or counted footprint is the plot's, not the camp's, and
            // is left out of the order entirely -- see "the CLEAR phase"
            // below. The order rebuilds whenever that set changes.
            EnsureClearing(false);
            Vector3 c = CampCentre;
            if (fellOrder != null && fellOrderVersion == clearVersion
                && ReferenceEquals(fellOrderWood, wood)
                && (fellOrderFrom - c).sqrMagnitude < 0.25f) return;

            int count = 0;
            for (int i = 0; i < wood.TreeCount; i++)
                if (i >= siteTree.Length || !siteTree[i]) count++;
            var idx = new int[count];
            var d2 = new float[count];
            for (int i = 0, n = 0; i < wood.TreeCount; i++)
            {
                if (i < siteTree.Length && siteTree[i]) continue;
                idx[n] = i;
                Vector3 p = wood.TreeAt(i).baseAt - c;
                p.y = 0f;
                d2[n] = p.sqrMagnitude;
                n++;
            }
            System.Array.Sort(d2, idx);
            fellOrder = idx;
            fellOrderVersion = clearVersion;
            if (posInOrder == null || posInOrder.Length != wood.TreeCount)
                posInOrder = new int[wood.TreeCount];
            for (int i = 0; i < posInOrder.Length; i++) posInOrder[i] = -1;
            for (int k = 0; k < idx.Length; k++) posInOrder[idx[k]] = k;
            // From the front: trees already down are skipped, so starting over
            // costs one pass and cannot double-fell anything.
            fellCursor = 0;
            fellOrderFrom = c;
            fellOrderWood = wood;

            // **How much of the order is the camp's own clearing.** The order
            // is nearest-first, so the trees inside `Feel.campClearing` ARE
            // its first entries and one count says which. Kevin, 2026-09-22:
            // these never come back while the fire is lit.
            float clear2 = Feel.campClearing * Feel.campClearing;
            clearingTrees = 0;
            while (clearingTrees < d2.Length && d2[clearingTrees] <= clear2) clearingTrees++;

            // A new order means a different prefix, so whatever was drawn from
            // the old one has to be drawn again -- including standing up trees
            // that are no longer near enough to the fire to be down.
            drawnWood = null;
            drawnDown = 0;
        }

        int[] fellOrder;
        /// Tree index -> its place in `fellOrder`, -1 for a plot's tree.
        int[] posInOrder;
        int fellOrderVersion = -1;
        int fellCursor;
        Vector3 fellOrderFrom;
        Terrain.SceneryWood fellOrderWood;

        /// Entries at the front of `fellOrder` that stand inside the camp's
        /// clearing. See `DownWanted`.
        int clearingTrees;

        /// The wood `DrawWood` last put a picture on, and how much of the
        /// order it put down. Not saved: both are re-derived from the ledger
        /// the first time a mesh is seen, which is the entire point.
        Terrain.SceneryWood drawnWood;
        int drawnDown;
        Terrain.SceneryWood woodCache;
        float owedSince = -1f;

        /// Trees this outpost took down with nobody swinging at them: the
        /// arrival flush, the clock being scrubbed, and the grace running out.
        /// **Counted for the probes**, which gate that a camp working at the
        /// pace of its own day never needs one.
        public int FlushedTrees { get; private set; }

        /// Trees the ledger has paid for and the mesh has not yet shown. Zero
        /// is the only acceptable answer the moment she stops looking.
        public int TreesOwed => ledger == null
            ? 0 : Mathf.Max(0, Mathf.FloorToInt(ledger.timberTaken) - ledger.treesFelled);

        /// The welded wood on this island, cached. `GetComponentInChildren` was
        /// being run on a path that is now touched every frame a camp is
        /// watched, and the wood does not move.
        Terrain.SceneryWood WoodHere()
        {
            if (woodCache == null) woodCache = GetComponentInChildren<Terrain.SceneryWood>();
            return woodCache;
        }

        /// The herd on this island, cached like the wood. Null on an island
        /// with no animals at all -- `FaunaField` destroys a root it put
        /// nothing under -- and that null is the whole reason hunting is
        /// never offered there.
        public FaunaLod FaunaHere()
        {
            if (faunaCache == null) faunaCache = GetComponentInChildren<FaunaLod>();
            return faunaCache;
        }
        FaunaLod faunaCache;

        // --- the CLEAR phase: what stands on a plot, 2026-09-23 ---------------
        //
        // Kevin: *"any blueprint can be placed over any trees or small rocks
        // etc and the area will be cleared by the villagers before buildings
        // begin construction."*
        //
        // ## How it persists without breaking the wood's one invariant
        //
        // The camp's felled set is a PREFIX of `fellOrder` and nothing about
        // it is saved but counts. A plot's trees cannot be part of that
        // prefix -- they come down in the plot's order, not the camp's -- so
        // they are taken OUT of `fellOrder` altogether:
        //
        // - The **clearing registry** below is every footprint that owns
        //   ground: every raised building (`ledger.raised`), every raised
        //   wall segment (`ledger.builtWalls`), and every queued row that was
        //   counted at siting (`PendingBuild.clearSited`). Each owns the trees
        //   and stone/ore rocks whose base is inside its footprint, first
        //   owner wins, in that canonical order (raised, walls, queue order).
        //   It is derived from saved rows plus geometry, so it is the same on
        //   every load.
        // - Owned trees are EXCLUDED from `fellOrder` (so `DrawWood` never
        //   restands them and `ClaimTree` never sends a cutter to them), and
        //   owned rocks are held out of `GatherSync`'s order
        //   (`ResourceNode.HeldBySite`).
        // - Within one footprint the trees and rocks are ordered NEAREST THE
        //   CAMP FIRST. For a raised footprint all of them are down; for a
        //   queued row the first `len - TreesLeft` trees and `len - RocksLeft`
        //   rocks are. Nearest-the-camp matters: the footprint trees the
        //   camp's own felling had already taken when the row was sited are
        //   exactly the camp-nearest ones, so "the first N are down" draws
        //   the same trees that were down, not a reshuffle.
        // - At siting, those camp-felled trees move from the camp's books to
        //   the plot's (`TakeFootprint`: `treesFelled` and `timberTaken` both
        //   drop by that many, so the owed count is unchanged and the camp's
        //   visible prefix is the same trees minus the plot's). Rocks the
        //   seam had already hidden move the same way (`standing` rises by a
        //   prop's worth each). Cancel/move hands them back (`ReturnToCamp`).
        //
        // Site trees are felled through `FellForLedger` rather than a
        // non-ledger path: excluded from the order, `DrawWood` never touches
        // them anyway, and if the row is later cancelled they rejoin the
        // camp's wood as the books' trees, which the camp can then account
        // for (a non-ledger stump could never come back).
        //
        // ## Known gaps
        // - Boulders baked into the scenery mesh (`SceneryGround`) have no
        //   index and no node; they are neither counted nor cleared. Siting
        //   has never refused them, and still does not.
        // - Piers are skipped entirely: half over water, and the land half is
        //   a strip of beach where no tree stands.
        // - Cancelling or moving a part-cleared row returns its stumps to the
        //   camp's wood as counts; the camp's prefix may then show a
        //   different tree down in their place. Logs already booked stay
        //   booked.
        // - An OLD save's raised buildings now own their footprints: those
        //   trees were already felled (by the raise's `FellWithin`) and still
        //   sit in `treesFelled`, so the camp's prefix may take a few more
        //   trees at its edge on the first load. One-off.

        /// Metres of margin round a building's footprint that must be clear.
        public const float ClearMargin = 1.0f;
        /// Half-width of the band along a wall's post line that must be clear.
        public const float WallClearBand = 1.5f;

        /// One footprint shape: a rotated rectangle, or a band round a line.
        struct ClearShape
        {
            public bool line;
            public Vector3 c;           // rect centre
            public Quaternion inv;      // world -> rect local
            public float hx, hz;        // half extents incl. margin
            public Vector3 a, b;        // line posts
            public float band;
            public float reach;         // bounding radius round `c`, cull

            public static ClearShape Rect(Vector3 at, float yaw, Vector2 footprint)
            {
                var s = new ClearShape
                {
                    c = new Vector3(at.x, 0f, at.z),
                    inv = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)),
                    hx = 0.5f * footprint.x + ClearMargin,
                    hz = 0.5f * footprint.y + ClearMargin,
                };
                s.reach = Mathf.Sqrt(s.hx * s.hx + s.hz * s.hz);
                return s;
            }

            public static ClearShape Line(Vector3 a, Vector3 b)
            {
                a.y = 0f; b.y = 0f;
                return new ClearShape
                {
                    line = true, a = a, b = b, band = WallClearBand,
                    c = 0.5f * (a + b),
                    reach = 0.5f * (b - a).magnitude + WallClearBand,
                };
            }

            public bool Contains(Vector3 p)
            {
                p.y = 0f;
                Vector3 d = p - c;
                if (d.sqrMagnitude > reach * reach) return false;
                if (line)
                {
                    Vector3 ab = b - a;
                    float l2 = ab.sqrMagnitude;
                    float t = l2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
                    return (p - (a + ab * t)).sqrMagnitude <= band * band;
                }
                Vector3 local = inv * d;
                return Mathf.Abs(local.x) <= hx && Mathf.Abs(local.z) <= hz;
            }
        }

        class ClearEntry
        {
            public PendingBuild row;    // null for a raised footprint
            public ClearShape shape;
            public int[] trees = System.Array.Empty<int>();
            public ResourceNode[] rocks = System.Array.Empty<ResourceNode>();
            public int drawnTrees = -1, drawnRocks = -1;
            public int TreesDown => row == null ? trees.Length
                : Mathf.Clamp(trees.Length - row.TreesLeft, 0, trees.Length);
            public int RocksDown => row == null ? rocks.Length
                : Mathf.Clamp(rocks.Length - row.RocksLeft, 0, rocks.Length);
        }

        readonly List<ClearEntry> clearEntries = new List<ClearEntry>();
        readonly Dictionary<PendingBuild, ClearEntry> clearByRow = new Dictionary<PendingBuild, ClearEntry>();
        /// Tree index -> owned by some footprint. Excluded from `fellOrder`.
        bool[] siteTree = System.Array.Empty<bool>();
        readonly HashSet<ResourceNode> siteRocks = new HashSet<ResourceNode>();
        int clearKey = int.MinValue, clearRockKey = int.MinValue;
        Terrain.SceneryWood clearWood;
        /// Bumped whenever the set of owned TREES may have changed; the fell
        /// order rebuilds on it.
        int clearVersion;
        static readonly List<ResourceNode> rockScratch = new List<ResourceNode>();

        int ClearTreeKey()
        {
            unchecked
            {
                int h = 17;
                if (ledger != null)
                {
                    if (ledger.sites != null)
                        foreach (var r in ledger.sites)
                            if (r != null && r.clearSited)
                                h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
                    h = h * 31 + (ledger.raised != null ? ledger.raised.Count : 0);
                    h = h * 31 + (ledger.builtWalls != null ? ledger.builtWalls.Count : 0);
                }
                Vector3 c = CampCentre;
                h = h * 31 + Mathf.RoundToInt(c.x * 2f);
                h = h * 31 + Mathf.RoundToInt(c.z * 2f);
                return h;
            }
        }

        /// **Bring the registry up to date.** Cheap when nothing moved (one
        /// hash over the queue). `rocksToo` also re-reads the island's rock
        /// props, which only the reconciling paths need.
        void EnsureClearing(bool rocksToo)
        {
            if (ledger == null) return;
            var wood = WoodHere();
            int key = ClearTreeKey();
            bool treesStale = key != clearKey || !ReferenceEquals(clearWood, wood)
                || (wood != null && siteTree.Length != wood.TreeCount);
            int rockKey = key * 31 + ResourceNode.All.Count;
            bool rocksStale = rocksToo && (treesStale || rockKey != clearRockKey);
            if (!treesStale && !rocksStale) return;

            if (treesStale)
            {
                clearEntries.Clear();
                clearByRow.Clear();
                int n = wood != null ? wood.TreeCount : 0;
                if (siteTree.Length != n) siteTree = new bool[n];
                else System.Array.Clear(siteTree, 0, n);

                if (ledger.raised != null)
                    foreach (var b in ledger.raised)
                    {
                        if (b == null) continue;
                        var plan = PlanFor(b.planId, b.length);
                        if (plan.kind == BuildKind.Pier) continue;
                        AddEntry(null, ClearShape.Rect(new Vector3(b.x, 0f, b.z), b.yaw, plan.footprint), wood);
                    }
                if (ledger.builtWalls != null)
                    foreach (var w in ledger.builtWalls)
                        if (w != null) AddEntry(null, ClearShape.Line(w.A, w.B), wood);
                if (ledger.sites != null)
                    foreach (var r in ledger.sites)
                    {
                        if (r == null || !r.clearSited) continue;
                        var shape = ShapeOf(r, out bool skip);
                        if (skip) continue;
                        var e = AddEntry(r, shape, wood);
                        clearByRow[r] = e;
                    }
                clearKey = key;
                clearWood = wood;
                clearVersion++;
                rocksStale = true;
            }

            if (rocksStale)
            {
                // Rocks: re-read the props (they stream with the island).
                foreach (var old in siteRocks) if (old != null) old.HeldBySite = false;
                siteRocks.Clear();
                GatherRocks(rockScratch);
                foreach (var e in clearEntries)
                {
                    e.rocks = OwnRocks(e.shape, rockScratch, true);
                    e.drawnRocks = -1;
                }
                clearRockKey = rockKey;
            }
        }

        ClearShape ShapeOf(PendingBuild r, out bool skip)
        {
            skip = false;
            if (r.isWall) return ClearShape.Line(r.postA, r.postB);
            var plan = PlanFor(r.planId, r.length);
            if (plan.kind == BuildKind.Pier) { skip = true; return default; }
            return ClearShape.Rect(r.At, r.yaw, plan.footprint);
        }

        ClearEntry AddEntry(PendingBuild row, ClearShape shape, Terrain.SceneryWood wood)
        {
            var e = new ClearEntry { row = row, shape = shape };
            e.trees = OwnTrees(shape, wood, true);
            clearEntries.Add(e);
            return e;
        }

        /// Trees inside `shape` not owned by an earlier footprint, nearest the
        /// camp first. `claim` marks them owned.
        int[] OwnTrees(ClearShape shape, Terrain.SceneryWood wood, bool claim)
        {
            if (wood == null || wood.TreeCount == 0) return System.Array.Empty<int>();
            Vector3 camp = CampCentre;
            var list = new List<int>();
            var d2 = new List<float>();
            for (int i = 0; i < wood.TreeCount; i++)
            {
                if (i < siteTree.Length && siteTree[i]) continue;
                Vector3 p = wood.TreeAt(i).baseAt;
                if (!shape.Contains(p)) continue;
                list.Add(i);
                Vector3 d = p - camp; d.y = 0f;
                d2.Add(d.sqrMagnitude + i * 1e-6f);   // stable tie-break
            }
            var idx = list.ToArray();
            System.Array.Sort(d2.ToArray(), idx);
            if (claim) foreach (int i in idx) if (i < siteTree.Length) siteTree[i] = true;
            return idx;
        }

        /// This island's stone and ore props, whatever their state.
        void GatherRocks(List<ResourceNode> into)
        {
            into.Clear();
            var isl = Island;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Home != isl) continue;
                if (n.Resource != Res.Stone && n.Resource != Res.Ore) continue;
                into.Add(n);
            }
        }

        /// Rocks inside `shape` not owned already: stone ones first, then ore
        /// (the ledger books `clearOre` last), each nearest the camp first.
        ResourceNode[] OwnRocks(ClearShape shape, List<ResourceNode> pool, bool claim)
        {
            Vector3 camp = CampCentre;
            var picked = new List<ResourceNode>();
            foreach (var n in pool)
                if (n != null && !siteRocks.Contains(n) && shape.Contains(n.transform.position))
                    picked.Add(n);
            picked.Sort((x, y) =>
            {
                int ox = x.Resource == Res.Ore ? 1 : 0, oy = y.Resource == Res.Ore ? 1 : 0;
                if (ox != oy) return ox.CompareTo(oy);
                Vector3 dx = x.transform.position - camp, dy = y.transform.position - camp;
                dx.y = 0f; dy.y = 0f;
                int c = dx.sqrMagnitude.CompareTo(dy.sqrMagnitude);
                if (c != 0) return c;
                return x.transform.position.x.CompareTo(y.transform.position.x);
            });
            if (claim)
                foreach (var n in picked) { siteRocks.Add(n); n.HeldBySite = true; }
            return picked.ToArray();
        }

        /// **Make every plot show what its row says.** Fell the first
        /// `TreesDown` of each footprint, stand the rest back up (only a tree
        /// the books felled can be, see `SceneryWood.Restand`), and hide the
        /// first `RocksDown` rocks. Only redraws an entry whose count moved,
        /// so it is cheap on the every-quarter-second `CatchUp` path.
        void SyncClearing()
        {
            if (ledger == null) return;
            EnsureClearing(true);
            var wood = WoodHere();
            foreach (var e in clearEntries)
            {
                int td = e.TreesDown;
                if (wood != null && td != e.drawnTrees)
                {
                    for (int k = 0; k < e.trees.Length; k++)
                    {
                        int i = e.trees[k];
                        if (i < 0 || i >= wood.TreeCount) continue;
                        if (k < td) { if (!wood.TreeAt(i).felled) wood.FellForLedger(i); }
                        else wood.Restand(i);
                    }
                    e.drawnTrees = td;
                }
                int rd = e.RocksDown;
                if (rd != e.drawnRocks)
                {
                    for (int k = 0; k < e.rocks.Length; k++)
                        if (e.rocks[k] != null) e.rocks[k].SetGathered(k < rd);
                    e.drawnRocks = rd;
                }
            }
        }

        /// **How many obstructions a site HERE would have to clear.** For the
        /// siting ghost ("clears 4 trees, 1 rock"). Counts what is STANDING
        /// and not already owned by another footprint. Cached on the last
        /// question, so a ghost held still costs nothing.
        public void CountObstructions(BuildPlan plan, Vector3 at, float yaw, out int trees, out int rocks)
        {
            trees = rocks = 0;
            if (ledger == null || plan.kind == BuildKind.Pier) return;
            CountShape(ClearShape.Rect(at, yaw, plan.footprint), out trees, out rocks, out _);
        }

        public void CountObstructionsWall(Vector3 a, Vector3 b, out int trees, out int rocks)
        {
            trees = rocks = 0;
            if (ledger == null) return;
            CountShape(ClearShape.Line(a, b), out trees, out rocks, out _);
        }

        ClearShape lastCountShape;
        int lastCountTrees = -1, lastCountRocks, lastCountOre, lastCountVersion, lastCountFrame = -1;

        void CountShape(ClearShape shape, out int trees, out int rocks, out int ore)
        {
            EnsureClearing(true);
            if (lastCountTrees >= 0 && lastCountVersion == clearVersion
                // Held still, a ghost re-asks every frame; a quarter of a
                // second of staleness is invisible and saves the per-frame
                // walk over every tree on the island.
                && Time.frameCount - lastCountFrame < 15
                && SameShape(shape, lastCountShape))
            {
                trees = lastCountTrees; rocks = lastCountRocks; ore = lastCountOre;
                return;
            }
            trees = rocks = ore = 0;
            var wood = WoodHere();
            foreach (int i in OwnTrees(shape, wood, false))
                if (!wood.TreeAt(i).felled) trees++;
            GatherRocks(rockScratch);
            foreach (var n in OwnRocks(shape, rockScratch, false))
                if (!n.Gathered) { rocks++; if (n.Resource == Res.Ore) ore++; }
            lastCountShape = shape; lastCountTrees = trees; lastCountRocks = rocks;
            lastCountOre = ore; lastCountVersion = clearVersion; lastCountFrame = Time.frameCount;
        }

        static bool SameShape(ClearShape x, ClearShape y)
            => x.line == y.line && (x.c - y.c).sqrMagnitude < 1e-4f
               && (x.a - y.a).sqrMagnitude < 1e-4f && (x.b - y.b).sqrMagnitude < 1e-4f
               && Quaternion.Dot(x.inv, y.inv) > 0.99999f
               && Mathf.Abs(x.hx - y.hx) < 1e-3f && Mathf.Abs(x.hz - y.hz) < 1e-3f;

        /// **Count a fresh row's plot and move its already-felled ground off
        /// the camp's books.** Called while `row` is NOT yet in the queue, so
        /// the registry and the fell order are the ones without it. Sets
        /// `clearTrees/clearRocks/clearOre`, zeroes `clearDone`, and marks the
        /// row `clearSited` so the registry claims its ground from now on.
        void TakeFootprint(PendingBuild row)
        {
            if (row == null || ledger == null) return;
            var shape = ShapeOf(row, out bool skip);
            if (skip) return;
            var wood = WoodHere();
            if (wood != null && wood.TreeCount > 0) BuildFellOrder(wood);
            EnsureClearing(true);

            int standing = 0, campDown = 0;
            if (wood != null && wood.TreeCount > 0)
            {
                int down = DownWanted(wood);
                foreach (int i in OwnTrees(shape, wood, false))
                {
                    if (!wood.TreeAt(i).felled) standing++;
                    else if (posInOrder != null && i < posInOrder.Length
                             && posInOrder[i] >= 0 && posInOrder[i] < down) campDown++;
                }
            }
            int rocks = 0, ore = 0;
            GatherRocks(rockScratch);
            foreach (var n in OwnRocks(shape, rockScratch, false))
            {
                if (!n.Gathered) { rocks++; if (n.Resource == Res.Ore) ore++; }
                else
                {
                    // Hidden by the seam's own prefix: the seam gives it up.
                    var st = ledger.Stock(n.Resource);
                    if (st != null) st.standing = Mathf.Min(st.standingMax, st.standing + n.UnitsPerProp);
                }
            }

            row.clearTrees = standing;
            row.clearRocks = rocks;
            row.clearOre = ore;
            row.clearDone = 0f;
            row.clearSited = true;

            if (campDown > 0)
            {
                ledger.treesFelled = Mathf.Max(0, ledger.treesFelled - campDown);
                ledger.timberTaken = Mathf.Max(0f, ledger.timberTaken - campDown);
                if (ledger.treesRegrown > ledger.treesFelled) ledger.treesRegrown = ledger.treesFelled;
            }
        }

        /// What of `row`'s plot is down right now, by identity -- taken
        /// BEFORE the row leaves the queue, handed to `ReturnToCamp` after.
        void SnapshotDown(PendingBuild row, List<int> trees, List<ResourceNode> rocks)
        {
            trees.Clear(); rocks.Clear();
            if (row == null) return;
            EnsureClearing(true);
            if (!clearByRow.TryGetValue(row, out var e)) return;
            for (int k = 0; k < e.TreesDown; k++) trees.Add(e.trees[k]);
            for (int k = 0; k < e.RocksDown; k++) if (e.rocks[k] != null) rocks.Add(e.rocks[k]);
        }

        /// **A row left the queue without being raised** (cancelled, or moved
        /// to other ground): the stumps and broken rocks it leaves behind go
        /// back on the camp's books, so the counts still describe what is
        /// down. Only the ones no other footprint picked up.
        void ReturnToCamp(List<int> trees, List<ResourceNode> rocks)
        {
            if (ledger == null) return;
            EnsureClearing(true);
            int back = 0;
            foreach (int i in trees) if (i >= 0 && (i >= siteTree.Length || !siteTree[i])) back++;
            if (back > 0)
            {
                ledger.treesFelled += back;
                ledger.timberTaken += back;
            }
            foreach (var n in rocks)
            {
                if (n == null || siteRocks.Contains(n)) continue;
                var st = ledger.Stock(n.Resource);
                if (st != null) st.standing = Mathf.Max(0f, st.standing - n.UnitsPerProp);
            }
        }

        readonly List<int> snapTrees = new List<int>();
        readonly List<ResourceNode> snapRocks = new List<ResourceNode>();

        // --- who is clearing which obstruction --------------------------------

        readonly List<CampWorker> clearHands = new List<CampWorker>();
        readonly List<PendingBuild> clearRowsOf = new List<PendingBuild>();
        /// The claim: a tree index, or `-1 - k` for rock k of the row's entry.
        readonly List<int> clearTargets = new List<int>();

        /// **Send a builder to the next thing on this plot.** The candidates
        /// are the row's standing obstructions IN THE ORDER THE LEDGER TAKES
        /// THEM DOWN (trees nearest the camp first, then stone rocks, then
        /// ore), so the one a man walks to is the one that is about to fall.
        /// Each hand gets the first one nobody else holds; when there are
        /// more hands than obstructions the extras double up on the front
        /// one rather than standing idle.
        public bool ClaimClearing(PendingBuild row, CampWorker w, out Vector3 at, out bool isRock)
        {
            at = Vector3.zero;
            isRock = false;
            if (w == null) return false;
            if (row == null || row.Cleared || ledger == null || !ledger.sites.Contains(row))
            { ReleaseClearing(w); return false; }
            EnsureClearing(true);
            if (!clearByRow.TryGetValue(row, out var e)) { ReleaseClearing(w); return false; }
            var wood = WoodHere();
            PruneClearing();

            int fallback = int.MinValue;
            for (int k = e.TreesDown; k < e.trees.Length; k++)
            {
                int i = e.trees[k];
                if (wood == null || i < 0 || i >= wood.TreeCount || wood.TreeAt(i).felled) continue;
                if (fallback == int.MinValue) fallback = i;
                if (ClearClaimedByAnother(i, w)) continue;
                return HoldClearing(w, row, i, e, out at, out isRock);
            }
            for (int k = e.RocksDown; k < e.rocks.Length; k++)
            {
                var n = e.rocks[k];
                if (n == null || n.Gathered) continue;
                int t = -1 - k;
                if (fallback == int.MinValue) fallback = t;
                if (ClearClaimedByAnother(t, w)) continue;
                return HoldClearing(w, row, t, e, out at, out isRock);
            }
            if (fallback != int.MinValue) return HoldClearing(w, row, fallback, e, out at, out isRock);
            ReleaseClearing(w);
            return false;
        }

        bool HoldClearing(CampWorker w, PendingBuild row, int target, ClearEntry e,
            out Vector3 at, out bool isRock)
        {
            isRock = target < 0;
            at = isRock ? e.rocks[-1 - target].transform.position
                        : WoodHere().TreeAt(target).baseAt;
            for (int k = 0; k < clearHands.Count; k++)
                if (ReferenceEquals(clearHands[k], w))
                { clearRowsOf[k] = row; clearTargets[k] = target; return true; }
            clearHands.Add(w); clearRowsOf.Add(row); clearTargets.Add(target);
            return true;
        }

        /// **Has the thing this hand is standing at gone down?** True when it
        /// has (the ledger crossed a whole obstruction and `SyncClearing`
        /// showed it), when the row is cleared, raised or cancelled, or when
        /// the hand holds no claim at all -- in every case, ask again.
        public bool ClearingTargetGone(CampWorker w)
        {
            for (int k = 0; k < clearHands.Count; k++)
            {
                if (!ReferenceEquals(clearHands[k], w)) continue;
                var row = clearRowsOf[k];
                if (row == null || row.Cleared || ledger == null || !ledger.sites.Contains(row)) return true;
                int t = clearTargets[k];
                if (t >= 0)
                {
                    var wood = WoodHere();
                    return wood == null || t >= wood.TreeCount || wood.TreeAt(t).felled;
                }
                EnsureClearing(true);
                if (!clearByRow.TryGetValue(row, out var e)) return true;
                int rk = -1 - t;
                return rk >= e.rocks.Length || e.rocks[rk] == null || e.rocks[rk].Gathered;
            }
            return true;
        }

        public void ReleaseClearing(CampWorker w)
        {
            for (int k = clearHands.Count - 1; k >= 0; k--)
                if (clearHands[k] == null || ReferenceEquals(clearHands[k], w))
                { clearHands.RemoveAt(k); clearRowsOf.RemoveAt(k); clearTargets.RemoveAt(k); }
        }

        bool ClearClaimedByAnother(int target, CampWorker w)
        {
            for (int k = 0; k < clearHands.Count; k++)
                if (clearTargets[k] == target && !ReferenceEquals(clearHands[k], w)) return true;
            return false;
        }

        void PruneClearing()
        {
            for (int k = clearHands.Count - 1; k >= 0; k--)
                if (clearHands[k] == null)
                { clearHands.RemoveAt(k); clearRowsOf.RemoveAt(k); clearTargets.RemoveAt(k); }
        }

        // --- who is on which tree ---------------------------------------------

        /// **Tunables, as plain statics.** An `Outpost` is added at runtime by
        /// the survey, so a `[SerializeField]` here would be a dial nobody can
        /// turn -- the same reason `Hand.Feel` and `CampWorker.Feel` are shaped
        /// this way.
        public static class Feel
        {
            /// How long a tree the ledger has paid for may stand with nobody
            /// swinging at it. Long enough to cover the walk out from the fire
            /// at camp pace; short enough that a watched camp never looks
            /// stuck.
            public static float fellGraceSeconds = 10f;

            /// How far the ledger may run ahead of the mesh, over and above
            /// one tree per hand cutting, before the lag is abandoned and the
            /// whole debt is taken at once. Two is slack for the hand who is
            /// carrying and the hand who is walking back.
            public static int fellBacklogSlack = 2;

            /// **Ground the wood never takes back.** Kevin, 2026-09-22:
            /// *"they should re-grow further away from camp, to help the camp
            /// not get overgrown."* Four times the clearing a camp is founded
            /// with (`CampClearingRadius`, 7.5 m), so that the huts, the
            /// piles, the fire ring and the walk between them stay open ground
            /// for as long as somebody lives here -- and the wood beyond it
            /// closes back in, which is what makes leaving a place for a
            /// season mean something.
            public static float campClearing = 30f;
        }

        /// The hands with a tree claimed here, and which tree each one is on.
        /// Two lists rather than a dictionary: there are never more than a
        /// handful, and a scan of four entries is cheaper than a hash.
        readonly List<CampWorker> claimHands = new List<CampWorker>();
        readonly List<int> claimTrees = new List<int>();

        /// How many hands are cutting wood here and can be SEEN to be. What
        /// rule (c) measures its slack against.
        public int CuttingHands { get { PruneClaims(); return claimHands.Count; } }

        /// **The slot-th tree the deterministic order has not taken down yet.**
        ///
        /// This is the whole of what a worker is allowed to know about which
        /// tree is next: the order belongs to the ledger, and a man who chose
        /// his own trunk is the bug Kevin reported.
        public int NextToFell(int slot)
            => NextToFell(slot, out int i, out _) ? i : -1;

        public bool NextToFell(int slot, out int treeIndex, out Vector3 baseAt)
        {
            treeIndex = -1;
            baseAt = Vector3.zero;
            var wood = WoodHere();
            if (wood == null || wood.TreeCount == 0 || slot < 0) return false;
            BuildFellOrder(wood);

            int seen = 0;
            for (int k = 0; k < fellOrder.Length; k++)
            {
                int i = fellOrder[k];
                if (wood.TreeAt(i).felled) continue;
                if (seen++ < slot) continue;
                treeIndex = i;
                baseAt = wood.TreeAt(i).baseAt;
                return true;
            }
            return false;
        }

        /// **Give this hand the front-most tree nobody else is on.**
        ///
        /// The k-th hand to ask gets the k-th entry of the order, so the trees
        /// being worked are always the ones the ledger is about to take down,
        /// and two men are never sent to the same trunk.
        ///
        /// **The whole island, nearest first** (2026-09-21). This used to stop
        /// at `CampWorker.Reach` and hand back "nothing", and a man with
        /// nothing walked in circles by the fire -- Kevin: *"when I assign
        /// people to build and there are no resources to build with they just
        /// walk around aimlessly. Instead they should gather the resources
        /// necessary (as long as those resources exist on the island)."* The
        /// order already IS the ledger's, measured from the camp outward and
        /// walked front-first, and the ledger fells past any reach the moment
        /// the near wood is gone; so the reach was the one thing making the
        /// man and the books disagree. Now he walks as far as the next tree
        /// is, and the only false answer is an island with nothing left
        /// standing -- which is when `OutpostLedger.BuildStarved` says so too
        /// (`ReconcileWood`).
        ///
        /// He stays enrolled either way: a hand between errands is still a
        /// hand cutting wood here, and dropping him out of the count for the
        /// second and a half he spends walking home would let rule (c) empty
        /// the wood behind his back.
        public bool ClaimTree(CampWorker w, out int treeIndex, out Vector3 baseAt)
        {
            treeIndex = -1;
            baseAt = Vector3.zero;
            if (w == null) return false;

            var wood = WoodHere();
            if (wood == null || wood.TreeCount == 0) { Enrol(w, -1); return false; }
            BuildFellOrder(wood);
            PruneClaims();

            for (int k = 0; k < fellOrder.Length; k++)
            {
                int i = fellOrder[k];
                if (wood.TreeAt(i).felled) continue;
                if (ClaimedByAnother(i, w)) continue;
                Enrol(w, i);
                treeIndex = i;
                baseAt = wood.TreeAt(i).baseAt;
                return true;
            }
            Enrol(w, -1);
            return false;
        }

        /// This hand has stopped cutting: picked up, re-ordered, recalled or
        /// switched off with the camp.
        public void ReleaseTree(CampWorker w)
        {
            for (int k = claimHands.Count - 1; k >= 0; k--)
                if (claimHands[k] == null || ReferenceEquals(claimHands[k], w))
                { claimHands.RemoveAt(k); claimTrees.RemoveAt(k); }
        }

        /// The tree this hand has claimed, or -1.
        public int TreeClaimedBy(CampWorker w)
        {
            for (int k = 0; k < claimHands.Count; k++)
                if (ReferenceEquals(claimHands[k], w)) return claimTrees[k];
            return -1;
        }

        /// Is this tree down? Asked by the man standing at it, which is how he
        /// knows to pick up a log -- he does not fell it, he sees it fall.
        public bool TreeIsFelled(int treeIndex)
        {
            var wood = WoodHere();
            if (wood == null || treeIndex < 0 || treeIndex >= wood.TreeCount) return true;
            return wood.TreeAt(treeIndex).felled;
        }

        /// Where a tree stands, for anything that has to walk to one.
        public bool TreeBase(int treeIndex, out Vector3 at)
        {
            at = Vector3.zero;
            var wood = WoodHere();
            if (wood == null || treeIndex < 0 || treeIndex >= wood.TreeCount) return false;
            at = wood.TreeAt(treeIndex).baseAt;
            return true;
        }

        void Enrol(CampWorker w, int treeIndex)
        {
            for (int k = 0; k < claimHands.Count; k++)
                if (ReferenceEquals(claimHands[k], w)) { claimTrees[k] = treeIndex; return; }
            claimHands.Add(w);
            claimTrees.Add(treeIndex);
        }

        bool ClaimedByAnother(int treeIndex, CampWorker w)
        {
            for (int k = 0; k < claimHands.Count; k++)
                if (claimTrees[k] == treeIndex && !ReferenceEquals(claimHands[k], w)) return true;
            return false;
        }

        bool SomebodyChopping(int treeIndex)
        {
            for (int k = 0; k < claimHands.Count; k++)
            {
                var w = claimHands[k];
                if (w != null && w.IsFellingNow(treeIndex)) return true;
            }
            return false;
        }

        /// Workers die with their bodies -- `CampWorker.Remove` destroys the
        /// component, and a destroyed component still sits in this list until
        /// somebody looks.
        void PruneClaims()
        {
            for (int k = claimHands.Count - 1; k >= 0; k--)
                if (claimHands[k] == null) { claimHands.RemoveAt(k); claimTrees.RemoveAt(k); }
        }

        /// Ground height here, for anything that has to stand something on it.
        /// The height field is the authority everywhere in this codebase; this
        /// is just the polite way to ask an outpost for it.
        public float GroundAt(Vector3 at) => height != null ? height(at.x, at.z) : at.y;

        // --- the live raid, 2026-09-22 -----------------------------------------

        /// **The nearest shore to a point, as a raider would use it**: `shore`
        /// is dry sand a step above the waterline, `water` is where a hull
        /// can stop with ~5 m under it. Sixteen headings out from `from`,
        /// half-metre steps to 120 m, the shortest wins. False if no heading
        /// reaches water that deep -- a camp in the middle of a big island
        /// is not raidable from its own fire, and says so.
        public bool ShoreNear(Vector3 from, out Vector3 shore, out Vector3 water)
        {
            shore = from; water = from;
            if (height == null) return false;
            const float Step = 0.5f, Reach = 120f, WantDepth = 5f, LandUp = 1.5f;
            float best = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k * (Mathf.PI * 2f / 16f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 land = from; bool haveLand = false;
                for (float d = Step; d <= Reach; d += Step)
                {
                    Vector3 q = from + dir * d;
                    float h = height(q.x, q.z);
                    if (!haveLand)
                    {
                        if (h >= 0f) land = q;
                        else haveLand = true;   // crossed the waterline
                    }
                    if (haveLand && -h >= WantDepth)
                    {
                        if (d < best)
                        {
                            best = d;
                            // Back up the beach a little so the party lands
                            // on sand, not in the wash.
                            Vector3 s = land - dir * LandUp;
                            s.y = height(s.x, s.z);
                            shore = s;
                            q.y = 0f;
                            water = q;
                        }
                        break;
                    }
                }
            }
            return best < float.MaxValue;
        }

        /// **A building is gone**: the raiders have knocked the watchtower
        /// down, or anything else that can end a building later. The books
        /// and the ground agree again: one `built` id out, its `raised` row
        /// (nearest by position) out, anybody assigned to it idle, the object
        /// destroyed. The ledger's ceiling follows on the next `CatchUp`.
        public void Demolish(Building b)
        {
            if (b == null) return;
            string id = b.Id;
            built.Remove(b);
            if (ledger != null)
            {
                int bestI = -1; float bestD = float.MaxValue;
                for (int i = 0; i < ledger.raised.Count; i++)
                {
                    var r = ledger.raised[i];
                    if (r == null || r.planId != id) continue;
                    float d = (r.At - new Vector3(b.transform.position.x, 0f, b.transform.position.z)).sqrMagnitude;
                    if (d < bestD) { bestD = d; bestI = i; }
                }
                // The ledger takes the `built` id and that `raised` row out,
                // and the station row THIS building was (not the plan's last)
                // -- so a surviving twin keeps its own stock.
                ledger.DemolishBuilt(id, bestI);
                if (!ledger.built.Contains(id))
                    foreach (var h in ledger.hands)
                        if (h != null && h.order == OutpostOrder.Work && h.target == id)
                        { h.order = OutpostOrder.Idle; h.target = ""; }
            }
            Destroy(b.gameObject);
            if (Watched) { ArrangeHands(); PuppetsToWork(); }
        }

        /// The raid director only thinks about a camp somebody is standing
        /// at: a raid is something you FIGHT, so it happens in front of you.
        /// (What happens while you are away is the ledger's clock.)
        /// Seconds between the self-ticks below.
        const float CatchUpEvery = 0.25f;
        float nextCatchUp;

        void Update()
        {
            // **The books settle on their own while a camp is watched,
            // 2026-09-23.** Kevin: *"when the hut was completed it remained
            // a blueprint even after it was completed, until I pressed on
            // it."* `CatchUp` is what runs the tick AND what stands a
            // finished site up (`FinishReady`), and until today nothing
            // called it on its own: it rode on a sheet refresh, a Hand
            // order, a crew delivery or an anchor drop. So a hut finished
            // by the arithmetic sat there as a drawing until the player
            // touched something. The tick itself advances on a fixed
            // quantum of game time, so this adds reconciliation passes,
            // not simulation -- and the camp page already ran one of them
            // every frame whenever it was up.
            if (!Watched) return;
            // Four times a second, not sixty: `CatchUp` reconciles the
            // props and the bodies as well as running the tick, and the
            // tick itself only advances on a 0.1-day quantum anyway. A hut
            // that finishes a quarter of a second before it stands up is a
            // hut that stood up when it was finished; sixty passes of prop
            // reconciliation a second on a phone is not.
            nextCatchUp -= Time.unscaledDeltaTime;
            if (nextCatchUp <= 0f)
            {
                nextCatchUp = CatchUpEvery;
                CatchUp();
            }
            if (HasCamp) Combat.RaidDirector.Consider(this, Time.deltaTime);
        }

        // --- telling one hand what to do -------------------------------------

        /// **Send this hand after a resource.** `resource` must be something
        /// the island actually has — see `Gatherable`.
        public bool OrderGather(OutpostHand h, string resource)
        {
            if (h == null || ledger == null || !Res.IsGatherable(resource)) return false;
            h.order = OutpostOrder.Gather;
            h.target = resource;
            ArrangeHands();
            PuppetsToWork();
            return true;
        }

        /// **Assign this hand to a building.** The position is the building's,
        /// so what they make is decided by what they were assigned to rather
        /// than by anything carried on the hand.
        public bool Assign(OutpostHand h, string planId)
        {
            if (h == null || ledger == null) return false;
            if (!BuildPlans.HasPosition(planId)) return false;
            if (CountOf(planId) <= 0) return false;         // it is not standing here
            h.order = OutpostOrder.Work;
            h.target = planId;
            ArrangeHands();
            PuppetsToWork();
            return true;
        }

        /// **Put this one hand on the blueprint.** `Site` orders everybody to
        /// build and the crew list has never needed anything finer, but the
        /// Hand drops ONE person on a drawing, and the rest of the camp should
        /// go on with what they were doing. Nothing to build is a refusal
        /// rather than an order that silently does nothing.
        public bool OrderBuild(OutpostHand h)
        {
            if (h == null || ledger == null || !ledger.Building) return false;
            h.order = OutpostOrder.Build;
            h.target = "";
            ArrangeHands();
            PuppetsToWork();
            return true;
        }

        public bool OrderIdle(OutpostHand h)
        {
            if (h == null) return false;
            h.order = OutpostOrder.Idle;
            h.target = "";
            ArrangeHands();
            return true;
        }

        /// **What can be gathered here**, in the order the menu offers it.
        ///
        /// Timber first because every island has trees, then whatever kind the
        /// populator gave this one. A stock with nothing left in it is still
        /// listed — an empty seam is information, and hiding it would look
        /// like the menu was broken.
        public List<string> Gatherable()
        {
            var list = new List<string>();
            if (ledger == null) return list;
            foreach (var st in ledger.stocks)
                if (st != null && Res.IsGatherable(st.resource)) list.Add(st.resource);
            return list;
        }

        /// **The positions standing here that somebody could be put in.**
        /// One entry per building with a job, so two sawmills offer two.
        public List<string> Positions()
        {
            var list = new List<string>();
            foreach (var b in built)
                if (b != null && BuildPlans.HasPosition(b.Id) && !list.Contains(b.Id))
                    list.Add(b.Id);
            return list;
        }

        /// **What this camp could build next.** A plan already standing here
        /// is offered again only if it is worth having twice -- a second store
        /// hut is, a second fire is not.
        public List<BuildPlan> Buildable()
        {
            var list = new List<BuildPlan>();
            foreach (var p in BuildPlans.AtACamp)
            {
                if (p.kind == BuildKind.Fire) continue;      // the fire is how you got here
                list.Add(p);
            }
            return list;
        }

        /// The building this hand is assigned to, or null if they belong to
        /// the fire.
        public Building WorkplaceOf(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Work || string.IsNullOrEmpty(h.target))
                return null;
            foreach (var b in built) if (b != null && b.Id == h.target) return b;
            return null;
        }

        /// How far off the fire they stand. Close enough to be warming their
        /// hands at it, far enough that four of them are four people and not
        /// one blob at this zoom.
        public const float FireRingRadius = 2.9f;

        /// How much wood a camp clears when it is founded. Room to walk round
        /// the fire and stack what came down, nothing more.
        public const float CampClearingRadius = 7.5f;

        /// Everything this place can keep. Land more than this on one voyage
        /// and the surplus stays on the ground and is not there when you get
        /// back.
        /// **What this place keeps OF EACH THING.**
        ///
        /// It used to be one number for one resource, because timber was all
        /// an island had. Kevin, 2026-09-19: *"crew on the island can gather
        /// resources up to 10 of each without a storage unit."* So the fire's
        /// ten, and every store hut's twenty, apply to each kind separately --
        /// which is also what makes a second resource worth gathering rather
        /// than a competitor for the same ten slots.
        public int KeepsOfEach
        {
            get
            {
                int n = openCapacity;
                foreach (var b in built)
                    if (b != null)
                        n += b.StoreCapacity + (ledger != null ? Economy.Techs.StoreBonus(b.Id, ledger.LevelOf(b.Id)) : 0);
                return n;
            }
        }

        /// The old name, kept because home's voyage panel still asks in the
        /// singular and means the same thing there: home keeps one pile.
        public int StoreCapacity => KeepsOfEach;

        public IReadOnlyList<Building> Built => built;

        public int CountOf(string planId)
        {
            int n = 0;
            foreach (var b in built) if (b != null && b.Id == planId) n++;
            return n;
        }

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            if (Home == null && IsHome) Home = this;
        }

        void OnDisable()
        {
            all.Remove(this);
            if (Home == this) Home = null;
        }

        /// The outpost on this island, or null if there is not one yet. Does
        /// NOT survey -- see `Establish`.
        public static Outpost Of(Island isle)
        {
            if (isle == null) return null;
            foreach (var o in all) if (o != null && o.Island == isle) return o;
            return null;
        }

        // --- siting ---------------------------------------------------------

        /// Attach an outpost to ground that has already been surveyed. This is
        /// the home path: the populator measures before the scenery is baked,
        /// so the clearing can be reserved, and hands the result straight in.
        ///
        /// `viewHalfWidth` is passed rather than read off `Dock` because a camp
        /// has no pier. Home passes the docked shot's half-width and gets
        /// exactly the clearing it always had; anywhere else passes its own
        /// overview, so the clearing is sized to the shot the player will
        /// actually be looking at. Zero falls back to the dock's, which keeps
        /// every existing caller behaving identically.
        public void Configure(Settlement settlement, System.Func<float, float, float> terrainHeight,
            float minGroundHeight, float viewHalfWidth = 0f)
        {
            site = settlement;
            height = terrainHeight;
            minHeight = minGroundHeight;

            // Bare ground keeps nothing.
            // Kevin, 2026-09-22: no island is home any more -- the starting
            // island's beach keeps 0 like everywhere else.
            openCapacity = 0;

            // Centre the clearing on a circle that FITS in the buildable
            // patch, not on its centroid: a lobed patch has a centroid that
            // need not be on it at all, and the whole point of this disc is
            // that everything inside it is ground you can build on.
            //
            // And not on the island's BIGGEST such circle either. At home that
            // one is 130 m from the head of the pier, well outside the frame
            // the docked camera holds, so everything raised in it would be
            // invisible at the one moment the player is standing still looking
            // at the place. `VillageAt` is the best clearing inside that frame
            // -- see SettlementSite.Find.
            ClearingCentre = settlement.VillageAt;

            // The clearing is NOT the inscribed circle. That circle guarantees
            // every point in it is buildable, which sounds right and costs the
            // settlement most of its ground: home's best in-frame one is
            // 12.6 m, which holds exactly one storehouse. The guarantee was
            // never needed -- `Corners` tests each building against the actual
            // rectangle it stands on, so a candidate on bad ground is refused
            // whatever the clearing says.
            //
            // So the clearing is sized by what the SHOT holds instead: the
            // measured half-width of the view, less the room a building needs
            // to stand at its edge and still be in it.
            float widest = 0f;
            foreach (var plan in BuildPlans.All)
                widest = Mathf.Max(widest,
                    0.5f * Mathf.Sqrt(plan.footprint.x * plan.footprint.x
                                    + plan.footprint.y * plan.footprint.y));
            float reach = viewHalfWidth > 0.01f ? viewHalfWidth : Dock.ViewHalfWidth;
            ClearingRadius = Mathf.Clamp(reach - widest - 2f, 14f, 30f);

            // A CAMP's clearing is sized by the ground, not by the shot.
            //
            // Home's 30 m comes from what the docked camera holds, and home
            // has the broad flat ground to fill it. An island need not: the
            // measured circle that actually fits inside its buildable patch
            // can be a third of that, and a spiral searching 30 m of a 10 m
            // patch puts every candidate off the good ground and finds
            // nowhere to stand a fire. The floor drops to 4 m for the same
            // reason -- 14 m is a village's minimum, and a camp is a fire.
            // Kevin, 2026-09-22: no island is home any more -- the starting
            // island's clearing is ground-measured like every other camp.
            if (settlement.VillageClearing > 0.01f)
                ClearingRadius = Mathf.Clamp(
                    Mathf.Min(ClearingRadius, settlement.VillageClearing), 4f, 30f);

            // Seed the ledger off the ground that was just surveyed: how much
            // timber stands within reach is a property of the place, so it
            // belongs to the survey rather than to a constant.
            //
            // **The island's wood, not the clearing's.** This used to be the
            // FLAT ground the survey found, which is a fact about where you can
            // build and not about how much timber there is -- a wooded islet
            // with half a hectare of level ground was given twenty logs, spent
            // fourteen of them on a fire and a first pile, and could never
            // finish a sawmill. Hands walk the whole island for a tree; the
            // stock is the whole island's, less the share that is beach, rock
            // and meadow.
            float workedHa = WorkedHectares();
            if (ledger == null)
                ledger = OutpostLedger.For(ClearingCentre, workedHa);

            // **What else this island has, the populator already decided.**
            // `WorldSettings.kinds` gives every island one of Timber, Stone,
            // Ore or Spice and scatters props of it with `ResourceNode`s on
            // them, unlocked further from home. So the Gather menu's contents
            // are a fact about the place rather than a fixed list -- and
            // seeding the stock here is the one moment the ledger is allowed
            // to learn it, because after this it must work with the island
            // unloaded.
            string kind = Island != null ? Island.ResourceName : null;
            if (!string.IsNullOrEmpty(kind) && kind != Res.Timber && Res.IsGatherable(kind))
                ledger.SeedStock(kind, workedHa);

            // **And stone, wherever you are, 2026-09-21.** Kevin: *"all
            // buildings require at least wood and stone."* A price you
            // cannot pay on three islands in four is not a price, it is a
            // wall -- so every island has SOME rock in it, and the populator
            // puts a few boulders on the ground to say so.
            //
            // A third of the seam an ore-and-stone island gets, because the
            // island whose KIND is Stone has to stay worth sailing to: a few
            // boulders behind the camp are enough to finish the buildings
            // you raise there and nothing like enough to load a hold with.
            EnsureStoneStock();
            EnsureFoodStock();
            PlaceCampStone();
        }

        /// Share of a Stone island's density that an island of any other
        /// kind gets, so that its own buildings can be paid for. See
        /// `EnsureStoneStock`.
        public const float ScatteredStoneShare = 1f / 3f;

        /// **Every camp can quarry SOME stone**, 2026-09-21. Kevin: *"all
        /// buildings require at least wood and stone."*
        ///
        /// A stock rather than a special case: the ledger already knows how
        /// to hold standing rock, pay a blueprint out of it and let
        /// `GatherSync` hide boulders as it falls, so stone on an ordinary
        /// island is the same object a Stone island's seam is, seeded a
        /// third as rich (`ScatteredStoneShare`) -- enough to raise what you
        /// build there, nowhere near enough to fill a hold with.
        ///
        /// **Called from `CatchUp`, not only from `Configure`, and that is
        /// the whole reason it is a method.** A camp restored from a save
        /// brings its own ledger, written before stone existed, and
        /// `Configure`'s seeding never touches it -- so the camp would stand
        /// there with boulders round it and no seam in the books, and every
        /// blueprint after the fire would stall on "NO STONE LEFT". Seeding
        /// where the ledger is next ticked catches both paths with one line.
        public void EnsureStoneStock()
        {
            if (ledger == null || Island == null) return;
            if (Island.ResourceName == Res.Stone) return;   // it has a proper seam
            if (ledger.Stock(Res.Stone) != null) return;
            ledger.SeedStock(Res.Stone, WorkedHectares() * ScatteredStoneShare);
        }

        /// **Put the island's few boulders where the camp can see them.**
        ///
        /// The populator scatters three to six of them anywhere on the
        /// island above the beach, which is right for an island's LOOK and
        /// useless as a quarry: on a 147 m island the nearest one measured
        /// 121 m from the fire, and a builder walking 240 m for one stone is
        /// a builder who reads as broken. Nothing at populate time knows
        /// where the camp will be -- the clearing is surveyed here, at
        /// `Configure`, which is the first moment anybody does.
        ///
        /// So they are MOVED rather than re-made: same objects, same
        /// `ResourceNode`s, same `GatherSync` ordering, just stood in a ring
        /// outside the clearing where a man can walk out to one and back.
        /// Idempotent -- a boulder already inside the ring is left alone, so
        /// re-surveying (or a second visit) moves nothing.
        ///
        /// A Stone island is skipped entirely: its seam is the reason to
        /// sail there and it belongs where the ground put it.
        void PlaceCampStone()
        {
            if (Island == null || height == null) return;
            if (Island.ResourceName == Res.Stone) return;

            // **Round the FIRE, not round the survey.** The clearing is
            // where a camp could have gone; the campfire is where the player
            // put it, and on the first island this was measured on those two
            // were 177 m apart -- so a ring laid on the clearing left every
            // boulder as far from the builders as the populator had. The
            // camp centre is only known once the fire is lit, which is why
            // this is also called from `CatchUp` and not only from
            // `Configure`.
            Vector3 centre = hasCampCentre ? campCentre : ClearingCentre;
            if ((stonePlacedAt - centre).sqrMagnitude < 1f) return;
            stonePlacedAt = centre;

            campStone.Clear();
            foreach (var n in ResourceNode.All)
                if (n != null && n.Home == Island && n.Resource == Res.Stone) campStone.Add(n);
            if (campStone.Count == 0) return;

            // Outside the clearing, so a boulder is never standing where a
            // building will go, and within a walk of it.
            float near = Mathf.Max(ClearingRadius + 4f, 14f);
            float far = near + 14f;
            // A bearing that depends only on WHERE this island is, so the
            // ring reproduces on every visit and across a save.
            float turn = Mathf.Abs(centre.GetHashCode() % 360) * Mathf.Deg2Rad;

            for (int i = 0; i < campStone.Count; i++)
            {
                var n = campStone[i];
                Vector3 d = n.transform.position - centre;
                d.y = 0f;
                if (d.magnitude <= far) continue;           // already within reach

                float a = turn + (i / (float)campStone.Count) * Mathf.PI * 2f;
                for (int k = 0; k < 8; k++)
                {
                    float ang = a + k * 0.55f;
                    float r = Mathf.Lerp(near, far, ((i * 0.37f + k * 0.19f) % 1f));
                    Vector3 at = centre
                        + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    float h = height(at.x, at.z);
                    if (h < minHeight) continue;            // beach, or in the water
                    at.y = h;
                    n.transform.position = at;
                    break;
                }
            }
            campStone.Clear();
        }

        static readonly List<ResourceNode> campStone = new List<ResourceNode>();
        /// The centre the boulders were last laid round, so `CatchUp` can
        /// call this every time and do nothing every time but the first.
        Vector3 stonePlacedAt = new Vector3(float.NaN, float.NaN, float.NaN);

        // --- the wheat ---------------------------------------------------------

        /// The wheat dressed onto this island, cached like the wood.
        Terrain.SceneryCrops CropsHere()
        {
            if (cropsCache == null) cropsCache = GetComponentInChildren<Terrain.SceneryCrops>();
            return cropsCache;
        }
        Terrain.SceneryCrops cropsCache;

        /// **An island with wheat on it has a Food stock.** Seeded by the
        /// same hectare rule as every other resource (`Res.PerHectare`) so
        /// the camp can be worked unloaded, and then, whenever the beds can
        /// be seen, sized to them: one bed is one unit of Food, exactly as
        /// `ReconcileWood` makes the trees the authority on timber.
        void EnsureFoodStock()
        {
            if (ledger == null) return;
            var crops = CropsHere();
            if (crops == null || crops.BedCount == 0) return;
            if (ledger.Stock(Res.Food) == null)
                ledger.SeedStock(Res.Food, WorkedHectares());
        }

        /// **An island with a herd on it has game; one without has none, and
        /// never grows one.**
        ///
        /// Every other stock is seeded by the hectare so the camp can be
        /// worked with the island unloaded, and then sized to the mesh when
        /// the mesh turns up. Game cannot be: `Res.PerHectare(Game)` is zero
        /// on purpose, because the only thing that ever says how many goats
        /// are on a crag is the goats. So the count IS the seeding, it
        /// happens the first time the herd is loaded, and on an island where
        /// `FaunaHere()` is null nothing is ever written -- which is what
        /// keeps Hunt out of the Gather menu there (see `Gatherable`).
        void EnsureGameStock()
        {
            if (ledger == null) return;
            if (ledger.Stock(Res.Game) != null) return;
            var fauna = FaunaHere();
            if (fauna == null) return;
            int alive = AliveHere(fauna);
            if (alive <= 0) return;
            ledger.AddStanding(Res.Game, alive, Res.RegrowPerDay(Res.Game));
        }

        /// Beds are the authority on Food while they are loaded: the ceiling
        /// is the bed count, and the stock can never exceed what stands.
        void ReconcileCrops()
        {
            var crops = CropsHere();
            if (crops == null || crops.BedCount == 0) return;
            EnsureFoodStock();
            var stock = ledger.Stock(Res.Food);
            if (stock == null) return;
            // In yield units: a wheat bed is 1, a berry bush 0.5. Capacity
            // is every bed; what can be taken now is what stands.
            stock.standingMax = crops.TotalUnits;
            float standing = crops.StandingUnits;
            if (stock.standing > standing) stock.standing = standing;
            else if (stock.standing < 1f && standing >= 1f)
                stock.standing = Mathf.Min(1f, standing);
        }

        /// How many beds the books say have been cut: the Food stock's
        /// shortfall from its ceiling. Regrowth closes the gap, and the
        /// beds stand up again from the back of the order.
        public int BedsOwed
        {
            get
            {
                var stock = ledger != null ? ledger.Stock(Res.Food) : null;
                if (stock == null) return 0;
                return Mathf.Clamp(Mathf.FloorToInt(stock.standingMax - stock.standing + 1e-3f),
                                   0, Mathf.RoundToInt(stock.standingMax));
            }
        }

        /// **Make the field agree with the books**: harvested beds are a
        /// prefix of the nearest-the-camp order, exactly `BedsOwed` long.
        /// Pure in the ledger, like `SyncFelling`; a bed comes down the
        /// moment the Food count pays for it and stands back up when the
        /// regrowth has paid it back.
        public void SyncHarvest()
        {
            if (ledger == null) return;
            var crops = CropsHere();
            if (crops == null || crops.BedCount == 0) return;
            if (ledger.Stock(Res.Food) == null) return;
            BuildHarvestOrder(crops);
            int want = BedsOwed;
            for (int k = 0; k < harvestOrder.Length; k++)
            {
                int i = harvestOrder[k];
                if (k < want) crops.Harvest(i);
                else crops.Regrow(i);
            }
        }

        /// **Make the herd agree with the books.** The ledger has already
        /// taken the animal; this is the moment it falls over.
        ///
        /// Unlike a bed, a kill does not come back -- `Animal.Die` is one
        /// way, so this can only ever remove, and it removes exactly the
        /// difference. `CeilToInt` is the line that keeps a part-stalked
        /// animal alive: the hunter is half a day into his goat at
        /// `standing == 3.4`, and a goat that dropped dead halfway through
        /// being walked up on would be the arithmetic showing through.
        ///
        /// **Unwatched, nothing happens**, exactly as with felling. Nobody is
        /// there to see it, the books go on being right, and the mesh catches
        /// up on the next arrival -- when `CatchUp` calls this with the herd
        /// loaded and the shortfall already in the ledger.
        ///
        /// The one it takes is the one a hunter has already claimed, if there
        /// is one, so the animal that dies is the animal he was stalking;
        /// otherwise the nearest to the camp, which is the same
        /// nearest-first rule the wood and the wheat are cut by.
        public void SyncHunting()
        {
            if (!Watched || ledger == null) return;
            var stock = ledger.Stock(Res.Game);
            if (stock == null) return;
            var fauna = FaunaHere();
            if (fauna == null) return;

            int alive = AliveHere(fauna);
            int want = Mathf.Max(0, Mathf.CeilToInt(stock.standing));
            // Bounded by the count read ONCE, so a `Die` that failed to take
            // itself out of the list cannot turn this into a cull.
            int owed = alive - want;
            for (int n = 0; n < owed; n++)
            {
                var beast = NextQuarry(fauna);
                if (beast == null) break;
                beast.Die();
            }
        }

        /// The animal that dies next: the one somebody is already stalking,
        /// else the one nearest the fire.
        Animal NextQuarry(FaunaLod fauna)
        {
            var animals = fauna.Animals;
            if (animals == null) return null;
            Vector3 c = CampCentre;
            Animal near = null;
            float best = float.MaxValue;
            for (int i = 0; i < animals.Count; i++)
            {
                var a = animals[i];
                if (a == null || a.Dead) continue;
                if (a.Hunted) return a;
                Vector3 p = a.transform.position - c;
                p.y = 0f;
                float d2 = p.sqrMagnitude;
                if (d2 < best) { best = d2; near = a; }
            }
            return near;
        }

        void BuildHarvestOrder(Terrain.SceneryCrops crops)
        {
            Vector3 c = CampCentre;
            if (harvestOrder != null && harvestOrder.Length == crops.BedCount
                && ReferenceEquals(harvestOrderCrops, crops)
                && (harvestOrderFrom - c).sqrMagnitude < 0.25f) return;
            var idx = new int[crops.BedCount];
            var d2 = new float[crops.BedCount];
            for (int i = 0; i < idx.Length; i++)
            {
                idx[i] = i;
                Vector3 p = crops.BedAt(i).at - c;
                p.y = 0f;
                d2[i] = p.sqrMagnitude;
            }
            System.Array.Sort(d2, idx);
            harvestOrder = idx;
            harvestOrderFrom = c;
            harvestOrderCrops = crops;
        }

        int[] harvestOrder;
        Vector3 harvestOrderFrom;
        Terrain.SceneryCrops harvestOrderCrops;

        readonly List<CampWorker> bedHands = new List<CampWorker>();
        readonly List<int> bedClaims = new List<int>();

        /// **Give this hand the front-most standing bed nobody else is on**,
        /// which is the bed the ledger will cut next. False with nothing
        /// standing.
        public bool ClaimBed(CampWorker w, out int bedIndex, out Vector3 at)
        {
            bedIndex = -1;
            at = Vector3.zero;
            var crops = CropsHere();
            if (w == null || crops == null || crops.BedCount == 0) return false;
            BuildHarvestOrder(crops);
            for (int k = bedHands.Count - 1; k >= 0; k--)
                if (bedHands[k] == null) { bedHands.RemoveAt(k); bedClaims.RemoveAt(k); }
            for (int k = 0; k < harvestOrder.Length; k++)
            {
                int i = harvestOrder[k];
                if (crops.BedAt(i).harvested) continue;
                bool taken = false;
                for (int j = 0; j < bedHands.Count; j++)
                    if (bedClaims[j] == i && !ReferenceEquals(bedHands[j], w)) { taken = true; break; }
                if (taken) continue;
                int slot = bedHands.IndexOf(w);
                if (slot < 0) { bedHands.Add(w); bedClaims.Add(i); }
                else bedClaims[slot] = i;
                bedIndex = i;
                at = crops.BedAt(i).at;
                return true;
            }
            return false;
        }

        public void ReleaseBed(CampWorker w)
        {
            for (int k = bedHands.Count - 1; k >= 0; k--)
                if (bedHands[k] == null || ReferenceEquals(bedHands[k], w))
                { bedHands.RemoveAt(k); bedClaims.RemoveAt(k); }
        }

        public bool BedIsHarvested(int bedIndex)
        {
            var crops = CropsHere();
            if (crops == null || bedIndex < 0 || bedIndex >= crops.BedCount) return true;
            return crops.BedAt(bedIndex).harvested;
        }

        /// **Farm hook.** Called with the building the moment a farm plot is
        /// RAISED (never for a ghost). The farm system plants its beds here
        /// through `Terrain.SceneryCrops.Plant`.
        public static System.Action<Building> PlantFarmBeds;

        void AfterRaised(BuildPlan plan, Building b)
        {
            if (b == null) return;
            if (plan.id == BuildPlans.Farm.id) PlantFarmBeds?.Invoke(b);
        }

        /// Share of an island's disc that is worth working. The rest is
        /// beach, bare rock and open ground.
        const float WorkedShare = 0.6f;

        float WorkedHectares()
        {
            float flat = site != null ? site.AreaHectares : 0f;
            if (Island == null) return flat;
            float r = Island.Radius;
            return Mathf.Max(flat, WorkedShare * Mathf.PI * r * r / 10000f);
        }

        /// What a NEW outpost is sited under, published once by the world
        /// build.
        ///
        /// Same pattern as `Island.TerrainHeight`, and for the same reason:
        /// the populator is the only thing that knows the terrain's sand
        /// height, and an outpost has to be establishable from wherever the
        /// player decides to make camp. One writer, many readers.
        public struct SiteRules
        {
            public System.Func<float, float, float> height;

            /// Floor the SURVEY works to. Keeps a settlement off the beach:
            /// sand is flat, so it passes a slope test with room to spare and
            /// drags the patch onto the foreshore.
            ///
            /// **Not the same number as `buildFloor`, and conflating them cost
            /// a run.** Home surveys at `sandHeight + 0.5` and refuses
            /// individual buildings at `sandHeight + 1.2`; passing the stricter
            /// one to the survey found no ground at all on five islands out of
            /// five. A survey floor rejects the whole PLACE; a build floor
            /// rejects one shed.
            public float surveyFloor;

            /// Floor an individual building's corners are tested against.
            public float buildFloor;

            /// Half-width of the shot the player will judge the place in.
            public float viewHalfWidth;
            public bool Valid => height != null;
        }
        public static SiteRules Rules;

        /// Survey the island the ship is at and make camp possible there.
        /// Null when the ground will not take a settlement, or when the world
        /// has not published its rules yet.
        public static Outpost Establish(Island isle)
        {
            if (isle == null || !Rules.Valid) return null;
            var existing = Of(isle);
            if (existing != null) return existing;
            if (refused.Contains(isle)) return null;
            return Establish(isle, Rules.height, Rules.surveyFloor, Rules.buildFloor,
                SettlementRadiusFor(isle.Radius),
                Rules.viewHalfWidth > 0.01f ? Rules.viewHalfWidth : Dock.ViewHalfWidth);
        }

        /// Survey an island that was never surveyed at world build, and put an
        /// outpost on it.
        ///
        /// **Lazy on purpose.** `SettlementSite.Find` rasterises the island and
        /// then searches that raster for the largest inscribed circle; doing it
        /// for every island in the discovery radius at world build would be
        /// paid by every player on every launch, for islands most of them will
        /// never land on. It runs when the ship anchors instead -- the player
        /// is stationary and the camera is rising, which is the one moment in
        /// the game with room in the frame budget.
        ///
        /// Returns null when the island has no ground worth building on. That
        /// is a real answer, not a failure: a sandbank that honestly holds
        /// nothing is a better story than a global rule about what may be
        /// settled.
        public static Outpost Establish(Island isle, System.Func<float, float, float> terrainHeight,
            float surveyFloor, float buildFloor, float searchRadius, float viewHalfWidth)
        {
            if (isle == null || terrainHeight == null) return null;

            var existing = Of(isle);
            if (existing != null) return existing;

            // No `preferNear` here, unlike home.
            //
            // Home is sited into the homecoming SHOT -- the best clearing
            // within reach of the pier head, because a village the player
            // never stands still in front of may as well not be there. A camp
            // has no pier and no arrival composition to be sited into, so it
            // takes the island's own best ground and the overview goes to it
            // instead. Passing the island CENTRE as the preferred point was
            // the first attempt and it is worse than nothing: on a lobed
            // island the centre is as likely to be a summit as a field.
            var flat = Terrain.SettlementSite.Find(isle.transform.position, searchRadius,
                terrainHeight, 4f, surveyFloor);
            // Remember the "no". Otherwise a barren rock is re-rastered every
            // time she anchors there, and the laziness buys nothing.
            if (!flat.found) { refused.Add(isle); return null; }

            var settlement = isle.gameObject.AddComponent<Settlement>();
            settlement.Configure(flat);

            var outpost = isle.gameObject.AddComponent<Outpost>();
            outpost.Configure(settlement, terrainHeight, buildFloor, viewHalfWidth);
            return outpost;
        }

        // --- surveying over frames ------------------------------------------

        /// Survey this island WITHOUT spending a frame on it.
        ///
        /// The synchronous `Establish` costs 129 ms on an average island and
        /// **443 ms on the biggest** -- measured, not guessed, and far past the
        /// 16.7 ms a frame has. Almost all of it is the 32 k calls to the
        /// height function that fill the raster; the analysis on top is array
        /// passes and costs a millisecond or two.
        ///
        /// So the raster is filled a band of rows at a time. The moment this
        /// runs is the moment the anchor goes down, and the camera spends
        /// about 0.7 s rising into the overview before the player can act on
        /// anything -- which is more than enough, and means the survey is
        /// finished before there is a question for it to answer.
        ///
        /// Safe to call every frame: a survey already running or already done
        /// is left alone.
        public static void BeginSurvey(Island isle, MonoBehaviour host)
        {
            if (isle == null || host == null || !Rules.Valid) return;
            if (Surveyed(isle) || surveying.Contains(isle)) return;
            surveying.Add(isle);
            host.StartCoroutine(Survey(isle));
        }

        static readonly HashSet<Island> surveying = new HashSet<Island>();

        /// What the last frame-spread survey actually cost, recorded BY the
        /// survey rather than recomputed from its rules. A gate that re-ran the
        /// arithmetic would agree with itself whatever the frames did.
        public static float LastSurveyWorstFrameMs { get; private set; }
        public static int LastSurveyFrames { get; private set; }
        public static float WorstSurveyFrameMs { get; private set; }

        /// How many bands ran, and how many of them went over budget.
        ///
        /// **The max alone is the wrong statistic and it cost three rounds to
        /// see it.** A survey is thousands of short bands, and a single GC or
        /// a slice the OS took back adds ten milliseconds to whichever band it
        /// lands in — so the worst of eight thousand samples always finds an
        /// outlier, whatever the code does. Chasing it tuned a control loop
        /// against noise. What actually says "does this hitch" is how OFTEN a
        /// band is slow: three in eight thousand is the machine, two thousand
        /// is the feature.
        public static int SurveyBands { get; private set; }
        public static int SurveyBandsOverBudget { get; private set; }
        public static float SurveyBandMeanMs =>
            SurveyBands > 0 ? surveyBandTotalMs / SurveyBands : 0f;
        static float surveyBandTotalMs;

        /// A band the player could notice. One frame at 60 fps is 16.7 ms and
        /// the ship is stopped with the camera rising, so 10 is generous.
        public const float BandBudgetMs = 10f;

        /// The one-frame analysis pass at the end of a survey, which is not a
        /// band and cannot be spread: flood fill, connected components and a
        /// distance transform over the whole raster, each needing the finished
        /// grid. Spreading it would mean holding a half-labelled grid across
        /// frames, which is a lot of machinery for a cost paid once while the
        /// ship is stopped.
        public static float WorstSolveMs { get; private set; }

        public static void ResetSurveyCost()
        {
            WorstSurveyFrameMs = 0f;
            SurveyBands = 0;
            SurveyBandsOverBudget = 0;
            surveyBandTotalMs = 0f;
            WorstSolveMs = 0f;
        }

        /// True while the ground here is being looked at but has not answered.
        public static bool Surveying(Island isle) => isle != null && surveying.Contains(isle);

        /// How far out to look for a camp site.
        ///
        /// NOT `HarbourSite.SearchRadiusFor`, which home uses: that one is
        /// sized to find a HARBOUR, so it reaches well past the island to take
        /// in the water and the neighbouring shores, and on a big island it
        /// hits its 900 m ceiling. A camp only cares about the island's own
        /// ground, and the difference is twenty-five times the raster.
        ///
        /// Home is deliberately left on the harbour radius so this change
        /// cannot move the village.
        public static float SettlementRadiusFor(float meanRadius)
            => Mathf.Clamp(meanRadius * 1.35f + 40f, 120f, 420f);

        static System.Collections.IEnumerator Survey(Island isle)
        {
            // Everything Unity-side is read up front: the rest of this runs
            // across frames, and a transform read later would be a different
            // question than the one asked.
            Vector3 centre = isle.transform.position;
            float searchRadius = SettlementRadiusFor(isle.Radius);
            float viewHalfWidth = Rules.viewHalfWidth > 0.01f ? Rules.viewHalfWidth : Dock.ViewHalfWidth;
            var height = Rules.height;
            float surveyFloor = Rules.surveyFloor, buildFloor = Rules.buildFloor;

            const float Cell = 4f;
            int n = Terrain.SettlementSite.Grid(searchRadius, Cell);
            float x0 = centre.x - searchRadius, z0 = centre.z - searchRadius;
            var h = new float[n * n];

            // **The band size is measured, not chosen.**
            //
            // Two guesses at the height function's cost were wrong in opposite
            // directions -- 13 us a sample from one reading, 0.7 us from
            // another, and the truth moves with where you sample, because the
            // erosion fBm does different work over ocean and over land. A
            // constant tuned against either would be wrong on the other, and
            // wrong again on a phone. So: time the first row, then size the
            // band from what it actually cost.
            const float TargetMs = 4f;
            var clock = new System.Diagnostics.Stopwatch();
            int rowsPerFrame = 1;
            int lastYieldRow = -1;
            float rowMsAvg = -1f;
            float worst = 0f; int frames = 1;

            clock.Restart();
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                    h[j * n + i] = height(x0 + i * Cell, z0 + j * Cell);

                if (j == 0)
                {
                    float rowMs = Mathf.Max(0.001f, (float)clock.Elapsed.TotalMilliseconds);
                    rowMsAvg = rowMs;
                    rowsPerFrame = Mathf.Clamp(Mathf.FloorToInt(TargetMs / rowMs),
                        1, Mathf.Max(1, n / 4));
                }

                if (j - lastYieldRow >= rowsPerFrame)
                {
                    float ms = (float)clock.Elapsed.TotalMilliseconds;
                    if (ms > worst) worst = ms;
                    SurveyBands++;
                    surveyBandTotalMs += ms;
                    if (ms > BandBudgetMs) SurveyBandsOverBudget++;

                    // **Re-size the band every time — but SLOWLY, and off a
                    // smoothed cost.**
                    //
                    // Calibrating off row 0 alone assumes the first row is
                    // representative and it is not: the height function does
                    // different work over ocean than over land, and a world
                    // with twice the islands is a busier frame than the one
                    // row 0 was timed in. Shrinking the islands 1/1000 -> 1/700
                    // took the worst band from under 10 ms to 14.9.
                    //
                    // The first attempt at a fix made it WORSE, at 34.6 ms, and
                    // the reason is worth keeping: the band time is a wall
                    // clock, so a slice the OS did not interrupt reads fast,
                    // which sized the next band bigger, which read slower...
                    // **An undamped control loop on a noisy measurement
                    // oscillates instead of settling.** So: an exponential
                    // average of the per-row cost, and the band may change by
                    // at most half again per step.
                    int rows = Mathf.Max(1, j - lastYieldRow);
                    float perRow = Mathf.Max(0.0005f, ms / rows);
                    rowMsAvg = rowMsAvg <= 0f ? perRow : Mathf.Lerp(rowMsAvg, perRow, 0.35f);

                    int want = Mathf.FloorToInt(TargetMs / rowMsAvg);
                    want = Mathf.Clamp(want,
                        Mathf.Max(1, Mathf.FloorToInt(rowsPerFrame / 1.5f)),
                        Mathf.Max(1, Mathf.CeilToInt(rowsPerFrame * 1.5f)));
                    rowsPerFrame = Mathf.Clamp(want, 1, Mathf.Max(1, n / 4));

                    lastYieldRow = j;
                    yield return null;
                    frames++;
                    clock.Restart();
                }
                // The island can be destroyed mid-survey -- play mode ending,
                // or a world rebuild. Answering about a place that is gone is
                // worse than not answering.
                if (isle == null) { surveying.Remove(isle); yield break; }
            }

            var flat = Terrain.SettlementSite.Solve(h, n, x0, z0, centre, height,
                Cell, surveyFloor, default, 0f);
            // The analysis pass — flood, label, distance transform — runs in
            // ONE frame, and it is not a sampling band. Counted separately or
            // the report contradicts itself: it showed "0 bands over 10 ms"
            // beside "worst 12.9 ms", because the worst was this and it was
            // never a band at all. It is a real single-frame cost and the
            // player could feel it, so it gets its own number rather than
            // hiding inside a max.
            float tail = (float)clock.Elapsed.TotalMilliseconds;
            if (tail > worst) worst = tail;
            if (tail > WorstSolveMs) WorstSolveMs = tail;

            LastSurveyWorstFrameMs = worst;
            LastSurveyFrames = frames;
            if (worst > WorstSurveyFrameMs) WorstSurveyFrameMs = worst;

            surveying.Remove(isle);
            if (isle == null) yield break;
            if (!flat.found) { refused.Add(isle); yield break; }
            if (Of(isle) != null) yield break;      // something beat us to it

            var settlement = isle.gameObject.AddComponent<Settlement>();
            settlement.Configure(flat);
            var outpost = isle.gameObject.AddComponent<Outpost>();
            outpost.Configure(settlement, height, buildFloor, viewHalfWidth);
        }

        /// Keep something out of the way -- the beacon, the head of the pier.
        /// Reserved before anything is built, so the first storehouse is not
        /// raised on top of them.
        public void Reserve(Vector3 point, float radius)
        {
            reserved.Add(new Vector4(point.x, point.y, point.z, radius));
        }

        /// True where the scenery must not put a tree. Flat distance only --
        /// the clearing is a disc on the map, and the trees it excludes stand
        /// on whatever height the ground has there.
        ///
        /// **Nothing bakes with this any more (2026-09-23).** Kevin: *"do
        /// away with the area of the camp fire"* -- the starting island grows
        /// its wood through the survey's clearing like every other island,
        /// and a town's ground is cleared plot by plot by the hands (the
        /// CLEAR phase). Kept as the question "is this inside the survey's
        /// disc" for the probes that still ask it.
        public bool KeepOut(float x, float z)
        {
            float dx = x - ClearingCentre.x, dz = z - ClearingCentre.z;
            return dx * dx + dz * dz < ClearingRadius * ClearingRadius;
        }

        /// Put the plan up. Null if there is nowhere in the clearing left to
        /// stand it -- the caller pays only if this returns something.
        public Building Raise(BuildPlan plan)
        {
            if (!Sited) return null;
            // A pier has no "somewhere in the clearing": it stands where the
            // beach meets water deep enough, and only `SnapPier` knows where.
            if (plan.kind == BuildKind.Pier) return null;

            float len = plan.footprint.x, wid = plan.footprint.y;
            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            float room = ClearingRadius - halfDiag - 1.5f;
            if (room <= 0f) return null;

            // Golden-angle spiral out from the middle: the candidates come in
            // roughly increasing distance from the centre, so the settlement
            // fills from the inside out and reads as one place rather than a
            // ring of sheds.
            const int Tries = 220;
            const float Golden = 2.39996323f;
            for (int i = 0; i < Tries; i++)
            {
                float t = (i + 0.5f) / Tries;
                float r = room * Mathf.Sqrt(t);
                float a = i * Golden;
                // Round the CAMP, not the survey's clearing (2026-09-23): the
                // town centre is wherever the player put it, and this spiral
                // is the fallback for a building whose saved spot the ground
                // no longer takes -- it must land in the town, not in a disc
                // that may be a hundred metres off.
                Vector3 p = CampCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                if (!Clear(p, halfDiag)) continue;

                Vector3 toCentre = CampCentre - p;
                toCentre.y = 0f;
                // Door toward the middle of the clearing. A building whose
                // back is to the village is the tell that nobody chose where
                // it went. The plan's ridge runs along local X and the door is
                // in a gable end, so local -X is what has to face in.
                Quaternion facing = toCentre.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(toCentre.normalized, Vector3.up) * Quaternion.Euler(0f, 90f, 0f)
                    : Quaternion.identity;

                if (!Corners(p, facing, len, wid, out float lo, out float hi)) continue;

                p.y = hi;
                var go = BuildingFactory.Raise(plan, transform, p, facing, hi - lo);
                var b = go.GetComponent<Building>();
                built.Add(b);
                // Kevin, 2026-09-22: the grass was growing through the huts.
                Terrain.SceneryGround.ClearFootprintNear(p, facing, plan.footprint, 1f);
                var res = new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f);
                reserved.Add(res);
                buildingReservations.Add(res);
                // The spiral chose the spot; the save must not let it choose
                // again. See `OutpostLedger.raised`.
                if (ledger != null) ledger.RecordRaised(plan.id, p, facing.eulerAngles.y);
                AfterRaised(plan, b);
                return b;
            }
            return null;
        }

        /// Put the plan up AT A CHOSEN POINT, which is what the player does
        /// when they site a camp.
        ///
        /// The spiral version above answers "somewhere in the clearing"; this
        /// answers "here". Same two tests, same instantiation -- the only
        /// thing that changes is who picked the spot, and that is exactly why
        /// this must not grow its own copy of either test. A ghost that goes
        /// green on one rule and a raise that refuses on another is the bug
        /// this whole shape exists to prevent.
        public Building Raise(BuildPlan plan, Vector3 at)
            => Raise(plan, at, AutoYaw(at));

        public Building Raise(BuildPlan plan, Vector3 at, float yaw)
        {
            if (!CanPlace(plan, at, yaw, out _, out float lo, out float hi)) return null;

            float len = plan.footprint.x, wid = plan.footprint.y;
            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);

            Vector3 p = at;
            p.y = hi;
            var go = BuildingFactory.Raise(plan, transform, p, facing, hi - lo);
            var b = go.GetComponent<Building>();
            built.Add(b);
            Terrain.SceneryGround.ClearFootprintNear(p, facing, plan.footprint, 1f);
            var res = new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f);
            reserved.Add(res);
            buildingReservations.Add(res);
            if (ledger != null) ledger.RecordRaised(plan.id, p, yaw,
                plan.kind == BuildKind.Pier ? plan.footprint.x : 0f);
            // The dock hook -- see `RegisterPierDock`. Only a RAISED pier
            // registers; a ghost is made by the factory, not by this method.
            if (plan.kind == BuildKind.Pier)
            {
                var pier = go.GetComponent<Pier>();
                if (pier != null) pier.Register(b);
            }
            AfterRaised(plan, b);
            return b;
        }

        /// The reservations `Raise` made, as opposed to the ones the world
        /// build handed in through `Reserve` (the beacon, the head of the
        /// pier). Tracked so `Adopt` can drop exactly those and no others.
        readonly HashSet<Vector4> buildingReservations = new HashSet<Vector4>();

        // --- a save coming back ----------------------------------------------

        /// **Install a saved ledger and make the ground agree with it.**
        ///
        /// The ledger is the outpost (D2), so restoring one is: take the rows,
        /// then draw what they describe -- every building at the spot it was
        /// raised at, the blueprint if there is one, the piles, and the wood
        /// thinned to `treesFelled`. Nothing is paid for: the wood was cut in
        /// the session that saved it.
        ///
        /// The bodies are NOT restored here. A hand row is a name; the
        /// `CrewAgent` that wears it is aboard the freshly booted ship, and
        /// `Rehome` walks each one over after this. Order matters the other
        /// way too: `TimeOfDay` must already be scrubbed to the saved clock
        /// before `CatchUp` runs at the end, or a ledger saved at day 3 and
        /// ticked from day 0 pays out three days of phantom timber.
        public bool Adopt(OutpostLedger saved, Vector3 savedCampCentre, bool savedHasCampCentre)
        {
            if (saved == null || !Sited) return false;

            // What stood here before -- home's storehouses from THIS boot,
            // or nothing -- comes down first. A save replaces; it does not
            // add, and two storehouses on one plot is what "load" would
            // otherwise mean at home.
            RetireAllBlueprints();
            foreach (var w in walls) if (w != null) Destroy(w.gameObject);
            walls.Clear();
            foreach (var old in built) if (old != null) Destroy(old.gameObject);
            built.Clear();
            reserved.RemoveAll(buildingReservations.Contains);
            buildingReservations.Clear();

            ledger = saved;
            // JsonUtility cannot say "null": a ledger with no blueprint comes
            // back with an EMPTY one, and an empty one has `needed == 0`, which
            // reads as complete, which would raise a campfire nobody sited.
            // **And a save written before the build QUEUE (2026-09-22)
            // carries its one drawing in the old single `pending` slot.**
            // `MigratePending` lifts it into `sites` as a one-element queue
            // and nulls the slot; a save written since has an empty one and
            // it does nothing. Deliberately NOT a version bump, for the same
            // reason the chart was not: `JsonUtility` leaves a field its JSON
            // does not mention at its constructed value, so an old save reads
            // back correctly instead of being refused.
            ledger.MigratePending();
            if (ledger.sites == null) ledger.sites = new List<PendingBuild>();
            ledger.sites.RemoveAll(r => r == null || string.IsNullOrEmpty(r.planId));
            if (ledger.raised == null) ledger.raised = new List<BuiltBuilding>();
            if (ledger.built == null) ledger.built = new List<string>();
            if (ledger.hands == null) ledger.hands = new List<OutpostHand>();
            if (ledger.stores == null) ledger.stores = new List<OutpostStore>();
            if (ledger.stocks == null) ledger.stocks = new List<OutpostStock>();

            hasCampCentre = savedHasCampCentre;
            if (savedHasCampCentre)
            {
                campCentre = savedCampCentre;
                if (height != null) campCentre.y = height(campCentre.x, campCentre.z);
            }
            // A path grid baked on arrival round the survey's clearing is
            // centred on the wrong place now (2026-09-23: the camp need not
            // be anywhere near the clearing). Rebuilt on the next ask.
            var grid = GetComponent<CampPath>();
            if (grid != null) grid.Invalidate();
            // The order the wood comes down in is measured from the centre,
            // and the centre just moved.
            fellOrder = null;

            // Every building at its own spot. The list is copied and cleared
            // first because `Raise` records again into it; a spot the ground
            // no longer takes (a terrain parameter moved between sessions)
            // falls back to the spiral, and says so, rather than vanishing.
            var spots = new List<BuiltBuilding>(ledger.raised);
            ledger.raised.Clear();
            foreach (var r in spots)
            {
                if (r == null || string.IsNullOrEmpty(r.planId)) continue;
                var plan = PlanFor(r.planId, r.length);
                Vector3 at = r.At;
                if (height != null) at.y = height(at.x, at.z);
                var b = Raise(plan, at, r.yaw);
                if (b == null)
                {
                    b = Raise(plan);
                    Debug.LogWarning("Outpost.Adopt: " + plan.label + " would not stand at ("
                        + r.x.ToString("F0") + "," + r.z.ToString("F0") + ") any more -- "
                        + (b != null ? "re-sited by the spiral" : "DROPPED"));
                }
            }
            // Rows that count a building nobody recorded a spot for (a probe
            // that wrote `built` by hand) still get one, from the spiral.
            var wanted = new Dictionary<string, int>();
            foreach (var id in ledger.built)
            {
                if (string.IsNullOrEmpty(id)) continue;
                wanted.TryGetValue(id, out int n);
                wanted[id] = n + 1;
            }
            foreach (var kv in wanted)
                for (int i = CountOf(kv.Key); i < kv.Value; i++)
                    if (Raise(PlanNamed(kv.Key)) == null) break;

            // **Every segment back on its own two posts.** Before
            // `CatchUp`, because `EnsureBlueprints` draws wall SITES and a
            // repair site wants the breached segment it is mending to
            // already be standing behind it.
            if (ledger.builtWalls == null) ledger.builtWalls = new List<BuiltWall>();
            ledger.builtWalls.RemoveAll(w => w == null);
            var savedWalls = new List<BuiltWall>(ledger.builtWalls);
            ledger.builtWalls.Clear();
            foreach (var w in savedWalls) StandWall(w);

            ledger.ceilingPer = KeepsOfEach;
            // The rest is what arrival does: blueprint, felling, piles.
            CatchUp();
            return true;
        }

        /// **Give a saved hand its body back.**
        ///
        /// `Station` is the player's act: it writes the row, and it refuses a
        /// name that already has one -- which after a load is every name. This
        /// is the other half only: the row is already here, so park the body
        /// the way `Station` would have, and change no number by doing so.
        public bool Rehome(Crew.CrewAgent hand)
        {
            if (hand == null || ledger == null) return false;
            if (HandNamed(hand.DisplayName) == null) return false;
            hand.ReturnAboard();
            hand.transform.SetParent(transform, true);
            hand.gameObject.SetActive(Watched);
            ArrangeHands();
            if (Watched) PuppetsToWork();
            return true;
        }

        /// Door toward the middle of the clearing, as the spiral does. A fire
        /// has no door, but a hut sited by hand should not have its back to
        /// the rest of the camp any more than one sited by the spiral.
        /// The facing a building gets when nobody has chosen one: door toward
        /// the middle of the camp. The player can turn it from there.
        public float AutoYaw(Vector3 at) => FacingAt(at).eulerAngles.y;

        Quaternion FacingAt(Vector3 at)
        {
            Vector3 toCentre = CampCentre - at;
            toCentre.y = 0f;
            return toCentre.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toCentre.normalized, Vector3.up)
                    * Quaternion.Euler(0f, 90f, 0f)
                : Quaternion.identity;
        }

        /// **Can this plan stand here?** The one test the ghost and the raise
        /// both ask.
        ///
        /// Deliberately does NOT test the clearing. The survey's clearing says
        /// where a camp COULD go if nobody chose; once the player is choosing,
        /// the constraint that matters is the one they can see -- the ring
        /// round the ship -- and that belongs to the siting interface, not to
        /// the ground. What the ground still gets to refuse is beach, water,
        /// a slope nothing will stand on, and somewhere already occupied.
        public bool CanPlace(BuildPlan plan, Vector3 at, out string why)
            => CanPlace(plan, at, AutoYaw(at), out why, out _, out _);

        public bool CanPlace(BuildPlan plan, Vector3 at, float yaw, out string why)
            => CanPlace(plan, at, yaw, out why, out _, out _);

        public bool CanPlace(BuildPlan plan, Vector3 at, float yaw, out string why,
            out float lo, out float hi)
        {
            lo = hi = 0f;
            why = "";
            if (!Sited) { why = "this ground was never surveyed"; return false; }

            // A pier is half over water by design, so the shore, corner and
            // beach tests below would all refuse it. It has its own.
            if (plan.kind == BuildKind.Pier) return CanPlacePier(plan, at, yaw, out why, out lo, out hi);

            // On this island at all. The height test below rejects open water
            // on its own, but it cannot tell the player WHY, and "out past the
            // shore" is the mistake a top-down view makes easiest to make.
            if (Island != null && Island.HasProfile)
            {
                float d = Island.FlatDistance(at, Island.transform.position);
                if (d > Island.RadiusToward(at))
                {
                    why = "that is past the shore";
                    return false;
                }
            }

            float len = plan.footprint.x, wid = plan.footprint.y;
            if (!Corners(at, Quaternion.Euler(0f, yaw, 0f), len, wid, out lo, out hi,
                    out string footing))
            {
                // The corner test knows which of the two it tripped on and
                // by how much; guessing from the CENTRE's height (what this
                // used to do) reported "too steep" for a hut whose downhill
                // corner was in the sand.
                why = footing;
                return false;
            }

            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            if (!Clear(at, halfDiag, out string blocked)) { why = blocked; return false; }
            return true;
        }

        /// **Where a pier would go if the player points HERE.**
        ///
        /// The player chooses a stretch of beach and nothing else: the pier
        /// walks itself down to the waterline, turns to face straight out to
        /// sea, and runs out until there is `PierBerthDepth` of water under
        /// its end. R does not turn it and the tap does not fix its length.
        ///
        /// Walk: from the picked point, downhill along the height field until
        /// the ground crosses mean water (uphill instead if the pick was
        /// already wet). The heading is the downhill direction at that
        /// crossing, measured on a 4 m stencil so a ripple in the sand does
        /// not swing a 14 m pier; where the beach is too flat to have a
        /// downhill, "away from the island's middle" stands in. The land end
        /// is `PierLandIn` metres back up the beach from the crossing; the
        /// sea end is the first whole metre from `PierLength` to
        /// `PierLongest` with the berth depth under it.
        ///
        /// Returns the pier's CENTRE (y = deck height), heading as the yaw
        /// `Raise` takes, and the length as a plan (`WithLength`); or false
        /// with a reason, and the best guess it had so a ghost can still be
        /// drawn red where the player is pointing.
        public bool SnapPier(Vector3 picked, out Vector3 centre, out float yaw,
            out BuildPlan plan, out string why)
        {
            plan = BuildPlans.Pier;
            yaw = 0f;
            centre = picked;
            centre.y = BuildPlans.PierDeck;
            why = "";
            if (!Sited) { why = "this ground was never surveyed"; return false; }

            // Which way is the sea. Downhill, or failing that radially out.
            Vector3 seaward = Downhill(picked, 2f);
            if (seaward.sqrMagnitude < 1e-6f)
            {
                seaward = Island != null ? picked - Island.transform.position : Vector3.forward;
                seaward.y = 0f;
                if (seaward.sqrMagnitude < 1e-6f) seaward = Vector3.forward;
                seaward.Normalize();
            }

            // Find the waterline along that direction.
            const float Step = 0.5f, Reach = 60f;
            bool wet = height(picked.x, picked.z) < 0f;
            Vector3 dir = wet ? -seaward : seaward;
            Vector3 a = picked, b = picked;
            bool crossed = false;
            for (float d = Step; d <= Reach; d += Step)
            {
                b = picked + dir * d;
                if ((height(b.x, b.z) < 0f) != wet) { crossed = true; break; }
                a = b;
            }
            if (!crossed)
            {
                why = wet ? "that is open water -- point at a beach"
                          : "no shore within reach of that spot";
                return false;
            }
            for (int i = 0; i < 6; i++)             // bisect to ~1 cm
            {
                Vector3 m = (a + b) * 0.5f;
                if ((height(m.x, m.z) < 0f) == wet) a = m; else b = m;
            }
            Vector3 shore = (a + b) * 0.5f;

            // Heading: straight out from the beach at the crossing.
            Vector3 heading = Downhill(shore, 4f);
            if (heading.sqrMagnitude < 1e-6f) heading = seaward;
            if (Vector3.Dot(heading, seaward) < 0f) heading = -heading;   // never back up the beach

            Vector3 land = shore - heading * PierLandIn;
            float landH = height(land.x, land.z);
            if (landH <= 0.05f) { why = "there is no beach here to land a pier on"; return false; }
            if ((landH - height(shore.x, shore.z)) / PierLandIn > PierLandSlope)
            { why = "the beach is too steep for a pier"; return false; }

            // Run out to water deep enough.
            float length = -1f;
            for (float L = BuildPlans.PierLength; L <= BuildPlans.PierLongest + 1e-3f; L += 1f)
            {
                Vector3 e = land + heading * L;
                if (-height(e.x, e.z) >= BuildPlans.PierBerthDepth) { length = L; break; }
            }
            yaw = YawAlong(heading);
            if (length < 0f)
            {
                plan = plan.WithLength(BuildPlans.PierLongest);
                centre = land + heading * (BuildPlans.PierLongest * 0.5f);
                centre.y = BuildPlans.PierDeck;
                why = "the water here is too shallow for a pier";
                return false;
            }
            plan = plan.WithLength(length);
            centre = land + heading * (length * 0.5f);
            centre.y = BuildPlans.PierDeck;
            return true;
        }

        /// Metres of pier that stand on the sand, back from the waterline.
        public const float PierLandIn = 3f;
        /// Steepest beach (rise per metre) a pier's land end will take.
        /// Far looser than a hut's: it is a ramp, not a floor.
        public const float PierLandSlope = 0.5f;

        /// Downhill direction of the height field at `p`, flat, unit length;
        /// zero where the ground is level to within a millimetre per metre.
        Vector3 Downhill(Vector3 p, float stencil)
        {
            float gx = height(p.x + stencil, p.z) - height(p.x - stencil, p.z);
            float gz = height(p.x, p.z + stencil) - height(p.x, p.z - stencil);
            var g = new Vector3(gx, 0f, gz) / (2f * stencil);
            return g.magnitude < 1e-3f ? Vector3.zero : -g.normalized;
        }

        /// The yaw whose facing puts local +X (the ridge, land to sea) along
        /// `heading`: `Quaternion.Euler(0, yaw, 0) * Vector3.right`.
        static float YawAlong(Vector3 heading)
            => Mathf.Atan2(-heading.z, heading.x) * Mathf.Rad2Deg;

        /// **Can a pier of `plan.footprint.x` metres stand at this centre and
        /// yaw?** The same question `SnapPier` has just answered, asked again
        /// of the answer -- so a saved pier, a blueprint and a fresh siting
        /// all go through one test, and a ghost that goes green here is a
        /// pier that `Raise` will stand.
        bool CanPlacePier(BuildPlan plan, Vector3 at, float yaw, out string why,
            out float lo, out float hi)
        {
            // Deck height is world, not ground: `Raise` puts the root at `hi`
            // and the factory hangs the posts down from there. Zero footing.
            lo = hi = BuildPlans.PierDeck;
            why = "";
            float len = plan.footprint.x, wid = plan.footprint.y;
            Vector3 heading = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 land = at - heading * (len * 0.5f);
            Vector3 sea = at + heading * (len * 0.5f);
            if (height(land.x, land.z) <= 0.05f)
            { why = "there is no beach here to land a pier on"; return false; }
            if (-height(sea.x, sea.z) < BuildPlans.PierBerthDepth)
            { why = "the water here is too shallow for a pier"; return false; }
            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            if (!Clear(at, halfDiag, out string blockedP)) { why = blockedP; return false; }
            return true;
        }

        /// Nothing already claimed within reach of this footprint.
        /// **The one queued site `Clear` does not count as occupied.**
        ///
        /// Two callers need it and both are asking about a drawing that is
        /// already in the queue: the ghost while the player MOVES one (it
        /// must not be refused by the spot it is standing on), and
        /// `EnsureBlueprints` measuring the footing for a row it is about to
        /// draw. Set it, ask, clear it -- it is never left set across a
        /// frame, because a stale one is a hole in the overlap rule.
        public PendingBuild IgnoreSite { get; set; }

        bool Clear(Vector3 p, float halfDiag) => Clear(p, halfDiag, out _);

        bool Clear(Vector3 p, float halfDiag, out string why)
        {
            if (!ClearOfReserved(p, halfDiag, out why)) return false;
            return ClearOfPlans(p, halfDiag, out why);
        }

        /// The hand-placed keep-outs and the ground the standing buildings
        /// reserved. Split out because a WALL has to ask this one without
        /// asking the other (`CanPlaceWall` tests the queue and the other
        /// segments by LINE, which a disc cannot do).
        bool ClearOfReserved(Vector3 p, float halfDiag, out string why)
        {
            why = "";
            foreach (var r in reserved)
            {
                float dx = p.x - r.x, dz = p.z - r.z;
                float need = halfDiag + r.w;
                if (dx * dx + dz * dz < need * need)
                {
                    // **Say which kind of thing.** A reservation is either
                    // a building that stands here (`buildingReservations`)
                    // or a hand-placed keep-out -- the head of the pier.
                    // "Something already stands there" over an invisible
                    // keep-out is the refusal Kevin could not read.
                    why = buildingReservations.Contains(r)
                        ? "something already stands there"
                        : $"that is inside the harbour's keep-out ({r.w:F0} m)";
                    return false;
                }
            }
            return true;
        }

        bool ClearOfPlans(Vector3 p, float halfDiag, out string why)
        {
            why = "";

            // **And every drawing already queued, 2026-09-22.** A blueprint
            // is not in `reserved` -- it is a promise, and a promise that
            // reserved ground would go on reserving it after the player
            // cancelled -- so the queue is tested here instead. Without this
            // the second blueprint of a camp can be sited inside the first,
            // and the raise that came later would be refused on ground the
            // ghost went green on. `Outpost.RaiseRow` takes a row OUT of the
            // queue before raising it, so nothing refuses itself.
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                {
                    if (row == null || string.IsNullOrEmpty(row.planId)) continue;
                    if (row == IgnoreSite) continue;
                    var plan = PlanFor(row.planId, row.length);
                    float rl = plan.footprint.x, rw = plan.footprint.y;
                    float rHalf = 0.5f * Mathf.Sqrt(rl * rl + rw * rw) + spacing * 0.5f;
                    float dx = p.x - row.x, dz = p.z - row.z;
                    float need = halfDiag + rHalf;
                    if (row.isWall)
                    {
                        // A wall row is a LINE, not a disc round its
                        // midpoint: measuring a 12 m segment as a circle
                        // would refuse a hut six metres off either end of
                        // it and allow one standing on the middle.
                        if (WallSegment.FlatDistance(row.postA, row.postB, p)
                            < halfDiag + WallClearance)
                        {
                            why = "the wall going up there is in the way";
                            return false;
                        }
                        continue;
                    }
                    if (dx * dx + dz * dz < need * need)
                    {
                        why = $"too close to the {plan.label} going up there";
                        return false;
                    }
                }

            // **And every segment that STANDS.** A raised wall is not in
            // `reserved` either -- it has no sensible centre and no
            // sensible radius -- so the line is tested directly.
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                if (w.FlatDistanceTo(p) < halfDiag + WallClearance)
                {
                    why = w.IsGate ? "the gate is in the way" : "the wall is in the way";
                    return false;
                }
            }
            return true;
        }

        /// Metres a building keeps off a wall line. A palisade is a thin
        /// thing but nobody builds a hut flush against one, and the footing
        /// of a segment on a slope reaches a little either side of the line.
        public const float WallClearance = 1.6f;


        // === WALLS AND GATES (Phase 1, 2026-09-23) ==========================
        //
        // `docs/PLAN-fortress-harbour.md`, and Kevin's own siting design
        // (D5, "connect the dots"). The whole of the wall's economy is the
        // build queue it already had: a segment is a `PendingBuild` with
        // two posts on it, stocked and raised by the same code that stocks
        // and raises a hut. What is new here is only the three things a
        // rectangle on a plot never had to answer -- where a post snaps,
        // whether a LINE can stand, and what happens to the pathing grid
        // when it does.

        /// **The snap grid for a post, metres, and it is not a taste
        /// decision.** A segment's blocked cells are rasterised into
        /// `CampPath`, whose cells are exactly this size -- so a post that
        /// did not land on a cell centre would put the wall's obstacle a
        /// metre from the wall. If one of these two numbers ever changes,
        /// both change.
        public const float WallPostStep = 2f;

        /// **The longest one segment may be.** Longer runs are split by the
        /// siting tool into several sites, which is the point: a segment is
        /// one site in the queue, so a sixty-metre wall is five things the
        /// hands can be hauling to at once rather than one thing they
        /// finish in a week.
        public const float MaxWallSegment = 12f;

        /// Shorter than this is a tap, not a drag.
        public const float MinWallSegment = 0.5f;

        /// Every RAISED segment on this island, breached or not. Kept apart
        /// from `built` on purpose: `CountOf`, `Demolish`, the store
        /// ceiling and the assign menus are all about buildings with rooms
        /// in them, and a hundred metres of fence in that list would answer
        /// every one of those questions wrongly.
        readonly List<WallSegment> walls = new List<WallSegment>();

        public IReadOnlyList<WallSegment> Walls => walls;

        /// **HOOK for the wall siting tool (one line, the UI's to wire).**
        /// The build page lists the palisade, but the tool that draws a run
        /// is `UI/WallSiting`, which this file must not know about any more
        /// than it knows about `CampSiting`. Expected wiring, beside the
        /// dock hook above:
        ///
        ///   Outpost.BeginWallSiting = camp => WallSiting.Begin(camp);
        ///
        /// Null means the tool is not installed, and the build page's
        /// palisade row greys itself out rather than throwing.
        public static System.Action<Outpost> BeginWallSiting;

        /// **Snap a point to the wall lattice.** XZ to the 2 m step, Y to
        /// the ground there. The lattice is the WORLD's, not the camp's, so
        /// two runs drawn on either side of a camp meet exactly.
        public Vector3 SnapPost(Vector3 world)
        {
            float x = Mathf.Round(world.x / WallPostStep) * WallPostStep;
            float z = Mathf.Round(world.z / WallPostStep) * WallPostStep;
            return new Vector3(x, GroundAt(new Vector3(x, 0f, z)), z);
        }

        /// **Can a segment stand between these two posts?**
        ///
        /// The same shape of answer `CanPlace` gives -- true, or false with
        /// a sentence naming the rule -- because the siting tool paints the
        /// line green or red and prints exactly this string under the
        /// thumb.
        public bool CanPlaceWall(Vector3 a, Vector3 b, out string why)
        {
            why = "";
            if (!Sited) { why = "this ground was never surveyed"; return false; }

            a = SnapPost(a);
            b = SnapPost(b);
            Vector3 flat = b - a;
            flat.y = 0f;
            float len = flat.magnitude;
            if (len <= MinWallSegment) { why = "that is not a length of wall"; return false; }
            if (len > MaxWallSegment + 0.01f)
            {
                why = $"one length of wall is {MaxWallSegment:F0} m at most";
                return false;
            }

            // Both posts on the island, not out past the shore. Same test
            // and the same words `CanPlace` uses.
            if (Island != null && Island.HasProfile)
                foreach (var post in new[] { a, b })
                    if (Island.FlatDistance(post, Island.transform.position)
                        > Island.RadiusToward(post))
                    { why = "that is past the shore"; return false; }

            // **The beach rule, per post** -- `Corners`' rule, applied to
            // the two ends instead of to four corners. A palisade with its
            // foot in the surf is a palisade the sea takes.
            if (a.y < minHeight || b.y < minHeight)
            {
                why = "one end of it is down on the beach";
                return false;
            }

            // **The slope ALONG the run**, which is the only slope a line
            // has. Same limit a building stands on, because a wall on
            // ground steeper than that has daylight under half of it.
            float rise = Mathf.Abs(b.y - a.y);
            if (rise / len > BuildSlope)
            {
                float deg = Mathf.Atan(rise / len) * Mathf.Rad2Deg;
                why = $"too steep -- {deg:F0}° along it, and {BuildSlopeDegrees:F0}° is the limit";
                return false;
            }

            // Not through anything that stands.
            for (int i = 0; i < built.Count; i++)
            {
                var bld = built[i];
                if (bld == null) continue;
                Vector2 fp = bld.Footprint;
                float halfDiag = 0.5f * Mathf.Sqrt(fp.x * fp.x + fp.y * fp.y);
                if (WallSegment.FlatDistance(a, b, bld.transform.position)
                    < halfDiag + WallClearance)
                { why = $"the {bld.Label} is in the way"; return false; }
            }

            // Nor through anything queued.
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                {
                    if (row == null || string.IsNullOrEmpty(row.planId)) continue;
                    if (row == IgnoreSite) continue;
                    if (row.isWall)
                    {
                        if (TooCloseLines(a, b, row.postA, row.postB))
                        { why = "there is already a wall going up there"; return false; }
                        continue;
                    }
                    var plan = PlanFor(row.planId, row.length);
                    float rl = plan.footprint.x, rw = plan.footprint.y;
                    float rHalf = 0.5f * Mathf.Sqrt(rl * rl + rw * rw);
                    if (WallSegment.FlatDistance(a, b, row.At) < rHalf + WallClearance)
                    { why = $"the {plan.label} going up there is in the way"; return false; }
                }

            // Nor through another segment. Sharing a POST is not crossing:
            // that is what a run is (D5, "a post is shared between
            // adjoining segments").
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                if (TooCloseLines(a, b, w.A, w.B))
                { why = w.IsGate ? "the gate is in the way" : "there is already a wall there"; return false; }
            }

            // And not on the fire itself or anything else that reserved
            // ground -- the head of the pier, the beacon. **Only the
            // reservations**: the rest of `Clear` tests the walls and the
            // queue as discs, and it has already been done properly, by
            // line, above.
            //
            // **By LINE, not a disc round the midpoint (2026-09-23).** The
            // old call handed `ClearOfReserved` a disc of radius len/2 on
            // the segment's middle, so a 12 m length refused every hut and
            // the fire within ~9 m of its MIDDLE even when the line itself
            // passed well clear -- which is exactly where a camp wall goes.
            // A tool artefact, not terrain: the line is now measured.
            if (!LineClearOfReserved(a, b, out string blocked))
            { why = blocked; return false; }

            return true;
        }

        /// `ClearOfReserved`'s rule and words, asked of a LINE: does the
        /// segment pass inside any reserved disc?
        bool LineClearOfReserved(Vector3 a, Vector3 b, out string why)
        {
            why = "";
            foreach (var r in reserved)
            {
                var c = new Vector3(r.x, r.y, r.z);
                if (WallSegment.FlatDistance(a, b, c) >= r.w + 0.2f) continue;
                why = buildingReservations.Contains(r)
                    ? "something already stands there"
                    : $"that is inside the harbour's keep-out ({r.w:F0} m)";
                return false;
            }
            return true;
        }

        /// Do two wall lines foul each other? Endpoints that coincide are a
        /// shared post and are allowed; anything else inside a post step is
        /// two walls in one place.
        static bool TooCloseLines(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            // **ROOT CAUSE of "the third post stems from the first one, and
            // red lines" (Kevin, iPhone, 2026-09-23).** The old version
            // chose each segment's "far end" by asking only whether a0 met
            // b0. In a run the shared post is almost never that pair: the
            // next segment B->C meets the queued A->B at a0 == b1, so the
            // "far end" it measured was B itself, at distance 0, and EVERY
            // segment after the first was refused as "there is already a
            // wall going up there". The preview went red after each ✓, and
            // a drag long enough to split had its second piece refused at
            // `Confirm`, which left post A where it was -- the next line
            // grew out of the first post. Closing the ring (a1 == b0) was
            // refused the same way. Now the shared post is found on BOTH
            // segments, whichever ends they are, and flat (posts are on the
            // XZ lattice; the height is not part of "the same post").
            bool a0s = SamePost(a0, b0) || SamePost(a0, b1);
            bool a1s = SamePost(a1, b0) || SamePost(a1, b1);
            bool b0s = SamePost(b0, a0) || SamePost(b0, a1);
            if (!a0s && !a1s)
                return WallSegment.FlatDistance(a0, a1, b0, b1) < WallPostStep * 0.5f;
            // Both ends shared: the same segment drawn again (either way).
            if (a0s && a1s) return true;
            // Sharing one post, so the distance is zero by construction.
            // What is left to refuse is a segment doubled back ON the other
            // one, which shows up as a far end close to the other line.
            Vector3 aFar = a0s ? a1 : a0;
            Vector3 bFar = b0s ? b1 : b0;
            float far = Mathf.Min(
                WallSegment.FlatDistance(b0, b1, aFar),
                WallSegment.FlatDistance(a0, a1, bFar));
            return far < WallPostStep * 0.5f;
        }

        /// Two posts on one lattice point. Flat: posts snap to the 2 m XZ
        /// grid, so within a third of a metre is the same post.
        static bool SamePost(Vector3 p, Vector3 q)
        {
            float dx = p.x - q.x, dz = p.z - q.z;
            return dx * dx + dz * dz < 0.1f;
        }

        /// **Queue one segment.** Null with a reason if the ground refuses
        /// it; otherwise the row is in the queue at once and the hands can
        /// start hauling to it while the player goes on drawing (D5).
        public PendingBuild SiteWall(Vector3 a, Vector3 b, out string why)
        {
            if (ledger == null) { why = "this ground was never surveyed"; return null; }
            ledger.MigratePending();
            // The belt, same as a plan's own siting: the sheet already
            // hides a locked wall.
            if (!ledger.PlanUnlocked(BuildPlans.Palisade.id))
            { why = ledger.PlanLockReason(BuildPlans.Palisade.id); return null; }
            a = SnapPost(a);
            b = SnapPost(b);
            if (!CanPlaceWall(a, b, out why)) return null;
            // A FRESH line clears its band first (2026-09-23). Gates and
            // repairs go through `QueueWallRow` without this: they stand on
            // a line that is already raised and already owns its ground.
            var row = QueueWallRow(BuildPlans.Palisade, a, b, true);
            why = "";
            return row;
        }

        /// The row itself, shared by siting, the gate and the repair. It
        /// does NOT test the ground: a gate and a repair are both on a line
        /// that already proved itself.
        PendingBuild QueueWallRow(BuildPlan plan, Vector3 a, Vector3 b, bool fresh = false)
        {
            Vector3 mid = 0.5f * (a + b);
            float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            Vector3 run = b - a;
            run.y = 0f;
            var row = new PendingBuild
            {
                planId = plan.id,
                isWall = true,
                postA = a,
                postB = b,
                x = mid.x,
                z = mid.z,
                yaw = run.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(run.normalized, Vector3.up).eulerAngles.y : 0f,
                length = len,
                // **The palisade is priced by the metre (D1), the gate is
                // priced flat.** `BuildPlan.cost` is zero on the palisade
                // precisely so nothing can price a wall by the plan.
                needed = plan.id == BuildPlans.Palisade.id
                    ? BuildPlans.PalisadeCost(len) : Mathf.Max(0, plan.cost),
                stoneNeeded = Mathf.Max(0, plan.stoneCost),
                brickNeeded = Mathf.Max(0, plan.brickCost),
                phased = true,
            };
            if (fresh) TakeFootprint(row);
            ledger.sites.Add(row);
            if (fresh) { SyncFelling(); GatherSync.Sync(this); }
            // Idle hands take up a fresh segment on their own, 2026-09-23
            // (`OutpostLedger.EnlistFree`).
            if (ledger.EnlistFree() > 0 && Watched) PuppetsToWork();
            EnsureBlueprints();
            if (ledger.ReadyToRaise) FinishReady();
            return row;
        }

        /// **Make this segment a gate.** The segment goes on standing while
        /// the gate is stocked (D5) and is swapped for it when the site is
        /// raised -- so a camp does not open a hole in its own wall the
        /// moment the player decides it wants a door.
        public PendingBuild MakeGate(WallSegment seg)
        {
            if (seg == null || ledger == null || seg.IsGate) return null;
            ledger.MigratePending();
            foreach (var row in ledger.sites)
                if (row != null && row.isWall && row.planId == BuildPlans.Gate.id
                    && (row.postA - seg.A).sqrMagnitude < 0.05f
                    && (row.postB - seg.B).sqrMagnitude < 0.05f) return row;
            return QueueWallRow(BuildPlans.Gate, seg.A, seg.B);
        }

        /// **A breach is a repair site.** Queued once per breach (the
        /// segment only crosses zero once), on the same posts, and stocked
        /// and built like anything else -- which is the acceptance test's
        /// last step: *"the hands rebuild it after"*.
        public PendingBuild QueueRepair(WallSegment seg)
        {
            if (seg == null || ledger == null) return null;
            ledger.MigratePending();
            foreach (var row in ledger.sites)
                if (row != null && row.isWall
                    && (row.postA - seg.A).sqrMagnitude < 0.05f
                    && (row.postB - seg.B).sqrMagnitude < 0.05f) return row;
            // A breached gate is repaired as the gate it is; anything else
            // is a palisade again.
            return QueueWallRow(seg.IsGate ? BuildPlans.Gate : BuildPlans.Palisade,
                seg.A, seg.B);
        }

        /// The segment on these posts, or null. Posts are on the 2 m
        /// lattice, so "the same posts" is an exact question.
        public WallSegment WallOn(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                if ((w.A - a).sqrMagnitude < 0.05f && (w.B - b).sqrMagnitude < 0.05f) return w;
                if ((w.A - b).sqrMagnitude < 0.05f && (w.B - a).sqrMagnitude < 0.05f) return w;
            }
            return null;
        }

        /// The standing segment nearest this point within `within` metres,
        /// or null. What a raid party asks when it wants something to break.
        public WallSegment NearestWall(Vector3 to, float within = 400f)
        {
            WallSegment best = null;
            float bestD = within;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || w.Breached) continue;
                float d = w.FlatDistanceTo(to);
                if (d >= bestD) continue;
                bestD = d;
                best = w;
            }
            return best;
        }

        /// **Stand a queued wall row up.** Mends what is already there
        /// where there is something (a repair), swaps a wall for a gate
        /// where the row is a gate, and otherwise puts a new segment on the
        /// ground. Null means the ground refused it and the row goes back
        /// in the queue.
        WallSegment RaiseWall(PendingBuild row)
        {
            if (row == null || !row.isWall) return null;
            Vector3 a = SnapPost(row.postA), b = SnapPost(row.postB);
            bool gate = row.planId == BuildPlans.Gate.id;

            var existing = WallOn(a, b);
            if (existing != null)
            {
                if (gate && !existing.IsGate)
                {
                    var go = BuildingFactory.RaiseWall(transform, a, b, true,
                        out var gWhole, out var gBroken);
                    // The visuals live under the segment that is already
                    // standing; the factory root is only their carrier.
                    gWhole.SetParent(existing.transform, false);
                    gBroken.SetParent(existing.transform, false);
                    gWhole.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                    gBroken.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                    Destroy(go);
                    existing.BecomeGate(gWhole, gBroken);
                }
                else existing.Mend();
                ClearGroundAlong(a, b);
                return existing;
            }

            var root = BuildingFactory.RaiseWall(transform, a, b, gate,
                out var whole, out var broken);
            var seg = root.AddComponent<WallSegment>();
            seg.Configure(BuildPlans.Named(row.planId));
            float maxHp = WallSegment.HpFor(Vector3.Distance(
                new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)), gate);
            var bw = new BuiltWall
            {
                ax = a.x, az = a.z, bx = b.x, bz = b.z,
                isGate = gate, hp = maxHp, maxHp = maxHp,
            };
            ledger.builtWalls.Add(bw);
            seg.Row = bw;
            seg.Configure(this, a, b, gate, maxHp, maxHp, whole, broken);
            walls.Add(seg);

            ClearGroundAlong(a, b);
            var map = CampPath.For(this);
            if (map != null) map.MarkWall(seg, true);
            return seg;
        }

        /// The same, from a saved row. No ledger row is written (it is the
        /// ledger row) and no autosave is fired.
        void StandWall(BuiltWall w)
        {
            if (w == null || !Sited) return;
            Vector3 a = new Vector3(w.ax, GroundAt(new Vector3(w.ax, 0f, w.az)), w.az);
            Vector3 b = new Vector3(w.bx, GroundAt(new Vector3(w.bx, 0f, w.bz)), w.bz);
            var root = BuildingFactory.RaiseWall(transform, a, b, w.isGate,
                out var whole, out var broken);
            var seg = root.AddComponent<WallSegment>();
            seg.Configure(BuildPlans.Named(w.isGate ? BuildPlans.Gate.id : BuildPlans.Palisade.id));
            float maxHp = w.maxHp > 0f ? w.maxHp : WallSegment.HpFor(
                Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)), w.isGate);
            seg.Row = w;
            w.maxHp = maxHp;
            seg.Configure(this, a, b, w.isGate, w.hp, maxHp, whole, broken);
            walls.Add(seg);
            ledger.builtWalls.Add(w);

            // A breached segment comes back breached, and a breached
            // segment blocks nobody.
            var map = CampPath.For(this);
            if (map != null && !seg.Breached) map.MarkWall(seg, true);
        }

        /// **Take the grass out from under a run.** `SceneryGround` only
        /// knows how to clear a rectangle (`ClearFootprintNear`), which is
        /// exactly what a segment is once it is turned along its own run --
        /// so the wall gets the same treatment a hut gets and nothing in
        /// the terrain code has to learn what a wall is.
        void ClearGroundAlong(Vector3 a, Vector3 b)
        {
            Vector3 run = b - a;
            run.y = 0f;
            float len = run.magnitude;
            if (len < 0.01f) return;
            var facing = Quaternion.LookRotation(run.normalized, Vector3.up)
                * Quaternion.Euler(0f, 90f, 0f);
            Terrain.SceneryGround.ClearFootprintNear(
                0.5f * (a + b), facing, new Vector2(len, 1.2f), 0.4f);
        }

        /// Forget a segment that has been torn down. The object destroys
        /// itself; this is the record.
        public void ForgetWall(WallSegment seg)
        {
            if (seg == null) return;
            walls.Remove(seg);
            if (ledger != null && ledger.builtWalls != null && seg.Row != null)
                ledger.builtWalls.Remove(seg.Row);
        }

        /// The four corners of the footprint, measured off the height field.
        /// Rejects ground that is too steep to stand a building on or low
        /// enough to be beach -- the same two tests SettlementSite used to find
        /// the patch, applied to this actual rectangle rather than to the patch
        /// as a whole.
        bool Corners(Vector3 p, Quaternion facing, float len, float wid,
            out float lo, out float hi)
            => Corners(p, facing, len, wid, out lo, out hi, out _);

        /// **The steepest ground a building will stand on, degrees.**
        ///
        /// Was `SettlementSite.BuildableSlope` (0.176, a hair under TEN
        /// degrees) -- the threshold the world survey uses to find a flat
        /// patch big enough for a whole village. Using the survey's number
        /// for a single hut is what made Kevin's "areas that look clear"
        /// refuse: this island style is sleek low hills (see the island
        /// style v2 work), and almost nothing on one is under ten degrees.
        ///
        /// The honest limit is "ground the people who live here can walk",
        /// which is `CampPath.MaxSlopeDegrees` (38). Pulled in a little
        /// from that, because a floor is not a footpath and a hut on a 38
        /// degree slope would need a storey of stilts under one corner.
        /// This still refuses a cliff: 30 degrees over an 8x5 hut's 9.4 m
        /// diagonal is a 5.4 m step, which is as far as the footing will
        /// stretch.
        public const float BuildSlopeDegrees = 30f;

        /// The same limit as a rise-over-run, which is what the corners are
        /// measured in.
        public static float BuildSlope => Mathf.Tan(BuildSlopeDegrees * Mathf.Deg2Rad);

        bool Corners(Vector3 p, Quaternion facing, float len, float wid,
            out float lo, out float hi, out string fail)
        {
            lo = float.MaxValue; hi = float.MinValue;
            fail = "";
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 c = p + facing * new Vector3(sx * len * 0.5f, 0f, sz * wid * 0.5f);
                    float h = height(c.x, c.z);
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
            if (lo < minHeight)
            {
                fail = "a corner of it is down on the beach";
                return false;
            }
            float span = Mathf.Sqrt(len * len + wid * wid);
            float slope = (hi - lo) / span;
            if (slope > BuildSlope)
            {
                float deg = Mathf.Atan(slope) * Mathf.Rad2Deg;
                fail = $"too steep -- {deg:F0}° across it, and {BuildSlopeDegrees:F0}° is the limit";
                return false;
            }
            return true;
        }
    }
}
