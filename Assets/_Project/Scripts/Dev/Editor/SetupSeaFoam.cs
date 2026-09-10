using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ocean;

/// Wires the foam/crest pass into Sea.unity and reports whether the two
/// shaders it touches actually compiled.
///
/// Everything goes through `SerializedObject` because Unity serialised the old
/// values into the scene and the asset files when those components were added,
/// and a serialised value beats a C# field initialiser forever — the trap that
/// has bitten this project on `WeatherField`, on `SpeedJuice` and on the storm
/// sky material. Re-run it after changing any default here.
///
/// **`check_compile_errors` is C# only.** A broken shader reports clean and
/// then renders magenta, and a broken COMPUTE shader is worse: the dispatch is
/// silently skipped and the textures keep whatever was in them, which reads as
/// a frozen sea rather than an error. So this reimports both and says so.
public static class SetupSeaFoam
{
    // A visible starting point for the near-field chop, not a derived number.
    // Cascade 2 is the 32 m patch and it is NOT in the physics readback
    // (`DisplacementReadback.PhysicsCascades` is 2), so sharpening it costs no
    // Newton iteration — which is the only reason a number can be moved here
    // at all. `SeaFoamTuner` is how it gets its final value.
    const float CrestSharpenZ = 1.35f;

    /// The AUTHORED foam values for each sea state, restated here so this
    /// script can put them back rather than merely hope they are still right.
    ///
    /// This exists because of a fault that cost real time on 2026-09-09 and
    /// which every tool in this project can trip: a `ScriptableObject` under
    /// Resources IS the asset, a runtime write to it survives leaving play
    /// mode, and `AssetDatabase.ImportAsset(..., ForceUpdate)` does NOT throw
    /// that away — a dirty in-memory object wins. So a tuner or a probe that
    /// writes these at runtime leaves four flattened anchors loaded, and the
    /// next `SaveAssets` anywhere in the editor writes the flattening to disk.
    /// It did: all four went to 0.50 / 0.45 / 4.0 and had to come back out of
    /// git.
    ///
    /// name, foamThreshold, foamHalflife, foamInjection, foamInjectSharpness.
    /// The first four are the values these assets have always carried; the
    /// sharpness is new and 6 replaces a hard-coded 2 — see the note in
    /// FoamAccumulate.compute for what 2 was doing to the sea.
    static readonly (string name, float thr, float half, float inj, float sharp)[] Foam =
    {
        ("SeaState_Calm",   0.02f, 3.0f, 0.15f, 6f),
        ("SeaState_Normal", 0.03f, 4.0f, 0.40f, 6f),
        ("SeaState_Rough",  0.05f, 4.2f, 0.42f, 6f),
        ("SeaState_Stormy", 0.10f, 4.5f, 0.45f, 6f),
    };

    /// The foam material's values, pushed EXPLICITLY.
    ///
    /// A `.mat` snapshots a shader property's default when the property is
    /// created and a later change to that default never reaches it — the trap
    /// that had `_StormDeep` reading a stale colour for a whole session. The
    /// ocean material already carries `_FoamJThreshold` at its old value, so
    /// changing the shader default would have done nothing at all. Anything
    /// here that matters gets written and read back.
    static readonly (string prop, float value)[] MatFoam =
    {
        // How folded the water has to be before the shader calls it breaking.
        // 0.72 was authored against a Jacobian that was missing its cross term
        // AND was being weighted to death by the clipmap, so it had to be
        // generous to produce anything. Both are fixed, so it can be strict.
        ("_FoamJThreshold", 0.55f),
        ("_FoamSnap",       0.12f),
        ("_FoamCrestGain",  1.00f),
        ("_FoamRelief",     1.10f),
        ("_FoamSparkle",    0.70f),
        ("_FoamBandLift",   0.70f),
        ("_FoamLiftFar",    420f),
        ("_FoamTrailFloor", 0.20f),
        ("_FoamTrailGain",  1.80f),
    };

