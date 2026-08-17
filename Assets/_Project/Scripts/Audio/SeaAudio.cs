using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Audio
{
    /// Wind, water rush and hull slap — generated as filtered noise in code so
    /// the game ships with sound and no audio assets. Everything is driven by
    /// what the ship is actually doing, which is most of the sensation of speed.
    public class SeaAudio : MonoBehaviour
    {
        [SerializeField] float windBaseVolume = 0.16f;
        [SerializeField] float windGustVolume = 0.34f;
        [SerializeField] float rushMaxVolume = 0.42f;
        [SerializeField] float slapVolume = 0.55f;
        [SerializeField] float slapCooldown = 0.45f;
        [SerializeField] float slapHeaveThreshold = 1.6f; // m/s of downward heave

        ShipMotor motor;
        AudioSource wind, rush, slap;
        float prevY;
        float prevVelY;
        float lastSlap = -99f;
        bool primed;

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();

            wind = MakeSource("WindLoop", Noise("wind", 4f, 0.020f, 0.0015f), 0.9f, true);
            rush = MakeSource("WaterRush", Noise("rush", 4f, 0.14f, 0.010f), 1f, true);
            slap = MakeSource("HullSlap", Noise("slap", 0.5f, 0.30f, 0.004f), 1f, false);
            slap.loop = false;
            slap.playOnAwake = false;
            slap.Stop();
        }

        AudioSource MakeSource(string name, AudioClip clip, float pitch, bool play)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f; // 2D: this is the player's own boat
            src.volume = 0f;
            src.pitch = pitch;
            if (play) src.Play();
            return src;
        }

        /// One-pole low-pass + high-pass over white noise. lp controls how dark
        /// the result is (wind = very dark rumble, spray = bright hiss).
        static AudioClip Noise(string name, float seconds, float lp, float hp)
        {
            const int rate = 22050;
            int n = Mathf.RoundToInt(rate * seconds);
            var data = new float[n];
            var rnd = new System.Random(name.GetHashCode());
            float low = 0f, high = 0f, peak = 0.0001f;

            for (int i = 0; i < n; i++)
            {
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                low += (w - low) * lp;
                high += (low - high) * hp;
                float s = low - high;
                data[i] = s;
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }

            float norm = 0.85f / peak;
            for (int i = 0; i < n; i++) data[i] *= norm;

            // Crossfade the seam so the loop doesn't click.
            int fade = Mathf.Min(700, n / 4);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[i] = Mathf.Lerp(data[n - fade + i], data[i], t);
            }

            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Update()
        {
            if (motor == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
            float gust = motor.GustFactor01;

            float windTarget = windBaseVolume + windGustVolume * gust + 0.10f * speed01;
            wind.volume = Mathf.Lerp(wind.volume, windTarget, 1f - Mathf.Exp(-2.5f * dt));
            wind.pitch = Mathf.Lerp(wind.pitch, 0.85f + 0.35f * gust, 1f - Mathf.Exp(-2f * dt));

            float rushTarget = rushMaxVolume * Mathf.Pow(speed01, 1.4f)
                               * (1f + 0.5f * motor.SurfBoost01);
            rush.volume = Mathf.Lerp(rush.volume, rushTarget, 1f - Mathf.Exp(-4f * dt));
            rush.pitch = Mathf.Lerp(rush.pitch, 0.8f + 0.5f * speed01 + 0.2f * motor.SurfBoost01,
                1f - Mathf.Exp(-4f * dt));

            // Hull slap when the bow drops hard into a trough.
            float y = motor.transform.position.y;
            float velY = (y - prevY) / dt;
            if (primed)
            {
                float accelDown = (prevVelY - velY) / dt;
                if (velY < -slapHeaveThreshold && accelDown > 3f && Time.time - lastSlap > slapCooldown)
                {
                    slap.volume = Mathf.Clamp01(slapVolume * Mathf.Clamp01(-velY / 4f));
                    slap.pitch = Random.Range(0.85f, 1.15f);
                    slap.Play();
                    lastSlap = Time.time;
                }
            }
            prevY = y;
            prevVelY = velY;
            primed = true;
        }
    }
}
