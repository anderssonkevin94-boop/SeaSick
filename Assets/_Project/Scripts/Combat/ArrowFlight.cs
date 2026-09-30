using System;
using UnityEngine;

namespace SeaSick.Combat
{
    /// <summary>
    /// **One arrow in the air (bows, 2026-09-30, docs/GDD.md "Bows").**
    ///
    /// Pure show, like `Impact`: the books (`World.OutpostLedger.SpendArrow`)
    /// have already spent the arrow and the caller has already rolled the
    /// hit; this flies a chunky shaft along a shallow arc from the bow to the
    /// mark so the player SEES the shot, then calls `onArrive` -- which is
    /// where a caller lands the damage, so a hit reads as the arrow striking,
    /// not as the bow twanging. A hit sticks in the target (parented to it,
    /// so it rides a walking raider or a rolling hull) for a moment; a miss
    /// stands in the ground or is gone in the water.
    ///
    /// Sized for the phone: a real arrow is a pencil line at camp zoom, so
    /// the shaft is thicker than life and the fletching is a bright red.
    /// </summary>
    public class ArrowFlight : MonoBehaviour
    {
        /// Metres of arc height per metre flown: a shallow lob, not a
        /// mortar -- enough to read as an arrow over a wall.
        const float ArcPerMetre = 0.12f;
        /// Seconds a stuck or grounded arrow stays before it is gone.
        const float StaySeconds = 1.6f;
        /// The shortest flight, so a point-blank shot is still seen.
        const float MinFlightSeconds = 0.12f;

        Vector3 from, to;
        float flight, t, arc, stayLeft = -1f;
        Action onArrive;
        Transform stickTo;

        /// **Loose one arrow** from `from` at `to`. `onArrive` runs once, the
        /// frame it lands. `stickTo` (the target, or null for a miss) is what
        /// it sticks in; `scale` = 1 is camp size, bigger at sea.
        public static ArrowFlight Loose(Vector3 from, Vector3 to, Action onArrive = null,
            Transform stickTo = null, float scale = 1f)
        {
            var go = new GameObject("Arrow");
            go.transform.position = from;
            var a = go.AddComponent<ArrowFlight>();
            a.from = from;
            a.to = to;
            float dist = Vector3.Distance(from, to);
            a.flight = Mathf.Max(MinFlightSeconds, dist / RaidFightTuning.ArrowSpeed);
            a.arc = dist * ArcPerMetre;
            a.onArrive = onArrive;
            a.stickTo = stickTo;
            a.Build(Mathf.Max(0.1f, scale));
            a.Place(0f);
            return a;
        }

        void Update()
        {
            if (stayLeft >= 0f)
            {
                stayLeft -= Time.deltaTime;
                if (stayLeft < 0f) Destroy(gameObject);
                return;
            }
            // A target that sank or faded mid-flight: the arrow still lands
            // where it was going.
            t += Time.deltaTime;
            if (t < flight) { Place(t / flight); return; }

            Place(1f);
            var cb = onArrive;
            onArrive = null;
            try { cb?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            if (stickTo != null) transform.SetParent(stickTo, true);
            stayLeft = StaySeconds;
        }

        /// Along the arc at `k` (0..1), pointed down the way it is flying.
        void Place(float k)
        {
            Vector3 p = Vector3.Lerp(from, to, k) + Vector3.up * (4f * arc * k * (1f - k));
            float k2 = Mathf.Min(1f, k + 0.02f);
            Vector3 ahead = Vector3.Lerp(from, to, k2) + Vector3.up * (4f * arc * k2 * (1f - k2));
            Vector3 dir = k2 > k ? ahead - p : to - from;
            if (dir.sqrMagnitude < 1e-6f) dir = to - from;
            transform.position = p;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        // --- the look -----------------------------------------------------------

        static Material shaftMat, headMat, featherMat, puffMat;

        void Build(float scale)
        {
            if (shaftMat == null) shaftMat = Mat(World.Res.Colour(World.Res.Arrows));
            if (headMat == null) headMat = Mat(new Color(0.22f, 0.22f, 0.24f));
            if (featherMat == null) featherMat = Mat(new Color(0.86f, 0.18f, 0.14f));
            // Tip at +Z: the shaft ends at the origin (the point that lands).
            Part(new Vector3(0.045f, 0.045f, 0.85f) * scale, new Vector3(0f, 0f, -0.425f) * scale, shaftMat);
            Part(new Vector3(0.08f, 0.08f, 0.12f) * scale, new Vector3(0f, 0f, 0.02f) * scale, headMat);
            Part(new Vector3(0.16f, 0.015f, 0.18f) * scale, new Vector3(0f, 0f, -0.76f) * scale, featherMat);
            Part(new Vector3(0.015f, 0.16f, 0.18f) * scale, new Vector3(0f, 0f, -0.76f) * scale, featherMat);
        }

        void Part(Vector3 size, Vector3 at, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static Material Mat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        /// **The hit effect**: a short burst of splinters (a hull) or dust (a
        /// raider) where an arrow bites. Small on purpose -- a cannonball is
        /// `Impact.Burst`; an arrow is a tick of wood chips.
        public static void Puff(Vector3 at, Color colour, float scale = 1f, int count = 8)
        {
            var go = new GameObject("ArrowHit");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.35f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 5f * scale);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, count);
            main.playOnAwake = false;
            main.startColor = colour;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f * scale;
            if (puffMat == null) puffMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = puffMat;
            ps.Emit(Mathf.Max(1, count));
            Destroy(go, 1.5f);
        }
    }
}
