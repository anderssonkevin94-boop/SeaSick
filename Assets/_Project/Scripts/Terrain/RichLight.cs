using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Terrain
{
    /// The island art's "rich light" dial (Kevin, 2026-10-02: "i want the
    /// other assets to pop as much as they do. to have that rich beautiful
    /// color that we somehow created for the boat").
    ///
    /// Sends `_SS_RichLight`, which blends Environment Toon (+ Textured),
    /// Terrain Vertex Color and Worn Road from their old ambient (0) to the
    /// ship's (1): SH along the normal + a warm/cool hemisphere bounce that
    /// fades at night and in storms + a cool moon fill, and a 22 % shadow
    /// floor. The shader half is Art/Shaders/World/RichLight.hlsl. A float, not
    /// a keyword, so it adds no shader variants. An unset global reads 0, the
    /// old look, so nothing changes if this never runs.
    ///
    /// Tunable live in FeelLab (listed in Dev/FeelLab.cs TypeFullNames).
    public static class RichLight
    {
        /// 0 = the island's old cel ambient, 1 = the ship's ambient. ON by
        /// default since 2026-10-03: shown off / half / on side by side
        /// (softer palm shadows, lighter yellower grass), Kevin: "i like that
        /// lighting a lot more though." Still tunable on FeelLab.
        public static float amount = 1f;

        static readonly int RichLightId = Shader.PropertyToID("_SS_RichLight");
        static float sent = float.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            sent = float.NaN;
            RenderPipelineManager.beginContextRendering -= OnBeginRendering;
            RenderPipelineManager.beginContextRendering += OnBeginRendering;
            Push();
        }

        // Once per rendered frame, a float compare; a global set only when
        // the dial actually moves (FeelLab writes the static field directly).
        static void OnBeginRendering(ScriptableRenderContext context, List<Camera> cameras) => Push();

        static void Push()
        {
            float v = Mathf.Clamp01(amount);
            if (v == sent) return;
            sent = v;
            Shader.SetGlobalFloat(RichLightId, v);
        }
    }
}
