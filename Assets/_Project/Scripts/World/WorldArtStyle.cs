using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// Scene-owned art treatment for runtime-built ships and scenery. Keeps
    /// the lab isolated and shares material instances across the whole fleet.
    [DefaultExecutionOrder(-1000)]
    public class WorldArtStyle : MonoBehaviour
    {
        public static WorldArtStyle Instance { get; private set; }
        [SerializeField] Texture2D shipPalette;
        [SerializeField] Material sceneryMaterial;
        [SerializeField] string sceneryResource = "Flora/graphic_flora";
        readonly Dictionary<Material, Material> fleetMaterials = new Dictionary<Material, Material>();

        public static string SceneryResource => Instance != null ? Instance.sceneryResource : "Flora/seasick_flora";
        public static Material SceneryOverride => Instance != null ? Instance.sceneryMaterial : null;
        public static string CacheSuffix => Instance != null ? "_Art" + Instance.GetInstanceID() : "";

        void OnEnable() { Instance = this; }
        void OnDestroy()
        {
            BuildingFactory.ReleaseArtMaterials("_Art" + GetInstanceID());
            if (Instance == this) Instance = null;
            foreach (var m in fleetMaterials.Values) if (m != null) Destroy(m);
            fleetMaterials.Clear();
        }

        public static Color BuildingColour(string role, Color fallback)
        {
            if (Instance == null) return fallback;
            switch (role)
            {
                case "wall": return new Color(0.64f, 0.44f, 0.25f);
                case "beam": return new Color(0.27f, 0.25f, 0.23f);
                case "thatch": return new Color(0.75f, 0.39f, 0.20f);
                case "footing": return new Color(0.55f, 0.58f, 0.59f);
                default: return fallback;
            }
        }

        public static void ApplyFleet(Transform root)
        {
            if (Instance == null || Instance.shipPalette == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || !source.HasProperty("_BaseMap")) continue;
                    var texture = source.GetTexture("_BaseMap");
                    if (texture == Instance.shipPalette || texture == null || texture.name != "seasick_palette") continue;
                    if (!Instance.fleetMaterials.TryGetValue(source, out var copy))
                    {
                        copy = new Material(source) { name = source.name + "_Graphic" };
                        copy.SetTexture("_BaseMap", Instance.shipPalette);
                        if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", Instance.shipPalette);
                        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", 0.08f);
                        Instance.fleetMaterials.Add(source, copy);
                    }
                    materials[i] = copy;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
    }
}
