using System;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **"Add to this slot" -- the bottom drawer.** (Mockup frame 2.)
    ///
    /// Covers the screen it is added to (absolute, full size): a dim that
    /// closes it on tap, and a sheet from the bottom naming the exact slot,
    /// with a two-wide grid of module cards. Each card says what the module
    /// does and what you have: none -> "Build + place · free" (primary),
    /// some in store -> "Place", a one-per-ship module already fitted ->
    /// "Move here", not allowed here -> the reason and a dead "—", locked by
    /// the dry dock -> dashed "Locked".
    ///
    /// `onPick(moduleId)` for any live button, `onClose` for the X / dim.
    public sealed class ModuleDrawerView : VisualElement
    {
        public event Action<string> onPick;
        public event Action onClose;

        readonly Label title, sub, foot;
        readonly VisualElement grid;

        public ModuleDrawerView()
        {
            AddToClassList("ys-drawer-layer");
            var dim = new VisualElement(); dim.AddToClassList("ys-dim");
            dim.RegisterCallback<ClickEvent>(_ => onClose?.Invoke());
            Add(dim);

            var sheet = new VisualElement(); sheet.AddToClassList("ys-drawer");
            var grab = new VisualElement(); grab.AddToClassList("ys-grab"); sheet.Add(grab);

            var head = new VisualElement(); head.AddToClassList("ys-dh");
            var words = new VisualElement(); words.AddToClassList("ys-dh-words");
            title = new Label(); title.AddToClassList("ys-dh-title");
            sub = new Label(); sub.AddToClassList("ys-dh-sub");
            words.Add(title); words.Add(sub); head.Add(words);
            var x = new Button(() => onClose?.Invoke()); x.AddToClassList("ys-square");
            x.Add(new YardGlyph("close", 20f));
            head.Add(x);
            sheet.Add(head);

            grid = new VisualElement(); grid.AddToClassList("ys-mods");
            sheet.Add(grid);
            foot = new Label(); foot.AddToClassList("ys-hint");
            sheet.Add(foot);
            Add(sheet);
            style.display = DisplayStyle.None;
        }

        public void Bind(YardDrawerVm vm)
        {
            style.display = vm.open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!vm.open) return;
            title.text = vm.title ?? "Add to this slot";
            sub.text = vm.sub ?? "";
            foot.text = vm.footnote ?? "";
            foot.style.display = string.IsNullOrEmpty(vm.footnote) ? DisplayStyle.None : DisplayStyle.Flex;
            grid.Clear();
            var items = vm.items ?? Array.Empty<YardDrawerItemVm>();
            for (int i = 0; i < items.Length; i++) grid.Add(Card(items[i]));
        }

        VisualElement Card(YardDrawerItemVm it)
        {
            var card = new VisualElement(); card.AddToClassList("ys-mod");
            if (it.highlighted) card.AddToClassList("ys-mod--on");
            if (it.lockedByDock) card.AddToClassList("ys-mod--lock");
            else if (!it.canPlace) card.AddToClassList("ys-mod--no");

            var mh = new VisualElement(); mh.AddToClassList("ys-mh");
            var mi = new VisualElement(); mi.AddToClassList("ys-mi");
            var glyph = new YardGlyph(it.iconKey, 26f);
            if (it.lockedByDock) glyph.Alpha = 0.35f;
            mi.Add(glyph); mh.Add(mi);
            var words = new VisualElement(); words.AddToClassList("ys-face-words");
            var n = new Label(it.label ?? ""); n.AddToClassList("ys-mn");
            var s = new Label(it.stats ?? ""); s.AddToClassList("ys-ms");
            words.Add(n); words.Add(s); mh.Add(words);
            card.Add(mh);

            string have; bool zero; string btn; bool primary = false, live = true;
            if (it.lockedByDock) { have = it.reason ?? "Locked"; zero = true; btn = "Locked"; live = false; }
            else if (!it.canPlace) { have = it.reason ?? "Not here"; zero = true; btn = "—"; live = false; }
            else if (!string.IsNullOrEmpty(it.aboardAt)) { have = $"aboard ({it.aboardAt})"; zero = false; btn = "Move here"; }
            else if (it.inStore <= 0) { have = "You have 0"; zero = true; btn = it.buildLabel ?? "Build + place · free"; primary = true; }
            else { have = $"{it.inStore} in store"; zero = false; btn = "Place"; }

            var h = new Label(have); h.AddToClassList("ys-have"); if (zero) h.AddToClassList("ys-have--zero");
            card.Add(h);
            string id = it.moduleId;
            var b = new Button(() => { if (live) onPick?.Invoke(id); }) { text = btn };
            b.AddToClassList("ys-mb");
            if (primary) b.AddToClassList("ys-mb--pri");
            b.SetEnabled(live);
            card.Add(b);
            return card;
        }
    }
}
