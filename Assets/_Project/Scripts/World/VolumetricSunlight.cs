using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SeaSick.World
{
    // A local, depth-clipped scattering volume. Owned by SkyDirector; never saved into Sea.unity.
    public sealed class VolumetricSunlight : MonoBehaviour
    {
        public bool PreviewEnabled { get; set; } = true;
        Material material;
        GameObject volume;
        SunVolumeSettings settings;
        void OnEnable()
        {
            settings = Resources.Load<SunVolumeSettings>("SunVolumeSettings");
            var template = Resources.Load<Material>("SunVolume");
            if (!settings || !template) return;
            material = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
            volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = "Volumetric sunlight (runtime)";
            volume.hideFlags = HideFlags.HideAndDontSave;
            Destroy(volume.GetComponent<Collider>());
            var renderer = volume.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            volume.SetActive(false);
            RenderPipelineManager.beginCameraRendering += Prepare;
        }
        void Prepare(ScriptableRenderContext context, Camera camera)
        {
            var sky = SkyDirector.Instance;
            var quality = Application.isMobilePlatform ? settings.mobileQuality : settings.desktopQuality;
            bool visible = PreviewEnabled && quality != SunVolumeSettings.Quality.Off && sky &&
                camera.cameraType == CameraType.Game && sky.SunDirection.y > .015f;
            volume.SetActive(visible);
            if (!visible) return;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            float range = Mathf.Min(settings.distance, camera.farClipPlane * .4f);
            volume.transform.position = camera.transform.position;
            volume.transform.localScale = Vector3.one * (range * 2);
            material.SetFloat("_Range", range);
            material.SetFloat("_Samples", quality == SunVolumeSettings.Quality.Low ? 16 : quality == SunVolumeSettings.Quality.Medium ? 32 : 48);
            float lowSun = 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.6f,sky.SunDirection.y));
            float fade = Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.10f,sky.SunDirection.y)) * (1-sky.Storminess01*.85f);
            material.SetFloat("_Density",settings.density * Mathf.Lerp(.3f,1,lowSun)*fade);
            material.SetFloat("_Strength", settings.strength);
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Prepare;
            if (volume) Destroy(volume);
            if (material) Destroy(material);
        }
    }
}
