namespace SeaSick.World.Life
{
    /// **Phase 3's blocking flag.** True from the moment a pending grave at
    /// the watched camp starts its forced-placement flow
    /// (`GravePlacementFlow`) until the player confirms where the tombstone
    /// stands. Every chokepoint that would let the player do something else
    /// ashore while a body is unburied checks this first and refuses --
    /// see `IslandInput.RegisterTap`/`BeginPress`, `Outpost.Raise` (the
    /// player-chosen overload, never the `force:true` one `Adopt` uses) and
    /// `AnchorController.WeighAnchor`.
    ///
    /// Deliberately NOT saved: a session that reopens mid-flow just asks
    /// `GravePlacementFlow` again, which finds the same still-`placed ==
    /// false` grave in `Lives.Graveyard` and starts over. Camera pan/zoom
    /// is not gated -- Kevin's brief says looking round stays allowed.
    public static class GraveGate
    {
        public static bool Blocking { get; internal set; }
    }
}
