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

    /// Import scale, and the ONE number that changes the boat's size.
    /// Everything below was measured off the model at ReferenceScale and is
    /// derived from it, so doubling this doubles the hull, the probes, the
    /// mass, the camera and the freeboard together instead of leaving half of
    /// them behind in metres (which is exactly how the first cutover shipped
    /// a 12 m boat doing 35 knots).
    public const float Scale = 3.4f;
    const float ReferenceScale = 1.7f;
    static float K { get { return Scale / ReferenceScale; } }

    static float VisualYOffset { get { return 0.55f * K; } }
    const float VisualYaw = 90f;         // bow was -X in model space, wants +Z

    static float HullLength { get { return 10.43f * K; } }
    static float HullBeam { get { return 4.22f * K; } }
    static float KeelY { get { return -0.51f * K; } }
    static float RailY { get { return 1.10f * K; } }
    static float DeckY { get { return 1.23f * K; } }
    static float RailTopY { get { return 2.11f * K; } }

    static float StemRadius { get { return 0.50f * K; } }
    static float BodyRadius { get { return 0.55f * K; } }
    static float RailRadius { get { return 0.45f * K; } }

    // Displacement goes with volume, so mass is a cube law. The float ratio
    // mass / (density x volume) stays at the sloop's 0.60 either way.
    static float Mass { get { return 2400f * K * K * K; } }
    static float TotalVolume { get { return 3.90f * K * K * K; } }

    static float WheelRadius { get { return 1.265f * K; } }
    // Hull speed goes with the square root of waterline length. The constant
    // is a game number, not a physical one -- 7.5 gave a displacement hull's
    // honest 10.6 m/s at Scale 3.4, and on a 515 m storm roller that is a boat
    // being overtaken rather than a boat working a face. 14.14 puts her at a
    // round 20 m/s. The root law stays so Scale is still the one knob.
    public static float MaxSpeed { get { return 14.14f * Mathf.Sqrt(K); } }

    /// How far inboard of the deck edge a gun's centre sits.
    const float GunInset = 0.45f;

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
        // Unpack, or nothing below can restructure it: Unity refuses to
        // reparent a child out of a prefab instance, and SetParent fails
        // SILENTLY. That is how the lantern pivots ended up rotating empty
        // GameObjects while the lanterns stayed under Details, rigid. The
        // script rebuilds this whole visual from the FBX on every run, so
        // there is nothing to gain from keeping the prefab link.
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);
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

        LoadDeck(ship.transform, visual.transform);
        sb.AppendLine(deckVerts != null
            ? "deck sampled: " + deckVerts.Length + " verts, y "
                + DeckYAt(0f, 0f, DeckY).ToString("F2") + " amidships, "
                + DeckYAt(0f, 4f, DeckY).ToString("F2") + " forward"
            : "DECK NOT READABLE — everything will stand at the flat fallback");

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

        // The wheel is a thing a person's hands go on, so it does NOT scale
        // with the hull — counter-scale it back to its reference size. Guns
        // and crew are separate objects and were never affected.
        Transform helm = FindDeep(visual.transform, "HelmWheel");
        // Only the wheel. Counter-scaling HelmStand as well was wrong twice
        // over: it halves its CHILD's local position (the wheel jumped from
        // z -6.36 to -3.18, taking the helmsman with it), and the stand's own
        // mesh is offset from its origin in vertex data, so scaling about that
        // origin walks the pedestal up the deck. The pedestal grows with the
        // boat; only the thing hands go on stays human-sized.
        if (helm != null) helm.localScale = Vector3.one / K;

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
        // The pivot is parented to the SHIP, not to the model: LanternSwing
        // does its pendulum in hull axes, and the visual root carries a 90
        // degree yaw that would otherwise swap fore-and-aft for athwartships.
        // It also keeps the pivot's rest pose identity, which is what the
        // first attempt got wrong — pivot at (90,0,0) on top of a lamp at
        // (89.98,0,0) is 180 degrees out, and both lanterns hung sideways.
        int lanterns = 0;
        string[] lampNames = { "LanternBow", "LanternStern" };
        for (int i = 0; i < lampNames.Length; i++)
        {
            Transform lamp = FindDeep(visual.transform, lampNames[i]);
            if (lamp == null) continue;
            Renderer r = lamp.GetComponent<Renderer>();
            if (r == null) continue;

            Vector3 chainTop = r.bounds.center + Vector3.up * r.bounds.extents.y;

            GameObject pivot = new GameObject(lampNames[i] + "_Pivot");
            pivot.transform.SetParent(ship.transform, false);
            pivot.transform.localPosition = ship.transform.InverseTransformPoint(chainTop);
            pivot.transform.localRotation = Quaternion.identity;

            lamp.SetParent(pivot.transform, true);
            HangFromChain(lamp, pivot.transform);

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
            mso.FindProperty("rowSpeed").floatValue = 4f * Mathf.Sqrt(K);

            // Freeboard. These sink amounts were authored against the sloop's
            // ~2 m of freeboard; carried over unscaled onto 1.37 m they put
            // the deck under as soon as she is loaded.
            mso.FindProperty("sinkAtMarkedLine").floatValue = 0.30f * K;
            mso.FindProperty("sinkPerOverload").floatValue = 0.44f * K;
            mso.FindProperty("sinkAtFullBilge").floatValue = 0.26f * K;
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
            hso.FindProperty("stackOrigin").vector3Value =
                new Vector3(0f, DeckYAt(0f, -0.6f * K, DeckY), -0.6f * K);
            hso.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("cargo stack origin amidships");
        }

        // --- guns fit the shorter deck --------------------------------------
        CannonBattery guns = ship.GetComponent<CannonBattery>();
        if (guns != null)
        {
            var gso = new SerializedObject(guns);
            // Inboard of where the planking actually ends at each station —
            // at the bow the deck narrows, and a fixed offset put the fore
            // guns out through the railing.
            float foreZ = 3.40f * K, aftZ = 1.20f * K;
            float foreX = Mathf.Max(0.55f, DeckHalfWidthAt(foreZ, 1.61f * K) - GunInset);
            float aftX = Mathf.Max(0.55f, DeckHalfWidthAt(aftZ, 1.61f * K) - GunInset);
            // One deck height for the battery, taken where the guns are.
            float gunDeck = Mathf.Min(DeckYAt(foreX, foreZ, DeckY), DeckYAt(aftX, aftZ, DeckY));
            gso.FindProperty("forePosition").vector2Value = new Vector2(foreX, foreZ);
            gso.FindProperty("aftPosition").vector2Value = new Vector2(aftX, aftZ);
            gso.FindProperty("deckHeight").floatValue = gunDeck;
            sb.AppendLine("guns at x " + foreX.ToString("F2") + "/" + aftX.ToString("F2")
                + ", deck " + gunDeck.ToString("F2"));
            gso.ApplyModifiedPropertiesWithoutUndo();

        }

        // --- keep the sea out of the boat -----------------------------------
        // Sized off the real deck: the volume runs from just under the
        // planking up past the rail, and is kept inside the deck's own plan so
        // it can never cut into the sea outside the hull.
        HullWaterClip clip = ship.GetComponent<HullWaterClip>();
        if (clip == null) clip = ship.AddComponent<HullWaterClip>();
        {
            float deckLo = DeckYAt(0f, 0f, DeckY);
            float railTop = RailTopY;
            float slack = 0.25f * K;
            float halfH = (railTop - (deckLo - slack)) * 0.5f;
            float midY = (railTop + (deckLo - slack)) * 0.5f;
            // Inboard of the planking, and short of the stem and transom, so
            // the ellipse stays strictly inside the hull.
            float axisX = Mathf.Max(0.5f, DeckHalfWidthAt(0f, 1.61f * K) - 0.10f * K);
            float axisZ = 4.9f * K;
            clip.Configure(new Vector3(0f, midY, 0.25f * K), new Vector2(axisX, axisZ), halfH);
            EditorUtility.SetDirty(clip);
            sb.AppendLine("hull water clip: centre y " + midY.ToString("F2")
                + ", axes " + axisX.ToString("F2") + " x " + axisZ.ToString("F2")
                + ", half-height " + halfH.ToString("F2"));
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
            SetF(cso2, "distance", 12f * K);
            SetF(cso2, "height", 8f * K);
            SetF(cso2, "lookAhead", 12f * K);
            SetF(cso2, "cruiseDistance", 6.5f * K);
            SetF(cso2, "cruiseHeight", 3.0f * K);
            SetF(cso2, "cruiseLookAhead", 3.5f * K);
            SetF(cso2, "stormDrop", 2.4f * K);
            SetF(cso2, "stormPullIn", 1.8f * K);
            cso2.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine("ChaseCamera reframed: distance " + (12f * K).ToString("F1") + ", height " + (8f * K).ToString("F1"));
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
        float handZ = helmLocal.z - 0.75f;
        float handY = DeckYAt(0f, handZ, DeckY);
        BuildHand(ship.transform, new Vector3(0f, handY, handZ));
        sb.AppendLine("helmsman standing at z " + handZ.ToString("F2") + ", deck y " + handY.ToString("F2"));

        // --- crew stand on THIS deck ----------------------------------------
        // Their stations are scene-serialised from the 21 m sloop, so without
        // this they hang in the air off the bow. CannonBattery re-posts the
        // gunners at runtime; these are the resting positions and what edit
        // mode shows.
        Vector2[] spots =
        {
            new Vector2(-1.05f * K,  3.20f * K),
            new Vector2( 1.05f * K,  3.20f * K),
            new Vector2(-1.05f * K,  1.00f * K),
            new Vector2( 1.05f * K,  1.00f * K),
            new Vector2( 0.00f,      -1.60f * K),
        };
        Vector3[] stations = new Vector3[spots.Length];
        for (int i = 0; i < spots.Length; i++)
            stations[i] = new Vector3(spots[i].x, DeckYAt(spots[i].x, spots[i].y, DeckY), spots[i].y);
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

    /// Hang a lantern properly, by MEASURING it rather than deriving Euler
    /// angles through an axis conversion that has already been wrong twice.
    /// Point the mesh's longest axis straight down, work out which end is the
    /// chain (it is the thinner one) and put that end up, then slide the lamp
    /// so the top of the chain sits on the pivot.
    static void HangFromChain(Transform lamp, Transform pivot)
    {
        MeshFilter mf = lamp.GetComponent<MeshFilter>();
        Renderer r = lamp.GetComponent<Renderer>();
        if (mf == null || mf.sharedMesh == null || r == null) return;
        Mesh m = mf.sharedMesh;

        Vector3 ext = m.bounds.size;
        Vector3 longAxis = Vector3.forward;
        if (ext.x >= ext.y && ext.x >= ext.z) longAxis = Vector3.right;
        else if (ext.y >= ext.z) longAxis = Vector3.up;

        // The thinnest axis is the flat of the ring and the lantern's glass,
        // and it wants to face athwartships so the ring hangs fore-and-aft
        // through its bracket. FromToRotation alone only pins the long axis
        // and leaves the roll about it arbitrary, which is why the ring came
        // out flat and sticking out sideways from the beam.
        Vector3 thinAxis = Vector3.forward;
        if (ext.x <= ext.y && ext.x <= ext.z) thinAxis = Vector3.right;
        else if (ext.y <= ext.z) thinAxis = Vector3.up;

        lamp.localPosition = Vector3.zero;
        lamp.localRotation = Aim(longAxis, thinAxis, Vector3.down, Vector3.right);

        // Which end is the chain? Compare how far the mesh spreads sideways
        // in each half along the long axis; the chain is the thin end.
        if (m.isReadable)
        {
            Vector3[] v = m.vertices;
            float mid = Vector3.Dot(m.bounds.center, longAxis);
            float spreadHi = 0f, spreadLo = 0f;
            int nHi = 0, nLo = 0;
            for (int i = 0; i < v.Length; i++)
            {
                float along = Vector3.Dot(v[i], longAxis);
                Vector3 side = v[i] - longAxis * along;
                if (along >= mid) { spreadHi += side.magnitude; nHi++; }
                else { spreadLo += side.magnitude; nLo++; }
            }
            if (nHi > 0) spreadHi /= nHi;
            if (nLo > 0) spreadLo /= nLo;
            // After FromToRotation the +longAxis end points DOWN. The thin
            // end belongs up, so flip when the thin end is the one facing down.
            bool thinEndIsDown = spreadHi < spreadLo;
            if (thinEndIsDown)
                lamp.localRotation = Aim(longAxis, thinAxis, Vector3.up, Vector3.right);
        }

        // Slide it so the top of the chain meets the pivot.
        Vector3 top = r.bounds.center + Vector3.up * r.bounds.extents.y;
        lamp.position += pivot.position - top;
    }

    /// A rotation taking local axis a onto world axis A and local b onto B.
    /// Two constraints, so nothing is left arbitrary — which is the whole
    /// point: one constraint fixes which way a thing points and says nothing
    /// about how it is rolled around that direction.
    static Quaternion Aim(Vector3 a, Vector3 b, Vector3 A, Vector3 B)
    {
        Quaternion from = Quaternion.LookRotation(a, b);
        Quaternion to = Quaternion.LookRotation(A, B);
        return to * Quaternion.Inverse(from);
    }

    // ---- the deck is not flat -------------------------------------------
    // It has camber and sheer and spans 0.67 m in height, so one deck
    // constant leaves everything amidships standing in the air. These sample
    // the real planking.
    static Vector3[] deckVerts;
    static Transform deckT, shipT;

    static void LoadDeck(Transform ship, Transform visual)
    {
        deckVerts = null; deckT = null; shipT = ship;
        Transform d = FindDeep(visual, "DeckPlanks");
        if (d == null) return;
        MeshFilter mf = d.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
        Mesh m = mf.sharedMesh;
        Vector3[] v = m.vertices;
        Vector3[] local = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
            local[i] = ship.InverseTransformPoint(d.TransformPoint(v[i]));
        deckVerts = local;
        deckT = d;
    }

    /// Highest deck vertex near this spot, in ship-local metres.
    static float DeckYAt(float x, float z, float fallback)
    {
        if (deckVerts == null) return fallback;
        float best = float.MinValue;
        for (int i = 0; i < deckVerts.Length; i++)
        {
            Vector3 p = deckVerts[i];
            float dx = p.x - x, dz = p.z - z;
            if (dx * dx + dz * dz > 0.36f * K * K) continue;
            if (p.y > best) best = p.y;
        }
        return best > float.MinValue ? best : fallback;
    }

    /// How far out the planking reaches at this station, so a gun is placed
    /// inboard of the deck edge instead of hanging through the railing where
    /// the hull narrows toward the bow.
    static float DeckHalfWidthAt(float z, float fallback)
    {
        if (deckVerts == null) return fallback;
        float best = 0f;
        for (int i = 0; i < deckVerts.Length; i++)
        {
            Vector3 p = deckVerts[i];
            if (Mathf.Abs(p.z - z) > 0.5f * K) continue;
            float ax = Mathf.Abs(p.x);
            if (ax > best) best = ax;
        }
        return best > 0.1f ? best : fallback;
    }

    static Transform FindDeep(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
        return null;
    }
}
