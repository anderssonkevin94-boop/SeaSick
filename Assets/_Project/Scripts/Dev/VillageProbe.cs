using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.World;
using SeaSick.Terrain;

/// Does the village clearing exist, is it empty, and did reserving it move
/// the rest of the wood?
///
/// Both halves are read off the BAKED MESH. The scenery is several hundred
/// trees welded into one mesh, so the only honest question is what the
/// shipped geometry contains -- a check that re-ran the scatter rules would
/// agree with itself whatever the mesh held. Trees are found by trunk colour
/// exactly as WoodProbe finds them: IslandScenery writes a trunk as a
/// four-sided prism in one flat brown, so a run of consecutive trunk-brown
/// vertices is one tree and its base is the lowest of them.
///
/// The second half is the one worth having. `IslandScenery` walks ONE
/// `System.Random` through the grid in order, so a keep-out that `continue`s
/// removes rolls and reshuffles every tree after it -- the whole wood moves
/// when a clearing is nudged five metres. So this bakes the SAME island a
/// second time with no keep-out at all and matches the two trunk sets: every
/// tree outside the clearing must be in both, at the same place.
public class VillageProbe : MonoBehaviour
{
    static readonly Color32 Trunk = new Color32(92, 64, 40, 255);

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("VillageProbe: not in play mode"); return; }
        var sb = new StringBuilder();

        var village = Village.Home;
        var island = Island.All.Find(i => i.IsHome);
        if (island == null) { Report("no home island\n"); return; }
        var settlement = island.GetComponent<Settlement>();

        sb.AppendLine($"home island '{island.name}' at "
            + $"{island.transform.position.x:F0},{island.transform.position.z:F0}, mean radius {island.Radius:F0} m");
        if (settlement == null) { sb.AppendLine("NO Settlement -- the site search found nothing"); Report(sb.ToString()); return; }
        sb.AppendLine($"settlement: {settlement.AreaHectares:F2} ha, core {settlement.Core:F0} m, "
            + $"inscribed {settlement.Inscribed:F0} m at {settlement.InscribedAt.x:F0},{settlement.InscribedAt.z:F0}");
        if (village == null) { sb.AppendLine("NO Village component"); Report(sb.ToString()); return; }
        sb.AppendLine($"clearing: r={village.ClearingRadius:F1} m centred "
            + $"{village.ClearingCentre.x:F0},{village.ClearingCentre.z:F0}   "
            + $"(capacity {village.StoreCapacity}, {village.Built.Count} built)");

        // --- what the shipped mesh actually holds ---------------------------
        Transform scenery = null;
        foreach (var t in island.GetComponentsInChildren<Transform>())
            if (t.name == "Scenery") scenery = t;
        if (scenery == null) { sb.AppendLine("no Scenery mesh"); Report(sb.ToString()); return; }

        var shipped = Trunks(scenery.gameObject);
        int inside = 0; float nearest = float.MaxValue;
        Vector2 c = new Vector2(village.ClearingCentre.x, village.ClearingCentre.z);
        foreach (var p in shipped)
        {
            float d = Vector2.Distance(new Vector2(p.x, p.z), c);
            if (d < village.ClearingRadius) inside++;
            if (d < nearest) nearest = d;
        }
        sb.AppendLine($"shipped scenery: {shipped.Count} trees; "
            + $"{inside} inside the clearing (want 0); nearest trunk {nearest:F1} m from centre "
            + $"({nearest - village.ClearingRadius:+0.0;-0.0} m outside the edge)");

        // --- and what it would have held with no clearing at all ------------
        var pop = FindFirstObjectByType<TerrainWorldPopulator>();
        if (pop == null || pop.terrain == null || Island.TerrainHeight == null)
        {
            sb.AppendLine("no populator/height function -- skipping the shuffle check");
            Report(sb.ToString()); return;
        }
        var probe = IslandScenery.Build(island.transform, island.transform.position, island.Radius,
            (x, z) => Island.TerrainHeight(x, z), pop.terrain, island.RadiusAt,
            pop.terrain.seed * 7919 + 0, TerrainParams.From(pop.terrain), null, null);
        if (probe == null) { sb.AppendLine("re-bake produced nothing"); Report(sb.ToString()); return; }
        var bare = Trunks(probe);
        probe.SetActive(false);
        Destroy(probe);

        // Match by position. Same stream => the same tree at the same metre.
        int outsideBare = 0, matched = 0; float worst = 0f;
        var lookup = new List<Vector3>(shipped);
        foreach (var b in bare)
        {
            if (Vector2.Distance(new Vector2(b.x, b.z), c) < village.ClearingRadius) continue;
            outsideBare++;
            float best = float.MaxValue;
            foreach (var sTree in lookup)
            {
                float d = (sTree - b).sqrMagnitude;
                if (d < best) best = d;
            }
            best = Mathf.Sqrt(best);
            if (best < 0.05f) matched++;
            if (best > worst) worst = best;
        }
        sb.AppendLine($"no-clearing bake: {bare.Count} trees, {outsideBare} of them outside the clearing");
        sb.AppendLine($"  matched in the shipped mesh: {matched}/{outsideBare}"
            + $"   worst displacement {worst:F3} m   (want all matched, worst ~0)");
        sb.AppendLine($"  trees the clearing cost: {bare.Count - shipped.Count}");

        Report(sb.ToString());
    }

    /// Raise storehouses until the clearing refuses one, and measure each
    /// against the ground it stands on. Nothing is flattened, so the only
    /// question that matters is how far the low corner is below the floor --
    /// that gap is what the footing has to bridge, and an unbridged one is a
    /// building floating at one corner.
    public static void Build()
    {
        if (!Application.isPlaying) { Debug.LogError("VillageProbe: not in play mode"); return; }
        var village = Village.Home;
        if (village == null) { Report("no Village\n"); return; }
        var sb = new StringBuilder();
        var plan = BuildPlans.Storehouse;
        var dock = Dock.Home;

        int before = village.Built.Count;
        for (int i = 0; i < 12; i++)
        {
            var b = village.Raise(plan);
            if (b == null) { sb.AppendLine($"refused #{before + i + 1} -- clearing full"); break; }
            Vector3 p = b.transform.position;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = p + b.transform.rotation
                        * new Vector3(sx * plan.footprint.x * 0.5f, 0f, sz * plan.footprint.y * 0.5f);
                    float h = Island.TerrainHeight != null ? Island.TerrainHeight(corner.x, corner.z) : 0f;
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
            float fromCentre = Vector2.Distance(new Vector2(p.x, p.z),
                new Vector2(village.ClearingCentre.x, village.ClearingCentre.z));
            float fromPier = dock != null
                ? Vector2.Distance(new Vector2(p.x, p.z), new Vector2(dock.Root.x, dock.Root.z)) : -1f;
            sb.AppendLine($"#{village.Built.Count}: at {p.x:F0},{p.y:F1},{p.z:F0}   "
                + $"{fromCentre:F1} m from clearing centre (r {village.ClearingRadius:F1})   "
                + $"pier {fromPier:F0} m   floor {p.y:F2}, corners {lo:F2}..{hi:F2}, "
                + $"footing must bridge {hi - lo:F2} m");
        }
        sb.AppendLine($"capacity now {village.StoreCapacity} "
            + $"({village.CountOf(plan.id)} × {plan.label})");
        Report(sb.ToString());
    }

    /// The village from the two eyes that matter: the shot the player gets
    /// when she comes alongside (the game's own docked overview -- whatever
    /// the camera is actually doing, not a vantage derived a second time),
    /// and straight down on the clearing so the siting can be read as a plan.
    public static void Shot()
    {
        if (!Application.isPlaying) { Debug.LogError("VillageProbe: not in play mode"); return; }
        var village = Village.Home;
        if (village == null) { Debug.LogError("VillageProbe: no Village"); return; }

        var cam = Camera.main;
        if (cam != null) Capture(cam, "/tmp/village-dock.png");

        var go = new GameObject("VillageProbeCam");
        var top = go.AddComponent<Camera>();
        top.CopyFrom(cam != null ? cam : Camera.current);
        top.fieldOfView = 46f;
        float up = village.ClearingRadius * 2.6f;
        go.transform.position = village.ClearingCentre + new Vector3(0f, up, -up * 0.55f);
        go.transform.LookAt(village.ClearingCentre);
        Capture(top, "/tmp/village-plan.png");
        Destroy(go);
    }

    /// Rendered to a RenderTexture rather than ScreenCapture, which does not
    /// come back on the same frame and picks up the IMGUI over the top.
    static void Capture(Camera cam, string path)
    {
        const int W = 900, H = 1500;   // portrait, as the game is
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32)
        { antiAliasing = 2 };
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        float prevAspect = cam.aspect;
        // Forced, not inherited. The editor's Game view is landscape and the
        // game is not; a shot judged at 1.41 shows a village that is off the
        // left edge on a phone.
        cam.aspect = (float)W / H;
        cam.ResetProjectionMatrix();
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        cam.targetTexture = prevTarget;
        cam.aspect = prevAspect;
        cam.ResetProjectionMatrix();
        RenderTexture.active = prevActive;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.Destroy(tex);
        rt.Release();
        Object.Destroy(rt);
    }

    /// Every tree base in a scenery bake. The base of each tree's vertex
    /// run is taken off the MESH (its lowest vertex), and the run itself
    /// from the builder's index -- the kit's trunks carry a graded bark, so
    /// there is no single brown to scan for any more.
    static List<Vector3> Trunks(GameObject scenery)
    {
        var found = new List<Vector3>();
        var wood = scenery.GetComponent<SeaSick.Terrain.SceneryWood>();
        if (wood == null) return found;
        var cells = wood.Cells;
        var cached = new Dictionary<int, Vector3[]>();
        for (int i = 0; i < wood.TreeCount; i++)
        {
            var t = wood.TreeAt(i);
            if (!cached.TryGetValue(t.cell, out var verts))
            {
                var m = cells[t.cell].lod0;
                verts = m != null ? m.vertices : new Vector3[0];
                cached[t.cell] = verts;
            }
            if (t.vertCount == 0 || t.vertStart + t.vertCount > verts.Length) continue;
            Vector3 lo = verts[t.vertStart];
            for (int k = t.vertStart; k < t.vertStart + t.vertCount; k++) if (verts[k].y < lo.y) lo = verts[k];
            found.Add(lo);
        }
        return found;
    }

    static void Report(string s)
    {
        Debug.Log("VILLAGE PROBE\n" + s);
        System.IO.File.WriteAllText("/tmp/village-probe.txt", s);
    }
}
