using UnityEngine;
using SeaSick.World;
using SeaSick.World.Life;

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

            GUILayout.EndArea();
            GUI.matrix = old;
        }
    }
}
