using System;
using SeaSick.Ship.Modular;
using UnityEngine;

namespace SeaSick.UI.ModularYard
{
    // Owns only off-world preview objects. Never accesses the live ship/camera.
    public sealed class ShipyardPreview : IDisposable
    {
        readonly GameObject root;
        ModularShipView view;
        readonly Func<ShipConfiguration, Transform, GameObject> factory;
        readonly Camera camera;
        readonly GameObject outline;
        readonly Material lineMaterial;
        readonly LineRenderer[] lines = new LineRenderer[12];
        RenderTexture texture;
        Bounds bounds;          // root-LOCAL (the ship's own frame), see Render
        Bounds highlightBounds; // root-LOCAL
        bool hasHighlight;
        bool focusHighlight;
        // yaw is RELATIVE to the ship's heading (root's yaw), not world:
        // 0 = looking along the bow from astern, +90 = from port... so the
        // same numbers frame the same shot at any slip, any island.
        float yaw = DefaultYaw, pitch = DefaultPitch, zoom = 1f;
        const float DefaultYaw = 40f, DefaultPitch = 32f;
        bool staged;        // root is in the world (dry dock), terrain can occlude
        bool userMoved;     // the player orbited since the last auto-framing
        bool viewChosen;
        public Texture Texture => texture;
        public string Error { get; private set; }
        public event Action TextureChanged;

        /// **Section sheet framing** (docs/SHIPYARD-SECTIONS-UI.md "the
        /// preview highlights that section ... and frames it"): true trains
        /// the camera on the last highlighted part's own bounds instead of
        /// the whole ship's; false (the overview) frames the whole ship as
        /// before. A no-op while nothing is highlighted -- `Build` sets
        /// `hasHighlight` only when `highlight` actually matched a placed
        /// part, so opening a sheet before the first render settles never
        /// frames an empty box.
        public void SetFocus(bool focus) { if (focus == focusHighlight) return; focusHighlight = focus; Render(); }

        public ShipyardPreview(Func<ShipConfiguration, Transform, GameObject> factory = null)
        {
            this.factory = factory;
            root = new GameObject("Shipyard preview (isolated)") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0, 0, -1000);
            var ship = new GameObject("Draft vessel"); ship.transform.SetParent(root.transform, false);
            view = ship.AddComponent<ModularShipView>();
            var cam = new GameObject("Preview camera"); cam.transform.SetParent(root.transform, false);
            camera = cam.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(71, 100, 113, 255);
            camera.nearClipPlane = .1f; camera.farClipPlane = 180f;
            outline = new GameObject("Selected section"); outline.transform.SetParent(root.transform, false);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("No unlit preview highlight shader is available.");
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetColor("_BaseColor", new Color32(151, 227, 250, 255));
            lineMaterial.color = new Color32(151, 227, 250, 255);
            for (int i = 0; i < lines.Length; i++)
            {
                var go = new GameObject("Edge"); go.transform.SetParent(outline.transform, false);
                var line = go.AddComponent<LineRenderer>(); lines[i] = line;
                line.useWorldSpace = false; line.positionCount = 2; line.widthMultiplier = .025f;
                line.sharedMaterial = lineMaterial; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Resize(600, 700);
        }

        /// **Stage the preview IN the world dry dock when there is one**
        /// (2026-09-26). `root` was always parked off-world at `(0,0,-1000)`
        /// with the camera's culling mask limited to layer 31, so nothing
        /// else could ever show through -- fine for an isolated ship on a
        /// flat colour, wrong for "sitting in the dry dock on her keel
        /// pads". When `DryDockSlip.HomeSlip` exists this moves `root` onto
        /// its `PreviewAnchor` (a real world transform) and widens the
        /// culling mask so the dock and the island around it render too;
        /// with no dry dock it falls back to exactly the old off-world
        /// stage. Cheap to call every `Build` -- it only moves a transform
        /// and flips one int, and the dry dock does not appear or vanish
        /// mid-session.
        void StageAt(SeaSick.World.DryDockSlip slip, float bowReachM = 0f)
        {
            if (slip != null)
            {
                var anchor = slip.PreviewAnchorFor(bowReachM);
                if (!staged || (root.transform.position - anchor.position).sqrMagnitude > .01f) viewChosen = false;
                root.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                camera.cullingMask = ~0;
                staged = true;
            }
            else
            {
                if (staged) viewChosen = false;
                root.transform.position = new Vector3(0, 0, -1000);
                root.transform.rotation = Quaternion.identity;
                camera.cullingMask = 1 << 31;
                staged = false;
            }
        }

