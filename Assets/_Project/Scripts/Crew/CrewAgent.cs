using System.Collections.Generic;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.World.Life;
using UnityEngine;

namespace SeaSick.Crew
{
    /// One crew member on deck.
    ///
    /// **Seasickness is characterisation, not a mechanic.** It tracks how
    /// rough the sea is right now — they go green and unsteady in a bad sea
    /// and get their colour back in calm water — and it costs the player
    /// nothing at all. It reads the weather on their faces, which is worth
    /// keeping; it is no longer a clock, and it never takes anyone off a post.
    /// (The hook is deliberately left here for a future crew-happiness system.)
    ///
    /// What DOES take them off a post is work: hands on the buckets are hands
    /// off the guns. That is the real cost now, and the player causes it by
    /// loading the ship.
    ///
    /// Lives as a child of the ship, so deck motion comes for free.
    public class CrewAgent : MonoBehaviour
    {
        [Header("Who")]
        [SerializeField] CrewMemberDef def;

        [Header("Sickness (cosmetic only)")]
        [Tooltip("How green a really foul sea makes them. Pure characterisation — costs nothing.")]
        [SerializeField] float sicknessAtWorstSea = 1f;
        [Tooltip("Seconds to go green in a bad sea, and to get their colour back in a calm one.")]
        [SerializeField] Vector2 sicknessHalflife = new Vector2(22f, 45f);

        [Header("Heaving -- and now the rail (phase 5a, 2026-09-28)")]
        [Tooltip("How queasy before they start retching. **They used to never leave their post for it; now they WALK to the rail, heave there, and walk back** -- their station goes unmanned for the round trip, same as a hand sent to the buckets.")]
        [SerializeField] float heaveThreshold = 0.72f;
        [Tooltip("Seconds between heaves, at the threshold and at the very worst.")]
        [SerializeField] Vector2 heaveInterval = new Vector2(26f, 7f);
        [SerializeField] float heaveLength = 2.2f;

        [Header("Deck positions (ship-local)")]
        [SerializeField] Vector3 stationLocal = new Vector3(1.0f, 2.0f, -2.5f);
        [SerializeField] Vector3 railLocal = new Vector3(1.8f, 2.0f, -2.5f);
        [SerializeField] float walkSpeed = 1.4f;

        [Header("Shore leave")]
        [SerializeField] float shoreWalkSpeed = 6.5f;
        [SerializeField] float swingInterval = 0.42f;

        [Header("Bailing")]
        [Tooltip("How far inboard of their station they step to work a bucket.")]
        [SerializeField] float bailStepInboard = 0.55f;

        [Header("Acting")]
        [Tooltip("ONLY the skin renderer. The tint below is the crew member's " +
                 "skin colour, so pointing this at the clothes turns the whole " +
                 "man green instead of just his face and forearms.")]
        [SerializeField] Renderer[] tintRenderers;
        [Tooltip("Left empty, it is found on the visual child.")]
        [SerializeField] Animator animator;
        [SerializeField] Color healthyTint = new Color(0.87f, 0.65f, 0.48f);
        [SerializeField] Color sickTint = new Color(0.55f, 0.78f, 0.45f);
        [SerializeField] float maxSwayDegrees = 9f;

        /// How green they look. Cosmetic: drives tint, sway and retching, and
        /// nothing else. Tracks the sea rather than accumulating.
        public float Sickness01 { get; private set; }
        public int PukeCount { get; private set; }
        public string StateName => state.ToString();
        public CrewMemberDef Def => def;
        public string DisplayName => def != null ? def.displayName : name;

        /// **Give a body somebody to be.** For figures made at runtime --
        /// villagers recruited by a camp (`BornVillager`) -- whose def is a
        /// fresh instance rather than an authored asset. The authored crew
        /// keep the def the scene gave them and never call this.
        public void SetDef(CrewMemberDef who)
        {
            if (who == null) return;
            def = who;
        }

        /// **This is your deck now.**
        ///
        /// `ship` is read off the parent in Awake, which is right for a hand
        /// who was born a child of the hull and wrong for one who was born at
        /// a camp: his "ship" would be the outpost, and `PutBackOnStation`
        /// would walk him off the deck and back into the fire ring. Carrying
        /// somebody aboard therefore says so, and the spot he is standing on
        /// becomes the post he keeps.
        public void BoardShip(Transform hull, Vector3 stationLocalPos)
        {
            ship = hull;
            stationLocal = stationLocalPos;
            railLocal = stationLocalPos + new Vector3(0.6f, 0f, 0f);
            PutBackOnStation();
        }

        /// Standing at their post and free to work it. Bailing, ashore, at
        /// or travelling to the rail, resting off a rescue, or over the side
        /// means no — this is the single question every ship system asks
        /// before it does anything. Being sick is NOT a reason on its own.
        public bool Available => state == State.Station && restLeft <= 0f;

        /// **Phase 5a.** 0..1, starts full. Drains toward zero in a rough,
        /// heeling or slammed sea (see `TrackGrip`); refills whenever none
        /// of that is happening, calm water included. Ticks for EVERY
        /// aboard hand, station or rail alike — only somebody actually AT
        /// the rail (`IsAtRail`) can go over when it runs out.
        public float Grip01 { get; private set; } = 1f;

        /// At the rail right now — heaving, or gripping it through a
        /// warning. The only place a fall can happen.
        public bool IsAtRail => state == State.AtRail || state == State.RailHold;

        /// Real seconds left of a post-rescue rest (`ApplyRescueAftermath`)
        /// — standing at their post but not working it yet, soaked and
        /// shaken. Separate from the rail-warning hold, which uses `state`
        /// alone (see `RailHold`) to keep `Available` correct.
        float restLeft;

        /// **Something else is moving this body.** Set by `World.CampWorker`
        /// (and the Hand) for as long as they drive the transform of a hand
        /// parked at a camp. A parked hand's state is `Station`, and `Station`
        /// both writes the body's rotation every frame and counts as being at
        /// sea — neither of which is true of somebody walking to a sawmill.
        ///
        /// Two things honour it, and only two: `ActBody` leaves the root
        /// rotation alone (the tint is untouched, so the sea still shows on
        /// their faces), and `TrackSickness` treats them as ashore. The state
        /// machine is NOT bypassed — `Station` does nothing anyway, and the
        /// walk cycle is driven by how fast the body is actually moving, so
        /// the legs come along for free however something else moves them.
        public bool Puppeted { get; set; }

        /// How fast they work when they are working. Flat: a queasy hand is
        /// still a hand. Kept as a seam for a future happiness/skill system.
        public float WorkRate01 => state == State.Station ? 1f : 0f;

        /// On a bucket instead of their post.
        public bool IsBailing => state == State.Bailing;

        /// Where this one starts retching — spread across the crew so five
        /// people don't heave on the same frame.
        float HeaveAt => Mathf.Clamp01(
            heaveThreshold + 0.1f * (def != null ? def.ironStomach : 0f) + pukeJitter);

        /// 0 at the threshold, 1 at the worst — drives how often.
        float Severity01 => Mathf.InverseLerp(HeaveAt, 1f, Sickness01);

