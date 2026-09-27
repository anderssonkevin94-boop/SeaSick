using System;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The problem above the Confirm button, with its own fix.** (Mockup
    /// frame 1: "10 crew aboard, 8 beds. Add a bunk, or leave 2 ashore." +
    /// [Leave 2].) Ember when it blocks Confirm, amber when it is advice
    /// (frame 5's "Guns up high make her roll"). Shows the first blocker
    /// only -- the phone has room for one line of trouble -- and hides
    /// itself when there is none. `onFix(fixId)`.
    public sealed class BlockerBarView : VisualElement
    {
        public event Action<string> onFix;
        readonly Label text;
        readonly Button fix;
        string fixId;

        public BlockerBarView()
        {
            AddToClassList("ys-warn");
            text = new Label(); text.AddToClassList("ys-warn-text");
            Add(text);
            fix = new Button(() => onFix?.Invoke(fixId)); fix.AddToClassList("ys-fix");
            Add(fix);
            style.display = DisplayStyle.None;
        }

        public void Bind(YardBlockerVm[] blockers)
        {
            bool any = blockers != null && blockers.Length > 0;
            style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any) return;
            var b = blockers[0];
            text.text = b.text ?? "";
            EnableInClassList("ys-warn--amber", b.warnOnly);
            fixId = b.fixId;
            fix.text = b.fixLabel ?? "";
            fix.style.display = string.IsNullOrEmpty(b.fixLabel) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
