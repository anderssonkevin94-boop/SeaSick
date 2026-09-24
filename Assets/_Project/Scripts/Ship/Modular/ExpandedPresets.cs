namespace SeaSick.Ship.Modular
{
    /// W1x (expanded-beam, "W1-center-expansion-r1") ship presets, kept OUT
    /// of ShipConfiguration.cs deliberately -- same reason WidePresets.cs
    /// was (that file is edited in parallel elsewhere). Mirrors
    /// ShipConfiguration.Short()/Long()/WithMiddles(n) exactly, just with the
    /// W1x hull ids; guns are explicit equipment (2026-09-25 base), same
    /// slot ids as the W1-r2 presets (only their Y moved). See
    /// docs/EXPANDED-HULL-VALIDATION.md.
    public static class ExpandedPresets
    {
        public const string ExpandedStern = "hull.stern.w1x.v1";
        public const string ExpandedMiddle = "hull.middle.w1x.v1";
        public const string ExpandedBow = "hull.bow.w1x.v1";

        const string GunSlotStar = "DeckSlot_1_-1", GunSlotPort = "DeckSlot_1_1";
        const string SternGunSlotStar = "DeckSlot_2_-1", SternGunSlotPort = "DeckSlot_2_1";

        /// Stern + bow, reinforced M1 wheel, chimney, 4 guns (expanded short assembly).
        public static ShipConfiguration ExpandedShort() => WithMiddles(0);

        /// Stern + one middle + bow, 6 guns (expanded long assembly).
        public static ShipConfiguration ExpandedLong() => WithMiddles(1);

        public static ShipConfiguration WithMiddles(int n)
        {
            var c = new ShipConfiguration
            {
                sternId = ExpandedStern,
                bowId = ExpandedBow,
                rotorId = ShipConfiguration.ReinforcedRotor,
                carrierId = ShipConfiguration.M1Carrier,
            };
            for (int i = 0; i < n; i++) c.middleIds.Add(ExpandedMiddle);
            c.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlotStar, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlotPort, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotStar, moduleId = ShipConfiguration.EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotPort, moduleId = ShipConfiguration.EquipmentCannon });
            if (n > 0)
            {
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlotStar, moduleId = ShipConfiguration.EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlotPort, moduleId = ShipConfiguration.EquipmentCannon });
            }
            return c;
        }
    }
}
