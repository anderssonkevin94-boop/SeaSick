using UnityEngine;

namespace SeaSick.Ocean
{
    /// A wave the size of a mountain: roughly 100m tall and 30m thick, running
    /// across the deep water in the west.
    ///
    /// **This is deliberately NOT part of the wave field.** A Gerstner wave
    /// self-intersects long before this steepness, and at five times the ship's
    /// length tall and narrower than it is high this is not water you ride —
    /// it is a moving cliff. So it is an obstacle with its own rules, which is
    /// what lets it have rules at all.
    ///
    /// It **mauls, it does not kill.** The way to live through one is the way
    /// you would at sea: put your bow into it. Take it on the beam and it rolls
    /// you, floods you and strips your deck. Either way you come out the far
    /// side still sailing — poorer, wetter, and short-handed.
    public class MountainSea : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField] float height = 96f;
        [SerializeField] float thickness = 30f;
        [SerializeField] float crestLength = 620f;
        [SerializeField] int segments = 26;

        [Header("Motion")]
        [SerializeField] float travelSpeed = 17f;

        [Header("What it costs you")]
        [Tooltip("Hull lost taking it perfectly bow-on.")]
        [SerializeField] float hullDamageBowOn = 0.06f;
        [Tooltip("Hull lost taking it square on the beam.")]
        [SerializeField] float hullDamageBeamOn = 0.30f;
        [SerializeField] float bilgeBowOn = 0.18f;
        [SerializeField] float bilgeBeamOn = 0.70f;
        [Tooltip("Degrees she is laid over meeting it bow-on.")]
        [SerializeField] float rollBowOn = 9f;
        [Tooltip("Degrees she is laid over caught square across it — rail under.")]
        [SerializeField] float rollBeamOn = 62f;
        [Tooltip("Cargo torn off the deck when she's caught beam-on.")]
        [SerializeField] int cargoSweptBeamOn = 12;
        [Tooltip("Fraction of way scrubbed off, bow-on .. beam-on.")]
        [SerializeField] Vector2 speedScrub = new Vector2(0.55f, 0.9f);

        /// Unit vector the wall is travelling along, in world XZ.
        public Vector2 Travel { get; private set; } = Vector2.left;
        public float Height => height;
        public float CrestLength => crestLength;
        /// Where the crest line sits right now.
        public Vector2 Centre => new Vector2(transform.position.x, transform.position.z);

        float lastHitTime = -99f;
        Ship.ShipMotor ship;
        Transform crest;

        public static MountainSea Spawn(Vector3 position, Vector2 travel, Material water)
        {
            var go = new GameObject("MountainSea");
            go.transform.position = new Vector3(position.x, 0f, position.z);
            var ms = go.AddComponent<MountainSea>();
            ms.Travel = travel.normalized;
            ms.Build(water);
            return ms;
        }

        /// A ridge of water: a long, slightly ragged wall with a steep face.
        /// Low-poly on purpose — it has to read as a silhouette on the horizon
        /// from kilometres away, which is the whole basis for avoiding it.
        void Build(Material water)
        {
            var go = new GameObject("Crest");
            crest = go.transform;
            crest.SetParent(transform, false);

            var mesh = new Mesh { name = "MountainSeaCrest" };
            int cols = Mathf.Max(4, segments);
            var verts = new Vector3[(cols + 1) * 4];
            var tris = new int[cols * 6 * 3];

            float half = crestLength * 0.5f;
            int v = 0;
            for (int i = 0; i <= cols; i++)
            {
                float t = i / (float)cols;
                float x = Mathf.Lerp(-half, half, t);

                // Ragged along its length, and lower at the ends so it reads as
                // a swell rather than a fence.
                float taper = Mathf.Sin(t * Mathf.PI);
                float ragged = 1f + Mathf.PerlinNoise(t * 4.3f, 0.37f) * 0.35f;
                float h = height * Mathf.Lerp(0.45f, 1f, taper) * ragged;

                verts[v++] = new Vector3(x, -8f, -thickness * 0.5f);          // back foot
                verts[v++] = new Vector3(x, h * 0.92f, -thickness * 0.18f);   // back shoulder
                verts[v++] = new Vector3(x, h, thickness * 0.16f);            // crest
                verts[v++] = new Vector3(x, -8f, thickness * 0.5f);           // front foot
            }

            int ti = 0;
            for (int i = 0; i < cols; i++)
            {
                int a = i * 4, b = (i + 1) * 4;
                for (int r = 0; r < 3; r++)
                {
                    tris[ti++] = a + r;     tris[ti++] = a + r + 1; tris[ti++] = b + r;
                    tris[ti++] = b + r;     tris[ti++] = a + r + 1; tris[ti++] = b + r + 1;
                }
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = water;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Face the wall across its direction of travel.
            transform.rotation = Quaternion.LookRotation(
                new Vector3(Travel.x, 0f, Travel.y), Vector3.up);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            transform.position += new Vector3(Travel.x, 0f, Travel.y) * travelSpeed * dt;

            if (ship == null) ship = FindAnyObjectByType<Ship.ShipMotor>();
            if (ship == null) return;

            CheckStrike();
        }

        /// Signed distance from the ship to the crest line, positive ahead of
        /// the wall (it has not reached her yet).
        public float SignedDistanceTo(Vector3 worldPos)
        {
            Vector2 p = new Vector2(worldPos.x, worldPos.z) - Centre;
            return Vector2.Dot(p, Travel);
        }

        /// How far along the crest the ship is, from the centre. Beyond half
        /// the length she is simply past the end of it.
        public float AlongCrest(Vector3 worldPos)
        {
            Vector2 p = new Vector2(worldPos.x, worldPos.z) - Centre;
            Vector2 along = new Vector2(-Travel.y, Travel.x);
            return Mathf.Abs(Vector2.Dot(p, along));
        }

        /// Seconds until it reaches a point, or -1 if it never will.
        public float SecondsTo(Vector3 worldPos)
        {
            float d = -SignedDistanceTo(worldPos);
            if (d <= 0f || travelSpeed <= 0.01f) return -1f;
            return d / travelSpeed;
        }

        void CheckStrike()
        {
            if (Time.time - lastHitTime < 6f) return;

            Vector3 sp = ship.transform.position;
            if (AlongCrest(sp) > crestLength * 0.5f) return;      // past the end
            if (Mathf.Abs(SignedDistanceTo(sp)) > thickness * 0.5f) return;

            lastHitTime = Time.time;
            Strike();
        }

        /// The maul. How badly depends entirely on where her bow is pointing.
        void Strike()
        {
            // The wall comes FROM the direction it travels toward, so meeting
            // it bow-on means pointing back along its travel.
            Vector3 comingFrom = new Vector3(-Travel.x, 0f, -Travel.y);
            float angle = Vector3.Angle(ship.transform.forward, comingFrom);

            // 0 = bow (or stern) into it, 1 = square on the beam. Taking it
            // stern-first is nearly as good as bow-first: what kills you is
            // presenting the whole length of the hull to it.
            float beam = Mathf.Sin(angle * Mathf.Deg2Rad);
            beam = Mathf.Clamp01(beam);

            float hull = Mathf.Lerp(hullDamageBowOn, hullDamageBeamOn, beam);
            float water = Mathf.Lerp(bilgeBowOn, bilgeBeamOn, beam);
            float roll = Mathf.Lerp(rollBowOn, rollBeamOn, beam);
            float scrub = Mathf.Lerp(speedScrub.x, speedScrub.y, beam);

            var hullInt = ship.GetComponent<Ship.HullIntegrity>();
            if (hullInt != null) hullInt.Batter(ship.transform.position, hull);

            var bilge = ship.GetComponent<Ship.Bilge>();
            if (bilge != null) bilge.Breach(water);

            // Which way she goes over is which side it hit.
            float side = Mathf.Sign(Vector3.Dot(ship.transform.right, comingFrom));
            ship.Knockdown(roll * (side >= 0f ? -1f : 1f), Mathf.Lerp(1.6f, 3.4f, beam));

            // A wall of water takes the way off her, whatever she was doing.
            ship.ScrubWay(scrub);

            foreach (var c in ship.GetComponentsInChildren<Crew.CrewAgent>())
                if (c != null) c.Jolt(Mathf.Lerp(0.05f, 0.22f, beam));

            // Caught beam-on, the deck cargo simply goes.
            if (beam > 0.55f)
            {
                var voyage = FindAnyObjectByType<Voyage.VoyageManager>();
                if (voyage != null)
                    voyage.Jettison(Mathf.RoundToInt(cargoSweptBeamOn * beam));
            }

            LastStrikeBeam01 = beam;
            LastStrikeTime = Time.time;
        }

        /// How badly the last strike caught her, for the readout and for tests.
        public static float LastStrikeBeam01 { get; private set; } = -1f;
        public static float LastStrikeTime { get; private set; } = -99f;
    }
}
