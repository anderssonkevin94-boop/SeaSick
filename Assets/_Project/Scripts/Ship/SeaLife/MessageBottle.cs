using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;
using SeaSick.World.Life;
using SeaSick.UI.Sheets;
using SeaSick.Ship.Overboard;

namespace SeaSick.Ship.SeaLife
{
    /// **Message in a bottle** — build item 2 of "things to find at sea"
    /// (2026-09-28). Rare alternative to a flotsam spawn (`bottleChance`).
    /// Sail over it (same `IOverboardTarget` sail-over pickup as everything
    /// else here) and it charts the nearest island the player hasn't seen
    /// yet — `Discovery.NoteGlimpse`, the same "sailed past, hatched blob,
    /// no name" state a real glimpse gives, since finding a note about an
    /// island is not the same as having anchored off it. If every island is
    /// already on the chart, it's just a line of flavour text instead.
    public class MessageBottle : MonoBehaviour, IOverboardTarget
    {
        static readonly List<MessageBottle> all = new List<MessageBottle>();
        public static IReadOnlyList<MessageBottle> All => all;

        static readonly string[] FlavourNotes =
        {
            "\"If you find this, the fish here bite at dusk.\"",
            "\"Lost my hat overboard on the 12th. If found, keep it.\"",
            "\"We are well. The wind has been kind. -- a fellow sailor\"",
            "\"Mind the reef two leagues west of here.\"",
            "\"Someone owes me a drink for finding this bottle first.\"",
        };

        float timeLeft;
        float timeTotal = 1f;
        public bool Resolved { get; private set; }
        public Vector3 WorldPosition => transform.position;
        Transform ship;
        Vector3 driftVel;

        // --- IOverboardTarget -------------------------------------------
        Transform IOverboardTarget.Transform => transform;
        public float TimeLeft01 => timeTotal > 0f ? Mathf.Clamp01(timeLeft / timeTotal) : 0f;
        string IOverboardTarget.Label => "the bottle";
        int IOverboardTarget.RescuePriority => 2;
        bool IOverboardTarget.Boardable => true;
        public Transform Hull => ship;
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }
        void IOverboardTarget.OnHauled(string rescuerName) => Recover();

        public static MessageBottle Spawn(Transform hull, Vector3 worldPos)
        {
            if (hull == null) return null;
            var go = new GameObject("MessageBottle");
            go.transform.position = worldPos;
            var b = go.AddComponent<MessageBottle>();
            b.ship = hull;
            b.timeTotal = Mathf.Max(20f, SeaLifeTuning.FlotsamLifeSeconds);
            b.timeLeft = b.timeTotal;

            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            b.driftVel = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * SeaLifeTuning.FlotsamDriftSpeed;

            b.BuildVisual();
            all.Add(b);
            return b;
        }

        void BuildVisual()
        {
            var v = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            v.name = "Bottle";
            var col = v.GetComponent<Collider>();
            if (col != null) Destroy(col);
            v.transform.SetParent(transform, false);
            v.transform.localScale = new Vector3(0.12f, 0.22f, 0.12f);
            var mr = v.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = BottleMat();
        }

        static Material bottleMat;
        static Material BottleMat()
        {
            if (bottleMat == null)
            {
                bottleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                bottleMat.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.35f, 0.75f));
            }
            return bottleMat;
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;

            Vector3 pos = BeingHauled
                ? Vector3.MoveTowards(transform.position, HaulAnchor, OverboardTuning.HaulPullSpeed * dt)
                : transform.position + driftVel * dt;

            if (Ocean.OceanSampler.Ready)
            {
                var sample = Ocean.OceanSampler.SampleImmediate(pos);
                pos.y = sample.height + 0.1f;
            }
            transform.position = pos;
            transform.Rotate(Vector3.up, dt * 25f, Space.Self);

            if (ship != null)
            {
                Vector3 toHer = pos - ship.position; toHer.y = 0f;
                Vector3 fwd = ship.forward; fwd.y = 0f;
                if (Vector3.Dot(toHer.normalized, fwd.normalized) < -0.2f
                    && toHer.magnitude > SeaLifeTuning.FlotsamCullAsternMetres)
                {
                    Cull();
                    return;
                }
            }

            timeLeft -= dt;
            if (timeLeft <= 0f) Cull();
        }

        void Cull()
        {
            if (Resolved) return;
            Resolved = true;
            Destroy(gameObject);
        }

        void Recover()
        {
            if (Resolved) return;
            Resolved = true;

            Island nearest = NearestUndiscovered(transform.position);
            if (nearest != null)
            {
                Discovery.NoteGlimpse(nearest);
                string dir = CompassDir(transform.position, nearest.transform.position);
                Banner.Show("A message in a bottle: an island to the " + dir + "!");
            }
            else
            {
                var note = FlavourNotes[Random.Range(0, FlavourNotes.Length)];
                Banner.Show(note);
            }
            Destroy(gameObject);
        }

        static Island NearestUndiscovered(Vector3 from)
        {
            Island best = null;
            float bestDist = float.MaxValue;
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                if (Discovery.Of(isle) != Seen.Never) continue;
                float d = Vector3.SqrMagnitude(isle.transform.position - from);
                if (d < bestDist) { bestDist = d; best = isle; }
            }
            return best;
        }

        public Vector3 NearestHullSide()
        {
            if (ship == null) return transform.position;
            Vector3 local = ship.InverseTransformPoint(transform.position);
            var motor = ship.GetComponent<ShipMotor>();
            float halfLen = motor != null ? Mathf.Max(1f, motor.HullLength * 0.5f) : 12f;
            float halfBeam = OverboardTuning.HullHalfBeamMetres;
            float side = Mathf.Sign(local.x != 0f ? local.x : 1f);
            float z = Mathf.Clamp(local.z, -halfLen, halfLen);
            return ship.TransformPoint(new Vector3(halfBeam * side, 0f, z));
        }

        static string CompassDir(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (ang < 0f) ang += 360f;
            string[] dirs = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            int idx = Mathf.RoundToInt(ang / 45f) % 8;
            return dirs[idx];
        }
    }
}
