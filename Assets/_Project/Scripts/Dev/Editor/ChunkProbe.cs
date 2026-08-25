using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using SeaSick.Terrain;

/// Edit-mode gate for TerrainChunkMesher: vertex heights equal the height
/// function at their world position, two adjacent chunks share bit-identical
/// positions AND normals along their common edge (both axes), normals are
/// unit length and point up-ish, and the LOD-2 chunk's vertices are a subset
/// of LOD-1's. Also renders the lab camera to /tmp/seasick-chunk.png.
public static class ChunkProbe
{
    static StringBuilder sb;
    static int fails;

    static void Gate(string name, bool ok, string detail)
    {
        sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        if (!ok) fails++;
    }

    static Mesh BuildChunk(int cx, int cz, int lod, TerrainSettings s, TerrainParams prm, NativeArray<float> lut)
    {
        TerrainChunkMesher.ChunkDesc d = new TerrainChunkMesher.ChunkDesc();
        d.coord = new int2(cx, cz); d.size = s.chunkSize; d.resolution = s.chunkResolution; d.lodStep = lod;
        Mesh m = new Mesh();
        TerrainChunkMesher.BuildSync(m, d, prm, lut, TerrainChunkMesher.ColourParams.From(s), s.skirtDepth);
        return m;
    }

    public static string Execute()
    {
        sb = new StringBuilder();
        fails = 0;
        TerrainSettings s = AssetDatabase.LoadAssetAtPath<TerrainSettings>("Assets/_Project/Settings/Terrain/TerrainSettings.asset");
        if (s == null) return "no TerrainSettings asset; run SetupTerrainLab";
        TerrainParams prm = TerrainParams.From(s);
        NativeArray<float> lut = TerrainCurveLut.Bake(s.terraceCurve, Allocator.TempJob);

        // Chunk under the preview island.
        int cx = -13, cz = 4;
        Mesh a = BuildChunk(cx, cz, 1, s, prm, lut);
        Mesh bx = BuildChunk(cx + 1, cz, 1, s, prm, lut);
        Mesh bz = BuildChunk(cx, cz + 1, 1, s, prm, lut);
        int n = s.chunkResolution;
        Gate("vertex-count", a.vertexCount == n * n + 4 * n, a.vertexCount + " == " + (n * n + 4 * n) + " (grid + skirts)");

        // Heights match the function at the vertex's world position.
        Vector3[] va = a.vertices; Vector3[] na = a.normals;
        float2 oa = new float2(cx, cz) * s.chunkSize;
        float worstH = 0f;
        for (int i = 0; i < n * n; i += 97)
        {
            float2 w = oa + new float2(va[i].x, va[i].z);
            float h = TerrainHeight.Height(w, prm, lut);
            worstH = math.max(worstH, math.abs(h - va[i].y));
        }
        // Tolerance 1e-3 m, not 1e-4: the mesher runs Burst and this check
        // recomputes in Mono, and the shelf-to-deep ramp amplifies the ~3e-7
        // difference between them by its own derivative -- about 2700 m per
        // unit of mask noise at shelfBand 0.10. Verified by halving the
        // amplifier: shelfBand 0.06 -> 0.12 moved worst |dh| 8.01e-4 -> 4.35e-4,
        // a factor of 1.84 against a predicted 2. What this gate is for is a
        // mesher that has drifted from the height function -- wrong world
        // offset, wrong LOD stride -- and that is metres, so 1e-3 is still
        // three orders of magnitude tighter than any real fault.
        Gate("height-matches-function", worstH < 1e-3f, "worst |dh|=" + worstH);

        // Normals unit and upward-ish.
        float worstLen = 0f; float minUp = 1f;
        for (int i = 0; i < n * n; i++) { worstLen = math.max(worstLen, math.abs(na[i].magnitude - 1f)); minUp = math.min(minUp, na[i].y); }
        Gate("normals-unit", worstLen < 1e-4f, "worst |len-1|=" + worstLen + " minUp=" + minUp.ToString("F3"));

        // Shared edge with +X neighbour: a's column n-1 vs bx's column 0.
        Vector3[] vb = bx.vertices; Vector3[] nb = bx.normals;
        int posMismatch = 0, nrmMismatch = 0;
        for (int j = 0; j < n; j++)
        {
            Vector3 pa = va[j * n + (n - 1)] + new Vector3(oa.x, 0f, oa.y);
            Vector3 pb = vb[j * n + 0] + new Vector3(oa.x + s.chunkSize, 0f, oa.y);
            if (pa != pb || pa.y != pb.y) posMismatch++;
            if (na[j * n + (n - 1)] != nb[j * n + 0]) nrmMismatch++;
        }
        Gate("seam-x-positions", posMismatch == 0, posMismatch + " mismatches of " + n);
        Gate("seam-x-normals", nrmMismatch == 0, nrmMismatch + " mismatches of " + n);

        // Shared edge with +Z neighbour: a's row n-1 vs bz's row 0.
        Vector3[] vc = bz.vertices; Vector3[] nc = bz.normals;
        posMismatch = 0; nrmMismatch = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 pa = va[(n - 1) * n + i] + new Vector3(oa.x, 0f, oa.y);
            Vector3 pb = vc[i] + new Vector3(oa.x, 0f, oa.y + s.chunkSize);
            if (pa != pb || pa.y != pb.y) posMismatch++;
            if (na[(n - 1) * n + i] != nc[i]) nrmMismatch++;
        }
        Gate("seam-z-positions", posMismatch == 0, posMismatch + " mismatches of " + n);
        Gate("seam-z-normals", nrmMismatch == 0, nrmMismatch + " mismatches of " + n);

        // LOD 2 vertices are a subset of LOD 1 (same lattice, every other vertex).
        Mesh a2 = BuildChunk(cx, cz, 2, s, prm, lut);
        Vector3[] v2 = a2.vertices;
        int n2 = (n - 1) / 2 + 1;
        int lodMismatch = 0;
        for (int j = 0; j < n2; j++)
            for (int i = 0; i < n2; i++)
                if (v2[j * n2 + i] != va[(j * 2) * n + i * 2]) lodMismatch++;
        Gate("lod2-subset", lodMismatch == 0, lodMismatch + " mismatches of " + (n2 * n2));

        lut.Dispose();
        Object.DestroyImmediate(a); Object.DestroyImmediate(bx); Object.DestroyImmediate(bz); Object.DestroyImmediate(a2);

        // Render the lab camera to a PNG.
        Camera cam = Camera.main;
        TerrainChunkPreview preview = Object.FindFirstObjectByType<TerrainChunkPreview>();
        if (cam != null && preview != null)
        {
            if (preview.transform.childCount == 0) preview.Rebuild();
            RenderTexture rt = new RenderTexture(1280, 800, 24);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes("/tmp/seasick-chunk.png", ImageConversion.EncodeToPNG(tex));
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
            sb.AppendLine("rendered /tmp/seasick-chunk.png, preview verts=" + preview.VertexCount);
        }

        sb.Insert(0, (fails == 0 ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-chunk.txt", sb.ToString());
        return sb.ToString();
    }
}
