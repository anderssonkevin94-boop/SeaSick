using UnityEngine;

namespace SeaSick.CameraRig
{
    /// Three calm resting positions. Reversals settle at center before another
    /// sustained turn earns the opposite quarter; small corrections stay near aft.
    public sealed class SailingTurnOrbit
    {
        public float Angle { get; private set; }
        public int Side { get; private set; }
        float candidateSeconds, straightSeconds, centerSeconds, velocity;
        int candidate;
        bool centering;

        public void Reset()
        {
            Angle = 0f; Side = candidate = 0; centering = false;
            candidateSeconds = straightSeconds = centerSeconds = velocity = 0f;
        }

        public float Step(float yawDegreesPerSecond, float speed, float dt, bool active, float orbitDeg = 8f)
        {
            dt = Mathf.Clamp(dt, 0f, .1f);
            if (dt <= 0f) return Angle;
            if (!active)
            {
                candidate = 0; candidateSeconds = centerSeconds = 0f; velocity = 0f;
                return Angle;
            }
            float yaw = speed > .8f ? yawDegreesPerSecond : 0f;
            int turn = Mathf.Abs(yaw) > 3f ? (yaw > 0f ? 1 : -1) : 0;
            bool deliberate = Mathf.Abs(yaw) > 8f;
            // 2026-09-29: the full quarter is a live knob (was 25 deg, too much swing);
            // the gentle-turn hint scales with it and stays small.
            float quarter = Mathf.Max(0f, orbitDeg);
            float hint = Mathf.Min(7f, quarter * .3f);
            float aim = Side * quarter;
            if (turn == 0)
            {
                candidate = 0; candidateSeconds = 0f;
                straightSeconds += dt;
                if (straightSeconds >= 3f) { Side = 0; aim = 0f; }
            }
            else
            {
                straightSeconds = 0f;
                if (candidate != turn) { candidate = turn; candidateSeconds = 0f; centerSeconds = 0f; }
                candidateSeconds = deliberate ? candidateSeconds + dt : 0f;
                if (Side != 0 && Side != turn)
                {
                    Side = 0; centering = true; centerSeconds = 0f;
                }
                if (!centering)
                {
                    if (deliberate && candidateSeconds >= .9f) Side = turn;
                    // Gentle turns reveal only a hint of side, never a full quarter.
                    if (!deliberate) Side = 0;
                    aim = Side != 0 ? Side * quarter : turn * Mathf.Lerp(0f, hint, Mathf.InverseLerp(3f, 8f, Mathf.Abs(yaw)));
                }
            }
            if (centering)
            {
                aim = 0f;
                centerSeconds = Mathf.Abs(Angle) < 2.5f ? centerSeconds + dt : 0f;
                if (centerSeconds >= .65f) { centering = false; candidateSeconds = 0f; }
            }
            Angle = Mathf.SmoothDamp(Angle, aim, ref velocity, 1.1f, 22f, dt);
            return Angle;
        }
    }
}
