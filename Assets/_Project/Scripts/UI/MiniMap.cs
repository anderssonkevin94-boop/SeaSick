using SeaSick.Combat;
using SeaSick.Terrain;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using SheetsHud = global::SeaSick.UI.Sheets.Sheets;

namespace SeaSick.UI
{
    /// North-up minimap: land coloured by what it carries, reefs, the storm
    /// front, and your ship as an arrow showing heading. North-up (rather than
    /// ship-up) because it pairs with the bearing tape — the tape tells you
    /// where to point, the map tells you what's out there.
    ///
    /// Land is drawn from the populator's flood-fill mask, not as a circle per
    /// island. A circle of MaxRadius is a lie about a crescent or a long spit:
    /// it swallowed whole bays the ship can actually sail into, and on the big
    /// concave islands it covered more water than land.
    public class MiniMap : MonoBehaviour
    {
        [SerializeField] float range = 1500f;  // half-extent in metres

        ShipMotor motor;
        VoyageManager voyage;
        TerrainWorldPopulator populator;

        Texture2D discTex;
        Texture2D arrowTex;
        AnchorController anchor;
        Texture2D ringTex;
        Texture2D maskTex;
        Color32[] maskPixels;
        int maskStamp = int.MinValue;   // island colour state the texture holds

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            anchor = motor != null ? motor.GetComponent<AnchorController>() : null;
            voyage = FindFirstObjectByType<VoyageManager>();
            populator = FindFirstObjectByType<TerrainWorldPopulator>();
            discTex = BuildDisc(48);
            ringTex = BuildRing(64);
            arrowTex = BuildArrow(32);
        }

