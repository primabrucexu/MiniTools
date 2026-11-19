using System;
using System.Windows.Forms;
using DualScreenSwap;

internal static class SettingsCancelTest
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        bool applied = false;
        using (SettingsForm form = new SettingsForm(HotkeySpec.Default, true, delegate { applied = true; return null; }))
        {
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
            CheckBox startup = null;
            foreach (Control control in form.Controls)
                if (control is CheckBox && control.Text.StartsWith("开机自启动", StringComparison.Ordinal)) startup = (CheckBox)control;
            if (startup == null || !startup.Checked) throw new Exception("Startup checkbox did not reflect the enabled state");
            startup.Checked = false;
            form.CancelButton.PerformClick();
            Application.DoEvents();
            bool closed = form.IsDisposed || !form.Visible;
            Console.WriteLine(closed && !applied ? "PASS: Cancel closes settings without applying a changed startup selection" : "FAIL: Cancel did not preserve unapplied settings");
            if (!closed) form.Close();
            if (!closed || applied) return 1;
        }
        bool savedStartup = false;
        using (SettingsForm form = new SettingsForm(HotkeySpec.Default, false, delegate(HotkeySpec hotkey, bool startupEnabled)
        {
            savedStartup = startupEnabled && hotkey.Matches(HotkeySpec.Default);
            return null;
        }))
        {
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
            foreach (Control control in form.Controls)
                if (control is CheckBox && control.Text.StartsWith("开机自启动", StringComparison.Ordinal))
                    ((CheckBox)control).Checked = true;
            form.AcceptButton.PerformClick();
            Application.DoEvents();
            bool closed = form.IsDisposed || !form.Visible;
            Console.WriteLine(closed && savedStartup ? "PASS: Save passes the selected startup state and shortcut to the settings callback" : "FAIL: Startup selection was not applied");
            if (!closed) form.Close();
            return closed && savedStartup ? 0 : 1;
        }
    }
}
