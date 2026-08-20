using UnityEngine;

namespace SeaSick.Ocean
{
    /// Air full of water.
    ///
    /// A storm sea is not just taller — it is smoking. The tops of the waves
    /// are torn off and driven downwind as spindrift, and the whole surface
    /// disappears into a haze of it. Without that the sea reads as heavy syrup
    /// no matter how big the numbers get.
    ///
    /// Spindrift is emitted at MEASURED crests rather than at random points, so
    /// it appears where the water actually is highest. The threshold is found
    /// per frame from the samples themselves — no wave-amplitude API to keep in
    /// step, and it works the same at every sea state and every region scale.
    [RequireComponent(typeof(SeaSick.Ship.ShipMotor))]
    public class StormSpray : MonoBehaviour
    {
        [Header("Spindrift")]
        [Tooltip("Sea sampled per frame looking for crests to tear.")]
        [SerializeField] int samplesPerFrame = 24;
        [Tooltip("Radius of the sampled disc. Roughly what the camera holds.")]
        [SerializeField] float sampleRadius = 110f;
        [Tooltip("Sampling is biased ahead of the ship — that's where you look.")]
        [SerializeField] float sampleLead = 35f;
        // Self-calibrating crest threshold.
        //
        // The first version used an absolute margin in metres and MEASURED ONE
        // spindrift particle alive in a full storm. 6.5m above the local mean
        // was very nearly the absolute ceiling of a sea that turned out to be
        // heaving 12.4m peak to peak — not the 23.5m the storm reaches at its
        // worst. Tuning values are balanced against each other, never absolute.
        //
        // It is now a FRACTION of the sea's own spread, tracked across frames,
        // so it finds the tops at any sea state and any region scale with no
        // wave-spectrum API to keep in step with.
        [Range(0.1f, 0.95f)] [SerializeField] float crestFraction = 0.42f;
        [Tooltip("How fast the crest estimate follows a changing sea.")]
        [SerializeField] float calibrationRate = 0.7f;
        [Tooltip("Particles per second torn off the tops at full storm.")]
        [SerializeField] float spindriftRate = 110f;
        [Tooltip("Downwind speed of torn spray, calm..full storm.")]
        [SerializeField] Vector2 driftSpeed = new Vector2(9f, 24f);
        [SerializeField] int maxSpindrift = 260;

        [Header("Mist")]
        [Tooltip("Big, faint and few. These are overdraw — keep them cheap.")]
        [SerializeField] int maxMist = 16;
        [SerializeField] float mistRate = 4f;
        [SerializeField] float mistAlpha = 0.06f;

        [Header("Response")]
        [Tooltip("Nothing at all below this. Home water stays clean air.")]
        [SerializeField] float threshold = 0.18f;
        [Tooltip("Logs the emission budget once a second. Diagnostics only.")]
        [SerializeField] bool logDiagnostics;

        ParticleSystem spindrift, mist;
        SeaSick.Ship.ShipMotor motor;
        float mistDue, driftDue;
        Vector3[] samples;   // reused every frame; nothing allocated at sea
        float seaMean, seaCrest;
        bool calibrated;
        float dbgClock, dbgBudget;
        int dbgEmits, dbgAbove, dbgUpdates;

        /// Diagnostics — what the sea is doing and where the tops are judged
        /// to start. Absolute thresholds are what broke this the first time.
        public float SeaMean => seaMean;
        public float SeaCrest => seaCrest;
        public float CrestThreshold => seaMean + (seaCrest - seaMean) * crestFraction;
        public int Emitted { get; private set; }

        void Start()
        {
            motor = GetComponent<SeaSick.Ship.ShipMotor>();
            samples = new Vector3[Mathf.Clamp(samplesPerFrame, 2, 32)];
            BuildSystems();
        }

