using UnityEngine;

namespace SeaSick.Terrain
{
    /// Tuning for populating the procedural archipelago with gameplay:
    /// island discovery, resources, reefs, monsters, raiders. Lives in an
    /// asset so the defaults are real (see OceanQuality for the rationale).
    [CreateAssetMenu(menuName = "SeaSick/World Settings", fileName = "WorldSettings")]
    public class WorldSettings : ScriptableObject
    {
        [System.Serializable]
        public class ResourceKind
        {
            public string name;
            public Color beaconColor;
            [Range(0f, 1f), Tooltip("Unlocked from this fraction of discoveryRadius outward.")] public float minRing;
        }

        [Header("Island discovery")]
        [Tooltip("Islands are discovered within this distance of home, metres.")]
        public float discoveryRadius = 3000f;
        [Tooltip("Scan cell size, metres. Smaller finds smaller islets but costs more at startup.")]
        public float scanCell = 24f;
        [Tooltip("Land area below which a component is ignored (a rock, not an island), m².")]
        public float minIslandArea = 3000f;
        [Tooltip("Islands with a mean shoreline radius below this are shelter only — no resources, no raiders.")]
        public float shelterOnlyBelowRadius = 60f;
        [Tooltip("Max rise per metre over the first 12 m inland for a bearing to count as a beach.")]
        public float beachMaxSlope = 0.7f;

        [Header("Resources")]
        public ResourceKind[] kinds =
        {
            new ResourceKind { name = "Timber", beaconColor = new Color(0.55f, 0.85f, 0.4f),  minRing = 0f },
            new ResourceKind { name = "Stone",  beaconColor = new Color(0.75f, 0.78f, 0.85f), minRing = 0.3f },
            new ResourceKind { name = "Ore",    beaconColor = new Color(1f, 0.85f, 0.35f),    minRing = 0.55f },
            new ResourceKind { name = "Spice",  beaconColor = new Color(0.95f, 0.45f, 0.75f), minRing = 0.78f },
        };
        [Tooltip("Props per metre of mean island radius (clamped 5..26). One prop = one unit.")]
        public float propsPerRadius = 0.13f;

        [Header("Reefs")]
        public int reefCount = 24;
        public Vector2 reefRadiusRange = new Vector2(6f, 13f);
        public float reefMinDistance = 220f;
        public float reefMaxDistance = 1200f;
        [Tooltip("Reefs only in water at least this deep, metres.")]
        public float reefMinDepth = 4f;

        [Header("Sea monsters")]
        public int monsterCount = 3;
        public float monsterDistance = 260f;
        public float monsterBearingDeg = 0f;
        public float monsterSpreadDeg = 26f;

        [Header("Raiders")]
        public int raidersPerIsland = 1;
        public int maxRaiders = 8;
        public float raiderMinIslandRadius = 60f;
        public float patrolClearance = 78f;

        [Tooltip("0 = random each run; otherwise deterministic placement of reefs/props.")]
        public int seed = 0;
    }
}
