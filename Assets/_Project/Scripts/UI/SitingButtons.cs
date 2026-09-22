using UnityEngine;

namespace SeaSick.UI
{
    /// **The three thumbs under the blueprint.**
    ///
    /// Kevin, 2026-09-22: *"when placing the blueprint there should be a x,
    /// checkmark and rotation symbol under the blueprint to make it easier to
    /// place and orientate. it should only be placed when the checkmark is
    /// pressed."*
    ///
    /// So the tap on the ground stopped being a commit and became a move, and
    /// the commit moved here, on to a button big enough for a thumb that is
    /// already resting near the drawing. Pure drawing: it owns no state, it
    /// decides nothing about whether a spot is good, and it never touches
    /// `CampSiting` — it reports which of the three was pressed and lets the
    /// mode act. One frame, one answer.
    ///
    /// The cluster is projected from the ghost's world position every frame
    /// rather than parked in a corner of the screen, because the thing being
    /// answered is *"here?"* and the answer belongs next to the *here*.
    public static class SitingButtons
    {
        public enum Press { None, Cancel, Rotate, Confirm }

        /// A thumb, near enough. 2.6 units is the same order as the sheet's
        /// own buttons (2.2) with the extra a round target wants.
        public static float Diameter => HudLayout.Unit * 2.6f;
        public static float Gap => HudLayout.Unit * 0.6f;

        static GUIStyle glyph;
        static int builtFor = -1;

        static void Build()
        {
            if (builtFor == HudLayout.Unit && glyph != null) return;
            builtFor = HudLayout.Unit;
            // Built once per size, never per frame: IMGUI keys its cached text
            // meshes on the style INSTANCE (see UITheme.ButtonPressed).
            glyph = new GUIStyle(UITheme.Button)
            {
                fontSize = HudLayout.Unit + 8,
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
            };
        }

        /// Where the cluster sits this frame, or an empty rect when the ghost
        /// is behind the camera and there is nowhere honest to put it.
        public static Rect Cluster(Vector3 world) => Cluster(world, 3);

        /// ...for a cluster of `count` buttons. A wall has no rotation, so
        /// its cluster is two wide (✕ ✓) unless the run has come back round
        /// near its own first post, when the middle button becomes "close
        /// the ring" — see `WallSiting`.
        public static Rect Cluster(Vector3 world, int count)
        {
            var cam = Camera.main;
            if (cam == null) return new Rect();

            count = Mathf.Clamp(count, 1, 3);
            float d = Diameter, g = Gap;
            float w = d * count + g * (count - 1);
            var safe = HudLayout.Safe;

            Vector3 sp = cam.WorldToScreenPoint(world);
            float cx, gy;
            if (sp.z <= 0f)
            {
                // Behind the camera: WorldToScreenPoint mirrors, so its x is a
                // lie. Park the cluster centre-bottom instead of drawing it in
                // the wrong half of the screen.
                cx = safe.center.x;
                gy = safe.yMax - d * 2f;
            }
            else
            {
                cx = sp.x;
                gy = Screen.height - sp.y + d * 0.55f;   // GUI space, under the ghost
            }

            float minX = safe.x + g, maxX = safe.xMax - g;
            float minY = safe.y + g, maxY = HudLayout.BottomClustersTop - g;

            // The legacy camp bar claims its strip with `UIBlocker` only, so
            // `BottomClustersTop` does not see it. It is the two-row shape on
            // a phone (a line plus a 2.2-unit button plus padding), and it is
            // the one thing guaranteed to be on screen while siting, so keep
            // the cluster clear of that much of the bottom edge.
            maxY = Mathf.Min(maxY, safe.yMax - HudLayout.Unit * 6f);

            // Landscape puts the sheet down the right-hand third, where
            // `BottomClustersTop` cannot see it. Push the cluster clear of
            // whichever side it is on.
            if (HudLayout.SheetOpen && HudLayout.Wide)
            {
                var s = HudLayout.SheetRect;
                if (s.center.x > safe.center.x) maxX = Mathf.Min(maxX, s.xMin - g);
                else minX = Mathf.Max(minX, s.xMax + g);
            }

            if (maxY < minY + d) maxY = minY + d;
            float x = Mathf.Clamp(cx - w * 0.5f, minX, Mathf.Max(minX, maxX - w));
            float y = Mathf.Clamp(gy, minY, maxY - d);
            return new Rect(x, y, w, d);
        }

