using System;
using SeaSick.Ship;
using SeaSick.Ship.Harpoon;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// Sailing chrome has its own layout; island and dock sheets keep theirs.
    /// Every published hit rect is computed before layout, in GUI pixels.
    public sealed class QuietSailingHud
    {
        public static bool Active { get; private set; }
        public static bool MapOpen { get; private set; }
        public static bool WeaponsOpen { get; private set; }
        public static Rect SteeringRect { get; private set; }
        public Rect MenuRect, CompassRect, CargoRect, BoostRect, HarpoonRect, AlertRect;
        public Rect HarpoonDrawnRect
        {
            get { var r=harpoon.worldBound; float s=Mathf.Max(.0001f,SheetHost.PanelScale);
                return new Rect(r.x/s,r.y/s,r.width/s,r.height/s); }
        }
        Rect weaponsRect, portRect, starboardRect, actionRect, mapRect;
        static QuietSailingHud current;
        readonly VisualElement layer;
        readonly Button menu, compass, cargo, boost, weapons, port, starboard, action, alert;
        readonly VisualElement harpoon;
        readonly Label harpoonText, cargoText, hint;
        readonly SeaGlyph holdRing, hook;
        readonly PaperSeaMap map;
        readonly SailingCompass compassTape;
        int pointer = -1;
        float pressedAt;
        bool cut, heldLine;
        HarpoonGun pressedGun;
        const float CutSeconds = .75f;
        static readonly Color Ink = new Color32(17, 38, 49, 225);
        static readonly Color Pearl = new Color32(239, 243, 233, 255);
        static readonly Color Gold = new Color32(247, 203, 93, 255);

        public QuietSailingHud(VisualElement root, Action openHold, Action alertTap)
        {
            current = this;
            layer = new VisualElement { name = "quiet-sailing-hud", pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            root.Add(layer);
            menu = Button("", SeaSick.UI.Menus.GameMenus.TogglePause, "Pause / menu");
            menu.Add(Icon(SeaGlyph.Kind.Menu));
            compass = Button("N", () => { MapOpen = !MapOpen; WeaponsOpen = false; }, "Open live sea chart");
            compassTape = new SailingCompass(); compass.Add(compassTape);
            cargo = Button("", openHold, "Cargo and crew");
            cargo.style.flexDirection = FlexDirection.Row;
            var bag = new HudGlyph(HudGlyph.Kind.Backpack); bag.style.width = 22; bag.style.height = 24; cargo.Add(bag);
            cargoText = new Label(); cargoText.style.fontSize = 13; cargoText.style.color = Pearl; cargo.Add(cargoText);
            boost = Button("", () => SheetBits.Motor?.GetComponent<HelmInput>()?.ToggleBoost(), "Boost");
            boost.Add(Icon(SeaGlyph.Kind.Bolt));
            weapons = Button("Guns", () => { WeaponsOpen = !WeaponsOpen; MapOpen = false; }, "Manual cannon controls");
            port = Button("‹ Port", () => Fire(false), "Fire port broadside");
            starboard = Button("Stbd ›", () => Fire(true), "Fire starboard broadside");
            action = Button("", () => SeaActions.Tap(), "Nearby action");
            alert = Button("", alertTap, "Ship needs attention");
            alert.style.color = Gold;
            harpoon = new VisualElement { name = "quiet-harpoon", tooltip = "Tap: fire / reel / stop. Hold: cut line." };
            StyleButton(harpoon); layer.Add(harpoon);
            hook = Icon(SeaGlyph.Kind.Hook); harpoon.Add(hook);
            harpoonText = new Label("Fire"); harpoonText.pickingMode = PickingMode.Ignore;
            harpoonText.style.fontSize = 12; harpoonText.style.color = Pearl; harpoon.Add(harpoonText);
            holdRing = Icon(SeaGlyph.Kind.Ring); holdRing.style.position = Position.Absolute;
            holdRing.style.left = holdRing.style.top = 0; holdRing.style.width = Length.Percent(100); holdRing.style.height = Length.Percent(100);
            holdRing.Tint = Gold; holdRing.style.display = DisplayStyle.None; harpoon.Add(holdRing);
            harpoon.RegisterCallback<PointerDownEvent>(e => {
                if (pointer >= 0 || e.button != 0) return;
                pointer = e.pointerId; pressedAt = Time.unscaledTime; cut = false;
                pressedGun = HarpoonGun.Player; heldLine = pressedGun != null && pressedGun.LineOut;
                harpoon.CapturePointer(pointer); e.StopPropagation();
            });
            harpoon.RegisterCallback<PointerUpEvent>(e => {
                if (e.pointerId != pointer) return;
                bool inside = harpoon.worldBound.Contains(new Vector2(e.position.x, e.position.y));
                if (!cut && inside && (!heldLine || pressedGun != null && pressedGun.LineOut) && pressedGun != null && pressedGun == HarpoonGun.Player && pressedGun.Available)
                { SeaHud.RecordHarpoonTap(); pressedGun.FireOrCut(); }
                CancelPress(); e.StopPropagation();
            });
            harpoon.RegisterCallback<PointerMoveEvent>(e => {
                if (e.pointerId == pointer && !harpoon.worldBound.Contains(new Vector2(e.position.x,e.position.y))) CancelPress();
            });
            harpoon.RegisterCallback<PointerCancelEvent>(_ => CancelPress());
            harpoon.RegisterCallback<PointerCaptureOutEvent>(_ => CancelPress());
            hint = new Label("Drag to sail"); hint.pickingMode = PickingMode.Ignore; hint.style.position = Position.Absolute;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter; hint.style.color = Pearl; hint.style.fontSize = 12; layer.Add(hint);
            map = new PaperSeaMap(() => MapOpen = false); layer.Add(map);
            Hide();
        }
        static SeaGlyph Icon(SeaGlyph.Kind kind)
        {
            var g = new SeaGlyph(kind) { pickingMode = PickingMode.Ignore };
            g.style.width = 25; g.style.height = 25; g.Tint = Pearl; return g;
        }
        Button Button(string text, Action tap, string tooltip)
        {
            var b = new Button(tap) { text = text, tooltip = tooltip }; StyleButton(b); layer.Add(b); return b;
        }
        static void StyleButton(VisualElement v)
        {
            v.style.position = Position.Absolute; v.style.backgroundColor = Ink;
            v.style.borderTopLeftRadius = v.style.borderTopRightRadius = v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = 22;
            v.style.borderLeftWidth = v.style.borderRightWidth = v.style.borderTopWidth = v.style.borderBottomWidth = 0;
            v.style.marginLeft = v.style.marginRight = v.style.marginTop = v.style.marginBottom = 0;
            v.style.paddingLeft = v.style.paddingRight = 6; v.style.paddingTop = v.style.paddingBottom = 0;
            v.style.alignItems = Align.Center; v.style.justifyContent = Justify.Center;
            v.style.color = Pearl; v.style.fontSize = 14;
        }
        static void Fire(bool right)
        {
            var b = CombatHud.Source?.Battery;
            if (b != null && (right ? b.StarboardReady : b.PortReady) > 0) b.FireBroadside(right);
        }
        void CancelPress()
        {
            int old = pointer; pointer = -1; pressedGun = null; heldLine = false;
            holdRing.style.display = DisplayStyle.None;
            if (old >= 0 && harpoon.HasPointerCapture(old)) harpoon.ReleasePointer(old);
        }
        public void Hide()
        {
            CancelPress(); layer.style.display = DisplayStyle.None;
            Active = MapOpen = WeaponsOpen = false; SteeringRect = Rect.zero;
        }
        public void Tick(string warning)
        {
            current = this; Active = true; layer.style.display = DisplayStyle.Flex;
            var safe = Screen.safeArea;
            if (safe.width < 1) safe = new Rect(0, 0, Screen.width, Screen.height);
            float u = Mathf.Min(safe.width / 390f, safe.height / 650f);
            float s = SheetHost.PanelScale;
            float x = safe.xMin, y = Screen.height - safe.yMax, bottom = Screen.height - safe.yMin;
            // Small islands of chrome, never a full-width opaque status strip.
            MenuRect = new Rect(x + 14*u, y + 10*u, 46*u, 46*u);
            CompassRect = new Rect(safe.center.x - 72*u, y + 10*u, 144*u, 46*u);
            CargoRect = new Rect(safe.xMax - 92*u, y + 10*u, 78*u, 46*u);
            BoostRect = new Rect(safe.xMax - 70*u, bottom - 70*u, 56*u, 56*u);
            HarpoonRect = new Rect(safe.xMax - 78*u, bottom - 154*u, 64*u, 72*u);
            weaponsRect = new Rect(x + 14*u, bottom - 70*u, 56*u, 56*u);
            SteeringRect = new Rect(safe.center.x - 92*u, bottom - 170*u, 184*u, 152*u);
            portRect = new Rect(x + 14*u, bottom - 192*u, 86*u, 46*u);
            starboardRect = new Rect(x + 14*u, bottom - 138*u, 86*u, 46*u);
            actionRect = new Rect(safe.center.x - 80*u, bottom - 230*u, 160*u, 44*u);
            AlertRect = string.IsNullOrEmpty(warning) || MapOpen ? Rect.zero
                : new Rect(safe.center.x - 150*u, y + 66*u, 300*u, 32*u);
            Place(menu, MenuRect,s,u); Place(compass, CompassRect,s,u); Place(cargo,CargoRect,s,u);
            Place(boost,BoostRect,s,u); Place(harpoon,HarpoonRect,s,u); Place(weapons,weaponsRect,s,u);
            Place(port,portRect,s,u); Place(starboard,starboardRect,s,u); Place(action,actionRect,s,u);
            Show(port,WeaponsOpen); Show(starboard,WeaponsOpen);
            var battery = CombatHud.Source?.Battery;
            port.SetEnabled(battery != null && battery.PortReady > 0);
            starboard.SetEnabled(battery != null && battery.StarboardReady > 0);
            var offer = SeaActions.Current;
            bool hasAction = SeaActions.HasOffer && !MapOpen;
            Show(action,hasAction); action.text = offer.enabled ? offer.title : offer.detail;
            action.SetEnabled(offer.enabled); SeaActions.Visible = hasAction; SeaActions.Rect = hasAction ? actionRect : Rect.zero;
            Show(alert,AlertRect.width > 0); if (AlertRect.width > 0) { Place(alert,AlertRect,s,u); alert.text = warning; }
            compass.text = "";
            compassTape.Refresh();
            var v = SheetBits.Voyage;
            cargoText.text = v != null ? v.TotalHeld + "/" + v.HoldCapacity : "—";
            cargoText.style.color = v != null && v.Overloaded ? Gold : Pearl;
            var helm = SheetBits.Motor != null ? SheetBits.Motor.GetComponent<HelmInput>() : null;
            boost.style.backgroundColor = helm != null && helm.BoostArmed ? (Color)new Color32(109,92,49,245) : Ink;
            Place(hint, new Rect(SteeringRect.x, SteeringRect.yMax-20*u, SteeringRect.width,20*u),s,u);
            Show(hint, helm != null && GestureHints.ShowOnce(GestureHints.Stick,helm.StickInUse)
                && !GestureHints.Shown(GestureHints.Stick,Time.unscaledDeltaTime));
            var gun = HarpoonGun.Player;
            Show(harpoon,gun != null);
            if (gun != null)
            {
                harpoonText.text = gun.State == HarpoonState.Hooked ? (gun.IsReeling ? "Stop" : "Reel")
                    : gun.State == HarpoonState.Ready ? "Fire" : gun.State == HarpoonState.Reloading ? "Reload" : "…";
                hook.Tint = gun.State == HarpoonState.Hooked
                    ? gun.Band == TensionBand.Strained ? (Color)new Color32(247,135,107,255) : Gold : Pearl;
                if (gun.HoldFull) harpoonText.text = "Full";
                harpoon.style.opacity = gun.Available ? 1f : .45f;
                if (pointer >= 0 && (!gun.Available || gun != pressedGun)) CancelPress();
                if (pointer >= 0 && heldLine && gun.LineOut && !cut)
                {
                    float p = Mathf.Clamp01((Time.unscaledTime-pressedAt)/CutSeconds);
                    holdRing.Progress = p; holdRing.MarkDirtyRepaint(); Show(holdRing,true);
                    if (p >= 1) { gun.CutLine(); cut = true; }
                }
            }
            float side = Mathf.Min(safe.width - 28*u, safe.height - 310*u, 420*u);
            mapRect = new Rect(safe.center.x-side/2,y+72*u,side,side);
            Place(map,mapRect,s,u); Show(map,MapOpen); if (MapOpen) map.Refresh();
        }
        static void Place(VisualElement v, Rect r, float s, float u)
        {
            v.style.left = r.x*s; v.style.top = r.y*s; v.style.width = r.width/u; v.style.height = r.height/u;
            v.style.transformOrigin = new TransformOrigin(0,0);
            v.style.scale = new Scale(new Vector3(u*s,u*s,1));
        }
        static void Show(VisualElement v,bool yes) => v.style.display = yes ? DisplayStyle.Flex : DisplayStyle.None;
        public static bool Blocks(Vector2 p)
        {
            if (!Active || current == null) return false;
            var q = current;
            return q.MenuRect.Contains(p) || q.CompassRect.Contains(p) || q.CargoRect.Contains(p)
                || q.BoostRect.Contains(p) || q.HarpoonRect.Contains(p) || q.weaponsRect.Contains(p)
                || q.AlertRect.Contains(p) || (MapOpen && q.mapRect.Contains(p))
                || (WeaponsOpen && (q.portRect.Contains(p) || q.starboardRect.Contains(p)))
                || (SeaActions.Visible && q.actionRect.Contains(p));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Active = MapOpen = WeaponsOpen = false; SteeringRect = Rect.zero; current = null; }

    }
}
