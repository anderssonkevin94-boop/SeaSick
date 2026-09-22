using UnityEngine;
namespace SeaSick.Dev {
// Isolated lighting lab controls; never attached to a gameplay camera.
public sealed class VolumetricLabCamera : MonoBehaviour {
    private bool orbit = true;
    private Renderer atmosphere;
    private float clock;
    private void Start() {
        var volume = GameObject.Find("Shadowed atmosphere — toggle renderer to compare");
        if (volume) atmosphere = volume.GetComponent<Renderer>();
    }
    private void Update() {
        if (!orbit) return;
        clock += Time.deltaTime * .22f;
        transform.position = Vector3.Lerp(new Vector3(-13,5,-20), new Vector3(10,4,-14), .5f-.5f*Mathf.Cos(clock));
        transform.LookAt(new Vector3(0,6,13));
    }
    private void OnGUI() {
        GUILayout.BeginArea(new Rect(20,20,300,145), GUI.skin.box);
        GUILayout.Label("3D SUNLIGHT • isolated prototype");
        if (atmosphere) atmosphere.enabled = GUILayout.Toggle(atmosphere.enabled, "Shadowed light volume");
        orbit = GUILayout.Toggle(orbit, "Move camera through viewing angles");
        GUILayout.Label("96 samples / pixel · performance unprofiled");
        GUILayout.EndArea();
    }
}
}
