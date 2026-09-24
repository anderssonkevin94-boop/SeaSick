using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Test-bench spinner: turns ModularShipView.RotorPivot about its local X
    /// at a fixed visual rate. NOT propulsion -- it knows nothing about speed,
    /// thrust or the wheel's size, and must never be used as if it did.
    [RequireComponent(typeof(ModularShipView))]
    public class RotorSpin : MonoBehaviour
    {
        [SerializeField] float degreesPerSecond = 90f;
        ModularShipView view;

        void Awake() { view = GetComponent<ModularShipView>(); }

        void Update()
        {
            var pivot = view != null ? view.RotorPivot : null;
            if (pivot != null) pivot.Rotate(Vector3.right, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
