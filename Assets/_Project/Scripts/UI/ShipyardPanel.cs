using UnityEngine;
using SeaSick.Ship;

namespace SeaSick.UI
{
    /// The yard, as a panel you can press.
    ///
    /// A test rig first and a game screen second: the point is to be able to
    /// walk the whole twenty-rung ladder in one sitting and watch the hull,
    /// the mass and the bay count change under you. It draws on UITheme so it
    /// matches the rest of the game, and it is a PANEL rather than a banner --
    /// warnings live on the instruments here.
    ///
    /// Opened and closed by the tab at the edge of the screen.
    public class ShipyardPanel : MonoBehaviour
    {
        [SerializeField] Shipyard yard;
        [SerializeField] bool open = true;

        Vector2 scroll;
        string note = "";
        float noteAt = -99f;

        void Awake()
        {
            if (yard == null) yard = FindFirstObjectByType<Shipyard>();
        }

        // No keyboard toggle. The project runs on the Input System package,
        // so touching UnityEngine.Input throws every frame -- and this game is
        // built for a portrait phone, where the on-screen tab is the control
        // that actually exists.

        void Say(string s) { note = s; noteAt = Time.unscaledTime; }

        void OnGUI()
        {
            if (yard == null) return;
            int u = UITheme.Unit;
            float w = Mathf.Min(Screen.width * 0.46f, u * 26f);
            float pad = u * 0.7f;

            var tab = new Rect(pad, Screen.height * 0.30f, u * 5.4f, u * 2.0f);
            if (GUI.Button(tab, open ? "◀ Yard" : "Yard ▶", UITheme.Button))
                open = !open;
            UIBlocker.Block(tab);
            if (!open) return;

            var n = yard.Node;
            if (n == null)
            {
                GUI.Label(new Rect(pad, tab.yMax + pad, w, u * 2f),
                          "no ladder manifest — see ShipLadder", UITheme.Body);
                return;
            }

            // Height from the space that actually remains, not a guess. The
            // first pass used a fixed 34 units and the cell board -- the part
            // this panel exists for -- fell off the bottom of the screen.
            float top = tab.yMax + pad * 0.6f;
            float h = Mathf.Min(Screen.height - top - pad, u * 40f);
            var panel = new Rect(pad, top, w, h);
            UITheme.Rect(panel, UITheme.PanelSolid);
            UIBlocker.Block(panel);

            GUILayout.BeginArea(new Rect(panel.x + pad, panel.y + pad,
                                         panel.width - pad * 2f,
                                         panel.height - pad * 2f));
            // One scroll view around the whole panel. The content is a
            // variable number of move-reasons and up to twelve bays of board,
            // so it cannot be made to fit -- it has to be scrollable.
            scroll = GUILayout.BeginScrollView(scroll);

            // --- who she is now ---------------------------------------------
            GUILayout.Label($"Rung {n.node} of {ShipLadder.Count - 1}", UITheme.Small);
            GUILayout.Label(n.label, UITheme.Strong);
            GUILayout.Label(
                $"{n.length:F1} × {n.beam:F1} m   draft {n.draft:F2}\n"
                + $"{n.mass_kg / 1000f:F0} t   L/B {n.loa_over_beam:F2}",
                UITheme.Small);

            // The corridor, drawn. 3.0 to 4.3, with her sitting somewhere in
            // it -- this is the whole pacing rule, and seeing the marker walk
            // to the wall is what makes the next move obvious.
            var bar = GUILayoutUtility.GetRect(1f, u * 0.9f);
            UITheme.Rect(bar, UITheme.Track);
            float t01 = Mathf.InverseLerp(ShipLadder.BeamyLimit,
                                          ShipLadder.SlenderLimit, n.loa_over_beam);
            UITheme.Rect(new Rect(bar.x + bar.width * t01 - 1f, bar.y, 3f, bar.height),
                         UITheme.Sea);
            GUILayout.Label($"{ShipLadder.BeamyLimit:F1} beamy  ←  L/B  →  "
                            + $"slender {ShipLadder.SlenderLimit:F1}", UITheme.Small);

            // --- the three moves --------------------------------------------
            GUILayout.Space(u * 0.4f);
            GUILayout.Label("HULL — she is re-lofted, never replaced", UITheme.Small);
            GUILayout.BeginHorizontal();
            Move("Lengthen", "lengthen", u);
            Move("Girdle", "girdle", u);
            Move("Raise", "raise", u);
            GUILayout.EndHorizontal();

            // ONE reason line, for the move the corridor is currently refusing.
            // Three paragraphs of explanation pushed the board -- the thing
            // this panel exists for -- clean off the bottom of the screen.
            foreach (var m in new[] { "lengthen", "girdle", "raise" })
            {
                var why = yard.WhyNot(m);
                if (string.IsNullOrEmpty(why)) continue;
                GUILayout.Label(why, UITheme.Small);
                break;
            }

            // --- how she handles ---------------------------------------------
            // The two numbers the hull decides for her, so the effect of a
            // fitting can be seen against what it is fighting.
            var motor = yard.GetComponent<SeaSick.Ship.ShipMotor>();
            if (motor != null)
                GUILayout.Label($"top {motor.MaxSpeed:F1} m/s   "
                    + $"turn {motor.MaxTurnRate:F1}°/s   "
                    + $"accel {motor.AccelerationNow:F2} m/s²", UITheme.Small);

            // --- everything else she can be given ------------------------------
            //
            // Two kinds of upgrade, and they are different questions.
            //
            // NUMBER is a question of space, so it is a bay: one more gun or
            // one more hand costs a bay, and when the bays run out the answer
            // is a bigger ship. That is the whole pacing rule, and it is why
            // these rows sit under the hull moves rather than beside them.
            //
            // QUALITY is a fitting: bought once, carried across every rung,
            // and gated by something physical about the hull rather than by a
            // level number — so the reason is always a fact about the ship.
            GUILayout.Space(u * 0.5f);
            GUILayout.Label("GUNS", UITheme.Small);
            Quantity(SeaSick.Ship.BayUse.Battery, "number",
                     $"{yard.Guns} a side", u);
            Track(SeaSick.Ship.FitTrack.Guns, u);

            GUILayout.Space(u * 0.4f);
            GUILayout.Label("SAIL", UITheme.Small);
            Track(SeaSick.Ship.FitTrack.SailPlan, u);
            Track(SeaSick.Ship.FitTrack.SailArea, u);

            GUILayout.Space(u * 0.4f);
            GUILayout.Label("CREW", UITheme.Small);
            Quantity(SeaSick.Ship.BayUse.Quarters, "berths",
                     $"{yard.Berths} of 20", u);
            Track(SeaSick.Ship.FitTrack.Crew, u);

            GUILayout.Space(u * 0.4f);
            GUILayout.Label("STEERING", UITheme.Small);
            Track(SeaSick.Ship.FitTrack.Rudder, u);

            // The hold gets a row for the same reason the others do: guns and
            // berths eat bays, and without this the only way to get cargo
            // space back is to know that the board below exists.
            GUILayout.Space(u * 0.4f);
            GUILayout.Label("HOLD", UITheme.Small);
            Quantity(SeaSick.Ship.BayUse.Hold, "bays",
                     $"{yard.Cargo} cargo", u);

            GUILayout.Space(u * 0.5f);

            // --- what she is carrying ----------------------------------------
            GUILayout.Label(
                $"{n.bays} bays × {n.tiers} tiers = {n.cells} cells   "
                + $"({yard.Count(SeaSick.Ship.BayUse.Empty)} empty)", UITheme.Small);
            var crewCol = yard.Undermanned ? UITheme.Bad : UITheme.Text;
            var prev = GUI.color; GUI.color = crewCol;
            GUILayout.Label(
                $"crew {yard.Berths}   guns {yard.Guns}   cargo {yard.Cargo}"
                + (yard.Undermanned ? $"   needs {yard.CrewNeeded}" : ""),
                UITheme.Body);
            GUI.color = prev;
            // What her battery could be, and WHY it stops there. Without this
            // the crew-training track looks like a seasickness upgrade, and
            // the reason a first-rate carries twenty guns and not sixty is
            // invisible — it is drill, and drill is for sale.
            GUILayout.Label(
                $"{n.ports_per_side} ports a side · {yard.CrewPerGunNow:0.#} hands "
                + $"a gun · she can man {yard.MaxGunsManned}", UITheme.Small);

            // --- the board ----------------------------------------------------
            GUILayout.Space(u * 0.3f);
            GUILayout.Label("tap a cell: hold → guns → berths → empty", UITheme.Small);
            for (int ti = n.tier_names.Length - 1; ti >= 0; ti--)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Short(n.tier_names[ti]), UITheme.Small,
                                GUILayout.Width(u * 3.4f));
                for (int bi = 0; bi < n.bay_labels.Length; bi++)
                {
                    var use = yard.Use(n.bay_labels[bi], n.tier_names[ti]);
                    GUI.color = Tint(use);
                    if (GUILayout.Button(Glyph(use), UITheme.Button,
                                         GUILayout.Width(u * 1.7f),
                                         GUILayout.Height(u * 1.7f)))
                    {
                        yard.CycleUse(n.bay_labels[bi], n.tier_names[ti]);
                        Say($"{n.bay_labels[bi]} {n.tier_names[ti]}: "
                            + yard.Use(n.bay_labels[bi], n.tier_names[ti]));
                    }
                    GUI.color = prev;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(u * 0.4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ undo", UITheme.Button, GUILayout.Height(u * 1.8f)))
            { yard.Undo(); Say(yard.Status); }
            if (GUILayout.Button("skiff", UITheme.Button, GUILayout.Height(u * 1.8f)))
            { yard.Apply(0); Say(yard.Status); }
            if (GUILayout.Button("brig", UITheme.Button, GUILayout.Height(u * 1.8f)))
            { yard.Apply(12); Say(yard.Status); }
            if (GUILayout.Button("1st rate", UITheme.Button, GUILayout.Height(u * 1.8f)))
            { yard.Apply(ShipLadder.Count - 1); Say(yard.Status); }
            GUILayout.EndHorizontal();

            if (Time.unscaledTime - noteAt < 4f && !string.IsNullOrEmpty(note))
                GUILayout.Label(note, UITheme.Small);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// One QUANTITY row: how many bays are given to a use, with a button
        /// each way. The yard picks WHICH bay — low and amidships for a gun,
        /// on deck for a berth — so the player is asked "how many", which is
        /// the question they actually have.
        void Quantity(SeaSick.Ship.BayUse use, string label, string reading, int u)
        {
            string why = yard.WhyNotAdd(use);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UITheme.Small, GUILayout.Width(u * 5.0f));
            var prev = GUI.color;
            GUI.color = Tint(use);
            GUILayout.Label(reading, UITheme.Body, GUILayout.Width(u * 5.4f));
            GUI.color = prev;

            GUI.enabled = yard.Count(use) > 0;
            if (GUILayout.Button("−", UITheme.Button,
                                 GUILayout.Width(u * 2.6f),
                                 GUILayout.Height(u * 1.7f)))
            { yard.RemoveCell(use); Say(yard.Status); }

            GUI.enabled = why == null;
            if (GUILayout.Button("+", UITheme.Button,
                                 GUILayout.Width(u * 2.6f),
                                 GUILayout.Height(u * 1.7f)))
            { yard.AddCell(use); Say(yard.Status); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (why != null) GUILayout.Label(why, UITheme.Small);
        }

        /// One fitting track: what she has, and what it would take to better it.
        void Track(SeaSick.Ship.FitTrack t, int u)
        {
            int lvl = yard.Fit.Level(t);
            string why = yard.WhyNotFit(t);
            GUILayout.BeginHorizontal();
            GUILayout.Label(SeaSick.Ship.ShipFit.Label(t), UITheme.Small,
                            GUILayout.Width(u * 5.0f));
            // Pips, so the level reads without counting words.
            var prev = GUI.color;
            for (int i = 0; i < SeaSick.Ship.ShipFit.MaxLevel; i++)
            {
                var r = GUILayoutUtility.GetRect(u * 0.5f, u * 0.9f,
                                                 GUILayout.Width(u * 0.5f));
                UITheme.Rect(new Rect(r.x, r.y + u * 0.25f, u * 0.34f, u * 0.34f),
                             i < lvl ? UITheme.Sea : UITheme.Track);
            }
            GUI.color = prev;
            GUI.enabled = why == null;
            if (GUILayout.Button(ShipFitName(t, lvl), UITheme.Button,
                                 GUILayout.Height(u * 1.7f)))
            { yard.Upgrade(t); Say(yard.Status); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (why != null && lvl < SeaSick.Ship.ShipFit.MaxLevel)
                GUILayout.Label(why, UITheme.Small);
        }

        static string ShipFitName(SeaSick.Ship.FitTrack t, int lvl)
            => lvl >= SeaSick.Ship.ShipFit.MaxLevel
                ? SeaSick.Ship.ShipFit.Name(t, lvl)
                : SeaSick.Ship.ShipFit.Name(t, lvl + 1) + " ▸";

        void Move(string label, string move, int u)
        {
            bool can = yard.CanMove(move);
            GUI.enabled = can;
            if (GUILayout.Button(label, UITheme.Button, GUILayout.Height(u * 2.0f)))
            {
                yard.Move(move);
                Say(yard.Status);
            }
            GUI.enabled = true;
        }

        static string Short(string tier) =>
            tier.Replace("GunDeck", "").Replace("Deck", "dk");

        static string Glyph(BayUse u) => u switch
        {
            BayUse.Hold => "▣",
            BayUse.Battery => "◄",
            BayUse.Quarters => "≈",
            BayUse.Ballast => "■",
            _ => "·",
        };

        static Color Tint(BayUse u) => u switch
        {
            BayUse.Hold => UITheme.Cargo,
            BayUse.Battery => UITheme.Bad,
            BayUse.Quarters => UITheme.Sea,
            // Iron: the one thing aboard that is not there to be used.
            BayUse.Ballast => new Color(0.62f, 0.64f, 0.68f),
            _ => UITheme.TextDim,
        };
    }
}
