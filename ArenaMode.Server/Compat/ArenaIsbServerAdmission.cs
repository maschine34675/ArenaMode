using ArenaMode.Shared;

namespace ArenaMode.Server.Compat;

internal static class ArenaIsbServerAdmission
{
    internal static string? RejectionReason { get; private set; }

    // Server mods are fixed for this process. Server-only installations must
    // not bypass admission merely because their BepInEx half is missing.
    internal static void Initialize(IEnumerable<string> loadedModIds)
    {
        var ids = loadedModIds.ToHashSet(StringComparer.Ordinal);
        RejectionReason = ArenaIsbAdmission.GetRejectionReason(ids.Contains);
    }
}
