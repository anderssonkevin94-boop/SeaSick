using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// Builds the little meshes that represent a unit of cargo — a log, a
    /// block of stone, an ore chunk, a spice bundle — and stacks them.
    /// Used both for the ship's hold and the stockpile at home, so a resource
    /// looks the same wherever it is.
    ///
    /// **Four of the eight resources were logs (2026-09-20).** The switch
    /// below had cases for Stone, Ore and Spice and a `default` that built a
    /// log, which was right while timber was the only thing an island made.
    /// It stopped being right the day a camp could turn timber into boards,
    /// ore into tools and a field into food: a hold full of tools looked
    /// exactly like a hold full of firewood, and the whole point of the stack
    /// at the stern is that *you can tell at a glance what you're carrying*.
    /// Boards, Tools, Food and Meals have their own shapes now, coloured from
    /// `Res.Colour` so a pile beside the fire and a crate on the deck are the
    /// same colour — `CampPiles` draws from the same table. Timber, Stone, Ore
    /// and Spice are untouched, because they are already in front of Kevin.
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
                case "Boards":
                {
                    // A flat stack of sawn planks, cross-piled the way the
                    // camp's own timber stack is. Four courses is enough to
                    // read as "cut and stacked" rather than "a log".
                    var pale = Mat("boards", Res.Colour("Boards"));
                    for (int i = 0; i < 4; i++)
                    {
                        var plank = Prim(PrimitiveType.Cube, root.transform,
                            new Vector3(1.25f, 0.11f, 0.72f), pale);
                        plank.transform.localPosition = new Vector3(0f, 0.09f + i * 0.14f, 0f);
                        plank.transform.localRotation =
                            Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
                    }
                    break;
                }
                case "Tools":
                {
                    // A small dark crate with a batten round it — the one
                    // thing in the hold that is worth more than its volume,
                    // so it is the smallest thing in the hold.
                    var body = Mat("tools", Res.Colour("Tools"));
                    var crate = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.82f, 0.68f, 0.82f), body);
                    crate.transform.localPosition = new Vector3(0f, 0.34f, 0f);
                    crate.transform.localRotation =
                        Quaternion.Euler(0f, Random.Range(-10f, 10f), 0f);
                    var band = Prim(PrimitiveType.Cube, crate.transform,
                        new Vector3(1.06f, 0.22f, 1.06f),
                        Mat("toolsband", Shade(Res.Colour("Tools"), 0.55f)));
                    band.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                    break;
                }
                case "Food":
                {
                    // A sack, tied at the neck. Green-brown, because what is
                    // in it came out of a field an hour ago.
                    var cloth = Mat("food", Res.Colour("Food"));
                    var sack = Prim(PrimitiveType.Sphere, root.transform,
                        new Vector3(0.92f, 0.78f, 0.78f), cloth);
                    sack.transform.localPosition = new Vector3(0f, 0.36f, 0f);
                    var neck = Prim(PrimitiveType.Sphere, root.transform,
                        new Vector3(0.34f, 0.30f, 0.30f),
                        Mat("foodtie", Shade(Res.Colour("Food"), 0.65f)));
                    neck.transform.localPosition = new Vector3(0f, 0.74f, 0f);
                    break;
                }
                case "Meals":
                {
                    // A lidded pot. Food is what the island grew; meals are
                    // what somebody cooked, and the lid is the whole
                    // difference in one shape.
                    var ware = Mat("meals", Res.Colour("Meals"));
                    var pot = Prim(PrimitiveType.Cylinder, root.transform,
                        new Vector3(0.80f, 0.28f, 0.80f), ware);
                    pot.transform.localPosition = new Vector3(0f, 0.28f, 0f);
                    var lid = Prim(PrimitiveType.Cylinder, root.transform,
                        new Vector3(0.88f, 0.05f, 0.88f),
                        Mat("mealslid", Shade(Res.Colour("Meals"), 0.7f)));
                    lid.transform.localPosition = new Vector3(0f, 0.60f, 0f);
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

        /// The same colour, darker — for a band, a tie or a lid, so a shape
        /// reads as two parts without a second entry in `Res.Colour`.
        static Color Shade(Color c, float by) =>
            new Color(c.r * by, c.g * by, c.b * by, c.a);

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
