using UnityEngine;

namespace SeaSick.World
{
    /// Where the village goes: the largest contiguous piece of buildable
    /// ground on an island, measured off the height field at build time.
    ///
    /// It exists mainly so the camera has something true to frame. Framing
    /// the ISLAND put the settlement at four pixels a person -- correct, and
    /// useless, because what the player is there to look at is people walking
    /// between buildings, not a landform.
    public class Settlement : MonoBehaviour
    {
        public static Settlement Home { get; private set; }

        [SerializeField] Vector3 centre;
        [SerializeField] float extent;
        [SerializeField] float core;
        [SerializeField] float areaHa;
        [SerializeField] float inscribed;
        [SerializeField] Vector3 inscribedAt;

        /// Middle of the buildable ground.
        public Vector3 Centre => centre;

        /// Furthest part of it from that middle -- what a camera has to cover
        /// to hold the whole settlement.
        public float Extent => extent;

        /// The radius holding four fifths of the buildable ground -- the part
        /// that reads as a place, rather than the tendrils that make `Extent`
        /// three times bigger than the field it belongs to.
        public float Core => core;

        public float AreaHectares => areaHa;

        /// What the camera should hold, which is NOT the same as where you
        /// can build.
        ///
        /// Framing exactly the buildable ground gave a close-up of a wood
        /// with no island around it -- you could count the trees and had no
        /// idea where you were. The village is the subject, but the subject
        /// needs the ground it sits in: the paths people walk, the trees that
        /// get cleared, the beach the dock runs up to. Half as much again in
        /// every direction is that context, and it also lands where the eye
        /// expects a settlement view to sit.
        public float ViewRadius => Mathf.Max(70f, core * 1.7f);

        /// Radius of the largest circle that fits inside it: the real answer
        /// to "does a walled compound fit here".
        public float Inscribed => inscribed;
        public Vector3 InscribedAt => inscribedAt;

        public void Configure(Terrain.SettlementSite.Site s)
        {
            centre = s.centre;
            extent = s.extent;
            core = s.core;
            areaHa = s.areaHa;
            inscribed = s.inscribed;
            inscribedAt = s.inscribedAt;
        }

        void OnEnable() { if (Home == null) Home = this; }
        void OnDisable() { if (Home == this) Home = null; }
    }
}
