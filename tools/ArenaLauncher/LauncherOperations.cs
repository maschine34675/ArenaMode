using ArenaStart;

namespace ArenaLauncher;

public interface ILauncherOperations
{
    Task<ManagedPreview> Inspect(string source, bool diagnostics);
    Task Start(string source, bool diagnostics, Action<string> report);
}

public sealed class LauncherOperations : ILauncherOperations
{
    public Task<ManagedPreview> Inspect(string source, bool diagnostics) =>
        Task.Run(() => ManagedEnvironment.Inspect(source, diagnostics));

    public Task Start(string source, bool diagnostics, Action<string> report) => Task.Run(async () =>
    {
        var messages = new RuntimeReporter(report);
        var root = ManagedEnvironment.Ensure(source, diagnostics, messages.Write);
        await RuntimeHost.Run(root, smoke: false, messages.Write, waitForClients: true);
    });
}
