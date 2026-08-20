using SeaSick.Combat;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// North-up minimap: islands coloured by what they carry, reefs, the storm
    /// front, and your ship as an arrow showing heading. North-up (rather than
    /// ship-up) because it pairs with the bearing tape — the tape tells you
    /// where to point, the map tells you what's out there.
    public class MiniMap : MonoBehaviour
    {
        [SerializeField] float range = 1500f;  // half-extent in metres

        /// How much vertical space the map claims in the top-right, so the
        /// ship status panel can sit underneath it instead of on top.
        public static float ReservedHeight { get; private set; }

        ShipMotor motor;
        VoyageManager voyage;

        Texture2D discTex;
        Texture2D arrowTex;

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            voyage = FindFirstObjectByType<VoyageManager>();
            discTex = BuildDisc(48);
            arrowTex = BuildArrow(32);
        }

        static Texture2D BuildDisc(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    // Soft edge so small islands don't alias into squares.
                    float a = Mathf.Clamp01((r - d) / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        static Texture2D BuildArrow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Triangle pointing up: narrows toward the top.
                    float t = y / (float)(size - 1);
                    float halfWidth = Mathf.Lerp(0.06f, 0.42f, 1f - t) * size;
                    float dx = Mathf.Abs(x - size * 0.5f);
                    bool inside = dx <= halfWidth && t > 0.12f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 1f : 0f));
                }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        static Color ResourceColour(string res) => res switch
        {
            "Timber" => new Color(0.45f, 0.78f, 0.38f),
            "Stone" => new Color(0.72f, 0.75f, 0.80f),
            "Ore" => new Color(1f, 0.82f, 0.32f),
            "Spice" => new Color(0.93f, 0.45f, 0.72f),
            "Home" => new Color(1f, 0.58f, 0.25f),
            _ => new Color(0.86f, 0.79f, 0.60f),   // bare shelter rock
        };

        void OnGUI()
        {
            if (motor == null) return;
            int u = UITheme.Unit;
            float pad = u * 0.7f;
            float size = Mathf.Min(Screen.width * 0.34f, u * 12f);
            var box = new Rect(Screen.width - size - pad, pad, size, size);
            ReservedHeight = size + pad;

            UITheme.Rect(box, UITheme.PanelSolid);

            Vector3 shipPos = motor.transform.position;
            float scale = (size * 0.5f) / range;

            // Everything is clipped to the map box.
            GUI.BeginGroup(box);
            var local = new Rect(0f, 0f, size, size);
            Vector2 centre = new Vector2(size * 0.5f, size * 0.5f);

            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                Vector2 p = MapPoint(centre, shipPos, isle.transform.position, scale);
                float r = Mathf.Max(2.5f, isle.MaxRadius * scale);
                if (p.x < -r || p.x > size + r || p.y < -r || p.y > size + r) continue;

                var c = ResourceColour(isle.IsHome ? "Home" : isle.ResourceName);
                if (!isle.IsHome && !isle.HasResources) c = ResourceColour("");
                Disc(p, r, c);
                if (isle.IsHome) Ring(p, r + 3f, ResourceColour("Home"));
            }

            foreach (var reef in Reef.All)
            {
                if (reef == null) continue;
                Vector2 p = MapPoint(centre, shipPos, reef.transform.position, scale);
                if (p.x < 0f || p.x > size || p.y < 0f || p.y > size) continue;
                Disc(p, 1.8f, new Color(0.95f, 0.35f, 0.30f, 0.85f));
            }

            // Monsters get a blip so a test target is findable rather than hunted.
            foreach (var monster in SeaMonster.All)
            {
                if (monster == null || !monster.Alive) continue;
                Vector2 p = MapPoint(centre, shipPos, monster.transform.position, scale);
                if (p.x < 0f || p.x > size || p.y < 0f || p.y > size) continue;
                Disc(p, 3.2f, new Color(0.80f, 0.35f, 0.85f, 0.95f));
            }

            // Raiders, and a faint ring showing the water each one is guarding
            // — the patrol is the information, not the ship's current position.
            foreach (var raider in EnemyShip.All)
            {
                if (raider == null || !raider.Alive) continue;
                if (raider.Home != null)
                {
                    Vector2 h = MapPoint(centre, shipPos, raider.Home.transform.position, scale);
                    Ring(h, Mathf.Max(3f, raider.PatrolRadius * scale),
                        new Color(0.95f, 0.30f, 0.25f, 0.30f));
                }

                Vector2 p = MapPoint(centre, shipPos, raider.transform.position, scale);
                if (p.x < 0f || p.x > size || p.y < 0f || p.y > size) continue;
                Disc(p, 3.0f, new Color(1f, 0.40f, 0.30f, 0.98f));
            }

            // Ship arrow, rotated to heading. North is up.
            float arrow = u * 1.1f;
            var arrowRect = new Rect(centre.x - arrow * 0.5f, centre.y - arrow * 0.5f, arrow, arrow);
            var prev = GUI.color;
            GUIUtility.RotateAroundPivot(motor.Heading, centre);
            GUI.color = Color.white;
            GUI.DrawTexture(arrowRect, arrowTex);
            GUI.matrix = Matrix4x4.identity;
            GUI.color = prev;

            GUI.EndGroup();

            // North tick outside the clip group.
            GUI.Label(new Rect(box.x, box.y - u * 0.1f, box.width, u * 1.2f), "N", UITheme.Small2Centered);
        }

        /// World position -> map pixel. Note z maps to -y: screen y grows down.
        static Vector2 MapPoint(Vector2 centre, Vector3 shipPos, Vector3 world, float scale)
        {
            return new Vector2(
                centre.x + (world.x - shipPos.x) * scale,
                centre.y - (world.z - shipPos.z) * scale);
        }

        void Disc(Vector2 p, float radius, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f), discTex);
            GUI.color = prev;
        }

        void Ring(Vector2 p, float radius, Color c)
        {
            var prev = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, 0.5f);
            GUI.DrawTexture(new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f), discTex);
            GUI.color = prev;
        }

    }
}
