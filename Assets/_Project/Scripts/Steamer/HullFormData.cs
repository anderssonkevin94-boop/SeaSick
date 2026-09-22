using UnityEngine;

namespace SeaSick.Steamer
{
    /// One transverse section of the hull, as a table against height. The
    /// generator integrates the real section; the physics only ever lerps.
    /// Field names are the JSON keys (docs/steamer-spec.md §3) -- JsonUtility
    /// matches by name, so renaming one here silently reads zeros.
    [System.Serializable]
    public class HullFormStation
    {
        public float z, dz, keelY, deckY;
        public float[] y, halfBreadth, area, momentY;
    }

    /// The notch the stern wheel turns in, cut into her between two hull
    /// cheeks. Physics-visible because it reaches below the waterline: the
    /// generator has already taken it out of the station tables, and the one
    /// thing the runtime needs from it is where it starts, so the sea is not
    /// clipped away from under the wheel.
    [System.Serializable]
    public class HullFormWell
    {
        public float halfWidth, fwdZ, floorY, platformRise, platformFwdZ;
    }

    /// JsonUtility mirror of `Resources/Steamer/hullform.json`: the contract
    /// between the hull generator (Python/Blender) and the strip buoyancy.
    ///
    /// Ship-local frame: +Z bow, +Y up, +X starboard, origin on the centreline
    /// at the design waterline and the midpoint of the LWL. Every `y` in the
    /// tables is in that frame, so level 0 IS her marks.
    [System.Serializable]
    public class HullFormData
    {
        public const string ResourcePath = "Steamer/hullform";

        public float lwl, loa, beam, beamOverGuards, draft, depth, volume, massKg, waterplane, kb, bm, gm, kg, lcbZ;
        public float gyradiusRoll, gyradiusPitch, gyradiusYaw;
        public Vector3 com, wheelAxle, rudder, helm;
        public float wheelRadius, wheelWidth, wheelFloatDepth, wheelDesignDip, rudderArea, funnelTopY;
        public int wheelFloats;
        /// Where a gun stands, STARBOARD side, solved against her own
        /// planking by the hull generator. `CannonBattery.Fit` mirrors them.
        public Vector3[] gunSockets;
        /// The paddle well. Null on a hull that has none.
        public HullFormWell well;
        public HullFormStation[] stations;

        public int StationCount => stations != null ? stations.Length : 0;

        /// **The same hull at another size.** Every length goes as k, every
        /// area as k², every volume and mass as k³, and the section moment
        /// tables (area × height) as k³ -- so a scaled form floats on its own
        /// marks and rolls with its own period (which goes as √k) without
        /// the generator being run again. Kevin, 2026-09-22: *"the player
        /// ship is too big … proportionately to everything else."* A deep
        /// copy; the loaded asset is untouched.
        public HullFormData Scaled(float k)
        {
            float k2 = k * k, k3 = k2 * k;
            var d = (HullFormData)MemberwiseClone();
            d.lwl *= k; d.loa *= k; d.beam *= k; d.beamOverGuards *= k;
            d.draft *= k; d.depth *= k; d.kb *= k; d.bm *= k; d.gm *= k; d.kg *= k; d.lcbZ *= k;
            d.gyradiusRoll *= k; d.gyradiusPitch *= k; d.gyradiusYaw *= k;
            d.volume *= k3; d.massKg *= k3; d.waterplane *= k2; d.rudderArea *= k2;
            d.com = com * k; d.wheelAxle = wheelAxle * k; d.rudder = rudder * k; d.helm = helm * k;
            d.wheelRadius *= k; d.wheelWidth *= k; d.wheelFloatDepth *= k; d.wheelDesignDip *= k;
            d.funnelTopY *= k;
            if (gunSockets != null)
            {
                d.gunSockets = new Vector3[gunSockets.Length];
                for (int i = 0; i < gunSockets.Length; i++) d.gunSockets[i] = gunSockets[i] * k;
            }
            if (well != null)
                d.well = new HullFormWell
                {
                    halfWidth = well.halfWidth * k, fwdZ = well.fwdZ * k, floorY = well.floorY * k,
                    platformRise = well.platformRise * k, platformFwdZ = well.platformFwdZ * k,
                };
            if (stations != null)
            {
                d.stations = new HullFormStation[stations.Length];
                for (int i = 0; i < stations.Length; i++)
                {
                    var s = stations[i];
                    if (s == null) continue;
                    var t = new HullFormStation
                    {
                        z = s.z * k, dz = s.dz * k, keelY = s.keelY * k, deckY = s.deckY * k,
                        y = Scale(s.y, k), halfBreadth = Scale(s.halfBreadth, k),
                        area = Scale(s.area, k2), momentY = Scale(s.momentY, k3),
                    };
                    d.stations[i] = t;
                }
            }
            return d;
        }

