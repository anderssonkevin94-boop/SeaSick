using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **The rope**, Barb_Muzzle to the barb's Line_Attach (PLAN-harpoon §2.4:
    /// slack sag → taut → strained, creaking and glowing warm). A
    /// LineRenderer on a sag curve: a parabola drop under the chord, the
    /// cheap stand-in for a catenary at these spans, shrinking with tension.
    ///
    /// Look: `Resources/Harpoon/rope-look.json` when it exists (widths and
    /// colours per band, colours as "#rrggbb" or [r,g,b(,a)] in 0..1 or
    /// 0..255); otherwise the defaults below. Material:
    /// `Resources/Harpoon/SS_Harpoon_Rope`, else the particles-unlit
    /// keep-alive the firing arcs use (it ships in the phone build), tinted
    /// through the vertex colour.
    public class HarpoonLine : MonoBehaviour
    {
        const int Points = 16;
        /// m a slack line's belly stays above the low (surface) end, so the
        /// swell doesn't swallow it.
        const float SlackLift = 0.35f;
        const float WhipSeconds = 0.55f;

        LineRenderer lr;
        readonly Vector3[] pts = new Vector3[Points];
        static AudioClip creakClip;
        AudioSource creak;
        float nextCreakAt;

        // Snap/cut recoil: the free end flies back to the muzzle.
        float whipT = -1f;
        Vector3 whipFrom;
        float whipAmp;

        // --- look ---
        float slackWidth = 0.05f, tautWidth = 0.045f, strainedWidth = 0.06f;
        Color slackColor = new Color(0.55f, 0.45f, 0.32f);
        Color tautColor = new Color(0.78f, 0.7f, 0.55f);
        Color strainedColor = new Color(1f, 0.45f, 0.18f);

        public static HarpoonLine Create(Transform parent)
        {
            var go = new GameObject("HarpoonLine");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<HarpoonLine>();
            line.Build();
            return line;
        }

        void Build()
        {
            lr = gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = Points;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sharedMaterial = RopeMaterial();
            lr.enabled = false;
            ReadLook();
        }

        static Material ropeMat;
        static Material RopeMaterial()
        {
            if (ropeMat != null) return ropeMat;
            var authored = Resources.Load<Material>("Harpoon/SS_Harpoon_Rope");
            if (authored != null) return ropeMat = authored;
            var keep = Resources.Load<Material>("Shaders/Keepalive/Keep_ParticlesUnlit_Opaque");
            if (keep != null) ropeMat = new Material(keep);
            else ropeMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            ropeMat.name = "HarpoonRope (runtime)";
            ropeMat.SetColor("_BaseColor", Color.white);
            return ropeMat;
        }

        [System.Serializable]
        class LookJson
        {
            public float slackWidth, tautWidth, strainedWidth;
        }

        void ReadLook()
        {
            var text = Resources.Load<TextAsset>("Harpoon/rope-look");
            if (text == null || string.IsNullOrEmpty(text.text)) return;
            string json = text.text;
            try
            {
                var w = JsonUtility.FromJson<LookJson>(json);
                if (w.slackWidth > 0f) slackWidth = w.slackWidth;
                if (w.tautWidth > 0f) tautWidth = w.tautWidth;
                if (w.strainedWidth > 0f) strainedWidth = w.strainedWidth;
            }
            catch (System.ArgumentException) { }
            ReadColor(json, "slackColor", ref slackColor);
            ReadColor(json, "tautColor", ref tautColor);
            ReadColor(json, "strainedColor", ref strainedColor);
        }

        /// Lenient: `"key": "#rrggbb"` or `"key": [r, g, b, a]` (0..1 or 0..255).
        static void ReadColor(string json, string key, ref Color c)
        {
            int k = json.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal);
            if (k < 0) return;
            int colon = json.IndexOf(':', k);
            if (colon < 0) return;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return;
            if (json[i] == '"')
            {
                int end = json.IndexOf('"', i + 1);
                if (end > i && ColorUtility.TryParseHtmlString(json.Substring(i + 1, end - i - 1), out var parsed))
                    c = parsed;
            }
            else if (json[i] == '[')
            {
                int end = json.IndexOf(']', i);
                if (end < 0) return;
                var parts = json.Substring(i + 1, end - i - 1).Split(',');
                var v = new float[4] { c.r, c.g, c.b, 1f };
                bool bytes = false;
                for (int p = 0; p < parts.Length && p < 4; p++)
                    if (float.TryParse(parts[p].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float f))
                    {
                        v[p] = f;
                        if (f > 1f) bytes = true;
                    }
                float s = bytes ? 1f / 255f : 1f;
                c = new Color(v[0] * s, v[1] * s, v[2] * s, parts.Length > 3 ? v[3] * s : 1f);
            }
        }

        public void Hide()
        {
            whipT = -1f;
            if (lr != null) lr.enabled = false;
        }

        /// **The line this frame.** `slackMetres` is how much longer the line
        /// is than the chord (0 = straight); `tension01` drives the band look.
        public void Draw(Vector3 from, Vector3 to, float tension01, float slackMetres)
        {
            whipT = -1f;
            float t = Mathf.Clamp01(tension01);
            float chord = Vector3.Distance(from, to);
            // Sag: a parabola whose drop gives the extra length (s ≈ 8d²/3L),
            // plus a little hang under any tension short of strain.
            float slackDrop = Mathf.Sqrt(Mathf.Max(0f, slackMetres) * chord * 3f / 8f);
            float hang = chord * 0.035f * (1f - Mathf.SmoothStep(0f, HarpoonTuning.strainBand, t));
            // A very slack line lies along the water instead of diving under
            // it: the low end is at the surface, so the drop is capped to keep
            // the middle a hand above it (at the surface itself the swell
            // swallowed it), and every point is floored there below.
            float low = Mathf.Min(from.y, to.y);
            float waterCap = Mathf.Max(0f, (from.y + to.y) * 0.5f - low - SlackLift);
            float sag = Mathf.Min(slackDrop + hang, Mathf.Min(chord * 0.35f, waterCap));

            bool strained = t >= HarpoonTuning.strainBand;
            float flutter = strained ? 0.04f * Mathf.InverseLerp(HarpoonTuning.strainBand, 1f, t) : 0f;
            Vector3 side = Vector3.Cross(to - from, Vector3.up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;

            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1);
                Vector3 p = Vector3.Lerp(from, to, u);
                float bow = 4f * u * (1f - u);
                p.y = Mathf.Max(p.y - sag * bow, low + SlackLift * bow);
                if (flutter > 0f)
                    p += side * (flutter * Mathf.Sin(u * Mathf.PI) * Mathf.Sin(Time.time * 47f + u * 9f));
                pts[i] = p;
            }
            Apply(t);
            if (strained) Creak(t);
        }

        /// **Snap or cut**: the free end recoils to the muzzle, thrashing.
        /// `violent` = a snap; a cut just falls back.
        public void Whip(Vector3 freeEnd, bool violent)
        {
            whipFrom = freeEnd;
            whipT = 0f;
            whipAmp = violent ? 1.2f : 0.35f;
        }

        public bool Whipping => whipT >= 0f;

        /// Advance the recoil; false once it is over (the line hides).
        public bool TickWhip(Vector3 muzzle, float dt)
        {
            if (whipT < 0f) return false;
            whipT += dt;
            float s = Mathf.Clamp01(whipT / WhipSeconds);
            if (s >= 1f) { Hide(); return false; }
            float ease = 1f - (1f - s) * (1f - s);
            Vector3 end = Vector3.Lerp(whipFrom, muzzle, ease);
            Vector3 side = Vector3.Cross(end - muzzle, Vector3.up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
            float amp = whipAmp * (1f - s);
            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1);
                Vector3 p = Vector3.Lerp(muzzle, end, u);
                float wave = Mathf.Sin(u * 7f - whipT * 30f) * u;
                p += side * (amp * wave) + Vector3.up * (amp * 0.5f * Mathf.Cos(u * 5f - whipT * 24f) * u);
                pts[i] = p;
            }
            Apply(whipAmp > 1f ? 1f : 0.3f);
            return true;
        }

        void Apply(float t)
        {
            float w; Color c;
            if (t < HarpoonTuning.tautBand)
            {
                float k = t / Mathf.Max(0.01f, HarpoonTuning.tautBand);
                w = Mathf.Lerp(slackWidth, tautWidth, k * 0.5f);
                c = Color.Lerp(slackColor, tautColor, k * 0.5f);
            }
            else if (t < HarpoonTuning.strainBand)
            {
                float k = Mathf.InverseLerp(HarpoonTuning.tautBand, HarpoonTuning.strainBand, t);
                w = Mathf.Lerp(tautWidth, strainedWidth, k * 0.3f);
                c = Color.Lerp(tautColor, strainedColor, k * 0.25f);
            }
            else
            {
                float k = Mathf.InverseLerp(HarpoonTuning.strainBand, 1f, t);
                w = strainedWidth;
                // The glow pulses as it nears the snap.
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * (8f + 10f * k));
                c = Color.Lerp(Color.Lerp(tautColor, strainedColor, 0.6f), strainedColor, k) * pulse;
                c.a = 1f;
            }
            lr.startWidth = lr.endWidth = w;
            lr.startColor = lr.endColor = c;
            lr.SetPositions(pts);
            lr.enabled = true;
        }

        // --- creak: a short stick-slip groan, synthesised like PaddleSound's
        // clips (no audio asset), only while strained, at most every so often.
        void Creak(float t)
        {
            if (!JuiceTuning.soundOn || Time.time < nextCreakAt) return;
            if (creakClip == null) creakClip = MakeCreak();
            if (creak == null)
            {
                creak = gameObject.AddComponent<AudioSource>();
                creak.playOnAwake = false;
                creak.spatialBlend = 0f;
                creak.dopplerLevel = 0f;
                creak.priority = 96;
            }
            float k = Mathf.InverseLerp(HarpoonTuning.strainBand, 1f, t);
            creak.pitch = Mathf.Lerp(0.9f, 1.25f, k) + Random.Range(-0.05f, 0.05f);
            creak.PlayOneShot(creakClip, Mathf.Lerp(0.25f, 0.5f, k));
            nextCreakAt = Time.time + Mathf.Lerp(1.1f, 0.45f, k);
        }

        static AudioClip MakeCreak()
        {
            const int rate = 44100;
            int n = Mathf.RoundToInt(rate * 0.32f);
            var data = new float[n];
            var rnd = new System.Random(7919);
            float phase = 0f, peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Sin(Mathf.PI * t / 0.32f);
                // A pulse train sliding 70 → 110 Hz: fibres catching and slipping.
                phase += (70f + 40f * t / 0.32f) / rate;
                float frac = phase - Mathf.Floor(phase);
                float pulse = Mathf.Exp(-frac * 18f);
                float grit = (float)(rnd.NextDouble() * 2.0 - 1.0) * 0.25f;
                data[i] = env * (pulse * (0.8f + grit) - 0.25f);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            for (int i = 0; i < n; i++) data[i] *= 0.8f / peak;
            var clip = AudioClip.Create("HarpoonCreak", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
