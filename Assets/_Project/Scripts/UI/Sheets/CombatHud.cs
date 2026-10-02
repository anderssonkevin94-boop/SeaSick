using SeaSick.Combat;
using SeaSick.Ship;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The sea combat controls, 2026-09-30 (island UI restructure, phase 6;
    /// Kevin approved mockup "8b · Sea: combat").** One red target chip under
    /// the sea top bar and ONE combat row above the order strip replace the
    /// IMGUI fire chips (`CannonBattery.OnGUI`, "◀ port 1/1 ready" / "stbd ▶")
    /// and the round lock button (`CombatLock.OnGUI`, `HudLayout.Slot.Lock`).
    /// <list type="bullet">
    /// <item>**Chip** -- "RAIDER · 120 m · locked" (or "RAIDER · 140 m" with a
    ///   candidate only). Shown while a lock or a candidate exists.</item>
    /// <item>**Row** -- [◀ Fire port] [Lock / Release] [Fire stbd ▶], 64 design
    ///   px tall, just above the sea HUD's order strip (`BottomPx`: since
    ///   2026-10-02 the strip is 30 tall on a 6 margin, plus a 14 gap = 50;
    ///   it was a 120 px helm row). A Fire button is bright ice when a gun
    ///   on that side is loaded, dark while it reloads, with the reload bar
    ///   inside. Lock reads "Lock · 120 m" with a candidate, "Release" ember
    ///   while held, amber while the lock is slipping out of range. Nobody
    ///   on a side's guns: that button becomes plain text ("No crew"), and
    ///   with nobody on either side one line reads "No crew on the guns".</item>
    /// <item>**Note** -- one line above the row: "Locked guns fire by
    ///   themselves · 2 archers shooting".</item>
    /// </list>
    ///
    /// **Show rule** (unchanged from the IMGUI chips): only with an enemy to
    /// fight -- `CannonBattery.EnemyInRangeNow` (a held lock, or a hostile
    /// within 1.5x the guns' reach, min 60 m) -- plus, as the old lock button
    /// did, whenever a candidate is inside the lock's own reach, so the lock
    /// is never out of the thumb's way. Hidden ashore, behind a sheet or the
    /// sea drawer, and with a shipyard or menu modal up.
    ///
    /// **Actions are the old ones:** `CannonBattery.FireBroadside`,
    /// `CombatLock.ToggleLock`, auto-fire while locked (`CombatLock` sets
    /// `AutoFireTarget`). Desktop Q / E / Space are untouched. Tapping an
    /// enemy hull to lock it (`CombatLock.WouldLock`) works anywhere outside
    /// the row, because the row registers in `UIBlocker.SheetBlocked` via
    /// `Blocks` (the helm stick and world taps ignore it).
    ///
    /// **Layout is in DESIGN px**, the 390-wide mockup, scaled by one
    /// `style.scale` on each root so it reads the same whatever reference
    /// resolution the panel is on (1280x720 at sea today, 430x932 ashore):
    /// upright a design px is `safe.width / 390` screen px (the row fills the
    /// safe width less 10 px a side); on a desk it is one panel unit and the
    /// row is centred. Built once, re-texted only when its state key moves,
    /// nothing allocated per frame (the distance strings change at 4 Hz).
    ///
    /// The sea HUD (top bar, order strip, action card) is another file: it sets
    /// `TopPx` / `BottomPx` if its bars are not where the mockup has them and
    /// hides its action card while `Visible`.
    public static class CombatHud
    {
        /// The mockup's width, design px.
        public const float DesignWidth = 390f;
        /// Side margin, design px.
        public const float Side = 10f;
        public const float RowHeight = 64f;
        public const float NoteHeight = 16f;
        public const float NoteGap = 6f;
        /// Design px from the safe area's bottom edge to the row's bottom:
        /// the order strip's 6 margin + 30 height + 14 gap. The sea HUD sets
        /// it every frame from its own constants (`SeaHud.HelmBottom` ...).
        public static float BottomPx = 50f;
        /// Design px from the safe area's top edge to the chip's top: under
        /// the sea top bar (10 margin + 52 bar + 10 gap).
        public static float TopPx = 72f;

        /// True while the combat row (and the chip, with a target) is up.
        public static bool Visible { get; private set; }
        /// The row plus its note, GUI space (origin top-left). Zero hidden.
        /// This is the blocked rect.
        public static Rect Rect { get; private set; }
        /// The Lock / Release button, GUI space. Zero hidden.
        public static Rect LockRect { get; private set; }
        /// The red target chip, GUI space. Zero while it is not showing.
        public static Rect ChipRect { get; private set; }

        /// Is a screen point (GUI space) on the row? `UIBlocker.SheetBlocked`
        /// asks, so the helm stick and world taps ignore it.
        public static bool Blocks(Vector2 guiPoint) => Visible && Rect.Contains(guiPoint);

        /// The player's lock component; it registers itself while enabled.
        internal static CombatLock Source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Visible = false;
            Rect = LockRect = ChipRect = Rect.zero;
            Source = null;
        }

        internal sealed class View
        {
            readonly VisualElement wrap;
            readonly Label chip, note, noCrew, portTitle, stbdTitle, lockTitle, lockSub;
            readonly VisualElement portFill, stbdFill;
            readonly Button portBtn, stbdBtn, lockBtn;
            readonly VisualElement spacer;

            bool shown, chipShown;
            int stateKey = int.MinValue;
            float nextText;
            int lastDist = int.MinValue;
            int portBar = -1, stbdBar = -1;
            IHittable lastTarget;
            float pulseUntil;
            bool pulsing;
            float lastLeft = float.NaN, lastBottom = float.NaN, lastK = float.NaN;
            float lastChipLeft = float.NaN, lastChipTop = float.NaN;

            public View(VisualElement root)
            {
                var style = Resources.Load<StyleSheet>("UI/CombatHud");
                if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);

                wrap = new VisualElement { pickingMode = PickingMode.Ignore };
                wrap.AddToClassList("combat-wrap");

                note = NewLabel("combat-note");
                wrap.Add(note);

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("combat-row");
                wrap.Add(row);

                portBtn = NewFire("◀ Fire port", "combat-fire--port", () => Fire(false),
                    out portTitle, out portFill);
                row.Add(portBtn);

                noCrew = NewLabel("combat-nocrew");
                noCrew.text = "No crew on the guns";
                row.Add(noCrew);

                lockBtn = new Button(ToggleLock);
                lockBtn.AddToClassList("combat-lock");
                lockTitle = NewLabel("combat-lock-title");
                lockSub = NewLabel("combat-lock-sub");
                lockBtn.Add(lockTitle);
                lockBtn.Add(lockSub);
                row.Add(lockBtn);

                stbdBtn = NewFire("Fire stbd ▶", "combat-fire--stbd", () => Fire(true),
                    out stbdTitle, out stbdFill);
                row.Add(stbdBtn);

                spacer = new VisualElement { pickingMode = PickingMode.Ignore };
                spacer.AddToClassList("combat-spacer");
                row.Add(spacer);

                chip = NewLabel("combat-chip");
                root.Add(wrap);
                root.Add(chip);
                Hide();
            }

            static Label NewLabel(string cls)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                return l;
            }

            static Button NewFire(string title, string sideClass, System.Action tap,
                out Label titleLabel, out VisualElement fill)
            {
                var b = new Button(tap);
                b.AddToClassList("combat-fire");
                b.AddToClassList(sideClass);
                titleLabel = NewLabel("combat-fire-title");
                titleLabel.text = title;
                b.Add(titleLabel);
                var track = new VisualElement { pickingMode = PickingMode.Ignore };
                track.AddToClassList("combat-bar");
                fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("combat-bar-fill");
                fill.style.width = Length.Percent(0f);
                track.Add(fill);
                b.Add(track);
                return b;
            }

            // ---- actions: the old ones, unchanged ------------------------

            static void Fire(bool starboardSide)
            {
                var src = Source;
                var b = src != null ? src.Battery : null;
                if (b == null) return;
                // A side with nothing loaded stays silent, as the old
                // disabled IMGUI button did.
                if ((starboardSide ? b.StarboardReady : b.PortReady) > 0) b.FireBroadside(starboardSide);
            }

            static void ToggleLock()
            {
                var src = Source;
                if (src != null) src.ToggleLock();
            }

            public void Hide()
            {
                if (shown || wrap.style.display != DisplayStyle.None)
                {
                    shown = false;
                    wrap.style.display = DisplayStyle.None;
                }
                if (chipShown || chip.style.display != DisplayStyle.None)
                {
                    chipShown = false;
                    chip.style.display = DisplayStyle.None;
                }
                Visible = false;
                Rect = LockRect = ChipRect = Rect.zero;
                stateKey = int.MinValue;
                lastTarget = null;
            }

            /// Once a frame from `SheetHost.LateUpdate`, after the Next card.
            public void Tick(VisualElement root)
            {
                var lk = Source;
                if (lk == null || !lk.isActiveAndEnabled
                    || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked
                    || MidnightLandHud.Active || ThumbBar.PlacementActive
                    || SheetHost.FrameOpen || SeaLedger.IsOpen)
                { Hide(); return; }

                var bat = lk.Battery;
                bool locked = lk.Locked != null;
                IHittable target = locked ? lk.Locked : lk.CurrentCandidate;
                bool guns = bat != null && bat.TotalGuns > 0;
                bool want = target != null || (guns && bat.EnemyInRangeNow);
                if (!want) { Hide(); return; }

                float now = Time.unscaledTime;
                float dist = target != null ? lk.DistanceTo(target) : 0f;
                bool slip = locked && dist >= lk.BreakRange * 0.75f;
                int portMan = guns ? bat.PortManned : 0, stbdMan = guns ? bat.StarboardManned : 0;
                int portReady = guns ? bat.PortReady : 0, stbdReady = guns ? bat.StarboardReady : 0;
                bool portHas = guns && bat.PortCount > 0, stbdHas = guns && bat.StarboardCount > 0;
                int archers = lk.ArchersReady;

                // A new candidate swells the Lock button for 0.4 s, once.
                if (!locked && target != null && !ReferenceEquals(target, lastTarget))
                {
                    pulseUntil = now + 0.4f;
                    if (!pulsing) { pulsing = true; lockBtn.AddToClassList("combat-lock--new"); }
                }
                lastTarget = locked ? null : target;
                if (pulsing && now >= pulseUntil)
                {
                    pulsing = false;
                    lockBtn.RemoveFromClassList("combat-lock--new");
                }

                int key = (locked ? 1 : 0) | (slip ? 2 : 0) | (target != null ? 4 : 0) | (guns ? 8 : 0)
                          | (portMan > 0 ? 16 : 0) | (stbdMan > 0 ? 32 : 0)
                          | (portReady > 0 ? 64 : 0) | (stbdReady > 0 ? 128 : 0)
                          | (portHas ? 256 : 0) | (stbdHas ? 512 : 0)
                          | (Mathf.Min(archers, 99) << 10) | ((target is EnemyShip) ? 1 : (target is SeaMonster) ? 2 : 0) << 17;
                int roundDist = Mathf.RoundToInt(dist);
                bool textDue = key != stateKey || (now >= nextText && roundDist != lastDist);
                if (textDue)
                {
                    nextText = now + 0.25f;
                    lastDist = roundDist;
                    Apply(key, target, locked, slip, roundDist, guns, portHas, stbdHas,
                          portMan, stbdMan, portReady, stbdReady, archers);
                }

                // Reload bars, quantised to 2 %.
                if (guns)
                {
                    int pb = Mathf.RoundToInt(bat.PortLoaded01 * 50f);
                    if (pb != portBar) { portBar = pb; portFill.style.width = Length.Percent(pb * 2f); }
                    int sb = Mathf.RoundToInt(bat.StarboardLoaded01 * 50f);
                    if (sb != stbdBar) { stbdBar = sb; stbdFill.style.width = Length.Percent(sb * 2f); }
                }

                if (!shown) { shown = true; wrap.style.display = DisplayStyle.Flex; }
                bool chipWant = target != null;
                if (chipWant != chipShown)
                {
                    chipShown = chipWant;
                    chip.style.display = chipWant ? DisplayStyle.Flex : DisplayStyle.None;
                }

                Place(root);
            }

            /// Text and classes; runs only when the state moved or the
            /// distance did (at most 4 Hz).
            void Apply(int key, IHittable target, bool locked, bool slip, int dist, bool guns,
                       bool portHas, bool stbdHas, int portMan, int stbdMan, int portReady,
                       int stbdReady, int archers)
            {
                stateKey = key;

                // Lock / Release.
                lockTitle.text = locked ? "Release" : "Lock";
                lockSub.text = target != null ? dist + " m" : "-";
                lockBtn.EnableInClassList("combat-lock--held", locked && !slip);
                lockBtn.EnableInClassList("combat-lock--slip", slip);
                lockBtn.EnableInClassList("combat-lock--none", target == null);

                // Fire buttons: a side with no guns at all is not drawn;
                // nobody on the guns is plain text, not a dead button.
                bool noneManned = guns && portMan == 0 && stbdMan == 0;
                SetFire(portBtn, portHas && !noneManned, portMan > 0, portReady > 0);
                SetFire(stbdBtn, stbdHas && !noneManned, stbdMan > 0, stbdReady > 0);
                SetDisplay(noCrew, noneManned);
                SetDisplay(spacer, noneManned);
                portTitle.text = portMan > 0 ? "◀ Fire port" : "No crew";
                stbdTitle.text = stbdMan > 0 ? "Fire stbd ▶" : "No crew";

                // The chip.
                if (target != null)
                {
                    string kind = (target is EnemyShip) ? "RAIDER" : (target is SeaMonster) ? "SEA BEAST" : "TARGET";
                    chip.text = locked ? kind + " · " + dist + " m · locked" : kind + " · " + dist + " m";
                }

                // The note: what the guns will do, and the bows.
                bool anyCrew = guns && (portMan > 0 || stbdMan > 0);
                string line = !anyCrew ? ""
                    : locked ? "Locked guns fire by themselves"
                    : target != null ? "Lock on and the guns fire by themselves" : "";
                if (archers > 0)
                    line += (line.Length > 0 ? " · " : "") + archers + (archers == 1 ? " archer" : " archers") + " shooting";
                note.text = line;
            }

            static void SetFire(Button b, bool present, bool manned, bool ready)
            {
                SetDisplay(b, present);
                b.EnableInClassList("combat-fire--crewless", !manned);
                b.EnableInClassList("combat-fire--ready", manned && ready);
                // Crewless is text, not a control: taps fall through (the
                // row's rect is blocked either way).
                b.pickingMode = manned ? PickingMode.Position : PickingMode.Ignore;
            }

            static void SetDisplay(VisualElement e, bool on)
            {
                var d = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (e.style.display != d) e.style.display = d;
            }

            /// Position and scale (design px -> panel units), and publish the
            /// screen rects. Styles are written only when a number moved.
            void Place(VisualElement root)
            {
                var safe = Screen.safeArea;
                if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
                float s = Mathf.Max(1e-4f, SheetHost.PanelScale);
                bool wide = HudLayout.Wide;
                // Screen px per design px: upright the row fills the safe
                // width; on a desk a design px is one panel unit.
                float ppd = wide ? 1f / s : safe.width / DesignWidth;
                float k = ppd * s;
                float rowW = (DesignWidth - Side * 2f) * ppd;
                float x = wide ? safe.xMin + (safe.width - rowW) * .5f : safe.xMin + Side * ppd;
                float bottom = safe.yMin + BottomPx * ppd;

                float left = x * s, bot = bottom * s;
                if (!Mathf.Approximately(left, lastLeft)) { lastLeft = left; wrap.style.left = left; }
                if (!Mathf.Approximately(bot, lastBottom)) { lastBottom = bot; wrap.style.bottom = bot; }
                if (!Mathf.Approximately(k, lastK))
                {
                    lastK = k;
                    wrap.style.scale = new Scale(new Vector3(k, k, 1f));
                    chip.style.scale = new Scale(new Vector3(k, k, 1f));
                }

                float h = (NoteHeight + NoteGap + RowHeight) * ppd;
                Rect = new Rect(x, Screen.height - bottom - h, rowW, h);

                var lb = lockBtn.worldBound;
                LockRect = lb.width > 0f && lb.height > 0f
                    ? new Rect(lb.x / s, lb.y / s, lb.width / s, lb.height / s) : Rect.zero;

                if (chipShown)
                {
                    float cl = (safe.xMin + Side * ppd) * s;
                    float ct = (Screen.height - safe.yMax + TopPx * ppd) * s;
                    if (!Mathf.Approximately(cl, lastChipLeft)) { lastChipLeft = cl; chip.style.left = cl; }
                    if (!Mathf.Approximately(ct, lastChipTop)) { lastChipTop = ct; chip.style.top = ct; }
                    var cb = chip.worldBound;
                    ChipRect = cb.width > 0f && cb.height > 0f
                        ? new Rect(cb.x / s, cb.y / s, cb.width / s, cb.height / s) : Rect.zero;
                }
                else ChipRect = Rect.zero;

                Visible = true;
                // Not declared on its own any more (2026-09-30): `SeaHud`'s
                // Wheel reserve, ticked right after, grows to cover this row,
                // and that reserve is what the IMGUI HUD and
                // `HudOverlapProbe` see.
            }
        }
    }
}
