using UnityEngine;

namespace SeaSick.World
{
    /// A blueprint standing on the ground: what the player sited, waiting on
    /// the wood to build it.
    ///
    /// **This object owns nothing.** The blueprint's truth is
    /// `OutpostLedger.pending` — where it is, what it costs, how much has been
    /// delivered — because a camp has to go on being half built while its
    /// island is unloaded and the player is two hours' sail away. This is the
    /// rendering of that row, put up when the terrain is there to stand it on
    /// and thrown away when it is not, exactly like the parked crew bodies.
    ///
    /// So: nothing here may be the only copy of anything. If this component is
    /// destroyed mid-build and rebuilt from the ledger on the next visit, the
    /// player must not be able to tell.
    public class BuildSite : MonoBehaviour
    {
        /// The plan this is a drawing of.
        public string PlanId { get; private set; }

        /// **The queue row this object draws (2026-09-22).** A camp can hold
        /// several drawings now, so "the pending one" is not an answer: the
        /// object carries a reference to its own row, and `Outpost` is what
        /// binds them (`EnsureBlueprints`). The row is still the truth and
        /// this is still only its rendering -- destroy this and the row goes
        /// on being built while the island is unloaded.
        public PendingBuild Row { get; private set; }

        public void Bind(PendingBuild row) { Row = row; }

        Outpost outpost;
        GameObject ghost;
        Transform stack;
        int drawnLogs = -1;
        float drawnFill = -1f;

        /// How solid the drawing gets as it fills. It never reaches 1: a
        /// blueprint one log short of finished must still read as a blueprint,
        /// or the player stops being able to tell what is built from what is
        /// promised — which is the one thing this whole shape exists to say.
        const float AlphaEmpty = 0.22f;
        const float AlphaFull = 0.55f;

        /// **How solid it gets while it is being RAISED (2026-09-23).** The
        /// stocking phase fills it from `AlphaEmpty` to `AlphaFull`; the
        /// building phase carries it from there to here, so a player can
        /// tell the two apart at a glance -- a drawing getting less
        /// see-through while nothing is being carried to it is a building
        /// going up. It still stops short of 1: the moment it IS the
        /// building, this object is destroyed and the real mesh stands in
        /// its place (`Outpost.RaiseRow`).
        const float AlphaRaising = 0.85f;

        /// Logs drawn in the stack beside it. The cost is small and the stack
        /// is the gauge, so every log delivered shows up as one more log.
        const int MaxDrawnLogs = 16;

        public static BuildSite Place(Outpost owner, BuildPlan plan,
            Vector3 floorAt, Quaternion facing, float footing)
        {
            var root = new GameObject("BuildSite_" + plan.id);
            root.transform.SetParent(owner.transform, true);
            root.transform.SetPositionAndRotation(floorAt, facing);

            var site = root.AddComponent<BuildSite>();
            site.outpost = owner;
            site.PlanId = plan.id;
            site.ghost = BuildingFactory.Ghost(plan, root.transform,
                floorAt, facing, footing, AlphaEmpty);

            // Four stakes and nothing between them. The ghost says what is
            // coming; the stakes say somebody has been here and measured it,
            // and they are what stays legible from the air once the view is
            // zoomed out past the point where a translucent hut is a smudge.
            float len = plan.footprint.x, wid = plan.footprint.y;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Stake(root.transform, new Vector3(
                        sx * len * 0.5f, 0f, sz * wid * 0.5f));

            var stackGo = new GameObject("Delivered");
            stackGo.transform.SetParent(root.transform, false);
            stackGo.transform.localPosition = new Vector3(
                -len * 0.5f - 0.9f, 0f, 0f);
            site.stack = stackGo.transform;

            return site;
        }

