using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// Stands every canonical size in the game side by side and photographs
/// them, so "does the giant look big?" has an answer instead of an argument.
///
/// This is the tool the project did not have. Every size was chosen against
/// whatever happened to be on screen at the time -- so trees were built at a
/// third of the ship's length, then at her full length, and neither was
/// checked against a crew member or a building because there was nowhere to
/// stand them together. A number in metres is not the problem; seeing the
/// number beside its neighbours is.
///
/// Writes /tmp/seasick-ruler.png and a text list of what is in the row.
public class ScaleRuler : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ScaleRuler: not in play mode"); return; }
        var old = FindAnyObjectByType<ScaleRuler>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("ScaleRuler").AddComponent<ScaleRuler>();
    }

    struct Item
    {
        public string name; public float height; public float width; public Color colour; public bool capsule;
        public Item(string n, float h, float w, Color c, bool cap = false)
        { name = n; height = h; width = w; colour = c; capsule = cap; }
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var sea = SeaSick.Ocean.SeaStateController.Instance;
        if (sea != null) sea.ForceSeverity(0.1f);
        TimeOfDay.SetTime01(0.34f);
        TimeOfDay.Paused = true;

        // Flat open water beside the ship: the row is built on a platform at
        // sea level rather than on the terrain, because a slope under the
        // line-up is exactly the thing that makes two heights hard to
        // compare, and comparing them is the entire point.
        Vector3 at = motor != null ? motor.transform.position : Vector3.zero;
        Vector3 right = motor != null ? motor.transform.right : Vector3.right;
        Vector3 fwd = motor != null ? motor.transform.forward : Vector3.forward;
        // Standing on a deck ABOVE the waterline, not at it: the first run
        // put the row at sea level and the swell washed over the crew member,
        // which is a poor way to read 1.7 m off a picture.
        Vector3 origin = at + right * 46f;
        origin.y = 3f;

        var root = new GameObject("RulerRow");
        root.transform.position = origin;

        if (motor != null)
        {
            var rb = motor.GetComponent<Rigidbody>();
            Vector3 park = origin + new Vector3(-34f, 0f, 6f);
            motor.transform.position = new Vector3(park.x, motor.transform.position.y, park.z);
            motor.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
        deck.transform.SetParent(root.transform, false);
        deck.transform.localScale = new Vector3(120f, 0.4f, 26f);
        deck.transform.localPosition = new Vector3(34f, -0.2f, 0f);
        deck.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.62f, 0.60f, 0.55f));

        var items = new[]
        {
            new Item("crew 1.7 m",        WorldScale.Person,    0.55f, new Color(0.86f, 0.78f, 0.62f), true),
            new Item("giant 2.9 m",       WorldScale.Giant,     0.95f, new Color(0.72f, 0.45f, 0.40f), true),
            new Item("palisade 2.5 m",    WorldScale.Palisade,  4.0f,  new Color(0.48f, 0.36f, 0.24f)),
            new Item("hut 3 m",           WorldScale.Hut,       4.5f,  new Color(0.55f, 0.42f, 0.30f)),
            new Item("longhouse 7 m",     WorldScale.Longhouse, 9.0f,  new Color(0.50f, 0.38f, 0.28f)),
            new Item("tower 11 m",        WorldScale.WatchTower, 3.6f, new Color(0.44f, 0.34f, 0.26f)),
            new Item("tree 9 m",          WorldScale.TreeMin,   2.4f,  new Color(0.16f, 0.36f, 0.18f)),
            new Item("tree 14 m",         WorldScale.TreeMax,   3.4f,  new Color(0.16f, 0.36f, 0.18f)),
        };

        var sb = new StringBuilder();
        sb.AppendLine("the row, left to right, all standing on one flat deck at sea level:");
        float x = 6f;
        foreach (var it in items)
        {
            var go = GameObject.CreatePrimitive(it.capsule ? PrimitiveType.Capsule : PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = it.name;
            go.transform.SetParent(root.transform, false);
            // A Unity capsule is 2 units tall at scale 1, a cube 1.
            float yScale = it.capsule ? it.height * 0.5f : it.height;
            go.transform.localScale = new Vector3(it.width, yScale, it.width);
            go.transform.localPosition = new Vector3(x, it.height * 0.5f, 0f);
            go.GetComponent<Renderer>().sharedMaterial = Mat(it.colour);
            sb.AppendLine($"  {it.name,-18} {it.height,5:F1} m = {WorldScale.InCrew(it.height),4:F1} crew");
            x += it.width + 4.5f;
        }
        sb.AppendLine($"  {"ship",-18} {WorldScale.ShipLength,5:F1} m = "
            + $"{WorldScale.InCrew(WorldScale.ShipLength),4:F1} crew long"
            + (motor != null ? $", top speed {motor.MaxSpeed:F1} m/s = {motor.MaxSpeed * 1.94384f:F1} knots" : ""));

        // Aim across the row with the ship in frame behind it.
        var main = Camera.main;
        var cam = new GameObject("RulerCam").AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = 3000f;
        // Broadside to the row, far enough back to hold all of it and close
        // enough that 1.7 m is legible.
        Vector3 rowCentre = origin + new Vector3(x * 0.5f, 0f, 0f);
        cam.transform.position = rowCentre + new Vector3(0f, 7f, -52f);
        cam.transform.LookAt(rowCentre + new Vector3(0f, 4.5f, 0f));

        yield return new WaitForSeconds(2.5f);
        Shoot(cam, "/tmp/seasick-ruler.png");
        yield return null;

        Destroy(cam.gameObject);
        Destroy(root);
        TimeOfDay.Paused = false;
        if (sea != null) sea.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-ruler.txt", sb.ToString());
        Debug.Log("ScaleRuler\n" + sb);
        Destroy(gameObject);
    }

    static Material Mat(Color c)
    {
        var sh = Shader.Find("SeaSick/Terrain Vertex Color");
        var m = new Material(sh);
        if (m.HasProperty("_Tint")) m.SetColor("_Tint", c);
        if (m.HasProperty("_StriationStrength")) m.SetFloat("_StriationStrength", 0f);
        if (m.HasProperty("_DetailStrength")) m.SetFloat("_DetailStrength", 0.08f);
        return m;
    }

    static void Shoot(Camera cam, string path)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0); tex.Apply();
        cam.targetTexture = prev; RenderTexture.active = prevActive;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex); rt.Release(); Destroy(rt);
    }
}
