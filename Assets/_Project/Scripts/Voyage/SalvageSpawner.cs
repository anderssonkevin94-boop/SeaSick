using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Ship.SeaLife;
using UnityEngine;

namespace SeaSick.Voyage
{
    /// Floating temptations. Salvage crates give +2 timber when you steer over
    /// them — small voluntary detours, each a risk/reward call. Plain flotsam
    /// (planks) gives nothing but drifts past as a speed reference, which is
    /// half of why the sea felt static.
    public class SalvageSpawner : MonoBehaviour
    {
        [SerializeField] ShipMotor ship;
        [SerializeField] VoyageManager voyage;
        [SerializeField] int crateCount = 7;
        [SerializeField] int flotsamCount = 16;
        [SerializeField] float pickupRadius = 6f;
        [SerializeField] int salvageValue = 2;
        [SerializeField] float spawnRingMin = 80f;
        [SerializeField] float spawnRingMax = 320f;
        [SerializeField] float despawnDistance = 450f;

        static readonly Vector3 CrateShape = Vector3.one * 1.1f;
        static readonly Vector3 PlankShape = new Vector3(0.35f, 0.15f, 2.2f);

        Transform[] crates;
        Transform[] flotsam;
        // Persistent registry probes: two dozen floaters bobbing every frame
        // belong in the one batched ocean query, not in per-object sampling.
        OceanProbeRegistry.Handle[] crateHandles;
        OceanProbeRegistry.Handle[] flotsamHandles;
        Material crateMat;
        Material plankMat;
        float messageUntil;
        GUIStyle style;

        bool warnedNotBuilt;
        int rebuilt;

        void Start()
        {
            try { Build(); }
            catch (System.Exception e)
            {
                // Swallowed deliberately, and REPORTED. An exception escaping
                // Start leaves the arrays null and the only visible symptom is
                // Update throwing forever, which says nothing about the cause.
                Debug.LogError("SalvageSpawner: Start failed, no salvage this session -- " + e);
            }
        }

        void Build()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            if (voyage == null) voyage = FindFirstObjectByType<VoyageManager>();

            crates = new Transform[crateCount];
            flotsam = new Transform[flotsamCount];
            Bind();
        }

        /// Makes the floater set whole: creates whatever GameObject is missing
        /// and registers a fresh ocean handle for every one of them. Safe to
        /// call again at any point — it only builds what is not already there,
        /// which is what makes recovering from a domain reload a rebind rather
        /// than a second set of crates.
        void Bind()
        {
            EnsureMaterials();
            // Anything we still hold is an orphan by the time we get here: a
            // domain reload takes the registry's static list with it.
            Release();
            crateHandles = BindSet(crates, "SalvageCrate", CrateShape, crateMat, isCrate: true);
            flotsamHandles = BindSet(flotsam, "Flotsam", PlankShape, plankMat, isCrate: false);
        }

        /// Astra's sea discovery kit v1 (Kevin approved 2026-09-30): a crate
        /// floater is the `SalvageCluster` (approved crate, two broken boards),
        /// a flotsam floater one of the board pieces, picked by its index so
        /// the sea shows a mix of lashed bundles, long and short boards
        /// without drawing from `Random`. The art is authored surface-centred
        /// (cluster: crate bottom .22 m under the origin), and the floater
        /// root still rides the wave at +.15 m, so the art child is lowered
        /// `ArtDrop` to sit in the water instead of hovering. Movement, pickup
        /// and respawn belong to the root and are unchanged. When a model is
        /// missing the old cube is built.
        const float ArtDrop = 0.12f;

        static string FlotsamModel(int i)
        {
            switch (i % 4)
            {
                case 0: return SeaKit.LashedBoardBundle;
                case 1: return SeaKit.BrokenBoardLong;
                case 2: return SeaKit.BrokenBoardShort;
                default: return SeaKit.BrokenBoardLong;
            }
        }

