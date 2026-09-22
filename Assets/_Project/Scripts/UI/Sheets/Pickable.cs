using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **A thing the finger can actually land on.**
    ///
    /// The sheet HUD's whole premise is that the OBJECT is the button, and a
    /// mesh with no collider is not a button however obviously it is a
    /// campfire. Several of the things the player must be able to tap are
    /// exactly that: the fire is a `Light` with a mesh under it, a build site
    /// is a decal and some posts.
    ///
    /// Rather than make every world factory remember to add one — which is a
    /// rule that holds until the next factory — the host sweeps for pickables
    /// without a collider twice a second and drops a trigger sphere on them.
    /// A trigger, so nothing in the physics world is changed by being
    /// tappable; the picker's raycast is the only thing that looks at it.
    [DisallowMultipleComponent]
    public class Pickable : MonoBehaviour
    {
        /// Added by `Ensure` and left alone afterwards, so a later real
        /// collider on the prefab wins without this one being removed twice.
        SphereCollider added;

        /// Give this object a tap target if it has none of its own.
        public static void Ensure(Component c, float minRadius = 1.2f)
        {
            if (c == null) return;
            var go = c.gameObject;
            if (go.GetComponent<Pickable>() != null) return;
            if (go.GetComponentInChildren<Collider>(true) != null) return;

            var p = go.AddComponent<Pickable>();
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = Mathf.Max(minRadius, RadiusOf(go));
            // The mesh's own middle, not the transform's: a fire's quad is
            // usually offset up from the pivot on the ground.
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null)
                col.center = go.transform.InverseTransformPoint(rend.bounds.center);
            p.added = col;
        }

        static float RadiusOf(GameObject go)
        {
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend == null) return 0f;
            var e = rend.bounds.extents;
            return Mathf.Max(e.x, e.z);
        }

        /// Sweep the four kinds of thing that are tappable and can be bare.
        /// Cheap enough at twice a second: these are tens of objects, not
        /// thousands, and only while she is lying at an island.
        public static void EnsureAll()
        {
            foreach (var f in Object.FindObjectsByType<World.Campfire>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Ensure(f, 1.6f);
            foreach (var b in Object.FindObjectsByType<World.BuildSite>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Ensure(b, 2.0f);
            foreach (var b in Object.FindObjectsByType<World.Building>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Ensure(b, 1.5f);
            foreach (var d in Object.FindObjectsByType<World.Dock>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Ensure(d, 3.0f);
        }

        void OnDestroy()
        {
            if (added != null) Destroy(added);
        }
    }
}
