using UnityEngine;

namespace SeaSick.Ship
{
    /// Approved fleet artwork. All positions are ship-local metres, bow +Z.
    public sealed class FleetVisual : MonoBehaviour
    {
        public int stage;
        public Transform[] gunTemplates;
        public Transform[] sailPivots;
        public Vector3 helm;
        public float freeboard, length;
        public static FleetVisual Build(Transform parent, int node)
        {
            var prefab = Resources.Load<FleetVisual>($"Ships/FleetV3/Ship{node + 1:00}");
            if (!prefab) return null;
            // Opt-in art playtest affects the player's ship, not the enemy fleet.
            if (parent.GetComponent<Shipyard>() != null)
            {
                var replacement = AstraSteamerVisual.Build(parent, node, prefab);
                if (replacement != null) return replacement;
            }
            var visual = Instantiate(prefab, parent, false);
            visual.name = "HullVisual";
            foreach (var gun in visual.gunTemplates) gun.gameObject.SetActive(false);
            return visual;
        }
        public float DeckHeight(float z)
        {
            var steamer = GetComponent<AstraSteamerVisual>();
            if (steamer != null) return steamer.DeckHeight(z);
            if (stage <= 3) return freeboard * .5f + .12f;
            if (stage >= 7 && z < -length * .25f) return helm.y;
            float t = Mathf.Clamp01((z - length * .22f) / (length * .28f));
            return freeboard + .27f * Mathf.Pow(Mathf.Abs(z) / (length * .5f), 2)
                + (stage < 7 ? .12f : .32f) * Mathf.Pow(Mathf.Max(0, -z / (length * .5f)), 3)
                + (.60f + length * .032f) * t * t + .03f;
        }
    }
}
