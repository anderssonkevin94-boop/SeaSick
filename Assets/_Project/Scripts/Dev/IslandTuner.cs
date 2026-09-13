using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using SeaSick.Terrain;
using SeaSick.UI;
using SeaSick.World;

/// **Hand Kevin the controls.** Every call left on the islands is a look
/// call, and this project's own record says three measured rounds get lost
/// to one hands-on round.
///
/// So: the knobs that decide what an island IS, live, with the numbers on
/// screen and a key that flies you to the next island so variety can be
/// judged as a comparison rather than a memory.
///
/// **What it does not do.** It rebuilds the terrain MESH (TerrainStreamer
/// .MarkDirty), not the world built on top of it: the populator sampled its
/// height function once at Start, so island radial profiles, beaches, props,
/// the dock and the village clearing all stay where the OLD terrain put them.
/// Grounding and landing will disagree with what you see. That is fine for
/// judging a landform and useless for judging anything else — restart play
/// to make the world agree with the numbers again.
public class IslandTuner : MonoBehaviour, SeaSick.UI.IDevTool
{
    [Tooltip("Off = the shipped islands, untouched, and the arrow keys left alone.")]
    [SerializeField] bool active = false;

    // --- IDevTool: opened from the settings drawer, which decides where it
    // draws. See SeaSick.UI.DevTools.
    public string ToolName => "Islands";
    public string ToolBlurb => "what an island IS — mesh only, restart play to rebuild the world on it";
    public bool ToolActive { get => active; set => active = value; }
    void OnEnable() => SeaSick.UI.DevTools.Register(this);
    void OnDisable() => SeaSick.UI.DevTools.Unregister(this);

