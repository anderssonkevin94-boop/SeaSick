using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.World
{
    /// **Torch posts along the worn roads (2026-09-27).**
    ///
    /// Kevin, on the phone: *"I also don't see the light posts."* Astra's
    /// `Torch_Post_A/B` (roads-astra-lvl1-v1, 404 tris, ground pivot,
    /// `Flame_Anchor` marker) stood beside her roads; the v2 roads had none.
    ///
    /// **Where (her README):** sparingly, roughly every 8-12 m, alternating
    /// sides, a little closer at junctions, never on the road, never in a
    /// building, a wall or the sea. Only on ESTABLISHED road (wear above
    /// `EstablishedWear`, about a day past the "on" threshold); they go when
    /// the road fades. Nothing is saved: every site is DERIVED from the road
    /// chains `CampRoads` just built, and chosen by a hash of the 2 m world
    /// cell the road passes through (highest hash first, `Spacing` apart),
    /// so a rebuild that leaves a stretch alone leaves its posts alone.
    ///
    /// **Light (iPhone):** a small flat-shaded flame at `Flame_Anchor` on
    /// every post at night (one shared mesh + material, the kit's own
    /// toon shader with a high ambient floor, so no new shader to keep
    /// alive), but REAL point lights only on the `MaxLights` posts nearest
    /// the camera, faded in and out so a hand-over never pops. Mobile URP
    /// is Forward+, so the cap is ours to keep, not the pipeline's.
    [DisallowMultipleComponent]
    public class RoadTorches : MonoBehaviour
    {
        public static bool Enabled = true;
        /// Wear (m walked, decayed) a road cell needs before it gets posts.
        /// `CampRoads.OnWear` is 30 and a busy trunk passes it in about a
        /// day, so 60 is "a day or so after it became a road".
        public static float EstablishedWear = 60f;
        public static float Spacing = 9f;          // minimum between two posts
        public static float JunctionReach = 4.5f;  // a post this close to a junction is preferred
        public static float SideGap = 0.5f;        // beyond the road's half width
        public static int MaxLights = 4;
        public static float LightRange = 7f;
        public static float LightIntensity = 1.5f;
        public static float LightReach = 60f;      // no real light beyond this from the camera
        public static float FadeSeconds = 0.6f;
        public static Color LightColour = new Color(1f, 0.62f, 0.30f);

        public static int ActiveLights { get; private set; }
        public static float LastLayoutMs { get; private set; }
        public static float LastUpdateMs { get; private set; }
        public static int TotalPosts { get; private set; }

        class Post
        {
            public GameObject go;
            public Transform flame;
            public MeshRenderer flameRenderer;
            public Light light;
            public int variant;
            public float weight, seed;
            public bool wanted;
        }

        struct Cand { public Vector2 p, tan; public float pri; public int side, chain; public float arc; }
        struct Site { public Vector3 pos; public float yaw; public int variant, chain; public float arc; public int side; public Vector2 p, tan; }

        CampRoads roads;
        readonly List<Post> posts = new List<Post>();
        static readonly List<RoadTorches> all = new List<RoadTorches>();
        static readonly List<Cand> cands = new List<Cand>();
        static readonly List<Site> sites = new List<Site>();
        static readonly List<Vector2> juncs = new List<Vector2>();
        static GameObject[] prefabs;
        static Mesh flameMesh;
        static Material flameMat;
        static int lastFrame = -1;
        static float nextPick;

        public static RoadTorches For(CampRoads r)
        {
            var t = r.GetComponent<RoadTorches>();
            if (t == null) t = r.gameObject.AddComponent<RoadTorches>();
            t.roads = r;
            return t;
        }

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() { all.Remove(this); }
        void OnDestroy() { all.Remove(this); }

        // --- where -------------------------------------------------------------

        /// Re-derive every post from the chains of the build that just
        /// finished. Posts whose site did not move keep their GameObject.
        public void Layout()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            sites.Clear();
            if (Enabled && roads != null && roads.Camp != null) Sites();
            Apply();
            LastLayoutMs = (float)clock.Elapsed.TotalMilliseconds;
            TotalPosts = 0;
            foreach (var t in all) TotalPosts += t.posts.Count;
        }

        void Sites()
        {
            var camp = roads.Camp;
            var pts = roads.chainPts;
            var spans = roads.chainSpans;
            juncs.Clear();
            foreach (var s in spans)
            {
                if ((s.z & 1) != 0) juncs.Add(pts[s.x]);
                if ((s.z & 2) != 0) juncs.Add(pts[s.x + s.y - 1]);
            }

            // One candidate per 2 m cell per chain, sampled at 1 m.
            cands.Clear();
            for (int c = 0; c < spans.Count; c++)
            {
                var s = spans[c];
                float arc = 0f;
                long lastCell = long.MinValue;
                for (int n = s.x; n < s.x + s.y - 1; n++)
                {
                    Vector2 a = pts[n], b = pts[n + 1];
                    float len = Vector2.Distance(a, b);
                    if (len < 1e-4f) continue;
                    Vector2 tan = (b - a) / len;
                    for (float u = 0f; u < len; u += 1f)
                    {
                        Vector2 p = a + tan * u;
                        int cx = Mathf.RoundToInt(p.x / 2f), cz = Mathf.RoundToInt(p.y / 2f);
                        long key = ((long)cx << 32) ^ (uint)cz;
                        if (key == lastCell) continue;
                        lastCell = key;
                        float pri = CampRoads.HashOf(cx * 31 + 7, cz * 17 - 3);
                        foreach (var j in juncs)
                        {
                            float dj = Vector2.Distance(j, p);
                            if (dj > 1.8f && dj < JunctionReach) { pri += 1f; break; }
                        }
                        int side = CampRoads.HashOf(cx * 5 - 11, cz * 9 + 2) < 0.5f ? -1 : 1;
                        cands.Add(new Cand { p = p, tan = tan, pri = pri, side = side, chain = c, arc = arc + u });
                    }
                    arc += len;
                }
            }
            cands.Sort((x, y) => y.pri.CompareTo(x.pri));

            float out_ = CampRoads.HalfWidth + SideGap;
            foreach (var cd in cands)
            {
                if (roads.WearNear(cd.p) < EstablishedWear) continue;
                bool far = true;
                foreach (var st in sites)
                    if ((st.p - cd.p).sqrMagnitude < Spacing * Spacing) { far = false; break; }
                if (!far) continue;
                for (int k = 0; k < 2; k++)
                {
                    int side = k == 0 ? cd.side : -cd.side;
                    Vector2 q = cd.p + new Vector2(-cd.tan.y, cd.tan.x) * (side * out_);
                    if (!Clear(camp, q)) continue;
                    sites.Add(new Site { p = q, tan = cd.tan, chain = cd.chain, arc = cd.arc, side = side });
                    break;
                }
            }

            // Alternate sides along each chain where the other side is free.
            sites.Sort((x, y) => x.chain != y.chain ? x.chain.CompareTo(y.chain) : x.arc.CompareTo(y.arc));
            for (int i = 1; i < sites.Count; i++)
            {
                Site prev = sites[i - 1], s = sites[i];
                if (prev.chain != s.chain || prev.side != s.side) continue;
                Vector2 onRoad = s.p - new Vector2(-s.tan.y, s.tan.x) * (s.side * out_);
                Vector2 q = onRoad - new Vector2(-s.tan.y, s.tan.x) * (s.side * out_);
                if (!Clear(camp, q)) continue;
                bool far = true;
                for (int j = 0; j < sites.Count; j++)
                    if (j != i && (sites[j].p - q).sqrMagnitude < Spacing * Spacing * 0.5f) { far = false; break; }
                if (!far) continue;
                s.p = q; s.side = -s.side;
                sites[i] = s;
            }

            for (int i = 0; i < sites.Count; i++)
            {
                Site s = sites[i];
                int hx = Mathf.RoundToInt(s.p.x * 4f), hz = Mathf.RoundToInt(s.p.y * 4f);
                s.variant = CampRoads.HashOf(hx + 101, hz - 37) < 0.5f ? 0 : 1;
                s.yaw = CampRoads.HashOf(hx - 53, hz + 71) * 360f;
                s.pos = new Vector3(s.p.x, camp.GroundAt(new Vector3(s.p.x, 0f, s.p.y)), s.p.y);
                sites[i] = s;
            }
        }

        /// Beside the road, not under a building, off the walls, on ground a
        /// hand could stand on (no sea, cliff or rock).
        bool Clear(Outpost camp, Vector2 q)
        {
            if (roads.RoadDistance(q) < CampRoads.HalfWidth + 0.3f) return false;
            if (roads.UnderBuilding(q)) return false;
            var map = CampPath.For(camp);
            if (map == null || !map.RoadGround(new Vector3(q.x, 0f, q.y))) return false;
            var built = camp.Built;
            for (int b = 0; b < built.Count; b++)
            {
                var bd = built[b];
                if (bd == null || bd is WallSegment) continue;
                Vector2 f = bd.Footprint;
                if (f.x <= 0f || f.y <= 0f) continue;
                Transform t = bd.transform;
                Vector3 d = new Vector3(q.x, 0f, q.y) - t.position; d.y = 0f;
                Vector3 r = t.right; r.y = 0f; r.Normalize();
                Vector3 fw = t.forward; fw.y = 0f; fw.Normalize();
                if (Mathf.Abs(Vector3.Dot(d, r)) <= 0.5f * f.x + 0.6f && Mathf.Abs(Vector3.Dot(d, fw)) <= 0.5f * f.y + 0.6f)
                    return false;
            }
            foreach (var w in camp.Walls)
            {
                if (w == null) continue;
                Vector2 a = new Vector2(w.A.x, w.A.z), e = new Vector2(w.B.x, w.B.z) - a;
                float ll = e.sqrMagnitude;
                float t = ll > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, e) / ll) : 0f;
                if ((a + e * t - q).sqrMagnitude < 1.4f * 1.4f) return false;
            }
            Vector3 fire = camp.CampCentre;
            if ((new Vector2(fire.x, fire.z) - q).sqrMagnitude < 3.5f * 3.5f) return false;
            return true;
        }

        // --- what --------------------------------------------------------------

        void Apply()
        {
            if (!LoadArt()) { Trim(0); return; }
            // Keep a post whose site is unchanged; rebuild the rest.
            var keep = new bool[posts.Count];
            var taken = new bool[sites.Count];
            for (int i = 0; i < posts.Count; i++)
            {
                var p = posts[i];
                if (p.go == null) continue;
                for (int s = 0; s < sites.Count; s++)
                {
                    if (taken[s] || sites[s].variant != p.variant) continue;
                    if ((sites[s].pos - p.go.transform.position).sqrMagnitude > 0.01f) continue;
                    keep[i] = taken[s] = true;
                    break;
                }
            }
            for (int i = posts.Count - 1; i >= 0; i--)
                if (!keep[i]) { if (posts[i].go != null) Destroy(posts[i].go); posts.RemoveAt(i); }
            for (int s = 0; s < sites.Count; s++)
            {
                if (taken[s]) continue;
                var st = sites[s];
                // A plain parent: the FBX root carries the x100 / -90 X
                // import transform (the Astra rig-scale trap), so nothing of
                // ours is ever parented under it.
                var go = new GameObject("RoadTorch");
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(st.pos, Quaternion.Euler(0f, st.yaw, 0f));
                var art = Instantiate(prefabs[st.variant], go.transform, false);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                var post = new Post { go = go, variant = st.variant, seed = (st.pos.x * 12.9898f + st.pos.z * 78.233f) % 100f };
                Transform anchor = null;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name.EndsWith("Flame_Anchor")) { anchor = t; break; }
                var fl = new GameObject("Flame");
                fl.transform.SetParent(go.transform, false);
                fl.transform.localPosition = anchor != null ? go.transform.InverseTransformPoint(anchor.position) : new Vector3(0f, 1.7f, 0f);
                fl.AddComponent<MeshFilter>().sharedMesh = flameMesh;
                var fr = fl.AddComponent<MeshRenderer>();
                fr.sharedMaterial = flameMat;
                fr.shadowCastingMode = ShadowCastingMode.Off;
                fr.receiveShadows = false;
                fr.lightProbeUsage = LightProbeUsage.Off;
                fr.enabled = false;
                post.flame = fl.transform;
                post.flameRenderer = fr;
                posts.Add(post);
            }
        }

        void Trim(int n)
        {
            for (int i = posts.Count - 1; i >= n; i--)
            {
                if (posts[i].go != null) Destroy(posts[i].go);
                posts.RemoveAt(i);
            }
        }

        public static bool ArtLoaded => prefabs != null && prefabs[0] != null && prefabs[1] != null && flameMat != null;

        /// Load the kit and build the flame now (CampRoads calls it in a
        /// build slice of its own, so the first road is not also a hitch).
        public static void Preload() => LoadArt();

        static bool LoadArt()
        {
            if (prefabs != null && prefabs[0] != null && prefabs[1] != null && flameMat != null) return true;
            prefabs = new[] { Resources.Load<GameObject>("Roads/Torch_Post_A"), Resources.Load<GameObject>("Roads/Torch_Post_B") };
            if (prefabs[0] == null || prefabs[1] == null) { Debug.LogError("[RoadTorches] Resources/Roads/Torch_Post_A/B missing"); return false; }
            var kitMat = prefabs[0].GetComponentInChildren<MeshRenderer>().sharedMaterial;
            // The kit's own toon shader (EnvironmentToon, kept alive by
            // Keep_EnvironmentToon.mat) with its ambient floor pushed past
            // 1: the vertex colours come out at full strength whatever the
            // sun is doing, which on a flame is what "lit" means.
            flameMat = new Material(kitMat) { name = "TorchFlame (runtime)" };
            if (flameMat.HasProperty("_Ambient")) flameMat.SetFloat("_Ambient", 2.2f);
            if (flameMat.HasProperty("_BaseColor")) flameMat.SetColor("_BaseColor", Color.white);
            flameMat.enableInstancing = true;
            flameMesh = BuildFlame();
            return true;
        }

        /// Three low-poly tongues, flat shaded, yellow at the root and
        /// orange-red at the tip. ~60 tris; origin at the fuel.
        static Mesh BuildFlame()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            void Tongue(Vector3 root, float h, float r, float lean, float twist, Color low, Color high)
            {
                const int sides = 5;
                Vector3 tip = root + new Vector3(Mathf.Cos(twist) * lean, h, Mathf.Sin(twist) * lean);
                Vector3 bottom = root - new Vector3(0f, h * 0.12f, 0f);
                var ring = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = twist + i * Mathf.PI * 2f / sides;
                    ring[i] = root + new Vector3(Mathf.Cos(a) * r, h * 0.3f, Mathf.Sin(a) * r) + (tip - root) * 0.12f;
                }
                for (int i = 0; i < sides; i++)
                {
                    Vector3 a = ring[i], b = ring[(i + 1) % sides];
                    int k = v.Count;
                    v.Add(a); v.Add(tip); v.Add(b);
                    c.Add(low); c.Add(high); c.Add(low);
                    t.Add(k); t.Add(k + 1); t.Add(k + 2);
                    k = v.Count;
                    v.Add(a); v.Add(b); v.Add(bottom);
                    c.Add(low); c.Add(low); c.Add(low);
                    t.Add(k); t.Add(k + 1); t.Add(k + 2);
                }
            }
            Color yellow = new Color(1f, 0.86f, 0.38f), orange = new Color(1f, 0.52f, 0.14f), red = new Color(0.95f, 0.30f, 0.08f);
            Tongue(Vector3.zero, 0.36f, 0.085f, 0.02f, 0.3f, yellow, orange);
            Tongue(new Vector3(0.05f, -0.02f, 0.02f), 0.22f, 0.055f, 0.07f, 0.4f, orange, red);
            Tongue(new Vector3(-0.045f, -0.02f, -0.03f), 0.25f, 0.055f, 0.07f, 3.6f, orange, red);
            var m = new Mesh { name = "TorchFlame" };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // --- when --------------------------------------------------------------

        void Update()
        {
            // One pass a frame for every camp's posts: the light cap is global.
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Tick();
            LastUpdateMs = (float)clock.Elapsed.TotalMilliseconds;
        }

        static readonly List<Post> near = new List<Post>();

        static void Tick()
        {
            var sky = SkyDirector.Instance;
            float night = sky != null ? Mathf.Clamp01(sky.Night01) : 0f;
            float lit = Mathf.Clamp01((night - 0.15f) / 0.35f);
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float time = Time.time;

            // Pick the nearest posts for real light, a few times a second.
            if (time >= nextPick || lit <= 0f)
            {
                nextPick = time + 0.25f;
                near.Clear();
                foreach (var tc in all)
                    foreach (var p in tc.posts) { p.wanted = false; if (p.go != null && lit > 0f) near.Add(p); }
                if (near.Count > 0 && cam != null)
                {
                    near.Sort((a, b) => (a.go.transform.position - eye).sqrMagnitude.CompareTo((b.go.transform.position - eye).sqrMagnitude));
                    for (int i = 0; i < near.Count && i < MaxLights; i++)
                        if ((near[i].go.transform.position - eye).sqrMagnitude < LightReach * LightReach) near[i].wanted = true;
                }
            }

            int on = 0;
            foreach (var tc in all) foreach (var p in tc.posts) if (p.weight > 0f) on++;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, FadeSeconds);
            int active = 0;
            foreach (var tc in all)
            {
                foreach (var p in tc.posts)
                {
                    if (p.go == null) continue;
                    float t = time * 7.1f + p.seed;
                    float flick = 1f + 0.12f * (Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.63f + 1.3f) * 0.4f);

                    var fr = p.flameRenderer;
                    if (fr != null)
                    {
                        bool show = lit > 0.01f;
                        if (fr.enabled != show) fr.enabled = show;
                        if (show) p.flame.localScale = new Vector3(1f, flick, 1f) * Mathf.Lerp(0.4f, 1f, lit);
                    }

                    // Fade out first; fade in only into a free slot.
                    if (p.wanted && lit > 0f)
                    {
                        if (p.weight > 0f || on < MaxLights)
                        {
                            if (p.weight <= 0f) on++;
                            p.weight = Mathf.Min(1f, p.weight + step);
                        }
                    }
                    else if (p.weight > 0f)
                    {
                        p.weight = Mathf.Max(0f, p.weight - step);
                        if (p.weight <= 0f) on--;
                    }

                    if (p.weight > 0f)
                    {
                        if (p.light == null)
                        {
                            var lg = new GameObject("TorchLight");
                            lg.transform.SetParent(p.go.transform, false);
                            if (p.flame != null) lg.transform.localPosition = p.flame.localPosition + new Vector3(0f, 0.15f, 0f);
                            p.light = lg.AddComponent<Light>();
                            p.light.type = LightType.Point;
                            p.light.color = LightColour;
                            p.light.range = LightRange;
                            p.light.shadows = LightShadows.None;
                        }
                        if (!p.light.enabled) p.light.enabled = true;
                        p.light.intensity = LightIntensity * p.weight * lit * flick;
                        active++;
                    }
                    else if (p.light != null && p.light.enabled) p.light.enabled = false;
                }
            }
            ActiveLights = active;
        }
    }
}
