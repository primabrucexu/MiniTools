using System;
using Microsoft.Win32;

namespace DualScreenSwap
{
    internal static class StartupRegistration
    {
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "DualScreenSwap";

        internal static bool IsEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key != null && key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null;
            }
        }

        // If saving the shortcut fails, restore the previous Run value and its type.
        internal static void SaveThen(bool enabled, string executablePath, Action saveSettings)
        {
            using (RegistryKey key = enabled
                ? Registry.CurrentUser.CreateSubKey(RunKeyPath)
                : Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    if (enabled) throw new InvalidOperationException("无法打开当前用户的开机自启动设置。");
                    saveSettings();
                    return;
                }

                object original = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind originalKind = original == null ? RegistryValueKind.None : key.GetValueKind(ValueName);
                if (enabled) key.SetValue(ValueName, "\"" + executablePath + "\"", RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);

                try { saveSettings(); }
                catch (Exception saveError)
                {
                    try
                    {
                        if (original == null) key.DeleteValue(ValueName, false);
                        else key.SetValue(ValueName, original, originalKind);
                    }
                    catch (Exception restoreError)
                    {
                        throw new InvalidOperationException(saveError.Message + "\n未能恢复原开机自启动设置：" + restoreError.Message, saveError);
                    }
                    throw;
                }
            }
        }
    }
}