        static float[] Scale(float[] a, float f)
        {
            if (a == null) return null;
            var r = new float[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] * f;
            return r;
        }

        /// Null, with an error in the log, if the generator has not run or
        /// wrote something the tables cannot be read from. Callers treat null
        /// as "no steamer", not as something to limp along with: a hull with
        /// no tables has no buoyancy at all.
        public static HullFormData Load()
        {
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null)
            {
                Debug.LogError("[Steamer] Resources/" + ResourcePath + ".json is missing -- run the hull generator.");
                return null;
            }
            HullFormData data = null;
            try { data = JsonUtility.FromJson<HullFormData>(text.text); }
            catch (System.Exception e)
            {
                Debug.LogError("[Steamer] hullform.json did not parse: " + e.Message);
                return null;
            }
            string why = null;
            if (data == null || !data.Validate(out why))
            {
                Debug.LogError("[Steamer] hullform.json is unusable: " + (data == null ? "empty" : why));
                return null;
            }
            return data;
        }

        /// Structural check only -- whether the tables can be indexed without
        /// throwing. Whether the NUMBERS are right is `Rederive`'s job.
        public bool Validate(out string why)
        {
            why = null;
            if (stations == null || stations.Length < 3) { why = "fewer than 3 stations"; return false; }
            for (int i = 0; i < stations.Length; i++)
            {
                var s = stations[i];
                if (s == null || s.y == null || s.y.Length < 2) { why = "station " + i + " has no levels"; return false; }
                int n = s.y.Length;
                if (s.halfBreadth == null || s.halfBreadth.Length != n ||
                    s.area == null || s.area.Length != n ||
                    s.momentY == null || s.momentY.Length != n)
                { why = "station " + i + " tables differ in length"; return false; }
                if (s.dz <= 0f) { why = "station " + i + " has dz <= 0"; return false; }
                for (int k = 1; k < n; k++)
                    if (s.y[k] <= s.y[k - 1]) { why = "station " + i + " levels are not ascending"; return false; }
            }
            if (volume <= 0f) { why = "volume <= 0"; return false; }
            return true;
        }

        // ------------------------------------------------------------------
        // Table lookups. Levels ascend but need not be uniform (the generator
        // is free to crowd them around the waterline), so this is a search,
        // not an index computation. 17 levels: a linear scan beats a binary
        // search and is branch-predictable, since neighbouring calls land in
        // the same segment.

        /// Segment index k and lerp weight t such that level sits between
        /// y[k] and y[k+1]. Clamped at both ends: below the keel everything
        /// is what it is at the keel (nothing), above the last level
        /// everything is what it is at the top (the deck is watertight).
        static void Locate(float[] y, float level, out int k, out float t)
        {
            int last = y.Length - 1;
            if (!(level > y[0])) { k = 0; t = 0f; return; }        // also catches NaN
            if (level >= y[last]) { k = last - 1; t = 1f; return; }
            k = 0;
            while (k < last - 1 && level >= y[k + 1]) k++;
            t = (level - y[k]) / (y[k + 1] - y[k]);
        }

