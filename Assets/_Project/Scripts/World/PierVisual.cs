using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SeaSick.World
{
    /// **Astra's pier kit v4 laid out as a pier's planks, 2026-09-23.**
    ///
    /// Decoration only. The pier's LOGIC -- its root at deck height in the
    /// middle of its length, `Pier.LandEnd` / `SeaEnd` / `Berth`, the dock
    /// registry, the length `Outpost.SnapPier` chose -- is untouched: this
    /// hangs one child, `PierKit`, off the root and puts Astra's modules in
    /// it. Nothing here has a collider (the kit's are stripped, as `Dress`
    /// strips a building's), so pathing and grounding see exactly what they
    /// saw with the box pier.
    ///
    /// **The kit's contract** (`art-staging/pier-astra-lvl1-v4/README.md`):
    /// every deck module's root sits at MEAN WATER at its land end, the
    /// walking surface is 1.2 m above it, and Blender +X runs land to sea.
    /// So the `PierKit` child is lowered 1.2 m and moved half the length
    /// toward land, and the modules run from its origin along +X.
    ///
    /// **No axis is hard-coded.** The FBXs are exported -Z forward / Y up
    /// and Unity's importer may add a root rotation, a x100 scale, or both.
    /// Each prefab's frame is measured once from its own markers
    /// (`<Part>__Snap_Land`, `__Snap_Sea`, `__Deck_Land`): along = Snap_Sea
    /// - Snap_Land, up = Deck_Land - Snap_Land, and the width axis is their
    /// cross product. The accessories (shaft, cap, ramp, pad) carry no snap
    /// markers, so they borrow `Pier_Deck_2m`'s frame -- same exporter, same
    /// importer -- and are refused unless their own marker pair agrees with
    /// it. Every position is read through the transforms, never as raw
    /// numbers, so an imported root scale cancels out.
    ///
    /// **All or nothing.** If any prefab is missing from `Resources/Pier/`,
    /// or any marker is missing or disagrees with the contract, `Build`
    /// returns false before placing anything and the caller raises the box
    /// pier instead. Half a kit on a beach is worse than plain boxes.
    public static class PierVisual
    {
        /// Where the prefabs load from: `Resources/Pier/<Part>.prefab`.
        public const string Folder = "Pier/";
        /// The one child this puts on a pier's root.
        public const string VisualName = "PierKit";

        const string Deck1 = "Pier_Deck_1m", Deck2 = "Pier_Deck_2m";
        const string Infill1 = "Pier_Infill_1m", Infill2 = "Pier_Infill_2m";
        const string SeaEnd1 = "Pier_Sea_End_1m", SeaEnd2 = "Pier_Sea_End_2m";
        const string Piling = "Pier_Piling_1m", Cap = "Pier_Pile_Cap";
        const string Ramp = "Pier_Shore_Ramp", Pad = "Pier_Ramp_Foot_Pad";

        static readonly string[] Parts =
        {
            Deck1, Deck2, Infill1, Infill2, SeaEnd1, SeaEnd2, Piling, Cap, Ramp, Pad,
        };

        // --- the contract, in CANONICAL metres ------------------------------
        //
        // Canonical = the kit's own frame with Unity's axis names: X along
        // (land to sea, Blender +X), Y up (Blender +Z), Z across to the LEFT
        // walking out (Blender +Y). A Blender (x, y, z) is canonical (x, z, y).
        // It is also the `PierKit` child's local frame: the pier root's +X is
        // land to sea and its +Z is the left side (`Pier.Berth` is off the
        // right side, which is where the kit's cleat is).

        /// Walking surface above a module's root. V3 had it at 0; a V3
        /// prefab slipped into the folder is refused on this number.
        const float KitDeck = 1.2f;
        const float RampRun = 2.4f, DropMin = 0.3f, DropMax = 2.5f;
        /// How far a shaft is driven below the ground under it.
        const float Embed = 0.3f;
        /// Aimed support spacing along the deck, metres.
        const float SupportEvery = 4f;
        /// A shaft shorter than this (the beach is up at the cap) is skipped.
        const float ShaftMin = 0.05f;
        /// Position tolerance for every contract check, metres.
        const float Tol = 0.02f;
        const float AxisTolDeg = 2f;

        static readonly Vector3 PileLeftC = new Vector3(0.4f, 0.46f, 1.24f);
        static readonly Vector3 LightC = new Vector3(0.4f, 2.5f, 0.58f);
        static readonly Color LanternColour = new Color(1f, 0.64f, 0.32f);

        /// Why the last `Build` fell back to boxes, or empty if it did not.
        public static string LastFailure { get; private set; } = "";

        /// One measured prefab.
        sealed class Part
        {
            public string name;
            public GameObject asset;
            /// Canonical axes -> the prefab root's space (root rotation and
            /// position removed, root scale kept).
            public Quaternion rot = Quaternion.identity;
            /// The canonical origin in that space, and in the root's local
            /// (pre-scale) coordinates.
            public Vector3 rOrigin, lOrigin;
            /// Root-space units per metre (1 for a clean import, 100 or 0.01
            /// when a scale was left on the root).
            public float upm = 1f;
            // modules
            public float length;
            public bool supported, seaEnd;
            /// Shaft: the root-local axis it runs along (stretched).
            /// Ramp: `Ramp_Span`'s local axis toward the Toe (stretched).
            public int axis = -1;
        }

        static Dictionary<string, Part> kit;
        static readonly HashSet<string> warned = new HashSet<string>();
        static MaterialPropertyBlock glowBlock;

        /// Forget the measured kit, so the next build reloads the prefabs.
        /// For the editor after a re-import without a domain reload.
        public static void ResetKit() { kit = null; LastFailure = ""; warned.Clear(); }

        // --- build ----------------------------------------------------------

        /// Lay the kit out under `root` (a pier's root: at deck height,
        /// centred along `plan.footprint.x`, local +X land to sea). False,
        /// with nothing left behind, when the kit cannot be used.
        public static bool Build(Transform root, BuildPlan plan, System.Func<float, float, float> ground)
        {
            if (root == null) return false;
            if (!EnsureKit(out string why))
            {
                LastFailure = why;
                WarnOnce("[Pier] Astra's pier kit not used, box pier instead: " + why);
                return false;
            }

            var vis = new GameObject(VisualName).transform;
            vis.SetParent(root, false);
            try
            {
                Assemble(root, vis, plan, ground);
                LastFailure = "";
                return true;
            }
            catch (System.Exception e)
            {
                LastFailure = "layout threw: " + e.Message;
                Debug.LogWarning("[Pier] kit layout failed, box pier instead: " + e);
                // Out of the root first: a deferred Destroy would otherwise
                // leave the half-built kit beside the boxes for a frame, and
                // `Ghost` tints/strips the root in that same frame.
                vis.SetParent(null, false);
                vis.gameObject.SetActive(false);
                Kill(vis.gameObject);
                return false;
            }
        }

        static void Assemble(Transform root, Transform vis, BuildPlan plan,
            System.Func<float, float, float> ground)
        {
            float len = plan.footprint.x;                 // logical, never changed
            int lv = Mathf.Max(1, Mathf.RoundToInt(len)); // visual, whole metres
            float deckY = root.position.y;                 // world deck height

            // Land end of the kit on the pier's land end, at mean water.
            vis.localPosition = new Vector3(-len * 0.5f, -KitDeck, 0f);
            vis.localRotation = Quaternion.identity;
            vis.localScale = Vector3.one;

            var layout = Layout(lv);
            for (int i = 0; i < layout.Count; i++)
            {
                var slot = layout[i];
                var p = kit[slot.part];
                var m = Place(p, vis, new Vector3(slot.x, 0f, 0f), $"Module_{i:00}_{slot.part}");
                if (p.supported) Posts(p, m, vis, i, ground);
                if (p.seaEnd) Lantern(p, m, vis, slot.x);
            }

            // The ramp, hinged at the land-end deck edge, down to a foot
            // 2.4 m further up the beach.
            Vector3 foot = root.TransformPoint(new Vector3(-len * 0.5f - RampRun, 0f, 0f));
            float g = ground != null ? ground(foot.x, foot.z) : 0f;
            float d = Mathf.Clamp(deckY - g, DropMin, DropMax);
            var ramp = Place(kit[Ramp], vis, Vector3.zero, "Ramp");
            PoseRamp(vis, ramp, kit[Ramp], d);
            // The pad: its origin is its TOP contact surface, level, unscaled.
            Place(kit[Pad], vis, new Vector3(-RampRun, KitDeck - d, 0f), "RampPad");
        }

        struct Slot { public string part; public int x; }

        /// **The bay rule.** One sea-end module last (2 m, or 1 m on a pier
        /// of one metre). The run before it is split into `round(run / 4)`
        /// support spans of whole metres, as even as they divide, the longer
        /// ones seaward. Each span is one supported deck (2 m, or 1 m for a
        /// 1 m span) and then infill: 2 m pieces, and one 1 m piece for an
        /// odd remainder, last. 15 m gives supports at 0, 4, 8 and the sea
        /// end at 13 with the 1 m infill at 12 -- Astra's review layout --
        /// and 24 m, the longest `SnapPier` builds, is 13 modules and 7
        /// support pairs.
        static List<Slot> Layout(int lv)
        {
            var list = new List<Slot>();
            int seaLen = lv >= 2 ? 2 : 1;
            int run = lv - seaLen;
            int x = 0;
            if (run > 0)
            {
                int spans = Mathf.Max(1, Mathf.RoundToInt(run / SupportEvery));
                int baseLen = run / spans, extra = run % spans;
                for (int s = 0; s < spans; s++)
                {
                    int span = baseLen + (s >= spans - extra ? 1 : 0);
                    int head = span >= 2 ? 2 : 1;
                    list.Add(new Slot { part = head == 2 ? Deck2 : Deck1, x = x });
                    x += head;
                    int rest = span - head;
                    while (rest >= 2) { list.Add(new Slot { part = Infill2, x = x }); x += 2; rest -= 2; }
                    if (rest == 1) { list.Add(new Slot { part = Infill1, x = x }); x += 1; }
                }
            }
            list.Add(new Slot { part = seaLen == 2 ? SeaEnd2 : SeaEnd1, x = x });
            return list;
        }

        /// The module layout for a pier of `lengthMetres`, as text:
        /// "Deck2@0 Infill2@2 ... SeaEnd2@13".
        public static string Describe(int lengthMetres)
        {
            var sb = new StringBuilder();
            foreach (var s in Layout(Mathf.Max(1, lengthMetres)))
                sb.Append(Short(s.part)).Append('@').Append(s.x).Append(' ');
            return sb.ToString().TrimEnd();
        }

        static string Short(string part) => part.Replace("Pier_", "").Replace("_", "");

        /// A cap and a shaft at each of a supported module's pile markers.
        /// Both origins on the marker; only the shaft is stretched, down to
        /// the ground under it (clamped at `PostDeepest`) and 0.3 m into it.
        static void Posts(Part p, Transform module, Transform vis, int i,
            System.Func<float, float, float> ground)
        {
            var shaft = kit[Piling];
            for (int side = 0; side < 2; side++)
            {
                string tag = side == 0 ? "L" : "R";
                var marker = Find(module, p.name + (side == 0 ? "__Pile_Left" : "__Pile_Right"));
                Vector3 at = vis.InverseTransformPoint(marker.position);
                Place(kit[Cap], vis, at, $"Cap_{i:00}{tag}");

                Vector3 w = marker.position;
                float g = ground != null ? ground(w.x, w.z) : 0f;
                float bottom = Mathf.Max(g, BuildingFactory.PostDeepest) - Embed;
                float metres = w.y - bottom;
                if (metres < ShaftMin) continue;        // the beach is up at the cap
                Place(shaft, vis, at, $"Shaft_{i:00}{tag}", shaft.axis, metres);
            }
        }

        /// The sea end's lantern: a small warm point light at its
        /// `Light_Source`, night-gated by the same `Campfire` every other
        /// building lamp uses (so `Ghost` strips it like theirs), and the
        /// amber panes brightened once through a property block.
        static void Lantern(Part p, Transform module, Transform vis, int slotX)
        {
            var marker = Find(module, p.name + "__Light_Source");
            Vector3 at = marker != null
                ? vis.InverseTransformPoint(marker.position)
                : new Vector3(slotX, 0f, 0f) + LightC;

            var go = new GameObject("PierLantern");
            go.transform.SetParent(vis, false);
            go.transform.localPosition = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = LanternColour;
            l.range = 6f;
            l.intensity = 1.2f;              // a lantern, dimmer than a window (`Lamp` is 1.6)
            l.shadows = LightShadows.None;
            var flame = go.AddComponent<Campfire>();
            flame.flicker = 0.05f;

            // The panes. The vertex-colour shader the kits use has no
            // emission, but it has `_Ambient`, an unlit floor added to the
            // light: 1 makes the amber read as lit at night without a new
            // material. URP Lit gets `_EmissionColor` if its keyword is on.
            foreach (var r in module.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (n.IndexOf("Glow", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Pane", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                glowBlock ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(glowBlock);
                if (mat.HasProperty("_EmissionColor") && mat.IsKeywordEnabled("_EMISSION"))
                    glowBlock.SetColor("_EmissionColor", LanternColour * 1.5f);
                else if (mat.HasProperty("_Ambient"))
                    glowBlock.SetFloat("_Ambient", 1f);
                else continue;
                r.SetPropertyBlock(glowBlock);
            }
        }

        /// **The ramp, per the kit's equations**, in Unity terms: stretch
        /// `Ramp_Span` along its own axis toward the Toe by
        /// sqrt(2.4^2 + d^2) / 2.4, then swing `Ramp_Hinge` about the pier's
        /// ACROSS axis by atan2(d, 2.4). Blender's `-atan2` is about +Y in a
        /// right-handed frame; Unity's `AngleAxis` is left-handed, so about
        /// the same physical axis the sign is +. That is the first guess,
        /// and the Toe decides: it must land 2.4 m landward of the deck edge
        /// and d below it, or the other sign is tried and a warning logged.
        static void PoseRamp(Transform vis, Transform ramp, Part rp, float d)
        {
            var hinge = Find(ramp, "Ramp_Hinge");
            var span = Find(ramp, "Ramp_Span");
            var toe = Find(ramp, Ramp + "__Toe");

            var sc = span.localScale;
            sc[rp.axis] *= Mathf.Sqrt(RampRun * RampRun + d * d) / RampRun;
            span.localScale = sc;

            Quaternion rest = hinge.rotation;
            Vector3 across = vis.TransformDirection(Vector3.forward);
            Vector3 want = new Vector3(-RampRun, KitDeck - d, 0f);
            float ang = Mathf.Atan2(d, RampRun) * Mathf.Rad2Deg;

            float bestErr = float.MaxValue, bestSign = 1f;
            for (int k = 0; k < 2; k++)
            {
                float sign = k == 0 ? 1f : -1f;
                hinge.rotation = Quaternion.AngleAxis(sign * ang, across) * rest;
                float err = (vis.InverseTransformPoint(toe.position) - want).magnitude;
                if (err < bestErr) { bestErr = err; bestSign = sign; }
                if (err <= Tol) break;
            }
            hinge.rotation = Quaternion.AngleAxis(bestSign * ang, across) * rest;
            if (bestErr > Tol)
                WarnOnce($"[Pier] ramp Toe misses its mark by {bestErr:F3} m at drop {d:F2} m "
                    + "with either hinge sign -- check Ramp_Hinge / Ramp_Span in the prefab.");
            else if (bestSign < 0f)
                WarnOnce("[Pier] ramp hinge needed the flipped sign (-atan2 in Unity); the Toe check caught it.");
        }

        /// Instantiate `p` under `parent` with its canonical origin at `at`
        /// (parent-local metres) and its canonical axes on the parent's.
        /// `stretch` scales one root-local axis (the shaft's length).
        static Transform Place(Part p, Transform parent, Vector3 at, string name,
            int stretchAxis = -1, float stretch = 1f)
        {
            var go = Object.Instantiate(p.asset, parent, false);
            go.name = name;
            var t = go.transform;
            Vector3 scale = p.asset.transform.localScale / p.upm;
            if (stretchAxis >= 0) scale[stretchAxis] *= stretch;
            t.localRotation = Quaternion.Inverse(p.rot);
            t.localScale = scale;
            t.localPosition = at - t.localRotation * Vector3.Scale(scale, p.lOrigin);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
            return t;
        }

        // --- measuring the kit, once ----------------------------------------

        static bool EnsureKit(out string why)
        {
            why = "";
            if (kit != null)
            {
                bool alive = true;
                foreach (var p in kit.Values) if (p.asset == null) { alive = false; break; }
                if (alive) return true;
                kit = null;
            }

            var k = new Dictionary<string, Part>();
            foreach (var name in Parts)
            {
                var asset = Resources.Load<GameObject>(Folder + name);
                if (asset == null) { why = $"no prefab at Resources/{Folder}{name}"; return false; }
                k[name] = new Part { name = name, asset = asset };
            }

            // The deck modules measure themselves; Deck_2m's frame is the kit's.
            if (!Module(k[Deck2], 2f, true, false, out why)) return false;
            if (!Module(k[Deck1], 1f, true, false, out why)) return false;
            if (!Module(k[Infill1], 1f, false, false, out why)) return false;
            if (!Module(k[Infill2], 2f, false, false, out why)) return false;
            if (!Module(k[SeaEnd1], 1f, true, true, out why)) return false;
            if (!Module(k[SeaEnd2], 2f, true, true, out why)) return false;
            Quaternion conv = k[Deck2].rot;
            foreach (var n in new[] { Deck1, Infill1, Infill2, SeaEnd1, SeaEnd2 })
            {
                float a = Quaternion.Angle(k[n].rot, conv);
                if (a > AxisTolDeg) { why = $"{n}'s axes are {a:F1} deg off {Deck2}'s"; return false; }
            }

            // The accessories borrow that frame and must agree with it.
            var shaft = k[Piling];
            if (!Accessory(shaft, conv, Piling + "__Top", Piling + "__Bottom",
                new Vector3(0f, -1f, 0f), out why)) return false;
            {
                var root = shaft.asset.transform;
                Vector3 dl = RootLocal(root, Find(root, Piling + "__Bottom"))
                           - RootLocal(root, Find(root, Piling + "__Top"));
                shaft.axis = Dominant(dl, out float purity);
                if (purity < 0.999f)
                { why = $"{Piling}: the shaft is not along one of the prefab root's axes, so it cannot be stretched"; return false; }
            }
            if (!Accessory(k[Cap], conv, Cap + "__Shaft_Top", Cap + "__Cap_Top",
                new Vector3(0f, 1.289f, 0f), out why)) return false;
            if (!Accessory(k[Pad], conv, Pad + "__Toe_Contact", Pad + "__Shore_Edge",
                new Vector3(-0.64f, 0f, 0f), out why)) return false;
            if (!RampPart(k[Ramp], conv, out why)) return false;

            kit = k;
            return true;
        }

        /// A deck, infill or sea-end module: its frame from its own snap
        /// markers, then the contract checked against it.
        static bool Module(Part p, float length, bool supported, bool seaEnd, out string why)
        {
            why = "";
            p.length = length; p.supported = supported; p.seaEnd = seaEnd;
            var root = p.asset.transform;
            var land = Find(root, p.name + "__Snap_Land");
            var sea = Find(root, p.name + "__Snap_Sea");
            var deck = Find(root, p.name + "__Deck_Land");
            if (land == null || sea == null || deck == null)
            { why = $"{p.name}: missing __Snap_Land / __Snap_Sea / __Deck_Land marker"; return false; }

            Vector3 rl = RootSpace(root, land), rs = RootSpace(root, sea), rd = RootSpace(root, deck);
            Vector3 along = rs - rl;
            float units = along.magnitude;
            if (units < 1e-6f) { why = $"{p.name}: Snap_Land and Snap_Sea coincide"; return false; }
            along /= units;
            Vector3 up = Vector3.ProjectOnPlane(rd - rl, along);
            float deckUnits = up.magnitude;
            if (deckUnits < 1e-6f)
            { why = $"{p.name}: the walking surface is at the root (a V3 kit? V4 has it 1.2 m up)"; return false; }
            up /= deckUnits;

            p.upm = units / length;
            // Blender +Y (left walking out) = along x up with Unity's cross:
            // the importer's handedness flip is a reflection, which negates
            // a cross product, so Blender's Z x X comes out as along x up.
            // Pile_Left below checks it.
            p.rot = Quaternion.LookRotation(Vector3.Cross(along, up), up);
            p.rOrigin = rl;
            p.lOrigin = RootLocal(root, land);

            float deckM = deckUnits / p.upm;
            if (Mathf.Abs(deckM - KitDeck) > 0.01f)
            { why = $"{p.name}: walking surface {deckM:F3} m above the root, the V4 contract says {KitDeck}"; return false; }

            if (supported)
            {
                var pl = Find(root, p.name + "__Pile_Left");
                var pr = Find(root, p.name + "__Pile_Right");
                if (pl == null || pr == null) { why = $"{p.name}: missing __Pile_Left / __Pile_Right"; return false; }
                Vector3 c = Canon(p, RootSpace(root, pl));
                if ((c - PileLeftC).magnitude > Tol)
                {
                    var mirrored = new Vector3(PileLeftC.x, PileLeftC.y, -PileLeftC.z);
                    if ((c - mirrored).magnitude <= Tol)
                        WarnOnce($"[Pier] {p.name}: Pile_Left measured on the RIGHT -- the import mirrored the "
                            + "width; the lantern and cleat will be on the opposite sides from Astra's.");
                    else
                    { why = $"{p.name}: Pile_Left at {c:F3} module metres, contract {PileLeftC:F3}"; return false; }
                }
            }
            if (seaEnd && Find(root, p.name + "__Light_Source") == null)
                WarnOnce($"[Pier] {p.name}: no __Light_Source marker; the lantern light goes at the contract point.");
            return true;
        }

        /// An accessory with one marker pair: origin at `from`, frame
        /// borrowed from the deck kit, refused if `from -> to` does not run
        /// along `canon` (canonical metres) in that frame.
        static bool Accessory(Part p, Quaternion conv, string from, string to, Vector3 canon, out string why)
        {
            why = "";
            var root = p.asset.transform;
            var a = Find(root, from);
            var b = Find(root, to);
            if (a == null || b == null) { why = $"{p.name}: missing {from} / {to}"; return false; }
            return Axis(p, conv, a, b, canon, out why);
        }

        static bool Axis(Part p, Quaternion conv, Transform a, Transform b, Vector3 canon, out string why)
        {
            why = "";
            var root = p.asset.transform;
            Vector3 r0 = RootSpace(root, a), r1 = RootSpace(root, b);
            Vector3 v = r1 - r0;
            float units = v.magnitude;
            if (units < 1e-6f) { why = $"{p.name}: {a.name} and {b.name} coincide"; return false; }
            float ang = Vector3.Angle(v, conv * canon);
            if (ang > AxisTolDeg)
            { why = $"{p.name}: {a.name} -> {b.name} is {ang:F1} deg off the deck kit's axes"; return false; }
            p.upm = units / canon.magnitude;
            p.rot = conv;
            p.rOrigin = r0;
            p.lOrigin = RootLocal(root, a);
            return true;
        }

        /// The ramp: `Ramp_Hinge` -> `__Toe` runs 2.4 m landward at rest,
        /// and the water-level origin is 1.2 m straight below the hinge.
        static bool RampPart(Part p, Quaternion conv, out string why)
        {
            var root = p.asset.transform;
            var hinge = Find(root, "Ramp_Hinge");
            var span = Find(root, "Ramp_Span");
            var toe = Find(root, Ramp + "__Toe");
            if (hinge == null || span == null || toe == null)
            { why = $"{Ramp}: missing Ramp_Hinge / Ramp_Span / {Ramp}__Toe"; return false; }
            if (!toe.IsChildOf(span) || !span.IsChildOf(hinge))
            { why = $"{Ramp}: expected Ramp_Hinge > Ramp_Span > {Ramp}__Toe"; return false; }
            if (!Axis(p, conv, hinge, toe, new Vector3(-RampRun, 0f, 0f), out why)) return false;

            if ((RootSpace(root, span) - RootSpace(root, hinge)).magnitude / p.upm > Tol)
            { why = $"{Ramp}: Ramp_Span's origin is not on the hinge"; return false; }

            p.rOrigin = RootSpace(root, hinge) - conv * new Vector3(0f, KitDeck, 0f) * p.upm;
            p.lOrigin = root.InverseTransformPoint(root.position + root.rotation * p.rOrigin);

            p.axis = Dominant(span.InverseTransformPoint(toe.position), out float purity);
            if (purity < 0.999f)
            { why = $"{Ramp}: the Toe is not along one of Ramp_Span's axes, so the span cannot be stretched"; return false; }
            return true;
        }

        // --- helpers --------------------------------------------------------

        /// Position in the root's space with its rotation and position taken
        /// out and its scale left in (a prefab asset's root has no parent).
        static Vector3 RootSpace(Transform root, Transform t)
            => Quaternion.Inverse(root.rotation) * (t.position - root.position);

        /// Position in the root's local, pre-scale coordinates.
        static Vector3 RootLocal(Transform root, Transform t) => root.InverseTransformPoint(t.position);

        /// Root-space point -> canonical metres.
        static Vector3 Canon(Part p, Vector3 r) => Quaternion.Inverse(p.rot) * (r - p.rOrigin) / p.upm;

        static int Dominant(Vector3 v, out float purity)
        {
            int i = 0;
            for (int a = 1; a < 3; a++) if (Mathf.Abs(v[a]) > Mathf.Abs(v[i])) i = a;
            float m = v.magnitude;
            purity = m > 1e-9f ? Mathf.Abs(v[i]) / m : 0f;
            return i;
        }

        /// A descendant by exact name, or by name before an importer's
        /// `.001`-style suffix.
        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || BuildingFactory.Stem(t.name) == name) return t;
            return null;
        }

        static Transform FindSuffix(Transform root, string suffix)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (BuildingFactory.Stem(t.name).EndsWith(suffix, System.StringComparison.Ordinal)) return t;
            return null;
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        static void WarnOnce(string msg)
        {
            if (warned.Add(msg)) Debug.LogWarning(msg);
        }

        // --- verification ---------------------------------------------------

        /// **What the coordinator runs after the import.** Reads a raised
        /// pier (or its ghost) back and reports: piece counts, the walking
        /// surface at both ends against the root's deck height, the first
        /// and last snap points against the pier's logical land and sea
        /// ends, the ramp Toe against where the equations put it, the pad
        /// on the Toe, every shaft's foot against the ground, every cap on
        /// its shaft, and the lantern on its marker. Ends PASS or FAIL.
        public static string SelfCheck(Transform builtRoot)
        {
            var sb = new StringBuilder("[PierVisual.SelfCheck] ");
            if (builtRoot == null) return sb.Append("null root").ToString();
            var vis = builtRoot.Find(VisualName);
            if (vis == null)
                return sb.Append("no ").Append(VisualName).Append(" under '").Append(builtRoot.name)
                    .Append("' -- box pier fallback. Last kit failure: ")
                    .Append(string.IsNullOrEmpty(LastFailure) ? "(none recorded)" : LastFailure).ToString();

            bool pass = true;
            float len = -2f * vis.localPosition.x;
            int lv = Mathf.Max(1, Mathf.RoundToInt(len));
            var ground = Island.TerrainHeight;
            float deckY = builtRoot.position.y;

            var modules = new List<Transform>();
            var caps = new List<Transform>();
            var shafts = new List<Transform>();
            Transform ramp = null, pad = null, lantern = null;
            foreach (Transform c in vis)
            {
                if (c.name.StartsWith("Module_", System.StringComparison.Ordinal)) modules.Add(c);
                else if (c.name.StartsWith("Cap_", System.StringComparison.Ordinal)) caps.Add(c);
                else if (c.name.StartsWith("Shaft_", System.StringComparison.Ordinal)) shafts.Add(c);
                else if (c.name == "Ramp") ramp = c;
                else if (c.name == "RampPad") pad = c;
                else if (c.name == "PierLantern") lantern = c;
            }
            modules.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            sb.Append($"logical {len:F2} m, visual {lv} m; modules {modules.Count}, caps {caps.Count}, "
                + $"shafts {shafts.Count}, ramp {(ramp != null ? 1 : 0)}, pad {(pad != null ? 1 : 0)}, "
                + $"lantern {(lantern != null ? 1 : 0)}\n");
            sb.Append("  layout ").Append(Describe(lv)).Append('\n');

            if (modules.Count == 0) { sb.Append("  FAIL no modules\n"); return sb.Append("FAIL").ToString(); }
            var first = modules[0];
            var last = modules[modules.Count - 1];

            // Walking surface vs the root's deck (root-local y = 0).
            var dl = FindSuffix(first, "__Deck_Land");
            var ds = FindSuffix(last, "__Deck_Sea");
            if (dl == null || ds == null) { sb.Append("  FAIL deck markers missing\n"); pass = false; }
            else
            {
                float eL = builtRoot.InverseTransformPoint(dl.position).y;
                float eS = builtRoot.InverseTransformPoint(ds.position).y;
                bool ok = Mathf.Abs(eL) <= Tol && Mathf.Abs(eS) <= Tol;
                pass &= ok;
                sb.Append($"  {Mark(ok)} walking surface vs deck: land {eL:+0.000;-0.000} m, sea {eS:+0.000;-0.000} m\n");
            }

            // Snap ends vs the logical ends.
            var sl = FindSuffix(first, "__Snap_Land");
            var ss = FindSuffix(last, "__Snap_Sea");
            if (sl == null || ss == null) { sb.Append("  FAIL snap markers missing\n"); pass = false; }
            else
            {
                Vector3 lLand = builtRoot.InverseTransformPoint(sl.position);
                Vector3 lSea = builtRoot.InverseTransformPoint(ss.position);
                Vector3 eLand = lLand - new Vector3(-len * 0.5f, -KitDeck, 0f);
                Vector3 eSea = lSea - new Vector3(len * 0.5f, -KitDeck, 0f);
                float rounding = lv - len;
                bool ok = eLand.magnitude <= Tol
                    && Mathf.Abs(eSea.x - rounding) <= Tol && Mathf.Abs(eSea.y) <= Tol && Mathf.Abs(eSea.z) <= Tol;
                pass &= ok;
                sb.Append($"  {Mark(ok)} Snap_Land - logical land end {eLand:F3}; "
                    + $"Snap_Sea - logical sea end {eSea:F3} (whole-metre rounding {rounding:+0.00;-0.00})\n");
                var pier = builtRoot.GetComponent<Pier>();
                if (pier != null)
                {
                    Vector3 a = sl.position - pier.LandEnd; a.y = 0f;
                    Vector3 b = ss.position - pier.SeaEnd; b.y = 0f;
                    sb.Append($"       vs Pier.LandEnd {a.magnitude:F3} m flat, vs Pier.SeaEnd {b.magnitude:F3} m flat\n");
                }
            }

            // Ramp Toe vs the equations, and the pad on it.
            if (ramp == null || pad == null) { sb.Append("  FAIL ramp or pad missing\n"); pass = false; }
            else
            {
                Vector3 foot = builtRoot.TransformPoint(new Vector3(-len * 0.5f - RampRun, 0f, 0f));
                float g = ground != null ? ground(foot.x, foot.z) : 0f;
                float d = Mathf.Clamp(deckY - g, DropMin, DropMax);
                var toe = FindSuffix(ramp, "__Toe");
                var contact = FindSuffix(pad, "__Toe_Contact");
                if (toe == null || contact == null) { sb.Append("  FAIL Toe / Toe_Contact marker missing\n"); pass = false; }
                else
                {
                    Vector3 want = new Vector3(-len * 0.5f - RampRun, -d, 0f);
                    Vector3 got = builtRoot.InverseTransformPoint(toe.position);
                    float eToe = (got - want).magnitude;
                    float ePad = (contact.position - toe.position).magnitude;
                    bool ok = eToe <= Tol && ePad <= Tol;
                    pass &= ok;
                    sb.Append($"  {Mark(ok)} ramp drop {d:F2} m: Toe error {eToe:F3} m (at {got:F2}, want {want:F2}); pad contact to Toe {ePad:F3} m\n");
                }
            }

            // Shafts: foot on the ground, plumb, joined to their caps.
            float worstFoot = 0f, worstTilt = 0f, worstJoin = 0f;
            foreach (var s in shafts)
            {
                var top = FindSuffix(s, "__Top");
                var bottom = FindSuffix(s, "__Bottom");
                if (top == null || bottom == null) { worstFoot = float.PositiveInfinity; continue; }
                float g = ground != null ? ground(top.position.x, top.position.z) : 0f;
                float want = Mathf.Max(g, BuildingFactory.PostDeepest) - Embed;
                worstFoot = Mathf.Max(worstFoot, Mathf.Abs(bottom.position.y - want));
                worstTilt = Mathf.Max(worstTilt, Vector3.Angle(bottom.position - top.position, Vector3.down));
                var cap = vis.Find("Cap_" + s.name.Substring("Shaft_".Length));
                var join = FindSuffix(cap, "__Shaft_Top");
                worstJoin = join == null ? float.PositiveInfinity
                    : Mathf.Max(worstJoin, (join.position - top.position).magnitude);
            }
            {
                bool ok = worstFoot <= Tol && worstTilt <= 1f && worstJoin <= Tol;
                pass &= ok;
                sb.Append($"  {Mark(ok)} shafts: worst foot vs ground-{Embed} {worstFoot:F3} m, "
                    + $"worst tilt {worstTilt:F2} deg, worst cap join {worstJoin:F3} m\n");
            }

            // Caps: top about 0.55 m proud of the deck (1.749 - 1.2).
            float capLo = float.MaxValue, capHi = float.MinValue;
            foreach (var c in caps)
            {
                var ct = FindSuffix(c, "__Cap_Top");
                if (ct == null) continue;
                float y = builtRoot.InverseTransformPoint(ct.position).y;
                capLo = Mathf.Min(capLo, y); capHi = Mathf.Max(capHi, y);
            }
            if (caps.Count > 0)
            {
                bool ok = Mathf.Abs(capLo - 0.549f) <= Tol && Mathf.Abs(capHi - 0.549f) <= Tol;
                pass &= ok;
                sb.Append($"  {Mark(ok)} cap tops above deck {capLo:F3}..{capHi:F3} m (want 0.549)\n");
            }

            // Lantern on its marker.
            var src = FindSuffix(last, "__Light_Source");
            if (lantern == null) { sb.Append("  FAIL no PierLantern\n"); pass = false; }
            else
            {
                float e = src != null ? (lantern.position - src.position).magnitude : -1f;
                var l = lantern.GetComponent<Light>();
                bool ok = src == null || e <= Tol;
                pass &= ok;
                string where = src != null ? e.ToString("F3") + " m" : "(no marker)";
                string what = l != null
                    ? "range " + l.range.ToString("F1") + " intensity now " + l.intensity.ToString("F2")
                    : "none (ghost?)";
                sb.Append("  ").Append(Mark(ok)).Append(" lantern vs Light_Source ").Append(where)
                    .Append(", light ").Append(what).Append('\n');
            }

            return sb.Append(pass ? "PASS" : "FAIL").ToString();
        }

        static string Mark(bool ok) => ok ? "ok  " : "FAIL";
    }
}
