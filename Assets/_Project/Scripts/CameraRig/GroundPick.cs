using UnityEngine;

namespace SeaSick.CameraRig
{
    /// Where on the ground is the player pointing?
    ///
    /// **Against the height FIELD, not against colliders**, and that is not a
    /// preference. `TerrainSettings.colliderRadius` is 1 chunk — colliders
    /// exist within about 128 m of the ship and nowhere else — while
    /// `IslandCam` will frame up to 520 m of ground. A screen ray cast with
    /// `Physics.Raycast` therefore passes clean through the far half of the
    /// island the player is looking at and reports a miss, which is a bug that
    /// only appears when somebody zooms out, i.e. exactly when they are
    /// choosing where to put something.
    ///
    /// The height field is a pure function of position and is already the
    /// authority everywhere else in this codebase (see `Outpost.SiteRules`),
    /// so intersecting it directly is both correct and free of the streamer.
    public static class GroundPick
    {
        /// Metres of slack below the surface that still counts as a hit, so a
        /// ray that grazes a slope does not walk all the way to the far side
        /// of the island before it notices.
        const float Skin = 0.05f;

        /// How far a pick will reach. Beyond this there is no island worth
        /// pointing at: the widest frame is 520 m of ground from about 800 m
        /// back, so 3 km is generous by a factor of three.
        const float MaxDistance = 3000f;

        /// Deepest the marcher will go below sea level before giving up. The
        /// height field goes on returning sea bed out past every shore, so a
        /// ray aimed at the horizon must be stopped by something.
        const float FloorY = -40f;

        /// The height field, or null if the world has not been built yet.
        ///
        /// **Both of these are non-serialisable statics**, so a script
        /// recompile during play mode nulls them and every pick silently
        /// misses — the same trap that made the outpost survey report "not
        /// sited" with no error. Callers must handle false.
        public static System.Func<float, float, float> Height =>
            World.Outpost.Rules.Valid ? World.Outpost.Rules.height
                                      : World.Island.TerrainHeight;

        /// The point under this screen position, if the ray meets the ground.
        public static bool FromScreen(Camera cam, Vector2 screen, out Vector3 hit)
        {
            hit = default;
            if (cam == null) return false;
            return Along(cam.ScreenPointToRay(screen), out hit);
        }

        /// March a ray until it goes under the ground, then bisect.
        ///
        /// The step adapts to how far above the surface the ray is, which is
        /// what keeps this cheap from a camera 800 m up: the first few steps
        /// cross hundreds of metres, and it only starts inching once it is
        /// close. A fixed 2 m step over 1.5 km would be 750 evaluations of a
        /// function measured at up to 13 us each — 10 ms, in a frame where the
        /// player is dragging a ghost about.
        public static bool Along(Ray ray, out Vector3 hit)
        {
            hit = default;
            var h = Height;
            if (h == null) return false;

            // A ray pointing up or along the horizon never comes down.
            if (ray.direction.y > -0.01f) return false;

            float t = 0f;
            float prevT = 0f;
            float prevGap = ray.origin.y - h(ray.origin.x, ray.origin.z);
            if (prevGap < 0f) { hit = ray.origin; return true; }

            while (t < MaxDistance)
            {
                // Never step less than a metre, or a near-tangent ray spends
                // its whole budget crawling; never more than the gap, or it
                // can jump a ridge and land on the ground behind it.
                float step = Mathf.Clamp(prevGap * 0.8f, 1f, 200f);
                t += step;

                Vector3 p = ray.GetPoint(t);
                if (p.y < FloorY) return false;

                float gap = p.y - h(p.x, p.z);
                if (gap <= Skin)
                {
                    // Bisect between the last point above and this one below.
                    float lo = prevT, hi = t;
                    for (int i = 0; i < 14; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        Vector3 q = ray.GetPoint(mid);
                        if (q.y - h(q.x, q.z) > 0f) lo = mid; else hi = mid;
                    }
                    hit = ray.GetPoint(hi);
                    hit.y = h(hit.x, hit.z);
                    return true;
                }

                prevT = t;
                prevGap = gap;
            }
            return false;
        }
    }
}
