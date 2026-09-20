using UnityEngine;

namespace SeaSick.CameraRig
{
    /// **Pure two-finger maths**, factored out of `IslandInput` so it can be
    /// probed at a phone's 422 px of screen height and a desk's 2340 without
    /// a scene, a device, or even play mode.
    ///
    /// No Unity input is read here — both fingers' positions arrive as plain
    /// `Vector2`s, one frame apart, and every threshold is a FRACTION of
    /// `screenH`, never a pixel count, so the same physical gesture reads
    /// the same on a phone and a 4K desk monitor.
    public static class TwoFinger
    {
        /// Below this fraction of screen height apart, two fingers are
        /// treated as coincident: dividing by their separation would blow
        /// zoom up towards infinity, and the angle between two points on
        /// top of each other is noise, not a twist.
        public const float MinSeparationFrac = 0.01f;

        /// Turn two fingers' positions last frame and this frame into a
        /// zoom, a twist and a shared-vertical (tilt) signal.
        ///
        /// `zoomFactor` follows `IslandCam.ZoomAt`'s own convention: a
        /// factor below 1 zooms IN. Spreading the fingers apart grows the
        /// separation, so the factor has to be OLD separation over NEW, not
        /// the other way round.
        ///
        /// `twistDeg` is the signed angle the line between the fingers has
        /// turned through — a pure pinch, where both fingers move along
        /// that same line, turns it by exactly zero.
        ///
        /// `sharedVertical` is the part of the fingers' vertical motion
        /// they AGREE on, as a fraction of `screenH`: if both moved up by
        /// the same amount that is a tilt request; if they moved apart
        /// vertically (one up, one down, as any twist away from the
        /// horizontal does a little) neither finger's motion is a shared
        /// signal and this reads exactly zero, which is what keeps a twist
        /// from also being read as a tilt.
        ///
        /// `midDelta` is how far the pair's midpoint moved, for a caller
        /// that wants to pan with two fingers as well as zoom and twist —
        /// `IslandInput` does not, today, but the maths falls out of the
        /// same two positions and costs nothing extra to hand back.
        public static void Solve(Vector2 a0, Vector2 b0, Vector2 a1, Vector2 b1, float screenH,
            out float zoomFactor, out float twistDeg, out Vector2 midDelta, out float sharedVertical)
        {
            Vector2 d0 = b0 - a0;
            Vector2 d1 = b1 - a1;
            float sep0 = d0.magnitude;
            float sep1 = d1.magnitude;
            float minSep = Mathf.Max(0.0001f, MinSeparationFrac * Mathf.Max(1f, screenH));

            midDelta = (a1 + b1) * 0.5f - (a0 + b0) * 0.5f;

            if (sep0 < minSep || sep1 < minSep)
            {
                // Fingers too close together, at either end, to read a
                // separation from — report "nothing happened" rather than
                // a spike from dividing by a near-zero length.
                zoomFactor = 1f;
                twistDeg = 0f;
                sharedVertical = 0f;
                return;
            }

            zoomFactor = sep0 / sep1;
            twistDeg = Vector2.SignedAngle(d0, d1);

            float dyA = a1.y - a0.y;
            float dyB = b1.y - b0.y;
            // Mathf.Sign(0) is +1, not 0, so a finger that has not moved
            // vertically at all agrees with whichever way the other one is
            // going and contributes nothing (the Min below is 0) rather
            // than falsely cancelling a real tilt.
            if (Mathf.Sign(dyA) != Mathf.Sign(dyB))
            {
                sharedVertical = 0f;
            }
            else
            {
                float shared = Mathf.Sign(dyA) * Mathf.Min(Mathf.Abs(dyA), Mathf.Abs(dyB));
                sharedVertical = screenH > 0f ? shared / screenH : 0f;
            }
        }
    }
}
