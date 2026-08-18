using System.Collections.Generic;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// The cargo you can actually see. Every unit in the hold is a real object
    /// stacked at the stern, so a full ship looks full and you can tell at a
    /// glance what you're carrying.
    public class ShipHold : MonoBehaviour
    {
        // The hull floor rises toward the transom, so a stack sitting at deck
        // height amidships was buried in the planking back here — the whole
        // first layer (perRow², i.e. four items) was invisible.
        [SerializeField] Vector3 stackOrigin = new Vector3(0f, 2.95f, -4.4f);
        [SerializeField] int perRow = 2;
        [SerializeField] float spacing = 1.3f;
        [SerializeField] float layerHeight = 0.85f;
        [SerializeField] int maxVisible = 24;

        readonly List<GameObject> stack = new List<GameObject>();
        Transform anchorRoot;
        VoyageManager voyage;

        void Start()
        {
            voyage = FindFirstObjectByType<VoyageManager>();
            var root = new GameObject("HoldStack");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = stackOrigin;
            anchorRoot = root.transform;
        }

        /// Called when a crew member sets a unit down in the hold.
        public void AddVisual(string resource)
        {
            if (anchorRoot == null || stack.Count >= maxVisible) return;
            var item = CargoVisual.Build(resource, anchorRoot);
            item.transform.localPosition =
                CargoVisual.StackSlot(stack.Count, perRow, spacing, layerHeight);
            item.transform.localRotation = Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f);
            stack.Add(item);
        }

        /// Remove the top unit — used when unloading at home, or when mutinous
        /// crew throw cargo over the side.
        public bool RemoveVisual()
        {
            if (stack.Count == 0) return false;
            int last = stack.Count - 1;
            if (stack[last] != null) Destroy(stack[last]);
            stack.RemoveAt(last);
            return true;
        }

        public void ClearVisuals()
        {
            foreach (var item in stack) if (item != null) Destroy(item);
            stack.Clear();
        }

        public int VisibleCount => stack.Count;

        /// Where the next unit will land, in world space — crew walk to this
        /// point to set their load down.
        public Vector3 DropPoint =>
            anchorRoot != null
                ? anchorRoot.TransformPoint(
                    CargoVisual.StackSlot(Mathf.Min(stack.Count, maxVisible - 1),
                        perRow, spacing, layerHeight))
                : transform.TransformPoint(stackOrigin);

        void LateUpdate()
        {
            // Keep the visible stack honest if the hold changed without going
            // through us (mutiny ditching cargo, banking at home).
            if (voyage == null) return;
            int want = Mathf.Min(voyage.TotalHeld, maxVisible);
            while (stack.Count > want) RemoveVisual();
        }
    }
}
