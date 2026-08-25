using UnityEngine;

namespace SeaSick.Ocean
{
    /// Everything that defines a sea state, as physical parameters an artist
    /// can reason about: wind speed and fetch shape the JONSWAP spectrum,
    /// swell is a separate long-period train, choppiness sharpens crests.
    /// Calm/Normal/Stormy are three of these assets; SeaStateController lerps
    /// between them and rebuilds the initial spectrum on a throttle.
    [CreateAssetMenu(menuName = "SeaSick/Ocean Spectrum", fileName = "SeaState")]
    public class OceanSpectrumSettings : ScriptableObject
    {
        [Header("Wind sea (JONSWAP)")]
        [Tooltip("Wind speed at 10 m, m/s. Calm ~5, Normal ~12, Stormy ~22.")]
        public float windSpeed = 12f;
        [Tooltip("Fetch in kilometres. Calm ~50, Normal ~100, Stormy ~200.")]
        public float fetchKm = 100f;
        [Tooltip("JONSWAP peak enhancement. 3.3 is the literature default.")]
        public float gamma = 3.3f;
        [Tooltip("Wind/wave direction in degrees (0 = +X, CCW).")]
        public float windDirectionDeg = 15f;
        [Tooltip("Water depth in metres for the dispersion relation.")]
        public float depth = 200f;

        [Header("Swell (long rollers, independent of local wind)")]
        [Tooltip("Significant wave height of the swell train alone, metres. 0 = none.")]
        public float swellHeight = 0f;
        public float swellWavelength = 220f;
        public float swellDirectionDeg = -20f;
        [Tooltip("How tight the swell is around its wavelength/direction. Higher = cleaner rollers -- and past about 4, PARALLEL ones: a single narrow train is corduroy, and a ship climbing it reads as going up a hill rather than into a sea.")]
        [Range(1f, 16f)] public float swellSharpness = 6f;

        [Header("Second swell train (the crossing sea)")]
        [Tooltip("A real storm sea is two or three trains at an angle to each other; where they meet you get the steep, short-lived pyramidal peaks that read as CONFUSED water. One train alone cannot produce them at any height. 0 = none.")]
        public float swell2Height = 0f;
        public float swell2Wavelength = 180f;
        [Tooltip("Degrees. What matters is the ANGLE to the main train -- 30-60 degrees gives interference you can see without destroying the rideable face.")]
        public float swell2DirectionDeg = 45f;
        [Range(1f, 16f)] public float swell2Sharpness = 3f;

        [Header("Scale (declared, then verified)")]
        [Tooltip("The significant wave height this state is expected to produce, metres. NOT an input to the simulation -- the spectrum decides the real Hs. It exists so the depth-limited envelope knows how tall the sea is without measuring it every frame, and WaveSizeProbe checks the two agree.")]
        public float nominalHs = 3f;

        [Header("Shape")]
        [Tooltip("Horizontal displacement scale (lambda). Sharpens crests; too high folds the surface.")]
        [Range(0f, 2f)] public float choppiness = 1f;

        [Tooltip("Over-drives the spectrum's short-wave tail: surface TEXTURE without touching wave size. JONSWAP's tail is physically right and visually thin, and at storm wind speeds the wind sea's own peak is out past 150 m, so almost nothing lands at the scales the eye reads as rough. This is a multiplier on spectral DENSITY, so amplitude goes as its square root -- 9 makes the short waves three times taller. 1 = physical.")]
        [Range(1f, 25f)] public float detailGain = 1f;
        [Tooltip("Wavelength, metres, where the detail gain starts. It ramps to full over the three octaves below this.")]
        public float detailWavelength = 40f;

        [Header("Foam")]
        [Tooltip("Jacobian value below which the surface is folding and foam is injected.")]
        public float foamThreshold = 0.6f;
        [Tooltip("Foam persistence half-life, seconds.")]
        public float foamHalflife = 4f;
        [Tooltip("Injection strength when crests break.")]
        public float foamInjection = 0.5f;