        void BuildSystems()
        {
            // Spindrift: bright, thin, stretched along its own velocity so it
            // reads as a streak of torn water rather than a snowflake.
            var streak = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            streak.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
            streak.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(streak, 3010);

            var go = new GameObject("Spindrift");
            go.transform.SetParent(transform, false);
            spindrift = go.AddComponent<ParticleSystem>();
            var main = spindrift.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxSpindrift;
            main.startLifetime = 1.6f;
            main.startSpeed = 0f;          // velocity is supplied per particle
            main.startSize = 0.5f;
            main.gravityModifier = 0.9f;   // it falls back into the sea
            var emission = spindrift.emission;
            emission.enabled = false;      // emitted by hand, at crests
            var fade = spindrift.colorOverLifetime;
            fade.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.15f),
                        new GradientAlphaKey(0f, 1f) });
            fade.color = grad;
            var sr = spindrift.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = streak;
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            // Narrow and long. The first pass was 3m wide and stretched to
            // 8m, which read as cotton balls resting on the water rather than
            // water being torn off the top of a wave.
            sr.velocityScale = 0.30f;
            sr.lengthScale = 1.0f;
            sr.alignment = ParticleSystemRenderSpace.World;

            // Mist: a few very large, very faint puffs lying on the water. This
            // is what puts distance between the ship and the next wave.
            var haze = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            haze.SetColor("_BaseColor", new Color(0.86f, 0.89f, 0.91f, 1f));
            haze.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(haze, 3005);

            var mgo = new GameObject("SeaMist");
            mgo.transform.SetParent(transform, false);
            mist = mgo.AddComponent<ParticleSystem>();
            var mmain = mist.main;
            mmain.simulationSpace = ParticleSystemSimulationSpace.World;
            mmain.maxParticles = maxMist;
            mmain.startLifetime = 9f;
            mmain.startSpeed = 0f;
            mmain.startSize = 20f;
            mmain.gravityModifier = 0f;
            var memission = mist.emission;
            memission.enabled = false;
            var mfade = mist.colorOverLifetime;
            mfade.enabled = true;
            var mgrad = new Gradient();
            mgrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f),
                        new GradientAlphaKey(0f, 1f) });
            mfade.color = mgrad;
            var mr = mist.GetComponent<ParticleSystemRenderer>();
            mr.sharedMaterial = haze;
            mr.renderMode = ParticleSystemRenderMode.Billboard;
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

        void Update()
        {
            var sky = SeaSick.World.SkyDirector.Instance;
            float storm = sky != null ? sky.Storminess01 : 0f;
            if (storm < threshold) return;

            var field = WaveField.Instance;
            if (field == null) return;

            // Above the threshold, ramp the whole effect in over the next
            // stretch so it arrives with the weather instead of switching on.
            float t = Mathf.InverseLerp(threshold, 1f, storm);

            Vector2 wind = field.WindDirection;
            Vector3 wind3 = new Vector3(wind.x, 0f, wind.y);
            Vector3 centre = transform.position + transform.forward * sampleLead;

            dbgUpdates++;
            TearCrests(field, centre, wind3, t);
            DriftMist(field, centre, wind3, t);

            if (!logDiagnostics) return;
            dbgClock += Time.deltaTime;
            if (dbgClock >= 1f)
            {
                Debug.Log($"SPRAYDBG updates/s {dbgUpdates}  emits/s {dbgEmits}  " +
                          $"above/s {dbgAbove}  budget added/s {dbgBudget:F1}  " +
                          $"driftDue {driftDue:F2}  dt {Time.deltaTime:F4}  " +
                          $"rate {spindriftRate} t {t:F2}  alive {spindrift.particleCount}");
                dbgClock = 0f; dbgUpdates = 0; dbgEmits = 0; dbgAbove = 0; dbgBudget = 0f;
            }
        }

        /// Sample the sea around the ship, work out where the tops are from the
        /// samples themselves, and throw the tops downwind.
        void TearCrests(WaveField field, Vector3 centre, Vector3 wind3, float t)
        {
            int n = samples.Length;
            float sum = 0f, top = -99999f;
            int found = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 off = Random.insideUnitCircle * sampleRadius;
                Vector2 p = new Vector2(centre.x + off.x, centre.z + off.y);
                float h = field.SampleHeightFast(p, Time.time);
                sum += h;
                if (h > top) top = h;
                samples[found++] = new Vector3(p.x, h, p.y);
            }
            if (found == 0) return;

            // Where the sea's middle and its tops are, averaged over frames so
            // a handful of samples doesn't make the threshold jump about.
            float frameMean = sum / found;
            if (!calibrated) { seaMean = frameMean; seaCrest = top; calibrated = true; }
            float k = 1f - Mathf.Exp(-calibrationRate * Time.deltaTime);
            seaMean = Mathf.Lerp(seaMean, frameMean, k);
            seaCrest = Mathf.Lerp(seaCrest, top, k);
            float threshold = CrestThreshold;

            // How much spray there is, and where it comes from, are separate
            // questions: the samples find the tops, the budget decides how hard
            // it is blowing.
            // Unspent budget must EVAPORATE, not bank.
            //
            // Without the ceiling this accumulator reached 178 particles of
            // credit while the sea happened to be quiet, then dumped the lot
            // the moment the crests came back — measured at 681 particles/s
            // against a 110/s budget. It also made the whole effect frame-rate
            // dependent, since the dump rate is samplesPerFrame x fps.
            float add = spindriftRate * t * Time.deltaTime;
            driftDue = Mathf.Min(driftDue + add, add * 2f + 1f);
            dbgBudget += add;
            float speed = Mathf.Lerp(driftSpeed.x, driftSpeed.y, t);

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < found && driftDue >= 1f; i++)
            {
                if (samples[i].y < threshold) continue;
                driftDue -= 1f;
                Emitted++;
                dbgEmits++; dbgAbove++;

                ep.position = samples[i] + Vector3.up * 0.6f;
                ep.velocity = wind3 * (speed * Random.Range(0.75f, 1.25f))
                            + Vector3.up * Random.Range(1.5f, 5f);
                ep.startLifetime = Random.Range(0.9f, 1.8f);
                ep.startSize = Random.Range(0.25f, 0.75f) * Mathf.Lerp(0.8f, 1.3f, t);
                ep.startColor = Color.white;
                spindrift.Emit(ep, 1);
            }
        }

        void DriftMist(WaveField field, Vector3 centre, Vector3 wind3, float t)
        {
            mistDue += mistRate * t * Time.deltaTime;
            if (mistDue < 1f) return;
            int count = Mathf.FloorToInt(mistDue);
            mistDue -= count;

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                // Kept well off the lens: a 25m puff born on top of the
                // camera is a grey wall, however soft its edges are.
                Vector2 off = Random.insideUnitCircle.normalized
                            * Random.Range(sampleRadius * 0.7f, sampleRadius * 1.7f);
                Vector2 p = new Vector2(centre.x + off.x, centre.z + off.y);
                float h = field.SampleHeightFast(p, Time.time);

                ep.position = new Vector3(p.x, h + Random.Range(0f, 9f), p.y);
                ep.velocity = wind3 * Random.Range(3f, 7f);
                ep.startLifetime = Random.Range(7f, 11f);
                ep.startSize = Random.Range(24f, 46f);
                ep.startColor = new Color(1f, 1f, 1f, mistAlpha * t);
                mist.Emit(ep, 1);
            }
        }
    }
}
