using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The camp's wall as a chain of nodes: one post per node.**
    ///
    /// Segments meet at shared posts (D5, "a post is shared between
    /// adjoining segments"), so a post belongs to the NODE, not to either
    /// segment's end -- two segments each standing their own would be two
    /// posts in one place. This keeps the posts, keyed by node position,
    /// on the camp's own object, and turns each to the bisector of the runs
    /// that meet there (`WallVisual.NodeYaw`).
    ///
    /// It also owns the one thing about a segment's drawing that depends on
    /// its neighbours -- whether an acute bend trims its end -- and redraws
    /// a segment only when that (or its gate-ness, or the side its camp is
    /// on) changed. Added by `BuildingFactory.RaiseWall` to the `Outpost`
    /// the first wall is raised under; segments sign in and out themselves
    /// (`WallSegment.Configure` / `OnDestroy`).
    ///
    /// **No per-frame work**: a change marks it dirty and enables it, the
    /// next `LateUpdate` settles every node at once (an island loading
    /// forty segments is one pass, not forty) and it switches itself off.
    [DisallowMultipleComponent]
    public class WallChain : MonoBehaviour
    {
        readonly List<WallSegment> segs = new List<WallSegment>();
        readonly Dictionary<long, Transform> posts = new Dictionary<long, Transform>();
        Transform postRoot;
        Outpost camp;
        bool campLooked;
        Vector3 previewCentre;
        bool dirty;

        /// Node positions closer than this are one node. Posts sit on the
        /// 2 m lattice (`Outpost.SnapPost`), so this is generous.
        const float NodeCell = 0.1f;

        /// The chain on this parent, or null. `create` adds one when the
        /// parent is a camp -- the only thing whose walls form a chain.
        public static WallChain Of(Transform parent, bool create = false)
        {
            if (parent == null) return null;
            var chain = parent.GetComponent<WallChain>();
            if (chain == null && create && parent.GetComponent<Outpost>() != null)
                chain = parent.gameObject.AddComponent<WallChain>();
            return chain;
        }

        Outpost Camp
        {
            get
            {
                if (!campLooked) { camp = GetComponent<Outpost>(); campLooked = true; }
                return camp;
            }
        }

        /// What the rails face: the camp's centre (its fire).
        Vector3 Centre => Camp != null ? Camp.CampCentre : previewCentre;

        System.Func<Vector3, float> Ground => Camp != null ? Camp.GroundAt : null;

        internal void PreviewCentre(Vector3 centre) => previewCentre = centre;

        public void Add(WallSegment seg)
        {
            if (seg == null || segs.Contains(seg)) return;
            segs.Add(seg);
            MarkDirty();
        }

        public void Remove(WallSegment seg)
        {
            if (segs.Remove(seg)) MarkDirty();
        }

        public void MarkDirty()
        {
            dirty = true;
            enabled = true;
        }

        void LateUpdate()
        {
            if (dirty) Refresh();
            enabled = false;
        }

        /// **How a segment on these posts should be drawn**, asked before it
        /// exists (the factory) and after (the refresh). The segment itself,
        /// and anything on the same two posts (the wall a gate is replacing),
        /// are not its neighbours.
        public WallVisual.Fit FitFor(Vector3 a, Vector3 b, bool gate, WallSegment self)
        {
            var fit = new WallVisual.Fit { gate = gate, flip = WallVisual.Flip(a, b, Centre) };
            Vector3 ab = WallVisual.Flat(b - a);
            // A wall tower on either node (2026-09-27): the run stops at
            // its legs. Not for a gate -- a tower never snaps to a gate's
            // posts, and a gate's module owns its own ends.
            if (!gate && Camp != null)
            {
                fit.towerA = Camp.WallTowerAt(a) != null;
                fit.towerB = Camp.WallTowerAt(b) != null;
            }
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s == null || s == self) continue;
                if ((Same(s.A, a) && Same(s.B, b)) || (Same(s.A, b) && Same(s.B, a))) continue;
                fit.trimA |= Acute(a, ab, s);
                fit.trimB |= Acute(b, -ab, s);
            }
            return fit;
        }

        static bool Acute(Vector3 node, Vector3 outward, WallSegment s)
        {
            Vector3 other;
            if (Same(s.A, node)) other = s.B - s.A;
            else if (Same(s.B, node)) other = s.A - s.B;
            else return false;
            return Vector3.Angle(outward, WallVisual.Flat(other)) < WallVisual.AcuteTrim;
        }

        static bool Same(Vector3 p, Vector3 q)
            => Mathf.Abs(p.x - q.x) < NodeCell && Mathf.Abs(p.z - q.z) < NodeCell;

        static long Key(Vector3 p)
            => ((long)Mathf.RoundToInt(p.x / NodeCell) << 32)
                ^ (uint)Mathf.RoundToInt(p.z / NodeCell);

        class Node
        {
            public Vector3 at;
            public bool covered;
            public readonly List<Vector3> outgoing = new List<Vector3>(3);
        }

        /// **Settle every node now**: redraw the segments whose fit
        /// changed, stand a post on each node that has none, turn the ones
        /// whose runs changed, and take down the posts no segment ends at.
        public void Refresh()
        {
            dirty = false;
            segs.RemoveAll(s => s == null);
            if (!WallVisual.KitReady) return;   // the extruded fallback draws its own posts

            var ground = Ground;
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                var fit = FitFor(s.A, s.B, s.IsGate, s);
                if (fit.Key == s.FitKey) continue;
                WallVisual.Build(s.transform, s.A, s.B, fit, ground, out var whole, out var broken);
                s.Redraw(whole, broken, fit.Key);
            }

            var nodes = new Dictionary<long, Node>();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                bool gateHere = s.IsGate && WallVisual.GateOnNode(WallVisual.Flat(s.B - s.A).magnitude);
                AddEnd(nodes, s.A, s.B - s.A, gateHere);
                AddEnd(nodes, s.B, s.A - s.B, gateHere);
            }

            CoverTowers(nodes);

            if (postRoot == null)
            {
                postRoot = new GameObject("WallPosts").transform;
                postRoot.SetParent(transform, false);
            }

            var stale = new List<long>();
            foreach (var pair in posts)
                if (pair.Value == null || !nodes.TryGetValue(pair.Key, out var n) || n.covered)
                    stale.Add(pair.Key);
            foreach (var key in stale)
            {
                if (posts[key] != null) WallVisual.Kill(posts[key].gameObject);
                posts.Remove(key);
            }

            foreach (var pair in nodes)
            {
                var n = pair.Value;
                if (n.covered) continue;
                float yaw = WallVisual.NodeYaw(n.outgoing);
                if (posts.TryGetValue(pair.Key, out var p))
                {
                    p.rotation = Quaternion.Euler(0f, yaw, 0f);
                    continue;
                }
                posts[pair.Key] = WallVisual.Post(postRoot, n.at, yaw).transform;
            }
        }

        static void AddEnd(Dictionary<long, Node> nodes, Vector3 at, Vector3 outward, bool gateHere)
        {
            long key = Key(at);
            if (!nodes.TryGetValue(key, out var n))
            {
                n = new Node { at = at };
                nodes[key] = n;
            }
            n.covered |= gateHere;
            n.outgoing.Add(WallVisual.Flat(outward));
        }

        /// A wall tower stands in for the post on its node (2026-09-27).
        void CoverTowers(Dictionary<long, Node> nodes)
        {
            if (Camp == null) return;
            foreach (var n in nodes.Values)
                if (!n.covered && Camp.WallTowerAt(n.at) != null) n.covered = true;
        }

        /// The standing posts, for checks.
        public int PostCount => posts.Count;
    }
}
