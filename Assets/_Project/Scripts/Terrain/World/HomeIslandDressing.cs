using System;
using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// Loads the authored home-island dressing exported beside the flora kit.
    /// Positions in the contract are local to the authored island (x,z) and
    /// heights are absolute above sea level.
    public static class HomeIslandDressing
    {
        [Serializable] class File { public Instance[] instances; }
        [Serializable] class Instance { public string assetId, kind; public Vec3 position, scale; public float yaw; }
        [Serializable] class Vec3 { public float x, y, z; }

        public static GameObject Build(Transform parent, Vector3 centre, float radius,
            SeaSick.World.Island isle, TerrainSettings terrain,
            System.Func<float, float, bool> keepOut)
        {
            var asset = Resources.Load<TextAsset>("Flora/HomeIslandDressing");
            if (asset == null) return null;
            var data = JsonUtility.FromJson<File>(asset.text);
            if (data == null || data.instances == null) return null;
            var root = new GameObject("Scenery"); root.transform.SetParent(parent, true);
            root.transform.position = new Vector3(terrain.homeIsleCentre.x, terrain.seaLevel, terrain.homeIsleCentre.y);
            root.transform.rotation = Quaternion.Euler(0f, terrain.homeIsleCoveBearing + 180f, 0f);
            var cells = new List<SceneryWood.Cell>(); var trees = new List<SceneryWood.Tree>();
            var owner = root.AddComponent<HomeIslandDressingMeshes>();
            var clones = new Dictionary<Mesh, Mesh>();
            float s = terrain.homeIsleRadius / 110f;
            foreach (var d in data.instances)
            {
                if (d == null || string.IsNullOrEmpty(d.assetId) || d.position == null) continue;
                var prefab = Resources.Load<GameObject>("IslandAssets/" + d.assetId);
                if (prefab == null) { Debug.LogWarning("HomeIslandDressing: missing IslandAssets/" + d.assetId); continue; }
                float x = d.position.x * s, z = d.position.z * s;
                Vector3 world = root.transform.TransformPoint(new Vector3(x, d.position.y, z));
                if (keepOut != null && (keepOut(world.x, world.z)
                    || keepOut(world.x + 2f, world.z) || keepOut(world.x - 2f, world.z)
                    || keepOut(world.x, world.z + 2f) || keepOut(world.x, world.z - 2f))) continue;
                var go = UnityEngine.Object.Instantiate(prefab, root.transform);
                go.transform.localPosition = new Vector3(x, d.position.y, z);
                go.transform.localRotation = Quaternion.Euler(0f, d.yaw, 0f);
                var sc = d.scale == null ? Vector3.one : new Vector3(d.scale.x, d.scale.y, d.scale.z);
                go.transform.localScale = new Vector3(sc.x * s, sc.y, sc.z * s);
                var rs = go.GetComponentsInChildren<MeshRenderer>(true);
                if (IsMeadowAsset(d.assetId))
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null) mf.sharedMesh = MeadowMesh(mf.sharedMesh, clones, owner);
                MeshRenderer hi = null, lo = null;
                foreach (var r in rs) { if (r.name.EndsWith("_LOD1")) lo = r; else if (hi == null) hi = r; }
                var lod = go.GetComponent<LODGroup>(); if (lod != null) lod.enabled = false;
                if (lo != null) lo.enabled = false;
                bool tree = string.Equals(d.kind, "tree", StringComparison.OrdinalIgnoreCase);
                int cell = cells.Count;
                cells.Add(new SceneryWood.Cell { r0 = hi, r1 = lo, centre = go.transform.position, radius = 6f * s });
                if (tree) trees.Add(new SceneryWood.Tree { baseAt = go.transform.position, cell = cell, instance = go });
            }
            root.AddComponent<SceneryWood>().Configure(cells, trees, isle);
            root.AddComponent<SceneryLod>().Configure(cells, terrain);
            return root;
        }

        static bool IsMeadowAsset(string id)
            => id == "Grass" || id == "Grass_B" || id == "Fern" || id == "Fern_B";

        static Mesh MeadowMesh(Mesh source, Dictionary<Mesh, Mesh> clones, HomeIslandDressingMeshes owner)
        {
            if (clones.TryGetValue(source, out var existing)) return existing;
            var copy = UnityEngine.Object.Instantiate(source); copy.name = source.name + "_HomeMeadow";
            var colors = copy.colors;
            var meadow = new Color(.24f, .38f, .065f, 1f);
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color(colors[i].r * .35f + meadow.r * .65f,
                    colors[i].g * .35f + meadow.g * .65f,
                    colors[i].b * .35f + meadow.b * .65f, colors[i].a);
            copy.colors = colors;
            clones[source] = copy; owner.Own(copy);
            return copy;
        }
    }
}
