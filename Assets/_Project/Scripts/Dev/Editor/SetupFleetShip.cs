using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Put one of the progression hulls under the player, so a hull can be SAILED
/// rather than looked at in a lab.
///
/// The numbers are not new: mass is 1025 x the volume the hull displaces at
/// her drawn waterline, the probe rig is `FleetLayout` lifted 0.55 x draft off
/// the drawn keel, and the damping is held at the ratio the coefficients were
/// tuned at. That is the same rule `HullFloatProbe` floats them by and the
/// same one every hull is re-massed onto, so a hull behaves here exactly as
/// she behaved in the float test.
///
/// `RunProbe.T1()` .. `T5()` wear a hull. There is no way back to the paddle
/// steamer any more: she and her `SetupPaddleBoat` were removed from the
/// project, and `Restore()` went with them. The ladder is the way to change
/// hull now -- see `Shipyard`.
public static class SetupFleetShip
{
    const string ArtDir = "Assets/_Project/Art/Ship/Hulls";
    const float Rho = 1025f;
    const float FloatRatio = 0.60f;      // mass / (rho * totalVolume)
    const float ProbeLiftRatio = 0.55f;  // above the DRAWN keel
    const float TunedMass = 19200f;      // what the drag coefficients were tuned at
    const float TunedLoa = 24.2f;

    struct Spec
    {
        public string file, label;
        public float loa, beam, draft, depth, dispVol;
        public Spec(string f, string l, float lo, float b, float dr, float de, float v)
        { file = f; label = l; loa = lo; beam = b; draft = dr; depth = de; dispVol = v; }
    }

    // Same table as HullFloatProbe. If one moves, move the other.
    static readonly Spec[] Fleet =
    {
        new Spec("hull_t1_raft",          "T1 log raft",     5.65f,  3.40f, 0.29f,  0.61f,    4.33f),
        new Spec("hull_t2_skiff",         "T2 fishing skiff", 9.00f,  2.90f, 0.55f,  1.45f,    5.20f),
        new Spec("hull_t3_sloop",         "T3 coastal sloop",15.00f,  4.80f, 1.35f,  2.55f,   45.13f),
        new Spec("hull_t4_brig",          "T4 brig",         26.00f,  7.80f, 2.50f,  5.20f,  269.56f),
        new Spec("hull_t5_shipoftheline", "T5 three-decker", 46.00f, 13.00f, 5.40f, 12.40f, 1880.80f),
    };

    public static string Wear(int tier)
    {
        var sb = new StringBuilder();
        GameObject ship = GameObject.Find("PlayerShip");
        if (ship == null) return "no PlayerShip in the open scene";
        if (tier < 1 || tier > 5) return "tier must be 1..5";
        Spec s = Fleet[tier - 1];

        var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtDir}/{s.file}.fbx");
        if (src == null) return $"missing {ArtDir}/{s.file}.fbx";

        // --- swap the visual ------------------------------------------------
        Transform old = ship.transform.Find("FleetVisual");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(src, ship.transform);
        vis.name = "FleetVisual";
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale = Vector3.one;
        sb.AppendLine($"wearing {s.label}  ({s.loa} x {s.beam} m, draft {s.draft})");

        // --- mass and buoyancy, on the fleet's own rule ----------------------
        float mass = Rho * s.dispVol;
        float k = mass / TunedMass;
        var rb = ship.GetComponent<Rigidbody>();
        if (rb != null) { rb.mass = mass; EditorUtility.SetDirty(rb); }
        sb.AppendLine($"mass {mass / 1000f:F1} t (1025 x {s.dispVol:F2} m3)");

        float keelY = -s.draft * (1f - ProbeLiftRatio);
        float railY = s.depth - s.draft;
        var probes = ship.GetComponent<BuoyancyProbeSet>();
        if (probes != null)
        {
            probes.SetProbes(BuoyancyProbeSet.FleetLayout(
                s.loa * 0.86f, s.beam * 0.9f, s.draft, keelY, railY));
            EditorUtility.SetDirty(probes);
            sb.AppendLine($"probes: probe keel {keelY:F2} (drawn {-s.draft:F2}), rail {railY:F2}");
        }

        var buoy = ship.GetComponent<BuoyantBody>();
        if (buoy != null)
        {
            var so = new SerializedObject(buoy);
            so.FindProperty("totalVolume").floatValue = s.dispVol / FloatRatio;
            so.FindProperty("linearDrag").floatValue = 28000f * k;
            so.FindProperty("quadraticDrag").floatValue = 3000f * k;
            so.FindProperty("angularDragTorque").floatValue =
                30000f * k * (s.loa / TunedLoa) * (s.loa / TunedLoa);
            so.FindProperty("inertiaBoxDims").vector3Value =
                new Vector3(s.beam, s.depth, s.loa * 0.9f);
            so.FindProperty("centreOfMass").vector3Value =
                new Vector3(0f, s.depth * 0.38f - s.draft, 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(buoy);
            sb.AppendLine($"damping x{k:F2}, velocity tau {mass / (28000f * k):F3} s");
        }

        // --- she has a sail and no engine ------------------------------------
        var motor = ship.GetComponent<ShipMotor>();
        if (motor != null)
        {
            var mso = new SerializedObject(motor);
            mso.FindProperty("windDriven").boolValue = true;
            // Hull speed, near enough: 1.25 x sqrt(waterline) m/s, then scaled
            // to sit against the steamer's 15 m/s at 24.2 m so the ladder
            // reads as a ladder rather than as physics nobody asked for.
            float top = 15f * Mathf.Sqrt(s.loa / TunedLoa);
            mso.FindProperty("maxSpeed").floatValue = top;
            mso.FindProperty("rowSpeed").floatValue = Mathf.Min(2.5f, top * 0.35f);
            // Freeboard sinks were authored against a ~2 m rail; scale to hers.
            float fb = railY / 2.2f;
            mso.FindProperty("sinkAtMarkedLine").floatValue = 0.30f * fb;
            mso.FindProperty("sinkPerOverload").floatValue = 0.44f * fb;
            mso.FindProperty("sinkAtFullBilge").floatValue = 0.26f * fb;
            // Let the sail trim: ShipMotor slerps mastPivot with the wind.
            Transform sail = null;
            foreach (var t in vis.GetComponentsInChildren<Transform>())
                if (t.name.Contains("Sail")) { sail = t; break; }
            if (sail != null) mso.FindProperty("mastPivot").objectReferenceValue = sail;
            mso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(motor);
            sb.AppendLine($"ShipMotor: windDriven ON, maxSpeed {top:F1} m/s"
                          + (sail != null ? $", sail '{sail.name}' trims with the wind" : ", no sail found"));
        }

        EditorSceneManagerDirty();
        return sb.ToString();
    }

    static void EditorSceneManagerDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
