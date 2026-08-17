using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Foam streaks lying on the water near the ship. They don't move — the
    /// ship moves past them — which is exactly the point: an empty ocean gives
    /// the eye nothing to measure speed against. Recycled from behind to ahead.
    public class SeaStreaks : MonoBehaviour
    {
        [SerializeField] int streakCount = 46;
        [SerializeField] float spawnRadius = 85f;
        [SerializeField] float recycleBehind = 60f;
        [SerializeField] Vector2 sizeRange = new Vector2(3.5f, 10f);
        [SerializeField] Color foamColor = new Color(1f, 1f, 1f, 0.14f);

        Transform target;
        WaveField waves;
        Transform[] streaks;
        float[] widths;

        void Start()
        {
            var motor = FindFirstObjectByType<ShipMotor>();
            target = motor != null ? motor.transform : null;
            waves = WaveField.Instance != null ? WaveField.Instance : FindFirstObjectByType<WaveField>();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3010;
            mat.SetColor("_BaseColor", foamColor);

            streaks = new Transform[streakCount];
            widths = new float[streakCount];
            for (int i = 0; i < streakCount; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "FoamStreak";
                Destroy(q.GetComponent<Collider>());
                q.GetComponent<MeshRenderer>().sharedMaterial = mat;
                q.transform.SetParent(transform, true);
                streaks[i] = q.transform;
                widths[i] = Random.Range(0.5f, 1.4f);
                Place(i, initial: true);
            }
        }

        void Place(int i, bool initial)
        {
            if (target == null) return;
            Vector3 basePos = target.position;
            Vector3 fwd = target.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;

            // Sprinkle ahead of the bow (or all around on first fill) so the
            // band the player is sailing into always has reference points.
            float along = initial
                ? Random.Range(-recycleBehind, spawnRadius)
                : Random.Range(spawnRadius * 0.45f, spawnRadius);
            float across = Random.Range(-spawnRadius * 0.75f, spawnRadius * 0.75f);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 p = basePos + fwd * along + right * across;
            streaks[i].position = new Vector3(p.x, 0f, p.z);

            float len = Random.Range(sizeRange.x, sizeRange.y);
            streaks[i].localScale = new Vector3(len, widths[i], 1f);
        }

        void LateUpdate()
        {
            if (target == null || streaks == null) return;
            float t = Time.time;
            Vector3 fwd = target.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;

            for (int i = 0; i < streaks.Length; i++)
            {
                Vector3 p = streaks[i].position;
                Vector3 offset = p - target.position;
                offset.y = 0f;

                if (Vector3.Dot(offset, fwd) < -recycleBehind || offset.magnitude > spawnRadius * 1.6f)
                {
                    Place(i, initial: false);
                    p = streaks[i].position;
                }

                float h = waves != null ? waves.SampleHeightFast(new Vector2(p.x, p.z), t) : 0f;
                streaks[i].position = new Vector3(p.x, h + 0.08f, p.z);
                streaks[i].rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }
    }
}
