using System.Collections;
using System.Text;
using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

/// Does a mountain sea maul rather than kill, and does meeting it bow-on
/// actually save you? Drives the ship straight into one at four angles.
public class MountainProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-mountain-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("MountainProbe: not in play mode"); return; }
        new GameObject("MountainProbe").AddComponent<MountainProbe>();
    }

    StringBuilder log = new StringBuilder();
    void Line(string s) { log.AppendLine(s); System.IO.File.WriteAllText(OutPath, log.ToString()); }

    ShipMotor ship;
    HullIntegrity hull;
    Bilge bilge;
    SeaSick.Voyage.VoyageManager voyage;

    IEnumerator Start()
    {
        ship = FindAnyObjectByType<ShipMotor>();
        hull = ship != null ? ship.GetComponent<HullIntegrity>() : null;
        bilge = ship != null ? ship.GetComponent<Bilge>() : null;
        voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        if (ship == null) { Line("FAIL: no ship"); yield break; }

        var water = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        water.SetColor("_BaseColor", new Color(0.07f, 0.24f, 0.38f, 1f));

        Line("A mountain sea run down at four angles. It must MAUL, never kill:");
        Line("she has to still be sailing on the far side every time.");
        Line("");
        Line("angle\thull\tbilge\troll\tspeed\tcargo\tafloat");

        // 0 = bow into it, 90 = square on the beam.
        foreach (float angle in new[] { 0f, 30f, 60f, 90f })
        {
            // Fresh ship each run so the numbers are comparable.
            if (hull != null) hull.FullRepair();
            if (bilge != null) bilge.Dry();
            if (voyage != null)
            {
                voyage.Jettison(999);
                for (int i = 0; i < 20; i++) voyage.AddLoot(1, "Timber");
            }

            Vector3 spot = new Vector3(0f, 0f, -900f);
            ship.transform.position = new Vector3(spot.x, ship.transform.position.y, spot.z);

            // The wall runs due east; she meets it at `angle` off dead-on.
            Vector2 travel = new Vector2(1f, 0f);
            var wall = MountainSea.Spawn(spot + new Vector3(-260f, 0f, 0f), travel, water);

            // Point her: 0 means bow into the oncoming wall (facing west).
            SetHeading(270f + angle);
            yield return new WaitForSeconds(0.2f);

            float t0 = Time.time;
            while (Time.time - t0 < 30f && MountainSea.LastStrikeTime < t0)
                yield return null;

            // Roll is a damped spring. Follow it for a couple of seconds and
            // keep the peak, or you sample it already settled.
            float peakRoll = 0f;
            float watch = Time.time;
            while (Time.time - watch < 2.5f)
            {
                float r = Mathf.Max(
                    Mathf.Abs(NormalizeAngle(ship.transform.eulerAngles.z)),
                    Mathf.Abs(ship.KnockdownRoll));
                if (r > peakRoll) peakRoll = r;
                yield return null;
            }

            bool struck = MountainSea.LastStrikeTime >= t0;
            Line($"{angle:F0}°\t{(hull != null ? hull.Integrity01 : -1f):F2}\t" +
                 $"{(bilge != null ? bilge.Bilge01 : -1f):F2}\t{peakRoll:F0}°\t" +
                 $"{ship.CurrentSpeed:F1}\t{(voyage != null ? voyage.TotalHeld : -1)}\t" +
                 $"{(struck ? (ship.CurrentSpeed >= 0f ? "yes" : "?") : "NO STRIKE")}");

            if (wall != null) Destroy(wall.gameObject);
            yield return new WaitForSeconds(0.5f);
        }

        Line("");
        Line("Every row must show a ship still afloat with hull > 0.");
        Line("DONE");
    }

    void SetHeading(float deg)
    {
        var f = typeof(ShipMotor).GetField("heading",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(ship, deg);
    }

    static float NormalizeAngle(float d) => d > 180f ? d - 360f : d;
}
