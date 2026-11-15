using System;
using System.Windows.Forms;
using DualScreenSwap;

internal static class SettingsCancelTest
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        using (SettingsForm form = new SettingsForm(HotkeySpec.Default, delegate { return null; }))
        {
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
            form.CancelButton.PerformClick();
            Application.DoEvents();
            bool closed = form.IsDisposed || !form.Visible;
            Console.WriteLine(closed ? "PASS: Nonmodal cancel closes settings" : "FAIL: Nonmodal cancel leaves settings open");
            if (!closed) form.Close();
            return closed ? 0 : 1;
        }
    }
}
