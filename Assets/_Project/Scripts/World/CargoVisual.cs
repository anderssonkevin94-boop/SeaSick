using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// Builds the little meshes that represent a unit of cargo — a log, a
    /// block of stone, an ore chunk, a spice bundle — and stacks them.
    /// Used both for the ship's hold and the stockpile at home, so a resource
    /// looks the same wherever it is.
    public static class CargoVisual
    {
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        static Material Mat(string key, Color c)
        {
            if (materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.15f);
            materials[key] = m;
            return m;
        }

        public static GameObject Build(string resource, Transform parent)
        {
            var root = new GameObject($"Cargo_{resource}");
            root.transform.SetParent(parent, false);

            switch (resource)
            {
                case "Stone":
                {
                    var b = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(1.1f, 0.75f, 1.1f), Mat("stone", new Color(0.62f, 0.63f, 0.66f)));
                    b.transform.localRotation = Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f);
                    break;
                }
                case "Ore":
                {
                    Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.95f, 0.7f, 0.95f), Mat("ore", new Color(0.30f, 0.29f, 0.33f)));
                    var vein = Prim(PrimitiveType.Sphere, root.transform,
                        new Vector3(0.55f, 0.3f, 0.55f), Mat("orevein", new Color(1f, 0.82f, 0.28f)));
                    vein.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                    break;
                }
                case "Spice":
                {
                    var sack = Prim(PrimitiveType.Sphere, root.transform,
                        new Vector3(0.9f, 0.75f, 0.9f), Mat("spice", new Color(0.82f, 0.62f, 0.32f)));
                    sack.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                    var tie = Prim(PrimitiveType.Sphere, root.transform,
                        new Vector3(0.4f, 0.25f, 0.4f), Mat("spicetie", new Color(0.92f, 0.36f, 0.62f)));
                    tie.transform.localPosition = new Vector3(0f, 0.78f, 0f);
                    break;
                }
                default: // Timber — a log lying across the deck
                {
                    var log = Prim(PrimitiveType.Cylinder, root.transform,
                        new Vector3(0.42f, 1.5f, 0.42f), Mat("log", new Color(0.45f, 0.30f, 0.18f)));
                    log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    log.transform.localPosition = new Vector3(0f, 0.42f, 0f);
                    break;
                }
            }
            return root;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// Where the nth item sits in a stack: rows across, then layers up.
        public static Vector3 StackSlot(int index, int perRow, float spacing, float layerHeight)
        {
            int layer = index / (perRow * perRow);
            int inLayer = index % (perRow * perRow);
            int row = inLayer / perRow;
            int col = inLayer % perRow;
            return new Vector3(
                (col - (perRow - 1) * 0.5f) * spacing,
                layer * layerHeight,
                (row - (perRow - 1) * 0.5f) * spacing);
        }
    }
}
