using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Live wave tuning with a top-down view of the water and the boat in it.
///
/// Window > SeaSick > Storm Tuner.
///
/// Why a window rather than just the inspector: judging a sea needs to see the
/// PATTERN — how far apart the crests are, whether they all run the same way,
/// whether the surface is confused or corduroy — and none of that is legible
/// from the chase camera sitting inside the wave. From above, with the boat in
/// frame for scale, wavelength and directionality read immediately.
///
/// How it stays honest:
///
/// - It edits the sea-state ASSET, and hands the ocean that same asset, so
///   there is exactly one copy of the values. No second state to reconcile,
///   and what you tune is what ships.
/// - It pins the weather by FORCING severity, and deliberately does not
///   disable SeaStateController — see PinWeather for why that innocuous-looking
///   shortcut switches the whole storm off.
/// - FREEZE is the important button. Sea state drifts and the field moves, so
///   two settings compared across a few seconds of drag are not comparable —
///   the same patch of water has measured 50.8%, 31.5%, 24.5% and 28.1% grey
///   across four frames of an unpinned sea. Freeze pins the phase so a change
///   is the only thing that changed.
///
/// The numbers beside the sliders are ESTIMATES for while you drag.
/// WaveSizeProbe is the truth, and the window says so.
public class StormTuner : EditorWindow
{
    const string StateDir = "Assets/_Project/Settings/Resources/Ocean/";
    static readonly string[] StateNames = { "SeaState_Calm", "SeaState_Normal", "SeaState_Stormy" };
    // Severity that makes SeaStateController's blend land exactly on each asset.
    static readonly float[] StateSeverity = { 0f, 0.5f, 1f };

    int stateIndex = 2;
    OceanSpectrumSettings asset;
    StormShape.Knobs knobs;

    // --- preview ---
    Camera cam;
    RenderTexture rt;
    float viewMetres = 900f;
    // Only has to clear the crests: an orthographic camera's framing does not
    // depend on how high it is.
    float altitude = 300f;
    float exposure = 4f;
    bool livePreview = true;

    /// Height is the DEFAULT view, and the lit render is the secondary one.
    ///
    /// That is backwards from what you would expect, and it is deliberate. The
    /// lit render answers "what does it look like" and is nearly black from
    /// above — the storm body colours run 0.06-0.10 and looking straight down
    /// pins Fresnel at its 0.02 floor, so there is almost no sky coming back.
    /// Lifting the ambient barely touched it, because the ocean shader's body
    /// colour is not lit by ambient at all.
    ///
    /// The height view answers "what SHAPE is it", which is the actual question
    /// the sliders control, and it does so with no dependence on lighting,
    /// weather or time of day. Wavelength and chaos read instantly.
    enum PreviewMode { Height, Lit }
    PreviewMode mode = PreviewMode.Height;

    Texture2D heightTex;
    const int HeightN = 80;         // 6400 samples; SampleImmediate is not cheap
    float sampledMin, sampledMax, sampledRms;
    float sampledForView, sampledForHs;
    Vector3 sampledAt;
    double lastSampleTime, lastRepaint;
    ShipMotor cachedMotor;      // FindAnyObjectByType per repaint is not free either
    bool dirty;                 // asset edited, not yet written to disk

    // --- pinning ---
    bool frozen;
    double frozenAt;
    bool pinned;                 // we have taken control of the weather

    bool showRaw;

    [MenuItem("Window/SeaSick/Storm Tuner")]
    public static void Open()
    {
        var w = GetWindow<StormTuner>("Storm Tuner");
        w.minSize = new Vector2(420f, 640f);
        w.Show();
    }

    void OnEnable()
    {
        LoadAsset();
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        Save();
        Unpin();
        DestroyPreview();
    }

