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
        StartButton.Visibility = Visibility.Visible;
        RebuildButton.IsEnabled = false;
        RebuildButton.Visibility = RebuildHint.Visibility = Visibility.Collapsed;
        PrivateSizeText.Text = SharedSizeText.Text = "—";
        DestinationText.Text = "Check an installation to see its private Arena folder.";
        EnvironmentHint.Text = "Catalog sizes, not total disk usage. Caches and logs need additional space.";
        UpdateHint.Text = "Check an installation to see pending mod updates.";
        RecommendedModsText.Text = "Check the installation to see which optional mod packages are present.";
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
    private async void Rebuild_Click(object sender, RoutedEventArgs e) => await RebuildEnvironment();

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
            RecommendedModsText.Text = string.IsNullOrWhiteSpace(preview.RecommendedMods)
                ? "Optional mod availability was not reported by this check."
                : preview.RecommendedMods;
            EnvironmentHint.Text = preview.Exists
                ? "Existing environment · catalog sizes only, plus caches and logs. Unchanged files are verified and reused."
                : "Created on first launch on the same NTFS drive. Catalog sizes above; caches and logs need additional space.";
            UpdateHint.Text = preview.RebuildReason ?? (preview.RecoveryPending
                ? "Interrupted mod update detected. The previous package will be restored before verification."
                : preview.Updates is { Total: > 0 } updates
                    ? $"Mod update on launch: {updates.Added} new · {updates.Changed} changed · {updates.Removed} removed files."
                    : preview.Exists ? "The recorded mod package matches the source. Private files are verified before launch."
                    : "The first setup includes approved mods from this installation.");
            RebuildButton.Visibility = RebuildHint.Visibility = preview.Exists ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Visibility = preview.RebuildReason == null ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Content = preview.Exists ? "Launch _Arena" : "Prepare & _launch";
            SetStatus(preview.RebuildReason != null ? "Arena rebuild required" : preview.Exists ? "Ready to verify & launch" : "Ready to prepare",
                preview.RebuildReason != null
                    ? "Close normal EFT, SPT Server and SPT Launcher first. Rebuild checks the private server; launch the game afterwards."
                    : "Close normal EFT, SPT Server and SPT Launcher first. Arena opens the game directly after verification.", false);
            UpdateLayout();
            StatusCard.BringIntoView();
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
        if (busy || preview == null || preview.RebuildReason != null) return;
        var selected = preview;
        Begin("Preparing ArenaMode", "Preparing or updating private mod files; game assets stay shared through NTFS hardlinks. Verification can take several minutes.");
        var operation = epoch;
        try
        {
            try { rememberSource?.Invoke(selected.Source); }
            catch (Exception error) { AppendLog("Could not remember folder: " + error.Message); }
            await operations.Start(selected.Source, diagnostics, message => Report(operation, message));
            preview = selected with { Exists = true };
            StartButton.Content = "Launch _Arena";
            EnvironmentHint.Text = "Sizes above are from the last installation check. Recheck to refresh them after an update; caches and logs need additional space.";
            UpdateHint.Text = "Approved mods will be checked again before the next launch.";
            SetStatus("Session finished", "The private server has stopped. You can launch again or close this window.", false);
            AppendLog("Private session finished; runtime cleanup completed.");
        }
        catch (Exception error)
        {
            preview = null;
            SetStatus("Arena start stopped", error.Message, true);
            AppendLog("Start refused: " + error.Message);
            AddNotice("Check the installation again before retrying. Interrupted mod updates are rolled back before launch; profiles are never reset.");
        }
        finally { End(); }
    }

    internal async Task RebuildEnvironment()
    {
        if (busy || preview is not { Exists: true } selected || selected.RecoveryPending && selected.RebuildReason == null) return;
        Begin("Rebuilding Arena environment", "Preparing a separate runtime and testing its private server. Previous account/settings are retained.");
        var operation = epoch;
        try
        {
            await operations.Rebuild(selected.Source, diagnostics, message => Report(operation, message));
            preview = null;
            SetStatus("Arena rebuild complete", "Check the installation, then launch the new Arena environment. Previous runtimes remain on disk.", false);
            AppendLog("Rebuild completed; no game was started.");
        }
        catch (Exception error)
        {
            preview = null;
            SetStatus("Arena rebuild stopped", error.Message, true);
            AddNotice("Check the installation before retrying. Incomplete candidates are retained; previous profiles are not reset.");
            AppendLog("Rebuild stopped: " + error.Message);
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
            else if (message.StartsWith("Rebuilding Arena:", StringComparison.Ordinal)) StatusHeading.Text = "Rebuilding Arena environment";
            else if (message.StartsWith("Recovering interrupted mod update.", StringComparison.Ordinal)) StatusHeading.Text = "Recovering mod update";
            else if (message.StartsWith("Updating Arena mods:", StringComparison.Ordinal) ||
                message.StartsWith("Preparing mod update:", StringComparison.Ordinal)) StatusHeading.Text = "Updating Arena mods";
            else if (message.StartsWith("Verif", StringComparison.Ordinal) || message.StartsWith("Checking", StringComparison.Ordinal))
                StatusHeading.Text = "Verifying files";
        }));
    }

    private void Begin(string title, string detail)
    {
        busy = true; // Before any await: repeated clicks cannot start another task.
        epoch++;
        SourceBox.IsEnabled = BrowseButton.IsEnabled = InspectButton.IsEnabled = StartButton.IsEnabled = false;
        RebuildButton.IsEnabled = false;
        NoticeText.Visibility = Visibility.Collapsed;
        Activity.IsIndeterminate = true;
        SetStatus(title, detail, false);
    }

    private void End()
    {
        busy = false; // Only after full RuntimeHost cleanup, never on a window event.
        Activity.IsIndeterminate = false;
        SourceBox.IsEnabled = BrowseButton.IsEnabled = InspectButton.IsEnabled = true;
        StartButton.IsEnabled = preview != null && preview.RebuildReason == null;
        RebuildButton.IsEnabled = preview is { Exists: true } && (!preview.RecoveryPending || preview.RebuildReason != null);
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
