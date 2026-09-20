using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What the camp has gathered, stacked on the ground beside the fire.**
    ///
    /// Kevin, 2026-09-19: *"they will pile them close to the campfire."* So
    /// the stores are not a number in a sheet — they are a thing you fly over
    /// and read. Four stacks beside a fire is a working camp; one stack and
    /// three empty spaces is a camp that has run out of something.
    ///
    /// Drawn FROM the ledger and owning nothing, exactly like `BuildSite` and
    /// the parked crew. A camp three kilometres astern has no piles in the
    /// scene and has lost nothing by it.
    public class CampPiles : MonoBehaviour
    {
        Outpost outpost;

        /// Where the ring of stacks sits, metres from the fire. Outside the
        /// crew's own ring (`Outpost.FireRingRadius`, 2.9 m) so that people
        /// and goods do not stand in each other.
        const float Radius = 5.2f;

        /// Most units drawn in one stack. The ceiling starts at ten and a
        /// store hut takes it to thirty; past a dozen the stack stops being
        /// countable anyway, so it grows in height and then stops.
        const int MaxDrawn = 12;

        readonly Dictionary<string, Transform> stacks = new Dictionary<string, Transform>();
        readonly Dictionary<string, int> drawn = new Dictionary<string, int>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static CampPiles EnsureOn(Outpost owner)
        {
            if (owner == null) return null;
            var found = owner.GetComponentInChildren<CampPiles>(true);
            if (found != null) return found;

            var go = new GameObject("CampPiles");
            go.transform.SetParent(owner.transform, false);
            var piles = go.AddComponent<CampPiles>();
            piles.outpost = owner;
            return piles;
        }

        void LateUpdate()
        {
            if (outpost == null || outpost.Ledger == null) return;
            // Nothing is kept anywhere until the fire is lit, so there is
            // nothing to draw and no place to draw it round.
            if (!outpost.HasCamp) return;
            Refresh();
        }

        /// Bring the stacks up to what the ledger says. Cheap when nothing has
        /// changed: a stack is rebuilt only when its whole-unit count moves.
        public void Refresh()
        {
            var l = outpost.Ledger;
            Vector3 fire = outpost.CampCentre;

            // A stable order, so a stack does not hop round the fire when a
            // new resource appears. `stores` is append-only in practice, but
            // "in practice" is how a camp ends up rearranging itself on
            // arrival, so the angle is taken from the resource NAME.
            foreach (var s in l.stores)
            {
                if (s == null || string.IsNullOrEmpty(s.resource)) continue;
                if (drawn.TryGetValue(s.resource, out int was) && was == s.whole) continue;
                drawn[s.resource] = s.whole;
                Rebuild(s.resource, s.whole, fire);
            }
        }

        void Rebuild(string resource, int count, Vector3 fire)
        {
            if (!stacks.TryGetValue(resource, out var stack) || stack == null)
            {
                var go = new GameObject("Pile_" + resource);
                go.transform.SetParent(transform, false);
                stack = go.transform;
                stacks[resource] = stack;
            }

            // Angle from the name: the same resource lands in the same place
            // at every camp, which is what lets a player read a camp from the
            // air without looking anything up.
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            Vector3 at = fire + new Vector3(Mathf.Cos(a) * Radius, 0f, Mathf.Sin(a) * Radius);
            at.y = outpost.GroundAt(at);
            stack.position = at;
            stack.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);

            for (int i = stack.childCount - 1; i >= 0; i--)
                Destroy(stack.GetChild(i).gameObject);
            if (count <= 0) return;

            var mat = MatFor(resource);
            int n = Mathf.Min(count, MaxDrawn);
            bool logs = resource == Res.Timber || resource == Res.Boards;

            for (int i = 0; i < n; i++)
            {
                int row = i / 3, col = i % 3;
                var go = GameObject.CreatePrimitive(
                    logs ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                var c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
                go.transform.SetParent(stack, false);

                if (logs)
                {
                    // Cross-piled, the way timber is actually stacked.
                    bool across = row % 2 == 1;
                    go.transform.localScale = new Vector3(0.24f, 0.8f, 0.24f);
                    go.transform.localRotation = Quaternion.Euler(
                        across ? 90f : 0f, across ? 0f : 90f, 0f);
                    go.transform.localPosition = new Vector3(
                        across ? (col - 1) * 0.32f : 0f,
                        0.13f + row * 0.26f,
                        across ? 0f : (col - 1) * 0.32f);
                }
                else
                {
                    // Sacks and rubble: a heap, not masonry.
                    go.transform.localScale = new Vector3(0.44f, 0.34f, 0.44f);
                    go.transform.localRotation = Quaternion.Euler(
                        0f, (i * 37) % 360, 0f);
                    go.transform.localPosition = new Vector3(
                        (col - 1) * 0.42f, 0.17f + row * 0.3f,
                        ((i % 5) - 2) * 0.09f);
                }
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static Material MatFor(string resource)
        {
            if (mats.TryGetValue(resource, out var m) && m != null) return m;
            m = new Material(Shader.Find(
                WorldArtStyle.Instance != null
                    ? "SeaSick/Environment Toon"
                    : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Res.Colour(resource));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            mats[resource] = m;
            return m;
        }
    }
}
