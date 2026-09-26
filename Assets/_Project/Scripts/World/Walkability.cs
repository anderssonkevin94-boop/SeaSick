using UnityEngine;

namespace SeaSick.World
{
    /// **How steep is too steep, for every walker, in one place (2026-09-27).**
    ///
    /// Kevin, on the phone: *"villagers and goats can just walk straight up
    /// the sides of the mountains. there needs to be a reasonable cap for
    /// what units can ascend."* The camp grid (`CampPath`) did have a slope
    /// cap, but three kinds of step never asked it: the straight-line
    /// fallback when a plan fails or the target is off the grid, the last
    /// leg from the snapped cell up to a target standing on a slope, and
    /// every hop under six metres. Animals never asked it at all -- `Animal.Step`
    /// tested only the beach and the walls, and a goat's "standable" was a
    /// 58° gradient on a one-metre cross.
    ///
    /// So every walker now answers the same two questions here, with the
    /// same numbers:
    ///
    ///  * **Can this ground be stood on?** (`Standable`, and `CampPath`'s grid
    ///    computes exactly this per cell from its own height samples,
    ///    `CampPath.MaxSlopeDegrees` now reading `ManMaxDegrees`): the
    ///    rise to EVERY one of the eight neighbours one `Span` away is within
    ///    the walker's grade. Eight, not the old four-axis max, because a
    ///    slope running diagonally to the grid read 1/√2 of its real
    ///    steepness on the axes.
    ///
    ///  * **Can this step be taken?** (`StepOk`): a probe `Probe` metres ahead
    ///    in the direction of travel. This is the BACKSTOP, not the planner
    ///    -- it catches the fallbacks above -- so it is allowed `StepSlack`
    ///    more than the grid, which keeps it from refusing a route the grid
    ///    already approved over a bump between two cell centres.
    ///
    /// Men, raiders and boar share one cap; goats get more, but still no
    /// sheer face. One extra height sample per moving walker per frame; no
    /// raycasts.
    public static class Walkability
    {
        public enum Feet { Man, Goat }

        /// Steepest ground a man (villager, raider) or a boar will walk on.
        /// 33° is a rise of 1.3 m over one 2 m path cell -- a stiff hill,
        /// and well short of the carved cliff faces (the scenery drops its
        /// cliff shards on 40°+ ground, `IslandScenery`'s `sheer`).
        public static float ManMaxDegrees = 33f;

        /// Goats climb: 45° is a 2 m rise per 2 m. Still not a cliff.
        public static float GoatMaxDegrees = 45f;

        /// Metres the standability test measures over: one `CampPath` cell,
        /// so a point test and a grid cell ask the same question.
        public const float Span = 2f;

        /// Metres ahead the per-step backstop looks.
        public const float Probe = 1f;

        /// How much steeper than the cap a single step may be before the
        /// backstop refuses it. The grid is the real cap; this only has to
        /// catch a straight line up a mountainside.
        public static float StepSlack = 1.25f;

        public static float MaxDegrees(Feet f) => f == Feet.Goat ? GoatMaxDegrees : ManMaxDegrees;

        /// Rise per metre of run allowed.
        public static float Grade(Feet f) => Mathf.Tan(Mathf.Clamp(MaxDegrees(f), 5f, 80f) * Mathf.Deg2Rad);

        /// Largest rise to an orthogonal neighbour one `Span` away.
        public static float MaxStep(Feet f) => Grade(f) * Span;

        static readonly float[] NX = { 1f, -1f, 0f, 0f, 1f, 1f, -1f, -1f };
        static readonly float[] NZ = { 0f, 0f, 1f, -1f, 1f, -1f, 1f, -1f };
        const float Diag = 1.41421356f;

        /// Is the ground at (x, z) standable at this grade (rise per metre)?
        /// Nine height samples; for placement and target picks, never per
        /// frame.
        public static bool Standable(System.Func<float, float, float> height, float x, float z, float grade)
        {
            if (height == null) return true;
            float h = height(x, z);
            return SteepestRise(height, x, z, h) <= grade;
        }

        public static bool Standable(System.Func<float, float, float> height, float x, float z, Feet f)
            => Standable(height, x, z, Grade(f));

