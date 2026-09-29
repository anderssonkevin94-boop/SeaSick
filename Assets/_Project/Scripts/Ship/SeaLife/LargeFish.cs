using UnityEngine;
using SeaSick.Ocean;

namespace SeaSick.Ship.SeaLife
{
    /// **Astra's large swimming fish** (approved 2026-09-29,
    /// `art-staging/large-fish-v1`): one 5.9 m ambient fish cruising under the
    /// water near the ship. Scenery only -- no collider, no gameplay.
    ///
    /// - **Spawning** is `SeaLifeDirector.TickLargeFish`: one at a time, ahead
    ///   of the bow, clear of land. It despawns once it is more than
    ///   `SeaLifeTuning.LargeFishDespawnMetres` from the ship; the director
    ///   brings the next one.
    /// - **Movement**: a slow wander (broad turns, `LargeFishTurnDegPerSec`
    ///   at most) at 2-3 m/s, with a "pass the ship" leg every so often that
    ///   steers it to a point just ahead of the bow so it gets seen, and a
    ///   steer home when it strays. Land ahead turns it away.
    /// - **Depth**: the ENTIRE dorsal fin stays below the local surface. One
    ///   `OceanSampler.SampleImmediate` per frame at the fin; the root sits
    ///   `dorsalTop + LargeFishFinClearance` below it, and the smoothing may
    ///   only lag DOWNWARD -- a rising surface is never allowed to leave the
    ///   fin above the water.
    /// - **Visibility**: see `FishUnderwater.shader`. Where the ocean tier has
    ///   refraction off (the phone), the see-through material is appended as
    ///   a second material on the one-submesh skinned mesh, so the mesh is
    ///   skinned once and drawn once opaque (hidden by the water) and once
    ///   through the water. Where refraction is on (PC) only the opaque
    ///   material is used; the ocean shows it through `_CameraOpaqueTexture`.
    public class LargeFish : MonoBehaviour
    {
        public const string ResourcePath = "SeaLife/LargeFish";

        public static LargeFish Active { get; private set; }

        [SerializeField] private Animator animator;
        [SerializeField] private SkinnedMeshRenderer body;
        [SerializeField] private Material opaqueMaterial;
        [SerializeField] private Material underwaterMaterial;

        static readonly int SurfaceY = Shader.PropertyToID("_FishSurfaceY");

        Transform ship;
        float yaw;
        float yawRate;
        float wanderTarget;
        float speed;
        float speedTarget;
        float nextWanderPick;
        float nextPassAt;
        float passUntil;
        float passSide = 1f;
        float nextLandCheck;
        bool landAhead;
        float bank;
        float y;
        bool yInit;
        float lastSurface;
        float dorsalTop = 1.6f;
        MaterialPropertyBlock mpb;
        bool seeThrough;

        /// Instantiates the fish at `spot` (y ignored), heading `headingDeg`.
        public static LargeFish Spawn(Transform hull, Vector3 spot, float headingDeg)
        {
            if (hull == null || Active != null) return null;
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null) { Debug.LogWarning("[LargeFish] no prefab at Resources/" + ResourcePath); return null; }
            var go = Instantiate(prefab, new Vector3(spot.x, -4f, spot.z), Quaternion.Euler(0f, headingDeg, 0f));
            go.name = "LargeFish";
            var fish = go.GetComponent<LargeFish>();
            if (fish == null) { Destroy(go); return null; }
            fish.ship = hull;
            fish.yaw = headingDeg;
            fish.wanderTarget = headingDeg;
            return fish;
        }

        void Awake()
        {
            if (Active != null && Active != this) { Destroy(gameObject); return; }
            Active = this;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (body == null) body = GetComponentInChildren<SkinnedMeshRenderer>();
            mpb = new MaterialPropertyBlock();

            // Highest point above the root, measured on the real mesh rather
            // than assumed (Astra FBX: x100 root scale, see astra-rig-scale-trap).
            if (body != null)
            {
                body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                body.receiveShadows = false;
                float top = body.bounds.max.y - transform.position.y;
                if (top > 0.2f && top < 5f) dorsalTop = top;
            }

            var q = OceanQuality.Active;
            seeThrough = q != null && !q.refraction && underwaterMaterial != null;
            if (body != null && opaqueMaterial != null)
                body.sharedMaterials = seeThrough
                    ? new[] { opaqueMaterial, underwaterMaterial }   // extra slot re-draws the one submesh
                    : new[] { opaqueMaterial };

            speed = speedTarget = SeaLifeTuning.LargeFishSpeed;
            nextWanderPick = Time.time + Random.Range(2f, 5f);
            nextPassAt = Time.time + Random.Range(6f, 14f);
        }

