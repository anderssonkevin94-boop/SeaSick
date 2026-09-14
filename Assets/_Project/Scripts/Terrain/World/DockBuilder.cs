using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// The home island's dock, baked into one mesh.
    ///
    /// Built in a LOCAL frame -- +Z seaward, +X to starboard of that, y is
    /// world height -- and the GameObject carries the rotation. Authoring a
    /// pier in world space means every plank carries the coast's bearing in
    /// it, and the first time the site moves, every number is wrong.
    ///
    /// Sizes come from WorldScale or are derived from it here, with the
    /// reasoning next to them. Nothing about a dock is arbitrary: it is a
    /// thing for people to walk on and a boat to lie against, so both of
    /// those set its dimensions.
    public static class DockBuilder
    {
        /// Deck height above mean sea level.
        ///
        /// Her own deck sits about 1.4 m above her waterline and her rail
        /// above that, so a pier at 1.8 m is a short step DOWN onto her --
        /// which is what boarding a small vessel from a pier is. Level with
        /// her deck would look like a mistake; a metre higher and the crew
        /// would be climbing.
        public const float DeckY = 1.8f;

        /// Two crew abreast with something between them.
        public static float Walkway => SeaSick.World.WorldScale.Person * 1.8f;

        /// The head is where the work happens: she lies against its long
        /// side, so it has to be long enough to take her spring lines and
        /// wide enough to stack what comes off her.
        public const float HeadLength = 10f;
        public const float HeadWidth = 7f;

        const float DeckThickness = 0.35f;
        const float PileSize = 0.5f;
        const float PileSpacing = 4.5f;
        const float RampRun = 7f;

        static Color32 Plank => SeaSick.World.WorldArtStyle.Instance != null
            ? new Color32(166, 122, 68, 255) : new Color32(122, 96, 66, 255);
        static Color32 PlankDark => SeaSick.World.WorldArtStyle.Instance != null
            ? new Color32(139, 98, 53, 255) : new Color32(98, 76, 52, 255);
        static Color32 PileWood => SeaSick.World.WorldArtStyle.Instance != null
            ? new Color32(99, 70, 43, 255) : new Color32(78, 62, 46, 255);
        static Color32 Bollard => SeaSick.World.WorldArtStyle.Instance != null
            ? new Color32(58, 62, 68, 255) : new Color32(64, 54, 44, 255);

        public static GameObject Build(Transform parent, HarbourSite.Site site,
            System.Func<float, float, float> height)
        {
            var go = new GameObject("Dock");
            go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(site.root.x, 0f, site.root.z);
            go.transform.rotation = Quaternion.LookRotation(
                new Vector3(site.seaward.x, 0f, site.seaward.y), Vector3.up);

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color32>();
            var t = new List<int>();

            float pier = site.pierLength;
            float halfW = Walkway * 0.5f;
            float rootY = site.root.y;

            // Ramp: the land is barely above the water here, so the walkway
            // has to climb to deck height before it goes anywhere.
            Wedge(v, n, c, t,
                new Vector3(-halfW, rootY + 0.1f, -1.5f), new Vector3(halfW, rootY + 0.1f, -1.5f),
                new Vector3(-halfW, DeckY, RampRun), new Vector3(halfW, DeckY, RampRun),
                DeckThickness, PlankDark);

            // The walkway out to the head.
            Box(v, n, c, t,
                new Vector3(0f, DeckY - DeckThickness * 0.5f, (RampRun + pier) * 0.5f),
                new Vector3(Walkway, DeckThickness, pier - RampRun), Plank);

            // The head, straddling the point where she floats along her whole
            // length -- half of it inshore of that mark, half beyond.
            Box(v, n, c, t,
                new Vector3(0f, DeckY - DeckThickness * 0.5f, pier),
                new Vector3(HeadWidth, DeckThickness, HeadLength), Plank);

            // Piles down to the seabed, both edges of the walkway and the
            // four corners of the head. Their length is read off the ground
            // under each one rather than assumed: the whole point of this
            // site is that the bottom drops away fast.
            for (float z = RampRun; z <= pier - HeadLength * 0.5f; z += PileSpacing)
            {
                Pile(v, n, c, t, go.transform, new Vector2(-halfW + PileSize * 0.5f, z), height);
                Pile(v, n, c, t, go.transform, new Vector2(halfW - PileSize * 0.5f, z), height);
            }
            float hz0 = pier - HeadLength * 0.5f + PileSize, hz1 = pier + HeadLength * 0.5f - PileSize;
            float hx = HeadWidth * 0.5f - PileSize;
            Pile(v, n, c, t, go.transform, new Vector2(-hx, hz0), height);
            Pile(v, n, c, t, go.transform, new Vector2(hx, hz0), height);
            Pile(v, n, c, t, go.transform, new Vector2(-hx, hz1), height);
            Pile(v, n, c, t, go.transform, new Vector2(hx, hz1), height);
            Pile(v, n, c, t, go.transform, new Vector2(-hx, pier), height);
            Pile(v, n, c, t, go.transform, new Vector2(hx, pier), height);

            // Bollards on the berth side. HarbourSite puts her off the local
            // -X edge, so they go there and nowhere else.
            for (int i = -1; i <= 1; i++)
                Box(v, n, c, t,
                    new Vector3(-HeadWidth * 0.5f + 0.6f, DeckY + 0.45f, pier + i * 3.4f),
                    new Vector3(0.36f, 0.9f, 0.36f), Bollard);

            var mesh = new Mesh { name = "DockMesh" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = DockMaterial();

            var dock = go.AddComponent<SeaSick.World.Dock>();
            dock.Configure(site, DeckY);
            return go;
        }

        static Material dockMat;

        /// The scenery shader, so a pier is lit like everything else standing
        /// on this island, with the cliff striation off.
        static Material DockMaterial()
        {
            if (SeaSick.World.WorldArtStyle.SceneryOverride != null)
                return SeaSick.World.WorldArtStyle.SceneryOverride;
            if (dockMat != null) return dockMat;
            var sh = Shader.Find("SeaSick/Terrain Vertex Color");
            dockMat = new Material(sh) { name = "Dock" };
            if (dockMat.HasProperty("_StriationStrength")) dockMat.SetFloat("_StriationStrength", 0f);
            if (dockMat.HasProperty("_DetailStrength")) dockMat.SetFloat("_DetailStrength", 0.35f);
            if (dockMat.HasProperty("_NormalStrength")) dockMat.SetFloat("_NormalStrength", 0.2f);
            return dockMat;
        }

        /// One pile, from just under the decking down into the seabed.
        static void Pile(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Transform frame, Vector2 atLocal, System.Func<float, float, float> height)
        {
            Vector3 w = frame.TransformPoint(new Vector3(atLocal.x, 0f, atLocal.y));
            float bed = height(w.x, w.z) - 0.6f;          // buried, not resting on top
            float top = DeckY - DeckThickness;
            if (top - bed < 0.4f) return;
            Box(v, n, c, t,
                new Vector3(atLocal.x, (top + bed) * 0.5f, atLocal.y),
                new Vector3(PileSize, top - bed, PileSize), PileWood);
        }

        /// A sloped slab: four top corners given, extruded down by `thick`.
        static void Wedge(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 d, Vector3 e, float thick, Color32 col)
        {
            Vector3 down = Vector3.down * thick;
            Quad(v, n, c, t, a, b, e, d, col);                       // top
            Quad(v, n, c, t, d + down, e + down, b + down, a + down, col);   // underside
            Quad(v, n, c, t, a, d, d + down, a + down, col);         // port
            Quad(v, n, c, t, e, b, b + down, e + down, col);         // starboard
            Quad(v, n, c, t, d, e, e + down, d + down, col);         // seaward end
        }

        static void Box(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 centre, Vector3 size, Color32 col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = centre + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = centre + new Vector3(h.x, -h.y, -h.z);
            Vector3 p110 = centre + new Vector3(h.x, h.y, -h.z);
            Vector3 p010 = centre + new Vector3(-h.x, h.y, -h.z);
            Vector3 p001 = centre + new Vector3(-h.x, -h.y, h.z);
            Vector3 p101 = centre + new Vector3(h.x, -h.y, h.z);
            Vector3 p111 = centre + new Vector3(h.x, h.y, h.z);
            Vector3 p011 = centre + new Vector3(-h.x, h.y, h.z);
            Quad(v, n, c, t, p010, p110, p111, p011, col);   // top
            Quad(v, n, c, t, p001, p101, p100, p000, col);   // bottom
            Quad(v, n, c, t, p000, p100, p110, p010, col);   // -Z
            Quad(v, n, c, t, p101, p001, p011, p111, col);   // +Z
            Quad(v, n, c, t, p001, p000, p010, p011, col);   // -X
            Quad(v, n, c, t, p100, p101, p111, p110, col);   // +X
        }

        static void Quad(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color32 col)
        {
            int i0 = v.Count;
            // Cross(e-a, b-a), NOT Cross(b-a, e-a). The winding and the normal
            // are two separate decisions and they were disagreeing: the faces
            // drew (so the winding was right) and every one of them was lit
            // from behind, which renders as ambient-only near-black. On a
            // deck top, a, b, d, e run -X-Z, +X-Z, +X+Z, -X+Z, so b-a is +X
            // and e-a is +Z, and X cross Z is -Y -- pointing into the pier.
            Vector3 nrm = Vector3.Normalize(Vector3.Cross(e - a, b - a));
            v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            for (int k = 0; k < 4; k++) { n.Add(nrm); c.Add(col); }
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
            t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
        }
    }
}
