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
        // A big, slow rotation rather than jitter: over a voyage the wind
        // genuinely changes which islands are upwind of you, so a route that
        // was a beat on the way out can be a reach on the way back.
        [SerializeField] float wanderDegrees = 62f;
        [SerializeField] float wanderSpeed = 0.010f;

        [Header("Wind shadow")]
        [Tooltip("How far downwind of an island the air stays disturbed.")]
        [SerializeField] float shadowLength = 260f;
        [SerializeField, Range(0f, 1f)] float shadowStrength = 0.72f;

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
            (1f + (gustStrength - 1f) * GustFactor(pos)) * ShadowFactor(pos);

        /// Islands block the wind. Sailing into the lee of a big one robs you
        /// of drive, which turns the land into something you route around
        /// rather than just past.
        public float ShadowFactor(Vector2 pos)
        {
            float factor = 1f;
            foreach (var isle in World.Island.All)
            {
                if (isle == null) continue;
                Vector3 c = isle.transform.position;
                Vector2 rel = pos - new Vector2(c.x, c.z);
                // Downwind of the island means along the direction it blows.
                float along = Vector2.Dot(rel, BaseDir);
                float radius = isle.MaxRadius;
                // Reach is measured from the far shore, not the centre — a big
                // island otherwise swallows its own shadow.
                float reach = radius + shadowLength;
                if (along <= 0f || along > reach) continue;

                Vector2 acrossDir = new Vector2(BaseDir.y, -BaseDir.x);
                float across = Mathf.Abs(Vector2.Dot(rel, acrossDir));
                // The shadow spreads and thins as it trails away.
                float width = radius * Mathf.Lerp(1f, 1.7f, along / reach);
                if (across > width) continue;

                float lateral = 1f - across / width;
                float fade = 1f - along / reach;
                factor = Mathf.Min(factor,
                    1f - shadowStrength * Mathf.SmoothStep(0f, 1f, lateral) * Mathf.SmoothStep(0f, 1f, fade));
            }
            return factor;
        }
    }
}
