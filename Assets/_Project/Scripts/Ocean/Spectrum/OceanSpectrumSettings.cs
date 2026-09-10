using UnityEngine;

namespace SeaSick.Ocean
{
    /// Everything that defines a sea state, as physical parameters an artist
    /// can reason about: wind speed and fetch shape the JONSWAP spectrum,
    /// swell is a separate long-period train, choppiness sharpens crests.
    /// Calm/Normal/Stormy are three of these assets; SeaStateController lerps
    /// between them and rebuilds the initial spectrum on a throttle.
    ///
    /// **A hull averages a wave over her own length, so what separates a big
    /// ship from a small one is WAVELENGTH — not height, and not steepness.**
    /// That is the reason the wind-sea band below exists, and it was measured
    /// before it was added: floating the whole ship progression through five sea
    /// states (`HullFloatProbe`, 2026-09-01) found every hull from a 5.65 m
    /// raft to a 46 m three-decker rolling the same 14-17 degrees, because
    /// every scrap of this sea's energy sat in two narrow trains at 150-470 m
    /// and at those lengths a ship is a cork whatever her size. LOA over
    /// wavelength ran 0.02 to 0.21 across the fleet; a hull only begins to
    /// bridge and resist a sea as that ratio approaches 1.
    ///
    /// The long trains could not simply be shortened. The storm swell is
    /// 64 m at 470 m, which is already steepness 0.136 — the breaking limit —
    /// so shortening it means lowering the wave height this game is built
    /// around. The fix is therefore additive: put energy in the band that was
    /// empty, and leave the mountains alone.
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

        [Header("Wind sea band — the short steep waves a hull actually feels")]
        [Tooltip("Significant height of the short wind-sea train alone, metres. 0 = none.")]
        public float windSeaHeight = 0f;
        [Tooltip("Its wavelength, metres. Keep it inside cascade 1 (16-64 m): that is the band no other component of this sea puts energy in, and the band every hull in the fleet is comparable to.")]
        public float windSeaWavelength = 32f;
        [Tooltip("Broad on purpose. Above about 4 a narrow train reads as corduroy; the wind sea is the least organised water in the spectrum and should look it.")]
        [Range(1f, 8f)] public float windSeaSharpness = 2.5f;

        [Header("Scale (declared, then verified)")]
        [Tooltip("The significant wave height this state is expected to produce, metres. NOT an input to the simulation -- the spectrum decides the real Hs. It exists so the depth-limited envelope knows how tall the sea is without measuring it every frame, and WaveSizeProbe checks the two agree.")]
        public float nominalHs = 3f;

        [Header("Shape")]
        [Tooltip("Horizontal displacement scale (lambda). Sharpens crests; too high folds the surface.")]
        [Range(0f, 2f)] public float choppiness = 1f;

        [Tooltip("Per-cascade multiplier on choppiness — crest sharpness BY BAND. " +
                 "Sharpness and sampler cost used to be the same knob: lambda is " +
                 "the horizontal displacement the CPU twin inverts by Newton, so " +
                 "raising it costs an iteration and the budget is spent. " +
                 "x = cascade 0 (the 2048 m swell) and y = cascade 1 (128 m) are " +
                 "READ BY PHYSICS and cost that iteration — do not move them " +
                 "without re-running DivergenceProbe. z = cascade 2 (the 32 m " +
                 "near-field chop) is not in the readback at all and is FREE: it " +
                 "is also the band the eye reads as sharp when you are standing " +
                 "on the deck.")]
        public Vector3 crestSharpen = new Vector3(1f, 1f, 1f);

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
        [Tooltip("How SELECTIVE the injection is — the width of the ramp below foamThreshold, " +
                 "inverted. Low is a wide ramp and fills the buffer everywhere, which reads as " +
                 "a flat milk over the whole sea; high marks only water that has really folded, " +
                 "which is what leaves trails you can see the shape of. Was a hard-coded 2.")]
        [Range(1f, 16f)] public float foamInjectSharpness = 6f;

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
            windSeaHeight = s.windSeaHeight; windSeaWavelength = s.windSeaWavelength;
            windSeaSharpness = s.windSeaSharpness;
            detailGain = s.detailGain; detailWavelength = s.detailWavelength;
            choppiness = s.choppiness; crestSharpen = s.crestSharpen;
            nominalHs = s.nominalHs;
            foamThreshold = s.foamThreshold; foamHalflife = s.foamHalflife;
            foamInjection = s.foamInjection;
            foamInjectSharpness = s.foamInjectSharpness;
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
            windSeaHeight = Mathf.Lerp(a.windSeaHeight, b.windSeaHeight, t);
            windSeaWavelength = Mathf.Lerp(a.windSeaWavelength, b.windSeaWavelength, t);
            windSeaSharpness = Mathf.Lerp(a.windSeaSharpness, b.windSeaSharpness, t);
            detailGain = Mathf.Lerp(a.detailGain, b.detailGain, t);
            detailWavelength = Mathf.Lerp(a.detailWavelength, b.detailWavelength, t);
            choppiness = Mathf.Lerp(a.choppiness, b.choppiness, t);
            crestSharpen = Vector3.Lerp(a.crestSharpen, b.crestSharpen, t);
            nominalHs = Mathf.Lerp(a.nominalHs, b.nominalHs, t);
            foamThreshold = Mathf.Lerp(a.foamThreshold, b.foamThreshold, t);
            foamHalflife = Mathf.Lerp(a.foamHalflife, b.foamHalflife, t);
            foamInjection = Mathf.Lerp(a.foamInjection, b.foamInjection, t);
            foamInjectSharpness = Mathf.Lerp(a.foamInjectSharpness, b.foamInjectSharpness, t);
        }
    }
}
