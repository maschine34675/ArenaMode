using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArenaLauncher;
using ArenaStart;

internal static class Program
{
    private static int assertions;

    [STAThread]
    private static int Main()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/maschine-ArenaLauncher;component/Theme.xaml", UriKind.Relative)
        });
        app.Startup += async (_, _) =>
        {
            try
            {
                await Run();
                Console.WriteLine($"ARENA_LAUNCHER_CHECKS_PASS ({assertions} assertions; actual WPF window + controlled lifecycle)");
                app.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static void Check(bool value, string reason)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(reason);
    }

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(
        () => { }, DispatcherPriority.ApplicationIdle).Task;

    private static async Task Run()
    {
        var backend = new ControlledOperations();
        var remembered = new List<string>();
        var window = new MainWindow(operations: backend, rememberSource: remembered.Add);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        await Drain();
        Check(window.IsVisible && window.CanClose && !window.StartButton.IsEnabled, "initial window state");
        await window.StartSession();
        Check(backend.Starts == 0, "start without preview reached backend");
        await window.InspectSource();
        Check(backend.Inspections == 0 && window.StatusHeading.Text == "Select an SPT folder", "blank path accepted");

        window.SourceBox.Text = @"C:\missing";
        var inspection = window.InspectSource();
        Check(window.IsBusy && !window.SourceBox.IsEnabled && !window.InspectButton.IsEnabled, "inspection not guarded before await");
        await window.InspectSource();
        Check(backend.Inspections == 1, "duplicate inspection reached backend");
        window.Close();
        Check(!closed && window.NoticeText.Visibility == Visibility.Visible, "busy inspection window closed");
        backend.Preview.SetException(new InvalidDataException("Fixture: no SPT installation."));
        await inspection;
        Check(!window.IsBusy && window.CanClose && !window.StartButton.IsEnabled &&
            window.StatusDetail.Text.Contains("no SPT installation"), "failed preview left busy/launch enabled");
        Check(window.NoticeText.Visibility == Visibility.Collapsed, "blocked-close hint survived inspection completion");

        backend.Preview = NewSignal<ManagedPreview>();
        window.SourceBox.Text = @"C:\SPT41";
        inspection = window.InspectSource();
        backend.Preview.SetResult(Preview(exists: false));
        await inspection;
        Check(window.StartButton.IsEnabled && backend.Starts == 0, "inspection started native processes");
        Check(window.PrivateSizeText.Text.Contains("4") && window.SharedSizeText.Text.Contains("53"), "catalog sizes missing");
        SaveImage(window, "ready");

        var start = window.StartSession();
        Check(window.IsBusy && !window.CanClose && !window.StartButton.IsEnabled &&
            !window.BrowseButton.IsEnabled && !window.InspectButton.IsEnabled, "start not guarded before await");
        await window.StartSession();
        await window.InspectSource();
        Check(backend.Starts == 1 && backend.Inspections == 2 && remembered.SequenceEqual([@"C:\SPT41"]),
            "repeat start/check bypassed session guard");
        window.Close();
        Check(!closed, "active session window closed");
        await Task.Run(() => backend.Report!("Session running. Fixture private server and launcher."));
        await Drain();
        Check(window.StatusHeading.Text == "Arena session running" && window.ActivityLog.Text.Contains("Fixture"),
            "background progress not rendered on UI thread");
        window.WindowState = WindowState.Minimized;
        Check(window.WindowState == WindowState.Minimized && window.IsBusy, "active session cannot minimize");
        window.WindowState = WindowState.Normal;
        SaveImage(window, "busy");
        var oldReport = backend.Report!;
        backend.Session.SetResult();
        await start;
        oldReport("late old progress");
        await Drain();
        Check(!window.IsBusy && window.CanClose && window.StartButton.IsEnabled &&
            window.StatusHeading.Text == "Session finished" && !window.ActivityLog.Text.Contains("late old"),
            "finished session overridden by queued old progress");
        Check(window.NoticeText.Visibility == Visibility.Collapsed, "blocked-close hint survived session completion");

        backend.Session = NewSignal();
        start = window.StartSession();
        oldReport("stale previous session");
        await Drain();
        Check(!window.ActivityLog.Text.Contains("stale previous"), "old session progress entered new session");
        backend.Session.SetException(new IOException("Fixture: integrity check refused."));
        await start;
        Check(!window.IsBusy && !window.StartButton.IsEnabled && window.SourceBox.IsEnabled &&
            window.StatusHeading.Text == "Arena start stopped", "start error left ready/busy state");
        Check(window.NoticeText.Visibility == Visibility.Visible && window.NoticeText.Text.Contains("Check the installation"),
            "retry guidance disappeared with old blocked-close notice");
        await window.StartSession();
        Check(backend.Starts == 2, "failed start can retry without inspection");
        SaveImage(window, "error");
        window.Close();
        Check(closed, "idle window cannot close");

        // A real backend invalid-path call must fail without preparing a directory or starting processes.
        var absent = Path.Combine(Path.GetTempPath(), "ArenaLauncherAbsent-" + Guid.NewGuid().ToString("N"));
        var live = new MainWindow(absent);
        live.Show();
        await live.InspectSource();
        Check(!live.IsBusy && !live.StartButton.IsEnabled && !Directory.Exists(absent) &&
            !Directory.Exists(absent + "-Arena"), "invalid real source created runtime or enabled launch");
        live.Close();
    }

    private static ManagedPreview Preview(bool exists) => new(@"C:\SPT41", @"C:\SPT41-Arena", exists,
        4L * 1073741824, (long)(53.43 * 1073741824));

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> NewSignal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void SaveImage(MainWindow window, string state)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/arena-launcher-ui"));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, "fixture-" + state + ".png"));
        encoder.Save(output);
    }

    private sealed class ControlledOperations : ILauncherOperations
    {
        public int Inspections, Starts;
        public TaskCompletionSource<ManagedPreview> Preview = NewSignal<ManagedPreview>();
        public TaskCompletionSource Session = NewSignal();
        public Action<string>? Report;
        public Task<ManagedPreview> Inspect(string source, bool diagnostics) { Inspections++; return Preview.Task; }
        public Task Start(string source, bool diagnostics, Action<string> report)
        { Starts++; Report = report; return Session.Task; }
    }
}
