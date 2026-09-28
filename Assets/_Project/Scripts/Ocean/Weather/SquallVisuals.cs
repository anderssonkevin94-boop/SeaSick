using UnityEngine;

namespace SeaSick.Ocean
{
    /// <summary>
    /// The squall's cheap, visible-from-a-kilometre body: a handful of large
    /// dark billboards for the cloud bank overhead, and one translucent
    /// cylinder for the rain curtain underneath. Built once (lazily, on the
    /// first squall of the run) and only ever repositioned/rescaled after
    /// that -- no `Instantiate` per squall, no per-frame allocation, and
    /// nothing sampled off the ocean (`StormSpray` already owns that
    /// budget; this only ever reads the centre/radius `SquallDirector`
    /// hands it).
    /// </summary>
    public class SquallVisuals : MonoBehaviour
    {
        const int CloudCount = 5;
        static readonly Vector2[] CloudOffsetsUnit =
        {
            new Vector2(0f, 0f), new Vector2(0.55f, 0.2f), new Vector2(-0.5f, 0.3f),
            new Vector2(0.2f, -0.55f), new Vector2(-0.3f, -0.4f),
        };
        static readonly float[] CloudSizeScale = { 1.15f, 0.85f, 0.95f, 0.75f, 0.8f };
        static readonly float[] CloudAltitudeScale = { 1f, 0.85f, 1.1f, 0.75f, 0.95f };

        [Tooltip("Altitude band the cloud billboards sit at, metres.")]
        public Vector2 cloudAltitude = new Vector2(120f, 180f);
        [Tooltip("How big each cloud billboard is, metres, before its own per-cloud size scale.")]
        public float cloudBaseSize = 180f;
        [Tooltip("How tall the rain curtain stands, metres.")]
        public float rainHeight = 150f;

        Transform[] clouds;
        Transform rain;
        Material cloudMat, rainMat;
        Camera cam;
        bool built;
        bool visible;

        void Build()
        {
            if (built) return;
            built = true;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var tex = FoamTexture.SoftPuff();

            cloudMat = new Material(shader);
            cloudMat.SetColor("_BaseColor", new Color(0.10f, 0.11f, 0.13f, 0.55f));
            cloudMat.SetTexture("_BaseMap", tex);
            MakeTransparent(cloudMat, 2995);

            rainMat = new Material(shader);
            rainMat.SetColor("_BaseColor", new Color(0.55f, 0.60f, 0.66f, 0.18f));
            rainMat.SetTexture("_BaseMap", tex);
            MakeTransparent(rainMat, 2996);

            var cloudRoot = new GameObject("SquallClouds").transform;
            cloudRoot.SetParent(transform, false);
            clouds = new Transform[CloudCount];
            for (int i = 0; i < CloudCount; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Cloud" + i;
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.GetComponent<MeshRenderer>().sharedMaterial = cloudMat;
                quad.transform.SetParent(cloudRoot, false);
                clouds[i] = quad.transform;
            }

            var curtain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            curtain.name = "SquallRain";
            var ccol = curtain.GetComponent<Collider>();
            if (ccol != null) Destroy(ccol);
            curtain.GetComponent<MeshRenderer>().sharedMaterial = rainMat;
            curtain.transform.SetParent(transform, false);
            rain = curtain.transform;

            SetActiveInternal(false);
        }

        static void MakeTransparent(Material m, int queue)
        {
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
        }

        public void SetActive(bool on)
        {
            if (!built)
            {
                if (!on) return;
                Build();
            }
            SetActiveInternal(on);
        }

        void SetActiveInternal(bool on)
        {
            if (visible == on) return;
            visible = on;
            if (clouds != null) foreach (var c in clouds) if (c != null) c.gameObject.SetActive(on);
            if (rain != null) rain.gameObject.SetActive(on);
        }

        /// `nearness01`: 0 far away, 1 the ship is at the centre -- used only
        /// to deepen the rain curtain's own alpha a touch as it is approached,
        /// never to change the poly/draw-call budget (five quads and one
        /// cylinder, always).
        public void UpdateVisual(Vector3 centre, float radius, float nearness01)
        {
            if (!visible || clouds == null) return;
            if (cam == null) cam = Camera.main;

            for (int i = 0; i < clouds.Length; i++)
            {
                var c = clouds[i];
                if (c == null) continue;
                Vector2 off = CloudOffsetsUnit[i] * radius * 0.6f;
                float alt = Mathf.Lerp(cloudAltitude.x, cloudAltitude.y, CloudAltitudeScale[i]);
                c.position = new Vector3(centre.x + off.x, alt, centre.z + off.y);
                float size = cloudBaseSize * CloudSizeScale[i];
                c.localScale = new Vector3(size, size, 1f);
                if (cam != null)
                {
                    // Face the camera on the horizontal axis only -- a cloud
                    // sheet that also pitches with the camera reads as a
                    // card, not a bank of weather sitting overhead.
                    Vector3 toCam = cam.transform.position - c.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 1f)
                        c.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }
            }

            if (rain != null)
            {
                rain.position = new Vector3(centre.x, rainHeight * 0.5f, centre.z);
                float diameter = radius * 1.8f;
                rain.localScale = new Vector3(diameter, rainHeight * 0.5f, diameter);
                if (rainMat != null)
                {
                    var c = rainMat.GetColor("_BaseColor");
                    rainMat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, Mathf.Lerp(0.12f, 0.26f, nearness01)));
                }
            }
        }
    }
}
