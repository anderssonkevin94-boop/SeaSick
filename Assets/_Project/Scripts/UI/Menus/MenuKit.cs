using System;
using SeaSick.UI.Sheets;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Menus
{
    /// **The parts a menu screen is made of**, the way `SheetKit` is for a
    /// sheet. Every screen (Home, Load, Pause) is a `.menu-card` centred over
    /// a scrim; this builds that shell and the small dialog/toast overlays
    /// every screen ends up needing, so none of them re-invent a confirm box.
    internal static class MenuKit
    {
        public static VisualElement Root()
        {
            var root = new VisualElement();
            root.AddToClassList("menu-root");
            var scrim = new VisualElement();
            scrim.AddToClassList("menu-scrim");
            root.Add(scrim);
            return root;
        }

        public static VisualElement Card(bool wide = false)
        {
            var card = new VisualElement();
            card.AddToClassList("menu-card");
            if (wide) card.AddToClassList("menu-card--wide");
            return card;
        }

        public static Label Title(string s)
        {
            var l = new Label(s ?? "");
            l.AddToClassList("menu-title");
            return l;
        }

        public static Label Subtitle(string s)
        {
            var l = new Label(s ?? "");
            l.AddToClassList("menu-subtitle");
            return l;
        }

        /// A full-width decision button, sized for a thumb.
        public static Button Btn(string text, Action onClick, bool primary = false, bool enabled = true)
        {
            var b = SheetKit.Btn(text, onClick, primary);
            b.AddToClassList("menu-btn");
            b.SetEnabled(enabled);
            return b;
        }

        public static Button Back(Action onClick)
        {
            var b = new Button(() => onClick?.Invoke()) { text = "‹" };
            b.AddToClassList(SheetTheme.Close);
            b.AddToClassList("menu-back");
            return b;
        }

        // --------------------------------------------------------------
        // Confirm / prompt / toast -- pushed onto the SAME document, above
        // whatever screen is live, so a dialog never needs its own modal.
        // --------------------------------------------------------------

        /// A yes/no box. `onOk` runs and the dialog closes either way.
        public static VisualElement Confirm(string body, string okText, Action onOk,
                                            Action onCancel, VisualElement parent)
        {
            VisualElement overlay = null;
            void Close() { if (overlay != null && overlay.parent != null) overlay.parent.Remove(overlay); }

            overlay = new VisualElement();
            overlay.AddToClassList("menu-dialog-root");
            var box = new VisualElement();
            box.AddToClassList("menu-dialog");
            overlay.Add(box);

            var b = new Label(body ?? ""); b.AddToClassList("menu-dialog-body");
            box.Add(b);

            var actions = new VisualElement(); actions.AddToClassList("menu-dialog-actions");
            var cancel = SheetKit.Btn("Cancel", () => { Close(); onCancel?.Invoke(); });
            var ok = SheetKit.Btn(okText ?? "OK", () => { Close(); onOk?.Invoke(); }, primary: true);
            actions.Add(cancel);
            actions.Add(ok);
            box.Add(actions);

            parent.Add(overlay);
            return overlay;
        }

        /// A single-line text prompt, pre-filled with `initial`. `onOk` gets
        /// the trimmed text; empty input is treated as Cancel.
        public static VisualElement Prompt(string body, string initial, string okText,
                                           Action<string> onOk, Action onCancel, VisualElement parent)
        {
            VisualElement overlay = null;
            void Close() { if (overlay != null && overlay.parent != null) overlay.parent.Remove(overlay); }

            overlay = new VisualElement();
            overlay.AddToClassList("menu-dialog-root");
            var box = new VisualElement();
            box.AddToClassList("menu-dialog");
            overlay.Add(box);

            var b = new Label(body ?? ""); b.AddToClassList("menu-dialog-body");
            box.Add(b);

            var field = new TextField { value = initial ?? "" };
            field.AddToClassList("menu-dialog-field");
            box.Add(field);

            var actions = new VisualElement(); actions.AddToClassList("menu-dialog-actions");
            var cancel = SheetKit.Btn("Cancel", () => { Close(); onCancel?.Invoke(); });
            var ok = SheetKit.Btn(okText ?? "OK", () =>
            {
                string text = (field.value ?? "").Trim();
                Close();
                if (string.IsNullOrEmpty(text)) onCancel?.Invoke();
                else onOk?.Invoke(text);
            }, primary: true);
            actions.Add(cancel);
            actions.Add(ok);
            box.Add(actions);

            parent.Add(overlay);
            field.Focus();
            return overlay;
        }

        /// A line that says what just happened and fades from the layout on
        /// its own -- "Saved", "could not save: <reason>".
        public static void Toast(VisualElement parent, string text, float seconds = 1.6f)
        {
            var wrap = new VisualElement(); wrap.AddToClassList("menu-toast");
            var body = new Label(text ?? ""); body.AddToClassList("menu-toast-body");
            wrap.Add(body);
            parent.Add(wrap);
            wrap.schedule.Execute(() => { if (wrap.parent != null) wrap.parent.Remove(wrap); })
                .ExecuteLater((long)(seconds * 1000f));
        }
    }
}
