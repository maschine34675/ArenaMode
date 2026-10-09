namespace ArenaMode.Modes;

/// <summary>Only modes with an exact-raid protected terminal coordinator participate.</summary>
internal static class ArenaMatchResult
{
    internal static bool HasResult(IArenaMode mode) => mode is TeamDeathmatchMode t ? t.HasResult : mode is ZombieMode z ? z.HasResult : mode is RoundsMode d && d.HasResult;
    internal static bool OwnsRaid(IArenaMode mode) => mode is TeamDeathmatchMode t ? t.ResultOwnsCurrentRaid : mode is ZombieMode z ? z.ResultOwnsCurrentRaid : mode is RoundsMode d && d.ResultOwnsCurrentRaid;
    internal static bool MayAutoOpenPanel(IArenaMode mode) => OwnsRaid(mode)
        && (mode is not ZombieMode zombie || zombie.ResultPanelReady);
    internal static string Heading(IArenaMode mode) => mode is TeamDeathmatchMode t ? t.ResultHeading : mode is ZombieMode z ? z.ResultHeading : (mode as RoundsMode)?.ResultHeading;
    internal static string Detail(IArenaMode mode) => mode is TeamDeathmatchMode t ? t.ResultDetail : mode is ZombieMode z ? z.ResultDetail : (mode as RoundsMode)?.ResultDetail;
    internal static string ExitStatus(IArenaMode mode) => mode is TeamDeathmatchMode t ? t.ResultExitStatus : mode is ZombieMode z ? z.ResultExitStatus : (mode as RoundsMode)?.ResultExitStatus;
    internal static void RequestExit(IArenaMode mode)
    { if (mode is TeamDeathmatchMode t) t.RequestResultExit(); else if (mode is RoundsMode d) d.RequestResultExit(); else if (mode is ZombieMode z) z.RequestResultExit(); }
    internal static void ReportProblem(IArenaMode mode, string problem)
    { if (mode is TeamDeathmatchMode t) t.ReportResultExitProblem(problem); else if (mode is RoundsMode d) d.ReportResultExitProblem(problem); else if (mode is ZombieMode z) z.ReportResultExitProblem(problem); }
}
