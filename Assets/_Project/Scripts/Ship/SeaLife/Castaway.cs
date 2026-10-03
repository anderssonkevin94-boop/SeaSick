using System.Collections.Generic;
using UnityEngine;
using SeaSick.Ship.Overboard;
using SeaSick.Ship.Harpoon;
using SeaSick.World.Life;

namespace SeaSick.Ship.SeaLife
{
    /// **A stranger clinging to a board in open water** (Kevin, 2026-09-30:
    /// *"Please add so people can be found floating in the water."*).
    /// Spawned and resolved by `Voyage.CastawaySpawner`, which owns the
    /// Pull aboard button, the boarding and the landing at a camp; this is
    /// only the thing in the sea.
    ///
    /// **The body is Astra's deckhand** (`Resources/AstraPlaytest/
    /// DeckhandVisual`, the same figure the helmsman wears), NOT a
    /// `CrewAgent` clone: a person in the water is nobody's crew yet, so no
    /// roster, save or name census (`CrewNames.InUse`) may find a body here.
    /// Rig scale trap (memory `astra-rig-scale-trap`): the FBX root is ×100
    /// with ~92× bones, so the prefab is instanced whole under a unit-scale
    /// root, its own root scale untouched, and nothing is parented to a bone.
    /// The wave only ROTATES `upper_arm` toward "up" after the Animator has
    /// posed it, which is scale-free.
    ///
    /// Half in the water: the art is sunk `ArtSink` so the waterline sits at
    /// the chest -- head, shoulders and one waving arm show -- with Astra's
    /// `LashedBoardBundle` (or `BrokenBoardLong`) across them at the surface.
    ///
    /// Implements `IOverboardTarget` with `Boardable = false` so `RescueHud`
    /// draws its edge arrow and tap-to-steer for it (like a fish shoal) but
    /// never runs its own sail-over boarding: pulling them aboard is the
    /// spawner's button.
    public class Castaway : MonoBehaviour, IOverboardTarget, IHarpoonable
    {
        static readonly List<Castaway> all = new List<Castaway>();
        public static IReadOnlyList<Castaway> All => all;

        /// Metres the deckhand is lowered so the waterline is at the chest
        /// (the figure is 1.7 m, shoulders at ~1.42 m).
        const float ArtSink = 1.2f;
        /// Board art height against the root (which rides the surface):
        /// same +0.03 m the salvage floaters' boards end up at.
        const float BoardLift = 0.03f;
        const int RingSegments = 24;

        public string CastawayName { get; private set; }
        public bool Resolved { get; private set; }
        public Vector3 WorldPosition => transform.position;
        public float TimeLeft01 => timeTotal > 0f ? Mathf.Clamp01(timeLeft / timeTotal) : 0f;
        /// Being pulled in by the spawner after Pull aboard was tapped.
        public bool Pulling { get; private set; }
        /// Set by the spawner while the Pull aboard button is up. `RescueHud`
        /// drops its tap-to-steer zone for them then, so a tap meant for the
        /// button can never land on the steer zone instead.
        public bool InPullReach { get; set; }

        // --- IOverboardTarget ------------------------------------------------
        Transform IOverboardTarget.Transform => transform;
        string IOverboardTarget.Label => CastawayName;
        /// A person: first in any list of things in the water.
        int IOverboardTarget.RescuePriority => 0;
        /// `RescueHud` must not auto-board them; the button does.
        bool IOverboardTarget.Boardable => false;
        public Transform Hull => ship;
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }
        /// Reached when the bow harpoon reels them to the rail -- routed to the
        /// same commit the Pull aboard button uses.
        void IOverboardTarget.OnHauled(string rescuerName) => Hauled?.Invoke(this, rescuerName);

        // --- IHarpoonable: a line to the board they cling to. Crew, not cargo:
        // no hold room needed. ------------------------------------------------
        Transform IHarpoonable.Transform => transform;
        public Vector3 HookPoint => transform.position + Vector3.up * 0.2f;
        string IHarpoonable.HarpoonLabel => "castaway";
        public bool CanBeHarpooned => !Resolved && !Pulling && !BeingHauled;
        public float HarpoonMass => 1.2f;
        public int HarpoonHoldUnits => 0;

        /// Raised by `OnHauled`; the spawner listens.
        public static event System.Action<Castaway, string> Hauled;

        Transform ship;
        Vector3 driftVel;
        float timeLeft, timeTotal = 1f;
        float phase;
        float yaw;
        Transform upperArm, foreArm;
        float armSide = 1f;
        LineRenderer ring, line;
        Material ringMat, lineMat;
        Vector3 pullTo;

