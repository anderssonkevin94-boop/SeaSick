using System.Text;
using UnityEngine;

namespace SeaSick.World
{
    /// **Where the fire's store cache stands (2026-10-03).**
    ///
    /// The approved fire cache (a 3.2 x 2.3 m groundsheet and a 1.85 m
    /// tripod, `BuildingFactory.FireCachePrefab`) is the one piece of camp
    /// furniture the player never places, so it must never land on
    /// anything. The coordinator's rules (2026-10-03): buildings never
    /// move, and nothing new may overlap an existing building, drawing,
    /// wall, road or the supper ring in an existing save.
    ///
    /// - **Bearings, in a fixed order:** straight inland first (fire ->
    ///   island centre, i.e. behind the fire seen from the water a camp is
    ///   approached from; +X when the fire sits on the island's centre),
    ///   then +-45, +-90, +-135 and 180 degrees off it. The first that is
    ///   clear wins; none clear = no cache drawn (the goods still count, the
    ///   fire's `StorageSlotView` just has no anchors).
    /// - **Outside the supper ring:** the cache's near hem stands
    ///   `FireCacheRingMargin` beyond the ring (the larger of
    ///   `FireRingRadius` and `CampLifeTuning.FireRingRadius`, measured from
    ///   `CampCentre`, which IS the fire), facing the fire.
    /// - **"Clear" is the build placement test itself**, `CanPlace` on the
    ///   cache's footprint (on the island, ground a building may stand on,
    ///   and `Clear`: every standing building's disc, every drawing, every
    ///   wall), plus no tree or rock in it (`CountObstructions`, as a moved
    ///   building asks) and no standing road within its half-diagonal plus
    ///   the road's half-width (the disc test `Clear` uses for a road on
    ///   order).
    /// - **Stable:** the choice is saved (`OutpostLedger.fireCache`) and
    ///   reused verbatim on every load while the fire stands where it was
    ///   chosen for; only a moved fire chooses again. On a load it is chosen
    ///   AFTER every saved building, wall and road stands (end of `Adopt`),
    ///   so it is tested against the whole saved layout -- and since
    ///   `Adopt` raises saved buildings with `force`, the cache can never
    ///   push one anywhere.
    /// - **Registered:** its disc goes into `reserved` like a building's,
    ///   so a later blueprint, wall or move is refused there ("something
    ///   already stands there"), and `CanPlaceRoad` refuses a road across
    ///   it. Its walk box (`BuildingSolids`) exists only where it stands.
    /// - One cache a camp, beside the first fire raised.
    ///
    /// **Not changed:** before a store hut exists, a carry still ends at
    /// `CampWorker.PileAt`'s spot by the fire (`StoreSpot`), not at the
    /// cache's `Marker_Pickup` -- the goods appear in the cache from the
    /// books regardless. Moving that drop-off is a separate decision.
    public partial class Outpost
    {
        /// Metres between the supper ring and the cache's near hem: a
        /// seated hand's body and elbow room.
        public const float FireCacheRingMargin = 0.6f;

        /// Bearings tried, degrees off "straight inland", in order.
        static readonly float[] FireCacheBearings = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

        /// The cache's footprint as a plan, for `CanPlace` / `CountObstructions`
        /// only (centred on the footprint, not the cache root). Never raised.
        static BuildPlan FireCacheFootprint => new BuildPlan
        {
            id = "FireCache",
            label = "fire's store",
            footprint = new Vector2(BuildingFactory.FireCacheBox.z * 2f, BuildingFactory.FireCacheBox.w * 2f),
            ridge = 1.86f,
        };

        Building cacheFire;
        Vector4? cacheReservation;
        string fireCacheReport = "not placed yet";

        /// The first standing campfire, or null.
        Building FirstFire()
        {
            for (int i = 0; i < built.Count; i++)
                if (built[i] != null && built[i].Id == BuildPlans.Campfire.id) return built[i];
            return null;
        }

