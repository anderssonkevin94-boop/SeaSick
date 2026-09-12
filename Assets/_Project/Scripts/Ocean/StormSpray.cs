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

        [Header("Breaking crests")]
        // Spindrift is WEATHER: the wind tearing the tops off, and it belongs
        // behind a storminess gate. A crest folding over on itself and
        // throwing its own top forward is not weather -- it happens in any sea
        // with wind in it, and gating the whole component on `Storminess01`
        // is why the seas the game is actually sailed in carry no white water
        // at all. These two share the scan and nothing else.
        //
        // The trigger is the OCEAN'S OWN FOAM FIELD, not a guess: `sample.foam`
        // is the cascade-0 turbulence buffer read back to the CPU, which is
        // the same number the surface shader whitens itself with. So a spume
        // burst can only happen where the water is already drawn breaking, and
        // the two can never disagree about where the sea is folding.
        // A FRACTION of the foam this sea is actually making, not a count.
        //
        // The first version was an absolute 0.22 on `sample.foam`, tuned when
        // the persistent buffer was saturated and sat at 0.3-0.4 over the whole
        // ocean. Making the buffer selective dropped it to 0.05 in a 14 m sea,
        // and the gate promptly became unreachable: 0.5 crests a second and
        // five particles alive, in a sea that is visibly breaking. Same
        // mistake, third time in this file's history -- the spindrift crest
        // threshold started as an absolute 6.5 m and put ONE particle on a full
        // storm. A threshold into the sea is a fraction of the sea's own scale.
        [Tooltip("Fraction of the peak foam this sea is currently making, above which a " +
                 "crest counts as breaking. Relative, so it survives any retune of the " +
                 "foam buffer — which an absolute number did not.")]
        [Range(0.1f, 0.95f)] [SerializeField] float foamGateFraction = 0.55f;
        [Tooltip("Absolute floor, so glassy water makes nothing however you scale a fraction of it.")]
        [SerializeField] float foamGateFloor = 0.02f;
        [Tooltip("Breaking crests marked per second, at most.")]
        [SerializeField] float breakerRate = 7f;
        [Tooltip("Particles thrown along one crest. A breaker is a LINE, not a point.")]
        [SerializeField] int spumePerCrest = 14;
        [Tooltip("How far along the crest the line is spread, as a fraction of the sample radius.")]
        [Range(0.02f, 0.5f)] [SerializeField] float crestSpread = 0.13f;
        [SerializeField] int maxSpume = 420;
        [Tooltip("Nothing is thrown closer to the ship than this MANY HULL LENGTHS — " +
                 "her own bow and beam spray owns that water. A fixed count of metres " +
                 "would be a hole around a skiff and inside a three-decker.")]
        [SerializeField] float hullClearLoa = 0.6f;

        [Header("Mist")]
        // Big, faint and FEW, and "few" is a fill-rate number, not a taste one.
        // Sixteen puffs 24-46 m across, born 77-187 m out, put five to ten
        // layers of near-full-screen alpha blend over the ocean on a portrait
        // phone -- the ocean shader paid over again per layer. Halved to eight
        // and pushed out to 110-264 m: the same haze between the ship and the
        // next wave, a smaller share of the screen each, and fewer of them
        // stacked on any one pixel.
        [Tooltip("Big, faint and few. These are overdraw — keep them cheap. Capped at " +
                 "MistCeiling however it is authored; the cap is a fill-rate limit, not taste.")]
        [SerializeField] int maxMist = 8;
        /// The fill-rate ceiling, and it is a CEILING rather than just a
        /// default because `maxMist` is serialized into Sea.unity at 16 — the
        /// same trap as SeaStateController's `rebuildHz`, where editing the
        /// field initialiser changes nothing about the game that ships. Eight
        /// is what the sea gets until someone pushes a new value through
        /// SerializedObject, and then it is still eight.
        const int MistCeiling = 8;
        [SerializeField] float mistRate = 4f;
        [SerializeField] float mistAlpha = 0.06f;

        [Header("Response")]
        [Tooltip("Nothing at all below this. Home water stays clean air.")]
        [SerializeField] float threshold = 0.18f;
        [Tooltip("Logs the emission budget once a second. Diagnostics only.")]
        [SerializeField] bool logDiagnostics;

        ParticleSystem spindrift, mist, spume;
        SeaSick.Ship.ShipMotor motor;
        float mistDue, driftDue;
        Vector3[] samples;   // reused every frame; nothing allocated at sea
        float[] sampleFoam;  // the ocean's foam at that same spot, same frame
        // The crest scan rides the physics driver's ONE batched Burst query
        // per step instead of 24 main-thread `SampleImmediate` calls per
        // frame. Measured before this: StormSpray.Update 1.0 ms a frame on
        // the Mac in a storm -- the whole of the heavy-water CPU cost, and on
        // a phone several times that. The registry doc names "spray scans"
        // as exactly what it is for; this was the one consumer not using it.
        //
        // Each handle is moved to a fresh random spot only once the driver
        // has sampled the spot it was last given (`sampledFrame` moves), so
        // the (position, height) pair read here always belongs together.
        OceanProbeRegistry.Handle[] handles;
        int[] seenFrame;
        float seaMean, seaCrest;
        int found;
        float breakerDue;
        bool calibrated;
        float dbgClock, dbgBudget;
        int dbgEmits, dbgAbove, dbgUpdates;

        /// Diagnostics — what the sea is doing and where the tops are judged
        /// to start. Absolute thresholds are what broke this the first time.
        public float SeaMean => seaMean;
        public float SeaCrest => seaCrest;
        public float CrestThreshold => seaMean + (seaCrest - seaMean) * crestFraction;
        public int Emitted { get; private set; }
        /// Diagnostics — breaking crests marked, and the peak foam the scan is
        /// seeing. If `PeakFoam` never reaches `foamGate` the sea is not
        /// folding as far as the simulation is concerned, and the fix is the
        /// spectrum's `foamThreshold`, not this number.
        public int Breakers { get; private set; }
        public float PeakFoam { get; private set; }
        /// The gate as it stands right now, in the same units as `PeakFoam`.
        public float FoamGate => Mathf.Max(foamGateFloor, foamGateFraction * PeakFoam);
        public float FoamGateFraction
        {
            get => foamGateFraction;
            set => foamGateFraction = Mathf.Clamp(value, 0.1f, 0.95f);
        }
        public float BreakerRate { get => breakerRate; set => breakerRate = Mathf.Max(0f, value); }
        public int SpumePerCrest { get => spumePerCrest; set => spumePerCrest = Mathf.Clamp(value, 1, 40); }
        public int SpumeAlive => spume != null ? spume.particleCount : 0;
        public int SpindriftAlive => spindrift != null ? spindrift.particleCount : 0;

        void Start()
        {
            motor = GetComponent<SeaSick.Ship.ShipMotor>();
            samples = new Vector3[Mathf.Clamp(samplesPerFrame, 2, 32)];
            sampleFoam = new float[samples.Length];
            BuildSystems();
        }

        /// Registered lazily and re-checked every frame rather than once in
        /// Start: a domain reload mid-play empties the registry's static list
        /// and nulls this plain array while the component itself survives
        /// (the documented asymmetry), so "did Start run" is not "is my state
        /// whole".
        void EnsureHandles(Vector3 centre)
        {
            if (handles != null && handles.Length == samples.Length
                && OceanProbeRegistry.Handles.Count > 0) return;
            handles = new OceanProbeRegistry.Handle[samples.Length];
            seenFrame = new int[samples.Length];
            for (int i = 0; i < handles.Length; i++)
            {
                Vector2 off = Random.insideUnitCircle * sampleRadius;
                handles[i] = OceanProbeRegistry.Register(
                    new Vector3(centre.x + off.x, 0f, centre.z + off.y));
            }
        }

        void OnDestroy()
        {
            if (handles == null) return;
            for (int i = 0; i < handles.Length; i++)
                OceanProbeRegistry.Unregister(handles[i]);
            handles = null;
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
            mmain.maxParticles = Mathf.Clamp(maxMist, 1, MistCeiling);
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

            // Spume: the water a crest throws off itself as it folds. It is
            // the opposite of spindrift in every way that matters -- heavy
            // instead of blown, tumbling instead of streaked, falling back
            // onto the face instead of driving downwind -- so it is its own
            // system with its own material rather than a second emit into the
            // spindrift one.
            var chunk = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            chunk.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
            chunk.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(chunk, 3008);

            var sgo = new GameObject("CrestSpume");
            sgo.transform.SetParent(transform, false);
            spume = sgo.AddComponent<ParticleSystem>();
            var smain = spume.main;
            smain.simulationSpace = ParticleSystemSimulationSpace.World;
            smain.maxParticles = maxSpume;
            smain.startLifetime = 1.4f;
            smain.startSpeed = 0f;        // supplied per particle
            smain.startSize = 1.5f;
            smain.gravityModifier = 1.15f; // it falls back down the face
            var semission = spume.emission;
            semission.enabled = false;     // emitted by hand, at breaking crests
            // Grows as it is thrown, then dies -- a fold throws a curtain that
            // opens. Shrinking it instead reads as a puff of smoke.
            var ssize = spume.sizeOverLifetime;
            ssize.enabled = true;
            ssize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1.1f), new Keyframe(1f, 0.85f)));
            var srot = spume.rotationOverLifetime;
            srot.enabled = true;
            srot.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
            var sfade = spume.colorOverLifetime;
            sfade.enabled = true;
            var sgrad = new Gradient();
            sgrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f),
                        new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
            sfade.color = sgrad;
            var spr = spume.GetComponent<ParticleSystemRenderer>();
            spr.sharedMaterial = chunk;
            spr.renderMode = ParticleSystemRenderMode.Billboard;
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
            if (!OceanSampler.Ready) return;
            var sky = SeaSick.World.SkyDirector.Instance;
            float storm = sky != null ? sky.Storminess01 : 0f;

            // Above the threshold, ramp the weather in over the next stretch
            // so it arrives with the storm instead of switching on.
            float t = Mathf.InverseLerp(threshold, 1f, storm);

            Vector2 wind = SeaStateController.Instance != null
                ? SeaStateController.Instance.WindDirection : Vector2.right;
            Vector3 wind3 = new Vector3(wind.x, 0f, wind.y);
            Vector3 centre = transform.position + transform.forward * sampleLead;

            dbgUpdates++;
            EnsureHandles(centre);
            // The scan is shared and runs at every sea state. Breaking crests
            // are a property of the WATER and come off the ocean's own foam
            // field; spindrift and mist are a property of the WEATHER and stay
            // behind the storminess gate below.
            ScanSea(centre);
            BreakCrests(wind3);

            if (storm < threshold) return;
            TearCrests(wind3, t);
            DriftMist(centre, wind3, t);

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

        /// Sample the sea around the ship and work out, from the samples
        /// themselves, where its middle and its tops are. Everything that
        /// throws a particle reads this and nothing re-samples.
        void ScanSea(Vector3 centre)
        {
            int n = samples.Length;
            float sum = 0f, top = -99999f, peakFoam = 0f;
            found = 0;
            for (int i = 0; i < n; i++)
            {
                var hd = handles[i];
                if (hd.sampledFrame != seenFrame[i])
                {
                    // Fresh: the sample belongs to the spot the handle holds.
                    // Bank the triple, then send the handle somewhere new.
                    samples[i] = new Vector3(hd.position.x, hd.sample.height, hd.position.z);
                    sampleFoam[i] = hd.sample.foam;
                    seenFrame[i] = hd.sampledFrame;
                    Vector2 off = Random.insideUnitCircle * sampleRadius;
                    hd.position = new Vector3(centre.x + off.x, 0f, centre.z + off.y);
                }
                if (seenFrame[i] == 0) continue;   // never sampled yet
                float h = samples[i].y;
                sum += h;
                if (h > top) top = h;
                if (sampleFoam[i] > peakFoam) peakFoam = sampleFoam[i];
                found++;
            }
            if (found == 0) return;

            // Where the sea's middle and its tops are, averaged over frames so
            // a handful of samples doesn't make the threshold jump about.
            float frameMean = sum / found;
            if (!calibrated) { seaMean = frameMean; seaCrest = top; calibrated = true; }
            float k = 1f - Mathf.Exp(-calibrationRate * Time.deltaTime);
            seaMean = Mathf.Lerp(seaMean, frameMean, k);
            seaCrest = Mathf.Lerp(seaCrest, top, k);
            PeakFoam = Mathf.Lerp(PeakFoam, peakFoam, k);
        }

        /// Throw the tops downwind. Storm weather only.
        void TearCrests(Vector3 wind3, float t)
        {
            if (found == 0) return;
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
            for (int i = 0; i < samples.Length && driftDue >= 1f; i++)
            {
                if (seenFrame[i] == 0) continue;
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

        /// White water where the sea is actually folding over.
        ///
        /// A breaking crest is a LINE, not a point — one fold running across
        /// the wave — so a hit throws a row of spume along the crest rather
        /// than a puff at the sample. That is also what makes 24 samples a
        /// frame enough: the scan only has to FIND a breaker, and the line it
        /// draws is what you actually see. A puff per sample would need an
        /// order of magnitude more sampling to read as anything at all.
        void BreakCrests(Vector3 wind3)
        {
            if (found == 0 || spume == null) return;

            // Evaporates, never banks — the same rule the spindrift budget had
            // to learn after it dumped 178 particles of banked credit the
            // moment the crests came back.
            float add = breakerRate * Time.deltaTime;
            breakerDue = Mathf.Min(breakerDue + add, add * 2f + 1f);
            if (breakerDue < 1f) return;

            // How big the thrown water is, as a fraction of the sea's OWN
            // measured spread rather than a count of metres — the rule the
            // spindrift threshold and the shader's colour ramp both had to
            // learn. A 3 m sea folds in handfuls, a 60 m one in cartloads.
            float gate = FoamGate;
            float spread = Mathf.Max(0.4f, seaCrest - seaMean);
            float size = Mathf.Clamp(spread * 0.14f, 0.5f, 6f);
            float lineLen = sampleRadius * crestSpread;
            // The wave runs downwind, so the crest lies across the wind.
            Vector3 along = new Vector3(-wind3.z, 0f, wind3.x);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.right;
            along.Normalize();

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < samples.Length && breakerDue >= 1f; i++)
            {
                if (seenFrame[i] == 0) continue;
                if (sampleFoam[i] < gate) continue;
                // Her own bow and beam spray owns the water alongside her. A
                // second system throwing foam into it is one of the ways white
                // water ends up somewhere it has no business being.
                Vector3 at = samples[i];
                Vector3 d = at - transform.position; d.y = 0f;
                float clear = hullClearLoa * (motor != null ? motor.HullLength : 24.2f);
                if (d.sqrMagnitude < clear * clear) continue;

                breakerDue -= 1f;
                Breakers++;

                // Strength is also relative: at the gate it is a slap, at the
                // peak the sea is making it is a wall.
                float strength = Mathf.InverseLerp(gate, Mathf.Max(gate * 1.6f, PeakFoam),
                                                   sampleFoam[i]);
                int count = Mathf.Max(3,
                    Mathf.RoundToInt(spumePerCrest * (0.45f + 0.55f * strength)));
                for (int k = 0; k < count; k++)
                {
                    float u = k / (float)(count - 1) - 0.5f;
                    Vector3 p = at + along * (u * lineLen) + Vector3.up * (size * 0.3f);
                    ep.position = p + Random.insideUnitSphere * (size * 0.35f);
                    // Thrown forward off the face and up, and it falls back
                    // down the face — the gravity on the system is what makes
                    // it read as water with mass rather than as blown spray.
                    ep.velocity = wind3 * ((2.5f + 5f * strength) * Random.Range(0.6f, 1.4f))
                                + Vector3.up * (Random.Range(1.2f, 4.5f) * (0.6f + strength))
                                + along * Random.Range(-1.2f, 1.2f);
                    ep.startLifetime = Random.Range(0.9f, 1.9f);
                    ep.startSize = size * Random.Range(0.6f, 1.5f);
                    ep.startColor = Color.white;
                    spume.Emit(ep, 1);
                }
            }
        }

        void DriftMist(Vector3 centre, Vector3 wind3, float t)
        {
            mistDue += mistRate * t * Time.deltaTime;
            if (mistDue < 1f) return;
            int count = Mathf.FloorToInt(mistDue);
            mistDue -= count;

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                // Kept well off the lens: a 25m puff born on top of the
                // camera is a grey wall, however soft its edges are. The band
                // is 1.4x further out than it was (77-187 m -> 110-264 m), so
                // each puff covers less of the screen and fewer of them
                // overlap on one pixel -- see maxMist.
                Vector2 off = Random.insideUnitCircle.normalized
                            * Random.Range(sampleRadius * 1.0f, sampleRadius * 2.4f);
                Vector2 p = new Vector2(centre.x + off.x, centre.z + off.y);
                float h = OceanSampler.SampleImmediate(new Vector3(p.x, 0f, p.y)).height;

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
