using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DualScreenSwap
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (Mutex mutex = new Mutex(true, @"Local\DualScreenSwap", out firstInstance))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (!firstInstance)
                {
                    MessageBox.Show("工具已经在运行，请在右下角托盘中找到“双屏窗口互换”图标。", "双屏窗口互换", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    using (TrayContext context = new TrayContext())
                        Application.Run(context);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        internal Action<int> HotkeyPressed;
        internal HotkeyWindow()
        {
            CreateHandle(new CreateParams { Caption = "DualScreenSwap.Hotkeys", Parent = new IntPtr(-3) });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && HotkeyPressed != null)
                HotkeyPressed(message.WParam.ToInt32());
            base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly HotkeyWindow messageWindow;
        private readonly HotkeyManager hotkeys;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem hotkeyStatus;
        private readonly System.Windows.Forms.Timer startup;
        private SettingsForm settings;
        private HotkeySpec configured;
        private bool swapping;
        private string startupWarning;

        internal TrayContext()
        {
            messageWindow = new HotkeyWindow();
            hotkeys = new HotkeyManager(messageWindow.Handle);
            configured = HotkeySettingsStore.Load(out startupWarning);
            string error;
            if (!hotkeys.TryChange(configured, out error))
                startupWarning = (startupWarning == null ? "" : startupWarning + "\n\n") + error + "\n仍可通过托盘“立即交换”操作，或进入设置更换快捷键。";

            menu = new ContextMenuStrip();
            menu.Items.Add("立即交换", null, delegate { Swap(); });
            hotkeyStatus = new ToolStripMenuItem { Enabled = false };
            menu.Items.Add(hotkeyStatus);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置…", null, delegate { OpenSettings(); });
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { OpenSettings(); };
            tray.BalloonTipClicked += delegate { OpenSettings(); };
            UpdateStatus();
            messageWindow.HotkeyPressed = delegate(int id)
            {
                if (id == hotkeys.HotkeyId && settings == null) Swap();
            };
            startup = new System.Windows.Forms.Timer { Interval = 350 };
            startup.Tick += delegate
            {
                startup.Stop();
                if (!String.IsNullOrEmpty(startupWarning))
                {
                    MessageBox.Show(startupWarning, "双屏窗口互换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    OpenSettings();
                }
                else
                    tray.ShowBalloonTip(3500, "双屏窗口互换已启动", "按 " + configured.DisplayName + " 交换窗口。右键托盘图标可修改快捷键。", ToolTipIcon.Info);
            };
            startup.Start();
        }

        private void UpdateStatus()
        {
            bool registered = hotkeys.Current != null;
            hotkeyStatus.Text = registered ? "快捷键：" + hotkeys.Current.DisplayName : "快捷键未启用";
            tray.Text = "双屏窗口互换 · " + (registered ? hotkeys.Current.DisplayName : "快捷键未启用");
        }

        private void OpenSettings()
        {
            if (settings != null) { settings.Activate(); return; }
            settings = new SettingsForm(configured, delegate(HotkeySpec candidate)
            {
                string error;
                if (!hotkeys.TryChange(candidate, delegate { HotkeySettingsStore.Save(candidate); }, out error))
                    return error;
                configured = candidate;
                UpdateStatus();
                return null;
            });
            settings.FormClosed += delegate { settings.Dispose(); settings = null; };
            settings.Show();
            settings.Activate();
        }

        private void Swap()
        {
            if (swapping || settings != null) return;
            swapping = true;
            try
            {
                string error = WindowSwapper.Swap();
                if (error != null)
                    tray.ShowBalloonTip(5000, "未完成窗口交换", error, ToolTipIcon.Warning);
            }
            catch (Exception error)
            {
                tray.ShowBalloonTip(5000, "未完成窗口交换", error.Message, ToolTipIcon.Error);
            }
            finally { swapping = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                startup.Dispose();
                if (settings != null) settings.Close();
                hotkeys.Dispose();
                messageWindow.Dispose();
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
