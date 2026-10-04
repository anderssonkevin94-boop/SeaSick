using System.Collections.Generic;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.Ship;

namespace SeaSick.World.Life
{
    /// <summary>
    /// **Castaways you can see** (phase 7, docs/PLAN-DEATH-RESCUE.md
    /// "Recruits at sea"; build brief items 1-2). Two jobs, both driven off
    /// the ship's own position, same lazy-lookup shape as
    /// `Ship.Overboard.RescueHud`'s helm reference:
    ///
    /// 1. **Show every washed-ashore/stranger castaway** (`Lives.Castaways`)
    ///    as a standing figure plus a small signal-fire smoke column,
    ///    whenever the ship is within `RecruitTuning.CastawayShowMetres` of
    ///    their island -- so a save made before this phase, or a swimmer who
    ///    washed ashore last session, gets their figure the moment the ship
    ///    comes back in range, with nothing baked into the scene.
    /// 2. **Roll a stranger** on a camp-less island the FIRST time the ship
    ///    comes that close to it (`Lives.IsIslandRolled`/`MarkIslandRolled`
    ///    guard against re-rolling a declined/picked-up one), deterministic
    ///    off the island's own (seed-stable) GameObject name.
    ///
    /// **Visual stand-in**, same spirit as `Swimmer`: a real crew body (the
    /// same authored figure a camp's own villagers wear) so it reads at a
    /// distance, plus a bare `ParticleSystem` for the smoke -- not the
    /// `BornVillager` factory's tag, on purpose (see `SpawnBody`).
    /// </summary>
    public class CastawayField : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<CastawayField>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("CastawayField");
            go.AddComponent<CastawayField>();
            DontDestroyOnLoad(go);
        }

        ShipMotor ship;
        float nextShipLookup;

        readonly Dictionary<string, GameObject> visuals = new Dictionary<string, GameObject>();
        static readonly HashSet<string> seenThisFrame = new HashSet<string>();
        static Material smokeMat;

        void Update()
        {
            if (Time.time >= nextShipLookup)
            {
                if (ship == null) ship = FindAnyObjectByType<ShipMotor>();
                nextShipLookup = Time.time + 1f;
            }
            if (ship == null) return;

            Vector3 shipPos = ship.transform.position;
            float showDist = RecruitTuning.CastawayShowMetres;

            // --- strangers: roll once per camp-less island in range --------
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                Vector3 d = isle.transform.position - shipPos; d.y = 0f;
                float gap = d.magnitude - isle.RadiusToward(shipPos);
                if (gap <= showDist) TryRollStranger(isle, shipPos);
            }

            // --- visuals: spawn/despawn to match who is in range right now --
            seenThisFrame.Clear();
            foreach (var c in Lives.Castaways)
            {
                if (c == null || string.IsNullOrEmpty(c.name)) continue;
                Vector3 cp = new Vector3(c.x, 0f, c.z);
                Vector3 sp = shipPos; sp.y = 0f;
                if (Vector3.Distance(cp, sp) > showDist) continue;
                seenThisFrame.Add(c.name);
                if (!visuals.ContainsKey(c.name)) SpawnVisual(c);
            }

            // Whoever fell out of range, or was picked up (no longer in
            // `Lives.Castaways` at all), loses their figure -- cheap to
            // rebuild the moment the ship comes back, and a picked-up
            // castaway's figure is gone for good (they're crew now).
            if (visuals.Count > 0)
            {
                stale.Clear();
                foreach (var kv in visuals) if (!seenThisFrame.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var name in stale)
                {
                    if (visuals.TryGetValue(name, out var go) && go != null) Destroy(go);
                    visuals.Remove(name);
                }
            }
        }

        static readonly List<string> stale = new List<string>();

        /// The beach figures this class stood up. They wear the castaway's
        /// name but are nobody's body -- `CastawayRepair` must not read one
        /// as the castaway living elsewhere (2026-10-04).
        static readonly HashSet<CrewAgent> figures = new HashSet<CrewAgent>();
        public static bool IsFigure(CrewAgent a) => a != null && figures.Contains(a);

        // ---------------------------------------------------------- strangers --

        void TryRollStranger(Island isle, Vector3 shipPos)
        {
            string key = isle.gameObject.name;
            if (Lives.IsIslandRolled(key)) return;
            // Rolled exactly once, right now, win or lose -- never re-tried.
            Lives.MarkIslandRolled(key);

            var outpost = Outpost.Of(isle);
            if (outpost != null && outpost.HasCamp) return; // camped islands never get a stranger
            if (Lives.StrangersAlive() >= RecruitTuning.MaxStrangers) return;

            uint h = LifeStory.Fnv32(key);
            float roll = (h % 10000u) / 10000f;
            if (roll >= RecruitTuning.StrangerChance) return;

            string name = FreeName(h);
            if (string.IsNullOrEmpty(name)) return; // the whole pool is spoken for -- skip rather than collide

            Vector3 pos = isle.ShorePoint(0, 1, shipPos);
            Lives.MarkCastaway(new CastawayRecord { name = name, island = key, x = pos.x, z = pos.z });
            Lives.MarkStranger(name);
        }

        /// A name nobody is using anywhere -- crew, camps, graves or the
        /// castaways already ashore -- deterministic off `h` so the same
        /// island rolls the same name every time this ever runs (it only
        /// ever runs once per island, but a name should still read as
        /// "this island's stranger" rather than randomised noise in a log).
        static string FreeName(uint h)
        {
            var taken = CrewNames.InUse();
            var pool = CrewNames.Pool;
            for (int i = 0; i < pool.Length; i++)
            {
                string candidate = pool[(int)((h + (uint)i) % (uint)pool.Length)];
                if (!taken.Contains(candidate) && !Lives.IsTaken(candidate)) return candidate;
            }
            return null;
        }

        // ---------------------------------------------------------- visuals --

        void SpawnVisual(CastawayRecord c)
        {
            Island isle = FindIsland(c.island);
            Transform parent = isle != null ? isle.transform : transform;

            var root = new GameObject("Castaway_" + c.name);
            root.transform.SetParent(parent, true);
            float y = Island.TerrainHeight != null ? Island.TerrainHeight(c.x, c.z) : parent.position.y;
            root.transform.position = new Vector3(c.x, y, c.z);

            var body = SpawnBody(c.name, root.transform);
            if (body != null)
            {
                figures.RemoveWhere(f => f == null);
                figures.Add(body);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
            }

            BuildSmoke(root.transform);

            visuals[c.name] = root;
        }

        /// **The same authored figure a camp's own villagers wear**
        /// (`BornVillager.Make` is that factory -- see the class doc) --
        /// minus the `BornVillager` TAG, stripped the instant it is made.
        /// That tag is how `SaveGame` finds bodies to write into the ship's
        /// crew list and board on load (`SaveGame.cs`, `BornVillager.All()`);
        /// a castaway standing on a beach, owned by no ledger, is not crew
        /// and must never be written down as some -- the `CastawayRecord`
        /// already saves this person, by position, and re-spawning this
        /// figure from it is this class's whole job.
        static CrewAgent SpawnBody(string displayName, Transform parent)
        {
            var agent = BornVillager.Make(displayName, parent);
            if (agent == null) return null;
            var tag = agent.GetComponent<BornVillager>();
            if (tag != null) DestroyImmediate(tag);
            agent.gameObject.SetActive(true);
            return agent;
        }

        static Island FindIsland(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var isle in Island.All)
                if (isle != null && isle.gameObject.name == key) return isle;
            return null;
        }

        /// A bare vertical puff column -- not a real campfire prop (none of
        /// this project's are wired for smoke), just enough grey motion to
        /// read from the water the way the build brief asks: "so it is
        /// visible from sea".
        static void BuildSmoke(Transform parent)
        {
            var go = new GameObject("SignalSmoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0.8f, 0f, 0.8f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 3.5f;
            main.startSpeed = 1.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startColor = new Color(0.75f, 0.74f, 0.72f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.15f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.7f, 0.68f, 0.65f), 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (smokeMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                smokeMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                if (smokeMat.HasProperty("_Surface")) smokeMat.SetFloat("_Surface", 1f);
                if (smokeMat.HasProperty("_Blend")) smokeMat.SetFloat("_Blend", 0f);
            }
            renderer.sharedMaterial = smokeMat;
        }
    }
}
