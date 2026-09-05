using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// Floats all five progression hulls beside the ship the game already has,
/// through five sea states, and MEASURES what happens to them.
///
/// The point is not the picture. A hull shape is a guess until something has
/// tried to sink it: the raft should be unusable in anything but a calm, the
/// three-decker should barely notice a sea that swamps the sloop, and if that
/// ordering does not come out of the physics then the sizes are wrong and no
/// amount of looking at them in a lab scene would have said so.
///
/// **Masses are the honest ones** — 1025 kg/m^3 times the volume each hull
/// actually displaces at her drawn waterline, integrated station by station in
/// `~/blender_objects/seasick_hulls.py`.
///
/// The paddle steamer is now on the same rule. She carried 19.2 t against a
/// hull that displaces 58.5 t, and standing her in this row is what made that
/// readable rather than arguable: through all five sea states she moved less
/// than a metre while the fleet rolled past 30 degrees. She was re-massed to
/// her measured displacement on 2026-09-02 and given the damping law below,
/// so the baseline row is no longer a different kind of body — it is the
/// sixth hull, and any difference left in it is a difference of SHAPE.
///
/// `totalVolume` is then set to hold the float ratio at the 0.60 the probe rig
/// was tuned at, because that ratio — not the absolute volume — is what the
/// buoyancy model actually keys on.
///
/// Run in play mode in Sea.unity: `RunProbe.Float()`.
/// Writes /tmp/seasick-float.txt and /tmp/seasick-float-<state>.png.
public class HullFloatProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HullFloatProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<HullFloatProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("HullFloatProbe").AddComponent<HullFloatProbe>();
    }

    const string ArtDir = "Assets/_Project/Art/Ship/Hulls";
    const float Rho = 1025f;
    /// The float ratio the probe rig was tuned at, mass / (rho * totalVolume).
    const float FloatRatio = 0.60f;
    /// The mass and length the drag coefficients below were TUNED at. This was
    /// the paddle steamer's mass until 2026-09-02 and is no longer — she is
    /// 58.5 t now — but the anchor must not move with her, or every hull's
    /// damping shifts whenever she is re-measured. `SetupPaddleBoat.TunedMass`
    /// is the same anchor; the two have to stay equal.
    const float RefMass = 19200f;
    const float RefLoa = 24.2f;
    const float Gap = 8f;
    /// Fraction of the draft the probe rig sits ABOVE the drawn keel. See the
    /// note in Build(): measured across the fleet, not chosen.
    const float ProbeLiftRatio = 0.55f;

    struct Spec
    {
        public string file, label;
        public float loa, beam, draft, depth, dispVol;
        public Spec(string f, string l, float lo, float b, float dr, float de, float v)
        { file = f; label = l; loa = lo; beam = b; draft = dr; depth = de; dispVol = v; }
    }

    // dispVol: cubic metres below the drawn waterline, from hydrostatics().
    static readonly Spec[] Fleet =
    {
        new Spec("hull_t1_raft",          "T1 raft",       5.65f,  3.40f, 0.29f,  0.61f,    4.33f),
        new Spec("hull_t2_skiff",         "T2 skiff",      9.00f,  2.90f, 0.55f,  1.45f,    5.20f),
        new Spec("hull_t3_sloop",         "T3 sloop",     15.00f,  4.80f, 1.35f,  2.55f,   45.13f),
        // Every hull moved on 2026-09-02 and none of them changed SHAPE. Their
        // underwater loops used to be spaced evenly in HEIGHT, which cut the
        // corner at the turn of the bilge and understated the section; spaced
        // by arc length instead, the polygon follows the curve. The skiff
        // barely moved (5.19 -> 5.20) because she is nearly a wedge; the
        // three-decker moved most (1835.11 -> 1880.80, +2.5%) because she has
        // the fullest bilge in the fleet. These are the truer numbers.
        new Spec("hull_t4_brig",          "T4 brig",      26.00f,  7.80f, 2.50f,  5.20f,  269.56f),
        new Spec("hull_t5_shipoftheline", "T5 3-decker",  46.00f, 13.00f, 5.40f, 12.40f, 1880.80f),
    };

    struct SeaState { public string name; public float hs; public SeaState(string n, float h) { name = n; hs = h; } }
    static readonly SeaState[] States =
    {
        new SeaState("calm",  1.2f),
        new SeaState("lively", 3.5f),
        new SeaState("rough",  9.0f),
        new SeaState("heavy", 20.0f),
        new SeaState("wild",  40.0f),
    };

    class Rig
    {
        public string label;
        public Transform t;
        public Rigidbody rb;
        public BuoyantBody body;
        public BuoyancyProbeSet probes;
        public float loa, beam, draft, depth, railY, deckY, mass;
        public float probeKeelY;
        public bool baseline;               // the ship already in the game

        public int n;
        public double ySum, ySqSum, rollSum, rollSqSum, pitchSum, pitchSqSum, accSum;
        public float rollMax, pitchMax, yMin, yMax, railMax, accMax;
        public int railUnder, buried, deckUnder;
        public float prevVy;

        public void Reset()
        {
            n = 0; ySum = ySqSum = rollSum = rollSqSum = pitchSum = pitchSqSum = accSum = 0;
            rollMax = pitchMax = railMax = accMax = 0f;
            yMin = float.MaxValue; yMax = float.MinValue;
            railUnder = buried = deckUnder = 0;
            prevVy = rb != null ? rb.linearVelocity.y : 0f;
        }

        public void Sample(float dt)
        {
            float rel = t.position.y - body.MeanWaterHeight;
            float roll = Mathf.Asin(Mathf.Clamp(t.right.y, -1f, 1f)) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float vy = rb.linearVelocity.y;
            float acc = dt > 1e-5f ? (vy - prevVy) / dt : 0f;
            prevVy = vy;

            n++;
            ySum += rel; ySqSum += rel * rel;
            rollSum += Mathf.Abs(roll); rollSqSum += roll * roll;
            pitchSum += Mathf.Abs(pitch); pitchSqSum += pitch * pitch;
            accSum += Mathf.Abs(acc);
            if (Mathf.Abs(roll) > rollMax) rollMax = Mathf.Abs(roll);
            if (Mathf.Abs(pitch) > pitchMax) pitchMax = Mathf.Abs(pitch);
            if (rel < yMin) yMin = rel;
            if (rel > yMax) yMax = rel;
            if (Mathf.Abs(acc) > accMax) accMax = Mathf.Abs(acc);
            float rail = body.MaxRailImmersion;
            if (rail > railMax) railMax = rail;
            if (rail > 0f) railUnder++;
            if (body.Buried) buried++;
            // green water ON the deck, not just over the rail line
            if (body.MeanWaterHeight - (t.position.y + deckY) > 0f) deckUnder++;
        }

        public float MeanY => n > 0 ? (float)(ySum / n) : 0f;
        public float HeaveRms => n > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(ySqSum / n) - MeanY * MeanY)) : 0f;
        public float RollRms => n > 0 ? Mathf.Sqrt((float)(rollSqSum / n)) : 0f;
        public float PitchRms => n > 0 ? Mathf.Sqrt((float)(pitchSqSum / n)) : 0f;
        public float AccMean => n > 0 ? (float)(accSum / n) : 0f;
        public float RailPct => n > 0 ? 100f * railUnder / n : 0f;
        public float DeckPct => n > 0 ? 100f * deckUnder / n : 0f;
        public float BuriedPct => n > 0 ? 100f * buried / n : 0f;
    }

    readonly List<Rig> rigs = new List<Rig>();
    GameObject root;
    Camera cam;
    Vector3 site;
    float rowSpan;

    IEnumerator Start()
    {
        var sb = new StringBuilder("=== HullFloatProbe ===\n");
        var sea = SeaStateController.Instance;
        if (sea == null || !OceanSampler.Ready)
        {
            Debug.LogError("HullFloatProbe: no sea controller / sampler");
            yield break;
        }

        TimeOfDay.SetTime01(0.36f);
        TimeOfDay.Paused = true;

        // Open water. The region envelope flattens the sea near land, so a
        // float test run off the home island would be measuring shelter.
        var motor = FindAnyObjectByType<ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        site = home + new Vector3(2600f, 0f, 0f);
        site.y = 0f;
        float env = RegionField.Instance != null
            ? RegionField.Instance.Evaluate(new Vector2(site.x, site.z)) : 1f;
        sb.AppendLine($"site {site.x:F0},{site.z:F0} — {Vector3.Distance(home, site):F0} m from home, "
                      + $"region envelope {env:F2} (1.00 = full open sea)");
        if (sea.Follow != null) sea.Follow = null;

        root = new GameObject("FloatRow");
        root.transform.position = site;

        // The row runs across the world X axis; every hull's bow is on +Z, so
        // the relative wave heading is whatever the sea is doing and gets
        // REPORTED rather than assumed.
        float x = 0f;

        // The ship already in the game, first in the row, untouched.
        if (motor != null)
        {
            var pb = motor.GetComponent<BuoyantBody>();
            var prb = motor.GetComponent<Rigidbody>();
            if (pb != null && prb != null)
            {
                x += 8.44f * 0.5f + Gap;
                motor.transform.position = new Vector3(site.x + x, motor.transform.position.y, site.z);
                motor.transform.rotation = Quaternion.identity;
                prb.linearVelocity = Vector3.zero;
                prb.angularVelocity = Vector3.zero;
                prb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
                rigs.Add(new Rig
                {
                    label = "-- paddle steamer (in game)", t = motor.transform, rb = prb,
                    body = pb, probes = motor.GetComponent<BuoyancyProbeSet>(),
                    loa = RefLoa, beam = 8.44f, draft = 1.02f, depth = 4.2f,
                    railY = 2.2f, deckY = 1.23f * 2f, mass = prb.mass, baseline = true,
                });
                x += 8.44f * 0.5f;
                sb.AppendLine($"baseline: paddle steamer {prb.mass / 1000f:F1} t");
            }
        }

        foreach (var s in Fleet)
        {
            x += s.beam * 0.5f + Gap;
            var rig = Build(s, new Vector3(site.x + x, 0f, site.z), sb);
            if (rig != null) rigs.Add(rig);
            x += s.beam * 0.5f;
        }
        rowSpan = x;

        BuildSurfaceMarkers();

        cam = new GameObject("FloatCam").AddComponent<Camera>();
        var main = Camera.main;
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = 6000f;

        yield return new WaitForSeconds(1f);

        // --- solve the resting waterline -----------------------------------
        // She must float where she was DRAWN to float. The rig is a stand-in
        // for the underwater volume, not an outline of it, so the two do not
        // agree by construction — and the sign is the counter-intuitive one
        // the paddle steamer already cost a build to learn: RAISING the probe
        // rig makes the hull sit LOWER, because the probes are then shallower
        // for a given hull position and make less lift.
        sea.ForceHs(1.0f);
        yield return new WaitForSeconds(6f);
        sb.AppendLine("\n--- resting waterline solve (calm) ---");
        for (int pass = 0; pass < 4; pass++)
        {
            foreach (var r in rigs) r.Reset();
            float t0 = Time.time;
            while (Time.time - t0 < 3.5f)
            {
                yield return new WaitForFixedUpdate();
                foreach (var r in rigs) r.Sample(Time.fixedDeltaTime);
            }
            var line = new StringBuilder($"  pass {pass}: ");
            bool done = true;
            foreach (var r in rigs)
            {
                float err = r.MeanY;
                line.Append($"{r.label.Substring(0, Mathf.Min(9, r.label.Length))} {err,6:F2}  ");
                if (r.baseline) continue;
                if (Mathf.Abs(err) > 0.02f) done = false;
                if (pass < 3)
                {
                    r.probeKeelY += err;
                    r.probes.SetProbes(BuoyancyProbeSet.FleetLayout(
                        r.loa * 0.86f, r.beam * 0.9f, r.draft, r.probeKeelY, r.railY));
                }
            }
            sb.AppendLine(line.ToString());
            if (done) break;
        }
        foreach (var r in rigs)
            if (!r.baseline)
                sb.AppendLine($"  {r.label,-14} probe keel {r.probeKeelY,6:F2} m "
                              + $"(drawn keel {-r.draft,5:F2}, lift {r.probeKeelY + r.draft,5:F2})");

        // --- the sea states -------------------------------------------------
        foreach (var st in States)
        {
            sea.ForceHs(st.hs);
            yield return new WaitForSeconds(14f);       // let the spectrum and the hulls catch up

            foreach (var r in rigs) r.Reset();
            float t0 = Time.time;
            while (Time.time - t0 < 26f)
            {
                yield return new WaitForFixedUpdate();
                foreach (var r in rigs) r.Sample(Time.fixedDeltaTime);
            }

            sb.AppendLine($"\n--- {st.name}: Hs {sea.CurrentHs:F1} m "
                          + $"({SeaStateController.NameForHs(sea.CurrentHs, ref band)}), "
                          + $"wind {sea.WindDirectionDeg:F0}°, swell {sea.SwellDirectionDeg:F0}° "
                          + "(hulls all head +Z) ---");
            sb.AppendLine($"  {"hull",-16}{"sits m",8}{"heave",8}{"roll°",8}{"rollMax",9}"
                          + $"{"pitch°",8}{"pitchMx",9}{"rail%",7}{"deck%",7}{"bury%",7}{"accG",7}");
            foreach (var r in rigs)
                sb.AppendLine($"  {r.label,-16}{r.MeanY,8:F2}{r.HeaveRms,8:F2}{r.RollRms,8:F1}"
                              + $"{r.rollMax,9:F1}{r.PitchRms,8:F1}{r.pitchMax,9:F1}"
                              + $"{r.RailPct,7:F0}{r.DeckPct,7:F0}{r.BuriedPct,7:F0}"
                              + $"{r.accMax / 9.81f,7:F1}");

            sb.AppendLine("  " + Transect(sea));
            UpdateMarkers();
            Frame(sea.CurrentHs);
            yield return null;
            Shoot(cam, $"/tmp/seasick-float-{st.name}.png");
            yield return null;
            UpdateMarkers();
            FrameClose(sea.CurrentHs);
            yield return null;
            Shoot(cam, $"/tmp/seasick-float-{st.name}-close.png");
            yield return null;
        }

        sea.ReleaseForce();
        TimeOfDay.Paused = false;
        System.IO.File.WriteAllText("/tmp/seasick-float.txt", sb.ToString());
        Debug.Log("HullFloatProbe done\n" + sb);
    }

    int band;
    Transform[] markers;

    Rig Build(Spec s, Vector3 at, StringBuilder sb)
    {
#if UNITY_EDITOR
        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtDir}/{s.file}.fbx");
