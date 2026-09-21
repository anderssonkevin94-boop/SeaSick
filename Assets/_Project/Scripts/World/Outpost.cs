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

        [Tooltip("What open ground keeps before anything is built. 30 at home — that is the beach, and the reason to build the first storehouse. ZERO anywhere else: an island with nobody on it keeps nothing, and the campfire is what first gives a place a ceiling at all.")]
        [SerializeField] int openCapacity = 30;

        [Tooltip("Metres between buildings -- room to walk round one, which is what a village looks like from above.")]
        [SerializeField] float spacing = 6f;

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
            // **One definition of the ceiling, and it is what stands on the
            // ground.** The ledger could have carried its own and drifted from
            // the buildings the moment a storehouse went up; instead the
            // buildings ARE the ledger's ceiling, pushed in before every tick.
            ledger.ceilingPer = KeepsOfEach;
            ledger.Tick(TimeOfDay.Seconds);

            // The arithmetic can finish a building on an island nobody is
            // looking at, so the raise cannot live in the tick -- it needs a
            // scene to put something in. It happens here instead, which is
            // called on arrival, and whose whole job is "make the world agree
            // with the ledger".
            if (ledger.ReadyToRaise) FinishPending();
            else if (ledger.Building) EnsureBlueprint();
            SyncFelling();
            // The piles beside the fire are drawn from the stores, so they
            // want to exist wherever the stores are being looked at.
            if (HasCamp) CampPiles.EnsureOn(this);
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

        /// The blueprint standing here, if one is drawn. Rebuilt from the
        /// ledger whenever the island is loaded -- never the only copy.
        BuildSite blueprint;

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
        {
            if (ledger == null) { why = "this ground was never surveyed"; return -1; }
            if (ledger.pending != null) { why = "something is already being built here"; return -1; }
            if (CountOf(plan.id) > 0) { why = $"there is already a {plan.label} here"; return -1; }
            if (!CanPlace(plan, at, yaw, out why, out float lo, out float hi)) return -1;

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
                campCentre = spot;
                hasCampCentre = true;

                // The key moves to where the player put it, and it moves NOW --
                // before any wood is counted. A ledger keyed to the survey's
                // clearing and then filled by a camp forty metres away is a
                // camp that will not be found again after a save.
                ledger.SetKey(campCentre);
            }
            ledger.pending = new PendingBuild
            {
                planId = plan.id,
                x = spot.x,
                z = spot.z,
                yaw = yaw,
                needed = Mathf.Max(0, plan.cost),
            };

            // Making camp is everybody's job. A LATER building takes whoever is
            // not already in a position: a sawyer pulled off his mill every
            // time a hut is drawn is the list undoing what the Hand just did.
            if (plan.kind == BuildKind.Fire) ledger.OrderAll(OutpostOrder.Build);
            else
                foreach (var h in ledger.hands)
                    if (h != null && h.order != OutpostOrder.Work)
                    { h.order = OutpostOrder.Build; h.target = ""; }
            EnsureBlueprint();
            // A plan that costs nothing is finished the moment it is sited.
            // Nothing does today; the dev path (`MakeCamp`) reaches the same
            // door by paying the cost outright.
            if (ledger.ReadyToRaise) FinishPending();
            why = "";
            return ledger.pending != null ? ledger.pending.needed - ledger.pending.done : 0;
        }

        /// Logs that came out of the ground the last thing built here stands
        /// on. Reported rather than returned because the felling now happens
        /// when the build FINISHES, which can be days after the player sited
        /// it and on a frame nobody asked a question on.
        public int LastClearingFelled { get; private set; }

        /// Draw the blueprint if the ledger says there is one and nothing is
        /// drawing it. Called on arrival, so a camp you sited and sailed away
        /// from is standing there half built when you get back.
        void EnsureBlueprint()
        {
            if (ledger == null || !ledger.Building || !Sited) return;
            if (blueprint != null && blueprint.PlanId == ledger.pending.planId) return;
            if (blueprint != null) blueprint.Retire();

            var plan = PlanNamed(ledger.pending.planId);
            Vector3 at = ledger.pending.At;
            at.y = height(at.x, at.z);
            if (!CanPlace(plan, at, ledger.pending.yaw, out _, out float lo, out float hi))
            {
                // The ground moved under a saved blueprint (a terrain
                // parameter changed between sessions). Draw it anyway at the
                // height the field gives now: refusing to draw it would leave
                // a row nobody can see, act on or cancel.
                lo = hi = at.y;
            }
            at.y = hi;
            blueprint = BuildSite.Place(this, plan, at,
                Quaternion.Euler(0f, ledger.pending.yaw, 0f), hi - lo);
            blueprint.Refresh(ledger.pending);
        }

        /// The wood is in: take the drawing down and stand the thing up.
        void FinishPending()
        {
            if (ledger == null || ledger.pending == null) return;
            var plan = PlanNamed(ledger.pending.planId);
            Vector3 at = ledger.pending.At;
            if (height != null) at.y = height(at.x, at.z);

            // The blueprint goes first. `Raise` reserves the ground it stands
            // on, and the drawing is not a reservation -- but leaving it up
            // for a frame beside the real thing is two buildings in one place,
            // which is exactly what a player reports as a duplicate.
            if (blueprint != null) { blueprint.Retire(); blueprint = null; }

            var b = Raise(plan, at, ledger.pending.yaw);
            if (b == null)
            {
                // Refused on ground it was green on when it was sited. Rather
                // than silently eating the wood, keep the row: the blueprint
                // comes back next frame and the player can move it.
                EnsureBlueprint();
                return;
            }

            // The fire is the camp; anything else is a building AT the camp.
            // See `Site`.
            Vector3 stoodAt = b.transform.position;
            if (plan.kind == BuildKind.Fire || !hasCampCentre)
            {
                campCentre = stoodAt;
                hasCampCentre = true;
                ledger.SetKey(campCentre);
            }
            ledger.built.Add(plan.id);
            ledger.pending = null;
            ledger.ceilingPer = KeepsOfEach;

            // **The ground is cleared now, not when it was sited.** At home
            // the village clearing is reserved before the scenery is baked;
            // anywhere else the trees are standing when the player chooses, so
            // they come down through the same path the crew fell them by --
            // and what comes down is a camp appearing in the wood rather than
            // a gap appearing where a camp might one day go.
            LastClearingFelled = 0;
            var wood = GetComponentInChildren<Terrain.SceneryWood>();
            if (wood != null)
                LastClearingFelled = wood.FellWithin(stoodAt, CampClearingRadius);
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
            // The BUILDERS do. Everybody used to, which reset a sawyer and a
            // farmhand to cutting timber every time a hut was finished.
            foreach (var h in ledger.hands)
                if (h != null && h.order == OutpostOrder.Build)
                { h.order = OutpostOrder.Gather; h.target = Res.Timber; }
            // And they stand round it, which is the moment the camp stops
            // being a clearing and starts being somewhere people are.
            ArrangeHands();
            CampPiles.EnsureOn(this);
        }

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
        public bool CancelPending()
        {
            if (ledger == null || ledger.pending == null) return false;
            ledger.pending = null;
            // Whoever was building goes back to cutting; nobody else is moved.
            foreach (var h in ledger.hands)
                if (h != null && h.order == OutpostOrder.Build)
                { h.order = OutpostOrder.Gather; h.target = Res.Timber; }
            if (blueprint != null) { blueprint.Retire(); blueprint = null; }
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

            if (Site(BuildPlans.Campfire, at, out why) < 0) return -1;

            if (ledger != null && ledger.pending != null)
            {
                ledger.pending.done = ledger.pending.needed;
                ledger.pending.donePart = 0f;
                FinishPending();
            }
            if (!HasCamp) { why = "the fire would not stand there"; return -1; }

            if (ledger != null) ledger.lastTicked = TimeOfDay.Seconds;
            why = "";
            return LastClearingFelled;
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

            hand.transform.SetParent(transform, true);
            // Off only when nobody is here to see them. Leaving somebody
            // ashore in front of you and watching them wink out is the bug
            // this line is the whole of.
            hand.gameObject.SetActive(Watched);
            ArrangeHands();
            if (Watched) PuppetsToWork();
            return true;
        }

        /// Take this hand back aboard. The ledger stops counting them here.
        public bool Recall(Crew.CrewAgent hand, Transform ship)
        {
            if (hand == null || ship == null) return false;
            var row = HandNamed(hand.DisplayName);
            if (row == null) return false;
            ledger.hands.Remove(row);
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

        public void ShowHands(bool visible)
        {
            Watched = visible;
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
            else
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
        public void SyncFelling()
        {
            if (ledger == null) return;
            int want = Mathf.FloorToInt(ledger.timberTaken);
            if (want <= ledger.treesFelled) { owedSince = -1f; return; }

            var wood = WoodHere();
            if (wood == null || wood.TreeCount == 0) return;
            BuildFellOrder(wood);
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

                wood.Fell(i);
                ledger.treesFelled++;
                fellCursor++;
                owedSince = Time.unscaledTime;
            }

            // The island ran out of trees before the ledger ran out of logs.
            // Stop asking: the stock is the authority on how much wood there
            // was, and the mesh is only the picture of it.
            if (fellCursor >= fellOrder.Length) ledger.treesFelled = want;
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
            Vector3 c = CampCentre;
            if (fellOrder != null && fellOrder.Length == wood.TreeCount
                && ReferenceEquals(fellOrderWood, wood)
                && (fellOrderFrom - c).sqrMagnitude < 0.25f) return;

            var idx = new int[wood.TreeCount];
            var d2 = new float[wood.TreeCount];
            for (int i = 0; i < idx.Length; i++)
            {
                idx[i] = i;
                Vector3 p = wood.TreeAt(i).baseAt - c;
                p.y = 0f;
                d2[i] = p.sqrMagnitude;
            }
            System.Array.Sort(d2, idx);
            fellOrder = idx;
            // From the front: trees already down are skipped, so starting over
            // costs one pass and cannot double-fell anything.
            fellCursor = 0;
            fellOrderFrom = c;
            fellOrderWood = wood;
        }

        int[] fellOrder;
        int fellCursor;
        Vector3 fellOrderFrom;
        Terrain.SceneryWood fellOrderWood;
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
        /// and two men are never sent to the same trunk. Returns false when
        /// there is nothing standing within `maxDistance` of the camp -- the
        /// wood in reach is cut out, and a man who went on chopping a tree
        /// that will never fall would be the animation lying about the numbers.
        ///
        /// He stays enrolled either way: a hand between errands is still a
        /// hand cutting wood here, and dropping him out of the count for the
        /// second and a half he spends walking home would let rule (c) empty
        /// the wood behind his back.
        public bool ClaimTree(CampWorker w, float maxDistance,
            out int treeIndex, out Vector3 baseAt)
        {
            treeIndex = -1;
            baseAt = Vector3.zero;
            if (w == null) return false;

            var wood = WoodHere();
            if (wood == null || wood.TreeCount == 0) { Enrol(w, -1); return false; }
            BuildFellOrder(wood);
            PruneClaims();

            Vector3 c = CampCentre;
            float max2 = maxDistance * maxDistance;
            for (int k = 0; k < fellOrder.Length; k++)
            {
                int i = fellOrder[k];
                if (wood.TreeAt(i).felled) continue;
                Vector3 p = wood.TreeAt(i).baseAt;
                float dx = p.x - c.x, dz = p.z - c.z;
                // The order IS by distance, so the first one out of reach means
                // every one after it is too.
                if (dx * dx + dz * dz > max2) break;
                if (ClaimedByAnother(i, w)) continue;
                Enrol(w, i);
                treeIndex = i;
                baseAt = p;
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
                foreach (var b in built) if (b != null) n += b.StoreCapacity;
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

            // Bare ground keeps nothing. Home is the exception and keeps its
            // beach, which is the whole reason its first storehouse exists.
            if (!IsHome) openCapacity = 0;

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
            if (!IsHome && settlement.VillageClearing > 0.01f)
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
                Vector3 p = ClearingCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                if (!Clear(p, halfDiag)) continue;

                Vector3 toCentre = ClearingCentre - p;
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
                reserved.Add(new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f));
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
            reserved.Add(new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f));
            return b;
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
            if (!Corners(at, Quaternion.Euler(0f, yaw, 0f), len, wid, out lo, out hi))
            {
                why = height(at.x, at.z) < minHeight
                    ? "too low -- that is beach"
                    : "the ground is too steep there";
                return false;
            }

            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            if (!Clear(at, halfDiag)) { why = "something already stands there"; return false; }
            return true;
        }

        /// Nothing already claimed within reach of this footprint.
        bool Clear(Vector3 p, float halfDiag)
        {
            foreach (var r in reserved)
            {
                float dx = p.x - r.x, dz = p.z - r.z;
                float need = halfDiag + r.w;
                if (dx * dx + dz * dz < need * need) return false;
            }
            return true;
        }

        /// The four corners of the footprint, measured off the height field.
        /// Rejects ground that is too steep to stand a building on or low
        /// enough to be beach -- the same two tests SettlementSite used to find
        /// the patch, applied to this actual rectangle rather than to the patch
        /// as a whole.
        bool Corners(Vector3 p, Quaternion facing, float len, float wid,
            out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 c = p + facing * new Vector3(sx * len * 0.5f, 0f, sz * wid * 0.5f);
                    float h = height(c.x, c.z);
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
            if (lo < minHeight) return false;
            float span = Mathf.Sqrt(len * len + wid * wid);
            return (hi - lo) / span <= Terrain.SettlementSite.BuildableSlope;
        }
    }
}
