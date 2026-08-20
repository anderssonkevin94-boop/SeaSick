using System.Collections.Generic;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Combat
{
    /// A raider holding station off an island it considers its own.
    ///
    /// It sails rather than drives: speed comes off the same polar the player
    /// uses (ShipMotor.SailPolar), so a raider cannot point straight upwind
    /// either — it has to beat, and it slows in irons exactly as you do. That
    /// matters more than any amount of chase logic, because it means a wind
    /// advantage is a real advantage against them, and running downwind is a
    /// genuine escape rather than an animation.
    ///
    /// "Pathfinding" here is steering, not a graph search. The sea is open
    /// water with a handful of round obstacles that already publish their own
    /// radii (Island.RadiusToward, Reef.Radius), so a lookahead and a lateral
    /// shove is both cheaper and more ship-like than a grid — a raider should
    /// bear away from a shoal, not follow waypoints around it.
    public class EnemyShip : MonoBehaviour, IHittable
    {
        public static readonly List<EnemyShip> All = new List<EnemyShip>();

        public enum Duty { Patrol, Block, Chase, Return }

        [Header("Hull")]
        [SerializeField] float hitRadius = 4.2f;   // half-beam-ish; length comes from HitAxis
        [SerializeField] int hitPoints = 8;
        [SerializeField] float length = 17f;

        [Header("Sailing")]
        [SerializeField] float maxSpeed = 17f;      // a touch slower than the player's 21
        [SerializeField] float acceleration = 2.2f;
        [SerializeField] float turnRate = 26f;      // deg/sec at speed

        [Header("Duty")]
        [SerializeField] float orbitRadius = 90f;
        [SerializeField] float orbitLeadDeg = 26f;  // how far round the circle to steer
        [SerializeField] float alertRange = 260f;   // player this close to the island: block
        [SerializeField] float chaseRange = 130f;   // this close: go for them
        [SerializeField] float standDownRange = 340f;

        [Header("Guns")]
        [SerializeField] float gunRange = 60f;
        [SerializeField] float reloadTime = 5.5f;   // slower than the player's 3.2
        [SerializeField] float fireArcDeg = 20f;
        [SerializeField] float muzzleSpeed = 42f;
        // Scatter, so a raider is a threat rather than a sniper.
        [SerializeField] float spreadDeg = 3.4f;
        // Only part of the lead, on purpose: hold a straight course and you get
        // hit, change course and their shot goes where you were going to be.
        // That is the dodge, and it costs nothing to implement because the
        // player is already the one deciding the ship's heading.
        [Range(0f, 1f)] [SerializeField] float leadFactor = 0.8f;
        [SerializeField] float engageRange = 150f;
        [SerializeField] float gunStandoff = 46f;
        // Same rail positions as the player's battery, because a raider is
        // wearing the player's hull — the geometry that fits one fits the other.
        [SerializeField] Vector2 gunFore = new Vector2(1.45f, 4.8f);
        [SerializeField] Vector2 gunAft = new Vector2(1.45f, -1.2f);
        [SerializeField] float gunDeckHeight = 2.05f;
        [SerializeField] float recoilRoll = 9f;
        // Oversized on purpose. The player sees their own guns from the deck;
        // a raider is read at 46m and up, where a correctly-scaled cannon is
        // three dark pixels. Legibility at engagement range beats accuracy of
        // proportion.
        [SerializeField] float gunScale = 1.7f;

        [Header("Avoidance")]
        [SerializeField] float lookahead = 70f;
        [SerializeField] float clearance = 26f;
        // Steering alone can never guarantee clearance: a raider makes 17 m/s
        // and turns at 26 deg/s, so its turning circle is ~38m and a big
        // island is 200m across — it simply cannot always turn in time. The
        // shoreline has to be solid as well as avoided, the same way
        // HullIntegrity makes it solid for the player.
        [SerializeField] float hullMargin = 9f;
        // Hull-to-hull separation. Deliberately smaller than the ship's length
        // so two vessels can lie alongside for a broadside without being
        // shoved apart — the point is to stop them merging, not to fend them
        // off the moment they get interesting.
        [SerializeField] float hullBeam = 7.5f;

        Island home;
        int patrolSign = 1;
        int damage;
        float diedAt = -1f;
        float heading;
        float speed;
        float lastHitAt = -99f;
        float bobSeed;
        float readyAt;
        float gunHeel, gunHeelVel;

        readonly List<SeaSick.Ship.Cannon> portGuns = new List<SeaSick.Ship.Cannon>();
        readonly List<SeaSick.Ship.Cannon> starGuns = new List<SeaSick.Ship.Cannon>();

        Transform hull;
        readonly List<Renderer> skin = new List<Renderer>();
        readonly List<Color> skinColor = new List<Color>();
        MaterialPropertyBlock mpb;
        bool flashClear = true;
        ShipMotor player;

        public Vector3 HitCentre => transform.position + Vector3.up * 2.2f;
        public float HitRadius => hitRadius;
        /// A 21m hull is not a ball. Without a length here, shots at the bow
        /// or stern pass straight through and the ship reads as having no
        /// hit box at all.
        public Vector3 HitAxis => transform.forward * (length * 0.46f);
        public bool Alive => diedAt < 0f;
        public int HitPoints => hitPoints;
        public int DamageTaken => damage;
        public float Health01 => Mathf.Clamp01(1f - (float)damage / Mathf.Max(1, hitPoints));
        public Duty Current { get; private set; } = Duty.Patrol;
        public Island Home => home;
        public float PatrolRadius => orbitRadius;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            HitTargets.Register(this);
        }

        void OnDisable()
        {
            All.Remove(this);
            HitTargets.Unregister(this);
        }

        static readonly Color RaiderRed = new Color(0.62f, 0.12f, 0.10f);
        bool built;

        void Awake()
        {
            bobSeed = Random.Range(0f, 100f);
            mpb = new MaterialPropertyBlock();
        }

        public void Configure(Island island, float radius, int direction)
        {
            home = island;
            orbitRadius = radius;
            patrolSign = direction >= 0 ? 1 : -1;
        }

        // ---------------------------------------------------------------- art

        /// Raiders wear the player's hull, tinted red.
        ///
        /// Cloned from the live PlayerShip rather than loaded from a prefab so
        /// there is no asset to keep in sync — the raider is whatever the
        /// player's ship currently is. Only the three visual children are
        /// taken (Hull, MastPivot, RudderPivot); crew are separate children
        /// and are deliberately left behind.
        ///
        /// Deferred to the first Update, not Awake: script order is not
        /// guaranteed and these spawn from ArchipelagoGenerator.Awake, so the
        /// player's hull may not be reachable yet.
        bool BuildFromPlayer()
        {
            var ship = FindFirstObjectByType<ShipMotor>();
            if (ship == null) return false;

            var root = new GameObject("Hull");
            root.transform.SetParent(transform, false);
            hull = root.transform;

            bool any = false;
            foreach (var partName in new[] { "Hull", "MastPivot", "RudderPivot" })
            {
                var part = ship.transform.Find(partName);
                if (part == null) continue;

                var copy = Instantiate(part.gameObject, hull);
                copy.name = partName;
                copy.transform.localPosition = part.localPosition;
                copy.transform.localRotation = part.localRotation;
                copy.transform.localScale = part.localScale;

                // Strip behaviour and collision: this is scenery hanging off a
                // raider, and anything live on it would fight EnemyShip.
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
                foreach (var col in copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
                any = true;
            }

            if (!any) { Destroy(root); return false; }

            // Tint through property blocks rather than material instances, so
            // the raiders still batch with the player's hull.
            GetComponentsInChildren(true, skin);
            foreach (var r in skin)
            {
                Color c = r != null && r.sharedMaterial != null
                    && r.sharedMaterial.HasProperty("_BaseColor")
                    ? r.sharedMaterial.GetColor("_BaseColor") : Color.white;
                skinColor.Add(Color.Lerp(c, RaiderRed, 0.68f));
            }
            Tint(c => c);
            BuildGuns();
            return true;
        }

        /// Real guns on the rail, so the thing shooting at you is visible.
        /// Built from the same Cannon component the player uses: the mesh, the
        /// recoil and the muzzle smoke all come for free, and only the
        /// ballistics differ — a raider lays its guns by pointing the ship.
        void BuildGuns()
        {
            // Lighter than the player's iron so the guns separate from a dark
            // red hull instead of disappearing into it.
            var wood = Mat(new Color(0.34f, 0.23f, 0.14f), 0.12f);
            var iron = Mat(new Color(0.30f, 0.31f, 0.34f), 0.55f);

            MakeGun("GunPortFore", -gunFore.x, gunFore.y, -1f, portGuns, wood, iron);
            MakeGun("GunPortAft", -gunAft.x, gunAft.y, -1f, portGuns, wood, iron);
            MakeGun("GunStarFore", gunFore.x, gunFore.y, 1f, starGuns, wood, iron);
            MakeGun("GunStarAft", gunAft.x, gunAft.y, 1f, starGuns, wood, iron);
        }

        void MakeGun(string name, float x, float z, float sideSign,
            List<SeaSick.Ship.Cannon> side, Material wood, Material iron)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, gunDeckHeight, z);
            go.transform.localRotation = Quaternion.Euler(0f, sideSign * 90f, 0f);
            go.transform.localScale = Vector3.one * gunScale;

            var gun = go.AddComponent<SeaSick.Ship.Cannon>();
            gun.Build(wood, iron);
            side.Add(gun);
        }

        /// Fallback if the player's hull cannot be found — better a visible
        /// raider built from primitives than an invisible one.
        void Build()
        {
            var timber = Mat(new Color(0.19f, 0.15f, 0.13f), 0.12f);
            var trim = Mat(new Color(0.42f, 0.11f, 0.10f), 0.20f);
            var canvas = Mat(new Color(0.52f, 0.16f, 0.15f), 0.05f);
            var spar = Mat(new Color(0.33f, 0.24f, 0.15f), 0.15f);

            var root = new GameObject("Hull");
            root.transform.SetParent(transform, false);
            hull = root.transform;

            float half = length * 0.5f;

            var body = Prim(PrimitiveType.Cube, hull, new Vector3(5.0f, 2.6f, length * 0.78f), timber);
            body.transform.localPosition = new Vector3(0f, 1.0f, -0.4f);

            // Bow wedge, so the silhouette has a pointed end at gunnery range.
            var bow = Prim(PrimitiveType.Cube, hull, new Vector3(3.4f, 2.3f, 4.6f), timber);
            bow.transform.localPosition = new Vector3(0f, 1.05f, half - 2.0f);
            bow.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);

            var castle = Prim(PrimitiveType.Cube, hull, new Vector3(4.0f, 1.6f, 4.2f), timber);
            castle.transform.localPosition = new Vector3(0f, 2.5f, -half + 2.6f);

            // A red strake — the read at range is "not one of mine".
            var strake = Prim(PrimitiveType.Cube, hull, new Vector3(5.15f, 0.5f, length * 0.74f), trim);
            strake.transform.localPosition = new Vector3(0f, 2.15f, -0.4f);

            var mast = Prim(PrimitiveType.Cylinder, hull, new Vector3(0.42f, 7.5f, 0.42f), spar);
            mast.transform.localPosition = new Vector3(0f, 8.4f, 1.2f);

            var sail = Prim(PrimitiveType.Cube, hull, new Vector3(0.22f, 8.2f, 6.4f), canvas);
            sail.transform.localPosition = new Vector3(0f, 8.8f, 0.4f);

            var boom = Prim(PrimitiveType.Cylinder, hull, new Vector3(0.24f, 3.2f, 0.24f), spar);
            boom.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            boom.transform.localPosition = new Vector3(0f, 4.8f, 0.4f);

            var rudder = Prim(PrimitiveType.Cube, hull, new Vector3(0.3f, 2.2f, 1.6f), spar);
            rudder.transform.localPosition = new Vector3(0f, 0.4f, -half + 0.2f);

            GetComponentsInChildren(true, skin);
            foreach (var r in skin)
                skinColor.Add(r != null && r.sharedMaterial != null
                    ? r.sharedMaterial.GetColor("_BaseColor") : Color.white);
        }

        static Material Mat(Color c, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------ sailing

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!Alive) { Sink(dt); return; }

            if (!built)
            {
                built = true;
                if (!BuildFromPlayer()) Build();
            }

            // Built on demand: script order is not guaranteed, so nothing here
            // trusts that the player existed when this spawned.
            if (player == null) player = FindFirstObjectByType<ShipMotor>();

            Vector3 goal = DecideGoal();
            float desired = Steer(goal);
            SailToward(desired, dt);
            KeepClear();
            TryFire();
            RideSea(dt);
            Flash();
        }

        /// Where this raider wants to be, given what the player is doing.
        Vector3 DecideGoal()
        {
            Vector3 centre = home != null ? home.transform.position : Vector3.zero;
            Vector3 pos = transform.position;

            float playerToIsland = player != null
                ? Flat(player.transform.position - centre).magnitude : float.MaxValue;

            // Stand down at a longer range than we alert at, or a raider
            // dithers between duties while the player circles at the edge.
            if (Current != Duty.Patrol && playerToIsland > standDownRange) Current = Duty.Return;
            if (playerToIsland < chaseRange) Current = Duty.Chase;
            else if (playerToIsland < alertRange) Current = Duty.Block;
            else if (Current == Duty.Return || Current == Duty.Patrol) Current = Duty.Patrol;

            switch (Current)
            {
                case Duty.Chase:
                {
                    Vector3 pp = player.transform.position;
                    float range = Flat(pp - pos).magnitude;

                    // Inside gun range, stop charging and start circling. A bow
                    // carries no guns: to shoot you it has to give you its
                    // side, which is the same bargain the player is making.
                    // This is what turns a chase into an engagement.
                    if (range < engageRange)
                    {
                        Vector3 fromPlayer = Flat(pos - pp);
                        if (fromPlayer.sqrMagnitude < 1f) fromPlayer = -Forward();
                        float a = Mathf.Atan2(fromPlayer.x, fromPlayer.z) * Mathf.Rad2Deg
                                  + orbitLeadDeg * patrolSign;
                        return pp + new Vector3(
                            Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * gunStandoff;
                    }

                    // Further out, steer at where they are going.
                    return pp + player.Velocity * 1.6f;
                }

                case Duty.Block:
                    // Put the hull between the island and the intruder. That is
                    // the whole point of a guard: not to chase, but to be in
                    // the way of the thing being guarded.
                    Vector3 toPlayer = Flat(player.transform.position - centre).normalized;
                    float standoff = Mathf.Min(orbitRadius, playerToIsland * 0.62f);
                    return centre + toPlayer * standoff;

                default:
                    // Steer to a point further round the circle than we are
                    // now, which gives a smooth orbit instead of the in-out
                    // weave you get from chasing the nearest point on it.
                    Vector3 fromCentre = Flat(pos - centre);
                    if (fromCentre.sqrMagnitude < 1f) fromCentre = Vector3.forward;
                    float ang = Mathf.Atan2(fromCentre.x, fromCentre.z) * Mathf.Rad2Deg
                                + orbitLeadDeg * patrolSign;
                    return centre + new Vector3(
                        Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * orbitRadius;
            }
        }

        /// Bearing to the goal, bent away from anything solid in the way.
        float Steer(Vector3 goal)
        {
            Vector3 pos = transform.position;
            Vector3 toGoal = Flat(goal - pos);
            if (toGoal.sqrMagnitude < 0.01f) toGoal = Forward();

            Vector3 dir = toGoal.normalized;
            // Look further ahead the faster we are going — a fixed lookahead
            // is fine at steerage way and far too short at full speed.
            float reach = Mathf.Max(lookahead, speed * 5f);
            Vector3 probe = pos + dir * reach;

            // Islands are not circles, so ask for the shoreline distance on the
            // bearing we are actually approaching from. A raider's own island
            // is still solid — orbiting it must not mean sailing through it.
            var isle = Island.Nearest(probe);
            if (isle != null)
                dir = Avoid(pos, dir, isle.transform.position, isle.RadiusToward(probe));

            var reef = Reef.Nearest(probe);
            if (reef != null)
                dir = Avoid(pos, dir, reef.transform.position, reef.Radius);

            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// Push the heading sideways when the line ahead passes too close to a
        /// hazard. Sliding along the tangent rather than reversing keeps the
        /// raider making way — a ship that stops to think reads as broken.
        Vector3 Avoid(Vector3 pos, Vector3 dir, Vector3 obstacle, float radius)
        {
            Vector3 toObs = Flat(obstacle - pos);
            float dist = toObs.magnitude;
            float keepOut = radius + clearance;
            if (dist < 0.01f || dist > lookahead + keepOut) return dir;

            float along = Vector3.Dot(toObs, dir);
            if (along < 0f) return dir;                       // it is astern

            float lateral = (toObs - dir * along).magnitude;
            if (lateral > keepOut) return dir;

            // Bear away to whichever side we are already favouring.
            Vector3 side = Vector3.Cross(Vector3.up, toObs.normalized);
            if (Vector3.Dot(side, dir) < 0f) side = -side;

            float urgency = Mathf.Clamp01(1f - lateral / keepOut)
                            * Mathf.Clamp01(1f - along / (lookahead + keepOut));
            return (dir + side * (urgency * 2.2f)).normalized;
        }

        /// Turn toward the wanted bearing and take whatever speed the wind will
        /// give on that heading. If the bearing is inside the no-go zone the
        /// raider beats instead, picking the tack that makes ground.
        void SailToward(float wantedHeading, float dt)
        {
            // Raiders sail exactly the sea the player does: no polar, no no-go,
            // no tacking. They can steer anywhere, and heavy water on the bow
            // slows them by the same rule and the same amount.
            float turn = turnRate * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(speed / maxSpeed));
            float delta = Mathf.DeltaAngle(heading, wantedHeading);
            heading += Mathf.Clamp(delta, -turn * dt, turn * dt);

            float sea = ShipMotor.SeaResistanceAt(
                new Vector2(transform.position.x, transform.position.z),
                Forward(), 0.45f, out _, out _, out _);

            // Hard turns cost way, same as the player's hull.
            float target = maxSpeed * sea
                           * Mathf.Lerp(1f, 0.72f, Mathf.Clamp01(Mathf.Abs(delta) / 60f));

            speed = Mathf.MoveTowards(speed, target, acceleration * dt);
            transform.position += Forward() * speed * dt;
        }

        void RideSea(float dt)
        {
            Vector3 p = transform.position;

            float surface = Ocean2.OceanSampler.Ready
                ? Ocean2.OceanSampler.SampleImmediate(p).height : 0f;
            transform.position = new Vector3(
                p.x, Mathf.Lerp(p.y, surface, 1f - Mathf.Exp(-7f * dt)), p.z);

            gunHeelVel += (-30f * gunHeel - 3.6f * gunHeelVel) * dt;
            gunHeel += gunHeelVel * dt;

            float lean = Mathf.Sin((Time.time + bobSeed) * 0.8f) * 3.5f + gunHeel;
            float pitch = Mathf.Sin((Time.time + bobSeed) * 1.1f) * 2.2f;
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.Euler(pitch, heading, lean),
                1f - Mathf.Exp(-6f * dt));

            // A hull moving through water leaves a mark, and the sim is
            // already built for it, so a raider's wake costs nothing extra.
            var sim = Ocean2.DynamicWaterSim.Instance;
            if (sim != null && speed > 0.5f)
            {
                float k = Mathf.Clamp01(speed / maxSpeed);
                sim.Stamp(new Vector2(p.x, p.z), 5.5f, 0.55f * dt * k, 0.35f * dt * k);
            }
        }

        /// Fire when a side bears and the player is in reach. Same bargain the
        /// player has: no firing arc without giving up the bow.
        void TryFire()
        {
            if (player == null || Time.time < readyAt) return;

            Vector3 to = Flat(player.transform.position - transform.position);
            float dist = to.magnitude;
            if (dist > gunRange || dist < 1f) return;

            float rel = Vector3.SignedAngle(Forward(), to, Vector3.up);
            bool starboard = rel >= 0f;
            if (Mathf.Abs(Mathf.DeltaAngle(rel, starboard ? 90f : -90f)) > fireArcDeg) return;

            readyAt = Time.time + reloadTime;

            // Lead only partly — see leadFactor.
            float flight = dist / Mathf.Max(1f, muzzleSpeed);
            Vector3 aim = player.transform.position
                          + player.Velocity * (leadFactor * flight)
                          + Vector3.up * 1.6f;

            var guns = starboard ? starGuns : portGuns;
            Vector3 beam = starboard ? transform.right : -transform.right;

            if (guns.Count == 0)
            {
                // No hull to hang guns on: still shoot, so a fallback raider
                // is not a harmless one.
                SeaSick.Ship.CannonBall.FireAt(
                    transform.position + beam * 3.4f + Vector3.up * 2.8f,
                    aim, muzzleSpeed, spreadDeg, this);
            }
            else
            {
                foreach (var gun in guns)
                {
                    if (gun == null) continue;
                    SeaSick.Ship.CannonBall.FireAt(gun.MuzzlePoint, aim, muzzleSpeed, spreadDeg, this);
                    gun.RecoilOnly();
                }
            }

            // Her own broadside throws her over too.
            gunHeelVel += recoilRoll * (starboard ? 1f : -1f);
        }

        /// The shoreline is solid, not merely discouraged. Islands are not
        /// circles, so the radius is taken on the bearing the raider is
        /// actually sitting on.
        void KeepClear()
        {
            Vector3 pos = transform.position;

            var isle = Island.Nearest(pos);
            if (isle != null) Shove(isle.transform.position, isle.RadiusToward(pos), hullMargin);

            var reef = Reef.Nearest(pos);
            if (reef != null) Shove(reef.transform.position, reef.Radius, hullMargin * 0.5f);

            // Other hulls are solid too. Raiders share a patrol circle, so
            // without this they slowly converge and end up sailing as one
            // ship inside another.
            if (player != null) Shove(player.transform.position, hullBeam, hullBeam);
            foreach (var other in All)
            {
                if (other == null || other == this || !other.Alive) continue;
                Shove(other.transform.position, hullBeam, hullBeam);
            }
        }

        void Shove(Vector3 centre, float radius, float margin)
        {
            Vector3 d = Flat(transform.position - centre);
            float dist = d.magnitude;
            float solid = radius + margin;
            if (dist > solid || dist < 0.01f) return;

            Vector3 outward = d / dist;
            Vector3 fixedPos = centre + outward * solid;
            transform.position = new Vector3(fixedPos.x, transform.position.y, fixedPos.z);
            speed *= 0.55f;   // touching bottom costs way, as it should
        }

        Vector3 Forward() =>
            new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // --------------------------------------------------------------- hits

        public bool TakeHit(Vector3 point, float damageAmount)
        {
            if (!Alive) return false;

            damage += Mathf.Max(1, Mathf.RoundToInt(damageAmount));
            lastHitAt = Time.time;
            Splinters(point);

            if (damage >= hitPoints)
            {
                diedAt = Time.time;
                HitTargets.Unregister(this);
                Ocean2.DynamicWaterSim.Splash(transform.position, 18f, 2.6f);
            }
            return true;
        }

        void Sink(float dt)
        {
            float k = Mathf.Clamp01((Time.time - diedAt) / 5f);
            transform.position += Vector3.down * (k * 2.4f * dt);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.Euler(18f, heading, 58f), 1.1f * dt);
            if (k >= 1f) Destroy(gameObject);
        }

        void Flash()
        {
            const float FlashTime = 0.3f;
            float since = Time.time - lastHitAt;
            if (since < FlashTime)
            {
                float f = 1f - since / FlashTime;
                Tint(c => Color.Lerp(c, new Color(1f, 0.55f, 0.25f), f));
                flashClear = false;
            }
            else if (!flashClear)
            {
                Tint(c => c);
                flashClear = true;
            }
        }

        void Tint(System.Func<Color, Color> f)
        {
            for (int i = 0; i < skin.Count; i++)
            {
                var r = skin[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", f(skinColor[i]));
                r.SetPropertyBlock(mpb);
            }
        }

        void Splinters(Vector3 at)
        {
            var go = new GameObject("Splinters");
            go.transform.position = at;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.gravityModifier = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 90;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.45f, 0.34f, 0.22f), new Color(0.22f, 0.18f, 0.15f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));

            ps.Emit(30);
            Destroy(go, 2.5f);
        }

        /// Drop a raider on station off an island.
        public static EnemyShip Spawn(Island island, float radius, int direction, string name)
        {
            float ang = Random.Range(0f, 360f);
            Vector3 pos = island.transform.position + new Vector3(
                Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * radius;

            var go = new GameObject(name);
            go.transform.position = pos;

            var ship = go.AddComponent<EnemyShip>();
            ship.Configure(island, radius, direction);

            // Start along the orbit rather than spinning on spawn.
            ship.heading = ang + 90f * (direction >= 0 ? 1f : -1f);
            go.transform.rotation = Quaternion.Euler(0f, ship.heading, 0f);
            return ship;
        }
    }
}
