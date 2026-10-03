using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Dev
{
    /// **Edit-mode shots of the storage-slot containers with stock in them
    /// (2026-10-03)**, no play mode (RAM is tight; `BarrowShot`'s rig: staged
    /// high above the world on a grass quad, a throwaway camera rendered
    /// into a RenderTexture, everything destroyed before it returns).
    ///
    /// Stages the real wrappers `StorageL1Import` builds, exactly as
    /// `BuildingFactory` puts them on: the store hut's `storage_l1` as a
    /// root's `Model`, and Astra's campfire with `firecache_l1` at its
    /// FIRST bearing as `Outpost.PlaceFireCache` places it on open ground
    /// (behind the fire, near hem `Outpost.FireCacheRingMargin` past the
    /// supper ring, front toward the fire; a real camp may take a later
    /// bearing if that one is not clear). A real `StorageSlotView` on each
    /// root shows one fake ledger through its own allocation and kit code
    /// (`StorageSlotView.StageForShot`): timber 13, stone 25, boards 7,
    /// potato 30, carrot 5, fish 3, meat 2, hide 1, baked potato 9, spear 3.
    ///
    /// Three sites: the hut (first store, its slots first); the fire cache
    /// BEFORE a hut (it holds the camp's store); the fire cache AFTER one
    /// (it only takes the overflow past the hut's slots, so with this ledger
    /// mostly empty -- the "hut fills first" rule made visible).
    ///
    /// Shots per site, phone portrait (540 x 1170, the 1080 x 2340 screen
    /// at half size): `camp50` (the camp camera's ~50 deg pitch, ~20 m of
    /// ground up the frame), `close50` (same pitch, ~8 m, the closest zoom),
    /// `front30` (a lower three-quarter, 30 deg, yaw 25). The camera sits on
    /// the hut's working front (+Z) and, for the fire, on the far side of
    /// the fire from the cache (the cache's open front faces the fire).
    ///
    /// `unity cmd eval --json --code 'return SeaSick.Dev.StorageShot.Run("/tmp/storage-shots");'`
    /// Returns which slot shows which step, and the files written.
    public static class StorageShot
    {
        static Vector3 Origin => new Vector3(0f, 1500f, 0f);
        const int W = 540, H = 1170;
        const float Fov = 30f;

        static readonly Dictionary<string, int> Ledger = new Dictionary<string, int>
        {
            { Res.Timber, 13 }, { Res.Stone, 25 }, { Res.Boards, 7 },
            { Res.Potato, 30 }, { Res.Carrot, 5 },
            { Res.Fish, 3 }, { Res.Meat, 2 }, { Res.Hide, 1 },
            { Res.BakedPotato, 9 }, { Res.Spear, 3 },
        };

        static int Count(string res) => res != null && Ledger.TryGetValue(res, out int n) ? n : 0;

        public static string Run(string outDir)
        {
            if (Application.isPlaying) return "stop play mode first";
            var hutAsset = Resources.Load<GameObject>(BuildingFactory.StorageL1Prefab);
            var cacheAsset = Resources.Load<GameObject>(BuildingFactory.FireCachePrefab);
            var fireAsset = Resources.Load<GameObject>(BuildPlans.Campfire.prefab);
            if (hutAsset == null || cacheAsset == null)
                return $"no wrappers at Resources/{BuildingFactory.StorageL1Prefab} / {BuildingFactory.FireCachePrefab}: run StorageL1Import first";
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            sb.Append("ledger:");
            foreach (var kv in Ledger) sb.Append($" {kv.Key} {kv.Value}");
            sb.AppendLine();
            var made = new List<Object>();
            RenderTexture rt = null;
            try
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                made.Add(ground);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.transform.SetPositionAndRotation(Origin + new Vector3(20f, -0.01f, 0f), Quaternion.Euler(90f, 0f, 0f));
                ground.transform.localScale = new Vector3(140f, 140f, 1f);
                var gmat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                made.Add(gmat);
                gmat.SetColor("_BaseColor", new Color(0.47f, 0.56f, 0.38f));
                ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

                var lightGo = new GameObject("StorageShot_Light");
                made.Add(lightGo);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.3f;
                l.shadows = LightShadows.Soft;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                // --- the hut: the camp's first store ---------------------------
                var hut = new GameObject("StorageShot_Hut");
                made.Add(hut);
                hut.transform.SetPositionAndRotation(Origin, Quaternion.identity);
                Model(hutAsset, hut.transform);
                sb.AppendLine("== store hut (first store)");
                sb.Append(hut.AddComponent<StorageSlotView>().StageForShot(Count, null));

                // --- the fire with its cache, before and after a hut -------------
                var hutSlots = StorageSlots.SlotsOf(BuildPlans.Storage.id, 1);
                var before = Fire(fireAsset, cacheAsset, Origin + new Vector3(20f, 0f, 0f), "Before", made);
                sb.AppendLine("== fire cache, no store hut yet (it IS the store)");
                sb.Append(before.AddComponent<StorageSlotView>().StageForShot(Count, null));
                var after = Fire(fireAsset, cacheAsset, Origin + new Vector3(40f, 0f, 0f), "After", made);
                sb.AppendLine("== fire cache behind a store hut (overflow past the hut's slots)");
                sb.Append(after.AddComponent<StorageSlotView>().StageForShot(Count, hutSlots));

                // --- shots --------------------------------------------------------
                var camGo = new GameObject("StorageShot_Cam");
                made.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.76f, 0.88f);
                cam.fieldOfView = Fov;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                rt = new RenderTexture(W, H, 24);
                cam.targetTexture = rt;
                cam.aspect = W / (float)H;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                made.Add(tex);

                Vector3 hutAim = Origin + new Vector3(0f, 1.0f, 0f);
                // The cache's middle, in the fire's frame.
                Vector3 cacheLocal = CacheRoot()
                    + new Vector3(BuildingFactory.FireCacheBox.x, 0.4f, BuildingFactory.FireCacheBox.y);
                var sites = new (string name, Vector3 aim, Vector3 camp)[]
                {
                    ("hut", hutAim, hutAim),
                    // camp50 aims between fire and cache so both are in frame.
                    ("cache-before", before.transform.TransformPoint(cacheLocal), before.transform.TransformPoint(cacheLocal * 0.5f)),
                    ("cache-after", after.transform.TransformPoint(cacheLocal), after.transform.TransformPoint(cacheLocal * 0.5f)),
                };
                foreach (var (name, aim, camp) in sites)
                {
                    sb.Append(Shoot(cam, rt, tex, camp, 50f, 0f, 20f, Path.Combine(outDir, name + "-camp50.jpg")));
                    sb.Append(Shoot(cam, rt, tex, aim, 50f, 0f, 8f, Path.Combine(outDir, name + "-close50.jpg")));
                    sb.Append(Shoot(cam, rt, tex, aim, 30f, 25f, 8f, Path.Combine(outDir, name + "-front30.jpg")));
                }
                cam.targetTexture = null;
            }
            catch (System.Exception e) { sb.AppendLine("FAILED: " + e); }
            finally
            {
                for (int i = made.Count - 1; i >= 0; i--) if (made[i] != null) Object.DestroyImmediate(made[i]);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
            return sb.ToString();
        }

        /// `BuildingFactory.Dress`'s placement: a `Model` child at zero.
        static GameObject Model(GameObject asset, Transform root)
        {
            var m = Object.Instantiate(asset, root);
            m.name = "Model";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            return m;
        }

        /// A campfire root wearing Astra's fire (when it loads) and the cache
        /// beside it at `CacheRoot` (bearing #0 on open ground).
        static GameObject Fire(GameObject fireAsset, GameObject cacheAsset, Vector3 at, string tag, List<Object> made)
        {
            var root = new GameObject("StorageShot_Fire" + tag);
            made.Add(root);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);
            if (fireAsset != null) Model(fireAsset, root.transform);
            var cache = Object.Instantiate(cacheAsset, root.transform);
            cache.name = BuildingFactory.FireCacheChild;
            cache.transform.localPosition = CacheRoot();
            cache.transform.localRotation = Quaternion.identity;   // front +Z, toward the fire
            return root;
        }

        /// The cache root in the fire's frame, straight behind it (-Z):
        /// `Outpost.ChooseFireCache`'s distance (supper ring + margin + the
        /// 1.18 m from the cache root to its near hem).
        static Vector3 CacheRoot()
        {
            var k = BuildingFactory.FireCacheBox;
            float d = Mathf.Max(Outpost.FireRingRadius, SeaSick.World.Life.CampLifeTuning.FireRingRadius)
                + Outpost.FireCacheRingMargin + k.y + k.w;
            return new Vector3(0f, 0f, -d);
        }

        /// One portrait JPG: the lens `pitch` degrees down at `aim`, from
        /// `yaw` degrees round from +Z, far enough that about `ground`
        /// metres of ground run up the frame.
        static string Shoot(Camera cam, RenderTexture rt, Texture2D tex, Vector3 aim,
            float pitch, float yaw, float ground, string file)
        {
            float d = ground * Mathf.Sin(pitch * Mathf.Deg2Rad) / (2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
            Vector3 back = Quaternion.Euler(0f, yaw, 0f)
                * new Vector3(0f, Mathf.Sin(pitch * Mathf.Deg2Rad), Mathf.Cos(pitch * Mathf.Deg2Rad));
            Vector3 from = aim + back * d;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(aim - from, Vector3.up));
            var prev = RenderTexture.active;
            try
            {
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
            }
            finally { RenderTexture.active = prev; }
            File.WriteAllBytes(file, tex.EncodeToJPG(88));
            return $"wrote {file} (pitch {pitch}, yaw {yaw}, {d:F1} m)\n";
        }
    }
}
