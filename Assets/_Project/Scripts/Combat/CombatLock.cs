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
    /// leaving the lock -- and the lock camera -- keyboard-only. It became a
    /// real button in the shared bottom-centre prompt slot in 04a07b9 (same
    /// day), and a tap on the enemy ship in the top half of the screen (out
    /// of the helm's zone) locked it too.
    ///
    /// **Moved to its own corner** (2026-09-27, Kevin: "the click to lock
    /// button should appear somewhere better on the screen where it's easier
    /// to press. even pressing the ship should work."). The shared slot was
    /// the wrong home for it: it is ~126 px tall, shares real estate with
    /// "come alongside", and sits far enough from the thumb's rest position
    /// that Kevin still could not reliably press it in a fight. It now draws
    /// itself at a fixed spot -- `HudLayout.Slot.Lock`, bottom-right, nearest
    /// the safe area's edge -- rather than bidding for the contested slot,
    /// and a tap locks the enemy ship anywhere on screen, not only the top
    /// half. Both the button and a tap still call `ToggleLock` / `LockOn`,
    /// the same path as space. While a lock holds, the battery fires on its
    /// own (`CannonBattery.AutoFireTarget`).
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
        [SerializeField] float tapMaxSeconds = 0.25f;
        [Tooltip("Furthest a tap may wander, as a fraction of the short screen side -- this already comes out close to Apple's own slop constant (~11-12 pt) at phone resolutions, so it is left resolution-relative rather than a hardcoded pixel count.")]
        [SerializeField] float tapMaxMove01 = 0.03f;
        [Tooltip("Smallest tap radius around a ship's projected hull, points. Generous on purpose: 'even pressing the ship should work' (Kevin, 2026-09-27), not just its exact silhouette.")]
        [SerializeField] float tapHitRadiusPt = 60f;

        Vector2 tapStart;
        float tapStartTime = -1f;

        /// A short tap on an enemy ANYWHERE on screen locks it (2026-09-27;
        /// this used to be restricted to the top half, out of the helm's
        /// zone, when the lock button lived in the shared prompt slot and
        /// needed the bottom half kept clear for it). What still makes this
        /// safe against the floating stick and against reintroducing
        /// tap-to-sail is the SAME gesture gate as before: only a release
        /// that was short (`tapMaxSeconds`) and barely moved (`tapMaxMove01`)
        /// counts as a tap at all -- a drag that starts on a ship is still a
        /// drag, and a tap that lands on open water hits nothing and does
        /// nothing, same as always. A tap on the ship you already have
        /// locked is a no-op (`LockOn` below refuses it): release is the
        /// button's job only, so a stray tap near the target can't drop it.
        void TapToLock()
        {
            var p = UnityEngine.InputSystem.Pointer.current;
            if (p == null || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;

            if (p.press.wasPressedThisFrame)
            {
                tapStart = p.position.ReadValue();
                tapStartTime = !UIBlocker.Blocked(tapStart) ? Time.unscaledTime : -1f;
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

        /// Whether a tap landing at this screen point (input space, the same
        /// convention `PickAt` and `Pointer.position` use) would lock an
        /// enemy — for `HelmInput`, so the SAME touch-up that locks a ship
        /// does not also read as the helm's "tap = stop" (2026-09-27, Kevin:
        /// tapping a ship in the lower half of the screen was locking it AND
        /// ringing the telegraph to stop in the same gesture). Cheap: reuses
        /// `PickAt`'s own hit test, no extra allocation or state.
        public bool WouldLock(Vector2 screen) => PickAt(screen) != null;

        /// The enemy under a screen point (input space), within break range,
        /// or null. The hit circle is the hull's own projected size, but never
        /// smaller than `tapHitRadiusPt`.
        IHittable PickAt(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            if (self == null) self = GetComponent<PlayerHull>();
            float minR = PtPx(tapHitRadiusPt);

            IHittable best = null;
            float bestSq = float.MaxValue;
            foreach (var t in HitTargets.All)
            {
                if (t == null || !t.Alive || ReferenceEquals(t, self) || t is PlayerHull || t is IFriendly) continue;
                if (Distance(t) > breakRange) continue;
                Vector3 sp = cam.WorldToScreenPoint(t.HitCentre);
                if (sp.z <= 0f) continue;
                Vector3 edge = cam.WorldToScreenPoint(t.HitCentre + cam.transform.right * t.HitRadius);
                float r = Mathf.Max(minR, Vector2.Distance(sp, edge));
                float sq = ((Vector2)sp - screen).sqrMagnitude;
                if (sq <= r * r && sq < bestSq) { bestSq = sq; best = t; }
            }
            return best;
        }

        /// Points to pixels. `Screen.dpi / 160` is the same points-to-pixels
        /// guess `FeelLab` makes; 0 dpi (some desktops, and the editor Game
        /// view) reads as 1x, so the pt-based floors below fall back to being
        /// literal pixel counts there rather than vanishing.
        static float PtPx(float pt)
        {
            float dpi = Screen.dpi;
            float scale = dpi > 0f ? Mathf.Clamp(dpi / 160f, 1f, 3f) : 1f;
            return pt * scale;
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

        // ---- the lock button: its own corner, not the shared prompt slot ---

        readonly HudLabel buttonCaption = new HudLabel();
        static Texture2D roundTex;

        /// A soft-edged filled circle, built once and cached. Same rule as
        /// the rest of this HUD's text meshes: build once, key off reference
        /// equality, never regenerate per frame.
        static Texture2D RoundTex()
        {
            if (roundTex != null) return roundTex;
            const int n = 64;
            roundTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            roundTex.hideFlags = HideFlags.HideAndDontSave;
            Vector2 c = new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f);
            float rad = n * 0.5f;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(rad - dist));
                }
            roundTex.SetPixels(px);
            roundTex.Apply();
            return roundTex;
        }

        static void DrawRound(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, RoundTex());
            GUI.color = prev;
        }

        /// What the button showed LAST frame, so a pulse fires once per new
        /// candidate rather than every frame the same one is in range.
        IHittable pulseFrom;
        float pulseStart = -10f;

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
            if (shown == null) { ButtonRect = default; pulseFrom = null; return; }

            // A short pulse the moment a NEW candidate comes into range --
            // not on every frame the same one sits there, and not while a
            // lock is held (taking the lock is the payoff; it doesn't need
            // to keep announcing itself). Cheap: one reference compare and a
            // float lerp, no allocation.
            if (Locked == null && !ReferenceEquals(shown, pulseFrom))
                pulseStart = Time.unscaledTime;
            pulseFrom = Locked == null ? shown : null;
            float pulseT = Mathf.Clamp01(1f - (Time.unscaledTime - pulseStart) / 0.4f);

            // Its own fixed corner now (`HudLayout.Slot.Lock`, bottom-right,
            // nearest the safe area's edge) rather than a bid for the shared
            // prompt slot -- see the class doc for why. ~72 pt, the size
            // Kevin asked for: the unit-scaled size is primary (it already
            // matches the rest of the HUD across both target resolutions),
            // with a points-based floor under it for a device whose `u`
            // clamps low relative to its real pixel density.
            float baseD = Mathf.Max(u * 10.5f, PtPx(66f));
            float diameter = baseD * (1f + 0.16f * pulseT);
            var r2 = HudLayout.Place(HudLayout.Slot.Lock, diameter, diameter);
            ButtonRect = r2;
            // Claimed on every event, same reason the rest of this HUD does:
            // a button that only claims on Repaint loses the mouse-up that
            // would have pressed it.
            UIBlocker.Block(r2);

            bool held = Locked != null;
            float dist = Distance(shown);
            bool slipping2 = held && dist > breakRange;

            if (Event.current.type == EventType.Repaint)
            {
                // Ember/red once locked (amber while the lock is slipping,
                // same fade the target brackets use), a cool highlight that
                // brightens with the pulse while it's only a candidate.
                Color fill = held
                    ? Color.Lerp(new Color(0.82f, 0.20f, 0.16f, 0.97f),
                                 new Color(0.95f, 0.58f, 0.22f, 0.90f), slipping2 ? 1f : 0f)
                    : Color.Lerp(new Color(0.10f, 0.20f, 0.28f, 0.92f),
                                 new Color(0.35f, 0.66f, 0.86f, 1f), pulseT);
                DrawRound(r2, fill);

                if (buttonCaption.Changed(HudLabel.Key(held ? 1 : 0, Mathf.RoundToInt(dist))))
                    buttonCaption.Set(held ? "Release" : $"{dist:F0} m");

                // Reticle icon on top, the distance (or "Release") under it --
                // both fit inside the circle without crowding it.
                GUI.Label(new Rect(r2.x, r2.y + r2.height * 0.14f, r2.width, r2.height * 0.42f),
                    "◎", UITheme.Title);
                GUI.Label(new Rect(r2.x, r2.y + r2.height * 0.58f, r2.width, r2.height * 0.3f),
                    buttonCaption.Content, UITheme.Small2Centered);
            }

            // Invisible on top of the drawn circle -- GUI.Button's own click
            // detection is rect-based regardless of what style draws it, so
            // this still needs to run on every event, not only Repaint.
            if (GUI.Button(r2, GUIContent.none, GUIStyle.none)) ToggleLock();
        }
    }
}
