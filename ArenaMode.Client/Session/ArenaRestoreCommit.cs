namespace ArenaMode.Session;

/// <summary>
/// Reversible client mutations keep their retry snapshot until write-back
/// succeeds. Dropping it in a finally after a failed restore leaves the next
/// ordinary raid with Arena buffs or vault limits and nothing to put back.
/// </summary>
internal static class ArenaRestoreCommit
{
    public static bool ShouldDropRetryState(bool writeBackSucceeded) => writeBackSucceeded;
}
