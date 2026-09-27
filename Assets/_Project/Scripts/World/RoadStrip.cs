using UnityEngine;

namespace SeaSick.World
{
    /// **A flat ribbon draped on the ground between two points (2026-09-27)**
    /// -- the chalk drawing of a road segment: the siting ghost
    /// (`UI/RoadSiting`) and a queued road's blueprint (`BuildSite.PlaceRoad`).
    /// The finished road is drawn by `CampRoads`, not by this.
    public static class RoadStrip
    {
        /// Across, metres: the kit's 2.16 m road, a shade narrower so the
        /// chalk reads as a plan of it.
        public const float Width = 2f;
        const float Lift = 0.06f;

        /// A child of `parent` (placed at the world origin, identity) with a
        /// ribbon from `a` to `b`, sampled every metre on `ground`. The mesh
        /// is its own; `Free` destroys it with the object.
        public static GameObject Make(Transform parent, Vector3 a, Vector3 b,
            System.Func<Vector3, float> ground, string name = "RoadStrip")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            Vector3 run = b - a; run.y = 0f;
            float len = run.magnitude;
            var mesh = new Mesh { name = name };
            if (len > 0.01f)
            {
                Vector3 dir = run / len;
                Vector3 side = new Vector3(dir.z, 0f, -dir.x) * (0.5f * Width);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len));
                var v = new Vector3[(steps + 1) * 2];
                var t = new int[steps * 6];
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 c = a + run * (i / (float)steps);
                    Vector3 l = c - side, r = c + side;
                    l.y = (ground != null ? ground(l) : a.y) + Lift;
                    r.y = (ground != null ? ground(r) : a.y) + Lift;
                    v[i * 2] = l;
                    v[i * 2 + 1] = r;
                    if (i < steps)
                    {
                        int k = i * 6, o = i * 2;
                        // Clockwise from above: l0, l1, r0 / r0, l1, r1.
                        t[k] = o; t[k + 1] = o + 2; t[k + 2] = o + 1;
                        t[k + 3] = o + 1; t[k + 4] = o + 2; t[k + 5] = o + 3;
                    }
                }
                mesh.vertices = v;
                mesh.triangles = t;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<RoadStripMesh>();
            return go;
        }
    }
}
