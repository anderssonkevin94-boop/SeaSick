namespace SeaSick.Ship.Modular
{
    /// W2-r1 (wide/deep) ship presets, kept OUT of ShipConfiguration.cs
    /// deliberately: that file is being edited in parallel on
    /// modular-ships/SeaSick-modular for equipment/guns. Mirrors
    /// ShipConfiguration.Short()/Long() exactly, just with the W2-r1 hull
    /// ids. See docs/WIDE-HULL-VALIDATION.md.
    public static class WidePresets
    {
        public const string WideStern = "hull.stern.w2r1.v1";
        public const string WideMiddle = "hull.middle.w2r1.v1";
        public const string WideBow = "hull.bow.w2r1.v1";

        /// Stern + bow, reinforced M1 wheel, chimney (W2-r1 short assembly).
        public static ShipConfiguration WideShort() => WithMiddles(0);

        /// Stern + one middle + bow (W2-r1 long assembly).
        public static ShipConfiguration WideLong() => WithMiddles(1);

        public static ShipConfiguration WithMiddles(int n)
        {
            var c = new ShipConfiguration
            {
                sternId = WideStern,
                bowId = WideBow,
                rotorId = ShipConfiguration.ReinforcedRotor,
                carrierId = ShipConfiguration.M1Carrier,
            };
            for (int i = 0; i < n; i++) c.middleIds.Add(WideMiddle);
            c.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            return c;
        }
    }
}
