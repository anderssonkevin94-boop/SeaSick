namespace SeaSick.Combat
{
    /// **A hit target the lock never picks** (2026-10-03, the kraken's arms).
    /// It is still in `HitTargets`, so a ball that crosses it hits it, but
    /// `CombatLock` (candidate, tap, `LockOn`) and the floating health bars
    /// (`TargetHUD`) pass it over: a raised arm hanging over the ship is
    /// nearer than the head and would otherwise steal the lock.
    public interface ILockExempt { }
}
