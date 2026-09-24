using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Dev
{
    /// Live tuning lab for the helm/handling/juice feel, drawn straight over
    /// the game with IMGUI so a slider dragged on Kevin's iPhone is felt the
    /// same frame. Companion to the (Agent A/B/C) tuning classes below, which
    /// this file never creates and does not reference by type -- only by
    /// name, through reflection, so it compiles and runs whether none, some
    /// or all three of them exist in the project yet:
    ///   SeaSick.Ship.HelmTuning, SeaSick.Ship.HandlingTuning,
    ///   SeaSick.Ship.JuiceTuning
    ///
    /// Modelled on PerfHUD's rules: dev/editor gated, and its label strings
    /// are cached (`Knob.ValueText`) and rebuilt only when the rounded value
    /// moves -- IMGUI garbage has cost this project real frame time before,
    /// and a wall of ~25 sliders redrawing their value text every event
    /// would be exactly that mistake again. The sliders and buttons
    /// themselves still run every IMGUI event, same as any interactive
    /// panel; that cost only exists while the panel is open and Kevin is
    /// actively tuning, never while collapsed or during ordinary play.
    ///
    /// Collapsed: one small "FEEL" button, top-left, translucent. Nothing
    /// else is drawn and nothing else consumes input -- the helm zone (the
    /// bottom half of the screen, where the floating stick lives) is never
    /// touched.
    ///
    /// Expanded: a scrollable panel over the TOP HALF only, so the bottom
    /// half stays free to steer with while a slider is open. `GUI.matrix` is
    /// scaled by `Screen.dpi/160` (clamped 1..3) before anything is laid
    /// out, which is what turns a Rect authored in plain numbers into a
    /// thumb-sized row on a Retina phone: dividing `Screen.width/height` by
    /// that same scale gives a "logical" canvas in points (~375x812 on an
    /// iPhone 16 Pro) instead of raw device pixels, and Unity's IMGUI maps
    /// touch/mouse events through `GUI.matrix` automatically, so hit-testing
    /// lines up with what is drawn.
    public class FeelLab : MonoBehaviour
    {
        // ------------------------------------------------------------ boot
        // Same shape as PerfHUD.Install: a dev instrument has no business
        // being baked into a scene, so the code on disk stays the truth and
        // every scene gets it for free.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            if (FindAnyObjectByType<FeelLab>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("FeelLab");
            go.AddComponent<FeelLab>();
            DontDestroyOnLoad(go);
        }

        const string PrefsKey = "FeelLab.json";
        const float RowH = 44f; // ~44pt: Apple's own minimum touch target.

        static readonly string[] TypeFullNames =
        {
            "SeaSick.Ship.HelmTuning",
            "SeaSick.Ship.HandlingTuning",
            "SeaSick.Ship.JuiceTuning",
        };

        /// The spec's range table, keyed "ClassName.fieldName". Anything not
        /// listed here (a field added later) falls back to
        /// default*0.25..default*4, or 0..1 when the default is 0 -- see
        /// `RangeFor`.
        static readonly Dictionary<string, (float min, float max)> Ranges =
            new Dictionary<string, (float min, float max)>
        {
            {"HelmTuning.rudderPerRim",        (0.3f, 1.5f)},
            {"HelmTuning.rudderCurve",         (0.5f, 3f)},
            {"HelmTuning.rudderMoveSpeed",     (1f, 12f)},
            {"HelmTuning.rudderReturnPerSec",  (0.5f, 8f)},
            {"HelmTuning.throttleDeadZone",    (0f, 0.3f)},

            {"HandlingTuning.yawTauBuild",          (0.1f, 1.5f)},
            {"HandlingTuning.yawTauRelease",        (0.1f, 2f)},
            {"HandlingTuning.turnCircleLengths",    (0.8f, 3f)},
            {"HandlingTuning.turnRateAtRest01",     (0f, 1f)},
            {"HandlingTuning.turnSpeedBleed",       (0f, 0.6f)},
            {"HandlingTuning.turnHeelDegrees",      (0f, 20f)},
            {"HandlingTuning.accelScale",           (0.3f, 3f)},
            {"HandlingTuning.topSpeedScale",        (0.5f, 2f)},   // 2 = PaddleDrive.TopSpeedNow clamp; Kevin pinned 1.5
            {"HandlingTuning.coastDownScale",       (0.3f, 3f)},
            {"HandlingTuning.paddleResponsiveness", (0f, 1.5f)},

            {"JuiceTuning.camFovBoostDeg",    (0f, 25f)},
            {"JuiceTuning.camDropMeters",     (0f, 5f)},
            {"JuiceTuning.camLeanPerYawDeg",  (0f, 0.5f)},
            {"JuiceTuning.camLagSeconds",     (0f, 2f)},   // Kevin pinned 1.0
            {"JuiceTuning.sprayScale",        (0f, 5f)},   // Kevin pinned 3; particle caps bound it
            {"JuiceTuning.wakeScale",         (0f, 3f)},
            {"JuiceTuning.soundPitchRange",   (0f, 1f)},
        };

        static (float min, float max) RangeFor(string key, float def)
        {
            if (Ranges.TryGetValue(key, out var r)) return r;
            if (Mathf.Approximately(def, 0f)) return (0f, 1f);
            float a = def * 0.25f, b = def * 4f;
            return a <= b ? (a, b) : (b, a);
        }

        // --------------------------------------------------------- model
        class Knob
        {
            public string Key;   // "Class.field" -- also the JSON key.
            public string Label; // field name alone, for the row.
            public FieldInfo Field;
            public bool IsBool;
            public float Min, Max;
            public object Default;

            // Cached display string: only rebuilt when the rounded (2dp)
            // value actually moves.
            public string ValueText = "";
            public int ValueTextKey = int.MinValue;
        }

        class Section
        {
            public string Name;
            public readonly List<Knob> Knobs = new List<Knob>();
        }

        readonly List<Section> sections = new List<Section>();
        readonly HashSet<string> resolvedClassNames = new HashSet<string>();
        float nextResolveAttempt;
        Dictionary<string, object> savedAtStartup;

        bool expanded;
        bool showJson;
        Vector2 scroll;

        ShipMotor motor;
        Rigidbody motorRb;
        float motorSearchDue;

        float fps = 60f;
        string readoutText = "no ShipMotor found";
        string defaultsDeltaText = "DEFAULTS: (none changed)";
        float nextReadoutRefresh;

        void Awake()
        {
            savedAtStartup = ParseFlatJson(PlayerPrefs.GetString(PrefsKey, string.Empty));
        }

        // ----------------------------------------------------------- reflection
        static Type ResolveType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        /// Builds a Section (and captures its defaults) the first time each
        /// tuning class resolves. Retried on a timer, not every frame --
        /// this project's rule against a Find/scan in a hot Update -- and
        /// re-tried forever rather than giving up, because another agent's
        /// patch can land and start compiling in mid-session.
        void TryResolveSections()
        {
            if (resolvedClassNames.Count >= TypeFullNames.Length) return;
            if (Time.unscaledTime < nextResolveAttempt) return;
            nextResolveAttempt = Time.unscaledTime + 1f;

            foreach (var full in TypeFullNames)
            {
                string className = full.Substring(full.LastIndexOf('.') + 1);
                if (resolvedClassNames.Contains(className)) continue;

                var t = ResolveType(full);
                if (t == null) continue;

                var section = new Section { Name = className };
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (f.IsLiteral || f.IsInitOnly) continue; // consts/readonly can't be tuned live
                    bool isFloat = f.FieldType == typeof(float);
                    bool isBool = f.FieldType == typeof(bool);
                    if (!isFloat && !isBool) continue;

                    string key = className + "." + f.Name;
                    var knob = new Knob
                    {
                        Key = key,
                        Label = f.Name,
                        Field = f,
                        IsBool = isBool,
                    };

                    object cur = f.GetValue(null);
                    knob.Default = cur; // "the defaults captured at first sight"
                    if (!isBool)
                    {
                        var range = RangeFor(key, (float)cur);
                        knob.Min = range.min;
                        knob.Max = range.max;
                    }
                    section.Knobs.Add(knob);

                    // LOAD, applied automatically at startup: a saved value
                    // wins over the compiled default the instant the field
                    // is discovered, so a relaunch quietly restores Kevin's
                    // tuning without him having to press anything.
                    if (savedAtStartup != null && savedAtStartup.TryGetValue(key, out object saved))
                    {
                        if (isBool && saved is bool sb) f.SetValue(null, sb);
                        else if (!isBool && saved is float sf) f.SetValue(null, sf);
                    }
                }
                sections.Add(section);
                resolvedClassNames.Add(className);
            }
        }

        void ResetAll()
        {
            foreach (var s in sections)
                foreach (var k in s.Knobs)
                    k.Field.SetValue(null, k.Default);
        }

        void ApplyDict(Dictionary<string, object> dict)
        {
            foreach (var s in sections)
            foreach (var k in s.Knobs)
            {
                if (!dict.TryGetValue(k.Key, out object v)) continue;
                if (k.IsBool && v is bool b) k.Field.SetValue(null, b);
                else if (!k.IsBool && v is float f) k.Field.SetValue(null, f);
            }
        }

        // --------------------------------------------------------- serializer
        // Hand-rolled on purpose: JsonUtility can't (de)serialize a loose
        // name->float/bool map, and this format is simple enough that
        // pulling in a JSON library for it would be the tail wagging the dog.
        string BuildJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var s in sections)
            foreach (var k in s.Knobs)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(k.Key).Append("\":");
                if (k.IsBool) sb.Append((bool)k.Field.GetValue(null) ? "true" : "false");
                else sb.Append(((float)k.Field.GetValue(null)).ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append('}');
            return sb.ToString();
        }

        static Dictionary<string, object> ParseFlatJson(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json)) return result;
            int i = 0, n = json.Length;
            while (i < n)
            {
                while (i < n && json[i] != '"') i++;
                if (i >= n) break;
                i++;
                int keyStart = i;
                while (i < n && json[i] != '"') i++;
                if (i >= n) break;
                string key = json.Substring(keyStart, i - keyStart);
                i++; // closing quote

                while (i < n && json[i] != ':') i++;
                if (i >= n) break;
                i++; // colon
                while (i < n && (json[i] == ' ' || json[i] == '\t')) i++;

                int valStart = i;
                while (i < n && json[i] != ',' && json[i] != '}') i++;
                string raw = json.Substring(valStart, i - valStart).Trim();

                if (raw == "true") result[key] = true;
                else if (raw == "false") result[key] = false;
                else if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                    result[key] = fv;
            }
            return result;
        }

        // ------------------------------------------------------------- readout
        void Update()
        {
            TryResolveSections();

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-5f);
            fps = Mathf.Lerp(fps, 1f / dt, 1f - Mathf.Exp(-dt * 4f));

            if (motor == null)
            {
                if (Time.unscaledTime >= motorSearchDue)
                {
                    motorSearchDue = Time.unscaledTime + 2f;
                    motor = FindFirstObjectByType<ShipMotor>();
                    motorRb = motor != null ? motor.GetComponent<Rigidbody>() : null;
                }
            }

            if (Time.unscaledTime >= nextReadoutRefresh)
            {
                nextReadoutRefresh = Time.unscaledTime + 0.25f; // 4x/s
                RebuildReadout();
                RebuildDefaultsDelta();
            }
        }

        void RebuildReadout()
        {
            if (motor == null)
            {
                readoutText = $"no ShipMotor found   {fps:F0} fps";
                return;
            }

            float speed = motor.CurrentSpeed;
            float throttle = motor.Throttle;
            float throttleOrder = motor.ThrottleOrder;
            float rudder = motor.Rudder;
            float yawRate = motorRb != null ? motorRb.angularVelocity.y * Mathf.Rad2Deg : 0f;
            float heel = Signed(motor.transform.eulerAngles.z);

            readoutText =
                $"spd {speed:F1} m/s  yaw {yawRate:F0}°/s  heel {heel:F0}°  " +
                $"thr {throttle:F2}→{throttleOrder:F2}  rdr {rudder:F2}  {fps:F0} fps";
        }

        void RebuildDefaultsDelta()
        {
            bool any = false;
            var sb = new StringBuilder("DEFAULTS: ");
            foreach (var s in sections)
            foreach (var k in s.Knobs)
            {
                object cur = k.Field.GetValue(null);
                bool differs = k.IsBool
                    ? !cur.Equals(k.Default)
                    : !Mathf.Approximately((float)cur, (float)k.Default);
                if (!differs) continue;
                any = true;
                sb.Append(k.Key).Append('=');
                sb.Append(k.IsBool
                    ? ((bool)cur ? "true" : "false")
                    : ((float)cur).ToString("F2", CultureInfo.InvariantCulture));
                sb.Append("  ");
            }
            defaultsDeltaText = any ? sb.ToString() : "DEFAULTS: (none changed)";
        }

        static float Signed(float a) => a > 180f ? a - 360f : a;

        // ------------------------------------------------------------------ gui
        GUIStyle panelStyle, buttonStyle, toggleStyle, labelStyle,
            rowLabelStyle, sectionHeaderStyle, dimLabelStyle, textAreaStyle;
        Texture2D panelTex;
        bool stylesReady;

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            panelTex = new Texture2D(1, 1);
            panelTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.80f));
            panelTex.Apply();

            panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTex } };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            toggleStyle = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            labelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 12, normal = { textColor = Color.white } };
            rowLabelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 12, normal = { textColor = Color.white } };
            sectionHeaderStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
            dimLabelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 10, wordWrap = true, normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };
            textAreaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 9, wordWrap = true };
        }

        void OnGUI()
        {
            EnsureStyles();

            // Screen.dpi is 0 on some desktop setups; fall back to 1x there
            // rather than clamping a divide-by-zero into 1 by accident.
            float dpi = Screen.dpi;
            float scale = dpi > 0f ? Mathf.Clamp(dpi / 160f, 1f, 3f) : 1f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float logicalW = Screen.width / scale;
            float logicalH = Screen.height / scale;

            if (!expanded)
            {
                // Collapsed: ONLY this rect is drawn and ONLY this rect can
                // consume a touch. Top-left, clear of the bottom-half helm
                // zone by construction.
                var rect = new Rect(10f, 10f, 84f, RowH);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                if (GUI.Button(rect, "FEEL", buttonStyle)) expanded = true;
                GUI.color = prev;
                return;
            }

            DrawExpanded(logicalW, logicalH);
        }

        void DrawExpanded(float logicalW, float logicalH)
        {
            float panelH = logicalH * 0.5f; // top half only -- bottom stays free to steer
            var panelRect = new Rect(0f, 0f, logicalW, panelH);
            GUI.Box(panelRect, GUIContent.none, panelStyle);

            const float pad = 8f;
            float innerW = logicalW - pad * 2f;
            float y = pad;

            const float closeW = 76f;
            if (GUI.Button(new Rect(pad + innerW - closeW, y, closeW, RowH * 0.8f), "CLOSE", buttonStyle))
                expanded = false;
            GUI.Label(new Rect(pad, y, innerW - closeW - 8f, RowH * 0.8f), readoutText, labelStyle);
            y += RowH * 0.8f + 4f;

            float bw = (innerW - 16f) / 5f;
            float bx = pad;
            if (GUI.Button(new Rect(bx, y, bw, RowH), "RESET", buttonStyle)) ResetAll();
            bx += bw + 4f;
            if (GUI.Button(new Rect(bx, y, bw, RowH), "SAVE", buttonStyle))
            {
                PlayerPrefs.SetString(PrefsKey, BuildJson());
                PlayerPrefs.Save();
            }
            bx += bw + 4f;
            if (GUI.Button(new Rect(bx, y, bw, RowH), "LOAD", buttonStyle))
                ApplyDict(ParseFlatJson(PlayerPrefs.GetString(PrefsKey, string.Empty)));
            bx += bw + 4f;
            if (GUI.Button(new Rect(bx, y, bw, RowH), "COPY", buttonStyle))
                GUIUtility.systemCopyBuffer = BuildJson();
            bx += bw + 4f;
            if (GUI.Button(new Rect(bx, y, bw, RowH), "LOG", buttonStyle))
                Debug.Log(BuildJson());
            y += RowH + 6f;

            showJson = GUI.Toggle(new Rect(pad, y, 110f, RowH * 0.7f), showJson, "SHOW JSON", toggleStyle);
            y += RowH * 0.7f + 4f;

            if (showJson)
            {
                const float jsonH = 70f;
                GUI.TextArea(new Rect(pad, y, innerW, jsonH), BuildJson(), textAreaStyle);
                y += jsonH + 4f;
            }

            GUI.Label(new Rect(pad, y, innerW, RowH * 0.7f), defaultsDeltaText, dimLabelStyle);
            y += RowH * 0.7f + 4f;

            if (sections.Count == 0)
            {
                GUI.Label(new Rect(pad, y, innerW, RowH),
                    "waiting for HelmTuning / HandlingTuning / JuiceTuning to compile in…", dimLabelStyle);
                return;
            }

            var viewRect = new Rect(pad, y, innerW, Mathf.Max(0f, panelH - y - pad));
            float contentH = 0f;
            foreach (var s in sections)
                contentH += RowH * 0.8f + s.Knobs.Count * (RowH + 2f) + 6f;
            var contentRect = new Rect(0f, 0f, innerW - 18f, contentH);

            scroll = GUI.BeginScrollView(viewRect, scroll, contentRect);
            float cy = 0f;
            foreach (var s in sections)
            {
                GUI.Label(new Rect(0f, cy, contentRect.width, RowH * 0.8f), s.Name, sectionHeaderStyle);
                cy += RowH * 0.8f;
                foreach (var k in s.Knobs)
                {
                    DrawKnobRow(new Rect(0f, cy, contentRect.width, RowH), k);
                    cy += RowH + 2f;
                }
                cy += 6f;
            }
            GUI.EndScrollView();
        }

        void DrawKnobRow(Rect r, Knob k)
        {
            float labelW = r.width * 0.34f;

            GUI.Label(new Rect(r.x, r.y, labelW, r.height), k.Label, rowLabelStyle);

            if (k.IsBool)
            {
                bool cur = (bool)k.Field.GetValue(null);
                bool next = GUI.Toggle(new Rect(r.x + labelW, r.y, r.width - labelW, r.height),
                    cur, cur ? "ON" : "OFF", toggleStyle);
                if (next != cur) k.Field.SetValue(null, next);
                return;
            }

            float value = (float)k.Field.GetValue(null);

            const float valueW = 56f;
            int roundedKey = Mathf.RoundToInt(value * 100f);
            if (k.ValueTextKey != roundedKey)
            {
                k.ValueTextKey = roundedKey;
                k.ValueText = value.ToString("F2", CultureInfo.InvariantCulture);
            }
            GUI.Label(new Rect(r.x + labelW, r.y, valueW, r.height), k.ValueText, rowLabelStyle);

            float sliderW = r.width - labelW - valueW - 4f;
            float newValue = GUI.HorizontalSlider(
                new Rect(r.x + labelW + valueW + 4f, r.y + r.height * 0.5f - 6f, Mathf.Max(10f, sliderW), 12f),
                value, k.Min, k.Max);
            if (!Mathf.Approximately(newValue, value))
                k.Field.SetValue(null, newValue);
        }
    }
}
