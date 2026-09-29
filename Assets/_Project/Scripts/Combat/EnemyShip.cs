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

        /// Raiders alive and patrolling this island. What a camp's ledger is
        /// told before every tick (`Outpost.CatchUp`), so a raid is a fact
        /// about ships the player can see on the minimap and sink, never a
        /// roll.
        public static int CountAt(SeaSick.World.Island island)
        {
            if (island == null) return 0;
            int n = 0;
            foreach (var r in All)
                if (r != null && r.Alive && r.Home == island) n++;
            return n;
        }

        /// Raid is a duty, not a mode flag, because everything that makes a
        /// raider tick already switches on duty — one more case keeps the
        /// beaching ship inside the same state machine the player has been
        /// reading since the first patrol.
        public enum Duty { Patrol, Block, Chase, Return, Raid }

        [Header("Hull")]
        [SerializeField] float hitRadius = 4.2f;   // half-beam-ish; length comes from HitAxis
        [SerializeField] int hitPoints = 8;
        [SerializeField] float length = 17f;

        [Header("Sailing")]
        [SerializeField] float maxSpeed = 17f;      // raiders keep the old sloop's legs; the paddle boat's 20 outruns them on purpose
        [SerializeField] float acceleration = 2.2f;
        [SerializeField] float turnRate = 26f;      // deg/sec at speed

        [Header("Duty")]
        [SerializeField] float orbitRadius = 90f;
        [SerializeField] float orbitLeadDeg = 26f;  // how far round the circle to steer
        [SerializeField] float alertRange = 260f;   // player this close to the island: block
        [SerializeField] float chaseRange = 130f;   // this close: go for them
        [SerializeField] float standDownRange = 340f;

        [Header("Raid")]
        // Two ways to know she has arrived, because neither is reliable alone:
        // the site's water point can be a few metres off after the hull has
        // been shoved about, and the terrain sampler is not always installed.
        [SerializeField] float beachStop = 8f;    // this close to Site.water counts as beached
        [SerializeField] float beachDepth = 3f;   // ...or this little water under her

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
        // A fleet hull fires from its own ports, and the big ones have six a
        // side. Six balls in one breath is not a broadside, it is a wall of
        // iron — the escort should read as heavier than a sloop, not as an
        // instant kill. Cap the volley and spread the chosen ports along the
        // side so the flashes still run the length of her.
        [SerializeField] int maxShotsPerVolley = 4;

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

        // **The land is read off the height field, not off a circle.**
        // Kevin, 2026-09-26: *"enemy ships are sailing through my island."*
        // Islands out here are lobed, 300-1200 m across, and the per-bearing
        // outline the old avoidance used is the FIRST water crossing from
        // the centre: every arm beyond a bay, and every neighbour that is
        // not `Island.Nearest` to the lookahead point, was open sea to a
        // raider. Measured on Kevin's save: patrol hulls had ground above
        // the keel in 11% of samples. So both the steering and the hard
        // stop now ask `Island.TerrainHeight` directly.
        [SerializeField] float draught = 3.2f;      // ground shallower than this stops her dead (1.5 m keel + a trough)
        [SerializeField] float steerDepth = 5f;     // ...and shallower than this is steered round
        [SerializeField] float planEvery = 0.3f;    // seconds between bearing fans
        // On a raid the ground this close to Site.water is hers to run up;
        // everywhere else, including the rest of the camp's own island, is
        // solid. The old exemption was the whole home island, so a raider
        // whose camp lay on the far side sailed straight across it and
        // "beached" on the first shallows she met, 200 m from the camp.
        [SerializeField] float beachZone = 45f;
        [SerializeField] float raidGiveUpSeconds = 150f;

        Island home;
        float steerOffset;   // degrees off the goal bearing the last fan chose
        float nextPlanAt;
        bool landClose;      // the last fan saw ground inside its reach
        Vector3 lastClear;   // where the hard stop last found her in water
        bool haveClear;
        float circleExtra;   // degrees further round her circle, off a neighbour's shore
        bool runIn;          // on a raid: open water all the way to the landing
        int raidSign;        // on a raid: which way round her island she is going
        float raidStartedAt;
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

        // A fleet hull has no Cannon components — the art already carries its
        // ports, so all a raider needs is the point the ball leaves from. One
        // empty child per port, parked at the barrel's mouth, so the muzzle
        // rides the hull for free and there is nothing to keep in sync.
        readonly List<Transform> portMuzzles = new List<Transform>();
        readonly List<Transform> starMuzzles = new List<Transform>();

        Transform hull;
        readonly List<Renderer> skin = new List<Renderer>();
        readonly List<Color> skinColor = new List<Color>();
        MaterialPropertyBlock mpb;
        bool flashClear = true;
        ShipMotor player;
        float nextPlayerLookup;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
        /// Which rung of the ladder she wears, or -1 for the old look (a
        /// tinted clone of whatever the player is currently sailing).
        public int LadderNode { get; private set; } = -1;
        public Island Home => home;
        public float PatrolRadius => orbitRadius;

        // ---------------------------------------------------------------- raid

        /// The landing party, once she has grounded. Owned by its own
        /// GameObject rather than parented here, so sinking the ship does not
        /// delete the men on the beach — they see `!ship.Alive` themselves,
        /// drop what they are carrying and run.
        RaidParty party;

        public bool Raiding => Current == Duty.Raid;
        /// Grounded and holding: sails come off, guns stay manned.
        public bool Beached { get; private set; }
        public RaidSite Site { get; private set; }

        /// Send her at a camp. Called by RaidDirector, which picks the site.
        public void BeginRaid(RaidSite site)
        {
            if (!Alive) return;
            Site = site;
            Current = Duty.Raid;
            raidStartedAt = Time.time;
            nextPlanAt = 0f;
            runIn = false;
            raidSign = 0;
            Beached = false;
            party = null;
        }

        /// Call the party back and put to sea. Duty.Return is deliberate: it
        /// already falls through into the patrol orbit, so a beaten raider
        /// rejoins the ring she came from without a second exit path.
        public void EndRaid()
        {
            if (!Raiding) return;
            party?.Recall();
            party = null;
            Beached = false;
            Current = Duty.Return;
        }

        /// A raider off `isle` with nothing better to do — who the director
        /// taps for the next raid. First fit, not nearest: they all share one
        /// orbit, so "nearest" would be noise.
        public static EnemyShip IdleAt(Island isle)
        {
            if (isle == null) return null;
            foreach (var r in All)
                if (r != null && r.Alive && r.Home == isle && !r.Raiding) return r;
            return null;
        }

        /// Metres of water under a point, or +inf when nothing has published a
        /// terrain sampler — an unknown bottom must never read as shallow, or
        /// a raider beaches herself in the middle of the sea.
        static float DepthAt(Vector3 p)
        {
            var h = Island.TerrainHeight;
            return h != null ? -h(p.x, p.z) : float.PositiveInfinity;
        }

        /// One registry handle per raider instead of a main-thread
        /// `SampleImmediate` per raider per frame. See RideSea for the
        /// measurement.
        Ocean.OceanProbeRegistry.Handle seaProbe;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            HitTargets.Register(this);
            EnsureProbe();
        }

        void OnDisable()
        {
            All.Remove(this);
            HitTargets.Unregister(this);
            Ocean.OceanProbeRegistry.Unregister(seaProbe);
            seaProbe = null;
        }

        /// Re-registered rather than assumed: a domain reload mid-play empties
        /// the registry's static list while the component and this field
        /// survive, so "I have a handle" is not "the driver knows about it".
        /// StormSpray.EnsureHandles documents the same asymmetry.
        void EnsureProbe()
        {
            if (seaProbe != null && Ocean.OceanProbeRegistry.Handles.Count > 0) return;
            seaProbe = Ocean.OceanProbeRegistry.Register(transform.position);
        }

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

            CollectSkin(false);
            BuildGuns();
            return true;
        }

        /// Raiders wear the approved fleet art, red hull and white canvas.
        ///
        /// The ladder rung is handed down by whoever spawned her, so a raider
        /// is a SHIP rather than a reskin: the length, the beam and the number
        /// of ports all come off the same ladder entry the player's yard
        /// reads, and a Guild escort looks like one because she IS one.
        ///
        /// `FleetVisual.Build` takes the 0-based ladder node and loads
        /// `Ship{node + 1:00}`, so nodes 7..11 are Ship08..Ship12 — the five
        /// mid stages, which is the band a raider should be sitting in.
        ///
        /// Nothing here offsets the hull vertically. The fleet meshes are
        /// exported with the waterline at Y = 0 (see FleetAssetImport's own
        /// header) and `Shipyard` parents them at localPosition zero for
        /// exactly that reason; `RideSea` already puts this transform's origin
        /// on the wave surface, so the ship floats on her marks with no
        /// correction at all. A `freeboard` shove here would lift her out of
        /// the sea, not settle her into it.
        bool BuildFromFleet()
        {
            if (LadderNode < 0) return false;

            var root = new GameObject("Hull");
            root.transform.SetParent(transform, false);

            var art = FleetVisual.Build(root.transform, LadderNode);
            if (art == null)
            {
                Debug.LogWarning(
                    $"EnemyShip: no fleet art for ladder node {LadderNode} " +
                    "(Resources/Ships/FleetV3) — falling back to the player's hull.");
                Destroy(root);
                return false;
            }

            hull = root.transform;

            // Read the art BEFORE it is stripped: the components are about to
            // go, the transforms are not, so the muzzles are pulled out now
            // and kept as plain children.
            foreach (var template in art.gunTemplates)
            {
                if (template == null) continue;
                var gun = template.GetComponent<FleetGun>();

                // The barrel already sits at the pivot height and points
                // outboard; the ball leaves its mouth. Without a barrel the
                // port itself is the best point there is.
                Transform from = gun != null && gun.barrel != null ? gun.barrel : template;
                float reach = gun != null ? gun.muzzleLength : 0f;

                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(from, false);
                muzzle.localPosition = Vector3.forward * reach;

                // Which side she bears on. Local x is ship-local because the
                // whole fleet root hangs off this transform unrotated.
                (template.localPosition.x >= 0f ? starMuzzles : portMuzzles).Add(muzzle);

                // The ports are the guns, so let them be seen: `Build` parks
                // the templates inactive because the yard uses them as
                // patterns, and a raider has no such second pass.
                template.gameObject.SetActive(true);
            }

            // Her hull is her own, not the player's: the ladder entry is the
            // single source for what a rung measures.
            var node = ShipLadder.Node(LadderNode);
            if (node != null)
            {
                length = node.length;
                hitRadius = node.beam * 0.5f + 0.6f;
            }
            else if (art.length > 0f) length = art.length;

            // Scenery from here down — anything live on it would fight
            // EnemyShip, exactly as on the cloned hull.
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) Destroy(col);

            CollectSkin(true);
            // No BuildGuns: a fleet hull's ports ARE her battery.
            return true;
        }

        /// Gather every piece that takes the raider red, and the colour it
        /// takes. Tint through property blocks rather than material
        /// instances, so the raiders still batch with the player's hull.
        ///
        /// `sparingSails` leaves the canvas out of the lists entirely rather
        /// than tinting it back to white: a renderer that is never collected
        /// is never touched by `Tint`, including during a hit flash, and white
        /// sails over a red hull is the whole silhouette.
        void CollectSkin(bool sparingSails)
        {
            GetComponentsInChildren(true, skin);
            if (sparingSails)
                skin.RemoveAll(r => r != null && (IsSail(r.name)
                    || (r.transform.parent != null && IsSail(r.transform.parent.name))));

            skinColor.Clear();
            foreach (var r in skin)
            {
                Color c = r != null && r.sharedMaterial != null
                    && r.sharedMaterial.HasProperty("_BaseColor")
                    ? r.sharedMaterial.GetColor("_BaseColor") : Color.white;
                // Kevin, 2026-09-29: "enemy ships are tinted red, please remove
                // that" -- raiders keep the fleet art's own colours; the skin is
                // still collected so the hit flash works.
                skinColor.Add(c);
            }
            Tint(0f);
        }

        static bool IsSail(string name) =>
            !string.IsNullOrEmpty(name)
            && name.IndexOf("Sail", System.StringComparison.OrdinalIgnoreCase) >= 0;

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
                if (!BuildFromFleet() && !BuildFromPlayer()) Build();
            }

            // Built on demand: script order is not guaranteed, so nothing here
            // trusts that the player existed when this spawned.
            //
            // Retried once a second, not every frame. Until the player exists
            // this scan ran per raider per frame, and Shipyard measured the
            // same call at 0.24-1.4 ms plus a share of the per-frame garbage —
            // times eight raiders. A second's delay is invisible for a ship
            // that is only ever missing during the first frames of a scene.
            if (player == null && Time.unscaledTime >= nextPlayerLookup)
            {
                player = FindFirstObjectByType<ShipMotor>();
                nextPlayerLookup = Time.unscaledTime + 1f;
            }

            Vector3 goal = DecideGoal();
            float desired = Steer(goal);
            Vector3 before = transform.position;
            SailToward(desired, dt);
            KeepClear();
            HoldOffLand(before);
            TryFire();
            RideSea(dt);
            Flash();
        }

        /// Where this raider wants to be, given what the player is doing.
        Vector3 DecideGoal()
        {
            Vector3 centre = home != null ? home.transform.position : Vector3.zero;
            Vector3 pos = transform.position;

            // A raid outranks every other duty. None of the alert/chase/
            // stand-down transitions below may run while she is on one: a
            // raider that switched to Chase halfway to the beach would turn
            // the raid into the same patrol skirmish it was meant to replace,
            // and the player would never see a landing.
            if (Raiding)
            {
                // Half her hull gone and she is done — the player can break a
                // raid by fighting the ship, which is the whole bargain.
                if (Health01 < 0.5f) { EndRaid(); return CircleGoal(centre, pos); }

                // She could not find a way round in time (a bay, a reef):
                // give it up rather than press into a coast forever.
                if (!Beached && Time.time - raidStartedAt > raidGiveUpSeconds)
                { EndRaid(); return CircleGoal(centre, pos); }

                // Shallow water only counts as arrived INSIDE the beach zone:
                // anywhere else it is a coast in the way, not the landing.
                float toSite = Flat(Site.water - pos).sqrMagnitude;
                if (!Beached
                    && (toSite <= beachStop * beachStop
                        || (toSite <= beachZone * beachZone && DepthAt(pos) < beachDepth)))
                {
                    Beached = true;
                    party = RaidParty.Begin(this);
                }
                if (Beached || Island.TerrainHeight == null) return Site.water;

                // Run in only once the line to the landing is water. Until
                // then she sails her station circle -- which clears her own
                // island by construction -- round to the landing's side. A
                // camp on the far shore used to mean a beeline at it: across
                // the island before the height-field stop, into the near
                // bay after it.
                if (Time.time >= nextPlanAt)
                {
                    Vector3 line = Flat(Site.water - pos);
                    float d = line.magnitude;
                    runIn = d < 1f || ClearRun(pos, Mathf.Atan2(line.x, line.z) * Mathf.Rad2Deg, d) >= d - 0.5f;
                }
                if (runIn) return Site.water;
                if (raidSign == 0)
                {
                    Vector3 a = Flat(pos - centre), b = Flat(Site.water - centre);
                    raidSign = Mathf.DeltaAngle(Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg,
                                                Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg) >= 0f ? 1 : -1;
                }
                return CircleGoal(centre, pos, raidSign);
            }

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
                    return CircleGoal(centre, pos);
            }
        }

        /// Steer to a point further round the circle than we are now, which
        /// gives a smooth orbit instead of the in-out weave you get from
        /// chasing the nearest point on it.
        Vector3 CircleGoal(Vector3 centre, Vector3 pos) => CircleGoal(centre, pos, patrolSign);

        Vector3 CircleGoal(Vector3 centre, Vector3 pos, int patrolSign)
        {
            Vector3 fromCentre = Flat(pos - centre);
            if (fromCentre.sqrMagnitude < 1f) fromCentre = Vector3.forward;
            float ang = Mathf.Atan2(fromCentre.x, fromCentre.z) * Mathf.Rad2Deg
                        + orbitLeadDeg * patrolSign;

            // Her station circle is sized off her own island and can run
            // across a neighbour's: aim further round it, past that shore,
            // rather than at a point on land she would circle forever.
            // Re-asked at the fan's rate, not every frame.
            if (Island.TerrainHeight != null && Time.time >= nextPlanAt)
            {
                circleExtra = 0f;
                for (int k = 0; k <= 12; k++)
                {
                    float a = (ang + k * 10f * patrolSign) * Mathf.Deg2Rad;
                    Vector3 q = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * orbitRadius;
                    if (!Solid(q.x, q.z, steerDepth)) { circleExtra = k * 10f * patrolSign; break; }
                }
            }
            ang += circleExtra;
            return centre + new Vector3(
                Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * orbitRadius;
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

            if (Island.TerrainHeight == null)
            {
                // No height field (a test scene): the old circle model.
                var isle = Island.Nearest(probe);
                if (isle != null && !(Raiding && isle == home))
                    dir = Avoid(pos, dir, isle.transform.position, isle.RadiusToward(probe));
            }

            var reef = Reef.Nearest(probe);
            if (reef != null)
                dir = Avoid(pos, dir, reef.transform.position, reef.Radius);

            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (Island.TerrainHeight == null) return want;

            // The land itself: a fan of bearings round the one she wants,
            // nearest first, and the first whose line is clear water wins.
            // Re-cast a few times a second rather than every frame -- a
            // ray is ~7 height samples, and the answer only changes as fast
            // as she can turn. Staggered per hull, and off `UnityEngine.Random`
            // so the seeded world sequence is never drawn from.
            if (Time.time >= nextPlanAt)
            {
                nextPlanAt = Time.time + planEvery * (0.8f + 0.4f * Mathf.Repeat(GetInstanceID() * 0.618f, 1f));
                float toGoal2 = Flat(goal - pos).magnitude;
                steerOffset = PlanOffset(pos, want, Mathf.Min(reach, Mathf.Max(toGoal2, 30f)));
            }
            return want + steerOffset;
        }

        /// Ground a raider must not sail over. `limit` is how deep counts as
        /// too shallow; in the raid's own landing zone only dry land is.
        ///
        /// The zone used to be exempt outright, and a raider runs in at up to
        /// 17 m/s and sheds way at 2.2 m/s² -- ~65 m to stop. Once `Beached`
        /// she coasted on over the exempt ground with nothing to undo it and
        /// stopped at the zone's edge, 30-45 m up the island (Kevin's save,
        /// second raid on Island_2: at rest with 41 m of hill above her keel).
        /// Now the shallows are hers but the waterline is a wall.
        bool Solid(float x, float z, float limit)
        {
            var h = Island.TerrainHeight;
            if (h == null) return false;
            if (Raiding)
            {
                float dx = x - Site.water.x, dz = z - Site.water.z;
                if (dx * dx + dz * dz < beachZone * beachZone) return h(x, z) > 0f;
            }
            return h(x, z) > -limit;
        }

        /// Metres of clear water along a bearing, up to `reach`.
        float ClearRun(Vector3 pos, float bearing, float reach)
        {
            const float Step = 12f;
            float r = bearing * Mathf.Deg2Rad;
            float sx = Mathf.Sin(r), sz = Mathf.Cos(r);
            for (float t = Step; t <= reach + 0.01f; t += Step)
                if (Solid(pos.x + sx * t, pos.z + sz * t, steerDepth)) return t - Step;
            return reach;
        }

        /// How far off `want` to steer so the line ahead is water. Tries the
        /// side she already favoured first, so she does not dither between
        /// two ways round the same headland.
        float PlanOffset(Vector3 pos, float want, float reach)
        {
            landClose = false;
            if (ClearRun(pos, want, reach) >= reach) return 0f;
            landClose = true;

            float first = steerOffset >= 0f ? 1f : -1f;
            float bestOffset = steerOffset, bestRun = -1f;
            for (int k = 1; k <= 12; k++)
            {
                for (int side = 0; side < 2; side++)
                {
                    float off = (side == 0 ? first : -first) * k * 15f;
                    float run = ClearRun(pos, want + off, reach);
                    if (run >= reach) return off;
                    if (run > bestRun) { bestRun = run; bestOffset = off; }
                }
            }
            if (bestRun > 0f) return bestOffset;   // boxed in: the most water ahead

            // Already in the shallows, every line blocked at its first step:
            // head for the deepest water round her, never further up the beach.
            var h = Island.TerrainHeight;
            float deepest = float.MaxValue;
            for (int k = 0; k < 12; k++)
            {
                float off = k * 30f - 180f;
                float r = (want + off) * Mathf.Deg2Rad;
                float g = h(pos.x + Mathf.Sin(r) * 15f, pos.z + Mathf.Cos(r) * 15f);
                if (g < deepest) { deepest = g; bestOffset = off; }
            }
            return bestOffset;
        }

        /// The hard stop. If this frame's move put her keel or her bow on
        /// ground she was not on a moment ago, it is undone and she loses
        /// way -- touching bottom costs, as it does the player. A hull that
        /// was already on the ground (a shove, a bad spawn) is let move, or
        /// she could never get off; the fan is already steering her out.
        void HoldOffLand(Vector3 before)
        {
            var h = Island.TerrainHeight;
            if (h == null) return;
            // Open water and nothing near: check one frame in eight. The
            // fan's rays are the early warning; this is the wall behind them,
            // and `lastClear` is what makes the skipped frames safe -- a
            // grounding is undone back to the last spot KNOWN to be water,
            // not merely to the previous frame.
            // A raid is checked every frame: she is running at a beach on
            // purpose, and there is only ever one of her.
            if (!landClose && !Raiding && ((Time.frameCount + GetInstanceID()) & 7) != 0) return;

            Vector3 f = Forward() * (length * 0.45f);
            Vector3 now = transform.position;
            if (!Solid(now.x, now.z, draught) && !Solid(now.x + f.x, now.z + f.z, draught)
                && !Solid(now.x - f.x, now.z - f.z, draught))
            {
                lastClear = now; haveClear = true;
                return;
            }

            speed *= 0.5f;
            nextPlanAt = 0f;   // re-plan now: whatever she was steering for is wrong
            if (haveClear)
            {
                transform.position = new Vector3(lastClear.x, now.y, lastClear.z);
                return;
            }
            // Never been in clear water (a bad spawn, a shove): she may move,
            // but only off the ground, never further up it.
            if (h(now.x, now.z) > h(before.x, before.z))
                transform.position = new Vector3(before.x, now.y, before.z);
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

            // Aground: she holds. The wind is still on her sails in the fiction
            // but her keel is in the sand, so the wanted speed is zero
            // outright rather than whatever the polar would have given her.
            if (Beached) target = 0f;

            speed = Mathf.MoveTowards(speed, target, acceleration * dt);
            transform.position += Forward() * speed * dt;
        }

        void RideSea(float dt)
        {
            Vector3 p = transform.position;

            // Her height comes off the ONE batched Burst query the physics
            // driver runs per step, not a main-thread sample per raider per
            // frame. Measured at sea: 14 `SampleImmediate` calls a frame
            // against the 8 the sampler's own doc budgets for, and 11 of them
            // were this line and SeaMonster's, at ~42 us each. StormSpray made
            // exactly this move and got 1.0 ms a frame back.
            //
            // The height read here is up to one physics step old. At 17 m/s
            // that is 34 cm of fetch under a hull already lerping toward the
            // surface at 7/s, and the player's own hull rides the same
            // latency — the raider is not being held to a stricter standard
            // than the ship it is chasing.
            //
            // Unlike StormSpray this does NOT gate on `sampledFrame`: that
            // gate exists because a spray handle TELEPORTS to a fresh random
            // spot, so a stale height would belong to another part of the sea
            // entirely. This handle crawls along with the hull, so the last
            // sample is always within a fraction of a metre of where she is.
            EnsureProbe();
            float surface = seaProbe.sampledFrame != 0
                ? seaProbe.sample.height
                // First frame only — nothing has been batched for this raider
                // yet and she would otherwise snap up from y=0.
                : (Ocean.OceanSampler.Ready ? Ocean.OceanSampler.SampleImmediate(p).height : 0f);
            seaProbe.position = p;

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
            var sim = Ocean.DynamicWaterSim.Instance;
            if (sim != null && speed > 0.5f)
            {
                float k = Mathf.Clamp01(speed / maxSpeed);
                sim.Stamp(new Vector2(p.x, p.z), 5.5f, 0.55f * dt * k, 0.35f * dt * k);
            }
        }

        /// Fire when a side bears and something is in reach — the player's
        /// hull or a watchtower. Same bargain the player has: no firing arc
        /// without giving up the bow, whatever she is shooting at.
        void TryFire()
        {
            if (Time.time < readyAt) return;

            // Nearest target in reach, of the two kinds a raider has: the
            // player's hull and any watchtower still standing. Nearest rather
            // than "player first", because a raider that sails past a tower
            // shooting at her to keep plinking at a distant player reads as
            // not noticing she is being shot.
            Vector3 pos = transform.position;

            float playerRange = float.MaxValue;
            if (player != null)
            {
                float d = Flat(player.transform.position - pos).magnitude;
                if (d <= gunRange && d >= 1f) playerRange = d;
            }

            float towerRange = float.MaxValue;
            WatchtowerGun tower = null;
            var towers = WatchtowerGun.All;
            for (int i = 0; towers != null && i < towers.Count; i++)
            {
                var t = towers[i];
                if (t == null || !t.Alive) continue;
                float d = Flat(t.HitCentre - pos).magnitude;
                if (d > gunRange || d < 1f || d >= towerRange) continue;
                towerRange = d; tower = t;
            }

            // On a raid the tower wins whenever one bears at all: the guns on
            // the hill are what stops a landing, so silencing them is the
            // raid's own business and the player is a distraction from it.
            bool atTower = tower != null && (Raiding || towerRange < playerRange);
            if (!atTower) tower = null;
            if (tower == null && playerRange == float.MaxValue) return;

            float dist = atTower ? towerRange : playerRange;
            Vector3 aim = AimPoint(tower, dist);
            Vector3 to = Flat(aim - pos);
            if (to.sqrMagnitude < 0.01f) return;

            float rel = Vector3.SignedAngle(Forward(), to, Vector3.up);
            bool starboard = rel >= 0f;
            if (Mathf.Abs(Mathf.DeltaAngle(rel, starboard ? 90f : -90f)) > fireArcDeg) return;

            readyAt = Time.time + reloadTime;

            var guns = starboard ? starGuns : portGuns;
            var ports = starboard ? starMuzzles : portMuzzles;
            Vector3 beam = starboard ? transform.right : -transform.right;

            if (ports.Count > 0)
            {
                // A fleet hull: the art's own ports do the shooting. There is
                // no Cannon to recoil, so she gets the roll below and nothing
                // else — a puff at each port can wait for the pass that gives
                // raiders smoke.
                //
                // Spread the picks across the side instead of taking the first
                // few, so a capped volley still flashes bow to stern rather
                // than bunching forward.
                int shots = Mathf.Min(ports.Count, Mathf.Max(1, maxShotsPerVolley));
                for (int i = 0; i < shots; i++)
                {
                    var muzzle = ports[shots == 1 ? ports.Count / 2
                        : Mathf.RoundToInt(i * (ports.Count - 1f) / (shots - 1f))];
                    if (muzzle == null) continue;
                    SeaSick.Ship.CannonBall.FireAt(
                        muzzle.position, aim, muzzleSpeed, spreadDeg, this);
                }
            }
            else if (guns.Count == 0)
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

        /// Where to point the guns for one target. A tower does not move, so
        /// there is nothing to lead and its own hit centre is the aim point; a
        /// ship gets the partial lead that makes changing course a real dodge
        /// (see leadFactor). Passing null means "the player".
        Vector3 AimPoint(WatchtowerGun tower, float dist)
        {
            if (tower != null) return tower.HitCentre;

            float flight = dist / Mathf.Max(1f, muzzleSpeed);
            return player.transform.position
                   + player.Velocity * (leadFactor * flight)
                   + Vector3.up * 1.6f;
        }

        /// The shoreline is solid, not merely discouraged. Islands are not
        /// circles, so the radius is taken on the bearing the raider is
        /// actually sitting on.
        void KeepClear()
        {
            Vector3 pos = transform.position;

            // The circle shove is the fallback for a scene with no height
            // field; with one, `HoldOffLand` is the shoreline, and a radial
            // shove off a lobed island's centre only teleported her sideways.
            if (Island.TerrainHeight == null)
            {
                var isle = Island.Nearest(pos);
                if (isle != null && !(Raiding && isle == home))
                    Shove(isle.transform.position, isle.RadiusToward(pos), hullMargin);
            }

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
                Ocean.DynamicWaterSim.Splash(transform.position, 18f, 2.6f);

                // Sunk mid-raid: deliberately NOT a Recall. A recall is an
                // orderly withdrawal; this is the ship going down under the
                // men on the beach, and the party reads `!ship.Alive` itself,
                // drops the loot and runs. Site is left as it was so they can
                // still find the shore they came up. Nothing is destroyed
                // here — Sink takes this GameObject, and the party lives on
                // its own object precisely so it outlives her.
                if (Raiding) { Beached = false; Current = Duty.Return; }
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

        static readonly Color HitFlash = new Color(1f, 0.55f, 0.25f);

        void Flash()
        {
            const float FlashTime = 0.3f;
            float since = Time.time - lastHitAt;
            if (since < FlashTime)
            {
                Tint(1f - since / FlashTime);
                flashClear = false;
            }
            else if (!flashClear)
            {
                Tint(0f);
                flashClear = true;
            }
        }

        /// How far each piece is pushed toward the hit colour, 0 = its own.
        ///
        /// A plain float, not a `Func<Color,Color>`: the old signature was fed
        /// a CAPTURING lambda (`c => Color.Lerp(c, ..., f)`) every frame of
        /// every flash, which is a closure object plus a delegate allocated
        /// per raider per frame for the whole 0.3 s. The property name is
        /// hashed once for the same reason — `SetColor("_BaseColor", ...)`
        /// hashes the string on every renderer, every frame.
        void Tint(float flash)
        {
            for (int i = 0; i < skin.Count; i++)
            {
                var r = skin[i];
                if (r == null) continue;
                Color c = skinColor[i];
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, flash > 0f ? Color.Lerp(c, HitFlash, flash) : c);
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

        /// Drop a raider on station off an island, wearing the old look: a
        /// tinted clone of the player's own hull.
        public static EnemyShip Spawn(Island island, float radius, int direction, string name)
            => Spawn(island, radius, direction, name, -1);

        /// Drop a raider on station off an island, wearing rung `ladderNode`
        /// of the fleet art. Pass -1 for the player-clone look.
        public static EnemyShip Spawn(Island island, float radius, int direction, string name,
            int ladderNode)
        {
            // Her station circle is sized off her own island alone, and in
            // a tight group it runs across the neighbours (Kevin's save:
            // Island_15's circle was 7/72 land and the raider spent her
            // watch pinned in a pocket between three islands). Widen it, up
            // to 200 m, to the ring with the least land on it.
            var hf = Island.TerrainHeight;
            if (hf != null)
            {
                float best = radius; int bestSolid = int.MaxValue;
                Vector3 c = island.transform.position;
                for (float grow = 0f; grow <= 200f; grow += 50f)
                {
                    int solid = 0;
                    for (int k = 0; k < 36; k++)
                    {
                        float a = k * 10f * Mathf.Deg2Rad;
                        if (hf(c.x + Mathf.Sin(a) * (radius + grow), c.z + Mathf.Cos(a) * (radius + grow)) > -5f) solid++;
                    }
                    if (solid < bestSolid) { bestSolid = solid; best = radius + grow; }
                    if (solid == 0) break;
                }
                radius = best;
            }

            float ang = Random.Range(0f, 360f);
            Vector3 pos = island.transform.position + new Vector3(
                Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * radius;
            // Her station circle can cross a neighbour's shore: start her in
            // deep water on it, not inside somebody else's island.
            var h = Island.TerrainHeight;
            for (int k = 1; h != null && h(pos.x, pos.z) > -6f && k < 24; k++)
            {
                float a = (ang + k * 15f) * Mathf.Deg2Rad;
                pos = island.transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
                if (h(pos.x, pos.z) <= -6f) ang += k * 15f;
            }

            var go = new GameObject(name);
            go.transform.position = pos;

            var ship = go.AddComponent<EnemyShip>();
            ship.LadderNode = ladderNode;
            ship.Configure(island, radius, direction);

            // Start along the orbit rather than spinning on spawn.
            ship.heading = ang + 90f * (direction >= 0 ? 1f : -1f);
            go.transform.rotation = Quaternion.Euler(0f, ship.heading, 0f);
            return ship;
        }
    }
}
