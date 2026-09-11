using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using SeaSick.Terrain;
using SeaSick.UI;
using SeaSick.World;

/// **Hand Kevin the controls**, for the half of an island that grows on it.
///
/// Every note he has given on the scenery has been a look call with a number
/// behind it — "too many trees", "big versions of those rocks" — and every
/// one of them cost a round of me picking a number, baking a world and
/// photographing it. The first round of "too many trees" took the wood from
/// too thick to five bald islands in one step, which is what guessing looks
/// like. This project's own record is three measured rounds lost to one
/// hands-on round.
///
/// Unlike `IslandTuner` this one is honest end to end: the scenery is welded
/// at world build, so the tuner REBAKES it (`TerrainWorldPopulator.Redress`)
/// and what you are looking at is what those settings actually produce. The
/// landform underneath does not change, which is the point — the island stays
/// the same island while the wood on it changes.
public class FloraTuner : MonoBehaviour, SeaSick.UI.IDevTool
{
    [Tooltip("Off = the shipped scenery, untouched, and the arrow keys left alone.")]
    [SerializeField] bool active = false;

    public string ToolName => "Flora";
    public string ToolBlurb => "the wood, the wheat and the crags — rebakes the island you are at";
    public bool ToolActive { get => active; set => active = value; }
    void OnEnable() => SeaSick.UI.DevTools.Register(this);
    void OnDisable() => SeaSick.UI.DevTools.Unregister(this);

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("FloraTuner: not in play mode"); return; }
        var existing = FindAnyObjectByType<FloraTuner>();
        if (existing == null)
            existing = new GameObject("FloraTuner").AddComponent<FloraTuner>();
        SeaSick.UI.SettingsPanel.Toggle(existing);
    }

    class Knob
    {
        public string label;
        public System.Func<TerrainSettings, float> get;
        public System.Action<TerrainSettings, float> set;
        public float step, min, max;
        public string unit = "";
    }

    Knob[] knobs;
    /// Built on demand and never trusted from `Start`: `Knob` holds
    /// delegates, so a domain reload brings the component back with this
    /// array null and does NOT call Start again — which presents as an
    /// exception every frame out of OnGUI. `IslandTuner` carries the same
    /// note for the same reason, and tuning is exactly when scripts get
    /// edited.
    Knob[] Knobs => knobs ?? (knobs = BuildKnobs());

    TerrainSettings ts;
    TerrainWorldPopulator pop;
    SeaSick.Ship.ShipMotor ship;
    int sel, islandIndex = -1;
    string readout = "R rebakes the island you are at";

    bool Bind()
    {
        if (pop == null) pop = FindAnyObjectByType<TerrainWorldPopulator>();
        if (ship == null) ship = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (ts == null) ts = pop != null ? pop.terrain : null;
        return ts != null && pop != null;
    }

    Knob[] BuildKnobs()
    {
        return new[]
        {
            new Knob { label = "how thick the wood is", step = 0.05f, min = 0.1f, max = 2f,
                get = s => s.treeDensity, set = (s, v) => s.treeDensity = v },
            new Knob { label = "tree spacing", unit = "m", step = 0.25f, min = 3f, max = 12f,
                get = s => s.treeSpacing, set = (s, v) => s.treeSpacing = v },
            new Knob { label = "grass a tree needs above the sand", unit = "m", step = 0.2f, min = 0f, max = 8f,
                get = s => s.sandTreeMargin, set = (s, v) => s.sandTreeMargin = v },
            new Knob { label = "palms allowed on the beach", step = 0.01f, min = 0f, max = 0.3f,
                get = s => s.palmOnSand, set = (s, v) => s.palmOnSand = v },
            new Knob { label = "islands that are broadleaf", step = 0.05f, min = 0f, max = 1f,
                get = s => s.broadleafIslands, set = (s, v) => s.broadleafIslands = v },

            new Knob { label = "stand size (x island radius)", step = 0.05f, min = 0.2f, max = 1.2f,
                get = s => s.standSpan, set = (s, v) => s.standSpan = v },
            new Knob { label = "stand vs glade contrast", step = 0.1f, min = 0f, max = 6f,
                get = s => s.standContrast, set = (s, v) => s.standContrast = v },
            new Knob { label = "what a glade keeps", step = 0.02f, min = 0f, max = 0.8f,
                get = s => s.standFloor, set = (s, v) => s.standFloor = v },
            new Knob { label = "lattice warp (x spacing)", step = 0.1f, min = 0f, max = 2f,
                get = s => s.treeWarp, set = (s, v) => s.treeWarp = v },
            new Knob { label = "scrub in the glades", step = 0.05f, min = 0f, max = 1f,
                get = s => s.scrubChance, set = (s, v) => s.scrubChance = v },

            new Knob { label = "islands that are farmed", step = 0.05f, min = 0f, max = 1f,
                get = s => s.farmedShare, set = (s, v) => s.farmedShare = v },
            new Knob { label = "wheat spacing", unit = "m", step = 0.05f, min = 1.2f, max = 4f,
                get = s => s.cropSpacing, set = (s, v) => s.cropSpacing = v },
            new Knob { label = "steepest ground anyone sows", step = 0.01f, min = 0.05f, max = 0.5f,
                get = s => s.fieldSlopeMax, set = (s, v) => s.fieldSlopeMax = v },
            new Knob { label = "fields stop this far up", unit = "m", step = 2f, min = 6f, max = 80f,
                get = s => s.fieldMaxRise, set = (s, v) => s.fieldMaxRise = v },

            new Knob { label = "crag size x", step = 0.1f, min = 0.5f, max = 4f,
                get = s => s.cragScale, set = (s, v) => s.cragScale = v },
            new Knob { label = "outcrops that are headlands", step = 0.02f, min = 0f, max = 0.5f,
                get = s => s.headlandShare, set = (s, v) => s.headlandShare = v },
            new Knob { label = "stone on the shore", step = 0.1f, min = 0f, max = 3f,
                get = s => s.shoreRock, set = (s, v) => s.shoreRock = v },
        };
    }

    void Update()
    {
        // Gated on `active`: a tuner switched off in the scene must not eat
        // the arrow keys, which every other tuner also uses.
        if (!active) return;
        var kb = Keyboard.current;
        if (kb == null || !Bind()) return;
        var ks = Knobs;

        if (kb.upArrowKey.wasPressedThisFrame) sel = (sel - 1 + ks.Length) % ks.Length;
        if (kb.downArrowKey.wasPressedThisFrame) sel = (sel + 1) % ks.Length;

        float dir = 0f;
        if (kb.leftArrowKey.wasPressedThisFrame) dir = -1f;
        if (kb.rightArrowKey.wasPressedThisFrame) dir = 1f;
        if (dir != 0f)
        {
            var k = ks[Mathf.Clamp(sel, 0, ks.Length - 1)];
            float mult = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 4f : 1f;
            k.set(ts, Mathf.Clamp(k.get(ts) + dir * k.step * mult, k.min, k.max));
            // NOT rebaked on every keypress. A rebake is a few hundred
            // thousand triangles welded on the main thread; held down, the
            // arrow key would stall the editor rather than tune anything.
            readout = "changed — press R to rebake";
        }

        if (kb.rKey.wasPressedThisFrame) Rebake();
        if (kb.nKey.wasPressedThisFrame) NextIsland();
        if (kb.pKey.wasPressedThisFrame) Print();
    }

    Island Current()
    {
        if (islandIndex >= 0 && islandIndex < Island.All.Count) return Island.All[islandIndex];
        return ship != null ? Island.Nearest(ship.transform.position) : null;
    }

    /// Re-dress the island in view against the numbers now on screen, and
    /// report what the bake actually laid down — off `IslandScenery.Report`,
    /// which is the bake's own account, not a re-run of the placement rules.
    void Rebake()
    {
        if (!Bind()) return;
        var isle = Current();
        if (isle == null) { readout = "no island in reach"; return; }
        float t0 = Time.realtimeSinceStartup;
        pop.Redress(isle);
        float ms = (Time.realtimeSinceStartup - t0) * 1000f;

        var d = default(IslandScenery.Dressed);
        float near = float.MaxValue;
        foreach (var e in IslandScenery.Report)
        {
            float dist = Vector3.Distance(e.centre, isle.transform.position);
            if (dist < near) { near = dist; d = e; }
        }
        float ha = Mathf.Max(0.01f, Mathf.PI * d.radius * d.radius / 10000f);
        readout = $"{isle.name} r{d.radius:F0}: {d.trees} trees ({d.trees / ha:F0}/ha), "
                + $"{d.scrub} scrub, {d.crops} wheat   [{ms:F0} ms]";
        Debug.Log("FloraTuner: " + readout);
    }

    /// Warp to the next island, so species and density are judged as a
    /// comparison rather than from memory of the last one — the whole reason
    /// "one species per island" is a thing you have to see two islands to
    /// check.
    void NextIsland()
    {
        if (!Bind() || ship == null || Island.All.Count == 0) return;
        islandIndex = (islandIndex + 1) % Island.All.Count;
        var isle = Island.All[islandIndex];
        if (isle == null) return;

        // Stand off the island's tallest bearing, the way IslandLook frames
        // one: a vantage picked by compass rather than by measurement ends up
        // looking at the inside of a hill.
        float bestAng = 0f, bestH = -1f;
        for (int a = 0; a < 48; a++)
        {
            float ang = a / 48f * Mathf.PI * 2f;
            Vector3 p = isle.transform.position
                + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * isle.RadiusAt(ang) * 0.5f;
            float h = Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : 0f;
            if (h > bestH) { bestH = h; bestAng = ang; }
        }
        Vector3 dir = new Vector3(Mathf.Sin(bestAng), 0f, Mathf.Cos(bestAng));
        Vector3 at = isle.transform.position + dir * (isle.RadiusAt(bestAng) + 300f);
        at.y = ship.transform.position.y;
        var rb = ship.GetComponent<Rigidbody>();
        ship.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-dir));
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        ship.AnchorPoint = at;
        readout = $"{isle.name} — R to rebake it";
    }

    /// Dump every value, so a settled session can be written back into the
    /// asset. The numbers are the deliverable, not the feeling — and a new
    /// serialized field does NOT pick up a changed C# default, so the asset
    /// is the only place a setting is really decided.
    void Print()
    {
        if (!Bind()) return;
        var sb = new StringBuilder("// FloraTuner — settled values\n");
        foreach (var k in Knobs) sb.AppendLine($"{k.label,-36} {k.get(ts),8:F3} {k.unit}");
        sb.AppendLine(readout);
        Debug.Log("FLORA TUNER\n" + sb);
        System.IO.File.WriteAllText("/tmp/flora-tune.txt", sb.ToString());
    }

    public void DrawTool(Rect panel)
    {
        if (!Bind()) return;
        var ks = Knobs;
        int u = UITheme.Unit;

        float y = panel.y + u * 0.5f;
        GUI.Label(new Rect(panel.x + u * 0.6f, y, panel.width, u * 1.6f),
            "↑↓ pick   ←→ change (shift ×4)   R rebake   N next island   P print",
            UITheme.Small);
        y += u * 1.9f;

        for (int i = 0; i < ks.Length; i++)
        {
            var k = ks[i];
            var r = new Rect(panel.x + u * 0.6f, y, panel.width - u * 1.2f, u * 1.4f);
            if (i == sel) UITheme.Rect(r, UITheme.Track);
            GUI.Label(r, $"{(i == sel ? "▸ " : "  ")}{k.label}", UITheme.Small);
            GUI.Label(r, $"{k.get(ts):F2} {k.unit}    ", UITheme.Small2Centered);
            y += u * 1.5f;
        }

        y += u * 0.4f;
        GUI.Label(new Rect(panel.x + u * 0.6f, y, panel.width - u * 1.2f, u * 1.6f), readout, UITheme.Small);
        y += u * 1.8f;
        GUI.Label(new Rect(panel.x + u * 0.6f, y, panel.width - u * 1.2f, u * 1.6f),
            "R rebakes THIS island only — sail to another and press R there too",
            UITheme.Small);
    }
}
