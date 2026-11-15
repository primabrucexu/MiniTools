using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DualScreenSwap
{
    internal sealed class HotkeySpec
    {
        internal uint Modifiers { get; private set; }
        internal Keys Key { get; private set; }
        internal static HotkeySpec Default { get { return new HotkeySpec(3, Keys.P); } }

        internal HotkeySpec(uint modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        internal string DisplayName
        {
            get
            {
                List<string> parts = new List<string>();
                if ((Modifiers & 2) != 0) parts.Add("Ctrl");
                if ((Modifiers & 1) != 0) parts.Add("Alt");
                if ((Modifiers & 4) != 0) parts.Add("Shift");
                parts.Add(KeyName(Key));
                return String.Join(" + ", parts.ToArray());
            }
        }

        internal string Validate()
        {
            if (Modifiers == 0 || (Modifiers & ~7U) != 0)
                return "请至少选择 Ctrl、Alt、Shift 中的一个修饰键。";
            if (Key == Keys.F12)
                return "F12 是 Windows 调试器保留键，请选择其他主键。";
            if (!IsSupportedKey(Key)) return "请选择字母、数字、功能键或列表中的其他主键。";
            return null;
        }

        internal bool Matches(HotkeySpec other)
        {
            return other != null && Modifiers == other.Modifiers && Key == other.Key;
        }

        internal static bool IsSupportedKey(Keys key)
        {
            return (key >= Keys.A && key <= Keys.Z) ||
                (key >= Keys.D0 && key <= Keys.D9) ||
                (key >= Keys.NumPad0 && key <= Keys.NumPad9) ||
                (key >= Keys.F1 && key <= Keys.F24 && key != Keys.F12) ||
                key == Keys.Space || key == Keys.Home || key == Keys.End ||
                key == Keys.PageUp || key == Keys.PageDown || key == Keys.Insert || key == Keys.Delete;
        }

        internal static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num " + ((int)key - (int)Keys.NumPad0).ToString(CultureInfo.InvariantCulture);
            if (key == Keys.Space) return "Space";
            if (key == Keys.PageUp) return "Page Up";
            if (key == Keys.PageDown) return "Page Down";
            return key.ToString();
        }
    }

    internal sealed class HotkeyManager : IDisposable
    {
        private readonly IntPtr window;
        private const uint NoRepeat = 0x4000;
        internal HotkeySpec Current { get; private set; }
        internal int HotkeyId { get; private set; }

        internal HotkeyManager(IntPtr windowHandle)
        {
            window = windowHandle;
        }

        internal bool TryChange(HotkeySpec next, out string error)
        {
            return TryChange(next, null, out error);
        }

        // Keep the old registration until both the new registration and saving succeed.
        internal bool TryChange(HotkeySpec next, Action save, out string error)
        {
            error = next == null ? "请选择快捷键。" : next.Validate();
            if (error != null) return false;

            bool changed = !next.Matches(Current);
            int candidateId = HotkeyId == 0x5100 ? 0x5101 : 0x5100;
            if (changed && !RegisterHotKey(window, candidateId, next.Modifiers | NoRepeat, (uint)next.Key))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == 1409
                    ? next.DisplayName + " 已被其他程序占用，请更换快捷键。（错误 1409）"
                    : "无法注册 " + next.DisplayName + "，请更换快捷键。（Windows 错误 " + code + "）";
                return false;
            }

            try
            {
                if (save != null) save();
            }
            catch (Exception ex)
            {
                if (changed) UnregisterHotKey(window, candidateId);
                error = "无法保存设置，原快捷键保持不变：" + ex.Message;
                return false;
            }

            if (changed)
            {
                if (Current != null) UnregisterHotKey(window, HotkeyId);
                Current = next;
                HotkeyId = candidateId;
            }
            return true;
        }

        public void Dispose()
        {
            if (Current == null) return;
            UnregisterHotKey(window, HotkeyId);
            Current = null;
            HotkeyId = 0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    internal static class HotkeySettingsStore
    {
        internal static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualScreenSwap", "settings.ini"); }
        }

        internal static HotkeySpec Load(out string warning)
        {
            warning = null;
            if (!File.Exists(FilePath)) return HotkeySpec.Default;
            try
            {
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                uint modifiers;
                int key;
                if (lines.Length != 2 || !lines[0].StartsWith("Modifiers=", StringComparison.Ordinal) ||
                    !lines[1].StartsWith("Key=", StringComparison.Ordinal) ||
                    !UInt32.TryParse(lines[0].Substring(10), NumberStyles.None, CultureInfo.InvariantCulture, out modifiers) ||
                    !Int32.TryParse(lines[1].Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out key))
                    throw new FormatException("配置内容不完整或格式不正确。");
                HotkeySpec loaded = new HotkeySpec(modifiers, (Keys)key);
                string error = loaded.Validate();
                if (error != null) throw new FormatException(error);
                return loaded;
            }
            catch (Exception ex)
            {
                warning = "未能读取快捷键设置，将使用默认的 Ctrl + Alt + P。" + Environment.NewLine + ex.Message;
                return HotkeySpec.Default;
            }
        }

        internal static void Save(HotkeySpec spec)
        {
            string error = spec.Validate();
            if (error != null) throw new ArgumentException(error, "spec");
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, "Modifiers=" + spec.Modifiers.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Key=" + ((int)spec.Key).ToString(CultureInfo.InvariantCulture) + Environment.NewLine, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox controlKey = new CheckBox();
        private readonly CheckBox altKey = new CheckBox();
        private readonly CheckBox shiftKey = new CheckBox();
        private readonly ComboBox mainKey = new ComboBox();
        private readonly Label preview = new Label();
        private readonly List<Keys> keys = new List<Keys>();
        private readonly Func<HotkeySpec, string> apply;

        internal SettingsForm(HotkeySpec current, Func<HotkeySpec, string> applySettings)
        {
            apply = applySettings;
            Text = "双屏窗口互换 · 设置";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 265);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;

            Label heading = new Label { Text = "交换窗口的全局快捷键", AutoSize = true, Location = new Point(24, 22) };
            Controls.Add(heading);
            controlKey.Text = "Ctrl";
            altKey.Text = "Alt";
            shiftKey.Text = "Shift";
            CheckBox[] modifiers = { controlKey, altKey, shiftKey };
            for (int i = 0; i < modifiers.Length; i++)
            {
                modifiers[i].Location = new Point(24 + i * 90, 56);
                modifiers[i].AutoSize = true;
                modifiers[i].CheckedChanged += delegate { UpdatePreview(); };
                Controls.Add(modifiers[i]);
            }
            controlKey.Checked = (current.Modifiers & 2) != 0;
            altKey.Checked = (current.Modifiers & 1) != 0;
            shiftKey.Checked = (current.Modifiers & 4) != 0;

            Label keyLabel = new Label { Text = "主键", AutoSize = true, Location = new Point(24, 98) };
            Controls.Add(keyLabel);
            mainKey.Location = new Point(78, 94);
            mainKey.Size = new Size(190, 28);
            mainKey.DropDownStyle = ComboBoxStyle.DropDownList;
            for (int i = 1; i <= 255; i++)
            {
                Keys key = (Keys)i;
                if (!HotkeySpec.IsSupportedKey(key)) continue;
                keys.Add(key);
                mainKey.Items.Add(HotkeySpec.KeyName(key));
            }
            mainKey.SelectedIndex = keys.IndexOf(current.Key);
            if (mainKey.SelectedIndex < 0) mainKey.SelectedIndex = keys.IndexOf(Keys.P);
            mainKey.SelectedIndexChanged += delegate { UpdatePreview(); };
            Controls.Add(mainKey);

            preview.Location = new Point(24, 138);
            preview.Size = new Size(430, 25);
            Controls.Add(preview);
            Label hint = new Label
            {
                Text = "保存时检测全局占用；冲突时保留原快捷键。\n普通软件内部快捷键不属于全局占用检测范围。",
                Location = new Point(24, 170),
                Size = new Size(430, 42),
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(hint);
            Button save = new Button { Text = "保存", Location = new Point(284, 222), Size = new Size(80, 30) };
            Button cancel = new Button { Text = "取消", Location = new Point(376, 222), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
            save.Click += SaveClicked;
            cancel.Click += delegate { Close(); };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            UpdatePreview();
        }

        private HotkeySpec SelectedHotkey()
        {
            uint modifiers = (controlKey.Checked ? 2U : 0U) | (altKey.Checked ? 1U : 0U) | (shiftKey.Checked ? 4U : 0U);
            Keys key = mainKey.SelectedIndex >= 0 ? keys[mainKey.SelectedIndex] : Keys.None;
            return new HotkeySpec(modifiers, key);
        }

        private void UpdatePreview()
        {
            preview.Text = "快捷键：" + SelectedHotkey().DisplayName;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            HotkeySpec selected = SelectedHotkey();
            string error = selected.Validate();
            if (error == null) error = apply(selected);
            if (error != null)
            {
                MessageBox.Show(this, error, "无法保存快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
