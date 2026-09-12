using UnityEngine;

namespace SeaSick.Ship
{
    /// Keeps the sea out of the boat.
    ///
    /// In mountainous seas the ocean surface rises above the deck and renders
    /// straight through it — you end up looking at open water inside the hull.
    /// This feeds the ocean shader the hull's inboard volume in ship space and
    /// the shader clips itself away inside it.
    ///
    /// Deliberately NOT a stencil pass. A stencil mask needs an extra draw and
    /// gets the near field wrong: ocean between the camera and the boat falls
    /// in the same screen pixels as the boat's interior, so it gets punched out
    /// too. Clipping in the ocean's own fragment shader against a volume is
    /// exact from every angle, costs four instructions, and adds no draw call.
    ///
    /// The plan section is an ellipse rather than a box because a hull tapers;
    /// a box would reach past the stem and cut a notch in the sea ahead of her.
    public class HullWaterClip : MonoBehaviour
    {
        [Tooltip("Centre of the inboard volume in ship-local metres.")]
        [SerializeField] Vector3 centre = new Vector3(0f, 1.15f, 0.2f);
        [Tooltip("Semi-axis across the beam (x) and along the hull (z), metres.")]
        [SerializeField] Vector2 semiAxes = new Vector2(1.75f, 5.0f);
        [Tooltip("Half-height of the band from the deck up past the rail, metres.")]
        [SerializeField] float halfHeight = 0.75f;
        [SerializeField] bool active = true;

        static readonly int MatrixId = Shader.PropertyToID("_HullClipWorldToLocal");
        static readonly int CentreId = Shader.PropertyToID("_HullClipCentre");
        static readonly int SizeId = Shader.PropertyToID("_HullClipSize");

        void OnEnable() { Push(); }
        void LateUpdate() { Push(); }

        void OnDisable()
        {
            // Leave the sea alone when this is not driving it.
            Shader.SetGlobalVector(SizeId, Vector4.zero);
            Shader.DisableKeyword(HullClipKeyword);
        }

        // The clip() in Ocean.shader lives behind a global keyword now: a
        // discard anywhere in a fragment shader makes the whole thing late-Z
        // on a tile GPU, so the shipped variant has none, and only a hull
        // that is actually clipping switches it on. Must follow `active`
        // exactly -- keyword on with _HullClipSize.w at 0 is harmless, but
        // keyword off with a hull in the water draws the sea inside her.
        const string HullClipKeyword = "_HULL_CLIP";

        void Push()
        {
            Shader.SetGlobalMatrix(MatrixId, transform.worldToLocalMatrix);
            Shader.SetGlobalVector(CentreId, centre);
            Shader.SetGlobalVector(SizeId,
                new Vector4(semiAxes.x, halfHeight, semiAxes.y, active ? 1f : 0f));
            if (active) Shader.EnableKeyword(HullClipKeyword);
            else Shader.DisableKeyword(HullClipKeyword);
        }

        /// Sized from the real deck by the setup script, not by eye.
        public void Configure(Vector3 volumeCentre, Vector2 axes, float half)
        {
            centre = volumeCentre;
            semiAxes = axes;
            halfHeight = half;
            Push();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(centre, new Vector3(semiAxes.x * 2f, halfHeight * 2f, semiAxes.y * 2f));
        }
    }
}
