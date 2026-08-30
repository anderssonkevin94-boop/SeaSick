using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// Do the crew actually stand ON the planking?
///
/// They were scaled 1.13x by their ROOT transforms to reach WorldScale.Person,
/// and a root scale moves every point of a body except the pivot. If a crew
/// body's lowest vertex sits at its own pivot, the feet stay put and the
/// figure grows upward; if it sits anywhere else, the feet move by the offset
/// times the scale change -- 10 cm into the deck, or 10 cm above it -- and
/// nothing in the project would have said so.
///
/// **Measure the MARGIN, not the outcome.** "Are they on the deck" answers
/// yes/no and tells you nothing about how close it came; the signed gap
/// between the lowest vertex and the highest plank under it says how much
/// room there is and which way it is going.
///
/// Everything is computed in SHIP-LOCAL space from mesh VERTICES, not from
/// world renderer bounds: bounds are world-axis-aligned, so the moment she
/// heels, a bounds minimum is a corner of a box rather than a foot, and it
/// would report a phantom sink that is really just roll.
public class DeckStandProbe : MonoBehaviour
{
    public static void Execute()
    {
        var sb = new StringBuilder();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null)
        {
            Report(sb + "no ShipMotor in the scene\n");
            return;
        }
        Transform ship = motor.transform;

        // --- the planking, in ship-local metres -----------------------------
        Transform deck = null;
        foreach (var t in ship.GetComponentsInChildren<Transform>(true))
            if (t.name == "DeckPlanks") { deck = t; break; }
        List<Vector3> planks = new List<Vector3>();
        int[] tris = null;
        if (deck != null)
        {
            var mf = deck.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
            {
                foreach (var v in mf.sharedMesh.vertices)
                    planks.Add(ship.InverseTransformPoint(deck.TransformPoint(v)));
                tris = mf.sharedMesh.triangles;
            }
        }
        if (planks.Count == 0)
        {
            Report(sb + "no readable DeckPlanks mesh -- cannot measure a gap against nothing\n");
            return;
        }
        float deckLo = float.MaxValue, deckHi = float.MinValue;
        foreach (var p in planks) { deckLo = Mathf.Min(deckLo, p.y); deckHi = Mathf.Max(deckHi, p.y); }
        sb.AppendLine($"deck: {planks.Count} verts, ship-local y {deckLo:F2} to {deckHi:F2} m "
            + $"({deckHi - deckLo:F2} m of camber and sheer -- one deck constant cannot serve it)");
        sb.AppendLine($"ship lossy scale {ship.lossyScale.x:F2}, playing {Application.isPlaying}");
        sb.AppendLine();

        // --- every body that stands on it -----------------------------------
        int sunk = 0, floating = 0, ok = 0;
        float bailWorst = float.MaxValue;
        foreach (var t in ship.GetComponentsInChildren<Transform>(true))
        {
            bool isCrew = t.name == "Helmsman" || t.GetComponent<SeaSick.Crew.CrewAgent>() != null;
            if (!isCrew) continue;

            Vector3 lowLocal, highLocal;
            if (!Extent(t, ship, out lowLocal, out highLocal)) continue;

            float height = highLocal.y - lowLocal.y;
            float root = ship.InverseTransformPoint(t.position).y;
            float plank = PlankUnder(planks, lowLocal.x, lowLocal.z);

            // The INDEPENDENT number: the planking directly beneath the foot,
            // interpolated across the triangle it lands in. `plank` above
            // reproduces the rule SetupPaddleBoat placed them by, so it can
            // only ever agree with itself; this one can disagree, and on a
            // cambered deck a "highest vertex within 1.2 m" is a nearby high
            // point rather than the board under the boot.
            float surface = SurfaceUnder(planks, tris, lowLocal.x, lowLocal.z);
            bool haveSurface = surface > float.MinValue;
            float gap = haveSurface ? lowLocal.y - surface : lowLocal.y - plank;

            string verdict;
            if (gap < -0.02f) { verdict = "SUNK into the planking"; sunk++; }
            else if (gap > 0.02f) { verdict = "FLOATING above it"; floating++; }
            else { verdict = "standing on it"; ok++; }

            sb.AppendLine($"{t.name,-14} {height:F2} m tall   scale {t.localScale.x:F3}");
            sb.AppendLine($"   feet y {lowLocal.y:F3}   deck under the foot "
                + (haveSurface ? $"{surface:F3}" : "off the planking")
                + $"   GAP {gap:+0.000;-0.000} m   {verdict}");
            sb.AppendLine($"   (placement rule said {plank:F3} -- "
                + (haveSurface ? $"{plank - surface:+0.000;-0.000} m off the real board)" : "no board here)"));
            sb.AppendLine($"   pivot y {root:F3} -> feet sit {lowLocal.y - root:+0.000;-0.000} m from their own pivot "
                + $"(at x {lowLocal.x:F2}, z {lowLocal.z:F2})");

            // A station is not the only place they stand. CrewAgent steps
            // them 55 % inboard to work a bucket and KEEPS the station's y --
            // and the deck is crowned, so inboard is uphill. Whatever the
            // resting pose measures, this is the pose a loaded ship spends
            // its time in.
            float bx = lowLocal.x * (1f - 0.55f);
            float bs = SurfaceUnder(planks, tris, bx, lowLocal.z);
            if (bs > float.MinValue)
            {
                float bgap = lowLocal.y - bs;
                bailWorst = Mathf.Min(bailWorst, bgap);
                sb.AppendLine($"   bailing at x {bx:F2}: deck {bs:F3}, "
                    + $"GAP {bgap:+0.000;-0.000} m"
                    + (bgap < -0.02f ? "   SUNK while bailing" : ""));
            }
        }