        /// Draw the three, claim their rects, and say which was pressed.
        ///
        /// `canConfirm` false draws ✓ muted and makes it a no-op — the ghost
        /// is out of the ring or on ground that will not take it, and the
        /// reason is already on the sheet.
        public static Press Draw(Vector3 world, bool canConfirm, string reason)
            => Draw(world, canConfirm, reason, true, "↻");

        /// ...with the middle button optional, and its glyph the caller's.
        ///
        /// A wall segment cannot be rotated — it IS its two posts — so the
        /// wall tool draws two buttons, and promotes the middle one to
        /// "close the ring" (⭯) only on the frames where the run has come
        /// back near the post it started from. Same `Press.Rotate` on the
        /// way out: this file reports which of the three was pressed and
        /// decides nothing about what that means.
        public static Press Draw(Vector3 world, bool canConfirm, string reason,
            bool withMiddle, string middleGlyph)
        {
            Build();
            int count = withMiddle ? 3 : 2;
            var row = Cluster(world, count);
            if (row.width <= 0f) return Press.None;

            float d = Diameter, g = Gap;
            var cancel = new Rect(row.x, row.y, d, d);
            var rotate = new Rect(row.x + d + g, row.y, d, d);
            var confirm = new Rect(row.x + (d + g) * (withMiddle ? 2f : 1f), row.y, d, d);

            // Claimed every OnGUI, pressed or not: a tap that lands here must
            // never also reach the ground pick underneath or the helm.
            UIBlocker.Block(cancel);
            if (withMiddle) UIBlocker.Block(rotate);
            UIBlocker.Block(confirm);

            var press = Press.None;
            if (Tap(cancel, "✕", UITheme.Bad, true)) press = Press.Cancel;
            if (withMiddle && Tap(rotate, middleGlyph, UITheme.Text, true)) press = Press.Rotate;
            if (Tap(confirm, "✓", UITheme.Good, canConfirm) && canConfirm) press = Press.Confirm;

            if (!canConfirm && !string.IsNullOrEmpty(reason))
                Say(row.center.x, row.yMax + g * 0.5f, "✕ " + reason);

            return press;
        }

        /// **A line of help where the buttons would be**, for a tool that
        /// has nothing to confirm yet. The wall tool spends its first moment
        /// with no posts planted and therefore no segment to put buttons
        /// under; an empty screen there reads as a tool that did not arm.
        public static void Hint(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Build();
            var safe = HudLayout.Safe;
            float y = Mathf.Min(HudLayout.BottomClustersTop, safe.yMax - HudLayout.Unit * 6f)
                      - HudLayout.Unit * 2f;
            Say(safe.center.x, Mathf.Max(safe.y + Gap, y), text);
        }

        static void Say(float centreX, float y, string text)
        {
            var say = new Rect(centreX - HudLayout.Unit * 9f, y,
                               HudLayout.Unit * 18f, HudLayout.Unit * 1.4f);
            UITheme.Rect(say, UITheme.Panel);
            GUI.Label(say, text, UITheme.Small2Centered);
        }

        static bool Tap(Rect r, string mark, Color tint, bool live)
        {
            var prevContent = GUI.contentColor;
            var prevBg = GUI.backgroundColor;
            GUI.contentColor = live ? tint : new Color(tint.r, tint.g, tint.b, 0.30f);
            GUI.backgroundColor = live ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            bool hit = GUI.Button(r, mark, glyph);
            GUI.contentColor = prevContent;
            GUI.backgroundColor = prevBg;
            return hit && live;
        }
    }
}