        /// Called when the ship docks at home.
        public void Rest()
        {
            Sickness01 = 0f;
            if (IsAboard && !AtCamp) ReturnToStation();
        }

        /// **Parked at a camp: this body is not on any deck (2026-09-27).**
        /// A hand left ashore keeps `State.Station` (he never walked off; the
        /// outpost re-parented him), so every "back to your post" snap --
        /// `Rest`, bailing, a re-posting -- wrote his deck `stationLocal` into
        /// a transform whose parent is now the ISLAND: the whole camp stood
        /// in a deck-shaped cluster 5 m under the ground by the island's
        /// origin. `VoyageManager` rested its crew list (cached at Start,
        /// before the save moved them ashore) every frame at home.
        public bool AtCamp => GetComponentInParent<World.Outpost>(true) != null;

        /// Put them on a bucket. Ignored if they aren't aboard.
        public void StartBailing()
        {
            if (AtCamp) return;
            if (state == State.Station || state == State.Returning) state = State.Bailing;
        }

        /// Take them off the buckets and send them back to their post.
        public void StopBailing()
        {
            if (state == State.Bailing) state = State.Returning;
        }

        /// Put them back at their post, wherever they were and whatever they
        /// were doing.
        ///
        /// Unlike `ReturnAboard`, which makes them WALK back, this is for the
        /// cases where the body has been moved by something else -- parked at
        /// a camp and brought back aboard, restored from a save -- and the
        /// state machine has to agree with where the transform now is rather
        /// than path to it.
        public void PutBackOnStation()
        {
            ReleaseNode();
            state = State.Station;
            carriedResource = null;
            if (carried != null) { Destroy(carried); carried = null; }
            if (ship != null)
            {
                transform.SetParent(ship, true);
                transform.localPosition = stationLocal;
                transform.localRotation = Quaternion.identity;
            }
        }

        /// Thrown across the deck — running aground rattles them badly. Now
        /// permanent progress up the meter, so every hit spends the voyage.
        public void Jolt(float amount) => Sickness01 = Mathf.Clamp01(Sickness01 + amount);

        /// Post this hand to a station — used by CannonBattery to stand each
        /// gunner at their own gun. Taken from the live gun transforms rather
        /// than baked into the scene, so the deck can never drift out of sync
        /// with where the guns actually are.
        public void AssignStation(Vector3 station, Vector3 rail)
        {
            stationLocal = station;
            railLocal = rail;
            // Script order isn't guaranteed, so this can land before or after
            // Start: snap them over if they're just standing about.
            if (state == State.Station && !AtCamp) transform.localPosition = stationLocal;
        }

        enum State
        {
            // aboard
            Station, Bailing, Returning,
            // phase 5a: the rail round-trip a heave now takes
            RailGoing, AtRail, RailHold, RailReturning,
            // phase 5b: the haul -- a hand sent to throw the line
            HaulGoing, Hauling, HaulReturning,
            // ashore
            GoingAshore, ToNode, Chopping, ToShip, Idling, Boarding
        }
        State state = State.Station;

        /// Ashore covers the whole work loop, not just standing about.
        public bool IsAshore => state == State.ToNode || state == State.Chopping
            || state == State.ToShip || state == State.Idling || state == State.GoingAshore;
        /// The ship this hand walks back to while ashore (set by `GoAshore`);
        /// null for a hand who has never been ashore. Read by the shipyard's
        /// refit guard, which will not rebuild a deck a hand is out of.
        public Transform HomeShip => ship;
        public bool IsAboard => state == State.Station || state == State.Bailing
            || state == State.Returning || state == State.RailGoing
            || state == State.AtRail || state == State.RailHold || state == State.RailReturning
            || state == State.HaulGoing || state == State.Hauling || state == State.HaulReturning;

        /// **5b.** Out at the rail hauling a swimmer in, one way or another
        /// (walking out, hauling, or walking back) -- `Available` is already
        /// false for all three, this is just for anyone who wants to say why.
        public bool IsHauling => state == State.HaulGoing || state == State.Hauling
            || state == State.HaulReturning;

        Transform ship;
        Vector3 shoreTarget;
        World.Island workIsland;
        Ship.ShipHold hold;
        Ship.Gangway gangway;
        Voyage.VoyageManager voyage;

        World.ResourceNode targetNode;
        GameObject carried;
        string carriedResource;
        int carriedCount = 1;

        /// **The gather party this hand is out with, or null (2026-09-27).**
        /// Set by `GoAshoreInParty`, cleared the moment he is back aboard. A
        /// party hand works only what the party asks for (`NextSource`),
        /// cuts at the party's dials (`WorkSeconds`), carries a whole armful,
        /// and puts it in the SHIP'S HOLD through the party -- never into a
        /// camp's pile, which is the difference from the plain shore party.
        Ship.GatherParty party;
        public Ship.GatherParty Party => party;

        /// Units in his arms right now (0 when empty), for the party's
        /// "in hand" count.
        public int CarriedUnits => string.IsNullOrEmpty(carriedResource) ? 0 : carriedCount;
        int hitsLeft;
        float swingTimer;

        /// Send this crew member ashore to work an island.
        public void GoAshore(Vector3 landingPoint, World.Island island,
            Ship.ShipHold shipHold, Ship.Gangway plank, Voyage.VoyageManager voyageManager)
        {
            if (IsAshore) return;
            ship = transform.parent;
            shoreTarget = landingPoint;
            workIsland = island;
            hold = shipHold;
            gangway = plank;
            voyage = voyageManager;
            transform.SetParent(null, true);
            transform.localRotation = Quaternion.identity;
            // Both footing caches are about WHERE THEY ARE, and they are about
            // to be somewhere else. Forced stale rather than trusted: the
            // throttles are an optimisation, and an optimisation that survives
            // a teleport is a bug.
            nextIsleLookup = 0f;
            groundHeld = false;
            groundTickEnd = 0f;
            PathToShore(landingPoint);
            state = State.GoingAshore;
        }

        /// Go ashore as one of a gather party. Same walk down the plank as
        /// `GoAshore`; what differs is what he looks for once he is there.
        public void GoAshoreInParty(Ship.GatherParty withParty, Vector3 landingPoint,
            World.Island island, Ship.ShipHold shipHold, Ship.Gangway plank,
            Voyage.VoyageManager voyageManager)
        {
            if (IsAshore) return;
            party = withParty;
            GoAshore(landingPoint, island, shipHold, plank, voyageManager);
        }

        /// Recall to the ship. Drops any claim and walks back.
        public void ReturnAboard()
        {
            if (!IsAshore) return;
            ReleaseNode();
            PathToShip(ship != null ? ship.TransformPoint(stationLocal) : transform.position);
            state = State.Boarding;
        }

        void ReleaseNode()
        {
            if (targetNode != null) targetNode.Release(this);
            targetNode = null;
        }

        /// Look for the next thing to cut. Nothing left means idle on the beach.
        void SeekWork() => SeekWork(false);

