using System.Collections.Generic;
using UnityEngine;
using SeaSick.Terrain;

namespace SeaSick.World
{
    /// **Something alive on the land that isn't ours.**
    ///
    /// An island reads as a set once every moving thing on it is a crewman we
    /// posted there. The wood, the boulders and the huts are all STILL, so the
    /// only motion ashore belongs to the player's own ledger -- and a place
    /// where nothing moves unless you put it there is a diorama. Goats on a
    /// ridge and gulls turning over the shore cost almost nothing and are the
    /// difference between a landform and a habitat.
    ///
    /// **Ambient, on purpose.** Nothing here is huntable, nothing produces,
    /// nothing touches the ledger. Same rule as `CampWorker`: if an animal
    /// could be shot for meat, an island would pay differently depending on
    /// whether anyone was watching it, and the absentee loop was built to
    /// avoid exactly that. This pass buys the LOOK of a living island; the
    /// economy stays where it is.
    ///
    /// **Who lives where is a fact about the island, not a roll.** Goats come
    /// with `Rock01`, boar with `Verdancy01` -- the same two character fields
    /// `IslandScenery` reads once at the centre to decide what the wood is.
    /// So a bare rocky stack gets goats and a green one gets boar, every time,
    /// and the animals corroborate the ground rather than contradicting it.
    ///
    /// **Everything is placed above where sand stops** (`sandTop`, derived by
    /// the caller the way the wood derives it). An animal standing in the surf
    /// looks drowned, and the beach is the one part of an island the player
    /// looks straight at when landing.
    ///
    /// Cost: the herd never thinks past `FaunaLod.ThinkRange`, and the models
    /// are one shared Resources load each. All numbers below are PLACEHOLDERS
    /// -- first-pass guesses, tune against Kevin's play, not against a probe.
    public static class FaunaField
    {
        // ---- placeholder tuning ------------------------------------------
        // Herd sizes. Goats want rock, boar want green, gulls want a coast
        // big enough to be worth circling.
        public const float GoatRockiness = 0.35f;   // Rock01 above this -> goats
        public const float BoarVerdancy = 0.45f;    // Verdancy01 above this -> boar
        public const float GullMinRadius = 60f;     // m, island too small to bother
        public const float CampKeepOut = 25f;       // m clear of a camp centre
        public const float HerdSpread = 8f;         // m, members about their anchor

        // Slope an animal will stand on, metres of rise per metre. Goats like
        // slopes; a boar on a 1.6 gradient reads as a mountain goat.
        public const float GoatMaxSlope = 1.6f;
        public const float BoarMaxSlope = 0.8f;

        /// Seeded per island, called once after its scenery is built. Returns
        /// the fauna root (with its `FaunaLod`) or null when nothing lives
        /// here, so the caller can ignore the result.
        public static GameObject Populate(Transform parent, Island island, int seed,
            float rockiness, float verdancy,
            System.Func<float, float, float> height, float sandTop)
        {
            if (island == null || height == null) return null;

            var root = new GameObject("Fauna");
            root.transform.SetParent(parent != null ? parent : island.transform, false);
            root.transform.position = island.transform.position;

            var lod = root.AddComponent<FaunaLod>();
            lod.Bind(island, height, sandTop);

            var rng = new System.Random(seed * 7919 + 131);

            // **Herd counts scale with the island.** A 60 m islet with nine
            // goats on it is a petting zoo, and the size of a herd is one of
            // the few honest size cues the player gets from a distance.
            float r = island.MaxRadius;
            float sizeScale = Mathf.Clamp01((r - 50f) / 250f);          // 0 at 50 m, 1 at 300 m
            int sizeCap = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(2f, 12f, sizeScale)));

            if (!island.IsHome && rockiness > GoatRockiness)
            {
                int n = Mathf.Min(sizeCap, 3 + Mathf.RoundToInt(rockiness * 6f));
                Herd(lod, Animal.Kind.Goat, n, rng);
            }
            if (!island.IsHome && verdancy > BoarVerdancy)
            {
                int n = Mathf.Min(sizeCap, 2 + Mathf.RoundToInt(verdancy * 6f));
                Herd(lod, Animal.Kind.Boar, n, rng);
            }

