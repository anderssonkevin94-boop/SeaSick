using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// Copy the settlement kit's prefabs where the game can load them.
    ///
    /// `SettlementKitV1` lives under `Art/`, which is right for an art drop
    /// and useless at runtime: `Resources.Load` only sees what is under a
    /// folder called `Resources`. Same answer the ships use — the Blender
    /// export writes straight into `Resources/Ships` — except that this kit
    /// was already imported, so the prefabs are copied rather than rebuilt.
    ///
    /// **Copies, not moves.** The originals stay where the art pass put them
    /// and where `SettlementAssetImport` will rebuild them; this is the
    /// runtime's copy. A copied prefab keeps its references by GUID, so the
    /// models and the vertex-palette material are still the ones in `Art/` and
    /// nothing is duplicated but the prefab file itself.
    ///
    /// Idempotent: run it again after the kit changes.
    public static class SettlementToResources
    {
        const string From = "Assets/_Project/Art/SettlementKitV1/Prefabs";
        const string To = "Assets/_Project/Resources/Settlement";

        /// The ones a camp can actually raise. The rest of the kit (the other
        /// campfire stages, hut_02/03, farm_02/03) is not copied until
        /// something offers them -- an unused prefab in Resources is shipped
        /// weight in every build.
        static readonly string[] Wanted =
        {
            "campfire_02", "storage", "hut_01", "farm_01",
            "sawmill", "kitchen", "blacksmith",
        };

        [MenuItem("SeaSick/Art/Copy settlement kit into Resources")]
        public static void Execute()
        {
            if (!AssetDatabase.IsValidFolder(To))
            {
                Directory.CreateDirectory(To);
                AssetDatabase.Refresh();
            }

            int copied = 0, missing = 0;
            foreach (var id in Wanted)
            {
                string src = $"{From}/{id}.prefab";
                string dst = $"{To}/{id}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null)
                {
                    Debug.LogWarning($"[Settlement] no prefab at {src}");
                    missing++;
                    continue;
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null)
                    AssetDatabase.DeleteAsset(dst);
                if (AssetDatabase.CopyAsset(src, dst)) copied++;
                else Debug.LogWarning($"[Settlement] could not copy {src}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Report what the GAME will find, not what this method did: the
            // question worth answering is whether `Resources.Load` works, and
            // a successful copy into the wrong folder would answer the other
            // one.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Settlement kit -> Resources: {copied} copied, {missing} missing");
            foreach (var id in Wanted)
            {
                var go = Resources.Load<GameObject>("Settlement/" + id);
                sb.AppendLine(go != null
                    ? $"  [ok  ] Settlement/{id}   loads, {Renderers(go)} renderers"
                    : $"  [FAIL] Settlement/{id}   Resources.Load returned null");
            }
            Debug.Log(sb.ToString());
        }

        static int Renderers(GameObject go) =>
            go.GetComponentsInChildren<MeshRenderer>(true).Length;
    }
}
