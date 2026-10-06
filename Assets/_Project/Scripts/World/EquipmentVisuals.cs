using UnityEngine;

namespace SeaSick.World
{
    /// Shield presentation reads equipment ownership; it never creates items.
    /// Props live under the body root to avoid the rig's enlarged bone scale.
    [DefaultExecutionOrder(210)]
    public sealed class EquipmentVisuals : MonoBehaviour
    {
        CampWorker worker;
        Transform wrist;
        GameObject shield;
        static Material wood, boss;

        void LateUpdate()
        {
            if (worker == null) worker = GetComponent<CampWorker>();
            var hand = worker != null ? worker.HandRow : null;
            bool show = hand?.equipment?.offHand == "WoodShield" && hand.equipment.mainHand != Res.Bow
                && !hand.hiddenInHut && !hand.recovering;
            if (!show) { if (shield != null) shield.SetActive(false); return; }
            if (shield == null) Build();
            shield.SetActive(true);
            bool fighting = hand.defending;
            float scale = transform.lossyScale.y;
            Vector3 position = fighting && wrist != null
                ? wrist.position + transform.forward * .12f * scale
                : transform.TransformPoint(new Vector3(0f, 1.05f, -.24f));
            shield.transform.SetPositionAndRotation(position, transform.rotation * Quaternion.Euler(90f, 0f, 0f));
        }

        void Build()
        {
            // The left hand is selected by body-space position, not mirrored rig names.
            float leftmost = float.MaxValue;
            foreach (var bone in GetComponentsInChildren<Transform>(true)) {
                string name = bone.name.ToLowerInvariant();
                if (!name.StartsWith("hand") || name.Contains("finger")) continue;
                float x = transform.InverseTransformPoint(bone.position).x;
                if (x < leftmost) { wrist = bone; leftmost = x; }
            }
            if (wood == null) wood = MakeMaterial(new Color(.39f, .23f, .11f));
            if (boss == null) boss = MakeMaterial(new Color(.25f, .29f, .31f));
            shield = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shield.name = "Equipped shield";
            Destroy(shield.GetComponent<Collider>());
            shield.transform.SetParent(transform, false);
            shield.transform.localScale = new Vector3(.64f, .045f, .64f);
            shield.GetComponent<Renderer>().sharedMaterial = wood;
            var centre = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            centre.name = "Shield boss";
            Destroy(centre.GetComponent<Collider>());
            centre.transform.SetParent(shield.transform, false);
            centre.transform.localPosition = new Vector3(0f, 1f, 0f);
            centre.transform.localScale = new Vector3(.28f, 1.8f, .28f);
            centre.GetComponent<Renderer>().sharedMaterial = boss;
        }

        static Material MakeMaterial(Color color)
        {
            var material = new Material(Shader.Find(WorldArtStyle.Instance != null
                ? "SeaSick/Environment Toon" : "Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .05f);
            return material;
        }
        void OnDisable() { if (shield != null) shield.SetActive(false); }
        void OnDestroy() { if (shield != null) Destroy(shield); }
    }
}
