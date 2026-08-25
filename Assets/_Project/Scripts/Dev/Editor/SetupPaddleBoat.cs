using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ocean;

/// The paddle boat cutover on Sea.unity. Strips the sloop's hull, sail and
/// rudder, drops the new boat in, and pushes every serialised value the swap
/// depends on. Idempotent: re-run after changing any number here.
///
/// Numbers are measured off the imported FBX, not guessed. At import scale
/// 1.7 she is 12.12 m overall, hull 10.43 by 4.22 m, deck top 1.23 m above
/// the designed waterline, rails at 1.56 m, wheels 2.53 m across biting
/// 0.66 m deep. The visual root is lifted so the designed waterline sits on
/// the ship origin, which is what the buoyancy probes are authored against.
///
/// Port and starboard are bound by MEASURED position, never by the object
/// names: the source model's wheel named Stbd sits to port once Blender's
/// axes are converted.
public static class SetupPaddleBoat
{
    const string FbxPath = "Assets/_Project/Art/Ship/paddle_boat.fbx";

    const float VisualYOffset = 0.55f;   // model waterline to ship origin
    const float VisualYaw = 90f;         // bow was -X in model space, wants +Z

    const float HullLength = 10.43f;
    const float HullBeam = 4.22f;
    const float KeelY = -0.51f;
    const float RailY = 1.10f;
    const float DeckY = 1.23f;

    const float StemRadius = 0.50f;
    const float BodyRadius = 0.55f;
    const float RailRadius = 0.45f;

    // Float equilibrium is held at the sloop's ratio: mass / (density x
    // volume) = 0.60, so she sits at the same relative submersion as the hull
    // every buoyancy gate was tuned against.
    const float Mass = 2400f;
    const float TotalVolume = 3.90f;

    const float WheelRadius = 1.265f;
    const float MaxSpeed = 7.5f;

    public static string Execute()
    {
        var sb = new StringBuilder();

        GameObject ship = GameObject.Find("PlayerShip");
        if (ship == null) return "no PlayerShip in the open scene";

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) return "no FBX at " + FbxPath;

        // --- strip the sailing ship's visuals -------------------------------
        string[] gone = { "Hull", "MastPivot", "RudderPivot", "PaddleBoatVisual" };
        for (int i = 0; i < gone.Length; i++)
        {
            Transform t = ship.transform.Find(gone[i]);
            if (t != null) { Object.DestroyImmediate(t.gameObject); sb.AppendLine("removed " + gone[i]); }
        }

        // --- drop the boat in ----------------------------------------------
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        visual.name = "PaddleBoatVisual";
        visual.transform.SetParent(ship.transform, false);
        visual.transform.localPosition = new Vector3(0f, VisualYOffset, 0f);
        visual.transform.localRotation = Quaternion.Euler(0f, VisualYaw, 0f);
        visual.transform.localScale = Vector3.one;
        sb.AppendLine("added PaddleBoatVisual at y " + VisualYOffset + ", yaw " + VisualYaw);

