using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// Authors the short water on the four sea-state assets: the new wind-sea
/// band, the crossing swell (shortened), and the detail gain.
///
/// The band is new (2026-09-02) and it exists because `HullFloatProbe` floated
/// the whole ship progression and found that hull SIZE changed nothing about
/// how a hull rides: raft and three-decker both rolled 14-17 degrees in the
/// storm, 19x of length apart. Every joule in this sea sat in two narrow
/// trains at 150-470 m, and against water that long a ship is a cork whatever
/// she measures.
///
/// **The quantity is not slope, it is the LENGTH the slope changes over.**
/// That correction is worth writing down, because the obvious reading is
/// wrong: the storm swell is steeper than anything added here. 64 m over
/// 470 m is 23 degrees; the 3.4 m band at 38 m is 16. Adding slope was never
/// the point and would not have worked.
///
/// A hull averages a wave over her own length. Against a 470 m swell every
/// hull in this fleet spans a tenth of a wavelength or less, so all of them —
/// raft and three-decker alike — sit on what is effectively one uniform tilt
/// and follow it exactly. Nothing about length can matter, at any steepness.
/// Against a 38 m wave the three-decker spans 1.2 wavelengths and averages
/// most of it to nothing, while the raft spans 0.15 and takes the whole ride.
/// THAT is the difference the progression needs, and the only way to get it
/// is water whose wavelength is comparable to the hulls.
///
/// Sized against the fleet rather than against the wind, and openly so: the
/// physically honest wind sea for 22 m/s over 200 km of fetch is about 5 m at
/// 150 m, which is real and which this sea's 64 m swell would swallow whole.
/// Wave height here is already a game number (see WorldScale), so the band
/// that has to be felt beside it is one too. Same admission `detailGain`
/// makes.
///
/// LOA / wavelength for the fleet, per band — a hull begins to bridge and
/// resist a sea as this approaches 1, and is a cork below about 0.2:
///
///                    raft  skiff  sloop  brig  3-decker
///   stormy wind sea  0.13   0.20   0.34  0.59    1.05    (44 m, NEW)
///   stormy crossing  0.03   0.05   0.09  0.15    0.27    (170 m, untouched)
///   stormy primary   0.01   0.02   0.03  0.06    0.10    (470 m, untouched)
///
/// Before this the top two rows were the whole sea and the fleet sat between
/// 0.01 and 0.27 of a wavelength — one uniform tilt under every hull.
///
/// **Shortening the crossing swell was tried, measured, and reverted.**
/// It was the obvious next lever: 24 m at 170 m is real energy parked as far
/// out of reach as the primary train. Moved to 90 m it has to drop to 12 m to
/// stay under the breaking limit, and the numbers said no. Pitch ordering got
/// cleaner (the three-decker finally came out best at 3.6 degrees against the
/// raft's 6.4), but the storm stopped being a storm: measured Hs fell 27.1 to
/// 20.7 m — far more than the 4 % the quadrature predicted, because a 1500 m
/// transect samples a 90 m train well and a 470 m one barely at all — and the
/// raft went from having her deck under 22 % of the time to 4 %. Vertical
/// acceleration collapsed from 2.9 g to 1.0 g on the raft and the whole fleet
/// flattened. Trading the storm's teeth for one column of ordering is a bad
/// deal, so the crossing swell keeps its height and its 170 m.
///
/// **Roll cannot be made to rank by hull size, and this stopped trying.**
/// Roll is driven by the wave slope across the BEAM, so it wants wavelengths
/// near 13 m, not near 46 — and at 13 m the steepness limit caps a train at
/// about 1.5 m, which is nothing beside a 64 m sea. The three-decker will
/// always roll most here because she is the beamiest thing in the water. Pitch,
/// vertical acceleration and green water are the three that do rank, and they
/// are enough.
public static class TuneWindSea
{
    const string Dir = "Assets/_Project/Settings/Resources/Ocean";

    public static string Execute()
    {
        var sb = new StringBuilder("=== TuneWindSea ===\n");

        //            wind sea            crossing swell2       detail
        //            Hs   lambda sharp   Hs     lambda sharp   gain
        Apply(sb, "Calm",   0.40f, 18f, 2.6f,   0.18f,  70f, 3.0f, 10f);
        Apply(sb, "Normal", 1.30f, 24f, 2.5f,   1.20f, 120f, 3.0f,  9f);
        Apply(sb, "Rough",  3.20f, 34f, 2.4f,   5.00f, 150f, 2.8f,  9f);
        Apply(sb, "Stormy", 5.00f, 44f, 2.3f,  24.00f, 170f, 2.5f,  9f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(sb.ToString());
        return sb.ToString();
    }

    static void Apply(StringBuilder sb, string name,
                      float hs, float lambda, float sharp,
                      float s2hs, float s2lambda, float s2sharp, float gain)
    {
        var s = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>($"{Dir}/SeaState_{name}.asset");
        if (s == null) { sb.AppendLine($"  MISSING SeaState_{name}"); return; }

        float beforeHs = TotalHs(s);
        float beforeS2 = s.swell2Wavelength;

        s.windSeaHeight = hs;
        s.windSeaWavelength = lambda;
        s.windSeaSharpness = sharp;
        s.swell2Height = s2hs;
        s.swell2Wavelength = s2lambda;
        s.swell2Sharpness = s2sharp;
        s.detailGain = gain;
        EditorUtility.SetDirty(s);

        sb.AppendLine($"  {name,-7} wind sea {hs,5:F2} m at {lambda,3:F0} m (steep {hs / lambda,5:F3})   "
                      + $"crossing {s2hs,5:F2} m at {s2lambda,3:F0} m was {beforeS2,3:F0} m "
                      + $"(steep {s2hs / s2lambda,5:F3})   "
                      + $"total Hs {beforeHs,5:F1} -> {TotalHs(s),5:F1} m "
                      + $"(declared {s.nominalHs:F1})");
    }

    /// Trains add in VARIANCE, so heights add in quadrature. This is how the
    /// height cost of shortening the crossing swell gets stated instead of
    /// guessed: steepness caps a short train's height, so moving energy down
    /// in wavelength always costs some, and the only honest question is how
    /// much.
    ///
    /// The three GAUSSIAN TRAINS only — it does not count the JONSWAP wind
    /// sea, which is where nearly all of a calm's height comes from. So the
    /// number is meaningful for the big states, where the trains are the sea,
    /// and reads far below the declared Hs for the small ones, where it is
    /// not. `WaveSizeProbe` measures the real thing.
    static float TotalHs(OceanSpectrumSettings s) =>
        Mathf.Sqrt(s.swellHeight * s.swellHeight
                   + s.swell2Height * s.swell2Height
                   + s.windSeaHeight * s.windSeaHeight);
}
