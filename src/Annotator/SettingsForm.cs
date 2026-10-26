using Microsoft.Win32;

namespace Annotator;

/// Per-user autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run (no admin rights needed).
internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "ScreenshotAnnotator";
    internal static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    internal static string Command => $"\"{ExePath}\"";
    internal static string? Current
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) as string; }
    }
    internal static bool Enabled => string.Equals(Current, Command, StringComparison.OrdinalIgnoreCase);
    internal static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal sealed class SettingsForm : Form
{
    internal CheckBox StartWithWindows { get; }
    public SettingsForm()
    {
        Text = "Screenshot Annotator Settings";
        Icon = AppIcon.Large;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(16);
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        StartWithWindows = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = Autostart.Enabled };
        StartWithWindows.CheckedChanged += (_,_) =>
        {
            try { Autostart.Set(StartWithWindows.Checked); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Screenshot Annotator", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var hint = new Label { Text = "Starts quietly in the tray when you sign in.\nPress Ctrl+Alt+A to capture a region.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 12) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        layout.Controls.AddRange([StartWithWindows, hint, close]);
        Controls.Add(layout);
        AcceptButton = close; CancelButton = close;
    }
}
