using UnityEngine;

namespace SeaSick.Ocean
{
    /// A world-anchored buffer of everything disturbing the water near the
    /// player. The previous wake was an analytic shape bolted to the ship, so
    /// it rotated with you — the giveaway that the boat was sliding over a
    /// surface rather than moving through it. This keeps history instead: the
    /// wake stays where you sailed, curves through your turns, and fades.
    ///
    /// R = displacement, G = foam. Anything that touches the water can stamp
    /// into it — the hull now, cannonball splashes later, for free.
    public class WakeTexture : MonoBehaviour
    {
        public static WakeTexture Instance { get; private set; }

        [SerializeField] int resolution = 512;
        [Tooltip("World size the buffer covers, centred on the ship.")]
        [SerializeField] float worldSize = 420f;
        [SerializeField] float decayPerSecond = 0.55f;
        [SerializeField] Transform target;

        [Header("Hull stamp")]
        [SerializeField] float hullLength = 20f;
        [SerializeField] float hullWidth = 7f;
        // Per-second rates now, so the look doesn't change with framerate.
        [SerializeField] float hullFoam = 9f;
        [SerializeField] float hullDisplace = 5f;

        RenderTexture bufferA, bufferB;
        Material blitMat;
        Texture2D brush;
        Vector2 centre;
        Vector2 lastCentre;
        Ship.ShipMotor motor;

        static readonly int WakeTexId = Shader.PropertyToID("_SS_WakeTex");
        static readonly int WakeRectId = Shader.PropertyToID("_SS_WakeRect");
        static readonly int OffsetId = Shader.PropertyToID("_Offset");
        static readonly int DecayId = Shader.PropertyToID("_Decay");
        static readonly int StampColorId = Shader.PropertyToID("_StampColor");

        void OnEnable() { Instance = this; }
        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalVector(WakeRectId, Vector4.zero);
        }

        void Start()
        {
            motor = FindFirstObjectByType<Ship.ShipMotor>();
            if (target == null && motor != null) target = motor.transform;

            var fmt = RenderTextureFormat.ARGBHalf;
            if (!SystemInfo.SupportsRenderTextureFormat(fmt)) fmt = RenderTextureFormat.ARGB32;
            bufferA = Make(fmt);
            bufferB = Make(fmt);

            blitMat = new Material(Shader.Find("SeaSick/WakeBlit"));
            brush = BuildBrush(64);

            centre = target != null
                ? new Vector2(target.position.x, target.position.z) : Vector2.zero;
            lastCentre = centre;
        }

        RenderTexture Make(RenderTextureFormat fmt)
        {
            var rt = new RenderTexture(resolution, resolution, 0, fmt)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                autoGenerateMips = false,
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
            return rt;
        }

        /// Soft radial brush — the shape every stamp is drawn with.
        static Texture2D BuildBrush(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);   // smoothstep falloff
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        void LateUpdate()
        {
            if (target == null || blitMat == null) return;

            lastCentre = centre;
            // Snap the window to texel boundaries so the buffer never crawls
            // against the world as the ship moves.
            float texel = worldSize / resolution;
            var raw = new Vector2(target.position.x, target.position.z);
            centre = new Vector2(Mathf.Round(raw.x / texel) * texel, Mathf.Round(raw.y / texel) * texel);

            // 1. Scroll the previous buffer to stay world-anchored, and fade.
            Vector2 shift = (centre - lastCentre) / worldSize;
            blitMat.SetVector(OffsetId, new Vector4(shift.x, shift.y, 0f, 0f));
            blitMat.SetFloat(DecayId, Mathf.Pow(decayPerSecond, Time.deltaTime));
            Graphics.Blit(bufferA, bufferB, blitMat, 0);

            // 2. Stamp what's touching the water this frame.
            StampHull();

            (bufferA, bufferB) = (bufferB, bufferA);

            Shader.SetGlobalTexture(WakeTexId, bufferA);
            Shader.SetGlobalVector(WakeRectId,
                new Vector4(centre.x, centre.y, worldSize, 1f / worldSize));
        }

        void StampHull()
        {
            if (motor == null) return;
            float speed01 = Mathf.Clamp01(motor.CurrentSpeed / Mathf.Max(1f, motor.MaxSpeed));
            // Stamps are a RATE, not a per-frame amount. Without this the wake
            // accumulates as fast as the machine can draw frames.
            float dt = Mathf.Min(Time.deltaTime, 0.1f);

            Vector3 f = target.forward;
            Vector2 fwd = new Vector2(f.x, f.z).normalized;
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            Vector2 pos = new Vector2(target.position.x, target.position.z);

            // Lay the hull's footprint down as a short line of brush marks, so
            // successive frames join into a continuous trail.
            const int marks = 4;
            for (int i = 0; i < marks; i++)
            {
                float t = i / (float)(marks - 1) - 0.5f;
                Vector2 p = pos + fwd * (t * hullLength);
                float w = hullWidth * Mathf.Lerp(0.7f, 1.15f, 1f - Mathf.Abs(t) * 2f);
                Stamp(p, w, hullFoam * (0.35f + speed01) * dt / marks,
                      hullDisplace * (0.3f + speed01) * dt / marks);
            }

            // Shoulders throw the foam wider where the hull parts the water.
            float shoulder = hullWidth * 0.75f;
            float shoulderFoam = hullFoam * speed01 * 0.5f * dt;
            Stamp(pos + fwd * (hullLength * 0.28f) + right * shoulder,
                  hullWidth * 0.7f, shoulderFoam, 0f);
            Stamp(pos + fwd * (hullLength * 0.28f) - right * shoulder,
                  hullWidth * 0.7f, shoulderFoam, 0f);
        }

        /// Draw one contribution into the buffer at a world position.
        /// radius is in metres; foam and displace are additive amounts.
        public void Stamp(Vector2 worldPos, float radius, float foam, float displace)
        {
            if (bufferB == null || blitMat == null) return;

            Vector2 uv = (worldPos - centre) / worldSize + new Vector2(0.5f, 0.5f);
            float r = radius / worldSize;
            if (uv.x + r < 0f || uv.x - r > 1f || uv.y + r < 0f || uv.y - r > 1f) return;

            var prev = RenderTexture.active;
            RenderTexture.active = bufferB;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, 1f, 1f, 0f);
            blitMat.SetVector(StampColorId, new Vector4(displace, foam, 0f, 0f));
            // GL pixel matrix has y growing downward; flip so world +z is up.
            var rect = new Rect(uv.x - r, (1f - uv.y) - r, r * 2f, r * 2f);
            Graphics.DrawTexture(rect, brush, blitMat, 1);
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        /// Public entry point for anything else hitting the water — cannonballs,
        /// debris, a crew member going over the side.
        public static void Splash(Vector3 worldPos, float radius, float strength)
        {
            if (Instance == null) return;
            Instance.Stamp(new Vector2(worldPos.x, worldPos.z), radius, strength * 2f, strength);
        }

        void OnDestroy()
        {
            if (bufferA != null) bufferA.Release();
            if (bufferB != null) bufferB.Release();
        }
    }
}
