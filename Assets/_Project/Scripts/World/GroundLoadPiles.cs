using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **What a fallen hand left behind (death/rescue phase 2, 2026-09-27).**
    ///
    /// `OutpostLedger.groundLoads` is a list of plain data (a resource, a
    /// count, a world spot); this draws it, exactly the way `CampPiles`
    /// draws the stores -- same shapes, same `DrawPile` call -- so a
    /// dropped carcass or armful of logs reads as the same kind of thing a
    /// stockpile is, not new art. Drawn only while the camp is watched
    /// (`LateUpdate` -> `HasCamp`), the same rule every other camp visual
    /// follows: nothing here has state of its own, it is a picture of the
    /// ledger's list.
    /// </summary>
    public class GroundLoadPiles : MonoBehaviour
    {
        Outpost outpost;
        readonly List<Transform> piles = new List<Transform>();
        readonly List<int> drawnCount = new List<int>();
        readonly List<string> drawnRes = new List<string>();

        public static GroundLoadPiles EnsureOn(Outpost owner)
        {
            if (owner == null) return null;
            var found = owner.GetComponentInChildren<GroundLoadPiles>(true);
            if (found != null) return found;
            var go = new GameObject("GroundLoadPiles");
            go.transform.SetParent(owner.transform, false);
            var comp = go.AddComponent<GroundLoadPiles>();
            comp.outpost = owner;
            return comp;
        }

        void LateUpdate()
        {
            if (outpost == null || outpost.Ledger == null || !outpost.HasCamp) return;
            Refresh();
        }

        void Refresh()
        {
            var loads = outpost.Ledger.groundLoads;
            int n = loads != null ? loads.Count : 0;
            while (piles.Count < n)
            {
                var go = new GameObject("GroundLoad" + piles.Count);
                go.transform.SetParent(transform, false);
                piles.Add(go.transform);
                drawnCount.Add(-1);
                drawnRes.Add(null);
            }
            for (int i = 0; i < piles.Count; i++)
            {
                if (piles[i] == null) continue;
                if (i >= n)
                {
                    if (piles[i].gameObject.activeSelf) piles[i].gameObject.SetActive(false);
                    drawnCount[i] = -1; drawnRes[i] = null;
                    continue;
                }
                var g = loads[i];
                bool show = g != null && g.count > 0;
                if (piles[i].gameObject.activeSelf != show) piles[i].gameObject.SetActive(show);
                if (!show) { drawnCount[i] = -1; drawnRes[i] = null; continue; }
                piles[i].position = new Vector3(g.x, outpost.GroundAt(new Vector3(g.x, 0f, g.z)), g.z);
                if (drawnCount[i] == g.count && drawnRes[i] == g.res) continue;
                drawnCount[i] = g.count; drawnRes[i] = g.res;
                CampPiles.DrawPile(piles[i], g.res, g.count, false);
            }
        }
    }
}