        public void Build(AssemblyResult result, string highlight, ShipConfiguration configuration = null)
        {
            lastResult = result;
            StageAt(SeaSick.World.DryDockSlip.HomeSlip);
            if (factory != null)
            {
                if (view != null) { view.gameObject.SetActive(false); Delete(view.gameObject); }
                var ship = factory(configuration, root.transform);
                view = ship != null ? ship.GetComponent<ModularShipView>() : null;
                if (view == null)
                {
                    Error = "The ship preview is unavailable."; outline.SetActive(false);
                    bounds = new Bounds();
                    if (texture != null) camera.Render();
                    return;
                }
            }
            else
            {
                foreach (Transform child in view.transform) child.gameObject.SetActive(false);
                view.Build(result);
            }
            Error = view.missingParts.Count > 0 ? "Some ship meshes could not be loaded." : null;
            // Re-stage now the ship's real length is known: the dry dock
            // slides a long ship sea-ward so her bow stops short of the Head
            // gantry (DryDockSlip.PreviewAnchorFor). Bow = root-local +Z.
            var slipNow = SeaSick.World.DryDockSlip.HomeSlip;
            if (slipNow != null) StageAt(slipNow, BowReach());
            // Bounds in root-LOCAL space: a world AABB of a ship on a slip
            // at 45 deg is ~40% too big and shrank her in the frame.
            bool first = true;
            var toRoot = root.transform.worldToLocalMatrix;
            foreach (var r in view.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var lb = r.localBounds; var m = toRoot * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, Corner(i)));
                    if (first) { bounds = new Bounds(c, Vector3.zero); first = false; } else bounds.Encapsulate(c);
                }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            outline.SetActive(false);
            hasHighlight = false;
            foreach (var p in result.placed)
            {
                if (p.instanceKey != highlight) continue;
                ModularScale.AuthoringBoxToGame(p.boundsMinU, p.boundsMaxU, result.metresPerUnit, out var min, out var max);
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++) corners[i] = p.positionM + p.rotation * new Vector3(
                    (i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                int edge = 0;
                for (int i = 0; i < 8; i++) for (int bit = 1; bit <= 4; bit <<= 1)
                    if ((i & bit) == 0) { lines[edge].SetPosition(0, corners[i]); lines[edge++].SetPosition(1, corners[i | bit]); }
                outline.SetActive(true);
                var hb = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 8; i++) hb.Encapsulate(corners[i]);
                // `corners` are in the ship's own LOCAL frame (`p.positionM`
                // is authoring-space, matching the preview ship's zero local
                // offset under `root`) -- correct as-is for the LineRenderer,
                // whose positions are local to `outline` (itself parented at
                // `root` with no offset of its own). `highlightBounds` is
                // used by `Render()` to point the CAMERA, though, and that
                // has to be WORLD space to compare against `bounds` (built
                // from `Renderer.bounds`, already world) -- without this the
                // camera framed a point ~1000 m from the ship (`root` sits
                // at z=-1000 off-world) and the section sheet's preview
                // rendered nothing (2026-09-25 review, empty "SECTION"
                // viewport).
                // Every corner through root's full transform: since the dry
                // dock stages `root` ROTATED (bow to the head end) and, from
                // 2026-09-26, slid along the slip, adding only its position
                // framed the camera on open water beside the ship.
                // (2026-09-27: `Render` now frames in root-LOCAL space and
                // transforms itself, so the local box is exactly right.)
                highlightBounds = hb;
                hasHighlight = true;
                break;
            }
            if (!viewChosen || !userMoved) ChooseView();
            Render();
        }

        AssemblyResult lastResult;

        static Vector3 Corner(int i) => new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);

        /// Where a gun slot sits along its own section, 0 = aft end .. 1 =
        /// fore end, for the Interior cutaway's markers. Both numbers come
        /// from the assembly frame the preview was built from: the slot's
        /// raw position and the section's authored length (bounds U.x, the
        /// same span `RaisedDeckPhysics.FindRaisedSectionRanges` uses), so
        /// the view offset the equipment views add cancels out.
        public bool TrySlotAlong(string sectionKey, string slotId, out float along)
        {
            along = 0.5f;
            var r = lastResult;
            if (r == null || r.placed == null || r.slots == null) return false;
            float? rawZ = null;
            foreach (var sl in r.slots) if (sl.qualifiedId == slotId) { rawZ = sl.positionM.z; break; }
            if (rawZ == null) return false;
            foreach (var p in r.placed)
            {
                if (p.instanceKey != sectionKey) continue;
                float a = p.positionM.z + p.boundsMinU.x * r.metresPerUnit;
                float b = p.positionM.z + p.boundsMaxU.x * r.metresPerUnit;
                if (b < a) (a, b) = (b, a);
                if (b - a < 0.01f) return false;
                along = Mathf.Clamp01((rawZ.Value - a) / (b - a));
                return true;
            }
            return false;
        }

        /// How far ahead of the ship's own origin her bow tip reaches
        /// (root-local +Z, from every mesh's local bounds), for staging her
        /// in the dry dock.
        float BowReach()
        {
            if (view == null) return 0f;
            float reach = 0f; bool any = false;
            var inv = root.transform.worldToLocalMatrix;
            foreach (var r in view.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var lb = r.localBounds; var m = inv * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = m.MultiplyPoint3x4(c).z;
                    if (!any || z > reach) { reach = z; any = true; }
                }
            }
            return any ? reach : 0f;
        }

        public void Resize(int width, int height)
        {
            float factor = Mathf.Min(1f, 1024f / Mathf.Max(width, height));
            width = Mathf.Max(64, Mathf.RoundToInt(width * factor)); height = Mathf.Max(64, Mathf.RoundToInt(height * factor));
            if (texture != null && texture.width == width && texture.height == height) return;
            camera.targetTexture = null;
            if (texture != null) { texture.Release(); Delete(texture); }
            texture = new RenderTexture(width, height, 24) { name = "Shipyard preview", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 2 };
            texture.Create(); camera.targetTexture = texture; TextureChanged?.Invoke(); Render();
        }

        /// Drag orbit, clamped so a drag can never swing the view behind the
        /// island: a step that puts more of the hull behind land than the
        /// current view is dropped (pitch-only / yaw-only fallbacks keep the
        /// drag feeling alive along the free axis).
        public void Orbit(Vector2 delta)
        {
            float ny = yaw - delta.x * .35f, np = Mathf.Clamp(pitch + delta.y * .25f, 10, 85);
            if (staged)
            {
                int now = NearHits(yaw, pitch);
                if (NearHits(ny, np) > now)
                {
                    if (NearHits(yaw, np) <= now) ny = yaw;
                    else if (NearHits(ny, pitch) <= now) np = pitch;
                    else { ny = yaw; np = pitch; }
                }
            }
            yaw = ny; pitch = np; userMoved = true; Render();
        }
        public void Zoom(float factor) { zoom = Mathf.Clamp(zoom * factor, .65f, 1.35f); Render(); }

        /// Top: straight down, ship lying ALONG the wide preview (bow to the
        /// right). Three-quarter (the cube button): back to the auto framing.
        public void SetView(bool top)
        {
            zoom = 1; userMoved = false;
            if (top) { yaw = 90f; pitch = 89.9f; userMoved = true; } else ChooseView();
            Render();
        }

        // ---- framing -------------------------------------------------------
        //
        // 2026-09-27 (Kevin's phone): since the preview is staged IN the
        // world dry dock (culling mask = everything), the old fixed WORLD
        // yaw (-38) with the camera parked 65 m back put the island's hill
        // between camera and ship at most slips -- the preview opened on a
        // green cliff with a sliver of hull. Now: (1) yaw is relative to the
        // ship; (2) the azimuth is picked from candidates by raycasting the
        // hull's sample points toward the camera against the Land layer,
        // preferring the seaward (astern) quarters; (3) the camera sits just
        // outside the ship's bounding sphere -- it is ORTHOGRAPHIC, so the
        // distance changes nothing in the framing, but anything farther than
        // that (the hill) is now behind the camera and cannot draw over the
        // ship. Terrain stays visible as backdrop; no layer culling needed.

        Bounds Frame => focusHighlight && hasHighlight ? highlightBounds : bounds;
        float ShipYaw => root.transform.eulerAngles.y;
        Quaternion ViewRotation(float y, float p) => Quaternion.Euler(p, ShipYaw + y, 0);
        /// Just outside the WHOLE ship's bounding sphere as seen from the
        /// framed box's centre (a section close-up must not near-clip the
        /// rest of the hull).
        float CameraDistance(Bounds f) => Vector3.Distance(f.center, bounds.center) + bounds.extents.magnitude + 1.5f;

        static readonly float[] CandidateYaws = { 40, -40, 60, -60, 25, -25, 80, -80, 105, -105, 130, -130, 155, -155, 180, 0 };
        static readonly float[] CandidatePitches = { DefaultPitch, 48f, 65f };

        void ChooseView()
        {
            viewChosen = true;
            yaw = DefaultYaw; pitch = DefaultPitch;
            if (!staged || bounds.size.sqrMagnitude < .001f) return;
            int best = int.MaxValue;
            foreach (float p in CandidatePitches)
                foreach (float y in CandidateYaws)
                {
                    // near hits hide the ship (weight 100); far hits only
                    // mean the backdrop column crosses land -- a tiebreak
                    // that leans the view out to sea.
                    int score = NearHits(y, p) * 100 + FarHits(y, p);
                    if (score < best) { best = score; yaw = y; pitch = p; }
                    if (best == 0) return;
                }
        }

        const int LandMaskFallback = 1 << 8;
        static int LandMask
        {
            get { int i = SeaSick.Terrain.LandLayer.Index; return i > 0 ? 1 << i : LandMaskFallback; }
        }

        int NearHits(float y, float p) => Hits(y, p, CameraDistance(bounds));
        int FarHits(float y, float p) => Hits(y, p, 80f);

        /// Hull sample points (box corners pulled 15% in, centre, deck
        /// centre) that land blocks on their way to a camera at yaw/pitch.
        int Hits(float y, float p, float range)
        {
            if (!staged) return 0;
            var t = root.transform;
            Vector3 toCam = -(ViewRotation(y, p) * Vector3.forward);
            int hits = 0, mask = LandMask;
            for (int i = 0; i < 10; i++)
            {
                Vector3 local = i < 8 ? bounds.center + Vector3.Scale(bounds.extents * .85f, Corner(i))
                    : bounds.center + (i == 9 ? Vector3.up * bounds.extents.y * .85f : Vector3.zero);
                if (Physics.Raycast(t.TransformPoint(local), toCam, range, mask, QueryTriggerInteraction.Ignore)) hits++;
            }
            return hits;
        }

        public void Render()
        {
            if (texture == null || bounds.size.sqrMagnitude < .001f) return;
            var frame = Frame;
            var t = root.transform;
            var rotation = ViewRotation(yaw, pitch);
            float dist = CameraDistance(frame);
            camera.transform.rotation = rotation;
            camera.transform.position = t.TransformPoint(frame.center) - rotation * Vector3.forward * dist;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = dist + 400f; // world behind the ship still draws as backdrop
            float halfX = 0, halfY = 0;
            var inv = Quaternion.Inverse(rotation);
            for (int i = 0; i < 8; i++)
            {
                var offset = t.rotation * Vector3.Scale(frame.extents, Corner(i));
                var p = inv * offset;
                halfX = Mathf.Max(halfX, Mathf.Abs(p.x)); halfY = Mathf.Max(halfY, Mathf.Abs(p.y));
            }
            camera.aspect = (float)texture.width / texture.height;
            camera.orthographicSize = Mathf.Max(halfY, halfX / camera.aspect) * 1.12f / zoom;
            camera.Render();
        }

        static void Delete(UnityEngine.Object obj)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); }
        public void Dispose()
        {
            camera.targetTexture = null;
            if (texture != null) { texture.Release(); Delete(texture); }
            root.SetActive(false); Delete(root); Delete(lineMaterial);
        }
    }
}
