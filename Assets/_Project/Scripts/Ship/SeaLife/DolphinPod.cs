using System.Collections.Generic;
using UnityEngine;
using SeaSick.Ship.Overboard;

namespace SeaSick.Ship.SeaLife
{
    /// **Dolphins at the bow** — build item 4 of "things to find at sea"
    /// (2026-09-28), the reward for sailing well: pillar #1 is sailing feel,
    /// so this is what smooth, fast sailing earns you rather than costs you.
    /// Not an `IOverboardTarget` — nothing to rescue, nothing to tap — just
    /// a visual and a static hook `CrewAgent.TrackSickness` reads so
    /// everybody's colour comes back a bit faster while the pod is here.
    public class DolphinPod : MonoBehaviour
    {
        static DolphinPod active;

        /// Read every frame by `CrewAgent.TrackSickness` — 1 with no pod
        /// present, `SeaLifeTuning.DolphinSicknessDecayMultiplier` while one
        /// is at the bow. A plain static rather than an instance lookup
        /// because crew tick every frame and there is at most one pod ever.
        public static float DecayMultiplier => active != null ? SeaLifeTuning.DolphinSicknessDecayMultiplier : 1f;
        public static bool Present => active != null;

        Transform ship;
        float lifeLeft;
        readonly List<Transform> dolphins = new List<Transform>();
        readonly List<float> phase = new List<float>();
        readonly List<float> side = new List<float>();

        public static DolphinPod Spawn(Transform hull)
        {
            if (hull == null || active != null) return null;
            var go = new GameObject("DolphinPod");
            var pod = go.AddComponent<DolphinPod>();
            pod.ship = hull;
            pod.lifeLeft = Mathf.Max(4f, SeaLifeTuning.DolphinDurationSeconds);
            active = pod;

            int count = Random.Range(SeaLifeTuning.DolphinCountMin, SeaLifeTuning.DolphinCountMax + 1);
            for (int i = 0; i < count; i++)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Dolphin";
                var col = body.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                body.transform.SetParent(go.transform, false);
                body.transform.localScale = new Vector3(0.4f, 0.9f, 0.4f);
                body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var mr = body.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = DolphinMat();

                pod.dolphins.Add(body.transform);
                pod.phase.Add(Random.Range(0f, Mathf.PI * 2f));
                pod.side.Add(i % 2 == 0 ? 1f : -1f);
            }

            Banner.Show("Dolphins at the bow");
            return pod;
        }

        static Material dolphinMat;
        static Material DolphinMat()
        {
            if (dolphinMat == null)
            {
                dolphinMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                dolphinMat.SetColor("_BaseColor", new Color(0.45f, 0.47f, 0.5f));
            }
            return dolphinMat;
        }

        void OnDestroy() { if (active == this) active = null; }

        void Update()
        {
            if (ship == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;

            for (int i = 0; i < dolphins.Count; i++)
            {
                if (dolphins[i] == null) continue;
                phase[i] += dt * 2.2f;
                // Arc in and out of the water in rhythm with the ship: a
                // sine on height, riding just off the bow's flanks, ahead
                // and slightly out.
                float bob = Mathf.Sin(phase[i]) * 0.9f - 0.2f;
                float along = 4f + Mathf.Cos(phase[i] * 0.5f) * 1.5f;
                Vector3 local = new Vector3(side[i] * 2.6f, bob, along);
                dolphins[i].position = ship.TransformPoint(local);
                dolphins[i].rotation = ship.rotation * Quaternion.Euler(Mathf.Sin(phase[i]) * 25f, 0f, 90f);
            }

            lifeLeft -= dt;
            if (lifeLeft <= 0f) Destroy(gameObject);
        }
    }
}
