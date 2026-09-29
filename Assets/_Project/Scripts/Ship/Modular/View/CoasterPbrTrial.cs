using UnityEngine;
namespace SeaSick.Ship.Modular
{
    // Pilot limited to the raised stern with an exposed front. Other configurations
    // retain their approved geometry/materials until their own atlases are authored.
    public static class CoasterPbrTrial
    {
        public static bool Enabled = true;
        public static bool Apply(Transform host)
        {
            if (!Enabled) return false;
            var kit = Resources.Load<GameObject>("ShipModules/PbrTrial/SternAtlas");
            if (kit == null) return false;
            var names = Resources.Load<TextAsset>("ShipModules/PbrTrial/Included");
            if (names == null) return false;
            var source = host.GetComponentInChildren<MeshFilter>();
            if (source == null) return false;
            Transform parent = source.transform.parent;
            foreach (var r in host.GetComponentsInChildren<MeshRenderer>())
                if (names.text.Contains("\"" + r.name + "\"")) r.enabled = false;
            var instance = Object.Instantiate(kit, parent, false);
            instance.name = "Baked stern PBR";
            return true;
        }
    }
}
