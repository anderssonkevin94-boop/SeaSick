using SeaSick.CameraRig;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Combat
{
    /// Hold a fight in view. Space locks the nearest enemy; the camera then
    /// frames the pair of you instead of just your stern.
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
        float outOfRangeFor;

        public IHittable Locked { get; private set; }

        /// True when space belongs to the lock rather than to the anchor.
        /// Computed rather than stored, so it never depends on which component
        /// ran first this frame.
        public bool WantsSpace => Locked != null || Candidate() != null;

        void Update()
        {
            if (chase == null) chase = FindFirstObjectByType<ChaseCamera>();

            // Drop a lock that has died, sunk out of the registry, or run.
            if (Locked != null)
            {
                bool dead = !Locked.Alive || !HitTargets.All.Contains(Locked);

                outOfRangeFor = Distance(Locked) > breakRange
                    ? outOfRangeFor + Time.deltaTime : 0f;

                if (dead || outOfRangeFor >= breakGrace) Release();
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                if (Locked != null) Release();
                else
                {
                    var c = Candidate();
                    if (c != null) Take(c);
                }
            }

            // Re-point every frame: the target is a ship and it is moving.
            if (chase != null)
                chase.LockTarget = Locked is MonoBehaviour mb && mb != null ? mb.transform : null;
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
        }

        /// Nearest thing worth locking, inside acquisition range.
        IHittable Candidate()
        {
            if (self == null) self = GetComponent<PlayerHull>();

            IHittable best = null;
            float bestSq = lockRange * lockRange;

            foreach (var t in HitTargets.All)
            {
                if (t == null || !t.Alive || ReferenceEquals(t, self) || t is PlayerHull) continue;
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

        void OnGUI()
        {
            // Draw-only panel: skip the non-Repaint events. See StatusHUD for
            // the measurement — IMGUI runs OnGUI once per event, and the
            // discarded passes were the game's biggest source of GC garbage.
            if (Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;

            int u = UITheme.Unit;

            // Brackets on the locked target, so "locked" is a thing you see on
            // the enemy rather than a word in a corner.
            if (Locked != null)
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
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                        {
                            float cx = sp.x + sx * r, cy = y + sy * r;
                            UITheme.Rect(new Rect(cx - (sx < 0 ? 0f : r * 0.45f), cy, r * 0.45f, t), c);
                            UITheme.Rect(new Rect(cx - (sx < 0 ? 0f : t), cy - (sy < 0 ? 0f : r * 0.45f), t, r * 0.45f), c);
                        }
                }
            }

            // Prompt, bottom centre, above the broadside buttons.
            string msg;
            if (Locked != null)
            {
                float d = Distance(Locked);
                msg = d > breakRange
                    ? $"lock slipping —  {d:F0} m"
                    : $"space  ·  release lock   ({d:F0} m)";
            }
            else msg = Candidate() != null ? "space  ·  lock on" : null;
            if (msg == null) return;

            // The shared prompt slot, at a rank below the anchor's. This line
            // used to be pinned at `h − 8.2u` while AnchorController pinned its
            // BUTTON at `h − 6.9u`, and at 1080x2340 the two rects overlap by
            // 34 px. Whichever drew second won the pixels; whichever drew
            // second in the mouse pass won the tap.
            if (!Prompts.Claim(Prompts.Rank.Combat)) return;
            var r2 = Prompts.Begin().Next(u * 1.9f);
            UITheme.Rect(r2, UITheme.Panel);
            GUI.Label(r2, msg, UITheme.Small2Centered);
        }
    }
}
