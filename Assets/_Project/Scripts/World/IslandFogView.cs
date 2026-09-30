using System.Collections.Generic;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.World
{
    /// **The cloud over unexplored islands (Kevin, 2026-09-30).** Draws
    /// `IslandFog` in the world: one soft white cloud mesh per unsettled
    /// island near the camera, draped a canopy above the ground and sloping
    /// down to the water at the coast, so from the deck or from the island
    /// camera the land under it is dimmed and greyed and its trees and props
    /// are faint shapes at most. Alpha is the island's fog grid as an RG16 (R fog, G ground)
    /// texture (`SeaSick/Island Fog`); opened cells fade out over ~1 s.
    /// Nothing on a settled island (camp or any building); when a camp is
    /// made, the whole cover fades away and is freed.
    ///
    /// Also opens the landing strip: on the ship's anchoring at an island
    /// (`AnchorController` goes Anchored/Ashore) it calls
    /// `IslandFog.RevealShoreNear` there. Idempotent, so a landing sheet
    /// calling it too costs nothing.
    ///
    /// **Per frame:** a float compare, then for each cover with cells still
    /// fading, a byte step over the box of changed cells and one small
    /// texture upload. No allocations. The scan for islands in range runs
    /// twice a second and builds at most one cover a scan (the grid's
    /// height samples are the only real cost, once per island per world).
    /// Draw cost: one transparent draw per fogged island in view.
    public class IslandFogView : MonoBehaviour
    {
        /// Covers are built and drawn for islands this close to the camera.
        const float ViewRange = 1100f;
        const float PollSeconds = 0.5f;
        /// Seconds for a cell to open, and for a settled island's cover to go.
        const float FadeSeconds = 1f;
        /// The cloud's height over the highest ground near it: above a 13 m
        /// canopy. 21 since 2026-09-30: the tallest palms (~18 m) poked
        /// green fronds out through a 15 m sheet.
        const float Lift = 21f;
        /// Where the coast skirt meets the water.
        const float SkirtY = 1.2f;
        /// The mesh's vertex spacing, in fog cells.
        const int Step = 2;
        /// The cover's strength while a building is being sited on this
        /// island (Make camp on a fresh island): a veil, so the ground reads.
        const float PlacingVeil = 0.3f;

        static IslandFogView instance;
        static Shader shader;
        static bool shaderMissing;

        sealed class Cover
        {
            public Island island;
            public IslandFog fog;
            public GameObject go;
            public MeshRenderer renderer;
            public Material material;
            public Texture2D texture;
            public Mesh mesh;
            /// Texels, `stride` bytes each: R = the cloud shown (eased),
            /// G = the ground height (`GroundByte`, 0 = sea). 2026-09-30:
            /// the shader tells a tree behind the cloud from the ground by G.
            public byte[] shown;
            public int stride = 2;
            public int ax0, ay0, ax1 = -1, ay1 = -1;   // box still fading
            public float fade = 1f;
            public bool leaving;
            public int version;
        }

        readonly List<Cover> covers = new List<Cover>();
        readonly Dictionary<Island, Cover> byIsland = new Dictionary<Island, Cover>();
        float nextPoll;
        AnchorController anchor;
        float nextAnchorLook;
        AnchorController.State lastState = AnchorController.State.Underway;
        Island lastAnchorIsland;
        static readonly int FogTexId = Shader.PropertyToID("_FogTex");
        static readonly int FogRectId = Shader.PropertyToID("_FogRect");
        static readonly int FogTexelId = Shader.PropertyToID("_FogTexel");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        /// The one view, made on first ask. Lives across scene loads; its
        /// covers die with their islands.
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("IslandFogView");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<IslandFogView>();
        }

        void OnDestroy()
        {
            foreach (var c in covers) Free(c);
            covers.Clear();
            byIsland.Clear();
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollSeconds;
                WatchAnchor();
                Poll();
            }

            float dt = Time.unscaledDeltaTime;
            int step = Mathf.Max(1, Mathf.RoundToInt(dt / FadeSeconds * 255f));
            for (int i = covers.Count - 1; i >= 0; i--)
            {
                var c = covers[i];
                if (c.island == null) { Free(c); covers.RemoveAt(i); continue; }
                if (c.leaving)
                {
                    c.fade -= dt / FadeSeconds;
                    if (c.fade <= 0f || c.material == null)
                    {
                        Free(c);
                        byIsland.Remove(c.island);
                        covers.RemoveAt(i);
                        continue;
                    }
                    c.material.SetFloat(FadeId, c.fade);
                    continue;
                }
                if (c.texture == null) continue;   // an island with no land to cover

                // Siting a camp (or anything) on this island: the ground has
                // to be seen to be chosen, so the cloud thins to a veil.
                float want = SeaSick.UI.Sheets.ThumbBar.PlacementActive && c.island == lastAnchorIsland
                    ? PlacingVeil : 1f;
                if (c.fade != want)
                {
                    c.fade = Mathf.MoveTowards(c.fade, want, dt / FadeSeconds);
                    c.material.SetFloat(FadeId, c.fade);
                }

                if (c.fog.Version != c.version)
                {
                    c.version = c.fog.Version;
                    if (c.fog.TakeDirty(out int x0, out int y0, out int x1, out int y1))
                    {
                        if (c.ax1 < c.ax0) { c.ax0 = x0; c.ay0 = y0; c.ax1 = x1; c.ay1 = y1; }
                        else
                        {
                            c.ax0 = Mathf.Min(c.ax0, x0); c.ay0 = Mathf.Min(c.ay0, y0);
                            c.ax1 = Mathf.Max(c.ax1, x1); c.ay1 = Mathf.Max(c.ay1, y1);
                        }
                    }
                }
                if (c.ax1 >= c.ax0) Animate(c, step);
            }
        }

        /// Step every fading cell in the box toward its target; upload once.
        static void Animate(Cover c, int step)
        {
            var fog = c.fog;
            int w = fog.Width;
            bool moved = false;
            for (int y = c.ay0; y <= c.ay1; y++)
                for (int x = c.ax0; x <= c.ax1; x++)
                {
                    int k = y * w + x;
                    int target = fog.FogAt(k) ? 255 : 0;
                    int cur = c.shown[k * c.stride];
                    if (cur == target) continue;
                    cur = cur < target ? Mathf.Min(target, cur + step) : Mathf.Max(target, cur - step);
                    c.shown[k * c.stride] = (byte)cur;
                    moved = true;
                }
            if (moved)
            {
                c.texture.SetPixelData(c.shown, 0);
                c.texture.Apply(false, false);
            }
            else { c.ax0 = c.ay0 = 0; c.ax1 = c.ay1 = -1; }
        }

        /// Anchoring opens the strip of shore the crew will step onto.
        void WatchAnchor()
        {
            if (anchor == null)
            {
                if (Time.unscaledTime < nextAnchorLook) return;
                nextAnchorLook = Time.unscaledTime + 3f;
                anchor = FindFirstObjectByType<AnchorController>();
                if (anchor == null) return;
            }
            var state = anchor.CurrentState;
            var isle = anchor.CurrentIsland;
            bool at = state == AnchorController.State.Anchored || state == AnchorController.State.Ashore;
            bool wasAt = lastState == AnchorController.State.Anchored || lastState == AnchorController.State.Ashore;
            if (at && isle != null && (!wasAt || isle != lastAnchorIsland) && !IslandFog.Settled(isle))
                IslandFog.For(isle)?.RevealShoreNear(anchor.transform.position);
            lastState = state;
            lastAnchorIsland = at ? isle : null;
        }

        /// Twice a second: build the nearest missing cover (one a scan),
        /// hide far ones, let settled islands' covers fade away.
        void Poll()
        {
            if (shaderMissing) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;

            Island build = null;
            float buildGap = float.MaxValue;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isle = all[i];
                if (isle == null) continue;
                byIsland.TryGetValue(isle, out var c);
                if (c != null && c.fog != IslandFog.Existing(isle))
                {
                    // The world was rebuilt under the same island object.
                    Free(c); covers.Remove(c); byIsland.Remove(isle); c = null;
                }
                bool settled = IslandFog.Settled(isle);
                if (c != null)
                {
                    if (settled) { c.leaving = true; continue; }
                    if (c.renderer != null)
                    {
                        float g = Island.FlatDistance(eye, isle.transform.position) - isle.MaxRadius;
                        c.renderer.enabled = g < ViewRange * 1.2f;
                    }
                    continue;
                }
                if (settled) continue;
                float gap = Island.FlatDistance(eye, isle.transform.position) - isle.MaxRadius;
                if (gap < ViewRange && gap < buildGap) { buildGap = gap; build = isle; }
            }
            if (build != null) Make(build);
        }

        void Make(Island isle)
        {
            if (shader == null)
            {
                shader = Shader.Find("SeaSick/Island Fog");
                if (shader == null)
                {
                    shaderMissing = true;
                    Debug.LogError("[IslandFogView] SeaSick/Island Fog shader missing (Resources/Shaders/Keepalive/Keep_IslandFog.mat keeps it in a build)");
                    return;
                }
            }
            var fog = IslandFog.For(isle);
            var c = new Cover { island = isle, fog = fog, version = fog.Version };
            fog.TakeDirty(out _, out _, out _, out _);   // everything open now is drawn open, no fade
            covers.Add(c);
            byIsland[isle] = c;
            if (fog.LandCells == 0) return;   // nothing to cover; an empty entry stops a rebuild

            int w = fog.Width, h = fog.Height, n = w * h;
            // RG16 everywhere that matters (Metal, desktop); RGBA32 if not.
            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RG16) ? TextureFormat.RG16 : TextureFormat.RGBA32;
            c.stride = format == TextureFormat.RG16 ? 2 : 4;
            c.shown = new byte[n * c.stride];
            for (int k = 0; k < n; k++)
            {
                c.shown[k * c.stride] = fog.FogAt(k) ? (byte)255 : (byte)0;
                c.shown[k * c.stride + 1] = GroundByte(fog, k);
            }
            c.texture = new Texture2D(w, h, format, false, true)
            {
                name = "IslandFog_" + isle.name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            c.texture.SetPixelData(c.shown, 0);
            c.texture.Apply(false, false);

            c.mesh = BuildMesh(fog);
            c.material = new Material(shader) { name = "IslandFog_" + isle.name };
            c.material.SetTexture(FogTexId, c.texture);
            c.material.SetVector(FogRectId, new Vector4(fog.Origin.x, fog.Origin.y,
                1f / (w * fog.Cell), 1f / (h * fog.Cell)));
            c.material.SetVector(FogTexelId, new Vector4(1f / w, 1f / h, 0f, 0f));
            c.material.SetFloat(FadeId, 1f);

            c.go = new GameObject("IslandFog_" + isle.name);
            c.go.transform.SetParent(transform, false);
            c.go.AddComponent<MeshFilter>().sharedMesh = c.mesh;
            var r = c.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = c.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            c.renderer = r;
        }

        /// The ground height in G: half-metres, 1..255 (0.5..127.5 m) on
        /// land, 0 on the sea. The shader reads it back as g * 127.5.
        static byte GroundByte(IslandFog fog, int k)
        {
            if (!fog.LandAt(k)) return 0;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(fog.GroundAt(k) * 2f), 1, 255);
        }

        /// A sheet over the covered cells, in world space: every `Step`
        /// cells a vertex at `Lift` over the highest land near it, or down at
        /// the water where no land is near (the skirt the sea sees).
        static Mesh BuildMesh(IslandFog fog)
        {
            int w = fog.Width, h = fog.Height;
            int vw = w / Step + 2, vh = h / Step + 2;
            var verts = new Vector3[vw * vh];
            for (int vy = 0; vy < vh; vy++)
                for (int vx = 0; vx < vw; vx++)
                {
                    int cx = vx * Step, cy = vy * Step;   // the corner's cell
                    float top = 0f; bool land = false;
                    for (int y = cy - 3; y <= cy + 2; y++)
                    {
                        if (y < 0 || y >= h) continue;
                        for (int x = cx - 3; x <= cx + 2; x++)
                        {
                            if (x < 0 || x >= w) continue;
                            int k = y * w + x;
                            if (!fog.LandAt(k)) continue;
                            land = true;
                            top = Mathf.Max(top, fog.GroundAt(k));
                        }
                    }
                    verts[vy * vw + vx] = new Vector3(
                        fog.Origin.x + cx * fog.Cell,
                        land ? top + Lift : SkirtY,
                        fog.Origin.y + cy * fog.Cell);
                }

            var tris = new List<int>(vw * vh * 6);
            for (int vy = 0; vy < vh - 1; vy++)
                for (int vx = 0; vx < vw - 1; vx++)
                {
                    // The quad's cells, one ring wider: any under cloud?
                    bool cover = false;
                    for (int y = vy * Step - 1; y <= vy * Step + Step && !cover; y++)
                    {
                        if (y < 0 || y >= h) continue;
                        for (int x = vx * Step - 1; x <= vx * Step + Step; x++)
                        {
                            if (x < 0 || x >= w) continue;
                            if (fog.CoverAt(y * w + x)) { cover = true; break; }
                        }
                    }
                    if (!cover) continue;
                    int a = vy * vw + vx, b = a + 1, d = a + vw, e = d + 1;
                    tris.Add(a); tris.Add(d); tris.Add(e);
                    tris.Add(a); tris.Add(e); tris.Add(b);
                }

            var mesh = new Mesh { name = "IslandFog" };
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Free(Cover c)
        {
            if (c == null) return;
            if (c.go != null) Destroy(c.go);
            if (c.material != null) Destroy(c.material);
            if (c.texture != null) Destroy(c.texture);
            if (c.mesh != null) Destroy(c.mesh);
            c.go = null; c.material = null; c.texture = null; c.mesh = null; c.renderer = null;
        }
    }
}
