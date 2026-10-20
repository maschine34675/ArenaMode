using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ArenaStart;
using Microsoft.Win32;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ArenaLauncherChecks")]

namespace ArenaLauncher;

public partial class MainWindow : Window
{
    private readonly ILauncherOperations operations;
    private readonly Action<string>? rememberSource;
    private readonly bool diagnostics;
    private readonly Queue<string> log = new();
    private ManagedPreview? preview;
    private int epoch;
    private bool busy, closed;
    internal bool IsBusy => busy;
    internal bool CanClose => !busy;

    private void Licenses_Click(object sender, RoutedEventArgs e) => LicensesWindow.Show(this);

    public MainWindow(string source = "", bool diagnostics = false,
        ILauncherOperations? operations = null, Action<string>? rememberSource = null)
    {
        InitializeComponent();
        this.operations = operations ?? new LauncherOperations();
        this.rememberSource = rememberSource;
        this.diagnostics = diagnostics;
        BuildBadge.Text = diagnostics ? "PROTOTYPE · DIRECT START · DIAGNOSTICS" : "PROTOTYPE · DIRECT START";
        SourceBox.Text = source;
        SourceBox.TextChanged += (_, _) => InvalidatePreview();
        Closing += OnClosing;
        Closed += (_, _) => closed = true;
    }

    private void InvalidatePreview()
    {
        preview = null;
        StartButton.IsEnabled = false;
        PrivateSizeText.Text = SharedSizeText.Text = "—";
        DestinationText.Text = "Check an installation to see its private Arena folder.";
        EnvironmentHint.Text = "Catalog sizes, not total disk usage. Caches and logs need additional space.";
        if (!busy) SetStatus("Check your installation", "The selected folder has changed.", false);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFolderDialog { Title = "Select your existing SPT installation", Multiselect = false };
        if (dialog.ShowDialog(this) == true) SourceBox.Text = dialog.FolderName;
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await InspectSource();
    private async void Start_Click(object sender, RoutedEventArgs e) => await StartSession();

    internal async Task InspectSource()
    {
        if (busy) return;
        var source = SourceBox.Text.Trim();
        InvalidatePreview();
        if (string.IsNullOrWhiteSpace(source))
        {
            SetStatus("Select an SPT folder", "Use Browse or enter the folder containing EscapeFromTarkov.exe.", true);
            return;
        }
        Begin("Checking installation", "Reading the file catalog. No runtime is created by this check.");
        try
        {
            preview = await operations.Inspect(source, diagnostics);
            DestinationText.Text = preview.Destination;
            PrivateSizeText.Text = $"{preview.PrivateBytes / 1073741824d:F2} GiB";
            SharedSizeText.Text = $"{preview.SharedBytes / 1073741824d:F2} GiB";
            EnvironmentHint.Text = preview.Exists
                ? "Existing environment · catalog sizes only, plus caches and logs. Files are verified again on launch, not recopied."
                : "Created on first launch on the same NTFS drive. Catalog sizes above; caches and logs need additional space.";
            StartButton.Content = preview.Exists ? "Launch _Arena" : "Prepare & _launch";
            SetStatus(preview.Exists ? "Ready to verify & launch" : "Ready to prepare",
                "Close normal EFT, SPT Server and SPT Launcher first. Arena opens the game directly after verification.", false);
            AppendLog($"Selected {preview.Source}; private runtime {preview.Destination}.");
        }
        catch (Exception error)
        {
            preview = null;
            SetStatus("Installation not ready", error.Message, true);
            AppendLog("Check refused: " + error.Message);
        }
        finally { End(); }
    }

    internal async Task StartSession()
    {
        if (busy || preview == null) return;
        var selected = preview;
        Begin("Preparing ArenaMode", "Copying private files only on first setup; sharing game assets through NTFS hardlinks. Verification can take several minutes.");
        var operation = epoch;
        try
        {
            try { rememberSource?.Invoke(selected.Source); }
            catch (Exception error) { AppendLog("Could not remember folder: " + error.Message); }
            await operations.Start(selected.Source, diagnostics, message => Report(operation, message));
            preview = selected with { Exists = true };
            StartButton.Content = "Launch _Arena";
            EnvironmentHint.Text = "Existing environment · catalog sizes only, plus caches and logs. Files are verified again on launch, not recopied.";
            SetStatus("Session finished", "The private server has stopped. You can launch again or close this window.", false);
            AppendLog("Private session finished; runtime cleanup completed.");
        }
        catch (Exception error)
        {
            preview = null;
            SetStatus("Arena start stopped", error.Message, true);
            AppendLog("Start refused: " + error.Message);
            AddNotice("Check the installation again before retrying. No automatic reset or repair was performed.");
        }
        finally { End(); }
    }

    private void Report(int operation, string message)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (closed || !busy || epoch != operation) return;
            AppendLog(message);
            StatusDetail.Text = message;
            if (message.StartsWith("Session running.", StringComparison.Ordinal)) StatusHeading.Text = "Arena session running";
            else if (message.StartsWith("Private server exited unexpectedly", StringComparison.Ordinal))
                SetStatus("Private server stopped unexpectedly", message, true);
            else if (message.StartsWith("Private Arena server PID", StringComparison.Ordinal)) StatusHeading.Text = "Starting private server";
            else if (message.StartsWith("Starting the private game directly.", StringComparison.Ordinal)) StatusHeading.Text = "Starting the game";
            else if (message.StartsWith("Copying game settings:", StringComparison.Ordinal)) StatusHeading.Text = "Applying game settings";
            else if (message.StartsWith("Verif", StringComparison.Ordinal) || message.StartsWith("Checking", StringComparison.Ordinal))
                StatusHeading.Text = "Verifying files";
        }));
    }

    private void Begin(string title, string detail)
    {
        busy = true; // Before any await: repeated clicks cannot start another task.
        epoch++;
        SourceBox.IsEnabled = BrowseButton.IsEnabled = InspectButton.IsEnabled = StartButton.IsEnabled = false;
        NoticeText.Visibility = Visibility.Collapsed;
        Activity.IsIndeterminate = true;
        SetStatus(title, detail, false);
    }

    private void End()
    {
        busy = false; // Only after full RuntimeHost cleanup, never on a window event.
        Activity.IsIndeterminate = false;
        SourceBox.IsEnabled = BrowseButton.IsEnabled = InspectButton.IsEnabled = true;
        StartButton.IsEnabled = preview != null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (CanClose) return;
        e.Cancel = true;
        AddNotice("Work is still running. Keep this window open; you can minimize it. Close EFT normally to finish the private session.");
    }

    private void SetStatus(string title, string detail, bool error)
    {
        NoticeText.Visibility = Visibility.Collapsed; // Clear any earlier blocked-close hint with the new outcome.
        StatusHeading.Text = title;
        StatusHeading.Foreground = error ? new SolidColorBrush(Color.FromRgb(244, 151, 143))
            : (Brush)FindResource("AccentBrush");
        StatusDetail.Text = detail;
    }

    internal void AddNotice(string message)
    {
        NoticeText.Text = message;
        NoticeText.Visibility = Visibility.Visible;
    }

    private void AppendLog(string message)
    {
        log.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
        while (log.Count > 200) log.Dequeue();
        ActivityLog.Text = string.Join(Environment.NewLine, log);
        ActivityLog.ScrollToEnd();
    }
}
