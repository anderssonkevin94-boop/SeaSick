using UnityEngine;

namespace SeaSick.World.Life
{
    /// **Four small tombstone variants, built from primitives** -- no new
    /// art asset, the same shape `HunterProps.Prim`/`LadderLayout.Wood` use
    /// for every other primitive-built prop in the low-poly style: a runtime
    /// `SeaSick/Environment Toon` (or URP Lit, off the phone's toon shader)
    /// material tinted per-variant, one `MeshRenderer` per primitive, no
    /// collider on the primitives themselves -- one box collider on the
    /// root instead, so a tap lands on the whole stone.
    ///
    /// Picked deterministically off `LifeStory.Fnv32(name)` (the same stable
    /// hash the story generator uses), so the SAME grave wears the SAME
    /// stone every time it is drawn -- the ghost while placing, and again on
    /// `Outpost.Adopt`'s restore.
    public static class GraveVisual
    {
        static readonly Color StoneGrey = new Color(0.56f, 0.55f, 0.52f);
        static readonly Color WoodBrown = new Color(0.40f, 0.28f, 0.17f);

        /// Roughly 0.5-0.9 m tall, per Kevin's brief.
        public static GameObject Build(Transform parent, string goName, string graveName, uint seed, out Material mat)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);

            mat = new Material(Shader.Find(WorldArtStyle.Instance != null
                ? "SeaSick/Environment Toon" : "Universal Render Pipeline/Lit"));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);

            int variant = (int)(seed % 4u);
            Color baseColor;
            switch (variant)
            {
                case 0: baseColor = StoneGrey; BuildSlab(go.transform, mat); break;
                case 1: baseColor = WoodBrown; BuildCross(go.transform, mat); break;
                case 2: baseColor = StoneGrey; BuildCairn(go.transform, mat, seed); break;
                default: baseColor = WoodBrown; BuildLeaningBoard(go.transform, mat); break;
            }
            mat.SetColor("_BaseColor", baseColor);

            var tag = go.AddComponent<Gravestone>();
            tag.graveName = graveName;

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.4f, 0f);
            col.size = new Vector3(0.7f, 0.9f, 0.5f);

            return go;
        }

        static GameObject Prim(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale,
            Quaternion localRot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }

        /// A rounded stone slab -- a squat cube, since the primitive kit has
        /// no rounded cap; the low-poly style reads it as a slab regardless.
        static void BuildSlab(Transform t, Material mat)
        {
            Prim(t, PrimitiveType.Cube, new Vector3(0f, 0.32f, 0f), new Vector3(0.5f, 0.64f, 0.14f), Quaternion.identity, mat);
        }

        /// A wooden cross of two planks.
        static void BuildCross(Transform t, Material mat)
        {
            Prim(t, PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(0.09f, 0.8f, 0.09f), Quaternion.identity, mat);
            Prim(t, PrimitiveType.Cube, new Vector3(0f, 0.56f, 0f), new Vector3(0.5f, 0.09f, 0.09f), Quaternion.identity, mat);
        }

        /// A small cairn: 3-5 stacked, staggered stones, count and jitter
        /// off the same seed so it is stable but not identical to another
        /// cairn variant elsewhere.
        static void BuildCairn(Transform t, Material mat, uint seed)
        {
            int n = 3 + (int)((seed >> 3) % 3u); // 3..5
            float y = 0.05f;
            for (int i = 0; i < n; i++)
            {
                float s = Mathf.Max(0.18f, 0.42f - i * 0.06f);
                float ang = (seed % 360u) + i * 47f;
                float rx = 0.06f * Mathf.Cos(ang * Mathf.Deg2Rad);
                float rz = 0.06f * Mathf.Sin(ang * Mathf.Deg2Rad);
                y += s * 0.35f;
                Prim(t, PrimitiveType.Sphere, new Vector3(rx, y, rz), new Vector3(s, s * 0.75f, s),
                    Quaternion.Euler(ang * 0.3f, ang, 0f), mat);
                y += s * 0.15f;
            }
        }

        /// A leaning wooden board with a stone at its foot.
        static void BuildLeaningBoard(Transform t, Material mat)
        {
            Prim(t, PrimitiveType.Cube, new Vector3(0.05f, 0.33f, 0f), new Vector3(0.4f, 0.66f, 0.05f),
                Quaternion.Euler(0f, 0f, 10f), mat);
            Prim(t, PrimitiveType.Sphere, new Vector3(-0.18f, 0.08f, 0.05f), new Vector3(0.2f, 0.16f, 0.2f),
                Quaternion.identity, mat);
        }
    }
}