        void SeekWork(bool fromDeck)
        {
            if (party != null)
            {
                // The party decides: the next source it wants worked, claimed
                // for him, or nothing -- and nothing means walk home.
                targetNode = party.NextSource(this);
                if (targetNode == null)
                {
                    PathToShip(ship != null ? ship.TransformPoint(stationLocal) : transform.position);
                    state = State.Boarding;
                    return;
                }
                // Off the deck he goes down the plank first; from the beach
                // (or the last rock) it is a straight walk the party has
                // already checked is walkable from the landing.
                if (fromDeck) PathToShore(targetNode.transform.position);
                else SetPath(targetNode.transform.position);
                state = State.ToNode;
                return;
            }
            if (workIsland == null || (voyage != null && voyage.HoldFull))
            {
                state = State.Idling;
                return;
            }
            targetNode = World.ResourceNode.FindFree(workIsland, transform.position, this);
            state = targetNode != null ? State.ToNode : State.Idling;
        }

        void PickUp(string resource)
        {
            carriedResource = resource;
            carried = World.CargoVisual.Build(resource, transform);
            carried.transform.localPosition = new Vector3(0f, 1.45f, 0.45f);
            carried.transform.localScale = Vector3.one * 0.7f;
        }

        void DropOff()
        {
            if (carried != null) Destroy(carried);
            carried = null;
            if (string.IsNullOrEmpty(carriedResource)) { carriedResource = null; return; }

            // A gather party carries for the SHIP: straight into the hold,
            // past any camp (there is usually none; if there is, a party is
            // still the ship's errand, not the camp's).
            if (party != null)
            {
                party.Delivered(this, carriedResource, carriedCount);
                carriedResource = null;
                carriedCount = 1;
                return;
            }

            // **A camp takes what is cut on its own island.**
            //
            // Without this the ship's party carries wood past a fire that
            // exists to keep exactly that, and straight into the hold — which
            // makes the camp an ornament on the island it is standing on.
            //
            // There is no double-counting to worry about, and the reason is
            // worth stating: the ledger counts only the hands who LIVE here,
            // and a hand walking about is by definition not one of them. A
            // stationed hand is a row that produces arithmetically; a visiting
            // hand is a body that delivers. The two sets never overlap.
            var camp = workIsland != null ? World.Outpost.Of(workIsland) : null;
            if (camp != null && camp.Ledger != null)
            {
                camp.CatchUp();
                var l = camp.Ledger;

                // **A blueprint is paid before a pile is filled.** A hand who
                // walked a log past the half-built fire and stacked it beside
                // one is the same nonsense as carrying it past the camp into
                // the hold, one step further in. This is also the whole of how
                // a VISITING hand helps build: the stationed rows accrue
                // through the tick, the bodies deliver through here, and the
                // two never double-count because a hand who lives here is a
                // row and a hand walking about is not.
                // Only the stuff it is actually built OUT of. A hand carrying
                // ore past a half-built shed is not paying for it, and before
                // resources were per-kind this branch happily let him.
                if (l.Building
                    && carriedResource == World.BuildPlans.Named(l.Pending.planId).resource)
                {
                    // Through the ledger's door, not a bare increment: the
                    // oldest site short of this material takes it, and a
                    // site already stocked spills it to the pile instead of
                    // counting past its need (Kevin, 2026-09-23).
                    l.DeliverToSite(carriedResource, 1);
                    carriedResource = null;
                    // The last log finishes it, and the fire should be alight
                    // before the man has walked away from it.
                    camp.CatchUp();
                    return;
                }

                // **Per resource, not per camp.** Ten of each is what the fire
                // watches over, so a hand carrying ore past a full timber pile
                // still has somewhere to put it.
                if (camp.HasCamp && l.Add(carriedResource, 1) > 0)
                {
                    carriedResource = null;
                    return;
                }
                // The pile is full. Fall through: better in the hold than
                // vanished, and a full camp is exactly when you want the wood
                // on the ship anyway.
            }

            if (voyage != null)
            {
                voyage.AddLoot(1, carriedResource);
                if (hold != null) hold.AddVisual(carriedResource);
            }
            carriedResource = null;
        }

        SmoothnessMeter meter;
        Ship.ShipMotor motor;
        Ship.HullIntegrity hull;
        Ship.AnchorController anchor;
        float walkSpeedSeen;      // metres/second, smoothed, for the Animator
        Vector3 lastSample;
        bool sampledAshore;
        bool sampled;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        MaterialPropertyBlock block;
        float heaveTimer;     // time left in the current retch
        float heaveCooldown;  // time until the next one
        float pukeJitter;
        float swayPhase;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ------------------------------------------------- what is underfoot
        //
        // A shore party walking over water used to make THREE managed calls
        // per agent per frame from WalkToWorld: an un-Bursted
        // `OceanSampler.SampleImmediate`, an `Island.Nearest` scan of every
        // island, and a delegate into the terrain height field. The immediate
        // sample is the expensive one — the cost probe counted 14 a frame at
        // sea against the 8 the sampler's own doc budgets for, at ~42 us each
        // — and a full shore party over the water spends that budget on feet.
        // StormSpray moved its 24 immediate samples into the registry and got
        // 1.0 ms a frame back; this is the same move.
        //
        // The other two are throttled rather than batched: nothing was wrong
        // with them but the rate.

        /// One handle per hand, folded into the ONE batched Burst query the
        /// physics driver runs per step. Only read while they are over water.
        Ocean.OceanProbeRegistry.Handle seaProbe;

        /// Which island they are walking on. Re-resolved twice a second: at
        /// 6.5 m/s that is three metres of walking, and `Nearest` answers with
        /// the island whose SHORELINE is closest — an answer that does not
        /// change hands over three metres unless they are already in the
        /// strait between two, where either gives the same footing. The
        /// shoreline test itself stays per-frame (see WalkToWorld): it is an
        /// array index, and it is the call that decides water from land.
        World.Island footingIsle;
        float nextIsleLookup;
        const float IsleInterval = 0.5f;

        /// The terrain height, sampled at 10 Hz and interpolated across the
        /// gap. Simply HOLDING the last value would trade the per-frame cost
        /// for a lag that grows with the slope being crossed — the error the
        /// big comment in WalkToWorld was written about. So each tick samples
        /// where they will BE when it expires and the frames between walk the
        /// straight line to it: no lag at all on a straight leg, and at worst
        /// one tick of a turn to catch up.
        float groundFrom, groundTo, groundTickEnd;
        bool groundHeld;
        const float GroundInterval = 0.1f;

        void OnEnable()
        {
            seaProbe = Ocean.OceanProbeRegistry.Register(transform.position);
        }

        void OnDisable()
        {
            Ocean.OceanProbeRegistry.Unregister(seaProbe);
            seaProbe = null;
        }

        /// Re-registered rather than assumed: a domain reload mid-play empties
        /// the registry's static list while the component and this field
        /// survive. StormSpray.EnsureHandles documents the same asymmetry.
        void EnsureProbe()
        {
            if (seaProbe != null && Ocean.OceanProbeRegistry.Handles.Count > 0) return;
            seaProbe = Ocean.OceanProbeRegistry.Register(transform.position);
        }

