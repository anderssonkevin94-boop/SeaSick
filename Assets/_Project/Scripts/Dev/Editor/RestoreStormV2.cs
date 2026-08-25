using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// The storm sea as it stood after the texture pass (commit 7d29ef9) — the one
/// Kevin judged good, and the point to come back to.
///
/// "We have lost what made the general ocean good. Now it's just very very
/// flat." Comparing the two spectra, four things had drifted, and every one of
/// them pushes toward uniform rounded rollers:
///
///   choppiness   1.0 -> 0.45   the big one. Choppiness IS crest sharpness, so
///                              halving it rounds every crest into a sinusoid.
///                              A sea can be steeper on average and still read
///                              as flat if nothing on it comes to a point.
///   wavelength   470 -> 299    shorter waves, and the face went 9.5 boat
///                              lengths to 6.2.
///   sharpness    3.2 -> 3.98   a NARROWER swell band, which is more uniform,
///                              which is the corduroy the texture pass existed
///                              to break up.
///   cross train  170 m at 45 deg -> 132 m at 32 deg. Less angle between the
///                              trains means less interference, so fewer of the
///                              confused pyramidal peaks that read as a storm.
///
/// Written as LITERAL FIELD VALUES rather than through StormShape, on purpose.
/// The knobs derive the two trains from (height, chaos), and round-tripping v2
/// through them lands close but not identical -- 63.0/26.5 m against 64/24, and
/// nominalHs 68.4 against 65. Close is not the same as the sea he approved, and
/// nominalHs is what the depth limit caps against.
///
/// Measured at v2 (WaveSizeProbe, severity 1.00): Hs 62.05 m, face angle median
/// 15.4 deg, face length 230 m = 9.5 boat lengths, seabed clearance 40.6 m.
public static class RestoreStormV2
{
    const string Path = "Assets/_Project/Settings/Resources/Ocean/SeaState_Stormy.asset";

    public static string Execute()
    {
        var s = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(Path);
        if (s == null) return "no SeaState_Stormy at " + Path;

        Undo.RecordObject(s, "Restore storm v2");
        s.windSpeed = 22f; s.fetchKm = 200f; s.gamma = 3.3f;
        s.windDirectionDeg = 15f; s.depth = 30f;
        s.swellHeight = 64f; s.swellWavelength = 470f;
        s.swellDirectionDeg = 0f; s.swellSharpness = 3.2f;
        s.swell2Height = 24f; s.swell2Wavelength = 170f;
        s.swell2DirectionDeg = 45f; s.swell2Sharpness = 2.5f;
        s.nominalHs = 65f;
        s.choppiness = 1f;
        s.detailGain = 6f; s.detailWavelength = 40f;
        s.foamThreshold = 0.3f; s.foamHalflife = 4.5f; s.foamInjection = 0.45f;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();

        var check = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(Path);
        return $"storm restored to v2: {check.swellHeight:F0} m @ {check.swellWavelength:F0} m "
             + $"(sharp {check.swellSharpness:F1}) + {check.swell2Height:F0} m @ "
             + $"{check.swell2Wavelength:F0} m at {check.swell2DirectionDeg:F0} deg, "
             + $"choppiness {check.choppiness:F2}, detail x{check.detailGain:F0}, "
             + $"nominalHs {check.nominalHs:F0}";
    }
}
