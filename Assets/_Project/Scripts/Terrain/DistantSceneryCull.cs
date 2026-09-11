using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// Stops trees, rocks and huts being drawn where there is no ground under
    /// them.
    ///
    /// Scenery is spawned once for the whole `discoveryRadius` (3 km) and
    /// parented to per-island roots, deliberately, "so nothing waits on chunk
    /// streaming". Terrain chunks are streamed to `viewRadius * chunkSize`
    /// (1536 m). The two never had to agree while the camera's far plane was
    /// 600 m and hid everything past it — but opening the far plane so the
    /// horizon could be seen also revealed a kilometre and a half of trees
    /// standing on open water.
    ///
    /// Past the streamed radius an island is a silhouette and nothing else, so
    /// its props come off. The cull distance is not a taste setting: it is
    /// where the real ground stops.
    ///
    /// Whole islands at a time, on a throttle, and only on a change — a
    /// per-prop distance test every frame over a few thousand objects would
    /// cost more than the drawing it saves.
    ///
    /// FINDING THE ISLANDS USED TO COST MORE THAN THE CULL SAVED. The scan was
    /// `FindObjectsByType<Transform>` filtered by name: every transform in the
    /// scene (289 terrain chunks, thousands of scenery cells, their LOD
    /// children and props), allocated into an array, every 2 s. Its early-out
    /// compared root count to group count and so could never fire, because a
    /// root with no kept renderers was skipped and never became a group — so
    /// the full rebuild ran on EVERY scan: `GetComponentsInChildren<Renderer>`
    /// per island, `GetComponentInParent<SceneryLod>` per renderer, a fresh
    /// list and `ToArray()` each time, and then `visible = true` on every new
    /// group, which made each distant island re-enable and re-disable all its
    /// renderers on the following tick. Measured in the profiler at 62–74 ms
    /// and ~115 KB of garbage every 2 s — a hitch twice a minute, twice a
    /// second's worth of frame budget, to answer a question the world already
    /// knows the answer to.
    ///
    /// So ask the world instead: `Island.All` and `Reef.All` are registries the
    /// components maintain themselves, and they are the same objects the name
    /// filter was looking for. Groups are keyed by root and kept across scans;
    /// a scan only builds roots it has not seen, rebuilds one whose
    /// `hierarchyCount` has changed (props, the dock and the village buildings
    /// attach AFTER `Island` registers, so a one-shot snapshot would miss
    /// their renderers — an int compare per island catches the late arrivals
    /// for nothing), and drops roots that have left the registry. A rebuild
    /// carries the group's `visible` state onto the new renderers, so nothing
    /// flashes back on at range. Steady state allocates nothing.
    public class DistantSceneryCull : MonoBehaviour
    {
        [SerializeField] TerrainSettings settings;
        [SerializeField] Transform target;
        [Tooltip("Extra metres past the streamed terrain edge before scenery is dropped, so an island does not flicker as you sail the boundary.")]
        [SerializeField] float margin = 120f;
        [SerializeField] float scanInterval = 2f;
        [SerializeField] float testInterval = 0.25f;

        class Group
        {
            public Transform root;
            public Renderer[] renderers;
            public Vector3 centre;
            public float radius;
            public bool visible = true;
            /// What the root's hierarchy measured when the renderers were
            /// gathered — a change means something was parented in or removed.
            public int hierarchyCount;
            /// Which scan last saw this root in the registries.
            public int seen;
        }

        readonly Dictionary<Transform, Group> byRoot = new Dictionary<Transform, Group>();
        /// The same groups, in a list, so the 0.25 s loop is an index walk.
        readonly List<Group> groups = new List<Group>();
        /// Reused by every group build, so gathering renderers allocates only
        /// the array the group keeps.
        readonly List<Renderer> scratch = new List<Renderer>(512);

        Camera cam;
        int scanStamp;
        float nextScan, nextTest;

        void OnEnable() { nextScan = 0f; nextTest = 0f; }

        void Update()
        {
            if (settings == null) return;
            if (target == null)
            {
                // Camera.main is a tagged search; resolve it once and hold it.
                if (cam == null) cam = Camera.main;
                if (cam != null) target = cam.transform;
            }
            if (target == null) return;

            if (Time.time >= nextScan) { nextScan = Time.time + scanInterval; Scan(); }
            if (Time.time < nextTest) return;
            nextTest = Time.time + testInterval;

            float cull = settings.viewRadius * settings.chunkSize + margin;
            Vector3 p = target.position;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.root == null || g.renderers.Length == 0) continue;
                float d = Vector2.Distance(new Vector2(p.x, p.z),
                                           new Vector2(g.centre.x, g.centre.z)) - g.radius;
                bool want = d <= cull;
                if (want == g.visible) continue;
                g.visible = want;
                Apply(g);
            }
        }

        void Scan()
        {
            // Islands and reefs arrive over time as the world populates, and
            // their contents keep growing after they register, so this is a
            // reconcile against the registries rather than a snapshot.
            scanStamp++;

            var isles = Island.All;
            for (int i = 0; i < isles.Count; i++)
                if (isles[i] != null) Track(isles[i].transform);

            var reefs = Reef.All;
            for (int i = 0; i < reefs.Count; i++)
                if (reefs[i] != null) Track(reefs[i].transform);

            // Anything the registries no longer carry (or that has been
            // destroyed under us) stops being managed.
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                var g = groups[i];
                if (g.seen == scanStamp && g.root != null) continue;
                byRoot.Remove(g.root);
                groups.RemoveAt(i);
            }
        }

        void Track(Transform root)
        {
            if (byRoot.TryGetValue(root, out var g))
            {
                g.seen = scanStamp;
                // Cheap staleness test: props, the dock and village buildings
                // are parented in long after the island registers.
                if (root.hierarchyCount != g.hierarchyCount) Gather(g, true);
                return;
            }

            g = new Group { root = root, renderers = System.Array.Empty<Renderer>(), seen = scanStamp };
            // A first build touches no `enabled` flag: the group starts visible,
            // which is the state the renderers are already in, and whoever else
            // turned one off meant it.
            Gather(g, false);
            byRoot[root] = g;
            groups.Add(g);
        }

        /// (Re)collect a root's renderers. A group with none is still tracked —
        /// dropping it is what made the old early-out unreachable — it is just
        /// skipped by the distance loop.
        void Gather(Group g, bool apply)
        {
            var root = g.root;
            g.hierarchyCount = root.hierarchyCount;

            // The welded scenery has its own distance logic (SceneryLod:
            // per-cell detail AND cull), so it is left out here; two owners of
            // one `enabled` flag is a fight nobody wins. Inactive cells count
            // too — a disabled SceneryLod still owns its renderers.
            scratch.Clear();
            root.GetComponentsInChildren(true, scratch);
            int kept = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                var r = scratch[i];
                if (r == null) continue;
                if (r.GetComponentInParent<SceneryLod>(true) != null) continue;
                scratch[kept++] = r;
            }

            if (kept == 0)
            {
                g.renderers = System.Array.Empty<Renderer>();
                g.centre = root.position;
                g.radius = 0f;
                scratch.Clear();
                return;
            }

            var rends = new Renderer[kept];
            for (int i = 0; i < kept; i++) rends[i] = scratch[i];
            scratch.Clear();

            var b = rends[0].bounds;
            for (int i = 1; i < kept; i++) b.Encapsulate(rends[i].bounds);

            g.renderers = rends;
            // Bounds, not the island's nominal radius: props stand past it.
            g.centre = b.center;
            g.radius = Mathf.Max(b.extents.x, b.extents.z);

            // A rebuild must not resurrect a culled island: if the group is
            // off, the fresh renderers go off too. If it is ON the flags are
            // left alone -- the new renderers arrived enabled, and forcing
            // every one of them on would stomp anything another system had
            // deliberately turned off under this root.
            if (apply && !g.visible) Apply(g);
        }

        void Apply(Group g)
        {
            var rends = g.renderers;
            for (int r = 0; r < rends.Length; r++)
                if (rends[r] != null) rends[r].enabled = g.visible;
        }

        /// For probes: how many groups are being managed and how many are drawn.
        public string Describe()
        {
            int on = 0;
            foreach (var g in groups) if (g.visible) on++;
            float cull = settings != null
                ? settings.viewRadius * settings.chunkSize + margin : 0f;
            return $"DistantSceneryCull: {groups.Count} groups, {on} drawn, cull at {cull:F0} m";
        }
    }
}