        void OnDestroy() { if (Active == this) Active = null; }

        void Update()
        {
            if (ship == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = transform.position;
            Vector3 toShip = ship.position - pos; toShip.y = 0f;
            float shipDist = toShip.magnitude;
            if (shipDist > SeaLifeTuning.LargeFishDespawnMetres) { Destroy(gameObject); return; }

            Steer(pos, toShip, shipDist, dt);

            // --- heading: broad turns, rate-limited and eased ---
            float err = Mathf.DeltaAngle(yaw, wanderTarget);
            float maxRate = SeaLifeTuning.LargeFishTurnDegPerSec;
            float wantRate = Mathf.Clamp(err * 0.4f, -maxRate, maxRate);
            yawRate = Mathf.MoveTowards(yawRate, wantRate, maxRate * 0.5f * dt);
            yaw += yawRate * dt;

            speed = Mathf.MoveTowards(speed, speedTarget, 0.4f * dt);
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            pos += fwd * (speed * dt);

            // --- depth: whole dorsal fin below the local surface ---
            Vector3 fin = pos + fwd * 0.2f;
            // One sample per frame. Guarded: the sampler can throw while the
            // shore grid is not built yet (seen at the Home menu, 2026-09-29,
            // RegionField.ShoreWetDepth index -1); keep the last surface then.
            float surface = lastSurface;
            if (OceanSampler.Ready)
            {
                try { surface = lastSurface = OceanSampler.SampleImmediate(fin).height; }
                catch (System.Exception) { }
            }
            float ceiling = surface - dorsalTop - SeaLifeTuning.LargeFishFinClearance;
            if (!yInit) { y = ceiling; yInit = true; }
            y = Mathf.Lerp(y, ceiling, 1f - Mathf.Exp(-2f * dt));
            y = Mathf.Min(y, ceiling + 0.25f);   // lag downward only; never lets the fin breach
            y = Mathf.Min(y, surface - dorsalTop - 0.25f);
            pos.y = y;

            bank = Mathf.Lerp(bank, -yawRate * 0.6f, 1f - Mathf.Exp(-3f * dt));
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, Mathf.Clamp(bank, -12f, 12f)));

            if (animator != null)
                animator.speed = Mathf.Clamp(speed / Mathf.Max(0.5f, SeaLifeTuning.LargeFishSpeed), 0.75f, 1.35f);

            if (seeThrough && body != null)
            {
                body.GetPropertyBlock(mpb);
                mpb.SetFloat(SurfaceY, surface);
                body.SetPropertyBlock(mpb);
            }
        }

        void Steer(Vector3 pos, Vector3 toShip, float shipDist, float dt)
        {
            float t = Time.time;

            // Land ahead beats everything: turn well away and keep turning.
            if (t >= nextLandCheck)
            {
                nextLandCheck = t + 0.5f;
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                landAhead = !SeaLifeSpawn.ClearOfLand(pos + fwd * 25f);
            }
            if (landAhead)
            {
                wanderTarget = yaw + 120f;
                speedTarget = SeaLifeTuning.LargeFishSpeed;
                return;
            }

            // A pass: head for a point a little ahead of the bow, off to one
            // side, so the fish slides by (or under) the ship where it is seen.
            if (t >= nextPassAt)
            {
                passUntil = t + Random.Range(12f, 20f);
                passSide = Random.value < 0.5f ? -1f : 1f;
                nextPassAt = passUntil + Random.Range(18f, 35f);
            }
            if (t < passUntil || shipDist > SeaLifeTuning.LargeFishLeashMetres)
            {
                Vector3 target = ship.position + ship.forward * 14f + ship.right * (passSide * 3f);
                Vector3 d = target - pos; d.y = 0f;
                if (d.sqrMagnitude > 16f)
                    wanderTarget = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                speedTarget = SeaLifeTuning.LargeFishSpeed * 1.2f;
                return;
            }

            // Otherwise: wander. A new broad heading every few seconds.
            if (t >= nextWanderPick)
            {
                nextWanderPick = t + Random.Range(5f, 10f);
                wanderTarget = yaw + Random.Range(-70f, 70f);
                speedTarget = SeaLifeTuning.LargeFishSpeed * Random.Range(0.8f, 1.15f);
            }
        }
    }
}
