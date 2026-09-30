using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Run an action inside the game's own frame (2026-09-30).** An
    /// `unity cmd eval` runs from the editor loop, where `Screen.width` reads
    /// the editor window, not the Game view -- so a sheet opened straight from
    /// eval decides its shape (`HudLayout.Wide`, `SheetHost.HugsContent`)
    /// against the wrong screen. `NextFrame.Run(() => ...)` queues it for the
    /// next player-loop `Update`, where the screen is the Game view's.
    public class NextFrame : MonoBehaviour
    {
        static NextFrame instance;
        readonly Queue<Action> queue = new Queue<Action>();

        public static void Run(Action a)
        {
            if (a == null) return;
            if (instance == null)
            {
                var go = new GameObject("NextFrame (dev)");
                go.hideFlags = HideFlags.DontSave;
                instance = go.AddComponent<NextFrame>();
            }
            instance.queue.Enqueue(a);
        }

        void Update()
        {
            while (queue.Count > 0)
            {
                try { queue.Dequeue()(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
