using UnityEngine;

namespace SeaSick.World
{
    /// **Kevin's storage-visibility rule, applied to the crew shelter
    /// (2026-09-23).** Astra's kit (`art-staging/shelter-astra-lvl1-v1`)
    /// offers two cheap, real mappings and the README is explicit that
    /// nothing else here is backed by gameplay (chests are decorative,
    /// there is no occupancy save data, no sleep animation):
    ///
    /// - **Bedrolls = hands housed.** `Bed_Left`/`Bed_Right` are two fixed
    ///   modules and `OutpostLedger.Housed` (`hands.Count`) is the camp's
    ///   real occupancy, so showing `min(2, Housed)` beds made up is an
    ///   honest read of who actually lives here -- not a capacity number
    ///   (`BuildPlan.Hut.houses == 2` is the capacity; this is who is home).
    /// - **Entrance open/closed = anybody housed at all.** Open while the
    ///   camp has hands living in it, closed (tied shut) when it does not --
    ///   the same `Housed` read the campfire's cold/embers state uses.
    ///
    /// Lantern and chests stay as authored (always on): they are decorative
    /// props with no gameplay counterpart, per the README, not a state this
    /// view invents one for.
    public class ShelterStateView : MonoBehaviour
    {
        GameObject bedLeft, bedRight, entranceOpen, entranceClosed;
        bool discovered;

        OutpostLedger ledger;
        bool resolved;
        int resolveAttempts;
        int retryWait;
        const int RetryFrames = 30;
        const int MaxResolveAttempts = 20;

        int shownBeds = -1;
        bool shownOpen = true;

        void Start()
        {
            DiscoverSlots();
            if (bedLeft == null && bedRight == null && entranceOpen == null && entranceClosed == null)
                enabled = false;
        }

        void Update()
        {
            if (!resolved)
            {
                // **Never give up, just slow down (2026-09-23).** After
                // `MaxResolveAttempts` fast frames the view retries every
                // `RetryFrames` frames forever, rather than disabling itself
                // for good on a camp whose ledger arrived late (a load, a
                // raise mid-frame): one GetComponentInParent per ~half second.
                if (resolveAttempts >= MaxResolveAttempts && ++retryWait < RetryFrames) return;
                retryWait = 0;
                var outpost = GetComponentInParent<Outpost>();
                ledger = outpost != null ? outpost.Ledger : null;
                if (ledger != null) resolved = true;
                else { if (resolveAttempts < MaxResolveAttempts) resolveAttempts++; return; }
            }
            Apply();
        }

        void Apply()
        {
            int housed = Mathf.Max(0, ledger.Housed);
            int beds = Mathf.Clamp(housed, 0, 2);
            if (beds != shownBeds)
            {
                if (bedLeft != null) bedLeft.SetActive(beds >= 1);
                if (bedRight != null) bedRight.SetActive(beds >= 2);
                shownBeds = beds;
            }

            bool open = housed > 0;
            if (open != shownOpen)
            {
                if (entranceOpen != null) entranceOpen.SetActive(open);
                if (entranceClosed != null) entranceClosed.SetActive(!open);
                shownOpen = open;
            }
        }

        void DiscoverSlots()
        {
            if (discovered) return;
            discovered = true;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (BuildingFactory.Stem(t.name))
                {
                    case "Bed_Left": bedLeft = t.gameObject; break;
                    case "Bed_Right": bedRight = t.gameObject; break;
                    case "Entrance_Open": entranceOpen = t.gameObject; break;
                    case "Entrance_Closed": entranceClosed = t.gameObject; break;
                }
            }
            // Default open, no beds made up until the ledger says who's home.
            if (bedLeft != null) bedLeft.SetActive(false);
            if (bedRight != null) bedRight.SetActive(false);
            if (entranceOpen != null) entranceOpen.SetActive(true);
            if (entranceClosed != null) entranceClosed.SetActive(false);
        }

        /// **Drive the view without a live ledger** -- edit-mode / preview.
        public static ShelterStateView Preview(GameObject building, int housed)
        {
            var view = building.GetComponent<ShelterStateView>();
            if (view == null) view = building.AddComponent<ShelterStateView>();
            view.DiscoverSlots();
            view.resolved = true;
            view.ledger = null;
            view.enabled = false;
            int beds = Mathf.Clamp(housed, 0, 2);
            if (view.bedLeft != null) view.bedLeft.SetActive(beds >= 1);
            if (view.bedRight != null) view.bedRight.SetActive(beds >= 2);
            view.shownBeds = beds;
            bool open = housed > 0;
            if (view.entranceOpen != null) view.entranceOpen.SetActive(open);
            if (view.entranceClosed != null) view.entranceClosed.SetActive(!open);
            view.shownOpen = open;
            return view;
        }
    }
}
