using UnityEngine;

namespace SeaSick.World
{
    /// Caps the desktop frame rate.
    ///
    /// Nothing set `Application.targetFrameRate` and both quality levels
    /// carry `vSyncCount 0`, so a desktop build ran uncapped: the GPU pinned
    /// at whatever it could manage, thermal state drifted through a session,
    /// and every frame-time number quoted in this project was measured
    /// against a moving ceiling. `HitchProbe` had to pin 60 fps itself to
    /// make a 3-slot-vs-5-slot readback comparison show anything at all.
    ///
    /// Desktop only. A phone's platform default is its own decision (iOS
    /// runs 30 unless told otherwise) and raising it there is a battery and
    /// thermal call that belongs with the mobile tier work, not here.
    /// Probes that pin the rate themselves save and restore it, so this
    /// only sets the resting value.
    public static class FramePacing
    {
        public const int DesktopTarget = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Application.isMobilePlatform) return;
            Application.targetFrameRate = DesktopTarget;
        }
    }
}
