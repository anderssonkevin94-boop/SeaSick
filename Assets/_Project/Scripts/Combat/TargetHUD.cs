using SeaSick.Ship;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Combat
{
    /// Damage and range, floating over the thing itself rather than buried in a
    /// diagnostic overlay. Two things are readable at a glance: how hurt it is,
    /// and whether it is inside the guns' reach — the bar goes live the moment
    /// the shot could actually get there, so the range envelope is something
    /// you see on the target instead of a number you convert in your head.
    ///
    /// Drawn off the IHittable registry, not off any one kind of enemy, so a
    /// raider gets the same treatment as a monster and whatever comes next
    /// needs no work here.
    [RequireComponent(typeof(CannonBattery))]
    public class TargetHUD : MonoBehaviour
    {
        [SerializeField] float showWithin = 320f;   // don't clutter the horizon
        [SerializeField] float barWidth = 84f;
        [SerializeField] float headroom = 8.0f;     // metres above the hit centre

        CannonBattery battery;
        Camera cam;

        void Awake() { battery = GetComponent<CannonBattery>(); }

        void OnGUI()
        {
            // Draw-only panel: skip the non-Repaint events. See StatusHUD for
            // the measurement — IMGUI runs OnGUI once per event, and the
            // discarded passes were the game's biggest source of GC garbage.
            if (Event.current.type != EventType.Repaint) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            float reach = battery.GunRange;
            int u = UITheme.Unit;

            foreach (var t in HitTargets.All)
            {
                // Never label your own ship.
                if (t == null || !t.Alive || t is PlayerHull) continue;

                Vector3 world = t.HitCentre + Vector3.up * headroom;
                float dist = Vector3.Distance(transform.position, t.HitCentre);
                if (dist > showWithin) continue;

                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;   // behind the camera

                // Shrink with distance so a far target doesn't shout.
                float scale = Mathf.Lerp(1f, 0.62f, Mathf.Clamp01(dist / showWithin));
                float w = barWidth * scale;
                float h = Mathf.Max(4f, u * 0.42f * scale);
                float x = sp.x - w * 0.5f;
                float y = Screen.height - sp.y;

                bool inRange = dist <= reach;

                // Backing plate keeps it legible against bright water.
                UITheme.Rect(new Rect(x - 2f, y - 2f, w + 4f, h + 4f), UITheme.Panel);

                // Out of reach reads dim and grey: the guns cannot answer yet.
                Color fill = inRange
                    ? Color.Lerp(UITheme.Bad, UITheme.Good, t.Health01)
                    : new Color(0.62f, 0.66f, 0.70f, 0.75f);
                UITheme.Bar(new Rect(x, y, w, h), t.Health01, fill);

                // A tick per hit point, so damage is countable and not just a
                // shrinking bar — several broadsides' worth of work is legible.
                int pips = t.HitPoints;
                if (pips > 1 && pips <= 16 && scale > 0.75f)
                    for (int i = 1; i < pips; i++)
                        UITheme.Rect(new Rect(x + w * i / pips, y, 1f, h),
                            new Color(0f, 0f, 0f, 0.45f));

                if (scale < 0.75f) continue;

                var prev = GUI.contentColor;
                GUI.contentColor = inRange ? UITheme.Text : UITheme.TextDim;
                GUI.Label(new Rect(x - 24f, y + h + 1f, w + 48f, u * 1.4f),
                    inRange
                        ? $"{Label(t)}{t.HitPoints - t.DamageTaken}/{t.HitPoints}   {dist:F0} m"
                        : $"{Label(t)}{dist:F0} m — out of reach",
                    UITheme.Small2Centered);
                GUI.contentColor = prev;
            }
        }

        /// Raiders say what they are currently doing. Knowing a guard has
        /// switched from patrolling to coming for you is worth more than its
        /// hit points, and it is the only way to read the AI's intent.
        static string Label(IHittable t) =>
            t is EnemyShip ship ? $"{ship.Current.ToString().ToLower()}  " : "";
    }
}