        void Start()
        {
            meter = GetComponentInParent<SmoothnessMeter>();
            motor = GetComponentInParent<Ship.ShipMotor>();
            hull = GetComponentInParent<Ship.HullIntegrity>();
            anchor = GetComponentInParent<Ship.AnchorController>();
            block = new MaterialPropertyBlock();
            if (tintRenderers == null || tintRenderers.Length == 0)
                tintRenderers = FindSkinRenderers();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            transform.localPosition = stationLocal;
            // Seeded off the name so a given crew member always breaks at the
            // same point — variety between people, not between playthroughs.
            pukeJitter = (Mathf.Abs(DisplayName.GetHashCode() % 100) / 100f - 0.5f) * 0.08f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            TrackSickness(dt);
            TrackGrip(dt);
            if (restLeft > 0f) restLeft -= dt;
            TickHeaving(dt);
            TrackRailSafety(dt);
            RunStateMachine(dt);
            TrackWalking(dt);
            ActBody(dt);
        }

        /// The walk cycle is driven by how fast they are ACTUALLY moving, not
        /// by a list of states that walk. Every state that moves them does it
        /// through one of the Walk* helpers, so measuring the result cannot
        /// fall out of step the way an enumeration would the next time a state
        /// is added.
        ///
        /// The frame matters and is not cosmetic: aboard, they are children of
        /// a ship that is itself doing 15 m/s, so world-space movement would
        /// have the whole watch sprinting on the spot the moment she got under
        /// way. Aboard, measure in the SHIP's frame — which is the frame
        /// WalkTo moves them in anyway.
        void TrackWalking(float dt)
        {
            if (dt <= 0f) return;
            bool ashore = IsAshore;
            Vector3 here = ashore ? transform.position : transform.localPosition;
            float raw = 0f;
            if (sampled && ashore == sampledAshore)
                raw = (here - lastSample).magnitude / dt;
            lastSample = here;
            sampledAshore = ashore;
            sampled = true;

            // Doubled over the side is not walking, whatever their feet did
            // the frame before.
            if (heaveTimer > 0f) raw = 0f;
            walkSpeedSeen = Mathf.Lerp(walkSpeedSeen, raw, 1f - Mathf.Exp(-10f * dt));
            if (animator != null) animator.SetFloat(SpeedId, walkSpeedSeen);
        }

