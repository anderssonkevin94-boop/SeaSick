using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeaSick.World.Life;
using SeaSick.Combat;
using SeaSick.UI;

namespace SeaSick.World
{
    /// <summary>
    /// **Villagers with a day (2026-09-28), speech lines.** A short line of
    /// world-space text over one villager's head, watched camps only --
    /// same shape as `HutHidingLabels`/`Combat.RaiderMarkerField`: a plain
    /// `TextMesh` billboard, no UI (Astra owns `Scripts/UI/**`).
    ///
    /// At most one line up per watched camp at a time, on a global cooldown
    /// (`Life.CampLifeTuning.LineEverySeconds`, 30 s, +/- jitter) so a camp
    /// reads as occasionally chatty, not a wall of subtitles. A line is
    /// only started for a villager on screen and while no sheet is open,
    /// and is hidden while a sheet covers the view. The speaker is
    /// picked from whoever is not already lying low in a raid, and the LINE
    /// is picked by the speaker's own situation, in priority: pouting,
    /// hungry, just rescued/dragged someone (life log), a friend's death
    /// (a graveyard entry naming this hand as `other`), recently went
    /// overboard, at the fire in the evening (reminiscence), working, idle.
    /// A live raid silences everybody except a defender's one battle line;
    /// a hider never speaks (giving away the hut).
    /// </summary>
    public class VillagerChatter : MonoBehaviour
    {
        static VillagerChatter instance;
        Camera cam;