    /// SetDirty alone does not put anything on disk, and a whole tuning session
    /// was lost finding that out: the sliders moved, the water changed, the
    /// asset stayed exactly as it was. Written on mouse-up rather than on every
    /// drag frame, because SaveAssets is not cheap.
    void Save()
    {
        if (!dirty || asset == null) return;
        AssetDatabase.SaveAssets();
        dirty = false;
    }

    void LoadAsset()
    {
        asset = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(
            StateDir + StateNames[stateIndex] + ".asset");
        if (asset != null) knobs = StormShape.Read(asset);
    }

    void Tick()
    {
        if (pinned && Application.isPlaying)
        {
            // Re-assert every frame rather than switching the controller off.
            // Forcing severity already stops it rebuilding (its throttle needs
            // severity to MOVE), and re-checking costs a reference compare.
            var ctrl = SeaStateController.Instance;
            if (ctrl != null) ctrl.ForceSeverity(StateSeverity[stateIndex]);
            var ocean = OceanRenderer.Instance;
            if (ocean != null && ocean.Settings != asset) ocean.SetSettings(asset);
        }

        // Repaint at about 10 Hz, not on every editor update. The preview is a
        // diagnostic, not a video feed, and EditorApplication.update fires far
        // faster than anything here needs.
        if (Application.isPlaying && livePreview
            && EditorApplication.timeSinceStartup - lastRepaint > 0.1)
        {
            lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    // ------------------------------------------------------------------ GUI

    void OnGUI()
    {
        if (asset == null)
        {
            EditorGUILayout.HelpBox("Sea state asset not found under " + StateDir, MessageType.Error);
            if (GUILayout.Button("Reload")) LoadAsset();
            return;
        }

        EditorGUI.BeginChangeCheck();
        int newState = EditorGUILayout.Popup("Sea state", stateIndex,
            new[] { "Calm", "Normal", "Stormy" });
        if (EditorGUI.EndChangeCheck())
        {
            stateIndex = newState;
            LoadAsset();
            if (pinned) PinWeather();   // re-pin at the new severity
        }

        // Commit to disk when the drag ends.
        if (Event.current.type == EventType.MouseUp) Save();

        EditorGUILayout.Space(4);
        DrawSliders();
        EditorGUILayout.Space(6);
        DrawReadout();
        EditorGUILayout.Space(6);
        DrawControls();
        EditorGUILayout.Space(6);
        DrawPreview();
    }

    void DrawSliders()
    {
        EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        // Ranges are generous on purpose: the interesting settings for this
        // game have repeatedly turned out to be past where a "sensible" range
        // would have stopped.
        knobs.heightHs = Slider("Wave height (Hs)", knobs.heightHs, 0.2f, 90f, "m",
            "Significant wave height: the average of the biggest third. Individual waves reach about 1.6x this.");
        knobs.wavelength = Slider("Wavelength", knobs.wavelength, 20f, 900f, "m",
            "Crest to crest. This is the knob that decides whether a wave is CLIMBABLE or a wall — height alone never was.");
        knobs.chaos = Slider("Chaos", knobs.chaos, 0f, 1f, "",
            "0 = one clean train of parallel rollers. 1 = two trains crossing at 75 degrees, both broad. Moves energy between the trains; it CANNOT change the wave height.");
        knobs.texture = Slider("Surface texture", knobs.texture, 1f, 25f, "x",
            "Over-drives the spectrum's short-wave tail. 1 = physically honest and visually flat. This is the 'more noise overall' knob.");
        knobs.choppiness = Slider("Crest sharpness", knobs.choppiness, 0f, 2f, "",
            "Horizontal displacement. Sharpens crests and rounds troughs. Past ~1.3 the surface starts folding, which is also where foam comes from.");

        if (EditorGUI.EndChangeCheck()) ApplyKnobs();
    }

    float Slider(string label, float value, float min, float max, string unit, string tip)
    {
        var content = new GUIContent(label + (unit.Length > 0 ? "  (" + unit + ")" : ""), tip);
        return EditorGUILayout.Slider(content, value, min, max);
    }

    void ApplyKnobs()
    {
        Undo.RecordObject(asset, "Tune sea state");
        StormShape.Apply(asset, knobs);
        EditorUtility.SetDirty(asset);
        dirty = true;

        // Push to the running ocean. SetSettings takes the asset BY REFERENCE,
        // so marking the spectrum dirty is enough — there is no copy to keep
        // in step.
        if (Application.isPlaying)
        {
            var ocean = OceanRenderer.Instance;
            if (ocean != null)
            {
                if (!pinned) PinWeather();
                ocean.SetSettings(asset);
                ocean.MarkSpectrumDirty();
            }
        }
    }

    void DrawReadout()
    {
        EditorGUILayout.LabelField("What that produces", EditorStyles.boldLabel);

        float slope = StormShape.FaceSlopeDeg(knobs);
        float lengths = StormShape.FaceBoatLengths(knobs, 24.2f);
        float depth = StormShape.DepthNeeded(knobs, 0.55f);

        // Face angle is this project's acceptance criterion, so it is the one
        // that gets a verdict rather than just a number.
        string verdict =
            slope < 6f ? "flat — this is the sheet the project shipped once" :
            slope < 12f ? "gentle — reads as a hillside, not a sea" :
            slope <= 22f ? "GOOD — in the 15-20 deg target" :
            "very steep — past where real water breaks";

        EditorGUILayout.LabelField($"face angle  ~{slope:F1}°", verdict);
        EditorGUILayout.LabelField($"face length  ~{lengths:F1} boat lengths",
            lengths < 4f ? "too short to climb" : lengths > 20f ? "very long" : "climbable");
        EditorGUILayout.LabelField($"needs  {depth:F0} m of water",
            "shallower than this and the depth limit caps it");
        EditorGUILayout.LabelField("main train",
            $"{asset.swellHeight:F1} m @ {asset.swellWavelength:F0} m, sharpness {asset.swellSharpness:F1}");
        EditorGUILayout.LabelField("cross train",
            asset.swell2Height < 0.05f ? "none"
            : $"{asset.swell2Height:F1} m @ {asset.swell2Wavelength:F0} m at "
              + $"{asset.swell2DirectionDeg - asset.swellDirectionDeg:F0}°");

        EditorGUILayout.HelpBox(
            "These are estimates for while you drag. Run WaveSizeProbe for the real "
            + "face angle, Hs and seabed clearance — and DivergenceProbe after any big "
            + "steepness change, because the CPU sampler's Newton inversion is what "
            + "gives out first.", MessageType.None);

        showRaw = EditorGUILayout.Foldout(showRaw, "Raw spectrum fields", true);
        if (showRaw)
        {
            EditorGUI.indentLevel++;
            var so = new SerializedObject(asset);
            so.Update();
            var p = so.GetIterator();
            p.NextVisible(true);
            while (p.NextVisible(false)) EditorGUILayout.PropertyField(p, true);
            if (so.ApplyModifiedProperties())
            {
                knobs = StormShape.Read(asset);
                if (Application.isPlaying && OceanRenderer.Instance != null)
                    OceanRenderer.Instance.MarkSpectrumDirty();
            }
            EditorGUI.indentLevel--;
        }
    }

    void DrawControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode for the live view.", MessageType.Info);
                return;
            }

            bool wantFrozen = GUILayout.Toggle(frozen,
                new GUIContent(frozen ? "Frozen" : "Freeze",
                    "Pins the wave phase. Two settings compared across a moving sea are "
                    + "not comparable — freeze first, then change one thing."),
                "Button", GUILayout.Height(22));
            if (wantFrozen != frozen)
            {
                frozen = wantFrozen;
                if (frozen) { frozenAt = OceanTime.Now; OceanTime.Paused = true; }
                else OceanTime.Paused = false;
            }

            using (new EditorGUI.DisabledScope(!frozen))
                if (GUILayout.Button(new GUIContent("Step +2 s", "Advance the frozen sea."),
                        GUILayout.Height(22)))
                {
                    frozenAt += 2.0;
                    OceanTime.Scrub(frozenAt);
                }

            if (GUILayout.Button(new GUIContent("Release weather",
                    "Hand the sea back to SeaStateController."), GUILayout.Height(22)))
                Unpin();

            using (new EditorGUI.DisabledScope(!dirty))
                if (GUILayout.Button(new GUIContent(dirty ? "Save*" : "Saved",
                        "Write the sea state to disk."), GUILayout.Height(22)))
                    Save();
        }