        OceanProbeRegistry.Handle[] BindSet(Transform[] set, string name, Vector3 shape, Material mat, bool isCrate)
        {
            var handles = new OceanProbeRegistry.Handle[set.Length];
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] == null)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(transform, true);
                    var art = SeaKit.Spawn(isCrate ? SeaKit.SalvageCluster : FlotsamModel(i),
                        go.transform, new Vector3(0f, -ArtDrop, 0f));
                    if (art == null)
                    {
                        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.name = "Placeholder";
                        Object.Destroy(cube.GetComponent<Collider>());
                        cube.transform.SetParent(go.transform, false);
                        cube.transform.localScale = shape;
                        cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    }
                    set[i] = go.transform;
                    Respawn(set[i]);
                    rebuilt++;
                }
                handles[i] = OceanProbeRegistry.Register(set[i].position);
            }
            return handles;
        }

        void EnsureMaterials()
        {
            if (crateMat == null)
            {
                crateMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                crateMat.SetColor("_BaseColor", new Color(0.72f, 0.52f, 0.28f));
            }
            if (plankMat == null)
            {
                plankMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                plankMat.SetColor("_BaseColor", new Color(0.42f, 0.32f, 0.22f));
            }
        }

        void Release()
        {
            if (crateHandles != null)
                foreach (var h in crateHandles) OceanProbeRegistry.Unregister(h);
            if (flotsamHandles != null)
                foreach (var h in flotsamHandles) OceanProbeRegistry.Unregister(h);
            crateHandles = null;
            flotsamHandles = null;
        }

        void OnDestroy()
        {
            Release();
            if (crateMat != null) Object.Destroy(crateMat);
            if (plankMat != null) Object.Destroy(plankMat);
        }

        void Update()
        {
            if (ship == null) return;
            if (!EnsureFloaters()) return;

            float t = Time.time;
            Vector3 shipPos = ship.transform.position;

            for (int i = 0; i < crates.Length; i++)
                UpdateFloater(crates[i], shipPos, crateHandles[i], t, isCrate: true, i);
            for (int i = 0; i < flotsam.Length; i++)
                UpdateFloater(flotsam[i], shipPos, flotsamHandles[i], t, isCrate: false, i);
        }

        /// True when there is a complete set of floaters to update.
        ///
        /// Start builds two halves of one state and only one half survives a
        /// domain reload — which happens mid-play every time anything under
        /// Assets/ changes while you are playing, the same event that kills a
        /// running probe (docs/DEV-TOOLS.md). The Transform arrays are
        /// UnityEngine.Object references and Unity's backup restores them; the
        /// handle arrays are plain C# objects it cannot serialise and come back
        /// NULL, and OceanProbeRegistry's static list is emptied outright.
        /// Start is not called again.
        ///
        /// So "did Start finish?" — which is all the arrays used to be asked —
        /// is the wrong question every frame after the first: it reads the half
        /// that survives, passes, and the next line dereferences the half that
        /// did not. The right question is whether the state is whole NOW, and
        /// the right answer to a reload is to rebind, not to give up: it is a
        /// normal editor event here, not a broken session.
        bool EnsureFloaters()
        {
            if (crates == null || flotsam == null)
            {
                if (!warnedNotBuilt)
                {
                    warnedNotBuilt = true;
                    Debug.LogWarning("SalvageSpawner: Start did not finish building its "
                        + "floaters, so there is no salvage this session. See the error "
                        + "logged by Start above for the cause.");
                }
                return false;
            }

            if (Bound(crateHandles, crates) && Bound(flotsamHandles, flotsam)) return true;

            rebuilt = 0;
            try { Bind(); }
            catch (System.Exception e)
            {
                // Into the warn-once path above rather than round this branch
                // every frame, which is the failure this whole method is here
                // to stop happening.
                crates = null;
                flotsam = null;
                Debug.LogError("SalvageSpawner: could not rebind its floaters -- " + e);
                return false;
            }
            Debug.Log($"SalvageSpawner: rebound {crates.Length + flotsam.Length} floaters to "
                + $"the ocean registry after a domain reload ({rebuilt} had to be rebuilt).");
            return true;
        }

        static bool Bound(OceanProbeRegistry.Handle[] handles, Transform[] set)
            => handles != null && handles.Length == set.Length;

        void UpdateFloater(Transform f, Vector3 shipPos, OceanProbeRegistry.Handle handle,
            float t, bool isCrate, int seed)
        {
            Vector3 p = f.position;
            handle.position = p;
            if (OceanSampler.Ready)
                p.y = handle.sample.height + 0.15f;
            f.position = p;
            f.rotation = Quaternion.Euler(
                Mathf.Sin(t * 0.9f + seed * 2.1f) * 8f,
                seed * 47f + t * 3f,
                Mathf.Cos(t * 0.7f + seed * 1.3f) * 8f);

            Vector3 flat = p - shipPos;
            flat.y = 0f;
            float dist = flat.magnitude;

            if (isCrate && dist < pickupRadius)
            {
                voyage.AddSalvage(salvageValue);
                messageUntil = Time.time + 1.8f;
                Respawn(f);
            }
            else if (dist > despawnDistance)
            {
                Respawn(f);
            }
        }

        void Respawn(Transform f)
        {
            Vector3 basePos = ship != null ? ship.transform.position : Vector3.zero;
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(spawnRingMin, spawnRingMax);
            f.position = new Vector3(
                basePos.x + Mathf.Sin(ang) * dist, 0f,
                basePos.z + Mathf.Cos(ang) * dist);
        }

        void OnGUI()
        {
            // Draw-only panel: skip the non-Repaint events. See StatusHUD for
            // the measurement — IMGUI runs OnGUI once per event, and the
            // discarded passes were the game's biggest source of GC garbage.
            if (Event.current.type != EventType.Repaint) return;
            if (Time.time > messageUntil) return;
            // Same IMGUI-blind-spot suppression as the rest of the HUD
            // (2026-09-26 review: "+2 timber" floated over the Home card --
            // this is what that was, not `CampToasts`/`ReturnSummary`).
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            // High and centred under the compass, not a full-width strip
            // across the middle of the water. The rect was the whole screen
            // width at 0.62 of its height -- text centred in it, but a full
            // -width rect over the sea all the same, and 0.02 off the jettison
            // button that used to live at 0.60.
            var r = SeaSick.UI.HudLayout.ToastRow(SeaSick.UI.UITheme.Unit * 2f);
            GUI.Label(r, $"+{salvageValue} timber", SeaSick.UI.UITheme.Toast);
        }
    }
}
