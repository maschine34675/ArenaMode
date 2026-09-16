// Absent mode services doubled for the extracted slice, as in ModeBoundary.cs.
// The corpse budget itself is exercised in CorpseCleanupChecks; here only the
// round-start request is observable, so the double records its reasons.
namespace ArenaMode.Bots;

public static class CorpseCleanupService
{
    public static readonly List<string> Cleared = [];

    public static void ClearAllSoon(string reason) => Cleared.Add(reason);
}
