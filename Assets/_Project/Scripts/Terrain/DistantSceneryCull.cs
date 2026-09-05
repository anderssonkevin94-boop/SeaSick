using System.Collections.Generic;
using UnityEngine;

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
        }

        readonly List<Group> groups = new List<Group>();
        float nextScan, nextTest;

        void OnEnable() { nextScan = 0f; nextTest = 0f; }

        void Update()
        {
            if (settings == null) return;
            if (target == null && Camera.main != null) target = Camera.main.transform;
            if (target == null) return;

            if (Time.time >= nextScan) { nextScan = Time.time + scanInterval; Scan(); }
            if (Time.time < nextTest) return;
            nextTest = Time.time + testInterval;

            float cull = settings.viewRadius * settings.chunkSize + margin;
            Vector3 p = target.position;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.root == null) continue;
                float d = Vector2.Distance(new Vector2(p.x, p.z),
                                           new Vector2(g.centre.x, g.centre.z)) - g.radius;
                bool want = d <= cull;
                if (want == g.visible) continue;
                g.visible = want;
                for (int r = 0; r < g.renderers.Length; r++)
                    if (g.renderers[r] != null) g.renderers[r].enabled = want;
            }
        }

        void Scan()
        {
            // Islands and reefs arrive over time as the world populates, so
            // this re-scans rather than caching once at start.
            var roots = new List<Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.parent != null) continue;
                if (!t.name.StartsWith("Island_") && !t.name.StartsWith("Reef_")) continue;
                roots.Add(t);
            }
            if (roots.Count == groups.Count) return;

            groups.Clear();
            foreach (var root in roots)
            {
                var rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) continue;
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                groups.Add(new Group
                {
                    root = root, renderers = rends,
                    centre = b.center,
                    radius = Mathf.Max(b.extents.x, b.extents.z),
                    visible = true,
                });
            }
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
