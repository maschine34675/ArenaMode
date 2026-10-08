namespace ArenaMode.Session;

/// <summary>
/// The caption rule for the native matchmaker screens' session-mode label. EFT
/// writes <c>ESessionMode.Pve</c> ("PVE ZONE") above the side, location, accept
/// and deploying screens; inside the Arena flow the user wants "ARENA ZONE".
///
/// Pure on purpose (no Unity, no EFT types) so tools/NativeMenuChecks can
/// compile and mutate it: <see cref="ArenaSessionModeCaptionPatch"/> owns where
/// and when the rule is applied and fails open to the native text.
/// </summary>
internal static class ArenaSessionModeCaption
{
    /// <summary>Replaces the native "PVE ZONE" while an Arena setup flow is on screen.</summary>
    internal const string ArenaText = "ARENA ZONE";

    /// <summary>
    /// The Arena text while the Arena flow is selected, otherwise the native
    /// text unchanged (including null or empty: never invented, never trimmed).
    /// </summary>
    internal static string Resolve(bool arenaSelected, string nativeText)
        => arenaSelected ? ArenaText : nativeText;
}
