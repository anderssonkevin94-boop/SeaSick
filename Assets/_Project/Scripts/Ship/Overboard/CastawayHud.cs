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

        // --- state advanced once a frame in Update, read by Offer() ----------
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
            Offer();
        }

        // --- the sea action card (Kevin, 2026-09-30, island UI phase 6) -----
        //
        // The IMGUI button this drew at `BottomClustersTop` (with a
        // `new GUIStyle` per event) is an offer to the sea HUD's one action
        // card now (`SeaSick.UI.Sheets.SeaActions`): "Take X aboard" as the tap, the
        // fetch as the card's progress bar, and "No free berth" / "slow down"
        // as information (no chevron, the stick stays free under it).

        readonly SeaSick.UI.Sheets.SeaActions.Joined takeTitle = new SeaSick.UI.Sheets.SeaActions.Joined();
        readonly SeaSick.UI.Sheets.SeaActions.Joined fetchTitle = new SeaSick.UI.Sheets.SeaActions.Joined();
        System.Action startPick;

        void Offer()
        {
            if (string.IsNullOrEmpty(targetName)) return;
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            const int P = SeaSick.UI.Sheets.SeaActions.PriorityRescue + 5;
            if (startPick == null) startPick = StartPick;
            if (pickingName == targetName)
            {
                float t = Mathf.Clamp01(pickupElapsed / Mathf.Max(0.01f, RecruitTuning.PickupSeconds));
                SeaSick.UI.Sheets.SeaActions.Offer(P, "IN THE WATER", fetchTitle.Of("Fetching ", targetName, "..."),
                    "Hold her steady alongside", null, false, t);
                return;
            }
            if (!string.IsNullOrEmpty(berthWhy))
            {
                SeaSick.UI.Sheets.SeaActions.Offer(P, "IN THE WATER", "No free berth", berthWhy, null, false);
                return;
            }
            SeaSick.UI.Sheets.SeaActions.Offer(P, "IN THE WATER", takeTitle.Of("Take ", targetName, " aboard"),
                targetTooFast ? SeaSick.UI.Sheets.SeaActions.SlowTo(OverboardTuning.ThrowMaxSpeed) : "Lift them onto the ship",
                startPick, !targetTooFast);
        }

        void StartPick()
        {
            if (string.IsNullOrEmpty(targetName) || targetTooFast || !string.IsNullOrEmpty(berthWhy)) return;
            pickingName = targetName;
            pickupElapsed = 0f;
        }

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
