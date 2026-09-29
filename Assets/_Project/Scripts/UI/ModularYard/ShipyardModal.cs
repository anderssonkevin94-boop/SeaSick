using System;
using SeaSick.Ship.Modular;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    // Explicitly opened by the game integration; no automatic scene/bootstrap hook.
    public sealed class ShipyardModal : MonoBehaviour
    {
        static ShipyardModal active;
        public static bool IsOpen => active != null;
        /// DEV FALLBACK: true = the dry dock opens the pre-slot refit screen
        /// (ShipyardScreen + 3D ShipyardPreview) instead of the slot yard.
        /// Off by default; flip it from `unity cmd eval` if the slot yard
        /// misbehaves on a playtest.
        public static bool UseLegacyScreen;
        PanelSettings settings;
        ShipyardScreen screen;
        ShipyardSlotsScreen slots;
        UIDocument document;
        Action<bool> setWorldInputBlocked;

        public static ShipyardModal Open(IShipyardRefit backend, Action<bool> setWorldInputBlocked,
            Func<ShipConfiguration, int, string> removalBlocker = null)
        {
            if (active != null) return active;
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (setWorldInputBlocked == null) throw new ArgumentNullException(nameof(setWorldInputBlocked));
            var template = Resources.Load<PanelSettings>("UI/SheetPanel");
            if (template == null) throw new InvalidOperationException("UI/SheetPanel is missing.");
            var library = ModuleLibrary.LoadFromResources();
            var live = backend as ShipyardLiveBridge;
            var draft = new ShipyardDraft(library, backend.ReadCurrent(), backend, removalBlocker,
                live == null ? null : live.Allowed, live == null ? null : (Action<ShipConfiguration, int, int>)live.RenumberLayouts);
            var go = new GameObject("Modular shipyard");
            var modal = go.AddComponent<ShipyardModal>();
            try
            {
                active = modal;
                modal.setWorldInputBlocked = setWorldInputBlocked;
                setWorldInputBlocked(true);
                modal.settings = Instantiate(template);
                modal.settings.sortingOrder = 1000;
                modal.settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                modal.settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                modal.settings.match = 0;
                modal.document = go.AddComponent<UIDocument>();
                modal.document.panelSettings = modal.settings;
                modal.screen = new ShipyardScreen(draft, () => Destroy(go), live);
                modal.document.rootVisualElement.Add(modal.screen);
                modal.Update(); return modal;
            }
            catch { DestroyImmediate(go); throw; }
        }

        /// **The slot yard** (phase 2+3): one draft from the live ship
        /// (`ShipyardService.BeginSlotDraft`), the `ShipyardSlotsScreen` over
        /// it, world input blocked while open. The live ship is not hidden:
        /// there is no 3D preview to stage, and the screen is opaque.
        public static ShipyardModal OpenSlots(ShipyardService service, Action<bool> setWorldInputBlocked)
        {
            if (active != null) return active;
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (setWorldInputBlocked == null) throw new ArgumentNullException(nameof(setWorldInputBlocked));
            var template = Resources.Load<PanelSettings>("UI/SheetPanel");
            if (template == null) throw new InvalidOperationException("UI/SheetPanel is missing.");
            if (CoasterFamily.Is(service.Current)) { var bridge=new ShipyardLiveBridge(); return Open(bridge,setWorldInputBlocked,bridge.RemovalBlocker); }
            var draft = service.BeginSlotDraft();
            var go = new GameObject("Slot shipyard");
            var modal = go.AddComponent<ShipyardModal>();
            try
            {
                active = modal;
                modal.setWorldInputBlocked = setWorldInputBlocked;
                setWorldInputBlocked(true);
                modal.settings = Instantiate(template);
                modal.settings.sortingOrder = 1000;
                modal.settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                modal.settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                modal.settings.referenceResolution = new Vector2Int(390, 844);
                modal.document = go.AddComponent<UIDocument>();
                modal.document.panelSettings = modal.settings;
                modal.slots = new ShipyardSlotsScreen(draft, service.Library, () => Destroy(go),
                    () => ShipyardService.Player != null ? ShipyardService.Player.BeginSlotDraft() : null);
                modal.document.rootVisualElement.Add(modal.slots);
                modal.Update(); return modal;
            }
            catch { DestroyImmediate(go); throw; }
        }

        void Update()
        {
            if (document == null || settings == null) return;
            if (slots != null) { UpdateSlots(); return; }
            bool wide = Screen.width > Screen.height * 1.15f;
            settings.referenceResolution = wide ? new Vector2Int(844,390) : new Vector2Int(390,844);
            var root = document.rootVisualElement;
            float scale = settings.referenceResolution.x / (float)Mathf.Max(1, Screen.width);
            var safe = Screen.safeArea;
            root.style.paddingLeft = safe.xMin * scale;
            root.style.paddingRight = (Screen.width-safe.xMax) * scale;
            root.style.paddingTop = (Screen.height-safe.yMax) * scale;
            root.style.paddingBottom = safe.yMin * scale;
            root.style.backgroundColor = (Color)new Color32(22,43,57,255);
        }

        /// Same recipe as `ShipyardSlotsPreview`: phone = 390 units across;
        /// desktop = 844 units tall, the 440-wide column centred.
        void UpdateSlots()
        {
            settings.match = Screen.width > Screen.height ? 1f : 0f;
            var root = document.rootVisualElement;
            float scale = settings.match > 0.5f ? 844f / Mathf.Max(1, Screen.height) : 390f / Mathf.Max(1, Screen.width);
            var safe = Screen.safeArea;
            root.style.paddingLeft = safe.xMin * scale;
            root.style.paddingRight = (Screen.width - safe.xMax) * scale;
            root.style.paddingTop = (Screen.height - safe.yMax) * scale;
            root.style.paddingBottom = safe.yMin * scale;
            root.style.backgroundColor = YardPalette.Page;
        }

        void OnDisable()
        {
            screen?.Dispose(); screen = null;
            slots?.Dispose(); slots = null;
            if (document != null) document.rootVisualElement?.Clear();
            if (active == this) active = null;
            setWorldInputBlocked?.Invoke(false); setWorldInputBlocked = null;
        }
        void OnDestroy() { if (settings != null) Destroy(settings); }
    }
}
