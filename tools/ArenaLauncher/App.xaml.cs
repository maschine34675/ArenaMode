using System.IO;
using System.Windows;

namespace ArenaLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var diagnostics = false;
        string? source = null;
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--diagnostics") diagnostics = true;
            else if (e.Args[i] == "--source" && i + 1 < e.Args.Length) source = e.Args[++i];
            else
            {
                MessageBox.Show("Usage: maschine-ArenaLauncher.exe [--source <SPT folder>] [--diagnostics]",
                    "Arena Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(2);
                return;
            }
        }
        var settings = new LauncherSettings();
        string? warning = null;
        try { source ??= settings.Load(); }
        catch (Exception error) { warning = "Saved folder could not be read: " + error.Message; }
        if (source == null && File.Exists(Path.Combine(Environment.CurrentDirectory, "EscapeFromTarkov.exe")))
            source = Environment.CurrentDirectory;
        var window = new MainWindow(source ?? "", diagnostics, rememberSource: settings.Save);
        MainWindow = window;
        window.Show();
        if (warning != null) window.AddNotice(warning);
    }
}
