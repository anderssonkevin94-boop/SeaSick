using UnityEngine;

namespace SeaSick.World
{
    /// **What a pier knows that a dock needs.** Sits on the pier's root
    /// beside its `Building`, and answers three questions in world space:
    /// where the sea end is, which way the planks run (land to sea), and
    /// where a hull should lie alongside. The ship never reads this
    /// directly -- the dock registry does, through `Outpost.RegisterPierDock`.
    ///
    /// Local frame, set by `BuildingFactory`: the pier runs along local +X
    /// from the land end at -length/2 to the sea end at +length/2, the deck
    /// is at local y = 0 (world `BuildPlans.PierDeck`), and width is local Z.
    public class Pier : MonoBehaviour
    {
        [SerializeField] float length;
        [SerializeField] float width;

        /// How far off the pier's side the berth is, metres: a hull's half
        /// beam and a fender's worth of water.
        public const float BerthOffset = 4f;

        public float Length => length;
        public float Width => width;

        public void Configure(BuildPlan plan)
        {
            length = plan.footprint.x;
            width = plan.footprint.y;
        }

        /// Land end of the deck, world, at deck height.
        public Vector3 LandEnd => transform.TransformPoint(new Vector3(-length * 0.5f, 0f, 0f));

        /// Sea end of the deck, world, at deck height.
        public Vector3 SeaEnd => transform.TransformPoint(new Vector3(length * 0.5f, 0f, 0f));

        /// Which way the planks run, land to sea. Flat.
        public Vector3 HeadingDir
        {
            get
            {
                Vector3 d = transform.right;
                d.y = 0f;
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            }
        }

        /// Land-to-sea heading as a rotation whose forward is `HeadingDir`.
        public Quaternion Heading => Quaternion.LookRotation(HeadingDir, Vector3.up);

        /// **Where she lies alongside**: `BerthOffset` metres off the pier's
        /// RIGHT side (right when walking out to sea), level with the
        /// seaward third of the deck, at mean water. A hull bowsed in here
        /// with her bow on `Heading` has her port side to the planks.
        public Vector3 Berth
        {
            get
            {
                Vector3 along = transform.TransformPoint(new Vector3(length / 6f, 0f, 0f));
                Vector3 right = Heading * Vector3.right;
                Vector3 b = along + right * (width * 0.5f + BerthOffset);
                b.y = 0f;
                return b;
            }
        }

        /// True once the dock registry has been told about this pier. Set by
        /// `Outpost.Raise` and read by `OnDestroy`, so a ghost (which is
        /// built by the same factory but never raised) says nothing.
        public bool DockRegistered { get; private set; }

        Building building;

        /// Hand this pier to the dock registry, once. See `Outpost.RegisterPierDock`.
        public void Register(Building b)
        {
            if (DockRegistered) return;
            building = b;
            DockRegistered = true;
            Outpost.RegisterPierDock?.Invoke(b);
        }

        /// Every path that tears a pier down comes through here -- `Adopt`
        /// destroying what stood before a load, a scene unload -- so the
        /// registry hears about it exactly once, and never about a ghost.
        void OnDestroy()
        {
            if (!DockRegistered) return;
            DockRegistered = false;
            Outpost.UnregisterPierDock?.Invoke(building);
        }
    }
}
