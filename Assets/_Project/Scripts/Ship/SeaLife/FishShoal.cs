using System.Collections.Generic;
using UnityEngine;
using SeaSick.Voyage;
using SeaSick.World;
using SeaSick.Ship.Overboard;

namespace SeaSick.Ship.SeaLife
{
    /// **Fish shoal + seabirds** — build item 3 of "things to find at sea"
    /// (2026-09-28). Gulls circling over a darker patch of water; sail slow
    /// and close and the crew fish it for a trickle of food. Shows up in
    /// `RescueHud` as a tappable, steer-to target like everything else here
    /// — but `Boardable` is false, so the sail-over pickup never tries to
    /// "board" a patch of water.
    public class FishShoal : MonoBehaviour, IOverboardTarget
    {
        static readonly List<FishShoal> all = new List<FishShoal>();
        public static IReadOnlyList<FishShoal> All => all;

        public bool Resolved { get; private set; }
        public Vector3 WorldPosition => transform.position;

        Transform ship;
        int foodTaken;
        float fishTimer;
        float lifeLeft;
        float lifeTotal = 1f;
        GameObject disc;
        readonly List<Transform> birds = new List<Transform>();
        readonly List<float> birdPhase = new List<float>();
        readonly List<float> birdRadius = new List<float>();

        // --- IOverboardTarget -------------------------------------------
        Transform IOverboardTarget.Transform => transform;
        public float TimeLeft01 => lifeTotal > 0f ? Mathf.Clamp01(lifeLeft / lifeTotal) : 0f;
        string IOverboardTarget.Label => "the fish shoal";
        int IOverboardTarget.RescuePriority => 3; // last in line -- nobody is in danger here
        bool IOverboardTarget.Boardable => false;
        public Transform Hull => ship;
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }
        void IOverboardTarget.OnHauled(string rescuerName) { } // never called -- not boardable

        public static FishShoal Spawn(Transform hull, Vector3 worldPos)
        {
            if (hull == null) return null;
            var go = new GameObject("FishShoal");
            go.transform.position = worldPos;
            var s = go.AddComponent<FishShoal>();
            s.ship = hull;
            s.lifeTotal = Mathf.Max(10f, SeaLifeTuning.ShoalTimeoutSeconds);
            s.lifeLeft = s.lifeTotal;
            s.BuildVisual();
            all.Add(s);
            return s;
        }

        void BuildVisual()
        {
            disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "ShoalPatch";
            var col = disc.GetComponent<Collider>();
            if (col != null) Destroy(col);
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = new Vector3(SeaLifeTuning.ShoalRadius * 0.85f, 0.02f, SeaLifeTuning.ShoalRadius * 0.85f);
            var mr = disc.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = PatchMat();

            int count = Random.Range(SeaLifeTuning.ShoalBirdsMin, SeaLifeTuning.ShoalBirdsMax + 1);
            for (int i = 0; i < count; i++)
            {
                var bird = new GameObject("Gull");
                bird.transform.SetParent(transform, false);
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Body";
                var bcol = body.GetComponent<Collider>();
                if (bcol != null) Destroy(bcol);
                body.transform.SetParent(bird.transform, false);
                body.transform.localScale = new Vector3(0.5f, 0.12f, 0.22f);
                var bmr = body.GetComponent<MeshRenderer>();
                if (bmr != null) bmr.sharedMaterial = GullMat();

                birds.Add(bird.transform);
                birdPhase.Add(Random.Range(0f, Mathf.PI * 2f));
                birdRadius.Add(Random.Range(SeaLifeTuning.ShoalBirdRadiusMin, SeaLifeTuning.ShoalBirdRadiusMax));
            }
        }

        static Material patchMat;
        static Material PatchMat()
        {
            if (patchMat == null)
            {
                patchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                patchMat.SetColor("_BaseColor", new Color(0.05f, 0.15f, 0.18f, 0.5f));
            }
            return patchMat;
        }

        static Material gullMat;
        static Material GullMat()
        {
            if (gullMat == null)
            {
                gullMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                gullMat.SetColor("_BaseColor", Color.white);
            }
            return gullMat;
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;

            Vector3 pos = transform.position;
            if (Ocean.OceanSampler.Ready)
            {
                var sample = Ocean.OceanSampler.SampleImmediate(pos);
                pos.y = sample.height;
                transform.position = pos;
            }

            float birdHeight = Mathf.Lerp(6f, 10f, 0.5f);
            for (int i = 0; i < birds.Count; i++)
            {
                if (birds[i] == null) continue;
                birdPhase[i] += dt * 0.6f;
                float r = birdRadius[i];
                Vector3 local = new Vector3(Mathf.Cos(birdPhase[i]) * r, birdHeight, Mathf.Sin(birdPhase[i]) * r);
                birds[i].localPosition = local;
                birds[i].localRotation = Quaternion.LookRotation(
                    new Vector3(-Mathf.Sin(birdPhase[i]), 0f, Mathf.Cos(birdPhase[i])), Vector3.up);
            }

            TickFishing(dt);

            lifeLeft -= dt;
            if (lifeLeft <= 0f || foodTaken >= SeaLifeTuning.ShoalFood) Disperse();
        }

        void TickFishing(float dt)
        {
            if (ship == null) return;
            var motor = ship.GetComponent<ShipMotor>();
            if (motor == null) return;

            float dist = Vector3.Distance(motor.transform.position, transform.position);
            bool inRange = dist <= SeaLifeTuning.ShoalRadius && motor.CurrentSpeed <= SeaLifeTuning.ShoalMaxSpeed;
            if (!inRange) { fishTimer = 0f; return; }

            fishTimer += dt;
            if (fishTimer < SeaLifeTuning.FishSeconds) return;
            fishTimer = 0f;
            if (foodTaken >= SeaLifeTuning.ShoalFood) return;

            var voyage = ship.GetComponentInParent<VoyageManager>();
            if (voyage == null) voyage = Object.FindFirstObjectByType<VoyageManager>();
            if (voyage != null)
            {
                voyage.ReturnCargo(1, Res.Food);
                var shipHold = ship.GetComponent<ShipHold>();
                if (shipHold != null) shipHold.AddVisual(Res.Food);
            }
            foodTaken++;
            Banner.Show("The crew are fishing -- " + foodTaken + " food");
        }

        void Disperse()
        {
            if (Resolved) return;
            Resolved = true;
            Destroy(gameObject);
        }

        public Vector3 NearestHullSide()
        {
            // No "boarding" side for a shoal -- just where the tap-to-steer
            // reach math measures from, so reuse the ship's own position.
            return ship != null ? ship.position : transform.position;
        }
    }
}
