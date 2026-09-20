using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// **Where the Hand is pointing, drawn on the ground.**
    ///
    /// Two rings. The outer one follows the cursor and is tinted chalk when
    /// letting go would do something and red when it would not — the same two
    /// colours the siting ghost uses, because they mean the same thing and a
    /// player should only have to learn them once. The inner one goes round
    /// the THING: a circle on a tree, a prop or the ship, and the building's
    /// own footprint on a shed, so "you are over the sawmill" is a statement
    /// about the sawmill rather than about a patch of grass near it.
    ///
    /// Height-sampled `LineRenderer`s, the way `CampSiting.BuildRing` does it,
    /// and for the same reason: a flat circle laid over a beach slices into it
    /// and reads as a bug. Unlike that ring this one moves, so the sampling is
    /// redone only when the point it is drawn about has actually moved — a
    /// cursor resting on a tree costs nothing at all.
    ///
    /// Owns no rules. Everything it draws comes out of one `HandTarget`.
    public class HandCursor : MonoBehaviour
    {
        /// Tunables, as plain statics: this component is added at runtime, so
        /// a serialised field on it would never be editable.
        public static class Feel
        {
            /// Points round a ring. Twenty-four is smooth at a metre across
            /// and still smooth at ten.
            public const int Segments = 24;

            /// The cursor ring's radius, as a share of the metres of ground up
            /// the frame, held between a person's reach and a small clearing.
            public static float RadiusOfView = 0.02f;
            public static float RadiusMin = 1f;
            public static float RadiusMax = 8f;

            /// How far off the ground the lines float, so they are not
            /// z-fighting the terrain they are drawn on.
            public static float Lift = 0.22f;

            /// Line width in metres. Scaled with the view for the same reason
            /// the radius is: a 0.3 m line is invisible from 800 m up.
            public static float WidthOfView = 0.0045f;
            public static float WidthMin = 0.14f;

            /// How far the thing under the cursor has to move before the rings
            /// are re-sampled. A few centimetres: below this the player cannot
            /// see the difference and the height field is not free.
            public static float MoveEpsilon = 0.06f;
        }

        LineRenderer ring;       // follows the cursor
        LineRenderer mark;       // round the thing
        Material ringMat, markMat;

        Vector3 ringAt = new Vector3(float.NaN, 0f, 0f);
        float ringRadius = -1f;
        bool ringFlat;

        Vector3 markAt = new Vector3(float.NaN, 0f, 0f);
        float markRadius = -1f, markYaw = float.NaN;
        bool markBoxed, markFlat;

        Color ringColour = Color.clear;

        void OnDestroy()
        {
            if (ring != null) Destroy(ring.gameObject);
            if (mark != null) Destroy(mark.gameObject);
            if (ringMat != null) Destroy(ringMat);
            if (markMat != null) Destroy(markMat);
        }

        /// Draw this target. Safe and free to call every frame: nothing is
        /// rebuilt unless what it would draw has moved.
        public void Show(HandTarget t, float viewGround)
        {
            if (!IslandCam.Engaged || t.kind == HandTarget.Kind.None) { Hide(); return; }
            Build();

            float width = Mathf.Max(Feel.WidthMin, Feel.WidthOfView * viewGround);
            Color c = t.Allowed ? BuildingFactory.GhostChalk : BuildingFactory.GhostRefused;
            if (c != ringColour)
            {
                ringColour = c;
                // A ring drawn over open sea is the refusal itself, so the
                // mark takes the same colour: one thing changing colour, not
                // two things disagreeing.
                if (ringMat != null) ringMat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.85f));
                if (markMat != null) markMat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.55f));
            }
            ring.widthMultiplier = width;
            mark.widthMultiplier = width * 0.8f;

            // Over the sea, and on her deck, there is no ground to sample --
            // the height field goes on returning sea bed out past every shore,
            // so a sampled ring would sink.
            bool flat = t.kind == HandTarget.Kind.Water || t.kind == HandTarget.Kind.Ship;

            float radius = Mathf.Clamp(Feel.RadiusOfView * viewGround,
                Feel.RadiusMin, Feel.RadiusMax);
            if (!ring.enabled) ring.enabled = true;
            if (Moved(ringAt, t.point) || !Mathf.Approximately(radius, ringRadius)
                || flat != ringFlat)
            {
                ringAt = t.point;
                ringRadius = radius;
                ringFlat = flat;
                Circle(ring, t.point, radius, flat);
            }

            // Bare ground and open water have nothing to ring: the cursor IS
            // the whole answer there.
            bool hasMark = t.kind != HandTarget.Kind.Ground && t.kind != HandTarget.Kind.Water;
            if (!hasMark) { if (mark.enabled) mark.enabled = false; return; }
            if (!mark.enabled) mark.enabled = true;

            if (t.boxed)
            {
                float yaw = t.yaw;
                if (Moved(markAt, t.centre) || !markBoxed
                    || Mathf.Abs(Mathf.DeltaAngle(yaw, markYaw)) > 0.5f
                    || !Mathf.Approximately(t.extent, markRadius) || flat != markFlat)
                {
                    markAt = t.centre;
                    markYaw = yaw;
                    markRadius = t.extent;
                    markBoxed = true;
                    markFlat = flat;
                    Footprint(mark, t.centre, yaw, t.footprint, flat);
                }
                return;
            }

            if (Moved(markAt, t.centre) || markBoxed
                || !Mathf.Approximately(t.extent, markRadius) || flat != markFlat)
            {
                markAt = t.centre;
                markRadius = t.extent;
                markBoxed = false;
                markFlat = flat;
                Circle(mark, t.centre, Mathf.Max(0.5f, t.extent), flat);
            }
        }

        public void Hide()
        {
            if (ring != null && ring.enabled) ring.enabled = false;
            if (mark != null && mark.enabled) mark.enabled = false;
        }

        static bool Moved(Vector3 was, Vector3 now)
        {
            if (float.IsNaN(was.x)) return true;
            float dx = now.x - was.x, dy = now.y - was.y, dz = now.z - was.z;
            return dx * dx + dy * dy + dz * dz > Feel.MoveEpsilon * Feel.MoveEpsilon;
        }

        // --- the two renderers -----------------------------------------------

        void Build()
        {
            if (ring != null && mark != null) return;
            ringMat = NewMaterial();
            markMat = NewMaterial();
            ring = NewLine("HandRing", ringMat);
            mark = NewLine("HandMark", markMat);
        }

        static Material NewMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(shader);
            m.SetColor("_BaseColor", new Color(0.62f, 0.78f, 0.92f, 0.85f));
            return m;
        }

        LineRenderer NewLine(string label, Material m)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            // World space: the Hand lives on the ship, and she rolls at
            // anchor. A ring parented to her would roll with her.
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = m;
            lr.enabled = false;
            return lr;
        }

        // --- shapes, sampled off the height field -----------------------------

        static void Circle(LineRenderer lr, Vector3 centre, float radius, bool flat)
        {
            if (lr.positionCount != Feel.Segments) lr.positionCount = Feel.Segments;
            var h = GroundPick.Height;
            for (int i = 0; i < Feel.Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Feel.Segments;
                float x = centre.x + Mathf.Cos(a) * radius;
                float z = centre.z + Mathf.Sin(a) * radius;
                lr.SetPosition(i, new Vector3(x, Y(h, x, z, centre.y, flat), z));
            }
        }

        /// Four sides, four points a side: a rectangle drawn corner to corner
        /// cuts through any ground that is not a table, and a camp is sited on
        /// ground that is merely flat ENOUGH.
        const int PerSide = 5;

        static void Footprint(LineRenderer lr, Vector3 centre, float yaw,
            Vector2 footprint, bool flat)
        {
            int n = PerSide * 4;
            if (lr.positionCount != n) lr.positionCount = n;

            float slack = HandTargets.Feel.FootprintSlack;
            float hx = footprint.x * 0.5f + slack;
            float hz = footprint.y * 0.5f + slack;
            float r = yaw * Mathf.Deg2Rad;
            float s = Mathf.Sin(r), c = Mathf.Cos(r);
            var h = GroundPick.Height;

            int k = 0;
            for (int side = 0; side < 4; side++)
            {
                // Corners in the same local axes `Outpost.Corners` measures:
                // local X is the ridge, local Z is across it.
                float ax = side == 0 || side == 3 ? -hx : hx;
                float az = side < 2 ? -hz : hz;
                float bx = side == 0 ? hx : side == 1 ? hx : side == 2 ? -hx : -hx;
                float bz = side == 0 ? -hz : side == 1 ? hz : side == 2 ? hz : -hz;

                for (int i = 0; i < PerSide; i++)
                {
                    float f = i / (float)PerSide;
                    float lx = Mathf.Lerp(ax, bx, f);
                    float lz = Mathf.Lerp(az, bz, f);
                    float x = centre.x + lx * c + lz * s;
                    float z = centre.z - lx * s + lz * c;
                    lr.SetPosition(k++, new Vector3(x, Y(h, x, z, centre.y, flat), z));
                }
            }
        }

        static float Y(System.Func<float, float, float> h, float x, float z,
            float fallback, bool flat)
        {
            if (flat || h == null) return fallback + Feel.Lift;
            // Over water the field returns sea bed, so a ring that strayed off
            // the shore would vanish under the sea for half its length.
            return Mathf.Max(h(x, z), 0.2f) + Feel.Lift;
        }
    }
}
