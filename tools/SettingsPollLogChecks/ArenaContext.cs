namespace ArenaMode.Server;

// Only the unrelated static DI bridge is substituted. The real production
// adapter, real ASP.NET context/features, Harmony and SPT metadata are used.
internal static class ArenaContext
{
    internal static CheckLogger? Logger { get; set; } = new();
}

internal sealed class CheckLogger
{
    internal readonly List<string> Warnings = [];
    public void Debug(string message) { }
    public void Warning(string message) => Warnings.Add(message);
}
