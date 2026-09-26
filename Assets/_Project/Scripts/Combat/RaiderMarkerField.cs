using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeaSick.Combat
{
    /// A small red downward-pointing triangle hovering over every raider who
    /// has actually set foot on the beach -- Kevin, 2026-09-26: "a little red
    /// triangle over any disembarked raider" so a landing party is findable
    /// against the grass and huts, not just a dot on the minimap.
    ///
    /// `RaidWalker` only exists once `RaidParty.Begin` has put a body on the
    /// sand (see that file) -- there is no walker for a man still aboard the
    /// ship. So "the component is alive" already means "ashore", and the
    /// only hook this needed in `RaidWalker.cs` is `OnEnable`/`OnDisable`
    /// calling `Enter`/`Leave` below; everything else about the marker lives
    /// here, self-contained, so this file and a bug fix elsewhere in
    /// `RaidWalker` don't collide.
    ///
    /// One shared triangle mesh, one shared material on `SeaSick/BlueprintGhost`
    /// -- unlit, `ZTest Always` (reads through scenery, which is the point of
    /// a "hard to see" callout) and already kept alive for the iOS build by
    /// `Keep_BlueprintGhost.mat` (see `Resources/Shaders/Keepalive/README.md`),
    /// so no `Shader.Find`-returns-null risk on the phone. A small pool of
    /// marker transforms is reused across raids instead of instantiated per
    /// raider; `LateUpdate` walks the (never more than a handful) ashore list
    /// once, with no allocation and no `GetComponent` in the loop.
    public class RaiderMarkerField : MonoBehaviour
    {
        const float HoverHeight = 1.8f;
        const float TriHalf = 0.28f;
        static readonly Color MarkColour = new Color(0.92f, 0.14f, 0.14f, 0.95f);

        // Roughly constant screen size on a phone: scale grows with distance
        // rather than the marker shrinking to nothing at sailing range or
        // swallowing the screen up close. Heuristic, not a projection --
        // this is a callout, not a measuring instrument.
        const float DistanceScale = 0.045f;
        const float MinScale = 0.6f;
        const float MaxScale = 5f;

        static readonly List<RaidWalker> ashore = new List<RaidWalker>();
        static RaiderMarkerField instance;
        static Mesh triMesh;
        static Material triMat;

        readonly List<Transform> pool = new List<Transform>();
        Camera cam;

        /// Called from `RaidWalker.OnEnable` -- the walker exists, so he is
        /// on the sand.
        public static void Enter(RaidWalker w)
        {
            if (w != null && !ashore.Contains(w)) ashore.Add(w);
        }

        /// Called from `RaidWalker.OnDisable` -- fleeing into the water,
        /// boarding back, or destroyed outright all disable the component
        /// first, so this is the one place that needs to know.
        public static void Leave(RaidWalker w) => ashore.Remove(w);

        // --- lifecycle: stand once per play session, wipe on every reload ---

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReset()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// A Continue/Load/New-voyage reload (`SceneManager.LoadScene`)
        /// destroys every `RaidWalker` from the last game and this field's
        /// own GameObject with it (it is not `DontDestroyOnLoad`). Dropping
        /// the static list and letting `Stand` below put up a fresh instance
        /// is cheaper than trying to reconcile stale entries against a scene
        /// that no longer has them.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ashore.Clear();
            instance = null;
            // `Stand` below is [RuntimeInitializeOnLoadMethod(AfterSceneLoad)],
            // which fires once per DOMAIN load, not once per SCENE load --
            // with domain reload off (this project's convention), every
            // reload after the first left `instance` null forever and the
            // comment above ("letting Stand put up a fresh instance") never
            // actually happened again: the field silently stopped drawing
            // any marker for the rest of the session after one Continue/
            // Load/New. Call it directly here instead of trusting the
            // attribute to refire.
            Stand();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Stand()
        {
            if (instance != null) return;
            var go = new GameObject("RaiderMarkerField");
            instance = go.AddComponent<RaiderMarkerField>();
        }

        void LateUpdate()
        {
            // Walkers normally unregister themselves via OnDisable before
            // this ever sees a null; the sweep is a cheap safety net, not
            // the primary cleanup path.
            ashore.RemoveAll(w => w == null);
            if (ashore.Count == 0 && pool.Count == 0) return;

            if (cam == null) cam = Camera.main;
            EnsureAssets();

            while (pool.Count < ashore.Count) pool.Add(MakeMarker());

            int shown = 0;
            for (int i = 0; i < ashore.Count; i++)
            {
                var w = ashore[i];
                var mk = pool[shown];
                if (!mk.gameObject.activeSelf) mk.gameObject.SetActive(true);

                Vector3 pos = w.transform.position + Vector3.up * HoverHeight;
                mk.position = pos;

                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - pos;
                    if (toCam.sqrMagnitude > 0.0001f)
                        mk.rotation = Quaternion.LookRotation(toCam, Vector3.up);
                    float dist = toCam.magnitude;
                    float s = Mathf.Clamp(dist * DistanceScale, MinScale, MaxScale);
                    mk.localScale = new Vector3(s, s, s);
                }
                shown++;
            }
            for (int i = shown; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        Transform MakeMarker()
        {
            var go = new GameObject("RaiderMarker");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = triMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = triMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            go.SetActive(false);
            return go.transform;
        }

        static void EnsureAssets()
        {
            if (triMesh != null && triMat != null) return;

            if (triMesh == null)
            {
                // A downward-pointing triangle in the marker's own local XY
                // plane, tip at -Y. Both windings are present so the marker
                // reads correctly under `Cull Back` from either rotation the
                // billboard math above hands it, without touching the shared
                // BlueprintGhost shader to add `Cull Off`.
                var mesh = new Mesh { name = "RaiderMarkerTri" };
                var verts = new[]
                {
                    new Vector3(-TriHalf,  TriHalf, 0f),
                    new Vector3( TriHalf,  TriHalf, 0f),
                    new Vector3( 0f,      -TriHalf, 0f),
                };
                mesh.vertices = verts;
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
                mesh.RecalculateBounds();
                triMesh = mesh;
            }

            if (triMat == null)
            {
                var shader = Shader.Find("SeaSick/BlueprintGhost");
                var mat = new Material(shader);
                mat.SetColor("_BaseColor", MarkColour);
                triMat = mat;
            }
        }
    }
}
