using System.IO;
using System.Text;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeaSick.Ship.Modular.EditorTools
{
    /// **Batch-mode verification for milestone 1**, run on the modular-ships
    /// WORKTREE project only (never the main editor):
    ///
    ///   Unity -batchmode -projectPath SeaSick-modular
    ///         -executeMethod SeaSick.Ship.Modular.EditorTools.ModularShipPreview.Run
    ///
    /// 1. applies the ShipModules importer settings and imports the meshes;
    /// 2. logs every mesh's imported orientation and bounds (authoring units)
    ///    so the axis convention is checked against numbers, not assumed:
    ///    a hull section must run along +Z from its aft face, beam along X,
    ///    height along Y;
    /// 3. runs ModularShipSelfTest inside Unity (real JsonUtility, real
    ///    Resources.Load);
    /// 4. creates the isolated test scene;
    /// 5. renders preview PNGs of the milestone configurations to
    ///    Logs/modular-previews (gitignored) and logs the oversized rejection.
    /// Writes Logs/modular-preview.txt and exits 0 on success, 1 on failure.
    public static class ModularShipPreview
    {
        const string OutDir = "Logs/modular-previews";

        public static void Run()
        {
            var sb = new StringBuilder();
            bool ok = true;
            try
            {
                ModularShipTestSceneSetup.ConfigureImporters();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                sb.AppendLine("== imported orientation (authoring units, identity instance at origin)");
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Resources/ShipModules/Meshes" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { sb.AppendLine("  NULL " + path); ok = false; continue; }
                    var go = Object.Instantiate(prefab);
                    go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                    var b = WorldBounds(go);
                    var child = go.transform.childCount > 0 ? go.transform.GetChild(0) : null;
                    sb.AppendLine($"  {path.Replace("Assets/_Project/Resources/ShipModules/Meshes/", ""),-44} min {b.min.ToString("F2")} max {b.max.ToString("F2")}"
                        + $" rootRot {go.transform.localEulerAngles.ToString("F0")} childRot {(child != null ? child.localEulerAngles.ToString("F0") : "-")} childScale {(child != null ? child.localScale.ToString("F2") : "-")}");
                    Object.DestroyImmediate(go);
                }

                bool testOk = ModularShipSelfTest.Run();
                sb.AppendLine($"== ModularShipSelfTest in Unity: {ModularShipSelfTest.Passed} PASS, {ModularShipSelfTest.Failed} FAIL");
                if (!testOk) { ok = false; sb.AppendLine(ModularShipSelfTest.Report); }

                ModularShipTestSceneSetup.CreateTestScene();
                sb.AppendLine("== test scene created");

                Directory.CreateDirectory(OutDir);
                var lib = ModuleLibrary.LoadFromResources();
                var shots = new (string name, ShipConfiguration cfg)[]
                {
                    ("short-reinforced", ShipConfiguration.Short()),
                    ("long-reinforced", ShipConfiguration.Long()),
                    ("long-timber", WithRotor(ShipConfiguration.Long(), "wheel.rotor.m1.timber")),
                    ("long-plus-two-bays", ShipConfiguration.WithMiddles(3)),
                    ("expanded-short", ExpandedPresets.ExpandedShort()),
                    ("expanded-long", ExpandedPresets.ExpandedLong()),
                    ("expanded-long-top", ExpandedPresets.ExpandedLong()),
                    ("long-top", ShipConfiguration.Long()),
                    // docs/RAISED-DECK.md sec 9: raised Long and two-bay
                    // raised, same three views as the W1x expanded hull
                    // (side, 3/4, top) plus the per-part placement log the
                    // "-top" renders already produce below.
                    ("raised-long", RaisedPresets.RaisedLong()),
                    ("raised-long-side", RaisedPresets.RaisedLong()),
                    ("raised-long-top", RaisedPresets.RaisedLong()),
                    ("raised-two-bay", RaisedPresets.RaisedTwoBay()),
                    ("raised-two-bay-side", RaisedPresets.RaisedTwoBay()),
                    ("raised-two-bay-top", RaisedPresets.RaisedTwoBay()),
                };
                foreach (var (name, cfg) in shots)
                {
                    var res = ShipAssembler.Assemble(cfg, lib);
                    if (!res.ok) { sb.AppendLine($"== {name}: REJECTED {res.Summary()}"); ok = false; continue; }
                    sb.AppendLine($"== {name}: {res.Summary()}  placeholders [{string.Join(", ", res.placeholders)}]");
                    Render(name, res, sb, false);
                    if (name == "long-reinforced" || name == "long-timber" || name == "expanded-long") Render(name + "-stern", res, sb, true);
                }
                var bad = ShipAssembler.Assemble(WithRotor(ShipConfiguration.Long(), "wheel.rotor.m1l.oversized"), lib);
                sb.AppendLine($"== oversized on the standard stern: ok={bad.ok}");
                foreach (var r in bad.rejections) sb.AppendLine($"   {r.code}: {r.message}");
                if (bad.ok) ok = false;
            }
            catch (System.Exception e)
            {
                ok = false;
                sb.AppendLine("EXCEPTION " + e);
            }
            File.WriteAllText("Logs/modular-preview.txt", sb.ToString());
            Debug.Log("[ModularShipPreview]\n" + sb);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static string GetPath(Transform t, Transform root)
        {
            string p = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static ShipConfiguration WithRotor(ShipConfiguration c, string rotorId) { c.rotorId = rotorId; return c; }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static void Render(string name, AssemblyResult res, StringBuilder sb, bool sternClose)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);

            var shipGo = new GameObject("ModularShip");
            var view = shipGo.AddComponent<ModularShipView>();
            view.Build(res);
            var b = WorldBounds(shipGo);
            sb.AppendLine($"   world bounds min {b.min.ToString("F2")} max {b.max.ToString("F2")} size {b.size.ToString("F2")} m");
            if (name.EndsWith("-top"))
                // Per-part placement, for multi-part hulls: a half placed twice
                // (or not at all) shows as an x range off the centreline pattern.
                foreach (var r in shipGo.GetComponentsInChildren<Renderer>(true))
                    if (GetPath(r.transform, shipGo.transform).Contains("equipment") || r.name.Contains("Shell"))
                        sb.AppendLine($"     part {GetPath(r.transform, shipGo.transform),-70} x {r.bounds.min.x:F2}..{r.bounds.max.x:F2}  y {r.bounds.min.y:F2}..{r.bounds.max.y:F2}  z {r.bounds.min.z:F2}..{r.bounds.max.z:F2}");

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.55f, 0.66f);
            cam.fieldOfView = 35f;
            float dist = Mathf.Max(b.size.z, b.size.x) * 1.9f;
            // Three-quarter view from starboard (+X) and slightly aft, looking at the centre.
            camGo.transform.position = b.center + new Vector3(dist * 0.78f, dist * 0.42f, -dist * 0.46f);
            camGo.transform.LookAt(b.center);
            if (name.EndsWith("-top"))
            {
                camGo.transform.position = b.center + new Vector3(0f, dist * 0.9f, 0f);
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else if (name.EndsWith("-side"))
            {
                // Straight profile (beam-on, +X), slightly above the
                // waterline centre for a readable elevation.
                camGo.transform.position = b.center + new Vector3(dist * 1.0f, dist * 0.08f, 0f);
                camGo.transform.LookAt(b.center);
            }
            else if (sternClose && res.hasWheel)
            {
                // Close three-quarter view from aft and low, on the wheel pocket.
                var axle = res.wheelAxleM;
                camGo.transform.position = axle + new Vector3(2.6f, 0.9f, -3.4f);
                camGo.transform.LookAt(axle + new Vector3(0f, 0.2f, 0.3f));
                cam.fieldOfView = 40f;
                var pivot = view.RotorPivot;
                if (pivot != null) sb.AppendLine($"   rotor pivot at {pivot.position.ToString("F3")} m, parts under pivot {pivot.GetComponentsInChildren<Renderer>().Length}");
            }

            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
