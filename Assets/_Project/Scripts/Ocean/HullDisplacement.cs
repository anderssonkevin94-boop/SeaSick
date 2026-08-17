using UnityEngine;

namespace SeaSick.Ocean
{
    /// The hull's effect on the water surface. The ocean mesh queries this and
    /// actually deforms — the ship pushes a trough under itself, shoulders a
    /// bow wave up ahead, and leaves a Kelvin wake spreading astern at the
    /// real ~19.5° half-angle. This is what "displacing water" means.
    public class HullDisplacement : MonoBehaviour
    {
        public static HullDisplacement Instance { get; private set; }

        [Header("Hull trough")]
        [SerializeField] float hullHalfLength = 11f;
        [SerializeField] float hullHalfWidth = 4.5f;
        [SerializeField] float troughDepth = 1.25f;
        [SerializeField] float troughFalloff = 12f;

        [Header("Bow wave")]
        [SerializeField] float bowOffset = 9f;
        [SerializeField] float bowHeight = 1.5f;
        [SerializeField] float bowWidth = 7f;

        [Header("Kelvin wake")]
        [Tooltip("Half-angle of the wake V. Real ships are ~19.47 degrees.")]
        [SerializeField] float wakeHalfAngleDeg = 19.47f;
        [SerializeField] float wakeLength = 110f;
        [SerializeField] float wakeCrestHeight = 0.55f;
        [SerializeField] float wakeArmWidth = 5.5f;

        Transform ship;
        Ship.ShipMotor motor;

        // Cached each frame so the mesh loop doesn't re-read transforms.
        Vector2 pos;
        Vector2 fwd;
        Vector2 right;
        float speed01;
        float influenceRadiusSq;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start()
        {
            motor = FindFirstObjectByType<Ship.ShipMotor>();
            ship = motor != null ? motor.transform : null;
            float reach = Mathf.Max(wakeLength + wakeArmWidth,
                hullHalfLength + troughFalloff + bowOffset);
            influenceRadiusSq = reach * reach;
        }

        void LateUpdate()
        {
            if (ship == null) return;
            pos = new Vector2(ship.position.x, ship.position.z);
            Vector3 f = ship.forward;
            fwd = new Vector2(f.x, f.z).normalized;
            right = new Vector2(fwd.y, -fwd.x);
            speed01 = motor != null ? Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed) : 0f;
        }

        /// Vertical offset the hull adds to the water at a world position.
        /// Called per ocean vertex, so it stays cheap: a few dot products.
        public float HeightAt(Vector2 world)
        {
            if (ship == null) return 0f;

            float wx = world.x - pos.x;
            float wz = world.y - pos.y;

            // Cheap reject: almost every ocean vertex is nowhere near the ship.
            float distSq = wx * wx + wz * wz;
            if (distSq > influenceRadiusSq) return 0f;

            float along = wx * fwd.x + wz * fwd.y;     // +forward, -astern
            float across = wx * right.x + wz * right.y;
            float absAcross = Mathf.Abs(across);

            float h = 0f;

            // 1. The hull sits in a trough it pushes down and outward.
            float lengthT = Mathf.Clamp01(1f - Mathf.Abs(along) / (hullHalfLength + troughFalloff));
            float widthT = Mathf.Clamp01(1f - absAcross / (hullHalfWidth + troughFalloff));
            float hullT = Mathf.SmoothStep(0f, 1f, lengthT) * Mathf.SmoothStep(0f, 1f, widthT);
            h -= troughDepth * hullT;

            // 2. Water shouldered up at the bow, growing with speed.
            float bowAlong = along - bowOffset;
            float bowT = Mathf.Clamp01(1f - Mathf.Abs(bowAlong) / 9f)
                       * Mathf.Clamp01(1f - absAcross / bowWidth);
            h += bowHeight * speed01 * Mathf.SmoothStep(0f, 1f, bowT);

            // 3. Kelvin wake: two crests spreading astern at a fixed angle,
            //    independent of speed — as they are in reality.
            if (along < 0f)
            {
                float behind = -along;
                if (behind < wakeLength)
                {
                    float armOffset = behind * Mathf.Tan(wakeHalfAngleDeg * Mathf.Deg2Rad);
                    float distToArm = Mathf.Abs(absAcross - armOffset);
                    float armT = Mathf.Clamp01(1f - distToArm / wakeArmWidth);
                    float fade = Mathf.Clamp01(1f - behind / wakeLength);
                    h += wakeCrestHeight * speed01 * Mathf.SmoothStep(0f, 1f, armT) * fade * fade;

                    // Shallow depression right behind the transom.
                    float sternT = Mathf.Clamp01(1f - behind / 18f)
                                 * Mathf.Clamp01(1f - absAcross / 6f);
                    h -= 0.45f * speed01 * Mathf.SmoothStep(0f, 1f, sternT);
                }
            }

            return h;
        }
    }
}