#else
        GameObject src = null;
#endif
        if (src == null) { sb.AppendLine($"  MISSING {s.file}"); return null; }

        var go = Instantiate(src, at, Quaternion.identity, root.transform);
        go.name = s.label;
        go.SetActive(false);                 // fields must be set before Awake

        float mass = Rho * s.dispVol;
        float totalVolume = s.dispVol / FloatRatio;
        float k = mass / RefMass;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;

        var probes = go.AddComponent<BuoyancyProbeSet>();
        float railY = s.depth - s.draft;
        // Start the rig at the rule the first run's solve found, rather than
        // at the drawn keel. Solving all five independently gave lifts of
        // 0.15 / 0.29 / 0.73 / 1.37 / 3.01 m on drafts of 0.29 / 0.55 / 1.35 /
        // 2.50 / 5.40 — that is 0.52, 0.53, 0.54, 0.55, 0.56 of the draft,
        // across a 19x range of hull size. It is one rule, not five fudges.
        //
        // And it is the SAME rule the paddle steamer already needed: her
        // ProbeLift of 0.55 m on a 1.02 m draft is 0.54. That constant has
        // been in the project as a measured one-off since 2026-08-28; it is
        // actually a property of this probe layout, and any new hull will
        // need it.
        float keelY = -s.draft * (1f - ProbeLiftRatio);
        probes.SetProbes(BuoyancyProbeSet.FleetLayout(s.loa * 0.86f, s.beam * 0.9f, s.draft, keelY, railY));

        var body = go.AddComponent<BuoyantBody>();
        // Damping has to scale with the body or it means nothing: the tuned
        // coefficients give the 19 t steamer a 0.7 s velocity time constant,
        // and the same numbers on an 1881 t three-decker give her 67 s — she
        // would ring like a bell — while the 4 t raft would be glued to the
        // water. Held at the steamer's ratio instead.
        Set(body, "totalVolume", totalVolume);
        Set(body, "linearDrag", 28000f * k);
        Set(body, "quadraticDrag", 3000f * k);
        Set(body, "angularDragTorque", 30000f * k * (s.loa / RefLoa) * (s.loa / RefLoa));
        // Gyradii, not guesses: the box formula gives 0.29 x the dimension, so
        // 0.9 LOA lands pitch at 0.26 L and (beam, depth) lands roll at
        // 0.32-0.40 B — both inside what real hulls measure.
        Set(body, "inertiaBoxDims", new Vector3(s.beam, s.depth, s.loa * 0.9f));
        // KG at 0.38 of the depth above the keel: a well-ballasted ship. A
        // three-decker's real KG is ABOVE her waterline and she needs the
        // ballast to bring it down; assuming she has it, rather than letting a
        // guessed CoG capsize her, is the honest starting point.
        Set(body, "centreOfMass", new Vector3(0f, s.depth * 0.38f - s.draft, 0f));

        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.33f, 0.22f, 0.13f));
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.22f);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = mat;

        go.SetActive(true);

        sb.AppendLine($"  {s.label,-14} {mass / 1000f,8:F1} t  disp {s.dispVol,7:F1} m3  "
                      + $"capacity {totalVolume,7:F1} m3  keel {keelY,5:F2}  rail {railY,5:F2}");

        return new Rig
        {
            label = s.label, t = go.transform, rb = rb, body = body, probes = probes,
            loa = s.loa, beam = s.beam, draft = s.draft, depth = s.depth,
            railY = railY, deckY = railY * 0.6f, mass = mass, probeKeelY = keelY,
        };
    }

    static void Set(object o, string field, object v)
    {
        var f = o.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(o, v);
        else Debug.LogWarning($"HullFloatProbe: no field {field}");
    }

    /// What the sea actually IS at this state, measured off a transect
    /// rather than read off the spectrum settings.
    ///
    /// This exists because the whole reading of the first run turns on one
    /// number: if the waves are far longer than every hull in the fleet, then
    /// every hull just follows the surface and the size of a ship cannot
    /// matter to how she rides. That is a claim about wavelength, so it gets
    /// measured. LOA / wavelength is the seakeeping ratio — a hull only starts
    /// to bridge, slam and resist a sea as it approaches 1.
    string Transect(SeaStateController sea)
    {
        const int N = 512;
        const float Span = 1500f;
        float ang = sea.SwellDirectionDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
        Vector3 from = site + new Vector3(rowSpan * 0.5f, 0f, 0f) - dir * (Span * 0.5f);

        var h = new float[N];
        float mean = 0f;
        for (int i = 0; i < N; i++)
        {
            h[i] = OceanSampler.SampleImmediate(from + dir * (Span * i / (N - 1f))).height;
            mean += h[i];
        }
        mean /= N;

        float var2 = 0f, lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < N; i++)
        {
            float d = h[i] - mean;
            var2 += d * d;
            if (h[i] < lo) lo = h[i];
            if (h[i] > hi) hi = h[i];
        }
        float sd = Mathf.Sqrt(var2 / N);

        int crossings = 0;
        for (int i = 1; i < N; i++)
            if (h[i - 1] <= mean && h[i] > mean) crossings++;
        float lambda = crossings > 0 ? Span / crossings : 0f;

        var line = new StringBuilder(
            $"sea: Hs(measured) {4f * sd:F1} m, crest-trough {hi - lo:F1} m, "
            + $"mean wavelength {lambda:F0} m, steepness {(lambda > 1f ? 4f * sd / lambda : 0f):F3}\n  LOA/wavelength: ");
        foreach (var f in Fleet)
            line.Append($"{f.label.Substring(0, 2)} {(lambda > 1f ? f.loa / lambda : 0f):F2}  ");
        return line.ToString();
    }

    void Frame(float hs)
    {
        // Pull back as the sea grows, or the wild state is five specks in a
        // trough. Height tracks Hs because the row itself is being lifted.
        // The first run framed this obliquely and close, which put the near
        // hull across half the frame and the far one off the edge. The row is
        // rowSpan long; the camera has to stand back far enough to hold it.
        Vector3 centre = site + new Vector3(rowSpan * 0.5f, 0f, 0f);
        float back = rowSpan * 1.25f + hs * 1.5f;
        float up = rowSpan * 0.28f + hs * 0.8f;
        cam.transform.position = centre + new Vector3(-rowSpan * 0.25f, up, -back);
        cam.transform.LookAt(centre + new Vector3(0f, hs * 0.25f, 0f));
    }

    /// Bright beads pinned every frame to the height the SAMPLER reports, laid
    /// across the frame.
    ///
    /// This is a parity check, and it is the one that decides how to read every
    /// other number here. The close shots show a surface that looks like glass
    /// while the hulls floating on it sit twenty metres apart in height — and
    /// there are only two explanations. Either the sea really is that long and
    /// gentle (214 m wavelength, so half a wave fills the frame and reads as a
    /// tilted plane), or the shader and the C# wave field disagree and the
    /// physics is riding a sea the player never sees. WarpOut's comment names
    /// the second one as a known failure mode of this project.
    ///
    /// A bead sitting ON the rendered surface says the sampler and the shader
    /// agree and the sea is genuinely this smooth. A bead hanging in the air
    /// says they do not.
    void BuildSurfaceMarkers()
    {
        markers = new Transform[26];
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1f, 0.25f, 0.05f));
        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1f, 0.3f, 0f)); }
        for (int i = 0; i < markers.Length; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "SurfaceBead";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root.transform);
            g.transform.localScale = Vector3.one * 1.6f;
            g.GetComponent<Renderer>().sharedMaterial = m;
            markers[i] = g.transform;
        }
    }

    void UpdateMarkers()
    {
        if (markers == null) return;
        // Strung out toward the row from in front of the close camera, so they
        // cross the whole frame in both shots.
        for (int i = 0; i < markers.Length; i++)
        {
            float t = i / (markers.Length - 1f);
            var p = new Vector3(site.x - 20f + rowSpan * 1.15f * t, 0f, site.z - 20f + 26f * t);
            p.y = OceanSampler.SampleImmediate(p).height;
            markers[i].position = p;
        }
    }

    /// The row shot cannot show a sea state. From 270 m up and back, a 28 m
    /// sea on a 214 m wavelength is a 4-degree slope and reads as a millpond —
    /// which says nothing about whether it reads that way from a deck. This
    /// camera rides the surface at 4 m, the height of a small boat's mast, and
    /// looks down the row past the raft at the three-decker. It is the only
    /// framing in which a wave has anything to be measured against.
    void FrameClose(float hs)
    {
        float xNear = site.x + 8.44f + Gap + 3.4f;              // by the raft
        Vector3 at = new Vector3(xNear - 14f, 0f, site.z - 26f);
        at.y = OceanSampler.SampleImmediate(at).height + 4f + hs * 0.10f;
        cam.transform.position = at;
        Vector3 look = new Vector3(site.x + rowSpan * 0.72f, 0f, site.z);
        look.y = OceanSampler.SampleImmediate(look).height + 2f;
        cam.transform.LookAt(look);
    }

    static void Shoot(Camera cam, string path)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0); tex.Apply();
        cam.targetTexture = prev; RenderTexture.active = prevActive;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex); rt.Release(); Destroy(rt);
    }
}
