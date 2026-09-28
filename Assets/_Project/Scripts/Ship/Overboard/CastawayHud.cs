using UnityEngine;
using SeaSick.Crew;
using SeaSick.UI;
using SeaSick.World;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// <summary>
    /// **Picking up a castaway** (phase 7, docs/PLAN-DEATH-RESCUE.md
    /// "Recruits at sea"; build brief item 3). IMGUI stand-in, same spirit
    /// as `RescueHud`/`Banner`: gameplay, not a menu, so it lives here
    /// instead of `Scripts/UI`.
    ///
    /// Within `RecruitTuning.PickupMetres` of a castaway's saved beach spot
    /// and slower than `OverboardTuning.ThrowMaxSpeed` (the same "not
    /// making way" threshold the rescue's own Throw Line button uses): a
    /// big bottom-centre button, "Take &lt;name&gt; aboard" -- or, with no
    /// free berth (`Outpost.BerthRefusal`, the same check `CarryAboard`
    /// itself refuses on), a disabled one that says so. Tapping it starts a
    /// `RecruitTuning.PickupSeconds` fill (`Update` advances it once a
    /// frame; `OnGUI` only ever reads and draws); moving out of range,
    /// speeding up, or losing the berth cancels it with no penalty.
    /// </summary>
    public class CastawayHud : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<CastawayHud>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("CastawayHud");
            go.AddComponent<CastawayHud>();
            DontDestroyOnLoad(go);
        }

        HelmInput helm;
        float nextHelmLookup;

        // --- state advanced once a frame in Update, only ever READ in OnGUI --
        string targetName;      // nearest castaway in pickup range, or null
        bool targetTooFast;     // in range, but she's making too much way
        string berthWhy;        // non-empty = no free berth for targetName
        string pickingName;     // the target the player has committed to
        float pickupElapsed;

        void Update()
        {
            if (Time.time >= nextHelmLookup)
            {
                if (helm == null) helm = FindAnyObjectByType<HelmInput>();
                nextHelmLookup = Time.time + 1f;
            }
            if (helm == null) { targetName = null; return; }
            var motor = helm.GetComponent<ShipMotor>();
            if (motor == null) { targetName = null; return; }

            Vector3 shipPos = motor.transform.position; shipPos.y = 0f;
            string nearest = null;
            float bestDist = float.MaxValue;
            foreach (var c in Lives.Castaways)
            {
                if (c == null || string.IsNullOrEmpty(c.name)) continue;
                Vector3 cp = new Vector3(c.x, 0f, c.z);
                float d = Vector3.Distance(cp, shipPos);
                if (d <= RecruitTuning.PickupMetres && d < bestDist) { bestDist = d; nearest = c.name; }
            }
            targetName = nearest;
            targetTooFast = motor.CurrentSpeed > OverboardTuning.ThrowMaxSpeed;
            berthWhy = targetName != null ? Outpost.BerthRefusal(motor.transform) : null;

            bool canProgress = targetName != null && !targetTooFast && string.IsNullOrEmpty(berthWhy);
            if (pickingName != null && (pickingName != targetName || !canProgress))
            {
                // Moved off, sped up, or lost the berth mid-fetch -- no
                // penalty, just start the fill over next time.
                pickingName = null;
                pickupElapsed = 0f;
            }
            if (pickingName != null)
            {
                pickupElapsed += Time.deltaTime;
                if (pickupElapsed >= RecruitTuning.PickupSeconds) Commit(pickingName, motor.transform);
            }
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(targetName)) return;

            int u = HudLayout.Unit;
            float btnH = Mathf.Max(u * 3.4f, 64f);
            float btnW = Mathf.Min(HudLayout.Safe.width - HudLayout.Pad * 2f, u * 26f);
            var safe = HudLayout.Safe;
            var rect = new Rect(safe.x + (safe.width - btnW) * 0.5f,
                HudLayout.BottomClustersTop - HudLayout.Gap - btnH, btnW, btnH);
            UIBlocker.Block(rect);

            if (targetTooFast)
            {
                var hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(u * 0.9f),
                    alignment = TextAnchor.MiddleCenter,
                };
                hintStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
                GUI.Label(rect, "slow down to fetch " + targetName, hintStyle);
                return;
            }

            if (!string.IsNullOrEmpty(berthWhy))
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(rect, "No free berth");
                GUI.enabled = wasEnabled;
                return;
            }

            if (pickingName == targetName)
            {
                GUI.Box(rect, "");
                float t = Mathf.Clamp01(pickupElapsed / Mathf.Max(0.01f, RecruitTuning.PickupSeconds));
                var fill = new Rect(rect.x, rect.y, rect.width * t, rect.height);
                UITheme.Rect(fill, new Color(0.35f, 0.75f, 0.4f, 0.55f));
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(u * 0.95f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                style.normal.textColor = Color.white;
                GUI.Label(rect, "Fetching " + targetName + "...", style);
                return;
            }

            if (GUI.Button(rect, "Take " + targetName + " aboard"))
            {
                pickingName = targetName;
                pickupElapsed = 0f;
            }
        }

        /// **The pickup finishes.** Same boarding path camp-born hands use
        /// (`BornVillager.Board`, exactly what `Swimmer.Rescue` calls for a
        /// swimmer whose own body is already gone) -- removed from
        /// `Lives.Castaways` first, so a save mid-board never double-counts
        /// them.
        void Commit(string name, Transform hull)
        {
            pickingName = null;
            pickupElapsed = 0f;

            string island = "";
            foreach (var c in Lives.Castaways)
                if (c != null && c.name == name) { island = c.island; break; }
            bool wasStranger = Lives.IsStranger(name);

            if (!Lives.RemoveCastaway(name)) return;

            Lives.Log(name, LifeEvents.Rescued);
            if (wasStranger) Lives.Log(name, LifeEvents.FoundCastaway, island);

            var agent = BornVillager.Board(name, hull);
            if (agent != null)
            {
                var roster = hull.GetComponentInParent<CrewRoster>() ?? hull.GetComponentInChildren<CrewRoster>(true);
                roster?.Refresh();
            }
            Banner.Show(name + " is aboard");
        }
    }
}