        public Vector2 WindDir =>
            new Vector2(Mathf.Cos(windDirectionDeg * Mathf.Deg2Rad),
                        Mathf.Sin(windDirectionDeg * Mathf.Deg2Rad));

        public Vector2 SwellDir =>
            new Vector2(Mathf.Cos(swellDirectionDeg * Mathf.Deg2Rad),
                        Mathf.Sin(swellDirectionDeg * Mathf.Deg2Rad));

        public Vector2 Swell2Dir =>
            new Vector2(Mathf.Cos(swell2DirectionDeg * Mathf.Deg2Rad),
                        Mathf.Sin(swell2DirectionDeg * Mathf.Deg2Rad));

        /// Copy every parameter (used by SeaStateController's blend target).
        public void CopyFrom(OceanSpectrumSettings s)
        {
            windSpeed = s.windSpeed; fetchKm = s.fetchKm; gamma = s.gamma;
            windDirectionDeg = s.windDirectionDeg; depth = s.depth;
            swellHeight = s.swellHeight; swellWavelength = s.swellWavelength;
            swellDirectionDeg = s.swellDirectionDeg; swellSharpness = s.swellSharpness;
            swell2Height = s.swell2Height; swell2Wavelength = s.swell2Wavelength;
            swell2DirectionDeg = s.swell2DirectionDeg; swell2Sharpness = s.swell2Sharpness;
            detailGain = s.detailGain; detailWavelength = s.detailWavelength;
            choppiness = s.choppiness; nominalHs = s.nominalHs;
            foamThreshold = s.foamThreshold; foamHalflife = s.foamHalflife;
            foamInjection = s.foamInjection;
        }

        /// Lerp a and b into this instance (SeaStateController owns a runtime copy).
        public void LerpFrom(OceanSpectrumSettings a, OceanSpectrumSettings b, float t)
        {
            windSpeed = Mathf.Lerp(a.windSpeed, b.windSpeed, t);
            fetchKm = Mathf.Lerp(a.fetchKm, b.fetchKm, t);
            gamma = Mathf.Lerp(a.gamma, b.gamma, t);
            windDirectionDeg = Mathf.LerpAngle(a.windDirectionDeg, b.windDirectionDeg, t);
            depth = Mathf.Lerp(a.depth, b.depth, t);
            swellHeight = Mathf.Lerp(a.swellHeight, b.swellHeight, t);
            swellWavelength = Mathf.Lerp(a.swellWavelength, b.swellWavelength, t);
            swellDirectionDeg = Mathf.LerpAngle(a.swellDirectionDeg, b.swellDirectionDeg, t);
            swellSharpness = Mathf.Lerp(a.swellSharpness, b.swellSharpness, t);
            swell2Height = Mathf.Lerp(a.swell2Height, b.swell2Height, t);
            swell2Wavelength = Mathf.Lerp(a.swell2Wavelength, b.swell2Wavelength, t);
            swell2DirectionDeg = Mathf.LerpAngle(a.swell2DirectionDeg, b.swell2DirectionDeg, t);
            swell2Sharpness = Mathf.Lerp(a.swell2Sharpness, b.swell2Sharpness, t);
            detailGain = Mathf.Lerp(a.detailGain, b.detailGain, t);
            detailWavelength = Mathf.Lerp(a.detailWavelength, b.detailWavelength, t);
            choppiness = Mathf.Lerp(a.choppiness, b.choppiness, t);
            nominalHs = Mathf.Lerp(a.nominalHs, b.nominalHs, t);
            foamThreshold = Mathf.Lerp(a.foamThreshold, b.foamThreshold, t);
            foamHalflife = Mathf.Lerp(a.foamHalflife, b.foamHalflife, t);
            foamInjection = Mathf.Lerp(a.foamInjection, b.foamInjection, t);
        }
    }
}
