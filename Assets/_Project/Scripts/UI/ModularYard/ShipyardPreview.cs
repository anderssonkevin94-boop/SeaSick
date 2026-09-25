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
        Bounds bounds;
        Bounds highlightBounds;
        bool hasHighlight;
        bool focusHighlight;
        float yaw = -38f, pitch = 34f, zoom = 1f;
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

        public void Build(AssemblyResult result, string highlight, ShipConfiguration configuration = null)
        {
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
            bool first = true;
            foreach (var r in view.GetComponentsInChildren<Renderer>())
            { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
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
                highlightBounds = new Bounds(hb.center + root.transform.position, hb.size);
                hasHighlight = true;
                break;
            }
            Render();
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

        public void Orbit(Vector2 delta) { yaw -= delta.x * .35f; pitch = Mathf.Clamp(pitch + delta.y * .25f, 10, 85); Render(); }
        public void Zoom(float factor) { zoom = Mathf.Clamp(zoom * factor, .65f, 1.35f); Render(); }
        public void SetView(bool top) { yaw = top ? 0 : -38; pitch = top ? 89.9f : 34; zoom = 1; Render(); }

        public void Render()
        {
            if (texture == null || bounds.size.sqrMagnitude < .001f) return;
            var frame = focusHighlight && hasHighlight ? highlightBounds : bounds;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            camera.transform.rotation = rotation;
            camera.transform.position = frame.center - rotation * Vector3.forward * 65;
            float halfX = 0, halfY = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 offset = new Vector3((i & 1) == 0 ? -frame.extents.x : frame.extents.x,
                    (i & 2) == 0 ? -frame.extents.y : frame.extents.y, (i & 4) == 0 ? -frame.extents.z : frame.extents.z);
                var p = Quaternion.Inverse(rotation) * offset;
                halfX = Mathf.Max(halfX, Mathf.Abs(p.x)); halfY = Mathf.Max(halfY, Mathf.Abs(p.y));
            }
            camera.aspect = (float)texture.width / texture.height;
            camera.orthographicSize = Mathf.Max(halfY, halfX / camera.aspect) * 1.18f / zoom;
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
