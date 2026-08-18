using UnityEngine;

namespace SeaSick.Ocean
{
    /// Slow rivers in the sea. Fixed in world space, so learning where they run
    /// is worth something: a current going your way is a highway, one against
    /// you is a wall to route around. This is what stops every heading being
    /// equivalent on an otherwise uniform blue plane.
    public class CurrentField : MonoBehaviour
    {
        public static CurrentField Instance { get; private set; }

        [System.Serializable]
        public struct Stream
        {
            public Vector2 centre;
            public Vector2 direction;
            public float speed;
            public float halfWidth;
            public float length;
        }

        [SerializeField] int streamCount = 5;
        [SerializeField] Vector2 speedRange = new Vector2(2.2f, 4.5f);
        [SerializeField] Vector2 halfWidthRange = new Vector2(90f, 190f);
        [SerializeField] float lengthScale = 900f;
        [SerializeField] float spread = 900f;
        [SerializeField] int seed = 909;

        Stream[] streams;
        public int StreamCount => streams?.Length ?? 0;
        public Stream GetStream(int i) => streams[i];

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Awake()
        {
            var rnd = new System.Random(seed);
            streams = new Stream[streamCount];
            for (int i = 0; i < streamCount; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float dist = (float)rnd.NextDouble() * spread;
                float dir = (float)rnd.NextDouble() * Mathf.PI * 2f;
                streams[i] = new Stream
                {
                    centre = new Vector2(Mathf.Sin(ang) * dist, Mathf.Cos(ang) * dist),
                    direction = new Vector2(Mathf.Sin(dir), Mathf.Cos(dir)),
                    speed = Mathf.Lerp(speedRange.x, speedRange.y, (float)rnd.NextDouble()),
                    halfWidth = Mathf.Lerp(halfWidthRange.x, halfWidthRange.y, (float)rnd.NextDouble()),
                    length = lengthScale * Mathf.Lerp(0.7f, 1.5f, (float)rnd.NextDouble()),
                };
            }
        }

        /// Water velocity at a world position, summed over every stream.
        public Vector2 Sample(Vector2 pos)
        {
            if (streams == null) return Vector2.zero;
            Vector2 total = Vector2.zero;
            for (int i = 0; i < streams.Length; i++)
            {
                var s = streams[i];
                Vector2 rel = pos - s.centre;
                float along = Vector2.Dot(rel, s.direction);
                Vector2 across2 = new Vector2(s.direction.y, -s.direction.x);
                float across = Mathf.Abs(Vector2.Dot(rel, across2));

                if (across > s.halfWidth || Mathf.Abs(along) > s.length) continue;
                // Strongest along the spine, tapering to nothing at the edges
                // and the ends, so you feel yourself slip into and out of it.
                float w = Mathf.SmoothStep(0f, 1f, 1f - across / s.halfWidth);
                float l = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(along) / s.length);
                total += s.direction * (s.speed * w * l);
            }
            return total;
        }
    }
}
