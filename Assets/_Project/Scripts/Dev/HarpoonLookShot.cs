using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.Dev
{
    /// **Bow harpoon phase 0 look check (docs/PLAN-harpoon.md).** A dev shot
    /// tool, NOT game code: nothing references it. Play mode, new game
    /// (`SaveGame.Suppressed = true; GameBoot.Skip()`), `RunProbe.ViewPhone()`.
    ///
    /// Puts the imported `Resources/Harpoon/HarpoonMount` on the player's bow
    /// at the CONTRACT spot (BowLow prefab-local (0, 2.11, 6.3) u, identity,
    /// unit world scale), keeps a real salvage cluster (`SeaKit.SalvageCluster`)
    /// riding the wave at a bearing/range off the mount, yaws `Swivel` at it,
    /// lands a `HarpoonBarb` in it and runs a LineRenderer rope from
    /// `Barb_Muzzle` to the barb's `Line_Attach` in the rope-look.json state.
    /// Over the real HUD it draws MOCKS (UI Toolkit, its own elements on the
    /// sheet panel; SeaHud untouched): the hook marker over the crate and the
    /// harpoon button at the boost button's right edge, just above it, with
    /// its label pill reaching left.
    ///
    /// Eval: `return SeaSick.Dev.HarpoonLookShot.Begin(15f, 22f);`, then
    /// `Rope("slack"|"taut"|"strained")`, `Aim(bearing, range)`,
    /// `Shot(path)` (game view + HUD), `Close(path, yaw, dist, height, fov)`
    /// (temp camera, phone portrait, no HUD), `Info()`, `End()`.
    /// Bow lantern on its beam (Kevin 2026-10-04): `Lantern(dir)` = the line
    /// out dead ahead over it, three shots + its clearance to the line;
    /// `Passage(path)` = high view of the foredeck nose the mount blocks.
    public class HarpoonLookShot : MonoBehaviour
    {
        static HarpoonLookShot inst;

        Transform ship, bowVis, mount, swivel, muzzle, barb, lineAttach, crate;
        LineRenderer rope;
        Material ropeMat;
        Camera chase;
        float bearing = 15f, range = 22f;
        string state = "strained";
        RopeLook look;
        VisualElement uiRoot, marker, mock;
        Label pill;
        readonly StringBuilder report = new StringBuilder();

        // --- rope-look.json --------------------------------------------------------

        [System.Serializable] class RopeState
        {
            public float widthM, emissionIntensity, sagFractionOfSpan, pulseHz, pulseMinIntensity, widthJitterM, ambient;
            public string tint, emission;
            public bool restOnWater;
        }
        [System.Serializable] class RopeStates { public RopeState slack, taut, strained; }
        [System.Serializable] class RopeMaterial { public float tilesPerMetre; }
        [System.Serializable] class RopeReadability { public float minScreenWidthPx; }
        [System.Serializable] class RopeLook
        {
            public RopeMaterial material; public RopeStates states; public RopeReadability readability;
        }

        // --- entry points ------------------------------------------------------------

        public static string Begin(float bearingDeg = 15f, float rangeM = 22f)
        {
            if (!Application.isPlaying) return "play mode only";
            End();
            var go = new GameObject("HarpoonLookShot");
            inst = go.AddComponent<HarpoonLookShot>();
            inst.bearing = bearingDeg;
            inst.range = rangeM;
            return inst.Setup();
        }

        public static string Aim(float bearingDeg, float rangeM)
        {
            if (inst == null) return "not running";
            inst.bearing = bearingDeg; inst.range = rangeM;
            inst.LateUpdate();
            return $"aim {bearingDeg:F0} deg {rangeM:F0} m; swivel yaw {inst.SwivelYaw():F1}; " + inst.LineCheck();
        }

        public static string Rope(string s)
        {
            if (inst == null) return "not running";
            inst.state = s;
            inst.LateUpdate();
            return "rope " + s;
        }

        /// The whole game view (HUD included) at its own resolution. The file
        /// lands at the end of the frame, so read it after the next frame.
        public static string Shot(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            return "capturing " + path + $" ({Screen.width}x{Screen.height})";
        }

        /// A temporary camera orbiting the mount (yaw 0 = from dead astern of
        /// it, positive = from starboard), phone portrait 1080x2340, no HUD.
        /// `lookAt01` slides the aim point from the swivel (0) to the crate (1).
        public static string Close(string path, float yawDeg, float dist, float height, float fov = 40f, float lookAt01 = 0f)
        {
            if (inst == null) return "not running";
            var m = inst.mount;
            Vector3 fwd = Vector3.ProjectOnPlane(m.forward, Vector3.up).normalized;
            Vector3 pivot = inst.swivel.position + Vector3.up * 0.9f;
            Vector3 aim = Vector3.Lerp(pivot, inst.crate.position, lookAt01);
            Vector3 eye = pivot + Quaternion.AngleAxis(yawDeg, Vector3.up) * (-fwd) * dist + Vector3.up * height;
            return Render(path, eye, aim, fov);
        }

        /// Same, from an explicit eye and aim.
        public static string Render(string path, Vector3 eye, Vector3 aim, float fov)
        {
            const int W = 1080, H = 2340;
            var go = new GameObject("HarpoonLookCam");
            var cam = go.AddComponent<Camera>();
            if (inst != null && inst.chase != null) cam.CopyFrom(inst.chase);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye, Vector3.up));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.depth = 100;
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.aspect = (float)W / H;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
            return "wrote " + path;
        }

        /// **The bow lantern on its beam** (CoasterOutfitting hangs
        /// `Resources/Harpoon/BowLantern` on the low bow): crate dead ahead at
        /// `rangeM`, taut line, then a starboard side close-up, a high
        /// close-up from ahead-starboard and the game view (HUD), plus where
        /// the lantern hangs and how far the line passes from each of its parts.
        public static string Lantern(string dir, float rangeM = 20f)
        {
            if (inst == null) return "not running";
            inst.bearing = 0f; inst.range = rangeM; inst.state = "taut";
            inst.LateUpdate();
            var sb = new StringBuilder(inst.LanternReport());
            sb.AppendLine(Close(Path.Combine(dir, "lantern_side_deadahead.png"), 90f, 5.5f, 0.3f, 40f, 0.10f));
            sb.AppendLine(Close(Path.Combine(dir, "lantern_close_high.png"), 150f, 4.0f, 2.2f, 45f, 0.05f));
            sb.AppendLine(Shot(Path.Combine(dir, "lantern_game_deadahead.png")));
            return sb.ToString();
        }

        /// **The crew passage at the nose** (e_crew_passage_bow.png): high 3/4
        /// from astern over the foredeck, phone portrait, no HUD, so the mount's
        /// footprint (and the lantern beam's root at the stem) reads against
        /// the deck the hands walk. Reports the nearest crew hand for scale.
        public static string Passage(string path, float height = 6.5f, float back = 3.2f, float fov = 42f)
        {
            if (inst == null) return "not running";
            var m = inst.mount;
            Vector3 fwd = Vector3.ProjectOnPlane(m.forward, Vector3.up).normalized;
            Vector3 aim = m.position + fwd * 0.9f;
            Vector3 eye = m.position - fwd * back + Vector3.up * height;
            string hand = "no crew hand found";
            float best = float.MaxValue;
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (!a.isActiveAndEnabled || !a.transform.IsChildOf(inst.ship)) continue;
                float d = Vector3.Distance(a.transform.position, m.position);
                if (d < best) { best = d; hand = $"nearest animated hand {a.name} {d:F1} m from the mount"; }
            }
            return Render(path, eye, aim, fov) + "; " + hand;
        }

        string LanternReport()
        {
            Transform lantern = null;
            foreach (var t in ship.GetComponentsInChildren<Transform>()) if (t.name == "BowLantern") { lantern = t; break; }
            if (lantern == null) return "no BowLantern on the ship (not imported yet, or a raised bow): the kit's own lantern is up\n";
            var pivot = Find(lantern, "LanternBow_Pivot");
            Vector3 m = mount.InverseTransformPoint(muzzle.position), pv = mount.InverseTransformPoint(pivot.position);
            var sb = new StringBuilder($"BowLantern: pivot {pv.z - m.z:F2} m ahead of and {m.y - pv.y:F2} m below the muzzle (mount frame; art says 1.55 / 0.835), " +
                                       $"swing {(pivot.GetComponent<SeaSick.Ship.LanternSwing>() != null ? "on" : "OFF")}, lights on the pivot {pivot.GetComponentsInChildren<Light>().Length}\n");
            var pts = new Vector3[rope.positionCount];
            rope.GetPositions(pts);
            float half = rope.widthMultiplier * 0.5f;
            sb.Append("line clearance (world AABB, rope radius off):");
            foreach (var r in lantern.GetComponentsInChildren<MeshRenderer>())
            {
                float best = float.MaxValue;
                var b = r.bounds;
                for (int i = 0; i + 1 < pts.Length; i++)
                    for (int k = 0; k <= 40; k++)
                    {
                        var q = Vector3.Lerp(pts[i], pts[i + 1], k / 40f);
                        best = Mathf.Min(best, Vector3.Distance(q, b.ClosestPoint(q)));
                    }
                sb.Append($" {r.name} {best - half:F3} m;");
            }
            return sb.AppendLine().ToString();
        }

        public static string Info() => inst == null ? "not running" : inst.report + "\nnow: " + inst.Now();

        public static string End()
        {
            if (inst == null) return "nothing";
            Destroy(inst.gameObject);
            inst = null;
            return "ended";
        }

        // --- setup -------------------------------------------------------------------

        string Setup()
        {
            report.Clear();
            var helm = FindFirstObjectByType<SeaSick.Ship.HelmInput>();
            if (helm == null) return "no HelmInput (player ship)";
            ship = helm.transform;
            SeaSick.Ship.Modular.ModularShipView view = helm.GetComponentInChildren<SeaSick.Ship.Modular.ModularShipView>();
            if (view == null)
            {
                float best = float.MaxValue;
                foreach (var v in FindObjectsByType<SeaSick.Ship.Modular.ModularShipView>(FindObjectsSortMode.None))
                {
                    float d = (v.transform.position - ship.position).sqrMagnitude;
                    if (d < best) { best = d; view = v; }
                }
            }
            if (view == null || view.Current == null) return "no ModularShipView on the player";
            var cfg = view.Current;
            string stern = cfg.Find("stern")?.moduleId, bowId = cfg.Find("bow")?.moduleId;
            int middles = 0;
            foreach (var p in cfg.placed) if (p.kind == SeaSick.Ship.Modular.ModuleKind.Middle) middles++;
            var baseCfg = SeaSick.Ship.Modular.CoasterFamily.Base();
            bool isBase = stern == baseCfg.sternId && bowId == baseCfg.bowId && middles == 0;
            report.AppendLine($"ship {ship.name}: stern {stern}, bow {bowId}, middles {middles} -> {(isBase ? "CoasterFamily.Base" : "NOT the base coaster")}");

            // The bow module's visual (BowLow, u-space, x starboard / y up / z forward).
            Transform bowHost = null;
            foreach (Transform t in view.transform) if (t.name.StartsWith("bow (")) { bowHost = t; break; }
            if (bowHost == null) return report + "no bow module under " + view.name;
            foreach (Transform t in bowHost) if (t.GetComponentInChildren<MeshRenderer>() != null && t.name != "Connections" && t.name != "BowLantern") { bowVis = t; break; }
            if (bowVis == null) return report + "no bow visual under " + bowHost.name;
            report.AppendLine($"bow visual {bowHost.name}/{bowVis.name}, lossyScale {bowVis.lossyScale.x:F3}, fwd vs ship {Vector3.Angle(bowVis.forward, ship.forward):F1} deg, up vs ship {Vector3.Angle(bowVis.up, ship.up):F1} deg");

            // The mount, CONTRACT placement.
            var mountPrefab = Resources.Load<GameObject>("Harpoon/HarpoonMount");
            var barbPrefab = Resources.Load<GameObject>("Harpoon/HarpoonBarb");
            if (mountPrefab == null || barbPrefab == null) return report + "Resources/Harpoon prefabs missing (run HarpoonImport)";
            mount = Instantiate(mountPrefab, bowVis, false).transform;
            mount.name = "HarpoonMount (look shot)";
            mount.localPosition = new Vector3(0f, 2.11f, 6.3f);
            mount.localRotation = Quaternion.identity;
            mount.localScale = Vector3.one / bowVis.lossyScale.x;
            swivel = Find(mount, "Swivel");
            muzzle = Find(mount, "Barb_Muzzle");
            report.AppendLine($"mount lossyScale {mount.lossyScale.x:F3}, {Vector3.Distance(mount.position, ship.position):F2} m from the ship origin, " +
                              $"ship-local {ship.InverseTransformPoint(mount.position):F2}");
            DeckCheck();

            // The crate: the real salvage cluster art.
            crate = new GameObject("HarpoonLookCrate").transform;
            var art = SeaSick.Ship.SeaLife.SeaKit.Spawn(SeaSick.Ship.SeaLife.SeaKit.SalvageCluster, crate, new Vector3(0f, -0.12f, 0f));
            if (art == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(crate, false);
                report.AppendLine("SalvageCluster art missing: cube stand-in");
            }
            var cb = WorldBounds(crate);
            report.AppendLine($"crate art {(art ? art.name : "cube")} size {cb.size:F2}");

            barb = Instantiate(barbPrefab).transform;
            barb.name = "HarpoonBarb (look shot)";
            lineAttach = Find(barb, "Line_Attach");

            // The rope.
            var ta = Resources.Load<TextAsset>("Harpoon/rope-look");
            look = ta != null ? JsonUtility.FromJson<RopeLook>(ta.text) : null;
            if (look == null || look.states == null || look.states.slack == null) return report + "rope-look.json missing/unreadable";
            var src = Resources.Load<Material>("Harpoon/SS_Harpoon_Rope");
            ropeMat = new Material(src) { name = "SS_Harpoon_Rope (look shot)" };
            rope = new GameObject("HarpoonLookRope").AddComponent<LineRenderer>();
            rope.sharedMaterial = ropeMat;
            rope.positionCount = 21;
            rope.useWorldSpace = true;
            rope.alignment = LineAlignment.View;
            rope.textureMode = LineTextureMode.Tile;
            rope.textureScale = new Vector2(look.material.tilesPerMetre, 1f);
            rope.numCapVertices = 2;
            rope.numCornerVertices = 0;
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rope.receiveShadows = false;
            // Normals for the toon shader: without them a line sits in its darkest cel band (the dull olive taut).
            rope.generateLightingData = true;

            foreach (var c in FindObjectsByType<SeaSick.CameraRig.ChaseCamera>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled) { chase = c.GetComponent<Camera>(); break; }
            report.AppendLine("chase camera " + (chase ? chase.name : "MISSING"));

            BuildUi();
            LateUpdate();
            report.AppendLine(LineCheck());
            return report.ToString();
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            throw new System.Exception("no " + name + " under " + root.name);
        }

        static Bounds WorldBounds(Transform root)
        {
            var b = new Bounds(root.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return b;
        }

        /// The base's underside against the foredeck floor under its
        /// footprint, and every hull vertex inside the mount's solid boxes
        /// (the plinth/step box and the post column) at the current yaw.
        void DeckCheck()
        {
            var sb = new StringBuilder("deck: ");
            float floorMin = float.MaxValue, floorMax = float.MinValue;
            int inBase = 0, inPost = 0;
            string inBaseWho = "";
            foreach (var mf in bowVis.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.transform.IsChildOf(mount)) continue;
                bool floor = mf.name.Contains("Foredeck_Floor");
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = mount.InverseTransformPoint(mf.transform.TransformPoint(v));
                    bool foot = Mathf.Abs(p.x) <= 0.9f && p.z >= -1.06f && p.z <= 0.8f;
                    if (floor && foot && Mathf.Abs(p.y) < 0.5f) { floorMin = Mathf.Min(floorMin, p.y); floorMax = Mathf.Max(floorMax, p.y); }
                    if (!floor && foot && p.y > 0.01f && p.y < 0.29f) { inBase++; if (!inBaseWho.Contains(mf.name)) inBaseWho += mf.name + " "; }
                    if (!floor && Mathf.Abs(p.x) < 0.35f && Mathf.Abs(p.z) < 0.35f && p.y > 0.3f && p.y < 1.9f) inPost++;
                }
            }
            sb.Append(floorMax > float.MinValue
                ? $"Foredeck_Floor vertices under the footprint at y {floorMin:F3}..{floorMax:F3} m (mount origin = 0)"
                : "no Foredeck_Floor vertices under the footprint");
            sb.Append($"; hull vertices inside the base box {inBase} {inBaseWho}; inside the post column {inPost}");
            report.AppendLine(sb.ToString());
        }

        float SwivelYaw()
        {
            var a = swivel.localEulerAngles.y;
            return a > 180f ? a - 360f : a;
        }

        /// Which hull renderers the line passes through (their world bounds,
        /// then a segment-vs-mesh-AABB test in each renderer's own frame).
        string LineCheck()
        {
            if (rope == null) return "";
            var hits = new StringBuilder();
            var pts = new Vector3[rope.positionCount];
            rope.GetPositions(pts);
            foreach (var mf in ship.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.transform.IsChildOf(mount)) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    var a = mf.transform.InverseTransformPoint(pts[i]);
                    var b = mf.transform.InverseTransformPoint(pts[i + 1]);
                    var ray = new Ray(a, b - a);
                    if (mb.IntersectRay(ray, out float d) && d <= (b - a).magnitude) { hits.Append(mf.name + " "); break; }
                }
            }
            return "line passes through (mesh boxes): " + (hits.Length > 0 ? hits.ToString() : "nothing");
        }

        string Now()
        {
            if (crate == null) return "";
            float dist = Vector3.Distance(muzzle.position, crate.position);
            return $"bearing {bearing:F0} range {range:F0}, swivel yaw {SwivelYaw():F1}, muzzle->crate {dist:F1} m, rope {state}, " +
                   $"boost {SeaSick.UI.Sheets.SeaHud.BoostRect}, screen {Screen.width}x{Screen.height}, mock {(mock != null ? mock.worldBound.ToString() : "-")}, " +
                   $"marker {(marker != null ? marker.worldBound.ToString() : "-")}, panelScale {SeaSick.UI.Sheets.SheetHost.PanelScale:F3}";
        }

        // --- per frame -----------------------------------------------------------------

        void LateUpdate()
        {
            if (mount == null || crate == null) return;
            // The crate rides the wave at bearing/range off the mount.
            Vector3 fwd = Vector3.ProjectOnPlane(mount.forward, Vector3.up).normalized;
            Vector3 p = mount.position + Quaternion.AngleAxis(bearing, Vector3.up) * fwd * range;
            p.y = SeaSick.Ocean.OceanSampler.Ready ? SeaSick.Ocean.OceanSampler.SampleImmediate(p).height : 0f;
            crate.SetPositionAndRotation(p, Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 35f, 0f));

            // Swivel at the crate (mount-local yaw, the CONTRACT's +/-45).
            var local = mount.InverseTransformPoint(crate.position);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -45f, 45f);
            swivel.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // The barb bitten into the crate, along the line of flight.
            var cb = WorldBounds(crate);
            Vector3 bite = new Vector3(crate.position.x, cb.max.y - 0.15f, crate.position.z);
            Vector3 dir = (bite - muzzle.position).normalized;
            barb.rotation = Quaternion.LookRotation(dir, Vector3.up);
            barb.position = bite - dir * 0.30f;   // tip 0.25 m into the cluster

            TickRope();
            TickUi();
        }

        void TickRope()
        {
            var s = state == "slack" ? look.states.slack : state == "taut" ? look.states.taut : look.states.strained;
            Vector3 a = muzzle.position, b = lineAttach.position;
            float span = Vector3.Distance(a, b);
            float sag = s.sagFractionOfSpan * span;
            int n = rope.positionCount;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                var q = Vector3.Lerp(a, b, t) - Vector3.up * (4f * sag * t * (1f - t));
                if (s.restOnWater && SeaSick.Ocean.OceanSampler.Ready)
                {
                    float w = SeaSick.Ocean.OceanSampler.SampleImmediate(q).height + 0.03f;
                    if (q.y < w) q.y = w;
                }
                rope.SetPosition(i, q);
            }

            float width = s.widthM;
            if (s.widthJitterM > 0f) width += Mathf.Sin(Time.time * 37f) * s.widthJitterM;
            // Readability floor: never under minScreenWidthPx on the sea camera.
            if (chase != null && look.readability != null)
            {
                float d = Vector3.Distance(chase.transform.position, Vector3.Lerp(a, b, 0.5f));
                float pxPerM = Screen.height / (2f * d * Mathf.Tan(chase.fieldOfView * 0.5f * Mathf.Deg2Rad));
                width = Mathf.Max(width, look.readability.minScreenWidthPx / Mathf.Max(1e-3f, pxPerM));
            }
            rope.widthMultiplier = width;
            var tint = Hex(s.tint);
            rope.startColor = rope.endColor = tint;
            // The toon rope shader's only emission is its ambient floor
            // (rope-look.json `ambient`); the strained glow pulses it at pulseHz.
            float glow = s.ambient;
            if (s.emissionIntensity > 0f)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * s.pulseHz * 2f * Mathf.PI);
                glow = Mathf.Lerp(s.pulseMinIntensity, s.emissionIntensity, k) / Mathf.Max(1e-3f, s.emissionIntensity);
            }
            if (ropeMat.HasProperty("_Ambient")) ropeMat.SetFloat("_Ambient", glow);
        }

        static Color Hex(string h)
        {
            return ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.white;
        }

        // --- the HUD mocks -------------------------------------------------------------

        static readonly Color Pearl = new Color32(232, 242, 246, 255);
        static readonly Color Amber = new Color32(242, 196, 109, 255);
        static readonly Color Navy = new Color32(22, 41, 58, 235);

        void BuildUi()
        {
            var host = SeaSick.UI.Sheets.SheetHost.Instance;
            var doc = host != null ? host.GetComponent<UIDocument>() : null;
            if (doc == null) { report.AppendLine("no SheetHost UIDocument: no HUD mocks"); return; }
            uiRoot = doc.rootVisualElement;
            var style = Resources.Load<StyleSheet>("UI/SeaHud");

            // The harpoon button + its label pill, one right-anchored row.
            mock = new VisualElement { name = "HarpoonMock", pickingMode = PickingMode.Ignore };
            if (style != null) mock.styleSheets.Add(style);
            mock.style.position = Position.Absolute;
            mock.style.flexDirection = FlexDirection.Row;
            mock.style.alignItems = Align.Center;
            mock.style.transformOrigin = new TransformOrigin(Length.Percent(100), Length.Percent(100), 0f);
            pill = new Label("crate · 22 m");
            pill.AddToClassList("sea-chip");
            pill.AddToClassList("sea-chip--more");
            pill.style.maxWidth = 170f;
            pill.style.marginRight = 8f;
            pill.style.whiteSpace = WhiteSpace.Normal;
            pill.style.borderTopColor = pill.style.borderBottomColor = pill.style.borderLeftColor = pill.style.borderRightColor = (Color)new Color32(201, 147, 57, 255);
            pill.style.color = Pearl;
            mock.Add(pill);
            var button = new VisualElement();
            button.AddToClassList("sea-boost");
            button.style.position = Position.Relative;
            button.style.left = StyleKeyword.Auto;
            button.style.bottom = StyleKeyword.Auto;
            button.style.flexShrink = 0f;
            button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = Amber;
            var icon = new HookGlyph(Pearl, false) { pickingMode = PickingMode.Ignore };
            icon.style.width = 28f; icon.style.height = 28f;
            button.Add(icon);
            mock.Add(button);
            uiRoot.Add(mock);

            // The world marker over the crate: hook in a ring, highlighted.
            marker = new HookGlyph(Pearl, true) { name = "HarpoonMarkerMock", pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = 44f; marker.style.height = 44f;
            marker.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0f);
            uiRoot.Add(marker);
        }

        void TickUi()
        {
            if (uiRoot == null) return;
            float s = Mathf.Max(1e-4f, SeaSick.UI.Sheets.SheetHost.PanelScale);
            var boost = SeaSick.UI.Sheets.SeaHud.BoostRect;
            bool on = boost.width > 1f;
            mock.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            float ppd = on ? boost.width / SeaSick.UI.Sheets.SeaHud.BoostSize : Screen.width / 390f;
            float k = ppd * s;
            if (on)
            {
                float rootW = uiRoot.resolvedStyle.width, rootH = uiRoot.resolvedStyle.height;
                mock.style.right = rootW - boost.xMax * s;
                mock.style.bottom = rootH - (boost.yMin - SeaSick.UI.Sheets.SeaHud.BoostGap * ppd) * s;
                mock.style.scale = new Scale(new Vector3(k, k, 1f));
                pill.text = "crate · " + Mathf.RoundToInt(Vector3.Distance(muzzle.position, crate.position)) + " m";
            }

            // Marker: 1.6 m over the crate on the sea camera.
            bool seen = false;
            if (chase != null)
            {
                var sp = chase.WorldToScreenPoint(crate.position + Vector3.up * 1.6f);
                if (sp.z > 0f)
                {
                    seen = true;
                    float size = 44f;
                    marker.style.left = sp.x * s - size * 0.5f;
                    marker.style.top = (Screen.height - sp.y) * s - size;
                    marker.style.scale = new Scale(new Vector3(k, k, 1f));
                }
            }
            marker.style.display = seen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OnDestroy()
        {
            if (mount != null) Destroy(mount.gameObject);
            if (barb != null) Destroy(barb.gameObject);
            if (crate != null) Destroy(crate.gameObject);
            if (rope != null) Destroy(rope.gameObject);
            if (ropeMat != null) Destroy(ropeMat);
            mock?.RemoveFromHierarchy();
            marker?.RemoveFromHierarchy();
        }

        /// A painted hook (never a font glyph: emoji are blank boxes on the
        /// phone): eye, shank, round bend, point with a barb. `ringed` adds
        /// the world marker's highlighted ring and dark disc.
        sealed class HookGlyph : VisualElement
        {
            readonly Color ink;
            readonly bool ringed;

            public HookGlyph(Color ink, bool ringed)
            {
                this.ink = ink;
                this.ringed = ringed;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                float w = r.width, h = r.height;
                if (w < 1f || h < 1f) return;
                var p = ctx.painter2D;
                Vector2 o = Vector2.zero;
                float u = w;  // the hook box
                if (ringed)
                {
                    var c = new Vector2(w * .5f, h * .5f);
                    p.fillColor = new Color(0.04f, 0.09f, 0.13f, 0.78f);
                    p.BeginPath(); p.Arc(c, w * .5f - 2f, 0f, 360f); p.Fill();
                    p.strokeColor = Amber; p.lineWidth = 3f;
                    p.BeginPath(); p.Arc(c, w * .5f - 2f, 0f, 360f); p.Stroke();
                    u = w * 0.6f;
                    o = c - new Vector2(u, u) * .5f;
                }
                Vector2 P(float x, float y) => o + new Vector2(x * u, y * u);
                p.strokeColor = ink;
                p.lineWidth = Mathf.Max(2f, u * 0.11f);
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                // eye
                p.BeginPath(); p.Arc(P(.60f, .13f), u * .085f, 0f, 360f); p.Stroke();
                // shank down into the bend, round to the point
                p.BeginPath();
                p.MoveTo(P(.60f, .22f));
                p.LineTo(P(.60f, .62f));
                p.Arc(P(.40f, .62f), u * .20f, 0f, 180f, ArcDirection.Clockwise);
                p.LineTo(P(.20f, .46f));
                p.Stroke();
                // the barb
                p.BeginPath();
                p.MoveTo(P(.20f, .44f));
                p.LineTo(P(.32f, .56f));
                p.Stroke();
            }
        }
    }
}
