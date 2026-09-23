using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Visual-only playtest adapter. Existing ladder physics and saves remain authoritative.
    public sealed class AstraSteamerVisual : MonoBehaviour
    {
        public Transform geometry;
        public Transform paddle;
        float lengthScale = 1f, heightScale = 1f;
        Rigidbody body;

        public static FleetVisual Build(Transform parent, int node, FleetVisual existing)
        {
            var prefab = Resources.Load<GameObject>("AstraPlaytest/Steamer");
            var data = ShipLadder.Node(node);
            if (prefab == null || data == null) return null;
            var go = Instantiate(prefab, parent, false);
            go.name = "HullVisual";
            var art = go.GetComponent<AstraSteamerVisual>();
            var fleet = go.GetComponent<FleetVisual>();
            art.lengthScale = data.length / 23.95f;
            art.heightScale = Mathf.Max(.1f, data.RailY) / 1.76f;
            art.geometry.localScale = new Vector3(data.beam / 9.36f, art.heightScale, art.lengthScale);
            art.body = parent.GetComponent<Rigidbody>();
            fleet.stage = existing.stage;
            fleet.length = data.length;
            fleet.freeboard = data.RailY;
            fleet.helm = new Vector3(.7f * data.beam / 9.36f, art.DeckHeight(-6.5f * art.lengthScale), -6.5f * art.lengthScale);
            fleet.sailPivots = new Transform[0];
            var guns = new List<Transform>();
            foreach (int side in new[] { -1, 1 })
            {
                var templates = new List<Transform>();
                foreach (var t in existing.gunTemplates)
                    if (Mathf.Sign(t.localPosition.x) == side) templates.Add(t);
                for (int i = 0; i < templates.Count; i++)
                {
                    var gun = Instantiate(templates[i].gameObject, go.transform, false).transform;
                    float z = Mathf.Lerp(-.27f, .25f, (i + .5f) / templates.Count) * data.length;
                    gun.localPosition = new Vector3(side * data.beam * .38f, art.DeckHeight(z) + .05f, z);
                    gun.gameObject.SetActive(false);
                    guns.Add(gun);
                }
            }
            fleet.gunTemplates = guns.ToArray();
            ReplaceCaptain(parent);
            return fleet;
        }

        public static void ReplaceCaptain(Transform parent)
        {
            var captain = parent.Find("Helmsman");
            if (captain != null && captain.Find("AstraVisual") == null)
            {
                var crewVisual = Resources.Load<GameObject>("AstraPlaytest/DeckhandVisual");
                if (crewVisual != null)
                {
                    foreach (var renderer in captain.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                    var replacement = Instantiate(crewVisual, captain, false);
                    replacement.name = "AstraVisual";
                }
            }
        }

        public float DeckHeight(float z)
        {
            float sourceZ = z / lengthScale;
            float t = Mathf.Clamp01((sourceZ - 7f) / 5.4f);
            float height = 1.76f + .57f * t * t * (3f - 2f * t);
            if (sourceZ < -8.6f) height += 1.28f;
            return height * heightScale;
        }

        void Update()
        {
            if (paddle == null || body == null) return;
            float forwardSpeed = Vector3.Dot(body.linearVelocity, body.transform.forward);
            paddle.Rotate(Vector3.right, forwardSpeed / (1.62f * heightScale) * Mathf.Rad2Deg * Time.deltaTime, Space.Self);
        }
    }
}