            // Gulls everywhere with a coast worth having, home island
            // included -- the home shore is the one the player sees most.
            if (r > GullMinRadius)
            {
                int n = Mathf.Min(sizeCap + 2, 4 + Mathf.RoundToInt(r / 80f));
                Gulls(lod, n, rng);
            }

            if (lod.Count == 0) { Kill(root); return null; }
            lod.Finish();
            return root;
        }

        // ---- placement -----------------------------------------------------

        /// Is this a spot an animal of this kind can stand on? Inside the
        /// solid radius, above the beach, and not on a wall. The camp is a
        /// keep-out because a boar wandering through the sawmill would read as
        /// a bug rather than as wildlife.
        public static bool PointOk(FaunaLod f, Vector3 p, float maxSlope)
        {
            if (f == null) return false;
            Vector3 c = f.Centre;
            Vector3 d = p - c; d.y = 0f;
            float dist = d.magnitude;
            if (dist > f.Island.RadiusAt(Mathf.Atan2(d.x, d.z)) * 0.85f) return false;

            float h = f.Height(p.x, p.z);
            if (h < f.SandTop + 0.5f) return false;

            // Slope from a one-metre cross, which is the scale of a hoof
            // rather than of the landform.
            float hx = Mathf.Abs(f.Height(p.x + 1f, p.z) - f.Height(p.x - 1f, p.z)) * 0.5f;
            float hz = Mathf.Abs(f.Height(p.x, p.z + 1f) - f.Height(p.x, p.z - 1f)) * 0.5f;
            if (Mathf.Max(hx, hz) > maxSlope) return false;

            if (f.CampKeepOut > 0f)
            {
                Vector3 cd = p - f.CampAt; cd.y = 0f;
                if (cd.sqrMagnitude < f.CampKeepOut * f.CampKeepOut) return false;
            }
            return true;
        }

        /// Rejection-sample a standable point within `spread` of `about`.
        /// Gives up after 40 tries: an island can simply have nowhere left.
        public static bool TryPoint(FaunaLod f, Vector3 about, float spread,
                                    float maxSlope, System.Random rng, out Vector3 hit)
        {
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = Mathf.Sqrt((float)rng.NextDouble()) * spread;
                var p = new Vector3(about.x + Mathf.Sin(a) * d, 0f, about.z + Mathf.Cos(a) * d);
                if (!PointOk(f, p, maxSlope)) continue;
                p.y = f.Height(p.x, p.z);
                hit = p; return true;
            }
            hit = about; return false;
        }

        static void Herd(FaunaLod f, Animal.Kind kind, int count, System.Random rng)
        {
            float slope = kind == Animal.Kind.Goat ? GoatMaxSlope : BoarMaxSlope;

            // The anchor is what the herd belongs to: find it first, out on
            // the island somewhere, then hang the members off it. Without an
            // anchor a herd disperses into a scatter within a minute.
            if (!TryPoint(f, f.Centre, f.Island.MaxRadius * 0.8f, slope, rng, out var anchor)) return;

            for (int i = 0; i < count; i++)
            {
                if (!TryPoint(f, anchor, HerdSpread, slope, rng, out var at)) continue;
                var go = Body(kind, f.Root);
                go.transform.position = at;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                var a = go.AddComponent<Animal>();
                a.kind = kind;
                a.Bind(f, anchor, rng.Next());
                f.Add(go, a, null);
            }
        }

        static void Gulls(FaunaLod f, int count, System.Random rng)
        {
            // Two or three to an anchor with phase offsets, so they read as a
            // flock turning together rather than as N independent birds.
            int perAnchor = 2 + rng.Next(2);
            Vector3 anchor = Vector3.zero;
            float radius = 0f, alt = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i % perAnchor == 0)
                {
                    // Over the shore, not over the summit: gulls belong to the
                    // waterline, and that is also where the ship will be.
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float shore = f.Island.RadiusAt(ang);
                    anchor = f.Centre + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * shore;
                    anchor.y = 0f;
                    radius = Mathf.Lerp(20f, 40f, (float)rng.NextDouble());
                    alt = Mathf.Lerp(12f, 25f, (float)rng.NextDouble());
                }
                var go = Body(Animal.Kind.Goat, f.Root, gull: true);
                var g = go.AddComponent<Gull>();
                g.Bind(anchor, radius, alt, (float)rng.NextDouble() * Mathf.PI * 2f,
                       rng.Next(2) == 0 ? 1f : -1f);
                f.Add(go, null, g);
            }
        }

        /// **Gulls over the open sea.** Three that follow the ship's wake at
        /// three heights and two turns, so the ocean is never birdless where
        /// the player is. No `FaunaLod` (nothing to gate: they are always
        /// where the observer is) and no alarm (the ship is what they want).
        public static GameObject FollowShip(Transform ship, int count = 3, int seed = 7)
        {
            if (ship == null) return null;
            var rng = new System.Random(seed);
            var root = new GameObject("WakeGulls");
            for (int i = 0; i < count; i++)
            {
                var go = Body(Animal.Kind.Goat, root.transform, gull: true);
                var g = go.AddComponent<Gull>();
                g.Bind(ship.position, Mathf.Lerp(10f, 18f, (float)rng.NextDouble()),
                       Mathf.Lerp(7f, 14f, (float)rng.NextDouble()),
                       (float)rng.NextDouble() * Mathf.PI * 2f, i % 2 == 0 ? 1f : -1f);
                g.Follow(ship, Mathf.Lerp(6f, 14f, (float)rng.NextDouble()));
            }
            return root;
        }

        // ---- bodies ---------------------------------------------------------

        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        static Material standInMat;

        /// The FBX if the art is in, a coloured block if it is not. The
        /// stand-in exists so the whole system is playable and tunable before
        /// the models land -- a null prefab must never be the reason a feature
        /// can't be judged.
        static GameObject Body(Animal.Kind kind, Transform parent, bool gull = false)
        {
            string key = gull ? "gull" : (kind == Animal.Kind.Goat ? "goat" : "boar");
            if (!models.TryGetValue(key, out var src))
            {
                src = Resources.Load<GameObject>("Fauna/" + key);
                models[key] = src;
            }

            GameObject go;
            if (src != null)
            {
                go = Object.Instantiate(src, parent);
                Paint(go, IslandScenery.SceneryMaterial());
            }
            else
            {
                go = StandIn(key, parent);
            }
            go.name = key;
            return go;
        }

        /// A flattened box of roughly the right size, origin at the feet (at
        /// the body centre for the gull), nose down +Z -- the same contract the
        /// FBX has, so swapping the model in changes nothing but the silhouette.
        static GameObject StandIn(string key, Transform parent)
        {
            var root = new GameObject("standin");
            root.transform.SetParent(parent, false);

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = box.GetComponent<Collider>();
            if (col != null) Kill(col);             // ambient: nothing collides
            box.transform.SetParent(root.transform, false);

            Vector3 size; Color tint;
            switch (key)
            {
                case "goat": size = new Vector3(0.40f, 0.55f, 0.95f); tint = new Color(0.78f, 0.74f, 0.66f); break;
                case "boar": size = new Vector3(0.45f, 0.60f, 1.15f); tint = new Color(0.30f, 0.24f, 0.20f); break;
                default: size = new Vector3(0.55f, 0.16f, 0.34f); tint = new Color(0.92f, 0.92f, 0.94f); break;
            }
            box.transform.localScale = size;
            // Feet on the ground for the walkers, centred for the bird.
            box.transform.localPosition = key == "gull" ? Vector3.zero : new Vector3(0f, size.y * 0.5f, 0f);

            var r = box.GetComponent<Renderer>();
            if (standInMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                standInMat = new Material(sh) { name = "FaunaStandIn" };
            }
            // One material per tint, instanced: three animals, three materials,
            // and the stand-in is never what ships.
            var m = new Material(standInMat) { name = "FaunaStandIn " + key };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            r.sharedMaterial = m;
            return root;
        }

        /// The world can be built from an editor menu as well as in play, and
        /// `Destroy` is a no-op outside play mode -- which would leave the
        /// stand-in boxes colliding with the crew.
        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// Every slot on every renderer, as `SteamerBootstrap.Paint` does: an
        /// FBX with two material slots would keep its import grey on half of it.
        static void Paint(GameObject go, Material paint)
        {
            if (go == null || paint == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = paint;
                r.sharedMaterials = slots;
            }
        }
    }
}
