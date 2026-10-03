using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The level 2 wall and gate (2026-10-01), `art-staging/wall-gate-lvl2-v1`:
    /// a stone base under squared oak timbers, crude stone pillars.**
    ///
    /// Puts the two kit FBXs (`Art/WallL2/Models/wall-l2-kit.fbx`,
    /// `gate-l2-kit.fbx`) behind nine wrappers in `Resources/Palisade`, next
    /// to level 1's and in the same frame, so `WallVisual` places level 2
    /// with the code that places level 1:
    ///
    /// - `Wall2_Run_1m_A/B/C`, `Wall2_Filler_050m`, `Wall2_Filler_025m`,
    ///   `Wall2_Breached_1m`: +Z along the run, +Y up, the rear (rails) on
    ///   -X, root on `__Snap_Start`.
    /// - `Wall2_Post`: root on `__Post_Center`.
    /// - `Gate_L2` (the kit's `Gate2`) and `Gate_Breached_L2` (`Gate2_Breached`):
    ///   the 3 m module, root on `__Snap_Start`; the hinges stay at the
    ///   wrapper root with their leaves under them, so `GateLeaves` swings
    ///   them exactly as it swings `Gate_L1`'s.
    ///
    /// Unlike level 1 each FBX holds several pieces, and **it carries no
    /// materials at all** -- the colour is the one vertex colour layer
    /// ("Col") -- so every slot gets ONE material, `SS_WallL2_Vertex`, on
    /// `SeaSick/Environment Toon Textured` with no map: vertex colour *
    /// `_BaseColor` (white), the way level 1's map-less pegs are drawn.
    ///
    /// The frame is measured, never assumed: along = Snap_Start -> Snap_End,
    /// up = the axis across the run the mesh spans furthest, signed toward
    /// the tops, and the rear = along x up (the kit is Blender +X along,
    /// +Y rear, +Z up, and the import's reflection makes that cross product
    /// come out as the rear in Unity's axes -- level 1's measured frame
    /// agrees: +Z x +Y = -X, its rails). The gate then checks it: its hinges
    /// sit near the FRONT (+X), and a +100 deg turn of the left hinge about
    /// up swings its leaf out to +X, as `GateLeaves` expects.
    ///
    /// Run: copy the two FBXs to `Assets/_Project/Art/WallL2/Models/`, then
    /// `SeaSick.Dev.WallL2Import.Execute()` (or the menu item). Idempotent;
    /// GUIDs are kept after the first run.
    public static class WallL2Import
    {
        const string Root = "Assets/_Project/Art/WallL2";
        const string Models = Root + "/Models";
        const string WallKit = Models + "/wall-l2-kit.fbx";
        const string GateKit = Models + "/gate-l2-kit.fbx";
        const string Wrappers = "Assets/_Project/Resources/Palisade";
        const string ShaderName = "SeaSick/Environment Toon Textured";
        const string MatName = "SS_WallL2_Vertex";

        /// Kit piece -> (wrapper name, span along the run; 0 = a post).
        static readonly (string piece, string wrapper, float len)[] WallPieces =
        {
            ("Wall2_Run_1m_A", "Wall2_Run_1m_A", 1f),
            ("Wall2_Run_1m_B", "Wall2_Run_1m_B", 1f),
            ("Wall2_Run_1m_C", "Wall2_Run_1m_C", 1f),
            ("Wall2_Filler_050m", "Wall2_Filler_050m", 0.5f),
            ("Wall2_Filler_025m", "Wall2_Filler_025m", 0.25f),
            ("Wall2_Breached_1m", "Wall2_Breached_1m", 1f),
            ("Wall2_Post", "Wall2_Post", 0f),
        };

        static readonly (string piece, string wrapper, float len)[] GatePieces =
        {
            ("Gate2", "Gate_L2", 3f),
            ("Gate2_Breached", "Gate_Breached_L2", 3f),
        };

        [MenuItem("SeaSick/Art/Import level 2 wall + gate (stone base V1)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureFolder(Root, "Materials");
            var mat = VertexMaterial(log);
            Import(WallKit, log);
            Import(GateKit, log);
            var wallFrame = MeasureFrame(WallKit, "Wall2_Run_1m_A", log);
            var gateFrame = MeasureFrame(GateKit, "Gate2", log);
            foreach (var p in WallPieces) Wrap(WallKit, p.piece, p.wrapper, p.len, wallFrame, mat, log);
            foreach (var p in GatePieces) Wrap(GateKit, p.piece, p.wrapper, p.len, gateFrame, mat, log);
            CheckGate(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[WallL2] " + log);
            return log.ToString();
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        // --- the material ----------------------------------------------------------

        static Material VertexMaterial(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            string path = Root + "/Materials/" + MatName + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", null);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Ambient", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            log.AppendLine("material: " + path);
            return m;
        }

        // --- the models --------------------------------------------------------------

        static void Import(string path, StringBuilder log)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model at " + path + " -- copy the kit FBXs there first");
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;  // flat authored normals
            mi.importTangents = ModelImporterTangents.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            // Read/Write ON, as level 1: `WallVisual` merges the pieces into
            // one mesh per segment at runtime, which a player build can only
            // do from a CPU-readable mesh.
            mi.isReadable = true;
            mi.preserveHierarchy = true;
            // The kit has no materials; the wrappers assign the one material.
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int colours = model.GetComponentsInChildren<MeshFilter>(true)
                .Count(f => f.sharedMesh != null && f.sharedMesh.colors32 != null && f.sharedMesh.colors32.Length > 0);
            int meshes = model.GetComponentsInChildren<MeshFilter>(true).Length;
            log.AppendLine($"{path}: {meshes} meshes, {colours} with vertex colour");
            if (colours < meshes)
                throw new System.Exception(path + ": a mesh came in without its vertex colour layer");
        }

        // --- the frame ---------------------------------------------------------------

        static Transform FindExact(Transform root, string name)
            => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
               ?? throw new System.Exception(root.name + ": no " + name);

        /// Does this node belong to `piece`? Its root empty, its mesh, its
        /// markers (`piece__*`) and, for the gate, its named parts
        /// (`Gate2_Pillar_Left`...) but never the breached gate's.
        static bool Belongs(string piece, string name)
        {
            if (name == piece || name == piece + "_Root" || name.StartsWith(piece + "__")) return true;
            if (piece == "Gate2") return name.StartsWith("Gate2_") && !name.StartsWith("Gate2_Breached");
            if (piece == "Gate2_Breached") return name.StartsWith("Gate2_Breached_");
            return false;
        }

        static List<MeshFilter> PieceMeshes(Transform kit, string piece)
            => kit.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && Belongs(piece, f.name)).ToList();

        /// The rotation that takes the imported kit's axes to the wrapper
        /// frame (+Z along, +Y up, rear on -X), measured on `piece`.
        static Quaternion MeasureFrame(string kitPath, string piece, StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(kitPath));
            try
            {
                var s = FindExact(go.transform, piece + "__Snap_Start").position;
                var e = FindExact(go.transform, piece + "__Snap_End").position;
                Vector3 along = (e - s).normalized;
                var meshes = PieceMeshes(go.transform, piece);
                if (meshes.Count == 0) throw new System.Exception(kitPath + ": no meshes for " + piece);

                Vector3 best = Vector3.zero; float bestSpan = -1f; float bestSign = 1f;
                foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                {
                    if (Mathf.Abs(Vector3.Dot(axis, along)) > 0.5f) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var mf in meshes)
                        foreach (var v in mf.sharedMesh.vertices)
                        {
                            float d = Vector3.Dot(mf.transform.TransformPoint(v) - s, axis);
                            lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                        }
                    if (hi - lo > bestSpan) { bestSpan = hi - lo; best = axis; bestSign = hi > -lo ? 1f : -1f; }
                }
                Vector3 up = best * bestSign;
                var q = Quaternion.Inverse(Quaternion.LookRotation(along, up));
                log.AppendLine($"{piece} frame: along {along:F3} up {up:F3} (span {bestSpan:F2} m)");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- the wrappers ------------------------------------------------------------

        static void Wrap(string kitPath, string piece, string wrapper, float len, Quaternion frame,
            Material mat, StringBuilder log)
        {
            string path = Wrappers + "/" + wrapper + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var fresh = new GameObject(wrapper);
                PrefabUtility.SaveAsPrefabAsset(fresh, path);
                Object.DestroyImmediate(fresh);
            }
            string guid = AssetDatabase.AssetPathToGUID(path);

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception(wrapper + ": wrapper root is not at unit scale/identity");
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var kit = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(kitPath));
                kit.transform.SetPositionAndRotation(Vector3.zero, frame * kit.transform.rotation);
                var anchor = FindExact(kit.transform, piece + (len <= 0f ? "__Post_Center" : "__Snap_Start"));
                kit.transform.position -= anchor.position;

                // This piece's top-most nodes only (the kit holds them all).
                var mine = kit.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != kit.transform && Belongs(piece, t.name)
                        && (t.parent == kit.transform || !Belongs(piece, t.parent.name)))
                    .ToList();
                if (mine.Count == 0) throw new System.Exception(kitPath + ": nothing named " + piece);
                foreach (var t in mine) t.SetParent(root, true);
                Object.DestroyImmediate(kit);

                // Flat, like level 1's wrappers: every node straight under the
                // root -- EXCEPT a hinge's children (the leaves), which have to
                // turn with it.
                foreach (var t in root.GetComponentsInChildren<Transform>(true)
                             .Where(t => t != root && t.parent != root && !IsHinge(t.parent)).ToList())
                    t.SetParent(root, true);
                // The Blender root empties: nothing hangs off them once flattened.
                foreach (Transform t in root.Cast<Transform>().ToList())
                    if ((t.name == piece || t.name == piece + "_Root") && t.childCount == 0
                        && t.GetComponents<Component>().Length == 1)
                        Object.DestroyImmediate(t.gameObject);

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var f = r.GetComponent<MeshFilter>();
                    int subs = f != null && f.sharedMesh != null ? Mathf.Max(1, f.sharedMesh.subMeshCount) : 1;
                    r.sharedMaterials = Enumerable.Repeat(mat, subs).ToArray();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                var bounds = Bounds(root);
                if (len > 0f)
                {
                    var s = FindExact(root, piece + "__Snap_Start").localPosition;
                    var e = FindExact(root, piece + "__Snap_End").localPosition;
                    log.AppendLine($"{wrapper}: start {s:F3} end {e:F3} (want +Z {len}) bounds {bounds.min:F2}..{bounds.max:F2} tris {Tris(root)}");
                    if (Mathf.Abs(e.z - s.z - len) > 0.002f || Mathf.Abs(e.x - s.x) > 0.002f || Mathf.Abs(e.y - s.y) > 0.002f)
                        throw new System.Exception(wrapper + ": markers are not +Z " + len + " apart after the frame");
                }
                else
                    log.AppendLine($"{wrapper}: bounds {bounds.min:F2}..{bounds.max:F2} tris {Tris(root)}");
                if (bounds.max.y < 2f)
                    throw new System.Exception($"{wrapper}: only {bounds.max.y:F2} m tall -- the frame or scale is off");

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(path) != guid)
                throw new System.Exception(wrapper + ": wrapper GUID changed");
        }

        static bool IsHinge(Transform t) => t != null && t.name.StartsWith("Gate2_Hinge");

        /// **The gate's swing, checked in the wrapper.** The hinges sit near
        /// the front (+X, away from the rear the camp sees), and `GateLeaves`
        /// turns the left hinge +100 deg about up and the right one -100 deg:
        /// both leaves must then lie on the +X side.
        static void CheckGate(StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Wrappers + "/Gate_L2.prefab");
            var go = Object.Instantiate(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var left = FindExact(go.transform, "Gate2_Hinge");
                var right = FindExact(go.transform, "Gate2_Hinge_Right");
                FindExact(go.transform, "Gate2__Passage");
                if (left.parent != go.transform || right.parent != go.transform)
                    throw new System.Exception("Gate_L2: the hinges are not at the wrapper root");
                if (left.GetComponentInChildren<MeshRenderer>() == null || right.GetComponentInChildren<MeshRenderer>() == null)
                    throw new System.Exception("Gate_L2: a leaf is not under its hinge");
                float closedL = LeafX(left), closedR = LeafX(right);
                left.localRotation = Quaternion.AngleAxis(100f, Vector3.up) * left.localRotation;
                right.localRotation = Quaternion.AngleAxis(-100f, Vector3.up) * right.localRotation;
                float openL = LeafX(left), openR = LeafX(right);
                log.AppendLine($"gate: hinges at x {left.localPosition.x:F3} / {right.localPosition.x:F3}, z {left.localPosition.z:F3} / {right.localPosition.z:F3}; "
                    + $"leaf centre x closed {closedL:F2}/{closedR:F2} -> open {openL:F2}/{openR:F2}");
                if (left.localPosition.x <= 0f || right.localPosition.x <= 0f)
                    throw new System.Exception("Gate_L2: hinges on -X -- the kit is mirrored against the wrapper frame");
                if (openL < closedL + 0.3f || openR < closedR + 0.3f)
                    throw new System.Exception("Gate_L2: a leaf does not swing out to +X on GateLeaves' angles");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static float LeafX(Transform hinge)
        {
            var b = new Bounds(); bool any = false;
            foreach (var r in hinge.GetComponentsInChildren<MeshRenderer>(true))
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            return b.center.x;
        }

        static Bounds Bounds(Transform root)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static int Tris(Transform root)
            => root.GetComponentsInChildren<MeshFilter>(true).Sum(f =>
            {
                if (f.sharedMesh == null) return 0;
                long n = 0;
                for (int i = 0; i < f.sharedMesh.subMeshCount; i++) n += f.sharedMesh.GetIndexCount(i);
                return (int)(n / 3);
            });
    }
}
