using System;
using System.IO;
using System.Windows.Forms;
using DualScreenSwap;

internal static class SettingsStoreTests
{
    private static int passed;
    private static void Check(bool okay, string label)
    {
        if (!okay) throw new Exception(label);
        passed++;
        Console.WriteLine("PASS: " + label);
    }
    private static int Main()
    {
        string path = HotkeySettingsStore.FilePath;
        string directory = Path.GetDirectoryName(path);
        string temporary = path + ".tmp";
        bool directoryExisted = Directory.Exists(directory);
        byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        byte[] originalTemporary = File.Exists(temporary) ? File.ReadAllBytes(temporary) : null;
        try
        {
            string warning;
            HotkeySettingsStore.Save(HotkeySpec.Default);
            Check(HotkeySettingsStore.Load(out warning).Matches(HotkeySpec.Default) && warning == null, "Initial Save/Load persists default shortcut");
            HotkeySpec changed = new HotkeySpec(7, Keys.F15);
            HotkeySettingsStore.Save(changed);
            Check(HotkeySettingsStore.Load(out warning).Matches(changed) && warning == null, "Atomic replacement persists changed shortcut");
            string before = File.ReadAllText(path);
            bool rejected = false;
            try { HotkeySettingsStore.Save(new HotkeySpec(0, Keys.P)); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && File.ReadAllText(path) == before, "Invalid shortcut does not overwrite saved settings");
            Check(!File.Exists(temporary), "Successful writes leave no temporary file");
            File.WriteAllText(path, "Invalid config");
            Check(HotkeySettingsStore.Load(out warning).Matches(HotkeySpec.Default) && !String.IsNullOrEmpty(warning), "Malformed settings produce warning and default fallback");
            Console.WriteLine("RESULT: " + passed + " checks passed");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            if (original == null) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllBytes(path, original);
            if (originalTemporary == null) { if (File.Exists(temporary)) File.Delete(temporary); }
            else File.WriteAllBytes(temporary, originalTemporary);
            if (!directoryExisted && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                Directory.Delete(directory);
            Console.WriteLine("CLEANUP: Restored original user configuration state");
        }
    }
}
