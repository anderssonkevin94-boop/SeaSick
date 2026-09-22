using System;
using UnityEngine;

namespace SeaSick.Ship
{
    /// The twenty rungs of the modular ladder, as data.
    ///
    /// Written by `seasick_bays.py` from the SAME parameter dicts the hull
    /// meshes were lofted from, so the game cannot disagree with the geometry
    /// about how long a hull is or how many bays she has. Nothing in here is
    /// typed by hand -- see [Art/Ship/Hulls/ladder.json].
    ///
    /// Displacement is MEASURED off each hull's own stations, not estimated.
    /// The four rungs that land on an authored hull reproduce her volume to
    /// two decimals (5.20 / 45.13 / 269.56 / 1880.80 m3), which is the check
    /// that says the interpolated rungs between them can be trusted.
    [Serializable]
    public class LadderNode
    {
        public int node;
        public string name, label, move, mesh;
        public float length, beam, depth, draft, loa_over_beam, probe_lift;
        public float volume_m3, mass_kg;

        /// Hydrostatics, integrated over the same station rings the mesh was
        /// lofted from — so they cannot disagree with the hull.
        ///
        /// `tpc` is tonnes per centimetre immersion and it is the whole reason
        /// a small ship feels every barrel: it runs from 0.18 on the skiff to
        /// 4.43 on the three-decker, so the same load sinks the skiff
        /// twenty-five times deeper.
        ///
        /// `km` is KB + BM, the metacentre above the keel. Subtract the centre
        /// of gravity and you have GM, which decides how hard she is to heel
        /// and how slowly she rolls. Note what GIRDLING does to BM — it goes as
        /// the cube of beam, so widening her is a stability upgrade, which is
        /// exactly why real ships that proved crank were girdled.
        public float waterplane_m2, tpc_t_per_cm;
        public float kb_above_keel_m, bm_m, km_above_keel_m;
        public float[] volume_curve_z, volume_curve_v;

        /// Displaced volume at a given height of the waterline above her drawn
        /// one, by interpolation on the measured curve. Sinkage is NOT linear:
        /// a hull's waterplane widens as she settles, so the tenth centimetre
        /// of immersion buys more than the first.
        public float VolumeAt(float waterlineOffset)
        {
            if (volume_curve_z == null || volume_curve_z.Length < 2) return volume_m3;
            float z = Mathf.Clamp(waterlineOffset,
                                  volume_curve_z[0], volume_curve_z[^1]);
            for (int i = 0; i < volume_curve_z.Length - 1; i++)
            {
                if (z < volume_curve_z[i] || z > volume_curve_z[i + 1]) continue;
                float span = volume_curve_z[i + 1] - volume_curve_z[i];
                float t = span > 1e-6f ? (z - volume_curve_z[i]) / span : 0f;
                return Mathf.Lerp(volume_curve_v[i], volume_curve_v[i + 1], t);
            }
            return volume_m3;
        }

        /// Where the waterline sits for a given mass, in metres relative to her
        /// drawn line. Negative is riding high, positive is sunk past it.
        public float WaterlineForMass(float massKg)
        {
            float wantVol = massKg / 1025f;
            if (volume_curve_z == null || volume_curve_z.Length < 2)
                return (massKg - mass_kg) / Mathf.Max(1f, tpc_t_per_cm * 1000f) * 0.01f;
            for (int i = 0; i < volume_curve_v.Length - 1; i++)
            {
                if (wantVol < volume_curve_v[i] || wantVol > volume_curve_v[i + 1])
                    continue;
                float span = volume_curve_v[i + 1] - volume_curve_v[i];
                float t = span > 1e-6f ? (wantVol - volume_curve_v[i]) / span : 0f;
                return Mathf.Lerp(volume_curve_z[i], volume_curve_z[i + 1], t);
            }
            return wantVol > volume_curve_v[^1] ? volume_curve_z[^1] : volume_curve_z[0];
        }
        public int bays, tiers, cells, gun_rows, ports_per_side, masts;