    public static string Execute()
    {
        var log = new System.Text.StringBuilder();

        // --- the shaders -----------------------------------------------------
        log.AppendLine(Reimport("Assets/_Project/Art/Shaders/Ocean/Ocean.shader"));
        log.AppendLine(Reimport("Assets/_Project/Art/Shaders/Ocean/TimeEvolve.compute"));
        log.AppendLine(Reimport("Assets/_Project/Art/Shaders/Ocean/FoamAccumulate.compute"));

        // --- the sea states --------------------------------------------------
        // Every value is WRITTEN, not read-modify-written, because the loaded
        // objects cannot be trusted to still hold what the files hold. See the
        // Foam table above.
        foreach (var (name, thr, half, inj, sharp) in Foam)
        {
            var asset = Resources.Load<OceanSpectrumSettings>("Ocean/" + name);
            if (asset == null) { log.AppendLine($"{name}: NOT FOUND"); continue; }
            var so = new SerializedObject(asset);
            var p = so.FindProperty("crestSharpen");
            if (p == null) { log.AppendLine($"{name}: no crestSharpen property"); continue; }
            p.vector3Value = new Vector3(1f, 1f, CrestSharpenZ);
            so.FindProperty("foamThreshold").floatValue = thr;
            so.FindProperty("foamHalflife").floatValue = half;
            so.FindProperty("foamInjection").floatValue = inj;
            var sp = so.FindProperty("foamInjectSharpness");
            if (sp != null) sp.floatValue = sharp;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            log.AppendLine($"{name}: crestSharpen z {CrestSharpenZ:F2}, "
                         + $"foamThreshold {thr:F2}, halflife {half:F1}, injection {inj:F2}, "
                         + $"injectSharpness {sharp:F1}");
        }
        AssetDatabase.SaveAssets();

        // --- the ocean material ---------------------------------------------
        Material ocean = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/OceanSurface.mat");
        if (ocean == null) log.AppendLine("OceanSurface.mat: NOT FOUND");
        else
        {
            foreach (var (prop, value) in MatFoam)
            {
                if (!ocean.HasProperty(prop)) { log.AppendLine($"  {prop}: NOT ON SHADER"); continue; }
                ocean.SetFloat(prop, value);
            }
            EditorUtility.SetDirty(ocean);
            AssetDatabase.SaveAssets();
            // Read back, because "I set it" and "it is set" are different
            // claims and this project has been caught out by the difference.
            var back = new System.Text.StringBuilder();
            foreach (var (prop, value) in MatFoam)
                back.Append($"{prop}={(ocean.HasProperty(prop) ? ocean.GetFloat(prop) : float.NaN):F2} ");
            log.AppendLine("OceanSurface.mat read back: " + back);
        }

        // --- the tuner -------------------------------------------------------
        // Hung off whatever object already carries the water tuner, so the dev
        // controls stay in one place in the hierarchy.
        var clarity = Object.FindAnyObjectByType<WaterClarityTuner>(FindObjectsInactive.Include);
        GameObject host = clarity != null ? clarity.gameObject
                                          : GameObject.Find("SeaFoamTuner");
        if (host == null)
        {
            host = new GameObject("SeaFoamTuner");
            log.AppendLine("created host GameObject 'SeaFoamTuner'");
        }
        var tuner = host.GetComponent<SeaFoamTuner>();
        if (tuner == null)
        {
            tuner = host.AddComponent<SeaFoamTuner>();
            log.AppendLine($"added SeaFoamTuner to '{host.name}'");
        }
        var tso = new SerializedObject(tuner);
        tso.FindProperty("active").boolValue = true;
        tso.ApplyModifiedPropertiesWithoutUndo();

        // --- the spray -------------------------------------------------------
        var spray = Object.FindAnyObjectByType<StormSpray>(FindObjectsInactive.Include);
        if (spray == null) log.AppendLine("StormSpray: NOT IN SCENE — no crest spume");
        else
        {
            // The scene's StormSpray predates every field the breaking-crest
            // section added, so those arrive from the field initialisers; the
            // ones written here are the two the look depends on.
            var sso = new SerializedObject(spray);
            SetIfPresent(sso, "foamGateFraction", 0.55f);
            SetIfPresent(sso, "foamGateFloor", 0.02f);
            SetIfPresent(sso, "breakerRate", 7f);
            sso.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"StormSpray on '{spray.gameObject.name}': breaking crests wired");
        }

        // --- the ship's foam rig --------------------------------------------
        var juice = Object.FindAnyObjectByType<SeaSick.Ship.SpeedJuice>(FindObjectsInactive.Include);
        log.AppendLine(juice == null
            ? "SpeedJuice: NOT IN SCENE"
            : "SpeedJuice: present — Shipyard.Refit now sizes its rig to the rung");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        log.AppendLine("scene saved");
        var s = log.ToString();
        Debug.Log("SetupSeaFoam:\n" + s);
        return s;
    }

    static void SetIfPresent(SerializedObject so, string name, float v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.floatValue = v;
    }

    static string Reimport(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (obj == null) return path + ": NOT FOUND";
        if (obj is Shader sh) return $"{path}: isSupported={sh.isSupported}";
        if (obj is ComputeShader cs)
            return $"{path}: kernel 0 supported={cs.HasKernel(FirstKernel(path))}";
        return path + ": reimported";
    }

    static string FirstKernel(string path) =>
        path.EndsWith("TimeEvolve.compute") ? "ResolveOutputs" : "FoamAccumulate";
}
