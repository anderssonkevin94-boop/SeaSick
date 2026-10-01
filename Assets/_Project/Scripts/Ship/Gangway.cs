using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// How the crew cross between ship and land.
    ///
    /// * **Off a beach**: a plank run out from her deck to the sand
    ///   (`Extend(Island)`), as it always was.
    /// * **At a pier** (2026-10-01, Kevin: "the board that appears when i
    ///   dock clips through the ship and looks out of place"): a **catwalk**
    ///   (`ExtendToPier`). Its hinge sits on the pier's sea edge, its ship end
    ///   hooks over her pier-side gunwale, and it is re-aimed every frame so
    ///   the hook stays on the rail as she heaves and rolls -- she is held on
    ///   her berth horizontally by `ShipMotor.HoldStation`, so only the tilt
    ///   and a few per cent of length change. It swings down from upright
    ///   when it is run out and back up when she casts off.
    ///
    /// The visual is `Resources/Ship/Catwalk` (art-staging/catwalk-lvl1-v1,
    /// `Dev/Editor/CatwalkImport`): three meshes -- `Catwalk` (the span,
    /// rotates about the hinge, scales on Z), `Catwalk_ShipHook` (rotates
    /// with the span, never scales, rides at its stretched end) and
    /// `Catwalk_PierChocks` (static on the pier) -- plus contract empties.
    /// Without the prefab a stand-in of chunky boxes in the same contract is
    /// built, so nothing depends on the art being imported.
    ///
    /// Crew cross deck -> rail -> catwalk -> pier: `DeckPoint` (on her deck,
    /// inboard of the rail), `RailPoint` (the catwalk's ship-end step) and
    /// `LandingPoint` (its pier-end step). On the beach plank `RailPoint` is
    /// `DeckPoint`.
    public class Gangway : MonoBehaviour
    {
        [SerializeField] float width = 1.5f;
        [SerializeField] float thickness = 0.18f;
        [SerializeField] float deckHeight = 1.9f;
        [SerializeField] float extendSpeed = 3.5f;
        [Tooltip("Seconds for the catwalk to swing down from upright (and back).")]
        [SerializeField] float catwalkSwingSeconds = 1.3f;

        // --- the catwalk contract (README, art-staging/catwalk-lvl1-v1) ---
        /// Nominal span, hinge to ship end, metres.
        public const float CatwalkLength = 3f;
        const float MinScale = 0.75f, MaxScale = 1.25f;
        /// `Rail_Seat` is this far under the walking surface at the ship end.
        const float RailSeatDrop = 0.09f;
        /// The hinge sits this far above the pier deck (`Pier_Seat` = -0.20).
        const float HingeAboveDeck = 0.20f;
        /// Where along her length the catwalk lands, ship-local Z: midships,
        /// which is where the T-berth puts the pier's axis.
        const float StationZ = 0f;

        Transform plank;
        Material mat;
        float extension;       // 0 stowed, 1 fully run out
        Vector3 shoreTarget;
        bool wanted;

        // --- catwalk state ---
        enum Mode { Beach, Pier }
        Mode mode = Mode.Beach;
        Dock pier;
        Transform cwRoot, cwSpan, cwHook, cwChocks;
        float walkStartZ = 0.25f, walkEndZ = 2.40f;
        float cwSwing;          // 0 upright/hidden, 1 down on the rail
        float cwScale = 1f;
        Quaternion cwAim = Quaternion.identity;
        Vector3 cwHinge;
        int railSide = 1;       // +1 starboard, -1 port: her side facing the pier
        Vector3 railLocal, deckLocal;
        bool railMeasured;
        float meanY;            // her running mean height, for the standoff
        bool meanSeeded;

        public bool Ready => mode == Mode.Pier ? wanted && cwSwing > 0.98f && pier != null : extension > 0.98f;

        /// Where the crossing meets the land -- crew step off here. At a pier
        /// the catwalk's pier-end step (`Walk_Start`).
        public Vector3 LandingPoint => mode == Mode.Pier && cwRoot != null
            ? CatwalkPoint(walkStartZ) : shoreTarget;

        /// Where the crossing meets the ship, on her deck. At a pier it is
        /// inboard of the pier-side rail, on the walkable deck.
        public Vector3 DeckPoint => mode == Mode.Pier && railMeasured
            ? transform.TransformPoint(deckLocal)
            : transform.TransformPoint(new Vector3(0f, deckHeight, 0f));

        /// The catwalk's ship-end step, over her rail (`Walk_End`). The beach
        /// plank has no rail step: `DeckPoint`.
        public Vector3 RailPoint => mode == Mode.Pier && cwRoot != null
            ? CatwalkPoint(walkEndZ) : DeckPoint;

        /// The pier's land end while the catwalk is down at one, else null:
        /// crew walk the pier to and from it rather than across the water.
        public Vector3? PierRoot => mode == Mode.Pier && pier != null ? pier.Landing : (Vector3?)null;

        Vector3 CatwalkPoint(float z) => cwRoot.position + cwRoot.rotation * new Vector3(0f, 0f, z * cwScale);

        void Start()
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.52f, 0.36f, 0.20f));
            mat.SetFloat("_Smoothness", 0.1f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Plank";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            plank = go.transform;
            plank.SetParent(transform, true);
            go.SetActive(false);
        }

        void OnDestroy()
        {
            if (cwRoot != null) Destroy(cwRoot.gameObject);
            if (cwChocks != null) Destroy(cwChocks.gameObject);
        }

        /// Run the plank out toward an island.
        public void Extend(Island isle)
        {
            if (isle == null) { Withdraw(); return; }
            mode = Mode.Beach;
            pier = null;
            wanted = true;

            Vector3 from = transform.position;
            Vector3 toIsle = from - isle.transform.position;
            toIsle.y = 0f;
            float ang = Mathf.Atan2(toIsle.x, toIsle.z);
            // Aim at the waterline on our bearing, then a touch inland so the
            // plank lands on sand rather than in the shallows.
            shoreTarget = isle.SurfacePoint(ang, isle.RadiusAt(ang) * 0.97f);
        }

        /// Run the plank out to a fixed point. Kept for callers outside the
        /// mooring; a pier takes `ExtendToPier`.
        public void ExtendTo(Vector3 landing)
        {
            mode = Mode.Beach;
            pier = null;
            wanted = true;
            shoreTarget = landing;
        }

        /// Lay the catwalk from this pier's sea edge onto her rail. Called
        /// every frame she lies at it; cheap after the first.
        public void ExtendToPier(Dock d, Quaternion heading)
        {
            if (d == null) { Withdraw(); return; }
            if (mode != Mode.Pier || pier != d)
            {
                mode = Mode.Pier;
                pier = d;
                railMeasured = false;
                // The beach plank, if it was out, is stowed at once.
                extension = 0f;
                if (plank != null) plank.gameObject.SetActive(false);
            }
            wanted = true;
            if (!railMeasured) MeasureRail(d, heading);
            EnsureCatwalk();
        }

        public void Withdraw() { wanted = false; }

        /// **How far seaward of the pier head her centre should lie** so the
        /// nominal 3 m catwalk spans from the head to her pier-side rail at
        /// `heading`: her rail's half-width at midships plus the catwalk's
        /// horizontal reach at the pier-deck-to-rail rise. Measured off the
        /// live hull, so a refit or a different rung gets its own number.
        public float PierBerthOffset(Dock d, Quaternion heading)
        {
            if (d == null) return 5f;
            int side = SideFacing(d, heading);
            Vector3 rail = RailLocal(side, out _);
            float shipY = meanSeeded ? meanY : transform.position.y;
            float dy = (rail.y + shipY + RailSeatDrop) - (d.Head.y + HingeAboveDeck);
            float reach = Mathf.Sqrt(Mathf.Max(0.25f, CatwalkLength * CatwalkLength - dy * dy));
            return Mathf.Abs(rail.x) + reach;
        }

        /// Which of her sides faces the pier when she lies at `heading`.
        static int SideFacing(Dock d, Quaternion heading)
        {
            Vector3 right = heading * Vector3.right;
            return Vector3.Dot(right, -d.Seaward) >= 0f ? 1 : -1;
        }

        void MeasureRail(Dock d, Quaternion heading)
        {
            railSide = SideFacing(d, heading);
            railLocal = RailLocal(railSide, out deckLocal);
            railMeasured = true;
        }

        /// **Her gunwale top at midships on one side, ship-local**, read off
        /// the hull's own meshes (the modular sections are readable): the
        /// outermost hull vertices in a thin slice at `StationZ`, and the
        /// highest of them near that outer face. Crew, guns, cargo, lanterns
        /// and the plank are not hull. `deck` comes back as the walkable deck
        /// just inboard of it (`CoasterNavigation` when she has one).
        Vector3 RailLocal(int side, out Vector3 deck)
        {
            const float Slice = 0.35f;
            var toLocal = transform.worldToLocalMatrix;
            float outMost = float.MinValue;
            var mfs = GetComponentsInChildren<MeshFilter>();
            // Pass 1: the outer face.
            foreach (var mf in mfs)
            {
                if (!IsHull(mf)) continue;
                var mx = toLocal * mf.transform.localToWorldMatrix;
                foreach (var v0 in mf.sharedMesh.vertices)
                {
                    var p = mx.MultiplyPoint3x4(v0);
                    if (Mathf.Abs(p.z - StationZ) > Slice) continue;
                    float o = p.x * side;
                    if (o > outMost) outMost = o;
                }
            }
            float railY = float.MinValue, sumX = 0f; int nX = 0;
            if (outMost > 0.5f)
            {
                // Pass 2: the top of the band within 0.5 m of the outer face.
                foreach (var mf in mfs)
                {
                    if (!IsHull(mf)) continue;
                    var mx = toLocal * mf.transform.localToWorldMatrix;
                    foreach (var v0 in mf.sharedMesh.vertices)
                    {
                        var p = mx.MultiplyPoint3x4(v0);
                        if (Mathf.Abs(p.z - StationZ) > Slice || p.x * side < outMost - 0.5f) continue;
                        if (p.y > railY + 0.03f) { railY = p.y; sumX = 0f; nX = 0; }
                        if (p.y > railY - 0.03f) { sumX += p.x * side; nX++; }
                    }
                }
            }
            float railX;
            if (railY > float.MinValue && nX > 0) railX = sumX / nX;
            else
            {
                // No readable hull: her bounds, at the old deck height.
                var b = new Bounds(Vector3.zero, Vector3.zero);
                bool any = false;
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r.GetComponentInParent<Crew.CrewAgent>() != null) continue;
                    var c = toLocal.MultiplyPoint3x4(r.bounds.center);
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
                railX = any ? Mathf.Max(1.5f, b.extents.x) : 2.5f;
                railY = deckHeight + 0.3f;
            }

            // The deck just inboard of the rail.
            Vector3 guess = new Vector3(side * Mathf.Max(0f, railX - 0.9f), railY - 1.2f, StationZ);
            var nav = GetComponentInChildren<Modular.CoasterNavigation>();
            if (nav != null && nav.NodeCount > 0)
            {
                Vector3 w = nav.ClosestWalkable(guess);
                deck = (w - guess).sqrMagnitude < 4f ? w : guess;
            }
            else deck = guess;
            return new Vector3(side * railX, railY, StationZ);
        }

        bool IsHull(MeshFilter mf)
        {
            var m = mf.sharedMesh;
            if (m == null || !m.isReadable) return false;
            var r = mf.GetComponent<Renderer>();
            if (r == null || !r.enabled) return false;
            if (plank != null && mf.transform == plank) return false;
            if (mf.GetComponentInParent<Crew.CrewAgent>() != null) return false;
            if (mf.GetComponentInParent<Cannon>() != null) return false;
            for (var t = mf.transform; t != null && t != transform; t = t.parent)
            {
                string n = t.name;
                if (n.StartsWith("Cannon") || n.Contains("Lantern") || n.Contains("Cargo")
                    || n.Contains("HoldStack") || n.Contains("Helm") || n.Contains("Chimney")
                    || n.StartsWith("Crew") || n.StartsWith("Villager"))
                    return false;
            }
            return true;
        }

        // --- the catwalk visual -------------------------------------------

        void EnsureCatwalk()
        {
            if (cwRoot != null) return;
            cwRoot = new GameObject("CatwalkRig").transform;  // world-space: it hangs off the pier
            var prefab = Resources.Load<GameObject>("Ship/Catwalk");
            if (prefab != null)
            {
                var inst = Instantiate(prefab);
                Transform span = Find(inst.transform, "Catwalk");
                Transform hook = Find(inst.transform, "Catwalk_ShipHook");
                Transform chocks = Find(inst.transform, "Catwalk_PierChocks");
                Transform ws = Find(inst.transform, "Walk_Start");
                Transform we = Find(inst.transform, "Walk_End");
                if (span != null && hook != null)
                {
                    if (ws != null) walkStartZ = ws.localPosition.z;
                    if (we != null) walkEndZ = we.localPosition.z;
                    cwSpan = span; cwHook = hook;
                    span.SetParent(cwRoot, false);
                    span.localPosition = Vector3.zero; span.localRotation = Quaternion.identity;
                    hook.SetParent(cwRoot, false);
                    hook.localRotation = Quaternion.identity;
                    if (chocks != null)
                    {
                        chocks.SetParent(null, false);
                        chocks.name = "CatwalkChocks";
                        cwChocks = chocks;
                    }
                    Destroy(inst);
                }
                else
                {
                    Debug.LogWarning("Gangway: Resources/Ship/Catwalk lacks Catwalk/Catwalk_ShipHook; using the stand-in");
                    Destroy(inst);
                }
            }
            if (cwSpan == null) BuildStandIn();
            SetCatwalkVisible(false);
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        /// Chunky boxes in the same contract as the art: span (deck, two
        /// stringers, two hand ropes on posts), a threshold hook at the ship
        /// end, two chocks on the pier.
        void BuildStandIn()
        {
            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wood.SetColor("_BaseColor", new Color(0.74f, 0.60f, 0.40f));
            wood.SetFloat("_Smoothness", 0.05f);
            var dark = new Material(wood);
            dark.SetColor("_BaseColor", new Color(0.47f, 0.35f, 0.25f));

            cwSpan = new GameObject("Catwalk").transform;
            cwSpan.SetParent(cwRoot, false);
            Box(cwSpan, wood, new Vector3(0f, -0.06f, 1.3f), new Vector3(1.1f, 0.12f, 2.6f));
            Box(cwSpan, dark, new Vector3(-0.5f, -0.17f, 1.3f), new Vector3(0.14f, 0.16f, 2.6f));
            Box(cwSpan, dark, new Vector3(0.5f, -0.17f, 1.3f), new Vector3(0.14f, 0.16f, 2.6f));
            for (int i = 0; i < 3; i++)
                for (int s = -1; s <= 1; s += 2)
                    Box(cwSpan, dark, new Vector3(s * 0.52f, 0.45f, 0.35f + i * 1.0f), new Vector3(0.13f, 0.9f, 0.13f));
            Box(cwSpan, wood, new Vector3(-0.52f, 0.86f, 1.35f), new Vector3(0.06f, 0.06f, 2.1f));
            Box(cwSpan, wood, new Vector3(0.52f, 0.86f, 1.35f), new Vector3(0.06f, 0.06f, 2.1f));

            cwHook = new GameObject("Catwalk_ShipHook").transform;
            cwHook.SetParent(cwRoot, false);
            Box(cwHook, wood, new Vector3(0f, -0.05f, -0.2f), new Vector3(1.2f, 0.1f, 0.75f));
            Box(cwHook, dark, new Vector3(0f, -0.22f, 0.4f), new Vector3(1.0f, 0.3f, 0.12f));
            Box(cwHook, dark, new Vector3(0f, -0.22f, -0.38f), new Vector3(1.0f, 0.3f, 0.12f));

            cwChocks = new GameObject("CatwalkChocks").transform;
            Box(cwChocks, dark, new Vector3(-0.65f, -0.1f, -0.1f), new Vector3(0.25f, 0.2f, 0.4f));
            Box(cwChocks, dark, new Vector3(0.65f, -0.1f, -0.1f), new Vector3(0.25f, 0.2f, 0.4f));
        }

        static void Box(Transform parent, Material m, Vector3 at, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
        }

        void SetCatwalkVisible(bool on)
        {
            if (cwRoot != null && cwRoot.gameObject.activeSelf != on) cwRoot.gameObject.SetActive(on);
            if (cwChocks != null && cwChocks.gameObject.activeSelf != on) cwChocks.gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            // Her running mean height: the standoff is worked out against
            // where her rail sits on average, not in whatever trough.
            float y = transform.position.y;
            if (!meanSeeded) { meanY = y; meanSeeded = true; }
            else meanY = Mathf.Lerp(meanY, y, 1f - Mathf.Exp(-Time.deltaTime / 6f));

            UpdateCatwalk();
            UpdatePlank();
        }

        void UpdateCatwalk()
        {
            if (cwRoot == null) return;
            bool down = wanted && mode == Mode.Pier && pier != null;
            cwSwing = Mathf.MoveTowards(cwSwing, down ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.05f, catwalkSwingSeconds));
            if (cwSwing <= 0.001f)
            {
                SetCatwalkVisible(false);
                if (!down && mode == Mode.Pier && !wanted) pier = null;
                return;
            }
            if (pier == null) { SetCatwalkVisible(false); return; }
            SetCatwalkVisible(true);

            Vector3 sea = pier.Seaward;
            Vector3 head = pier.Head;
            cwHinge = head + Vector3.up * HingeAboveDeck;

            // While she is tied up the aim follows her rail; while it swings
            // up after a cast-off it keeps the last aim, so it does not chase
            // a ship that is leaving.
            if (down && railMeasured)
            {
                Vector3 seat = transform.TransformPoint(railLocal) + Vector3.up * RailSeatDrop;
                Vector3 aim = seat - cwHinge;
                float len = aim.magnitude;
                if (len > 0.1f)
                {
                    cwAim = Quaternion.LookRotation(aim / len, Vector3.up);
                    cwScale = Mathf.Clamp(len / CatwalkLength, MinScale, MaxScale);
                }
            }

            float e = cwSwing * cwSwing * (3f - 2f * cwSwing);     // smoothstep
            Quaternion upright = Quaternion.LookRotation(Vector3.up, -sea);
            cwRoot.SetPositionAndRotation(cwHinge, Quaternion.Slerp(upright, cwAim, e));
            cwSpan.localScale = new Vector3(1f, 1f, cwScale);
            cwHook.localPosition = new Vector3(0f, 0f, CatwalkLength * cwScale);
            if (cwChocks != null)
                cwChocks.SetPositionAndRotation(cwHinge, Quaternion.LookRotation(sea, Vector3.up));
        }

        void UpdatePlank()
        {
            if (plank == null) return;

            float target = wanted && mode == Mode.Beach ? 1f : 0f;
            extension = Mathf.MoveTowards(extension, target, extendSpeed * Time.deltaTime);

            if (extension <= 0.001f)
            {
                if (plank.gameObject.activeSelf) plank.gameObject.SetActive(false);
                return;
            }
            if (!plank.gameObject.activeSelf) plank.gameObject.SetActive(true);

            Vector3 deck = transform.TransformPoint(new Vector3(0f, deckHeight, 0f));
            Vector3 span = shoreTarget - deck;
            float full = span.magnitude;
            if (full < 0.1f) return;

            Vector3 dir = span / full;
            float length = full * extension;
            Vector3 mid = deck + dir * (length * 0.5f);

            plank.position = mid;
            plank.rotation = Quaternion.LookRotation(dir, Vector3.up);
            plank.localScale = new Vector3(width, thickness, length);
        }
    }
}