        /// Gun ports cut in each gun deck, LOWEST first. A hull carries far
        /// fewer ports than bays — the generator cuts one per gun she can
        /// actually man — so this, not `bays`, is what limits her battery.
        public int[] ports_per_row;
        public string[] bay_labels, tier_names;
        public float[] bay_x, tier_floor, tier_ceiling;

        /// Resources path of the hull mesh, without the extension.
        public string ResourcePath => "Ladder/" + mesh.Replace(".fbx", "");

        /// Where the buoyancy probes' keel sits: the measured rule of
        /// 0.55 x draft above the DRAWN keel, one ratio across a 19x range of
        /// hull and the same one the paddle steamer needed.
        public float ProbeKeelY => -draft + probe_lift;
        public float RailY => depth - draft;

        /// The mean of her bay stations. Her bays are NOT numbered
        /// symmetrically about the origin — the loft rakes her stations, so
        /// the brig's run -8.6 to +9.4 — and without this an evenly stowed
        /// ship would read as permanently down by the stern.
        public float BayMeanX
        {
            get
            {
                if (bay_x == null || bay_x.Length == 0) return 0f;
                float s = 0f;
                foreach (var x in bay_x) s += x;
                return s / bay_x.Length;
            }
        }

        /// Height of a tier's deck above the KEEL. `tier_floor` is measured
        /// from her drawn waterline, and everything about weight is measured
        /// from the keel, so this conversion had been written out by hand at
        /// every call site that needed it.
        public float TierHeightAboveKeel(int ti) =>
            (tier_floor != null && ti >= 0 && ti < tier_floor.Length)
                ? tier_floor[ti] + draft : 0f;
    }

    [Serializable]
    public class LadderData
    {
        public float deck_pitch, bay_pitch, bay_divisor;
        public LadderNode[] nodes;
    }

    public static class ShipLadder
    {
        const string Path = "Ladder/ladder";
        static LadderData data;

        public static LadderData Data
        {
            get
            {
                if (data != null) return data;
                var txt = Resources.Load<TextAsset>("Ships/FleetV3/ladder");
                if (txt == null) txt = Resources.Load<TextAsset>(Path);
                if (txt == null)
                {
                    Debug.LogError($"ShipLadder: no manifest at Resources/{Path}. "
                                   + "Run seasick_bays.write_manifest() in Blender "
                                   + "and copy ladder.json to Resources/Ladder/ladder.txt.");
                    return null;
                }
                data = JsonUtility.FromJson<LadderData>(txt.text);
                return data;
            }
        }

        public static int Count => Data != null ? Data.nodes.Length : 0;

        public static LadderNode Node(int i)
        {
            var d = Data;
            if (d == null || i < 0 || i >= d.nodes.Length) return null;
            return d.nodes[i];
        }

        // --- the corridor ---------------------------------------------------
        // Every authored hull sits between L/B 3.10 and 3.54. Holding a
        // corridor of 3.0 to 4.3 is what makes the ladder zigzag: two
        // lengthenings take her to the slender wall and force a girdling,
        // which resets her and buys two more. The rule is real naval
        // architecture and it writes the pacing for free.
        public const float SlenderLimit = 4.30f;
        public const float BeamyLimit = 3.00f;

        /// Why a move is not available from this rung, or null if it is.
        /// Returns the reason so the yard can SAY it rather than greying a
        /// button out and leaving the player to guess.
        public static string Blocked(int from, string move)
        {
            var a = Node(from);
            if (a == null) return "no such hull";
            var b = Node(from + 1);
            if (b == null) return "she is as big as the yard can build";
            if (b.move != move)
            {
                float ratio = a.loa_over_beam;
                if (move == "lengthen")
                    return $"L/B is already {ratio:F2}; she would pass "
                           + $"{SlenderLimit:F1} and hog. Girdle her first.";
                if (move == "girdle")
                    return $"L/B is {ratio:F2}; girdling now would take her "
                           + $"under {BeamyLimit:F1} and she would be a barge.";
                return $"her scantlings will not carry another deck yet.";
            }
            return null;
        }
    }
}
