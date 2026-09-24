using UnityEngine;

namespace SeaSick.Ship
{
    /// **Her engine, heard.** A procedural paddle-wheel chug whose pitch
    /// climbs with her speed and whose volume follows the throttle, and a
    /// short lever clack each time the telegraph order crosses a notch.
    ///
    /// Before this the ship made no sound of her own: `SeaAudio` has the wind,
    /// the water rushing past and the hull slapping, all of which answer the
    /// SEA. Nothing answered the helm, so an order was silent until the speed
    /// had built enough to change the rush, seconds later. The clack is heard
    /// the frame the order moves, and the chug's volume moves with the
    /// engine's own ramp, before the hull has gathered way.
    ///
    /// Everything is generated in code at Start (no audio assets), 2D, and
    /// quiet by default. Attached by `SpeedJuice.Start`, which only ever runs
    /// on the player's ShipMotor.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipMotor))]
    public class PaddleSound : MonoBehaviour
    {
        [Tooltip("Loudest the chug gets, at full throttle. Kept well under SeaAudio's water rush.")]
        [Range(0f, 1f)] [SerializeField] float volume = 0.35f;
        [Tooltip("Share of that volume left with the throttle closed and steam up. Anchored is silent.")]
        [Range(0f, 1f)] [SerializeField] float idleShare = 0.15f;
        [Tooltip("Chug pitch at rest. JuiceTuning.soundPitchRange raises it with speed.")]
        [SerializeField] float basePitch = 0.85f;
        [Tooltip("Extra pitch while the burn notch is rung.")]
        [SerializeField] float burnPitch = 0.06f;
        [Tooltip("How fast the volume follows the throttle, 1/s.")]
        [SerializeField] float volumeResponse = 5f;
        [Tooltip("How fast the pitch follows the speed, 1/s.")]
        [SerializeField] float pitchResponse = 3f;
        [Tooltip("Telegraph clack loudness.")]
        [Range(0f, 1f)] [SerializeField] float clickVolume = 0.45f;

        // The telegraph's notches. The burn tier is its own event (any order
        // above full ahead), so it is not in this list.
        static readonly float[] Notches = { 0f, 0.5f, 1f };
        // After a clack at a notch the order must move this far away before
        // that notch can clack again: a thumb resting ON half ahead jitters
        // across it, and that must not chatter.
        const float Rearm = 0.06f;
        const float MinClickGap = 0.08f;

        const int Rate = 44100;
        static AudioClip chugClip, clickClip;

        ShipMotor motor;
        AudioSource chug, click;
        readonly bool[] armed = { true, true, true };
        float prevOrder;
        bool wasBurning, primed;
        float lastClick = -99f;

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            if (chugClip == null) chugClip = MakeChug();
            if (clickClip == null) clickClip = MakeClick();

            chug = MakeSource("PaddleChug", chugClip, true);
            click = MakeSource("TelegraphClack", clickClip, false);
            chug.Play();
        }

