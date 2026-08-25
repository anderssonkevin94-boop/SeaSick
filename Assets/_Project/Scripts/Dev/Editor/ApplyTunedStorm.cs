using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// Kevin's storm, from the tuning session on 2026-08-25.
///
/// The Storm Tuner marked the asset dirty but never called SaveAssets, so the
/// session's values never reached disk and had to be read back off a
/// screenshot. The tuner writes on mouse-up now; this restores what he picked
/// so everything measured afterwards is measured against the sea he actually
/// chose rather than the one I shipped.
///
/// Deliberately going in unmodified, including the two values I flagged:
///
///   crest sharpness 0.195 — foam is injected where the surface FOLDS, and
///   folding is what choppiness does. At 0.195 the crests are rounded and
///   there may be very little foam left. FoamProbe should say.
///
///   Hs 76 on 299 m — an estimated 29.6 degree face on about 6.2 boat lengths,
///   against a storm-wave-pass target of 15-20 degrees on 10-18 lengths. That
///   is back toward the "wall taller than it is wide" that GDD section 5 rules
///   out. It is also the reason the rest of the world went flat: the depth
///   limit is proportional to Hs, so 76 m needs 138 m of water and crushes
///   everything shallower.
///
/// Both are his call to make with numbers in hand, which is what WaveSizeProbe
/// and FoamProbe are for. Recorded here so the reasoning is not lost.
public static class ApplyTunedStorm
{
    const string Path = "Assets/_Project/Settings/Resources/Ocean/SeaState_Stormy.asset";

    public static string Execute()
    {
        var s = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(Path);
        if (s == null) return "no SeaState_Stormy at " + Path;

        var k = new StormShape.Knobs
        {
            heightHs = 76.1f,
            wavelength = 299f,
            chaos = 0.283f,
            texture = 9.81f,
            choppiness = 0.195f,
        };

        Undo.RecordObject(s, "Apply tuned storm");
        StormShape.Apply(s, k);
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();

        // Read back from disk. The whole reason this script exists is that a
        // dirty asset is not a saved one.
        AssetDatabase.Refresh();
        var check = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(Path);
        var got = StormShape.Read(check);

        return $"storm now: main {check.swellHeight:F1} m @ {check.swellWavelength:F0} m "
             + $"(sharp {check.swellSharpness:F1}) + cross {check.swell2Height:F1} m @ "
             + $"{check.swell2Wavelength:F0} m at {check.swell2DirectionDeg - check.swellDirectionDeg:F0} deg, "
             + $"detail x{check.detailGain:F1}, choppiness {check.choppiness:F2}, nominalHs {check.nominalHs:F1}"
             + $"  |  reads back as height {got.heightHs:F1} chaos {got.chaos:F2}"
             + $"  |  est face {StormShape.FaceSlopeDeg(got):F1} deg, "
             + $"{StormShape.FaceBoatLengths(got, 24.2f):F1} boat lengths, "
             + $"needs {StormShape.DepthNeeded(got, 0.55f):F0} m of water";
    }
}
