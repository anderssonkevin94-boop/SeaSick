using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.UI
{
    /// **Siting mode: pick the spot.**
    ///
    /// Kevin's flow, 2026-09-19: anchored at an island, you choose to make
    /// camp, and instead of a camp appearing you get a blueprint on the end of
    /// your thumb. You put it somewhere within reach of the ship, it stays
    /// there as a drawing, and the hands you leave behind build it.
    ///
    /// This is the interface half of that. It owns exactly three things — the
    /// preview ghost, the ring on the ground, and the tap — and it owns no
    /// rules: whether a spot will take a building is `Outpost.CanPlace`, which
    /// is the same call the real raise makes. **A ghost that goes green on one
    /// test and a raise that refuses on another is the bug this shape exists
    /// to prevent**, and the way to keep it prevented is that there is only
    /// one test.
    ///
    /// A MODE, like the sheet it is driven from: while it is running the
    /// island view still pans and zooms under it, because choosing where to
    /// put a camp is mostly looking at the island.
    public class CampSiting : MonoBehaviour
    {
        /// **How far from the ship you may site something, metres.**
        ///
        /// Kevin's rule: only within a certain radius of the ship. The number
        /// is a guess and wants a dial — the median island is 78 m in radius
        /// and the default island shot holds 165 m of ground, so 80 m from a
        /// ship lying off the beach reaches a good part of a typical island
        /// without letting you develop the far side of a big one from the
        /// water.
        public static float SiteRadius = 80f;

        public static CampSiting Instance { get; private set; }

        /// Is the player placing something right now?
        public static bool Placing => Instance != null && Instance.plan.id != null;

        /// Why the spot under the pointer is refused, or "" if it is good.
        /// The sheet prints this; the ghost's colour says the same thing
        /// faster.
        public static string Refusal { get; private set; } = "";

        BuildPlan plan;
        Outpost outpost;
        Transform ship;
        GameObject ghost;
        LineRenderer ring;
        Vector3 at;
        bool valid;

        /// **Which way it faces, in eighths of a turn.**
        ///
        /// Kevin, 2026-09-19: *"with the buildings, i'd like an option to
        /// rotate them by 45 degrees in a complete rotation to get more
        /// freedom in how i build my settlement."* R turns it, shift+R turns
        /// it back, eight steps to the full circle.
        ///
        /// Automatic until it is touched — the door faces the middle of the
        /// camp, which is what the spiral did and what a building sited by
        /// hand should still do unless somebody says otherwise. The first
        /// press freezes that facing and turns from there. Same rule as
        /// `IslandCam.Driven`, and for the same reason: an untouched thing
        /// should be exactly what was composed for it.
        int turns;
        bool turned;
        float heldYaw;

        /// What the ghost is facing right now.
        public float Yaw => turned
            ? heldYaw + turns * 45f
            : (outpost != null ? outpost.AutoYaw(at) : 0f);

        /// Eight steps to the circle.
        public const int Steps = 8;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// Start placing. Harmless to call again while already placing.
        public static void Begin(Outpost target, BuildPlan what, Transform shipTransform)
        {
            if (Instance == null || target == null) return;
            Instance.Cancel();
            Instance.outpost = target;
            Instance.plan = what;
            Instance.ship = shipTransform;
            Instance.turns = 0;
            Instance.turned = false;
            Instance.BuildRing();
        }

        public static void End() { if (Instance != null) Instance.Cancel(); }

        void Cancel()
        {
            plan = default;
            outpost = null;
            Refusal = "";
            valid = false;
            if (ghost != null) { Destroy(ghost); ghost = null; }
            if (ring != null) { Destroy(ring.gameObject); ring = null; }
        }

        void Update()
        {
            if (plan.id == null) return;
            if (outpost == null || ship == null) { Cancel(); return; }

            var keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame) { Cancel(); return; }

            if (keys != null && keys.rKey.wasPressedThisFrame)
            {
                // Freeze whatever it was facing, then turn from there, so the
                // first press does not also swing it round to north.
                if (!turned) { turned = true; heldYaw = outpost.AutoYaw(at); turns = 0; }
                bool back = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed;
                turns = (turns + (back ? Steps - 1 : 1)) % Steps;
            }

            var pointer = Pointer.current;
            if (pointer == null) return;
            Vector2 screen = pointer.position.ReadValue();

            // The sheet is a thumb-height slab across the bottom of the
            // screen and the ground behind it is not pickable. Without this
            // the button that STARTS siting is also a tap on the ground
            // directly under it, and the camp lands wherever the button was.
            if (UIBlocker.Blocked(screen)) return;

            if (!GroundPick.FromScreen(Camera.main, screen, out Vector3 ground))
            {
                Refusal = "that is not ground";
                valid = false;
                ShowGhost(false);
                return;
            }

            at = ground;
            valid = Test(at, out string why);
            // `Yaw` reads `at`, so the ghost and the test are always asking
            // about the same rectangle on the same ground.

            Refusal = why;

            Place(at);
            ShowGhost(true);

            // **Commit on tap, not on press.** LMB now grabs the land to pan
            // it, so committing on press would drop a building every time
            // the player panned while choosing a spot. `IslandInput` only
            // raises `TapThisFrame` for a press that went down and came up
            // inside the drag slop, and it does so on THIS frame, before
            // this `Update` runs (`DefaultExecutionOrder(-50)`) — so `at`
            // and `valid`, both computed above from this same frame's
            // pointer position, already describe the ground at `TapAt`.
            // That is also why this stays the file's only `Test` call: a
            // second one at `TapAt` would just be re-asking the same
            // question about the same point.
            if (valid && IslandInput.TapThisFrame)
            {
                int wanted = outpost.Site(plan, at, Yaw, out string siteWhy);
                if (wanted < 0)
                {
                    // Refused at the last moment by a test the preview does
                    // not run (something else was sited in the same frame).
                    // Say it and stay in the mode rather than dropping the
                    // player back to a sheet with no explanation.
                    Refusal = siteWhy;
                    return;
                }

                DropTheViewOn(outpost);
                Cancel();
            }
        }

        /// **Drop the view on to what was just sited**: Kevin's call, 35 m
        /// above it.
        ///
        /// The decision the player has just made is about that patch of
        /// ground, and leaving them at 165 m looking at a whole island makes
        /// them go and find it again.
        ///
        /// Public and static so the probe drives the same call the tap does.
        /// A gate that reached into the camera itself would be measuring its
        /// own copy of this.
        public static void DropTheViewOn(Outpost outpost)
        {
            if (outpost == null) return;
            var isleCam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            if (isleCam == null) return;
            isleCam.LookAt(outpost.CampCentre, CameraRig.IslandCam.BlueprintHeight);
        }

        /// The ground test, plus the one rule that is not about the ground.
        bool Test(Vector3 p, out string why)
        {
            // The ring first: it is the rule the player can SEE, so it should
            // be the reason they are given when both are broken.
            float d = Vector3.Distance(
                new Vector3(p.x, 0f, p.z), new Vector3(ship.position.x, 0f, ship.position.z));
            if (d > SiteRadius)
            {
                why = $"too far from the ship ({d:F0} m of {SiteRadius:F0})";
                return false;
            }
            return outpost.CanPlace(plan, p, Yaw, out why);
        }

        void Place(Vector3 p)
        {
            if (ghost == null)
            {
                ghost = BuildingFactory.Ghost(plan, null, p, Quaternion.identity, 0f, 0.5f);
                ghost.name = "SitingGhost";
            }
            ghost.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Yaw, 0f));
            BuildingFactory.Tint(ghost,
                valid ? BuildingFactory.GhostChalk : BuildingFactory.GhostRefused, 0.5f);
        }

        void ShowGhost(bool on)
        {
            if (ghost != null && ghost.activeSelf != on) ghost.SetActive(on);
        }

        /// The ring on the ground round the ship: the reach, drawn where the
        /// decision is being made rather than written in the sheet.
        ///
        /// Sampled against the height field so it climbs the beach instead of
        /// slicing through it, and built ONCE — she is anchored, so it does
        /// not move, and 96 evaluations of the height function every frame is
        /// not a thing to spend on a circle.
        void BuildRing()
        {
            var go = new GameObject("SiteRing");
            go.transform.SetParent(transform, false);
            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 1.1f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ring.material.SetColor("_BaseColor", new Color(0.62f, 0.78f, 0.92f, 0.6f));

            const int Segments = 96;
            ring.positionCount = Segments;
            var h = GroundPick.Height;
            Vector3 c = ship != null ? ship.position : Vector3.zero;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float x = c.x + Mathf.Cos(a) * SiteRadius;
                float z = c.z + Mathf.Sin(a) * SiteRadius;
                // Over water the field returns sea bed, so the ring would
                // disappear under the sea for most of its length. Clamp it to
                // just above sea level, which is where the player is looking
                // from anyway.
                float y = h != null ? Mathf.Max(h(x, z), 0.2f) : 0.2f;
                ring.SetPosition(i, new Vector3(x, y + 0.35f, z));
            }
        }
    }
}
