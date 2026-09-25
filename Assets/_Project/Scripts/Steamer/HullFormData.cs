using System;
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
        /// Sea water, kg/m^3. The one place it is written: HullFormBody's
        /// default and the shipyard's hydrostatics both read it.
        public const float SeaWaterDensity = 1025f;

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

        /// **The same hull reshaped per axis** (modular shipyard, 2026-09-24):
        /// `sL` along her length (z), `sB` across her beam (x), `sD` in her
        /// depth (y, keel to deck; the waterline stays at y = 0). Unlike
        /// `Scaled` this is not a similarity, so each field follows its own
        /// definition:
        ///
        ///   lwl, loa                          x sL        (lengths)
        ///   beam, beamOverGuards              x sB
        ///   draft, depth, kb, kg              x sD        (heights; kb/kg above the keel)
        ///   volume, massKg                    x sL sB sD  (mass = rho V, so she floats on her marks)
        ///   waterplane                        x sL sB
        ///   bm = I_T / V                      x sB^2 / sD (I_T ~ L B^3, V ~ L B D)
        ///   gm                                gm + dkb + dbm - dkg (its definition, kb + bm - kg)
        ///   lcbZ                              x sL
        ///   gyradiusRoll                      x sB        (roll gyradius ~ 0.4 B, ITTC rule of thumb)
        ///   gyradiusPitch, gyradiusYaw        x sL        (~ 0.25 L; HullFormBody's own fallbacks)
        ///   com, gunSockets, helm, rudder     (x sB, y sD, z sL)
        ///   wheelAxle                         (x sB, y UNCHANGED, z sL): the axle height sets the
        ///                                     wheel's dip, i.e. her thrust, which must not change
        ///   wheelRadius/Width/Floats/FloatDepth/DesignDip, rudderArea
        ///                                     UNCHANGED -- the wheel and the blade are fittings,
        ///                                     not hull; no thrust or handling bonus from shape
        ///   funnelTopY                        + (sD - 1)(depth - draft): the funnel stands on the
        ///                                     deck and its own height does not change
        ///   well.halfWidth                    UNCHANGED (set by the wheel); fwdZ/platformFwdZ x sL;
        ///                                     floorY/platformRise x sD
        ///   stations: z, dz x sL; keelY, deckY, y[] x sD; halfBreadth[] x sB;
        ///             area[] x sB sD; momentY[] (area x height) x sB sD^2
        ///
        /// Longitudinal inertia of the waterplane goes as sL^3 sB and the
        /// transverse as sL sB^3 automatically, because both are integrated
        /// from the station tables (`Rederive`, `HullFormBody`), not stored.
        /// `Reshaped(1, 1, 1)` is field-for-field identical to the source (every
        /// rule is a multiplication by exactly 1 or an addition of exactly 0).
        /// Fields the generator writes but this class does not declare (lcfZ,
        /// inertiaT/L, cb, cwp, cm, volumeFwd/Aft, flareRatio,
        /// forecastleBreakZ) are never read at runtime and are not carried.
        /// A deep copy; the source is untouched.
        public HullFormData Reshaped(float sL, float sB, float sD)
        {
            float vol = sL * sB * sD;
            var d = (HullFormData)MemberwiseClone();
            d.lwl *= sL; d.loa *= sL;
            d.beam *= sB; d.beamOverGuards *= sB;
            d.draft *= sD; d.depth *= sD;
            d.kb *= sD; d.kg *= sD;
            d.bm *= sB * sB / sD;
            d.gm = gm + (d.kb - kb) + (d.bm - bm) - (d.kg - kg);
            d.lcbZ *= sL;
            d.volume *= vol; d.massKg *= vol;
            d.waterplane *= sL * sB;
            d.gyradiusRoll *= sB; d.gyradiusPitch *= sL; d.gyradiusYaw *= sL;
            d.com = Axes(com, sB, sD, sL);
            d.helm = Axes(helm, sB, sD, sL);
            d.rudder = Axes(rudder, sB, sD, sL);
            d.wheelAxle = new Vector3(wheelAxle.x * sB, wheelAxle.y, wheelAxle.z * sL);
            d.funnelTopY = funnelTopY + (sD - 1f) * (depth - draft);
            if (gunSockets != null)
            {
                d.gunSockets = new Vector3[gunSockets.Length];
                for (int i = 0; i < gunSockets.Length; i++) d.gunSockets[i] = Axes(gunSockets[i], sB, sD, sL);
            }
            if (well != null)
                d.well = new HullFormWell
                {
                    halfWidth = well.halfWidth, fwdZ = well.fwdZ * sL, floorY = well.floorY * sD,
                    platformRise = well.platformRise * sD, platformFwdZ = well.platformFwdZ * sL,
                };
            if (stations != null)
            {
                d.stations = new HullFormStation[stations.Length];
                for (int i = 0; i < stations.Length; i++)
                {
                    var s = stations[i];
                    if (s == null) continue;
                    d.stations[i] = new HullFormStation
                    {
                        z = s.z * sL, dz = s.dz * sL, keelY = s.keelY * sD, deckY = s.deckY * sD,
                        y = Scale(s.y, sD), halfBreadth = Scale(s.halfBreadth, sB),
                        area = Scale(s.area, sB * sD), momentY = Scale(s.momentY, sB * sD * sD),
                    };
                }
            }
            return d;
        }

        static Vector3 Axes(Vector3 v, float sx, float sy, float sz) => new Vector3(v.x * sx, v.y * sy, v.z * sz);

        /// **Raised deck** (docs/RAISED-DECK.md sec 6, modular shipyard,
        /// 2026-09-25): a freeboard extension applied AFTER `Reshaped`, only
        /// for a raised hull family. Appends ONE new top level to every
        /// station's tables, `extraY` (m) above its OLD `deckY`, wall-sided
        /// (half-breadth held at the old deck's own value -- the between-deck
        /// is enclosed and does not flare), and raises `deckY` itself to the
        /// new top. `depth` grows by `extraY` too (the freeboard the hull
        /// now has). Nothing BELOW the old deck changes: buoyancy at or under
        /// the old waterline is bit-for-bit what `Reshaped` gave it.
        ///
        /// This is deliberately the SAME mechanism a real wall-sided topside
        /// would give the strip model: `HullFormBody`'s deck-immersion check
        /// (`over = level - deckY`) and crew-walking height both read the
        /// station's own `deckY`, so extending the table converts both at
        /// once -- the plan's one `walkDeckZU` value (docs/RAISED-DECK.md
        /// sec 7) is realized here, not threaded as a second field.
        /// `extraY <= 0` returns an unmodified deep copy (mirrors `Reshaped`'s
        /// identity contract at its own no-op input).
        public HullFormData RaiseDeck(float extraY)
        {
            var d = (HullFormData)MemberwiseClone();
            if (extraY <= 0f || stations == null) { if (stations != null) d.stations = (HullFormStation[])stations.Clone(); return d; }
            d.depth = depth + extraY;
            d.stations = new HullFormStation[stations.Length];
            for (int i = 0; i < stations.Length; i++)
            {
                var s = stations[i];
                if (s == null) continue;
                int n = s.y != null ? s.y.Length : 0;
                var t = new HullFormStation { z = s.z, dz = s.dz, keelY = s.keelY, deckY = s.deckY + extraY };
                if (n == 0) { d.stations[i] = t; continue; }
                float oldDeckY = s.y[n - 1];
                float hbDeck = s.halfBreadth[n - 1];
                float areaAdded = 2f * hbDeck * extraY;
                float newY = oldDeckY + extraY;
                t.y = new float[n + 1]; t.halfBreadth = new float[n + 1]; t.area = new float[n + 1]; t.momentY = new float[n + 1];
                Array.Copy(s.y, t.y, n); Array.Copy(s.halfBreadth, t.halfBreadth, n);
                Array.Copy(s.area, t.area, n); Array.Copy(s.momentY, t.momentY, n);
                t.y[n] = newY;
                t.halfBreadth[n] = hbDeck; // wall-sided: unchanged above the old deck
                t.area[n] = s.area[n - 1] + areaAdded;
                // Moment of the added rectangular strip about y = 0, its own
                // centroid at the strip's mid-height, added to the running
                // integral the old table already carried to oldDeckY.
                t.momentY[n] = s.momentY[n - 1] + areaAdded * (oldDeckY + extraY * 0.5f);
                d.stations[i] = t;
            }
            return d;
        }

        /// **The stern's own fittings ride with the stern.** The wheel, its
        /// well, the rudder and the helm belong to the stern module, which
        /// does not stretch: when a bay is added the whole stern moves aft by
        /// half the bay. So after `Reshaped` stretched their z with the hull,
        /// this puts each at `reference` z + `dz` (x and y stay as `Reshaped`
        /// made them). On the reference ship `dz = 0` and every z is the
        /// reference's own, exactly.
        public void PinSternFittings(HullFormData reference, float dz)
        {
            wheelAxle.z = reference.wheelAxle.z + dz;
            rudder.z = reference.rudder.z + dz;
            helm.z = reference.helm.z + dz;
            if (well != null && reference.well != null && !ReferenceEquals(well, reference.well))
            {
                well.fwdZ = reference.well.fwdZ + dz;
                well.platformFwdZ = reference.well.platformFwdZ + dz;
            }
        }

        /// Enclosed volume below the deck line of every station, m^3: the
        /// space a hold can use (full-section area at deck level x strip).
        public float VolumeBelowDeck()
        {
            float v = 0f;
            if (stations == null) return v;
            for (int i = 0; i < stations.Length; i++)
                if (stations[i] != null) v += AreaAt(i, stations[i].deckY) * stations[i].dz;
            return v;
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
