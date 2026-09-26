using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Ladder, landing, ladder, landing: the shape of a way up a cliff
    /// (2026-09-27).**
    ///
    /// Kevin: *"is there any way to implement a ladder-platform-ladder-platform
    /// structure to ascend the mountains? it could be fun to use those in a
    /// build."* Since 81fd204 cliffs truly block every walker
    /// (`Walkability`), so this is the player's way over one.
    ///
    /// The player gives two points, a FOOT (walkable ground below) and a TOP
    /// (walkable ground above). This file owns every number about what stands
    /// between them and draws nothing itself -- `Ladder` (the finished thing),
    /// `BuildSite.PlaceLadder` (the blueprint) and `UI/LadderSiting` (the
    /// ghost) all build from one `Shape`, so the aim, the site and the
    /// structure cannot drift apart.
    ///
    /// **The chain.** The rise is split into flights of at most `MaxFlight`
    /// metres. Walking the line foot -> top, landing k sits where the ground
    /// first reaches its height (the ledge), its deck reaching `LandingDepth`
    /// outward from the face, on posts down to the ground; each flight leans
    /// from the landing below up to that ledge, and alternate flights step
    /// `SideStep` to either side so the chain reads as a zig-zag of separate
    /// ladders rather than one long pole. So it hugs the terrain it is given
    /// -- a sheer face makes a tight stack, a stepped one spreads out.
    ///
    /// **The walk.** `path` is the route a body takes (foot, flight bottoms,
    /// flight tops, the step across each landing, the top) and `legs` says
    /// how each piece is covered: on the ground, across a deck, or up a
    /// ladder. `LadderClimb` walks it; `CampPath` prices it (`ClimbSeconds`).
    ///
    /// Placeholder art (two rails and rungs; a plank deck on four posts), one
    /// mesh per chain, one material -- cheap on the phone. Astra redoes it.
    public static class LadderLayout
    {
        // --- the numbers (provisional; Kevin has not played one) --------------

        /// Tallest single flight, metres. A long ladder, but one a man carries.
        public const float MaxFlight = 3.5f;
        /// Less rise than this is not a cliff: walk it.
        public const float MinRise = 3f;
        /// More than this is too high for one chain.
        public const float MaxRise = 24f;
        /// Farthest the top may be from the foot, flat metres.
        public const float MaxRun = 8f;
        /// Seconds per metre of rise up (or down) a ladder. What the link in
        /// `CampPath` costs, and how long a climb takes to watch.
        public const float ClimbSecondsPerMetre = 1.5f;
        /// Walking speed across a landing and along the ground at either end
        /// of a chain, m/s.
        public const float DeckSpeed = 1.4f;
        public const float GroundSpeed = 2.2f;
        /// Seconds a climber stands on each landing before the next flight.
        public const float LandingPause = 0.35f;

        /// Horizontal lean of the FIRST flight, per metre of its rise (the
        /// others lean from the landing below to the ledge above).
        public const float Lean = 0.3f;
        public const float LandingDepth = 1.4f;
        public const float LandingWidth = 2.2f;
        public const float SideStep = 0.55f;
        /// Half the width between the rails.
        public const float RailHalf = 0.24f;
        /// Metres between rungs.
        public const float RungStep = 0.3f;

        public enum Leg : byte { Ground, Deck, Climb }

        /// Everything about one chain, in world space.
        public sealed class Shape
        {
            public Vector3 foot, top;
            /// Unit, flat: foot toward top.
            public Vector3 run;
            /// Unit, flat: to the right of `run`.
            public Vector3 side;
            public readonly List<Vector3> path = new List<Vector3>();
            /// `legs[i]` is how `path[i]` -> `path[i + 1]` is covered.
            public readonly List<Leg> legs = new List<Leg>();
            /// Each flight's centre line, bottom and top.
            public readonly List<Vector3> flightLo = new List<Vector3>();
            public readonly List<Vector3> flightHi = new List<Vector3>();
            /// Each landing: the centre of its deck's TOP surface.
            public readonly List<Vector3> landing = new List<Vector3>();
            /// And the ground under its four corners (post feet).
            public readonly List<Vector4> landingPostY = new List<Vector4>();

            public float Rise => top.y - foot.y;
            public float Run => Flat(foot, top);
            public int Flights => flightLo.Count;
            public int Landings => landing.Count;

            /// Seconds from foot to top (the same down).
            public float ClimbSeconds
            {
                get
                {
                    float s = 0f;
                    for (int i = 0; i + 1 < path.Count; i++) s += LegSeconds(path[i], path[i + 1], legs[i]);
                    return s;
                }
            }
        }

        /// How long one leg of the walk takes.
        public static float LegSeconds(Vector3 a, Vector3 b, Leg leg)
        {
            switch (leg)
            {
                case Leg.Climb: return Mathf.Max(0.3f, Mathf.Abs(b.y - a.y) * ClimbSecondsPerMetre);
                case Leg.Deck: return Flat(a, b) / DeckSpeed + LandingPause;
                default: return Mathf.Max(0.05f, Flat(a, b) / GroundSpeed);
            }
        }

        /// Timber a chain of this rise costs (provisional: 2 per metre, at
        /// least 4), through the playtest cap like every other price.
        public static int Cost(float rise) => BuildPlans.LadderCost(rise);

        // --- can it stand? ------------------------------------------------------

        /// **The one test.** `foot`/`top` are as tapped (either order: the
        /// lower one becomes the foot). Refusals are in the player's words.
        /// `ground` is the camp's height field. Does NOT ask about the camp
        /// (reach, other ladders) -- `Outpost.CanPlaceLadder` adds those.
        public static bool Valid(System.Func<Vector3, float> ground, ref Vector3 foot, ref Vector3 top,
            out string why)
        {
            why = "";
            if (ground == null) { why = "this ground was never surveyed"; return false; }
            foot.y = ground(foot);
            top.y = ground(top);
            if (top.y < foot.y) { var t = foot; foot = top; top = t; }

            float run = Flat(foot, top);
            if (run < 0.5f) { why = "tap the top of the cliff, not the foot again"; return false; }
            if (run > MaxRun + 0.01f) { why = $"too far — the top must be within {MaxRun:0} m of the foot"; return false; }

            System.Func<float, float, float> h = (x, z) => ground(new Vector3(x, 0f, z));
            if (!Walkability.Standable(h, foot.x, foot.z, Walkability.Feet.Man))
            { why = "the foot isn't walkable ground"; return false; }
            if (!Walkability.Standable(h, top.x, top.z, Walkability.Feet.Man))
            { why = "the top isn't walkable ground"; return false; }

            float rise = top.y - foot.y;
            if (rise < MinRise || WalkableLine(ground, foot, top))
            { why = "not a cliff — just walk"; return false; }
            if (rise > MaxRise + 0.01f) { why = $"too high — {MaxRise:0} m at most"; return false; }
            return true;
        }

        /// Could a man walk straight from one to the other? Every metre of
        /// the line against the per-step backstop.
        static bool WalkableLine(System.Func<Vector3, float> ground, Vector3 a, Vector3 b)
        {
            float run = Flat(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(run / Walkability.Probe));
            Vector3 prev = a;
            prev.y = ground(prev);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
                p.y = ground(p);
                float rise = Mathf.Abs(p.y - prev.y) / Mathf.Max(0.01f, Flat(prev, p));
                if (rise > Walkability.Grade(Walkability.Feet.Man)) return false;
                prev = p;
            }
            return true;
        }

        // --- the chain ----------------------------------------------------------

        /// Lay the chain out. `foot` must be the lower end (see `Valid`).
        public static Shape Plan(System.Func<Vector3, float> ground, Vector3 foot, Vector3 top)
        {
            var s = new Shape();
            foot.y = ground(foot);
            top.y = ground(top);
            s.foot = foot;
            s.top = top;
            Vector3 flat = top - foot;
            flat.y = 0f;
            float D = flat.magnitude;
            s.run = D > 1e-4f ? flat / D : Vector3.forward;
            s.side = Vector3.Cross(Vector3.up, s.run);

            float H = Mathf.Max(0.5f, top.y - foot.y);
            int n = Mathf.Max(1, Mathf.CeilToInt(H / MaxFlight - 1e-3f));
            float h = H / n;

            // Where along the line the ground first reaches each level.
            var ledge = new float[n + 1];
            ledge[0] = 0f;
            float sPrev = 0f;
            for (int k = 1; k <= n; k++)
            {
                float want = foot.y + k * h - 0.05f;
                float found = D;
                for (float x = sPrev; x <= D + 1e-3f; x += 0.25f)
                    if (ground(foot + s.run * x) >= want) { found = x; break; }
                ledge[k] = Mathf.Max(sPrev, found);
                sPrev = ledge[k];
            }

            Vector3 At(float along, float y, float lateral)
                => new Vector3(foot.x, y, foot.z) + s.run * along + s.side * lateral;

            float Offset(int k) => n == 1 ? 0f : ((k % 2 == 1) ? SideStep : -SideStep);

            s.path.Add(foot);
            for (int k = 1; k <= n; k++)
            {
                float yLo = foot.y + (k - 1) * h, yHi = foot.y + k * h;
                float o = Offset(k);
                float top_s = ledge[k] - (k < n ? 0.15f : 0f);
                float lo_s;
                if (k == 1)
                {
                    lo_s = Mathf.Max(0f, top_s - Lean * h);
                    Vector3 lo = At(lo_s, 0f, o);
                    lo.y = ground(lo);
                    yLo = lo.y;
                    s.path.Add(lo);
                    s.legs.Add(Leg.Ground);
                }
                else
                {
                    // From the landing below, near its inner edge.
                    lo_s = ledge[k - 1] - 0.45f;
                    Vector3 lo = At(lo_s, yLo, o);
                    s.path.Add(lo);
                    s.legs.Add(Leg.Deck);
                }
                if (top_s < lo_s + 0.1f) top_s = lo_s + 0.1f;
                Vector3 hi = At(top_s, yHi, o);
                s.flightLo.Add(At(lo_s, yLo, o));
                s.flightHi.Add(hi);
                s.path.Add(hi);
                s.legs.Add(Leg.Climb);

                if (k < n)
                {
                    Vector3 deck = At(ledge[k] - 0.5f * LandingDepth, yHi, 0f);
                    s.landing.Add(deck);
                    var post = new Vector4();
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 corner = deck + s.run * ((c < 2 ? -0.5f : 0.5f) * (LandingDepth - 0.1f))
                                       + s.side * (((c & 1) == 0 ? -0.5f : 0.5f) * (LandingWidth - 0.1f));
                        post[c] = Mathf.Min(ground(corner), yHi - 0.1f);
                    }
                    s.landingPostY.Add(post);
                }
            }
            s.path.Add(top);
            s.legs.Add(Leg.Ground);
            return s;
        }

        // --- the mesh -----------------------------------------------------------

        /// **One mesh for the whole chain**, in the frame of `origin`
        /// (identity rotation). Boxes with flat normals: 24 vertices each.
        public static Mesh BuildMesh(Shape s, Vector3 origin)
        {
            var v = new List<Vector3>(2048);
            var t = new List<int>(3072);

            for (int f = 0; f < s.Flights; f++)
            {
                Vector3 lo = s.flightLo[f] - origin, hi = s.flightHi[f] - origin;
                Vector3 up = hi - lo;
                float len = up.magnitude;
                if (len < 0.05f) continue;
                Vector3 dir = up / len;
                // Rails stand a little proud of the landing and the ledge.
                Vector3 ext = dir * 0.5f;
                for (int r = -1; r <= 1; r += 2)
                {
                    Vector3 off = s.side * (r * RailHalf);
                    Box(v, t, lo + off, hi + off + ext, s.side, 0.035f, 0.035f);
                }
                int rungs = Mathf.Max(1, Mathf.FloorToInt((hi.y - lo.y) / RungStep));
                for (int k = 1; k <= rungs; k++)
                {
                    Vector3 c = Vector3.Lerp(lo, hi, k / (float)(rungs + 1));
                    Box(v, t, c - s.side * RailHalf, c + s.side * RailHalf, dir, 0.022f, 0.022f);
                }
            }

            for (int l = 0; l < s.Landings; l++)
            {
                Vector3 c = s.landing[l] - origin;
                // Deck: planks across the run.
                const int planks = 5;
                float pw = LandingDepth / planks;
                for (int p = 0; p < planks; p++)
                {
                    Vector3 pc = c + s.run * (-0.5f * LandingDepth + (p + 0.5f) * pw) - Vector3.up * 0.05f;
                    Box(v, t, pc - s.side * (0.5f * LandingWidth), pc + s.side * (0.5f * LandingWidth),
                        s.run, 0.5f * pw - 0.015f, 0.05f);
                }
                // Four posts down to the ground.
                var py = s.landingPostY[l];
                for (int k = 0; k < 4; k++)
                {
                    Vector3 corner = c + s.run * ((k < 2 ? -0.5f : 0.5f) * (LandingDepth - 0.1f))
                                   + s.side * (((k & 1) == 0 ? -0.5f : 0.5f) * (LandingWidth - 0.1f));
                    Vector3 bottom = new Vector3(corner.x, py[k] - origin.y - 0.2f, corner.z);
                    Vector3 topP = corner + Vector3.up * 0.9f;   // and a handrail post
                    if (topP.y - bottom.y > 0.05f)
                        Box(v, t, bottom, topP, s.run, 0.05f, 0.05f);
                }
                // A rail along the outward edge.
                Vector3 rail = c - s.run * (0.5f * LandingDepth - 0.05f) + Vector3.up * 0.85f;
                Box(v, t, rail - s.side * (0.5f * LandingWidth), rail + s.side * (0.5f * LandingWidth),
                    s.run, 0.03f, 0.03f);
            }

            var mesh = new Mesh { name = "LadderChain" };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// A box from `a` to `b` (its long axis), `across` giving the
        /// orientation of its first half-width.
        static void Box(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 across,
            float halfA, float halfB)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            Vector3 w = axis / len;
            Vector3 u = Vector3.ProjectOnPlane(across, w);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Vector3.up, w);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Vector3.right, w);
            u.Normalize();
            Vector3 n = Vector3.Cross(w, u);
            u *= halfA;
            n *= halfB;

            // 8 corners
            Vector3 p0 = a - u - n, p1 = a + u - n, p2 = a + u + n, p3 = a - u + n;
            Vector3 p4 = b - u - n, p5 = b + u - n, p6 = b + u + n, p7 = b - u + n;
            Quad(v, t, p0, p1, p2, p3); // end a
            Quad(v, t, p7, p6, p5, p4); // end b
            Quad(v, t, p0, p4, p5, p1);
            Quad(v, t, p1, p5, p6, p2);
            Quad(v, t, p2, p6, p7, p3);
            Quad(v, t, p3, p7, p4, p0);
        }

        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            // Both windings: placeholder art, no culling surprises from the
            // box orientation (a few hundred extra triangles at most).
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        static Material wood;

        /// The ladder's one material.
        public static Material Wood()
        {
            if (wood != null) return wood;
            wood = new Material(Shader.Find(WorldArtStyle.Instance != null
                ? "SeaSick/Environment Toon" : "Universal Render Pipeline/Lit"));
            wood.SetColor("_BaseColor", new Color(0.50f, 0.38f, 0.24f));
            if (wood.HasProperty("_Smoothness")) wood.SetFloat("_Smoothness", 0.05f);
            return wood;
        }

        /// A GameObject drawing the chain: mesh + renderer, and one trigger
        /// collider per flight and landing so a tap on any of it lands on
        /// the chain (and nothing else, unlike one big box would).
        public static GameObject Draw(Transform parent, Shape s, string name, bool colliders)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(s.foot, Quaternion.identity);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildMesh(s, s.foot);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Wood();
            if (!colliders) return go;

            for (int f = 0; f < s.Flights; f++)
            {
                Vector3 lo = s.flightLo[f], hi = s.flightHi[f];
                Vector3 d = hi - lo;
                if (d.sqrMagnitude < 1e-4f) continue;
                var c = new GameObject("FlightHit");
                c.transform.SetParent(go.transform, true);
                c.transform.SetPositionAndRotation(0.5f * (lo + hi), Quaternion.LookRotation(d.normalized, -s.run));
                var box = c.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(1.2f, 0.6f, d.magnitude + 0.4f);
            }
            for (int l = 0; l < s.Landings; l++)
            {
                var c = new GameObject("LandingHit");
                c.transform.SetParent(go.transform, true);
                c.transform.SetPositionAndRotation(s.landing[l], Quaternion.LookRotation(s.run, Vector3.up));
                var box = c.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(LandingWidth, 0.6f, LandingDepth);
            }
            return go;
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