        // --- fix the axis conversion the exporter missed ---------------------
        // Blender's FBX bake converts top-level objects but leaves GRANDCHILD
        // local positions in Blender's axis order: X gets negated, Y and Z do
        // not swap. Measured, not assumed - PaddleWheel (depth 1) came out at
        // (-Xb, Zb, -Yb) and HelmWheel (depth 2) at (-Xb, Yb, Zb), which put
        // the helm wheel 0.84 m out to starboard at deck level instead of on
        // top of its stand. Same for both lanterns.
        int fixedUp = 0;
        Transform[] parts = visual.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            Transform t = parts[i];
            if (Depth(t, visual.transform) < 2) continue;
            Vector3 lp = t.localPosition;
            if (lp.sqrMagnitude < 1e-8f) continue;
            t.localPosition = new Vector3(lp.x, lp.z, -lp.y);
            sb.AppendLine("axis-fixed " + t.name + " " + lp + " -> " + t.localPosition);
            fixedUp++;
        }
        sb.AppendLine("grandchild transforms corrected: " + fixedUp);

        // --- wheels, bound by where they actually are -----------------------
        Transform wheelA = FindDeep(visual.transform, "PaddleWheel_Port");
        Transform wheelB = FindDeep(visual.transform, "PaddleWheel_Stbd");
        if (wheelA == null || wheelB == null) return "paddle wheels not found in the FBX";

        float ax = ship.transform.InverseTransformPoint(wheelA.position).x;
        float bx = ship.transform.InverseTransformPoint(wheelB.position).x;
        Transform portWheel = ax < bx ? wheelA : wheelB;
        Transform stbdWheel = ax < bx ? wheelB : wheelA;
        sb.AppendLine("port wheel = " + portWheel.name + " (local x " + Mathf.Min(ax, bx).ToString("F2")
            + "), starboard = " + stbdWheel.name + " (local x " + Mathf.Max(ax, bx).ToString("F2") + ")");

        Transform helm = FindDeep(visual.transform, "HelmWheel");

        // --- the drive ------------------------------------------------------
        PaddleDrive drive = ship.GetComponent<PaddleDrive>();
        if (drive == null) drive = ship.AddComponent<PaddleDrive>();
        var dso = new SerializedObject(drive);
        dso.FindProperty("portWheel").objectReferenceValue = portWheel;
        dso.FindProperty("starboardWheel").objectReferenceValue = stbdWheel;
        dso.FindProperty("helmWheel").objectReferenceValue = helm;
        dso.FindProperty("wheelRadius").floatValue = WheelRadius;
        dso.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine("PaddleDrive wired, wheel radius " + WheelRadius);

        // --- lanterns swing on their chains ---------------------------------
        int lanterns = 0;
        string[] lampNames = { "LanternBow", "LanternStern" };
        for (int i = 0; i < lampNames.Length; i++)
        {
            Transform lamp = FindDeep(visual.transform, lampNames[i]);
            if (lamp == null) continue;
            Renderer r = lamp.GetComponent<Renderer>();
            if (r == null) continue;

            // The pivot is the top of the chain. Placed in WORLD space so the
            // model's axis convention never has to be reasoned about.
            GameObject pivot = new GameObject(lampNames[i] + "_Pivot");
            pivot.transform.SetParent(lamp.parent, false);
            pivot.transform.position = r.bounds.center + Vector3.up * r.bounds.extents.y;
            pivot.transform.rotation = lamp.rotation;
            lamp.SetParent(pivot.transform, true);

            LanternSwing swing = pivot.AddComponent<LanternSwing>();
            var lso = new SerializedObject(swing);
            lso.FindProperty("chainLength").floatValue = Mathf.Max(0.2f, r.bounds.size.y * 0.8f);
            lso.ApplyModifiedPropertiesWithoutUndo();
            lanterns++;
        }
        sb.AppendLine("lanterns on pivots: " + lanterns);

        // --- she carries no sail --------------------------------------------
        ShipMotor motor = ship.GetComponent<ShipMotor>();
        if (motor != null)
        {
            var mso = new SerializedObject(motor);
            mso.FindProperty("windDriven").boolValue = false;
            mso.FindProperty("mastPivot").objectReferenceValue = null;
            mso.FindProperty("rudderPivot").objectReferenceValue = null;

            // Speed. The sloop's 18 m/s is 35 knots, and on a 12 m paddle
            // boat the first probe run measured her doing 20.4 m/s with her
            // deck buried in her own bow wave. Paddle propulsion tops out
            // where the rim speed does; 7.5 m/s is already a fast steamer.
            mso.FindProperty("maxSpeed").floatValue = MaxSpeed;
            mso.FindProperty("rowSpeed").floatValue = 4f;

            // Freeboard. These sink amounts were authored against the sloop's
            // ~2 m of freeboard; carried over unscaled onto 1.37 m they put
            // the deck under as soon as she is loaded.
            mso.FindProperty("sinkAtMarkedLine").floatValue = 0.30f;
            mso.FindProperty("sinkPerOverload").floatValue = 0.44f;
            mso.FindProperty("sinkAtFullBilge").floatValue = 0.26f;
            mso.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("ShipMotor: windDriven off, pivots cleared, maxSpeed " + MaxSpeed
                + ", freeboard sinks scaled to this hull");
        }

        // --- the wind arrow goes; wind now reads under the minimap ----------
        WindArrow arrow = ship.GetComponent<WindArrow>();
        if (arrow != null)
        {
            Transform built = ship.transform.Find("WindArrow");
            if (built != null) Object.DestroyImmediate(built.gameObject);
            Object.DestroyImmediate(arrow);
            sb.AppendLine("WindArrow removed (wind reads under the minimap now)");
        }

        // --- physics reshaped for this hull ---------------------------------
        Rigidbody rb = ship.GetComponent<Rigidbody>();
        if (rb != null) { rb.mass = Mass; sb.AppendLine("mass " + Mass + " kg"); }

        BuoyancyProbeSet probes = ship.GetComponent<BuoyancyProbeSet>();
        if (probes != null)
        {
            probes.SetProbes(BuoyancyProbeSet.HullLayout(
                HullLength, HullBeam, KeelY, RailY, StemRadius, BodyRadius, RailRadius));
            EditorUtility.SetDirty(probes);
            sb.AppendLine("probes: " + HullLength + " x " + HullBeam
                + ", keel " + KeelY + ", rail " + RailY
                + ", radii " + StemRadius + "/" + BodyRadius + "/" + RailRadius);
        }

        BuoyantBody buoy = ship.GetComponent<BuoyantBody>();
        if (buoy != null)
        {
            var bso = new SerializedObject(buoy);
            bso.FindProperty("totalVolume").floatValue = TotalVolume;
            bso.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("totalVolume " + TotalVolume + " m3 (float ratio "
                + (Mass / (1025f * TotalVolume)).ToString("F2") + ")");
        }

        // --- cargo stacks amidships, not over the transom -------------------
        ShipHold hold = ship.GetComponent<ShipHold>();
        if (hold != null)
        {
            var hso = new SerializedObject(hold);
            hso.FindProperty("stackOrigin").vector3Value = new Vector3(0f, DeckY, -0.6f);
            hso.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("cargo stack origin amidships");
        }

        // --- guns fit the shorter deck --------------------------------------
        CannonBattery guns = ship.GetComponent<CannonBattery>();
        if (guns != null)
        {
            var gso = new SerializedObject(guns);
            gso.FindProperty("forePosition").vector2Value = new Vector2(1.30f, 3.40f);
            gso.FindProperty("aftPosition").vector2Value = new Vector2(1.30f, 1.20f);
            gso.FindProperty("deckHeight").floatValue = DeckY;
            gso.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("guns repositioned for the 10.4 m deck");
        }

        // --- the camera frames a 12 m boat, not a 21 m one -------------------
        // Scene values were 20/13 for the sloop. Everything here is that
        // framing times the length ratio (12.1 / 21), including the storm
        // offsets, which DEV-TOOLS warns are measured from the SCENE numbers
        // and not from the code defaults.
        var cam = Object.FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
        if (cam != null)
        {
            var cso2 = new SerializedObject(cam);
            SetF(cso2, "distance", 12f);
            SetF(cso2, "height", 8f);
            SetF(cso2, "lookAhead", 12f);
            SetF(cso2, "cruiseDistance", 6.5f);
            SetF(cso2, "cruiseHeight", 3.0f);
            SetF(cso2, "cruiseLookAhead", 3.5f);
            SetF(cso2, "stormDrop", 2.4f);
            SetF(cso2, "stormPullIn", 1.8f);
            cso2.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("ChaseCamera reframed for a 12 m boat (20/13 -> 12/8)");
        }

        // --- a body at the wheel, always ------------------------------------
        Transform oldHand = ship.transform.Find("Helmsman");
        if (oldHand != null) Object.DestroyImmediate(oldHand.gameObject);

        // The helm wheel's RENDERER bounds, not its transform: HelmStand and
        // its children sit at the model origin with the offset baked into the
        // mesh, so transform.position puts the helmsman amidships.
        Vector3 helmLocal = new Vector3(0f, DeckY, -3.0f);
        Renderer helmR = helm != null ? helm.GetComponent<Renderer>() : null;
        if (helmR != null) helmLocal = ship.transform.InverseTransformPoint(helmR.bounds.center);
        BuildHand(ship.transform, new Vector3(0f, DeckY, helmLocal.z - 0.75f));
        sb.AppendLine("helmsman standing at z " + (helmLocal.z - 0.75f).ToString("F2"));

        // --- crew stand on THIS deck ----------------------------------------
        // Their stations are scene-serialised from the 21 m sloop, so without
        // this they hang in the air off the bow. CannonBattery re-posts the
        // gunners at runtime; these are the resting positions and what edit
        // mode shows.
        Vector3[] stations =
        {
            new Vector3(-1.20f, DeckY,  3.20f),
            new Vector3( 1.20f, DeckY,  3.20f),
            new Vector3(-1.20f, DeckY,  1.00f),
            new Vector3( 1.20f, DeckY,  1.00f),
            new Vector3( 0.00f, DeckY, -1.60f),
        };
        var hands = ship.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(true);
        for (int i = 0; i < hands.Length; i++)
        {
            Vector3 at = stations[i % stations.Length];
            var cso = new SerializedObject(hands[i]);
            var prop = cso.FindProperty("stationLocal");
            if (prop != null) { prop.vector3Value = at; cso.ApplyModifiedPropertiesWithoutUndo(); }
            hands[i].transform.localPosition = at;
        }
        sb.AppendLine("crew re-stationed on the new deck: " + hands.Length);

        // --- report where the moving parts ended up -------------------------
        sb.AppendLine("MEASURED, ship-local:");
        sb.AppendLine("  port wheel   " + V(ship.transform, portWheel));
        sb.AppendLine("  stbd wheel   " + V(ship.transform, stbdWheel));
        if (helm != null) sb.AppendLine("  helm wheel   " + V(ship.transform, helm));
        for (int i = 0; i < lampNames.Length; i++)
        {
            Transform lamp = FindDeep(visual.transform, lampNames[i]);
            if (lamp != null) sb.AppendLine("  " + lampNames[i].PadRight(12) + " " + V(ship.transform, lamp));
        }

        EditorSceneManager.MarkSceneDirty(ship.scene);
        EditorSceneManager.SaveScene(ship.scene);
        sb.AppendLine("scene saved");

        System.IO.File.WriteAllText("/tmp/seasick-paddleboat-setup.txt", sb.ToString());
        Debug.Log("SetupPaddleBoat:\n" + sb);
        return sb.ToString();
    }

    /// A placeholder crew body, matching the existing hands: capsule torso,
    /// sphere head, the shared CrewSkin material. This is the player character
    /// later, so it gets its own object rather than a CrewAgent that could
    /// wander off to bail.
    static GameObject BuildHand(Transform parent, Vector3 localPos)
    {
        Material skin = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/CrewSkin.mat");

        GameObject root = new GameObject("Helmsman");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPos;
        root.transform.localRotation = Quaternion.identity;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        body.transform.localScale = new Vector3(0.5f, 0.55f, 0.5f);
        if (skin != null) body.GetComponent<MeshRenderer>().sharedMaterial = skin;

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        Object.DestroyImmediate(head.GetComponent<Collider>());
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.24f, 0f);
        head.transform.localScale = Vector3.one * 0.42f;
        if (skin != null) head.GetComponent<MeshRenderer>().sharedMaterial = skin;

        return root;
    }

    static void SetF(SerializedObject so, string name, float v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.floatValue = v;
    }

    static int Depth(Transform t, Transform root)
    {
        int d = 0;
        Transform c = t;
        while (c != null && c != root) { d++; c = c.parent; }
        return d;
    }

    /// A part's renderer centre in ship-local space — where it actually is,
    /// which is the only thing worth reporting after an axis conversion.
    static string V(Transform ship, Transform part)
    {
        Renderer r = part.GetComponent<Renderer>();
        Vector3 p = r != null
            ? ship.InverseTransformPoint(r.bounds.center)
            : ship.InverseTransformPoint(part.position);
        return string.Format("({0:F2}, {1:F2}, {2:F2})", p.x, p.y, p.z);
    }

    static Transform FindDeep(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
        return null;
    }
}
