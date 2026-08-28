using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.World
{
    /// The weather you can see, and now the time of day as well. Two numbers —
    /// how bad it is where the ship actually is, and where the sun stands —
    /// drive the sky dome, the key light, the ambient and the fog together, so
    /// they can never disagree with each other.
    ///
    /// This exists because the simulation went mountainous long before the
    /// picture did: 24m seas under a bright blue sky read as a nice day with
    /// unusually large water. A storm is mostly a lighting state.
    ///
    /// It is also the single owner of "how bad is it here" — ChaseCamera and
    /// StormSpray both read Storminess01 from here rather than each deciding
    /// for themselves, so the sea, the air and the framing build together.
    ///
    /// Time of day is layered UNDER the storm rather than beside it: the clear
    /// palette becomes a function of sun elevation, and storminess then blends
    /// that toward the storm palette exactly as it always did. Storm is a lid
    /// over whatever hour it happens to be, which is why a night storm is
    /// darker than either on its own and needs no third palette.
    [DefaultExecutionOrder(-50)]
    public class SkyDirector : MonoBehaviour
    {
        public static SkyDirector Instance { get; private set; }

        [SerializeField] Transform ship;
        [Tooltip("The one directional light. It plays the sun by day and the " +
                 "moon by night — URP shadows a single directional light, so " +
                 "swapping its role is free and a second light would not be.")]
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

        [Header("Time of day")]
        [Tooltip("Real seconds in a whole day. 180 while testing so a cycle " +
                 "fits in a play session; 1440 shipped — a minute an hour.")]
        [SerializeField] float dayLength = 180f;
        [Tooltip("What time a run starts at. 0.30 is shortly after sunrise. " +
                 "Pushed into TimeOfDay every Awake, because TimeOfDay is a " +
                 "static and statics survive leaving play mode.")]
        [Range(0f, 1f)] [SerializeField] float startTime01 = 0.30f;
        [Tooltip("Clock speed multiplier. 0 stops time without pinning it.")]
        [SerializeField] float timeScale = 1f;
        [Tooltip("Negative to run. 0..1 PINS the clock, which every visual " +
                 "probe needs — at a 180s day the sun moves 2 degrees a " +
                 "second and two screenshots stop being comparable.")]
        [SerializeField] float pinTime = -1f;
        [Tooltip("Tilts the whole arc south so the sun peaks at (90 - this) " +
                 "and stays in a chase camera's frame instead of overhead.")]
        [SerializeField] float latitudeDeg = 38f;
        [Tooltip("Days in a lunar month. The moon lags by day/this, so it " +
                 "rises later each day and the night sky differs per voyage.")]
        [SerializeField] float synodicDays = 29.5f;

        // Clear weather at midday. Roughly what the scene shipped with, so home
        // water at noon looks the way it always did.
        [Header("Clear — midday")]
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

        // Sunrise and sunset share one palette — the sun's own arc decides
        // which side of the sky it lands on, so there is nothing to duplicate.
        // The sun sets due west, over the deep, which is the direction the
        // storms come from.
        [Header("Clear — dawn / dusk")]
        [SerializeField] Color duskZenith  = new Color(0.115f, 0.165f, 0.335f);
        [SerializeField] Color duskHorizon = new Color(0.93f, 0.52f, 0.30f);
        [SerializeField] Color duskGround  = new Color(0.145f, 0.125f, 0.125f);
        [SerializeField] Color duskSun     = new Color(1f, 0.60f, 0.34f);
        [SerializeField] float duskSunIntensity = 0.80f;
        [SerializeField] float duskOvercast = 0.10f;
        [SerializeField] float duskFogStart = 320f;
        [SerializeField] float duskFogEnd = 1250f;
        [SerializeField] float duskHorizonSharp = 2.6f;

        // Dark, but never black. The lantern has to matter and the islands'
        // lights have to be worth steering by — but you can still make your
        // way without them, you just might not see stones if you aren't
        // looking. Night is attention and risk, not a wall.
        [Header("Clear — night")]
        [SerializeField] Color nightZenith  = new Color(0.014f, 0.022f, 0.048f);
        [SerializeField] Color nightHorizon = new Color(0.055f, 0.075f, 0.118f);
        [SerializeField] Color nightGround  = new Color(0.010f, 0.014f, 0.023f);
        [SerializeField] Color moonColor    = new Color(0.62f, 0.72f, 1f);
        [Tooltip("Full-moon intensity, against the sun's 1.15. Low enough to " +
                 "read as night, high enough to pick out a wave face.")]
        [SerializeField] float moonIntensity = 0.20f;
        [Tooltip("How much light a new moon still gives — starlight, really.")]
        [Range(0f, 1f)] [SerializeField] float newMoonFloor = 0.25f;
        [Tooltip("THE darkness knob. Scales the ambient that lights every " +
                 "underside at night; the sky ambient follows the horizon " +
                 "colour on its own.")]
        [SerializeField] float nightAmbient = 0.18f;
        [SerializeField] float nightOvercast = 0.06f;
        [SerializeField] float nightHorizonSharp = 2.2f;
        [SerializeField] float nightFogStart = 240f;
        [SerializeField] float nightFogEnd = 950f;
        [Tooltip("Angular radius of the moon disc, in sine units. 0.045 is " +
                 "about 2.6 degrees — a shade wider than the real thing, for " +
                 "the same reason the sun disc is.")]
        [SerializeField] float moonSize = 0.045f;
        [SerializeField] float moonHalo = 0.35f;
        [SerializeField] float starBrightness = 1f;
        [Tooltip("Fraction of sky cells left EMPTY. Higher is fewer stars.")]
        [SerializeField] float starDensity = 0.948f;
        [Tooltip("How far the SEA's body colour is dimmed at full night. The " +
                 "body carries no diffuse term, so nothing else about it knows " +
                 "the sun has set.")]
        [Range(0.05f, 1f)] [SerializeField] float nightSeaDim = 0.30f;
        [Tooltip("How far the sea leans on the authored horizon instead of the " +
                 "stale daylight reflection probe, at full night.")]
        [Range(0.55f, 1f)] [SerializeField] float nightSeaSkyMix = 0.93f;

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
        [Tooltip("What the storm sky's own colours are scaled to at full " +
                 "night. The storm palette is an absolute grey, so without " +
                 "this a midnight storm renders BRIGHTER than a clear night.")]
        [Range(0.02f, 1f)] [SerializeField] float stormNightScale = 0.12f;

        [Header("Cloud")]
        [SerializeField] float cloudScaleClear = 0.030f;
        [SerializeField] float cloudScaleStorm = 0.019f;
        [SerializeField] float cloudSpeedClear = 0.7f;
        [SerializeField] float cloudSpeedStorm = 3.4f;

        /// 0 = the home shelf on a good day, 1 = deep in the western storm.
        public float Storminess01 { get; private set; }

        /// 0 = full daylight, 1 = full night. Driven by sun ELEVATION, not by
        /// the clock, so it is correct at any latitude and identical at dawn
        /// and dusk without a second curve.
        public float Night01 { get; private set; }

        /// Unit vectors toward each body, whichever one the light is playing.
        public Vector3 SunDirection { get; private set; }
        public Vector3 MoonDirection { get; private set; }

        /// True while the directional light is the sun rather than the moon.
        public bool SunIsUp { get; private set; }

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
        static readonly int MoonColorId  = Shader.PropertyToID("_MoonColor");
        static readonly int MoonSizeId   = Shader.PropertyToID("_MoonSize");
        static readonly int MoonHaloId   = Shader.PropertyToID("_MoonHalo");
        static readonly int StarBrightId = Shader.PropertyToID("_StarBright");
        static readonly int StarDensityId = Shader.PropertyToID("_StarDensity");

        static readonly int SunDirId    = Shader.PropertyToID("_SS_SunDir");
        static readonly int MoonDirId   = Shader.PropertyToID("_SS_MoonDir");
        static readonly int MoonPhaseId = Shader.PropertyToID("_SS_MoonPhase");
        static readonly int NightId     = Shader.PropertyToID("_SS_Night");
        static readonly int StarRotId   = Shader.PropertyToID("_SS_StarRot");
        static readonly int NightSeaDimId = Shader.PropertyToID("_SS_NightBodyDim");
        static readonly int NightSeaMixId = Shader.PropertyToID("_SS_NightSkyMix");
        static readonly int SkyWindId   = Shader.PropertyToID("_SS_SkyWind");
        static readonly int SkyHorizonId = Shader.PropertyToID("_SS_SkyHorizon");
        static readonly int StorminessId = Shader.PropertyToID("_SS_Storminess");

        /// One coherent set of sky values. Time of day picks one of these, and
        /// storminess then blends it toward the storm one — so there is a
        /// single blend path and nothing can be tinted twice.
        struct Palette
        {
            public Color zenith, horizon, ground, light;
            public float intensity, overcast, horizonSharp, fogStart, fogEnd;

            public static Palette Lerp(Palette a, Palette b, float t)
            {
                return new Palette
                {
                    zenith       = Color.Lerp(a.zenith, b.zenith, t),
                    horizon      = Color.Lerp(a.horizon, b.horizon, t),
                    ground       = Color.Lerp(a.ground, b.ground, t),
                    light        = Color.Lerp(a.light, b.light, t),
                    intensity    = Mathf.Lerp(a.intensity, b.intensity, t),
                    overcast     = Mathf.Lerp(a.overcast, b.overcast, t),
                    horizonSharp = Mathf.Lerp(a.horizonSharp, b.horizonSharp, t),
                    fogStart     = Mathf.Lerp(a.fogStart, b.fogStart, t),
                    fogEnd       = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
                };
            }
        }

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

            // TimeOfDay is a static, so it survives leaving play mode with
            // whatever a probe last scrubbed it to. Reassert the whole clock
            // every run or the second play session starts at a different hour
            // than the first and nothing says so.
            TimeOfDay.DayLength = dayLength;
            TimeOfDay.Scale = 1.0;
            TimeOfDay.Paused = false;
            // Scrub rather than SetTime01: SetTime01 preserves the day
            // number, and TimeOfDay is a static that survives leaving play
            // mode, so the counter climbed across sessions (measured: Day=3 on
            // what should have been a first run). The moon's bearing and its
            // phase both hang off `day / synodicDays`, so every play session
            // was getting a different moon with nothing saying so.
            TimeOfDay.Scrub(startTime01 * (double)dayLength);

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
            // One owner advances the clock, once a frame. A pin holds the hour
            // without stopping the rest of the game, which is what a visual
            // probe needs — it wants a still sun over a moving sea.
            if (pinTime >= 0f) TimeOfDay.SetTime01(Mathf.Clamp01(pinTime));
            else               TimeOfDay.Advance(Time.deltaTime * Mathf.Max(0f, timeScale));

            float want = forceStorm >= 0f ? Mathf.Clamp01(forceStorm) : SampleWeather();
            Storminess01 = Mathf.Lerp(Storminess01, want,
                1f - Mathf.Exp(-response * Time.deltaTime));
            Apply(Storminess01);
        }

        float SampleWeather()
        {
            var ctrl = SeaSick.Ocean.SeaStateController.Instance;
            if (ctrl == null || ship == null) return 0f;

            Vector2 p = new Vector2(ship.position.x, ship.position.z);
            // The weather system itself is the authority now: its storminess
            // already folds in the storm region the ship is sailing through.
            float storm = ctrl.Storminess01;
            float severity = ctrl.SeaSeverityAt(p);
            float lift = Mathf.InverseLerp(severityFloor, 1f, severity) * severityWeight;

            return Mathf.Clamp01(Mathf.Max(storm, lift));
        }

        /// The clear-weather sky at the current hour. Blended on the sun's
        /// ELEVATION rather than on the clock: dawn and dusk are the same
        /// picture from the same formula, and the latitude tilt is already
        /// baked into where the sun actually is.
        Palette ClearPalette(float sunElevation, float moonLight)
        {
            var day = new Palette
            {
                zenith = clearZenith, horizon = clearHorizon, ground = clearGround,
                light = clearSun, intensity = clearSunIntensity,
                overcast = clearOvercast, horizonSharp = clearHorizonSharp,
                fogStart = clearFogStart, fogEnd = clearFogEnd,
            };
            var dusk = new Palette
            {
                zenith = duskZenith, horizon = duskHorizon, ground = duskGround,
                light = duskSun, intensity = duskSunIntensity,
                overcast = duskOvercast, horizonSharp = duskHorizonSharp,
                fogStart = duskFogStart, fogEnd = duskFogEnd,
            };
            var night = new Palette
            {
                zenith = nightZenith, horizon = nightHorizon, ground = nightGround,
                light = moonColor, intensity = moonLight,
                overcast = nightOvercast, horizonSharp = nightHorizonSharp,
                fogStart = nightFogStart, fogEnd = nightFogEnd,
            };

            // Full day once the sun is ~10 degrees up; full night once it is
            // ~1 degree under. Between the two the dusk palette is mixed in on
            // top, peaking exactly on the horizon — so golden hour is a band
            // around the crossing rather than a hard switch.
            float dayness = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(-0.02f, 0.18f, sunElevation));
            float glow = Mathf.Clamp01(1f - Mathf.Abs(sunElevation) / 0.17f);
            glow = glow * glow * (3f - 2f * glow);

            Night01 = 1f - dayness;
            return Palette.Lerp(Palette.Lerp(night, day, dayness), dusk, glow);
        }

        void Apply(float t)
        {
            float time01 = TimeOfDay.Time01;
            int day = TimeOfDay.Day;

            // Both bodies are computed and published every frame regardless of
            // which one the light is playing. The sky shader draws the sun
            // disc from _SS_SunDir and the moon from _SS_MoonDir; if they came
            // off the light instead, the sun would follow the moon all night.
            Vector3 sunDir = TimeOfDay.SunDirection(time01, latitudeDeg);
            Vector3 moonDir = TimeOfDay.MoonDirection(time01, latitudeDeg, day, synodicDays);
            float phase = TimeOfDay.MoonPhase01(day, synodicDays);
            SunDirection = sunDir;
            MoonDirection = moonDir;

            // A moon under the horizon lights nothing; a new moon still leaves
            // starlight rather than a void.
            float moonLight = moonIntensity
                            * Mathf.Lerp(newMoonFloor, 1f, phase)
                            * Mathf.Clamp01(moonDir.y / 0.12f);

            var p = ClearPalette(sunDir.y, moonLight);
            // The storm palette is authored as absolute colours, so blending
            // toward it discards the hour completely: at storminess 0.84 —
            // which is simply what the western deep reads — about four fifths
            // of the sky came from a fixed grey and the whole day/night cycle
            // was invisible to anyone sailing where the game actually starts
            // them. At full storm it was worse than invisible: stormHorizon is
            // 30% grey against a clear night's 5.5%, so midnight in a storm
            // rendered BRIGHTER than midnight in fair weather.
            //
            // The fix is the same shape as the one the key light already
            // needed: scale by the day's light level rather than replacing.
            // Hue stays the storm's — a storm at noon is exactly what it was —
            // and only the luminance follows the sun down.
            float stormLevel = Mathf.Lerp(1f, stormNightScale, Night01);
            var storm = new Palette
            {
                zenith = stormZenith * stormLevel,
                horizon = stormHorizon * stormLevel,
                ground = stormGround * stormLevel,
                light = stormSun, intensity = p.intensity,
                overcast = stormOvercast, horizonSharp = stormHorizonSharp,
                fogStart = stormFogStart, fogEnd = stormFogEnd,
            };
            p = Palette.Lerp(p, storm, t);

            // Storm dims the key light by a RATIO, not to a fixed value. An
            // absolute 0.34 would have made a midnight storm brighter than a
            // clear night, because the moon only gives 0.20.
            float stormDim = Mathf.Lerp(1f, stormSunIntensity / Mathf.Max(0.01f, clearSunIntensity), t);
            float intensity = p.intensity * stormDim;

            HorizonColor = p.horizon;

            if (skyInstance != null)
            {
                skyInstance.SetColor(ZenithId, p.zenith);
                skyInstance.SetColor(HorizonId, p.horizon);
                skyInstance.SetColor(GroundId, p.ground);
                skyInstance.SetColor(SunColorId, p.light);
                skyInstance.SetFloat(OvercastId, p.overcast);
                skyInstance.SetFloat(HorizonSharpId, p.horizonSharp);
                // Scud only tears past once it is genuinely blowing.
                skyInstance.SetFloat(ScudId, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, t)));
                skyInstance.SetFloat(CloudScaleId, Mathf.Lerp(cloudScaleClear, cloudScaleStorm, t));
                skyInstance.SetFloat(CloudSpeedId, Mathf.Lerp(cloudSpeedClear, cloudSpeedStorm, t));

                // Pushed every frame rather than left to the material. A .mat
                // snapshots a shader property's default at the moment the
                // property is CREATED and never sees it again — and the sky
                // material predates every one of these, so left alone they
                // would read zero and there would simply be no moon and no
                // stars, with nothing anywhere reporting a problem. This
                // project has paid for that lesson once already with
                // _StormDeep.
                skyInstance.SetColor(MoonColorId, moonColor);
                skyInstance.SetFloat(MoonSizeId, moonSize);
                skyInstance.SetFloat(MoonHaloId, moonHalo);
                skyInstance.SetFloat(StarBrightId, starBrightness);
                skyInstance.SetFloat(StarDensityId, starDensity);
            }

            // One directional light, two roles. URP shadows exactly one of
            // these, so this is the only way night gets shadows at all. The
            // swap happens with the sun a hair under the horizon, where the
            // blended intensity is already down at moon level — so it is a
            // change of direction in a dim light, not a flash.
            SunIsUp = sunDir.y > -0.02f;
            if (sun != null)
            {
                Vector3 toBody = SunIsUp ? sunDir : moonDir;
                if (toBody.sqrMagnitude > 1e-6f)
                    sun.transform.rotation = Quaternion.LookRotation(-toBody.normalized);
                sun.color = p.light;
                sun.intensity = Mathf.Max(0f, intensity);
                sun.shadowStrength = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(intensity / clearSunIntensity));
            }

            // Ambient: the sky lights the tops, the sea lights the undersides.
            // In a storm both go grey-green and drop hard, which is what stops
            // the hull and the crew reading as a sunny-day scene in the dark.
            // The sky term already darkens on its own because it follows the
            // horizon colour; the other two are fixed colours and need the
            // night scale applied by hand.
            float ambientScale = Mathf.Lerp(1f, nightAmbient, Night01);
            Color skyAmb = p.horizon * Mathf.Lerp(0.95f, 0.62f, t);
            RenderSettings.ambientSkyColor = skyAmb;
            RenderSettings.ambientEquatorColor = Color.Lerp(
                new Color(0.30f, 0.36f, 0.40f), new Color(0.115f, 0.135f, 0.135f), t) * ambientScale;
            RenderSettings.ambientGroundColor = Color.Lerp(
                new Color(0.10f, 0.13f, 0.14f), new Color(0.045f, 0.055f, 0.058f), t) * ambientScale;

            RenderSettings.fogColor = p.horizon;
            RenderSettings.fogStartDistance = p.fogStart;
            RenderSettings.fogEndDistance = p.fogEnd;

            var wind = SeaSick.Ocean.SeaStateController.Instance != null
                ? SeaSick.Ocean.SeaStateController.Instance.WindDirection : Vector2.right;
            Shader.SetGlobalVector(SkyWindId, new Vector4(wind.x, wind.y, 0f, 0f));
            Shader.SetGlobalVector(SkyHorizonId, p.horizon);
            Shader.SetGlobalFloat(StorminessId, t);

            Shader.SetGlobalVector(SunDirId, sunDir);
            Shader.SetGlobalVector(MoonDirId, moonDir);
            Shader.SetGlobalFloat(MoonPhaseId, phase);
            // Phrased so that an UNSET global (0) means broad daylight — the
            // _SS_LayerOff rule. Anything that reads this and never gets it
            // renders exactly what it rendered before night existed.
            Shader.SetGlobalFloat(NightId, Night01);
            // Sent alongside _SS_Night, never separately: the ocean reads all
            // three or none, and none means daylight.
            Shader.SetGlobalFloat(NightSeaDimId, nightSeaDim);
            Shader.SetGlobalFloat(NightSeaMixId, nightSeaSkyMix);

            // Where the stars turn. The sun arc is a rotation in the plane
            // spanned by east (1,0,0) and (0, cos lat, -sin lat), so its axis —
            // the celestial pole — is their cross product, (0, sin lat, cos
            // lat): due north, at an altitude equal to the latitude. Deriving
            // it from the same latitude the sun uses is the point; a hand-typed
            // pole would drift out of agreement with the sun the first time
            // that number changed.
            float lat = latitudeDeg * Mathf.Deg2Rad;
            Shader.SetGlobalVector(StarRotId, new Vector4(
                0f, Mathf.Sin(lat), Mathf.Cos(lat),
                (time01 - 0.25f) * 2f * Mathf.PI));
        }
    }
}