        /// FULL-section immersed area (both sides) below `level`, m^2.
        public float AreaAt(int station, float level)
        {
            var s = stations[station];
            if (!(level > s.keelY)) return 0f;
            Locate(s.y, level, out int k, out float t);
            return Mathf.Max(0f, Mathf.Lerp(s.area[k], s.area[k + 1], t));
        }

        /// Height of the immersed section's centroid. Moment and area are
        /// lerped separately and THEN divided: lerping the centroid itself is
        /// wrong between levels, because the centroid is a ratio and both
        /// halves of it move.
        public float CentroidYAt(int station, float level)
        {
            var s = stations[station];
            if (!(level > s.keelY)) return s.keelY;
            Locate(s.y, level, out int k, out float t);
            float a = Mathf.Lerp(s.area[k], s.area[k + 1], t);
            // A sliver of area right at the keel: the ratio is 0/0 there and
            // the answer is the keel itself.
            if (a < 1e-4f) return s.keelY;
            float m = Mathf.Lerp(s.momentY[k], s.momentY[k + 1], t);
            // Cannot leave the section, whatever a rounding in the table says.
            return Mathf.Clamp(m / a, s.keelY, Mathf.Max(s.keelY, level));
        }

        public float HalfBreadthAt(int station, float level)
        {
            var s = stations[station];
            Locate(s.y, level, out int k, out float t);
            return Mathf.Max(0f, Mathf.Lerp(s.halfBreadth[k], s.halfBreadth[k + 1], t));
        }

        /// The book values, integrated again from the station tables at
        /// level 0 -- the same sum the generator did, so any disagreement is
        /// a bug on one side of the contract rather than a rounding.
        ///
        /// `kb` is above the KEEL (draft + centroid height, the centroid being
        /// negative in this frame). `longitudinalI` is the waterplane's second
        /// moment about its own centre of flotation, each strip's own
        /// dz^2/12 included -- the naval-architecture number. (The strip
        /// model's DELIVERED pitch stiffness is a little different -- point
        /// forces, taken about the CoM -- and `HullFormBody` sums that one
        /// itself.)
        public void Rederive(out float volume, out float waterplane, out float kb, out float bm,
                             out float lcbZ, out float longitudinalI)
        {
            volume = 0f; waterplane = 0f; kb = 0f; bm = 0f; lcbZ = 0f; longitudinalI = 0f;
            if (stations == null) return;

            float momentY = 0f, momentZ = 0f, transverseI = 0f, wpMomentZ = 0f;
            for (int i = 0; i < stations.Length; i++)
            {
                var s = stations[i];
                Locate(s.y, 0f, out int k, out float t);
                bool wet = 0f > s.keelY;
                float a = wet ? Mathf.Max(0f, Mathf.Lerp(s.area[k], s.area[k + 1], t)) : 0f;
                float m = wet ? Mathf.Lerp(s.momentY[k], s.momentY[k + 1], t) : 0f;
                float hb = wet ? Mathf.Max(0f, Mathf.Lerp(s.halfBreadth[k], s.halfBreadth[k + 1], t)) : 0f;

                volume += a * s.dz;
                momentY += m * s.dz;
                momentZ += a * s.dz * s.z;
                float wp = 2f * hb * s.dz;
                waterplane += wp;
                wpMomentZ += wp * s.z;
                // Wall-sided strip about the centreline: (2 hb)^3 dz / 12.
                transverseI += (2f / 3f) * hb * hb * hb * s.dz;
            }
            if (volume <= 1e-6f) return;

            kb = draft + momentY / volume;
            bm = transverseI / volume;
            lcbZ = momentZ / volume;

            float lcf = waterplane > 1e-6f ? wpMomentZ / waterplane : 0f;
            for (int i = 0; i < stations.Length; i++)
            {
                var s = stations[i];
                float wp = 2f * (0f > s.keelY ? HalfBreadthAt(i, 0f) : 0f) * s.dz;
                float arm = s.z - lcf;
                longitudinalI += wp * (arm * arm + s.dz * s.dz / 12f);
            }
        }
    }
}
