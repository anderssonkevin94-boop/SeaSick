using UnityEngine;
using SeaSick.Ocean;

/// Artist knobs over the physical spectrum.
///
/// `OceanSpectrumSettings` is authored in wind speed, fetch, and two swell
/// trains, which is the right way to STORE a sea and a miserable way to tune
/// one: "make the waves rougher" is four fields, three of which interact, and
/// two of which will quietly change the wave height while you are trying not
/// to. This collapses them into height / wavelength / chaos / texture and
/// derives the physics, so a knob does one job.
///
/// The important property is that HEIGHT AND CHAOS ARE INDEPENDENT. Chaos
/// moves energy between the two trains rather than adding any, because
/// independent trains add in VARIANCE, not in height: total variance is
/// (Hs/4)^2 and it is held constant as the split changes. Turning chaos up
/// cannot make the sea bigger, which is exactly the confusion that made the
/// old amplitude/steepness pair unusable.
///
/// `nominalHs` is written from the height knob, so the depth limit always
/// knows how tall the sea it is capping actually is.
///
/// These are ESTIMATES for live feedback while dragging. WaveSizeProbe is the
/// truth; run it before believing a number.
public static class StormShape
{
    public struct Knobs
    {
        public float heightHs;    // total significant wave height, metres
        public float wavelength;  // main train wavelength, metres
        public float chaos;       // 0 = one clean train, 1 = thoroughly confused
        public float texture;     // detailGain: surface roughness, 1 = physical
        public float choppiness;  // crest sharpness
    }

    // Chaos endpoints. Chosen so chaos 0.5 reproduces the shipped storm
    // (sharpness 3.2, cross train at 45 deg carrying 12% of the variance at
    // 0.36 of the main wavelength) -- so the slider opens centred on a sea
    // that has already been measured rather than on an arbitrary midpoint.
    const float SharpCalm = 5.0f, SharpWild = 1.4f;
    const float Sharp2Calm = 4.0f, Sharp2Wild = 1.2f;
    const float CrossVarCalm = 0f, CrossVarWild = 0.30f;
    const float CrossAngleCalm = 15f, CrossAngleWild = 75f;
    const float CrossLamCalm = 0.55f, CrossLamWild = 0.17f;

    public static Knobs Read(OceanSpectrumSettings s)
    {
        float v1 = Sq(s.swellHeight * 0.25f);
        float v2 = Sq(s.swell2Height * 0.25f);
        return new Knobs
        {
            heightHs = 4f * Mathf.Sqrt(Mathf.Max(v1 + v2, 0f)),
            wavelength = s.swellWavelength,
            // Read back off sharpness: it is the one term that is always
            // meaningful, where the cross-train fraction is ambiguous at
            // zero height.
            chaos = Mathf.Clamp01(Mathf.InverseLerp(SharpCalm, SharpWild, s.swellSharpness)),
            texture = s.detailGain,
            choppiness = s.choppiness,
        };
    }

    public static void Apply(OceanSpectrumSettings s, Knobs k)
    {
        float chaos = Mathf.Clamp01(k.chaos);
        float totalVar = Sq(Mathf.Max(k.heightHs, 0f) * 0.25f);
        float f = Mathf.Lerp(CrossVarCalm, CrossVarWild, chaos);

        s.swellHeight = 4f * Mathf.Sqrt(totalVar * (1f - f));
        s.swell2Height = 4f * Mathf.Sqrt(totalVar * f);
        s.swellWavelength = Mathf.Max(k.wavelength, 10f);
        s.swell2Wavelength = Mathf.Max(k.wavelength * Mathf.Lerp(CrossLamCalm, CrossLamWild, chaos), 10f);
        s.swellSharpness = Mathf.Lerp(SharpCalm, SharpWild, chaos);
        s.swell2Sharpness = Mathf.Lerp(Sharp2Calm, Sharp2Wild, chaos);
        s.swell2DirectionDeg = s.swellDirectionDeg + Mathf.Lerp(CrossAngleCalm, CrossAngleWild, chaos);
        s.detailGain = Mathf.Max(k.texture, 1f);
        s.choppiness = Mathf.Clamp(k.choppiness, 0f, 2f);
        // Declared height follows the knob, so the depth limit keeps capping
        // against the sea that is actually being produced.
        s.nominalHs = k.heightHs;
    }

    /// Rough face angle, degrees. A wave of height H and length L has a
    /// maximum face slope of atan(pi H / L); the typical wave in a narrow
    /// spectrum runs about 0.71 Hs (measured during the storm-wave pass).
    ///
    /// This lands within a couple of degrees of what WaveSizeProbe reports
    /// (predicted 18, measured median 15.4) and is here so the slider has a
    /// number next to it while you drag. It is NOT the acceptance criterion.
    public static float FaceSlopeDeg(Knobs k)
    {
        float h = 0.71f * Mathf.Max(k.heightHs, 0f);
        float l = Mathf.Max(k.wavelength, 1f);
        return Mathf.Atan(Mathf.PI * h / l) * Mathf.Rad2Deg;
    }

    /// Face length in boat lengths — the storm-wave pass's other criterion.
    /// A face is about a quarter wavelength of climbable water.
    public static float FaceBoatLengths(Knobs k, float boatLength)
    {
        return (Mathf.Max(k.wavelength, 1f) * 0.5f) / Mathf.Max(boatLength, 0.1f);
    }

    /// Metres of water needed before the depth limit stops capping this sea.
    public static float DepthNeeded(Knobs k, float breakFraction)
    {
        return breakFraction > 0.01f ? k.heightHs / breakFraction : 0f;
    }

    static float Sq(float x) => x * x;
}
