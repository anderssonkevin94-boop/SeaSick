using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What a dry dock knows that the shipyard needs.** Sits on the dry
    /// dock's root beside its `Building`, the way `Pier` sits on a pier's --
    /// same local frame, set by `BuildingFactory`: the slip runs along local
    /// +X from the land end at -length/2 to the open (sea) end at
    /// +length/2, the walkway is at local y = 0
    /// (`BuildPlans.DryDockDeck` above mean water), and the width is local Z.
    ///
    /// Named `DryDockSlip` rather than `DryDock` on purpose --
    /// `SeaSick.Ship.Modular.DryDock` already names the PER-SHIP equipment
    /// store a refit takes parts off into (`ShipyardService.Dock`); this is
    /// the unrelated WORLD BUILDING, and the two must never be confused by a
    /// `using` statement picking the wrong one.
    public class DryDockSlip : MonoBehaviour
    {
        [SerializeField] float length;
        [SerializeField] float width;

        public float Length => length;
        public float Width => width;

        public void Configure(BuildPlan plan)
        {
            length = plan.footprint.x;
            width = plan.footprint.y;
        }

        /// Land end of the walkway, world, at deck height.
        public Vector3 LandEnd => transform.TransformPoint(new Vector3(-length * 0.5f, 0f, 0f));

        /// Open (sea) end, world, at deck height.
        public Vector3 SeaEnd => transform.TransformPoint(new Vector3(length * 0.5f, 0f, 0f));

        /// Land-to-sea heading, flat.
        public Vector3 HeadingDir
        {
            get
            {
                Vector3 d = transform.right;
                d.y = 0f;
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            }
        }

        public Quaternion Heading => Quaternion.LookRotation(HeadingDir, Vector3.up);

        /// **Where the hull rests, on her keel pads** (the art contract,
        /// `art-staging/drydock-slip-v1/manifest.json`): dead centre of the
        /// slip along its length, on the channel centreline, at
        /// `BuildPlans.DryDockKeelAboveDeck` above the walkway -- the pads
        /// sit ABOVE the walkway, as a real cradle does (see
        /// `BuildPlans.DryDockDeck`'s comment). A static preview has no
        /// particular ship length to centre exactly between the sea
        /// entrance and the head gantry, so this is the slip's own middle
        /// rather than a per-ship computation; `PreviewAnchor`'s heading
        /// faces the bow toward the head end, clear of the open sea
        /// entrance, which is the more important of the two to get right
        /// for a static shot.
        public Vector3 ShipCenter =>
            transform.TransformPoint(new Vector3(0f, BuildPlans.DryDockKeelAboveDeck, 0f));

        /// A world-space anchor (position + rotation, at `ShipCenter`) for
        /// whatever stages a ship model here. Faces the BOW toward the land
        /// (head) end and the stern toward the sea entrance -- the opposite
        /// sense from `Heading`, which points `HeadingDir` (land to sea) the
        /// way a pier's does. A plain child transform rather than raw
        /// numbers, so a caller can parent under it and inherit both without
        /// recomputing.
        public Transform PreviewAnchor
        {
            get
            {
                if (previewAnchor == null)
                {
                    var go = new GameObject("ShipCenter (placeholder)");
                    go.transform.SetParent(transform, false);
                    previewAnchor = go.transform;
                }
                Quaternion bowToLand = Quaternion.LookRotation(-HeadingDir, Vector3.up);
                previewAnchor.SetPositionAndRotation(ShipCenter, bowToLand);
                return previewAnchor;
            }
        }
        Transform previewAnchor;

        /// True once the registry has been told about this dry dock. Set by
        /// `Outpost.Raise` and read by `OnDestroy`, so a ghost (built by the
        /// same factory but never raised) says nothing.
        public bool Registered { get; private set; }

        Building building;
        Island island;

        /// **Every dry dock standing anywhere**, mirroring `Dock.All`. A
        /// camp's dry dock registers on `Outpost.Raise` and leaves on
        /// destroy, so tearing one down (a load re-raising a ledger, a
        /// probe cleaning up) removes it from every reader at once.
        public static readonly List<DryDockSlip> All = new List<DryDockSlip>();

        /// Hand this dry dock to the registry, once, with the outpost that
        /// raised it (so `HomeSlip` can ask which island it is on without a
        /// spatial lookup).
        public void Register(Building b, Outpost outpost)
        {
            if (Registered) return;
            building = b;
            island = outpost != null ? outpost.Island : null;
            Registered = true;
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDestroy()
        {
            if (!Registered) return;
            Registered = false;
            All.Remove(this);
        }

        /// **The dry dock on the home berth's island**, or null. This is the
        /// one `ShipyardService.CanRefitNow` and the shipyard preview care
        /// about -- a dry dock built anywhere else does not open the
        /// shipyard, the same way a pier anywhere else is not the home
        /// berth. Resolved from `Dock.Home`'s own island, the same lookup
        /// `ShipyardService.HomeBerthOutpost` uses.
        public static DryDockSlip HomeSlip
        {
            get
            {
                var home = Dock.Home;
                if (home == null) return null;
                var isle = Island.Nearest(home.Berth);
                if (isle == null) return null;
                foreach (var s in All)
                    if (s != null && s.island == isle) return s;
                return null;
            }
        }
    }
}