        void DropFireCacheReservation()
        {
            if (cacheReservation.HasValue)
            {
                reserved.Remove(cacheReservation.Value);
                buildingReservations.Remove(cacheReservation.Value);
            }
            cacheReservation = null;
        }

        /// **Stand (or re-stand) the camp's fire cache.** Called once the
        /// whole saved camp stands (`Adopt`), after a fire is raised, and
        /// after the fire is moved. Uses the saved spot when it is still the
        /// fire's; otherwise chooses (see the class notes) and saves.
        void PlaceFireCache()
        {
            var fire = FirstFire();
            if (cacheFire != null && cacheFire != fire) BuildingFactory.RemoveFireCache(cacheFire.transform);
            DropFireCacheReservation();
            cacheFire = fire;
            if (fire == null || ledger == null) { fireCacheReport = "no fire"; return; }
            if (!BuildingFactory.HasFireCache)
            {
                // Not imported: nothing drawn, nothing reserved, nothing
                // saved -- the ground stays free until the art exists.
                fireCacheReport = "not imported (no Resources/" + BuildingFactory.FireCachePrefab + ")";
                return;
            }
            if (ledger.fireCache == null) ledger.fireCache = new FireCacheSpot();
            var spot = ledger.fireCache;
            Vector3 f = fire.transform.position;
            bool kept = spot.set && Mathf.Abs(spot.fireX - f.x) < 0.05f && Mathf.Abs(spot.fireZ - f.z) < 0.05f;
            var why = new StringBuilder();
            if (!kept)
            {
                spot.set = false;
                if (ChooseFireCache(f, why, out float x, out float z, out float yaw, out int bearing))
                {
                    spot.set = true;
                    spot.fireX = f.x; spot.fireZ = f.z;
                    spot.x = x; spot.z = z; spot.yaw = yaw; spot.bearing = bearing;
                }
            }
            if (!spot.set)
            {
                BuildingFactory.RemoveFireCache(fire.transform);
                BuildingFactory.RenewSlotView(fire);
                fireCacheReport = $"fire ({f.x:F1},{f.z:F1}): NO clear spot, no cache drawn\n{why}";
                return;
            }
            var cache = BuildingFactory.PutFireCache(fire.transform, spot.x, spot.z, spot.yaw, GroundAt);
            BuildingFactory.RenewSlotView(fire);
            if (cache == null) { fireCacheReport = "cache wrapper failed to load"; return; }

            // Reserve its ground like a building's (`Raise`'s disc).
            var k = BuildingFactory.FireCacheBox;
            Vector3 centre = cache.TransformPoint(new Vector3(k.x, 0f, k.y));
            float halfDiag = Mathf.Sqrt(k.z * k.z + k.w * k.w);
            var disc = new Vector4(centre.x, centre.y, centre.z, halfDiag + spacing * 0.5f);
            reserved.Add(disc);
            buildingReservations.Add(disc);
            cacheReservation = disc;
            fireCacheReport = $"fire ({f.x:F1},{f.z:F1}): cache at ({spot.x:F1},{spot.z:F1}) yaw {spot.yaw:F0}, "
                + $"bearing #{spot.bearing} ({FireCacheBearings[Mathf.Clamp(spot.bearing, 0, FireCacheBearings.Length - 1)]:+0;-0;0} deg off inland), "
                + (kept ? "the saved spot" : "chosen now") + (why.Length > 0 ? "\n" + why : "");
        }