        /// **The drawing of a WALL segment (2026-09-23).** Same object,
        /// same row, same log stack -- only the ghost is a line between two
        /// posts rather than a hut on a plot, and the stakes are the posts
        /// themselves, which the wall's own drawing already has.
        ///
        /// The root sits at the segment's MIDPOINT, which is where a
        /// hauler walks to and where the sheet hangs: `PendingBuild.x/z`
        /// are that same midpoint, so nothing downstream has to know this
        /// site is a wall.
        public static BuildSite PlaceWall(Outpost owner, BuildPlan plan,
            Vector3 postA, Vector3 postB, bool gate)
        {
            Vector3 mid = 0.5f * (postA + postB);
            Vector3 run = postB - postA;
            run.y = 0f;
            float len = Mathf.Max(0.5f, run.magnitude);
            Quaternion facing = run.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(run.normalized, Vector3.up)
                : Quaternion.identity;

            var root = new GameObject("BuildSite_" + plan.id);
            root.transform.SetParent(owner.transform, true);
            root.transform.SetPositionAndRotation(mid, facing);

            var site = root.AddComponent<BuildSite>();
            site.outpost = owner;
            site.PlanId = plan.id;
            site.ghost = BuildingFactory.WallGhost(root.transform, postA, postB,
                gate, AlphaEmpty);

            // The drawing has to be tappable -- it is how the site's sheet
            // is opened, the same as any other blueprint.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, BuildPlans.PalisadeHeight * 0.5f, 0f);
            box.size = new Vector3(1.2f, BuildPlans.PalisadeHeight, len);

            // The log stack goes beside the middle of the run, a metre off
            // the line, so it never sits inside the wall it is paying for.
            var stackGo = new GameObject("Delivered");
            stackGo.transform.SetParent(root.transform, false);
            stackGo.transform.localPosition = new Vector3(1.4f, 0f, 0f);
            site.stack = stackGo.transform;

            return site;
        }

        static void Stake(Transform parent, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.1f, 1.1f, 0.1f);
            go.transform.localPosition = at + new Vector3(0f, 0.45f, 0f);
            go.transform.localRotation = Quaternion.Euler(
                Random.Range(-7f, 7f), 0f, Random.Range(-7f, 7f));
            go.GetComponent<MeshRenderer>().sharedMaterial = StakeMat();
        }

        static Material stakeMat;

        static Material StakeMat()
        {
            if (stakeMat != null) return stakeMat;
            stakeMat = new Material(Shader.Find(
                WorldArtStyle.Instance != null
                    ? "SeaSick/Environment Toon"
                    : "Universal Render Pipeline/Lit"));
            stakeMat.SetColor("_BaseColor", new Color(0.52f, 0.41f, 0.26f));
            return stakeMat;
        }

        void Update()
        {
            var p = Row;
            if (p == null || p.planId != PlanId) return;
            Refresh(p);
        }

        /// Bring the drawing up to what the row says. Cheap to call every
        /// frame: nothing is rebuilt unless a whole log has landed since the
        /// last one.
        public void Refresh(PendingBuild p)
        {
            // `Progress01` is the stocking in its first half and the
            // raising in its second, so one number drives the whole ramp.
            float fill = p.Progress01;
            if (ghost != null && Mathf.Abs(fill - drawnFill) > 0.02f)
            {
                float a = fill <= 0.5f
                    ? Mathf.Lerp(AlphaEmpty, AlphaFull, fill * 2f)
                    : Mathf.Lerp(AlphaFull, AlphaRaising, (fill - 0.5f) * 2f);
                BuildingFactory.Tint(ghost, a);
                drawnFill = fill;
            }

            if (p.done == drawnLogs || stack == null) return;
            drawnLogs = p.done;

            for (int i = stack.childCount - 1; i >= 0; i--)
                Destroy(stack.GetChild(i).gameObject);

            // **A full stack means DONE.** It used to be one drawn log per log
            // delivered, capped at sixteen -- so a sawmill wanting 24 showed a
            // full stack at two thirds and then sat there looking finished
            // and unbuilt. The stack is the progress bar; it fills with it.
            int n = p.needed <= MaxDrawnLogs
                ? Mathf.Min(p.done, MaxDrawnLogs)
                : Mathf.Clamp(Mathf.FloorToInt(p.Fill01 * MaxDrawnLogs), p.done > 0 ? 1 : 0, MaxDrawnLogs);
            for (int i = 0; i < n; i++)
            {
                int row = i / 3, col = i % 3;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col2 = go.GetComponent<Collider>();
                if (col2 != null) Destroy(col2);
                go.transform.SetParent(stack, false);
                go.transform.localScale = new Vector3(0.22f, 0.7f, 0.22f);
                // Cylinders stand up; a stacked log lies down, and the rows
                // alternate the way a real stack is cross-piled.
                bool across = row % 2 == 1;
                go.transform.localRotation = Quaternion.Euler(
                    across ? 90f : 0f, across ? 0f : 90f, 0f);
                go.transform.localPosition = new Vector3(
                    across ? (col - 1) * 0.3f : 0f,
                    0.12f + row * 0.24f,
                    across ? 0f : (col - 1) * 0.3f);
                go.GetComponent<MeshRenderer>().sharedMaterial = StakeMat();
            }
        }

        /// Take the drawing down. The row it was drawing is the caller's to
        /// clear — this must never be the thing that decides a build is over.
        public void Retire()
        {
            if (this != null) Destroy(gameObject);
        }
    }
}