        /// **Spawn one.** `rng` is the spawner's own stream (never the
        /// world-build one), used for the drift heading, the board and the
        /// wave phase, so nothing here draws from `UnityEngine.Random`.
        public static Castaway Spawn(string name, Transform hull, Vector3 worldPos, System.Random rng)
        {
            if (string.IsNullOrEmpty(name) || hull == null) return null;
            var go = new GameObject("Castaway_" + name);
            go.transform.position = worldPos;
            var c = go.AddComponent<Castaway>();
            c.CastawayName = name;
            c.ship = hull;
            c.timeTotal = Mathf.Max(30f, RecruitTuning.SeaCastawayLifeSeconds);
            c.timeLeft = c.timeTotal;
            double r = rng != null ? rng.NextDouble() : 0.5;
            float ang = (float)(r * Mathf.PI * 2.0);
            c.driftVel = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * RecruitTuning.SeaCastawayDriftSpeed;
            c.phase = (float)((rng != null ? rng.NextDouble() : 0.3) * 10.0);
            bool bundle = rng == null || rng.Next(2) == 0;
            c.BuildVisual(bundle);
            c.FaceShip(1f);
            all.Add(c);
            HarpoonRegistry.Add(c);
            return c;
        }

        void BuildVisual(bool bundle)
        {
            // --- the person ---------------------------------------------------
            var prefab = Resources.Load<GameObject>("AstraPlaytest/DeckhandVisual");
            if (prefab != null)
            {
                // Whole prefab under a unit-scale root, its own root scale
                // and rotation kept (the ×100 rig lives INSIDE it).
                var go = Instantiate(prefab, transform, false);
                go.name = "Deckhand";
                var lp = go.transform.localPosition;
                go.transform.localPosition = new Vector3(lp.x, lp.y - ArtSink, lp.z);
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "upper_arm.R") upperArm = t;
                    else if (t.name == "forearm.R") foreArm = t;
                }
                // The wave aims the upper arm by where its forearm ends up;
                // that only converges if the forearm actually hangs off it.
                if (upperArm == null || foreArm == null || foreArm.parent != upperArm)
                {
                    upperArm = null;
                    foreArm = null;
                }
            }
            else
            {
                // Missing model: a head and shoulders out of primitives.
                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(transform, false);
                head.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                head.transform.localScale = Vector3.one * 0.32f;
            }

            // --- the board ----------------------------------------------------
            // Across the chest at the surface, turned a quarter so it lies
            // across the body rather than along the facing.
            var board = SeaKit.Spawn(bundle ? SeaKit.LashedBoardBundle : SeaKit.BrokenBoardLong,
                transform, new Vector3(0f, BoardLift, 0.15f));
            if (board != null) board.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            else
            {
                var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "Plank";
                Destroy(plank.GetComponent<Collider>());
                plank.transform.SetParent(transform, false);
                plank.transform.localPosition = new Vector3(0f, 0f, 0.15f);
                plank.transform.localScale = new Vector3(2.2f, 0.15f, 0.4f);
            }

            // --- the ring and the pull line ----------------------------------
            ringMat = MakeLineMat();
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(transform, false);
            ring = ringGo.AddComponent<LineRenderer>();
            SetupLine(ring, ringMat, RingSegments, true);

            lineMat = MakeLineMat();
            if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", new Color(0.92f, 0.85f, 0.65f, 1f));
            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(transform, false);
            line = lineGo.AddComponent<LineRenderer>();
            SetupLine(line, lineMat, 2, false);
            line.widthMultiplier = 0.06f;
            line.enabled = false;
        }

        static void SetupLine(LineRenderer lr, Material mat, int points, bool loop)
        {
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = points;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            lr.material = mat;
        }

        /// Same transparent unlit setup `Swimmer`'s ring uses.
        static Material MakeLineMat()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        /// **Tapped Pull aboard.** From now on they are drawn toward
        /// `railPoint` with a line, and the spawner commits once they are
        /// alongside (or after its own short timer).
        public void BeginPull(Vector3 railPoint)
        {
            if (Resolved) return;
            Pulling = true;
            pullTo = railPoint;
            if (line != null) line.enabled = true;
        }

        public void UpdatePullAnchor(Vector3 railPoint) => pullTo = railPoint;

        /// The spawner re-points this every frame at the live hull, so a
        /// refit that swaps the ship object never leaves it measuring reach
        /// against a destroyed one.
        public void SetHull(Transform hull) { if (hull != null) ship = hull; }

        /// Gone for good: pulled aboard (the spawner has already boarded
        /// them) or drifted off. Either way the thing in the water goes.
        public void Resolve()
        {
            if (Resolved) return;
            Resolved = true;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            all.Remove(this);
            HarpoonRegistry.Remove(this);
            if (ringMat != null) Destroy(ringMat);
            if (lineMat != null) Destroy(lineMat);
        }

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 target = BeingHauled ? HaulAnchor : pullTo;
            Vector3 pos = (Pulling || BeingHauled)
                ? Vector3.MoveTowards(transform.position, new Vector3(target.x, transform.position.y, target.z),
                    OverboardTuning.HaulPullSpeed * 1.6f * dt)
                : transform.position + driftVel * dt;
            if (Ocean.OceanSampler.Ready)
                pos.y = Ocean.OceanSampler.SampleImmediate(pos).height;
            transform.position = pos;

            FaceShip(dt * 1.2f);

            if (!Pulling && !BeingHauled)
            {
                timeLeft -= dt;
                if (timeLeft <= 0f) { Resolve(); return; }
            }

            UpdateRing();
            UpdateLine();
        }

        /// Turn slowly to face the ship (so the wave is at you), with a gentle
        /// rock on top of the wave's own motion.
        void FaceShip(float t)
        {
            if (ship != null)
            {
                Vector3 to = ship.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                    yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, Mathf.Clamp01(t));
            }
            float s = Time.time + phase;
            transform.rotation = Quaternion.Euler(Mathf.Sin(s * 0.9f) * 6f, yaw, Mathf.Cos(s * 0.7f) * 6f);
        }

        /// **The wave**, after the Animator has posed the arm this frame.
        /// Points the upper arm up and a little out to its own side, swinging
        /// ±18 degrees; the forearm swings a little more. Rotations only --
        /// nothing is parented to the bone (rig scale trap).
        void LateUpdate()
        {
            if (Resolved || upperArm == null || foreArm == null) return;
            Vector3 up = transform.up, fwd = transform.forward;
            // Which side of the body this arm is on, measured, not assumed
            // from the bone's name.
            armSide = Vector3.Dot(upperArm.position - transform.position, transform.right) >= 0f ? 1f : -1f;
            float s = Time.time * (Pulling ? 2.2f : 5.5f) + phase;
            float tilt = (Pulling ? 10f : 22f) + Mathf.Sin(s) * (Pulling ? 5f : 18f);
            // AngleAxis(+a, fwd) turns `up` toward -right, so the arm's own
            // side needs the negative.
            Vector3 want = Quaternion.AngleAxis(-armSide * tilt, fwd) * up;
            Vector3 have = foreArm.position - upperArm.position;
            if (have.sqrMagnitude < 1e-8f) return;
            upperArm.rotation = Quaternion.FromToRotation(have.normalized, want) * upperArm.rotation;
        }

        void UpdateRing()
        {
            if (ring == null) return;
            float t = TimeLeft01;
            Color col = t > 0.35f ? new Color(1f, 0.93f, 0.7f) : new Color(1f, 0.6f, 0.2f);
            col.a = 0.8f;
            if (ringMat.HasProperty("_BaseColor")) ringMat.SetColor("_BaseColor", col);

            var cam = Camera.main;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 30f;
            float legibility = Mathf.Clamp(camDist / 28f, 0.8f, 2.8f);
            float radius = 1.7f * legibility;
            ring.widthMultiplier = 0.09f * legibility;

            Vector3 c = transform.position;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(c.x + Mathf.Cos(a) * radius, c.y + 0.08f, c.z + Mathf.Sin(a) * radius));
            }
        }

        void UpdateLine()
        {
            if (line == null || !line.enabled) return;
            line.SetPosition(0, new Vector3(pullTo.x, pullTo.y + 1.6f, pullTo.z));
            line.SetPosition(1, transform.position + Vector3.up * 0.4f);
        }

        /// The nearest point on the hull's SIDE -- same rectangle
        /// `MessageBottle`/`Swimmer` use for their reach check.
        public Vector3 NearestHullSide()
        {
            if (ship == null) return transform.position;
            Vector3 local = ship.InverseTransformPoint(transform.position);
            var motor = ship.GetComponent<ShipMotor>();
            float halfLen = motor != null ? Mathf.Max(1f, motor.HullLength * 0.5f) : 12f;
            float halfBeam = OverboardTuning.HullHalfBeamMetres;
            float side = Mathf.Sign(local.x != 0f ? local.x : 1f);
            float z = Mathf.Clamp(local.z, -halfLen, halfLen);
            return ship.TransformPoint(new Vector3(halfBeam * side, 0f, z));
        }
    }
}
