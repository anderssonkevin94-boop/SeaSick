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

        CrewAgent origin;              // the body to reactivate on rescue -- may be destroyed already
        Transform ship;                // her hull, for Rescue()'s BoardShip
        Vector3 driftVel;
        GameObject visual;
        Light glow;
        static Material headMat;

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
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;

            // Ride the real wave. One-shot per swimmer per frame -- well
            // inside OceanSampler's "under ~8 a frame" budget even with a
            // handful of people in the water at once.
            Vector3 pos = transform.position + driftVel * dt;
            if (Ocean.OceanSampler.Ready)
                pos.y = Ocean.OceanSampler.SampleImmediate(pos).height + 0.15f;
            transform.position = pos;

            TimeLeft -= dt;
            if (TimeLeft <= 0f) ResolveTimeout();
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