        sb.AppendLine();
        sb.AppendLine($"{ok} standing, {sunk} sunk, {floating} floating   "
            + $"(tolerance +/-0.02 m = {0.02f / WorldScale.Person * 100f:F1} % of a person)");
        if (bailWorst < float.MaxValue)
            sb.AppendLine($"worst gap at a bucket: {bailWorst:+0.000;-0.000} m");
        Report(sb.ToString());
    }

    /// Lowest and highest point of a body, in ship-local space, from mesh
    /// vertices. A crew figure is a capsule and a sphere -- a few hundred
    /// vertices -- so exactness is free here and a world-bounds corner is not
    /// worth the ambiguity it would introduce.
    static bool Extent(Transform body, Transform ship, out Vector3 low, out Vector3 high)
    {
        low = high = Vector3.zero;
        bool any = false;
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
            {
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 p = ship.InverseTransformPoint(r.transform.TransformPoint(v));
                    if (!any) { low = high = p; any = true; }
                    else
                    {
                        if (p.y < low.y) low = p;
                        if (p.y > high.y) high = p;
                    }
                }
            }
            else
            {
                // Falls back to bounds, which is only honest upright.
                Vector3 a = ship.InverseTransformPoint(r.bounds.min);
                Vector3 b = ship.InverseTransformPoint(r.bounds.max);
                if (!any) { low = a; high = b; any = true; }
                else { if (a.y < low.y) low = a; if (b.y > high.y) high = b; }
            }
        }
        return any;
    }

    /// The highest plank near a spot -- the surface a foot would rest on.
    /// Same rule SetupPaddleBoat places them by, so a disagreement here is a
    /// disagreement with the thing that put them there.
    static float PlankUnder(List<Vector3> planks, float x, float z)
    {
        float best = float.MinValue;
        float r2 = 0.36f * SetupScaleSquared;
        for (int i = 0; i < planks.Count; i++)
        {
            float dx = planks[i].x - x, dz = planks[i].z - z;
            if (dx * dx + dz * dz > r2) continue;
            if (planks[i].y > best) best = planks[i].y;
        }
        return best;
    }

    /// SetupPaddleBoat searches within 0.6 * K metres; K is its Scale over the
    /// reference, 3.4 / 1.7 = 2.
    const float SetupScaleSquared = 4f;

    /// The deck surface exactly under a spot, by dropping a ray through the
    /// triangles. Returns the HIGHEST board found, which is the one a body
    /// standing there rests on; float.MinValue if the spot is off the deck
    /// entirely -- which is itself worth knowing, and which a nearest-vertex
    /// search would quietly hide by answering with the deck edge.
    static float SurfaceUnder(List<Vector3> v, int[] tris, float x, float z)
    {
        if (tris == null) return float.MinValue;
        float best = float.MinValue;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-9f) continue;              // edge-on in plan
            float w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            float w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            float w2 = 1f - w0 - w1;
            if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
            float y = w0 * a.y + w1 * b.y + w2 * c.y;
            if (y > best) best = y;
        }
        return best;
    }

    static void Report(string s)
    {
        System.IO.File.WriteAllText("/tmp/seasick-deckstand.txt", s);
        Debug.Log("DeckStandProbe\n" + s);
    }
}