        void OnEnable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced += Rebind;
        void OnDisable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced -= Rebind;
        void Rebind(GameObject oldShip, GameObject newShip)
        {
            motor = newShip != null ? newShip.GetComponent<ShipMotor>() : null;
            anchor = newShip != null ? newShip.GetComponent<AnchorController>() : null;
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

        /// An outline, not a disc. Ring() used to draw discTex at half alpha,
        /// which was survivable when islands were flat circles too — over the
        /// land mask a filled patrol circle covers the coast it is about.
        static Texture2D BuildRing(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01((1.6f - Mathf.Abs(d - (r - 1.6f))) / 1.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        void OnDestroy()
        {
            if (maskTex != null) Destroy(maskTex);
        }

        /// What the land layer currently looks like: which islands still have
        /// resources. Cheap to compute every frame, and it changes only when
        /// the crew strip an island, so the texture is rebuilt almost never.
        static int ColourStamp()
        {
            int h = 17;
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                h = h * 31 + isle.GetInstanceID();
                h = h * 31 + (isle.HasResources ? 1 : 0);
            }
            return h;
        }

        /// Paint the flood-fill mask into a texture, one texel per scan cell,
        /// coloured by what that land carries.
        void BuildMask()
        {
            int n = populator.MaskSize;
            if (maskTex == null || maskTex.width != n)
            {
                if (maskTex != null) Destroy(maskTex);
                maskTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                // Point, not bilinear: a scan cell is about a pixel wide at
                // this range, and bilinear would bleed the transparent water
                // texels' colour into every coastline.
                maskTex.filterMode = FilterMode.Point;
                maskTex.wrapMode = TextureWrapMode.Clamp;
                maskTex.hideFlags = HideFlags.HideAndDontSave;
                maskPixels = new Color32[n * n];
            }

            var mask = populator.LandMask;
            var water = new Color32(0, 0, 0, 0);
            var rock = ResourceColour("");
            rock.a = 0.85f;
            for (int i = 0; i < mask.Length; i++)
            {
                int v = mask[i];
                if (v == 0) { maskPixels[i] = water; continue; }
                if (v < 0) { maskPixels[i] = rock; continue; }

                var isle = populator.IslandForMask(v);
                Color c;
                if (isle == null) c = rock;
                else if (isle.IsHome) c = ResourceColour("Home");
                else if (!isle.HasResources) c = ResourceColour("");
                else c = ResourceColour(isle.ResourceName);
                c.a = 0.95f;
                maskPixels[i] = c;
            }
            maskTex.SetPixels32(maskPixels);
            maskTex.Apply(false);
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
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SheetsHud.ChartActive) return;   // the chart instrument replaced this

            if (motor == null) return;
            if (!HudVisibility.Minimap) return;
            // Kevin, 2026-09-29: "remove minimap when docked or landed on an
            // island" -- it is a sailing instrument; at anchor, alongside or
            // in the island view it only covers the camp.
            if (CameraRig.IslandCam.Engaged) return;
            if (anchor != null && (anchor.CurrentState == AnchorController.State.Anchored
                                   || anchor.CurrentState == AnchorController.State.Ashore)) return;
            int u = HudLayout.Unit;
            // Sized against the SAFE area, not the screen: on a notched phone
            // the difference is the map's own right-hand edge.
            float size = Mathf.Min(HudLayout.Safe.width * 0.34f, u * 12f);
            float windRow = u * 1.6f;

            // **Reserve above the repaint guard, draw below it.**
            //
            // The guard is still here and still earns its keep -- IMGUI runs
            // OnGUI once per EVENT, and formatting strings on the passes that
            // get thrown away was once the largest allocator in the game. But
            // claiming a rect costs two float compares and no allocation, and
            // reserving only on Repaint is what broke the top-right column:
            // `HudOverlapProbe` caught the ship panel drawn INSIDE this map at
            // (916..1066, 14..86) against (826..1066, 14..254). With
            // reservations only on repaint, the gap between them can exceed
            // the window in which a slot counts as still there -- so the panel
            // above went stale, stopped reserving its space, and the one below
            // stacked at zero. That made the layout depend on FRAME RATE,
            // which is why it showed up in a throttled editor.
            var box = HudLayout.Place(HudLayout.Slot.Map, size, size);
            var windRect = HudLayout.Place(HudLayout.Slot.Wind, size, windRow);

            if (Event.current.type != EventType.Repaint) return;

            UITheme.Rect(box, UITheme.PanelSolid);

            Vector3 shipPos = motor.transform.position;
            float scale = (size * 0.5f) / range;

            // Everything is clipped to the map box.
            GUI.BeginGroup(box);
            var local = new Rect(0f, 0f, size, size);
            Vector2 centre = new Vector2(size * 0.5f, size * 0.5f);

            // Land, as the shape it actually is.
            if (populator != null && populator.Done && populator.LandMask != null)
            {
                int stamp = ColourStamp();
                if (maskTex == null || stamp != maskStamp) { BuildMask(); maskStamp = stamp; }

                // The window of the mask under the map box. North (+z) is up,
                // and v grows the same way, so z maps to v directly.
                float extent = populator.MaskSize * populator.MaskCell;
                Vector2 o = populator.MaskOrigin;
                var uv = new Rect(
                    (shipPos.x - range - o.x) / extent,
                    (shipPos.z - range - o.y) / extent,
                    2f * range / extent,
                    2f * range / extent);
                GUI.DrawTextureWithTexCoords(local, maskTex, uv, true);
            }

            // Home gets a fixed marker rather than a ring at its radius: the
            // point is "this is home", and a radius ring on a concave island
            // is the same lie the discs were.
            foreach (var isle in Island.All)
            {
                if (isle == null || !isle.IsHome) continue;
                Vector2 p = MapPoint(centre, shipPos, isle.transform.position, scale);
                if (p.x < -8f || p.x > size + 8f || p.y < -8f || p.y > size + 8f) continue;
                Ring(p, 6f, ResourceColour("Home"));
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
                        new Color(0.95f, 0.30f, 0.25f, 0.55f));
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

            // Its own slot rather than "just under the box": the map can be
            // switched off, and then the wind row is what the column starts
            // with instead of leaving a hole where the map used to be.
            DrawWind(windRect, u);
        }

        static readonly string[] Points =
            { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
              "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        /// Wind, under the map. The paddle boat is not driven by it, but it
        /// still sets the sea she has to cross and still carries the raiders
        /// who do have sails — so it stays information, it just stops being a
        /// thing floating over the ship. (It replaced WindArrow.)
        void DrawWind(Rect r, int u)
        {
            UITheme.Rect(r, UITheme.PanelSolid);

            Vector2 w = motor.WindDirection;
            if (w.sqrMagnitude < 1e-4f) return;
            float bearing = Mathf.Repeat(Mathf.Atan2(w.x, w.y) * Mathf.Rad2Deg, 360f);
            string point = Points[Mathf.RoundToInt(bearing / 22.5f) % 16];

            // The arrow points the way the wind is going, matching the map's
            // north-up frame rather than the ship's heading.
            float glyph = u * 1.1f;
            var centre = new Vector2(r.x + glyph * 0.9f, r.y + r.height * 0.5f);
            var prev = GUI.color;
            GUIUtility.RotateAroundPivot(bearing + 180f, centre);
            GUI.color = new Color(0.85f, 0.92f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(centre.x - glyph * 0.5f, centre.y - glyph * 0.5f, glyph, glyph), arrowTex);
            GUI.matrix = Matrix4x4.identity;
            GUI.color = prev;

            var text = new Rect(r.x + glyph * 1.7f, r.y, r.width - glyph * 1.7f - u * 0.3f, r.height);
            GUI.Label(text, "wind " + point, UITheme.Small);
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
            GUI.color = new Color(c.r, c.g, c.b, c.a >= 1f ? 0.75f : c.a);
            GUI.DrawTexture(new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f), ringTex);
            GUI.color = prev;
        }

    }
}