        if (pinned)
            EditorGUILayout.LabelField("weather pinned at severity "
                + StateSeverity[stateIndex].ToString("F2"), EditorStyles.miniLabel);
    }

    // -------------------------------------------------------------- preview

    void DrawPreview()
    {
        EditorGUILayout.LabelField("Top down", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            mode = (PreviewMode)EditorGUILayout.EnumPopup(mode, GUILayout.Width(70));
            livePreview = GUILayout.Toggle(livePreview, "Live", "Button", GUILayout.Width(46));
            viewMetres = EditorGUILayout.Slider(
                new GUIContent("View (m)",
                    "How much ocean to frame. Keep under about 2000 m: past the displacement "
                    + "fade the sea flattens and you would be judging the fade, not the waves."),
                viewMetres, 100f, 2000f);
        }

        if (mode == PreviewMode.Lit)
        {
            altitude = EditorGUILayout.Slider(
                new GUIContent("Camera height (m)",
                    "Only has to clear the crests — orthographic framing does not depend on it."),
                altitude, 120f, 2000f);
            exposure = EditorGUILayout.Slider(
                new GUIContent("Exposure",
                    "Preview only, and it does not touch the game view."),
                exposure, 1f, 12f);
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Top-down view needs play mode.", MessageType.Info);
            return;
        }

        if (cachedMotor == null) cachedMotor = Object.FindAnyObjectByType<ShipMotor>();
        var motor = cachedMotor;
        if (motor == null)
        {
            EditorGUILayout.HelpBox("No ShipMotor in the scene.", MessageType.Warning);
            return;
        }

        Rect area = GUILayoutUtility.GetRect(10f, 4000f, 200f, 4000f);
        int w = Mathf.Max(64, (int)area.width);
        int h = Mathf.Max(64, (int)Mathf.Min(area.height, area.width));
        Rect img = new Rect(area.x, area.y, w, h);
        Vector3 shipPos = motor.transform.position;

        if (mode == PreviewMode.Lit)
        {
            EnsurePreview(w, h);
            if (cam == null || rt == null) return;
            cam.transform.position = new Vector3(shipPos.x, shipPos.y + altitude, shipPos.z);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographicSize = viewMetres * 0.5f * ((float)h / Mathf.Max(w, 1));
            cam.farClipPlane = altitude + 1200f;
            RenderFogFree(cam, exposure);
            GUI.DrawTexture(img, rt, ScaleMode.StretchToFill, false);
        }
        else
        {
            SampleHeightMap(shipPos);
            if (heightTex != null) GUI.DrawTexture(img, heightTex, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(img.x + 16f, img.yMax - 44f, img.width - 32f, 18f),
                $"trough {sampledMin:F0} m   crest {sampledMax:F0} m   Hs(4xRMS) {4f * sampledRms:F0} m",
                WhiteLabel());
        }

        DrawScaleOverlay(img, motor);
        DrawBoatFootprint(img, motor);
    }

    /// Height, sampled off the same CPU field the hull floats on.
    ///
    /// Deliberately NOT the rendered image: this has to say what shape the sea
    /// is, and the rendered image is a statement about lighting. It also means
    /// the view is identical whatever the weather or time of day, so two
    /// settings are actually comparable.
    ///
    /// Resampled only when something moved — SampleImmediate is a one-shot
    /// convenience with a budget of about eight calls a frame, and this asks
    /// for 6400 of them. With Freeze on (which is how this should be used) the
    /// map is static and costs nothing to look at.
    void SampleHeightMap(Vector3 shipPos)
    {
        if (!OceanSampler.Ready) return;

        // THROTTLED, hard. The first version restaled on every repaint whenever
        // the sea was not frozen, which is 6400 SampleImmediate calls per
        // repaint against a documented budget of about EIGHT per frame -- three
        // orders of magnitude over, and the reason the window crawled. A moving
        // sea only needs refreshing often enough to read as moving.
        double now = EditorApplication.timeSinceStartup;
        bool changed = heightTex == null
            || !Mathf.Approximately(sampledForView, viewMetres)
            || !Mathf.Approximately(sampledForHs, knobs.heightHs)
            || (shipPos - sampledAt).sqrMagnitude > 25f;
        bool due = !frozen && now - lastSampleTime > 0.25;
        if (!changed && !due) return;
        lastSampleTime = now;

        sampledForView = viewMetres;
        sampledForHs = knobs.heightHs;
        sampledAt = shipPos;

        if (heightTex == null)
        {
            heightTex = new Texture2D(HeightN, HeightN, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
              hideFlags = HideFlags.HideAndDontSave };
        }

        var hs = new float[HeightN * HeightN];
        float min = float.MaxValue, max = float.MinValue, sum = 0f, sumSq = 0f;
        for (int j = 0; j < HeightN; j++)
            for (int i = 0; i < HeightN; i++)
            {
                float u = i / (float)(HeightN - 1) - 0.5f;
                float v = j / (float)(HeightN - 1) - 0.5f;
                var p = new Vector3(shipPos.x + u * viewMetres, 0f, shipPos.z + v * viewMetres);
                float hh = OceanSampler.SampleImmediate(p).height;
                hs[j * HeightN + i] = hh;
                min = Mathf.Min(min, hh); max = Mathf.Max(max, hh);
                sum += hh; sumSq += hh * hh;
            }

        float mean = sum / hs.Length;
        sampledRms = Mathf.Sqrt(Mathf.Max(0f, sumSq / hs.Length - mean * mean));
        sampledMin = min; sampledMax = max;

        // Normalised on the sea's OWN spread, so the ramp reads the same at
        // every height setting and the slider does not just make it brighter.
        float span = Mathf.Max(2f * sampledRms, 0.05f);
        var px = new Color32[hs.Length];
        for (int k = 0; k < hs.Length; k++)
        {
            float t = Mathf.Clamp01(0.5f + (hs[k] - mean) / (2f * span));
            px[k] = Ramp(t);
        }
        heightTex.SetPixels32(px);
        heightTex.Apply(false);
    }

    /// Trough to crest: near-black blue, through mid teal, to white. Chosen so
    /// the CREST LINES read as ridges — the thing you are judging is where the
    /// crests are and which way they run.
    static Color32 Ramp(float t)
    {
        Color c = t < 0.5f
            ? Color.Lerp(new Color(0.03f, 0.06f, 0.16f), new Color(0.10f, 0.42f, 0.50f), t * 2f)
            : Color.Lerp(new Color(0.10f, 0.42f, 0.50f), Color.white, (t - 0.5f) * 2f);
        return c;
    }

    /// The boat, to scale and at her real heading, in the middle of the frame.
    /// This is the reference that makes the whole view mean something: 470 m of
    /// wavelength is an abstraction, and "nineteen boat lengths" is not.
    void DrawBoatFootprint(Rect img, ShipMotor motor)
    {
        float pxPerMetre = img.width / Mathf.Max(viewMetres, 1f);
        Vector2 c = new Vector2(img.center.x, img.center.y);
        float len = 24.2f * pxPerMetre * 0.5f;
        float beam = 8.44f * pxPerMetre * 0.5f;

        float yaw = motor.transform.eulerAngles.y * Mathf.Deg2Rad;
        // World +Z is up-screen in a top-down view, +X is right.
        Vector2 fwd = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
        Vector2 side = new Vector2(-fwd.y, fwd.x);

        Vector3[] quad =
        {
            c + fwd * len + side * beam,
            c + fwd * len - side * beam,
            c - fwd * len - side * beam,
            c - fwd * len + side * beam,
        };

        Handles.BeginGUI();
        Handles.color = new Color(1f, 0.85f, 0.3f);
        for (int i = 0; i < 4; i++)
            Handles.DrawAAPolyLine(2.5f, quad[i], quad[(i + 1) % 4]);
        // A stub off the bow so heading is unambiguous at small scales.
        Handles.DrawAAPolyLine(2.5f, c + fwd * len, c + fwd * (len + 10f));
        Handles.EndGUI();
    }

    static GUIStyle WhiteLabel()
    {
        var s = new GUIStyle(EditorStyles.boldLabel);
        s.normal.textColor = Color.white;
        return s;
    }

    /// A picture of water has no scale in it at all — this is the whole reason
    /// the boat is in frame. The bar and the boat mark make the wavelength
    /// readable as a number rather than a vibe.
    void DrawScaleOverlay(Rect img, ShipMotor motor)
    {
        float pxPerMetre = img.width / Mathf.Max(viewMetres, 1f);

        // Round bar length: 1, 2 or 5 x a power of ten, about a quarter frame.
        float target = viewMetres * 0.25f;
        float pow = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(target)));
        float barM = pow;
        if (target / pow >= 5f) barM = pow * 5f;
        else if (target / pow >= 2f) barM = pow * 2f;

        float barPx = barM * pxPerMetre;
        float y = img.yMax - 24f;
        float x = img.x + 16f;

        Handles.BeginGUI();
        Handles.color = Color.white;
        Handles.DrawAAPolyLine(3f, new Vector3(x, y), new Vector3(x + barPx, y));
        Handles.DrawAAPolyLine(3f, new Vector3(x, y - 5f), new Vector3(x, y + 5f));
        Handles.DrawAAPolyLine(3f, new Vector3(x + barPx, y - 5f), new Vector3(x + barPx, y + 5f));

        // The boat, to scale, as a second ruler right next to the real one.
        float boatPx = 24.2f * pxPerMetre;
        float by = y - 16f;
        Handles.color = new Color(1f, 0.85f, 0.3f);
        Handles.DrawAAPolyLine(3f, new Vector3(x, by), new Vector3(x + boatPx, by));
        Handles.EndGUI();

        var style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(x + barPx + 6f, y - 10f, 120f, 18f), barM + " m", style);
        style.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUI.Label(new Rect(x + boatPx + 6f, by - 10f, 160f, 18f), "boat 24.2 m", style);

        // Wavelength in boat lengths is the read this whole view exists for.
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(img.x + 16f, img.y + 8f, img.width - 32f, 18f),
            $"crest spacing should look like {knobs.wavelength:F0} m "
            + $"= {knobs.wavelength / 24.2f:F1} boat lengths", style);
        if (frozen)
            GUI.Label(new Rect(img.x + 16f, img.y + 26f, 200f, 18f), "FROZEN", style);
    }

    /// Renders with fog off and the ambient lifted, both restored afterwards.
    ///
    /// The storm's fog ends at 430 m, and an overhead camera is further from
    /// the water than that at any useful altitude, so the first fog-respecting
    /// render of this view came back as a single flat black square. Fog is a
    /// look-from-the-deck effect and this is a diagnostic: the whole point is
    /// to see the wave pattern, and fog's entire job is to stop you seeing it.
    ///
    /// Orthographic projection means ALTITUDE DOES NOT AFFECT FRAMING at all —
    /// it only has to clear the crests — so flying lower is not an alternative
    /// fix, just a smaller dose of the same problem.
    /// EXPOSURE, for the same reason. A storm sea seen from directly above is
    /// very nearly black: the storm body colours run 0.06-0.10, and looking
    /// straight down puts the Fresnel term at its 0.02 floor, so almost no sky
    /// is reflected back at the camera. That is correct, and it is useless to
    /// tune against. Lifting the ambient for the render brings the wave pattern
    /// out without touching what the game itself displays -- the same trick as
    /// the fog, and restored just as carefully.
    static void RenderFogFree(Camera c, float exposure)
    {
        bool fogWas = RenderSettings.fog;
        float endWas = RenderSettings.fogEndDistance;
        Color skyWas = RenderSettings.ambientSkyColor;
        Color eqWas = RenderSettings.ambientEquatorColor;
        Color grWas = RenderSettings.ambientGroundColor;

        RenderSettings.fog = false;
        RenderSettings.fogEndDistance = 100000f;
        if (exposure > 1.001f)
        {
            RenderSettings.ambientSkyColor = skyWas * exposure;
            RenderSettings.ambientEquatorColor = eqWas * exposure;
            RenderSettings.ambientGroundColor = grWas * exposure;
        }

        try { c.Render(); }
        finally
        {
            RenderSettings.fog = fogWas;
            RenderSettings.fogEndDistance = endWas;
            RenderSettings.ambientSkyColor = skyWas;
            RenderSettings.ambientEquatorColor = eqWas;
            RenderSettings.ambientGroundColor = grWas;
        }
    }

    void EnsurePreview(int w, int h)
    {
        if (rt == null || rt.width != w || rt.height != h)
        {
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR)
            { name = "StormTunerRT" };
            rt.Create();
            if (cam != null) cam.targetTexture = rt;
        }

        if (cam == null)
        {
            var go = new GameObject("StormTunerCam") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 1f;
            cam.cullingMask = ~0;
            // Never tagged MainCamera and never left enabled: the clipmap
            // follows Camera.main, and a stray second camera claiming that tag
            // would drag the whole ocean's geometry with it.
            cam.enabled = false;
            cam.targetTexture = rt;
        }
    }

    void DestroyPreview()
    {
        if (cam != null) { DestroyImmediate(cam.gameObject); cam = null; }
        if (rt != null) { rt.Release(); DestroyImmediate(rt); rt = null; }
        if (heightTex != null) { DestroyImmediate(heightTex); heightTex = null; }
    }

    // ------------------------------------------------------------- pinning

    /// Take the spectrum off SeaStateController so it stops re-deriving it from
    /// the ship's position and blending the edits away.
    ///
    /// NOTE: it does NOT disable the controller, which is the obvious way to do
    /// this and is a trap. `SeaStateController.OnDisable` sets `Instance = null`,
    /// and SkyDirector reads `SeaStateController.Instance` to decide how stormy
    /// the sky and the ocean's own storm colours are — so switching the
    /// controller off silently turns the STORM off, and the first top-down
    /// renders from this window came back in fair-weather turquoise under
    /// clear-weather fog at a forced severity of 1.0. Same shape as the
    /// SkyDirector parenting bug: nothing errors, the value just quietly
    /// becomes the default.
    ///
    /// Forcing severity is enough on its own. The controller's rebuild is
    /// throttled on severity CHANGING, so a pinned severity stops it after one
    /// pass, and Tick re-asserts the settings reference in case it slips one in.
    void PinWeather()
    {
        var ctrl = SeaStateController.Instance;
        if (ctrl == null) return;
        ctrl.ForceSeverity(StateSeverity[stateIndex]);
        pinned = true;

        var ocean = OceanRenderer.Instance;
        if (ocean != null) { ocean.SetSettings(asset); ocean.MarkSpectrumDirty(); }
    }

    void Unpin()
    {
        if (frozen) { OceanTime.Paused = false; frozen = false; }
        if (!pinned) return;
        var ctrl = SeaStateController.Instance;
        if (ctrl != null) ctrl.ReleaseForce();
        pinned = false;
    }
}
