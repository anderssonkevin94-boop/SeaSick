using SeaSick.Ship.Modular;
using SeaSick.UI.ModularYard;
using UnityEditor;
using UnityEngine;

namespace SeaSick.UI.ModularYard.EditorTools
{
    public sealed class ShipyardPreviewWindow : EditorWindow
    {
        ShipyardScreen screen;
        [MenuItem("SeaSick/Modular/Shipyard UI Preview")]
        static void Open()
        {
            var window = GetWindow<ShipyardPreviewWindow>();
            window.titleContent = new GUIContent("Shipyard"); window.minSize = new Vector2(320, 568);
        }
        public void CreateGUI()
        {
            screen?.Dispose(); rootVisualElement.Clear();
            var library = ModuleLibrary.LoadFromResources();
            if (!library.Ok) { rootVisualElement.Add(new UnityEngine.UIElements.Label(string.Join("\n", library.errors))); return; }
            screen = new ShipyardScreen(new ShipyardDraft(library, ShipConfiguration.Long()), Close);
            rootVisualElement.Add(screen);
        }
        void OnDisable() { screen?.Dispose(); screen = null; }
    }
}
