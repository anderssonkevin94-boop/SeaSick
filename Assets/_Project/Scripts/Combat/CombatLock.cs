using SeaSick.CameraRig;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Combat
{
    /// Hold a fight in view. Space locks the nearest enemy; the camera then
    /// frames the pair of you instead of just your stern.
    ///
    /// **On a phone the lock is a BUTTON** (2026-09-27, Kevin: "I can't press
    /// the button at sea that lets me lock on to the enemy"). The prompt used
    /// to be a `GUI.Label` reading "space · lock on" -- a keyboard hint drawn
    /// in the shape of a button, with nothing behind it a thumb could press.
    /// The phone's only lock path had been tapping the hull, and that went
    /// out with the rest of the tap-to-sail prototype (f858d54, 2026-09-24),
    /// leaving the lock -- and the lock camera -- keyboard-only.
    /// It is a real button in the shared prompt slot now, and a tap on the
    /// enemy ship itself (top half of the screen, out of the helm's zone)
    /// locks it too. Both call `ToggleLock` / `LockOn`, the same path as
    /// space. While a lock holds, the battery fires on its own
    /// (`CannonBattery.AutoFireTarget`).
    ///
    /// Opt-in on purpose. Keeping a target in frame means the view must turn
    /// when they are off the bow, and in a drag-to-steer game turning the view
    /// re-maps the helm — so the player decides when to accept that trade, and
    /// can drop it with the same key the moment it stops paying.
    public class CombatLock : MonoBehaviour
    {
        [SerializeField] float lockRange = 150f;
        /// Hysteresis: hold on past the range you could acquire at, or the
        /// lock drops every time they open the distance a little. But not too
        /// far past — the guns only reach 67m, so a lock held at 260m was
        /// paying the camera-swing cost long after the fight was over.
        [SerializeField] float breakRange = 200f;
        /// Must be outside breakRange for this long before releasing, so a
        /// single wave-driven metre over the line does not drop the lock.
        [SerializeField] float breakGrace = 1f;

        ChaseCamera chase;
        IHittable self;
        SeaSick.Ship.CannonBattery battery;
        float outOfRangeFor;
        float nextChaseLookup;
        /// This frame's `Candidate()`, found once in Update rather than on
        /// every IMGUI event.
        IHittable candidate;

        public IHittable Locked { get; private set; }

        /// What a lock would take right now, for the button and the probe.
        public IHittable CurrentCandidate => candidate;

        /// The lock button's rect last frame, GUI space, for `LockOnCheck`.
        public Rect ButtonRect { get; private set; }

        /// The lock button, the space bar and the probe all come here: lock
        /// the candidate, or drop the lock you hold. Returns true if a lock
        /// holds afterwards.
        public bool ToggleLock()
        {
            if (Locked != null) { Release(); return false; }
            var c = candidate ?? Candidate();
            if (c == null) return false;
            Take(c);
            return true;
        }

        /// Lock a specific target (a tap on it). Friendlies and your own hull
        /// are refused.
        public bool LockOn(IHittable t)
        {
            if (t == null || !t.Alive || t is PlayerHull || t is IFriendly
                || ReferenceEquals(t, self)) return false;
            Take(t);
            return true;
        }

        /// True when space belongs to the lock rather than to the anchor.
        /// Computed rather than stored, so it never depends on which component
        /// ran first this frame.
        public bool WantsSpace => Locked != null || Candidate() != null;

        void Update()
        {
            // Retried once a second, not every frame. A null camera meant a
            // full scene scan every frame for as long as it stayed null —
            // Shipyard measured the same call at 0.24-1.4 ms plus a share of
            // the per-frame garbage, for an object that is found on the first
            // frame it exists and then never looked for again.
            if (chase == null && Time.unscaledTime >= nextChaseLookup)
            {
                chase = FindFirstObjectByType<ChaseCamera>();
                nextChaseLookup = Time.unscaledTime + 1f;
            }

            // Drop a lock that has died, sunk out of the registry, or run.
            if (Locked != null)
            {
                bool dead = !Locked.Alive || !HitTargets.All.Contains(Locked);

                outOfRangeFor = Distance(Locked) > breakRange
                    ? outOfRangeFor + Time.deltaTime : 0f;

                if (dead || outOfRangeFor >= breakGrace) Release();
            }

            candidate = Locked == null ? Candidate() : null;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame
                && !SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked)
                ToggleLock();

            TapToLock();

            // Re-point every frame: the target is a ship and it is moving.
            if (chase != null)
                chase.LockTarget = Locked is MonoBehaviour mb && mb != null ? mb.transform : null;

            // Engaged means the guns work on their own (see CannonBattery).
            if (battery == null) battery = GetComponent<SeaSick.Ship.CannonBattery>();
            if (battery != null) battery.AutoFireTarget = Locked;
        }

        // ---- tap the enemy to lock it --------------------------------------

        [Header("Tap to lock")]
        [Tooltip("Longest press, seconds, that still counts as a tap rather than a drag or a hold.")]
        [SerializeField] float tapMaxSeconds = 0.35f;
        [Tooltip("Furthest a tap may wander, as a fraction of the short screen side.")]
        [SerializeField] float tapMaxMove01 = 0.03f;

        Vector2 tapStart;
        float tapStartTime = -1f;

        /// A short tap on an enemy in the TOP half locks it -- the one piece of
        /// the removed tap-to-sail prototype brought back, and only this piece:
        /// no tap on water does anything. The bottom half is
        /// the helm's (`TouchHelm` takes any press there, and a tap there
        /// means "stop"), so a tap on a ship drawn low on the screen goes to
        /// the helm as it always did -- the button is the lock there.
        void TapToLock()
        {
            var p = UnityEngine.InputSystem.Pointer.current;
            if (p == null || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;

            if (p.press.wasPressedThisFrame)
            {
                tapStart = p.position.ReadValue();
                // Input space is origin bottom-left: the top half is y > h/2.
                bool topHalf = tapStart.y > Screen.height * 0.5f;
                tapStartTime = topHalf && !UIBlocker.Blocked(tapStart) ? Time.unscaledTime : -1f;
                return;
            }
            if (!p.press.wasReleasedThisFrame || tapStartTime < 0f) return;

            float held = Time.unscaledTime - tapStartTime;
            tapStartTime = -1f;
            Vector2 end = p.position.ReadValue();
            float slop = tapMaxMove01 * Mathf.Min(Screen.width, Screen.height);
            if (held > tapMaxSeconds || (end - tapStart).sqrMagnitude > slop * slop) return;

            var t = PickAt(tapStart);
            if (t != null && !ReferenceEquals(t, Locked)) LockOn(t);
        }

        /// The enemy under a screen point (input space), within break range,
        /// or null. The hit circle is the hull's own projected size, but never
        /// smaller than a thumb.
        IHittable PickAt(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            if (self == null) self = GetComponent<PlayerHull>();
            float thumb = ThumbPx * 0.75f;

            IHittable best = null;
            float bestSq = float.MaxValue;
            foreach (var t in HitTargets.All)
            {
                if (t == null || !t.Alive || ReferenceEquals(t, self) || t is PlayerHull || t is IFriendly) continue;
                if (Distance(t) > breakRange) continue;
                Vector3 sp = cam.WorldToScreenPoint(t.HitCentre);
                if (sp.z <= 0f) continue;
                Vector3 edge = cam.WorldToScreenPoint(t.HitCentre + cam.transform.right * t.HitRadius);
                float r = Mathf.Max(thumb, Vector2.Distance(sp, edge));
                float sq = ((Vector2)sp - screen).sqrMagnitude;
                if (sq <= r * r && sq < bestSq) { bestSq = sq; best = t; }
            }
            return best;
        }

        /// 44 pt in pixels -- Apple's minimum tap target. `Screen.dpi / 160`
        /// is the same points-to-pixels guess `FeelLab` makes; 0 dpi (some
        /// desktops) reads as 1x.
        static float ThumbPx
        {
            get
            {
                float dpi = Screen.dpi;
                float scale = dpi > 0f ? Mathf.Clamp(dpi / 160f, 1f, 3f) : 1f;
                return 44f * scale;
            }
        }

        void Take(IHittable t)
        {
            Locked = t;
            outOfRangeFor = 0f;
        }

        void Release()
        {
            Locked = null;
            outOfRangeFor = 0f;
            if (chase != null) chase.LockTarget = null;
            if (battery != null) battery.AutoFireTarget = null;
        }

        void OnDisable() => Release();

        /// Nearest thing worth locking, inside acquisition range.
        IHittable Candidate()
        {
            if (self == null) self = GetComponent<PlayerHull>();

            IHittable best = null;
            float bestSq = lockRange * lockRange;

            foreach (var t in HitTargets.All)
            {
                if (t == null || !t.Alive || ReferenceEquals(t, self) || t is PlayerHull || t is IFriendly) continue;
                Vector3 d = t.HitCentre - transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = t; }
            }
            return best;
        }

        float Distance(IHittable t)
        {
            Vector3 d = t.HitCentre - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        readonly HudLabel buttonText = new HudLabel();
        SeaSick.Ship.AnchorController anchor;

        void OnGUI()
        {
            // Same suppressions as the rest of the sea HUD: the shipyard's
            // modal, the home/pause cards and the land sheet all sit where
            // this button would.
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen) return;
            if (SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            var cam = Camera.main;
            if (cam == null) return;

            int u = UITheme.Unit;

            // Brackets on the locked target, so "locked" is a thing you see on
            // the enemy rather than a word in a corner. Draw-only: Repaint.
            if (Locked != null && Event.current.type == EventType.Repaint)
            {
                Vector3 sp = cam.WorldToScreenPoint(Locked.HitCentre);
                if (sp.z > 0f)
                {
                    float y = Screen.height - sp.y;
                    float d = Distance(Locked);
                    float r = Mathf.Lerp(u * 2.6f, u * 1.2f, Mathf.Clamp01(d / breakRange));
                    float t = Mathf.Max(2f, u * 0.16f);

                    // Fade toward amber over the last quarter of the range, so
                    // losing the lock is something you watch coming rather
                    // than something that happens to you.
                    float slipping = Mathf.InverseLerp(breakRange * 0.75f, breakRange, d);
                    var c = Color.Lerp(new Color(1f, 0.45f, 0.35f, 0.95f),
                                       new Color(1f, 0.80f, 0.30f, 0.55f), slipping);

                    // Four corners rather than a full box: less to look through.
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            float cx = sp.x + sx * r, cy = y + sy * r;
                            UITheme.Rect(new Rect(cx - (sx < 0 ? 0f : r * 0.45f), cy, r * 0.45f, t), c);
                            UITheme.Rect(new Rect(cx - (sx < 0 ? 0f : t), cy - (sy < 0 ? 0f : r * 0.45f), t, r * 0.45f), c);
                        }
                }
            }

            var shown = Locked ?? candidate;
            if (shown == null) { ButtonRect = default; return; }

            // The shared prompt slot, at a rank below the anchor's. This line
            // used to be pinned at `h − 8.2u` while AnchorController pinned its
            // BUTTON at `h − 6.9u`, and at 1080x2340 the two rects overlap by
            // 34 px. Whichever drew second won the pixels; whichever drew
            // second in the mouse pass won the tap.
            //
            // Bid on EVERY event, not only Repaint (see `Prompts`): the old
            // label bid after its Repaint guard, which was harmless for a
            // label and would lose the mouse-up for a button.
            if (anchor == null) anchor = GetComponent<SeaSick.Ship.AnchorController>();
            bool underway = anchor == null
                || anchor.CurrentState == SeaSick.Ship.AnchorController.State.Underway;
            if (!Prompts.Claim(underway ? Prompts.Rank.CombatEngaged : Prompts.Rank.Combat))
            { ButtonRect = default; return; }

            // A thumb's target: the anchor prompt's 2.7u, but never under
            // 44 pt. At the clamped 20 px unit that is 54 px, which on a 3x
            // phone is 18 pt -- a third of Apple's minimum.
            float bh = Mathf.Max(u * 2.7f, ThumbPx);
            var r2 = Prompts.Begin().Next(bh);
            ButtonRect = r2;
            // Claimed on every event so `TouchHelm` (which takes any press in
            // the bottom half that nothing has claimed) leaves this one to
            // the button.
            UIBlocker.Block(r2);

            float dist = Distance(shown);
            bool slipping2 = Locked != null && dist > breakRange;
            int state = Locked == null ? 0 : slipping2 ? 2 : 1;
            bool wide = HudLayout.Wide;
            if (buttonText.Changed(HudLabel.Key(state, Mathf.RoundToInt(dist), wide ? 1 : 0)))
            {
                string key = wide ? "   (space)" : "";
                buttonText.Set(state switch
                {
                    0 => $"◎  Lock on   ·   {dist:F0} m{key}",
                    1 => $"Release   ·   {dist:F0} m   ·   guns auto{key}",
                    _ => $"Lock slipping   ·   {dist:F0} m{key}",
                });
            }
            if (GUI.Button(r2, buttonText.Content, UITheme.Button)) ToggleLock();
        }
    }
}
