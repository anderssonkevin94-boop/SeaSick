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
    /// - lamp (`HarpoonLamp`): `Lamp_Light` (spot, +Z), `Lamp_Lens` (glows).
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
            foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var part in go.GetComponentsInChildren<Transform>(true))
                if (part.name == "Lamp" || part.name == "Lamp_Light" || part.name == "Lamp_Lens")
                    part.gameObject.SetActive(false);
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
        /// (3.15 m forward at the coaster's 0.5 m per unit). The scan keeps
        /// its x/z and stands it on the deck actually there; the 2.11 is the
        /// height when there is no mesh to read.
        static readonly Vector3 BowModuleMountU = new Vector3(6.3f, 0f, 2.11f);

        /// The modular hull's view, if she wears one (a refitted hull does).
        public static SeaSick.Ship.Modular.ModularShipView ModularView(Transform ship) =>
            ship.GetComponentInChildren<SeaSick.Ship.Modular.ModularShipView>();

        /// **The bow stem in ship-local space**: where the fitting stands,
        /// on the centreline at deck height. A modular hull places it on its
        /// bow module at `BowModuleMountU`'s x/z (the art's own spot), on the
        /// top deck the scan finds there (a raised bow's upper floor, not the
        /// foredeck under it). The stock steamer carries a `HullFormData`
        /// (rebound on each refit): its forward-most station with room on
        /// deck. A hull with neither falls back to its `FleetVisual` deck
        /// line, then to the motor's length.
        public static Vector3 BowStem(Transform ship)
        {
            if (Bow(ship, out var view, out var bow, out var host))
            {
                Vector3 inBow = SeaSick.Ship.Modular.ModularScale.AuthoringToGame(BowModuleMountU, view.Current.metresPerUnit);
                if (host != null)
                {
                    var scan = ScanBow(host, inBow);
                    if (scan.hasDeck) inBow.y = scan.deckY;
                    return ship.InverseTransformPoint(host.TransformPoint(inBow));
                }
                Vector3 inView = bow.positionM + bow.rotation * inBow;
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
        /// fitting: the rope's fairlead. A modular hull gives the scan's stem
        /// point (the cap's forward-most face, at its top), else its bow
        /// module's bounds corner (the same module and units
        /// `BowModuleMountU` is measured in); the stock steamer gives its
        /// forward-most station at the deck line. False when the hull has
        /// neither, and the gun falls back to a point ahead of the muzzle.
        public static bool StemTop(Transform ship, out Vector3 local)
        {
            if (Bow(ship, out var view, out var bow, out var host))
            {
                float k = view.Current.metresPerUnit;
                if (host != null)
                {
                    var scan = ScanBow(host, SeaSick.Ship.Modular.ModularScale.AuthoringToGame(BowModuleMountU, k));
                    if (scan.hasStem)
                    {
                        local = ship.InverseTransformPoint(host.TransformPoint(scan.stemTop));
                        return true;
                    }
                }
                if (bow.boundsMaxU.x > BowModuleMountU.x)
                {
                    var tipU = new Vector3(bow.boundsMaxU.x, 0f, Mathf.Max(bow.boundsMaxU.z, BowModuleMountU.z));
                    Vector3 inView = bow.positionM + bow.rotation * SeaSick.Ship.Modular.ModularScale.AuthoringToGame(tipU, k);
                    local = ship.InverseTransformPoint(view.transform.TransformPoint(inView));
                    return true;
                }
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

        /// The modular view, its placed bow module and that module's drawn
        /// GameObject (`ModularShipView.Build` names it "bow (moduleId)"). The
        /// LAST match: a rebuild adds the new module after the old one, which
        /// is still a child until its Destroy lands at the end of the frame.
        /// `host` is null when the view drew nothing for the bow.
        static bool Bow(Transform ship, out SeaSick.Ship.Modular.ModularShipView view,
            out SeaSick.Ship.Modular.PlacedModule bow, out Transform host)
        {
            view = ModularView(ship);
            bow = view != null && view.Current != null
                ? view.Current.Find(SeaSick.Ship.Modular.ShipAssembler.StdKeyBow) : null;
            host = null;
            if (bow == null) return false;
            string prefix = bow.instanceKey + " (";
            foreach (Transform t in view.transform)
                if (t.name.StartsWith(prefix, System.StringComparison.Ordinal)) host = t;
            return true;
        }

        // --- the bow scan --------------------------------------------------

        /// What the bow module's own meshes say, in its local metres (+Z
        /// forward, +Y up; the frame the kit is authored in).
        public struct BowScan
        {
            public bool hasStem;
            /// The stem cap's forward-most face, at the cap's top.
            public Vector3 stemTop;
            public bool hasDeck;
            /// The top deck under the fitting's footprint.
            public float deckY;
        }

        /// m either side of the centreline a vertex still counts as the stem.
        const float StemHalfWidth = 0.25f;
        /// m behind the stem's front face the cap's top is looked for.
        const float StemTopDepth = 0.15f;
        /// m out from the fitting's centre to the four other footprint
        /// samples (the art's base is about 0.7 m across).
        const float FootprintReach = 0.2f;
        /// m a sloping deck may differ between footprint samples.
        const float DeckTolerance = 0.08f;
        /// A deck face's normal is at least this upright (|n.y| of a unit
        /// normal; 0.7 ≈ 45°): walls and the stem's faces never count.
        const float DeckFlatness = 0.7f;

        static Transform scannedHost;
        static Vector3 scannedAt;
        static BowScan scanned;
        static readonly System.Collections.Generic.List<Vector3> scanVerts = new System.Collections.Generic.List<Vector3>(4096);
        static readonly System.Collections.Generic.List<int> scanTris = new System.Collections.Generic.List<int>(12288);
        static readonly System.Collections.Generic.List<Vector3> scanStem = new System.Collections.Generic.List<Vector3>(1024);
        static readonly System.Collections.Generic.List<float>[] scanHeights =
        {
            new System.Collections.Generic.List<float>(), new System.Collections.Generic.List<float>(),
            new System.Collections.Generic.List<float>(), new System.Collections.Generic.List<float>(),
            new System.Collections.Generic.List<float>(),
        };

        /// **Reads the bow module's real meshes, once per drawn module**
        /// (cached on its GameObject, which a refit replaces; the gun only
        /// calls this from its fit). Every readable mesh under the module,
        /// minus the lanterns (kit lanterns: they hang ahead of the
        /// stem and are not the cap) and the merged draw batch (a copy of the
        /// same triangles, lanterns included), brought into module space.
        ///
        /// Stem: the forward-most vertex within `StemHalfWidth` of the
        /// centreline is the cap's front face; the highest centreline vertex
        /// within `StemTopDepth` behind it is the cap's top.
        ///
        /// Deck: a downward ray, in effect, at five points (the fitting's
        /// centre and `FootprintReach` to each side): every near-flat
        /// triangle over a point gives a height there. The deck is the
        /// highest height at the centre that all four other points share
        /// (within `DeckTolerance`), so it is a surface at least as broad as
        /// the fitting: a rail, post or cap passing over one point is not a
        /// deck, and neither is a floor's underside below another floor's
        /// top. A raised bow gives its upper floor, a low bow its foredeck.
        ///
        /// Cost: one pass over the module's vertices and triangles (the
        /// coaster bows are ~43k / ~66k vertices, ~14k / ~22k triangles),
        /// most triangles rejected by a box test before the five point tests.
        public static BowScan ScanBow(Transform host, Vector3 mountInBow)
        {
            if (host == scannedHost && host != null && scannedAt == mountInBow) return scanned;
            var r = new BowScan();
            float frontZ = float.NegativeInfinity;
            scanStem.Clear();
            for (int i = 0; i < scanHeights.Length; i++) scanHeights[i].Clear();
            float minX = mountInBow.x - FootprintReach, maxX = mountInBow.x + FootprintReach;
            float minZ = mountInBow.z - FootprintReach, maxZ = mountInBow.z + FootprintReach;
            Matrix4x4 toBow = host.worldToLocalMatrix;

            foreach (var rend in host.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (rend is MeshRenderer)
                {
                    var mf = rend.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                else if (rend is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
                if (mesh == null || !mesh.isReadable || Skipped(rend.transform, host)) continue;

                Matrix4x4 m = toBow * rend.transform.localToWorldMatrix;
                mesh.GetVertices(scanVerts);
                for (int i = 0; i < scanVerts.Count; i++)
                {
                    Vector3 v = m.MultiplyPoint3x4(scanVerts[i]);
                    scanVerts[i] = v;
                    if (Mathf.Abs(v.x) > StemHalfWidth) continue;
                    scanStem.Add(v);
                    if (v.z > frontZ) frontZ = v.z;
                }

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    mesh.GetTriangles(scanTris, sub);
                    for (int t = 0; t + 2 < scanTris.Count; t += 3)
                    {
                        Vector3 a = scanVerts[scanTris[t]], b = scanVerts[scanTris[t + 1]], c = scanVerts[scanTris[t + 2]];
                        if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < minX || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > maxX
                            || Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < minZ || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > maxZ) continue;
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        float len = n.magnitude;
                        if (len < 1e-8f || Mathf.Abs(n.y) < DeckFlatness * len) continue;
                        for (int s = 0; s < scanHeights.Length; s++)
                            if (HeightOver(a, b, c, FootprintSample(mountInBow, s), out float h)) scanHeights[s].Add(h);
                    }
                }
            }

            if (scanStem.Count > 0)
            {
                float topY = float.NegativeInfinity;
                foreach (var v in scanStem)
                    if (v.z >= frontZ - StemTopDepth && v.y > topY) topY = v.y;
                r.hasStem = true;
                r.stemTop = new Vector3(0f, topY, frontZ);
            }

            var centre = scanHeights[0];
            centre.Sort();
            for (int i = centre.Count - 1; i >= 0 && !r.hasDeck; i--)
            {
                bool broad = true;
                for (int s = 1; s < scanHeights.Length && broad; s++)
                    broad = HasNear(scanHeights[s], centre[i], DeckTolerance);
                if (broad) { r.hasDeck = true; r.deckY = centre[i]; }
            }

            scanVerts.Clear();
            scanTris.Clear();
            scanStem.Clear();
            scannedHost = host;
            scannedAt = mountInBow;
            scanned = r;
            return r;
        }

        /// Sample 0 is the fitting's centre; 1-4 sit `FootprintReach` to
        /// either side and fore and aft of it.
        static Vector3 FootprintSample(Vector3 centre, int s)
        {
            switch (s)
            {
                case 1: return centre + new Vector3(FootprintReach, 0f, 0f);
                case 2: return centre - new Vector3(FootprintReach, 0f, 0f);
                case 3: return centre + new Vector3(0f, 0f, FootprintReach);
                case 4: return centre - new Vector3(0f, 0f, FootprintReach);
                default: return centre;
            }
        }

        /// The triangle's height straight above or below `p` (x/z), false
        /// when `p` is outside it seen from above.
        static bool HeightOver(Vector3 a, Vector3 b, Vector3 c, Vector3 p, out float h)
        {
            h = 0f;
            float ux = b.x - a.x, uz = b.z - a.z, vx = c.x - a.x, vz = c.z - a.z;
            float d = ux * vz - uz * vx;
            if (Mathf.Abs(d) < 1e-10f) return false;
            float px = p.x - a.x, pz = p.z - a.z;
            float s = (px * vz - pz * vx) / d;
            float t = (ux * pz - uz * px) / d;
            const float eps = 1e-5f;
            if (s < -eps || t < -eps || s + t > 1f + eps) return false;
            h = a.y + s * (b.y - a.y) + t * (c.y - a.y);
            return true;
        }

        static bool HasNear(System.Collections.Generic.List<float> heights, float y, float tolerance)
        {
            foreach (var h in heights) if (Mathf.Abs(h - y) <= tolerance) return true;
            return false;
        }

        /// Lantern pieces (any "Lantern" in the name up to the module: the
        /// kit's `Lantern_Bow_*`, its chain and glass) and the merged draw batch are not the hull's shape.
        static bool Skipped(Transform t, Transform host)
        {
            if (t.GetComponent<SeaSick.Ship.Modular.CoasterOwnedMesh>() != null) return true;
            for (; t != null && t != host; t = t.parent)
                if (t.name.IndexOf("Lantern", System.StringComparison.Ordinal) >= 0) return true;
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

            // The gun lamp: a hooded casing bolted to the barrel's starboard side, lens facing +Z.
            var casing = Box(swivel, "Lamp", PrimitiveType.Cylinder, new Vector3(0.24f, 0.2f, 0.5f),
                new Vector3(0.14f, 0.09f, 0.14f), Iron);
            casing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lens = Box(swivel, HarpoonLamp.LensName, PrimitiveType.Cylinder, new Vector3(0.24f, 0.2f, 0.595f),
                new Vector3(0.11f, 0.01f, 0.11f), HarpoonLamp.LensMaterial);
            lens.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lampLight = new GameObject(HarpoonLamp.LightName).transform;
            lampLight.SetParent(swivel, false);
            lampLight.localPosition = new Vector3(0.24f, 0.2f, 0.6f);

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
