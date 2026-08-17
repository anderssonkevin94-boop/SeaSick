using UnityEngine;

namespace SeaSick.Ocean
{
    /// Draws gusts as scatters of small dark "cat's paw" ripple quads riding
    /// the water surface — the classic look of wind ruffling the sea, and the
    /// player's cue for where the fast lanes are.
    [RequireComponent(typeof(WindField), typeof(WaveField))]
    public class GustVisuals : MonoBehaviour
    {
        [SerializeField] int quadsPerGust = 24;
        [SerializeField] float quadSize = 7f;
        [SerializeField] Color rippleColor = new Color(0.02f, 0.08f, 0.14f, 0.5f);

        WindField wind;
        WaveField waves;
        Transform[] quads;
        Vector2[] localOffsets;
        int[] seededVersion;
        Material mat;

        void Start()
        {
            wind = GetComponent<WindField>();
            waves = GetComponent<WaveField>();

            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.SetColor("_BaseColor", rippleColor);
        }

        void LateUpdate()
        {
            int gustCount = wind.GustCount;
            if (gustCount == 0) return;
            int total = gustCount * quadsPerGust;

            if (quads == null || quads.Length != total)
            {
                quads = new Transform[total];
                localOffsets = new Vector2[total];
                seededVersion = new int[gustCount];
                for (int i = 0; i < total; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "GustRipple";
                    Destroy(q.GetComponent<Collider>());
                    q.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    q.transform.SetParent(transform, true);
                    quads[i] = q.transform;
                }
                for (int g = 0; g < gustCount; g++) Seed(g);
            }

            float t = Time.time;
            for (int g = 0; g < gustCount; g++)
            {
                var gust = wind.GetGust(g);
                if (seededVersion[g] != gust.version) Seed(g);

                for (int k = 0; k < quadsPerGust; k++)
                {
                    int i = g * quadsPerGust + k;
                    Vector2 p = gust.pos + localOffsets[i] * gust.radius;
                    float y = waves.SampleHeight(p, t) + 0.10f;
                    quads[i].position = new Vector3(p.x, y, p.y);
                    quads[i].rotation = Quaternion.Euler(90f, 0f, 0f);
                    quads[i].localScale = Vector3.one * quadSize;
                }
            }
        }

        void Seed(int g)
        {
            var gust = wind.GetGust(g);
            seededVersion[g] = gust.version;
            for (int k = 0; k < quadsPerGust; k++)
            {
                int i = g * quadsPerGust + k;
                localOffsets[i] = Random.insideUnitCircle * 0.85f;
            }
        }
    }
}
