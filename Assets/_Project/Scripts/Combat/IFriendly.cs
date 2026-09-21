namespace SeaSick.Combat
{
    /// Marker for an `IHittable` on the player's side. The player's own guns,
    /// the combat lock and the aim assist all skip it — a watchtower is a
    /// target for raiders, not for the hand on the tiller. Raiders make no
    /// such exception; a shell that lands short of a ship can still take a
    /// tower down, and that is intended.
    public interface IFriendly { }
}