        /// Rise per metre to the steepest of the eight neighbours `Span` away.
        public static float SteepestRise(System.Func<float, float, float> height, float x, float z, float h)
        {
            float worst = 0f;
            for (int d = 0; d < 8; d++)
            {
                float run = d >= 4 ? Span * Diag : Span;
                float r = Mathf.Abs(height(x + NX[d] * Span, z + NZ[d] * Span) - h) / run;
                if (r > worst) worst = r;
            }
            return worst;
        }

        /// Grid form of the same test, over heights already sampled on a
        /// square lattice of `cell` metres (`CampPath.Build`). Returns the
        /// steepest rise per metre to any of the eight neighbours.
        public static float SteepestRise(float[] hs, int n, int x, int y, float cell)
        {
            int i = y * n + x;
            float h = hs[i];
            float worst = 0f;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + (int)NX[d], ny = y + (int)NZ[d];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                float run = d >= 4 ? cell * Diag : cell;
                float r = Mathf.Abs(hs[ny * n + nx] - h) / run;
                if (r > worst) worst = r;
            }
            return worst;
        }

        /// **The per-step backstop.** `from.y` must be the ground under the
        /// walker (every walker here sets its Y from the field each frame);
        /// `to` gives only the direction. One height sample.
        public static bool StepOk(System.Func<float, float, float> height, Vector3 from, Vector3 to, Feet f)
        {
            if (height == null) return true;
            float dx = to.x - from.x, dz = to.z - from.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) return true;
            float ax = from.x + dx / len * Probe, az = from.z + dz / len * Probe;
            float rise = Mathf.Abs(height(ax, az) - from.y) / Probe;
            return rise <= Grade(f) * StepSlack;
        }

        /// Same, against a camp's own ground.
        /// (No delegate: `GroundAt` takes a Vector3, and a lambda here would
        /// allocate per walker per frame.)
        public static bool StepOk(Outpost camp, Vector3 from, Vector3 to, Feet f)
        {
            if (camp == null) return true;
            float dx = to.x - from.x, dz = to.z - from.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) return true;
            var ahead = new Vector3(from.x + dx / len * Probe, 0f, from.z + dz / len * Probe);
            float rise = Mathf.Abs(camp.GroundAt(ahead) - from.y) / Probe;
            return rise <= Grade(f) * StepSlack;
        }

        /// `Standable` against a camp's own ground (nine samples, no delegate).
        public static bool Standable(Outpost camp, float x, float z, Feet f)
        {
            if (camp == null) return true;
            float grade = Grade(f);
            float h = camp.GroundAt(new Vector3(x, 0f, z));
            for (int d = 0; d < 8; d++)
            {
                float run = d >= 4 ? Span * Diag : Span;
                float r = Mathf.Abs(camp.GroundAt(new Vector3(x + NX[d] * Span, 0f, z + NZ[d] * Span)) - h) / run;
                if (r > grade) return false;
            }
            return true;
        }

        /// **The one step rule every walker applies**: the backstop passes,
        /// or the walker is on ground it could not stand on and is walking
        /// DOWN off it. The escape is only priced (nine samples) when the
        /// cheap test has already refused.
        public static bool MayStep(Outpost camp, Vector3 from, Vector3 to, Feet f)
            => StepOk(camp, from, to, f)
               || (Downhill(camp, from, to) && !Standable(camp, from.x, from.z, f));

        public static bool MayStep(System.Func<float, float, float> height, Vector3 from, Vector3 to, Feet f)
            => StepOk(height, from, to, f)
               || (Downhill(height, from, to) && !Standable(height, from.x, from.z, f));

        public static bool Downhill(Outpost camp, Vector3 from, Vector3 to)
        {
            if (camp == null) return true;
            float dx = to.x - from.x, dz = to.z - from.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) return true;
            var ahead = new Vector3(from.x + dx / len * Probe, 0f, from.z + dz / len * Probe);
            return camp.GroundAt(ahead) < from.y;
        }

        /// Is a refused step at least going DOWN? A walker that finds itself
        /// on ground it could not have climbed to (spawned there, dropped by
        /// the Hand, a cap tuned tighter mid-session) may always walk off it
        /// downhill, so nothing is ever pinned.
        public static bool Downhill(System.Func<float, float, float> height, Vector3 from, Vector3 to)
        {
            if (height == null) return true;
            float dx = to.x - from.x, dz = to.z - from.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) return true;
            return height(from.x + dx / len * Probe, from.z + dz / len * Probe) < from.y;
        }
    }
}
