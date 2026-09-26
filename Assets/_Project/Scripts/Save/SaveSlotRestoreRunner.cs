using UnityEngine;

namespace SeaSick.Save
{
    /// **A bare coroutine host for `SaveSlots.RestorePending`.**
    /// `SaveGame.Restore` is a coroutine and `SaveSlots` is a static class,
    /// so a load kicked off from outside `MonoBehaviour` land (a menu
    /// button's click handler, `RestorePending` itself) needs somewhere to
    /// run it. This is exactly that and nothing else -- created, started,
    /// and destroyed by `SaveSlots` alone. Nobody should add one by hand.
    public class SaveSlotRestoreRunner : MonoBehaviour
    {
    }
}
