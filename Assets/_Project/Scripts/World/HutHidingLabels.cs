using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeaSick.Combat;

namespace SeaSick.World
{
    /// <summary>
    /// **"3 hiding" over a hut** (death/rescue phase 10, 2026-09-28). Not
    /// UI (Astra owns `Scripts/UI/**`) -- a plain `TextMesh` billboard,
    /// world-space, the same shape as `Combat.RaiderMarkerField`'s red
    /// triangle: one label per hut that currently has anybody actually
    /// hidden inside (`OutpostHand.hiddenInHut`, not merely `hidingHut` --
    /// a hand still walking to the door does not count yet).
    ///
    /// One shared pool of `TextMesh` transforms, re-scanned every frame off
    /// every watched camp's roster (a handful of hands at most): cheap
    /// enough not to need an event hook, and it never drifts out of step
    /// with a hand's own `hiddenInHut` the way a cached count could.
    /// </summary>
    public class HutHidingLabels : MonoBehaviour
    {
        const float HoverHeight = 3.2f;
        const float FontSize = 0.35f;

        static HutHidingLabels instance;
        Camera cam;

        readonly Dictionary<Building, TextMesh> labels = new Dictionary<Building, TextMesh>();
        readonly Dictionary<Building, int> counts = new Dictionary<Building, int>();
        readonly List<Building> stale = new List<Building>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReset()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            instance = null;
            Stand();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Stand()
        {
            if (instance != null) return;
            var go = new GameObject("HutHidingLabels");
            instance = go.AddComponent<HutHidingLabels>();
        }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;

            counts.Clear();
            foreach (var o in Outpost.All)
            {
                if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                foreach (var h in o.Ledger.hands)
                {
                    if (h == null || !h.hiddenInHut) continue;
                    var hut = RaidAlarm.NearestHut(o, o.Ledger.HandAt(h));
                    if (hut == null) continue;
                    counts.TryGetValue(hut, out int n);
                    counts[hut] = n + 1;
                }
            }

            stale.Clear();
            foreach (var kv in labels)
                if (kv.Key == null || !counts.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (var b in stale)
            {
                if (labels[b] != null) Destroy(labels[b].gameObject);
                labels.Remove(b);
            }

            foreach (var kv in counts)
            {
                var hut = kv.Key;
                if (!labels.TryGetValue(hut, out var tm) || tm == null)
                {
                    tm = MakeLabel();
                    labels[hut] = tm;
                }
                tm.text = kv.Value + (kv.Value == 1 ? " hiding" : " hiding");

                Vector3 pos = hut.transform.position + Vector3.up * HoverHeight;
                tm.transform.position = pos;
                // Screen-aligned billboard. A TextMesh reads along +X and is
                // drawn on its -Z face, so it must share the camera's own
                // rotation (its -Z then points back at the lens). The old
                // LookRotation(cam - pos) aimed +Z at the camera, so the
                // camera saw the mirrored back of the glyphs.
                if (cam != null) tm.transform.rotation = cam.transform.rotation;
            }
        }

        TextMesh MakeLabel()
        {
            var go = new GameObject("HutHidingLabel");
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            tm.characterSize = FontSize;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            return tm;
        }
    }
}
