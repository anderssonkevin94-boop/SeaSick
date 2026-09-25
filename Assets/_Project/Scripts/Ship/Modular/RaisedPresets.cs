namespace SeaSick.Ship.Modular
{
    /// W1xR (raised-deck) ship presets, kept OUT of ShipConfiguration.cs and
    /// ExpandedPresets.cs deliberately -- same reason those two are already
    /// separate (edited in parallel elsewhere). Mirrors
    /// ExpandedPresets.ExpandedShort()/ExpandedLong()/WithMiddles(n) exactly,
    /// just with the raised hull ids -- except there is no "Short" raised
    /// preset: the family needs 1-2 middle bays to close
    /// (docs/RAISED-DECK.md sec 3, ShipAssembler's RAISED_DECK_BAYS gate), so
    /// 0 middles is never offered. Guns are explicit equipment, same slot ids
    /// as the W1x presets on the carried-over DeckSlot_1/DeckSlot_2 pairs,
    /// PLUS the new DeckSlot_0 pair on every middle and the bow (the flush
    /// deck's own free gun stations, docs/RAISED-DECK.md sec 5).
    public static class RaisedPresets
    {
        public const string RaisedStern = "hull.stern.w1xr.v1";
        public const string RaisedMiddle = "hull.middle.w1xr.v1";
        public const string RaisedBow = "hull.bow.w1xr.v1";

        const string GunSlot0Star = "DeckSlot_0_-1", GunSlot0Port = "DeckSlot_0_1";
        const string GunSlot1Star = "DeckSlot_1_-1", GunSlot1Port = "DeckSlot_1_1";
        const string SternGunSlotStar = "DeckSlot_2_-1", SternGunSlotPort = "DeckSlot_2_1";

        /// Stern + one raised middle + bow, 5 gun pairs (docs/RAISED-DECK.md
        /// sec 5/10: stern's carried-over pair + middle/bow's carried-over
        /// pairs + the two new DeckSlot_0 pairs on middle and bow).
        public static ShipConfiguration RaisedLong() => WithMiddles(1);

        /// Stern + two raised middles + bow -- the family's other closed bay
        /// count (unverified chimney position for two bays, same -0.84 u
        /// offset per docs/RAISED-DECK.md sec 2, applied by the assembler
        /// regardless of bay count).
        public static ShipConfiguration RaisedTwoBay() => WithMiddles(2);

        public static ShipConfiguration WithMiddles(int n)
        {
            var c = new ShipConfiguration
            {
                sternId = RaisedStern,
                bowId = RaisedBow,
                rotorId = ShipConfiguration.ReinforcedRotor,
                carrierId = ShipConfiguration.M1Carrier,
            };
            for (int i = 0; i < n; i++) c.middleIds.Add(RaisedMiddle);
            c.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlot1Star, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlot1Port, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlot0Star, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlot0Port, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotStar, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotPort, moduleId = ShipConfiguration.EquipmentCannon });
            if (n > 0)
            {
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlot1Star, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlot1Port, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlot0Star, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlot0Port, moduleId = ShipConfiguration.EquipmentCannon });
            }
            if (n > 1)
            {
                c.equipment.Add(new EquipmentChoice { slotId = "middle[1]/" + GunSlot1Star, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[1]/" + GunSlot1Port, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[1]/" + GunSlot0Star, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[1]/" + GunSlot0Port, moduleId = ShipConfiguration.EquipmentCannon });
            }
            return c;
        }
    }
}
