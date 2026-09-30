using System.Collections.Generic;
using UnityEngine;
using SeaSick.CameraRig;

namespace SeaSick.World.Life
{
    /// **Phase 3: the forced tombstone-placement flow**
    /// (docs/PLAN-DEATH-RESCUE.md, "The tombstone").
    ///
    /// One process-wide instance (same boot shape as `LifeDevPanel`/
    /// `FeelLab`: `RuntimeInitializeOnLoadMethod` adds itself once, survives
    /// scene loads). Every frame it asks: is there a WATCHED camp with a
    /// pending grave (`GraveRecord.placed == false`) whose `camp` matches
    /// that camp's `OutpostLedger.CampLabel`, or is empty (a death at sea,
    /// future phases)? If so it raises `GraveGate.Blocking` and drives a
    /// ghost tombstone the player must site before anything else ashore
    /// works again. Multiple deaths queue: the oldest (`diedDay`, ties by
    /// registration order) goes first, and finishing one immediately picks
    /// up the next on the very next frame.
    ///
    /// **On-screen controls (2026-09-30, island UI phase 3):** the placement
    /// prompt and the confirm button are the bottom `ThumbBar`'s placement
    /// mode, the same one walls, roads and the first fire use -- with NO
    /// Cancel (`onCancel` null), because the burial is forced. World taps
    /// never fall through: `UIBlocker.SheetBlocked` reads `ThumbBar.Blocks`
    /// (bar + instruction card), and `IslandInput` routes every other tap
    /// ashore to `HandleTap` while `GraveGate.Blocking`.
    ///
    /// **Still IMGUI stand-in** (like `LifeDevPanel`): the story card and the
    /// "All graves" list, restyled later.
    public class GravePlacementFlow : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<GravePlacementFlow>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("GravePlacementFlow");
            go.AddComponent<GravePlacementFlow>();
            DontDestroyOnLoad(go);
        }

        /// A footprint just big enough to keep a tombstone off a doorway --
        /// not a real `BuildPlans` entry (never offered by the build menu),
        /// but a plan struct is the cheapest way to ask `Outpost.CanPlace`
        /// the SAME question every other siting asks: on land, not too
        /// steep, not overlapping a building/keep-out/queued site.
        static readonly BuildPlan GravePlan = new BuildPlan
        {
            id = "__Grave",
            footprint = new Vector2(0.8f, 0.8f),
            ridge = 0.8f,
        };

        /// Metres out from the fire the default spot search starts.
        const float DefaultRadiusStart = 3.5f;
        /// Metres out from the fire the default spot search gives up widening at.
        const float DefaultRadiusRoom = 30f;
        /// Radius `Outpost.Reserve` keeps clear once a grave is planted, so a
        /// later building/wall does not get sited on top of it.
        const float GraveReserveRadius = 0.6f;

        static readonly Color GhostValidColor = new Color(0.85f, 0.85f, 0.8f, 0.75f);
        static readonly Color GhostInvalidColor = new Color(0.9f, 0.15f, 0.15f, 0.75f);

        static GravePlacementFlow instance;
        /// Every stone raised this session (ghost excluded), keyed by grave
        /// name -- so a load's `RestoreGraves` never plants the same grave
        /// twice and a tap can be resolved back to its `GraveRecord`.
        static readonly Dictionary<string, GameObject> stones = new Dictionary<string, GameObject>();

        Outpost campOutpost;
        GraveRecord grave;
        GameObject ghost;
        Material ghostMat;
        Vector3 ghostAt;
        float ghostYaw;
        bool ghostValid;
        string ghostWhy = "";

        // --- the thumb bar's placement mode ---------------------------------
        /// True while WE put the placement up (so we only ever hide our own).
        bool barShown;
        /// The verdict last pushed to the bar; the status is only re-sent
        /// when one of these changes, so a frame allocates nothing.
        bool barValid;
        string barWhy;

        // --- the story-card stand-in ----------------------------------------
        GraveRecord storyShown;
        bool showingAllGraves;
        Vector2 allGravesScroll;

        void Awake() => instance = this;
        void OnDestroy()
        {
            // The bar and `GraveGate` outlive this component (domain reload
            // is off): never leave a placement nobody is running.
            HideBar();
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (grave == null)
            {
                if (!TryFindPending()) return;
                BeginPlacement();
                return;
            }
            Revalidate();
            SyncBar();
        }

        // --- the bar -----------------------------------------------------------

        /// **2026-09-30.** One bar, one placement at a time: while
        /// `CampSiting` has the bar (it cannot begin under a pending grave
        /// -- `Outpost.Raise` refuses -- but a placement already up when the
        /// death lands can), we leave it alone and take it back the frame
        /// it lets go. Shown with or without a camp, like the first fire.
        void SyncBar()
        {
            if (grave == null) return;
            if (SeaSick.UI.CampSiting.Placing) { barShown = false; return; }
            if (!barShown || !SeaSick.UI.Sheets.ThumbBar.PlacementActive) ShowBar();

            string why = ghostValid ? "" : (ghostWhy ?? "");
            if (barValid == ghostValid && barWhy == why) return;
            barValid = ghostValid; barWhy = why;
            PushStatus();
        }

        void ShowBar()
        {
            // No Cancel: the tombstone is forced (`onCancel` null), and no
            // Turn: a stone always faces the fire.
            SeaSick.UI.Sheets.ThumbBar.ShowPlacement(
                "Place " + grave.name + "'s grave",
                "Tap the ground to move it. Somewhere quiet, off the paths.",
                null, null, Confirm, "Lay to rest");
            barShown = true;
            barValid = ghostValid;
            barWhy = ghostValid ? "" : (ghostWhy ?? "");
            PushStatus();
        }

        void PushStatus()
        {
            if (ghostValid)
                SeaSick.UI.Sheets.ThumbBar.SetPlacementStatus("Good spot", true, true);
            else
                SeaSick.UI.Sheets.ThumbBar.SetPlacementStatus(
                    string.IsNullOrEmpty(ghostWhy) ? "Not here"
                        : char.ToUpperInvariant(ghostWhy[0]) + ghostWhy.Substring(1), false, false);
        }

        void HideBar()
        {
            if (!barShown) return;
            barShown = false;
            if (!SeaSick.UI.CampSiting.Placing) SeaSick.UI.Sheets.ThumbBar.HidePlacement();
        }

        // --- finding the queue ----------------------------------------------

        bool TryFindPending()
        {
            // Oldest pending grave first.
            GraveRecord best = null;
            foreach (var g in Lives.Graveyard)
            {
                if (g == null || g.placed) continue;
                if (best == null || g.diedDay < best.diedDay) best = g;
            }
            if (best == null) return false;

            // **Which camp it goes to** (Kevin 2026-09-28, "i cant place the
            // gravestones, it just shows up red wherever i press"): a death
            // at sea has no camp, and the first WATCHED camp in the list used
            // to win -- the empty home camp while he was looking at
            // Island_2, so every tap on Island_2 was "past the shore" of the
            // home island. Now: its own camp if it has one; else the watched
            // camp with people nearest the ship (then any watched camp).
            Outpost camp = null;
            float bestD = float.MaxValue;
            var ship = Object.FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
            Vector3 from = ship != null ? ship.transform.position
                : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            foreach (var o in Outpost.All)
            {
                if (o == null || !o.Watched || o.Ledger == null) continue;
                if (!string.IsNullOrEmpty(best.camp) && best.camp != o.Ledger.CampLabel) continue;
                float d = Vector3.Distance(from, o.CampCentre);
                if (o.Ledger.hands == null || o.Ledger.hands.Count == 0) d += 100000f;
                if (d < bestD) { bestD = d; camp = o; }
            }
            if (camp == null) return false;

            // Never in the middle of a raid on that camp: the grave waits
            // for the all clear (the Hide-all button must stay reachable).
            var raid = SeaSick.Combat.RaidParty.Active;
            if (raid != null && raid.Camp == camp) return false;

            campOutpost = camp;
            grave = best;
            return true;
        }

        /// A sea grave follows the tap to whichever watched camp's island
        /// was tapped.
        void AdoptCampAt(Vector3 p)
        {
            if (grave == null || !string.IsNullOrEmpty(grave.camp)) return;
            foreach (var o in Outpost.All)
            {
                if (o == null || o == campOutpost || !o.Watched || o.Island == null) continue;
                if (Island.FlatDistance(p, o.Island.transform.position) > o.Island.RadiusToward(p)) continue;
                campOutpost = o;
                if (ghost != null) ghost.transform.SetParent(o.transform, true);
                return;
            }
        }

        void BeginPlacement()
        {
            GraveGate.Blocking = true;
            FindDefaultSpot();
            ghost = GraveVisual.Build(campOutpost.transform, "Grave_Ghost_" + grave.name, grave.name,
                LifeStory.Fnv32(grave.name), out ghostMat);
            ghost.transform.SetPositionAndRotation(ghostAt, Quaternion.Euler(0f, ghostYaw, 0f));
            Revalidate();
            SyncBar();
        }

        float FacingFireYaw(Vector3 p)
        {
            Vector3 toCentre = campOutpost.CampCentre - p;
            toCentre.y = 0f;
            return toCentre.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toCentre.normalized, Vector3.up).eulerAngles.y
                : 0f;
        }

        /// A free ground spot a few metres out from the fire -- the golden-
        /// angle spiral `Outpost.Raise` uses for "somewhere in the clearing",
        /// asking the SAME `CanPlace` gate a ghost or a real raise would.
        /// Never fails outright: if every candidate refuses, the last one
        /// tried stands as the ghost's start and the player must drag it
        /// (Kevin's brief guarantees the player can always move it).
        void FindDefaultSpot()
        {
            Vector3 centre = campOutpost.CampCentre;
            const int Tries = 180;
            const float Golden = 2.39996323f;
            Vector3 fallback = centre + new Vector3(DefaultRadiusStart, 0f, 0f);
            for (int i = 0; i < Tries; i++)
            {
                float t = (i + 0.5f) / Tries;
                float r = DefaultRadiusStart + (DefaultRadiusRoom - DefaultRadiusStart) * Mathf.Sqrt(t);
                float a = i * Golden;
                Vector3 p = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float yaw = FacingFireYaw(p);
                if (i == 0) fallback = p;
                if (campOutpost.CanPlace(GravePlan, p, yaw, out _))
                {
                    SetGhostAt(p);
                    return;
                }
            }
            SetGhostAt(fallback);
        }

        void SetGhostAt(Vector3 p)
        {
            var h = GroundPick.Height;
            p.y = h != null ? h(p.x, p.z) : p.y;
            ghostAt = p;
            ghostYaw = FacingFireYaw(p);
        }

        void Revalidate()
        {
            ghostWhy = "";
            ghostValid = campOutpost != null && campOutpost.CanPlace(GravePlan, ghostAt, ghostYaw, out ghostWhy);
            if (ghostMat != null) ghostMat.SetColor("_BaseColor", ghostValid ? GhostValidColor : GhostInvalidColor);
        }

        // --- moving / confirming ---------------------------------------------

        /// Called by `IslandInput.RegisterTap` while `GraveGate.Blocking` --
        /// the same ground-tap-to-move-the-ghost the ordinary siting tools
        /// use (`GroundPick.FromScreen`, `UI/CampSiting.cs`'s pattern), just
        /// against the height field rather than a collider so it works at
        /// any zoom.
        public static void HandleTap(Vector2 screen)
        {
            if (instance == null || instance.grave == null || Camera.main == null) return;
            if (GroundPick.FromScreen(Camera.main, screen, out Vector3 hit))
            {
                instance.AdoptCampAt(hit);
                instance.MoveGhostTo(hit);
            }
        }

        void MoveGhostTo(Vector3 p)
        {
            SetGhostAt(p);
            if (ghost != null) ghost.transform.SetPositionAndRotation(ghostAt, Quaternion.Euler(0f, ghostYaw, 0f));
            Revalidate();
        }

        void Confirm()
        {
            if (!ghostValid || grave == null || campOutpost == null) return;

            grave.x = ghostAt.x;
            grave.z = ghostAt.z;
            grave.yaw = ghostYaw;
            grave.placed = true;

            if (ghost != null) Destroy(ghost);
            ghost = null; ghostMat = null;

            var stone = GraveVisual.Build(campOutpost.transform, "Grave_" + grave.name, grave.name,
                LifeStory.Fnv32(grave.name), out _);
            stone.transform.SetPositionAndRotation(ghostAt, Quaternion.Euler(0f, ghostYaw, 0f));
            stones[grave.name] = stone;
            campOutpost.Reserve(ghostAt, GraveReserveRadius);

            HideBar();
            var placed = grave;
            grave = null;
            campOutpost = null;
            GraveGate.Blocking = false;

            // Right after placing: the story card, per Kevin's brief.
            ShowStory(placed);
            // If another grave is pending it is picked up next `Update`.
        }

        // --- restore on load ---------------------------------------------------

        /// **Called from `Outpost.Adopt`**, after buildings/walls/roads/
        /// ladders stand: every ALREADY-PLACED grave at this camp is raised
        /// again exactly where it was sited -- x/z verbatim, yaw verbatim,
        /// only Y re-sampled off the terrain, the same "buildings never
        /// move" shape `Adopt` uses for everything else. Also re-seeds
        /// `Outpost.Reserve` for it, since `reserved` is runtime-only and
        /// does not itself survive a load.
        public static void RestoreGraves(Outpost o)
        {
            if (o == null || o.Ledger == null) return;
            string label = o.Ledger.CampLabel;
            var h = GroundPick.Height;
            foreach (var g in Lives.Graveyard)
            {
                if (g == null || !g.placed) continue;
                if (g.camp != label) continue;
                if (stones.ContainsKey(g.name)) continue;

                Vector3 p = new Vector3(g.x, 0f, g.z);
                p.y = h != null ? h(p.x, p.z) : p.y;

                var stone = GraveVisual.Build(o.transform, "Grave_" + g.name, g.name,
                    LifeStory.Fnv32(g.name), out _);
                stone.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, g.yaw, 0f));
                stones[g.name] = stone;
                o.Reserve(p, GraveReserveRadius);
            }
        }

        // --- the story card (tap-to-reopen) ------------------------------------

        /// Called by `IslandInput.RegisterTap` (not blocking) before it
        /// resolves the tap against a building/villager, so tapping a
        /// tombstone reliably opens its story rather than whatever stands
        /// behind it. Physics raycast, not `GroundPick` -- a tombstone has a
        /// real collider and this only needs to hit it, close to the ship
        /// where colliders exist.
        public static bool TryOpenStoryAt(Vector2 screen)
        {
            if (instance == null || Camera.main == null) return false;
            var ray = Camera.main.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 300f)) return false;
            var gs = hit.collider.GetComponentInParent<Gravestone>();
            if (gs == null || string.IsNullOrEmpty(gs.graveName)) return false;
            foreach (var g in Lives.Graveyard)
            {
                if (g != null && g.name == gs.graveName)
                {
                    ShowStory(g);
                    return true;
                }
            }
            return false;
        }

        public static void ShowStory(GraveRecord g)
        {
            if (instance == null || g == null) return;
            instance.storyShown = g;
            instance.showingAllGraves = false;
        }

        /// The dev LIFE panel's "All graves" button, and the story card's
        /// own "All graves" button, both come through here.
        public static void ShowAllGraves()
        {
            if (instance == null) return;
            instance.showingAllGraves = true;
            instance.storyShown = null;
        }

        static string CauseLabel(string cause) => cause switch
        {
            LifeEvents.KilledInRaid => "killed in a raid",
            LifeEvents.LostAtSea => "lost at sea",
            LifeEvents.HuntingAccident => "a hunting accident",
            LifeEvents.Shipwreck => "shipwreck",
            _ => "downed, nobody came in time",
        };

        // --- IMGUI (the story card and the graves list only; the placement is the ThumbBar's) ---

        void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 160f : 2f, 1f, 3f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            if (showingAllGraves) DrawAllGraves(w, h);
            else if (storyShown != null) DrawStoryCard(w, h);

            GUI.matrix = old;
        }

        const float RowH = 44f; // Apple's own minimum touch target.

        void DrawStoryCard(float w, float h)
        {
            float cw = Mathf.Min(w - 32, 380f);
            float ch = Mathf.Min(h - 32, 360f);
            var r = new Rect((w - cw) * 0.5f, (h - ch) * 0.5f, cw, ch);
            GUI.Box(r, "");
            GUILayout.BeginArea(r);
            var big = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            GUILayout.Label(storyShown.name, big);
            string days = storyShown.bornDay >= 0
                ? "Day " + storyShown.bornDay + " – Day " + storyShown.diedDay
                : "Day " + storyShown.diedDay;
            GUILayout.Label(days);
            GUILayout.Label(CauseLabel(storyShown.cause));
            GUILayout.Space(8);
            if (storyShown.story != null)
                foreach (var line in storyShown.story)
                    if (!string.IsNullOrEmpty(line)) GUILayout.Label(line);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All graves", GUILayout.Height(RowH))) ShowAllGraves();
            if (GUILayout.Button("Close", GUILayout.Height(RowH))) storyShown = null;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawAllGraves(float w, float h)
        {
            float cw = Mathf.Min(w - 32, 400f);
            float ch = Mathf.Min(h - 32, 480f);
            var r = new Rect((w - cw) * 0.5f, (h - ch) * 0.5f, cw, ch);
            GUI.Box(r, "");
            GUILayout.BeginArea(r);
            GUILayout.Label("All graves");
            allGravesScroll = GUILayout.BeginScrollView(allGravesScroll);
            foreach (var g in Lives.Graveyard)
            {
                if (g == null) continue;
                string born = g.bornDay >= 0 ? g.bornDay.ToString() : "?";
                string row = g.name + "  (Day " + born + "–" + g.diedDay + ")  " + CauseLabel(g.cause)
                    + (string.IsNullOrEmpty(g.camp) ? "" : "  · " + g.camp);
                if (GUILayout.Button(row, GUILayout.Height(RowH))) ShowStory(g);
            }
            if (Lives.Graveyard.Count == 0) GUILayout.Label("Nobody has died yet.");
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close", GUILayout.Height(RowH))) showingAllGraves = false;
            GUILayout.EndArea();
        }
    }
}
