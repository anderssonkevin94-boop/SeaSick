using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Voyage
{
    /// Floating temptations. Salvage crates give +2 timber when you steer over
    /// them — small voluntary detours, each a risk/reward call. Plain flotsam
    /// (planks) gives nothing but drifts past as a speed reference, which is
    /// half of why the sea felt static.
    public class SalvageSpawner : MonoBehaviour
    {
        [SerializeField] ShipMotor ship;
        [SerializeField] VoyageManager voyage;
        [SerializeField] int crateCount = 7;
        [SerializeField] int flotsamCount = 16;
        [SerializeField] float pickupRadius = 6f;
        [SerializeField] int salvageValue = 2;
        [SerializeField] float spawnRingMin = 80f;
        [SerializeField] float spawnRingMax = 320f;
        [SerializeField] float despawnDistance = 450f;

        Transform[] crates;
        Transform[] flotsam;
        float messageUntil;
        GUIStyle style;

        void Start()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            if (voyage == null) voyage = FindFirstObjectByType<VoyageManager>();

            var crateMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            crateMat.SetColor("_BaseColor", new Color(0.72f, 0.52f, 0.28f));
            var plankMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            plankMat.SetColor("_BaseColor", new Color(0.42f, 0.32f, 0.22f));

            crates = new Transform[crateCount];
            for (int i = 0; i < crateCount; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "SalvageCrate";
                Object.Destroy(c.GetComponent<Collider>());
                c.transform.localScale = Vector3.one * 1.1f;
                c.GetComponent<MeshRenderer>().sharedMaterial = crateMat;
                c.transform.SetParent(transform, true);
                crates[i] = c.transform;
                Respawn(crates[i]);
            }

            flotsam = new Transform[flotsamCount];
            for (int i = 0; i < flotsamCount; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = "Flotsam";
                Object.Destroy(p.GetComponent<Collider>());
                p.transform.localScale = new Vector3(0.35f, 0.15f, 2.2f);
                p.GetComponent<MeshRenderer>().sharedMaterial = plankMat;
                p.transform.SetParent(transform, true);
                flotsam[i] = p.transform;
                Respawn(flotsam[i]);
            }
        }

        void Update()
        {
            if (ship == null) return;
            var waves = WaveField.Instance;
            float t = Time.time;
            Vector3 shipPos = ship.transform.position;

            for (int i = 0; i < crates.Length; i++)
                UpdateFloater(crates[i], shipPos, waves, t, isCrate: true, i);
            for (int i = 0; i < flotsam.Length; i++)
                UpdateFloater(flotsam[i], shipPos, waves, t, isCrate: false, i);
        }

        void UpdateFloater(Transform f, Vector3 shipPos, WaveField waves, float t, bool isCrate, int seed)
        {
            Vector3 p = f.position;
            if (waves != null)
                p.y = waves.SampleHeight(new Vector2(p.x, p.z), t) + 0.15f;
            f.position = p;
            f.rotation = Quaternion.Euler(
                Mathf.Sin(t * 0.9f + seed * 2.1f) * 8f,
                seed * 47f + t * 3f,
                Mathf.Cos(t * 0.7f + seed * 1.3f) * 8f);

            Vector3 flat = p - shipPos;
            flat.y = 0f;
            float dist = flat.magnitude;

            if (isCrate && dist < pickupRadius)
            {
                voyage.AddSalvage(salvageValue);
                messageUntil = Time.time + 1.8f;
                Respawn(f);
            }
            else if (dist > despawnDistance)
            {
                Respawn(f);
            }
        }

        void Respawn(Transform f)
        {
            Vector3 basePos = ship != null ? ship.transform.position : Vector3.zero;
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(spawnRingMin, spawnRingMax);
            f.position = new Vector3(
                basePos.x + Mathf.Sin(ang) * dist, 0f,
                basePos.z + Mathf.Cos(ang) * dist);
        }

        void OnGUI()
        {
            if (Time.time > messageUntil) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 19,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            var r = new Rect(0f, Screen.height * 0.72f, Screen.width, 30f);
            GUI.Label(r, $"+{salvageValue} timber salvaged!", style);
        }
    }
}
