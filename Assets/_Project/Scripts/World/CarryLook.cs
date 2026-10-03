using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What a villager's arms hold, and how it sits in them** (2026-10-01,
    /// Kevin: *"we need to calibrate each item the villagers are holding.
    /// right now it looks ridiculous"* and *"smaller objects like vegetables
    /// or meats ... carry them (up to 8 at a time) in an open lid crate so
    /// you can see what they're carrying"*).
    ///
    /// ONE table (`For`) says, per resource, how a held load is drawn: its
    /// kind, how many units are shown at most, and the scale of one unit.
    /// `Build` lays the load out in the CARRY SOCKET's frame -- origin the
    /// load's bottom centre where the `Carry` clip keeps his fists (x his
    /// right, y up his spine, z out in front), measured off the clip by
    /// `VillagerCarryShot.Measure`: fists at x = +-0.31 m, y = -0.01, the
    /// forearms running back to the elbows at z = -0.21, the neck 0.34 m up
    /// and 0.33 m back. So long things (logs, planks, spears) lie ACROSS the
    /// forearms at their real length and stick out past the fists, a stack
    /// stays forward of his chest, and nothing tall sits at z < -0.1 where
    /// his face is.
    ///
    /// Real-world sizes against his 1.7 m: a log 1.25 m x 0.19 m, a plank
    /// 1.36 x 0.21 x 0.064 m, a stone ~0.25 m, a brick 0.22 x 0.075 x 0.11 m.
    /// The units are the resource / food / tool kit meshes; a resource with
    /// no model gets a plain box in its colour (never invisible).
    ///
    /// Small goods (every crop, fish, meat, forage, spice, flour and every
    /// cooked dish) ride in the open carry crate (`art-staging/carry-crate-v1`,
    /// `Resources/Kits/Carry/CarryCrate`, empties `Slot_0..7`), one item per
    /// slot, front row first, up to `CrateSlots`. If the crate FBX is
    /// missing a stand-in box with the same slots is built.
    ///
    /// Pure show: nothing here touches a ledger. The REAL count is the
    /// ledger's; this only draws min(count, shown).
    public static class CarryLook
    {
        public enum Kind { Logs, Planks, Rocks, Bricks, Bars, Hides, Poles, Tools, Haunch, Crate }

        public struct Row
        {
            public Kind kind;
            /// Most units drawn (the load is the picture of "several").
            public int shown;
            /// Scale of one unit mesh (kit meshes are authored at pile size).
            public float scale;
        }

        /// Slots in the crate.
        public const int CrateSlots = 8;

        /// **The carry table.** One row per resource; everything not named
        /// is a small good and goes in the crate.
        public static Row For(string res)
        {
            switch (res)
            {
                case Res.Timber: return new Row { kind = Kind.Logs, shown = 3, scale = 0.78f };
                case Res.Boards: return new Row { kind = Kind.Planks, shown = 5, scale = 0.85f };
                case Res.FineBoards: return new Row { kind = Kind.Planks, shown = 5, scale = 0.80f };
                case Res.Stone:
                case Res.Ore: return new Row { kind = Kind.Rocks, shown = 4, scale = 0.52f };
                case Res.Brick: return new Row { kind = Kind.Bricks, shown = 6, scale = 0.68f };
                case Res.Iron: return new Row { kind = Kind.Bars, shown = 6, scale = 1f };
                case Res.Hide: return new Row { kind = Kind.Hides, shown = 4, scale = 1f };
                case Res.Spear:
                case Res.IronSpear:
                case Res.Bow:
                case Res.Arrows: return new Row { kind = Kind.Poles, shown = res == Res.Arrows ? 8 : 3, scale = 1f };
                case Res.Tools:
                case Res.SawBlade: return new Row { kind = Kind.Tools, shown = 3, scale = 1f };
                case Res.Game: return new Row { kind = Kind.Haunch, shown = 2, scale = 1.7f };
                default: return new Row { kind = Kind.Crate, shown = CrateSlots, scale = 1f };
            }
        }

        /// True when `res` rides in the crate.
        public static bool InCrate(string res) => For(res).kind == Kind.Crate;

        /// Units actually drawn for a load of `count`.
        public static int Shown(string res, int count) => Mathf.Clamp(count, 1, Mathf.Max(1, For(res).shown));

        /// Metres a layout is pushed forward of the socket (half its depth,
        /// less a few centimetres where it presses on his shirt).
        static float Forward(Kind k)
        {
            switch (k)
            {
                case Kind.Logs: return 0.17f;
                case Kind.Planks: return 0.12f;
                case Kind.Rocks: return 0.16f;
                case Kind.Bricks: return 0.08f;
                case Kind.Bars: return 0.07f;
                case Kind.Hides: return 0.18f;
                case Kind.Poles: return 0.10f;
                case Kind.Tools: return 0.12f;
                case Kind.Haunch: return 0.21f;
                default: return 0.17f;   // the crate, 0.376 m deep
            }
        }

        /// Lifts every load a little off the socket: the fists' tops sit
        /// ~4 cm above it, and a log bedded into them reads as clipping.
        const float Lift = 0.03f;

        /// **Lay `count` of `res` out under `root`** (the socket frame).
        public static void Build(string res, int count, Transform root)
        {
            var row = For(res);
            int n = Shown(res, count);
            // **Out in front of his belly.** The clip's fists sit at the
            // front of his (chunky) chest, so a load centred on the socket is
            // half inside him (measured: the first sheet's stones and bricks
            // vanished into his chest). Each layout below is centred near
            // z = 0; this pushes it forward until its back edge just meets
            // the fists and the front of his shirt.
            var load = new GameObject("Load").transform;
            load.SetParent(root, false);
            load.localPosition = new Vector3(0f, 0f, Forward(row.kind));
            root = load;
            switch (row.kind)
            {
                case Kind.Logs: Logs(root, n, row.scale); break;
                case Kind.Planks: Planks(root, n, row.scale, res == Res.FineBoards); break;
                case Kind.Rocks: Rocks(root, res, n, row.scale); break;
                case Kind.Bricks: Bricks(root, n, row.scale); break;
                case Kind.Bars: Bars(root, n); break;
                case Kind.Hides: Hides(root, n); break;
                case Kind.Poles: Poles(root, res, n); break;
                case Kind.Tools: Tools(root, res, n); break;
                case Kind.Haunch: Haunch(root, n, row.scale); break;
                default: Crate(root, res, n); break;
            }
        }

        // --- bulky loads, across the forearms --------------------------------

        /// Logs across his arms: two side by side on the forearms, a third
        /// bedded in the groove on top.
        static void Logs(Transform root, int n, float k)
        {
            // Timber_Unit: 1.60 m long, 0.245 thick, bottom origin, long z.
            float d = 0.24f * k;
            var across = Quaternion.Euler(0f, 90f, 0f);
            for (int i = 0; i < n; i++)
            {
                Vector3 at;
                if (n == 1) at = new Vector3(0f, 0f, -0.02f);
                else if (i < 2) at = new Vector3(i == 0 ? 0.03f : -0.03f, 0f, i == 0 ? -0.105f : -0.105f + d);
                else at = new Vector3(0.01f, 0.86f * d, -0.105f + 0.5f * d);
                at.y += Lift;
                var turn = across * Quaternion.Euler(0f, (i % 2 == 0 ? -1f : 1f) * (1.5f + i), 0f);
                if (ResourceKit.Spawn(Res.Timber, false, root, at, turn, k) != null) continue;
                var log = Prim(PrimitiveType.Cylinder, root, new Vector3(d, 0.62f, d), Mat("log", Res.Colour(Res.Timber)));
                log.transform.localPosition = at + new Vector3(0f, 0.5f * d, 0f);
                log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        /// A bundle of planks, flat, across his arms.
        static void Planks(Transform root, int n, float k, bool fine)
        {
            // Boards_Unit: 1.60 x 0.25 x 0.075 m, bottom origin, long z.
            float th = 0.075f * k * (fine ? 0.8f : 1f);
            var across = Quaternion.Euler(0f, 90f, 0f);
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3(((i * 37) % 5 - 2) * 0.012f, Lift + i * th, -0.03f + ((i * 17) % 3 - 1) * 0.008f);
                var turn = across * Quaternion.Euler(0f, (i % 2 == 0 ? 1.5f : -1.5f), 0f);
                var go = ResourceKit.Spawn(Res.Boards, false, root, at, turn, k);
                if (go != null)
                {
                    if (fine)
                    {
                        // Fine boards: the same plank, planed thinner.
                        go.transform.localScale = Vector3.Scale(go.transform.localScale, ThinAlong(go.transform, 0.8f));
                    }
                    continue;
                }
                var plank = Prim(PrimitiveType.Cube, root, new Vector3(1.6f * k, th, 0.25f * k),
                    Mat(fine ? "planks_fine" : "planks", Res.Colour(fine ? Res.FineBoards : Res.Boards)));
                plank.transform.localPosition = at + new Vector3(0f, 0.5f * th, 0f);
                plank.transform.localRotation = Quaternion.Euler(0f, (i % 2 == 0 ? 1.5f : -1.5f), 0f);
            }
        }

        /// A local scale factor that thins a spawned unit along the parent's
        /// up axis, whatever node turn the mesh carries.
        static Vector3 ThinAlong(Transform t, float f)
        {
            Vector3 up = t.parent != null ? t.parent.up : Vector3.up;
            Vector3 l = t.InverseTransformDirection(up);
            l = new Vector3(Mathf.Abs(l.x), Mathf.Abs(l.y), Mathf.Abs(l.z));
            return new Vector3(Mathf.Lerp(1f, f, l.x), Mathf.Lerp(1f, f, l.y), Mathf.Lerp(1f, f, l.z));
        }

        /// Rough stone or ore: two chunks on his arms, more on top.
        static void Rocks(Transform root, string res, int n, float k)
        {
            // Stone_Unit / Ore_Unit: 0.49 wide x 0.31 tall x 0.435 deep, bottom origin.
            float w = 0.49f * k, h = 0.31f * k;
            for (int i = 0; i < n; i++)
            {
                Vector3 at;
                if (n == 1) at = new Vector3(0f, 0f, -0.02f);
                else if (i < 2) at = new Vector3((i == 0 ? 0.5f : -0.5f) * w * 1.02f, 0f, -0.02f + (i == 0 ? 0.015f : -0.015f));
                else if (n == 3) at = new Vector3(0.01f, 0.82f * h, -0.03f);
                else at = new Vector3((i == 2 ? 0.27f : -0.27f) * w, 0.82f * h, -0.035f);
                at.y += Lift;
                var turn = Quaternion.Euler(0f, 37f * i + (i % 2 == 0 ? 0f : 90f), 0f);
                if (ResourceKit.Spawn(res, false, root, at, turn, i >= 2 ? k * 0.9f : k) != null) continue;
                var c = Prim(PrimitiveType.Cube, root, new Vector3(w, h, 0.435f * k), Mat("rock_" + res, Res.Colour(res)));
                c.transform.localPosition = at + new Vector3(0f, 0.5f * h, 0f);
                c.transform.localRotation = turn;
            }
        }

        /// Bricks stacked true: two side by side, layer on layer.
        static void Bricks(Transform root, int n, float k)
        {
            // Brick_Unit: 0.36 x 0.12 tall x 0.18 deep, bottom origin.
            float w = 0.36f * k, h = 0.12f * k;
            for (int i = 0; i < n; i++)
            {
                int layer = i / 2, col = i % 2;
                bool lone = n % 2 == 1 && i == n - 1;
                var at = new Vector3(lone ? 0f : (col - 0.5f) * (w + 0.01f), Lift + layer * h, -0.02f + (layer % 2) * 0.006f);
                var yaw = Quaternion.Euler(0f, (i % 3 - 1) * 2f, 0f);
                if (ResourceKit.Spawn(Res.Brick, false, root, at, yaw, k) != null) continue;
                var b = Prim(PrimitiveType.Cube, root, new Vector3(w, h, 0.18f * k), Mat("bricks", Res.Colour(Res.Brick)));
                b.transform.localPosition = at + new Vector3(0f, 0.5f * h, 0f);
                b.transform.localRotation = yaw;
            }
        }

        /// Iron bars (no model): dark ingots stacked like bricks.
        static void Bars(Transform root, int n)
        {
            var mat = Mat("iron_bar", Res.Colour(Res.Iron));
            const float w = 0.26f, h = 0.065f, d = 0.10f;
            for (int i = 0; i < n; i++)
            {
                int layer = i / 2, col = i % 2;
                bool lone = n % 2 == 1 && i == n - 1;
                var b = Prim(PrimitiveType.Cube, root, new Vector3(w, h, d), mat);
                b.transform.localPosition = new Vector3(lone ? 0f : (col - 0.5f) * (w + 0.015f), Lift + h * (layer + 0.5f), -0.02f);
                b.transform.localRotation = Quaternion.Euler(0f, layer % 2 == 0 ? 0f : 4f, 0f);
            }
        }

        /// Folded hides (no model): a soft flat stack.
        static void Hides(Transform root, int n)
        {
            var mat = Mat("hide", Res.Colour(Res.Hide));
            var mat2 = Mat("hide_dark", Res.Colour(Res.Hide) * 0.82f);
            for (int i = 0; i < n; i++)
            {
                var b = Prim(PrimitiveType.Cube, root, new Vector3(0.56f, 0.055f, 0.36f), i % 2 == 0 ? mat : mat2);
                b.transform.localPosition = new Vector3(((i * 7) % 3 - 1) * 0.02f, Lift + 0.055f * (i + 0.5f), -0.02f);
                b.transform.localRotation = Quaternion.Euler(0f, ((i * 5) % 3 - 1) * 6f, 0f);
            }
        }

        /// Spears, bows, arrows: across his arms at their real length.
        static void Poles(Transform root, string res, int n)
        {
            // Tool frame: +Y up the haft -> lay it along +X (turn -90 about Z).
            var across = Quaternion.Euler(0f, 0f, -90f);
            if (res == Res.Spear || res == Res.IronSpear)
            {
                string kit = res == Res.IronSpear ? ToolKit.SpearIron : ToolKit.SpearStone;
                var mesh = ToolKit.MeshOf(kit);
                float mid = mesh != null ? mesh.bounds.center.y : 0.4f;
                for (int i = 0; i < n; i++)
                {
                    var holder = new GameObject("Spear").transform;
                    holder.SetParent(root, false);
                    holder.localRotation = across;
                    if (!ToolKit.Attach(kit, holder, true))
                    {
                        var shaft = Prim(PrimitiveType.Cube, holder, new Vector3(0.04f, 2.0f, 0.04f), Mat("spear", Res.Colour(res)));
                        shaft.transform.localPosition = new Vector3(0f, mid, 0f);
                    }
                    // The haft's middle over the socket.
                    holder.localPosition = new Vector3((i % 2 == 0 ? -1f : 1f) * 0.06f, Lift + 0.035f + (i / 2) * 0.05f, -0.07f + i * 0.06f)
                                           - holder.localRotation * new Vector3(0f, mid, 0f);
                }
                return;
            }
            if (res == Res.Bow)
            {
                var wood = Mat("bow", Res.Colour(Res.Bow));
                var cord = Mat("bowstring", new Color(0.86f, 0.82f, 0.72f));
                for (int i = 0; i < n; i++)
                {
                    float z = -0.06f + i * 0.09f, y = Lift + 0.03f + i * 0.02f;
                    // A flat bow: two limbs bent back from the grip, and the string.
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var limb = Prim(PrimitiveType.Cube, root, new Vector3(0.62f, 0.035f, 0.04f), wood);
                        limb.transform.localPosition = new Vector3(s * 0.30f, y, z + 0.035f);
                        limb.transform.localRotation = Quaternion.Euler(0f, s * 7f, 0f);
                    }
                    var str = Prim(PrimitiveType.Cube, root, new Vector3(1.18f, 0.012f, 0.012f), cord);
                    str.transform.localPosition = new Vector3(0f, y, z - 0.005f);
                }
                return;
            }
            // Arrows: a tied bundle, shafts across, fletching to his right.
            var shaftMat = Mat("arrow", Res.Colour(Res.Arrows));
            var flight = Mat("arrow_flight", new Color(0.92f, 0.90f, 0.86f));
            var tie = Mat("arrow_tie", new Color(0.45f, 0.32f, 0.20f));
            for (int i = 0; i < n; i++)
            {
                float y = Lift + 0.02f + (i / 4) * 0.025f, z = -0.06f + (i % 4) * 0.025f;
                var a = Prim(PrimitiveType.Cube, root, new Vector3(0.75f, 0.018f, 0.018f), shaftMat);
                a.transform.localPosition = new Vector3(0f, y, z);
                var f = Prim(PrimitiveType.Cube, root, new Vector3(0.12f, 0.03f, 0.008f), flight);
                f.transform.localPosition = new Vector3(0.31f, y, z);
            }
            var band = Prim(PrimitiveType.Cube, root, new Vector3(0.03f, 0.075f, 0.11f), tie);
            band.transform.localPosition = new Vector3(0f, Lift + 0.035f, -0.025f);
        }

        /// Tools lying flat across his arms (axe, hammer, saw...).
        static void Tools(Transform root, string res, int n)
        {
            string[] kits = res == Res.SawBlade
                ? new[] { ToolKit.Saw, ToolKit.Saw, ToolKit.Saw }
                : new[] { ToolKit.Axe, ToolKit.Hammer, ToolKit.Saw };
            var across = Quaternion.Euler(0f, 0f, -90f);
            for (int i = 0; i < n; i++)
            {
                string kit = kits[i % kits.Length];
                var mesh = ToolKit.MeshOf(kit);
                float mid = mesh != null ? mesh.bounds.center.y : 0.3f;
                var holder = new GameObject("Tool_" + kit).transform;
                holder.SetParent(root, false);
                holder.localRotation = across * Quaternion.Euler(0f, i % 2 == 0 ? 0f : 180f, 0f);
                holder.localPosition = new Vector3(0f, Lift + 0.04f + i * 0.045f, -0.08f + i * 0.08f)
                                       - holder.localRotation * new Vector3(0f, mid, 0f);
                if (!ToolKit.Attach(kit, holder, true))
                {
                    var b = Prim(PrimitiveType.Cube, holder, new Vector3(0.04f, 0.6f, 0.04f), Mat("tool_haft", new Color(0.44f, 0.31f, 0.19f)));
                    b.transform.localPosition = new Vector3(0f, mid, 0f);
                }
            }
        }

        /// A hunted animal, carried as big joints of meat across his arms.
        static void Haunch(Transform root, int n, float k)
        {
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3((i - 0.5f * (n - 1)) * 0.06f, Lift + i * 0.1f, -0.02f);
                var yaw = Quaternion.Euler(0f, 90f + (i % 2 == 0 ? -8f : 10f), 0f);
                if (ResourceKit.SpawnFood(Res.Meat, root, at, yaw, k) != null) continue;
                var b = Prim(PrimitiveType.Cube, root, new Vector3(0.45f, 0.12f, 0.34f), Mat("game", Res.Colour(Res.Game)));
                b.transform.localPosition = at + new Vector3(0f, 0.06f, 0f);
            }
        }

        // --- the crate --------------------------------------------------------

        sealed class CrateArt
        {
            public Mesh mesh;
            public Material mat;
            public Matrix4x4 node;           // the mesh node in the crate root's frame
            public Vector3[] slots = new Vector3[CrateSlots];
        }

        static CrateArt crate;
        static bool crateTried;

        /// The README's slots (stand-in, and the check the FBX is read
        /// against): 2 rows x 4, front row (z +) first, base on the floor.
        static Vector3 StandInSlot(int i) =>
            new Vector3(new[] { -0.161f, -0.054f, 0.054f, 0.161f }[i % 4], 0.06f, i < 4 ? 0.0725f : -0.0725f);

        static CrateArt LoadCrate()
        {
            if (crateTried) return crate;
            crateTried = true;
            var prefab = Resources.Load<GameObject>("Kits/Carry/CarryCrate");
            var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogWarning("[CarryLook] no crate at Resources/Kits/Carry/CarryCrate: drawing the stand-in box");
                return null;
            }
            var art = new CrateArt
            {
                mesh = mf.sharedMesh,
                mat = mf.GetComponent<MeshRenderer>() != null ? mf.GetComponent<MeshRenderer>().sharedMaterial : null,
                node = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix,
            };
            var all = prefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < CrateSlots; i++)
            {
                art.slots[i] = StandInSlot(i);
                foreach (var t in all)
                    if (t.name == "Slot_" + i) { art.slots[i] = prefab.transform.InverseTransformPoint(t.position); break; }
            }
            // **Front row out front.** The FBX imports turned half round
            // (its `Slot_0` lands at z -0.07, x +0.16: the README's axis
            // mapping guessed the other way). The crate is symmetric, so
            // turn the whole thing 180 degrees about up: slots 0-3 then fill
            // the row away from his belly first, as authored.
            if (art.slots[0].z < 0f)
            {
                var turn = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));
                art.node = turn * art.node;
                for (int i = 0; i < CrateSlots; i++) art.slots[i] = turn.MultiplyPoint3x4(art.slots[i]);
            }
            crate = art;
            return crate;
        }

        /// **A runner's barrow load** (2026-10-03, the Storehouse's bigger
        /// barrows: small goods 16 / 20 / 24): `Build`, except that crate
        /// goods are drawn WHOLE, up to three layers of the crate's 8 slots
        /// -- each layer above pulled in toward the crate's middle and set a
        /// half slot over so it nests, a heap rather than a column. The arm
        /// carry keeps `Build` (one layer, 8).
        public static void BuildHeaped(string res, int count, Transform root)
        {
            var row = For(res);
            if (row.kind != Kind.Crate || count <= CrateSlots) { Build(res, count, root); return; }
            var load = new GameObject("Load").transform;
            load.SetParent(root, false);
            load.localPosition = new Vector3(0f, 0f, Forward(row.kind));
            Crate(load, res, Mathf.Min(count, CrateSlots * HeapLayers), HeapLayers);
        }

        /// Layers a heaped crate holds (`BuildHeaped`), and the rise and pull
        /// of each layer over the one below (metres; share of the slot's
        /// distance from the crate's middle).
        const int HeapLayers = 3;
        const float HeapRise = 0.07f, HeapPull = 0.22f;

        /// The open crate on the socket, `n` of `res` in its slots.
        static void Crate(Transform root, string res, int n, int layers = 1)
        {
            var art = LoadCrate();
            var box = new GameObject("Crate").transform;
            box.SetParent(root, false);
            box.localPosition = new Vector3(0f, 0.005f, 0f);
            if (art != null)
            {
                var go = new GameObject("CarryCrate");
                go.transform.SetParent(box, false);
                go.transform.localPosition = art.node.GetColumn(3);
                go.transform.localRotation = art.node.rotation;
                go.transform.localScale = art.node.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = art.mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = art.mat != null ? art.mat : Mat("crate", new Color(0.55f, 0.40f, 0.25f));
            }
            else StandInCrate(box);

            for (int i = 0; i < n && i < CrateSlots * layers; i++)
            {
                int layer = i / CrateSlots, j = i % CrateSlots;
                Vector3 slot = art != null ? art.slots[j] : StandInSlot(j);
                if (layer > 0)
                {
                    // Nested on the layer below: pulled toward the middle,
                    // nudged a quarter slot along, raised one item.
                    float pull = 1f - HeapPull * layer;
                    slot = new Vector3(slot.x * pull + (layer % 2 == 1 ? 0.027f : -0.027f), slot.y + HeapRise * layer, slot.z * pull);
                }
                Item(box, res, i, slot);
            }
        }

        /// The stand-in crate (no FBX): floor, two end walls, two low long
        /// walls, the README's 0.50 x 0.36 m with 0.22 m ends.
        static void StandInCrate(Transform box)
        {
            var wood = Mat("crate", new Color(0.55f, 0.40f, 0.25f));
            var dark = Mat("crate_dark", new Color(0.40f, 0.28f, 0.17f));
            Box(box, dark, new Vector3(0.05f, 0.03f, 0.36f), new Vector3(-0.17f, 0.015f, 0f));
            Box(box, dark, new Vector3(0.05f, 0.03f, 0.36f), new Vector3(0.17f, 0.015f, 0f));
            Box(box, wood, new Vector3(0.50f, 0.03f, 0.36f), new Vector3(0f, 0.045f, 0f));
            Box(box, wood, new Vector3(0.03f, 0.19f, 0.36f), new Vector3(-0.235f, 0.125f, 0f));
            Box(box, wood, new Vector3(0.03f, 0.19f, 0.36f), new Vector3(0.235f, 0.125f, 0f));
            Box(box, wood, new Vector3(0.44f, 0.075f, 0.03f), new Vector3(0f, 0.0975f, 0.165f));
            Box(box, wood, new Vector3(0.44f, 0.075f, 0.03f), new Vector3(0f, 0.0975f, -0.165f));
        }

        /// **One small good on its slot** (`base` = the item's base point).
        /// Scales per the crate README (the food kit units are pile-sized):
        /// round things sit, carrots and wheat stand up out of the crate so
        /// they read from the camera, fish lie along the depth.
        static void Item(Transform box, string res, int i, Vector3 at)
        {
            float jitter = ((i * 53) % 7 - 3) * 0.004f;
            at += new Vector3(jitter, 0f, -jitter);
            var yaw = Quaternion.Euler(0f, 10f + (i * 47) % 70, 0f);
            switch (res)
            {
                case Res.Potato: Food(box, res, at, yaw, 0.52f); return;
                case Res.Onion: Food(box, res, at, yaw, 0.50f); return;
                case Res.Apple: Food(box, res, at, yaw, 0.54f); return;
                case Res.Food: Food(box, i % 2 == 0 ? Res.Apple : Res.Onion, at, yaw, 0.48f); return;
                case Res.Carrot:
                case Res.Wheat:
                    Food(box, res, at, Quaternion.Euler((i % 2 == 0 ? 7f : -6f), (i * 47) % 70, (i % 3 - 1) * 6f),
                        res == Res.Carrot ? 0.55f : 0.50f);
                    return;
                case Res.Fish:
                    Food(box, res, at + new Vector3(0f, 0f, 0f), Quaternion.Euler(0f, 90f + ((i * 13) % 5 - 2) * 5f, 0f), 0.42f);
                    return;
                case Res.Meat: Food(box, res, at + new Vector3(0f, (i % 2) * 0.02f, 0f), yaw, 0.56f); return;
                case Res.Spice: Sack(box, at, Res.Colour(Res.Spice), 0.10f); return;
                case Res.Flour: Sack(box, at, Res.Colour(Res.Flour), 0.12f); return;
            }
            if (Economy.FoodBook.IsDish(res))
            {
                var d = new GameObject("Dish_" + res).transform;
                d.SetParent(box, false);
                d.localPosition = at;
                d.localRotation = yaw;
                Dish(res, d, 0.12f);
                return;
            }
            Sack(box, at, Res.Colour(res), 0.095f);
        }

        static void Food(Transform box, string res, Vector3 at, Quaternion rot, float k)
        {
            if (ResourceKit.SpawnFood(res, box, at, rot, k) != null) return;
            Sack(box, at, Res.Colour(res), 0.09f);
        }

        /// A little tied bag, `s` metres across.
        static void Sack(Transform box, Vector3 at, Color c, float s)
        {
            var mat = Mat("bag_" + ColorUtility.ToHtmlStringRGB(c), c);
            var b = Prim(PrimitiveType.Cube, box, new Vector3(s, s * 0.9f, s * 1.1f), mat);
            b.transform.localPosition = at + new Vector3(0f, s * 0.45f, 0f);
            b.transform.localRotation = Quaternion.Euler(0f, 20f, 6f);
            var neck = Prim(PrimitiveType.Cube, box, new Vector3(s * 0.4f, s * 0.25f, s * 0.4f), mat);
            neck.transform.localPosition = at + new Vector3(0f, s * 1.0f, 0f);
            neck.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
        }

        // --- a dish ---------------------------------------------------------

        /// **One serving of a cooked dish, `d` metres across**, under
        /// `parent` with its base at the origin: a wooden bowl of the dish's
        /// colour with what it is made of on top; bread is a loaf, a pie a
        /// pie, ship's biscuit a pair of biscuits. Used in the crate and in
        /// a hungry hand's fist. The food (not the bowl) is under a child
        /// named "Food" so an eater can shrink it as he eats.
        public static void Dish(string res, Transform parent, float d)
        {
            var food = new GameObject("Food").transform;
            food.SetParent(parent, false);
            if (res == Res.Bread)
            {
                var loaf = Prim(PrimitiveType.Cube, food, new Vector3(d * 1.1f, d * 0.55f, d * 0.7f), Mat("bread", Res.Colour(Res.Bread)));
                loaf.transform.localPosition = new Vector3(0f, d * 0.275f, 0f);
                var top = Prim(PrimitiveType.Cube, food, new Vector3(d * 0.95f, d * 0.12f, d * 0.55f), Mat("bread_top", Res.Colour(Res.Bread) * 0.8f));
                top.transform.localPosition = new Vector3(0f, d * 0.58f, 0f);
                return;
            }
            if (res == Res.Meals)
            {
                for (int i = 0; i < 2; i++)
                {
                    var b = Prim(PrimitiveType.Cube, food, new Vector3(d * 0.8f, d * 0.22f, d * 0.8f), Mat("biscuit", Res.Colour(Res.Meals)));
                    b.transform.localPosition = new Vector3(0f, d * (0.11f + 0.23f * i), 0f);
                    b.transform.localRotation = Quaternion.Euler(0f, 25f * i, 0f);
                }
                return;
            }
            bool plate = res == Res.GrilledFish || res == Res.GrilledMeat || res == Res.FishPie;
            float h = plate ? d * 0.16f : d * 0.42f;
            var bowl = Prim(PrimitiveType.Cylinder, parent, new Vector3(d, h * 0.5f, d), Mat("bowl", new Color(0.82f, 0.70f, 0.50f)));
            bowl.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            if (res == Res.FishPie)
            {
                var crust = Prim(PrimitiveType.Cylinder, food, new Vector3(d * 0.86f, d * 0.08f, d * 0.86f), Mat("pie", Res.Colour(Res.FishPie)));
                crust.transform.localPosition = new Vector3(0f, h + d * 0.08f, 0f);
                return;
            }
            // A raw meal (a potato, an apple, forage) sits in the bowl as
            // itself: no stew under it.
            bool raw = !Economy.FoodBook.IsDish(res);
            if (!plate && !raw)
            {
                var stew = Prim(PrimitiveType.Cylinder, food, new Vector3(d * 0.84f, d * 0.03f, d * 0.84f), Mat("dish_" + res, Res.Colour(res)));
                stew.transform.localPosition = new Vector3(0f, h * 0.96f, 0f);
            }
            string topping = res == Res.BakedPotato ? Res.Potato
                : res == Res.GrilledFish ? Res.Fish
                : res == Res.GrilledMeat || res == Res.HuntersStew ? Res.Meat
                : res == Res.VegStew ? Res.Carrot
                : raw ? (res == Res.Food ? Res.Apple : res)
                : null;
            if (topping == null) return;
            var size = ResourceKit.FoodSize(topping);
            float foot = Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
            float k = (res == Res.GrilledFish || res == Res.Fish ? 0.95f : res == Res.VegStew || res == Res.HuntersStew ? 0.45f : 0.7f) * d / foot;
            var rot = topping == Res.Carrot || topping == Res.Wheat ? Quaternion.Euler(0f, 30f, 80f) : Quaternion.Euler(0f, 25f, 0f);
            var at = new Vector3(0f, h * (plate ? 1f : 0.9f), 0f);
            if (topping == Res.Carrot || topping == Res.Wheat) { k = (raw ? 0.8f : 0.45f) * d / Mathf.Max(0.01f, size.y); at += new Vector3(0f, 0.02f, 0f); }
            ResourceKit.SpawnFood(topping, food, at, rot, k);
        }

        // --- helpers --------------------------------------------------------

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// One material per colour, shared by every villager.
        static Material Mat(string key, Color c)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(WorldArtStyle.Instance != null
                ? "SeaSick/Environment Toon" : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            mats[key] = m;
            return m;
        }

        static GameObject Box(Transform parent, Material mat, Vector3 size, Vector3 at)
        {
            var go = Prim(PrimitiveType.Cube, parent, size, mat);
            go.transform.localPosition = at;
            return go;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }
    }
}
