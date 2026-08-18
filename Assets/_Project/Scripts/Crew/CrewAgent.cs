using System.Collections.Generic;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Crew
{
    /// One crew member on deck. Sickness fills from time at sea plus the
    /// ship's roughness (squared — smooth sailing is dramatically kinder).
    /// All acting is whole-body: green tint, sway, and the walk to the rail.
    /// Lives as a child of the ship, so deck motion comes for free.
    public class CrewAgent : MonoBehaviour
    {
        [Header("Who")]
        [SerializeField] CrewMemberDef def;

        [Header("Sickness")]
        [SerializeField] float baseRate = 0.005f;     // per second, just for being at sea
        [SerializeField] float roughnessRate = 0.12f; // per second at roughness = 1 (squared curve)
        [SerializeField] float pukeThreshold = 0.75f;
        [SerializeField] float pukeRelief = 0.25f;
        [SerializeField] float pukeDuration = 3.5f;

        [Header("Deck positions (ship-local)")]
        [SerializeField] Vector3 stationLocal = new Vector3(1.0f, 2.0f, -2.5f);
        [SerializeField] Vector3 railLocal = new Vector3(1.8f, 2.0f, -2.5f);
        [SerializeField] float walkSpeed = 1.4f;

        [Header("Shore leave")]
        [Tooltip("Sickness drains this fast per second while ashore.")]
        [SerializeField] float shoreRecoveryRate = 0.075f;
        [Tooltip("Feet on land only gets them this far without a doctor aboard.")]
        [SerializeField] float shoreRecoveryFloor = 0.2f;
        [SerializeField] float shoreAngerRecovery = 0.05f;
        [SerializeField] float shoreWalkSpeed = 6.5f;
        [SerializeField] float swingInterval = 0.42f;

        [Header("Anger (fuel for mutiny)")]
        [SerializeField] float angerRiseRate = 0.035f;   // per s while very sick
        [SerializeField] float angerDecayRate = 0.012f;  // per s once feeling better
        [SerializeField] float verySickThreshold = 0.8f;

        [Header("Acting")]
        [SerializeField] Renderer[] tintRenderers;
        [SerializeField] Color healthyTint = new Color(0.87f, 0.65f, 0.48f);
        [SerializeField] Color sickTint = new Color(0.55f, 0.78f, 0.45f);
        [SerializeField] Color angryTint = new Color(0.9f, 0.38f, 0.3f);
        [SerializeField] float maxSwayDegrees = 9f;

        public float Sickness01 { get; private set; }
        /// Fuel for mutiny: rises while very sick, cools once they feel better.
        public float Anger01 { get; private set; }
        public int PukeCount { get; private set; }
        public string StateName => state.ToString();
        public CrewMemberDef Def => def;

        /// Full recovery — called when the ship docks at home.
        public void Rest() { Sickness01 = 0f; Anger01 = 0f; }

        /// Thrown across the deck — running aground rattles them badly.
        public void Jolt(float amount)
        {
            Sickness01 = Mathf.Clamp01(Sickness01 + amount);
            Anger01 = Mathf.Clamp01(Anger01 + amount * 1.4f);
        }

        enum State
        {
            Station, ToRail, Puking, Returning,          // aboard
            GoingAshore, ToNode, Chopping, ToShip, Idling, Boarding
        }
        State state = State.Station;

        /// Ashore covers the whole work loop, not just standing about.
        public bool IsAshore => state == State.ToNode || state == State.Chopping
            || state == State.ToShip || state == State.Idling || state == State.GoingAshore;
        public bool IsAboard => state == State.Station || state == State.ToRail
            || state == State.Puking || state == State.Returning;

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

        [SerializeField] float rowingStrain = 2.1f;

        SmoothnessMeter meter;
        Ship.ShipMotor motor;
        MaterialPropertyBlock block;
        float pukeTimer;
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
        }

        void Update()
        {
            float dt = Time.deltaTime;
            AccumulateSickness(dt);
            RunStateMachine(dt);
            ActBody(dt);
        }

        void AccumulateSickness(float dt)
        {
            // Feet on solid ground is the cure — the walk over doesn't count.
            // Working ashore counts: they're on land the whole time.
            if (state == State.ToNode || state == State.Chopping
                || state == State.ToShip || state == State.Idling)
            {
                Sickness01 = Mathf.Max(shoreRecoveryFloor, Sickness01 - shoreRecoveryRate * dt);
                Anger01 = Mathf.Max(0f, Anger01 - shoreAngerRecovery * dt);
                return;
            }

            // Clambering off or back aboard: neither cured nor punished.
            if (state == State.GoingAshore || state == State.Boarding) return;

            float rough = meter != null ? meter.Roughness01 : 0f;
            float resistance = def != null ? def.ironStomach : 0f;
            float rate = (baseRate + roughnessRate * rough * rough) * (1f - 0.5f * resistance);

            // Rowing is hard labour: it always gets you home, but it wears the
            // crew out much faster than sailing does.
            if (motor != null && motor.Rowing) rate *= rowingStrain;
            Sickness01 = Mathf.Clamp01(Sickness01 + rate * dt);

            // Puking drops sickness below the anger threshold, so letting them
            // puke literally buys off the mutiny for a while.
            if (Sickness01 >= verySickThreshold)
                Anger01 = Mathf.Clamp01(Anger01 + angerRiseRate * dt);
            else if (Sickness01 < 0.6f)
                Anger01 = Mathf.Clamp01(Anger01 - angerDecayRate * dt);
        }

        void RunStateMachine(float dt)
        {
            switch (state)
            {
                case State.Station:
                    if (Sickness01 >= pukeThreshold) state = State.ToRail;
                    break;

                case State.ToRail:
                    if (WalkTo(railLocal, dt))
                    {
                        state = State.Puking;
                        pukeTimer = pukeDuration;
                        // Face overboard.
                        float outward = Mathf.Sign(railLocal.x);
                        transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f);
                    }
                    break;

                case State.Puking:
                    pukeTimer -= dt;
                    if (pukeTimer <= 0f)
                    {
                        Sickness01 = Mathf.Max(0f, Sickness01 - pukeRelief);
                        PukeCount++;
                        state = State.Returning;
                    }
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
                    if (ship == null) { state = State.Station; break; }
                    // The last waypoint is the station, but the ship is moving
                    // on the swell, so keep it current as we approach.
                    RefreshShipWaypoints(ship.TransformPoint(stationLocal), true);
                    if (FollowPath(dt, 0.35f))
                    {
                        transform.SetParent(ship, true);
                        transform.localPosition = stationLocal;
                        transform.localRotation = Quaternion.identity;
                        state = State.Station;
                    }
                    break;
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
                        var waves = Ocean.WaveField.Instance;
                        if (waves != null)
                            y = waves.SampleHeightFast(new Vector2(next.x, next.z), Time.time) + 0.35f;
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

            if (state == State.Puking)
            {
                // Lean over the rail, heaving. Big pose: must read from the
                // gameplay camera ~30m back, not just up close.
                float heave = Mathf.Sin(Time.time * 7f) * 8f;
                float outward = Mathf.Sign(railLocal.x);
                transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f)
                    * Quaternion.Euler(52f + heave, 0f, 0f);
            }
            else if (state == State.Station)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, sway);
            }

            // Green shift readable well before the puke threshold; anger adds
            // a red flush on top so a brewing mutiny is visible on bodies.
            // Built on demand: crew are instantiated and re-parented at
            // runtime, so Update can reach here before Start has run.
            if (block == null) block = new MaterialPropertyBlock();
            if (tintRenderers == null || tintRenderers.Length == 0)
                tintRenderers = GetComponentsInChildren<Renderer>();

            Color tint = Color.Lerp(healthyTint, sickTint, Mathf.Clamp01(Sickness01 * 1.15f));
            tint = Color.Lerp(tint, angryTint, Anger01 * 0.8f);
            block.SetColor(BaseColorId, tint);
            foreach (var r in tintRenderers)
                if (r != null) r.SetPropertyBlock(block);
        }
    }
}
