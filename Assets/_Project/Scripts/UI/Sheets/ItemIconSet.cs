using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **How an item's PNG icon reaches the player, without a Resources
    /// folder inside `Art/`.** The 256x256 placeholders Kevin approved
    /// (2026-09-26) live at `Assets/_Project/Art/UI/Icons/Items/<id>.png`,
    /// next to the rest of the game's art rather than under a `Resources`
    /// tree of their own -- so this one small asset (itself under
    /// `Resources/UI`, the same place `SheetPanel`/`Sheets.uss` already
    /// live) carries direct, serialized references to them. A serialized
    /// reference is never stripped from a build, exactly like a Resources
    /// folder, and it means the art folder does not have to become a
    /// Resources folder to be reachable at runtime.
    ///
    /// `id` is the `Res.*` constant (`"Timber"`, `"Boards"`, ...) for an
    /// item this camp can actually hold, or the bare icon file name
    /// (`"armor"`, `"cannon"`) for a category-tab glyph that has no
    /// resource behind it yet.
    public class ItemIconSet : ScriptableObject
    {
        [SerializeField] string[] ids;
        [SerializeField] Texture2D[] icons;

        Dictionary<string, Texture2D> byId;

        void Build()
        {
            byId = new Dictionary<string, Texture2D>();
            if (ids == null || icons == null) return;
            int n = Mathf.Min(ids.Length, icons.Length);
            for (int i = 0; i < n; i++)
                if (!string.IsNullOrEmpty(ids[i]) && icons[i] != null) byId[ids[i]] = icons[i];
        }

        public Texture2D Find(string id)
        {
            if (byId == null) Build();
            return id != null && byId.TryGetValue(id, out var t) ? t : null;
        }

        static ItemIconSet instance;
        static bool loaded;

        /// The icon for a resource id or tab glyph name, or null if the set
        /// is missing or has nothing under that key -- callers draw nothing
        /// rather than a missing-texture square.
        public static Texture2D Get(string id)
        {
            if (!loaded)
            {
                loaded = true;
                instance = Resources.Load<ItemIconSet>("UI/ItemIcons");
                if (instance == null)
                    Debug.LogWarning("[Stores] Resources/UI/ItemIcons.asset is missing — item tiles will draw blank.");
            }
            return instance != null ? instance.Find(id) : null;
        }
    }
}
