using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Astra's real dry-dock slip kit, 2026-09-26**, replacing
    /// `BuildingFactory.DryDockBoxes`' placeholder the way `PierVisual`
    /// replaced the box pier. Decoration only: `DryDockSlip`'s own logic
    /// (`Length`/`Width`/`LandEnd`/`SeaEnd`/`PreviewAnchor`) is untouched --
    /// this hangs one child, `DryDockKit`, off the building root and chains
    /// Astra's three pieces along it.
    ///
    /// **The kit's contract**
    /// (`art-staging/drydock-astra-lvl1-v1/manifest.json`, mirrored at
    /// `Resources/Buildings/DryDockSlip/manifest.json`): `DryDock_SeaEnd`
    /// (1.5 m), `DryDock_Bay` (3.0 m, repeated) and `DryDock_Head` (4.5 m),
    /// chained sea to land -- each next piece's origin on the previous
    /// piece's `Snap_Land`. Every piece's own origin IS its `Snap_Sea` (both
    /// sit at the piece's local zero, verified below rather than assumed),
    /// at GROUND level; `Snap_Land` sits at local (0, lengthM, 0) in the
    /// piece's own raw import axes -- the manifest's `slipAxis`. `DryDock_Bay`
    /// additionally carries `Keel_Rest` and `DryDock_Head` carries
    /// `Interaction_Point`, both offset from the sea->land axis by the
    /// piece's own "up" (ground to walkway/keel), which is how the up axis
    /// is measured. `DryDock_SeaEnd`'s own extra marker (`Sea_Entry`)
    /// coincides with its `Snap_Sea` and carries no height information, so
    /// its up axis is borrowed from `DryDock_Bay` once the two are checked
    /// to agree -- all three pieces come out of the same export.
    ///
    /// **No axis assumed.** Every direction used here is read off the
    /// asset's own marker transforms at measure time, never off a stored
    /// Euler angle, so a re-export that changes the FBX's baked import
    /// rotation is still read correctly (or refused with a reason, the way
    /// `PierVisual` refuses a kit it cannot make sense of).
    public static class DryDockVisual
    {
        public const string Folder = "Buildings/DryDockSlip/";
        const string SeaEndAsset = "DryDock_SeaEnd", BayAsset = "DryDock_Bay", HeadAsset = "DryDock_Head";
        /// The one child this puts on a dry dock's root.
        public const string VisualName = "DryDockKit";

        /// Lengths along the slip, metres -- the manifest's
        /// `lengthAlongSlipM` per piece.
        public const float SeaEndLen = 1.5f, BayLen = 3f, HeadLen = 4.5f;
        const float AxisTolDeg = 3f;
        const float LenTolM = 0.05f;

        /// Why the last `Build` fell back to boxes, or empty if it did not.
        public static string LastFailure { get; private set; } = "";

        sealed class Piece
        {
            public GameObject asset;
            /// Canonical (X = sea -> land, Y = up, Z = across) -> the raw
            /// import axes this piece's own mesh sits in. `Quaternion.identity`
            /// until measured.
            public Quaternion rot = Quaternion.identity;
            public float lengthM;
        }

        static Piece seaEnd, bay, head;
        static readonly HashSet<string> warned = new HashSet<string>();

        /// Forget the measured kit, so the next build reloads the prefabs.
        /// For the editor after a re-import without a domain reload.
        public static void ResetKit() { seaEnd = bay = head = null; LastFailure = ""; warned.Clear(); }

        // --- build ------------------------------------------------------

        /// Lay the kit out under `root` (a dry dock's `Building` root, at
        /// walkway height, centred on `plan.footprint.x`, local +X toward
        /// the sea -- `DryDockSlip.SeaEnd`/`LandEnd`). False, with nothing
        /// left behind, when the kit cannot be used; the caller raises the
        /// placeholder boxes instead. Returns the kit's true overall length
        /// (sea end + N bays + head, which need not equal `plan.footprint.x`
        /// to the centimetre -- the bay count is chosen to fill it) so the
        /// caller can size the slab and pads to match what was actually
        /// drawn rather than the plan's nominal figure.
        public static bool Build(Transform root, BuildPlan plan, out float builtLength)
        {
            builtLength = plan.footprint.x;
            if (root == null) return false;
            if (!EnsureKit(out string why))
            {
                LastFailure = why;
                WarnOnce("[DryDock] Astra's dry dock kit not used, box slip instead: " + why);
                return false;
            }

            var vis = new GameObject(VisualName).transform;
            vis.SetParent(root, false);
            try
            {
                builtLength = Assemble(root, vis, plan);
                LastFailure = "";
                return true;
            }
            catch (System.Exception e)
            {
                LastFailure = "layout threw: " + e.Message;
                Debug.LogWarning("[DryDock] kit layout failed, box slip instead: " + e);
                vis.SetParent(null, false);
                vis.gameObject.SetActive(false);
                Kill(vis.gameObject);
                builtLength = plan.footprint.x;
                return false;
            }
        }

        static float Assemble(Transform root, Transform vis, BuildPlan plan)
        {
            float wantLen = plan.footprint.x;
            int bays = Mathf.Max(2, Mathf.RoundToInt((wantLen - SeaEndLen - HeadLen) / BayLen));
            float total = SeaEndLen + bays * BayLen + HeadLen;

            // The kit's own origin (each piece's Snap_Sea) is GROUND level,
            // not the walkway -- `root` sits at the walkway
            // (`BuildPlans.DryDockDeck` above mean water, same convention
            // as `PierVisual`'s `KitDeck` lower). Lower the kit by the deck
            // height and slide it so the run is centred on the footprint.
            vis.localPosition = new Vector3(total * 0.5f, -BuildPlans.DryDockDeck, 0f);
            vis.localRotation = Quaternion.identity;
            vis.localScale = Vector3.one;

            // Chain sea to land: the first piece's Snap_Sea sits at the
            // run's own local zero (the sea-most point), each next piece's
            // origin lands on the previous one's Snap_Land -- exactly the
            // manifest's assembly order, walked along local -X since `vis`'s
            // origin was just placed at the SEA end of the run.
            float x = 0f;
            Place(seaEnd, vis, x, "SeaEnd"); x -= SeaEndLen;
            for (int i = 0; i < bays; i++) { Place(bay, vis, x, $"Bay_{i:00}"); x -= BayLen; }
            Place(head, vis, x, "Head");

            return total;
        }

        /// Places one piece with its own Snap_Sea (== its local origin) at
        /// `vis`-local X = `x`, oriented so the piece's own sea->land and
        /// up axes land on canonical X/Y.
        static void Place(Piece p, Transform vis, float x, string name)
        {
            var go = Object.Instantiate(p.asset, vis, false);
            go.name = name;
            var t = go.transform;
            t.localRotation = p.rot;
            t.localScale = p.asset.transform.localScale;
            t.localPosition = new Vector3(x, 0f, 0f);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
        }

        // --- measuring the kit, once --------------------------------------

        static bool EnsureKit(out string why)
        {
            why = "";
            if (seaEnd != null && bay != null && head != null
                && seaEnd.asset != null && bay.asset != null && head.asset != null)
                return true;

            var seAsset = Load(SeaEndAsset, out why); if (seAsset == null) return false;
            var byAsset = Load(BayAsset, out why); if (byAsset == null) return false;
            var hdAsset = Load(HeadAsset, out why); if (hdAsset == null) return false;
            var se = new Piece { asset = seAsset };
            var by = new Piece { asset = byAsset };
            var hd = new Piece { asset = hdAsset };

            if (!Measure(by, "Snap_Sea", "Snap_Land", "Keel_Rest", BayLen, out why)) return false;
            if (!Measure(hd, "Snap_Sea", "Snap_Land", "Interaction_Point", HeadLen, out why)) return false;
            float upAgree = Quaternion.Angle(by.rot, hd.rot);
            if (upAgree > AxisTolDeg)
            { why = $"DryDock_Bay and DryDock_Head disagree on the kit's up axis by {upAgree:F1} deg"; return false; }

            // DryDock_SeaEnd has no marker off its sea->land axis (Sea_Entry
            // sits on Snap_Sea), so measure its along axis alone and borrow
            // the up axis from the bay -- same export, checked to agree above.
            if (!Measure(se, "Snap_Sea", "Snap_Land", null, SeaEndLen, out why)) return false;
            se.rot = by.rot;

            seaEnd = se; bay = by; head = hd;
            return true;
        }

        static GameObject Load(string name, out string why)
        {
            why = "";
            var asset = Resources.Load<GameObject>(Folder + name);
            if (asset == null) why = $"no asset at Resources/{Folder}{name}";
            return asset;
        }

        /// Reads one piece's sea->land axis (`seaName` -> `landName`,
        /// required) and, when `upName` is given, its up axis (the
        /// component of `seaName -> upName` that is NOT along sea->land).
        /// Builds `p.rot`: canonical (X = sea->land, Y = up, Z = across) ->
        /// the piece's own raw import axes, via the same "measure both
        /// frames the same way" trick as `PierVisual.Module`/`Axis` -- so a
        /// reflection can never sneak in silently.
        static bool Measure(Piece p, string seaName, string landName, string upName, float lenM, out string why)
        {
            why = "";
            var root = p.asset.transform;
            var sea = FindStem(root, seaName);
            var land = FindStem(root, landName);
            if (sea == null || land == null)
            { why = $"{p.asset.name}: missing {seaName}/{landName} marker"; return false; }

            Vector3 alongRaw = land.localPosition - sea.localPosition;
            float units = alongRaw.magnitude;
            if (units < 1e-6f) { why = $"{p.asset.name}: {seaName} and {landName} coincide"; return false; }
            Vector3 along = alongRaw / units;

            float scale = root.localScale.x;
            float measuredLen = units * scale;
            if (Mathf.Abs(measuredLen - lenM) > LenTolM)
                WarnOnce($"[DryDock] {p.asset.name}: {seaName}->{landName} measures {measuredLen:F2} m, "
                    + $"manifest says {lenM:F2} m");
            p.lengthM = lenM;

            if (upName != null)
            {
                var upMarker = FindStem(root, upName);
                if (upMarker == null) { why = $"{p.asset.name}: missing {upName} marker"; return false; }
                Vector3 raw = upMarker.localPosition - sea.localPosition;
                Vector3 up = Vector3.ProjectOnPlane(raw, along);
                if (up.magnitude < 1e-6f)
                { why = $"{p.asset.name}: {upName} carries no height off {seaName}->{landName}, cannot read the up axis"; return false; }
                up.Normalize();
                Vector3 across = Vector3.Cross(along, up);

                Vector3 Xc = new Vector3(-1f, 0f, 0f);   // canonical: sea -> land is LOCAL -X (SeaEnd/LandEnd convention)
                Vector3 Yc = Vector3.up;
                Vector3 Zc = Vector3.Cross(Xc, Yc);

                Quaternion qNative = Quaternion.LookRotation(across, up);
                Quaternion qCanon = Quaternion.LookRotation(Zc, Yc);
                p.rot = qCanon * Quaternion.Inverse(qNative);
            }
            return true;
        }

        /// A descendant by exact name, or by name before an importer's
        /// `.001`-style suffix (several markers share a base name across
        /// one Blender file and the exporter carries the suffix through).
        static Transform FindStem(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || BuildingFactory.Stem(t.name) == name) return t;
            return null;
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        static void WarnOnce(string msg)
        {
            if (warned.Add(msg)) Debug.LogWarning(msg);
        }
    }
}
