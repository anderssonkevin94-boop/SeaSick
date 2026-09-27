using System.Runtime.InteropServices;
using UnityEngine;

namespace SeaSick.Ship
{
    /// **Native iOS haptics** (phase 5a). `JuiceTuning.hapticsOn` stays OFF
    /// forever -- `Handheld.Vibrate` deadlocked iOS 26's audio daemon at
    /// launch (see `JuiceTuning.cs:34`) and this project does not call it
    /// again. This is a SEPARATE path: `UIImpactFeedbackGenerator` /
    /// `UINotificationFeedbackGenerator` through a tiny Obj-C shim
    /// (`Assets/Plugins/iOS/SeaSickHaptics.mm`), which do not touch the
    /// audio session at all.
    ///
    /// Gated on its own knob, `JuiceTuning.overboardHapticsOn` -- separate
    /// from `hapticsOn` so a bad report about one never silently mutes the
    /// other. Compiled only for a real iOS device build; everywhere else
    /// (editor, Android, desktop) every call is a no-op.
    public static class OverboardHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SeaSick_HapticImpactHeavy();
        [DllImport("__Internal")] static extern void SeaSick_HapticNotifyWarning();
        [DllImport("__Internal")] static extern void SeaSick_HapticNotifyError();
#endif

        /// "Hold on!" -- the rail-grab warning.
        public static void Warning()
        {
            if (!JuiceTuning.overboardHapticsOn) return;
#if UNITY_IOS && !UNITY_EDITOR
            SeaSick_HapticNotifyWarning();
#endif
        }

        /// The splash -- somebody actually went in.
        public static void Fall()
        {
            if (!JuiceTuning.overboardHapticsOn) return;
#if UNITY_IOS && !UNITY_EDITOR
            SeaSick_HapticImpactHeavy();
#endif
        }

        /// Lost at sea -- the grim outcome.
        public static void Loss()
        {
            if (!JuiceTuning.overboardHapticsOn) return;
#if UNITY_IOS && !UNITY_EDITOR
            SeaSick_HapticNotifyError();
#endif
        }
    }
}
