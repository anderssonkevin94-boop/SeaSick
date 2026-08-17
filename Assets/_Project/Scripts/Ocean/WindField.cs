using UnityEngine;

namespace SeaSick.Ocean
{
    /// Dynamic wind: the base direction wanders slowly, and gust patches drift
    /// across the water. Inside a gust the ship sails notably faster but heels
    /// harder and rides rougher — the ocean gains lanes worth steering for.
    public class WindField : MonoBehaviour
    {
        public static WindField Instance { get; private set; }

        [Header("Base wind")]
        [SerializeField] float baseAngleDeg = 71f;   // world yaw the wind blows toward
        [SerializeField] float wanderDegrees = 28f;  // slow +/- drift
        [SerializeField] float wanderSpeed = 0.018f;

        [Header("Gusts")]
        [SerializeField] float gustStrength = 1.45f; // speed multiplier at gust center
        [SerializeField] int gustCount = 6;
        [SerializeField] Vector2 gustRadiusRange = new Vector2(22f, 40f);
        [SerializeField] float gustDriftSpeed = 4.5f;
        [SerializeField] float spawnRingMin = 100f;
        [SerializeField] float spawnRingMax = 320f;
        [SerializeField] float despawnDistance = 420f;
        [SerializeField] Transform focus; // the ship

        public struct Gust
        {
            public Vector2 pos;
            public float radius;
            public int version; // bumped on respawn so visuals can re-seed
        }

        Gust[] gusts;
        public Vector2 BaseDir { get; private set; } = new Vector2(0.95f, 0.33f);
        public int GustCount => gusts?.Length ?? 0;
        public Gust GetGust(int i) => gusts[i];
        public Transform Focus { get => focus; set => focus = value; }

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start()
        {
            gusts = new Gust[gustCount];
            for (int i = 0; i < gustCount; i++) Respawn(ref gusts[i], initial: true);
        }

        void Update()
        {
            float wander = (Mathf.PerlinNoise(Time.time * wanderSpeed, 0.37f) * 2f - 1f) * wanderDegrees;
            float a = (baseAngleDeg + wander) * Mathf.Deg2Rad;
            BaseDir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));

            Vector2 drift = BaseDir * (gustDriftSpeed * Time.deltaTime);
            Vector2 f = FocusPos();
            for (int i = 0; i < gusts.Length; i++)
            {
                gusts[i].pos += drift;
                if ((gusts[i].pos - f).sqrMagnitude > despawnDistance * despawnDistance)
                    Respawn(ref gusts[i], initial: false);
            }
        }

        Vector2 FocusPos()
        {
            if (focus == null) return Vector2.zero;
            return new Vector2(focus.position.x, focus.position.z);
        }

        void Respawn(ref Gust g, bool initial)
        {
            Vector2 f = FocusPos();
            // Respawned gusts appear upwind-biased so they drift past the ship.
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(spawnRingMin, initial ? spawnRingMax : spawnRingMax * 0.9f);
            Vector2 offset = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * dist;
            if (!initial) offset -= BaseDir * (dist * 0.6f); // bias upwind
            g.pos = f + offset;
            g.radius = Random.Range(gustRadiusRange.x, gustRadiusRange.y);
            g.version++;
        }

        /// 0 outside any gust .. 1 at a gust center.
        public float GustFactor(Vector2 pos)
        {
            if (gusts == null) return 0f;
            float best = 0f;
            for (int i = 0; i < gusts.Length; i++)
            {
                float d = (gusts[i].pos - pos).magnitude;
                float t = 1f - Mathf.Clamp01(d / gusts[i].radius);
                best = Mathf.Max(best, Mathf.SmoothStep(0f, 1f, t));
            }
            return best;
        }

        public float SampleStrength(Vector2 pos) =>
            1f + (gustStrength - 1f) * GustFactor(pos);
    }
}
