using System.Collections.Generic;
using SeaSick.Ship;
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

        [Header("Heaving (cosmetic only)")]
        [Tooltip("How queasy before they start retching. They never leave their post to do it.")]
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
        [SerializeField] Renderer[] tintRenderers;
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

        /// Standing at their post and free to work it. Bailing, ashore, or
        /// over the side means no — this is the single question every ship
        /// system asks before it does anything. Being sick is NOT a reason.
        public bool Available => state == State.Station;

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
            if (IsAboard) ReturnToStation();
        }

        /// Put them on a bucket. Ignored if they aren't aboard.
        public void StartBailing()
        {
            if (state == State.Station || state == State.Returning) state = State.Bailing;
        }

        /// Take them off the buckets and send them back to their post.
        public void StopBailing()
        {
            if (state == State.Bailing) state = State.Returning;
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
            if (state == State.Station) transform.localPosition = stationLocal;
        }

        enum State
        {
            Station, Bailing, Returning,                 // aboard
            GoingAshore, ToNode, Chopping, ToShip, Idling, Boarding
        }
        State state = State.Station;

        /// Ashore covers the whole work loop, not just standing about.
        public bool IsAshore => state == State.ToNode || state == State.Chopping
            || state == State.ToShip || state == State.Idling || state == State.GoingAshore;
        public bool IsAboard => state == State.Station || state == State.Bailing
            || state == State.Returning;

        Transform ship;
        Vector3 shoreTarget;
        World.Island workIsland;
        Ship.ShipHold hold;
        Ship.Gangway gangway;
        Voyage.VoyageManager voyage;

        World.ResourceNode targetNode;
        GameObject carried;
        string carriedResource;
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
            PathToShore(landingPoint);
            state = State.GoingAshore;
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
        void SeekWork()
        {
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
            if (voyage != null && !string.IsNullOrEmpty(carriedResource))
            {
                voyage.AddLoot(1, carriedResource);
                if (hold != null) hold.AddVisual(carriedResource);
            }
            carriedResource = null;
        }

        SmoothnessMeter meter;
        Ship.ShipMotor motor;
        MaterialPropertyBlock block;
        float heaveTimer;     // time left in the current retch
        float heaveCooldown;  // time until the next one
        float pukeJitter;
        float swayPhase;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            meter = GetComponentInParent<SmoothnessMeter>();
            motor = GetComponentInParent<Ship.ShipMotor>();
            block = new MaterialPropertyBlock();
            if (tintRenderers == null || tintRenderers.Length == 0)
                tintRenderers = GetComponentsInChildren<Renderer>();
            transform.localPosition = stationLocal;
            // Seeded off the name so a given crew member always breaks at the
            // same point — variety between people, not between playthroughs.
            pukeJitter = (Mathf.Abs(DisplayName.GetHashCode() % 100) / 100f - 0.5f) * 0.08f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            TrackSickness(dt);
            TickHeaving(dt);
            RunStateMachine(dt);
            ActBody(dt);
        }

        /// Sickness now READS the sea rather than counting time on it: it
        /// eases toward however rough things are right now, quickly on the way
        /// up and slowly on the way down, so a squall turns the deck green and
        /// a calm hour gives them their colour back. Ashore they settle.
        void TrackSickness(float dt)
        {
            float target;
            if (IsAshore) target = 0f;
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

                case State.GoingAshore:
                    if (FollowPath(dt, 0.4f)) SeekWork();
                    break;

                case State.ToNode:
                    if (targetNode == null || targetNode.Harvested) { SeekWork(); break; }
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
                            targetNode.Harvest();
                            if (workIsland != null) workIsland.Extract(1f);
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
                        SeekWork();
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
                        ReturnToStation();
                    }
                    break;
            }
        }

        void ReturnToStation()
        {
            transform.localPosition = stationLocal;
            transform.localRotation = Quaternion.identity;
            heaveCooldown = heaveInterval.x;
            state = State.Station;
        }

        /// A pace inboard of their post — clear of the gun, and derived from
        /// their own station so five people never bail on the same spot.
        Vector3 BailLocal => new Vector3(
            stationLocal.x * (1f - bailStepInboard), stationLocal.y, stationLocal.z);

        /// Retching is on a timer of its own and never touches the state
        /// machine: they heave where they stand and keep working.
        void TickHeaving(float dt)
        {
            if (heaveTimer > 0f) { heaveTimer -= dt; return; }

            if (!IsAboard || Sickness01 < HeaveAt)
            {
                heaveCooldown = heaveInterval.x;
                return;
            }

            heaveCooldown -= dt;
            if (heaveCooldown <= 0f)
            {
                heaveTimer = heaveLength;
                heaveCooldown = Mathf.Lerp(heaveInterval.x, heaveInterval.y, Severity01);
                PukeCount++;
            }
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
                y = target.y;
                var isle = World.Island.Nearest(next);
                if (isle != null)
                {
                    Vector3 flat = next - isle.transform.position;
                    flat.y = 0f;
                    if (flat.magnitude > isle.Radius)
                    {
                        if (Ocean2.OceanSampler.Ready)
                            y = Ocean2.OceanSampler.SampleImmediate(next).height + 0.35f;
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

            if (heaveTimer > 0f && IsAboard)
            {
                // Doubled over the side where they stand — gun crews are at
                // their own gunport anyway. Big pose: it has to read from the
                // chase camera ~30m back, not just up close.
                float heave = Mathf.Sin(Time.time * 7f) * 8f;
                float outward = Mathf.Sign(railLocal.x != 0f ? railLocal.x : 1f);
                transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f)
                    * Quaternion.Euler(52f + heave, 0f, 0f);
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
                tintRenderers = GetComponentsInChildren<Renderer>();

            Color tint = Color.Lerp(healthyTint, sickTint, Mathf.Clamp01(Sickness01 * 1.15f));
            block.SetColor(BaseColorId, tint);
            foreach (var r in tintRenderers)
                if (r != null) r.SetPropertyBlock(block);
        }
    }
}
