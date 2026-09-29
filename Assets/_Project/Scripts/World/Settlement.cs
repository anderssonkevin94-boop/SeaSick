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
        /// The survey on `Island.Home` (2026-09-29: derived from the home
        /// berth), null before the player has made a home.
        public static Settlement Home => Island.Home != null ? Island.Home.GetComponent<Settlement>() : null;

        [SerializeField] Vector3 centre;
        [SerializeField] float extent;
        [SerializeField] float core;
        [SerializeField] float areaHa;
        [SerializeField] float inscribed;
        [SerializeField] Vector3 inscribedAt;
        [SerializeField] float villageClearing;
        [SerializeField] Vector3 villageAt;

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

        /// How many buildings this ground holds at a given plot size.
        ///
        /// A village is not its footprints, it is its plots: a hut is 3 m
        /// tall and perhaps 5 m across, but nobody builds them touching, and
        /// the gaps are where the people walk. A 15 m plot is a building with
        /// room to pass round it, which is what a village looks like from
        /// above.
        public int Capacity(float plotMetres = 15f)
            => Mathf.FloorToInt(areaHa * 10000f / Mathf.Max(1f, plotMetres * plotMetres));

        /// The radius the buildings themselves would occupy, given how many
        /// of them there are -- so the camera frames a VILLAGE rather than
        /// all the ground a village could theoretically sprawl across.
        public float VillageRadius(int buildings = 20, float plotMetres = 15f)
            => Mathf.Min(core, Mathf.Sqrt(buildings * plotMetres * plotMetres / Mathf.PI));

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

        /// Where the village actually goes, and how much room it has: the
        /// best clearing inside the frame the docked camera holds, which is
        /// not the island's biggest one. See SettlementSite.Find.
        public float VillageClearing => villageClearing;
        public Vector3 VillageAt => villageAt;

        public void Configure(Terrain.SettlementSite.Site s)
        {
            centre = s.centre;
            extent = s.extent;
            core = s.core;
            areaHa = s.areaHa;
            inscribed = s.inscribed;
            inscribedAt = s.inscribedAt;
            villageClearing = s.villageClearing;
            villageAt = s.villageAt;
        }
    }
}