        float cooldown;
        readonly Dictionary<string, (Outpost camp, TextMesh label, float left)> speaking =
            new Dictionary<string, (Outpost, TextMesh, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReset()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { instance = null; Stand(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Stand()
        {
            if (instance != null) return;
            var go = new GameObject("VillagerChatter");
            instance = go.AddComponent<VillagerChatter>();
        }

        static readonly List<string> keyScratch = new List<string>();

        void Update()
        {
            if (cam == null) cam = Camera.main;
            float dt = Time.unscaledDeltaTime;

            // Fade/expire whatever is up.
            // Walk a copy of the keys: writing the struct back into the
            // dictionary while enumerating it threw every frame (2026-09-28).
            keyScratch.Clear();
            keyScratch.AddRange(speaking.Keys);
            foreach (var key in keyScratch)
            {
                var s = speaking[key];
                s.left -= dt;
                if (s.left <= 0f)
                {
                    if (s.label != null) Destroy(s.label.gameObject);
                    speaking.Remove(key);
                }
                else
                {
                    speaking[key] = s;
                    PositionLabel(s.label, s.camp, key);
                    FadeLabel(s.label, s.left);
                }
            }

            cooldown -= dt;
            if (cooldown > 0f) return;
            // A sheet is open: hold the next line back and look again soon.
            if (SheetCovering()) { cooldown = 2f; return; }
            cooldown = CampLifeTuning.LineEverySeconds
                + Random.Range(-CampLifeTuning.LineJitterSeconds, CampLifeTuning.LineJitterSeconds);

            foreach (var o in Outpost.All)
            {
                if (o == null || !o.Watched || o.Ledger?.hands == null) continue;
                if (speaking.ContainsKey(CampKey(o))) continue;   // one line at a time, per camp
                SayOne(o);
            }
        }

        static string CampKey(Outpost o) => o.gameObject.GetInstanceID().ToString();

        void SayOne(Outpost camp)
        {
            var hands = camp.Ledger.hands;
            // A stable-ish pick: walk from a rotating offset so it is not
            // always the first hand in the list who talks.
            int start = Mathf.Abs((int)(Time.unscaledTime * 0.3f)) % Mathf.Max(1, hands.Count);
            for (int k = 0; k < hands.Count; k++)
            {
                var h = hands[(start + k) % hands.Count];
                if (h == null || string.IsNullOrEmpty(h.name)) continue;
                // Only a villager the player can actually see speaks.
                if (!OnScreen(camp.Ledger.HandAt(h) + Vector3.up * CampLifeTuning.LineHeight)) continue;
                string line = Line.Pick(h, camp);
                if (string.IsNullOrEmpty(line)) continue;
                Speak(camp, h, line);
                return;
            }
        }

        void Speak(Outpost camp, OutpostHand h, string line)
        {
            var tm = MakeLabel(line);
            speaking[CampKey(camp)] = (camp, tm, CampLifeTuning.LineSeconds);
            // Stash the speaker's name on the label so `PositionLabel` can
            // find his current world position every frame without keeping
            // a body reference (a headless hand has none) — see `HandAt`.
            tm.gameObject.name = "Line:" + h.name;
            speakerByCamp[CampKey(camp)] = h.name;
            PositionLabel(tm, camp, CampKey(camp));   // no one-frame flash at the world origin
        }

        readonly Dictionary<string, string> speakerByCamp = new Dictionary<string, string>();

        void PositionLabel(TextMesh tm, Outpost camp, string campKey)
        {
            if (tm == null || camp?.Ledger == null) return;
            if (!speakerByCamp.TryGetValue(campKey, out var name)) return;
            OutpostHand h = null;
            foreach (var hh in camp.Ledger.hands) if (hh != null && hh.name == name) { h = hh; break; }
            if (h == null) return;
            Vector3 pos = camp.Ledger.HandAt(h) + Vector3.up * CampLifeTuning.LineHeight;
            tm.transform.position = pos;
            // Screen-aligned billboard: a TextMesh reads along +X and is
            // drawn on its -Z face, so share the camera's rotation (-Z then
            // points back at the lens). LookRotation(cam - pos) aimed +Z at
            // the camera and showed the mirrored back of the glyphs.
            if (cam != null) tm.transform.rotation = cam.transform.rotation;

            // Never over an open sheet, never for a villager who is off screen.
            bool show = !SheetCovering() && OnScreen(pos);
            foreach (var mr in tm.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.enabled != show) mr.enabled = show;
        }

        static bool SheetCovering() => HudLayout.SheetOpen || SeaSick.UI.Sheets.Sheets.IsOpen;

        bool OnScreen(Vector3 world)
        {
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(world);
            return v.z > 0f && v.x > 0.06f && v.x < 0.94f && v.y > 0.06f && v.y < 0.94f;
        }

        void FadeLabel(TextMesh tm, float left)
        {
            if (tm == null) return;
            float a = Mathf.Clamp01(left / 0.6f);   // last 0.6 s fades out
            var c = tm.color; c.a = a; tm.color = c;
            // The dark backing copy (child) fades with it.
            var back = tm.transform.childCount > 0 ? tm.transform.GetChild(0).GetComponent<TextMesh>() : null;
            if (back != null) { var b = back.color; b.a = a; back.color = b; }
        }

        TextMesh MakeLabel(string text)
        {
            var go = new GameObject("VillagerLine");
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            tm.characterSize = CampLifeTuning.LineCharSize;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.text = Wrap(text, 20);
            Configure(go.GetComponent<MeshRenderer>());

            // Dark backing copy, offset down-right and a hair behind (+Z is
            // away from the viewer), so white text stays readable on sand,
            // grass and sky alike. Child 0 by contract (see FadeLabel).
            var bgo = new GameObject("Backing");
            bgo.transform.SetParent(go.transform, false);
            bgo.transform.localPosition = new Vector3(0.05f, -0.05f, 0.02f);
            var bt = bgo.AddComponent<TextMesh>();
            bt.characterSize = tm.characterSize;
            bt.fontSize = tm.fontSize;
            bt.anchor = tm.anchor;
            bt.alignment = tm.alignment;
            bt.color = new Color(0f, 0f, 0f, 1f);
            bt.text = tm.text;
            Configure(bgo.GetComponent<MeshRenderer>());
            return tm;
        }

        static void Configure(MeshRenderer mr)
        {
            if (mr == null) return;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// Word-wrap to a narrow column so a long line stays inside a
        /// portrait screen instead of running off both edges.
        static string Wrap(string text, int col)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= col) return text;
            var sb = new System.Text.StringBuilder();
            int lineLen = 0;
            foreach (var word in text.Split(' '))
            {
                if (lineLen > 0 && lineLen + 1 + word.Length > col) { sb.Append('\n'); lineLen = 0; }
                else if (lineLen > 0) { sb.Append(' '); lineLen++; }
                sb.Append(word); lineLen += word.Length;
            }
            return sb.ToString();
        }

        /// **Dev**: force a line right now, for `LifeDevPanel`'s "Say a line
        /// now" button.
        public static void DebugSayNow(Outpost camp)
        {
            if (instance == null || camp == null) return;
            instance.cooldown = 0f;
            instance.SayOne(camp);
        }

        // --- line selection ---------------------------------------------

        static class Line
        {
            static readonly string[] Idle =
            {
                "Fine day for it.",
                "Nothing much doing right now.",
                "Wonder what's over the next swell.",
                "My feet could use the rest.",
            };
            static readonly string[] Working =
            {
                "This timber won't cut itself.",
                "Nearly got it.",
                "Heavier than it looks.",
            };
            static readonly string[] Evening =
            {
                "Remember the raid at {camp}?",
                "Good to sit a while.",
                "The stars are out early tonight.",
            };
            static readonly string[] Hungry =
            {
                "My belly's been empty since yesterday.",
                "Could eat a whole goat.",
            };
            static readonly string[] Pouting =
            {
                "Nobody listens to me.",
                "Nobody ever asks what I think.",
            };
            static readonly string[] Overboard =
            {
                "I can still taste the sea.",
                "Never sailing near the rail again.",
            };
            static readonly string[] Defending = { "Hold the gate!" };

            /// The line for this hand right now, or null (nobody nearby
            /// heard anything, or it is not his moment). Never called
            /// during a raid except the two carve-outs below.
            public static string Pick(OutpostHand h, Outpost camp)
            {
                var raid = RaidParty.Active;
                bool raidHere = raid != null && raid.Camp == camp;
                if (raidHere)
                {
                    if (h.defending) return Defending[Hash(h, 0) % Defending.Length];
                    return null;   // hiders: silent; everybody else: silent
                }
                if (h.hidingHut || h.hidingCrouch || h.fetchingSpear || h.returningSpear) return null;

                if (h.pouting) return Pick(Pouting, h, 1);

                if (camp.Ledger != null && h.supperHunger > 0.25f) return Pick(Hungry, h, 2); // short at last supper (2026-10-02), not the daily stomach

                var life = Lives.Record(h.name);
                if (life != null && life.events != null)
                {
                    for (int i = life.events.Count - 1; i >= 0; i--)
                    {
                        var ev = life.events[i];
                        if (ev.kind == LifeEvents.Rescued || ev.kind == LifeEvents.RescuedOther)
                            return "I thought I'd lost you, " + (string.IsNullOrEmpty(ev.other) ? "friend" : ev.other) + ".";
                        if (ev.kind == LifeEvents.DraggedOther)
                            return "If " + (string.IsNullOrEmpty(ev.other) ? "he" : ev.other) + " hadn't come...";
                        if (ev.kind == LifeEvents.Overboard) return Pick(Overboard, h, 3);
                    }
                }

                // A friend's death: any graveyard entry whose life events
                // mention this hand as `other`.
                var grave = FriendLostBy(h.name);
                if (grave != null) return "I miss " + grave + ".";

                var routinePhase = CampLifeTuning.PhaseAtHour(TimeOfDay.Hour);
                if (routinePhase == CampLifeTuning.RoutinePhase.Evening)
                    return Pick(Evening, h, 4).Replace("{camp}", camp.gameObject.name);

                if (h.order == OutpostOrder.Work || (h.order == OutpostOrder.Gather && !string.IsNullOrEmpty(h.target)))
                    return Pick(Working, h, 5);

                return Pick(Idle, h, 6);
            }

            /// A graveyard entry whose OWN life log (kept in `Lives.Record`
            /// even after death -- nothing ever removes it) mentions this
            /// hand as `other`: they rescued him, dragged him, fought
            /// beside him, something that named him. `GraveRecord` itself
            /// keeps no event list (just the 3-sentence `story`), so the
            /// lookup goes through the dead hand's own record.
            static string FriendLostBy(string name)
            {
                foreach (var g in Lives.Graveyard)
                {
                    if (g == null) continue;
                    var rec = Lives.Record(g.name);
                    if (rec?.events == null) continue;
                    foreach (var ev in rec.events)
                        if (ev.other == name) return g.name;
                }
                return null;
            }

            static string Pick(string[] set, OutpostHand h, int salt)
            {
                if (set == null || set.Length == 0) return null;
                return set[Hash(h, salt) % set.Length];
            }

            /// FNV-1a of the name + a coarse real-time minute bucket, so the
            /// same hand does not say the exact same line every time he
            /// speaks, but two calls in the same minute agree.
            static int Hash(OutpostHand h, int salt)
            {
                string s = (h.name ?? "") + ":" + salt + ":" + (Mathf.FloorToInt(Time.unscaledTime / 60f));
                uint hash = 2166136261;
                for (int i = 0; i < s.Length; i++) { hash ^= s[i]; hash *= 16777619; }
                return (int)(hash & 0x7fffffff);
            }
        }
    }
}
