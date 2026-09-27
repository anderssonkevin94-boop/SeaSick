using System;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The pinned bottom row: [Cancel] [Confirm].** (Mockup frames 1, 4, 5.)
    /// Cancel is optional (null label hides it -- frame 1 has one wide
    /// button); a disabled Confirm reads as the dim "Fix 1 problem to
    /// confirm" bar. Built once, re-texted by `Bind`.
    public sealed class ConfirmRowView : VisualElement
    {
        public event Action onCancel;
        public event Action onConfirm;
        readonly Button cancel, confirm;

        public ConfirmRowView()
        {
            AddToClassList("ys-btns");
            cancel = new Button(() => onCancel?.Invoke()); cancel.AddToClassList("ys-btn2");
            confirm = new Button(() => onConfirm?.Invoke()); confirm.AddToClassList("ys-cta");
            Add(cancel); Add(confirm);
        }

        public void Bind(YardActionsVm a)
        {
            cancel.text = a.cancelLabel ?? "";
            cancel.style.display = string.IsNullOrEmpty(a.cancelLabel) ? DisplayStyle.None : DisplayStyle.Flex;
            confirm.text = a.confirmLabel ?? "Confirm";
            confirm.SetEnabled(a.confirmEnabled);
            confirm.EnableInClassList("ys-cta--off", !a.confirmEnabled);
            confirm.EnableInClassList("ys-cta--moss", a.confirmEnabled && !string.IsNullOrEmpty(a.cancelLabel));
        }
    }
}
