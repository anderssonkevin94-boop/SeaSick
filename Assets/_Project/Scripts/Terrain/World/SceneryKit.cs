using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// The Blender-built templates that `IslandScenery` stamps into its
    /// welded meshes: trees at two levels of detail, unit boulder and cliff
    /// shards, and the ore outcrop.
    ///
    /// Built by `tools/blender/seasick_style.py` (`build_kit` + `export_kit`)
    /// into `Resources/Flora/seasick_flora.fbx`. Every template stands on
    /// its origin, in metres; colour is in the vertices, so the whole kit
    /// draws with the terrain's vertex-colour shader and adds no material.
    ///
    /// Why templates and not prefabs: the scenery is hundreds of trees in one
    /// mesh per cell precisely so it costs one draw call, and that is worth
    /// keeping (see `IslandScenery`). A prefab per tree would be thousands of
    /// renderers an island. So the kit is read ONCE into arrays and copied
    /// into the welded buffers at bake time.
    public static class SceneryKit
    {
        public class Template
        {
            public string name;
            public Vector3[] v;
            public Vector3[] n;
            public Color32[] c;
            public int[] t;
            /// Top of the mesh above its origin, metres.
            public float height;
            /// Widest reach from the origin in XZ, metres.
            public float radius;
            public int Tris => t.Length / 3;
        }

        static string loadedPath;
        static Dictionary<string, Template> byName;
        static bool tried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            loadedPath = null; tried = false; byName = null;
            foreach (var m in meshCache.Values) if (m != null) Object.Destroy(m);
            meshCache.Clear();
        }

        public static bool Available => Load() != null;

        public static Template Get(string name)
        {
            var d = Load();
            return d != null && d.TryGetValue(name, out var tp) ? tp : null;
        }

        public static IEnumerable<string> Names => Load() != null ? byName.Keys : System.Array.Empty<string>();

        static Dictionary<string, Template> Load()
        {
            string path = SeaSick.World.WorldArtStyle.SceneryResource;
            if (loadedPath != path)
            {
                loadedPath = path;
                tried = false;
                byName = null;
            }
            if (tried) return byName;
            tried = true;
            var go = Resources.Load<GameObject>(path);
            if (go == null)
            {
                Debug.LogWarning("SceneryKit: Resources/" + path + ".fbx not found -- islands fall back to procedural cones");
                return null;
            }
            var found = new Dictionary<string, Template>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                if (!m.isReadable)
                {
                    Debug.LogError("SceneryKit: mesh '" + mf.name + "' is not readable; FloraImport should have set it. Reimport the FBX.");
                    continue;
                }
                var tp = new Template
                {
                    name = mf.name, v = m.vertices, n = m.normals, c = m.colors32, t = m.triangles,
                };
                // The FBX is exported with the transform baked into the
                // vertices, but if a child ever carries one, honour it.
                var local = mf.transform.localToWorldMatrix;
                if (local != Matrix4x4.identity)
                {
                    for (int i = 0; i < tp.v.Length; i++)
                    {
                        tp.v[i] = local.MultiplyPoint3x4(tp.v[i]);
                        tp.n[i] = local.MultiplyVector(tp.n[i]).normalized;
                    }
                }
                if (tp.c == null || tp.c.Length != tp.v.Length)
                {
                    Debug.LogWarning("SceneryKit: '" + mf.name + "' has no vertex colours; painting it grey");
                    tp.c = new Color32[tp.v.Length];
                    for (int i = 0; i < tp.c.Length; i++) tp.c[i] = new Color32(120, 118, 112, 255);
                }
                if (tp.n == null || tp.n.Length != tp.v.Length)
                {
                    tp.n = new Vector3[tp.v.Length];
                    for (int i = 0; i < tp.n.Length; i++) tp.n[i] = Vector3.up;
                }
                float top = 0f, reach = 0f;
                for (int i = 0; i < tp.v.Length; i++)
                {
                    top = Mathf.Max(top, tp.v[i].y);
                    reach = Mathf.Max(reach, Mathf.Sqrt(tp.v[i].x * tp.v[i].x + tp.v[i].z * tp.v[i].z));
                }
                tp.height = top;
                tp.radius = reach;
                found[mf.name] = tp;
            }
            if (found.Count == 0)
            {
                Debug.LogWarning("SceneryKit: the FBX has no readable meshes");
                return null;
            }
            byName = found;
            var sb = new System.Text.StringBuilder("SceneryKit: ");
            foreach (var kv in found) sb.Append(kv.Key).Append(' ').Append(kv.Value.Tris).Append("t/").Append(kv.Value.height.ToString("F1")).Append("m  ");
            Debug.Log(sb.ToString());
            return byName;
        }

        /// Copy a template into the welded buffers at `at`, yawed about Y by
        /// `yaw` radians and scaled per axis. Non-uniform scale is what turns
        /// a unit shard into a cliff, so the normals are transformed by the
        /// inverse-transpose rather than just scaled.
        public static void Stamp(Template tp, List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, float yaw, Vector3 scale)
        {
            int baseIdx = v.Count;
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            float inx = 1f / Mathf.Max(1e-4f, scale.x), iny = 1f / Mathf.Max(1e-4f, scale.y), inz = 1f / Mathf.Max(1e-4f, scale.z);
            for (int i = 0; i < tp.v.Length; i++)
            {
                var p = tp.v[i];
                float px = p.x * scale.x, py = p.y * scale.y, pz = p.z * scale.z;
                v.Add(new Vector3(at.x + px * cs + pz * sn, at.y + py, at.z - px * sn + pz * cs));
                var q = tp.n[i];
                float qx = q.x * inx, qy = q.y * iny, qz = q.z * inz;
                float len = Mathf.Sqrt(qx * qx + qy * qy + qz * qz);
                if (len > 1e-6f) { qx /= len; qy /= len; qz /= len; }
                n.Add(new Vector3(qx * cs + qz * sn, qy, -qx * sn + qz * cs));
                c.Add(tp.c[i]);
            }
            for (int k = 0; k < tp.t.Length; k++) t.Add(baseIdx + tp.t[k]);
        }

        /// Same, with a full rotation: a cliff shard lies AGAINST its slope,
        /// which a yaw alone cannot do.
        public static void Stamp(Template tp, List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, Quaternion rot, Vector3 scale)
        {
            int baseIdx = v.Count;
            var inv = new Vector3(1f / Mathf.Max(1e-4f, scale.x), 1f / Mathf.Max(1e-4f, scale.y), 1f / Mathf.Max(1e-4f, scale.z));
            for (int i = 0; i < tp.v.Length; i++)
            {
                v.Add(at + rot * Vector3.Scale(tp.v[i], scale));
                n.Add(rot * Vector3.Scale(tp.n[i], inv).normalized);
                c.Add(tp.c[i]);
            }
            for (int k = 0; k < tp.t.Length; k++) t.Add(baseIdx + tp.t[k]);
        }

        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        /// A standalone Mesh of one template, for the few props that are
        /// GameObjects of their own (harvest nodes). Cached: one mesh per
        /// template, however many props use it.
        public static Mesh MeshOf(string name)
        {
            string cacheKey = SeaSick.World.WorldArtStyle.SceneryResource + "/" + name;
            if (meshCache.TryGetValue(cacheKey, out var m) && m != null) return m;
            var tp = Get(name);
            if (tp == null) return null;
            m = new Mesh { name = "Kit_" + name };
            m.SetVertices(tp.v);
            m.SetNormals(tp.n);
            m.SetColors(tp.c);
            m.SetTriangles(tp.t, 0);
            m.RecalculateBounds();
            meshCache[cacheKey] = m;
            return m;
        }
    }
}
