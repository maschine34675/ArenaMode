using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArenaLauncher;

/// <summary>Offline, selectable copies of the third-party notices shipped with the launcher.</summary>
public static class LicensesWindow
{
    public static void Show(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        string notices;
        try
        {
            notices = LoadNotices();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            MessageBox.Show(owner, "The embedded license notices could not be opened.\n\n" + ex.Message,
                "Third-party licenses", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var window = new Window
        {
            Owner = owner,
            Title = "Arena Launcher — Third-party licenses",
            Width = 950,
            Height = 700,
            MinWidth = 560,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = owner.TryFindResource("CanvasBrush") as Brush ?? Brushes.Black
        };
        var layout = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock
        {
            Text = "Third-party licenses\nFull notices are embedded for offline access. Select text and use Ctrl+C, or copy all below.",
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);

        var text = new TextBox
        {
            Text = notices,
            IsReadOnly = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var select = new Button { Content = "_Select all", Margin = new Thickness(0, 0, 10, 0) };
        select.Click += (_, _) =>
        {
            text.Focus();
            text.SelectAll();
        };
        var copy = new Button { Content = "_Copy all", Margin = new Thickness(0, 0, 10, 0) };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(notices);
            }
            catch (ExternalException)
            {
                MessageBox.Show(window, "The clipboard is currently unavailable. Please try again.",
                    "Copy licenses", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        var close = new Button { Content = "_Close", IsCancel = true };
        close.Click += (_, _) => window.Close();
        actions.Children.Add(select);
        actions.Children.Add(copy);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(text);
        window.Content = layout;
        window.ShowDialog();
    }

    private static string LoadNotices()
    {
        var documents = new (string File, string Title, string Source)[]
        {
            ("ARENA-LICENSE.txt", "Arena Launcher — original code license", "ArenaMode repository / LICENSE"),
            ("PATCH-DEPENDENCIES.txt", "Patch verification dependencies", "Package and upstream provenance is included in this notice."),
            ("DOTNET-10.0.12-LICENSE.txt", ".NET runtime 10.0.12 — license",
                "NuGet: Microsoft.NETCore.App.Runtime.win-x64 10.0.12 / LICENSE.TXT\nhttps://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64/10.0.12"),
            ("DOTNET-10.0.12-THIRD-PARTY-NOTICES.txt", ".NET runtime 10.0.12 — third-party notices",
                "NuGet: Microsoft.NETCore.App.Runtime.win-x64 10.0.12 / THIRD-PARTY-NOTICES.TXT\nhttps://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64/10.0.12"),
            ("WINDOWSDESKTOP-10.0.12-LICENSE.txt", ".NET Windows Desktop runtime 10.0.12 — license",
                "NuGet: Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.12 / LICENSE\nhttps://www.nuget.org/packages/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.12")
        };
        var result = new StringBuilder();
        foreach (var document in documents)
        {
            result.AppendLine(new string('=', 78)).AppendLine(document.Title)
                .AppendLine(document.Source).AppendLine(new string('=', 78)).AppendLine();
            using var stream = typeof(LicensesWindow).Assembly.GetManifestResourceStream("ArenaLauncher.Notices." + document.File)
                ?? throw new InvalidOperationException("Missing embedded notice: " + document.File);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            result.AppendLine(reader.ReadToEnd()).AppendLine();
        }
        return result.ToString();
    }
}
