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
        // Persistent registry probes: two dozen floaters bobbing every frame
        // belong in the one batched ocean query, not in per-object sampling.
        OceanProbeRegistry.Handle[] crateHandles;
        OceanProbeRegistry.Handle[] flotsamHandles;
        float messageUntil;
        GUIStyle style;

        bool warnedNotBuilt;

        void Start()
        {
            try { Build(); }
            catch (System.Exception e)
            {
                // Swallowed deliberately, and REPORTED. An exception escaping
                // Start leaves the arrays null and the only visible symptom is
                // Update throwing forever, which says nothing about the cause.
                Debug.LogError("SalvageSpawner: Start failed, no salvage this session -- " + e);
            }
        }

        void Build()
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

            crateHandles = new OceanProbeRegistry.Handle[crateCount];
            for (int i = 0; i < crateCount; i++)
                crateHandles[i] = OceanProbeRegistry.Register(crates[i].position);
            flotsamHandles = new OceanProbeRegistry.Handle[flotsamCount];
            for (int i = 0; i < flotsamCount; i++)
                flotsamHandles[i] = OceanProbeRegistry.Register(flotsam[i].position);
        }

        void OnDestroy()
        {
            if (crateHandles != null)
                foreach (var h in crateHandles) OceanProbeRegistry.Unregister(h);
            if (flotsamHandles != null)
                foreach (var h in flotsamHandles) OceanProbeRegistry.Unregister(h);
        }

        void Update()
        {
            if (ship == null) return;
            // Start builds these. If it threw partway they stay null and this
            // used to throw a NullReferenceException EVERY FRAME, for the whole
            // session -- thousands of identical lines burying anything useful
            // in the console. One line, then silence.
            if (crates == null || flotsam == null)
            {
                if (!warnedNotBuilt)
                {
                    warnedNotBuilt = true;
                    Debug.LogWarning("SalvageSpawner: Start did not finish building its "
                        + "floaters, so there is no salvage this session. See the error "
                        + "logged by Start above for the cause.");
                }
                return;
            }
            float t = Time.time;
            Vector3 shipPos = ship.transform.position;

            for (int i = 0; i < crates.Length; i++)
                UpdateFloater(crates[i], shipPos, crateHandles[i], t, isCrate: true, i);
            for (int i = 0; i < flotsam.Length; i++)
                UpdateFloater(flotsam[i], shipPos, flotsamHandles[i], t, isCrate: false, i);
        }

        void UpdateFloater(Transform f, Vector3 shipPos, OceanProbeRegistry.Handle handle,
            float t, bool isCrate, int seed)
        {
            Vector3 p = f.position;
            handle.position = p;
            if (OceanSampler.Ready)
                p.y = handle.sample.height + 0.15f;
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
            var r = new Rect(0f, Screen.height * 0.62f, Screen.width, SeaSick.UI.UITheme.Unit * 2f);
            GUI.Label(r, $"+{salvageValue} timber", SeaSick.UI.UITheme.Toast);
        }
    }
}