    /// The menu route in. It opens the drawer onto this tool rather than
    /// spawning a second overlay, so there is one way for a tuner to be on
    /// screen and one place for it to be.
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("IslandTuner: not in play mode"); return; }
        var existing = FindAnyObjectByType<IslandTuner>();
        if (existing == null)
            existing = new GameObject("IslandTuner").AddComponent<IslandTuner>();
        SeaSick.UI.SettingsPanel.Toggle(existing);
    }

    class Knob
    {
        public string label;
        public System.Func<TerrainSettings, float> get;
        public System.Action<TerrainSettings, float> set;
        public float step;
        public float min, max;
        public string unit = "";
        /// Shown as 1/value in metres — a frequency nobody can picture.
        public bool asScale;
    }

    Knob[] knobs;
    int sel;

    /// **Built on demand, never trusted from `Start`.**
    ///
    /// `Knob` is a plain C# class holding delegates, so Unity cannot
    /// serialise it: edit any script while the tuner is open and the domain
    /// reload brings the MonoBehaviour back with this array NULL and does
    /// NOT call `Start` again. The result is a NullReferenceException every
    /// frame out of OnGUI — which, in this editor, presents as a hung MCP
    /// bridge rather than as an error anyone notices. `SalvageSpawner` has
    /// the same note on it for the same reason; tuning is exactly when you
    /// are editing scripts, so this one would have hit constantly.
    Knob[] Knobs => knobs ?? (knobs = BuildKnobs());
    TerrainSettings ts;
    TerrainStreamer streamer;
    SeaSick.Ship.ShipMotor ship;
    string readout = "press M to measure the island in view";
    int islandIndex = -1;

    /// Also rebound on demand: these are UnityEngine.Object references so a
    /// reload DOES restore them, but the tuner can be spawned before the
    /// streamer exists and script order is not guaranteed.
    bool Bind()
    {
        if (streamer == null) streamer = FindAnyObjectByType<TerrainStreamer>();
        if (ship == null) ship = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (ts == null) ts = streamer != null ? streamer.settings : null;
        return ts != null;
    }

    Knob[] BuildKnobs()
    {
        return new[]
        {
            // **ISLAND SIZE, which was never on this panel.**
            //
            // `maskFrequency` is the continentalness frequency, and because
            // `landRatio` sets the land threshold as a QUANTILE, raising it
            // shrinks every island and makes proportionally more of them for
            // free -- the total area of land in the world does not change. So
            // this one dial is both "smaller islands" and "more places worth
            // sailing to", which is exactly the pair Kevin asked for.
            //
            // Shown as a scale in metres (1/f), because a frequency is not a
            // thing anyone can picture. Shipped at 1/1000.
            new Knob { label = "ISLAND SIZE", unit = "m", step = 50f, min = 250f, max = 2000f, asScale = true,
                get = t => t.maskFrequency, set = (t, v) => t.maskFrequency = v },
            new Knob { label = "how much of the world is land", step = 0.01f, min = 0.04f, max = 0.4f,
                get = t => t.landRatio, set = (t, v) => t.landRatio = v },

            new Knob { label = "rock relief", unit = "m", step = 2f, min = 0f, max = 120f,
                get = s => s.rockRelief, set = (s, v) => s.rockRelief = v },
            new Knob { label = "rock breaks out (soft isle)", step = 0.02f, min = 0f, max = 1f,
                get = s => s.rockThresholdSoft, set = (s, v) => s.rockThresholdSoft = v },
            new Knob { label = "rock breaks out (rocky isle)", step = 0.02f, min = 0f, max = 1f,
                get = s => s.rockThresholdHard, set = (s, v) => s.rockThresholdHard = v },
            new Knob { label = "rocky islands are rare", step = 0.1f, min = 0.2f, max = 6f,
                get = s => s.rockBias, set = (s, v) => s.rockBias = v },
            new Knob { label = "crag size", unit = "m", step = 10f, min = 30f, max = 600f, asScale = true,
                get = s => s.rockFrequency, set = (s, v) => s.rockFrequency = v },
            new Knob { label = "islets: how many", step = 0.02f, min = 0f, max = 0.8f,
                get = s => s.skerryAmount, set = (s, v) => s.skerryAmount = v },
            new Knob { label = "islets: rarity", step = 0.02f, min = 0.3f, max = 0.95f,
                get = s => s.skerryThreshold, set = (s, v) => s.skerryThreshold = v },
            new Knob { label = "islet spacing", unit = "m", step = 40f, min = 200f, max = 2500f, asScale = true,
                get = s => s.skerryFrequency, set = (s, v) => s.skerryFrequency = v },
            new Knob { label = "islet relief", unit = "m", step = 2f, min = 2f, max = 90f,
                get = s => s.skerryRelief, set = (s, v) => s.skerryRelief = v },
            new Knob { label = "green: lush vs bare", step = 0.1f, min = 0.2f, max = 5f,
                get = s => s.verdancyBias, set = (s, v) => s.verdancyBias = v },
            new Knob { label = "green: barest island keeps", step = 0.02f, min = 0f, max = 0.6f,
                get = s => s.verdancyFloor, set = (s, v) => s.verdancyFloor = v },
            new Knob { label = "rock kills green", step = 0.05f, min = 0f, max = 1f,
                get = s => s.verdancyRockSuppress, set = (s, v) => s.verdancyRockSuppress = v },
            new Knob { label = "wood thins from slope", step = 0.02f, min = 0.05f, max = 1f,
                get = s => s.vegSlopeSoft, set = (s, v) => s.vegSlopeSoft = v },
            new Knob { label = "nothing grows past slope", step = 0.02f, min = 0.1f, max = 1.5f,
                get = s => s.vegSlopeHard, set = (s, v) => s.vegSlopeHard = v },
            new Knob { label = "erosion (spurs & gullies)", step = 0.25f, min = 0f, max = 12f,
                get = s => s.erosion, set = (s, v) => s.erosion = v },
            new Knob { label = "erosion reaches", step = 0.05f, min = 0f, max = 1f,
                get = s => s.erosionAmount, set = (s, v) => s.erosionAmount = v },
            new Knob { label = "relief height", unit = "m", step = 5f, min = 10f, max = 300f,
                get = s => s.reliefHeight, set = (s, v) => s.reliefHeight = v },
            new Knob { label = "tallest island x", step = 0.25f, min = 1f, max = 12f,
                get = s => s.massifMax, set = (s, v) => s.massifMax = v },
            new Knob { label = "mountains are rare", step = 0.1f, min = 0.2f, max = 6f,
                get = s => s.massifBias, set = (s, v) => s.massifBias = v },
            new Knob { label = "upland starts at", step = 0.02f, min = 0.1f, max = 0.9f,
                get = s => s.uplandStart, set = (s, v) => s.uplandStart = v },
            new Knob { label = "lowland relief", unit = "m", step = 2f, min = 2f, max = 120f,
                get = s => s.plainRelief, set = (s, v) => s.plainRelief = v },
        };
    }

    void Update()
    {
        // Gated on `active`, which the drawer owns. Without this a tuner
        // sitting switched off in the scene still eats the arrow keys — and
        // the arrow keys are how you pick a knob on every OTHER tuner too.
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
            // Frequencies are edited as the SCALE they represent, in metres,
            // because 0.00714 is not a thing anyone can picture.
            float mult = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 4f : 1f;
            if (k.asScale)
            {
                float metres = 1f / Mathf.Max(1e-6f, k.get(ts));
                metres = Mathf.Clamp(metres + dir * k.step * mult, k.min, k.max);
                k.set(ts, 1f / metres);
            }
            else
            {
                k.set(ts, Mathf.Clamp(k.get(ts) + dir * k.step * mult, k.min, k.max));
            }
            if (streamer != null) streamer.MarkDirty();
        }

        if (kb.nKey.wasPressedThisFrame) NextIsland();
        if (kb.mKey.wasPressedThisFrame) Measure();
        if (kb.pKey.wasPressedThisFrame) Print();
    }

    /// Warp the ship to a vantage off the next island, so variety is judged
    /// by comparison rather than from memory of the last one.
    void NextIsland()
    {
        if (!Bind() || ship == null || Island.All.Count == 0) return;
        islandIndex = (islandIndex + 1) % Island.All.Count;
        var isle = Island.All[islandIndex];
        if (isle == null) return;

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
        Vector3 at = isle.transform.position + dir * (isle.RadiusAt(bestAng) + 380f);
        at.y = ship.transform.position.y;
        var rb = ship.GetComponent<Rigidbody>();
        ship.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-dir));
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        ship.AnchorPoint = at;
        readout = $"{isle.name} — press M to measure";
    }

    /// The island in view, by the same numbers RunProbe.Variety reports for
    /// the whole world, so a knob turn can be checked against the target
    /// rather than against an impression.
    void Measure()
    {
        if (!Bind()) return;
        var isle = islandIndex >= 0 && islandIndex < Island.All.Count ? Island.All[islandIndex] : null;
        if (isle == null && ship != null) isle = Island.Nearest(ship.transform.position);
        if (isle == null || Island.TerrainHeight == null) { readout = "no island"; return; }

        var prm = TerrainParams.From(ts);
        Vector3 c = isle.transform.position;
        float reach = isle.MaxRadius, step = Mathf.Max(6f, reach / 40f);
        int land = 0, rocky = 0, walk = 0; float peak = 0f;
        for (float z = -reach; z <= reach; z += step)
            for (float x = -reach; x <= reach; x += step)
            {
                float wx = c.x + x, wz = c.z + z;
                float h = Island.TerrainHeight(wx, wz);
                if (h < 0.5f) continue;
                land++;
                peak = Mathf.Max(peak, h);
                float sx = (Island.TerrainHeight(wx + 3f, wz) - Island.TerrainHeight(wx - 3f, wz)) / 6f;
                float sz = (Island.TerrainHeight(wx, wz + 3f) - Island.TerrainHeight(wx, wz - 3f)) / 6f;
                if (Mathf.Sqrt(sx * sx + sz * sz) < 0.466f) walk++;
                if (TerrainHeight.RockBreak(new float2(wx, wz),
                        TerrainHeight.Rock01(new float2(wx, wz), prm), prm) > 0.5f) rocky++;
            }
        if (land == 0) { readout = $"{isle.name}: no land found"; return; }
        float rEff = Mathf.Sqrt(land * step * step / Mathf.PI);
        readout = $"{isle.name}   {land * step * step / 10000f:F1} ha   r {rEff:F0} m   peak {peak:F0} m   "
                + $"peak/r {peak / Mathf.Max(1f, rEff):F2} (refs 0.30-0.45)   "
                + $"rock {100f * rocky / land:F1}%   walkable {100f * walk / land:F0}%";
        Debug.Log("IslandTuner: " + readout);
    }

    /// Dump every value so a session's settling can be written back into the
    /// asset — the numbers are the deliverable, not the feeling.
    void Print()
    {
        if (!Bind()) return;
        var sb = new StringBuilder();
        sb.AppendLine("// IslandTuner — settled values");
        foreach (var k in Knobs)
            sb.AppendLine(k.asScale
                ? $"{k.label,-30} {1f / k.get(ts),8:F0} m   (frequency 1/{1f / k.get(ts):F0})"
                : $"{k.label,-30} {k.get(ts),8:F3} {k.unit}");
        sb.AppendLine(readout);
        Debug.Log("ISLAND TUNER\n" + sb);
        System.IO.File.WriteAllText("/tmp/island-tune.txt", sb.ToString());
    }

    /// Drawn inside the settings drawer's rect. It used to take a panel out
    /// of (8, 8) up to 30 units wide — the top-left corner, which is where the
    /// crew pips are, and where `WaterClarityTuner` and `DevHUD` also drew.
    public void DrawTool(Rect panel)
    {
        if (!Bind()) return;
        var ks = Knobs;
        int u = UITheme.Unit;

        float y = panel.y + u * 0.5f;
        GUI.Label(new Rect(panel.x + u * 0.6f, y, panel.width, u * 1.6f),
            "↑↓ pick   ←→ change (shift ×4)   N next island   M measure   P print",
            UITheme.Small);
        y += u * 1.9f;

        for (int i = 0; i < ks.Length; i++)
        {
            var k = ks[i];
            var r = new Rect(panel.x + u * 0.6f, y, panel.width - u * 1.2f, u * 1.4f);
            if (i == sel) UITheme.Rect(r, UITheme.Track);
            float shown = k.asScale ? 1f / k.get(ts) : k.get(ts);
            string unit = k.asScale ? "m" : k.unit;
            GUI.Label(r, $"{(i == sel ? "▸ " : "  ")}{k.label}", UITheme.Small);
            GUI.Label(r, $"{shown:F3} {unit}    ", UITheme.Small2Centered);
            y += u * 1.5f;
        }

        y += u * 0.4f;
        GUI.Label(new Rect(panel.x + u * 0.6f, y, panel.width - u * 1.2f, u * 1.6f), readout, UITheme.Small);
        y += u * 1.8f;
        GUI.Label(new Rect(panel.x + u * 0.6f, y, panel.width - u * 1.2f, u * 1.6f),
            "mesh only — islands, props, dock and village keep the OLD terrain until you restart play",
            UITheme.Small);
    }
}
