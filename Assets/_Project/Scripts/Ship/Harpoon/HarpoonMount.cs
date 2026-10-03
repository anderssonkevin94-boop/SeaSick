using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **The bow fitting and the barb, art or stand-in** (PLAN-harpoon §5:
    /// a fixed bow fitting outside the slot grid, on every hull from the
    /// start). Loads `Resources/Harpoon/HarpoonMount` and `HarpoonBarb` when
    /// the art has landed; until then builds primitives carrying the SAME
    /// child names, so `HarpoonGun` never knows which it got:
    ///
    /// - mount: `Swivel` (yaw pivot; everything that turns is under it),
    ///   `Barb_Muzzle` (+forward, where the barb leaves and the line starts),
    ///   `Winch_Drum` (spins on local X while reeling), `Harpooner_Stand`.
    /// - barb: `Line_Attach`.
    public static class HarpoonMount
    {
        public const string MountResource = "Harpoon/HarpoonMount";
        public const string BarbResource = "Harpoon/HarpoonBarb";

        public static GameObject BuildMount(Transform parent)
        {
            GameObject go;
            var prefab = Resources.Load<GameObject>(MountResource);
            if (prefab != null)
            {
                go = Object.Instantiate(prefab, parent, false);
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
            }
            else go = PlaceholderMount(parent);
            go.name = "HarpoonMount";
            return go;
        }

        public static GameObject BuildBarb()
        {
            GameObject go;
            var prefab = Resources.Load<GameObject>(BarbResource);
            if (prefab != null)
            {
                go = Object.Instantiate(prefab);
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
            }
            else go = PlaceholderBarb();
            go.name = "HarpoonBarb";
            return go;
        }

        /// Finds a child by name at any depth, or null.
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = Find(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// **Where the fitting's base sits on the bow module**, in its
        /// authoring units, module-local from the bow module's aft socket
        /// (its origin): on the centreline, 6.3 forward, flush on the foredeck
        /// at 2.11 up -- the placement the mount art was exported against
        /// (3.15 m forward at the coaster's 0.5 m per unit).
        static readonly Vector3 BowModuleMountU = new Vector3(6.3f, 0f, 2.11f);

        /// The modular hull's view, if she wears one (a refitted hull does).
        public static SeaSick.Ship.Modular.ModularShipView ModularView(Transform ship) =>
            ship.GetComponentInChildren<SeaSick.Ship.Modular.ModularShipView>();

        /// **The bow stem in ship-local space**: where the fitting stands,
        /// on the centreline at deck height. A modular hull places it on its
        /// bow module at `BowModuleMountU` (the art's own spot). The stock
        /// steamer carries a `HullFormData` (rebound on each refit): its
        /// forward-most station with room on deck. A hull with neither falls
        /// back to its `FleetVisual` deck line, then to the motor's length.
        public static Vector3 BowStem(Transform ship)
        {
            var view = ModularView(ship);
            var bow = view != null && view.Current != null
                ? view.Current.Find(SeaSick.Ship.Modular.ShipAssembler.StdKeyBow) : null;
            if (bow != null)
            {
                Vector3 inView = bow.positionM + bow.rotation
                    * SeaSick.Ship.Modular.ModularScale.AuthoringToGame(BowModuleMountU, view.Current.metresPerUnit);
                return ship.InverseTransformPoint(view.transform.TransformPoint(inView));
            }

            var steamer = ship.GetComponent<SeaSick.Steamer.SteamerShip>();
            var data = steamer != null ? steamer.Data : null;
            if (data != null && data.StationCount > 0)
            {
                int best = -1;
                for (int i = 0; i < data.StationCount; i++)
                {
                    var s = data.stations[i];
                    if (data.HalfBreadthAt(i, s.deckY) < MinDeckHalfBreadth) continue;
                    if (best < 0 || s.z > data.stations[best].z) best = i;
                }
                if (best >= 0)
                {
                    var s = data.stations[best];
                    return new Vector3(0f, s.deckY, s.z);
                }
            }

            var fleet = ship.GetComponentInChildren<FleetVisual>();
            if (fleet != null && fleet.length > 1f)
            {
                float z = fleet.length * 0.5f - StemSetBack;
                return new Vector3(0f, fleet.DeckHeight(z), z);
            }

            var motor = ship.GetComponent<ShipMotor>();
            float len = motor != null ? motor.HullLength : 20f;
            return new Vector3(0f, FallbackDeckY, len * 0.5f - StemSetBack);
        }

        /// **The top of the bow stem cap in ship-local space**, ahead of the
        /// fitting: the rope's fairlead. A modular hull gives its bow module's
        /// forward-most, highest point on the centreline (module bounds, the
        /// same module and units `BowModuleMountU` is measured in); the stock
        /// steamer gives its forward-most station at the deck line. False when
        /// the hull has neither, and the gun falls back to a point ahead of
        /// the muzzle.
        public static bool StemTop(Transform ship, out Vector3 local)
        {
            var view = ModularView(ship);
            var bow = view != null && view.Current != null
                ? view.Current.Find(SeaSick.Ship.Modular.ShipAssembler.StdKeyBow) : null;
            if (bow != null && bow.boundsMaxU.x > BowModuleMountU.x)
            {
                var tipU = new Vector3(bow.boundsMaxU.x, 0f, Mathf.Max(bow.boundsMaxU.z, BowModuleMountU.z));
                Vector3 inView = bow.positionM + bow.rotation
                    * SeaSick.Ship.Modular.ModularScale.AuthoringToGame(tipU, view.Current.metresPerUnit);
                local = ship.InverseTransformPoint(view.transform.TransformPoint(inView));
                return true;
            }

            var steamer = ship.GetComponent<SeaSick.Steamer.SteamerShip>();
            var data = steamer != null ? steamer.Data : null;
            if (data != null && data.StationCount > 0)
            {
                int best = 0;
                for (int i = 1; i < data.StationCount; i++)
                    if (data.stations[i].z > data.stations[best].z) best = i;
                var s = data.stations[best];
                local = new Vector3(0f, s.deckY, s.z);
                return true;
            }

            local = Vector3.zero;
            return false;
        }

        /// The fitting is about this wide: a station narrower than twice this
        /// at the deck is the very tip, with no deck to bolt it to.
        const float MinDeckHalfBreadth = 0.35f;
        const float StemSetBack = 0.8f;
        const float FallbackDeckY = 1.6f;

        // --- stand-ins -----------------------------------------------------

        static Material wood, iron;

        static Material Mat(ref Material m, Color c)
        {
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HarpoonPlaceholder" };
                m.SetColor("_BaseColor", c);
            }
            return m;
        }

        static Material Wood => Mat(ref wood, new Color(0.42f, 0.3f, 0.2f));
        static Material Iron => Mat(ref iron, new Color(0.22f, 0.22f, 0.24f));

        static GameObject PlaceholderMount(Transform parent)
        {
            var root = new GameObject("HarpoonMount");
            root.transform.SetParent(parent, false);

            Box(root.transform, "Post", PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0f),
                new Vector3(0.32f, 0.3f, 0.32f), Wood);

            var swivel = new GameObject("Swivel").transform;
            swivel.SetParent(root.transform, false);
            swivel.localPosition = new Vector3(0f, 0.65f, 0f);

            Box(swivel, "Cradle", PrimitiveType.Cube, new Vector3(0f, 0.05f, -0.1f),
                new Vector3(0.36f, 0.14f, 0.8f), Wood);
            var barrel = Box(swivel, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0.15f),
                new Vector3(0.12f, 0.55f, 0.12f), Iron);
            barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var muzzle = new GameObject("Barb_Muzzle").transform;
            muzzle.SetParent(swivel, false);
            muzzle.localPosition = new Vector3(0f, 0.18f, 0.72f);

            var drum = new GameObject("Winch_Drum").transform;
            drum.SetParent(swivel, false);
            drum.localPosition = new Vector3(0f, 0.12f, -0.42f);
            var spool = Box(drum, "Spool", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(0.3f, 0.2f, 0.3f), Wood);
            spool.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // A spoke, so the spin reads on a plain cylinder.
            Box(drum, "Spoke", PrimitiveType.Cube, new Vector3(0f, 0.12f, 0f),
                new Vector3(0.42f, 0.06f, 0.06f), Iron);

            var stand = new GameObject("Harpooner_Stand").transform;
            stand.SetParent(root.transform, false);
            stand.localPosition = new Vector3(0f, 0f, -1.1f);
            return root;
        }

        static GameObject PlaceholderBarb()
        {
            var root = new GameObject("HarpoonBarb");
            var shaft = Box(root.transform, "Shaft", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.35f),
                new Vector3(0.06f, 0.35f, 0.06f), Wood);
            shaft.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var head = Box(root.transform, "Head", PrimitiveType.Cube, Vector3.zero,
                new Vector3(0.18f, 0.18f, 0.18f), Iron);
            head.localRotation = Quaternion.Euler(0f, 45f, 0f);
            var attach = new GameObject("Line_Attach").transform;
            attach.SetParent(root.transform, false);
            attach.localPosition = new Vector3(0f, 0f, -0.7f);
            return root;
        }

        static Transform Box(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
