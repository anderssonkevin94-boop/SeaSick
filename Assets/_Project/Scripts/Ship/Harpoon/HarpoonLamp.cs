using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **The gun lamp: the ship's bow light** (Kevin 2026-10-04: a hooded
    /// bullseye lantern bolted to the side of the harpoon gun, shining
    /// forward like a flashlight). Replaces the hull kit's post lantern and
    /// the old nose-block lantern, so the bow carries exactly one light.
    ///
    /// Contract with the mount art, all under `Swivel` (so the beam follows
    /// the gun's aim, dead ahead at rest):
    /// - `Lamp_Light`: an empty at the lens centre facing +Z. Missing: one is
    ///   made beside the muzzle, slightly behind it.
    /// - `Lamp_Lens`: the lens mesh, which glows with the light. Optional.
    /// - `Lamp`: the casing; plain art, nothing here touches it.
    ///
    /// On/off is the other ship lanterns' own signal (`CoasterLantern`):
    /// `SkyDirector.Night01` swings the light from a faint day glimmer to
    /// full. The lens follows it through a property block, so a shared
    /// material is never edited.
    public sealed class HarpoonLamp : MonoBehaviour
    {
        public const string LightName = "Lamp_Light";
        public const string LensName = "Lamp_Lens";

        /// The bow lantern's warm light colour (CoasterOutfitting's lantern loop).
        public static readonly Color WarmColor = new Color(1f, 0.55f, 0.23f);
        /// The lens colour at full glow: the hull lanterns' glass (CoasterOutfitting.Glass).
        static readonly Color LensGlow = new Color(1.8f, 0.85f, 0.35f, 1f);

        /// m to the side, and m behind the muzzle, of a lamp the art did not place.
        const float FallbackSide = 0.25f;
        const float FallbackBack = 0.2f;
        /// s between looks for a SkyDirector when there is none.
        const float SkyRetrySeconds = 2f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int AmbientId = Shader.PropertyToID("_Ambient");

        static Material lensMaterial;

        Light spot;
        Renderer lens;
        MaterialPropertyBlock block;
        SeaSick.World.SkyDirector sky;
        float skyRetry;

        /// Fits the lamp to a freshly built mount (art or placeholder).
        public static HarpoonLamp Attach(GameObject mount)
        {
            var swivel = HarpoonMount.Find(mount.transform, "Swivel") ?? mount.transform;
            var lightT = HarpoonMount.Find(mount.transform, LightName);
            if (lightT == null)
            {
                var muzzle = HarpoonMount.Find(mount.transform, "Barb_Muzzle");
                Vector3 at = muzzle != null
                    ? swivel.InverseTransformPoint(muzzle.position) + new Vector3(FallbackSide, 0f, -FallbackBack)
                    : new Vector3(FallbackSide, 0.2f, 0.5f);
                lightT = new GameObject(LightName).transform;
                lightT.SetParent(swivel, false);
                lightT.localPosition = at;
                lightT.localRotation = Quaternion.identity;
            }

            var lamp = mount.AddComponent<HarpoonLamp>();
            lamp.spot = lightT.GetComponent<Light>();
            if (lamp.spot == null) lamp.spot = lightT.gameObject.AddComponent<Light>();
            lamp.spot.type = LightType.Spot;
            lamp.spot.color = WarmColor;
            lamp.spot.shadows = LightShadows.None;
            lamp.spot.cullingMask = ~0;
            var lensT = HarpoonMount.Find(mount.transform, LensName);
            lamp.lens = lensT != null ? lensT.GetComponent<Renderer>() : null;
            if (lamp.lens != null && !Drivable(lamp.lens.sharedMaterial)) lamp.lens.sharedMaterial = LensMaterial;
            lamp.block = new MaterialPropertyBlock();
            lamp.Apply(0f);
            return lamp;
        }

        /// The flat warm material for a lens whose own material has no glow knob.
        public static Material LensMaterial
        {
            get
            {
                if (lensMaterial == null)
                {
                    lensMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Harpoon lamp lens" };
                    lensMaterial.SetColor(BaseColorId, LensGlow);
                }
                return lensMaterial;
            }
        }

        /// A material whose glow the block can drive: the toon shaders' `_Ambient`
        /// (their only self-light knob) or a plain `_EmissionColor`; or an unlit `_BaseColor`.
        static bool Drivable(Material m) =>
            m != null && (m.HasProperty(AmbientId) || m.HasProperty(EmissionColorId) || m.shader.name.Contains("Unlit"));

        void Update()
        {
            if (spot == null) return;
            if (sky == null && (skyRetry -= Time.deltaTime) <= 0f)
            {
                sky = FindFirstObjectByType<SeaSick.World.SkyDirector>();
                skyRetry = SkyRetrySeconds;
            }
            Apply(sky != null ? sky.Night01 : 0f);
        }

        void Apply(float night)
        {
            spot.spotAngle = HarpoonTuning.lampSpotAngleDeg;
            spot.innerSpotAngle = Mathf.Min(HarpoonTuning.lampInnerAngleDeg, HarpoonTuning.lampSpotAngleDeg);
            spot.range = HarpoonTuning.lampRange;
            spot.intensity = Mathf.Lerp(HarpoonTuning.lampDayIntensity, HarpoonTuning.lampNightIntensity, night);

            if (lens == null) return;
            float glow = Mathf.Lerp(HarpoonTuning.lampLensDayGlow, 1f, night);
            var m = lens.sharedMaterial;
            lens.GetPropertyBlock(block);
            if (m.HasProperty(AmbientId)) block.SetFloat(AmbientId, glow);
            else if (m.HasProperty(EmissionColorId)) block.SetColor(EmissionColorId, LensGlow * glow);
            else block.SetColor(BaseColorId, LensGlow * glow);
            lens.SetPropertyBlock(block);
        }
    }
}
