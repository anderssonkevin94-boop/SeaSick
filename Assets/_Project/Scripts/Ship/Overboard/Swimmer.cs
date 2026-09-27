using System.Collections.Generic;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// **A crew member in the water** (phase 5a, docs/PLAN-DEATH-RESCUE.md
    /// "Man overboard"). Created by `CrewAgent.FallOverboard`, destroyed by
    /// one of its own three outcomes: `Rescue()` (called by 5b once the haul
    /// finishes), or its own timeout resolving to washed-ashore or lost at
    /// sea.
    ///
    /// **Visual stand-in**, same spirit as `GravePlacementFlow`'s IMGUI: a
    /// bobbing sphere head with a small point light, not a real swimming
    /// figure. Rides the REAL wave height every frame off `OceanSampler`,
    /// which is the one thing about this that is not a placeholder.
    ///
    /// **Hooks for phase 5b (rescue side):**
    /// - `Swimmer.All` -- every swimmer in the water right now.
    /// - `WorldPosition` -- where to point the edge arrow / autopilot tap.
    /// - `TimeLeft01` -- 0..1, for the countdown ring.
    /// - `Rescue(rescuerName)` -- call this once the haul finishes; it puts
    ///   the crew member back on the roster and fires the life events.
    public class Swimmer : MonoBehaviour
    {
        static readonly List<Swimmer> all = new List<Swimmer>();
        public static IReadOnlyList<Swimmer> All => all;

        public string CrewName { get; private set; }
        public bool Scripted { get; private set; }
        public float TimeLeft { get; private set; }
        float timeTotal = 1f;
        public float TimeLeft01 => timeTotal > 0f ? Mathf.Clamp01(TimeLeft / timeTotal) : 0f;
        public Vector3 WorldPosition => transform.position;
        public bool Resolved { get; private set; }

        /// **5b: the ship's own hull, for reach/haul checks** — `RescueHud`
        /// judges "within reach" off the nearest point on the SIDE of this
        /// transform, not this swimmer's spawning ship reference (same
        /// transform, exposed read-only rather than reaching at `ship`
        /// through a friend-class assumption).
        public Transform Hull => ship;

        /// **5b, set by `CrewAgent.TickHaul`.** While true, `Update` pulls
        /// this swimmer toward `HaulAnchor` instead of drifting on her own —
        /// the visible "being hauled in" motion. Cleared by the haul ending,
        /// one way or the other.
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }

        CrewAgent origin;              // the body to reactivate on rescue -- may be destroyed already
        Transform ship;                // her hull, for Rescue()'s BoardShip
        Vector3 driftVel;
        GameObject visual;
        Light glow;
        static Material headMat;

        // --- phase 5b: the countdown ring ------------------------------------
        const int RingSegments = 28;
        LineRenderer ring;
        Material ringMat;

        /// **Spawn one.** `agent` keeps its body (deactivated, not
        /// destroyed) so `Rescue()` can hand it straight back; `firstTime`
        /// gives the scripted first swimmer his long timer.
        public static Swimmer Spawn(CrewAgent agent, Transform hull, Vector3 worldPos, bool firstTime)
        {
            if (agent == null) return null;
            var go = new GameObject("Swimmer_" + agent.DisplayName);
            go.transform.position = worldPos;
            var s = go.AddComponent<Swimmer>();
            s.CrewName = agent.DisplayName;
            s.Scripted = firstTime;
            s.origin = agent;
            s.ship = hull;

            float t = firstTime ? OverboardTuning.FirstTimeSwimSeconds : OverboardTuning.CalmSwimSeconds;
            if (!firstTime)
            {
                t -= OverboardTuning.StormSwimSecondsOff * Sailing.Storminess01;
                if (Sailing.IsNight) t -= OverboardTuning.NightSwimSecondsOff;
            }
            s.timeTotal = Mathf.Max(20f, t);
            s.TimeLeft = s.timeTotal;

            // Drift: a fixed slow heading, seeded off the name so two
            // swimmers in the same sea don't drift identically.
            float ang = (LifeStory.Fnv32(agent.DisplayName) % 360u) * Mathf.Deg2Rad;
            s.driftVel = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * OverboardTuning.DriftSpeed;

            s.BuildVisual();
            all.Add(s);
            return s;
        }

        void BuildVisual()
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Head";
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = Vector3.one * 0.34f;
            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
            if (headMat == null)
            {
                headMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                headMat.SetColor("_BaseColor", new Color(0.82f, 0.62f, 0.47f));
            }
            var mr = visual.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = headMat;

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 6f;
            glow.intensity = 1.4f;
            glow.color = new Color(1f, 0.92f, 0.75f);

            // A flat ring on the water, draining white -> amber -> red with
            // `TimeLeft01` -- same LineRenderer-loop shape as `SelectionRing`.
            // World space (not a child transform) so its own scale never
            // rides the head's bob.
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(transform, false);
            ring = ringGo.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.alignment = LineAlignment.View;
            ring.textureMode = LineTextureMode.Stretch;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            ringMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (ringMat.HasProperty("_Surface")) ringMat.SetFloat("_Surface", 1f);
            if (ringMat.HasProperty("_SrcBlend")) ringMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (ringMat.HasProperty("_DstBlend")) ringMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (ringMat.HasProperty("_ZWrite")) ringMat.SetFloat("_ZWrite", 0f);
            ringMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ringMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            ring.material = ringMat;
        }

        void OnDestroy()
        {
            all.Remove(this);
            if (ringMat != null) Destroy(ringMat);
        }

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;

            // Ride the real wave -- unless the haul has hold of her, in which
            // case she's pulled toward the rail instead of drifting on her
            // own (`CrewAgent.TickHaul` sets `BeingHauled`/`HaulAnchor` every
            // frame while it holds). One-shot ocean sample per swimmer per
            // frame either way -- well inside OceanSampler's "under ~8 a
            // frame" budget even with a handful of people in the water at
            // once.
            Vector3 pos = BeingHauled
                ? Vector3.MoveTowards(transform.position, HaulAnchor, OverboardTuning.HaulPullSpeed * dt)
                : transform.position + driftVel * dt;
            if (Ocean.OceanSampler.Ready)
                pos.y = Ocean.OceanSampler.SampleImmediate(pos).height + 0.15f;
            transform.position = pos;

            TimeLeft -= dt;
            if (TimeLeft <= 0f) ResolveTimeout();

            UpdateRing();
        }

        /// White -> amber -> red as `TimeLeft01` drains, shrinking a little
        /// with it, and sized off the CHASE CAMERA's distance/height so it
        /// reads the same whether she's close under the bow or half a
        /// screen away (the build brief's "read from the chase camera
        /// height").
        void UpdateRing()
        {
            if (ring == null) return;
            float t = TimeLeft01;
            Color col = t > 0.5f
                ? Color.Lerp(new Color(1f, 0.7f, 0.15f), Color.white, Mathf.InverseLerp(0.5f, 1f, t))
                : Color.Lerp(new Color(0.95f, 0.15f, 0.1f), new Color(1f, 0.7f, 0.15f), Mathf.InverseLerp(0f, 0.5f, t));

            var cam = Camera.main;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 30f;
            float legibility = Mathf.Clamp(camDist / 28f, 0.7f, 2.6f);

            float radius = Mathf.Lerp(0.55f, 1.15f, t) * legibility;
            float width = Mathf.Lerp(0.05f, 0.11f, t) * legibility;
            ring.widthMultiplier = width;
            col.a = 0.85f;
            if (ringMat.HasProperty("_BaseColor")) ringMat.SetColor("_BaseColor", col);
            if (ringMat.HasProperty("_Color")) ringMat.SetColor("_Color", col);

            Vector3 centre = transform.position;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                Vector3 p = centre + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
                if (Ocean.OceanSampler.Ready) p.y = Ocean.OceanSampler.SampleImmediate(p).height + 0.05f;
                ring.SetPosition(i, p);
            }
        }

        /// **5b.** The nearest point on the hull's SIDE (rail), not her
        /// centre -- a rectangle `hullHalfBeamMetres` wide and
        /// `ShipMotor.HullLength` long, in the hull's own local space. Used
        /// by `RescueHud` for the reach check and by `CrewAgent.StartHaul`
        /// for where the haul happens.
        public Vector3 NearestHullSide()
        {
            if (ship == null) return transform.position;
            Vector3 local = ship.InverseTransformPoint(transform.position);
            var motor = ship.GetComponent<Ship.ShipMotor>();
            float halfLen = motor != null ? Mathf.Max(1f, motor.HullLength * 0.5f) : 12f;
            float halfBeam = OverboardTuning.HullHalfBeamMetres;
            float side = Mathf.Sign(local.x != 0f ? local.x : 1f);
            float z = Mathf.Clamp(local.z, -halfLen, halfLen);
            return ship.TransformPoint(new Vector3(halfBeam * side, 0f, z));
        }

        void ResolveTimeout()
        {
            if (Resolved) return;
            Resolved = true;

            var nearest = World.Island.Nearest(transform.position);
            float shoreGap = nearest != null
                ? Vector3.Distance(new Vector3(nearest.transform.position.x, 0f, nearest.transform.position.z),
                    new Vector3(transform.position.x, 0f, transform.position.z)) - nearest.RadiusToward(transform.position)
                : float.MaxValue;

            if (nearest != null && shoreGap <= OverboardTuning.WashAshoreMetres)
                WashAshore(nearest);
            else
                LostAtSea();
        }

        void WashAshore(World.Island isle)
        {
            Lives.Log(CrewName, LifeEvents.WashedAshore, isle.gameObject.name);
            Lives.MarkCastaway(new CastawayRecord
            {
                name = CrewName,
                island = isle.gameObject.name,
                x = transform.position.x,
                z = transform.position.z,
            });
            Banner.Show(CrewName + " washed ashore on " + isle.gameObject.name);
            FinishScriptedIfNeeded();
            EndBodyForGood();
        }

        void LostAtSea()
        {
            // Same shape as `OutpostLedger.Die`, but this swimmer belongs to
            // no camp ledger at all -- built by hand rather than reused,
            // same reasoning `LifeRecord`'s doc gives for `camp = ""`:
            // `GravePlacementFlow.TryFindPending` already treats an empty
            // camp as "site this at the next watched camp".
            var record = Lives.Record(CrewName);
            int bornDay = record != null ? record.bornDay : -1;
            var grave = new GraveRecord
            {
                name = CrewName,
                camp = "",
                bornDay = bornDay,
                diedDay = World.TimeOfDay.Day,
                cause = LifeEvents.LostAtSea,
                x = transform.position.x,
                z = transform.position.z,
            };
            grave.story = LifeStory.Build(record, grave);
            Lives.Bury(grave);
            Banner.Show(CrewName + " was lost at sea.");
            FinishScriptedIfNeeded();
            EndBodyForGood();
        }

        /// **5b calls this once the haul finishes.** Puts the body back on
        /// the roster through the same boarding path authored/camp-born
        /// crew use, applies the sickness spike and the off-station spell,
        /// logs the events, and hands out a small sea-legs bump.
        public void Rescue(string rescuerName)
        {
            if (Resolved) return;
            Resolved = true;

            Lives.Log(CrewName, LifeEvents.Rescued);
            if (!string.IsNullOrEmpty(rescuerName))
                Lives.Log(rescuerName, LifeEvents.RescuedOther, other: CrewName);

            var record = Lives.Record(CrewName);
            if (record != null)
                record.seaLegs = Mathf.Clamp01(record.seaLegs + OverboardTuning.RescueSeaLegsGain);

            CrewAgent agent = origin;
            if (agent == null)
                agent = SeaSick.Crew.BornVillager.Board(CrewName, ship);
            else
            {
                agent.gameObject.SetActive(true);
                agent.ReboardAfterRescue();
            }
            if (agent != null)
                agent.ApplyRescueAftermath(OverboardTuning.RescueSicknessSpike,
                    OverboardTuning.RescueOffStationSeconds);

            FinishScriptedIfNeeded();
            Destroy(gameObject);
        }

        void FinishScriptedIfNeeded()
        {
            if (Scripted) FirstOverboard.Done = true;
        }

        /// Timed out (washed ashore or lost): the original body, if it is
        /// still sitting inactive on the ship, is not coming back under
        /// this name -- destroy it so nothing can accidentally reboard him.
        void EndBodyForGood()
        {
            if (origin != null)
                Destroy(origin.gameObject);
            Destroy(gameObject);
        }
    }
}
