using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.World
{
    /// The weather you can see. One number — how bad it is where the ship
    /// actually is — drives the sky dome, the sun, the ambient and the fog
    /// together, so they can never disagree with each other.
    ///
    /// This exists because the simulation went mountainous long before the
    /// picture did: 24m seas under a bright blue sky read as a nice day with
    /// unusually large water. A storm is mostly a lighting state.
    ///
    /// It is also the single owner of "how bad is it here" — ChaseCamera and
    /// StormSpray both read Storminess01 from here rather than each deciding
    /// for themselves, so the sea, the air and the framing build together.
    [DefaultExecutionOrder(-50)]
    public class SkyDirector : MonoBehaviour
    {
        public static SkyDirector Instance { get; private set; }

        [SerializeField] Transform ship;
        [SerializeField] Light sun;

        [Header("What counts as bad weather")]
        [Tooltip("Sea severity below this contributes nothing to the sky.")]
        [SerializeField] float severityFloor = 0.55f;
        [Tooltip("How much a merely rough sea can darken the sky on its own. " +
                 "The western storm always reaches 1 by itself.")]
        [Range(0f, 1f)] [SerializeField] float severityWeight = 0.5f;
        [Tooltip("Seconds-ish. Weather should arrive, not switch on.")]
        [SerializeField] float response = 0.35f;
        [Tooltip("Negative to follow the sea. 0..1 pins the sky for testing.")]
        [SerializeField] float forceStorm = -1f;

        // Clear weather. Roughly what the scene shipped with, so home water
        // looks the way it always did.
        [Header("Clear")]
        [SerializeField] Color clearZenith  = new Color(0.17f, 0.38f, 0.66f);
        [SerializeField] Color clearHorizon = new Color(0.68f, 0.80f, 0.88f);
        [SerializeField] Color clearGround  = new Color(0.22f, 0.30f, 0.34f);
        [SerializeField] Color clearSun     = new Color(1f, 0.93f, 0.82f);
        [SerializeField] float clearSunIntensity = 1.15f;
        [SerializeField] float clearOvercast = 0.10f;
        [SerializeField] float clearFogStart = 600f;
        [SerializeField] float clearFogEnd = 1500f;
        [Tooltip("How fast the gradient climbs to the zenith colour. Higher " +
                 "pulls the dark down to the skyline and leaves a thin bright " +
                 "band under it, which is what a storm sky actually looks like.")]
        [SerializeField] float clearHorizonSharp = 1.6f;

        // The western deep. Slate and dirty grey — no blue left anywhere, and
        // the light comes from a lid of cloud rather than from a direction.
        [Header("Storm")]
        [SerializeField] Color stormZenith  = new Color(0.075f, 0.088f, 0.105f);
        [SerializeField] Color stormHorizon = new Color(0.30f, 0.325f, 0.345f);
        [SerializeField] Color stormGround  = new Color(0.085f, 0.10f, 0.11f);
        [SerializeField] Color stormSun     = new Color(0.66f, 0.70f, 0.76f);
        [SerializeField] float stormSunIntensity = 0.34f;
        [SerializeField] float stormOvercast = 1f;
        [Tooltip("Visibility collapses. This is also what will make night bite.")]
        [SerializeField] float stormFogStart = 70f;
        [SerializeField] float stormFogEnd = 430f;
        [SerializeField] float stormHorizonSharp = 3.4f;

        [Header("Cloud")]
        [SerializeField] float cloudScaleClear = 0.030f;
        [SerializeField] float cloudScaleStorm = 0.019f;
        [SerializeField] float cloudSpeedClear = 0.7f;
        [SerializeField] float cloudSpeedStorm = 3.4f;

        /// 0 = the home shelf on a good day, 1 = deep in the western storm.
        public float Storminess01 { get; private set; }

        /// What the sky is doing at the skyline. The ocean reflects this, and
        /// the fog is set to it, so the sea meets the sky without a seam.
        public Color HorizonColor { get; private set; }

        Material skyInstance;

        static readonly int ZenithId    = Shader.PropertyToID("_ZenithColor");
        static readonly int HorizonId   = Shader.PropertyToID("_HorizonColor");
        static readonly int GroundId    = Shader.PropertyToID("_GroundColor");
        static readonly int SunColorId  = Shader.PropertyToID("_SunColor");
        static readonly int OvercastId  = Shader.PropertyToID("_Overcast");
        static readonly int HorizonSharpId = Shader.PropertyToID("_HorizonSharp");
        static readonly int ScudId      = Shader.PropertyToID("_Scud");
        static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
        static readonly int CloudSpeedId = Shader.PropertyToID("_CloudSpeed");

        static readonly int SunDirId    = Shader.PropertyToID("_SS_SunDir");
        static readonly int SkyWindId   = Shader.PropertyToID("_SS_SkyWind");
        static readonly int SkyHorizonId = Shader.PropertyToID("_SS_SkyHorizon");
        static readonly int StorminessId = Shader.PropertyToID("_SS_Storminess");

        void Awake()
        {
            Instance = this;
            if (sun == null)
            {
                sun = RenderSettings.sun;
                if (sun == null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional) { sun = l; break; }
            }
            if (ship == null)
            {
                var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
                if (motor != null) ship = motor.transform;
            }

            // Work on a copy: play-mode tuning must never write back into the
            // material asset on disk.
            var src = RenderSettings.skybox;
            if (src == null || src.shader == null || src.shader.name != "SeaSick/Sky")
            {
                var sh = Shader.Find("SeaSick/Sky");
                if (sh != null) src = new Material(sh);
            }
            if (src != null)
            {
                skyInstance = new Material(src);
                RenderSettings.skybox = skyInstance;
            }

            // Ambient from a custom procedural skybox would need
            // DynamicGI.UpdateEnvironment every frame, which costs milliseconds
            // on a phone. Three colours we set ourselves cost nothing and are
            // exact.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            Apply(0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (skyInstance != null) Destroy(skyInstance);
        }

        void LateUpdate()
        {
            float want = forceStorm >= 0f ? Mathf.Clamp01(forceStorm) : SampleWeather();
            Storminess01 = Mathf.Lerp(Storminess01, want,
                1f - Mathf.Exp(-response * Time.deltaTime));
            Apply(Storminess01);
        }

        float SampleWeather()
        {
            var ctrl = SeaSick.Ocean2.SeaStateController.Instance;
            if (ctrl == null || ship == null) return 0f;

            Vector2 p = new Vector2(ship.position.x, ship.position.z);
            // The weather system itself is the authority now: its storminess
            // already folds in the storm region the ship is sailing through.
            float storm = ctrl.Storminess01;
            float severity = ctrl.SeaSeverityAt(p);
            float lift = Mathf.InverseLerp(severityFloor, 1f, severity) * severityWeight;

            return Mathf.Clamp01(Mathf.Max(storm, lift));
        }

        void Apply(float t)
        {
            Color horizon = Color.Lerp(clearHorizon, stormHorizon, t);
            HorizonColor = horizon;

            if (skyInstance != null)
            {
                skyInstance.SetColor(ZenithId, Color.Lerp(clearZenith, stormZenith, t));
                skyInstance.SetColor(HorizonId, horizon);
                skyInstance.SetColor(GroundId, Color.Lerp(clearGround, stormGround, t));
                skyInstance.SetColor(SunColorId, Color.Lerp(clearSun, stormSun, t));
                skyInstance.SetFloat(OvercastId, Mathf.Lerp(clearOvercast, stormOvercast, t));
                skyInstance.SetFloat(HorizonSharpId, Mathf.Lerp(clearHorizonSharp, stormHorizonSharp, t));
                // Scud only tears past once it is genuinely blowing.
                skyInstance.SetFloat(ScudId, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, t)));
                skyInstance.SetFloat(CloudScaleId, Mathf.Lerp(cloudScaleClear, cloudScaleStorm, t));
                skyInstance.SetFloat(CloudSpeedId, Mathf.Lerp(cloudSpeedClear, cloudSpeedStorm, t));
            }

            if (sun != null)
            {
                sun.color = Color.Lerp(clearSun, stormSun, t);
                sun.intensity = Mathf.Lerp(clearSunIntensity, stormSunIntensity, t);
                Shader.SetGlobalVector(SunDirId, -sun.transform.forward);
            }

            // Ambient: the sky lights the tops, the sea lights the undersides.
            // In a storm both go grey-green and drop hard, which is what stops
            // the hull and the crew reading as a sunny-day scene in the dark.
            Color skyAmb = horizon * Mathf.Lerp(0.95f, 0.62f, t);
            RenderSettings.ambientSkyColor = skyAmb;
            RenderSettings.ambientEquatorColor = Color.Lerp(
                new Color(0.30f, 0.36f, 0.40f), new Color(0.115f, 0.135f, 0.135f), t);
            RenderSettings.ambientGroundColor = Color.Lerp(
                new Color(0.10f, 0.13f, 0.14f), new Color(0.045f, 0.055f, 0.058f), t);

            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = Mathf.Lerp(clearFogStart, stormFogStart, t);
            RenderSettings.fogEndDistance = Mathf.Lerp(clearFogEnd, stormFogEnd, t);

            var wind = SeaSick.Ocean2.SeaStateController.Instance != null
                ? SeaSick.Ocean2.SeaStateController.Instance.WindDirection : Vector2.right;
            Shader.SetGlobalVector(SkyWindId, new Vector4(wind.x, wind.y, 0f, 0f));
            Shader.SetGlobalVector(SkyHorizonId, horizon);
            Shader.SetGlobalFloat(StorminessId, t);
        }
    }
}