        /// The tint is a SKIN colour, so it must reach skin and nothing else.
        /// The old fallback took every renderer on the object, which was right
        /// when a crew member was a sphere and a capsule and is now the
        /// difference between a queasy man and a man in a green shirt.
        Renderer[] FindSkinRenderers()
        {
            var all = GetComponentsInChildren<Renderer>();
            var skin = new List<Renderer>();
            foreach (var r in all)
            {
                var m = r.sharedMaterial;
                if (m != null && m.name.IndexOf("skin", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    skin.Add(r);
            }
            return skin.Count > 0 ? skin.ToArray() : all;
        }

        /// Sickness now READS the sea rather than counting time on it: it
        /// eases toward however rough things are right now, quickly on the way
        /// up and slowly on the way down, so a squall turns the deck green and
        /// a calm hour gives them their colour back. Ashore they settle.
        void TrackSickness(float dt)
        {
            float target;
            // A hand parked at a camp is standing on an island. Their state is
            // `Station`, because that is what a body switched off at a camp
            // is, and without this they would go on being measured against
            // the sea the ship is in three kilometres away.
            if (IsAshore || Puppeted) target = 0f;
            else
            {
                float rough = meter != null ? meter.Roughness01 : 0f;
                float resistance = def != null ? def.ironStomach : 0f;
                target = sicknessAtWorstSea * rough * rough * (1f - 0.5f * resistance);
            }

            float halflife = target > Sickness01 ? sicknessHalflife.x : sicknessHalflife.y;
            float blend = 1f - Mathf.Exp(-(0.6931f / Mathf.Max(0.01f, halflife)) * dt);
            Sickness01 = Mathf.Clamp01(Mathf.Lerp(Sickness01, target, blend));
        }

        void RunStateMachine(float dt)
        {
            switch (state)
            {
                case State.Station:
                    break;

                case State.Bailing:
                    // They step inboard off their post and work a bucket. The
                    // gun behind them is silent for exactly as long as this
                    // lasts, which is the whole cost of a wet ship.
                    WalkTo(BailLocal, dt);
                    break;

                case State.Returning:
                    if (WalkTo(stationLocal, dt))
                    {
                        transform.localRotation = Quaternion.identity;
                        state = State.Station;
                    }
                    break;

                // --- phase 5a: the rail round-trip ---------------------------
                case State.RailGoing:
                    if (WalkTo(railLocal, dt))
                    {
                        state = State.AtRail;
                        heaveTimer = heaveLength;
                        PukeCount++;
                    }
                    break;

                case State.AtRail:
                    // Retching in place — TickHeaving counts heaveTimer down
                    // and moves on to RailReturning when it runs out, unless
                    // TrackRailSafety has already pulled this into RailHold.
                    break;

                case State.RailHold:
                    // Gripping the rail through the warning window — held
                    // here by `TrackRailSafety`, which is also what moves us
                    // back out (saved -> RailReturning, or overboard).
                    break;

                case State.RailReturning:
                    if (WalkTo(stationLocal, dt))
                    {
                        transform.localRotation = Quaternion.identity;
                        heaveCooldown = heaveInterval.x;
                        state = State.Station;
                    }
                    break;

                // --- phase 5b: the haul --------------------------------------
                case State.HaulGoing:
                    if (WalkTo(haulRailLocal, dt))
                    {
                        state = State.Hauling;
                        haulTimer = OverboardTuning.HaulSeconds;
                        EnsureHaulLine();
                    }
                    break;

                case State.Hauling:
                    TickHaul(dt);
                    break;

                case State.HaulReturning:
                    if (WalkTo(stationLocal, dt))
                    {
                        transform.localRotation = Quaternion.identity;
                        state = State.Station;
                    }
                    break;

                case State.GoingAshore:
                    if (FollowPath(dt, 0.4f)) SeekWork();
                    break;

                case State.ToNode:
                    if (targetNode == null || targetNode.Harvested) { SeekWork(); break; }
                    if (party != null)
                    {
                        // The last waypoint is the source; stand off by its
                        // own size (a big boulder is not worked from inside).
                        if (FollowPath(dt, Mathf.Max(1.9f, targetNode.StandOff + 0.6f)))
                        {
                            carriedCount = party.ArmfulAt(targetNode);
                            hitsLeft = Mathf.Max(1, Mathf.CeilToInt(
                                party.WorkSeconds(targetNode.Resource, carriedCount)
                                / Mathf.Max(0.05f, swingInterval)));
                            swingTimer = 0f;
                            state = State.Chopping;
                        }
                        break;
                    }
                    // Stop a pace short so they stand beside the tree, not in it.
                    if (WalkNear(targetNode.transform.position, 1.9f, dt))
                    {
                        hitsLeft = targetNode.HitsToHarvest;
                        swingTimer = 0f;
                        state = State.Chopping;
                    }
                    break;

                case State.Chopping:
                {
                    if (targetNode == null || targetNode.Harvested) { SeekWork(); break; }
                    swingTimer -= dt;
                    if (swingTimer <= 0f)
                    {
                        swingTimer = swingInterval;
                        targetNode.Strike();
                        hitsLeft--;
                        if (hitsLeft <= 0)
                        {
                            string res = targetNode.Resource;
                            if (party != null)
                            {
                                // Book the island's stock as the source comes
                                // out of the ground, then take it away.
                                party.Cut(this, targetNode, res, carriedCount);
                                targetNode.Harvest();
                            }
                            else
                            {
                                targetNode.Harvest();
                                if (workIsland != null) workIsland.Extract(1f);
                                carriedCount = 1;
                            }
                            targetNode = null;
                            PickUp(res);
                            PathToShip(hold != null ? hold.DropPoint
                                : (ship != null ? ship.TransformPoint(stationLocal) : transform.position));
                            state = State.ToShip;
                        }
                    }
                    // Lean into the swing.
                    float swing = Mathf.Sin(Time.time * 12f) * 16f;
                    Vector3 face = targetNode != null
                        ? targetNode.transform.position - transform.position : transform.forward;
                    face.y = 0f;
                    if (face.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.LookRotation(face, Vector3.up)
                            * Quaternion.Euler(swing, 0f, 0f);
                    break;
                }

                case State.ToShip:
                    RefreshShipWaypoints(hold != null ? hold.DropPoint : Vector3.zero, hold != null);
                    if (FollowPath(dt, 1.2f))
                    {
                        DropOff();
                        SeekWork(true);
                    }
                    break;

                case State.Idling:
                    // Nothing to cut — wait on the beach, and pick work back up
                    // if the hold empties or another node frees.
                    transform.rotation = Quaternion.Euler(
                        0f, Mathf.Sin(Time.time * 0.6f + shoreTarget.x) * 40f, 0f);
                    swingTimer -= dt;
                    if (swingTimer <= 0f) { swingTimer = 1.5f; SeekWork(); }
                    break;

                case State.Boarding:
                    if (ship == null) { ReturnToStation(); break; }

                    // The last waypoint is the station, but the ship is moving
                    // on the swell, so keep it current as we approach.
                    RefreshShipWaypoints(ship.TransformPoint(stationLocal), true);
                    if (FollowPath(dt, 0.35f))
                    {
                        transform.SetParent(ship, true);
                        // A party hand recalled with a load walked it over the
                        // plank: it goes in the hold, not over the side.
                        if (party != null)
                        {
                            if (!string.IsNullOrEmpty(carriedResource)) DropOff();
                            var p = party;
                            party = null;
                            p.Boarded(this);
                        }
                        ReturnToStation();
                    }
                    break;
            }
        }

        void ReturnToStation()
        {
            // Never onto a deck that is not there (see `AtCamp`).
            if (AtCamp) { state = State.Station; return; }
            transform.localPosition = stationLocal;
            transform.localRotation = Quaternion.identity;
            heaveCooldown = heaveInterval.x;
            state = State.Station;
        }

        /// A pace inboard of their post — clear of the gun, and derived from
        /// their own station so five people never bail on the same spot.
        Vector3 BailLocal => new Vector3(
            stationLocal.x * (1f - bailStepInboard), stationLocal.y, stationLocal.z);

        /// **Phase 5a: a heave now takes them to the rail and back.** While
        /// `AtRail`, `heaveTimer` still just counts the retch down the same
        /// way it always did; when it runs out here it hands off to
        /// `RailReturning` instead of going straight back to `Station`. A
        /// new trip can only START from `State.Station` — bailing or
        /// mid-return, they finish what they're doing first.
        void TickHeaving(float dt)
        {
            if (heaveTimer > 0f)
            {
                heaveTimer -= dt;
                if (heaveTimer <= 0f && state == State.AtRail) state = State.RailReturning;
                return;
            }

            // `Puppeted` for the same reason `TrackSickness` reads it: a hand
            // at a camp is `Station`, so without this a crewman left ashore
            // still green from the crossing would retch in the middle of the
            // village.
            if (state != State.Station || Puppeted || Sickness01 < HeaveAt)
            {
                heaveCooldown = heaveInterval.x;
                return;
            }

            heaveCooldown -= dt;
            if (heaveCooldown <= 0f)
            {
                heaveCooldown = Mathf.Lerp(heaveInterval.x, heaveInterval.y, Severity01);
                state = State.RailGoing;   // heaveTimer/PukeCount start on arrival
            }
        }

        // ------------------------------------------------- man overboard (5a)
        //
        // `TrackGrip` runs for every aboard hand, station or rail alike --
        // "at the station the grip just drains/refills" (build brief). Only
        // `TrackRailSafety`, gated on `IsAtRail`, can turn a low grip into a
        // warning and a fall.

        float NormalizeDeg(float degrees) =>
            degrees > 180f ? degrees - 360f : degrees;

        bool warnActive;
        float warnLeft;
        bool warnDangerSeen;
        bool forcedTrip;
        bool scriptedTrip;

        void TrackGrip(float dt)
        {
            if (IsAshore || Puppeted || AtCamp || !IsAboard) { Grip01 = 1f; return; }

            float rough = meter != null ? meter.Roughness01 : 0f;
            float roughDrain = Mathf.Max(0f, rough - OverboardTuning.CalmRoughness)
                * OverboardTuning.DrainPerRoughness;

            float heel = ship != null ? Mathf.Abs(NormalizeDeg(ship.eulerAngles.z)) : 0f;
            float heelDrain = Mathf.Max(0f, heel - OverboardTuning.HeelFreeDeg)
                * OverboardTuning.DrainPerHeelDeg;

            float slamDrain = 0f;
            if (hull != null && Time.time - hull.LastImpactTime < 0.5f)
                slamDrain = OverboardTuning.SlamDrainFlat
                    * Mathf.Clamp01(hull.LastImpactSpeed / Mathf.Max(0.1f, OverboardTuning.SlamSpeedForFullHit));

            float drain = roughDrain + heelDrain + slamDrain;
            if (drain > 0f)
            {
                float stormMul = 1f + OverboardTuning.StormDrainMultiplierExtra * Sailing.Storminess01;
                float nightMul = Sailing.IsNight ? OverboardTuning.NightDrainMultiplier : 1f;
                float seaLegs = Lives.Record(DisplayName)?.seaLegs ?? 0f;
                float seaLegsMul = Mathf.Lerp(1f, OverboardTuning.SeaLegsMinMultiplier, seaLegs);
                drain *= stormMul * nightMul * seaLegsMul;
                Grip01 = Mathf.Clamp01(Grip01 - drain * dt);
            }
            else
            {
                Grip01 = Mathf.Clamp01(Grip01 + OverboardTuning.RefillPerSecond * dt);
            }
        }

        /// The warning, the hold, and the fall/save decision. Only reachable
        /// while physically `IsAtRail` — a station hand's grip can bottom
        /// out and it costs him nothing but colour in his knuckles.
        void TrackRailSafety(float dt)
        {
            if (!IsAtRail) { warnActive = false; warnLeft = 0f; warnDangerSeen = false; return; }

            // Nobody else falls until the scripted first time has run its
            // course (build brief item 8) — except the one agent it forced
            // into this very trip, or a dev-forced test (`forcedTrip`
            // covers both; only an ORGANIC grip-driven trip is gated).
            if (FirstOverboard.Blocks(this) && !forcedTrip) return;

            if (!warnActive)
            {
                if (Grip01 <= OverboardTuning.WarnGrip || forcedTrip)
                {
                    warnActive = true;
                    warnLeft = OverboardTuning.WarnSeconds;
                    warnDangerSeen = false;
                    state = State.RailHold;
                    Banner.Show(DisplayName + ": \"Hold on!\"", 2f);
                    OverboardHaptics.Warning();
                }
                return;
            }

            // Held at zero for the whole window — visibly gripping for
            // their life — while the raw drain this frame decides whether
            // the danger actually eased.
            Grip01 = 0f;
            float rough = meter != null ? meter.Roughness01 : 0f;
            if (rough > OverboardTuning.CalmRoughness || forcedTrip) warnDangerSeen = true;

            warnLeft -= dt;
            if (warnLeft > 0f) return;

            warnActive = false;
            if (warnDangerSeen)
            {
                FallOverboard();
            }
            else
            {
                // Saved: eases back into the retch/return they were already
                // doing, with a little grip back so the same wave can't
                // instantly re-trigger the warning.
                Grip01 = Mathf.Max(Grip01, OverboardTuning.WarnGrip + 0.05f);
                forcedTrip = false;
                state = heaveTimer > 0f ? State.AtRail : State.RailReturning;
            }
        }

        /// **Dev panel**: knock grip down (or up) by hand, for testing the
        /// warning/fall without waiting on real weather.
        public void DebugAdjustGrip(float delta) => Grip01 = Mathf.Clamp01(Grip01 + delta);

        /// **Dev panel / `FirstOverboard`**: force this hand to the rail and
        /// through the normal warning+fall sequence right now, regardless of
        /// how queasy they actually are. `scripted` marks the swimmer with
        /// the long first-time timer and exempts them from
        /// `FirstOverboard.Blocks` for the run this triggers.
        public void ForceOverboardSequence(bool scripted)
        {
            if (!IsAboard || AtCamp) return;
            forcedTrip = true;
            scriptedTrip = scripted;
            // Pulled off whatever aboard state they were in (bailing,
            // returning, already mid rail-trip) and sent to the rail.
            if (state != State.AtRail && state != State.RailHold) state = State.RailGoing;
        }

        /// The fall itself: splash, banner, haptic, a brief real-time
        /// slow-down (only if nothing else has already touched
        /// `Time.timeScale`), a swimmer spawned in their place, and this
        /// body pulled off the roster.
        void FallOverboard()
        {
            Vector3 worldPos = transform.position;
            if (Ocean.OceanSampler.Ready) worldPos.y = Ocean.OceanSampler.SampleImmediate(worldPos).height;

            bool wasScripted = scriptedTrip;
            forcedTrip = false;
            scriptedTrip = false;
            warnActive = false;

            GoOverboardAt(worldPos, wasScripted);
        }

        /// **Dev panel (5b)**: skip the whole grip/warning sequence and drop
        /// this hand straight in the water a fixed distance off the ship's
        /// SIDE (whichever side her own rail is on) -- for testing Throw
        /// line quickly without waiting on real weather.
        public void DebugDropOverboardNear(float sideMetres)
        {
            if (!IsAboard || AtCamp) return;
            Vector3 outward = ship != null
                ? ship.right * Mathf.Sign(railLocal.x != 0f ? railLocal.x : 1f)
                : transform.right;
            Vector3 worldPos = transform.position + outward * Mathf.Abs(sideMetres);
            if (Ocean.OceanSampler.Ready) worldPos.y = Ocean.OceanSampler.SampleImmediate(worldPos).height;
            GoOverboardAt(worldPos, false);
        }

        /// Shared by the real fall and the dev drop: splash, banner, haptic,
        /// a brief real-time slow-down (only if nothing else has already
        /// touched `Time.timeScale`), a swimmer spawned at `worldPos`, and
        /// this body pulled off the roster.
        void GoOverboardAt(Vector3 worldPos, bool scripted)
        {
            Ocean.DynamicWaterSim.Splash(worldPos, 5f, 1f);
            Banner.Show("MAN OVERBOARD!");
            OverboardHaptics.Fall();
            Lives.Log(DisplayName, LifeEvents.Overboard);

            if (Mathf.Approximately(Time.timeScale, 1f))
                SlowMoRunner.Run(0.35f, 0.5f);

            var hullT = ship;
            var swimmer = Swimmer.Spawn(this, hullT, worldPos, scripted);

            // Off the books: the station empties (`Available` already false
            // mid-trip; `CrewRoster.Refresh` drops the cached array so
            // `AbleCount`/gun assignment stop counting this body at all).
            gameObject.SetActive(false);
            var roster = hullT != null ? hullT.GetComponentInParent<CrewRoster>() : null;
            if (roster != null) roster.Refresh();
        }

        // ------------------------------------------------- the haul (5b)
        //
        // `RescueHud` decides WHO gets sent and WHEN (nearest available hand,
        // in reach, ship slow enough); this is just the walk-out/haul/walk-
        // back the crew member does once picked.

        Vector3 haulRailLocal;
        IOverboardTarget haulTarget;
        float haulTimer;
        LineRenderer haulLine;

        /// **5b, generalised for phase 6: send this hand to haul `target`
        /// aboard** — a `Swimmer` or a `FloatingCargo`, whichever
        /// `RescueHud` picked. Refuses if this hand isn't `Available`
        /// (already covers bailing, at the rail, hauling somebody else,
        /// resting off a rescue, ashore...). Walks to the rail on the
        /// TARGET's side of the hull, at roughly her own fore/aft position,
        /// then hauls for `OverboardTuning.HaulSeconds`.
        public bool StartHaul(IOverboardTarget target)
        {
            if (!Available || target == null || ship == null) return false;

            Vector3 local = ship.InverseTransformPoint(target.WorldPosition);
            float side = Mathf.Sign(local.x != 0f ? local.x
                : (railLocal.x != 0f ? railLocal.x : 1f));
            float halfLen = motor != null ? Mathf.Max(1f, motor.HullLength * 0.5f - 1.5f) : Mathf.Abs(railLocal.z) + 1f;
            float z = Mathf.Clamp(local.z, -halfLen, halfLen);
            haulRailLocal = new Vector3(Mathf.Abs(railLocal.x) * side, railLocal.y, z);

            haulTarget = target;
            state = State.HaulGoing;
            return true;
        }

        /// Hauling in place: the line is drawn every frame, the swimmer is
        /// pulled toward the rail, and the timer runs down to a rescue --
        /// unless she's already been resolved some other way, or the ship
        /// has moved so far that the line would have to stretch past
        /// `haulSlipMultiple` times the throw reach, in which case it slips
        /// and the hand comes back empty-handed.
        void TickHaul(float dt)
        {
            if (haulTarget == null || haulTarget.Resolved) { EndHaul(); return; }

            Vector3 railWorld = ship != null ? ship.TransformPoint(haulRailLocal) : transform.position;
            float dist = Vector3.Distance(railWorld, haulTarget.WorldPosition);
            if (dist > OverboardTuning.ThrowReachMetres * OverboardTuning.HaulSlipMultiple)
            {
                EndHaul();
                return;
            }

            haulTarget.BeingHauled = true;
            haulTarget.HaulAnchor = railWorld;
            UpdateHaulLine(railWorld, haulTarget.WorldPosition);

            haulTimer -= dt;
            if (haulTimer <= 0f)
            {
                string rescuerName = DisplayName;
                var target = haulTarget;
                haulTarget = null;
                DestroyHaulLine();
                target.OnHauled(rescuerName);
                restLeft = Mathf.Max(restLeft, OverboardTuning.RescuerRecoverSeconds);
                state = State.HaulReturning;
            }
        }

        /// Line slipped, or the swimmer resolved some other way (rescued by
        /// nobody -- can't happen today -- washed ashore, lost, or her whole
        /// game object gone): let go and walk back.
        void EndHaul()
        {
            if (haulTarget != null) haulTarget.BeingHauled = false;
            haulTarget = null;
            DestroyHaulLine();
            state = State.HaulReturning;
        }

        void EnsureHaulLine()
        {
            if (haulLine != null) return;
            var go = new GameObject("HaulLine");
            haulLine = go.AddComponent<LineRenderer>();
            haulLine.useWorldSpace = true;
            haulLine.positionCount = 2;
            haulLine.widthMultiplier = 0.05f;
            haulLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            haulLine.receiveShadows = false;
            haulLine.alignment = LineAlignment.View;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.92f, 0.87f, 0.72f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.92f, 0.87f, 0.72f));
            haulLine.material = mat;
        }

        void UpdateHaulLine(Vector3 fromWorld, Vector3 toWorld)
        {
            if (haulLine == null) return;
            haulLine.SetPosition(0, fromWorld + Vector3.up * 0.6f);
            haulLine.SetPosition(1, toWorld);
        }

        void DestroyHaulLine()
        {
            if (haulLine == null) return;
            if (haulLine.material != null) Destroy(haulLine.material);
            Destroy(haulLine.gameObject);
            haulLine = null;
        }

        void OnDestroy() => DestroyHaulLine();

        /// **`Swimmer.Rescue`**: this exact body is still here (deactivated,
        /// never destroyed) — stand it back up on its old post.
        public void ReboardAfterRescue()
        {
            state = State.Station;
            heaveTimer = 0f;
            heaveCooldown = heaveInterval.x;
            warnActive = false;
            forcedTrip = false;
            scriptedTrip = false;
            Grip01 = 1f;
            PutBackOnStation();
        }

        /// Soaked and shaken: a sickness spike and a spell off station,
        /// same shape as the drag-to-hut recovery on land.
        public void ApplyRescueAftermath(float sicknessSpike, float offStationSeconds)
        {
            Sickness01 = Mathf.Clamp01(Sickness01 + sicknessSpike);
            restLeft = Mathf.Max(restLeft, offStationSeconds);
        }

        /// Travel horizontally toward a world target, riding the water surface
        /// while over open sea. Arrival is judged on HORIZONTAL distance only:
        /// the deck sits about 2m above the waterline, so a 3D distance test
        /// could never be satisfied while the wading code pins them to the
        /// surface — which left recalled crew walking forever.
        // --- Waypoint routing ---
        // Crew must use the plank rather than cutting across open water, so
        // every trip between ship and shore is routed through its two ends.
        readonly List<Vector3> path = new List<Vector3>();
        int pathIndex;
        bool onPlank;

        void SetPath(params Vector3[] points)
        {
            path.Clear();
            foreach (var p in points) path.Add(p);
            pathIndex = 0;
        }

        /// Route from wherever we are, over the plank, to a point on the ship.
        void PathToShip(Vector3 finalPoint)
        {
            if (gangway != null && gangway.Ready)
                SetPath(gangway.LandingPoint, gangway.DeckPoint, finalPoint);
            else
                SetPath(finalPoint);
        }

        /// Route from the deck, over the plank, to a point ashore.
        void PathToShore(Vector3 finalPoint)
        {
            if (gangway != null && gangway.Ready)
                SetPath(gangway.DeckPoint, gangway.LandingPoint, finalPoint);
            else
                SetPath(finalPoint);
        }

        /// The ship rides the swell, so any waypoint attached to it — the plank's
        /// deck end and the destination on board — has to be re-read each frame
        /// or the crew walk to where the ship used to be.
        void RefreshShipWaypoints(Vector3 finalPoint, bool updateFinal)
        {
            if (path.Count == 0) return;
            if (path.Count >= 3 && gangway != null && gangway.Ready)
                path[1] = gangway.DeckPoint;
            if (updateFinal) path[path.Count - 1] = finalPoint;
        }

        bool FollowPath(float dt, float finalStandOff)
        {
            if (path.Count == 0) return true;
            bool last = pathIndex >= path.Count - 1;
            // Intermediate points are plank ends: hug them so the crew stay on
            // the timber instead of clipping the corner over the water.
            onPlank = !last || path.Count > 1;
            if (WalkNear(path[pathIndex], last ? finalStandOff : 0.6f, dt))
            {
                if (last) { onPlank = false; return true; }
                pathIndex++;
            }
            return false;
        }

        /// Walk toward a point but stop `standOff` metres short of it.
        bool WalkNear(Vector3 target, float standOff, float dt)
        {
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            float dist = flat.magnitude;
            if (dist <= standOff) return true;
            Vector3 aim = target - flat.normalized * standOff;
            return WalkToWorld(aim, dt) || dist <= standOff;
        }

        bool WalkToWorld(Vector3 target, float dt)
        {
            Vector3 pos = transform.position;
            Vector3 flatPos = new Vector3(pos.x, 0f, pos.z);
            Vector3 flatTarget = new Vector3(target.x, 0f, target.z);
            Vector3 next = Vector3.MoveTowards(flatPos, flatTarget, shoreWalkSpeed * dt);
            float horizontal = Vector3.Distance(next, flatTarget);

            if (horizontal < 0.35f)
            {
                transform.position = target;   // snap: guarantees arrival
                return true;
            }

            float y;
            if (onPlank || horizontal < 4f)
            {
                // On the plank, follow its slope between the two ends instead
                // of riding the water underneath it.
                y = Mathf.Lerp(pos.y, target.y, 1f - Mathf.Exp(-6f * dt));
            }
            else
            {
                // **Follow the ground under their FEET, not the height of
                // wherever they are going.**
                //
                // This was `y = target.y`, held for the whole walk: a crew
                // member setting off for a tree sixty metres up the hill
                // popped to sixty metres on the first step and glided there
                // through the air, then sank into the slope on the way back
                // down with the ship's height. Measured across a shore party
                // on Island_1, against a raycast onto the drawn mesh: **mean
                // 30 m off the ground, worst 94 m.**
                //
                // Sampled, not smoothed. The height field is continuous and
                // they walk at a couple of metres a second, so following it
                // exactly cannot jitter — and a lag here would trade a fixed
                // error for one that grows with the slope they are crossing.
                // That is why the 10 Hz terrain tick below interpolates toward
                // where they are GOING rather than holding the last value.
                y = target.y;

                // The handle follows their feet whether or not the sea is what
                // is under them, so the first frame they step off a beach
                // reads a height taken beside them rather than wherever they
                // last waded.
                EnsureProbe();
                seaProbe.position = next;

                // Which island, twice a second. The shoreline test against it
                // still runs every frame — see the field comments.
                if (Time.time >= nextIsleLookup)
                {
                    footingIsle = World.Island.Nearest(next);
                    nextIsleLookup = Time.time + IsleInterval;
                }

                var isle = footingIsle;
                if (isle != null)
                {
                    Vector3 flat = next - isle.transform.position;
                    flat.y = 0f;
                    // Against the shoreline on THIS bearing. The mean radius
                    // called half a lobed island "sea" and floated them home
                    // on the swell across dry land.
                    float shore = isle.RadiusAt(Mathf.Atan2(flat.x, flat.z));
                    if (flat.magnitude > shore)
                    {
                        // Wading: the sea's height comes off the registry's
                        // batched Burst query, one physics step old. At
                        // 6.5 m/s that is 13 cm of walking under a surface
                        // they are standing 35 cm proud of anyway.
                        if (seaProbe.sampledFrame != 0) y = seaProbe.sample.height + 0.35f;
                        else if (Ocean.OceanSampler.Ready)
                            // First frame only — nothing batched for this hand yet.
                            y = Ocean.OceanSampler.SampleImmediate(next).height + 0.35f;
                    }
                    else if (World.Island.TerrainHeight != null)
                    {
                        y = Footing(next, flatTarget - next);
                    }
                }
            }

            transform.position = new Vector3(next.x, y, next.z);
            Vector3 look = flatTarget - next;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-6f * dt));
            return false;
        }

