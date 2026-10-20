namespace ArenaStart;

// UI/console output must never become a control channel for server/lease lifetime.
public sealed class RuntimeReporter(Action<string> report)
{
    public void Write(string message)
    {
        try { report(message); }
        catch (Exception) { /* A broken observer cannot abort native cleanup. */ }
    }
}
