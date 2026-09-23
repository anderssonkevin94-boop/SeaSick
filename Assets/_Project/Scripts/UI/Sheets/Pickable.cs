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
            // The model's own middle, not the transform's: a fire's quad is
            // usually offset up from the pivot on the ground.
            if (BoundsOf(go, out var bounds))
            {
                col.radius = Mathf.Max(minRadius, Mathf.Max(bounds.extents.x, bounds.extents.z));
                col.center = go.transform.InverseTransformPoint(bounds.center);
            }
            else col.radius = minRadius;
            p.added = col;
        }

        /// **The whole model, not its first mesh, 2026-09-23.** This read
        /// `GetComponentInChildren<Renderer>()`, which on an authored
        /// building is whichever part the FBX happened to list first -- a
        /// sign, a beam -- so the forge's tap target was a 1.5 m ball
        /// hanging off one corner. Kevin, on the phone: *"tapping the
        /// building as a whole does nothing."*
        static bool BoundsOf(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// Sweep the four kinds of thing that are tappable and can be bare.
        /// Cheap enough at twice a second: these are tens of objects, not
        /// thousands, and only while she is lying at an island.
        public static void EnsureAll()
        {
            foreach (var f in Object.FindObjectsByType<World.Campfire>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // **Only the camp's fire, 2026-09-23.** Every building's
                // window lamp is a `Campfire` too (`BuildingFactory.Lamp`
                // borrows its flicker), and giving each one a tap sphere did
                // two wrong things at once: the lamp became a little button
                // that opened the CAMP sheet, and its collider made
                // `Ensure` below believe the building already had one, so
                // the building itself was never tappable.
                var owner = f.GetComponentInParent<World.Building>();
                if (owner != null && owner.Kind != World.BuildKind.Fire) continue;
                Ensure(f, 1.6f);
            }
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
