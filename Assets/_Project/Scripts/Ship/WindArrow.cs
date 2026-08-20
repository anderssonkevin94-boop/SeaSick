using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Floating 3D arrow above the ship that always points downwind — the
    /// at-a-glance wind compass. Stretches and brightens inside a gust.
    [RequireComponent(typeof(ShipMotor))]
    public class WindArrow : MonoBehaviour
    {
        [SerializeField] float hoverHeight = 9f;
        [SerializeField] float hoverBehind = 4f; // toward the stern so it clears the sails
        [SerializeField] Color calmColor = new Color(1f, 0.95f, 0.7f, 0.9f);
        [SerializeField] Color gustColor = new Color(1f, 0.75f, 0.2f, 1f);
        [SerializeField] Color noGoColor = new Color(0.95f, 0.28f, 0.22f, 1f);

        ShipMotor motor;
        Transform arrow;
        Material mat;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            motor = GetComponent<ShipMotor>();

            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor(BaseColorId, calmColor);

            var root = new GameObject("WindArrow");
            arrow = root.transform;
            arrow.SetParent(transform, false);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            Object.Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(arrow, false);
            shaft.transform.localScale = new Vector3(0.22f, 0.08f, 2.4f);
            shaft.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            shaft.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Diamond head: a cube rotated 45° reads as an arrowhead from above.
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            Object.Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(arrow, false);
            head.transform.localScale = new Vector3(0.75f, 0.08f, 0.75f);
            head.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            head.transform.localPosition = new Vector3(0f, 0f, 0.75f);
            head.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void LateUpdate()
        {
            if (arrow == null) return;

            // Sit above the ship in world space: ignore hull roll/pitch so the
            // arrow stays level and readable.
            Vector3 basePos = transform.position;
            Vector3 flatBack = -transform.forward;
            flatBack.y = 0f;
            flatBack = flatBack.sqrMagnitude < 0.01f ? Vector3.back : flatBack.normalized;
            arrow.position = basePos + Vector3.up * hoverHeight + flatBack * hoverBehind
                + Vector3.up * (Mathf.Sin(Time.time * 1.7f) * 0.25f);

            Vector2 wind = motor.WindDirection;
            arrow.rotation = Quaternion.LookRotation(new Vector3(wind.x, 0f, wind.y), Vector3.up);

            float gust = motor.GustFactor01;
            arrow.localScale = new Vector3(1f, 1f, 1f + gust * 0.5f);
            // Warms up when she's driving into a heavy sea — the only heading
            // that costs anything now, and only while the water is big.
            Color c = Color.Lerp(calmColor, gustColor, gust);
            float strain = motor.HeadSea01 * motor.SeaSeverity01;
            if (strain > 0.01f) c = Color.Lerp(c, noGoColor, Mathf.Clamp01(strain * 1.3f));
            mat.SetColor(BaseColorId, c);
        }
    }
}