        /// The first clear bearing (class notes), as the cache root's world
        /// x/z and yaw. `why` collects each refused bearing's reason.
        bool ChooseFireCache(Vector3 fire, StringBuilder why, out float x, out float z, out float yaw, out int bearing)
        {
            x = z = yaw = 0f;
            bearing = -1;
            var k = BuildingFactory.FireCacheBox;
            float ring = Mathf.Max(FireRingRadius, Life.CampLifeTuning.FireRingRadius) + FireCacheRingMargin;
            // Root -> near hem along its front (+Z): 1.18 m.
            float hem = k.y + k.w;
            float halfDiag = Mathf.Sqrt(k.z * k.z + k.w * k.w);
            Vector3 centreOfRing = CampCentre;

            Vector3 inland = Vector3.right;
            if (Island != null)
            {
                Vector3 d = Island.transform.position - fire; d.y = 0f;
                if (d.sqrMagnitude > 1f) inland = d.normalized;
            }
            var plan = FireCacheFootprint;
            for (int i = 0; i < FireCacheBearings.Length; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, FireCacheBearings[i], 0f) * inland;
                // Hem `ring` out from the fire, the cache's front toward it.
                Vector3 root = fire + dir * (ring + hem);
                Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up);
                float y = rot.eulerAngles.y;
                Vector3 centre = root + rot * new Vector3(k.x, 0f, k.y);
                if (height != null) centre.y = height(centre.x, centre.z);

                // The ring, exactly: the footprint's nearest point to the
                // ring's centre (the fire normally, but never assumed).
                Vector3 local = Quaternion.Inverse(rot) * (centreOfRing - centre);
                float ox = Mathf.Max(0f, Mathf.Abs(local.x) - k.z), oz = Mathf.Max(0f, Mathf.Abs(local.z) - k.w);
                float ringGap = Mathf.Sqrt(ox * ox + oz * oz) - (ring - FireCacheRingMargin);
                string no = null;
                if (ringGap < FireCacheRingMargin - 0.01f) no = $"{ringGap:F2} m off the supper ring";
                else if (!CanPlace(plan, centre, y, out string blocked)) no = blocked;
                else
                {
                    CountObstructions(plan, centre, y, out int trees, out int rocks);
                    if (trees > 0 || rocks > 0) no = $"{trees} trees, {rocks} rocks on it";
                    else if (ledger.builtRoads != null)
                        foreach (var r in ledger.builtRoads)
                            if (r != null && WallSegment.FlatDistance(r.A, r.B, centre) < halfDiag + CampRoads.HalfWidth)
                            { no = "a road runs there"; break; }
                }
                if (no != null)
                {
                    why.AppendLine($"  bearing #{i} ({FireCacheBearings[i]:+0;-0;0} deg): {no}");
                    continue;
                }
                x = root.x; z = root.z; yaw = y; bearing = i;
                return true;
            }
            return false;
        }

        /// Is `p` on the fire cache's footprint grown by `margin`?
        /// (`CanPlaceRoad`: a road never runs across it.)
        bool OnFireCache(Vector3 p, float margin)
        {
            if (cacheFire == null) return false;
            var c = cacheFire.transform.Find(BuildingFactory.FireCacheChild);
            if (c == null) return false;
            var k = BuildingFactory.FireCacheBox;
            Vector3 l = c.InverseTransformPoint(new Vector3(p.x, c.position.y, p.z));
            return Mathf.Abs(l.x - k.x) <= k.z + margin && Mathf.Abs(l.z - k.y) <= k.w + margin;
        }

        /// Is `r` the fire cache's own disc, while its fire is the building
        /// being moved? (The fire is not refused by its own cache; the
        /// cache follows it.)
        bool IsMovingFiresCache(Vector4 r)
            => cacheReservation.HasValue && r == cacheReservation.Value
               && movingBuilt != null && movingBuilt == cacheFire;

        /// **Where every camp's fire cache stands**, one block a camp.
        /// `unity cmd eval --json --code 'return SeaSick.World.Outpost.FireCacheSpots();'`
        /// (play mode, a camp loaded).
        public static string FireCacheSpots()
        {
            var sb = new StringBuilder();
            foreach (var o in all)
            {
                if (o == null) continue;
                sb.Append(o.name).Append(": ").AppendLine(o.fireCacheReport);
            }
            return sb.Length > 0 ? sb.ToString() : "no camps";
        }
    }
}
