using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArenaMode.SessionHelper
{
    /// <summary>The optional desktop picker; all transition work stays in the tested helper.</summary>
    internal static class HelperStartWindow
    {
        internal static bool Run(HelperDirectStart starter, string requestedProfile)
        {
            var profiles = starter.Profiles();
            if (profiles.Count == 0) throw new HelperException("No initialized SPT profile found. Create a profile and finish character setup in SPT first.");
            string remembered = requestedProfile ?? starter.RememberedProfile();
            int selected = profiles.FindIndex(p => p.Id == remembered);
            if (requestedProfile != null && selected < 0) throw new HelperException("The selected SPT profile is unavailable. Open ArenaMode without arguments to choose another.");
            bool busy = false, result = true;
            Application.EnableVisualStyles();
            using (var form = new Form { Text = "ArenaMode", ClientSize = new Size(410, 178),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = true,
                StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 10), AutoScaleMode = AutoScaleMode.Dpi })
            using (var label = new Label { Text = "Profile", Location = new Point(20, 17), AutoSize = true })
            using (var combo = new ComboBox { Location = new Point(20, 43), Size = new Size(370, 30), DropDownStyle = ComboBoxStyle.DropDownList })
            using (var status = new Label { Text = "", Location = new Point(20, 83), Size = new Size(370, 24) })
            using (var shortcut = new LinkLabel { Text = "Desktop shortcut", Location = new Point(20, 130), AutoSize = true })
            using (var cancel = new Button { Text = "Cancel", Location = new Point(200, 121), Size = new Size(84, 34), DialogResult = DialogResult.Cancel })
            using (var start = new Button { Text = "Start Arena", Location = new Point(292, 121), Size = new Size(98, 34) })
            {
                foreach (var profile in profiles) combo.Items.Add(profile);
                combo.SelectedIndex = selected >= 0 ? selected : 0;
                form.Controls.AddRange(new Control[] { label, combo, status, shortcut, cancel, start });
                form.AcceptButton = start; form.CancelButton = cancel;
                form.FormClosing += (_, e) => { if (busy) e.Cancel = true; };
                shortcut.LinkClicked += (_, e) =>
                {
                    try { CreateShortcut(starter.HelperExe, starter.GameRoot, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); status.Text = "Shortcut created."; }
                    catch (Exception) { status.Text = "Shortcut could not be created."; }
                };
                starter.Progress = message => { if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke((Action)(() => status.Text = message)); };
                start.Click += async (_, e) =>
                {
                    if (busy) return;
                    string id = ((HelperStartProfile)combo.SelectedItem).Id;
                    busy = true; start.Enabled = cancel.Enabled = combo.Enabled = shortcut.Enabled = false;
                    status.Text = "Preparing SPT…";
                    try { result = await Task.Run(() => starter.Run(id)); }
                    finally { busy = false; form.Close(); }
                };
                if (requestedProfile != null) form.Shown += (_, e) => start.PerformClick();
                form.ShowDialog();
                starter.Progress = _ => { };
            }
            return result;
        }

        internal static string CreateShortcut(string executable, string gameRoot, string desktop)
        {
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)) throw new IOException("Desktop is unavailable.");
            string path = Path.Combine(desktop, "ArenaMode.lnk");
            object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                if (File.Exists(path))
                {
                    string target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                    if (!HelperPathSafety.PathsEqual(target, executable))
                    {
                        Marshal.FinalReleaseComObject(link); link = null;
                        path = Path.Combine(desktop, "ArenaMode - " + HelperDirectStart.Hash(System.Text.Encoding.UTF8.GetBytes(gameRoot.ToUpperInvariant())).Substring(0, 8) + ".lnk");
                        link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                        if (File.Exists(path))
                        {
                            target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                            if (!HelperPathSafety.PathsEqual(target, executable)) throw new IOException("A different shortcut already exists.");
                        }
                    }
                }
                foreach (var pair in new[] { new[] { "TargetPath", executable }, new[] { "Arguments", "start" },
                    new[] { "WorkingDirectory", gameRoot }, new[] { "Description", "Start an ArenaMode session" } })
                    link.GetType().InvokeMember(pair[0], BindingFlags.SetProperty, null, link, new object[] { pair[1] });
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return path;
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