        /// The ground under their feet, from a terrain sample taken 10 times a
        /// second instead of every frame.
        ///
        /// Each tick samples the height where the tick will END — one step of
        /// `toTarget`, clamped to how far they can actually walk in the
        /// interval — and the frames in between run the straight line from the
        /// last tick's end to this one's. On a straight leg, which is what
        /// WalkToWorld does between waypoints, that lands exactly on the
        /// sampled field with no lag at all; only a turn inside a tick costs
        /// anything, and it costs at most that tick.
        ///
        /// `groundHeld` alone is not enough to continue from: a hand who has
        /// been aboard for a minute would resume from a height sampled on
        /// another island. A gap of more than one tick re-anchors.
        float Footing(Vector3 here, Vector3 toTarget)
        {
            float now = Time.time;
            if (now >= groundTickEnd)
            {
                bool continuous = groundHeld && now < groundTickEnd + GroundInterval;
                groundFrom = continuous ? groundTo : World.Island.TerrainHeight(here.x, here.z);

                Vector3 step = toTarget;
                step.y = 0f;
                float reach = shoreWalkSpeed * GroundInterval;
                if (step.sqrMagnitude > reach * reach) step = step.normalized * reach;
                Vector3 ahead = here + step;

                groundTo = World.Island.TerrainHeight(ahead.x, ahead.z);
                groundTickEnd = now + GroundInterval;
                groundHeld = true;
            }
            return Mathf.Lerp(groundTo, groundFrom,
                Mathf.Clamp01((groundTickEnd - now) / GroundInterval));
        }

