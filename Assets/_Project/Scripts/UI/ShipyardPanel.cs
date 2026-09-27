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

        // --- Everything this panel says, built when the SHIP changes --------
        //
        // IMGUI runs OnGUI once per EVENT — Layout, Repaint, and one more for
        // every mouse move — so the twenty interpolations below ran several
        // times a frame, and so did the twelve `WhyNot*` calls, each of which
        // formats a sentence of its own inside `ShipLadder`/`ShipFit`. See
        // StatusHUD for the measurement. Nothing here moves unless the yard
        // moves it, so it is all built behind one key: the rung, the five bay
        // counts, the five fitting levels, and the three numbers the hull
        // hands the motor.
        //
        // (The GUILayout tree this panel builds is a separate cost, noted
        // below and closed by shutting the panel when she casts off.)
        long textKey = long.MinValue;
        string rungText = "", dimsText = "", handlingText = "";
        string cellsText = "", crewText = "", portsText = "";
        string moveWhy;                                       // the one reason line
        /// What the next rung is and what it costs, with what is in store
        /// beside it — the price tag under the three hull buttons.
        string nextText = "";
        bool nextAfford = true;
        readonly bool[] canMove = new bool[Moves.Length];
        readonly string[] quantityReading = new string[5];    // by BayUse
        readonly string[] quantityWhy = new string[5];
        readonly string[] trackWhy = new string[5];           // by FitTrack
        readonly string[] trackButton = new string[5];
        /// What the row says under the button: the price, or the reason, or
        /// both. See the block that fills it in `RefreshText`.
        readonly string[] trackNote = new string[5];
        string[] tierShort = new string[0];
        int tierShortFor = int.MinValue;

        /// The three hull moves, as a field rather than `new[] {…}` in the
        /// loop — that array was a fresh allocation on every event.
        static readonly string[] Moves = { "lengthen", "girdle", "raise" };

        /// Both ends of the corridor are compile-time constants, so the line
        /// under the bar is the same string for the life of the process.
        static readonly string CorridorText =
            $"{ShipLadder.BeamyLimit:F1} beamy  ←  L/B  →  slender {ShipLadder.SlenderLimit:F1}";

        SeaSick.Ship.ShipMotor motor;

        void Awake()
        {
            if (yard == null) yard = FindFirstObjectByType<Shipyard>();
            if (yard != null) motor = yard.GetComponent<SeaSick.Ship.ShipMotor>();
        }

        // No keyboard toggle. The project runs on the Input System package,
        // so touching UnityEngine.Input throws every frame -- and this game is
        // built for a portrait phone, where the on-screen tab is the control
        // that actually exists.

        void Say(string s) { note = s; noteAt = Time.unscaledTime; }

        SeaSick.Voyage.VoyageManager voyage;
        bool wasHome = true;
        float nextVoyageLookup;

        /// Out of OnGUI: a scene scan per EVENT while the reference was null.
        /// Same shape as `Shipyard.Update` — retry at 1 Hz, because script
        /// order is not guaranteed and the manager is not always up first.
        void Update()
        {
            if (yard != null && motor == null)
                motor = yard.GetComponent<SeaSick.Ship.ShipMotor>();
            if (voyage != null || Time.unscaledTime < nextVoyageLookup) return;
            voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            nextVoyageLookup = Time.unscaledTime + 1f;
        }

        /// Everything on the panel, rebuilt only when the ship underneath it
        /// has actually moved. See the field block above for why.
        void RefreshText()
        {
            var n = yard.Node;
            if (n == null) return;

            long k = n.node;
            for (int i = 0; i < 5; i++) k = k * 31 + yard.Count((SeaSick.Ship.BayUse)i);
            for (int i = 0; i < 5; i++) k = k * 31 + yard.Fit.Level((SeaSick.Ship.FitTrack)i);
            if (motor != null)
            {
                k = k * 31 + Mathf.RoundToInt(motor.MaxSpeed * 10f);
                k = k * 31 + Mathf.RoundToInt(motor.MaxTurnRate * 10f);
                k = k * 31 + Mathf.RoundToInt(motor.AccelerationNow * 100f);
            }
            // --- and the PURSE, or every price on this panel goes stale -------
            //
            // The key above is "what the ship is", and while the ladder was
            // free that was everything the panel said. It is not any more: a
            // button's enabled state and its reason now depend on what is in
            // the stores and on whether she is alongside, and neither of those
            // moves the ship. A voyage landing thirty boards would have left
            // every row greyed out with "needs 12 boards — home has 0" until
            // the player happened to press something that re-lofted her.
            //
            // This project has had exactly this bug once before and it is in
            // the trap log; it costs five dictionary lookups an event to not
            // have it again.
            if (voyage != null)
            {
                foreach (var res in SeaSick.Ship.ShipPrices.Priced)
                    k = k * 31 + voyage.Banked(res);
                k = k * 31 + (voyage.AtHome ? 1 : 0);
            }
            // The yard's outlook, tacked onto the price line below. `TargetLine`
            // caches its own string, so asking every event is cheap; what is
            // not cheap is re-formatting `nextText` every event, which is what
            // folding the outlook's hash into this key avoids.
            string outlook = SeaSick.Ship.TargetLine.Outlook(voyage, yard);
            k = k * 31 + (outlook != null ? outlook.GetHashCode() : 0);
            if (k == textKey) return;
            textKey = k;

            rungText = $"Rung {n.node} of {ShipLadder.Count - 1}";
            dimsText = $"{n.length:F1} × {n.beam:F1} m   draft {n.draft:F2}\n"
                     + $"{n.mass_kg / 1000f:F0} t   L/B {n.loa_over_beam:F2}";
            handlingText = motor != null
                ? $"top {motor.MaxSpeed:F1} m/s   "
                  + $"turn {motor.MaxTurnRate:F1}°/s   "
                  + $"accel {motor.AccelerationNow:F2} m/s²"
                : null;

            // ONE reason line, for the move the corridor — or the purse — is
            // currently refusing. `WhyNot` answers both now, in that order.
            moveWhy = null;
            for (int i = 0; i < Moves.Length; i++)
            {
                string why = yard.WhyNot(Moves[i]);
                canMove[i] = why == null;
                if (moveWhy == null && !string.IsNullOrEmpty(why)) moveWhy = why;
            }

            // --- the price tag ------------------------------------------------
            //
            // The rung she is buying is always `node + 1` whichever of the
            // three buttons is the one that is open, so there is one price and
            // one line, not three. Naming the hull it buys is what makes the
            // cost mean something: "12 boards and 20 timber" is a number,
            // "Long sloop — 12 boards and 20 timber" is a decision.
            var next = ShipLadder.Node(n.node + 1);
            var price = SeaSick.Ship.ShipPrices.ForRung(n.node + 1);
            if (next == null)
            {
                nextText = "she is as big as the yard can build";
                nextAfford = true;
            }
            else
            {
                string store = SeaSick.Ship.ShipPrices.InStore(price, voyage);
                nextText = price.Has
                    ? (store != null
                        ? $"next: {next.label} — {price}   ·   {store}"
                        : $"next: {next.label} — {price}")
                    : $"next: {next.label} — free";
                nextAfford = SeaSick.Ship.ShipPrices.CannotAfford(price, voyage) == null;
            }
            if (!string.IsNullOrEmpty(outlook))
                nextText += "   ·   " + outlook;

            quantityReading[(int)SeaSick.Ship.BayUse.Battery] = $"{yard.Guns} a side";
            quantityReading[(int)SeaSick.Ship.BayUse.Quarters] = $"{yard.Berths} of 20";
            quantityReading[(int)SeaSick.Ship.BayUse.Hold] = $"{yard.Cargo} cargo";
            for (int i = 0; i < 5; i++)
                quantityWhy[i] = yard.WhyNotAdd((SeaSick.Ship.BayUse)i);

            // One line under each fitting row, and it has to carry BOTH kinds
            // of answer without saying either of them twice.
            //
            // `Fit.Blocked` is the physical gate — masts, beam, length — and
            // `WhyNotFit` is that gate first and then the purse. When the hull
            // refuses, the price is still worth showing (it is what the player
            // is saving toward), so the two are joined. When the purse
            // refuses, its own sentence already quotes the price and what is
            // in store, so it stands alone. When neither refuses, the line is
            // the price: a button you can press must still say what it costs.
            for (int i = 0; i < 5; i++)
            {
                var t = (SeaSick.Ship.FitTrack)i;
                int lvl = yard.Fit.Level(t);
                trackWhy[i] = yard.WhyNotFit(t);
                trackButton[i] = ShipFitName(t, lvl);

                var p = SeaSick.Ship.ShipPrices.ForFit(t, lvl + 1);
                string cost = p.Has ? p.ToString() : null;
                string physical = yard.Fit.Blocked(t, n);
                trackNote[i] = lvl >= SeaSick.Ship.ShipFit.MaxLevel
                    ? null
                    : physical != null
                        ? (cost != null ? $"{cost}   ·   {physical}" : physical)
                        : (trackWhy[i] ?? cost);
            }

            cellsText = $"{n.bays} bays × {n.tiers} tiers = {n.cells} cells   "
                      + $"({yard.Count(SeaSick.Ship.BayUse.Empty)} empty)";
            crewText = $"crew {yard.Berths}   guns {yard.Guns}   cargo {yard.Cargo}"
                     + (yard.Undermanned ? $"   needs {yard.CrewNeeded}" : "");
            portsText = $"{n.ports_per_side} ports a side · {yard.CrewPerGunNow:0.#} hands "
                      + $"a gun · she can man {yard.MaxGunsManned}";

            // Two `Replace` calls a tier, per event, for a name that only
            // changes when she is re-lofted.
            if (tierShortFor != n.node || tierShort.Length != n.tier_names.Length)
            {
                tierShortFor = n.node;
                tierShort = new string[n.tier_names.Length];
                for (int i = 0; i < tierShort.Length; i++)
                    tierShort[i] = Short(n.tier_names[i]);
            }
        }

        void OnGUI()
        {
            if (yard == null) return;
            // Same IMGUI-blind-spot suppression as `CannonBattery`/`PerfHUD`/
            // `FeelLab`/`SettingsPanel`: this rail tab had none at all before
            // 2026-09-26's review caught "Yard >" poking into the Pause/Save
            // card from underneath.
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None
                || SeaSick.UI.Sheets.SeaLedger.IsOpen) return;
            int u = HudLayout.Unit;
            float w = Mathf.Min(HudLayout.Safe.width * 0.46f, u * 26f);
            float pad = HudLayout.Pad;

            // The yard shuts when she casts off. This panel is sixty GUILayout
            // controls, and GUILayout allocates its layout tree on every event
            // -- measured at 55 KB a frame with the panel open, which is the
            // single largest source of the garbage behind the 12 ms GC pauses
            // that read as "the sea goes choppy". `open` is scene-serialized
            // (and was saved true), so a default in code cannot close it; the
            // voyage state does. The tab still reopens it at sea on purpose.
            if (Event.current.type == EventType.Layout)
            {
                bool home = voyage == null || voyage.AtHome;
                if (wasHome && !home) open = false;
                wasHome = home;
            }

            var tab = HudLayout.Place(HudLayout.Slot.RailYard,
                                      HudLayout.RailWidth, HudLayout.RailButtonHeight);
            if (GUI.Button(tab, open ? "◀ Yard" : "Yard ▶", UITheme.Button))
                open = !open;
            UIBlocker.Block(tab);
            if (!open) return;

            // Below the early-out on purpose: a shut panel says nothing, so it
            // does not need to work out what it would have said.
            RefreshText();

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
            // It now stops above the helm and the broadside buttons rather
            // than at the bottom of the screen: a panel you opened must never
            // be sitting on the control that gets her out of trouble.
            float top = HudLayout.RailPanelTop;
            float h = Mathf.Min(HudLayout.BottomClustersTop - top - HudLayout.Gap, u * 40f);
            var panel = new Rect(pad, top, w, h);
            HudLayout.ClaimLeftPanel(panel);
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
            GUILayout.Label(rungText, UITheme.Small);
            GUILayout.Label(n.label, UITheme.Strong);
            GUILayout.Label(dimsText, UITheme.Small);

            // The corridor, drawn. 3.0 to 4.3, with her sitting somewhere in
            // it -- this is the whole pacing rule, and seeing the marker walk
            // to the wall is what makes the next move obvious.
            var bar = GUILayoutUtility.GetRect(1f, u * 0.9f);
            UITheme.Rect(bar, UITheme.Track);
            float t01 = Mathf.InverseLerp(ShipLadder.BeamyLimit,
                                          ShipLadder.SlenderLimit, n.loa_over_beam);
            UITheme.Rect(new Rect(bar.x + bar.width * t01 - 1f, bar.y, 3f, bar.height),
                         UITheme.Sea);
            GUILayout.Label(CorridorText, UITheme.Small);

            // --- the three moves --------------------------------------------
            GUILayout.Space(u * 0.4f);
            GUILayout.Label("HULL — she is re-lofted, never replaced", UITheme.Small);
            GUILayout.BeginHorizontal();
            Move("Lengthen", 0, u);
            Move("Girdle", 1, u);
            Move("Raise", 2, u);
            GUILayout.EndHorizontal();

            // What the next rung is and what it costs, under the buttons that
            // buy it. Tinted `Bad` when it cannot be had — the one piece of
            // state on this panel that changes without the ship changing, so
            // it is also the one the text cache had to be taught about.
            var priceWas = GUI.color;
            if (!nextAfford) GUI.color = UITheme.Bad;
            GUILayout.Label(nextText, UITheme.Small);
            GUI.color = priceWas;

            // ONE reason line, for the move the corridor is currently refusing.
            // Three paragraphs of explanation pushed the board -- the thing
            // this panel exists for -- clean off the bottom of the screen.
            if (moveWhy != null) GUILayout.Label(moveWhy, UITheme.Small);

            // --- how she handles ---------------------------------------------
            // The two numbers the hull decides for her, so the effect of a
            // fitting can be seen against what it is fighting.
            if (handlingText != null) GUILayout.Label(handlingText, UITheme.Small);

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
            Quantity(SeaSick.Ship.BayUse.Battery, "number", u);
            Track(SeaSick.Ship.FitTrack.Guns, u);

            GUILayout.Space(u * 0.4f);
            GUILayout.Label("SAIL", UITheme.Small);
            Track(SeaSick.Ship.FitTrack.SailPlan, u);
            Track(SeaSick.Ship.FitTrack.SailArea, u);

            GUILayout.Space(u * 0.4f);
            GUILayout.Label("CREW", UITheme.Small);
            Quantity(SeaSick.Ship.BayUse.Quarters, "berths", u);
            Track(SeaSick.Ship.FitTrack.Crew, u);

            GUILayout.Space(u * 0.4f);
            GUILayout.Label("STEERING", UITheme.Small);
            Track(SeaSick.Ship.FitTrack.Rudder, u);

            // The hold gets a row for the same reason the others do: guns and
            // berths eat bays, and without this the only way to get cargo
            // space back is to know that the board below exists.
            GUILayout.Space(u * 0.4f);
            GUILayout.Label("HOLD", UITheme.Small);
            Quantity(SeaSick.Ship.BayUse.Hold, "bays", u);

            GUILayout.Space(u * 0.5f);

            // --- what she is carrying ----------------------------------------
            GUILayout.Label(cellsText, UITheme.Small);
            var crewCol = yard.Undermanned ? UITheme.Bad : UITheme.Text;
            var prev = GUI.color; GUI.color = crewCol;
            GUILayout.Label(crewText, UITheme.Body);
            GUI.color = prev;
            // What her battery could be, and WHY it stops there. Without this
            // the crew-training track looks like a seasickness upgrade, and
            // the reason a first-rate carries twenty guns and not sixty is
            // invisible — it is drill, and drill is for sale.
            GUILayout.Label(portsText, UITheme.Small);

            // --- the board ----------------------------------------------------
            GUILayout.Space(u * 0.3f);
            GUILayout.Label("tap a cell: hold → guns → berths → empty", UITheme.Small);
            for (int ti = n.tier_names.Length - 1; ti >= 0; ti--)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ti < tierShort.Length ? tierShort[ti] : "",
                                UITheme.Small, GUILayout.Width(u * 3.4f));
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

            // --- the dev row -------------------------------------------------
            //
            // **These four are FREE and must stay free**, and the label says
            // so out loud so nobody reads them as the game. They go through
            // `Apply`/`Undo`, which is the path every probe and every ladder
            // walker in `Scripts/Dev` drives the hull with; pricing them would
            // turn each of those instruments into a test of the stores. See
            // the design rule at the top of `ShipPrices`.
            GUILayout.Space(u * 0.4f);
            GUILayout.Label("dev — free", UITheme.Small);
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
        void Quantity(SeaSick.Ship.BayUse use, string label, int u)
        {
            // Both the reading and the refusal come from the cache: `WhyNotAdd`
            // formats a sentence, and this ran four times per event.
            string reading = quantityReading[(int)use];
            string why = quantityWhy[(int)use];
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
            string why = trackWhy[(int)t];
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
            if (GUILayout.Button(trackButton[(int)t], UITheme.Button,
                                 GUILayout.Height(u * 1.7f)))
            { yard.Upgrade(t); Say(yard.Status); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            string note = trackNote[(int)t];
            if (note != null)
            {
                var noteWas = GUI.color;
                if (why != null) GUI.color = UITheme.Bad;
                GUILayout.Label(note, UITheme.Small);
                GUI.color = noteWas;
            }
        }

        static string ShipFitName(SeaSick.Ship.FitTrack t, int lvl)
            => lvl >= SeaSick.Ship.ShipFit.MaxLevel
                ? SeaSick.Ship.ShipFit.Name(t, lvl)
                : SeaSick.Ship.ShipFit.Name(t, lvl + 1) + " ▸";

        /// `move` is an index into `Moves`: `CanMove` goes through
        /// `ShipLadder.Blocked`, which builds a sentence to throw away, and it
        /// ran three times per event. The answer is cached with the rest.
        void Move(string label, int move, int u)
        {
            GUI.enabled = canMove[move];
            if (GUILayout.Button(label, UITheme.Button, GUILayout.Height(u * 2.0f)))
            {
                yard.Move(Moves[move]);
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
