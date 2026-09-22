using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **The ring on the ground under whatever is selected.**
    ///
    /// Half of "nothing is drawn over the world" is that the feedback for a
    /// tap has to be IN the world. A card that appears is not enough on its
    /// own: on a camp with three huts in shot, the player has to be able to
    /// see which one the card is about without reading it.
    ///
    /// Drawn as a `LineRenderer` loop rather than a generated annulus mesh
    /// because the ring is a line — a mesh would need a width in world units
    /// that reads thin at the camera's far end and fat at its near one, and a
    /// LineRenderer's width is already the knob for that.
    ///
    /// It sits on the GROUND, not at the anchor: an anchor is usually at a
    /// thing's middle (a hut's roof, a hand's chest) and a ring floating there
    /// reads as a halo rather than as a footprint.
    public class SelectionRing : MonoBehaviour
    {
        const int Segments = 64;
        /// Nothing smaller: below this a ring round a campfire is a dot.
        const float MinRadius = 1.5f;

        LineRenderer line;
        Material mat;
        float radius = MinRadius;
        ISheet tracked;

        void Awake()
        {
            var go = new GameObject("SheetSelectionRing");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Segments;
            line.widthMultiplier = 0.14f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.material = mat = MakeMaterial();
            Circle(1f);
            go.SetActive(false);
        }

        static Material MakeMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            // URP's Unlit ships opaque; the ring has to fade, so the blend is
            // set by hand. Setting `_Surface` alone is not enough at runtime —
            // that field is read by the material INSPECTOR, which is not
            // running, so the blend factors and the keyword go on directly.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        void Circle(float r)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        void LateUpdate()
        {
            var s = Sheets.Current;
            if (s == null || !s.StillValid)
            {
                if (line.gameObject.activeSelf) line.gameObject.SetActive(false);
                tracked = null;
                return;
            }

            if (!ReferenceEquals(s, tracked))
            {
                tracked = s;
                radius = RadiusAt(s.AnchorWorld);
                Circle(radius);
                line.gameObject.SetActive(true);
            }

            var p = Ground(s.AnchorWorld);
            line.transform.position = p;

            // A slow breath, so the ring reads as live rather than as a decal
            // somebody forgot to clear. Deliberately small: the campfire is
            // already flickering next to it.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
            var c = SheetTheme.Paper;
            c.a = Mathf.Lerp(0.55f, 0.95f, pulse);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            line.widthMultiplier = Mathf.Lerp(0.11f, 0.16f, pulse) * Mathf.Max(1f, radius / 3f);
        }

        /// Where the ground is under a point. Triggers are ignored on purpose:
        /// `Pickable`'s own tap spheres are the first thing a downward ray
        /// would otherwise find, and the ring would then sit in mid-air at
        /// exactly the height of the thing it is under.
        static Vector3 Ground(Vector3 at)
        {
            var from = at + Vector3.up * 40f;
            if (Physics.Raycast(from, Vector3.down, out var hit, 200f, ~0,
                                QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return new Vector3(at.x, at.y, at.z);
        }

        /// How wide the footprint is. Measured off whatever renderer owns the
        /// anchor rather than taken from the sheet, because `ISheet` says
        /// where its subject IS and deliberately not how big it is — a sheet
        /// is about a decision, not about a mesh.
        static float RadiusAt(Vector3 at)
        {
            float best = MinRadius;
            var hits = Physics.OverlapSphere(at, 2f, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null) continue;
                var rend = hits[i].GetComponentInParent<Renderer>();
                if (rend == null) rend = hits[i].GetComponentInChildren<Renderer>();
                if (rend == null) continue;
                var e = rend.bounds.extents;
                best = Mathf.Max(best, Mathf.Max(e.x, e.z) * 1.25f);
            }
            return Mathf.Clamp(best, MinRadius, 24f);
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }
}
