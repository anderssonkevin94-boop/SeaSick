using UnityEngine;

namespace SeaSick.World
{
    /// Caps the frame rate (desktop and phone).
    ///
    /// Nothing set `Application.targetFrameRate` and both quality levels
    /// carry `vSyncCount 0`, so a desktop build ran uncapped: the GPU pinned
    /// at whatever it could manage, thermal state drifted through a session,
    /// and every frame-time number quoted in this project was measured
    /// against a moving ceiling. `HitchProbe` had to pin 60 fps itself to
    /// make a 3-slot-vs-5-slot readback comparison show anything at all.
    ///
    /// Phones too, since 2026-09-29: iOS defaults to 30 fps when nothing is
    /// set, and at 30 the chase camera's pans visibly step (Kevin: "too rough
    /// around the edges and almost too much movement"). He approved 60 on his
    /// iPhone 16 Pro; battery drain and thermal throttling are the cost to
    /// watch, and this is the one line to lower if the phone runs hot.
    /// Probes that pin the rate themselves save and restore it, so this
    /// only sets the resting value.
    public static class FramePacing
    {
        public const int DesktopTarget = 60;
        public const int MobileTarget = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            Application.targetFrameRate = Application.isMobilePlatform ? MobileTarget : DesktopTarget;
        }
    }
}
