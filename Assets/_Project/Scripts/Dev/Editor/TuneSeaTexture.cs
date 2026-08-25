using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// The sea has the right SIZE and the wrong TEXTURE.
///
/// Kevin's note, and the screenshots agree: the calm water is boring, and the
/// 62 m storm rollers read as sailing up a hill rather than into a wave. Two
/// causes, and neither is height:
///
/// 1. THE STORM IS ONE NARROW TRAIN. swellSharpness was pushed to 5 on purpose
///    during the storm-wave pass, to make the rollers uniform — and it worked,
///    which is the problem. A single narrow band is parallel corduroy: every
///    face is the same face, arriving from the same bearing, and a ship
///    climbing it is climbing a hillside. Real storm seas carry two or three
///    trains at an angle, and the steep pyramidal peaks where they cross are
///    what reads as CONFUSED water. No amount of height produces that from one
///    train.
///
/// 2. THERE IS ALMOST NOTHING AT THE SCALES THE EYE READS AS ROUGH. At 22 m/s
///    over 200 km of fetch, JONSWAP puts its own peak past 150 m, and the tail
///    below 40 m carries very little of the variance. Physically correct;
///    visually a sheet. detailGain over-drives that tail so texture becomes a
///    separate knob from size — the same separation that had to be made
///    between amplitude and choppiness.
///
/// THE STORM DOES NOT GET BIGGER. The second train is carved OUT of the first
/// rather than added to it (70 -> 64 m, plus 24 m crossing) so total variance
/// is held roughly constant: 306 m^2 before, 292 m^2 after. Hs, the depth
/// limit, the seabed clearance and everything RideProbe measured stay where
/// they were. What changes is that the energy arrives from two directions.
///
/// Re-run WaveSizeProbe after this: detailGain adds tail variance, and if
/// measured Hs drifts away from the declared nominalHs the depth limit starts
/// capping against the wrong number.
public static class TuneSeaTexture
{
    const string Dir = "Assets/_Project/Settings/Resources/Ocean/";

    public static string Execute()
    {
        var sb = new StringBuilder();

        // --- STORMY ------------------------------------------------------
        // Main train drops 70 -> 64 and loosens 5 -> 3.2 so the band spreads
        // in wavelength AND bearing; the second train crosses at 45 degrees,
        // short enough (170 m) to break up the long faces without being chop.
        Tune(sb, "SeaState_Stormy", s =>
        {
            s.swellHeight = 64f;
            s.swellSharpness = 3.2f;
            s.swell2Height = 24f;
            s.swell2Wavelength = 170f;
            s.swell2DirectionDeg = 45f;
            s.swell2Sharpness = 2.5f;
            s.detailGain = 6f;
            s.detailWavelength = 40f;
        });

        // --- NORMAL ------------------------------------------------------
        Tune(sb, "SeaState_Normal", s =>
        {
            s.swellHeight = 0.4f;
            s.swellSharpness = 4f;
            s.swell2Height = 1.2f;
            s.swell2Wavelength = 120f;
            s.swell2DirectionDeg = 50f;
            s.swell2Sharpness = 3f;
            s.detailGain = 5f;
            s.detailWavelength = 30f;
        });

        // --- CALM --------------------------------------------------------
        // "Too calm and boring" is the note. A calm SHELF is the design, but
        // calm is not the same as flat: real sheltered water still has a long
        // gentle undulation under a skin of small ripple. windSpeed 5 -> 7 and
        // a small swell give it something to do; the detail gain goes highest
        // here and starts shortest, because at 7 m/s the small scales are ALL
        // the sea has and the eye is close to them.
        Tune(sb, "SeaState_Calm", s =>
        {
            s.windSpeed = 7f;
            s.swellHeight = 0.35f;
            s.swellWavelength = 160f;
            s.swellSharpness = 4f;
            s.swell2Height = 0.18f;
            s.swell2Wavelength = 70f;
            s.swell2DirectionDeg = 55f;
            s.swell2Sharpness = 3f;
            s.detailGain = 8f;
            s.detailWavelength = 20f;
            s.nominalHs = 1.3f;
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return sb.ToString();
    }

    static void Tune(StringBuilder sb, string name, System.Action<OceanSpectrumSettings> apply)
    {
        var s = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(Dir + name + ".asset");
        if (s == null) { sb.AppendLine(name + ": NOT FOUND"); return; }

        float beforeVar = Variance(s);
        apply(s);
        EditorUtility.SetDirty(s);
        float afterVar = Variance(s);

        sb.AppendLine($"{name}: swell {s.swellHeight:F1} m @ {s.swellWavelength:F0} m (sharp {s.swellSharpness:F1})"
            + $" + {s.swell2Height:F1} m @ {s.swell2Wavelength:F0} m at {s.swell2DirectionDeg:F0} deg"
            + $"  detail x{s.detailGain:F0} below {s.detailWavelength:F0} m"
            + $"  |  swell variance {beforeVar:F1} -> {afterVar:F1} m^2"
            + $" (Hs from swell alone {4f * Mathf.Sqrt(beforeVar):F1} -> {4f * Mathf.Sqrt(afterVar):F1} m)");
    }

    /// Variance carried by the swell trains alone, m^2. m0 = (Hs/4)^2 each,
    /// and independent trains add in variance, not in height — which is the
    /// whole reason splitting one train into two can be size-neutral.
    static float Variance(OceanSpectrumSettings s)
    {
        float a = s.swellHeight * 0.25f;
        float b = s.swell2Height * 0.25f;
        return a * a + b * b;
    }
}
