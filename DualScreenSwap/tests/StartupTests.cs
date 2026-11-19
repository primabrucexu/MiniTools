using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using DualScreenSwap;

internal static class StartupTests
{
    private static int passed;

    private static void Check(bool okay, string label)
    {
        if (!okay) throw new Exception(label);
        passed++;
        Console.WriteLine("PASS: " + label);
    }

    private static object Read(RegistryKey key)
    {
        return key.GetValue(StartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private static void ExpectSaveFailure(Action save)
    {
        try { save(); }
        catch (IOException error)
        {
            Check(error.Message == "Injected startup save failure", "Downstream save failure is surfaced");
            return;
        }
        throw new Exception("Expected downstream save failure");
    }

    private static void FailSave() { throw new IOException("Injected startup save failure"); }

    private static int Main()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistration.RunKeyPath))
        {
            object original = Read(key);
            RegistryValueKind originalKind = original == null ? RegistryValueKind.None : key.GetValueKind(StartupRegistration.ValueName);
            List<string> otherNames = new List<string>(key.GetValueNames());
            otherNames.Remove(StartupRegistration.ValueName);
            otherNames.Sort(StringComparer.Ordinal);
            try
            {
                key.DeleteValue(StartupRegistration.ValueName, false);
                Check(!StartupRegistration.IsEnabled, "Missing Run entry defaults to disabled");

                const string initialPath = @"C:\Tools With Spaces\DualScreenSwap.exe";
                bool saved = false;
                StartupRegistration.SaveThen(true, initialPath, delegate { saved = true; });
                Check(saved && (string)Read(key) == "\"" + initialPath + "\"" &&
                    key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.String,
                    "Enable stores the quoted executable path and completes settings save");
                Check(StartupRegistration.IsEnabled, "Enabled state is read back from the registry");

                const string movedPath = @"D:\Moved Tool\DualScreenSwap.exe";
                StartupRegistration.SaveThen(true, movedPath, delegate { });
                Check((string)Read(key) == "\"" + movedPath + "\"", "Saving from a new location updates the startup command");

                StartupRegistration.SaveThen(false, movedPath, delegate { });
                List<string> remainingNames = new List<string>(key.GetValueNames());
                remainingNames.Sort(StringComparer.Ordinal);
                Check(!StartupRegistration.IsEnabled && Read(key) == null &&
                    String.Join("\n", otherNames.ToArray()) == String.Join("\n", remainingNames.ToArray()),
                    "Disable removes only this application's Run entry");

                const string oldCommand = @"""%LOCALAPPDATA%\Old Tool\DualScreenSwap.exe""";
                key.SetValue(StartupRegistration.ValueName, oldCommand, RegistryValueKind.ExpandString);
                ExpectSaveFailure(delegate { StartupRegistration.SaveThen(true, movedPath, FailSave); });
                Check((string)Read(key) == oldCommand && key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.ExpandString,
                    "Failed update restores the unexpanded original value and registry type");

                ExpectSaveFailure(delegate { StartupRegistration.SaveThen(false, movedPath, FailSave); });
                Check((string)Read(key) == oldCommand && key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.ExpandString,
                    "Failed disable restores the previous Run entry");

                key.DeleteValue(StartupRegistration.ValueName, false);
                ExpectSaveFailure(delegate { StartupRegistration.SaveThen(true, movedPath, FailSave); });
                Check(Read(key) == null && !StartupRegistration.IsEnabled, "Failed first enable restores the absent-entry state");
                Console.WriteLine("RESULT: " + passed + " checks passed");
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
            finally
            {
                if (original == null) key.DeleteValue(StartupRegistration.ValueName, false);
                else key.SetValue(StartupRegistration.ValueName, original, originalKind);
                Console.WriteLine("CLEANUP: Restored original DualScreenSwap Run value and type");
            }
        }
    }
}