        bool WalkTo(Vector3 targetLocal, float dt)
        {
            Vector3 pos = Vector3.MoveTowards(transform.localPosition, targetLocal, walkSpeed * dt);
            transform.localPosition = pos;
            return (pos - targetLocal).sqrMagnitude < 0.001f;
        }

        void ActBody(float dt)
        {
            // Queasy sway: builds with sickness, wobbles faster when sicker.
            swayPhase += dt * Mathf.Lerp(1.2f, 3.2f, Sickness01);
            float sway = Mathf.Sin(swayPhase) * maxSwayDegrees * Sickness01;

            // **Somebody else owns this body's rotation.** Every branch below
            // writes `localRotation` outright, and a puppeted hand is being
            // walked to a sawmill by `World.CampWorker` or dangled from the
            // Hand — either of which would be fought frame by frame, which is
            // exactly what made the camp's villagers face the wrong way. The
            // tint underneath is NOT guarded: it is additive, it is the only
            // thing that reads the weather on their faces, and `VillagerActing`
            // deliberately stays off that channel so nothing double-books it.
            if (Puppeted) { /* pose is not ours */ }
            else if ((heaveTimer > 0f || state == State.RailHold) && IsAtRail)
            {
                // Doubled over the rail (retching), or gripping it white-
                // knuckled through a warning — the same big lean either way.
                // Big pose: it has to read from the chase camera ~30m back,
                // not just up close.
                float heave = Mathf.Sin(Time.time * 7f) * 8f;
                float outward = Mathf.Sign(railLocal.x != 0f ? railLocal.x : 1f);
                transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f)
                    * Quaternion.Euler(52f + heave, 0f, 0f);
            }
            else if (state == State.Hauling)
            {
                // Leaning back into the line, hand over hand -- the mirror
                // of the rail-retch lean (forward and down), this one's
                // backward and up. Same "reads from the chase camera"
                // sizing as the rest of the rail poses.
                float pull = Mathf.Abs(Mathf.Sin(Time.time * 5f)) * 10f;
                float outward = Mathf.Sign(haulRailLocal.x != 0f ? haulRailLocal.x : 1f);
                transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f)
                    * Quaternion.Euler(-18f - pull, 0f, 0f);
            }
            else if (state == State.Bailing)
            {
                // Scoop and fling: bend, twist outboard, throw it over. Fast
                // and rhythmic, so a deck full of bailing crew reads as panic.
                float scoop = Mathf.Sin(Time.time * 4.2f + swayPhase);
                float outward = Mathf.Sign(stationLocal.x != 0f ? stationLocal.x : 1f);
                transform.localRotation =
                    Quaternion.Euler(0f, 40f * outward * Mathf.Max(0f, scoop), 0f)
                    * Quaternion.Euler(38f + 26f * scoop, 0f, 0f);
            }
            else if (state == State.Station)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, sway);
            }

            // Green shift: the sea's mood, written on the crew. Nothing about
            // it costs the player anything.
            // Built on demand: crew are instantiated and re-parented at
            // runtime, so Update can reach here before Start has run.
            if (block == null) block = new MaterialPropertyBlock();
            if (tintRenderers == null || tintRenderers.Length == 0)
                tintRenderers = FindSkinRenderers();

            Color tint = Color.Lerp(healthyTint, sickTint, Mathf.Clamp01(Sickness01 * 1.15f));
            block.SetColor(BaseColorId, tint);
            foreach (var r in tintRenderers)
                if (r != null) r.SetPropertyBlock(block);
        }
    }
}
