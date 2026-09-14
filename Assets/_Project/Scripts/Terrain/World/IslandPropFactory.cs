using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Resource props (trees, boulders, ore, spice) and beacons, as primitives.
    /// Ported from the old ArchipelagoGenerator unchanged: props are a FIXED
    /// real-world size — a tree is a tree, and that's what tells the eye how
    /// big the land is.
    public static class IslandPropFactory
    {
        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

        public static GameObject Make(string kind)
        {
            if (SeaSick.World.WorldArtStyle.SceneryResource == "Flora/storybook_flora")
            {
                string id = kind == "Timber" ? "Broad" : kind == "Stone" ? "Boulder_" + Random.Range(0,4) : kind == "Ore" ? "Ore" : "Scrub_0";
                var prefab = Resources.Load<GameObject>("IslandAssets/" + id);
                if (prefab != null) return Object.Instantiate(prefab);
            }
            switch (kind)
            {
                // The Blender kit where it exists, the primitives where it
                // does not: a harvest node has to look like the scenery it
                // stands among, or the one tree the crew can cut is the one
                // tree that looks like a lollipop.
                case "Timber": return FromKit("Tree", "Spruce", 11f / 13f) ?? MakeTree();
                case "Stone": return FromKit("Boulder", "Boulder_" + Random.Range(0, 4), 1.3f) ?? MakeBoulder();
                case "Ore": return FromKit("OreRock", "Ore", 1f) ?? MakeOreRock();
                default: return MakeSpiceBush();
            }
        }

        /// One kit template as a prop of its own: a root (the caller scales
        /// and yaws it, the node shakes it) with the mesh under it, drawn by
        /// the scenery material so it is lit like everything round it.
        static GameObject FromKit(string name, string template, float scale)
        {
            var mesh = SceneryKit.MeshOf(template);
            if (mesh == null) return null;
            var root = new GameObject(name);
            var go = new GameObject(template);
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = IslandScenery.SceneryMaterial();
            return root;
        }

        public static GameObject MakeBeacon(Color colour, float height, float emission)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            Object.Destroy(beacon.GetComponent<Collider>());
            beacon.transform.localScale = new Vector3(1.5f, height, 1.5f);
            var bm = MakeMat(colour);
            bm.EnableKeyword("_EMISSION");
            bm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            bm.SetColor("_EmissionColor", colour * emission);
            beacon.GetComponent<MeshRenderer>().sharedMaterial = bm;
            return beacon;
        }

        static GameObject MakeTree()
        {
            var root = new GameObject("Tree");
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localScale = new Vector3(0.55f, 2.6f, 0.55f);
            trunk.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = Mat("trunk", new Color(0.36f, 0.25f, 0.15f));
            for (int i = 0; i < 2; i++)
            {
                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(canopy.GetComponent<Collider>());
                canopy.transform.SetParent(root.transform, false);
                float t = i * 0.5f;
                canopy.transform.localScale = Vector3.one * Mathf.Lerp(5.2f, 3.4f, t);
                canopy.transform.localPosition = new Vector3(0f, Mathf.Lerp(6.2f, 8.4f, t), 0f);
                canopy.GetComponent<MeshRenderer>().sharedMaterial = Mat("leaf", new Color(0.18f, 0.44f, 0.20f));
            }
            return root;
        }

        static GameObject MakeBoulder()
        {
            var root = new GameObject("Boulder");
            for (int i = 0; i < 2; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(rock.GetComponent<Collider>());
                rock.transform.SetParent(root.transform, false);
                rock.transform.localScale = new Vector3(Random.Range(2.6f, 4.4f), Random.Range(2.2f, 3.6f), Random.Range(2.6f, 4.4f));
                rock.transform.localPosition = new Vector3(Random.Range(-1.4f, 1.4f), 1.3f + i * 1.4f, Random.Range(-1.4f, 1.4f));
                rock.transform.localRotation = Quaternion.Euler(Random.Range(-14f, 14f), Random.Range(0f, 360f), Random.Range(-14f, 14f));
                rock.GetComponent<MeshRenderer>().sharedMaterial = Mat("stone", new Color(0.62f, 0.63f, 0.66f));
            }
            return root;
        }

        static GameObject MakeOreRock()
        {
            var root = new GameObject("OreRock");
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(rock.GetComponent<Collider>());
            rock.transform.SetParent(root.transform, false);
            rock.transform.localScale = new Vector3(3.6f, 3.2f, 3.6f);
            rock.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            rock.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
            rock.GetComponent<MeshRenderer>().sharedMaterial = Mat("darkrock", new Color(0.30f, 0.29f, 0.33f));
            var vein = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(vein.GetComponent<Collider>());
            vein.transform.SetParent(root.transform, false);
            vein.transform.localScale = new Vector3(2.1f, 1.1f, 2.1f);
            vein.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            var vm = Mat("orevein", new Color(1f, 0.82f, 0.28f));
            vm.EnableKeyword("_EMISSION");
            vm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            vm.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.2f) * 1.6f);
            vein.GetComponent<MeshRenderer>().sharedMaterial = vm;
            return root;
        }

        static GameObject MakeSpiceBush()
        {
            var root = new GameObject("SpiceBush");
            var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(bush.GetComponent<Collider>());
            bush.transform.SetParent(root.transform, false);
            bush.transform.localScale = new Vector3(3.6f, 2.4f, 3.6f);
            bush.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            bush.GetComponent<MeshRenderer>().sharedMaterial = Mat("spiceleaf", new Color(0.30f, 0.50f, 0.28f));
            for (int i = 0; i < 3; i++)
            {
                var flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(flower.GetComponent<Collider>());
                flower.transform.SetParent(root.transform, false);
                flower.transform.localScale = Vector3.one * 1.15f;
                float a = i * 2.1f;
                flower.transform.localPosition = new Vector3(Mathf.Sin(a) * 1.3f, 2.3f, Mathf.Cos(a) * 1.3f);
                flower.GetComponent<MeshRenderer>().sharedMaterial = Mat("spiceflower", new Color(0.92f, 0.34f, 0.62f));
            }
            return root;
        }

        public static Material Mat(string key, Color c)
        {
            if (matCache.TryGetValue(key, out var m) && m != null) return m;
            m = MakeMat(c);
            matCache[key] = m;
            return m;
        }

        public static Material MakeMat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.12f);
            return m;
        }
    }
}
