using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// A plank run out to the beach once the ship is alongside, so the crew
    /// walk ashore instead of wading. Extends when anchored, withdraws when
    /// you weigh.
    public class Gangway : MonoBehaviour
    {
        [SerializeField] float width = 1.5f;
        [SerializeField] float thickness = 0.18f;
        [SerializeField] float deckHeight = 1.9f;
        [SerializeField] float extendSpeed = 3.5f;

        Transform plank;
        Material mat;
        float extension;       // 0 stowed, 1 fully run out
        Vector3 shoreTarget;
        bool wanted;

        public bool Ready => extension > 0.98f;
        /// Where the plank meets the land — crew step off here.
        public Vector3 LandingPoint => shoreTarget;
        /// Where the plank meets the ship.
        public Vector3 DeckPoint => transform.TransformPoint(new Vector3(0f, deckHeight, 0f));

        void Start()
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.52f, 0.36f, 0.20f));
            mat.SetFloat("_Smoothness", 0.1f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Plank";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            plank = go.transform;
            plank.SetParent(transform, true);
            go.SetActive(false);
        }

        /// Run the plank out toward an island.
        public void Extend(Island isle)
        {
            if (isle == null) { Withdraw(); return; }
            wanted = true;

            Vector3 from = transform.position;
            Vector3 toIsle = from - isle.transform.position;
            toIsle.y = 0f;
            float ang = Mathf.Atan2(toIsle.x, toIsle.z);
            // Aim at the waterline on our bearing, then a touch inland so the
            // plank lands on sand rather than in the shallows.
            shoreTarget = isle.SurfacePoint(ang, isle.RadiusAt(ang) * 0.97f);
        }

        public void Withdraw() { wanted = false; }

        void LateUpdate()
        {
            if (plank == null) return;

            float target = wanted ? 1f : 0f;
            extension = Mathf.MoveTowards(extension, target, extendSpeed * Time.deltaTime);

            if (extension <= 0.001f)
            {
                if (plank.gameObject.activeSelf) plank.gameObject.SetActive(false);
                return;
            }
            if (!plank.gameObject.activeSelf) plank.gameObject.SetActive(true);

            Vector3 deck = DeckPoint;
            Vector3 span = shoreTarget - deck;
            float full = span.magnitude;
            if (full < 0.1f) return;

            Vector3 dir = span / full;
            float length = full * extension;
            Vector3 mid = deck + dir * (length * 0.5f);

            plank.position = mid;
            plank.rotation = Quaternion.LookRotation(dir, Vector3.up);
            plank.localScale = new Vector3(width, thickness, length);
        }
    }
}
