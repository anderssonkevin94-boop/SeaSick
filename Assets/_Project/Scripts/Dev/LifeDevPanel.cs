using UnityEngine;
using SeaSick.World;
using SeaSick.World.Life;
using SeaSick.Crew;
using SeaSick.Ship.Overboard;

namespace SeaSick.Dev
{
    /// **Dev stand-in for playtesting death/rescue phase 1 on the phone.**
    /// Astra owns `Scripts/UI/**`; this is a throwaway IMGUI panel, not real
    /// UI, so it lives in `Scripts/Dev` instead. Shown only in a debug build
    /// (the editor, or a `--dev` phone build) -- `Debug.isDebugBuild` covers
    /// both.
    ///
    /// Collapsed: a small "LIFE" button, top-left (the helm's floating
    /// stick lives at the BOTTOM of the screen -- Kevin's iPhone-first rule
    /// -- so top-left is the one corner nothing else claims). Expanded: the
    /// hands at the camp being watched, each with Down/Kill/Revive, plus
    /// the last death's name and 3-sentence story.
    ///
    /// Modelled on `FeelLab`'s boot/scale shape: a `RuntimeInitializeOnLoadMethod`
    /// adds itself to every scene, `GUI.matrix` is scaled by DPI so a row is
    /// thumb-sized on a Retina phone rather than a strip of raw pixels.
    public class LifeDevPanel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            if (FindAnyObjectByType<LifeDevPanel>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("LifeDevPanel");
            go.AddComponent<LifeDevPanel>();
            DontDestroyOnLoad(go);
        }

        const float RowH = 44f; // Apple's own minimum touch target.
        bool expanded;
        Vector2 scroll;
        GraveRecord lastDeath;

        void OnEnable() => Lives.Died += OnDied;
        void OnDisable() => Lives.Died -= OnDied;
        void OnDied(GraveRecord g) => lastDeath = g;

        Outpost WatchedCamp()
        {
            foreach (var o in Outpost.All)
                if (o != null && o.Watched) return o;
            return null;
        }

        void OnGUI()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;

            float scale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 160f : 2f, 1f, 3f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            if (!expanded)
            {
                if (GUI.Button(new Rect(8, 8, 72, RowH), "LIFE")) expanded = true;
                GUI.matrix = old;
                return;
            }

            // Top half only, same as FeelLab: the bottom stays free for the
            // helm stick even while this is open.
            float panelH = Mathf.Min(h * 0.6f, 520f);
            var panelRect = new Rect(8, 8, Mathf.Min(w - 16, 420f), panelH);
            GUI.Box(panelRect, "");
            GUILayout.BeginArea(panelRect);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Life / Death (dev)");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(RowH), GUILayout.Height(RowH))) expanded = false;
            GUILayout.EndHorizontal();

            var camp = WatchedCamp();
            if (camp == null || camp.Ledger == null || camp.Ledger.hands == null)
            {
                GUILayout.Label("No watched camp.");
            }
            else
            {
                scroll = GUILayout.BeginScrollView(scroll);
                foreach (var hnd in camp.Ledger.hands)
                {
                    if (hnd == null) continue;
                    GUILayout.BeginHorizontal();
                    string state = hnd.downed
                        ? (hnd.reached ? "  [DOWN, reached]" : "  [DOWN " + Mathf.CeilToInt(hnd.downedLeft) + "s]")
                        : hnd.recovering ? "  [recovering " + hnd.recoverLeft.ToString("0.00") + "d]"
                        : !string.IsNullOrEmpty(hnd.rescuing) ? "  [rescuing " + hnd.rescuing + "]"
                        // Phase 4: pouting shows its own countdown, or the
                        // cooldown left before he could pout again.
                        : hnd.pouting ? "  [POUT " + Mathf.CeilToInt(hnd.poutLeft) + "s]"
                        : hnd.poutCooldown > 0f ? "  [pout cd " + Mathf.CeilToInt(hnd.poutCooldown) + "s]"
                        : "";
                    string label = hnd.name + state;
                    GUILayout.Label(label, GUILayout.Width(220));
                    if (!hnd.downed)
                    {
                        if (GUILayout.Button("Down", GUILayout.Height(RowH)))
                            camp.Ledger.Down(hnd, LifeEvents.KilledInRaid);
                        if (GUILayout.Button("Accident", GUILayout.Height(RowH)))
                            camp.Ledger.Down(hnd, LifeEvents.HuntingAccident);
                    }
                    else
                    {
                        if (GUILayout.Button("Revive", GUILayout.Height(RowH)))
                            camp.Ledger.Revive(hnd);
                    }
                    // Phase 4: force a pout right now -- ignores the
                    // cooldown, still refuses if the floor would break
                    // (`OutpostLedger.ForcePout`).
                    if (!hnd.pouting && GUILayout.Button("Pout", GUILayout.Height(RowH)))
                        camp.Ledger.ForcePout(hnd);
                    if (GUILayout.Button("Kill", GUILayout.Height(RowH)))
                        camp.Ledger.Die(hnd, string.IsNullOrEmpty(hnd.downedCause)
                            ? LifeEvents.KilledInRaid : hnd.downedCause);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(8);
            if (lastDeath != null)
            {
                GUILayout.Label("Last death: " + lastDeath.name);
                if (lastDeath.story != null)
                    foreach (var line in lastDeath.story)
                        if (!string.IsNullOrEmpty(line)) GUILayout.Label(line);
            }
            else GUILayout.Label("Nobody has died yet.");

            // Phase 3: the same graveyard list the story card's own button
            // opens (`GravePlacementFlow.ShowAllGraves`), so the dev panel
            // never needs its own copy of that list.
            if (GUILayout.Button("All graves", GUILayout.Height(RowH)))
                GravePlacementFlow.ShowAllGraves();

            GUILayout.Space(8);
            DrawOverboard();

            GUILayout.EndArea();
            GUI.matrix = old;
        }

        /// **Phase 5a.** Grip readout for every hand on the currently
        /// loaded ship, plus force-a-fall and drain-grip buttons for
        /// testing the warning/fall without waiting on real weather.
        void DrawOverboard()
        {
            GUILayout.Label("Man overboard (dev)");
            var roster = FindAnyObjectByType<CrewRoster>();
            if (roster == null) { GUILayout.Label("No ship loaded."); return; }

            foreach (var c in roster.All)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.DisplayName + "  grip " + c.Grip01.ToString("0.00")
                    + (c.IsAtRail ? " [RAIL]" : ""), GUILayout.Width(220));
                if (GUILayout.Button("Drain", GUILayout.Height(RowH)))
                    c.DebugAdjustGrip(-0.5f);
                if (GUILayout.Button("MOB", GUILayout.Height(RowH)))
                    c.ForceOverboardSequence(scripted: false);
                GUILayout.EndHorizontal();
            }

            if (Swimmer.All.Count > 0)
            {
                GUILayout.Label("In the water:");
                foreach (var s in Swimmer.All)
                    if (s != null)
                        GUILayout.Label("  " + s.CrewName + "  " + Mathf.CeilToInt(s.TimeLeft) + "s left"
                            + (s.Scripted ? " (scripted)" : ""));
            }
        }
    }
}
