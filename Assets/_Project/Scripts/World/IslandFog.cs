namespace SeaSick.World
{
    /// **The fog of war is gone (Kevin, 2026-10-03: "remove the fog of
    /// war").** Every island is fully visible, always. This was the
    /// per-island reveal grid (2026-09-30: unsettled islands under cloud,
    /// landing-party explorers opening it, reveals saved per island); the
    /// grid, its world cloud (`IslandFogView`, the `SeaSick/Island Fog`
    /// shader), the chart's cloud bands, the anchoring shore strip and the
    /// save rows are deleted. What a party "found" is now everything on the
    /// island: `IslandInventory`.
    ///
    /// **What is left is a SHIM, only because `UI/Sheets/SeaHud.cs` was
    /// held by another session on 2026-10-03 and could not be edited.** It
    /// calls `Settled` (a real question -- "is this a camp-less island?" --
    /// which picks the HUD's place line) and `Existing(isle).Revealed01`
    /// (the "Island_3 · 35%" figure, now always 100). When SeaHud is free:
    /// replace `IslandFog.Settled(isle)` with `Outpost.IsSettled(isle)`,
    /// drop the `IslandFog.Existing` / `pct` lines and the " · pct%" suffix
    /// (the place text becomes the island's name), then delete this file.
    public sealed class IslandFog
    {
        static readonly IslandFog clear = new IslandFog();
        IslandFog() { }

        /// A camp, or any standing building. Kept for SeaHud's place line;
        /// the real home is `Outpost.IsSettled`.
        public static bool Settled(Island island) => Outpost.IsSettled(island);

        /// Never null for a real island, and always fully open.
        public static IslandFog Existing(Island island) => island == null ? null : clear;

        /// Always 1: there is no fog.
        public float Revealed01 => 1f;
    }
}
