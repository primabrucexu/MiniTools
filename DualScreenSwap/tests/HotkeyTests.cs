using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using DualScreenSwap;

internal static class HotkeyTests
{
    private static int passed;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2)
        {
            uint key = UInt32.Parse(args[1]);
            bool registered = RegisterHotKey(IntPtr.Zero, 99, 0x4007, key);
            int error = registered ? 0 : Marshal.GetLastWin32Error();
            Console.WriteLine(registered ? "FREE" : "BUSY:" + error);
            if (args[0] == "hold" && registered) Console.ReadLine();
            if (registered) UnregisterHotKey(IntPtr.Zero, 99);
            return registered ? 0 : error;
        }
        try
        {
            Run();
            Console.WriteLine("RESULT: " + passed + " checks passed");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static Process Child(string mode, Keys key)
    {
        return Process.Start(new ProcessStartInfo(Application.ExecutablePath, mode + " " + (int)key)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        });
    }

    private static bool Free(Keys key)
    {
        using (Process child = Child("probe", key))
        {
            string state = child.StandardOutput.ReadLine();
            if (!child.WaitForExit(5000)) { child.Kill(); throw new Exception("Probe timed out"); }
            if (state != "FREE" && state != "BUSY:1409") throw new Exception("Unexpected native probe result " + state);
            return state == "FREE";
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Run()
    {
        Check(HotkeySpec.Default.Modifiers == 3 && HotkeySpec.Default.Key == Keys.P, "Default is Ctrl+Alt+P");
        Check(new HotkeySpec(0, Keys.A).Validate() != null && new HotkeySpec(7, Keys.F12).Validate() != null, "Invalid and debugger-reserved combinations rejected");
        Check(Free(Keys.F13) && Free(Keys.F14) && Free(Keys.F15), "Isolated test combinations initially free");
        NativeWindow window = new NativeWindow();
        window.CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        try
        {
            using (HotkeyManager manager = new HotkeyManager(window.Handle))
            {
                string error;
                HotkeySpec initial = new HotkeySpec(7, Keys.F13);
                HotkeySpec next = new HotkeySpec(7, Keys.F14);
                Check(manager.TryChange(initial, out error) && error == null && !Free(Keys.F13), "Native initial registration reserves shortcut across processes");
                int initialId = manager.HotkeyId;
                using (Process blocker = Child("hold", Keys.F14))
                {
                    try
                    {
                        Check(blocker.StandardOutput.ReadLine() == "FREE", "Independent process reserves conflicting shortcut");
                        bool saved = false;
                        Check(!manager.TryChange(next, delegate { saved = true; }, out error) && error.Contains("1409") && !saved, "Conflict is detected and settings are not saved");
                        Check(manager.Current.Matches(initial) && manager.HotkeyId == initialId && !Free(Keys.F13), "Conflict preserves previous live registration");
                    }
                    finally
                    {
                        blocker.StandardInput.WriteLine("exit");
                        if (!blocker.WaitForExit(5000)) blocker.Kill();
                    }
                }
                Check(Free(Keys.F14), "Conflict holder releases its registration");
                using (RegistryKey startup = Registry.CurrentUser.CreateSubKey(StartupRegistration.RunKeyPath))
                {
                    object original = startup.GetValue(StartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    RegistryValueKind kind = original == null ? RegistryValueKind.None : startup.GetValueKind(StartupRegistration.ValueName);
                    try
                    {
                        bool startupWasWritten = false;
                        Check(!manager.TryChange(next, delegate
                        {
                            StartupRegistration.SaveThen(true, @"C:\Test Tool\DualScreenSwap.exe", delegate
                            {
                                startupWasWritten = StartupRegistration.IsEnabled;
                                throw new IOException("Injected save failure");
                            });
                        }, out error) && error.Contains("Injected save failure"), "Combined startup/settings save failure is surfaced");
                        object restored = startup.GetValue(StartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        Check(startupWasWritten && StructuralComparisons.StructuralEqualityComparer.Equals(original, restored) &&
                            (original == null || startup.GetValueKind(StartupRegistration.ValueName) == kind),
                            "Combined save failure restores the original startup entry");
                        Check(manager.Current.Matches(initial) && !Free(Keys.F13) && Free(Keys.F14), "Save failure retains original shortcut and releases candidate");
                    }
                    finally
                    {
                        if (original == null) startup.DeleteValue(StartupRegistration.ValueName, false);
                        else startup.SetValue(StartupRegistration.ValueName, original, kind);
                    }
                }
                bool didSave = false;
                Check(manager.TryChange(next, delegate { didSave = true; }, out error) && didSave, "Successful change commits settings callback");
                Check(manager.Current.Matches(next) && Free(Keys.F13) && !Free(Keys.F14), "Successful change releases old and reserves new shortcut");
                Check(manager.TryChange(next, out error) && !Free(Keys.F14), "Saving unchanged shortcut retains registration");
                Check(!manager.TryChange(new HotkeySpec(0, Keys.A), out error) && manager.Current.Matches(next), "Invalid change preserves registered shortcut");
                manager.Dispose();
                Check(manager.Current == null && manager.HotkeyId == 0 && Free(Keys.F14), "Dispose releases registration and clears current state");
            }
        }
        finally { window.DestroyHandle(); }
        if (!File.Exists(HotkeySettingsStore.FilePath))
        {
            string warning;
            Check(HotkeySettingsStore.Load(out warning).Matches(HotkeySpec.Default) && warning == null, "Missing configuration loads default without writing user data");
        }
        else Console.WriteLine("SKIP: Existing user settings untouched");
    }
}