        AudioSource MakeSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D: her own engine, not a sound in the world
            src.volume = 0f;
            src.pitch = basePitch;
            src.dopplerLevel = 0f;
            src.priority = 64;
            return src;
        }

        void Update()
        {
            if (motor == null || chug == null) return;
            float dt = Time.deltaTime;

            // Read every frame: the lab moves this live.
            float range = Mathf.Max(0f, JuiceTuning.soundPitchRange);
            float s01 = JuiceTuning.Speed01(motor);

            // Volume follows the THROTTLE (the engine), pitch follows the
            // SPEED (the wheel turning over faster as she gathers way). The
            // burn tier sits above 1 and pushes both a little past full.
            float thr = Mathf.Abs(motor.Throttle);
            float burn = Mathf.Clamp01((thr - 1f) / Mathf.Max(0.01f, motor.Overdrive - 1f));
            float wantVol = motor.Anchored
                ? 0f
                : volume * Mathf.Lerp(idleShare, 1f, Mathf.Clamp01(thr)) * (1f + 0.15f * burn);
            float wantPitch = basePitch * (1f + range * s01) + burnPitch * burn;
            if (dt > 0f)
            {
                chug.volume = Mathf.Lerp(chug.volume, Mathf.Clamp01(wantVol), 1f - Mathf.Exp(-volumeResponse * dt));
                chug.pitch = Mathf.Lerp(chug.pitch, wantPitch, 1f - Mathf.Exp(-pitchResponse * dt));
            }

            Telegraph();
        }

        /// A clack when the ORDER crosses a notch, a deeper clunk and a buzz
        /// when the burn is rung. Arriving at or passing a notch counts;
        /// leaving one does not, so sweeping 0 -> full clacks at half and at
        /// full, and stopping clacks once at stop.
        void Telegraph()
        {
            float order = motor.ThrottleOrder;
            bool burning = motor.Burning;
            if (!primed) { prevOrder = order; wasBurning = burning; primed = true; return; }

            for (int i = 0; i < Notches.Length; i++)
                if (!armed[i] && Mathf.Abs(order - Notches[i]) > Rearm) armed[i] = true;

            if (burning && !wasBurning)
            {
                Clack(0.72f, 1.2f);
                Haptics.Pulse();
            }
            else if (!Mathf.Approximately(order, prevOrder))
            {
                for (int i = 0; i < Notches.Length; i++)
                {
                    float n = Notches[i];
                    bool crossed = (prevOrder < n && order >= n) || (prevOrder > n && order <= n);
                    if (!crossed || !armed[i]) continue;
                    armed[i] = false;
                    // Stop is a lower note than the ahead notches, so the
                    // ear can tell "rung off" from "rung up" without looking.
                    Clack(n == 0f ? 0.86f : 1f, 1f);
                    Haptics.Pulse();
                    break;   // one clack per frame, however far it jumped
                }
            }

            prevOrder = order;
            wasBurning = burning;
        }

        void Clack(float pitch, float gain)
        {
            if (click == null || Time.unscaledTime - lastClick < MinClickGap) return;
            lastClick = Time.unscaledTime;
            click.pitch = pitch;
            click.PlayOneShot(clickClip, Mathf.Clamp01(clickVolume * gain));
        }

        // ------------------------------------------------------ synthesis

        /// 1.2 s loop, four paddle beats with an accent pattern: each beat is
        /// a falling low thud (with 2nd and 3rd harmonics so a PHONE speaker,
        /// which has almost nothing under 200 Hz, still hears the knock), a
        /// splash of dark noise as the float hits the water, and a steam
        /// chuff in the band a phone reproduces well. Under it a faint rumble.
        /// Beats are written modulo the loop length, so a tail that runs past
        /// the end lands at the start and the seam is exact.
        static AudioClip MakeChug()
        {
            const float seconds = 1.2f;
            int n = Mathf.RoundToInt(Rate * seconds);
            var data = new float[n];
            var rnd = new System.Random(7919);

            float[] accent = { 1f, 0.62f, 0.82f, 0.58f };
            int beatLen = Mathf.RoundToInt(Rate * 0.34f);
            float kChuffHi = OnePole(2500f), kChuffLo = OnePole(400f), kSplash = OnePole(1100f);

            for (int b = 0; b < 4; b++)
            {
                int start = Mathf.RoundToInt(b * seconds / 4f * Rate);
                float a = accent[b];
                float phase = 0f, hi = 0f, lo = 0f, sp = 0f;
                for (int k = 0; k < beatLen; k++)
                {
                    float t = k / (float)Rate;
                    // Thud: pitch falls 95 -> 58 Hz over the first 60 ms.
                    float f = Mathf.Lerp(58f, 95f, Mathf.Exp(-t / 0.06f));
                    phase += 2f * Mathf.PI * f / Rate;
                    float env = (1f - Mathf.Exp(-t / 0.004f)) * Mathf.Exp(-t / 0.075f);
                    float thud = (Mathf.Sin(phase) + 0.5f * Mathf.Sin(2f * phase)
                                  + 0.28f * Mathf.Sin(3f * phase)) * env;

                    float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    // Splash: dark noise, starting 15 ms after the thud.
                    sp += (w - sp) * kSplash;
                    float ts = t - 0.015f;
                    float splash = ts > 0f ? sp * Mathf.Exp(-ts / 0.05f) * 1.6f : 0f;
                    // Chuff: band-passed noise, 400..2500 Hz.
                    hi += (w - hi) * kChuffHi;
                    lo += (hi - lo) * kChuffLo;
                    float chuffEnv = (1f - Mathf.Exp(-t / 0.01f)) * Mathf.Exp(-t / 0.11f);
                    float chuff = (hi - lo) * chuffEnv * 0.9f;

                    data[(start + k) % n] += a * (0.8f * thud + 0.45f * splash + chuff);
                }
            }

            // The rumble bed: made longer than the loop, and the overhang is
            // crossfaded into the head so the last sample runs straight into
            // the first.
            int fade = 2000;
            var bed = new float[n + fade];
            float r1 = 0f, r2 = 0f, kBed = OnePole(140f), kBedHp = OnePole(30f);
            for (int i = 0; i < bed.Length; i++)
            {
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                r1 += (w - r1) * kBed;
                r2 += (r1 - r2) * kBedHp;
                bed[i] = (r1 - r2) * 2.5f;
            }
            for (int i = 0; i < fade; i++)
                bed[i] = Mathf.Lerp(bed[n + i], bed[i], i / (float)fade);
            for (int i = 0; i < n; i++) data[i] += 0.12f * bed[i];

            Normalise(data, 0.8f);
            var clip = AudioClip.Create("PaddleChug", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// The telegraph lever: two quick metallic clacks 28 ms apart.
        static AudioClip MakeClick()
        {
            int n = Mathf.RoundToInt(Rate * 0.09f);
            var data = new float[n];
            var rnd = new System.Random(104729);
            int[] at = { 0, Mathf.RoundToInt(Rate * 0.028f) };
            float[] gain = { 1f, 0.6f };
            for (int c = 0; c < at.Length; c++)
            {
                float prev = 0f;
                for (int k = 0; at[c] + k < n; k++)
                {
                    float t = k / (float)Rate;
                    float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    float hp = w - prev; prev = w;   // crude high-pass: a hard tick
                    float tick = hp * Mathf.Exp(-t / 0.0025f);
                    float ring = (0.45f * Mathf.Sin(2f * Mathf.PI * 2350f * t)
                                  + 0.3f * Mathf.Sin(2f * Mathf.PI * 880f * t))
                                 * Mathf.Exp(-t / 0.014f);
                    data[at[c] + k] += gain[c] * (0.6f * tick + ring);
                }
            }
            Normalise(data, 0.85f);
            var clip = AudioClip.Create("TelegraphClack", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// One-pole low-pass coefficient for a cutoff at the clip rate.
        static float OnePole(float hz) => 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);

        static void Normalise(float[] data, float peakTo)
        {
            float peak = 1e-4f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float k = peakTo / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
        }

        void OnDestroy()
        {
            if (chug != null) Destroy(chug.gameObject);
            if (click != null) Destroy(click.gameObject);
        }
    }

    /// **The hand's half of the telegraph.** Unity has no fine haptics on iOS
    /// without a native plugin: `Handheld.Vibrate` is the system's single
    /// ~0.4 s buzz, not a Taptic tick. So it is used sparingly -- only on a
    /// notch crossing and on burn engage, never continuously, and never more
    /// than twice a second. A proper Taptic Engine plugin
    /// (UIImpactFeedbackGenerator light/medium) is a later task and would
    /// replace the body of `Pulse` only.
    public static class Haptics
    {
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        const float MinGap = 0.5f;
        static float last = -99f;
#endif

        public static void Pulse()
        {
            if (!JuiceTuning.hapticsOn) return;
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            if (Time.unscaledTime - last < MinGap) return;
            last = Time.unscaledTime;
            Handheld.Vibrate();
#endif
            // Desk and editor: nothing to buzz.
        }
    }
}
